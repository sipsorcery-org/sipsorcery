// https://ffmpeg.org/doxygen/2.0/doc_2examples_2demuxing_8c-example.html

using System;
using System.Threading;
using FFmpeg.AutoGen;

namespace FFmpegMp4Test
{
    class Program
    {
        private const string MP4_FILE_PATH = "max_intro.mp4";

        unsafe static void Main(string[] args)
        {
            Console.WriteLine("FFmpeg MP4 Test");

            FFmpegInit.Initialise(FfmpegLogLevelEnum.AV_LOG_VERBOSE);
            //FFmpegInit.Initialise(FfmpegLogLevelEnum.AV_LOG_TRACE);

            var fmtCtx = ffmpeg.avformat_alloc_context();
            AVCodec* vidCodec = null;
            AVCodecContext* vidDecCtx = null;
            //AVCodecParserContext* vidParser = null;
            double videoTimebase = 0;
            AVCodec* audCodec = null;
            AVCodecContext* audDecCtx = null;
            AVPacket* pkt = ffmpeg.av_packet_alloc();
            //AVPacket* pktParsed = ffmpeg.av_packet_alloc();
            AVFrame* frame = ffmpeg.av_frame_alloc();

            ffmpeg.avformat_open_input(&fmtCtx, MP4_FILE_PATH, null, null).ThrowExceptionIfError();
            ffmpeg.avformat_find_stream_info(fmtCtx, null).ThrowExceptionIfError();

            ffmpeg.av_dump_format(fmtCtx, 0, MP4_FILE_PATH, 0);
            //ffmpeg.av_dump_format(fmtCtx, 1, MP4_FILE_PATH, 0);

            // Set up video decoder.
            int videoStreamIndex = ffmpeg.av_find_best_stream(fmtCtx, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, &vidCodec, 0).ThrowExceptionIfError();
            Console.WriteLine($"{ffmpeg.avcodec_get_name(vidCodec->id)} video codec for stream {videoStreamIndex}.");
            //vidParser = ffmpeg.av_parser_init((int)vidCodec->id);
            vidDecCtx = ffmpeg.avcodec_alloc_context3(vidCodec);
            if (vidDecCtx == null)
            {
                throw new ApplicationException("Failed to allocate video decoder codec context.");
            }
            ffmpeg.avcodec_parameters_to_context(vidDecCtx, fmtCtx->streams[videoStreamIndex]->codecpar).ThrowExceptionIfError();
            ffmpeg.avcodec_open2(vidDecCtx, vidCodec, null).ThrowExceptionIfError();

            // Set up audio decoder.
            int audioStreamIndex = ffmpeg.av_find_best_stream(fmtCtx, AVMediaType.AVMEDIA_TYPE_AUDIO, -1, videoStreamIndex, &audCodec, 0).ThrowExceptionIfError();
            Console.WriteLine($"{ffmpeg.avcodec_get_name(audCodec->id)} audio codec for stream {audioStreamIndex}.");
            audDecCtx = ffmpeg.avcodec_alloc_context3(audCodec);
            if (audDecCtx == null)
            {
                throw new ApplicationException("Failed to allocate audio decoder codec context.");
            }
            ffmpeg.avcodec_parameters_to_context(audDecCtx, fmtCtx->streams[audioStreamIndex]->codecpar).ThrowExceptionIfError();
            ffmpeg.avcodec_open2(audDecCtx, audCodec, null).ThrowExceptionIfError();

            videoTimebase = ffmpeg.av_q2d(fmtCtx->streams[videoStreamIndex]->time_base);

            // Decode loop.
            ffmpeg.av_init_packet(pkt);
            pkt->data = null;
            pkt->size = 0;

            //ffmpeg.av_init_packet(pktParsed);
            //pktParsed->data = null;
            //pktParsed->size = 0;

            long prevVidTs = 0;
            long prevAudTs = 0;

            DateTime startTime = DateTime.Now;

            while (ffmpeg.av_read_frame(fmtCtx, pkt) >= 0)
            {
                if(pkt->stream_index == videoStreamIndex)
                {
                    Console.WriteLine($"video {pkt->pts}, size {pkt->size}.");

                    //int pos = 0;
                    //int parseRes = ffmpeg.av_parser_parse2(vidParser, vidDecCtx, &pktParsed->data, &pktParsed->size, pkt->data, pkt->size, pkt->pts, pkt->dts, pos);
                    //Console.WriteLine($"video parse result {parseRes}.");

                    ffmpeg.avcodec_send_packet(vidDecCtx, pkt).ThrowExceptionIfError();
                    int recvRes = ffmpeg.avcodec_receive_frame(vidDecCtx, frame);
                    while (recvRes >= 0)
                    {
                        double dpts = 0;
                        if (frame->pts != ffmpeg.AV_NOPTS_VALUE)
                        {
                            dpts = videoTimebase * frame->pts;
                        }

                        Console.WriteLine($"Decoded video frame {frame->width}x{frame->height}, ts {frame->best_effort_timestamp}, delta {frame->best_effort_timestamp - prevVidTs}, dpts {dpts}.");
                        prevVidTs = frame->best_effort_timestamp;

                        int sleep = (int)(dpts * 1000 - DateTime.Now.Subtract(startTime).TotalMilliseconds);
                        Console.WriteLine($"sleep {sleep}.");
                        if (sleep > 0)
                        {
                            Thread.Sleep((int)sleep);
                        }

                        recvRes = ffmpeg.avcodec_receive_frame(vidDecCtx, frame);
                    }

                    if (recvRes < 0 && recvRes != ffmpeg.AVERROR(ffmpeg.EAGAIN))
                    {
                        recvRes.ThrowExceptionIfError();
                    }
                }
                else if(pkt->stream_index == audioStreamIndex)
                {
                    Console.WriteLine($"audio {pkt->pts}.");

                    ffmpeg.avcodec_send_packet(audDecCtx, pkt).ThrowExceptionIfError();
                    int recvRes = ffmpeg.avcodec_receive_frame(audDecCtx, frame);
                    while (recvRes >= 0)
                    {
                        Console.WriteLine($"Decoded audio frame samples {frame->nb_samples}, ts {frame->best_effort_timestamp}, delta {frame->best_effort_timestamp - prevAudTs}.");
                        prevAudTs = frame->best_effort_timestamp;
                        recvRes = ffmpeg.avcodec_receive_frame(vidDecCtx, frame);
                    }

                    if (recvRes < 0 && recvRes != ffmpeg.AVERROR(ffmpeg.EAGAIN))
                    {
                        recvRes.ThrowExceptionIfError();
                    }
                }

                ffmpeg.av_packet_unref(pkt);
            }

            Console.WriteLine($"Duration {DateTime.Now.Subtract(startTime).TotalSeconds}.");

            Console.WriteLine("Press any key to exit...");
            Console.ReadLine();
        }
    }
}
