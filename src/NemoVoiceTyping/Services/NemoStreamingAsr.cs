using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace NemoVoiceTyping.Services;

/// <summary>
/// Streaming RNN-T decoder for the NVIDIA NeMo "nemotron-speech-streaming-en-0.6b" model.
/// Pipelines audio samples through a log-mel extractor, a chunked transformer encoder,
/// and a stateful LSTM predictor + joint network using greedy decoding.
/// Emits new tokens via <see cref="TokenEmitted"/>.
/// </summary>
public sealed class NemoStreamingAsr : IDisposable
{
    // Shapes & constants from genai_config.json / encoder inspection
    private const int ChunkSamples = 8960;          // 560 ms @ 16 kHz
    private const int EncoderTimeIn = 65;           // 56 hops in chunk + 9 cached frames
    private const int PreEncodeCacheFrames = 9;
    private const int NMels = 128;
    private const int EncHidden = 1024;
    private const int EncLayers = 24;
    private const int LeftContext = 70;
    private const int ConvContext = 8;
    private const int EncTimeOut = 7;
    private const int DecLayers = 2;
    private const int DecHidden = 640;
    private const int VocabSize = 1025;
    private const int BlankId = 1024;
    private const int MaxSymbolsPerStep = 10;

    private readonly InferenceSession _encoder;
    private readonly InferenceSession _decoder;
    private readonly InferenceSession _joint;
    private readonly MelExtractor _mel;
    private readonly Tokenizer _tokenizer;

    // Preallocated tensors & input arrays (re-used across chunks)
    private readonly DenseTensor<float> _encInTensor;
    private readonly DenseTensor<long> _lengthTensor;
    private readonly DenseTensor<float> _encFrameTensor;
    private readonly DenseTensor<long> _targetsTensor;
    private readonly DenseTensor<float> _hInTensor;
    private readonly DenseTensor<float> _cInTensor;
    private readonly NamedOnnxValue[] _encOnce;
    private readonly NamedOnnxValue[] _decOnce;
    private readonly NamedOnnxValue[] _jointOnce;

    // Sliding audio buffer: enough for chunk + ~512 lookahead for windowing
    private readonly List<float> _audioBuf = new();
    // Cached previous mel frames for pre-encode cache
    private readonly float[,] _melCache = new float[NMels, PreEncodeCacheFrames];
    private bool _melCachePrimed;

    // Encoder state (kept across chunks)
    private float[] _cacheLastChannel = new float[1 * EncLayers * LeftContext * EncHidden];
    private float[] _cacheLastTime = new float[1 * EncLayers * EncHidden * ConvContext];
    private long[] _cacheLastChannelLen = new long[] { 0 };

    // Decoder state (kept across utterances)
    private float[] _h = new float[DecLayers * 1 * DecHidden];
    private float[] _c = new float[DecLayers * 1 * DecHidden];
    private float[] _hPending = new float[DecLayers * 1 * DecHidden];
    private float[] _cPending = new float[DecLayers * 1 * DecHidden];
    private long _lastToken = BlankId;

    public event Action<string>? TokenEmitted;

    public NemoStreamingAsr(string modelDir)
    {
        var so = new SessionOptions
        {
            LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            // Match nemo session_options from genai_config: disable thread spinning
            // (lower CPU between chunks, friendlier for an always-on background app)
        };
        so.AddSessionConfigEntry("session.intra_op.allow_spinning", "0");
        // Use roughly half the cores. The encoder is the only heavy op and it
        // parallelises well, but we don't want to monopolise the box.
        int cores = Math.Max(1, Environment.ProcessorCount / 2);
        so.IntraOpNumThreads = cores;
        so.InterOpNumThreads = 1;

        _encoder = new InferenceSession(Path.Combine(modelDir, "encoder.onnx"), so);
        _decoder = new InferenceSession(Path.Combine(modelDir, "decoder.onnx"), so);
        _joint = new InferenceSession(Path.Combine(modelDir, "joint.onnx"), so);
        _mel = new MelExtractor();
        _tokenizer = new Tokenizer(Path.Combine(modelDir, "vocab.txt"));

        // Preallocate tensors that don't change shape across calls
        _encInTensor = new DenseTensor<float>(new[] { 1, EncoderTimeIn, NMels });
        _lengthTensor = new DenseTensor<long>(new long[] { EncoderTimeIn }, new[] { 1 });
        _encFrameTensor = new DenseTensor<float>(new[] { 1, 1, EncHidden });
        _targetsTensor = new DenseTensor<long>(new[] { 1, 1 });
        _hInTensor = new DenseTensor<float>(_h, new[] { DecLayers, 1, DecHidden });
        _cInTensor = new DenseTensor<float>(_c, new[] { DecLayers, 1, DecHidden });

        _encOnce = new NamedOnnxValue[]
        {
            NamedOnnxValue.CreateFromTensor("audio_signal", _encInTensor),
            NamedOnnxValue.CreateFromTensor("length", _lengthTensor),
            // cache_* tensors are rebuilt each call because the backing array changes
            null!, null!, null!,
        };

        _decOnce = new NamedOnnxValue[]
        {
            NamedOnnxValue.CreateFromTensor("targets", _targetsTensor),
            NamedOnnxValue.CreateFromTensor("h_in", _hInTensor),
            NamedOnnxValue.CreateFromTensor("c_in", _cInTensor),
        };

        _jointOnce = new NamedOnnxValue[]
        {
            NamedOnnxValue.CreateFromTensor("encoder_output", _encFrameTensor),
            null!,
        };
    }

