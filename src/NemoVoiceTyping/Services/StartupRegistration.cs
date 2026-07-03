using System;
using System.IO;
using Microsoft.Win32;

namespace NemoVoiceTyping.Services;

public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "NemoVoiceTyping";
    private const string ExeName = "Nemo Voice Typing.exe";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string;
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true)!;
        if (enabled)
        {
            key.SetValue(ValueName, BuildRunCommand(Environment.ProcessPath, AppContext.BaseDirectory));
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    /// <summary>
    /// Builds the quoted command written to the Run key. Prefers the live
    /// process path; falls back to the app directory + exe name. Never uses
    /// Assembly.Location, which is empty in single-file publishes (IL3000).
    /// </summary>
    internal static string BuildRunCommand(string? processPath, string baseDirectory)
    {
        var exe = string.IsNullOrEmpty(processPath)
            ? Path.Combine(baseDirectory, ExeName)
            : processPath;
        return $"\"{exe}\"";
    }
}
