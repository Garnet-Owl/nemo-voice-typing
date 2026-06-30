using System;

namespace VoiceTyping.Services;

/// <summary>
/// Streaming log-mel spectrogram for the NeMo nemotron streaming encoder.
/// Matches audio_processor_config.json: 16 kHz, n_fft=512, hop=160, win=400,
/// 128 mels, fmin=0, fmax=8000, Hann window, preemphasis=0.97, mag^2, log(x+1e-10).
/// Uses STFT with center=True (reflect padding around each frame center).
/// </summary>
public sealed class MelExtractor
{
    public const int SampleRate = 16000;
    public const int NFft = 512;
    public const int HopLength = 160;
    public const int WinLength = 400;
    public const int NMels = 128;
    public const float Preemphasis = 0.97f;
    private const float LogEps = 1e-10f;

    private readonly float[] _window;       // length WinLength, Hann window
    private readonly float[,] _melFb;       // [NMels, NFft/2 + 1]
    private readonly float[] _fftReal;
    private readonly float[] _fftImag;

    public MelExtractor()
    {
        _window = new float[WinLength];
        for (int i = 0; i < WinLength; i++)
            _window[i] = 0.5f * (1f - MathF.Cos(2f * MathF.PI * i / (WinLength - 1)));

        _melFb = BuildMelFilterbank(NMels, NFft, SampleRate, 0f, SampleRate / 2f);

        _fftReal = new float[NFft];
        _fftImag = new float[NFft];
    }

    /// <summary>
    /// Computes <paramref name="frames"/> log-mel frames starting at the center of
    /// the first hop. Caller is responsible for supplying enough samples
    /// (frames * hop + NFft samples is plenty with center padding).
    /// Output layout: rows=mels (NMels), cols=time (frames).
    /// </summary>
    public float[,] Compute(float[] samples, int frames)
    {
        var mel = new float[NMels, frames];
        var spec = new float[NFft / 2 + 1];

        // Pre-emphasis applied implicitly via differencing in-place (read-only copy)
        for (int t = 0; t < frames; t++)
        {
            int center = t * HopLength;
            FillFrame(samples, center, _fftReal);
            Array.Clear(_fftImag, 0, NFft);
            Fft(_fftReal, _fftImag);
            for (int k = 0; k <= NFft / 2; k++)
            {
                float re = _fftReal[k];
                float im = _fftImag[k];
                spec[k] = re * re + im * im;
            }
            for (int m = 0; m < NMels; m++)
            {
                float sum = 0f;
                for (int k = 0; k <= NFft / 2; k++)
                    sum += _melFb[m, k] * spec[k];
                mel[m, t] = MathF.Log(sum + LogEps);
            }
        }
        return mel;
    }

    /// <summary>
    /// Window a single frame centered at <paramref name="center"/>.
    /// Uses reflect padding when the window extends past the buffer edges
    /// (matches torch / librosa center=True).
    /// Applies pre-emphasis and a Hann window.
    /// </summary>
    private void FillFrame(float[] samples, int center, float[] dst)
    {
        int half = WinLength / 2;
        int start = center - half;
        Array.Clear(dst, 0, NFft);
        for (int i = 0; i < WinLength; i++)
        {
            int idx = start + i;
            float x = Reflect(samples, idx);
            float prev = Reflect(samples, idx - 1);
            float pre = x - Preemphasis * prev;
            dst[i] = pre * _window[i];
        }
    }

    private static float Reflect(float[] s, int i)
    {
        int n = s.Length;
        if (n == 0) return 0f;
        if (i < 0) i = -i;
        if (i >= n) i = 2 * (n - 1) - i;
        if (i < 0 || i >= n) return 0f;
        return s[i];
    }

    /// <summary>In-place radix-2 Cooley-Tukey FFT. n must be a power of two.</summary>
    private static void Fft(float[] re, float[] im)
    {
        int n = re.Length;
        // Bit-reverse permutation
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            float ang = -2f * MathF.PI / len;
            float wRe = MathF.Cos(ang), wIm = MathF.Sin(ang);
            int half = len >> 1;
            for (int i = 0; i < n; i += len)
            {
                float curRe = 1f, curIm = 0f;
                for (int k = 0; k < half; k++)
                {
                    int a = i + k, b = a + half;
                    float tre = curRe * re[b] - curIm * im[b];
                    float tim = curRe * im[b] + curIm * re[b];
                    re[b] = re[a] - tre; im[b] = im[a] - tim;
                    re[a] += tre; im[a] += tim;
                    float nRe = curRe * wRe - curIm * wIm;
                    curIm = curRe * wIm + curIm * wRe;
                    curRe = nRe;
                }
            }
        }
    }

    private static float[,] BuildMelFilterbank(int nMels, int nFft, int sr, float fmin, float fmax)
    {
        int bins = nFft / 2 + 1;
        float[] freqs = new float[bins];
        for (int k = 0; k < bins; k++) freqs[k] = k * (float)sr / nFft;

        float melMin = HzToMel(fmin);
        float melMax = HzToMel(fmax);
        float[] melPoints = new float[nMels + 2];
        for (int i = 0; i < nMels + 2; i++)
            melPoints[i] = MelToHz(melMin + (melMax - melMin) * i / (nMels + 1));

        var fb = new float[nMels, bins];
        for (int m = 0; m < nMels; m++)
        {
            float left = melPoints[m], center = melPoints[m + 1], right = melPoints[m + 2];
            float lWidth = center - left;
            float rWidth = right - center;
            // Slaney-norm-style triangular filters
            for (int k = 0; k < bins; k++)
            {
                float f = freqs[k];
                float w = 0f;
                if (f >= left && f <= center && lWidth > 0) w = (f - left) / lWidth;
                else if (f > center && f <= right && rWidth > 0) w = (right - f) / rWidth;
                fb[m, k] = MathF.Max(0f, w);
            }
        }
        return fb;
    }

    private static float HzToMel(float hz) => 2595f * MathF.Log10(1f + hz / 700f);
    private static float MelToHz(float mel) => 700f * (MathF.Pow(10f, mel / 2595f) - 1f);
}
