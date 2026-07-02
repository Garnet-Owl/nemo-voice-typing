using System;
using NAudio.Wave;

namespace NemoVoiceTyping.Services;

/// <summary>
/// Short, synthesized audio cues for dictation start/stop.
///
/// Tones are generated at runtime rather than bundling audio files — this
/// keeps the app free of any licensing/attribution to track and follows
/// common voice-assistant conventions (Siri/Google Assistant/Windows):
/// a bright, quick two-tone RISING chime for "listening started"; a quick
/// two-tone FALLING chime for "you stopped it"; and a slower, smoothly
/// decaying tone for "it stopped itself after being idle" so the two kinds
/// of stop are easy to tell apart by ear without looking at the screen.
/// </summary>
public static class SoundCues
{
    private const int SampleRate = 44100;

    /// <summary>Two-tone rising chime — dictation just started listening.</summary>
    public static void PlayStart() => PlayAsync(BuildTwoTone(660, 880));

    /// <summary>Two-tone falling chime — the user toggled dictation off.</summary>
    public static void PlayManualStop() => PlayAsync(BuildTwoTone(880, 660));

    /// <summary>Slow fading tone — dictation stopped itself after 30s idle.</summary>
    public static void PlayIdleStop() => PlayAsync(BuildFadeOut(700, 500));

    private static void PlayAsync(float[] samples)
    {
        try
        {
            var provider = new ArrayWaveProvider(ToPcm16(samples), SampleRate);
            var wave = new WaveOutEvent();
            wave.Init(provider);
            wave.PlaybackStopped += (_, _) => wave.Dispose();
            wave.Play();
        }
        catch { /* a missing/busy audio device shouldn't crash dictation */ }
    }

    private static float[] BuildTwoTone(double f1, double f2)
    {
        var a = Tone(f1, 0.09, 0.35);
        var b = Tone(f2, 0.11, 0.35);
        var gap = new float[(int)(SampleRate * 0.015)];
        var result = new float[a.Length + gap.Length + b.Length];
        Array.Copy(a, 0, result, 0, a.Length);
        Array.Copy(gap, 0, result, a.Length, gap.Length);
        Array.Copy(b, 0, result, a.Length + gap.Length, b.Length);
        return result;
    }

    /// <summary>A single note that glides slightly downward and eases out to
    /// silence over half a second — reads as "winding down" rather than a
    /// crisp on/off click.</summary>
    private static float[] BuildFadeOut(double fStart, double fEnd)
    {
        const double duration = 0.5;
        int n = (int)(SampleRate * duration);
        var result = new float[n];
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)SampleRate;
            double frac = t / duration;
            double freq = fStart + (fEnd - fStart) * frac;
            double phase = 2 * Math.PI * freq * t;
            double env = Math.Pow(1.0 - frac, 1.6); // ease-out fade to zero
            result[i] = (float)(Math.Sin(phase) * env * 0.32);
        }
        return result;
    }

    private static float[] Tone(double freq, double seconds, double amplitude)
    {
        int n = (int)(SampleRate * seconds);
        var result = new float[n];
        int attack = Math.Max(1, (int)(n * 0.15));
        int release = Math.Max(1, (int)(n * 0.3));
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)SampleRate;
            double env = 1.0;
            if (i < attack) env = i / (double)attack;
            else if (i > n - release) env = (n - i) / (double)release;
            result[i] = (float)(Math.Sin(2 * Math.PI * freq * t) * amplitude * env);
        }
        return result;
    }

    private static byte[] ToPcm16(float[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            short s = (short)Math.Clamp(samples[i] * 32767f, short.MinValue, short.MaxValue);
            bytes[i * 2] = (byte)(s & 0xFF);
            bytes[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
        }
        return bytes;
    }

    private sealed class ArrayWaveProvider : IWaveProvider
    {
        private readonly byte[] _data;
        private int _pos;

        public ArrayWaveProvider(byte[] data, int sampleRate)
        {
            _data = data;
            WaveFormat = new WaveFormat(sampleRate, 16, 1);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(byte[] buffer, int offset, int count)
        {
            int remaining = _data.Length - _pos;
            int n = Math.Min(remaining, count);
            if (n <= 0) return 0;
            Array.Copy(_data, _pos, buffer, offset, n);
            _pos += n;
            return n;
        }
    }
}