    /// <summary>Drop all caches; call between utterances.</summary>
    public void Reset()
    {
        Array.Clear(_cacheLastChannel);
        Array.Clear(_cacheLastTime);
        _cacheLastChannelLen[0] = 0;
        Array.Clear(_h);
        Array.Clear(_c);
        _lastToken = BlankId;
        _audioBuf.Clear();
        _melCachePrimed = false;
        Array.Clear(_melCache);
    }

    /// <summary>Push new PCM samples; emits tokens as they decode.</summary>
    public void PushAudio(ReadOnlySpan<float> samples)
    {
        // Append to ring-ish buffer
        for (int i = 0; i < samples.Length; i++) _audioBuf.Add(samples[i]);

        while (_audioBuf.Count >= ChunkSamples)
        {
            // Take first ChunkSamples
            var chunk = new float[ChunkSamples];
            _audioBuf.CopyTo(0, chunk, 0, ChunkSamples);
            _audioBuf.RemoveRange(0, ChunkSamples);

            ProcessChunk(chunk);
        }
    }

    private void ProcessChunk(float[] chunk)
    {
        int newFrames = ChunkSamples / MelExtractor.HopLength; // 56
        var newMels = _mel.Compute(chunk, newFrames);

        // Fill encoder input in-place: 9 cached frames + 56 new frames
        for (int t = 0; t < PreEncodeCacheFrames; t++)
            for (int m = 0; m < NMels; m++)
                _encInTensor[0, t, m] = _melCachePrimed ? _melCache[m, t] : 0f;
        for (int t = 0; t < newFrames; t++)
            for (int m = 0; m < NMels; m++)
                _encInTensor[0, PreEncodeCacheFrames + t, m] = newMels[m, t];

        // Rotate mel cache
        for (int t = 0; t < PreEncodeCacheFrames; t++)
            for (int m = 0; m < NMels; m++)
                _melCache[m, t] = newMels[m, newFrames - PreEncodeCacheFrames + t];
        _melCachePrimed = true;

        // Cache tensors. Backing arrays may be reassigned per call by ONNX, so
        // we rebuild the wrapper but reuse the float buffers ONNX returns.
        _encOnce[2] = NamedOnnxValue.CreateFromTensor("cache_last_channel",
            new DenseTensor<float>(_cacheLastChannel, new[] { 1, EncLayers, LeftContext, EncHidden }));
        _encOnce[3] = NamedOnnxValue.CreateFromTensor("cache_last_time",
            new DenseTensor<float>(_cacheLastTime, new[] { 1, EncLayers, EncHidden, ConvContext }));
        _encOnce[4] = NamedOnnxValue.CreateFromTensor("cache_last_channel_len",
            new DenseTensor<long>(_cacheLastChannelLen, new[] { 1 }));

        using var encResults = _encoder.Run(_encOnce);
        Tensor<float>? encOut = null;
        foreach (var v in encResults)
        {
            switch (v.Name)
            {
                case "outputs": encOut = v.AsTensor<float>(); break;
                case "cache_last_channel_next": _cacheLastChannel = v.AsTensor<float>().ToArray(); break;
                case "cache_last_time_next": _cacheLastTime = v.AsTensor<float>().ToArray(); break;
                case "cache_last_channel_len_next": _cacheLastChannelLen = v.AsTensor<long>().ToArray(); break;
            }
        }
        if (encOut == null) return;

        for (int t = 0; t < EncTimeOut; t++)
        {
            for (int k = 0; k < EncHidden; k++)
                _encFrameTensor[0, 0, k] = encOut[0, t, k];

            int symbols = 0;
            while (symbols < MaxSymbolsPerStep)
            {
                _targetsTensor[0, 0] = _lastToken;
                // Refresh h/c in tensors (state arrays may have been replaced)
                CopyInto(_h, _hInTensor);
                CopyInto(_c, _cInTensor);

                using var decResults = _decoder.Run(_decOnce);
                float[] decOut640 = Array.Empty<float>();
                foreach (var v in decResults)
                {
                    switch (v.Name)
                    {
                        case "decoder_output": decOut640 = v.AsTensor<float>().ToArray(); break;
                        case "h_out": _hPending = v.AsTensor<float>().ToArray(); break;
                        case "c_out": _cPending = v.AsTensor<float>().ToArray(); break;
                    }
                }

                _jointOnce[1] = NamedOnnxValue.CreateFromTensor("decoder_output",
                    new DenseTensor<float>(decOut640, new[] { 1, 1, DecHidden }));

                using var jntResults = _joint.Run(_jointOnce);
                float[] logits = Array.Empty<float>();
                foreach (var v in jntResults)
                    if (v.Name == "joint_output") logits = v.AsTensor<float>().ToArray();

                int best = 0; float bestVal = float.NegativeInfinity;
                for (int k = 0; k < VocabSize; k++)
                    if (logits[k] > bestVal) { bestVal = logits[k]; best = k; }

                if (best == BlankId) break;

                _lastToken = best;
                _h = _hPending;
                _c = _cPending;
                TokenEmitted?.Invoke(_tokenizer.Piece(best));
                symbols++;
            }
        }
    }

    private static void CopyInto(float[] src, DenseTensor<float> dst)
    {
        var span = dst.Buffer.Span;
        src.AsSpan().CopyTo(span);
    }

    public void Dispose()
    {
        _encoder.Dispose();
        _decoder.Dispose();
        _joint.Dispose();
    }
}
