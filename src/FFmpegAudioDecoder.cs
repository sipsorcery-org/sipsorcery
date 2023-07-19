// Audio resampling: https://ffmpeg.org/doxygen/2.5/resampling_audio_8c-example.html#_a21

using FFmpeg.AutoGen;
using Microsoft.Extensions.Logging;
using SIPSorceryMedia.Abstractions;
using System;
using System.Threading.Tasks;

namespace SIPSorceryMedia.FFmpeg
{
    public class FFmpegAudioDecoder : IDisposable
    {
        private ILogger logger = SIPSorcery.LogFactory.CreateLogger<FFmpegAudioDecoder>();

        unsafe private AVInputFormat* _inputFormat = null;

        unsafe private AVFormatContext* _fmtCtx = null;
        unsafe private AVCodecContext* _audDecCtx = null;
        private int _audioStreamIndex;
        private double _audioTimebase;
        private double _audioAvgFrameRate;
        private int _maxAudioFrameSpace;
        unsafe internal SwrContext* _swrContext;

        private string _sourceUrl;
        private bool _repeat;
        private bool _isMicrophone;
        private bool _isInitialised;
        private bool _isStarted;
        private bool _isPaused;
        private bool _isClosed;
        private bool _isDisposed;
        private Task? _sourceTask;

        public delegate void OnFrameDelegate(ref AVFrame frame);
        public event OnFrameDelegate? OnAudioFrame;

        public event Action? OnEndOfFile;

        public event SourceErrorDelegate? OnError;

        public unsafe FFmpegAudioDecoder(string url, AVInputFormat* inputFormat = null, bool repeat = false, bool isMicrophone = false)
        {
            _sourceUrl = url;
            _inputFormat = inputFormat;
            _repeat = repeat;

            _isMicrophone = isMicrophone;
        }


        private void RaiseError(String err)
        {
            Dispose();
            OnError?.Invoke(err);
        }

        public unsafe Boolean InitialiseSource(int clockRate)
        {
            if (!_isInitialised)
            {
                _isInitialised = true;

                _fmtCtx = ffmpeg.avformat_alloc_context();
                _fmtCtx->flags = ffmpeg.AVFMT_FLAG_NONBLOCK;

                var pFormatContext = _fmtCtx;
                if (ffmpeg.avformat_open_input(&pFormatContext, _sourceUrl, _inputFormat, null) < 0)
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

                // Set up audio decoder.
                AVCodec* audCodec = null;
                _audioStreamIndex = ffmpeg.av_find_best_stream(_fmtCtx, AVMediaType.AVMEDIA_TYPE_AUDIO, -1, -1, &audCodec, 0);
                if (_audioStreamIndex < 0)
                {
                    RaiseError("Cannot get audio stream using specified codec");
                    return false;
                }


                logger.LogDebug($"FFmpeg file source decoder {ffmpeg.avcodec_get_name(audCodec->id)} audio codec for stream {_audioStreamIndex}.");
                _audDecCtx = ffmpeg.avcodec_alloc_context3(audCodec);
                if (_audDecCtx == null)
                {
                    RaiseError("Cannot create audio context");
                    return false;
                }

                if ( ffmpeg.avcodec_parameters_to_context(_audDecCtx, _fmtCtx->streams[_audioStreamIndex]->codecpar) < 0)
                {
                    var pCodecContext = _audDecCtx;
                    ffmpeg.avcodec_free_context(&pCodecContext);
                    _audDecCtx = null;

                    RaiseError("Cannot set parameters in this context");
                    return false;
                }

                if (ffmpeg.avcodec_open2(_audDecCtx, audCodec, null) < 0)
                {
                    var pCodecContext = _audDecCtx;
                    ffmpeg.avcodec_free_context(&pCodecContext);
                    _audDecCtx = null;

                    RaiseError("Cannot open Codec context");
                    return false;
                }

                // Set up an audio conversion context so that the decoded samples can always be delivered as signed 16 bit mono PCM.

                _swrContext = ffmpeg.swr_alloc();
                ffmpeg.av_opt_set_sample_fmt(_swrContext, "in_sample_fmt", _audDecCtx->sample_fmt, 0);
                ffmpeg.av_opt_set_sample_fmt(_swrContext, "out_sample_fmt", AVSampleFormat.AV_SAMPLE_FMT_S16, 0);

                ffmpeg.av_opt_set_int(_swrContext, "in_sample_rate", _audDecCtx->sample_rate, 0);
                ffmpeg.av_opt_set_int(_swrContext, "out_sample_rate", clockRate, 0);

                //FIX:Some Codec's Context Information is missing
                if (_audDecCtx->channel_layout == 0)
                {
                    long in_channel_layout = ffmpeg.av_get_default_channel_layout(_audDecCtx->channels);
                    ffmpeg.av_opt_set_channel_layout(_swrContext, "in_channel_layout", in_channel_layout, 0);
                }
                else
                    ffmpeg.av_opt_set_channel_layout(_swrContext, "in_channel_layout", (long)_audDecCtx->channel_layout, 0);
                ffmpeg.av_opt_set_channel_layout(_swrContext, "out_channel_layout", (long)ffmpeg.AV_CH_LAYOUT_MONO, 0);

                if ( ffmpeg.swr_init(_swrContext) < 0 )
                {
                    RaiseError("Cannot init context with specifiec parameters");
                    return false;
                }

                _audioTimebase = ffmpeg.av_q2d(_fmtCtx->streams[_audioStreamIndex]->time_base);
                _audioAvgFrameRate = ffmpeg.av_q2d(_fmtCtx->streams[_audioStreamIndex]->avg_frame_rate);
                _maxAudioFrameSpace = (int)(_audioAvgFrameRate > 0 ? 1000 / _audioAvgFrameRate : 10000 * clockRate);
            }
            return true;
        }

