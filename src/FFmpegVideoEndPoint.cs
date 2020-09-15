using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using FFmpeg.AutoGen;
using Microsoft.Extensions.Logging;
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
            VideoCodecsEnum.VP8
        };

        private VideoEncoder? _ffmpegEncoder;

        private VideoCodecsEnum _selectedSinkFormat = VideoCodecsEnum.VP8;
        private VideoCodecsEnum _selectedSourceFormat = VideoCodecsEnum.VP8;
        //private bool _isInitialised;
        private bool _isStarted;
        private bool _isClosed;
        private List<VideoCodecsEnum> _supportedCodecs = new List<VideoCodecsEnum>(SupportedCodecs);
        private bool _forceKeyFrame;

        public event VideoSinkSampleDecodedDelegate? OnVideoSinkDecodedSample;
        public event EncodedSampleDelegate? OnVideoSourceEncodedSample;

#pragma warning disable CS0067
        public event RawVideoSampleDelegate? OnVideoSourceRawSample;
        public event SourceErrorDelegate? OnVideoSourceError;
#pragma warning restore CS0067

        public FFmpegVideoEndPoint()
        {

        }

        public MediaEndPoints ToMediaEndPoints()
        {
            return new MediaEndPoints
            {
                VideoSource = this,
                VideoSink = this
            };
        }

        public List<VideoCodecsEnum> GetVideoSinkFormats()
        {
            return _supportedCodecs;
        }

        public void SetVideoSinkFormat(VideoCodecsEnum videoFormat)
        {
            if (videoFormat != VideoCodecsEnum.VP8)
            {
                throw new ApplicationException($"The FFmpeg Video Sink End Point does not support video codec {videoFormat}.");
            }

            _selectedSinkFormat = videoFormat;
        }

        public void GotVideoRtp(IPEndPoint remoteEndPoint, uint ssrc, uint seqnum, uint timestamp, int payloadID, bool marker, byte[] payload)
        {
            throw new ApplicationException("The FFmpeg Video End Point requires full video frames rather than individual RTP packets.");
        }

        public void GotVideoFrame(IPEndPoint remoteEndPoint, uint timestamp, byte[] payload)
        {
            //if (!_isClosed)
            //{
            int width = 0, height = 0;
            List<byte[]> rgbFrames = new List<byte[]>();//_ffmpegEncoder.Decode(_currVideoFrame, _currVideoFramePosn, out width, out height);

            if (rgbFrames == null)
            {
                logger.LogWarning("VPX decode of video sample failed.");
            }
            else
            {
                int w = (int)width;
                int h = (int)height;

                var i420Converter = new VideoFrameConverter(
                    w, h,
                    AVPixelFormat.AV_PIX_FMT_YUV420P,
                    w, h,
                    AVPixelFormat.AV_PIX_FMT_RGB24);

                foreach (var rgbFrame in rgbFrames)
                {
                    OnVideoSinkDecodedSample?.Invoke(rgbFrame, (uint)w, (uint)h, (int)(width * 3), VideoPixelFormatsEnum.Rgb);
                }
            }
            //}
        }

        public void RestrictCodecs(List<VideoCodecsEnum> codecs)
        {
            if (codecs == null || codecs.Count == 0)
            {
                _supportedCodecs = new List<VideoCodecsEnum>(SupportedCodecs);
            }
            else
            {
                _supportedCodecs = new List<VideoCodecsEnum>();
                foreach (var codec in codecs)
                {
                    if (SupportedCodecs.Any(x => x == codec))
                    {
                        _supportedCodecs.Add(codec);
                    }
                    else
                    {
                        logger.LogWarning($"Not including unsupported codec {codec} in filtered list.");
                    }
                }
            }
        }

        public Task PauseVideo()
        {
            throw new NotImplementedException();
        }

        public Task ResumeVideo()
        {
            throw new NotImplementedException();
        }

        public Task StartVideo()
        {
            if(!_isStarted)
            {
                _isStarted = true;
            }

            return Task.CompletedTask;
        }

        public Task CloseVideo()
        {
           if(!_isClosed)
            {
                _isClosed = true;
            }

            return Task.CompletedTask;
        }

        public List<VideoCodecsEnum> GetVideoSourceFormats()
        {
            return _supportedCodecs;
        }

        public void SetVideoSourceFormat(VideoCodecsEnum videoFormat)
        {
            if (videoFormat != VideoCodecsEnum.VP8)
            {
                throw new ApplicationException($"The FFmpeg Video Source End Point does not support video codec {videoFormat}.");
            }

            _selectedSourceFormat = videoFormat;
        }

        public void ExternalVideoSourceRawSample(uint durationMilliseconds, int width, int height, byte[] sample, VideoPixelFormatsEnum pixelFormat)
        {
            if (!_isClosed)
            {
                if (_ffmpegEncoder == null)
                {
                    _ffmpegEncoder = new VideoEncoder(AVCodecID.AV_CODEC_ID_VP8, width, height, DEFAULT_FRAMES_PER_SECOND);
                }

                if (OnVideoSourceEncodedSample != null)
                {
                    byte[]? encodedBuffer = _ffmpegEncoder.Encode(sample);

                    if (encodedBuffer != null)
                    {
                        //Console.WriteLine($"encoded buffer: {encodedBuffer.HexStr()}");
                        uint fps = (durationMilliseconds > 0) ? 1000 / durationMilliseconds : DEFAULT_FRAMES_PER_SECOND;
                        uint durationRtpTS = VIDEO_SAMPLING_RATE / fps;
                        OnVideoSourceEncodedSample.Invoke(durationRtpTS, encodedBuffer);
                    }

                    if (_forceKeyFrame)
                    {
                        _forceKeyFrame = false;
                    }
                }
            }
        }

        public void ForceKeyFrame()
        {
            throw new NotImplementedException();
        }

        public void Dispose()
        {
            _ffmpegEncoder?.Dispose();
        }
    }
}
