//-----------------------------------------------------------------------------
// Filename: HilbertFilter.cs
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
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using MathNet.Numerics;
using MathNet.Numerics.IntegralTransforms;
using NAudio.Wave;

namespace AudioScope
{
    public enum AudioSourceEnum
    {
        NAudio = 0,
        //PortAudio = 1,
        Simulation = 2,
        External = 3,
    }

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

        //private const int MAX_OUTPUT_QUEUE_SIZE = 20;
        private const int AUDIO_SAMPLE_PERIOD_MILLISECONDS = 30;
        private const int DISPLAY_ARRAY_STRIDE = 4; // Each element sent to the display function needs to have 4 floats.
        private const int PREVIOUS_SAMPLES_LENGTH = 3 * DISPLAY_ARRAY_STRIDE;

        private static readonly WaveFormat _waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(SAMPLE_RATE, NUM_CHANNELS);

        private WaveInEvent _waveInEvent;
        //private CircularBuffer _audioInBuffer;
        //private PortAudioSharp.Stream _audStm;
        private Timer _simulationTrigger;
        private int _simulationPeriodMilli;
        private DateTime _simulationStartTime;

        private Complex[] _analytic;
        //private MathNet.Filtering.OnlineFilter _lowpass;
        //private MathNet.Filtering.OnlineFilter _noiseLowpass;
        private LowPassFilter _angleLowPass;
        private LowPassFilter _noiseLowPass;

        private Complex[] _timeRingBuffer = new Complex[2 * FFT_SIZE];
        private int _timeIndex = 0;
        private float[] _previousResults = new float[3 * 4];
        private Complex _prevInput = new Complex(0.0f, 0.0f);
        private Complex _prevDiff = new Complex(0.0f, 0.0f);
        private float[] _lastSample;
        private int _outputSampleCount = 0;

        public AudioScope()
        {
            //uint n = FFT_SIZE - BUFFER_SIZE;
            uint n = FFT_SIZE;
            if (n % 2 == 0)
            {
                n -= 1;
            }

            _analytic = MakeAnalytic(n, FFT_SIZE);
            //_lowpass = MathNet.Filtering.OnlineFilter.CreateLowpass(MathNet.Filtering.ImpulseResponse.Infinite, 0.01, 0.5);
            //_noiseLowpass = MathNet.Filtering.OnlineFilter.CreateLowpass(MathNet.Filtering.ImpulseResponse.Infinite, 0.5, 0.7);
            _angleLowPass = new LowPassFilter(0.01f, 0.5f);
            _noiseLowPass = new LowPassFilter(0.5f, 0.7f);
        }

        public float[] GetSample()
        {
            return _lastSample;
        }

        //public void InitAudio(AudioSourceEnum audioSource)
        //{
        //    if (audioSource == AudioSourceEnum.NAudio)
        //    {
        //        //_audioInBuffer = new CircularBuffer(BUFFER_SIZE * _waveFormat.BlockAlign * CIRCULAR_BUFFER_SAMPLES);

        //        _waveInEvent = new WaveInEvent();
        //        _waveInEvent.BufferMilliseconds = AUDIO_SAMPLE_PERIOD_MILLISECONDS;
        //        _waveInEvent.NumberOfBuffers = 3;
        //        _waveInEvent.DeviceNumber = 0;
        //        _waveInEvent.WaveFormat = _waveFormat;
        //        _waveInEvent.DataAvailable += NAudioDataAvailable;
        //    }
        //    //else if (audioSource == AudioSourceEnum.PortAudio)
        //    //{
        //    //    PortAudio.Initialize();
        //    //    StreamParameters stmInParams = new StreamParameters { device = 0, channelCount = NUM_CHANNELS, sampleFormat = SampleFormat.Float32 };
        //    //    _audStm = new Stream(stmInParams, null, SAMPLE_RATE, BUFFER_SIZE, StreamFlags.NoFlag, PortAudioInCallback, null);
        //    //}
        //    else if (audioSource == AudioSourceEnum.Simulation)
        //    {
        //        _simulationPeriodMilli = 1000; // 1000 * BUFFER_SIZE / SAMPLE_RATE;
        //        _simulationTrigger = new Timer(GenerateSimulationSample);
        //    }
        //}

