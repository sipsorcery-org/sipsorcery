//-----------------------------------------------------------------------------
// Filename: VoIPMediaSession.cs
//
// Description: This class serves as a bridge, or mapping, between the media end points, 
// typically  provided by a separate package, and a media session. Its goal is to wire up the 
// sources and sinks from the media end point to the transport functions provided
// by an RTP session.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 04 Sep 2020	Aaron Clauson	Created, Dublin, Ireland.
// 26 Jul 2021	Kurt Kießling	Added secure media negotiation.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions;

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
    public class VoIPMediaSession : RTPSession
    {
        private const int TEST_PATTERN_FPS = 30;
        private const int TEST_PATTERN_ONHOLD_FPS = 3;

        private static ILogger logger = SIPSorcery.Sys.Log.Logger;

        private VideoTestPatternSource _videoTestPatternSource;
        private AudioExtrasSource _audioExtrasSource;
        private bool _videoCaptureDeviceFailed;

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

        /// <summary>
        /// Default constructor which creates the simplest possible send only audio session. It does not
        /// wire up any devices or video processing.
        /// </summary>
        public VoIPMediaSession(string musicFilePath = null, Func<AudioFormat, bool> restrictFormats = null) : base(false, false, false)
        {
            _audioExtrasSource = new AudioExtrasSource();
            _audioExtrasSource.OnAudioSourceEncodedSample += SendAudio;
            _audioExtrasSource.SetSource(AudioSourcesEnum.Music);

            if(restrictFormats != null)
            {
                _audioExtrasSource.RestrictFormats(restrictFormats);
            }


            Media = new MediaEndPoints { AudioSource = _audioExtrasSource };

            var audioTrack = new MediaStreamTrack(_audioExtrasSource.GetAudioSourceFormats());
            base.addTrack(audioTrack);
            Media.AudioSource.OnAudioSourceEncodedSample += SendAudio;
            base.OnAudioFormatsNegotiated += AudioFormatsNegotiated;
        }

        public VoIPMediaSession(MediaEndPoints mediaEndPoint, VideoTestPatternSource testPatternSource)
            : this(mediaEndPoint, null, 0, testPatternSource)
        { }

        public VoIPMediaSession(
            MediaEndPoints mediaEndPoint,
            IPAddress bindAddress = null,
            int bindPort = 0,
            VideoTestPatternSource testPatternSource = null)
            : this(new VoIPMediaSessionConfig { MediaEndPoint = mediaEndPoint, BindAddress = bindAddress, BindPort = bindPort, TestPatternSource = testPatternSource })
        {
        }

        public VoIPMediaSession(VoIPMediaSessionConfig config)
            : base(new RtpSessionConfig
            {
                IsMediaMultiplexed = false,
                IsRtcpMultiplexed = false,
                RtpSecureMediaOption = config.RtpSecureMediaOption,
                BindAddress = config.BindAddress,
                BindPort = config.BindPort,
                RtpPortRange = config.RtpPortRange
            })
        {
            if (config.MediaEndPoint == null)
            {
                throw new ArgumentNullException("MediaEndPoint", "The media end point parameter cannot be null.");
            }

            Media = config.MediaEndPoint;

            // The audio extras source is used for on-hold music.
            _audioExtrasSource = new AudioExtrasSource();
            _audioExtrasSource.OnAudioSourceEncodedSample += SendAudio;

            // Wire up the audio and video sample event handlers.
            if (Media.AudioSource != null)
            {
                var audioTrack = new MediaStreamTrack(config.MediaEndPoint.AudioSource.GetAudioSourceFormats());
                base.addTrack(audioTrack);
                Media.AudioSource.OnAudioSourceEncodedSample += SendAudio;
            }

            if (Media.VideoSource != null)
            {
                var videoTrack = new MediaStreamTrack(config.MediaEndPoint.VideoSource.GetVideoSourceFormats());
                base.addTrack(videoTrack);
                Media.VideoSource.OnVideoSourceEncodedSample += base.SendVideo;
                Media.VideoSource.OnVideoSourceError += VideoSource_OnVideoSourceError;

                if (config.TestPatternSource != null)
                {
                    // The test pattern source is used as failover if the webcam initialisation fails.
                    // It's also used as the video stream if the call is put on hold.
                    _videoTestPatternSource = config.TestPatternSource;
                    _videoTestPatternSource.OnVideoSourceEncodedSample += base.SendVideo;
                    //_videoTestPatternSource.OnVideoSourceRawSample += Media.VideoSource.ExternalVideoSourceRawSample;
                }
            }

            if (Media.VideoSink != null)
            {
                Media.VideoSink.OnVideoSinkDecodedSample += VideoSinkSampleReady;
                base.OnVideoFrameReceived += Media.VideoSink.GotVideoFrame;
            }

            if (Media.AudioSink != null)
            {
                base.OnRtpPacketReceived += RtpMediaPacketReceived;
            }

            base.OnAudioFormatsNegotiated += AudioFormatsNegotiated;
            base.OnVideoFormatsNegotiated += VideoFormatsNegotiated;
            
        }

        private async void VideoSource_OnVideoSourceError(string errorMessage)
        {
            if (!_videoCaptureDeviceFailed)
            {
                _videoCaptureDeviceFailed = true;

                logger.LogWarning($"Video source for capture device failure. {errorMessage}");

                // Can't use the webcam, switch to the test pattern source.
                await _videoTestPatternSource.StartVideo().ConfigureAwait(false);
            }
        }

        private void AudioFormatsNegotiated(List<AudioFormat> audoFormats)
        {
            var audioFormat = audoFormats.First();
            logger.LogDebug($"Setting audio sink and source format to {audioFormat.FormatID}:{audioFormat.Codec} {audioFormat.ClockRate} (RTP clock rate {audioFormat.RtpClockRate}).");
            Media.AudioSink?.SetAudioSinkFormat(audioFormat);
            Media.AudioSource?.SetAudioSourceFormat(audioFormat);
            _audioExtrasSource.SetAudioSourceFormat(audioFormat);
        }

        private void VideoFormatsNegotiated(List<VideoFormat> videoFormats)
        {
            var videoFormat = videoFormats.First();
            logger.LogDebug($"Setting video sink and source format to {videoFormat.FormatID}:{videoFormat.Codec}.");
            Media.VideoSink?.SetVideoSinkFormat(videoFormat);
            Media.VideoSource?.SetVideoSourceFormat(videoFormat);
            _videoTestPatternSource?.SetVideoSourceFormat(videoFormat);
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
                    if (Media.AudioSink != null)
                    {
                        await Media.AudioSink.StartAudioSink().ConfigureAwait(false);
                    }
                }

                if (HasVideo)
                {
                    if (Media.VideoSource != null)
                    {
                        if (!_videoCaptureDeviceFailed)
                        {
                            await Media.VideoSource.StartVideo().ConfigureAwait(false);
                        }
                        else
                        {
                            logger.LogWarning($"Webcam video source failed before start, switching to test pattern source.");

                            // The webcam source failed to start. Switch to a test pattern source.
                            await _videoTestPatternSource.StartVideo().ConfigureAwait(false);
                        }
                    }
                }
            }
        }

        public async override void Close(string reason)
        {
            if (!base.IsClosed)
            {
                base.Close(reason);

                if (_audioExtrasSource != null)
                {
                    _audioExtrasSource.OnAudioSourceEncodedSample -= SendAudio;
                    await _audioExtrasSource.CloseAudio().ConfigureAwait(false);
                }

                if (_videoTestPatternSource != null)
                {
                    await _videoTestPatternSource.CloseVideo().ConfigureAwait(false);
                    _videoTestPatternSource.OnVideoSourceRawSample -= Media.VideoSource.ExternalVideoSourceRawSample;
                }

                if (Media.AudioSource != null)
                {
                    await Media.AudioSource.CloseAudio().ConfigureAwait(false);
                }

                if (Media.AudioSink != null)
                {
                    await Media.AudioSink.CloseAudioSink().ConfigureAwait(false);
                }

                if (Media.VideoSource != null)
                {
                    await Media.VideoSource.CloseVideo().ConfigureAwait(false);
                }

                if (Media.VideoSink != null)
                {
                    Media.VideoSink.OnVideoSinkDecodedSample -= VideoSinkSampleReady;
                    base.OnVideoFrameReceived -= Media.VideoSink.GotVideoFrame;
                }
            }
        }

        private void VideoSinkSampleReady(byte[] buffer, uint width, uint height, int stride, VideoPixelFormatsEnum pixelFormat)
        {
            OnVideoSinkSample?.Invoke(buffer, width, height, stride, pixelFormat);
        }

        protected void RtpMediaPacketReceived(IPEndPoint remoteEndPoint, SDPMediaTypesEnum mediaType, RTPPacket rtpPacket)
        {
            var hdr = rtpPacket.Header;
            bool marker = rtpPacket.Header.MarkerBit > 0;

            if (mediaType == SDPMediaTypesEnum.audio && Media.AudioSink != null)
            {
                Media.AudioSink.GotAudioRtp(remoteEndPoint, hdr.SyncSource, hdr.SequenceNumber, hdr.Timestamp, hdr.PayloadType, marker, rtpPacket.Payload);
            }
        }

        public async Task PutOnHold()
        {
            if (HasAudio)
            {
                await Media.AudioSource.PauseAudio().ConfigureAwait(false);
                _audioExtrasSource.SetSource(AudioSourcesEnum.Music);
            }

            if (HasVideo)
            {
                await Media.VideoSource.PauseVideo().ConfigureAwait(false);

                //_videoTestPatternSource.SetEmbeddedTestPatternPath(VideoTestPatternSource.TEST_PATTERN_INVERTED_RESOURCE_PATH);
                _videoTestPatternSource.SetFrameRate(TEST_PATTERN_ONHOLD_FPS);

                Media.VideoSource.ForceKeyFrame();
                await _videoTestPatternSource.ResumeVideo().ConfigureAwait(false);
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
                await _videoTestPatternSource.PauseVideo().ConfigureAwait(false);

                //_videoTestPatternSource.SetEmbeddedTestPatternPath(VideoTestPatternSource.TEST_PATTERN_RESOURCE_PATH);
                _videoTestPatternSource.SetFrameRate(TEST_PATTERN_FPS);

                Media.VideoSource.ForceKeyFrame();

                if (!_videoCaptureDeviceFailed)
                {
                    await Media.VideoSource.ResumeVideo().ConfigureAwait(false);
                }
                else
                {
                    await _videoTestPatternSource.ResumeVideo().ConfigureAwait(false);
                }
            }
        }
    }
}
