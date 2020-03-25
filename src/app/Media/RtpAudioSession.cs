//-----------------------------------------------------------------------------
// Filename: RtpAudioSession.cs
//
// Description: A lightweight audio only RTP session suitable for testing.
// No rendering or capturing capabilities.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 19 Mar 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorcery.SIP.App;

namespace SIPSorcery.Media
{
    public enum DummyAudioSourcesEnum
    {
        None = 0,
        Music = 2,
        Silence = 3,
        WhiteNoise = 4,
        SineWave = 5,
        PinkNoise = 6
    }

    //public enum SampleRatesEnum
    //{
    //    Rate_8000 = 0,
    //    Rate_16000 = 1
    //}

    public class DummyAudioOptions
    {
        /// <summary>
        /// The type of audio source to use.
        /// </summary>
        public DummyAudioSourcesEnum AudioSource;

        /// <summary>
        /// If using a pre-recorded audio source this is the audio source file.
        /// </summary>
        public string SourceFile;

        /// <summary>
        /// Supports selecting the audio sample rate for codecs that support it.
        /// PCMU and PCMA only support 8KHz. G722 supports 8 or 16KHz.
        /// </summary>
        //public SampleRatesEnum SampleRate;
    }

    public class RtpAudioSession : RTPSession, IMediaSession
    {
        private const int DTMF_EVENT_DURATION = 1200;        // Default duration for a DTMF event.
        private const int DTMF_EVENT_PAYLOAD_ID = 101;
        private const int SAMPLE_RATE = 8000;                 // G711 and G722 (mistakenly) use an 8LHz clock.
        private const int AUDIO_SAMPLE_PERIOD_MILLISECONDS = 20;
        private static readonly byte PCMU_SILENCE_BYTE_ZERO = 0x7F;
        private static readonly byte PCMU_SILENCE_BYTE_ONE = 0xFF;
        private static readonly byte PCMA_SILENCE_BYTE_ZERO = 0x55;
        private static readonly byte PCMA_SILENCE_BYTE_ONE = 0xD5;

        private static Microsoft.Extensions.Logging.ILogger Log = SIPSorcery.Sys.Log.Logger;

        private StreamReader _audioStreamReader;
        private SignalGenerator _signalGenerator;
        private Timer _audioStreamTimer;
        private DummyAudioOptions _audioOpts;
        private SDPMediaFormatsEnum _audioCodec;
        private int _sampleRate = SAMPLE_RATE;

        private G722Codec _g722Codec;
        private G722CodecState _g722CodecState;

        public event Action<byte[], uint, uint, int> OnVideoSampleReady;
        public event Action<Complex[]> OnAudioScopeSampleReady;
        public event Action<Complex[]> OnHoldAudioScopeSampleReady;

        public RtpAudioSession(DummyAudioOptions audioOptions, SDPMediaFormatsEnum codec) :
            base(AddressFamily.InterNetwork, false, false, false)
        {
            if (!(codec == SDPMediaFormatsEnum.PCMA || codec == SDPMediaFormatsEnum.PCMU || codec == SDPMediaFormatsEnum.G722))
            {
                throw new ApplicationException("The codec must be PCMA, PCMU or G722.");
            }

            _audioOpts = audioOptions;
            _audioCodec = codec;
            //_sampleRate = 8000; // (audioOptions.SampleRate == SampleRatesEnum.Rate_8000) ? 8000 : 16000;

            var audioFormat = new SDPMediaFormat((int)codec, codec.ToString(), _sampleRate);

            if (codec == SDPMediaFormatsEnum.G722)
            {
                _g722Codec = new G722Codec();
                _g722CodecState = new G722CodecState(64000, G722Flags.None);
            }

            // RTP event support.
            SDPMediaFormat rtpEventFormat = new SDPMediaFormat(DTMF_EVENT_PAYLOAD_ID);
            rtpEventFormat.SetFormatAttribute($"{RTPSession.TELEPHONE_EVENT_ATTRIBUTE}/{_sampleRate}");
            rtpEventFormat.SetFormatParameterAttribute("0-16");

            var audioCapabilities = new List<SDPMediaFormat> { audioFormat, rtpEventFormat };

            MediaStreamTrack audioTrack = new MediaStreamTrack(null, SDPMediaTypesEnum.audio, false, audioCapabilities);
            base.addTrack(audioTrack);
        }

