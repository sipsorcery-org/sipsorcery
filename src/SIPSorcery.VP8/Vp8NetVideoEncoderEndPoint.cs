//-----------------------------------------------------------------------------
// Filename: Vp8NetVideoEncoderEndPoint.cs
//
// Description: Implements a video source and sink for the VP8.Net video decoder.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 28 Jan 2021  Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorceryMedia.Abstractions;

namespace Vpx.Net
{
    public class Vp8NetVideoEncoderEndPoint : IVideoSource, IVideoSink, IDisposable
    {
        private const int VIDEO_SAMPLING_RATE = 90000;
        private const int DEFAULT_FRAMES_PER_SECOND = 30;
        private const int VP8_FORMAT_ID = 96;

        private static ILogger logger = SIPSorcery.LogFactory.CreateLogger<Vp8NetVideoEncoderEndPoint>();

        public static readonly List<VideoFormat> SupportedFormats = new List<VideoFormat>
        {
            new VideoFormat(VideoCodecsEnum.VP8, VP8_FORMAT_ID, VIDEO_SAMPLING_RATE)
        };

        private MediaFormatManager<VideoFormat> _formatManager;
        private VP8Codec _vp8Codec;
        private bool _isClosed;

        /// <summary>
        /// This video source DOES NOT generate raw samples. Subscribe to the encoded samples event
        /// to get samples ready for passing to the RTP transport layer.
        /// </summary>
        [Obsolete("This video source only generates encoded samples. No raw video samples will be supplied to this event.")]
        public event RawVideoSampleDelegate OnVideoSourceRawSample { add { } remove { } }

        /// <summary>
        /// This event will be fired whenever a video sample is encoded and is ready to transmit to the remote party.
        /// </summary>
        public event EncodedSampleDelegate OnVideoSourceEncodedSample;

        /// <summary>
        /// This event is fired after the sink decodes a video frame from the remote party.
        /// </summary>
        public event VideoSinkSampleDecodedDelegate OnVideoSinkDecodedSample;

#pragma warning disable CS0067
        public event SourceErrorDelegate OnVideoSourceError;
        public event RawVideoSampleFasterDelegate OnVideoSourceRawSampleFaster;
        public event VideoSinkSampleDecodedFasterDelegate OnVideoSinkDecodedSampleFaster;
#pragma warning restore CS0067

        /// <summary>
        /// Creates a new video source that can encode and decode samples.
        /// </summary>
        public Vp8NetVideoEncoderEndPoint()
        {
            _formatManager = new MediaFormatManager<VideoFormat>(SupportedFormats);
            _vp8Codec = new VP8Codec();
        }

        /// <summary>
        /// Number of frames between forced keyframes (the GOP length). Defaults to the
        /// <see cref="VP8Codec"/> default. A short GOP bounds how long a packet-loss-induced
        /// decode error can persist: inter frames predict from the previous frame, so until a
        /// keyframe arrives a single lost RTP packet corrupts every subsequent frame. Because
        /// the SIPSorcery pipeline does not currently turn a received RTCP PLI into a keyframe
        /// request, the next scheduled keyframe is the only recovery point — so for
        /// high-motion sources sent over a lossy/bursty path a short interval (or 1, i.e. every
        /// frame a keyframe) is far more robust. Range [1, int.MaxValue].
        /// </summary>
        public int KeyframeIntervalFrames
        {
            get => _vp8Codec.KeyframeIntervalFrames;
            set => _vp8Codec.KeyframeIntervalFrames = value;
        }

        /// <summary>
        /// VP8 base quantizer index in the range [0, 127] used for every frame (higher = smaller
        /// frames and lower quality). Defaults to the <see cref="VP8Codec"/> default. This is the
        /// primary lever for trading bitrate against quality on this keyframe-oriented encoder,
        /// which has no rate control.
        /// </summary>
        public int BaseQIndex
        {
            get => _vp8Codec.BaseQIndex;
            set => _vp8Codec.BaseQIndex = value;
        }

