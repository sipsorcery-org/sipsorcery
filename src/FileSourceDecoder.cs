using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FFmpeg.AutoGen;
using Microsoft.Extensions.Logging;

namespace SIPSorceryMedia.FFmpeg
{
    public unsafe class FileSourceDecoder : IDisposable
    {
        private ILogger logger = SIPSorcery.LogFactory.CreateLogger<FileSourceDecoder>();

        private AVFormatContext* _fmtCtx;
        private AVCodecContext* _vidDecCtx;
        private int _videoStreamIndex;
        private double _videoTimebase;
        private AVCodecContext* _audDecCtx;
        private int _audioStreamIndex;

        private string _sourcePath;
        private bool _isStarted;
        private Task? _sourceTask;

        //public event Action<byte[]> OnEncodedPacket;
        public event Action<byte[], int, int> OnVideoFrame;
        public event Action<byte[]> OnAudioFrame;

        public FileSourceDecoder(string path)
        {
            if (!File.Exists(path))
            {
                throw new ApplicationException($"Source file for FFmpeg file source decoder could not be found {path}.");
            }

            _sourcePath = path;
        }

        public void InitialiseSource()
        {
            _fmtCtx = ffmpeg.avformat_alloc_context();

            var pFormatContext = _fmtCtx;
            ffmpeg.avformat_open_input(&pFormatContext, _sourcePath, null, null).ThrowExceptionIfError();
            ffmpeg.avformat_find_stream_info(_fmtCtx, null).ThrowExceptionIfError();

            ffmpeg.av_dump_format(_fmtCtx, 0, _sourcePath, 0);

            // Set up video decoder.
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

            // Set up audio decoder.
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

            _videoTimebase = ffmpeg.av_q2d(_fmtCtx->streams[_videoStreamIndex]->time_base);
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

        private void RunDecodeLoop()
        {
            AVPacket* pkt = ffmpeg.av_packet_alloc();
            AVFrame* frame = ffmpeg.av_frame_alloc();

            try
            {
                // Decode loop.
                ffmpeg.av_init_packet(pkt);
                pkt->data = null;
                pkt->size = 0;

                long prevVidTs = 0;
                long prevAudTs = 0;

                DateTime startTime = DateTime.Now;

                while (ffmpeg.av_read_frame(_fmtCtx, pkt) >= 0)
                {
                    if (pkt->stream_index == _videoStreamIndex)
                    {
                        //Console.WriteLine($"video {pkt->pts}, size {pkt->size}.");

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
                            OnVideoFrame?.Invoke(GetBuffer(frame), frame->width, frame->height);

                            double dpts = 0;
                            if (frame->pts != ffmpeg.AV_NOPTS_VALUE)
                            {
                                dpts = _videoTimebase * frame->pts;
                            }

                            //Console.WriteLine($"Decoded video frame {frame->width}x{frame->height}, ts {frame->best_effort_timestamp}, delta {frame->best_effort_timestamp - prevVidTs}, dpts {dpts}.");
                            prevVidTs = frame->best_effort_timestamp;

                            int sleep = (int)(dpts * 1000 - DateTime.Now.Subtract(startTime).TotalMilliseconds);
                            //Console.WriteLine($"sleep {sleep}.");
                            if (sleep > 0)
                            {
                                Thread.Sleep((int)sleep);
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
                        //Console.WriteLine($"audio {pkt->pts}.");

                        ffmpeg.avcodec_send_packet(_audDecCtx, pkt).ThrowExceptionIfError();
                        int recvRes = ffmpeg.avcodec_receive_frame(_audDecCtx, frame);
                        while (recvRes >= 0)
                        {
                            int bufferSize = ffmpeg.av_samples_get_buffer_size(null, frame->channels, frame->nb_samples, (AVSampleFormat)frame->format, 1);
                            byte[] buffer = new byte[bufferSize];

                            fixed (byte* pBuffer = buffer)
                            {
                                byte* pData = frame->data[0];
                                ffmpeg.av_samples_copy(&pBuffer, &pData, 0, 0, frame->nb_samples, frame->channels, (AVSampleFormat)frame->format);
                            }

                            OnAudioFrame?.Invoke(buffer);

                            //Console.WriteLine($"Decoded audio frame samples {frame->nb_samples}, ts {frame->best_effort_timestamp}, delta {frame->best_effort_timestamp - prevAudTs}.");
                            //prevAudTs = frame->best_effort_timestamp;
                            recvRes = ffmpeg.avcodec_receive_frame(_vidDecCtx, frame);
                        }

                        if (recvRes < 0 && recvRes != ffmpeg.AVERROR(ffmpeg.EAGAIN))
                        {
                            recvRes.ThrowExceptionIfError();
                        }
                    }

                    ffmpeg.av_packet_unref(pkt);
                }

                logger.LogDebug($"FFmpeg end of file for source {_sourcePath}.");
            }
            finally
            {
                ffmpeg.av_frame_unref(frame);
                ffmpeg.av_free(frame);

                ffmpeg.av_packet_unref(pkt);
                ffmpeg.av_free(pkt);
            }
        }

        private byte[] GetBuffer(AVFrame* frame)
        {
            int outputBufferSize = ffmpeg.av_image_get_buffer_size((AVPixelFormat)frame->format, frame->width, frame->height, 1);
            byte[] buffer = new byte[outputBufferSize];

            byte_ptrArray4 data = new byte_ptrArray4();
            data.UpdateFrom(frame->data.ToArray());
            int_array4 lineSz = new int_array4();
            lineSz.UpdateFrom(frame->linesize.ToArray());

            fixed (byte* pBuffer = buffer)
            {
                ffmpeg.av_image_copy_to_buffer(pBuffer, buffer.Length, data, lineSz, (AVPixelFormat)frame->format, frame->width, frame->height, 1).ThrowExceptionIfError();
            }

            return buffer;
        }

        public void Dispose()
        {
            ffmpeg.avcodec_close(_vidDecCtx);
            ffmpeg.avcodec_close(_audDecCtx);
            var pFormatContext = _fmtCtx;
            ffmpeg.avformat_close_input(&pFormatContext);
        }
    }
}
