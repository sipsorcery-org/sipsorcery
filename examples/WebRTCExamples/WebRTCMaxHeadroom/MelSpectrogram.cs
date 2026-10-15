//-----------------------------------------------------------------------------
// Filename: MelSpectrogram.cs
//
// Description: A C# port of Wav2Lip's librosa mel-spectrogram front-end (the audio.py
// melspectrogram() pipeline), so the lip-sync model can run fully in-process with no
// Python sidecar. The output must match librosa numerically - Wav2Lip is sensitive to
// its mel front-end - so every stage mirrors the reference implementation exactly:
//
//   1. pre-emphasis        y[n] = x[n] - 0.97*x[n-1]
//   2. STFT                n_fft=800, hop=200, periodic Hann, centred with zero padding
//                          (librosa >= 0.10 pad_mode='constant'), unscaled forward FFT
//   3. mel filterbank      80 mels, 55-7600 Hz, SLANEY mel scale + slaney area norm
//                          (librosa.filters.mel defaults htk=False, norm='slaney')
//   4. amp -> dB           20*log10(max(1e-5, x)) - 20        (ref_level_db)
//   5. normalise           clip(8*((S+100)/100) - 4, -4, 4)   (symmetric, max_abs=4)
//
// Validated against the Python output with a golden-file test (--mel-test): the same
// PCM must produce the same [80 x T] matrix within float tolerance.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace demo;

public sealed class MelSpectrogram
{
    public const int SampleRate = 16000;
    public const int NMels = 80;
    public const int MelsPerSecond = SampleRate / Hop;   // 80 mel columns per second.

    private const int NFft = 800;
    private const int Hop = 200;
    private const int Bins = NFft / 2 + 1;
    private const double FMin = 55.0;
    private const double FMax = 7600.0;
    private const double Preemphasis = 0.97;
    private const double MinLevelDb = -100.0;
    private const double RefLevelDb = 20.0;
    private const double MaxAbsValue = 4.0;
    private static readonly double MinLevel = Math.Pow(10.0, MinLevelDb / 20.0);   // 1e-5.

    private readonly double[] _window = BuildHannPeriodic(NFft);
    private readonly double[][] _melBasis = BuildMelBasis();

    /// <summary>
    /// Computes the normalised mel spectrogram of 16 kHz mono PCM. Returns [NMels, frames]
    /// with frames = pcm.Length / 200 + 1, values in [-4, 4] - identical layout and scaling
    /// to the Python audio.melspectrogram() the model was trained against.
    /// </summary>
    public float[,] Compute(ReadOnlySpan<short> pcm16)
    {
        int n = pcm16.Length;

        // Pre-emphasis on the [-1,1) float signal.
        var y = new double[n];
        double prev = 0.0;
        for (int i = 0; i < n; i++)
        {
            double s = pcm16[i] / 32768.0;
            y[i] = s - Preemphasis * prev;
            prev = s;
        }

        int pad = NFft / 2;                       // librosa center=True zero padding.
        int frames = (n + 2 * pad - NFft) / Hop + 1;
        var mel = new float[NMels, frames];
        var buf = new Complex[NFft];
        var mags = new double[Bins];

        for (int t = 0; t < frames; t++)
        {
            int start = t * Hop - pad;
            for (int k = 0; k < NFft; k++)
            {
                int idx = start + k;
                double v = (idx >= 0 && idx < n) ? y[idx] : 0.0;
                buf[k] = new Complex(v * _window[k], 0.0);
            }

            Fourier.Forward(buf, FourierOptions.NoScaling);   // numpy-convention forward FFT.
            for (int b = 0; b < Bins; b++)
            {
                mags[b] = buf[b].Magnitude;
            }

            for (int m = 0; m < NMels; m++)
            {
                var basis = _melBasis[m];
                double acc = 0.0;
                for (int b = 0; b < Bins; b++)
                {
                    acc += basis[b] * mags[b];
                }
                double db = 20.0 * Math.Log10(Math.Max(MinLevel, acc)) - RefLevelDb;
                double norm = 2.0 * MaxAbsValue * ((db - MinLevelDb) / -MinLevelDb) - MaxAbsValue;
                mel[m, t] = (float)Math.Clamp(norm, -MaxAbsValue, MaxAbsValue);
            }
        }

        return mel;
    }

    /// <summary>Periodic Hann window (scipy get_window('hann', N, fftbins=True)).</summary>
    private static double[] BuildHannPeriodic(int n)
    {
        var w = new double[n];
        for (int i = 0; i < n; i++)
        {
            w[i] = 0.5 - 0.5 * Math.Cos(2.0 * Math.PI * i / n);
        }
        return w;
    }

    // Slaney mel scale (librosa htk=False): linear below 1 kHz, logarithmic above.
    private static double HzToMel(double hz)
    {
        const double fsp = 200.0 / 3.0;
        const double minLogHz = 1000.0;
        double logStep = Math.Log(6.4) / 27.0;
        return hz < minLogHz ? hz / fsp : minLogHz / fsp + Math.Log(hz / minLogHz) / logStep;
    }

    private static double MelToHz(double mel)
    {
        const double fsp = 200.0 / 3.0;
        const double minLogMel = 1000.0 / fsp;
        double logStep = Math.Log(6.4) / 27.0;
        return mel < minLogMel ? mel * fsp : 1000.0 * Math.Exp(logStep * (mel - minLogMel));
    }

    /// <summary>librosa.filters.mel(sr=16000, n_fft=800, n_mels=80, fmin=55, fmax=7600):
    /// triangular filters on the slaney mel scale with slaney (area) normalisation.</summary>
    private static double[][] BuildMelBasis()
    {
        // FFT bin centre frequencies: linspace(0, sr/2, n_fft/2 + 1).
        var fftFreqs = new double[Bins];
        for (int i = 0; i < Bins; i++)
        {
            fftFreqs[i] = i * (SampleRate / 2.0) / (Bins - 1);
        }

        // NMels + 2 points evenly spaced in mel, converted back to Hz.
        var hzPts = new double[NMels + 2];
        double melMin = HzToMel(FMin), melMax = HzToMel(FMax);
        for (int i = 0; i < hzPts.Length; i++)
        {
            hzPts[i] = MelToHz(melMin + (melMax - melMin) * i / (NMels + 1));
        }

        var basis = new double[NMels][];
        for (int m = 0; m < NMels; m++)
        {
            var row = new double[Bins];
            double lo = hzPts[m], mid = hzPts[m + 1], hi = hzPts[m + 2];
            double enorm = 2.0 / (hi - lo);                    // slaney normalisation.
            for (int b = 0; b < Bins; b++)
            {
                double lower = (fftFreqs[b] - lo) / (mid - lo);
                double upper = (hi - fftFreqs[b]) / (hi - mid);
                row[b] = Math.Max(0.0, Math.Min(lower, upper)) * enorm;
            }
            basis[m] = row;
        }
        return basis;
    }
}
