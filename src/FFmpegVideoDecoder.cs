using FFmpeg.AutoGen;
using Microsoft.Extensions.Logging;
using SIPSorceryMedia.Abstractions;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SIPSorceryMedia.FFmpeg
{
    public class FFmpegVideoDecoder : IDisposable
    {
        private ILogger logger = SIPSorcery.LogFactory.CreateLogger<FFmpegVideoDecoder>();

        unsafe private AVInputFormat* _inputFormat = null;

        unsafe private AVFormatContext* _fmtCtx = null;
        unsafe private AVCodecContext* _vidDecCtx = null;
        private int _videoStreamIndex;
        private double _videoTimebase;
        private double _videoAvgFrameRate;
        private int _maxVideoFrameSpace;

        private string _sourceUrl;
        private bool _repeat;
        private bool _isInitialised;
        private bool _isCamera;
        private bool _isStarted;
        private bool _isPaused;
        private bool _isClosed;
        private bool _isDisposed;
        private Task? _sourceTask;

        public unsafe delegate void OnFrameDelegate(AVFrame* frame);
        public event OnFrameDelegate? OnVideoFrame;

        public event Action? OnEndOfFile;

        public event SourceErrorDelegate? OnError;

        public double VideoAverageFrameRate
        {
            get => _videoAvgFrameRate;
        }

        public double VideoFrameSpace
        {
            get => _maxVideoFrameSpace;
        }

        public unsafe FFmpegVideoDecoder(string url, AVInputFormat* inputFormat, bool repeat = false, bool isCamera = false)
        {
            _sourceUrl = url;

            _inputFormat = inputFormat;

            _repeat = repeat;

            _isCamera = isCamera;

            _isDisposed = false;
        }

        private void RaiseError(String err)
        {
            Dispose();
            OnError?.Invoke(err);
        }

        public unsafe bool InitialiseSource(Dictionary<string, string>? decoderOptions = null)
        {
            if (!_isInitialised)
            {
                logger.LogDebug($"Initialising FFmpeg video decoder for source {_sourceUrl}.");

                _isInitialised = true;
                _isDisposed = false;

                _fmtCtx = ffmpeg.avformat_alloc_context();
                _fmtCtx->flags = ffmpeg.AVFMT_FLAG_NONBLOCK;

                AVDictionary* options = null;

                if (decoderOptions != null)
                {
                    foreach (String key in decoderOptions.Keys)
                    {
                        if (ffmpeg.av_dict_set(&options, key, decoderOptions[key], 0) < 0)
                            logger.LogWarning($"Cannot set option [{key}]=[{decoderOptions[key]}]");
                    }
                }

                var pFormatContext = _fmtCtx;
                if (ffmpeg.avformat_open_input(&pFormatContext, _sourceUrl, _inputFormat, &options) < 0)
                {
                    ffmpeg.avformat_free_context(pFormatContext);
                    _fmtCtx = null;

                    RaiseError("Cannot open source");
                    return false;
                }

                if (ffmpeg.avformat_find_stream_info(_fmtCtx, null) < 0)
                {
                    RaiseError("Cannot get info from stream");
                    return false;
                }

                ffmpeg.av_dump_format(_fmtCtx, 0, _sourceUrl, 0);

                // Set up video decoder.
                AVCodec* vidCodec = null;
                _videoStreamIndex = ffmpeg.av_find_best_stream(_fmtCtx, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, &vidCodec, 0);
                if (_videoStreamIndex  < 0)
                {
                    RaiseError("Cannot get video stream using specified codec");
                    return false;
                }

                logger.LogDebug($"FFmpeg file source decoder [{ffmpeg.avcodec_get_name(vidCodec->id)}] video codec for stream [{_videoStreamIndex}] - url:[{_sourceUrl}].");
                _vidDecCtx = ffmpeg.avcodec_alloc_context3(vidCodec);
                if (_vidDecCtx == null)
                {
                    RaiseError("Cannot create video context");
                    return false;
                }

                if (ffmpeg.avcodec_parameters_to_context(_vidDecCtx, _fmtCtx->streams[_videoStreamIndex]->codecpar) < 0)
                {
                    var pCodecContext = _vidDecCtx;
                    ffmpeg.avcodec_free_context(&pCodecContext);
                    _vidDecCtx = null;

                    RaiseError("Cannot set parameters in this context");
                    return false;
                }

                if (ffmpeg.avcodec_open2(_vidDecCtx, vidCodec, null) < 0)
                {
                    var pCodecContext = _vidDecCtx;
                    ffmpeg.avcodec_free_context(&pCodecContext);
                    _vidDecCtx = null;

                    RaiseError("Cannot open Codec context");
                    return false;
                }

                _videoTimebase = ffmpeg.av_q2d(_fmtCtx->streams[_videoStreamIndex]->time_base);
                if (Double.IsNaN(_videoTimebase) || (_videoTimebase <= 0))
                    _videoTimebase = 0.001;

                _videoAvgFrameRate = ffmpeg.av_q2d(_fmtCtx->streams[_videoStreamIndex]->avg_frame_rate);
                if (Double.IsNaN(_videoAvgFrameRate) || (_videoAvgFrameRate <= 0))
                    _videoAvgFrameRate = 2;

                _maxVideoFrameSpace = (int)(_videoAvgFrameRate > 0 ? 1000 / _videoAvgFrameRate : 1000 / Helper.DEFAULT_VIDEO_FRAME_RATE);

                logger.LogDebug($"FFmpeg video decoder for source {_sourceUrl} successfully initialised.");
            }

            return true;
        }

        public bool StartDecode()
        {
            if (!_isStarted)
            {
                _isClosed = false;

                if (InitialiseSource())
                {
                    _isStarted = true;
                    _sourceTask = Task.Run(RunDecodeLoop);
                }
            }
            return _isStarted;
        }

        public bool Pause()
        {
            if (!_isClosed)
            {
                _isPaused = true;
            }
            return _isPaused;
        }

        public bool Resume()
        {
            if (_isPaused && !_isClosed)
            {
                _isPaused = false;
            }
            return !_isPaused;
        }

        public async Task Close()
        {
            _isClosed = true;

            if (_sourceTask != null)
            {
                // The decode loop should finish very quickly one the close is signaled.
                // Wait for it to complete in case the native objects need to be cleaned up.
                await _sourceTask;
            }
        }

        private void RunDecodeLoop()
        {
            bool needToRestartVideo = false;
            unsafe
            {
                AVPacket* pkt = null;
                AVFrame* avFrame = ffmpeg.av_frame_alloc();

                int eagain = ffmpeg.AVERROR(ffmpeg.EAGAIN);
                int error;

                bool canContinue = true;
                bool managePacket = true;
                double firts_dpts = 0;

                try
                {
                    // Decode loop.
                    pkt = ffmpeg.av_packet_alloc();

                Repeat:

                    DateTime startTime = DateTime.Now;

                    while (!_isClosed && !_isPaused && canContinue)
                    {
                        error = ffmpeg.av_read_frame(_fmtCtx, pkt);
                        if (error < 0)
                        {
                            managePacket = false;
                            if (error == eagain)
                            {
                                if (pkt != null)
                                {
                                    ffmpeg.av_packet_unref(pkt);
                                }
                            }
                            else
                            {
                                canContinue = false;
                            }
                        }
                        else
                        {
                            managePacket = true;
                        }

                        if (managePacket)
                        {
                            if (pkt->stream_index == _videoStreamIndex)
                            {
                                if (ffmpeg.avcodec_send_packet(_vidDecCtx, pkt) < 0)
                                {
                                    RaiseError("Cannot suplly packet to decoder");
                                    return;
                                }

                                int recvRes = ffmpeg.avcodec_receive_frame(_vidDecCtx, avFrame);
                                while (recvRes >= 0)
                                {
                                    Console.WriteLine($"video number samples {avFrame->nb_samples}, pts={avFrame->pts}, dts={(int)(_videoTimebase * avFrame->pts * 1000)}, width {avFrame->width}, height {avFrame->height}.");

                                    OnVideoFrame?.Invoke(avFrame);

                                    if (!_isCamera)
                                    {
                                        double dpts = 0;
                                        if (avFrame->pts != ffmpeg.AV_NOPTS_VALUE)
                                        {
                                            dpts = _videoTimebase * avFrame->pts;
                                            if (firts_dpts == 0)
                                            {
                                                firts_dpts = dpts;
                                            }

                                            dpts -= firts_dpts;
                                        }

                                        Console.WriteLine($"Decoded video frame {avFrame->width}x{avFrame->height}, ts {avFrame->best_effort_timestamp}, delta {avFrame->best_effort_timestamp - firts_dpts}, dpts {dpts}.");

                                        int sleep = (int)(dpts * 1000 - DateTime.Now.Subtract(startTime).TotalMilliseconds);
                                        if (sleep > Helper.MIN_SLEEP_MILLISECONDS)
                                        {
                                            ffmpeg.av_usleep((uint)(Math.Min(_maxVideoFrameSpace, sleep) * 1000));
                                        }
                                    }

                                    recvRes = ffmpeg.avcodec_receive_frame(_vidDecCtx, avFrame);
                                }

                                if (recvRes < 0 && recvRes != ffmpeg.AVERROR(ffmpeg.EAGAIN))
                                {
                                    RaiseError("Cannot receive more frame");
                                    return;
                                }
                            }

                            if (pkt != null)
                            {
                                ffmpeg.av_packet_unref(pkt);
                            }
                        }
                    }

                    if (_isPaused && !_isClosed)
                    {
                        ffmpeg.av_usleep((uint)(Helper.MIN_SLEEP_MILLISECONDS * 1000));
                        goto Repeat;
                    }
                    else
                    {
                        logger.LogDebug($"FFmpeg end of file for source {_sourceUrl}.");

                        if (!_isClosed && _repeat)
                        {
                            if (ffmpeg.avio_seek(_fmtCtx->pb, 0, ffmpeg.AVIO_SEEKABLE_NORMAL) < 0)
                            {
                                RaiseError("Cannot go to the beginning of the stream");
                                return;
                            }

                            if (ffmpeg.avformat_seek_file(_fmtCtx, _videoStreamIndex, 0, 0, _fmtCtx->streams[_videoStreamIndex]->duration, ffmpeg.AVSEEK_FLAG_ANY) < 0)
                            {
                                // We can't easily go back to the beginning of the file ...
                                canContinue = false;
                                needToRestartVideo = true;
                            }
                            else
                            {
                                canContinue = true;
                                goto Repeat;
                            }
                        }
                        else
                        {
                            OnEndOfFile?.Invoke();
                        }
                    }
                }
                finally
                {
                    //ffmpeg.av_frame_unref(avFrame);
                    //ffmpeg.av_free(avFrame);
                    if (avFrame != null)
                    {
                        ffmpeg.av_frame_free(&avFrame);
                    }

                    //ffmpeg.av_packet_unref(pkt);
                    //ffmpeg.av_free(pkt);
                    if (pkt != null)
                    {
                        ffmpeg.av_packet_free(&pkt);
                    }
                }
            }

            if (needToRestartVideo)
            {
                Dispose();
                Task.Run(StartDecode);
            }
        }

        public void Dispose()
        {
            if (_isInitialised && !_isDisposed)
            {
                _isClosed = true;
                _isDisposed = true;
                _isInitialised = false;
                _isStarted = false;

                logger.LogDebug("Disposing of FFmpegVideoDecoder.");
                unsafe
                {
                    try
                    {
                        if (_vidDecCtx != null)
                        {
                            var pCodecContext = _vidDecCtx;
                            ffmpeg.avcodec_free_context(&pCodecContext);
                            _vidDecCtx = null;
                        }
                    }
                    catch { }

                    try
                    {
                        if (_fmtCtx != null)
                        {
                            var pFormatContext = _fmtCtx;
                            ffmpeg.avformat_close_input(&pFormatContext);
                            ffmpeg.avformat_free_context(pFormatContext);
                            _fmtCtx = null;
                        }
                    }
                    catch { }
                }
            }
        }
    }
}