        //public void Start()
        //{
        //    _waveInEvent?.StartRecording();
        //    //_audStm?.Start();
        //    _simulationTrigger?.Change(0, _simulationPeriodMilli);
        //    _simulationStartTime = DateTime.Now;
        //}

        //public void Stop()
        //{
        //    _waveInEvent?.StopRecording();
        //    //_audStm?.Stop();
        //    _simulationTrigger?.Dispose();
        //}

        //private StreamCallbackResult PortAudioInCallback(IntPtr input, IntPtr output, uint frameCount, ref StreamCallbackTimeInfo timeInfo, StreamCallbackFlags statusFlags, IntPtr userDataPtr)
        //{
        //    Console.WriteLine($"AudioInCallback frame count {frameCount}.");

        //    float[] samples = new float[frameCount];
        //    Marshal.Copy(input, samples, 0, (int)frameCount);

        //    ProcessAudioInBuffer(samples);

        //    return StreamCallbackResult.Continue;
        //}

        /// <summary>
        /// Event handler for audio sample being supplied by local capture device.
        /// </summary>
        //private void NAudioDataAvailable(object sender, WaveInEventArgs args)
        //{
        //    //_audioInBuffer.Write(args.Buffer, 0, args.BytesRecorded);
        //    //while (_audioInBuffer.Count > (BUFFER_SIZE * 4))
        //    //{
        //    //    int bytesPerSample = _waveFormat.BlockAlign;

        //    //    byte[] buffer = new byte[BUFFER_SIZE * bytesPerSample];
        //    //    _audioInBuffer.Read(buffer, 0, BUFFER_SIZE * bytesPerSample);

        //    //    List<float> samples = new List<float>();
        //    //    for (int i = 0; i < BUFFER_SIZE * bytesPerSample; i += bytesPerSample)
        //    //    {
        //    //        samples.Add(BitConverter.ToSingle(buffer, i));
        //    //    }

        //    //    ProcessAudioInBuffer(samples.ToArray());
        //    //}

        //    int bytesPerSample = _waveFormat.BlockAlign;
        //    List<Complex> samples = new List<Complex>();
        //    for (int i = 0; i < args.BytesRecorded; i += bytesPerSample)
        //    {
        //        samples.Add(BitConverter.ToSingle(args.Buffer, i));

        //        //if(samples.Count >= BUFFER_SIZE)
        //        //{
        //        //    // It's more important that we keep up compared to dropping samples.
        //        //    break;
        //        //}
        //    }

        //    ProcessSample(samples.ToArray());
        //}

        //private void GenerateSimulationSample(Object userState)
        //{
        //    Complex[] sample = new Complex[BUFFER_SIZE];

        //    double freq = 440.0;// + DateTime.Now.Subtract(_simulationStartTime).TotalSeconds * 100;

        //    //if(DateTime.Now.Second % 2 == 0)
        //    //{
        //    //    simulationFreq = 880.0;
        //    //}

        //    for (int i = 0; i < sample.Length; i++)
        //    {
        //        //for (int j = 1; j < 7; j++)
        //        //{
        //        //    re += Math.Sin(2.0f * Math.PI * ((double)i / (simulationFreq * j)));
        //        //}

        //        //double re = Math.Sin(2.0f * Math.PI * ((double)i / simulationFreq ));

        //        //double re = Math.Sin(2 * Math.PI * i / (freq * 3)) +
        //        //    Math.Cos(4 * Math.PI * i / (freq * 2)) +
        //        //    Math.Cos(7 * Math.PI * i / (freq * 2));

        //        double re = Math.Sin(2.0f * Math.PI * ((double)i / freq));

