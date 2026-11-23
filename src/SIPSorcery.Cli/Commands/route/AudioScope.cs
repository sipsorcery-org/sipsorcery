//-----------------------------------------------------------------------------
// Filename: AudioScope.cs
//
// Description: Implementation of a Hilbert filter to visualise audio input. The
// audio is turned into its analytic signal (real part = the audio, imaginary part
// = its Hilbert transform) and each sample becomes a point in the plane, which
// AudioScopeRenderer draws as the "audio scope" trace used by the Softphone and
// WebRTCAudioScope examples. Originally based on
// https://github.com/conundrumer/visual-music-workshop.
//
// This is the examples/Softphone/AudioScope/AudioScope.cs analysis, brought into
// the CLI for the "route --scope-mode hilbert" video.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 29 Feb 2020	Aaron Clauson	Created, Dublin, Ireland.
// 08 Jun 2026	Aaron Clauson	Converted from OpenGL implementation to C# version.
// 24 Oct 2026	Aaron Clauson	Copied into the CLI from the Softphone example.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Numerics;
using MathNet.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace SIPSorcery.Cli.Commands.Route;

public sealed class LowPassFilter
{
    private readonly float _a0;
    private readonly float _a1;
    private readonly float _a2;
    private readonly float _b1;
    private readonly float _b2;

    private float _w1;
    private float _w2;

    public LowPassFilter(float n, float q)
    {
        float k = (float)Math.Tan(0.5 * n * Math.PI);
        float norm = 1.0f / (1.0f + k / q + k * k);
        _a0 = k * k * norm;
        _a1 = 2.0f * _a0;
        _a2 = _a0;
        _b1 = 2.0f * (k * k - 1.0f) * norm;
        _b2 = (1.0f - k / q + k * k) * norm;
    }

    public float Apply(float x)
    {
        float w0 = x - _b1 * _w1 - _b2 * _w2;
        float y = _a0 * w0 + _a1 * _w1 + _a2 * _w2;
        _w2 = _w1;
        _w1 = w0;

        return y;
    }
}

public sealed class AudioScope
{
    public const int FFT_SIZE = 1024;
    public const int BUFFER_SIZE = 256;

    // Display radius soft-limit. The analytic signal can exceed the unit circle on loud passages,
    // and a radius of 1.0 reaches the top/bottom edge of the scope, so anything louder clips
    // against the viewport and draws straight "squarish" edges. Compressing the radius through
    // tanh keeps the trace inside the circle: it is near-linear for small signals (quiet detail
    // preserved) and gently rounds off loud transients instead of clipping.
    private const double DISPLAY_GAIN = 1.0;   // Small-signal gain.
    private const double DISPLAY_LIMIT = 0.9;  // Max radius (1.0 == touches the top/bottom edge).

    /// <summary>Floats per output vertex: x, y, smoothed angular velocity, angular noise.</summary>
    public const int DISPLAY_ARRAY_STRIDE = 4;

    // Extra leading vertices kept from the original OpenGL line-strip-adjacency layout, so the trace
    // is the same length as the one the examples draw.
    private const int DISPLAY_ADJACENCY = 3;

    private readonly Complex[] _analytic;
    private readonly LowPassFilter _angleLowPass;
    private readonly LowPassFilter _noiseLowPass;

    private readonly Complex[] _timeRingBuffer = new Complex[2 * FFT_SIZE];
    private int _timeIndex;
    private Complex _prevInput = new(0.0f, 0.0f);
    private Complex _prevDiff = new(0.0f, 0.0f);
    private float[]? _lastSample;

    public AudioScope()
    {
        uint n = FFT_SIZE;
        if (n % 2 == 0)
        {
            n -= 1;
        }

        _analytic = MakeAnalytic(n, FFT_SIZE);
        _angleLowPass = new LowPassFilter(0.01f, 0.5f);
        _noiseLowPass = new LowPassFilter(0.5f, 0.7f);
    }

    /// <summary>
    /// The vertices computed by the most recent <see cref="ProcessSample"/>, <see cref="DISPLAY_ARRAY_STRIDE"/>
    /// floats each, or null if no audio has been processed yet.
    /// </summary>
    public float[]? GetSample() => _lastSample;

