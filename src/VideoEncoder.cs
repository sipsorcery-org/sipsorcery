using System;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SIPSorceryMedia.FFmpeg
{
    public sealed unsafe class VideoEncoder : IDisposable
    {
        private readonly AVCodec* _videoCodec;
        private readonly AVCodecContext* _videoCodecContext;
        private readonly int _frameWidth;
        private readonly int _frameHeight;
        private readonly AVCodecID _codecID;

        private static ILogger logger = NullLogger.Instance;

        public VideoEncoder(AVCodecID codecID, int frameWidth, int frameHeight, int framesPerSecond)
        {
            _codecID = codecID;
            _frameWidth = frameWidth;
            _frameHeight = frameHeight;

            _videoCodec = ffmpeg.avcodec_find_encoder(codecID);
            if (_videoCodec == null)
            {
                throw new ApplicationException($"Codec encoder could not be found for {codecID}.");
            }

            _videoCodecContext = ffmpeg.avcodec_alloc_context3(_videoCodec);
            if (_videoCodecContext == null)
            {
                throw new ApplicationException("Failed to allocated codec context.");
            }

            _videoCodecContext->width = frameWidth;
            _videoCodecContext->height = frameHeight;
            _videoCodecContext->time_base.den = framesPerSecond;
            _videoCodecContext->time_base.num = 1;
            _videoCodecContext->pix_fmt = AVPixelFormat.AV_PIX_FMT_YUV420P;

            if(codecID == AVCodecID.AV_CODEC_ID_H264)
            {
                _videoCodecContext->gop_size = 30; // Key frame interval.

                //_videoCodecContext->profile = ffmpeg.FF_PROFILE_H264_CONSTRAINED_BASELINE;
                ffmpeg.av_opt_set(_videoCodecContext->priv_data, "profile", "baseline", 0).ThrowExceptionIfError();
                //ffmpeg.av_opt_set(_videoCodecContext->priv_data, "packetization-mode", "0", 0).ThrowExceptionIfError();
                //ffmpeg.av_opt_set(_pCodecContext->priv_data, "preset", "veryslow", 0);
                //ffmpeg.av_opt_set(_videoCodecContext->priv_data, "profile-level-id", "42e01f", 0);
            }

            ffmpeg.avcodec_open2(_videoCodecContext, _videoCodec, null).ThrowExceptionIfError();

            //Console.WriteLine($"H264 encoding profile {_videoCodecContext->profile}.");

            //byte[] optBuffer = new byte[2048];
            //fixed(byte* pOptBuffer = optBuffer)
            //{
            //    ffmpeg.av_opt_get(_videoCodecContext, "profile", 0, &pOptBuffer).ThrowExceptionIfError();
            //    Console.WriteLine($"H264 encoding profile {Marshal.PtrToStringAnsi((IntPtr)pOptBuffer)}.");
            //}
        }

        public void Dispose()
        {
            ffmpeg.avcodec_close(_videoCodecContext);
            ffmpeg.av_free(_videoCodecContext);
            ffmpeg.av_free(_videoCodec);
        }

        public string GetCodecName()
        {
            var namePtr = _videoCodec->name;
            return Marshal.PtrToStringAnsi((IntPtr)namePtr);
        }

        public AVFrame MakeFrame(byte[] i420Buffer, int width, int height)
        {
            AVFrame i420Frame = new AVFrame
            {
                width = width,
                height = height,
                format = (int)AVPixelFormat.AV_PIX_FMT_YUV420P
            };

            fixed (byte* pSrcData = i420Buffer)
            {
                var data = new byte_ptrArray4();
                var linesize = new int_array4();

                ffmpeg.av_image_fill_arrays(ref data, ref linesize, pSrcData, AVPixelFormat.AV_PIX_FMT_YUV420P, width, height, 1).ThrowExceptionIfError();

                i420Frame.data.UpdateFrom(data);
                i420Frame.linesize.UpdateFrom(linesize);
            }

            return i420Frame;
        }

        public byte[] Encode(AVFrame i420Frame)
        {
            int _linesizeY = _frameWidth;
            int _linesizeU = _frameWidth / 2;
            int _linesizeV = _frameWidth / 2;

            int _ySize = _linesizeY * _frameHeight;
            int _uSize = _linesizeU * _frameHeight / 2;

            if (i420Frame.format != (int)_videoCodecContext->pix_fmt) throw new ArgumentException("Invalid pixel format.", nameof(i420Frame));
            if (i420Frame.width != _frameWidth) throw new ArgumentException("Invalid width.", nameof(i420Frame));
            if (i420Frame.height != _frameHeight) throw new ArgumentException("Invalid height.", nameof(i420Frame));
            if (i420Frame.linesize[0] != _linesizeY) throw new ArgumentException("Invalid Y linesize.", nameof(i420Frame));
            if (i420Frame.linesize[1] != _linesizeU) throw new ArgumentException("Invalid U linesize.", nameof(i420Frame));
            if (i420Frame.linesize[2] != _linesizeV) throw new ArgumentException("Invalid V linesize.", nameof(i420Frame));
            if (i420Frame.data[1] - i420Frame.data[0] != _ySize) throw new ArgumentException("Invalid Y data size.", nameof(i420Frame));
            if (i420Frame.data[2] - i420Frame.data[1] != _uSize) throw new ArgumentException("Invalid U data size.", nameof(i420Frame));

            var pPacket = ffmpeg.av_packet_alloc();

            try
            {
                ffmpeg.avcodec_send_frame(_videoCodecContext, &i420Frame).ThrowExceptionIfError();
                int error = ffmpeg.avcodec_receive_packet(_videoCodecContext, pPacket);

                if (error == 0)
                {
                    if (_codecID == AVCodecID.AV_CODEC_ID_H264)
                    {
                        // The libx264 logic in ffmpeg writes a 4 byte prefix to indicate the number of NALs that were returned.
                        byte[] arr = new byte[pPacket->size - 4];
                        Marshal.Copy((IntPtr)pPacket->data + 4, arr, 0, pPacket->size - 4);
                        return arr;
                    }
                    else
                    {
                        byte[] arr = new byte[pPacket->size];
                        Marshal.Copy((IntPtr)pPacket->data, arr, 0, pPacket->size);
                        return arr;
                    }
                }
                else if (error == ffmpeg.AVERROR(ffmpeg.EAGAIN))
                {
                    logger.LogDebug("Video encoder needs more data.");
                    return null;
                }
                else
                {
                    error.ThrowExceptionIfError();
                    return null;
                }
            }
            finally
            {
                ffmpeg.av_packet_unref(pPacket);
            }
        }
    }
}