        public void Close(string reason)
        {
            base.CloseSession(reason);

            _audioStreamTimer?.Dispose();
            _audioStreamReader?.Close();
        }

        public Task SendDtmf(byte key, CancellationToken ct)
        {
            var dtmfEvent = new RTPEvent(key, false, RTPEvent.DEFAULT_VOLUME, DTMF_EVENT_DURATION, DTMF_EVENT_PAYLOAD_ID);
            return SendDtmfEvent(dtmfEvent, ct);
        }

        public void SendMedia(SDPMediaTypesEnum mediaType, uint samplePeriod, byte[] sample)
        {
            if (mediaType == SDPMediaTypesEnum.audio)
            {
                base.SendAudioFrame(samplePeriod, (int)_audioCodec, sample);
            }
            else
            {
                throw new NotImplementedException();
            }
        }

        /// <summary>
        /// Initialises the audio source as required.
        /// </summary>
        public Task Start()
        {
            // If required start the audio source.
            if (_audioOpts != null && _audioOpts.AudioSource != DummyAudioSourcesEnum.None)
            {
                if (_audioOpts.AudioSource == DummyAudioSourcesEnum.Silence)
                {
                    _audioStreamTimer = new Timer(SendSilenceSample, null, 0, AUDIO_SAMPLE_PERIOD_MILLISECONDS);
                }
                else if (_audioOpts.AudioSource == DummyAudioSourcesEnum.PinkNoise ||
                    _audioOpts.AudioSource == DummyAudioSourcesEnum.WhiteNoise ||
                     _audioOpts.AudioSource == DummyAudioSourcesEnum.SineWave)
                {
                    _signalGenerator = new SignalGenerator(_sampleRate, 1);

                    switch (_audioOpts.AudioSource)
                    {
                        case DummyAudioSourcesEnum.PinkNoise:
                            _signalGenerator.Type = SignalGeneratorType.Pink;
                            break;
                        case DummyAudioSourcesEnum.SineWave:
                            _signalGenerator.Type = SignalGeneratorType.Sin;
                            break;
                        case DummyAudioSourcesEnum.WhiteNoise:
                        default:
                            _signalGenerator.Type = SignalGeneratorType.White;
                            break;
                    }

                    _audioStreamTimer = new Timer(SendNoiseSample, null, 0, AUDIO_SAMPLE_PERIOD_MILLISECONDS);
                }
                else if (_audioOpts.AudioSource == DummyAudioSourcesEnum.Music)
                {
                    if (String.IsNullOrEmpty(_audioOpts.SourceFile) || !File.Exists(_audioOpts.SourceFile))
                    {
                        Log.LogWarning("Could not start audio music source as the source file does not exist.");
                    }
                    else
                    {
                        _audioStreamReader = new StreamReader(_audioOpts.SourceFile);
                        _audioStreamTimer = new Timer(SendMusicSample, null, 0, AUDIO_SAMPLE_PERIOD_MILLISECONDS);
                    }
                }
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Sends audio samples read from a file.
        /// </summary>
        private void SendMusicSample(object state)
        {
            lock (_audioStreamTimer)
            {
                int sampleSize = (_sampleRate / 1000) * AUDIO_SAMPLE_PERIOD_MILLISECONDS;
                byte[] sample = new byte[sampleSize];

                int bytesRead = _audioStreamReader.BaseStream.Read(sample, 0, sample.Length);

                if (bytesRead > 0)
                {
                    //SendAudioFrame((uint)sampleSize, (int)_audioCodec, sample.Take(bytesRead).ToArray());
                    SendAudioFrame((uint)sampleSize, (int)_audioCodec, sample);
                }

                if (bytesRead == 0 || _audioStreamReader.EndOfStream)
                {
                    _audioStreamReader.BaseStream.Position = 0;
                }
            }

            //if (!m_isClosed)
            //{
            //    _audioStreamTimer.Change(AUDIO_SAMPLE_PERIOD_MILLISECONDS, Timeout.Infinite);
            //}
        }

        /// <summary>
        /// Sends the sounds of silence.
        /// </summary>
        private void SendSilenceSample(object state)
        {
            uint bufferSize = (uint)AUDIO_SAMPLE_PERIOD_MILLISECONDS;

            byte[] sample = new byte[bufferSize / 2];
            int sampleIndex = 0;

            for (int index = 0; index < bufferSize; index += 2)
            {
                if (_audioCodec == SDPMediaFormatsEnum.PCMA)
                {
                    sample[sampleIndex] = PCMA_SILENCE_BYTE_ZERO;
                    sample[sampleIndex + 1] = PCMA_SILENCE_BYTE_ONE;
                }
                else
                {
                    sample[sampleIndex] = PCMU_SILENCE_BYTE_ZERO;
                    sample[sampleIndex + 1] = PCMU_SILENCE_BYTE_ONE;
                }
            }

            SendAudioFrame(bufferSize, (int)_audioCodec, sample);

            //if (!m_isClosed)
            //{
            //    _audioStreamTimer.Change(AUDIO_SAMPLE_PERIOD_MILLISECONDS, Timeout.Infinite);
            //}
        }

        /// <summary>
        /// Sends a pseudo-random sample to replicate white noise.
        /// </summary>
        private void SendNoiseSample(object state)
        {
            int bufferSize = _sampleRate / AUDIO_SAMPLE_PERIOD_MILLISECONDS;
            float[] linear = new float[bufferSize];
            _signalGenerator.Read(linear, 0, bufferSize);

            byte[] encodedSample = new byte[bufferSize];

            short[] pcm = linear.Select(x => (short)(x * 32767f)).ToArray();

            if (_audioCodec == SDPMediaFormatsEnum.G722)
            {
                _g722Codec.Encode(_g722CodecState, encodedSample, pcm, bufferSize);
            }
            else
            {
                for (int index = 0; index < bufferSize; index++)
                {
                    if (_audioCodec == SDPMediaFormatsEnum.PCMU)
                    {
                        encodedSample[index] = MuLawEncoder.LinearToMuLawSample(pcm[index]);
                    }
                    //if (_audioCodec == SDPMediaFormatsEnum.PCMA)
                    //{
                    //    sample[sampleIndex] = (byte)Crypto.GetRandomInt(0xd5, 0xff);
                    //    sample[sampleIndex + 1] = (byte)Crypto.GetRandomInt(0x00, 0x55);
                    //}
                    //else
                    //{
                    //    sample[sampleIndex] = (byte)Crypto.GetRandomInt(0x80, 0xff);
                    //    sample[sampleIndex + 1] = (byte)Crypto.GetRandomInt(0x00, 0x7f);
                    //}
                }
            }

            SendAudioFrame((uint)bufferSize, (int)_audioCodec, encodedSample);

            //if (!m_isClosed)
            //{
            //    _audioStreamTimer.Change(AUDIO_SAMPLE_PERIOD_MILLISECONDS, Timeout.Infinite);
            //}
        }
    }

    /// <summary>
    /// mu-law encoder
    /// based on code from:
    /// http://hazelware.luggle.com/tutorials/mulawcompression.html
    /// </summary>
    public static class MuLawEncoder
    {
        private const int cBias = 0x84;
        private const int cClip = 32635;

        private static readonly byte[] MuLawCompressTable = new byte[256]
        {
             0,0,1,1,2,2,2,2,3,3,3,3,3,3,3,3,
             4,4,4,4,4,4,4,4,4,4,4,4,4,4,4,4,
             5,5,5,5,5,5,5,5,5,5,5,5,5,5,5,5,
             5,5,5,5,5,5,5,5,5,5,5,5,5,5,5,5,
             6,6,6,6,6,6,6,6,6,6,6,6,6,6,6,6,
             6,6,6,6,6,6,6,6,6,6,6,6,6,6,6,6,
             6,6,6,6,6,6,6,6,6,6,6,6,6,6,6,6,
             6,6,6,6,6,6,6,6,6,6,6,6,6,6,6,6,
             7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,
             7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,
             7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,
             7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,
             7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,
             7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,
             7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,
             7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7
        };

        /// <summary>
        /// Encodes a single 16 bit sample to mu-law
        /// </summary>
        /// <param name="sample">16 bit PCM sample</param>
        /// <returns>mu-law encoded byte</returns>
        public static byte LinearToMuLawSample(short sample)
        {
            int sign = (sample >> 8) & 0x80;
            if (sign != 0)
            {
                sample = (short)-sample;
            }
            if (sample > cClip)
            {
                sample = cClip;
            }
            sample = (short)(sample + cBias);
            int exponent = (int)MuLawCompressTable[(sample >> 7) & 0xFF];
            int mantissa = (sample >> (exponent + 3)) & 0x0F;
            int compressedByte = ~(sign | (exponent << 4) | mantissa);

            return (byte)compressedByte;
        }
    }

    /// <summary>
    /// Signal Generator
    /// Sin, Square, Triangle, SawTooth, White Noise, Pink Noise, Sweep.
    /// </summary>
    /// <remarks>
    /// Posibility to change ISampleProvider
    /// Example :
    /// ---------
    /// WaveOut _waveOutGene = new WaveOut();
    /// WaveGenerator wg = new SignalGenerator();
    /// wg.Type = ...
    /// wg.Frequency = ...
    /// wg ...
    /// _waveOutGene.Init(wg);
    /// _waveOutGene.Play();
    /// </remarks>
    public class SignalGenerator //: ISampleProvider
    {
        // Wave format
        //private readonly WaveFormat waveFormat;

        // Random Number for the White Noise & Pink Noise Generator
        private readonly Random random = new Random();

        private readonly double[] pinkNoiseBuffer = new double[7];

        // Const Math
        private const double TwoPi = 2 * Math.PI;

        // Generator variable
        private int nSample;

        // Sweep Generator variable
        private double phi;

        /// <summary>
        /// Initializes a new instance for the Generator (Default :: 44.1Khz, 2 channels, Sinus, Frequency = 440, Gain = 1)
        /// </summary>
        public SignalGenerator()
            : this(44100, 2)
        {

        }

        /// <summary>
        /// Initializes a new instance for the Generator (UserDef SampleRate &amp; Channels)
        /// </summary>
        /// <param name="sampleRate">Desired sample rate</param>
        /// <param name="channel">Number of channels</param>
        public SignalGenerator(int sampleRate, int channels)
        {
            phi = 0;
            //waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channel);
            SampleRate = sampleRate;
            Channels = channels;

            // Default
            Type = SignalGeneratorType.Sin;
            Frequency = 440.0;
            Gain = 1;
            PhaseReverse = new bool[channels];
            SweepLengthSecs = 2;
        }

        public int SampleRate { get; set; }

        public int Channels { get; set; }

        /// <summary>
        /// Frequency for the Generator. (20.0 - 20000.0 Hz)
        /// Sin, Square, Triangle, SawTooth, Sweep (Start Frequency).
        /// </summary>
        public double Frequency { get; set; }

        /// <summary>
        /// Return Log of Frequency Start (Read only)
        /// </summary>
        public double FrequencyLog => Math.Log(Frequency);

        /// <summary>
        /// End Frequency for the Sweep Generator. (Start Frequency in Frequency)
        /// </summary>
        public double FrequencyEnd { get; set; }

        /// <summary>
        /// Return Log of Frequency End (Read only)
        /// </summary>
        public double FrequencyEndLog => Math.Log(FrequencyEnd);

        /// <summary>
        /// Gain for the Generator. (0.0 to 1.0)
        /// </summary>
        public double Gain { get; set; }

        /// <summary>
        /// Channel PhaseReverse
        /// </summary>
        public bool[] PhaseReverse { get; }

        /// <summary>
        /// Type of Generator.
        /// </summary>
        public SignalGeneratorType Type { get; set; }

        /// <summary>
        /// Length Seconds for the Sweep Generator.
        /// </summary>
        public double SweepLengthSecs { get; set; }

        /// <summary>
        /// Reads from this provider.
        /// </summary>
        public int Read(float[] buffer, int offset, int count)
        {
            int outIndex = offset;

            // Generator current value
            double multiple;
            double sampleValue;
            double sampleSaw;

            // Complete Buffer
            for (int sampleCount = 0; sampleCount < count / Channels; sampleCount++)
            {
                switch (Type)
                {
                    case SignalGeneratorType.Sin:

                        // Sinus Generator

                        multiple = TwoPi * Frequency / SampleRate;
                        sampleValue = Gain * Math.Sin(nSample * multiple);

                        nSample++;

                        break;


                    case SignalGeneratorType.Square:

                        // Square Generator

                        multiple = 2 * Frequency / SampleRate;
                        sampleSaw = ((nSample * multiple) % 2) - 1;
                        sampleValue = sampleSaw > 0 ? Gain : -Gain;

                        nSample++;
                        break;

                    case SignalGeneratorType.Triangle:

                        // Triangle Generator

                        multiple = 2 * Frequency / SampleRate;
                        sampleSaw = ((nSample * multiple) % 2);
                        sampleValue = 2 * sampleSaw;
                        if (sampleValue > 1)
                        {
                            sampleValue = 2 - sampleValue;
                        }
                        if (sampleValue < -1)
                        {
                            sampleValue = -2 - sampleValue;
                        }

                        sampleValue *= Gain;

                        nSample++;
                        break;

                    case SignalGeneratorType.SawTooth:

                        // SawTooth Generator

                        multiple = 2 * Frequency / SampleRate;
                        sampleSaw = ((nSample * multiple) % 2) - 1;
                        sampleValue = Gain * sampleSaw;

                        nSample++;
                        break;

                    case SignalGeneratorType.White:

                        // White Noise Generator
                        sampleValue = (Gain * NextRandomTwo());
                        break;

                    case SignalGeneratorType.Pink:

                        // Pink Noise Generator

                        double white = NextRandomTwo();
                        pinkNoiseBuffer[0] = 0.99886 * pinkNoiseBuffer[0] + white * 0.0555179;
                        pinkNoiseBuffer[1] = 0.99332 * pinkNoiseBuffer[1] + white * 0.0750759;
                        pinkNoiseBuffer[2] = 0.96900 * pinkNoiseBuffer[2] + white * 0.1538520;
                        pinkNoiseBuffer[3] = 0.86650 * pinkNoiseBuffer[3] + white * 0.3104856;
                        pinkNoiseBuffer[4] = 0.55000 * pinkNoiseBuffer[4] + white * 0.5329522;
                        pinkNoiseBuffer[5] = -0.7616 * pinkNoiseBuffer[5] - white * 0.0168980;
                        double pink = pinkNoiseBuffer[0] + pinkNoiseBuffer[1] + pinkNoiseBuffer[2] + pinkNoiseBuffer[3] + pinkNoiseBuffer[4] + pinkNoiseBuffer[5] + pinkNoiseBuffer[6] + white * 0.5362;
                        pinkNoiseBuffer[6] = white * 0.115926;
                        sampleValue = (Gain * (pink / 5));
                        break;

                    case SignalGeneratorType.Sweep:

                        // Sweep Generator
                        double f = Math.Exp(FrequencyLog + (nSample * (FrequencyEndLog - FrequencyLog)) / (SweepLengthSecs * SampleRate));

                        multiple = TwoPi * f / SampleRate;
                        phi += multiple;
                        sampleValue = Gain * (Math.Sin(phi));
                        nSample++;
                        if (nSample > SweepLengthSecs * SampleRate)
                        {
                            nSample = 0;
                            phi = 0;
                        }
                        break;

                    default:
                        sampleValue = 0.0;
                        break;
                }

                // Phase Reverse Per Channel
                for (int i = 0; i < Channels; i++)
                {
                    if (PhaseReverse[i])
                    {
                        buffer[outIndex++] = (float)-sampleValue;
                    }
                    else
                    {
                        buffer[outIndex++] = (float)sampleValue;
                    }
                }
            }
            return count;
        }

        /// <summary>
        /// Private :: Random for WhiteNoise &amp; Pink Noise (Value form -1 to 1)
        /// </summary>
        /// <returns>Random value from -1 to +1</returns>
        private double NextRandomTwo()
        {
            return 2 * random.NextDouble() - 1;
        }

    }

    /// <summary>
    /// Signal Generator type
    /// </summary>
    public enum SignalGeneratorType
    {
        /// <summary>
        /// Pink noise
        /// </summary>
        Pink,
        /// <summary>
        /// White noise
        /// </summary>
        White,
        /// <summary>
        /// Sweep
        /// </summary>
        Sweep,
        /// <summary>
        /// Sine wave
        /// </summary>
        Sin,
        /// <summary>
        /// Square wave
        /// </summary>
        Square,
        /// <summary>
        /// Triangle Wave
        /// </summary>
        Triangle,
        /// <summary>
        /// Sawtooth wave
        /// </summary>
        SawTooth,
    }

}
