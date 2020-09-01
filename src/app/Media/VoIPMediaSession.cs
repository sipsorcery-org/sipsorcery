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
    /// <summary>
    /// This class serves as a bridge, or mapping, between the media end points, typically 
    /// provided by a separate package, and a media session. Its goal is to wire up the 
    /// sources and sinks from the media end point to the transport functions provided
    /// by an RTP session. 
    /// 
    /// For audio end points it will also attempt to encode and decode formats that the 
    /// inbuilt C# encoder class understands. The encoder can be turned off if an 
    /// application wishes to do its own audio encoding.
    /// 
    /// For video end points there are no C# encoders available so the application must
    /// always co-ordinate the encoding and decoding of samples sent to and received from
    /// the RTP transport.
    /// </summary>
    public class VoIPMediaSession : RTPSession, IMediaSession
    {
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
                var audioTrack = new MediaStreamTrack(mediaEndPoint.AudioSource.GetAudioSourceFormats());
                base.addTrack(audioTrack);

                // Example being a microphone.
                Media.AudioSource.OnAudioSourceRawSample += OnAudioSourceRawSample;
                Media.AudioSource.OnAudioSourceEncodedSample += OnAudioSourceEncodedSample;
            }

            if(Media.VideoSource != null)
            {
                var videoTrack = new MediaStreamTrack(mediaEndPoint.VideoSource.GetVideoSourceFormats());
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
        /// Handler for a raw PCM audio sample getting generated from the local audio source.
        /// </summary>
        /// <param name="durationMilliseconds">The duration of the sample in milliseconds.</param>
        /// <param name="pcmSample">The raw signed PCM sample.</param>
        private void OnAudioSourceRawSample(AudioSamplingRatesEnum samplingRate, uint durationMilliseconds, short[] sample)
        {
            var sendingFormat = base.GetSendingFormat(SDPMediaTypesEnum.audio);
            var sendingCodec = SDPMediaFormatInfo.GetAudioCodecForSdpFormat(sendingFormat.FormatCodec);
            var encodedSample = _audioEncoder.EncodeAudio(sample, sendingCodec, samplingRate);
            uint rtpTimestampDuration = RTP_AUDIO_TIMESTAMP_RATE / 1000 * durationMilliseconds;

            base.SendAudioFrame(rtpTimestampDuration, (int)sendingFormat.FormatCodec, encodedSample);
        }

        private void OnAudioSourceEncodedSample(AudioFormat audioFormat, uint durationRtpUnits, byte[] sample)
        {
            base.SendAudioFrame(durationRtpUnits, audioFormat.PayloadID, sample);
        }

        private void OnVideoSourceEncodedSample(VideoFormat videoFormat, uint durationRtpUnits, byte[] sample)
        {
            base.SendMedia(SDPMediaTypesEnum.video, durationRtpUnits, sample);
        }

        protected void RtpMediaPacketReceived(IPEndPoint remoteEndPoint, SDPMediaTypesEnum mediaType, RTPPacket rtpPacket)
        {
            var hdr = rtpPacket.Header;
            bool marker = rtpPacket.Header.MarkerBit > 0;

            if (mediaType == SDPMediaTypesEnum.audio && Media.AudioSink != null)
            {
                var sendingFormat = base.GetSendingFormat(SDPMediaTypesEnum.audio);
                var sendingCodec = SDPMediaFormatInfo.GetAudioCodecForSdpFormat(sendingFormat.FormatCodec);

                // If the audio source wants to do it's own decoding OR it's not one of the codecs that
                // this library has a decoder for then pass the raw RTP sample through.
                if (Media.AudioSink.EncodedSamplesOnly || 
                    !(sendingCodec == AudioCodecsEnum.PCMU 
                    || sendingCodec == AudioCodecsEnum.PCMA
                    || sendingCodec == AudioCodecsEnum.G722))
                {
                    Media.AudioSink.GotAudioRtp(remoteEndPoint, hdr.SyncSource, hdr.SequenceNumber, hdr.Timestamp, hdr.PayloadType, marker, rtpPacket.Payload);
                }
                else
                {
                    var decodedSample = _audioEncoder.DecodeAudio(rtpPacket.Payload, sendingCodec, Media.AudioSink.AudioPlaybackRate);
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
