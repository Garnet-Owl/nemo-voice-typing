using System;
using VoiceTyping.Config;

namespace VoiceTyping;

/// <summary>
/// Coordinates the dictation lifecycle. Phase-1 placeholder: just flips state
/// on the panel. Phase-2 wires in audio capture, ASR, and text injection.
/// </summary>
public sealed class DictationController : IDisposable
{
    private readonly AppConfig _config;
    private readonly FloatingPanel _panel;
    private bool _listening;

    public DictationController(AppConfig config, FloatingPanel panel)
    {
        _config = config;
        _panel = panel;
        _panel.SetStatus("Idle — press " + _config.Hotkey, listening: false);
    }

    public void Toggle()
    {
        _listening = !_listening;
        if (_listening)
        {
            _panel.SetStatus("Listening…", listening: true);
            _panel.SetPartial("(speech engine coming online in Phase 2)");
        }
        else
        {
            _panel.SetStatus("Idle — press " + _config.Hotkey, listening: false);
            _panel.SetPartial("");
        }
    }

    public void Dispose() { }
}
