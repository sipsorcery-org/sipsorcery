//-----------------------------------------------------------------------------
// Filename: PortAudioRtpSession.cs
//
// Description: Example of an RTP session that uses PortAUdio for audio
// capture and rendering. This class is a cut, paste and strip job from
// the RtpAvSession class in the SIPSorceryMedia library.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 17 Apr 2020 Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using PortAudioSharp;
using SIPSorcery.Net;
using SIPSorcery.SIP.App;

namespace demo
{
    public class PortAudioRtpSession : RTPSession, IMediaSession
    {
        private const int DTMF_EVENT_DURATION = 1200;        // Default duration for a DTMF event.
        private const int DTMF_EVENT_PAYLOAD_ID = 101;
        private const int AUDIO_SAMPLE_BUFFER_LENGTH = 160;   // At 8Khz buffer of 160 corresponds to 20ms samples.
        private const int AUDIO_SAMPLING_RATE = 8000;
        private const float NORMALISE_FACTOR = 32768f;

        private static Microsoft.Extensions.Logging.ILogger Log = SIPSorcery.Sys.Log.Logger;

        /// <summary>
        /// Combined audio capture and render stream.
        /// </summary>
        private PortAudioSharp.Stream _audioIOStream;

        private List<float> _pendingRemoteSamples = new List<float>();
        private uint _rtpAudioTimestampPeriod = 0;
        private SDPMediaFormat _sendingAudioFormat = null;
        private bool _isStarted = false;
        private bool _isClosed = false;

        /// <summary>
        /// Creates a new basic RTP session that captures and renders audio to/from the default system devices.
        /// </summary>
        public PortAudioRtpSession()
            : base(false, false, false)
        {
            var pcmu = new SDPMediaFormat(SDPMediaFormatsEnum.PCMU);
            var pcma = new SDPMediaFormat(SDPMediaFormatsEnum.PCMA);

            // RTP event support.
            int clockRate = pcmu.GetClockRate();
            SDPMediaFormat rtpEventFormat = new SDPMediaFormat(DTMF_EVENT_PAYLOAD_ID);
            rtpEventFormat.SetFormatAttribute($"{SDP.TELEPHONE_EVENT_ATTRIBUTE}/{clockRate}");
            rtpEventFormat.SetFormatParameterAttribute("0-16");

            var audioCapabilities = new List<SDPMediaFormat> { pcmu, pcma, rtpEventFormat };

            MediaStreamTrack audioTrack = new MediaStreamTrack(null, SDPMediaTypesEnum.audio, false, audioCapabilities);
            addTrack(audioTrack);

            // Where the magic (for processing received media) happens.
            base.OnRtpPacketReceived += RtpPacketReceived;
        }

        /// <summary>
        /// Starts the media capturing/source devices.
        /// </summary>
        public override async Task Start()
        {
            if (!_isStarted)
            {
                _sendingAudioFormat = base.GetSendingFormat(SDPMediaTypesEnum.audio);

                _isStarted = true;

                await base.Start();

                PortAudio.Initialize();

                var outputDevice = PortAudio.DefaultOutputDevice;
                if (outputDevice == PortAudio.NoDevice)
                {
                    throw new ApplicationException("No audio output device available.");
                }
                else
                {
                    StreamParameters stmInParams = new StreamParameters { device = 0, channelCount = 2, sampleFormat = SampleFormat.Float32 };
                    StreamParameters stmOutParams = new StreamParameters { device = outputDevice, channelCount = 2, sampleFormat = SampleFormat.Float32 };

                    // Combined audio capture and render.
                    _audioIOStream = new Stream(stmInParams, stmOutParams, AUDIO_SAMPLING_RATE, AUDIO_SAMPLE_BUFFER_LENGTH, StreamFlags.NoFlag, AudioSampleAvailable, null);
                    _audioIOStream.Start();
                }

                if (_rtpAudioTimestampPeriod == 0)
                {
                    _rtpAudioTimestampPeriod = (uint)(SDPMediaFormatInfo.GetClockRate(SDPMediaFormatsEnum.PCMU) / AUDIO_SAMPLE_BUFFER_LENGTH);
                }
            }
        }