        public void StartDecode()
        {
            if (!_isStarted)
            {
                _isStarted = true;
                _sourceTask = Task.Run(RunDecodeLoop);
            }
        }

        public Boolean Pause()
        {
            if (!_isClosed)
            {
                _isPaused = true;
            }
            return _isPaused;
        }

        public Boolean Resume()
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

        private unsafe void RunDecodeLoop()
        {
            bool needToRestartAudio = false;
            
            AVPacket* pkt = ffmpeg.av_packet_alloc();
            AVFrame* avFrame = ffmpeg.av_frame_alloc();

            int eagain = ffmpeg.AVERROR(ffmpeg.EAGAIN);
            int error;
            
            bool canContinue = true;
            bool managePacket = true;

            double firts_dpts = 0;

            double original_dpts = 0;

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
                            ffmpeg.av_packet_unref(pkt);
                        else
                            canContinue = false;
                    }
                    else
                        managePacket = true;

                    if (managePacket)
                    {
                        if (pkt->stream_index == _audioStreamIndex)
                        {
                            if (ffmpeg.avcodec_send_packet(_audDecCtx, pkt) < 0)
                            {
                                RaiseError("Cannot suplly packet to decoder");
                                return;
                            }

                            int recvRes = ffmpeg.avcodec_receive_frame(_audDecCtx, avFrame);
                            while (recvRes >= 0)
                            {

                                OnAudioFrame?.Invoke(ref *avFrame);


                                if (!_isMicrophone)
                                {
                                    double dpts = 0;
                                    if (avFrame->pts != ffmpeg.AV_NOPTS_VALUE)
                                    {
                                        dpts = _audioTimebase * avFrame->pts;
                                        original_dpts = dpts;

                                        if (firts_dpts == 0)
                                            firts_dpts = dpts;

                                        dpts -= firts_dpts;
                                    }
                                    int sleep = (int)(dpts * 1000 - DateTime.Now.Subtract(startTime).TotalMilliseconds);
                                    //Console.WriteLine($"sleep {sleep} {Math.Min(_maxAudioFrameSpace, sleep)} - firts_dpts:{firts_dpts} - dpts:{dpts} - original_dpts:{original_dpts}");


                                    if (sleep > Helper.MIN_SLEEP_MILLISECONDS)
                                        ffmpeg.av_usleep((uint)(Math.Min(_maxAudioFrameSpace, sleep) * 1000));
                                }

                                recvRes = ffmpeg.avcodec_receive_frame(_audDecCtx, avFrame);
                            }

                            if (recvRes < 0 && recvRes != ffmpeg.AVERROR(ffmpeg.EAGAIN))
                            {
                                RaiseError("Cannot receive more frame");
                                return;
                            }
                        }

                        ffmpeg.av_packet_unref(pkt);
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

                        if (ffmpeg.avformat_seek_file(_fmtCtx, _audioStreamIndex, 0, 0, _fmtCtx->streams[_audioStreamIndex]->duration, ffmpeg.AVSEEK_FLAG_ANY) < 0)
                        {
                            // We can't easily go back to the beginning of the file ...
                            canContinue = false;
                            needToRestartAudio = true;
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
                ffmpeg.av_frame_unref(avFrame);
                ffmpeg.av_free(avFrame);

                ffmpeg.av_packet_unref(pkt);
                ffmpeg.av_free(pkt);
            }

            if (needToRestartAudio)
            {
                Dispose();
                Task.Run(() => StartDecode());
            }
        }

        public unsafe void Dispose()
        {
            if (_isInitialised && !_isDisposed)
            {
                _isClosed = true;
                _isDisposed = true;
                _isInitialised = false;
                _isStarted = false;

                logger.LogDebug("Disposing of FFmpegAudioDecoder.");
                unsafe
                {
                    try
                    {
                        if (_audDecCtx != null)
                        {
                            var pCodecContext = _audDecCtx;
                            ffmpeg.avcodec_close(pCodecContext);
                            ffmpeg.avcodec_free_context(&pCodecContext);
                            _audDecCtx = null;
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
