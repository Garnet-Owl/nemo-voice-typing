using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace VoiceTyping.Services;

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
    private long _lastToken = BlankId;

    public event Action<string>? TokenEmitted;

    public NemoStreamingAsr(string modelDir)
    {
        var so = new SessionOptions { LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR };
        _encoder = new InferenceSession(Path.Combine(modelDir, "encoder.onnx"), so);
        _decoder = new InferenceSession(Path.Combine(modelDir, "decoder.onnx"), so);
        _joint = new InferenceSession(Path.Combine(modelDir, "joint.onnx"), so);
        _mel = new MelExtractor();
        _tokenizer = new Tokenizer(Path.Combine(modelDir, "vocab.txt"));
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
        // 56 mel frames from this chunk (hop=160 → 8960/160=56), center=true, reflect-padded
        int newFrames = ChunkSamples / MelExtractor.HopLength;
        var newMels = _mel.Compute(chunk, newFrames);

        // Build encoder input: [9 cached mel frames] + [56 new mel frames] → 65 frames
        var encIn = new DenseTensor<float>(new[] { 1, EncoderTimeIn, NMels });
        for (int t = 0; t < PreEncodeCacheFrames; t++)
            for (int m = 0; m < NMels; m++)
                encIn[0, t, m] = _melCachePrimed ? _melCache[m, t] : 0f;
        for (int t = 0; t < newFrames; t++)
            for (int m = 0; m < NMels; m++)
                encIn[0, PreEncodeCacheFrames + t, m] = newMels[m, t];

        // Update mel cache with last 9 frames of newMels
        for (int t = 0; t < PreEncodeCacheFrames; t++)
            for (int m = 0; m < NMels; m++)
                _melCache[m, t] = newMels[m, newFrames - PreEncodeCacheFrames + t];
        _melCachePrimed = true;

        var lengthTensor = new DenseTensor<long>(new long[] { EncoderTimeIn }, new[] { 1 });
        var cacheCh = new DenseTensor<float>(_cacheLastChannel, new[] { 1, EncLayers, LeftContext, EncHidden });
        var cacheTime = new DenseTensor<float>(_cacheLastTime, new[] { 1, EncLayers, EncHidden, ConvContext });
        var cacheLen = new DenseTensor<long>(_cacheLastChannelLen, new[] { 1 });

        var encInputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("audio_signal", encIn),
            NamedOnnxValue.CreateFromTensor("length", lengthTensor),
            NamedOnnxValue.CreateFromTensor("cache_last_channel", cacheCh),
            NamedOnnxValue.CreateFromTensor("cache_last_time", cacheTime),
            NamedOnnxValue.CreateFromTensor("cache_last_channel_len", cacheLen),
        };

        using var encResults = _encoder.Run(encInputs);
        DenseTensor<float>? encOut = null;
        foreach (var v in encResults)
        {
            switch (v.Name)
            {
                case "outputs": encOut = (DenseTensor<float>)v.AsTensor<float>().ToDenseTensor(); break;
                case "cache_last_channel_next": _cacheLastChannel = v.AsTensor<float>().ToArray(); break;
                case "cache_last_time_next": _cacheLastTime = v.AsTensor<float>().ToArray(); break;
                case "cache_last_channel_len_next": _cacheLastChannelLen = v.AsTensor<long>().ToArray(); break;
            }
        }
        if (encOut == null) return;

        // RNN-T greedy decode over EncTimeOut frames
        for (int t = 0; t < EncTimeOut; t++)
        {
            // Slice encoder frame [1, 1, 1024]
            var encFrame = new float[EncHidden];
            for (int k = 0; k < EncHidden; k++) encFrame[k] = encOut[0, t, k];

            int symbols = 0;
            while (symbols < MaxSymbolsPerStep)
            {
                // Decoder: targets [1, 1] = lastToken, h_in [2,1,640], c_in [2,1,640]
                var targets = new DenseTensor<long>(new[] { _lastToken }, new[] { 1, 1 });
                var hIn = new DenseTensor<float>(_h, new[] { DecLayers, 1, DecHidden });
                var cIn = new DenseTensor<float>(_c, new[] { DecLayers, 1, DecHidden });

                using var decResults = _decoder.Run(new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor("targets", targets),
                    NamedOnnxValue.CreateFromTensor("h_in", hIn),
                    NamedOnnxValue.CreateFromTensor("c_in", cIn),
                });

                float[] decOut640 = Array.Empty<float>();
                float[] hOutNext = _h;
                float[] cOutNext = _c;
                foreach (var v in decResults)
                {
                    switch (v.Name)
                    {
                        case "decoder_output": decOut640 = v.AsTensor<float>().ToArray(); break;
                        case "h_out": hOutNext = v.AsTensor<float>().ToArray(); break;
                        case "c_out": cOutNext = v.AsTensor<float>().ToArray(); break;
                    }
                }

                // Joint: encoder_output [1,1,1024], decoder_output [1,1,640]
                var encJ = new DenseTensor<float>(encFrame, new[] { 1, 1, EncHidden });
                // decOut640 layout: [batch=1, hidden=640, target_len=1] -> same memory as [1,1,640]
                var decJ = new DenseTensor<float>(decOut640, new[] { 1, 1, DecHidden });

                using var jntResults = _joint.Run(new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor("encoder_output", encJ),
                    NamedOnnxValue.CreateFromTensor("decoder_output", decJ),
                });

                float[] logits = Array.Empty<float>();
                foreach (var v in jntResults)
                    if (v.Name == "joint_output") logits = v.AsTensor<float>().ToArray();

                // logits shape [1,1,1,1025]
                int best = 0; float bestVal = float.NegativeInfinity;
                for (int k = 0; k < VocabSize; k++)
                {
                    if (logits[k] > bestVal) { bestVal = logits[k]; best = k; }
                }

                if (best == BlankId)
                {
                    break; // advance to next encoder frame
                }
                else
                {
                    _lastToken = best;
                    _h = hOutNext;
                    _c = cOutNext;
                    TokenEmitted?.Invoke(_tokenizer.Piece(best));
                    symbols++;
                }
            }
        }
    }

    public void Dispose()
    {
        _encoder.Dispose();
        _decoder.Dispose();
        _joint.Dispose();
    }
}
