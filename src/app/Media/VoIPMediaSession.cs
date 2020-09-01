using System;
using System.Collections.Generic;
using System.Linq;
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
        private static ILogger logger = SIPSorcery.Sys.Log.Logger;

        public MediaEndPoints Media { get; private set; }

        public VoIPMediaSession(
            MediaEndPoints mediaEndPoint,
            IPAddress bindAddress = null,
            int bindPort = 0)
            : base(false, false, false, bindAddress, bindPort)
        {
            if(mediaEndPoint == null)
            {
                throw new ArgumentNullException("mediaEndPoint", "The media end point parameter cannot be null.");
            }

            Media = mediaEndPoint;

            // Wire up the audio and video sample event handlers.
            if (Media.AudioSource != null)
            {
                var audioTrack = new MediaStreamTrack(mediaEndPoint.AudioSource.GetAudioSourceFormats());
                base.addTrack(audioTrack);
                Media.AudioSource.OnAudioSourceEncodedSample += OnAudioSourceEncodedSample;
            }

            if (Media.VideoSource != null)
            {
                var videoTrack = new MediaStreamTrack(mediaEndPoint.VideoSource.GetVideoSourceFormats());
                base.addTrack(videoTrack);
                Media.VideoSource.OnVideoSourceEncodedSample += OnVideoSourceEncodedSample;
            }

            if (Media.AudioSink != null || Media.VideoSink != null)
            {
                base.OnRtpPacketReceived += RtpMediaPacketReceived;
            }

            base.OnAudioFormatsNegotiated += AudioFormatsNegotiated;
            base.OnVideoFormatsNegotiated += VideoFormatsNegotiated;
        }

        private void AudioFormatsNegotiated(List<SDPMediaFormat> audoFormats)
        {
            var audioCodec = SDPMediaFormatInfo.GetAudioCodecForSdpFormat(audoFormats.First().FormatCodec);
            logger.LogDebug($"Setting audio sink and source format to {audioCodec}.");
            Media.AudioSink?.SetAudioSinkFormat(audioCodec);
            Media.AudioSource?.SetAudioSourceFormat(audioCodec);
        }

        private void VideoFormatsNegotiated(List<SDPMediaFormat> videoFormats)
        {
            var videoCodec = SDPMediaFormatInfo.GetVideoCodecForSdpFormat(videoFormats.First().FormatCodec);
            logger.LogDebug($"Setting video sink and source format to {videoCodec}.");
            Media.VideoSink?.SetVideoSinkFormat(videoCodec);
            Media.VideoSource?.SetVideoSourceFormat(videoCodec);
        }

        public async override Task Start()
        {
            if (!base.IsStarted)
            {
                await base.Start();
                await (Media.AudioSource?.StartAudio() ?? Task.CompletedTask);
                await (Media.VideoSource?.StartVideo() ?? Task.CompletedTask);
            }
        }

        public async override void Close(string reason)
        {
            if (!base.IsClosed)
            {
                base.Close(reason);
                await (Media.AudioSource?.CloseAudio() ?? Task.CompletedTask);
                await (Media.VideoSource?.CloseVideo() ?? Task.CompletedTask);
            }
        }

        private void OnAudioSourceEncodedSample(AudioCodecsEnum audioCodec, uint durationRtpUnits, byte[] sample)
        {
            //logger.LogDebug($"RTP audio duration {durationRtpUnits}, payload length {sample.Length} bytes.");
            base.SendMedia(SDPMediaTypesEnum.audio, durationRtpUnits, sample);
        }

        private void OnVideoSourceEncodedSample(VideoCodecsEnum videoCodec, uint durationRtpUnits, byte[] sample)
        {
            base.SendMedia(SDPMediaTypesEnum.video, durationRtpUnits, sample);
        }

        protected void RtpMediaPacketReceived(IPEndPoint remoteEndPoint, SDPMediaTypesEnum mediaType, RTPPacket rtpPacket)
        {
            var hdr = rtpPacket.Header;
            bool marker = rtpPacket.Header.MarkerBit > 0;

            if (mediaType == SDPMediaTypesEnum.audio && Media.AudioSink != null)
            {
                Media.AudioSink.GotAudioRtp(remoteEndPoint, hdr.SyncSource, hdr.SequenceNumber, hdr.Timestamp, hdr.PayloadType, marker, rtpPacket.Payload);
            }
            else if (mediaType == SDPMediaTypesEnum.video && Media.VideoSink != null)
            {
                Media.VideoSink.GotVideoRtp(remoteEndPoint, hdr.SyncSource, hdr.SequenceNumber, hdr.Timestamp, hdr.PayloadType, marker, rtpPacket.Payload);
            }
        }
    }
}
