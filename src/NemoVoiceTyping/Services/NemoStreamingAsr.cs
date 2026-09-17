using System;
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
    /// <summary>560 ms @ 16 kHz. Shapes and constants below come from
    /// genai_config.json and encoder inspection.</summary>
    private const int ChunkSamples = 8960;

    /// <summary>56 hops per chunk + 9 cached frames.</summary>
    private const int EncoderTimeIn = 65;
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

    /// <summary>Preallocated tensors and input arrays, reused across chunks.</summary>
    private readonly DenseTensor<float> _encInTensor;
    private readonly DenseTensor<long> _lengthTensor;
    private readonly DenseTensor<float> _encFrameTensor;
    private readonly DenseTensor<long> _targetsTensor;
    private readonly DenseTensor<float> _hInTensor;
    private readonly DenseTensor<float> _cInTensor;
    private readonly NamedOnnxValue[] _encOnce;
    private readonly NamedOnnxValue[] _decOnce;
    private readonly NamedOnnxValue[] _jointOnce;

    /// <summary>Samples waiting to complete the next chunk. Never holds a full
    /// chunk once <see cref="PushAudio"/> returns, since a full buffer is
    /// consumed immediately.</summary>
    private readonly float[] _audioBuf = new float[ChunkSamples];
    private int _audioFill;

    /// <summary>Previous mel frames carried into the next chunk's pre-encode cache.</summary>
    private readonly float[,] _melCache = new float[NMels, PreEncodeCacheFrames];
    private bool _melCachePrimed;

    /// <summary>Encoder state, kept across chunks and updated in place.</summary>
    private readonly float[] _cacheLastChannel = new float[1 * EncLayers * LeftContext * EncHidden];
    private readonly float[] _cacheLastTime = new float[1 * EncLayers * EncHidden * ConvContext];
    private readonly long[] _cacheLastChannelLen = new long[1];

    /// <summary>
    /// Decoder LSTM state. The committed state lives in the input tensors'
    /// own buffers, so a step needs no copy before it runs; the pending
    /// buffers stage each step's output until the token turns out not to be
    /// blank. A blank ends the step and the pending state is discarded.
    /// </summary>
    private readonly float[] _hPending = new float[DecLayers * 1 * DecHidden];
    private readonly float[] _cPending = new float[DecLayers * 1 * DecHidden];

    /// <summary>Decode-loop outputs, reused across every symbol.</summary>
    private readonly float[] _decOut = new float[DecHidden];
    private readonly float[] _logits = new float[VocabSize];

    private long _lastToken = BlankId;

    public event Action<string>? TokenEmitted;

    /// <summary>
    /// Session options mirror the nemo session_options in genai_config:
    /// thread spinning is disabled (lower CPU between chunks for an
    /// always-on app) and roughly half the cores are used — the encoder
    /// parallelises well but shouldn't monopolise the machine.
    /// </summary>
    public NemoStreamingAsr(string modelDir)
    {
        var so = new SessionOptions
        {
            LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
        };
        so.AddSessionConfigEntry("session.intra_op.allow_spinning", "0");
        int cores = Math.Max(1, Environment.ProcessorCount / 2);
        so.IntraOpNumThreads = cores;
        so.InterOpNumThreads = 1;

        _encoder = new InferenceSession(Path.Combine(modelDir, "encoder.onnx"), so);
        _decoder = new InferenceSession(Path.Combine(modelDir, "decoder.onnx"), so);
        _joint = new InferenceSession(Path.Combine(modelDir, "joint.onnx"), so);
        _mel = new MelExtractor();
        _tokenizer = new Tokenizer(Path.Combine(modelDir, "vocab.txt"));

        _encInTensor = new DenseTensor<float>(new[] { 1, EncoderTimeIn, NMels });
        _lengthTensor = new DenseTensor<long>(new long[] { EncoderTimeIn }, new[] { 1 });
        _encFrameTensor = new DenseTensor<float>(new[] { 1, 1, EncHidden });
        _targetsTensor = new DenseTensor<long>(new[] { 1, 1 });
        _hInTensor = new DenseTensor<float>(new[] { DecLayers, 1, DecHidden });
        _cInTensor = new DenseTensor<float>(new[] { DecLayers, 1, DecHidden });

        _encOnce = new NamedOnnxValue[]
        {
            NamedOnnxValue.CreateFromTensor("audio_signal", _encInTensor),
            NamedOnnxValue.CreateFromTensor("length", _lengthTensor),
            NamedOnnxValue.CreateFromTensor("cache_last_channel",
                new DenseTensor<float>(_cacheLastChannel, new[] { 1, EncLayers, LeftContext, EncHidden })),
            NamedOnnxValue.CreateFromTensor("cache_last_time",
                new DenseTensor<float>(_cacheLastTime, new[] { 1, EncLayers, EncHidden, ConvContext })),
            NamedOnnxValue.CreateFromTensor("cache_last_channel_len",
                new DenseTensor<long>(_cacheLastChannelLen, new[] { 1 })),
        };

        _decOnce = new NamedOnnxValue[]
        {
            NamedOnnxValue.CreateFromTensor("targets", _targetsTensor),
            NamedOnnxValue.CreateFromTensor("h_in", _hInTensor),
            NamedOnnxValue.CreateFromTensor("c_in", _cInTensor),
        };

        // The decoder emits [1, 640, 1] and the joint wants [1, 1, 640]. With a
        // target length of one those hold the same 640 contiguous floats, so the
        // buffer is shared and only the declared shape differs.
        _jointOnce = new NamedOnnxValue[]
        {
            NamedOnnxValue.CreateFromTensor("encoder_output", _encFrameTensor),
            NamedOnnxValue.CreateFromTensor("decoder_output",
                new DenseTensor<float>(_decOut, new[] { 1, 1, DecHidden })),
        };
    }

    /// <summary>Drop all caches; call between utterances.</summary>
    public void Reset()
    {
        Array.Clear(_cacheLastChannel);
        Array.Clear(_cacheLastTime);
        _cacheLastChannelLen[0] = 0;
        _hInTensor.Buffer.Span.Clear();
        _cInTensor.Buffer.Span.Clear();
        _lastToken = BlankId;
        _audioFill = 0;
        _melCachePrimed = false;
        Array.Clear(_melCache);
    }

    /// <summary>Push new PCM samples; emits tokens as they decode.</summary>
    public void PushAudio(ReadOnlySpan<float> samples)
    {
        while (!samples.IsEmpty)
        {
            int take = Math.Min(ChunkSamples - _audioFill, samples.Length);
            samples[..take].CopyTo(_audioBuf.AsSpan(_audioFill));
            _audioFill += take;
            samples = samples[take..];

            if (_audioFill < ChunkSamples) break;

            ProcessChunk(_audioBuf);
            _audioFill = 0;
        }
    }

    /// <summary>
    /// The encoder's state caches are read from and written back to the same
    /// buffers each chunk. That is safe only because Run has returned by the
    /// time the outputs are copied: ONNX allocates its own output memory, so
    /// nothing aliases the destination at the point of the write.
    /// </summary>
    private void ProcessChunk(float[] chunk)
    {
        int newFrames = ChunkSamples / MelExtractor.HopLength;
        var newMels = _mel.Compute(chunk, newFrames);

        for (int t = 0; t < PreEncodeCacheFrames; t++)
            for (int m = 0; m < NMels; m++)
                _encInTensor[0, t, m] = _melCachePrimed ? _melCache[m, t] : 0f;
        for (int t = 0; t < newFrames; t++)
            for (int m = 0; m < NMels; m++)
                _encInTensor[0, PreEncodeCacheFrames + t, m] = newMels[m, t];

        for (int t = 0; t < PreEncodeCacheFrames; t++)
            for (int m = 0; m < NMels; m++)
                _melCache[m, t] = newMels[m, newFrames - PreEncodeCacheFrames + t];
        _melCachePrimed = true;

        using var encResults = _encoder.Run(_encOnce);
        Tensor<float>? encOut = null;
        foreach (var v in encResults)
        {
            switch (v.Name)
            {
                case "outputs": encOut = v.AsTensor<float>(); break;
                case "cache_last_channel_next": CopyTensor(v.AsTensor<float>(), _cacheLastChannel); break;
                case "cache_last_time_next": CopyTensor(v.AsTensor<float>(), _cacheLastTime); break;
                case "cache_last_channel_len_next": CopyTensor(v.AsTensor<long>(), _cacheLastChannelLen); break;
            }
        }
        if (encOut == null) return;

        var encSpan = AsDense(encOut).Buffer.Span;
        var frameSpan = _encFrameTensor.Buffer.Span;

        for (int t = 0; t < EncTimeOut; t++)
        {
            encSpan.Slice(t * EncHidden, EncHidden).CopyTo(frameSpan);

            int symbols = 0;
            while (symbols < MaxSymbolsPerStep)
            {
                _targetsTensor[0, 0] = _lastToken;

                using var decResults = _decoder.Run(_decOnce);
                foreach (var v in decResults)
                {
                    switch (v.Name)
                    {
                        case "decoder_output": CopyTensor(v.AsTensor<float>(), _decOut); break;
                        case "h_out": CopyTensor(v.AsTensor<float>(), _hPending); break;
                        case "c_out": CopyTensor(v.AsTensor<float>(), _cPending); break;
                    }
                }

                using var jntResults = _joint.Run(_jointOnce);
                foreach (var v in jntResults)
                    if (v.Name == "joint_output") CopyTensor(v.AsTensor<float>(), _logits);

                int best = 0; float bestVal = float.NegativeInfinity;
                for (int k = 0; k < VocabSize; k++)
                    if (_logits[k] > bestVal) { bestVal = _logits[k]; best = k; }

                if (best == BlankId) break;

                _lastToken = best;
                _hPending.CopyTo(_hInTensor.Buffer.Span);
                _cPending.CopyTo(_cInTensor.Buffer.Span);
                TokenEmitted?.Invoke(_tokenizer.Piece(best));
                symbols++;
            }
        }
    }

    private static DenseTensor<T> AsDense<T>(Tensor<T> source)
        => source as DenseTensor<T>
           ?? throw new InvalidOperationException(
               $"Expected a dense tensor from ONNX, got {source.GetType().Name}.");

    /// <summary>
    /// Copies an inference output into a preallocated buffer. Replaces
    /// <c>Tensor&lt;T&gt;.ToArray()</c>, which resolves to the LINQ extension and
    /// allocates a fresh array per call — 6.5 MB on the large object heap for
    /// the encoder's channel cache alone, every 560 ms.
    /// </summary>
    private static void CopyTensor<T>(Tensor<T> source, T[] destination)
        => AsDense(source).Buffer.Span.CopyTo(destination);

    public void Dispose()
    {
        _encoder.Dispose();
        _decoder.Dispose();
        _joint.Dispose();
    }
}
