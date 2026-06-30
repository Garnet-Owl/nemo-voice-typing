using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows;
using VoiceTyping.Config;
using VoiceTyping.Services;

namespace VoiceTyping;

/// <summary>
/// Owns the dictation lifecycle: bridges <see cref="AudioCapture"/> →
/// <see cref="NemoStreamingAsr"/> → <see cref="TextInjector"/>.
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

    public DictationController(AppConfig config, FloatingPanel panel)
    {
        _config = config;
        _panel = panel;
        _audio.SamplesAvailable += OnSamples;
        _audio.LevelAvailable += level =>
        {
            _panel.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render,
                new Action(() => _panel.PushAudioLevel(level)));
        };
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
                MessageBox.Show("Model not found:\n" + _config.ModelDirectory,
                    "Voice Typing", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            try
            {
                _asr = new NemoStreamingAsr(_config.ModelDirectory);
                _asr.TokenEmitted += OnTokenEmitted;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to load ASR model:\n" + ex.Message,
                    "Voice Typing", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }

        _asr.Reset();
        _running = true;

        _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "ASR Worker" };
        _worker.Start();

        _audio.Start();
        _panel.SetListening(true);
    }

    private void Stop()
    {
        _running = false;
        _audio.Stop();
        _signal.Set();
        _worker?.Join(500);
        _worker = null;
        _panel.SetListening(false);
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
            catch { /* bad audio shouldn't crash dictation */ }
        }
    }

    private bool PendingWork()
    {
        lock (_queueLock) return _queue.Count > 0;
    }

    private void OnTokenEmitted(string piece)
    {
        string text = piece.Length > 0 && piece[0] == '\u2581'
            ? " " + piece.Substring(1)
            : piece;
        if (string.IsNullOrEmpty(text)) return;
        TextInjector.Type(text);
    }

    public void Dispose()
    {
        Stop();
        _audio.Dispose();
        _asr?.Dispose();
        _signal.Dispose();
    }
}
