//-----------------------------------------------------------------------------
// Filename: RtpAudioSession.cs
//
// Description: A lightweight audio only RTP session suitable for testing.
// No rendering or capturing capabilities.
//
// Note: The signal generation and audio codec classes have been copied
// verbatim from the NAudio project, see https://github.com/naudio/NAudio.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 19 Mar 2020	Aaron Clauson	Created, Dublin, Ireland.
// 21 Apr 2020  Aaron Clauson   Added alaw and mulaw decode classes.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorcery.SIP.App;

namespace SIPSorcery.Media
{
    public enum DummyAudioSourcesEnum
    {
        Music = 0,
        Silence = 1,
        WhiteNoise = 2,
        SineWave = 3,
        PinkNoise = 4,
        None = 5
    }

    public class DummyAudioOptions
    {
        /// <summary>
        /// The type of audio source to use.
        /// </summary>
        public DummyAudioSourcesEnum AudioSource;

        /// <summary>
        /// If using a pre-recorded audio source this is the audio source file.
        /// </summary>
        public Dictionary<SDPMediaFormatsEnum, string> SourceFiles;
    }

    /// <summary>
    /// An audio only RTP session that can supply an audio stream to the caller. Any incoming audio stream is 
    /// ignored and this class does NOT use any audio devices on the system for capture or playback.
    /// </summary>
    public class RtpAudioSession : RTPSession, IMediaSession
    {
        private const int SAMPLE_RATE = 8000;                 // G711 and G722 use an 8KHz for RTP timestamps clock.
        private const int AUDIO_SAMPLE_PERIOD_MILLISECONDS = 20;
        private static readonly byte PCMU_SILENCE_BYTE_ZERO = 0x7F;
        private static readonly byte PCMU_SILENCE_BYTE_ONE = 0xFF;
        private static readonly byte PCMA_SILENCE_BYTE_ZERO = 0x55;
        private static readonly byte PCMA_SILENCE_BYTE_ONE = 0xD5;

        private static ILogger Log = SIPSorcery.Sys.Log.Logger;

        private StreamReader _audioStreamReader;
        private SignalGenerator _signalGenerator;
        private Timer _audioStreamTimer;
        private DummyAudioOptions _audioOpts;
        private List<SDPMediaFormatsEnum> _audioCodecs; // The list of supported audio codecs.
        private SDPMediaFormat _sendingFormat; // The codec that we've selected to send with (must be supported by remote party).
        private bool _isStarted = false;

        private G722Codec _g722Codec;
        private G722CodecState _g722CodecState;

        public uint RtpPacketsSent
        {
            get { return base.AudioRtcpSession.PacketsSentCount; }
        }

        public uint RtpPacketsReceived
        {
            get { return base.AudioRtcpSession.PacketsReceivedCount; }
        }

