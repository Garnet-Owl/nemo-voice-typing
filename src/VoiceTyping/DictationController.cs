using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using VoiceTyping.Config;
using VoiceTyping.Services;

namespace VoiceTyping;

/// <summary>
/// Owns the dictation lifecycle: bridges <see cref="AudioCapture"/> →
/// <see cref="NemoStreamingAsr"/> → <see cref="TextInjector"/>, and lazily
/// downloads the model from Hugging Face on first use.
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
    private volatile bool _loading;

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
        if (_loading) return;
        if (_running) Stop();
        else _ = StartAsync();
    }

    private async Task StartAsync()
    {
        if (_asr == null)
        {
            _loading = true;
            _panel.SetLoading(true);
            try
            {
                var modelDir = await EnsureModelAsync().ConfigureAwait(true);
                if (modelDir == null) { _panel.SetLoading(false); return; }
                _asr = await Task.Run(() => new NemoStreamingAsr(modelDir)).ConfigureAwait(true);
                _asr.TokenEmitted += OnTokenEmitted;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to load ASR model:\n" + ex.Message,
                    "Nemo Voice Typing", MessageBoxButton.OK, MessageBoxImage.Error);
                _panel.SetLoading(false);
                return;
            }
            finally
            {
                _loading = false;
                _panel.SetLoading(false);
            }
        }

        _asr.Reset();
        _running = true;

        _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "ASR Worker" };
        _worker.Start();

        _audio.Start();
        _panel.SetListening(true);
    }

    /// <summary>
    /// Returns a usable model directory, downloading from Hugging Face if needed.
    /// Priority: explicit ModelDirectory config (if it exists), then the per-user
    /// cache under %LOCALAPPDATA%\VoiceTyping\models\...
    /// </summary>
    private async Task<string?> EnsureModelAsync()
    {
        if (!string.IsNullOrEmpty(_config.ModelDirectory)
            && Directory.Exists(_config.ModelDirectory)
            && File.Exists(Path.Combine(_config.ModelDirectory, "encoder.onnx")))
        {
            return _config.ModelDirectory;
        }

        var dl = new ModelDownloader();
        if (dl.IsComplete()) return dl.CacheDir;

        var progress = new Progress<(int file, int totalFiles, long received, long total)>(p =>
        {
            string pct = p.total > 0 ? $" {p.received * 100 / Math.Max(1, p.total)}%" : "";
            _panel.SetLoadingText($"Downloading model {p.file + 1}/{p.totalFiles}{pct}");
        });

        try
        {
            _panel.SetLoadingText("Downloading model…");
            await dl.DownloadAsync(progress, CancellationToken.None).ConfigureAwait(true);
            return dl.CacheDir;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not download the speech model:\n" + ex.Message
                + "\n\nCheck your internet connection and try again.",
                "Nemo Voice Typing", MessageBoxButton.OK, MessageBoxImage.Error);
            return null;
        }
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
