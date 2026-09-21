using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NemoVoiceTyping.Services;

namespace NemoVoiceTyping.IntegrationTests;

/// <summary>
/// Runs a WAV file through the production <see cref="NemoStreamingAsr"/>
/// pipeline exactly as <see cref="AudioCapture"/> would feed it (16 kHz mono
/// float PCM), for offline pressure-testing against recorded samples instead
/// of a live microphone.
/// </summary>
public static class AsrHarness
{
    public sealed record Result(string Text, bool HasControlTokenLeak, TimeSpan AudioDuration, TimeSpan ProcessingTime);

    public static float[] LoadAsSixteenKMono(string wavPath, float gain = 1f)
    {
        using var reader = new AudioFileReader(wavPath);
        var mono = reader.WaveFormat.Channels == 1 ? (ISampleProvider)reader : reader.ToMono();
        var resampled = new WdlResamplingSampleProvider(mono, AudioCapture.SampleRate);

        var chunks = new System.Collections.Generic.List<float[]>();
        var buffer = new float[AudioCapture.SampleRate];
        int total = 0;
        int read;
        while ((read = resampled.Read(buffer, 0, buffer.Length)) > 0)
        {
            var chunk = new float[read];
            Array.Copy(buffer, chunk, read);
            chunks.Add(chunk);
            total += read;
        }

        var samples = new float[total];
        int offset = 0;
        foreach (var chunk in chunks)
        {
            Array.Copy(chunk, 0, samples, offset, chunk.Length);
            offset += chunk.Length;
        }

        for (int i = 0; i < samples.Length; i++)
            samples[i] = AudioCapture.ApplyGain(samples[i] * gain);

        return samples;
    }

    public static Result Transcribe(string modelDir, string wavPath, float gain = 1f)
    {
        var samples = LoadAsSixteenKMono(wavPath, gain);
        var audioDuration = TimeSpan.FromSeconds(samples.Length / (double)AudioCapture.SampleRate);

        var sb = new StringBuilder();
        using var asr = new NemoStreamingAsr(modelDir);
        asr.TokenEmitted += piece =>
        {
            if (piece.Length > 0 && piece[0] == '▁')
            {
                sb.Append(' ');
                sb.Append(piece, 1, piece.Length - 1);
            }
            else
            {
                sb.Append(piece);
            }
        };

        var sw = Stopwatch.StartNew();
        asr.PushAudio(samples);
        sw.Stop();

        var text = sb.ToString().Trim();
        var hasLeak = text.Contains('<') && text.Contains('>');
        return new Result(text, hasLeak, audioDuration, sw.Elapsed);
    }

    public static string EnsureModel()
    {
        var dl = new ModelDownloader();
        if (!dl.IsComplete())
            dl.DownloadAsync(null, CancellationToken.None).GetAwaiter().GetResult();
        return dl.CacheDir;
    }
}
