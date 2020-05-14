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
                        pcm = SIPSorcery.Media.ALawDecoder.ALawToLinearSample(sample[index]);
                    }
                    else
                    {
                        pcm = SIPSorcery.Media.MuLawDecoder.MuLawToLinearSample(sample[index]);
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
}
