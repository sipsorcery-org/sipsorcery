// https://ffmpeg.org/doxygen/2.0/doc_2examples_2decoding_encoding_8c-example.html

using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using FFmpeg.AutoGen;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorceryMedia.FFmpeg;

namespace FFmpegMp4Test;

class Program
{
    private const string MP4_FILE_PATH = "max_intro.mp4";

    unsafe static void Main(string[] args)
    {
        Console.WriteLine("FFmpeg Transcode Test");

        var seriLogger = new LoggerConfiguration()
           .Enrich.FromLogContext()
           .MinimumLevel.Is(Serilog.Events.LogEventLevel.Debug)
           .WriteTo.Console()
           .CreateLogger();
        var factory = new SerilogLoggerFactory(seriLogger);

        FFmpegInit.Initialise(FfmpegLogLevelEnum.AV_LOG_VERBOSE, null, factory.CreateLogger<Program>());

        var fmtCtx = ffmpeg.avformat_alloc_context();

        ffmpeg.avformat_open_input(&fmtCtx, MP4_FILE_PATH, null, null).ThrowExceptionIfError();
        ffmpeg.avformat_find_stream_info(fmtCtx, null).ThrowExceptionIfError();
        ffmpeg.av_dump_format(fmtCtx, 0, MP4_FILE_PATH, 0);

        AVCodec* vidCodec = null;
        AVCodecContext* videoDecoderCtx = null;
        double videoTimebase = 0;
        AVPacket* pkt = ffmpeg.av_packet_alloc();
        AVFrame* frame = ffmpeg.av_frame_alloc();

        // Set up video decoder.
        int videoStreamIndex = ffmpeg.av_find_best_stream(fmtCtx, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, &vidCodec, 0).ThrowExceptionIfError();
        Console.WriteLine($"{ffmpeg.avcodec_get_name(vidCodec->id)} video codec for stream {videoStreamIndex}.");
        //vidParser = ffmpeg.av_parser_init((int)vidCodec->id);
        videoDecoderCtx = ffmpeg.avcodec_alloc_context3(vidCodec);
        if (videoDecoderCtx == null)
        {
            throw new ApplicationException("Failed to allocate video decoder codec context.");
        }
        ffmpeg.avcodec_parameters_to_context(videoDecoderCtx, fmtCtx->streams[videoStreamIndex]->codecpar).ThrowExceptionIfError();
        ffmpeg.avcodec_open2(videoDecoderCtx, vidCodec, null).ThrowExceptionIfError();

        videoTimebase = ffmpeg.av_q2d(fmtCtx->streams[videoStreamIndex]->time_base);

        // Set up video encoder. This is for the transcode step where the video frame is re-encoded after being successfully decoded.
        AVCodec* vidEncCodec = ffmpeg.avcodec_find_encoder(AVCodecID.AV_CODEC_ID_VP8);
        if (vidEncCodec == null)
        {
            throw new ApplicationException("Failed to find video encoder codec.");
        }
        Console.WriteLine($"{ffmpeg.avcodec_get_name(vidEncCodec->id)} video encoder codec for transcode.");

        AVCodecContext* videoEncoderCtx = ffmpeg.avcodec_alloc_context3(vidEncCodec);
        if (videoEncoderCtx == null)
        {
            throw new ApplicationException("Failed to allocate video encoder codec context.");
        }
        videoEncoderCtx->width = videoDecoderCtx->width;
        videoEncoderCtx->height = videoDecoderCtx->height;
        videoEncoderCtx->time_base.den = 30;
        videoEncoderCtx->time_base.num = 1;
        videoEncoderCtx->pix_fmt = videoDecoderCtx->pix_fmt;
        videoEncoderCtx->framerate.den = 1;
        videoEncoderCtx->framerate.num = 30;

        ffmpeg.av_opt_set(videoEncoderCtx->priv_data, "quality", "realtime", 0).ThrowExceptionIfError();

        ffmpeg.avcodec_open2(videoEncoderCtx, vidEncCodec, null).ThrowExceptionIfError();

        Console.WriteLine($"Successfully initialised ffmpeg based image encoder: CodecId:[{vidEncCodec->id}] - {videoEncoderCtx->width}:{videoEncoderCtx->height} - {videoEncoderCtx->framerate} Fps");

        // Decode loop.
        //ffmpeg.av_init_packet(pkt);
        pkt->data = null;
        pkt->size = 0;

        //ffmpeg.av_init_packet(pktParsed);
        //pktParsed->data = null;
        //pktParsed->size = 0;

        long prevVidTs = 0;
        DateTime startTime = DateTime.Now;
        int count = 0;

        while (ffmpeg.av_read_frame(fmtCtx, pkt) >= 0)
        {
            if (pkt->stream_index == videoStreamIndex)
            {
                Console.WriteLine($"video {pkt->pts}, size {pkt->size}.");

                //int pos = 0;
                //int parseRes = ffmpeg.av_parser_parse2(vidParser, vidDecCtx, &pktParsed->data, &pktParsed->size, pkt->data, pkt->size, pkt->pts, pkt->dts, pos);
                //Console.WriteLine($"video parse result {parseRes}.");

                ffmpeg.avcodec_send_packet(videoDecoderCtx, pkt).ThrowExceptionIfError();
                int recvRes = ffmpeg.avcodec_receive_frame(videoDecoderCtx, frame);

                while (recvRes >= 0)
                {
                    double dpts = 0;
                    if (frame->pts != ffmpeg.AV_NOPTS_VALUE)
                    {
                        dpts = videoTimebase * frame->pts;
                    }

                    Console.WriteLine($"Decoded video frame {frame->width}x{frame->height}, ts {frame->best_effort_timestamp}, delta {frame->best_effort_timestamp - prevVidTs}, dpts {dpts}.");
                    prevVidTs = frame->best_effort_timestamp;

                    // Encode the frame.
                    var pPacket = ffmpeg.av_packet_alloc();
                    ffmpeg.avcodec_send_frame(videoEncoderCtx, frame).ThrowExceptionIfError();
                    var error = ffmpeg.avcodec_receive_packet(videoEncoderCtx, pPacket);

                    if (error == 0)
                    {
                        byte[] arr = new byte[pPacket->size];
                        Marshal.Copy((IntPtr)pPacket->data, arr, 0, pPacket->size);
                        Console.WriteLine($"VP8 encoded packet size {arr.Length}, sha256: " + Convert.ToBase64String(SHA256.HashData(arr)));
                    }
                    else if (error == ffmpeg.AVERROR(ffmpeg.EAGAIN))
                    {
                        Console.WriteLine("Video encoder needs more data.");
                    }
                    else
                    {
                        error.ThrowExceptionIfError();
                    }

                    ffmpeg.av_packet_unref(pPacket);

                    int sleep = (int)(dpts * 1000 - DateTime.Now.Subtract(startTime).TotalMilliseconds);
                    Console.WriteLine($"sleep {sleep}.");
                    if (sleep > 0)
                    {
                        Thread.Sleep(sleep);
                    }

                    recvRes = ffmpeg.avcodec_receive_frame(videoDecoderCtx, frame);
                }

                if (recvRes < 0 && recvRes != ffmpeg.AVERROR(ffmpeg.EAGAIN))
                {
                    recvRes.ThrowExceptionIfError();
                }
            }

            ffmpeg.av_packet_unref(pkt);

            count++;

            if (count > 10)
            {
                break;
            }
        }

        Console.WriteLine($"Duration {DateTime.Now.Subtract(startTime).TotalSeconds}.");
    }
}
