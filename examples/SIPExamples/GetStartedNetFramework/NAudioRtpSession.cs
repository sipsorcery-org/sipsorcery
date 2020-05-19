//-----------------------------------------------------------------------------
// Filename: NAudioRtpSession.cs
//
// Description: Example of an RTP session that uses NAUdio for audio
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
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NAudio.Wave;
using SIPSorcery.Net;
using SIPSorcery.SIP.App;

namespace demo
{
    public class NAudioRtpSession : RTPSession, IMediaSession
    {
        private const int AUDIO_SAMPLE_PERIOD_MILLISECONDS = 30;
        private const int AUDIO_OUTPUTDEVICE_INDEX = -1;

        private static Microsoft.Extensions.Logging.ILogger Log = SIPSorcery.Sys.Log.Logger;

        private static readonly WaveFormat _waveFormat = new WaveFormat(8000, 16, 1);

        /// <summary>
        /// Audio render device.
        /// </summary>
        private WaveOutEvent _waveOutEvent;

        /// <summary>
        /// Buffer for audio samples to be rendered.
        /// </summary>
        private BufferedWaveProvider _waveProvider;

        /// <summary>
        /// Audio capture device.
        /// </summary>
        private WaveInEvent _waveInEvent;

        private uint _rtpAudioTimestampPeriod = 0;
        private SDPMediaFormat _sendingAudioFormat = null;
        private bool _isStarted = false;
        private bool _isClosed = false;

        /// <summary>
        /// Creates a new basic RTP session that captures and renders audio to/from the default system devices.
        /// </summary>
        public NAudioRtpSession()
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

                // Render device.
                _waveOutEvent = new WaveOutEvent();
                _waveOutEvent.DeviceNumber = AUDIO_OUTPUTDEVICE_INDEX;
                _waveProvider = new BufferedWaveProvider(_waveFormat);
                _waveProvider.DiscardOnBufferOverflow = true;
                _waveOutEvent.Init(_waveProvider);
                _waveOutEvent.Play();

                // Audio source.
                if (WaveInEvent.DeviceCount > 0)
                {
                    _waveInEvent = new WaveInEvent();
                    _waveInEvent.BufferMilliseconds = AUDIO_SAMPLE_PERIOD_MILLISECONDS;
                    _waveInEvent.NumberOfBuffers = 1;
                    _waveInEvent.DeviceNumber = 0;
                    _waveInEvent.WaveFormat = _waveFormat;
                    _waveInEvent.DataAvailable += LocalAudioSampleAvailable;

                    _waveInEvent.StartRecording();
                }
                else
                {
                    Log.LogWarning("No audio capture devices are available. No audio stream will be sent.");
                }

                if (_rtpAudioTimestampPeriod == 0)
                {
                    _rtpAudioTimestampPeriod = (uint)(SDPMediaFormatInfo.GetClockRate(SDPMediaFormatsEnum.PCMU) / AUDIO_SAMPLE_PERIOD_MILLISECONDS);
                }
            }
        }

        /// <summary>
        /// Closes the session.
        /// </summary>
        /// <param name="reason">Reason for the closure.</param>
        public override void Close(string reason)
        {
            if (!_isClosed)
            {
                _isClosed = true;

                base.OnRtpPacketReceived -= RtpPacketReceived;

                _waveOutEvent?.Stop();

                if (_waveInEvent != null)
                {
                    _waveInEvent.DataAvailable -= LocalAudioSampleAvailable;
                    _waveInEvent.StopRecording();
                }

                base.Close(reason);
            }
        }

        /// <summary>
        /// Event handler for audio sample being supplied by local capture device.
        /// </summary>
        private void LocalAudioSampleAvailable(object sender, WaveInEventArgs args)
        {
            byte[] sample = new byte[args.Buffer.Length / 2];
            int sampleIndex = 0;

            for (int index = 0; index < args.BytesRecorded; index += 2)
            {
                if (_sendingAudioFormat.FormatCodec == SDPMediaFormatsEnum.PCMU)
                {
                    var ulawByte = NAudio.Codecs.MuLawEncoder.LinearToMuLawSample(BitConverter.ToInt16(args.Buffer, index));
                    sample[sampleIndex++] = ulawByte;
                }
                else if (_sendingAudioFormat.FormatCodec == SDPMediaFormatsEnum.PCMA)
                {
                    var alawByte = NAudio.Codecs.ALawEncoder.LinearToALawSample(BitConverter.ToInt16(args.Buffer, index));
                    sample[sampleIndex++] = alawByte;
                }
            }

            base.SendAudioFrame((uint)sample.Length, Convert.ToInt32(_sendingAudioFormat.FormatID), sample);
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
                RenderAudio(rtpPacket);
            }
        }

        /// <summary>
        /// Render an audio RTP packet received from a remote party.
        /// </summary>
        /// <param name="rtpPacket">The RTP packet containing the audio payload.</param>
        private void RenderAudio(RTPPacket rtpPacket)
        {
            if (_waveProvider != null)
            {
                var sample = rtpPacket.Payload;

                for (int index = 0; index < sample.Length; index++)
                {
                    short pcm = 0;

                    if (rtpPacket.Header.PayloadType == (int)SDPMediaFormatsEnum.PCMA)
                    {
                        pcm = NAudio.Codecs.ALawDecoder.ALawToLinearSample(sample[index]);
                        byte[] pcmSample = new byte[] { (byte)(pcm & 0xFF), (byte)(pcm >> 8) };
                        _waveProvider.AddSamples(pcmSample, 0, 2);
                    }
                    else
                    {
                        pcm = NAudio.Codecs.MuLawDecoder.MuLawToLinearSample(sample[index]);
                        byte[] pcmSample = new byte[] { (byte)(pcm & 0xFF), (byte)(pcm >> 8) };
                        _waveProvider.AddSamples(pcmSample, 0, 2);
                    }
                }
            }
        }
    }
}