        //        sample[i] = (float)re;
        //    }

        //    ProcessSample(sample);
        //}

        /// <summary>
        /// Called to process the audio input once the required number of samples are available.
        /// </summary>
        public void ProcessSample(Complex[] samples)
        {
            //Console.WriteLine($"process sample {samples[0]},{samples[1]},{samples[2]},{samples[3]},{samples[4]},{samples[5]},{samples[6]},{samples[7]}");

            //for (int i = 0; i < samples.Length; i++)
            //{
            //    Complex mono = new Complex(GAIN * samples[i], 0.0f);
            //    _timeRingBuffer[_timeIndex + i] = mono;       // Left.
            //    _timeRingBuffer[_timeIndex + FFT_SIZE + i] = mono; // right
            //}

            //Array.Copy(samples, 0, _timeRingBuffer, _timeIndex, samples.Length);
            //Array.Copy(samples, 0, _timeRingBuffer, _timeIndex + FFT_SIZE, samples.Length);
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

            //Complex prevInput = new Complex(_previousResults[_previousResults.Length - DISPLAY_ARRAY_STRIDE], _previousResults[_previousResults.Length - DISPLAY_ARRAY_STRIDE + 1]);
            //Complex secondPrevInput = new Complex(_previousResults[_previousResults.Length - DISPLAY_ARRAY_STRIDE * 2], _previousResults[_previousResults.Length - DISPLAY_ARRAY_STRIDE * 2 + 1]);
            //Complex prevDiff = prevInput - secondPrevInput;

            //Console.WriteLine($"AudioScope output sample {_outputSampleCount}");

            for (int k = 0; k < complexAnalyticBuffer.Length; k++)
            {
                var diff = complexAnalyticBuffer[k] - _prevInput;
                _prevInput = complexAnalyticBuffer[k];

                var angle = (float)Math.Max(Math.Log(Math.Abs(GetAngle(diff, _prevDiff)), 2.0f), -1.0e12);
                _prevDiff = diff;
                var output = _angleLowPass.Apply(angle);

                //Console.WriteLine($"angle {angle}.");

                data[k * DISPLAY_ARRAY_STRIDE] = (float)(complexAnalyticBuffer[k].Real / scale);
                data[k * DISPLAY_ARRAY_STRIDE + 1] = (float)(complexAnalyticBuffer[k].Imaginary / scale);
                data[k * DISPLAY_ARRAY_STRIDE + 2] = (float)Math.Pow(2, output); // Smoothed angular velocity.
                data[k * DISPLAY_ARRAY_STRIDE + 3] = _noiseLowPass.Apply((float)Math.Abs(angle - output)); // Average angular noise.

                //Console.WriteLine($"{ data[k * DISPLAY_ARRAY_STRIDE]},{data[k * DISPLAY_ARRAY_STRIDE + 1] },angle={angle},{data[k * DISPLAY_ARRAY_STRIDE + 2] },{data[k * DISPLAY_ARRAY_STRIDE + 3] }");
            }

            Array.Copy(_previousResults, 0, data, 0, PREVIOUS_SAMPLES_LENGTH);
            _lastSample = data;
            _outputSampleCount++;

            _previousResults = data.Skip(data.Length - PREVIOUS_SAMPLES_LENGTH).ToArray();
        }

        // Angle between two complex numbers scaled into [0, 0.5].
        //public static float GetAngle(Complex u, Complex v)
        //{
        //    var lenProduct = u.Magnitude * v.Magnitude;
        //    if (lenProduct > 0)
        //    {
        //        var theta = (u.Real * v.Real - u.Imaginary * v.Imaginary) / lenProduct;
        //        var angle = Math.Acos(theta);
        //        return (float)(angle / (2 * Math.PI));
        //    }
        //    else
        //    {
        //        return 0;
        //    }
        //}

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
            Console.WriteLine($"MakeAnalytic n={n}, m={m}.");

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
