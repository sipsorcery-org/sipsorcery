using System;
using System.Collections.Generic;
using System.Net;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions.V1;

namespace SIPSorcery.Media
{
    public class RtpAudioVideoSession : RTPSession, IMediaSession
    {
        private static ILogger Log = SIPSorcery.Sys.Log.Logger;

        public IPlatformMediaSession PlatformMediaSession { get; private set; }

        private AudioEncoder _audioEncoder;

        public RtpAudioVideoSession(IPlatformMediaSession platformMediaSession) : base(false, false, false)
        {
            PlatformMediaSession = platformMediaSession;
            _audioEncoder = new AudioEncoder();
            base.OnRtpPacketReceived += RtpMediaPacketReceived;
            PlatformMediaSession.OnRawAudioSampleReady += RawAudioSampleReady;

            List<SDPMediaFormat> audioTrackFormats = new List<SDPMediaFormat>();
            foreach (var audioFormat in PlatformMediaSession.GetAudioFormats())
            {
                SDPMediaFormatsEnum sdpAudioFormat = SDPMediaFormatsEnum.Unknown;
                switch (audioFormat.Codec)
                {
                    case SIPSorceryMedia.Abstractions.V1.AudioCodecsEnum.PCMU:
                        sdpAudioFormat = SDPMediaFormatsEnum.PCMU;
                        break;
                    case SIPSorceryMedia.Abstractions.V1.AudioCodecsEnum.PCMA:
                        sdpAudioFormat = SDPMediaFormatsEnum.PCMA;
                        break;
                    case SIPSorceryMedia.Abstractions.V1.AudioCodecsEnum.G722:
                        sdpAudioFormat = SDPMediaFormatsEnum.G722;
                        break;
                    default:
                        Log.LogWarning("Audio format not recognised.");
                        break;
                }

                audioTrackFormats.Add(new SDPMediaFormat(sdpAudioFormat));
            }

            base.OnStarted += async () => await PlatformMediaSession.Start();
            base.OnClosed += async () => await PlatformMediaSession.Close();

            var audioTrack = new MediaStreamTrack(SDPMediaTypesEnum.audio, false, audioTrackFormats);
            base.addTrack(audioTrack);
        }

        /// <summary>
        /// Handler for a PCM audio sample getting generated from the local audio source.
        /// </summary>
        /// <param name="durationMilliseconds"></param>
        /// <param name="pcmSample"></param>
        protected void RawAudioSampleReady(int durationMilliseconds, byte[] pcmSample, AudioSamplingRatesEnum samplingRate)
        {
            var sendingFormat = base.GetSendingFormat(SDPMediaTypesEnum.audio);
            var encodedSample = _audioEncoder.EncodeAudio(pcmSample, sendingFormat, samplingRate);

            // int sampleRateTicks = (sampleRate == AudioSamplingRatesEnum.SampleRate8KHz) ? 8000 : 16000;
            // int durationMilliseconds = (sample.Length * 1000) / (sampleRateTicks * 2);
            int rtpTimestampDuration = 8000 / 1000 * durationMilliseconds;

            base.SendAudioFrame((uint)rtpTimestampDuration, (int)sendingFormat.FormatCodec, encodedSample);
        }

        /// <summary>
        /// Handler for a PCM audio sample getting generated from the local audio source.
        /// </summary>
        /// <param name="durationMilliseconds"></param>
        /// <param name="pcmSample"></param>
        protected void RawAudioSampleReady(int durationMilliseconds, short[] pcmSample, AudioSamplingRatesEnum samplingRate)
        {
            var sendingFormat = base.GetSendingFormat(SDPMediaTypesEnum.audio);
            var encodedSample = _audioEncoder.EncodeAudio(pcmSample, sendingFormat, samplingRate);

            // int sampleRateTicks = (sampleRate == AudioSamplingRatesEnum.SampleRate8KHz) ? 8000 : 16000;
            // int durationMilliseconds = (sample.Length * 1000) / (sampleRateTicks * 2);
            int rtpTimestampDuration = 8000 / 1000 * durationMilliseconds;

            base.SendAudioFrame((uint)rtpTimestampDuration, (int)sendingFormat.FormatCodec, encodedSample);
        }

        protected void RtpMediaPacketReceived(IPEndPoint remoteEndPoint, SDPMediaTypesEnum mediaType, RTPPacket rtpPacket)
        {
            if (mediaType == SDPMediaTypesEnum.audio)
            {
                var sendingFormat = base.GetSendingFormat(SDPMediaTypesEnum.audio);
                var decodedSample = _audioEncoder.DecodeAudio(rtpPacket.Payload, sendingFormat, AudioSamplingRatesEnum.Rate8KHz);

                PlatformMediaSession.ProcessRemoteAudioSample(decodedSample);
            }
        }
    }
}
