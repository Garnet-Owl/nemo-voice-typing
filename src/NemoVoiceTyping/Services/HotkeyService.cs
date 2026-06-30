using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace NemoVoiceTyping.Services;

/// <summary>
/// Registers a single system-wide hotkey via Win32 RegisterHotKey and
/// raises <see cref="Pressed"/> on the WPF dispatcher thread when fired.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ID = 0xB001;

    [Flags]
    private enum HotkeyMod : uint
    {
        None = 0, Alt = 1, Control = 2, Shift = 4, Win = 8, NoRepeat = 0x4000,
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private HwndSource? _source;
    private IntPtr _hwnd;
    private bool _registered;

    public event Action? Pressed;

    public bool Register(Window window, string hotkey)
    {
        var (mods, vk) = Parse(hotkey);
        var helper = new WindowInteropHelper(window);
        helper.EnsureHandle();
        _hwnd = helper.Handle;
        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(WndProc);
        _registered = RegisterHotKey(_hwnd, HOTKEY_ID, (uint)(mods | HotkeyMod.NoRepeat), vk);
        return _registered;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            Pressed?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private static (HotkeyMod mods, uint vk) Parse(string hotkey)
    {
        var mods = HotkeyMod.None;
        string keyName = "";
        foreach (var rawPart in hotkey.Split('+'))
        {
            var part = rawPart.Trim();
            switch (part.ToLowerInvariant())
            {
                case "ctrl": case "control": mods |= HotkeyMod.Control; break;
                case "alt":   mods |= HotkeyMod.Alt; break;
                case "shift": mods |= HotkeyMod.Shift; break;
                case "win": case "windows": mods |= HotkeyMod.Win; break;
                default: keyName = part; break;
            }
        }
        if (string.IsNullOrEmpty(keyName))
            throw new ArgumentException($"Invalid hotkey: {hotkey}");

        var key = (Key)Enum.Parse(typeof(Key), keyName, ignoreCase: true);
        var vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        return (mods, vk);
    }

    public void Dispose()
    {
        if (_registered)
        {
            UnregisterHotKey(_hwnd, HOTKEY_ID);
            _registered = false;
        }
        _source?.RemoveHook(WndProc);
        _source = null;
    }
}
