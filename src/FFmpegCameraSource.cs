using FFmpeg.AutoGen;
using Microsoft.Extensions.Logging;
using SIPSorceryMedia.Abstractions;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace SIPSorceryMedia.FFmpeg
{
    public class FFmpegCameraSource : IVideoSource, IDisposable
    {
        private const int VIDEO_SAMPLING_RATE = 90000;
        private const int DEFAULT_FRAME_RATE = 30;
        private const int VP8_INITIAL_FORMATID = 96;
        private const int H264_INITIAL_FORMATID = 100;

        private static List<VideoFormat> _supportedVideoFormats = new List<VideoFormat>
        {
            new VideoFormat(VideoCodecsEnum.VP8, VP8_INITIAL_FORMATID, VIDEO_SAMPLING_RATE),
            new VideoFormat(VideoCodecsEnum.H264, H264_INITIAL_FORMATID, VIDEO_SAMPLING_RATE)
        };

        public ILogger logger = SIPSorcery.LogFactory.CreateLogger<FFmpegCameraSource>();


        private bool _isStarted;
        private bool _isPaused;
        private bool _isClosed;

        private FFmpegVideoDecoder _videoDecoder;

        private VideoFrameConverter? _videoFrameConverter = null;

        private FFmpegVideoEncoder _videoEncoder;
        private bool _forceKeyFrame;

        private MediaFormatManager<VideoFormat> _videoFormatManager;

        public event EncodedSampleDelegate? OnVideoSourceEncodedSample;

#pragma warning disable CS0067
        public event RawVideoSampleDelegate? OnVideoSourceRawSample;
        public event SourceErrorDelegate? OnVideoSourceError;
#pragma warning restore CS0067

        public event Action? OnEndOfFile;

        public unsafe FFmpegCameraSource(string path)
        {
            string inputFormat = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "dshow"
                : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "v4l2"
                : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "avfoundation"
                : throw new NotSupportedException($"Cannot find adequate input format - OSArchitecture:[{RuntimeInformation.OSArchitecture}] - OSDescription:[{RuntimeInformation.OSDescription}]");

            AVInputFormat* aVInputFormat = ffmpeg.av_find_input_format(inputFormat);

            _videoFormatManager = new MediaFormatManager<VideoFormat>(_supportedVideoFormats);
            _videoEncoder = new FFmpegVideoEncoder();

            _videoDecoder = new FFmpegVideoDecoder(path, aVInputFormat);
            _videoDecoder.OnVideoFrame += FileSourceDecoder_OnVideoFrame;

            _videoDecoder.OnEndOfFile += () =>
            {
                logger.LogDebug($"File source decode complete for {path}.");
                OnEndOfFile?.Invoke();
                _videoDecoder.Dispose();
            };

            Initialise();
        }

        private void Initialise()
        {
            _videoDecoder?.InitialiseSource();
        }
        public bool IsPaused() => _isPaused;

        public List<VideoFormat> GetVideoSourceFormats()
        {
            return _videoFormatManager.GetSourceFormats();
        }

        public void SetVideoSourceFormat(VideoFormat videoFormat)
        {
            logger.LogDebug($"Setting video source format to {videoFormat.FormatID}:{videoFormat.Codec} {videoFormat.ClockRate}.");
            _videoFormatManager.SetSelectedFormat(videoFormat);
        }
        public void RestrictFormats(Func<VideoFormat, bool> filter)
        {
            _videoFormatManager.RestrictFormats(filter);
        }

        public void ForceKeyFrame() => _forceKeyFrame = true;
        public void ExternalVideoSourceRawSample(uint durationMilliseconds, int width, int height, byte[] sample, VideoPixelFormatsEnum pixelFormat) => throw new NotImplementedException();
        public bool HasEncodedVideoSubscribers() => OnVideoSourceEncodedSample != null;
        public bool IsVideoSourcePaused() => _isPaused;
        public Task StartVideo() => Start();
        public Task PauseVideo() => Pause();
        public Task ResumeVideo() => Resume();
        public Task CloseVideo() => Close();

         //private void FileSourceDecdoer_OnVideoFrame(byte[] buffer, int width, int height)
        private unsafe void FileSourceDecoder_OnVideoFrame(ref AVFrame frame)
        {
            if (OnVideoSourceEncodedSample != null)
            {
                int frameRate = (int)_videoDecoder.VideoAverageFrameRate;
                frameRate = (frameRate <= 0) ? DEFAULT_FRAME_RATE : frameRate;
                uint timestampDuration = (uint)(VIDEO_SAMPLING_RATE / frameRate);

                AVCodecID aVCodecId = FFmpegConvert.GetAVCodecID(_videoFormatManager.SelectedFormat.Codec);

                byte[]? encodedSample;
                if (frame.format == (int)AVPixelFormat.AV_PIX_FMT_YUV420P)
                {
                    encodedSample = _videoEncoder.Encode(aVCodecId, frame, frameRate, _forceKeyFrame);
                }
                else
                {
                    var width = frame.width;
                    var height = frame.height;

                    if (_videoFrameConverter == null ||
                        _videoFrameConverter.SourceWidth != width ||
                        _videoFrameConverter.SourceHeight != height)
                    {
                        _videoFrameConverter = new VideoFrameConverter(
                            width, height,
                            (AVPixelFormat)frame.format,
                            width, height,
                            AVPixelFormat.AV_PIX_FMT_YUV420P);
                    }

                    byte[] sample =  _videoFrameConverter.ConvertFrame(ref frame);

                    encodedSample = _videoEncoder.Encode(aVCodecId, sample, width, height, frameRate, _forceKeyFrame);
                }

                if (encodedSample != null)
                {
                    // Note the event handler can be removed while the encoding is in progress.
                    OnVideoSourceEncodedSample?.Invoke(timestampDuration, encodedSample);

                    if (_forceKeyFrame)
                    {
                        _forceKeyFrame = false;
                    }
                }
            }
        }

        public Task Start()
        {
            if (!_isStarted)
            {
                _isStarted = true;
                _videoDecoder.StartDecode();
            }

            return Task.CompletedTask;
        }

        public async Task Close()
        {
            if (!_isClosed)
            {
                _isClosed = true;
                await _videoDecoder.Close();
                Dispose();
            }
        }

        public Task Pause()
        {
            if (!_isPaused)
            {
                _isPaused = true;
                _videoDecoder.Pause();
            }

            return Task.CompletedTask;
        }

        public async Task Resume()
        {
            if (_isPaused && !_isClosed)
            {
                _isPaused = false;
                await _videoDecoder.Resume();
            }
        }

        public void Dispose()
        {
            _videoDecoder?.Dispose();

            _videoEncoder?.Dispose();
        }
    }
}
