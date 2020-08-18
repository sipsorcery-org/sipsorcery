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

        public IAudioVideoSession AudioVideoSession { get; private set; }

        private AudioEncoder _audioEncoder;

        public RtpAudioVideoSession(IAudioVideoSession audioVideoSession) : base(false, false, false)
        {
            AudioVideoSession = audioVideoSession;
            _audioEncoder = new AudioEncoder();
            base.OnRtpPacketReceived += OnRtpMediaPacketReceived;
            AudioVideoSession.OnRawAudioSampleReady += OnRawAudioSampleReady;

            List<SDPMediaFormat> audioTrackFormats = new List<SDPMediaFormat>();
            foreach (var audioFormat in AudioVideoSession.GetAudioFormats())
            {
                SDPMediaFormatsEnum sdpAudioFormat = SDPMediaFormatsEnum.Unknown;
                switch (audioFormat.Codec)
                {
                    case SIPSorceryMedia.Abstractions.V1.AudioCodecsEnum.PCMU:
                        sdpAudioFormat = SDPMediaFormatsEnum.PCMU;
                        break;
                    default:
                        Log.LogWarning("Audio format not recognised.");
                        break;
                }

                audioTrackFormats.Add(new SDPMediaFormat(sdpAudioFormat));
            }

            base.OnStarted += async () => await AudioVideoSession.Start();
            base.OnClosed += async () => await AudioVideoSession.Close();

            var audioTrack = new MediaStreamTrack(SDPMediaTypesEnum.audio, false, audioTrackFormats);
            base.addTrack(audioTrack);
        }

        /// <summary>
        /// Handler for a PCM audio sample getting generated from the local audio source.
        /// </summary>
        /// <param name="durationMilliseconds"></param>
        /// <param name="pcmSample"></param>
        private void OnRawAudioSampleReady(int durationMilliseconds, byte[] pcmSample)
        {
            var sendingFormat = base.GetSendingFormat(SDPMediaTypesEnum.audio);
            var encodedSample = _audioEncoder.EncodeAudio(pcmSample, sendingFormat, AudioSamplingRatesEnum.SampleRate8KHz);

            // int sampleRateTicks = (sampleRate == AudioSamplingRatesEnum.SampleRate8KHz) ? 8000 : 16000;
            // int durationMilliseconds = (sample.Length * 1000) / (sampleRateTicks * 2);
            int rtpTimestampDuration = 8000 / 1000 * durationMilliseconds;

            base.SendAudioFrame((uint)rtpTimestampDuration, (int)sendingFormat.FormatCodec, encodedSample);
        }

        private void OnRtpMediaPacketReceived(IPEndPoint remoteEndPoint, SDPMediaTypesEnum mediaType, RTPPacket rtpPacket)
        {
            if (mediaType == SDPMediaTypesEnum.audio)
            {
                var sendingFormat = base.GetSendingFormat(SDPMediaTypesEnum.audio);
                var decodedSample = _audioEncoder.DecodeAudio(rtpPacket.Payload, sendingFormat, AudioSamplingRatesEnum.SampleRate8KHz);

                AudioVideoSession.ProcessRemoteAudioSample(decodedSample);
            }
        }
    }
}
