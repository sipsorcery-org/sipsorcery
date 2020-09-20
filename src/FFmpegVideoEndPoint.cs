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
            VideoCodecsEnum.VP8,
            VideoCodecsEnum.H264
        };

        private VideoEncoder _ffmpegEncoder;

        private VideoCodecsEnum _selectedSinkFormat = VideoCodecsEnum.VP8;
        private VideoCodecsEnum _selectedSourceFormat = VideoCodecsEnum.VP8;
        private bool _isStarted;
        private bool _isPaused;
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

        public List<VideoCodecsEnum> GetVideoSinkFormats()
        {
            return _supportedCodecs;
        }

        public void SetVideoSinkFormat(VideoCodecsEnum videoFormat)
        {
            if (!SupportedCodecs.Any(x => x == videoFormat))
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
            if (!_isClosed)
            {
                AVCodecID codecID = FFmpegConvert.GetAVCodecID(_selectedSinkFormat);

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

        public List<VideoCodecsEnum> GetVideoSourceFormats()
        {
            return _supportedCodecs;
        }

        public void SetVideoSourceFormat(VideoCodecsEnum videoFormat)
        {
            if (!SupportedCodecs.Any(x => x == videoFormat))
            {
                throw new ApplicationException($"The FFmpeg Video Source End Point does not support video codec {videoFormat}.");
            }

            _selectedSourceFormat = videoFormat;
        }

        public void ExternalVideoSourceRawSample(uint durationMilliseconds, int width, int height, byte[] sample, VideoPixelFormatsEnum pixelFormat)
        {
            if (!_isClosed)
            {
                if (OnVideoSourceEncodedSample != null)
                {
                    uint fps = (durationMilliseconds > 0) ? 1000 / durationMilliseconds : DEFAULT_FRAMES_PER_SECOND;

                    byte[]? encodedBuffer = _ffmpegEncoder.Encode(FFmpegConvert.GetAVCodecID(_selectedSourceFormat), sample, width, height, (int)fps, _forceKeyFrame);

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

        public void ForceKeyFrame()
        {
            _forceKeyFrame = true;
        }

        public bool HasEncodedVideoSubscribers()
        {
            return OnVideoSourceEncodedSample != null;
        }

        public bool IsVideoSourcePaused()
        {
            return _isPaused;
        }

        public void Dispose()
        {
            _ffmpegEncoder?.Dispose();
        }
    }
}
