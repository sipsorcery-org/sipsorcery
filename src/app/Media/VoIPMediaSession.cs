using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions.V1;

namespace SIPSorcery.Media
{
    public class VoIPMediaSession : RTPSession, IMediaSession
    {
        /// <summary>
        /// The only supported encoding 
        /// </summary>
        public const int RTP_AUDIO_TIMESTAMP_RATE = 8000;         // G711 and G722 use an 8KHz for RTP timestamps clock.
        
        private static ILogger logger = SIPSorcery.Sys.Log.Logger;

        public MediaEndPoints Media { get; private set; }
        private AudioEncoder _audioEncoder;

        public VoIPMediaSession(
            MediaEndPoints mediaEndPoint,
            IPAddress bindAddress = null, 
            int bindPort = 0) 
            : base(false, false, false, bindAddress, bindPort)
        {
            Media = mediaEndPoint; 

            _audioEncoder = new AudioEncoder();

            // Wire up the audio and video sample event handlers.
            if (Media.AudioSource != null)
            {
                List<SDPMediaFormat> audioTrackFormats = new List<SDPMediaFormat>();
                foreach (var audioFormat in mediaEndPoint.AudioSource.GetAudioSourceFormats())
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
                            logger.LogWarning("Audio format not recognised.");
                            break;
                    }

                    audioTrackFormats.Add(new SDPMediaFormat(sdpAudioFormat));
                }

                var audioTrack = new MediaStreamTrack(SDPMediaTypesEnum.audio, false, audioTrackFormats);
                base.addTrack(audioTrack);

                // Example being a microphone.
                Media.AudioSource.OnAudioSourceRawSample += OnAudioSourceRawSample;
                Media.AudioSource.OnAudioSourceEncodedSample += OnAudioSourceEncodedSample;
            }

            if(Media.VideoSource != null)
            {
                List<SDPMediaFormat> videoTrackFormats = new List<SDPMediaFormat>();
                foreach (var videoFormat in mediaEndPoint.VideoSource.GetVideoSourceFormats())
                {
                    SDPMediaFormatsEnum sdpVideoFormat = SDPMediaFormatsEnum.Unknown;
                    switch (videoFormat.Codec)
                    {
                        case SIPSorceryMedia.Abstractions.V1.VideoCodecsEnum.VP8:
                            sdpVideoFormat = SDPMediaFormatsEnum.VP8;
                            break;
                        case SIPSorceryMedia.Abstractions.V1.VideoCodecsEnum.H264:
                            sdpVideoFormat = SDPMediaFormatsEnum.H264;
                            break;
                        default:
                            logger.LogWarning("Video format not recognised.");
                            break;
                    }

                    videoTrackFormats.Add(new SDPMediaFormat(sdpVideoFormat));
                }

                var videoTrack = new MediaStreamTrack(SDPMediaTypesEnum.video, false, videoTrackFormats);
                base.addTrack(videoTrack);

                // An example video source could be a webcam.
                Media.VideoSource.OnVideoSourceEncodedSample += OnVideoSourceEncodedSample;
            }
            
            if(Media.AudioSink != null || Media.VideoSink != null)
            {
                base.OnRtpPacketReceived += RtpMediaPacketReceived;
            }
        }

        public async override Task Start()
        {
            if (!base.IsStarted)
            {
                await base.Start();

                if (Media.AudioSource != null)
                {
                    await Media.AudioSource.StartAudio();
                }

                if (Media.VideoSource != null)
                {
                    await Media.VideoSource.StartVideo();
                }
            }
        }

        public async override void Close(string reason)
        {
            if (!base.IsClosed)
            {
                base.Close(reason);

                if (Media.AudioSource != null)
                {
                    await Media.AudioSource.CloseAudio();
                }

                if (Media.VideoSource != null)
                {
                    await Media.VideoSource.CloseVideo();
                }
            }
        }

        /// <summary>
        /// Handler for a PCM audio sample getting generated from the local audio source.
        /// </summary>
        /// <param name="durationMilliseconds"></param>
        /// <param name="pcmSample"></param>
        private void OnAudioSourceRawSample(AudioSamplingRatesEnum samplingRate, uint durationMilliseconds, short[] sample)
        {
            var sendingFormat = base.GetSendingFormat(SDPMediaTypesEnum.audio);
            var encodedSample = _audioEncoder.EncodeAudio(sample, sendingFormat, samplingRate);
            uint rtpTimestampDuration = RTP_AUDIO_TIMESTAMP_RATE / 1000 * durationMilliseconds;

            base.SendAudioFrame(rtpTimestampDuration, (int)sendingFormat.FormatCodec, encodedSample);
        }

        private void OnAudioSourceEncodedSample(AudioFormat audioFormat, uint durationRtpUnits, byte[] sample)
        {
            base.SendAudioFrame(durationRtpUnits, audioFormat.PayloadID, sample);
        }

        private void OnVideoSourceEncodedSample(VideoFormat videoFormat, uint durationRtpUnits, byte[] sample)
        {
            if(videoFormat.Codec == VideoCodecsEnum.H264)
            {
                base.SendH264Frame(durationRtpUnits, videoFormat.PayloadID, sample);
            }
            else if(videoFormat.Codec == VideoCodecsEnum.VP8)
            {
                base.SendVp8Frame(durationRtpUnits, videoFormat.PayloadID, sample);
            }
            else
            {
                logger.LogWarning($"An encoded video sample was received for {videoFormat.Codec} but there's no RTP send method.");
            }
        }

        protected void RtpMediaPacketReceived(IPEndPoint remoteEndPoint, SDPMediaTypesEnum mediaType, RTPPacket rtpPacket)
        {
            var hdr = rtpPacket.Header;
            bool marker = rtpPacket.Header.MarkerBit > 0;

            if (mediaType == SDPMediaTypesEnum.audio && Media.AudioSink != null)
            {
                var sendingFormat = base.GetSendingFormat(SDPMediaTypesEnum.audio);

                // If the audio source wants to do it's own decoding OR it's not one of the codecs that
                // this library has a decoder for then pass the raw RTP sample through.
                if (Media.AudioSink.EncodedSamplesOnly || 
                    !(sendingFormat.FormatCodec == SDPMediaFormatsEnum.PCMU 
                    || sendingFormat.FormatCodec == SDPMediaFormatsEnum.PCMA
                    || sendingFormat.FormatCodec == SDPMediaFormatsEnum.G722))
                {
                    Media.AudioSink.GotAudioRtp(remoteEndPoint, hdr.SyncSource, hdr.SequenceNumber, hdr.Timestamp, hdr.PayloadType, marker, rtpPacket.Payload);
                }
                else
                {
                    var decodedSample = _audioEncoder.DecodeAudio(rtpPacket.Payload, sendingFormat, Media.AudioSink.AudioPlaybackRate);
                    Media.AudioSink.GotAudioSample(decodedSample);
                }
            }
            else if(mediaType == SDPMediaTypesEnum.video && Media.VideoSink != null)
            {
                Media.VideoSink.GotVideoRtp(remoteEndPoint, hdr.SyncSource, hdr.SequenceNumber, hdr.Timestamp, hdr.PayloadType, marker, rtpPacket.Payload);
            }
        }
    }
}
