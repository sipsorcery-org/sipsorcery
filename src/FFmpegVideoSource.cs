using FFmpeg.AutoGen;
using Microsoft.Extensions.Logging;
using SIPSorceryMedia.Abstractions;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SIPSorceryMedia.FFmpeg
{
    public class FFmpegVideoSource: IVideoSource, IDisposable
    {
        private static ILogger logger = SIPSorcery.LogFactory.CreateLogger<FFmpegVideoSource>();

        internal static List<VideoFormat> _supportedVideoFormats = Helper.GetSupportedVideoFormats();

        internal bool _isStarted;
        internal bool _isPaused;
        internal bool _isClosed;

        internal FFmpegVideoDecoder ? _videoDecoder;

        internal VideoFrameConverter? _videoFrameYUV420PConverter = null;
        internal VideoFrameConverter? _videoFrameBGR24Converter = null;

        internal FFmpegVideoEncoder _videoEncoder;
        internal bool _forceKeyFrame;

        internal MediaFormatManager<VideoFormat> _videoFormatManager;

        public event EncodedSampleDelegate? OnVideoSourceEncodedSample;
        public event RawVideoSampleDelegate? OnVideoSourceRawSample;

#pragma warning disable CS0067
        public event SourceErrorDelegate? OnVideoSourceError;
#pragma warning restore CS0067
        public event Action? OnEndOfFile;

        public FFmpegVideoSource()
        {
            _videoFormatManager = new MediaFormatManager<VideoFormat>(_supportedVideoFormats);
            _videoEncoder = new FFmpegVideoEncoder();
        }

        public unsafe void CreateVideoDecoder(String path, AVInputFormat* avInputFormat, bool repeat = false, bool isCamera = false)
        {
            _videoDecoder = new FFmpegVideoDecoder(path, avInputFormat, repeat, isCamera);
            _videoDecoder.OnVideoFrame += VideoDecoder_OnVideoFrame;

            _videoDecoder.OnEndOfFile += () =>
            {
                logger.LogDebug($"File source decode complete for {path}.");
                OnEndOfFile?.Invoke();
                _videoDecoder.Dispose();
            };
        }

        public void InitialiseDecoder(Dictionary<string, string>? decoderOptions = null)
        {
            _videoDecoder?.InitialiseSource(decoderOptions);
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

        private unsafe void VideoDecoder_OnVideoFrame(ref AVFrame frame)
        {
            if ( (OnVideoSourceEncodedSample != null) || (OnVideoSourceRawSample != null) )
            {
                int frameRate = (int)_videoDecoder.VideoAverageFrameRate;
                frameRate = (frameRate <= 0) ? Helper.DEFAULT_VIDEO_FRAME_RATE : frameRate;
                uint timestampDuration = (uint)(Helper.VIDEO_SAMPLING_RATE / frameRate);

                var width = frame.width;
                var height = frame.height;

                // Manage Raw Sample
                if (OnVideoSourceRawSample != null)
                {
                    if (_videoFrameBGR24Converter == null ||
                        _videoFrameBGR24Converter.SourceWidth != width ||
                        _videoFrameBGR24Converter.SourceHeight != height)
                    {
                        _videoFrameBGR24Converter = new VideoFrameConverter(
                            width, height,
                            (AVPixelFormat)frame.format,
                            width, height,
                            AVPixelFormat.AV_PIX_FMT_BGR24);
                        logger.LogDebug($"BGR24Converter - Source Frame format: [{frame.format}]");
                    }
                    byte[] sampleBGR24 = _videoFrameBGR24Converter.ConvertFrame(ref frame);
                    if (sampleBGR24 != null)
                        OnVideoSourceRawSample?.Invoke(timestampDuration, width, height, sampleBGR24, VideoPixelFormatsEnum.Rgb);
                }

                // Manage Encoded Sample
                if (OnVideoSourceEncodedSample != null)
                { 
                    if (_videoFrameYUV420PConverter == null ||
                        _videoFrameYUV420PConverter.SourceWidth != width ||
                        _videoFrameYUV420PConverter.SourceHeight != height)
                    {
                        _videoFrameYUV420PConverter = new VideoFrameConverter(
                            width, height,
                            (AVPixelFormat)frame.format,
                            width, height,
                            AVPixelFormat.AV_PIX_FMT_YUV420P);
                        logger.LogDebug($"YUV420PConverter - Source Frame format: [{frame.format}]");
                    }
                    byte[] sampleYUV420P = _videoFrameYUV420PConverter.ConvertFrame(ref frame);

                    if (sampleYUV420P != null)
                    {
                        AVCodecID aVCodecId = FFmpegConvert.GetAVCodecID(_videoFormatManager.SelectedFormat.Codec);
                        byte[]? encodedSample = _videoEncoder.Encode(aVCodecId, sampleYUV420P, width, height, frameRate, _forceKeyFrame, AVPixelFormat.AV_PIX_FMT_YUV420P);

                        if (encodedSample != null)
                        {
                            // Note the event handler can be removed while the encoding is in progress.
                            OnVideoSourceEncodedSample?.Invoke(timestampDuration, encodedSample);
                        }
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
