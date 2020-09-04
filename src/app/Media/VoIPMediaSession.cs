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
        private const string TEST_PATTERN_IMAGE_PATH = "media/testpattern.jpeg";
        private const string TEST_PATTERN_ONHOLD_IMAGE_PATH = "media/testpattern_inverted.jpeg";
        private const int TEST_PATTERN_FPS = 30;
        private const int TEST_PATTERN_ONHOLD_FPS = 3;
        private const string MUSIC_FILE_PCMU = "media/Macroform_-_Simplicity.ulaw";
        private const string MUSIC_FILE_PCMA = "media/Macroform_-_Simplicity.alaw";
        private const string MUSIC_FILE_G722 = "media/Macroform_-_Simplicity.g722";

        private static ILogger logger = SIPSorcery.Sys.Log.Logger;

        private VideoTestPatternSource _videoTestPatternSource;
        private AudioExtrasSource _audioExtrasSource;

        public MediaEndPoints Media { get; private set; }

        public AudioExtrasSource AudioExtrasSource
        {
            get => _audioExtrasSource;
        }

        public VideoTestPatternSource TestPatternSource
        {
            get => _videoTestPatternSource;
        }

        public event VideoSinkSampleDecodedDelegate OnVideoSinkSample;

        public VoIPMediaSession(
            MediaEndPoints mediaEndPoint,
            IPAddress bindAddress = null,
            int bindPort = 0)
            : base(false, false, false, bindAddress, bindPort)
        {
            if (mediaEndPoint == null)
            {
                throw new ArgumentNullException("mediaEndPoint", "The media end point parameter cannot be null.");
            }

            Media = mediaEndPoint;

            // The audio extras source is used for on-hold music.
            _audioExtrasSource = new AudioExtrasSource();
            _audioExtrasSource.OnAudioSourceEncodedSample += SendAudio;

            // Wire up the audio and video sample event handlers.
            if (Media.AudioSource != null)
            {
                var audioTrack = new MediaStreamTrack(mediaEndPoint.AudioSource.GetAudioSourceFormats());
                base.addTrack(audioTrack);
                Media.AudioSource.OnAudioSourceEncodedSample += base.SendAudio;
            }

            if (Media.VideoSource != null)
            {
                var videoTrack = new MediaStreamTrack(mediaEndPoint.VideoSource.GetVideoSourceFormats());
                base.addTrack(videoTrack);
                Media.VideoSource.OnVideoSourceEncodedSample += base.SendVideo;

                // The video test pattern is used to provide a video stream to the remote party.
                _videoTestPatternSource = new VideoTestPatternSource();
                _videoTestPatternSource.OnVideoSourceRawSample += Media.VideoSource.ExternalVideoSourceRawSample;
            }

            if (Media.VideoSink != null)
            {
                Media.VideoSink.OnVideoSinkDecodedSample += VideoSinkSampleReady;
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
            _audioExtrasSource.SetAudioSourceFormat(audioCodec);
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
                await base.Start().ConfigureAwait(false);

                if (HasAudio)
                {
                    if (Media.AudioSource != null)
                    {
                        await Media.AudioSource.StartAudio().ConfigureAwait(false);
                    }
                }

                if (HasVideo)
                {
                    if (Media.VideoSource != null)
                    {
                        await Media.VideoSource.StartVideo().ConfigureAwait(false);
                    }

                    await _videoTestPatternSource.StartVideo().ConfigureAwait(false);
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
                    await Media.AudioSource.CloseAudio().ConfigureAwait(false);
                }

                if (_audioExtrasSource != null)
                {
                    _audioExtrasSource.OnAudioSourceEncodedSample -= SendAudio;
                    await _audioExtrasSource.CloseAudio().ConfigureAwait(false);
                }

                if (Media.VideoSource != null)
                {
                    await Media.VideoSource.CloseVideo().ConfigureAwait(false);
                }

                if (_videoTestPatternSource != null)
                {
                    _videoTestPatternSource.OnVideoSourceRawSample -= Media.VideoSource.ExternalVideoSourceRawSample;
                    await _videoTestPatternSource.CloseVideo().ConfigureAwait(false);
                }
            }
        }

        private void VideoSinkSampleReady(byte[] bmp, uint width, uint height, int stride)
        {
            OnVideoSinkSample?.Invoke(bmp, width, height, stride);
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

        public async Task PutOnHold()
        {
            if (HasAudio)
            {
                await Media.AudioSource.PauseAudio().ConfigureAwait(false);

                AudioSourceOptions audioOnHold =
                    new AudioSourceOptions
                    {
                        AudioSource = AudioSourcesEnum.Music,
                        SourceFiles = new Dictionary<AudioCodecsEnum, string>
                        {
                            { AudioCodecsEnum.PCMU, MUSIC_FILE_PCMU },
                            { AudioCodecsEnum.PCMA, MUSIC_FILE_PCMA }
                        }
                    };

                _audioExtrasSource.SetSource(audioOnHold);
            }

            if (HasVideo)
            {
                _videoTestPatternSource.SetTestPatternPath(TEST_PATTERN_ONHOLD_IMAGE_PATH);
                _videoTestPatternSource.SetFrameRate(TEST_PATTERN_ONHOLD_FPS);
                Media.VideoSource.ForceKeyFrame();
            }
        }

        public async void TakeOffHold()
        {
            if (HasAudio)
            {
                _audioExtrasSource.SetSource(AudioSourcesEnum.None);
                await Media.AudioSource.ResumeAudio().ConfigureAwait(false);
            }

            if (HasVideo)
            {
                _videoTestPatternSource.SetTestPatternPath(TEST_PATTERN_IMAGE_PATH);
                _videoTestPatternSource.SetFrameRate(TEST_PATTERN_FPS);
                Media.VideoSource.ForceKeyFrame();
            }
        }
    }
}
