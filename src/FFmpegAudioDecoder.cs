// Audio resampling: https://ffmpeg.org/doxygen/2.5/resampling_audio_8c-example.html#_a21

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FFmpeg.AutoGen;
using Microsoft.Extensions.Logging;

namespace SIPSorceryMedia.FFmpeg
{
    public class FFmpegAudioDecoder : IDisposable
    {
        private const int DEFAULT_VIDEO_FRAME_RATE = 30;
        private const int AUDIO_OUTPUT_SAMPLE_RATE = 8000;
        private const int MIN_SLEEP_MILLISECONDS = 15;

        private ILogger logger = SIPSorcery.LogFactory.CreateLogger<FFmpegAudioDecoder>();

        unsafe private AVInputFormat* _inputFormat = null;

        unsafe private AVFormatContext* _fmtCtx;
        unsafe private AVCodecContext* _audDecCtx;
        private int _audioStreamIndex;
        private double _audioTimebase;
        private double _audioAvgFrameRate;
        private int _maxAudioFrameSpace;
        unsafe private SwrContext* _swrContext;

        private string _sourceUrl;
        private bool _repeat;
        private bool _isInitialised;
        private bool _isStarted;
        private bool _isPaused;
        private bool _isClosed;
        private bool _isDisposed;
        private Task? _sourceTask;

        public delegate void OnFrameDelegate(ref AVFrame frame);

        public event Action<byte[]>? OnAudioFrame;
        public event Action? OnEndOfFile;