        /// <summary>
        /// Creates an audio only RTP session that can supply an audio stream to the caller.
        /// </summary>
        /// <param name="audioOptions">The options that determine the type of audio to stream to the remote party. Example
        /// type of audio sources are music, silence, white noise etc.</param>
        /// <param name="audioCodecs">The audio codecs to support.</param>
        /// <param name="bindAddress">Optional. If specified this address will be used as the bind address for any RTP
        /// and control sockets created. Generally this address does not need to be set. The default behaviour
        /// is to bind to [::] or 0.0.0.0,d depending on system support, which minimises network routing
        /// causing connection issues.</param>
        public RtpAudioSession(DummyAudioOptions audioOptions, List<SDPMediaFormatsEnum> audioCodecs, IPAddress bindAddress = null) :
            base(false, false, false, bindAddress)
        {
            if (audioCodecs == null || audioCodecs.Count() == 0)
            {
                _audioCodecs = new List<SDPMediaFormatsEnum> { SDPMediaFormatsEnum.PCMA, SDPMediaFormatsEnum.PCMU, SDPMediaFormatsEnum.G722 };
            }
            else if (audioCodecs.Any(x => !(x == SDPMediaFormatsEnum.PCMU || x == SDPMediaFormatsEnum.PCMA || x == SDPMediaFormatsEnum.G722)))
            {
                throw new ApplicationException("Only PCMA, PCMU and G722 audio codecs are supported.");
            }

            _audioOpts = audioOptions;
            _audioCodecs = audioCodecs;

            // RTP event support.
            SDPMediaFormat rtpEventFormat = new SDPMediaFormat(DTMF_EVENT_PAYLOAD_ID);
            rtpEventFormat.SetFormatAttribute($"{SDP.TELEPHONE_EVENT_ATTRIBUTE}/{SAMPLE_RATE}");
            rtpEventFormat.SetFormatParameterAttribute("0-16");

            var audioCapabilities = new List<SDPMediaFormat> { rtpEventFormat };
            foreach (var codec in _audioCodecs)
            {
                audioCapabilities.Add(new SDPMediaFormat(codec));
            }

            MediaStreamTrack audioTrack = new MediaStreamTrack(SDPMediaTypesEnum.audio, false, audioCapabilities);
            base.addTrack(audioTrack);
        }

        public override void Close(string reason)
        {
            base.Close(reason);

            _audioStreamTimer?.Dispose();
            _audioStreamReader?.Close();
        }

        /// <summary>
        /// Initialises the audio source as required.
        /// </summary>
        public override Task Start()
        {
            lock (this)
            {
                if (!_isStarted)
                {
                    _isStarted = true;

                    if (AudioLocalTrack == null || AudioLocalTrack.Capabilities == null || AudioLocalTrack.Capabilities.Count == 0)
                    {
                        throw new ApplicationException("Cannot start audio session without a local audio track being available.");
                    }
                    else if (AudioRemoteTrack == null || AudioRemoteTrack.Capabilities == null || AudioRemoteTrack.Capabilities.Count == 0)
                    {
                        throw new ApplicationException("Cannot start audio session without a remote audio track being available.");
                    }

                    // Choose which codec to use.
                    //_sendingFormat = AudioLocalTrack.Capabilities
                    //    .Where(x => x.FormatID != DTMF_EVENT_PAYLOAD_ID.ToString() && int.TryParse(x.FormatID, out _))
                    //    .OrderBy(x => int.Parse(x.FormatID)).First();
                    _sendingFormat = base.GetSendingFormat(SDPMediaTypesEnum.audio);

                    Log.LogDebug($"RTP audio session selected sending codec {_sendingFormat.FormatCodec}.");

                    if (_sendingFormat.FormatCodec == SDPMediaFormatsEnum.G722)
                    {
                        _g722Codec = new G722Codec();
                        _g722CodecState = new G722CodecState(64000, G722Flags.None);
                    }

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
                            _signalGenerator = new SignalGenerator(SAMPLE_RATE, 1);

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
                            if (_audioOpts.SourceFiles == null || !_audioOpts.SourceFiles.ContainsKey(_sendingFormat.FormatCodec))
                            {
                                Log.LogWarning($"Source file not set for codec {_sendingFormat.FormatCodec}.");
                            }
                            else
                            {
                                string sourceFile = _audioOpts.SourceFiles[_sendingFormat.FormatCodec];

                                if (String.IsNullOrEmpty(sourceFile) || !File.Exists(sourceFile))
                                {
                                    Log.LogWarning("Could not start audio music source as the source file does not exist.");
                                }
                                else
                                {
                                    _audioStreamReader = new StreamReader(sourceFile);
                                    _audioStreamTimer = new Timer(SendMusicSample, null, 0, AUDIO_SAMPLE_PERIOD_MILLISECONDS);
                                }
                            }
                        }
                    }
                }

