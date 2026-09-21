using System;
using NAudio.Wave;

namespace NemoVoiceTyping.Services;

/// <summary>
/// Captures 16 kHz mono PCM from the default microphone. Raises
/// <see cref="SamplesAvailable"/> with float samples in [-1, 1] and
/// <see cref="LevelAvailable"/> with the RMS level of that buffer (0..1),
/// both on the audio thread.
/// </summary>
public sealed class AudioCapture : IDisposable
{
    public const int SampleRate = 16000;

    /// <summary>Digital gain applied to every captured sample. NAudio hands
    /// back raw OS-level mic input with no automatic gain control, and most
    /// mics sit well below full scale at normal speaking volume, so the
    /// signal reaching the ASR model is boosted here before it's used for
    /// anything else.</summary>
    private const float InputGain = 4.0f;

    private WaveInEvent? _wave;

    public event Action<float[]>? SamplesAvailable;
    public event Action<double>? LevelAvailable;
    public bool IsRunning { get; private set; }

    public void Start()
    {
        if (IsRunning) return;
        _wave = new WaveInEvent
        {
            WaveFormat = new WaveFormat(SampleRate, 16, 1),
            BufferMilliseconds = 20,
            NumberOfBuffers = 4,
        };
        _wave.DataAvailable += OnData;
        _wave.StartRecording();
        IsRunning = true;
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        int sampleCount = e.BytesRecorded / 2;
        if (sampleCount == 0) return;

        var buf = new float[sampleCount];
        const float scale = 1f / 32768f;
        var bytes = e.Buffer;
        double sumSq = 0;
        for (int i = 0; i < sampleCount; i++)
        {
            short s = (short)(bytes[i * 2] | (bytes[i * 2 + 1] << 8));
            float f = ApplyGain(s * scale);
            buf[i] = f;
            sumSq += f * f;
        }
        double rms = Math.Sqrt(sumSq / sampleCount);
        double db = 20.0 * Math.Log10(rms + 1e-9);
        double level = MapDecibelsToUnitLevel(db);

        SamplesAvailable?.Invoke(buf);
        LevelAvailable?.Invoke(level);
    }

    /// <summary>Boosts a normalized sample by <see cref="InputGain"/>, then
    /// soft-limits with tanh so already-loud input saturates smoothly
    /// instead of hard-clipping into distortion.</summary>
    internal static float ApplyGain(float sample) => MathF.Tanh(sample * InputGain);

    /// <summary>Maps roughly -60..-12 dBFS onto 0..1 for the level meter.
    /// Calibrated against recorded speech peaking around -19 dBFS; the
    /// previous -45..-5 window left the meter barely moving for normal
    /// speaking volume.</summary>
    internal static double MapDecibelsToUnitLevel(double db)
        => Math.Clamp((db + 60.0) / 48.0, 0.0, 1.0);

    /// <summary>Stops and releases the underlying wave-in device. A fresh
    /// <see cref="WaveInEvent"/> is opened on the next <see cref="Start"/>,
    /// so the native device handle must not outlive one dictation session
    /// or repeated toggling leaks a handle per call.</summary>
    public void Stop()
    {
        if (!IsRunning) return;
        _wave!.DataAvailable -= OnData;
        _wave.StopRecording();
        _wave.Dispose();
        _wave = null;
        IsRunning = false;
    }

    public void Dispose() => Stop();
}