    /// <summary>
    /// Processes a block of audio samples (real part in -1..1, imaginary part zero).
    /// </summary>
    public void ProcessSample(Complex[] samples)
    {
        Array.Copy(samples, 0, _timeRingBuffer, _timeIndex, samples.Length > FFT_SIZE ? FFT_SIZE : samples.Length);
        Array.Copy(samples, 0, _timeRingBuffer, _timeIndex + FFT_SIZE, samples.Length > (_timeRingBuffer.Length / 2 - _timeIndex) ? _timeRingBuffer.Length / 2 - _timeIndex : samples.Length);

        _timeIndex = (_timeIndex + samples.Length) % FFT_SIZE;

        var freqBuffer = _timeRingBuffer.AsSpan(_timeIndex, FFT_SIZE).ToArray();

        Fourier.Forward(freqBuffer, FourierOptions.NoScaling);

        for (int j = 0; j < freqBuffer.Length; j++)
        {
            freqBuffer[j] = freqBuffer[j] * _analytic[j];
        }

        Fourier.Inverse(freqBuffer, FourierOptions.NoScaling);

        float scale = FFT_SIZE;

        // Draw a single contiguous slice of the analytic signal each frame, taken from the centre of
        // the inverse-FFT block to stay away from the circular-convolution wrap-around at the edges.
        int displayCount = BUFFER_SIZE + DISPLAY_ADJACENCY;
        int analyticOffset = (FFT_SIZE - displayCount) / 2;
        var complexAnalyticBuffer = freqBuffer.AsSpan(analyticOffset, displayCount).ToArray();
        var data = new float[displayCount * DISPLAY_ARRAY_STRIDE];

        for (int k = 0; k < complexAnalyticBuffer.Length; k++)
        {
            var diff = complexAnalyticBuffer[k] - _prevInput;
            _prevInput = complexAnalyticBuffer[k];

            var angle = (float)Math.Max(Math.Log(Math.Abs(GetAngle(diff, _prevDiff)), 2.0f), -1.0e12);
            _prevDiff = diff;
            var output = _angleLowPass.Apply(angle);

            double reX = complexAnalyticBuffer[k].Real / scale;
            double imY = complexAnalyticBuffer[k].Imaginary / scale;

            double r = Math.Sqrt(reX * reX + imY * imY);
            if (r > 1e-9)
            {
                double compressed = DISPLAY_LIMIT * Math.Tanh(r * DISPLAY_GAIN / DISPLAY_LIMIT);
                double s = compressed / r;
                reX *= s;
                imY *= s;
            }

            data[k * DISPLAY_ARRAY_STRIDE] = (float)reX;
            data[k * DISPLAY_ARRAY_STRIDE + 1] = (float)imY;
            data[k * DISPLAY_ARRAY_STRIDE + 2] = (float)Math.Pow(2, output); // Smoothed angular velocity.
            data[k * DISPLAY_ARRAY_STRIDE + 3] = _noiseLowPass.Apply(Math.Abs(angle - output)); // Average angular noise.
        }

        _lastSample = data;
    }

    public static float GetAngle(Complex v, Complex u)
    {
        var len_v_mul_u = v.Norm() * u;
        var len_u_mul_v = u.Norm() * v;
        var left = (len_v_mul_u - len_u_mul_v).Norm();
        var right = (len_v_mul_u + len_u_mul_v).Norm();

        return (float)(Math.Atan2(left, right) / Math.PI);
    }

    private static Complex[] MakeAnalytic(uint n, uint m)
    {
        var impulse = new Complex[m];

        var mid = (n - 1) / 2;

        impulse[mid] = new Complex(1.0f, 0.0f);
        float re = -1.0f / (mid - 1);
        for (int i = 1; i < mid + 1; i++)
        {
            if (i % 2 == 0)
            {
                impulse[mid + i] = new Complex(re, impulse[mid + i].Imaginary);
                impulse[mid - i] = new Complex(re, impulse[mid - i].Imaginary);
            }
            else
            {
                float im = (float)(2.0 / Math.PI / i);
                impulse[mid + i] = new Complex(impulse[mid + i].Real, im);
                impulse[mid - i] = new Complex(impulse[mid - i].Real, -im);
            }
            // hamming window
            var k = 0.53836 + 0.46164 * Math.Cos(i * Math.PI / (mid + 1));
            impulse[mid + i] = new Complex((float)(impulse[mid + i].Real * k), (float)(impulse[mid + i].Imaginary * k));
            impulse[mid - i] = new Complex((float)(impulse[mid - i].Real * k), (float)(impulse[mid - i].Imaginary * k));
        }

        Fourier.Forward(impulse, FourierOptions.NoScaling);

        return impulse;
    }
}
