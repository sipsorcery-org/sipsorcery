using FFmpeg.AutoGen;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace SIPSorceryMedia.FFmpeg
{
    public class FFmpegVideoDecoder : IDisposable
    {
        private const int DEFAULT_VIDEO_FRAME_RATE = 30;
        private const int AUDIO_OUTPUT_SAMPLE_RATE = 8000;
        private const int MIN_SLEEP_MILLISECONDS = 15;

        private ILogger logger = SIPSorcery.LogFactory.CreateLogger<FFmpegVideoDecoder>();

        unsafe private AVInputFormat* _inputFormat = null;

        unsafe private AVFormatContext* _fmtCtx;
        unsafe private AVCodecContext* _vidDecCtx;
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

        //public unsafe delegate void OnFrameDelegate(ref AVFrame frame);
        public delegate void OnFrameDelegate(ref AVFrame frame);
        public event OnFrameDelegate? OnVideoFrame;

        public event Action? OnEndOfFile;

        public double VideoAverageFrameRate
        {
            get => _videoAvgFrameRate;
        }

        public unsafe FFmpegVideoDecoder(string url, AVInputFormat* inputFormat = null, bool repeat = false, bool isCamera = false)
        {
            _sourceUrl = url;

            _inputFormat = inputFormat;

            _repeat = repeat;

            _isCamera = isCamera;
        }

        public unsafe void InitialiseSource()
        {
            if (!_isInitialised)
            {
                _isInitialised = true;

                _fmtCtx = ffmpeg.avformat_alloc_context();
                _fmtCtx->flags = ffmpeg.AVFMT_FLAG_NONBLOCK;

                var pFormatContext = _fmtCtx;
                ffmpeg.avformat_open_input(&pFormatContext, _sourceUrl, _inputFormat, null).ThrowExceptionIfError();
                ffmpeg.avformat_find_stream_info(_fmtCtx, null).ThrowExceptionIfError();

                ffmpeg.av_dump_format(_fmtCtx, 0, _sourceUrl, 0);

                // Set up video decoder.
                AVCodec* vidCodec = null;
                _videoStreamIndex = ffmpeg.av_find_best_stream(_fmtCtx, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, &vidCodec, 0).ThrowExceptionIfError();
                logger.LogDebug($"FFmpeg file source decoder {ffmpeg.avcodec_get_name(vidCodec->id)} video codec for stream {_videoStreamIndex}.");
                _vidDecCtx = ffmpeg.avcodec_alloc_context3(vidCodec);
                if (_vidDecCtx == null)
                {
                    throw new ApplicationException("Failed to allocate video decoder codec context.");
                }
                ffmpeg.avcodec_parameters_to_context(_vidDecCtx, _fmtCtx->streams[_videoStreamIndex]->codecpar).ThrowExceptionIfError();
                ffmpeg.avcodec_open2(_vidDecCtx, vidCodec, null).ThrowExceptionIfError();

                _videoTimebase = ffmpeg.av_q2d(_fmtCtx->streams[_videoStreamIndex]->time_base);
                _videoAvgFrameRate = ffmpeg.av_q2d(_fmtCtx->streams[_videoStreamIndex]->avg_frame_rate);
                _maxVideoFrameSpace = (int)(_videoAvgFrameRate > 0 ? 1000 / _videoAvgFrameRate : 1000 / DEFAULT_VIDEO_FRAME_RATE);
            }
        }

        public void StartDecode()
        {
            if (!_isStarted)
            {
                _isStarted = true;
                InitialiseSource();
                _sourceTask = Task.Run(RunDecodeLoop);
            }
        }

        public void Pause()
        {
            if (!_isClosed)
            {
                _isPaused = true;
            }
        }

        public async Task Resume()
        {
            if (_isPaused && !_isClosed)
            {
                _isPaused = false;

                if (_sourceTask != null)
                {
                    // Wait for the decode loop to finish in case Pause and Resume are called
                    // in quick succession.
                    await _sourceTask;
                }

                _sourceTask = Task.Run(RunDecodeLoop);
            }
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

        private unsafe void RunDecodeLoop()
        {
            AVPacket* pkt = ffmpeg.av_packet_alloc();
            AVFrame* avFrame = ffmpeg.av_frame_alloc();

            int eagain = ffmpeg.AVERROR(ffmpeg.EAGAIN);
            int error;

            bool canContinue = true;
            bool managePacket = true;

            double firts_dpts = 0;

            try
            {
                // Decode loop.
                ffmpeg.av_init_packet(pkt);
                pkt->data = null;
                pkt->size = 0;

            Repeat:

                DateTime startTime = DateTime.Now;

                while (!_isClosed && !_isPaused && canContinue)
                {
                    error = ffmpeg.av_read_frame(_fmtCtx, pkt);
                    if (error < 0)
                    {
                        managePacket = false;
                        if (error == eagain)
                            ffmpeg.av_packet_unref(pkt);
                        else
                            canContinue = false;
                    }
                    else
                        managePacket = true;

                    if (managePacket)
                    {
                        if (pkt->stream_index == _videoStreamIndex)
                        {
                            ffmpeg.avcodec_send_packet(_vidDecCtx, pkt).ThrowExceptionIfError();

                            int recvRes = ffmpeg.avcodec_receive_frame(_vidDecCtx, avFrame);
                            while (recvRes >= 0)
                            {
                                //Console.WriteLine($"video number samples {frame->nb_samples}, pts={frame->pts}, dts={(int)(_videoTimebase * frame->pts * 1000)}, width {frame->width}, height {frame->height}.");

                                OnVideoFrame?.Invoke(ref *avFrame);

                                if (!_isCamera)
                                {
                                    double dpts = 0;
                                    if (avFrame->pts != ffmpeg.AV_NOPTS_VALUE)
                                    {
                                        dpts = _videoTimebase * avFrame->pts;
                                        if (firts_dpts == 0)
                                            firts_dpts = dpts;

                                        dpts -= firts_dpts;
                                    }

                                    //Console.WriteLine($"Decoded video frame {frame->width}x{frame->height}, ts {frame->best_effort_timestamp}, delta {frame->best_effort_timestamp - prevVidTs}, dpts {dpts}.");

                                    int sleep = (int)(dpts * 1000 - DateTime.Now.Subtract(startTime).TotalMilliseconds);
                                    if (sleep > MIN_SLEEP_MILLISECONDS)
                                    {
                                        ffmpeg.av_usleep((uint)(Math.Min(_maxVideoFrameSpace, sleep) * 1000));
                                    }
                                }

                                recvRes = ffmpeg.avcodec_receive_frame(_vidDecCtx, avFrame);
                            }

                            if (recvRes < 0 && recvRes != ffmpeg.AVERROR(ffmpeg.EAGAIN))
                            {
                                recvRes.ThrowExceptionIfError();
                            }
                        }

                        ffmpeg.av_packet_unref(pkt);
                    }
                }

                if (_isPaused)
                {
                    ((int)ffmpeg.avio_seek(_fmtCtx->pb, 0, ffmpeg.AVIO_SEEKABLE_NORMAL)).ThrowExceptionIfError();

                    ffmpeg.avformat_seek_file(_fmtCtx, _videoStreamIndex, 0, 0, _fmtCtx->streams[_videoStreamIndex]->duration, 0).ThrowExceptionIfError();

                }
                else
                {
                    logger.LogDebug($"FFmpeg end of file for source {_sourceUrl}.");

                    if (!_isClosed && _repeat)
                    {
                        ((int)ffmpeg.avio_seek(_fmtCtx->pb, 0, ffmpeg.AVIO_SEEKABLE_NORMAL)).ThrowExceptionIfError();

                        ffmpeg.avformat_seek_file(_fmtCtx, _videoStreamIndex, 0, 0, _fmtCtx->streams[_videoStreamIndex]->duration, 0).ThrowExceptionIfError();

                        goto Repeat;
                    }
                    else
                    {
                        OnEndOfFile?.Invoke();
                    }
                }
            }
            finally
            {
                ffmpeg.av_frame_unref(avFrame);
                ffmpeg.av_free(avFrame);

                ffmpeg.av_packet_unref(pkt);
                ffmpeg.av_free(pkt);
            }
        }

        public unsafe void Dispose()
        {
            if (_isInitialised && !_isDisposed)
            {
                _isClosed = true;
                _isDisposed = true;

                logger.LogDebug("Disposing of FileSourceDecoder.");

                ffmpeg.avcodec_close(_vidDecCtx);

                var pFormatContext = _fmtCtx;
                ffmpeg.avformat_close_input(&pFormatContext);
            }
        }
    }
}
