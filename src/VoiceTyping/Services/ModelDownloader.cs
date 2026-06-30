using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace VoiceTyping.Services;

/// <summary>
/// Downloads the NeMo streaming ASR model from the Hugging Face Hub on first run.
///
/// Cache location: %LOCALAPPDATA%\VoiceTyping\models\nemotron-speech-streaming-en-0.6b\v3\
/// Uninstalling the app does NOT clear this cache (model is shareable across versions).
/// </summary>
public sealed class ModelDownloader
{
    public const string DefaultRepo = "Garnet-Owl/nemo-voice-typing-asr";

    // Files required by NemoStreamingAsr at runtime
    public static readonly string[] RequiredFiles = new[]
    {
        "encoder.onnx", "encoder.onnx.data",
        "decoder.onnx", "decoder.onnx.data",
        "joint.onnx", "joint.onnx.data",
        "silero_vad.onnx",
        "vocab.txt",
        "tokenizer.json", "tokenizer_config.json",
        "genai_config.json", "model_config.json", "audio_processor_config.json",
        "LICENSE", "NOTICES",
    };

    public static string DefaultCacheDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VoiceTyping", "models", "nemotron-speech-streaming-en-0.6b", "v3");

    public string CacheDir { get; }
    public string Repo { get; }

    public ModelDownloader(string? cacheDir = null, string repo = DefaultRepo)
    {
        CacheDir = cacheDir ?? DefaultCacheDir;
        Repo = repo;
    }

    public bool IsComplete()
    {
        if (!Directory.Exists(CacheDir)) return false;
        foreach (var f in RequiredFiles)
            if (!File.Exists(Path.Combine(CacheDir, f))) return false;
        return true;
    }

    /// <summary>
    /// Downloads any missing files. <paramref name="progress"/> reports
    /// (file index, total files, bytes received for current file, total bytes for current file).
    /// </summary>
    public async Task DownloadAsync(IProgress<(int file, int totalFiles, long received, long total)>? progress,
        CancellationToken ct)
    {
        Directory.CreateDirectory(CacheDir);
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("VoiceTyping/1.0 (+https://github.com/Garnet-Owl/nemo-voice-typing)");

        for (int i = 0; i < RequiredFiles.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            var name = RequiredFiles[i];
            var dst = Path.Combine(CacheDir, name);
            if (File.Exists(dst) && new FileInfo(dst).Length > 0) continue;

            var url = $"https://huggingface.co/{Repo}/resolve/main/{name}";
            await DownloadFileAsync(http, url, dst, i, RequiredFiles.Length, progress, ct);
        }
    }

    private static async Task DownloadFileAsync(HttpClient http, string url, string dst,
        int fileIndex, int totalFiles,
        IProgress<(int file, int totalFiles, long received, long total)>? progress,
        CancellationToken ct)
    {
        var tmp = dst + ".part";
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        long total = resp.Content.Headers.ContentLength ?? -1;
        long received = 0;
        var buffer = new byte[81920];

        await using (var fs = File.Create(tmp))
        await using (var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        {
            int read;
            while ((read = await src.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await fs.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                received += read;
                progress?.Report((fileIndex, totalFiles, received, total));
            }
        }
        if (File.Exists(dst)) File.Delete(dst);
        File.Move(tmp, dst);
    }
}
