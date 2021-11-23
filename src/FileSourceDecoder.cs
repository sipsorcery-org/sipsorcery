// Audio resampling: https://ffmpeg.org/doxygen/2.5/resampling_audio_8c-example.html#_a21

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FFmpeg.AutoGen;
using Microsoft.Extensions.Logging;

namespace SIPSorceryMedia.FFmpeg
{
    public class FileSourceDecoder : IDisposable
    {
        private const int DEFAULT_VIDEO_FRAME_RATE = 30;
        private const int AUDIO_OUTPUT_SAMPLE_RATE = 8000;
        private const int MIN_SLEEP_MILLISECONDS = 15;

        private ILogger logger = SIPSorcery.LogFactory.CreateLogger<FileSourceDecoder>();

        unsafe private AVFormatContext* _fmtCtx;
        unsafe private AVCodecContext* _vidDecCtx;
        private int _videoStreamIndex;
        private double _videoTimebase;
        private double _videoAvgFrameRate;
        private int _maxVideoFrameSpace;
        unsafe private AVCodecContext* _audDecCtx;
        private int _audioStreamIndex;
        private double _audioTimebase;
        private double _audioAvgFrameRate;
        private int _maxAudioFrameSpace;
        unsafe private SwrContext* _swrContext;

        private bool _useAudio;
        private bool _useVideo;

        private string _sourcePath;
        private bool _repeat;
        private bool _isInitialised;
        private bool _isStarted;
        private bool _isPaused;
        private bool _isClosed;
        private bool _isDisposed;
        private Task? _sourceTask;

        public delegate void OnFrameDelegate(ref AVFrame frame);

        //public event Action<byte[]> OnEncodedPacket;
        //public event Action<byte[], int, int>? OnVideoFrame;
        public event OnFrameDelegate? OnVideoFrame;
        public event Action<byte[]>? OnAudioFrame;
        public event Action? OnEndOfFile;

        public double VideoAverageFrameRate
        {
            get => _videoAvgFrameRate;
        }

        public FileSourceDecoder(string path, bool repeat, bool useVideo = true, bool useAudio = true)
        {
            if (!File.Exists(path))
            {
                throw new ApplicationException($"Source file for FFmpeg file source decoder could not be found {path}.");
            }

            if(!(useAudio || useVideo))
            {
                throw new ApplicationException($"Audio or Video must be used.");
            }

            _useAudio = useAudio;
            _useVideo = useVideo;

            _sourcePath = path;
            _repeat = repeat;

            _videoStreamIndex = -1;
            _audioStreamIndex = -1;

        }