        /// <summary>
        /// Opt-in per-macroblock intra-fallback mode decision on inter frames. Prevents
        /// quality drift on content ZEROMV prediction cannot represent (slow ramps/fades,
        /// fast-changing detail) at roughly double the inter-frame encode cost. Defaults to
        /// false. See <see cref="VP8Codec.EnableIntraFallback"/> for details.
        /// </summary>
        public bool EnableIntraFallback
        {
            get => _vp8Codec.EnableIntraFallback;
            set => _vp8Codec.EnableIntraFallback = value;
        }

        public void RestrictFormats(Func<VideoFormat, bool> filter) => _formatManager.RestrictFormats(filter);
        public List<VideoFormat> GetVideoSourceFormats() => _formatManager.GetSourceFormats();
        public void SetVideoSourceFormat(VideoFormat videoFormat) => _formatManager.SetSelectedFormat(videoFormat);
        public List<VideoFormat> GetVideoSinkFormats() => _formatManager.GetSourceFormats();
        public void SetVideoSinkFormat(VideoFormat videoFormat) => _formatManager.SetSelectedFormat(videoFormat);

        public void ForceKeyFrame() => _vp8Codec.ForceKeyFrame();
        public void GotVideoRtp(IPEndPoint remoteEndPoint, uint ssrc, uint seqnum, uint timestamp, int payloadID, bool marker, byte[] payload) =>
            throw new ApplicationException("The VP8 Video End Point requires full video frames rather than individual RTP packets.");
        public bool HasEncodedVideoSubscribers() => OnVideoSourceEncodedSample != null;
        public bool IsVideoSourcePaused() => false;
        public Task PauseVideo() => Task.CompletedTask;
        public Task ResumeVideo() => Task.CompletedTask;
        public Task StartVideo() => Task.CompletedTask;
        public Task CloseVideoSink() => Task.CompletedTask;
        public Task PauseVideoSink() => Task.CompletedTask;
        public Task ResumeVideoSink() => Task.CompletedTask;
        public Task StartVideoSink() => Task.CompletedTask;

        public MediaEndPoints ToMediaEndPoints()
        {
            return new MediaEndPoints
            {
                VideoSource = this,
                VideoSink = this
            };
        }

        public void ExternalVideoSourceRawSample(uint durationMilliseconds, int width, int height, byte[] sample, VideoPixelFormatsEnum pixelFormat)
        {
            if (!_isClosed)
            {
                if (OnVideoSourceEncodedSample != null)
                {
                    var encodedBuffer = _vp8Codec.EncodeVideo(width, height, sample, pixelFormat, VideoCodecsEnum.VP8);

                    if (encodedBuffer != null)
                    {
                        uint fps = (durationMilliseconds > 0) ? 1000 / durationMilliseconds : DEFAULT_FRAMES_PER_SECOND;
                        uint durationRtpTS = VIDEO_SAMPLING_RATE / fps;
                        OnVideoSourceEncodedSample.Invoke(durationRtpTS, encodedBuffer);
                    }
                }
            }
        }

        public void GotVideoFrame(IPEndPoint remoteEndPoint, uint timestamp, byte[] frame, VideoFormat format)
        {
            if (!_isClosed)
            {
                foreach (var decoded in _vp8Codec.DecodeVideo(frame, VideoPixelFormatsEnum.Bgr, VideoCodecsEnum.VP8))
                {
                    OnVideoSinkDecodedSample(decoded.Sample, decoded.Width, decoded.Height, (int)(decoded.Width * 3), VideoPixelFormatsEnum.Bgr);
                }
            }
        }

        public Task CloseVideo()
        {
            if (!_isClosed)
            {
                _isClosed = true;
                Dispose();
            }

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _vp8Codec?.Dispose();
        }

        public void ExternalVideoSourceRawSampleFaster(uint durationMilliseconds, RawImage rawImage)
        {
            throw new NotImplementedException();
        }
    }
}
