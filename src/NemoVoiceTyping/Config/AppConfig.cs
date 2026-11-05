using System;
using System.IO;
using System.Text.Json;

namespace NemoVoiceTyping.Config;

public sealed class AppConfig
{
    /// <summary>
    /// Optional override for the model directory. Empty means "use the
    /// per-user cache under %LOCALAPPDATA%\VoiceTyping\models\..." and
    /// download from Hugging Face if missing.
    /// </summary>
    public string ModelDirectory { get; set; } = "";

    public string Hotkey { get; set; } = "Ctrl+Alt+A";
    public bool RunAtStartup { get; set; } = false;
    public double PanelLeft { get; set; } = double.NaN;
    public double PanelTop { get; set; } = double.NaN;
    public bool AlwaysOnTop { get; set; } = true;

    /// <summary>
    /// How long the mic stays active with no recognized speech before
    /// dictation auto-stops. Changed from the floating panel's timeout
    /// popup; applied live on the next idle check.
    /// </summary>
    public int IdleTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// How dictated text reaches the focused window.
    ///
    /// <c>auto</c> sends keystrokes, and pastes instead into Windows 11
    /// Notepad, which drops synthesised keystrokes. <c>keystrokes</c> never
    /// touches the clipboard but garbles text in Notepad. <c>clipboard</c>
    /// always pastes, which fails where Ctrl+V is not paste, such as some
    /// terminals and password fields.
    /// </summary>
    public string TextInjection { get; set; } = "auto";

    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "NemoVoiceTyping", "config.json");

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                return JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
            }
        }
        catch { }
        return new AppConfig();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }
        catch { }
    }
}
