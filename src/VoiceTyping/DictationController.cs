using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows;
using VoiceTyping.Config;
using VoiceTyping.Services;

namespace VoiceTyping;

/// <summary>
/// Owns the dictation lifecycle: bridges <see cref="AudioCapture"/> →
/// <see cref="NemoStreamingAsr"/> → <see cref="TextInjector"/>.
///
/// Runs the ASR on a dedicated worker thread so the audio callback thread
/// only enqueues samples and returns immediately. Emitted SentencePiece pieces
/// are flushed to the focused window as they arrive, so the user sees text
/// land in their target app with low latency.
/// </summary>
public sealed class DictationController : IDisposable
{
    private readonly AppConfig _config;
    private readonly FloatingPanel _panel;
    private readonly AudioCapture _audio = new();
    private NemoStreamingAsr? _asr;
    private Thread? _worker;
    private readonly object _queueLock = new();
    private readonly Queue<float[]> _queue = new();
    private readonly ManualResetEventSlim _signal = new(false);
    private volatile bool _running;
    private readonly StringBuilder _utterance = new();

    public DictationController(AppConfig config, FloatingPanel panel)
    {
        _config = config;
        _panel = panel;
        _panel.SetStatus(StatusFor(false), listening: false);
        _audio.SamplesAvailable += OnSamples;
    }

    public void Toggle()
    {
        if (_running) Stop();
        else Start();
    }

    private void Start()
    {
        if (_asr == null)
        {
            if (!Directory.Exists(_config.ModelDirectory))
            {
                _panel.SetStatus("Model not found", listening: false);
                _panel.SetPartial(_config.ModelDirectory);
                return;
            }
            _panel.SetStatus("Loading model…", listening: false);
            _panel.SetPartial("");
            try
            {
                _asr = new NemoStreamingAsr(_config.ModelDirectory);
                _asr.TokenEmitted += OnTokenEmitted;
            }
            catch (Exception ex)
            {
                _panel.SetStatus("Model load failed", listening: false);
                _panel.SetPartial(ex.Message);
                return;
            }
        }

        _asr.Reset();
        _utterance.Clear();
        _running = true;

        _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "ASR Worker" };
        _worker.Start();

        _audio.Start();
        _panel.SetStatus("Listening…", listening: true);
        _panel.SetPartial("");
    }

    private void Stop()
    {
        _running = false;
        _audio.Stop();
        _signal.Set();
        _worker?.Join(500);
        _worker = null;
        _panel.SetStatus(StatusFor(false), listening: false);
    }

    private void OnSamples(float[] buf)
    {
        if (!_running) return;
        lock (_queueLock) _queue.Enqueue(buf);
        _signal.Set();
    }

    private void WorkerLoop()
    {
        while (_running || PendingWork())
        {
            float[]? next = null;
            lock (_queueLock)
            {
                if (_queue.Count > 0) next = _queue.Dequeue();
            }

            if (next == null)
            {
                _signal.Wait(50);
                _signal.Reset();
                continue;
            }

            try { _asr?.PushAudio(next); }
            catch (Exception ex)
            {
                Application.Current?.Dispatcher.BeginInvoke(() =>
                    _panel.SetPartial("ASR error: " + ex.Message));
            }
        }
    }

    private bool PendingWork()
    {
        lock (_queueLock) return _queue.Count > 0;
    }

    private void OnTokenEmitted(string piece)
    {
        // SentencePiece "▁foo" → " foo"; "bar" → "bar"
        string text = piece.Length > 0 && piece[0] == '\u2581'
            ? " " + piece.Substring(1)
            : piece;
        if (string.IsNullOrEmpty(text)) return;

        _utterance.Append(text);
        var preview = _utterance.ToString();
        if (preview.Length > 80) preview = "…" + preview[^80..];

        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            _panel.SetPartial(preview);
            TextInjector.Type(text);
        });
    }

    private string StatusFor(bool listening) => listening
        ? "Listening…"
        : $"Idle — press {_config.Hotkey}";

    public void Dispose()
    {
        Stop();
        _audio.Dispose();
        _asr?.Dispose();
        _signal.Dispose();
    }
}