        /// <summary>
        /// Sends a DTMF tone as an RTP event to the remote party.
        /// </summary>
        /// <param name="key">The DTMF tone to send.</param>
        /// <param name="ct">RTP events can span multiple RTP packets. This token can
        /// be used to cancel the send.</param>
        public Task SendDtmf(byte key, CancellationToken ct)
        {
            var dtmfEvent = new RTPEvent(key, false, RTPEvent.DEFAULT_VOLUME, DTMF_EVENT_DURATION, DTMF_EVENT_PAYLOAD_ID);
            return SendDtmfEvent(dtmfEvent, ct);
        }

        /// <summary>
        /// Closes the session.
        /// </summary>
        /// <param name="reason">Reason for the closure.</param>
        public void Close(string reason)
        {
            if (!_isClosed)
            {
                _isClosed = true;

                base.OnRtpPacketReceived -= RtpPacketReceived;

                _audioIOStream?.Stop();

                base.CloseSession(reason);
            }
        }

        /// <summary>
        /// Event handler for audio sample being supplied by local capture device. The callback will also
        /// playback any remote samples available.
        /// </summary>
        private StreamCallbackResult AudioSampleAvailable(IntPtr input, IntPtr output, uint frameCount, ref StreamCallbackTimeInfo timeInfo, StreamCallbackFlags statusFlags, IntPtr userDataPtr)
        {
            // Encode and transmit the sample from the audio input device.
            float[] inputSamples = new float[frameCount];
            Marshal.Copy(input, inputSamples, 0, (int)frameCount);

            byte[] outputSamples = new byte[frameCount];

            for (int index = 0; index < frameCount; index++)
            {
                if (_sendingAudioFormat.FormatCodec == SDPMediaFormatsEnum.PCMU)
                {
                    var ulawByte = SIPSorcery.Media.MuLawEncoder.LinearToMuLawSample((short)(inputSamples[index] * NORMALISE_FACTOR));
                    outputSamples[index] = ulawByte;
                }
                else if (_sendingAudioFormat.FormatCodec == SDPMediaFormatsEnum.PCMA)
                {
                    var alawByte = SIPSorcery.Media.ALawEncoder.LinearToALawSample((short)(inputSamples[index] * NORMALISE_FACTOR));
                    outputSamples[index] = alawByte;
                }
            }

            base.SendAudioFrame((uint)outputSamples.Length, Convert.ToInt32(_sendingAudioFormat.FormatID), outputSamples);

            // Check if there are any pending remote samples and if so push them to the audio output buffer.
            if (_pendingRemoteSamples.Count > 0)
            {
                lock (_pendingRemoteSamples)
                {
                    unsafe
                    {
                        float* audioOut = (float*)output;

                        for (int i = 0; i < _pendingRemoteSamples.Count; i++)
                        {
                            *audioOut++ = _pendingRemoteSamples[i];
                        }
                    }

                    _pendingRemoteSamples.Clear();
                }
            }

            return StreamCallbackResult.Continue;
        }

        /// <summary>
        /// Event handler for receiving RTP packets from a remote party.
        /// </summary>
        /// <param name="mediaType">The media type of the packets.</param>
        /// <param name="rtpPacket">The RTP packet with the media sample.</param>
        private void RtpPacketReceived(SDPMediaTypesEnum mediaType, RTPPacket rtpPacket)
        {
            //Log.LogDebug($"RTP packet received for {mediaType}.");

            if (mediaType == SDPMediaTypesEnum.audio)
            {
                var sample = rtpPacket.Payload;
                float[] outputSamples = new float[sample.Length];

                for (int index = 0; index < sample.Length; index++)
                {
                    short pcm = 0;

                    if (rtpPacket.Header.PayloadType == (int)SDPMediaFormatsEnum.PCMA)
                    {
                        pcm = ALawDecoder.ALawToLinearSample(sample[index]);
                    }
                    else
                    {
                        pcm = MuLawDecoder.MuLawToLinearSample(sample[index]);
                    }

                    outputSamples[index] = pcm * NORMALISE_FACTOR;
                }

                lock (_pendingRemoteSamples)
                {
                    _pendingRemoteSamples.AddRange(outputSamples);
                }
            }
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
}
