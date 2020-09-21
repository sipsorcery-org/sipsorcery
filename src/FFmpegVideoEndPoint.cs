using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using FFmpeg.AutoGen;
using Microsoft.Extensions.Logging;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.Abstractions.V1;

namespace SIPSorceryMedia.FFmpeg
{
    public class FFmpegVideoEndPoint : IVideoSink, IVideoSource, IDisposable
    {
        private const int VIDEO_SAMPLING_RATE = 90000;
        private const int DEFAULT_FRAMES_PER_SECOND = 30;

        public ILogger logger = SIPSorcery.LogFactory.CreateLogger<FFmpegVideoEndPoint>();

        public static readonly List<VideoCodecsEnum> SupportedCodecs = new List<VideoCodecsEnum>
        {
            VideoCodecsEnum.VP8,
            VideoCodecsEnum.H264
        };

        private VideoEncoder _ffmpegEncoder;

        private CodecManager<VideoCodecsEnum> _codecManager;
        private bool _isStarted;
        private bool _isPaused;
        private bool _isClosed;
        private bool _forceKeyFrame;

        public event VideoSinkSampleDecodedDelegate? OnVideoSinkDecodedSample;
        public event EncodedSampleDelegate? OnVideoSourceEncodedSample;

#pragma warning disable CS0067
        public event RawVideoSampleDelegate? OnVideoSourceRawSample;
        public event SourceErrorDelegate? OnVideoSourceError;
#pragma warning restore CS0067

        public FFmpegVideoEndPoint()
        {
            _codecManager = new CodecManager<VideoCodecsEnum>(SupportedCodecs);
            _ffmpegEncoder = new VideoEncoder();
        }

        public MediaEndPoints ToMediaEndPoints()
        {
            return new MediaEndPoints
            {
                VideoSource = this,
                VideoSink = this
            };
        }

        public List<VideoCodecsEnum> GetVideoSinkFormats() => _codecManager.GetSourceFormats();
        public void SetVideoSinkFormat(VideoCodecsEnum videoFormat) => _codecManager.SetSelectedCodec(videoFormat);
        public void RestrictCodecs(List<VideoCodecsEnum> codecs) => _codecManager.RestrictCodecs(codecs);
        public List<VideoCodecsEnum> GetVideoSourceFormats() => _codecManager.GetSourceFormats();
        public void SetVideoSourceFormat(VideoCodecsEnum videoFormat) => _codecManager.SetSelectedCodec(videoFormat);
        public void ForceKeyFrame() => _forceKeyFrame = true;
        public bool HasEncodedVideoSubscribers() => OnVideoSourceEncodedSample != null;
        public bool IsVideoSourcePaused() => _isPaused;
        public void GotVideoRtp(IPEndPoint remoteEndPoint, uint ssrc, uint seqnum, uint timestamp, int payloadID, bool marker, byte[] payload) =>
            throw new ApplicationException("The FFmpeg Video End Point requires full video frames rather than individual RTP packets.");

        public void GotVideoFrame(IPEndPoint remoteEndPoint, uint timestamp, byte[] payload)
        {
            if (!_isClosed)
            {
                AVCodecID codecID = FFmpegConvert.GetAVCodecID(_codecManager.SelectedCodec);

                var rgbFrames = _ffmpegEncoder.Decode(codecID, payload, out var width, out var height);

                if (rgbFrames == null || width == 0 || height == 0)
                {
                    logger.LogWarning($"Decode of video sample failed, width {width}, height {height}.");
                }
                else
                {
                    foreach (var rgbFrame in rgbFrames)
                    {
                        OnVideoSinkDecodedSample?.Invoke(rgbFrame, (uint)width, (uint)height, (int)(width * 3), VideoPixelFormatsEnum.Rgb);
                    }
                }
            }
        }

        public Task PauseVideo()
        {
            _isPaused = true;
            return Task.CompletedTask;
        }

        public Task ResumeVideo()
        {
            _isPaused = false;
            return Task.CompletedTask;
        }

        public Task StartVideo()
        {
            if (!_isStarted)
            {
                _isStarted = true;
            }

            return Task.CompletedTask;
        }

        public Task CloseVideo()
        {
            if (!_isClosed)
            {
                _isClosed = true;
            }

            return Task.CompletedTask;
        }

        public void ExternalVideoSourceRawSample(uint durationMilliseconds, int width, int height, byte[] sample, VideoPixelFormatsEnum pixelFormat)
        {
            if (!_isClosed)
            {
                if (OnVideoSourceEncodedSample != null)
                {
                    uint fps = (durationMilliseconds > 0) ? 1000 / durationMilliseconds : DEFAULT_FRAMES_PER_SECOND;

                    byte[]? encodedBuffer = _ffmpegEncoder.Encode(FFmpegConvert.GetAVCodecID(_codecManager.SelectedCodec), sample, width, height, (int)fps, _forceKeyFrame);

                    if (encodedBuffer != null)
                    {
                        //Console.WriteLine($"encoded buffer: {encodedBuffer.HexStr()}");
                        uint durationRtpTS = VIDEO_SAMPLING_RATE / fps;

                        // Note the event handler can be removed while the encoding is in progress.
                        OnVideoSourceEncodedSample?.Invoke(durationRtpTS, encodedBuffer);
                    }

                    if (_forceKeyFrame)
                    {
                        _forceKeyFrame = false;
                    }
                }
            }
        }

        public void Dispose()
        {
            _ffmpegEncoder?.Dispose();
        }
    }
}
