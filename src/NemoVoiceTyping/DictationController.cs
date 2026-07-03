using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using NemoVoiceTyping.Config;
using NemoVoiceTyping.Services;

namespace NemoVoiceTyping;

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
    private readonly PersonalDictionary _dictionary = new();
    private readonly DictationProcessor _processor;
    private NemoStreamingAsr? _asr;
    private Thread? _worker;
    private System.Threading.Timer? _tickTimer;
    private readonly object _queueLock = new();
    private readonly Queue<float[]> _queue = new();
    private readonly ManualResetEventSlim _signal = new(false);
    private volatile bool _running;
    private volatile bool _loading;
    private long _lastActivityTicks;
    private PersonalDictionaryWindow? _dictWindow;

    /// <summary>
    /// Read from config on every idle check so a change made from the
    /// panel's timeout picker applies mid-dictation.
    /// </summary>
    private TimeSpan IdleTimeout =>
        TimeSpan.FromSeconds(Math.Clamp(_config.IdleTimeoutSeconds,
            Services.DurationText.MinSeconds, Services.DurationText.MaxSeconds));

    public DictationController(AppConfig config, FloatingPanel panel)
    {
        _config = config;
        _panel = panel;
        _processor = new DictationProcessor(_dictionary);
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
        if (_running) Stop(StopReason.Manual);
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
        _processor.Reset();
        _dictionary.ReloadIfChanged();
        _running = true;
        Interlocked.Exchange(ref _lastActivityTicks, DateTime.UtcNow.Ticks);

        _worker = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = "ASR Worker",
            Priority = ThreadPriority.AboveNormal,
        };
        _worker.Start();

        _tickTimer = new System.Threading.Timer(OnTick, null, 100, 100);

        _audio.Start();
        _panel.SetListening(true);
        SoundCues.PlayStart();
    }

    private void OnTick(object? state)
    {
        try { _processor.Tick(); } catch (Exception ex) { Debug.WriteLine(ex); }
        if (!_running) return;

        var lastActivity = new DateTime(Interlocked.Read(ref _lastActivityTicks), DateTimeKind.Utc);
        if (DateTime.UtcNow - lastActivity <= IdleTimeout) return;

        _panel.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_running) Stop(StopReason.Idle);
        }));
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

    private enum StopReason { Manual, Idle, Shutdown }

    /// <summary>
    /// The processor is drained on stop so the last word isn't left in the
    /// buffer waiting for the model's 3.36s VAD threshold. Shutdown plays
    /// no chime.
    /// </summary>
    private void Stop(StopReason reason = StopReason.Manual)
    {
        _running = false;
        _audio.Stop();
        _signal.Set();
        _worker?.Join(500);
        _worker = null;
        _tickTimer?.Dispose();
        _tickTimer = null;
        try { _processor.FlushBuffer(); } catch (Exception ex) { Debug.WriteLine(ex); }
        try { _processor.Tick(); } catch (Exception ex) { Debug.WriteLine(ex); }
        _panel.SetListening(false);

        switch (reason)
        {
            case StopReason.Manual: SoundCues.PlayManualStop(); break;
            case StopReason.Idle: SoundCues.PlayIdleStop(); break;
        }
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

    /// <summary>Opens the personal dictionary editor — a small point-and-click
    /// window (add a word, or a "heard → meant" pair, remove with ✕). Reuses
    /// the same window instance if it's already open.</summary>
    public void OpenPersonalDictionary()
    {
        if (_dictWindow != null)
        {
            _dictWindow.Activate();
            return;
        }
        _dictWindow = new PersonalDictionaryWindow(_dictionary);
        _dictWindow.Closed += (_, _) => _dictWindow = null;
        _dictWindow.Show();
        _dictWindow.Activate();
    }

    private void OnTokenEmitted(string piece)
    {
        Interlocked.Exchange(ref _lastActivityTicks, DateTime.UtcNow.Ticks);
        _processor.Push(piece);
    }

    public void Dispose()
    {
        Stop(StopReason.Shutdown);
        _audio.Dispose();
        _asr?.Dispose();
        _signal.Dispose();
    }
}
