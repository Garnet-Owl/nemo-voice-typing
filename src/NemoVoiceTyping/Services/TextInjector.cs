using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace NemoVoiceTyping.Services;

/// <summary>
/// Injects text into whichever window currently has keyboard focus.
/// Uses SendInput with KEYEVENTF_UNICODE so it works for any Unicode codepoint
/// (including BMP and supplementary planes via UTF-16 surrogate pairs) without
/// touching the user's clipboard.
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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    public static void Type(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        // Per UTF-16 code unit we need 2 INPUTs (key down + key up).
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
}