                return base.Start();
            }
        }

        /// <summary>
        /// Sends audio samples read from a file.
        /// </summary>
        private void SendMusicSample(object state)
        {
            lock (_audioStreamTimer)
            {
                int sampleSize = SAMPLE_RATE / 1000 * AUDIO_SAMPLE_PERIOD_MILLISECONDS;
                byte[] sample = new byte[sampleSize];

                int bytesRead = _audioStreamReader.BaseStream.Read(sample, 0, sample.Length);

                if (bytesRead > 0)
                {
                    SendAudioFrame((uint)sampleSize, (int)_sendingFormat.FormatCodec, sample);
                }

                if (bytesRead == 0 || _audioStreamReader.EndOfStream)
                {
                    _audioStreamReader.BaseStream.Position = 0;
                }
            }
        }

        /// <summary>
        /// Sends the sounds of silence.
        /// </summary>
        private void SendSilenceSample(object state)
        {
            lock (_audioStreamTimer)
            {
                uint bufferSize = SAMPLE_RATE / 1000 * AUDIO_SAMPLE_PERIOD_MILLISECONDS;

                byte[] sample = new byte[bufferSize];

                for (int index = 0; index < bufferSize; index += 2)
                {
                    if (_sendingFormat.FormatCodec == SDPMediaFormatsEnum.PCMA)
                    {
                        sample[index] = PCMA_SILENCE_BYTE_ZERO;
                        sample[index + 1] = PCMA_SILENCE_BYTE_ONE;
                    }
                    else if (_sendingFormat.FormatCodec == SDPMediaFormatsEnum.PCMU)
                    {
                        sample[index] = PCMU_SILENCE_BYTE_ZERO;
                        sample[index + 1] = PCMU_SILENCE_BYTE_ONE;
                    }
                    else
                    {
                        throw new NotImplementedException($"Silence encoding not implemented for codec {_sendingFormat.FormatCodec}.");
                    }
                }

                SendAudioFrame(bufferSize, (int)_sendingFormat.FormatCodec, sample);
            }
        }

        /// <summary>
        /// Sends a pseudo-random sample to replicate white noise.
        /// </summary>
        private void SendNoiseSample(object state)
        {
            lock (_audioStreamTimer)
            {
                int bufferSize = SAMPLE_RATE / 1000 * AUDIO_SAMPLE_PERIOD_MILLISECONDS;
                float[] linear = new float[bufferSize];
                _signalGenerator.Read(linear, 0, bufferSize);

                byte[] encodedSample = new byte[bufferSize];

                short[] pcm = linear.Select(x => (short)(x * 32767f)).ToArray();

                if (_sendingFormat.FormatCodec == SDPMediaFormatsEnum.G722)
                {
                    _g722Codec.Encode(_g722CodecState, encodedSample, pcm, bufferSize);
                }
                else
                {
                    for (int index = 0; index < bufferSize; index++)
                    {
                        if (_sendingFormat.FormatCodec == SDPMediaFormatsEnum.PCMA)
                        {
                            encodedSample[index] = ALawEncoder.LinearToALawSample(pcm[index]);
                        }
                        else
                        {
                            encodedSample[index] = MuLawEncoder.LinearToMuLawSample(pcm[index]);
                        }
                    }
                }

                SendAudioFrame((uint)bufferSize, (int)_sendingFormat.FormatCodec, encodedSample);
            }
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
    /// A-law encoder
    /// </summary>
    public static class ALawEncoder
    {
        private const int cBias = 0x84;
        private const int cClip = 32635;
        private static readonly byte[] ALawCompressTable = new byte[128]
        {
             1,1,2,2,3,3,3,3,
             4,4,4,4,4,4,4,4,
             5,5,5,5,5,5,5,5,
             5,5,5,5,5,5,5,5,
             6,6,6,6,6,6,6,6,
             6,6,6,6,6,6,6,6,
             6,6,6,6,6,6,6,6,
             6,6,6,6,6,6,6,6,
             7,7,7,7,7,7,7,7,
             7,7,7,7,7,7,7,7,
             7,7,7,7,7,7,7,7,
             7,7,7,7,7,7,7,7,
             7,7,7,7,7,7,7,7,
             7,7,7,7,7,7,7,7,
             7,7,7,7,7,7,7,7,
             7,7,7,7,7,7,7,7
        };

        /// <summary>
        /// Encodes a single 16 bit sample to a-law
        /// </summary>
        /// <param name="sample">16 bit PCM sample</param>
        /// <returns>a-law encoded byte</returns>
        public static byte LinearToALawSample(short sample)
        {
            int sign;
            int exponent;
            int mantissa;
            byte compressedByte;

            sign = ((~sample) >> 8) & 0x80;
            if (sign == 0)
            {
                sample = (short)-sample;
            }
            if (sample > cClip)
            {
                sample = cClip;
            }
            if (sample >= 256)
            {
                exponent = (int)ALawCompressTable[(sample >> 8) & 0x7F];
                mantissa = (sample >> (exponent + 3)) & 0x0F;
                compressedByte = (byte)((exponent << 4) | mantissa);
            }
            else
            {
                compressedByte = (byte)(sample >> 4);
            }
            compressedByte ^= (byte)(sign ^ 0x55);
            return compressedByte;
        }
    }

    /// <summary>
    /// a-law decoder
    /// based on code from:
    /// http://hazelware.luggle.com/tutorials/mulawcompression.html
    /// </summary>
    public class ALawDecoder
    {
        /// <summary>
        /// only 512 bytes required, so just use a lookup
        /// </summary>
        private static readonly short[] ALawDecompressTable = new short[256]
        {
             -5504, -5248, -6016, -5760, -4480, -4224, -4992, -4736,
             -7552, -7296, -8064, -7808, -6528, -6272, -7040, -6784,
             -2752, -2624, -3008, -2880, -2240, -2112, -2496, -2368,
             -3776, -3648, -4032, -3904, -3264, -3136, -3520, -3392,
             -22016,-20992,-24064,-23040,-17920,-16896,-19968,-18944,
             -30208,-29184,-32256,-31232,-26112,-25088,-28160,-27136,
             -11008,-10496,-12032,-11520,-8960, -8448, -9984, -9472,
             -15104,-14592,-16128,-15616,-13056,-12544,-14080,-13568,
             -344,  -328,  -376,  -360,  -280,  -264,  -312,  -296,
             -472,  -456,  -504,  -488,  -408,  -392,  -440,  -424,
             -88,   -72,   -120,  -104,  -24,   -8,    -56,   -40,
             -216,  -200,  -248,  -232,  -152,  -136,  -184,  -168,
             -1376, -1312, -1504, -1440, -1120, -1056, -1248, -1184,
             -1888, -1824, -2016, -1952, -1632, -1568, -1760, -1696,
             -688,  -656,  -752,  -720,  -560,  -528,  -624,  -592,
             -944,  -912,  -1008, -976,  -816,  -784,  -880,  -848,
              5504,  5248,  6016,  5760,  4480,  4224,  4992,  4736,
              7552,  7296,  8064,  7808,  6528,  6272,  7040,  6784,
              2752,  2624,  3008,  2880,  2240,  2112,  2496,  2368,
              3776,  3648,  4032,  3904,  3264,  3136,  3520,  3392,
              22016, 20992, 24064, 23040, 17920, 16896, 19968, 18944,
              30208, 29184, 32256, 31232, 26112, 25088, 28160, 27136,
              11008, 10496, 12032, 11520, 8960,  8448,  9984,  9472,
              15104, 14592, 16128, 15616, 13056, 12544, 14080, 13568,
              344,   328,   376,   360,   280,   264,   312,   296,
              472,   456,   504,   488,   408,   392,   440,   424,
              88,    72,   120,   104,    24,     8,    56,    40,
              216,   200,   248,   232,   152,   136,   184,   168,
              1376,  1312,  1504,  1440,  1120,  1056,  1248,  1184,
              1888,  1824,  2016,  1952,  1632,  1568,  1760,  1696,
              688,   656,   752,   720,   560,   528,   624,   592,
              944,   912,  1008,   976,   816,   784,   880,   848
        };

        /// <summary>
        /// Converts an a-law encoded byte to a 16 bit linear sample
        /// </summary>
        /// <param name="aLaw">a-law encoded byte</param>
        /// <returns>Linear sample</returns>
        public static short ALawToLinearSample(byte aLaw)
        {
            return ALawDecompressTable[aLaw];
        }
    }

    /// <summary>
    /// mu-law decoder
    /// based on code from:
    /// http://hazelware.luggle.com/tutorials/mulawcompression.html
    /// </summary>
    public static class MuLawDecoder
    {
        /// <summary>
        /// only 512 bytes required, so just use a lookup
        /// </summary>
        private static readonly short[] MuLawDecompressTable = new short[256]
        {
             -32124,-31100,-30076,-29052,-28028,-27004,-25980,-24956,
             -23932,-22908,-21884,-20860,-19836,-18812,-17788,-16764,
             -15996,-15484,-14972,-14460,-13948,-13436,-12924,-12412,
             -11900,-11388,-10876,-10364, -9852, -9340, -8828, -8316,
              -7932, -7676, -7420, -7164, -6908, -6652, -6396, -6140,
              -5884, -5628, -5372, -5116, -4860, -4604, -4348, -4092,
              -3900, -3772, -3644, -3516, -3388, -3260, -3132, -3004,
              -2876, -2748, -2620, -2492, -2364, -2236, -2108, -1980,
              -1884, -1820, -1756, -1692, -1628, -1564, -1500, -1436,
              -1372, -1308, -1244, -1180, -1116, -1052,  -988,  -924,
               -876,  -844,  -812,  -780,  -748,  -716,  -684,  -652,
               -620,  -588,  -556,  -524,  -492,  -460,  -428,  -396,
               -372,  -356,  -340,  -324,  -308,  -292,  -276,  -260,
               -244,  -228,  -212,  -196,  -180,  -164,  -148,  -132,
               -120,  -112,  -104,   -96,   -88,   -80,   -72,   -64,
                -56,   -48,   -40,   -32,   -24,   -16,    -8,     -1,
              32124, 31100, 30076, 29052, 28028, 27004, 25980, 24956,
              23932, 22908, 21884, 20860, 19836, 18812, 17788, 16764,
              15996, 15484, 14972, 14460, 13948, 13436, 12924, 12412,
              11900, 11388, 10876, 10364,  9852,  9340,  8828,  8316,
               7932,  7676,  7420,  7164,  6908,  6652,  6396,  6140,
               5884,  5628,  5372,  5116,  4860,  4604,  4348,  4092,
               3900,  3772,  3644,  3516,  3388,  3260,  3132,  3004,
               2876,  2748,  2620,  2492,  2364,  2236,  2108,  1980,
               1884,  1820,  1756,  1692,  1628,  1564,  1500,  1436,
               1372,  1308,  1244,  1180,  1116,  1052,   988,   924,
                876,   844,   812,   780,   748,   716,   684,   652,
                620,   588,   556,   524,   492,   460,   428,   396,
                372,   356,   340,   324,   308,   292,   276,   260,
                244,   228,   212,   196,   180,   164,   148,   132,
                120,   112,   104,    96,    88,    80,    72,    64,
                 56,    48,    40,    32,    24,    16,     8,     0
        };

        /// <summary>
        /// Converts a mu-law encoded byte to a 16 bit linear sample
        /// </summary>
        /// <param name="muLaw">mu-law encoded byte</param>
        /// <returns>Linear sample</returns>
        public static short MuLawToLinearSample(byte muLaw)
        {
            return MuLawDecompressTable[muLaw];
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