        public unsafe void InitialiseSource()
        {
            if (!_isInitialised)
            {
                _isInitialised = true;

                _fmtCtx = ffmpeg.avformat_alloc_context();

                var pFormatContext = _fmtCtx;
                ffmpeg.avformat_open_input(&pFormatContext, _sourcePath, null, null).ThrowExceptionIfError();
                ffmpeg.avformat_find_stream_info(_fmtCtx, null).ThrowExceptionIfError();

                ffmpeg.av_dump_format(_fmtCtx, 0, _sourcePath, 0);

                // Set up video decoder.
                if (_useVideo)
                {
                    AVCodec* vidCodec = null;
                    _videoStreamIndex = ffmpeg.av_find_best_stream(_fmtCtx, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, &vidCodec, 0).ThrowExceptionIfError();
                    logger.LogDebug($"FFmpeg file source decoder {ffmpeg.avcodec_get_name(vidCodec->id)} video codec for stream {_videoStreamIndex}.");
                    //vidParser = ffmpeg.av_parser_init((int)vidCodec->id);
                    _vidDecCtx = ffmpeg.avcodec_alloc_context3(vidCodec);
                    if (_vidDecCtx == null)
                    {
                        throw new ApplicationException("Failed to allocate video decoder codec context.");
                    }
                    ffmpeg.avcodec_parameters_to_context(_vidDecCtx, _fmtCtx->streams[_videoStreamIndex]->codecpar).ThrowExceptionIfError();
                    ffmpeg.avcodec_open2(_vidDecCtx, vidCodec, null).ThrowExceptionIfError();
                }

                // Set up audio decoder.
                if (_useAudio)
                {
                    AVCodec* audCodec = null;
                    _audioStreamIndex = ffmpeg.av_find_best_stream(_fmtCtx, AVMediaType.AVMEDIA_TYPE_AUDIO, -1, _videoStreamIndex, &audCodec, 0).ThrowExceptionIfError();
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
                }

                if(_useVideo)
                {
                    _videoTimebase = ffmpeg.av_q2d(_fmtCtx->streams[_videoStreamIndex]->time_base);
                    _videoAvgFrameRate = ffmpeg.av_q2d(_fmtCtx->streams[_videoStreamIndex]->avg_frame_rate);
                    _maxVideoFrameSpace = (int)(_videoAvgFrameRate > 0 ? 1000 / _videoAvgFrameRate : 1000 / DEFAULT_VIDEO_FRAME_RATE);
                }

                if (_useAudio)
                {
                    _audioTimebase = ffmpeg.av_q2d(_fmtCtx->streams[_audioStreamIndex]->time_base);
                    _audioAvgFrameRate = ffmpeg.av_q2d(_fmtCtx->streams[_audioStreamIndex]->avg_frame_rate);
                    _maxAudioFrameSpace = (int)(_audioAvgFrameRate > 0 ? 1000 / _audioAvgFrameRate : 1000 / DEFAULT_VIDEO_FRAME_RATE);
                }
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
                    if (pkt->stream_index == _videoStreamIndex)
                    {
                        // TODO: Workout how to pass through compatible encoded frames without needing to decode and re-encode.

                        //byte[] arr = new byte[pkt->size];
                        //Marshal.Copy((IntPtr)pkt->data, arr, 0, pkt->size);

                        //OnEncodedPacket(arr);

                        //double dpts = 0;
                        //if (pkt->pts != ffmpeg.AV_NOPTS_VALUE)
                        //{
                        //    dpts = _videoTimebase * pkt->pts;
                        //}

                        ////Console.WriteLine($"Decoded video frame {frame->width}x{frame->height}, ts {frame->best_effort_timestamp}, delta {frame->best_effort_timestamp - prevVidTs}, dpts {dpts}.");
                        ////prevVidTs = _frame->best_effort_timestamp;

                        //int sleep = (int)(dpts * 1000 - DateTime.Now.Subtract(startTime).TotalMilliseconds);
                        //Console.WriteLine($"sleep {sleep}.");
                        //if (sleep > 0)
                        //{
                        //    Thread.Sleep((int)sleep);
                        //}

                        //Console.WriteLine($"video {pkt->pts}, size {pkt->size}.");

                        //int pos = 0;
                        //int parseRes = ffmpeg.av_parser_parse2(vidParser, vidDecCtx, &pktParsed->data, &pktParsed->size, pkt->data, pkt->size, pkt->pts, pkt->dts, pos);
                        //Console.WriteLine($"video parse result {parseRes}.");

                        ffmpeg.avcodec_send_packet(_vidDecCtx, pkt).ThrowExceptionIfError();
                        int recvRes = ffmpeg.avcodec_receive_frame(_vidDecCtx, frame);
                        while (recvRes >= 0)
                        {
                            //Console.WriteLine($"video number samples {frame->nb_samples}, pts={frame->pts}, dts={(int)(_videoTimebase * frame->pts * 1000)}, width {frame->width}, height {frame->height}.");

                            OnVideoFrame?.Invoke(ref *frame);

                            double dpts = 0;
                            if (frame->pts != ffmpeg.AV_NOPTS_VALUE)
                            {
                                dpts = _videoTimebase * frame->pts;
                            }

                            //Console.WriteLine($"Decoded video frame {frame->width}x{frame->height}, ts {frame->best_effort_timestamp}, delta {frame->best_effort_timestamp - prevVidTs}, dpts {dpts}.");
                            //prevVidTs = frame->best_effort_timestamp;

                            int sleep = (int)(dpts * 1000 - DateTime.Now.Subtract(startTime).TotalMilliseconds);
                            //Console.WriteLine($"sleep {sleep} {Math.Min(_maxVideoFrameSpace, sleep)}.");
                            if (sleep > MIN_SLEEP_MILLISECONDS)
                            {
                                ffmpeg.av_usleep((uint)(Math.Min(_maxVideoFrameSpace, sleep) * 1000));
                            }

                            recvRes = ffmpeg.avcodec_receive_frame(_vidDecCtx, frame);
                        }

                        if (recvRes < 0 && recvRes != ffmpeg.AVERROR(ffmpeg.EAGAIN))
                        {
                            recvRes.ThrowExceptionIfError();
                        }
                    }
                    else if (pkt->stream_index == _audioStreamIndex)
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
                                //byte* pData = frame->data[0];
                                //ffmpeg.av_samples_copy(&pBuffer, &pData, 0, 0, frame->nb_samples, frame->channels, (AVSampleFormat)frame->format);
                                dstSampleCount = ffmpeg.swr_convert(_swrContext, &pBuffer, bufferSize, frame->extended_data, frame->nb_samples).ThrowExceptionIfError();
                                //Console.WriteLine($"audio convert dst sample count {dstSampleCount}.");
                            }

                            OnAudioFrame?.Invoke(buffer.Take(dstSampleCount * 2).ToArray());

                            if (!_useVideo)
                            {
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
                            else
                            {
                                //Console.WriteLine($"Decoded audio frame samples {frame->nb_samples}, ts {frame->best_effort_timestamp}, delta {frame->best_effort_timestamp - prevAudTs}.");
                                //prevAudTs = frame->best_effort_timestamp;
                                recvRes = ffmpeg.avcodec_receive_frame(_vidDecCtx, frame);
                            }
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

                    if(_useVideo)
                        ffmpeg.avformat_seek_file(_fmtCtx, _videoStreamIndex, 0, 0, _fmtCtx->streams[_videoStreamIndex]->duration, 0).ThrowExceptionIfError();

                    if (_useAudio)
                        ffmpeg.avformat_seek_file(_fmtCtx, _audioStreamIndex, 0, 0, _fmtCtx->streams[_audioStreamIndex]->duration, 0).ThrowExceptionIfError();
                }
                else
                {
                    logger.LogDebug($"FFmpeg end of file for source {_sourcePath}.");

                    if (!_isClosed && _repeat)
                    {
                        ((int)ffmpeg.avio_seek(_fmtCtx->pb, 0, ffmpeg.AVIO_SEEKABLE_NORMAL)).ThrowExceptionIfError();

                        if (_useVideo)
                            ffmpeg.avformat_seek_file(_fmtCtx, _videoStreamIndex, 0, 0, _fmtCtx->streams[_videoStreamIndex]->duration, 0).ThrowExceptionIfError();

                        if (_useAudio)
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

        //private unsafe byte[] GetBuffer(AVFrame frame)
        //{
        //    int outputBufferSize = ffmpeg.av_image_get_buffer_size((AVPixelFormat)frame.format, frame.width, frame.height, 1);
        //    byte[] buffer = new byte[outputBufferSize];

        //    byte_ptrArray4 data = new byte_ptrArray4();
        //    data.UpdateFrom(frame.data.ToArray());
        //    int_array4 lineSz = new int_array4();
        //    lineSz.UpdateFrom(frame.linesize.ToArray());

        //    fixed (byte* pBuffer = buffer)
        //    {
        //        ffmpeg.av_image_copy_to_buffer(pBuffer, buffer.Length, data, lineSz, (AVPixelFormat)frame.format, frame.width, frame.height, 1).ThrowExceptionIfError();
        //    }

        //    return buffer;
        //}

        public unsafe void Dispose()
        {
            if (_isInitialised && !_isDisposed)
            {
                _isClosed = true;
                _isDisposed = true;

                logger.LogDebug("Disposing of FileSourceDecoder.");

                if(_useVideo)
                    ffmpeg.avcodec_close(_vidDecCtx);
                if(_useAudio)
                    ffmpeg.avcodec_close(_audDecCtx);

                var pFormatContext = _fmtCtx;
                ffmpeg.avformat_close_input(&pFormatContext);
                ffmpeg.swr_close(_swrContext);
            }
        }
    }
}
