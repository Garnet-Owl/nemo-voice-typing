using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;

namespace NemoVoiceTyping.Services;

/// <summary>
/// Injects text into whichever window currently has keyboard focus.
/// Uses SendInput with KEYEVENTF_UNICODE so it works for any Unicode codepoint
/// (including BMP and supplementary planes via UTF-16 surrogate pairs) without
/// touching the user's clipboard.
///
/// The exception is Windows 11 Notepad's editor, which loses injected key
/// events: words arrive as a run of their last character, or vanish. There the
/// text is pasted instead, which takes four key events whatever its length;
/// see <see cref="DictationClipboard"/>.
/// </summary>
public static class TextInjector
{
    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint type; public InputUnion U; }
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }
    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct HARDWAREINPUT { public uint uMsg; public ushort wParamL, wParamH; }
    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;
    private const ushort VK_BACK = 0x08;
    private const ushort VK_RETURN = 0x0D;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_V = 0x56;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct GUITHREADINFO
    {
        public int cbSize;
        public uint flags;
        public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret;
        public int caretLeft, caretTop, caretRight, caretBottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO gui);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(IntPtr hWnd, StringBuilder className, int maxCount);

    /// <summary>Set from <c>AppConfig.TextInjection</c>: auto, keystrokes or clipboard.</summary>
    public static string Mode { get; set; } = "auto";

    private const string NotepadEditorClass = "RichEditD2DPT";

    /// <summary>
    /// Gap between Backspace presses where batched key events get lost. One
    /// press at a time, 15 ms apart, removed every character in the
    /// maintainer's tests on Notepad, where a single batch often removed fewer.
    /// </summary>
    private const int BackspaceGapMs = 15;

    /// <summary>Each UTF-16 code unit needs two INPUTs: key down + key up.</summary>
    public static void Type(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        if (TryPaste(text)) return;

        var inputs = new INPUT[text.Length * 2];
        for (int i = 0; i < text.Length; i++)
        {
            ushort cu = text[i];
            inputs[i * 2] = new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new InputUnion { ki = new KEYBDINPUT { wVk = 0, wScan = cu, dwFlags = KEYEVENTF_UNICODE } }
            };
            inputs[i * 2 + 1] = new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new InputUnion { ki = new KEYBDINPUT { wVk = 0, wScan = cu, dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP } }
            };
        }
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
    }

    public static void Backspace(int count)
    {
        if (count <= 0) return;
        if (ShouldPaste(Mode, FocusedClassName()))
        {
            BackspaceOneAtATime(count);
            return;
        }
        var inputs = new INPUT[count * 2];
        for (int i = 0; i < count; i++)
        {
            inputs[i * 2] = new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new InputUnion { ki = new KEYBDINPUT { wVk = VK_BACK, wScan = 0, dwFlags = 0 } }
            };
            inputs[i * 2 + 1] = new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new InputUnion { ki = new KEYBDINPUT { wVk = VK_BACK, wScan = 0, dwFlags = KEYEVENTF_KEYUP } }
            };
        }
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
    }

    public static void PressEnter()
    {
        var inputs = new INPUT[2];
        inputs[0] = new INPUT { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wVk = VK_RETURN } } };
        inputs[1] = new INPUT { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wVk = VK_RETURN, dwFlags = KEYEVENTF_KEYUP } } };
        SendInput(2, inputs, Marshal.SizeOf(typeof(INPUT)));
    }

    /// <summary>
    /// Only Notepad's editor is matched so far. Other controls that lose
    /// keystrokes, such as Terminal's XAML input site, need checking one at a
    /// time, since Ctrl+V is not paste everywhere.
    /// </summary>
    internal static bool ShouldPaste(string mode, string focusedClass)
    {
        if (mode.Equals("clipboard", StringComparison.OrdinalIgnoreCase)) return true;
        if (mode.Equals("keystrokes", StringComparison.OrdinalIgnoreCase)) return false;
        return focusedClass == NotepadEditorClass;
    }

    private static bool TryPaste(string text) =>
        ShouldPaste(Mode, FocusedClassName()) && DictationClipboard.TryPaste(text, PressCtrlV);

    private static void BackspaceOneAtATime(int count)
    {
        var press = new INPUT[2];
        press[0] = new INPUT { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wVk = VK_BACK } } };
        press[1] = new INPUT { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wVk = VK_BACK, dwFlags = KEYEVENTF_KEYUP } } };
        for (int i = 0; i < count; i++)
        {
            SendInput(2, press, Marshal.SizeOf(typeof(INPUT)));
            Thread.Sleep(BackspaceGapMs);
        }
    }

    private static string FocusedClassName()
    {
        var info = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
        if (!GetGUIThreadInfo(0, ref info) || info.hwndFocus == IntPtr.Zero) return "";
        var name = new StringBuilder(256);
        return GetClassNameW(info.hwndFocus, name, name.Capacity) > 0 ? name.ToString() : "";
    }

    private static void PressCtrlV()
    {
        var inputs = new INPUT[4];
        inputs[0] = new INPUT { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wVk = VK_CONTROL } } };
        inputs[1] = new INPUT { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wVk = VK_V } } };
        inputs[2] = new INPUT { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wVk = VK_V, dwFlags = KEYEVENTF_KEYUP } } };
        inputs[3] = new INPUT { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wVk = VK_CONTROL, dwFlags = KEYEVENTF_KEYUP } } };
        SendInput(4, inputs, Marshal.SizeOf(typeof(INPUT)));
    }
}
