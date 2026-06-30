using System;
using System.IO;
using System.Text.Json;

namespace VoiceTyping.Config;

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

    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VoiceTyping", "config.json");

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
