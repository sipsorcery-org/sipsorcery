//-----------------------------------------------------------------------------
// Filename: AudioScope.cs
//
// Description: Implementation of a Hilbert filter to visualise audio input.
// Originally based on https://github.com/conundrumer/visual-music-workshop.

// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 29 Feb 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Linq;
using System.Numerics;
using MathNet.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace AudioScope
{
    public class LowPassFilter
    {
        private readonly float _k;
        private readonly float _norm;
        private readonly float _a0;
        private readonly float _a1;
        private readonly float _a2;
        private readonly float _b1;
        private readonly float _b2;

        private float _w1 = 0.0f;
        private float _w2 = 0.0f;

        public LowPassFilter(float n, float q)
        {
            _k = (float)Math.Tan((0.5 * n * Math.PI));
            _norm = 1.0f / (1.0f + _k / q + _k * _k);
            _a0 = _k * _k * _norm;
            _a1 = 2.0f * _a0;
            _a2 = _a0;
            _b1 = 2.0f * (_k * _k - 1.0f) * _norm;
            _b2 = (1.0f - _k / q + _k * _k) * _norm;
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

    public class AudioScope
    {
        public const int NUM_CHANNELS = 1;
        public const int SAMPLE_RATE = 44100;
        public const float maxAmplitude = 4.0F;
        public const int B = (1 << 16) - 1;
        public const int M = 4;
        public const int FFT_SIZE = 1024;
        public const int MID = (FFT_SIZE - 1) / 2;
        public const float DELAY_TIME = MID / SAMPLE_RATE;
        public const float GAIN = 1.0f;
        public const int BUFFER_SIZE = 256;
        public const int CIRCULAR_BUFFER_SAMPLES = 3;
        public const float CUTOFF_FREQ = 0.5f;

        private const int DISPLAY_ARRAY_STRIDE = 4; // Each element sent to the display function needs to have 4 floats.
        private const int PREVIOUS_SAMPLES_LENGTH = 3 * DISPLAY_ARRAY_STRIDE;

        private Complex[] _analytic;
        private LowPassFilter _angleLowPass;
        private LowPassFilter _noiseLowPass;

        private Complex[] _timeRingBuffer = new Complex[2 * FFT_SIZE];
        private int _timeIndex = 0;
        private float[] _previousResults = new float[3 * 4];
        private Complex _prevInput = new Complex(0.0f, 0.0f);
        private Complex _prevDiff = new Complex(0.0f, 0.0f);
        private float[] _lastSample = [];

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

        public float[] GetSample()
        {
            return _lastSample;
        }

        /// <summary>
        /// Called to process the audio input once the required number of samples are available.
        /// </summary>
        public void ProcessSample(Complex[] samples)
        {
            Array.Copy(samples, 0, _timeRingBuffer, _timeIndex, samples.Length > FFT_SIZE ? FFT_SIZE : samples.Length);
            Array.Copy(samples, 0, _timeRingBuffer, _timeIndex + FFT_SIZE, samples.Length > (_timeRingBuffer.Length/2 - _timeIndex) ? _timeRingBuffer.Length / 2 - _timeIndex : samples.Length);

            _timeIndex = (_timeIndex + samples.Length) % FFT_SIZE;

            var freqBuffer = _timeRingBuffer.Skip(_timeIndex).Take(FFT_SIZE).ToArray();

            Fourier.Forward(freqBuffer, FourierOptions.NoScaling);

            for (int j = 0; j < freqBuffer.Length; j++)
            {
                freqBuffer[j] = freqBuffer[j] * _analytic[j];
            }

            Fourier.Inverse(freqBuffer, FourierOptions.NoScaling);

            float scale = (float)FFT_SIZE;

            var complexAnalyticBuffer = freqBuffer.Skip(FFT_SIZE - BUFFER_SIZE).Take(BUFFER_SIZE).ToArray();
            var data = new float[BUFFER_SIZE * DISPLAY_ARRAY_STRIDE + PREVIOUS_SAMPLES_LENGTH];

            for (int k = 0; k < complexAnalyticBuffer.Length; k++)
            {
                var diff = complexAnalyticBuffer[k] - _prevInput;
                _prevInput = complexAnalyticBuffer[k];

                var angle = (float)Math.Max(Math.Log(Math.Abs(GetAngle(diff, _prevDiff)), 2.0f), -1.0e12);
                _prevDiff = diff;
                var output = _angleLowPass.Apply(angle);

                data[k * DISPLAY_ARRAY_STRIDE] = (float)(complexAnalyticBuffer[k].Real / scale);
                data[k * DISPLAY_ARRAY_STRIDE + 1] = (float)(complexAnalyticBuffer[k].Imaginary / scale);
                data[k * DISPLAY_ARRAY_STRIDE + 2] = (float)Math.Pow(2, output); // Smoothed angular velocity.
                data[k * DISPLAY_ARRAY_STRIDE + 3] = _noiseLowPass.Apply((float)Math.Abs(angle - output)); // Average angular noise.
            }

            Array.Copy(_previousResults, 0, data, 0, PREVIOUS_SAMPLES_LENGTH);
            _lastSample = data;

            _previousResults = data.Skip(data.Length - PREVIOUS_SAMPLES_LENGTH).ToArray();
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
}
