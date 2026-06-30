using System;
using NAudio.Wave;

namespace VoiceTyping.Services;

/// <summary>
/// Captures 16 kHz mono PCM from the default microphone. Raises
/// <see cref="SamplesAvailable"/> with float samples in [-1, 1] and
/// <see cref="LevelAvailable"/> with the RMS level of that buffer (0..1),
/// both on the audio thread.
/// </summary>
public sealed class AudioCapture : IDisposable
{
    public const int SampleRate = 16000;
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
            // 20 ms buffers keep the first emission snappy without flooding callbacks
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
            float f = s * scale;
            buf[i] = f;
            sumSq += f * f;
        }
        double rms = Math.Sqrt(sumSq / sampleCount);
        // Map ~ -45 dBFS .. -5 dBFS to 0..1
        double db = 20.0 * Math.Log10(rms + 1e-9);
        double level = Math.Clamp((db + 45.0) / 40.0, 0.0, 1.0);

        SamplesAvailable?.Invoke(buf);
        LevelAvailable?.Invoke(level);
    }

    public void Stop()
    {
        if (!IsRunning) return;
        _wave?.StopRecording();
        IsRunning = false;
    }

    public void Dispose()
    {
        Stop();
        _wave?.Dispose();
        _wave = null;
    }
}