        public unsafe FFmpegAudioDecoder(string url, AVInputFormat* inputFormat = null, bool repeat = false)
        {
            _sourceUrl = url;

            _inputFormat = inputFormat;

            _repeat = repeat;
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

                // Set up audio decoder.
                AVCodec* audCodec = null;
                _audioStreamIndex = ffmpeg.av_find_best_stream(_fmtCtx, AVMediaType.AVMEDIA_TYPE_AUDIO, -1, -1, &audCodec, 0).ThrowExceptionIfError();
                logger.LogDebug($"FFmpeg file source decoder {ffmpeg.avcodec_get_name(audCodec->id)} audio codec for stream {_audioStreamIndex}.");
                _audDecCtx = ffmpeg.avcodec_alloc_context3(audCodec);
                if (_audDecCtx == null)
                {
                    throw new ApplicationException("Failed to allocate audio decoder codec context.");
                }
                ffmpeg.avcodec_parameters_to_context(_audDecCtx, _fmtCtx->streams[_audioStreamIndex]->codecpar).ThrowExceptionIfError();
                ffmpeg.avcodec_open2(_audDecCtx, audCodec, null).ThrowExceptionIfError();

                // Set up an audio conversion context so that the decoded samples can always be delivered as signed 16 bit mono PCM.
                _swrContext = ffmpeg.swr_alloc();
                ffmpeg.av_opt_set_sample_fmt(_swrContext, "in_sample_fmt", _audDecCtx->sample_fmt, 0);
                ffmpeg.av_opt_set_channel_layout(_swrContext, "in_channel_layout", (long)_audDecCtx->channel_layout, 0);
                ffmpeg.av_opt_set_int(_swrContext, "in_sample_rate", _audDecCtx->sample_rate, 0);

                ffmpeg.av_opt_set_sample_fmt(_swrContext, "out_sample_fmt", AVSampleFormat.AV_SAMPLE_FMT_S16, 0);
                ffmpeg.av_opt_set_channel_layout(_swrContext, "out_channel_layout", ffmpeg.AV_CH_LAYOUT_MONO, 0);
                ffmpeg.av_opt_set_int(_swrContext, "out_sample_rate", AUDIO_OUTPUT_SAMPLE_RATE, 0);
                ffmpeg.swr_init(_swrContext).ThrowExceptionIfError();

               
                _audioTimebase = ffmpeg.av_q2d(_fmtCtx->streams[_audioStreamIndex]->time_base);
                _audioAvgFrameRate = ffmpeg.av_q2d(_fmtCtx->streams[_audioStreamIndex]->avg_frame_rate);
                _maxAudioFrameSpace = (int)(_audioAvgFrameRate > 0 ? 1000 / _audioAvgFrameRate : 1000 / DEFAULT_VIDEO_FRAME_RATE);
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
            AVFrame* frame = ffmpeg.av_frame_alloc();

            try
            {
                // Decode loop.
                ffmpeg.av_init_packet(pkt);
                pkt->data = null;
                pkt->size = 0;

            //long prevVidTs = 0;
            //long prevAudTs = 0;

            Repeat:

                DateTime startTime = DateTime.Now;

                while (!_isClosed && !_isPaused && ffmpeg.av_read_frame(_fmtCtx, pkt) >= 0)
                {
                    if (pkt->stream_index == _audioStreamIndex)
                    {
                        ffmpeg.avcodec_send_packet(_audDecCtx, pkt).ThrowExceptionIfError();
                        int recvRes = ffmpeg.avcodec_receive_frame(_audDecCtx, frame);
                        while (recvRes >= 0)
                        {
                            int numDstSamples = (int)ffmpeg.av_rescale_rnd(ffmpeg.swr_get_delay(_swrContext, _audDecCtx->sample_rate) + frame->nb_samples, AUDIO_OUTPUT_SAMPLE_RATE, _audDecCtx->sample_rate, AVRounding.AV_ROUND_UP);
                            int bufferSize = ffmpeg.av_samples_get_buffer_size(null, 1, numDstSamples, AVSampleFormat.AV_SAMPLE_FMT_S16, 1);

                            //Console.WriteLine($"audio size {bufferSize}, num src samples {frame->nb_samples}, num dst samples {numDstSamples}, pts={frame->pts}, dts={(int)(_audioTimebase * frame->pts * 1000)}.");

                            byte[] buffer = new byte[bufferSize];
                            int dstSampleCount = 0;

                            fixed (byte* pBuffer = buffer)
                            {
                                dstSampleCount = ffmpeg.swr_convert(_swrContext, &pBuffer, bufferSize, frame->extended_data, frame->nb_samples).ThrowExceptionIfError();
                                //Console.WriteLine($"audio convert dst sample count {dstSampleCount}.");
                            }

                            OnAudioFrame?.Invoke(buffer.Take(dstSampleCount * 2).ToArray());

                            double dpts = 0;
                            if (frame->pts != ffmpeg.AV_NOPTS_VALUE)
                            {
                                dpts = _audioTimebase * frame->pts;
                            }
                            int sleep = (int)(dpts * 1000 - DateTime.Now.Subtract(startTime).TotalMilliseconds);
                            //Console.WriteLine($"sleep {sleep} {Math.Min(_maxVideoFrameSpace, sleep)}.");
                            if (sleep > MIN_SLEEP_MILLISECONDS)
                            {
                                ffmpeg.av_usleep((uint)(Math.Min(_maxAudioFrameSpace, sleep) * 1000));
                            }
                            recvRes = ffmpeg.avcodec_receive_frame(_audDecCtx, frame);
                        }

                        if (recvRes < 0 && recvRes != ffmpeg.AVERROR(ffmpeg.EAGAIN))
                        {
                            recvRes.ThrowExceptionIfError();
                        }
                    }

                    ffmpeg.av_packet_unref(pkt);
                }

                if (_isPaused)
                {
                    ((int)ffmpeg.avio_seek(_fmtCtx->pb, 0, ffmpeg.AVIO_SEEKABLE_NORMAL)).ThrowExceptionIfError();

                    ffmpeg.avformat_seek_file(_fmtCtx, _audioStreamIndex, 0, 0, _fmtCtx->streams[_audioStreamIndex]->duration, 0).ThrowExceptionIfError();
                }
                else
                {
                    logger.LogDebug($"FFmpeg end of file for source {_sourceUrl}.");

                    if (!_isClosed && _repeat)
                    {
                        ((int)ffmpeg.avio_seek(_fmtCtx->pb, 0, ffmpeg.AVIO_SEEKABLE_NORMAL)).ThrowExceptionIfError();

                        ffmpeg.avformat_seek_file(_fmtCtx, _audioStreamIndex, 0, 0, _fmtCtx->streams[_audioStreamIndex]->duration, 0).ThrowExceptionIfError();

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
                ffmpeg.av_frame_unref(frame);
                ffmpeg.av_free(frame);

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

                ffmpeg.avcodec_close(_audDecCtx);

                var pFormatContext = _fmtCtx;
                ffmpeg.avformat_close_input(&pFormatContext);
                ffmpeg.swr_close(_swrContext);
            }
        }
    }
}
