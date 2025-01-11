using FFmpeg.AutoGen;
using Microsoft.Extensions.Logging;
using SIPSorceryMedia.Abstractions;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SIPSorceryMedia.FFmpeg
{
    public class FFmpegVideoSource : IVideoSource, IDisposable
    {
        private static ILogger logger = SIPSorcery.LogFactory.CreateLogger<FFmpegVideoSource>();

        internal static List<VideoFormat> _supportedVideoFormats = Helper.GetSupportedVideoFormats();

        internal bool _isStarted;
        internal bool _isPaused;
        internal bool _isClosed;

        private string path;
        private object _initialiseLock = new object();

        internal FFmpegVideoDecoder? _videoDecoder;

        internal VideoFrameConverter? _videoFrameYUV420PConverter = null;
        internal VideoFrameConverter? _videoFrameBGR24Converter = null;

        internal FFmpegVideoEncoder? _videoEncoder;
        internal bool _forceKeyFrame;

        internal MediaFormatManager<VideoFormat> _videoFormatManager;

        public event EncodedSampleDelegate? OnVideoSourceEncodedSample;
        public event RawVideoSampleFasterDelegate? OnVideoSourceRawSampleFaster;

        public event SourceErrorDelegate? OnVideoSourceError;

#pragma warning disable CS0067
        public event RawVideoSampleDelegate? OnVideoSourceRawSample;
#pragma warning restore CS0067

        public FFmpegVideoSource()
        {
            _videoFormatManager = new MediaFormatManager<VideoFormat>(_supportedVideoFormats);
            _videoEncoder = new FFmpegVideoEncoder();
            path = "";
        }

        public unsafe void CreateVideoDecoder(string path, AVInputFormat* avInputFormat, bool repeat = false, bool isCamera = false)
        {
            this.path = path;
            _videoDecoder = new FFmpegVideoDecoder(path, avInputFormat, repeat, isCamera);

            _videoDecoder.OnVideoFrame += VideoDecoder_OnVideoFrame;
            _videoDecoder.OnError += VideoDecoder_OnError;
            _videoDecoder.OnEndOfFile += VideoDecoder_OnEndOfFile;
        }

        private void VideoDecoder_OnEndOfFile()
        {
            VideoDecoder_OnError("End of file");
        }

        private void VideoDecoder_OnError(string errorMessage)
        {
            logger.LogDebug($"Video - Source error for {path} - ErrorMessage:[{errorMessage}]");
            OnVideoSourceError?.Invoke(errorMessage);
            Dispose();
        }

        public Boolean InitialiseDecoder(Dictionary<string, string>? decoderOptions = null)
        {
            return _videoDecoder?.InitialiseSource(decoderOptions) == true;
        }

        public bool IsPaused() => _isPaused;

        public List<VideoFormat> GetVideoSourceFormats()
        {
            return _videoFormatManager.GetSourceFormats();
        }

        public void SetVideoSourceFormat(VideoFormat videoFormat)
        {
            lock (_initialiseLock)
            {
                logger.LogDebug($"{nameof(FFmpegVideoSource)} setting video source format to {videoFormat.FormatID}:{videoFormat.Codec} {videoFormat.ClockRate}.");
                _videoFormatManager.SetSelectedFormat(videoFormat);
                InitialiseDecoder();
            }
        }

        public void RestrictFormats(Func<VideoFormat, bool> filter)
        {
            _videoFormatManager.RestrictFormats(filter);
        }

        public void SetVideoEncoderBitrate(long? avgBitrate = null, int? toleranceBitrate = null, long? minBitrate = null, long? maxBitrate = null)
        {
            if (_videoEncoder != null)
            {
                _videoEncoder.SetBitrate(avgBitrate, toleranceBitrate, minBitrate, maxBitrate);
            }
            else
            {
                throw new NullReferenceException("Video Encoder is not yet initialized.");
            }
        }

        public void ForceKeyFrame() => _forceKeyFrame = true;
        public void ExternalVideoSourceRawSample(uint durationMilliseconds, int width, int height, byte[] sample, VideoPixelFormatsEnum pixelFormat) => throw new NotImplementedException();
        public void ExternalVideoSourceRawSampleFaster(uint durationMilliseconds, RawImage rawImage) => throw new NotImplementedException();
        public bool HasEncodedVideoSubscribers() => OnVideoSourceEncodedSample != null;
        public bool IsVideoSourcePaused() => _isPaused;
        public Task StartVideo() => Start();
        public Task PauseVideo() => Pause();
        public Task ResumeVideo() => Resume();
        public Task CloseVideo() => Close();

        private unsafe void VideoDecoder_OnVideoFrame(AVFrame* frame)
        {
            if (_videoDecoder != null && _videoEncoder != null && (OnVideoSourceEncodedSample != null || OnVideoSourceRawSampleFaster != null))
            {
                int frameRate = (int)_videoDecoder.VideoAverageFrameRate;
                uint timestampDuration = (uint)_videoDecoder.VideoFrameSpace;

                var width = frame->width;
                var height = frame->height;
                AVPixelFormat actualFmt = (AVPixelFormat)frame->format;

                // Manage Raw Sample
                if (OnVideoSourceRawSampleFaster != null)
                {
                    if (_videoFrameBGR24Converter == null ||
                        _videoFrameBGR24Converter.SourceWidth != width ||
                        _videoFrameBGR24Converter.SourceHeight != height ||
                        _videoFrameBGR24Converter.SourcePixelFormat != actualFmt)
                    {
                        lock (_initialiseLock)
                        {
                            _videoFrameBGR24Converter = new VideoFrameConverter(
                                width, 
                                height,
                                actualFmt,
                                width, 
                                height,
                                AVPixelFormat.AV_PIX_FMT_BGR24);
                            logger.LogDebug($"{nameof(FFmpegVideoSource)} RawSampleFaster frame format: [{frame->format}]");
                        }
                    }

                    //var frameBGR24 = _videoFrameBGR24Converter.Convert(frame);
                    //if ((frameBGR24.width != 0) && (frameBGR24.height != 0))
                    //{
                    //    RawImage imageRawSample = new RawImage
                    //    {
                    //        Width = width,
                    //        Height = height,
                    //        Stride = frameBGR24.linesize[0],
                    //        Sample = (IntPtr)frameBGR24.data[0],
                    //        PixelFormat = VideoPixelFormatsEnum.Rgb
                    //    };
                    //    OnVideoSourceRawSampleFaster?.Invoke(timestampDuration, imageRawSample);
                    //}
                }

                // Manage Encoded Sample
                if (OnVideoSourceEncodedSample != null)
                {
                    ////if (actualFmt != AVPixelFormat.AV_PIX_FMT_YUV420P)
                    ////{
                    //    // No need to convert if the frame is already in YUV420P format.

                    //    if (_videoFrameYUV420PConverter == null ||
                    //        _videoFrameYUV420PConverter.SourceWidth != width ||
                    //        _videoFrameYUV420PConverter.SourceHeight != height ||
                    //        _videoFrameYUV420PConverter.SourcePixelFormat != actualFmt)
                    //    {
                    //        lock (_initialiseLock)
                    //        {
                    //            _videoFrameYUV420PConverter = new VideoFrameConverter(
                    //                width, height,
                    //                actualFmt,
                    //                width, height,
                    //                AVPixelFormat.AV_PIX_FMT_YUV420P);
                    //            logger.LogDebug($"{nameof(FFmpegVideoSource)} SourceEncodedSample frame format: [{frame.format}]");
                    //        }
                    //    }
                    ////}

                    ////AVFrame frameYUV420P = actualFmt == AVPixelFormat.AV_PIX_FMT_YUV420P ? frame : _videoFrameYUV420PConverter!.Convert(frame);

                    //AVFrame frameYUV420P = _videoFrameYUV420PConverter!.Convert(frame);

                    var frameYUV420P = frame;

                    if (frameYUV420P->width != 0 && frameYUV420P->height != 0)
                    {
                        AVCodecID? aVCodecId = FFmpegConvert.GetAVCodecID(_videoFormatManager.SelectedFormat.Codec);
                        if (aVCodecId != null)
                        {
                            byte[]? encodedSample = _videoEncoder.Encode(aVCodecId.Value, frameYUV420P, frameRate);

                            if (encodedSample != null)
                            {
                                // Note the event handler can be removed while the encoding is in progress.
                                OnVideoSourceEncodedSample?.Invoke(timestampDuration, encodedSample);
                            }
                            _forceKeyFrame = false;
                        }
                    }
                    else
                    {
                        _forceKeyFrame = true;
                    }
                }
            }
        }

        //private unsafe void VideoDecoder_OnVideoFrame(AVFrame* frame)
        //{
        //    logger.LogDebug($"OnVideoFrame format:[{frame->format}] - {frame->width}:{frame->height}.");

        //    int frameRate = (int)(_videoDecoder?.VideoAverageFrameRate ?? 30);
        //    uint timestampDuration = (uint)(_videoDecoder?.VideoFrameSpace ?? 33);

        //    if (frame->width != 0 && frame->height != 0 && _videoEncoder != null)
        //    {
        //        AVCodecID? aVCodecId = FFmpegConvert.GetAVCodecID(_videoFormatManager.SelectedFormat.Codec);
        //        if (aVCodecId != null)
        //        {
        //            byte[]? encodedSample = _videoEncoder.Encode(aVCodecId.Value, frame, frameRate);

        //            if (encodedSample != null)
        //            {
        //                // Note the event handler can be removed while the encoding is in progress.
        //                OnVideoSourceEncodedSample?.Invoke(timestampDuration, encodedSample);
        //            }
        //            _forceKeyFrame = false;
        //        }
        //    }
        //}

        public Task Start()
        {
            if (!_isStarted)
            {
                if (_videoDecoder != null)
                {
                    _isStarted = true;
                    _videoDecoder?.StartDecode();
                }
            }

            return Task.CompletedTask;
        }

        public async Task Close()
        {
            if (!_isClosed)
            {
                _isClosed = true;
                if (_videoDecoder != null)
                    await _videoDecoder.Close();
                Dispose();
            }
        }

        public Task Pause()
        {
            if (!_isPaused)
            {
                _isPaused = true;
                if (_videoDecoder != null)
                    _videoDecoder?.Pause();
            }

            return Task.CompletedTask;
        }

        public Task Resume()
        {
            if (_isPaused && !_isClosed)
            {
                _isPaused = false;
                if (_videoDecoder != null)
                    _videoDecoder.Resume();
            }
            return Task.CompletedTask;
        }

        public unsafe void Dispose()
        {
            _isStarted = false;

            if (_videoDecoder != null)
            {
                _videoDecoder.OnVideoFrame -= VideoDecoder_OnVideoFrame;
                _videoDecoder.OnError -= VideoDecoder_OnError;
                _videoDecoder.OnEndOfFile -= VideoDecoder_OnEndOfFile;

                _videoDecoder.Dispose();
                _videoDecoder = null;
            }

            if (_videoEncoder != null)
            {
                _videoEncoder.Dispose();
                _videoEncoder = null;
            }

            _videoFrameBGR24Converter?.Dispose();
            _videoFrameYUV420PConverter?.Dispose();
        }

    }
}
