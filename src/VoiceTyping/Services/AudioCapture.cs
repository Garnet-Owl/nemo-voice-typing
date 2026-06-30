using System;
using NAudio.Wave;

namespace VoiceTyping.Services;

/// <summary>
/// Captures 16 kHz mono PCM from the default microphone. Raises <see cref="SamplesAvailable"/>
/// with float samples in [-1, 1] on the audio thread.
/// </summary>
public sealed class AudioCapture : IDisposable
{
    public const int SampleRate = 16000;
    private WaveInEvent? _wave;

    public event Action<float[]>? SamplesAvailable;
    public bool IsRunning { get; private set; }

    public void Start()
    {
        if (IsRunning) return;
        _wave = new WaveInEvent
        {
            WaveFormat = new WaveFormat(SampleRate, 16, 1),
            BufferMilliseconds = 50, // ~800 samples per callback
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
        for (int i = 0; i < sampleCount; i++)
        {
            short s = (short)(bytes[i * 2] | (bytes[i * 2 + 1] << 8));
            buf[i] = s * scale;
        }
        SamplesAvailable?.Invoke(buf);
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
