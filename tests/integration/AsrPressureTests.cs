using System;
using System.IO;
using NemoVoiceTyping.Services;
using Xunit;
using Xunit.Abstractions;

namespace NemoVoiceTyping.IntegrationTests;

/// <summary>
/// Exercises the real ASR pipeline against a recorded Kenyan-accented English
/// clip instead of a live microphone. The sample lives under the repo's
/// gitignored .var/samples/ folder, so these tests no-op with a console
/// message on any machine that doesn't have it.
/// </summary>
public class AsrPressureTests
{
    private readonly ITestOutputHelper _output;

    public AsrPressureTests(ITestOutputHelper output) => _output = output;

    private static string? FindSampleWav()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, ".var", "samples", "kenyan_en_sample.wav");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    [Fact]
    public void Transcribes_without_leaking_control_tokens()
    {
        var wav = FindSampleWav();
        if (wav == null)
        {
            _output.WriteLine("No .var/samples/kenyan_en_sample.wav found; skipping.");
            return;
        }

        var modelDir = AsrHarness.EnsureModel();
        var result = AsrHarness.Transcribe(modelDir, wav);

        _output.WriteLine($"Transcript: {result.Text}");
        _output.WriteLine($"Audio: {result.AudioDuration.TotalSeconds:F2}s, processed in {result.ProcessingTime.TotalMilliseconds:F0}ms");

        Assert.False(result.HasControlTokenLeak, $"Transcript leaked a control token: {result.Text}");
        Assert.True(result.ProcessingTime < result.AudioDuration,
            $"Processing ({result.ProcessingTime}) took longer than the audio itself ({result.AudioDuration}) — not real-time safe.");
    }

    [Fact]
    public void Level_meter_visibly_reacts_to_normal_speaking_volume()
    {
        var wav = FindSampleWav();
        if (wav == null)
        {
            _output.WriteLine("No .var/samples/kenyan_en_sample.wav found; skipping.");
            return;
        }

        var samples = AsrHarness.LoadAsSixteenKMono(wav);
        const int bufferSize = 320; // 20ms at 16kHz, matches AudioCapture.BufferMilliseconds
        double maxLevel = 0;
        for (int i = 0; i + bufferSize <= samples.Length; i += bufferSize)
        {
            double sumSq = 0;
            for (int j = 0; j < bufferSize; j++) sumSq += samples[i + j] * samples[i + j];
            double rms = Math.Sqrt(sumSq / bufferSize);
            double db = 20.0 * Math.Log10(rms + 1e-9);
            maxLevel = Math.Max(maxLevel, AudioCapture.MapDecibelsToUnitLevel(db));
        }

        _output.WriteLine($"peak meter level for normal recorded speech: {maxLevel:F2}");
        Assert.True(maxLevel > 0.75, $"Meter should visibly react to normal speaking volume; peak was only {maxLevel:F2}");
    }

    [Theory]
    [InlineData(1.0f)]
    [InlineData(0.3f)]
    [InlineData(0.15f)]
    public void Reports_transcript_quality_at_varying_input_volume(float gain)
    {
        var wav = FindSampleWav();
        if (wav == null)
        {
            _output.WriteLine("No .var/samples/kenyan_en_sample.wav found; skipping.");
            return;
        }

        var modelDir = AsrHarness.EnsureModel();
        var result = AsrHarness.Transcribe(modelDir, wav, gain);

        _output.WriteLine($"gain={gain:F2} -> \"{result.Text}\"");
        Assert.False(result.HasControlTokenLeak, $"Transcript leaked a control token at gain {gain}: {result.Text}");
    }
}
