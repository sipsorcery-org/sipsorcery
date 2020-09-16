using System;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;
using Microsoft.Extensions.Logging;

namespace SIPSorceryMedia.FFmpeg
{
    public sealed unsafe class VideoEncoder : IDisposable
    {
        private AVCodec* _codec;
        private AVCodecContext* _encoderContext;
        private AVCodecContext* _decoderContext;
        //private readonly int _frameWidth;
        //private readonly int _frameHeight;
        //private readonly int _framesPerSecond;
        private AVCodecID _codecID;

        private VideoFrameConverter? _rgbToi420;
        private VideoFrameConverter? _i420ToRgb;
        private bool _isEncoderInitialised = false;
        private bool _isDecoderInitialised = false;

        private int _pts = 0;

        private ILogger logger = SIPSorcery.LogFactory.CreateLogger<VideoEncoder>();

        public VideoEncoder()
        {
            //ffmpeg.av_log_set_level(ffmpeg.AV_LOG_VERBOSE);
            ffmpeg.av_log_set_level(ffmpeg.AV_LOG_TRACE);

            //_codecID = codecID;
            //_frameWidth = frameWidth;
            //_frameHeight = frameHeight;
            //_framesPerSecond = framesPerSecond;

            //_codec = ffmpeg.avcodec_find_encoder(codecID);
            //if (_codec == null)
            //{
            //    throw new ApplicationException($"Codec encoder could not be found for {codecID}.");
            //}

            //Console.WriteLine($"H264 encoding profile {_videoCodecContext->profile}.");

            //byte[] optBuffer = new byte[2048];
            //fixed(byte* pOptBuffer = optBuffer)
            //{
            //    ffmpeg.av_opt_get(_videoCodecContext, "profile", 0, &pOptBuffer).ThrowExceptionIfError();
            //    Console.WriteLine($"H264 encoding profile {Marshal.PtrToStringAnsi((IntPtr)pOptBuffer)}.");
            //}
        }

        private void InitialiseEncoder(AVCodecID codecID, int width, int height, int fps)
        {
            if (!_isEncoderInitialised)
            {
                _isEncoderInitialised = true;

                _codecID = codecID;
                _codec = ffmpeg.avcodec_find_encoder(codecID);
                if (_codec == null)
                {
                    throw new ApplicationException($"Codec encoder could not be found for {codecID}.");
                }

                _encoderContext = ffmpeg.avcodec_alloc_context3(_codec);
                if (_encoderContext == null)
                {
                    throw new ApplicationException("Failed to allocate encoder codec context.");
                }

                _encoderContext->width = width;
                _encoderContext->height = height;
                _encoderContext->time_base.den = fps;
                _encoderContext->time_base.num = 1;
                _encoderContext->pix_fmt = AVPixelFormat.AV_PIX_FMT_YUV420P;

                if (_codecID == AVCodecID.AV_CODEC_ID_H264)
                {
                    _encoderContext->gop_size = 30; // Key frame interval.

                    //_videoCodecContext->profile = ffmpeg.FF_PROFILE_H264_CONSTRAINED_BASELINE;
                    ffmpeg.av_opt_set(_encoderContext->priv_data, "profile", "baseline", 0).ThrowExceptionIfError();
                    //ffmpeg.av_opt_set(_videoCodecContext->priv_data, "packetization-mode", "0", 0).ThrowExceptionIfError();
                    //ffmpeg.av_opt_set(_pCodecContext->priv_data, "preset", "veryslow", 0);
                    //ffmpeg.av_opt_set(_videoCodecContext->priv_data, "profile-level-id", "42e01f", 0);
                }

                ffmpeg.avcodec_open2(_encoderContext, _codec, null).ThrowExceptionIfError();

                _rgbToi420 = new VideoFrameConverter(
                    width, height,
                    //AVPixelFormat.AV_PIX_FMT_RGB24,
                    AVPixelFormat.AV_PIX_FMT_BGR24,
                    width, height,
                    AVPixelFormat.AV_PIX_FMT_YUV420P);
            }
        }

        private void InitialiseDecoder(AVCodecID codecID)
        {
            if (!_isDecoderInitialised)
            {
                _isDecoderInitialised = true;

                _codecID = codecID;
                _codec = ffmpeg.avcodec_find_decoder(codecID);
                if (_codec == null)
                {
                    throw new ApplicationException($"Codec encoder could not be found for {codecID}.");
                }

                _decoderContext = ffmpeg.avcodec_alloc_context3(_codec);
                if (_decoderContext == null)
                {
                    throw new ApplicationException("Failed to allocate decoder codec context.");
                }

                //_decoderContext->pix_fmt = AVPixelFormat.AV_PIX_FMT_YUV420P;
                //_decoderContext->time_base.den = fps;
                //_decoderContext->time_base.num = 1;
                //_decoderContext->width = 640;
                //_decoderContext->height = 480;
                //_decoderContext->debug = 1;

                //if (_codecID == AVCodecID.AV_CODEC_ID_H264)
                //{
                //_encoderContext->gop_size = 30; // Key frame interval.

                //_videoCodecContext->profile = ffmpeg.FF_PROFILE_H264_CONSTRAINED_BASELINE;
                //ffmpeg.av_opt_set(_encoderContext->priv_data, "profile", "baseline", 0).ThrowExceptionIfError();
                //ffmpeg.av_opt_set(_videoCodecContext->priv_data, "packetization-mode", "0", 0).ThrowExceptionIfError();
                //ffmpeg.av_opt_set(_pCodecContext->priv_data, "preset", "veryslow", 0);
                //ffmpeg.av_opt_set(_videoCodecContext->priv_data, "profile-level-id", "42e01f", 0);
                //}

                ffmpeg.avcodec_open2(_decoderContext, _codec, null).ThrowExceptionIfError();
            }
        }

        public void Dispose()
        {
            if (_encoderContext != null)
            {
                ffmpeg.avcodec_close(_encoderContext);
                ffmpeg.av_free(_encoderContext);
            }

            ffmpeg.av_free(_codec);
        }

        public string? GetCodecName()
        {
            var namePtr = _codec->name;
            return Marshal.PtrToStringAnsi((IntPtr)namePtr);
        }

        public AVFrame MakeFrame(byte[] i420Buffer, int width, int height)
        {
            AVFrame i420Frame = new AVFrame
            {
                width = width,
                height = height,
                format = (int)AVPixelFormat.AV_PIX_FMT_YUV420P,
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

        public byte[]? Encode(AVCodecID codecID, byte[] rgb, int width, int height, int fps)
        {
            if (!_isEncoderInitialised)
            {
                InitialiseEncoder(codecID, width, height, fps);
            }

            if (_rgbToi420 != null)
            {
                var i420Frame = _rgbToi420.Convert(rgb);
                return Encode(codecID, i420Frame, width, height, fps);
            }
            else
            {
                return null;
            }
        }

        public byte[]? Encode(AVCodecID codecID, AVFrame i420Frame, int width, int height, int fps)
        {
            if (!_isEncoderInitialised)
            {
                InitialiseEncoder(codecID, width, height, fps);
            }

            int _linesizeY = width;
            int _linesizeU = width / 2;
            int _linesizeV = width / 2;

            int _ySize = _linesizeY * height;
            int _uSize = _linesizeU * height / 2;

            if (i420Frame.format != (int)_encoderContext->pix_fmt) throw new ArgumentException("Invalid pixel format.", nameof(i420Frame));
            if (i420Frame.width != width) throw new ArgumentException("Invalid width.", nameof(i420Frame));
            if (i420Frame.height != height) throw new ArgumentException("Invalid height.", nameof(i420Frame));
            if (i420Frame.linesize[0] != _linesizeY) throw new ArgumentException("Invalid Y linesize.", nameof(i420Frame));
            if (i420Frame.linesize[1] != _linesizeU) throw new ArgumentException("Invalid U linesize.", nameof(i420Frame));
            if (i420Frame.linesize[2] != _linesizeV) throw new ArgumentException("Invalid V linesize.", nameof(i420Frame));
            if (i420Frame.data[1] - i420Frame.data[0] != _ySize) throw new ArgumentException("Invalid Y data size.", nameof(i420Frame));
            if (i420Frame.data[2] - i420Frame.data[1] != _uSize) throw new ArgumentException("Invalid U data size.", nameof(i420Frame));

            i420Frame.pts = _pts++;

            var pPacket = ffmpeg.av_packet_alloc();

            try
            {
                ffmpeg.avcodec_send_frame(_encoderContext, &i420Frame).ThrowExceptionIfError();
                int error = ffmpeg.avcodec_receive_packet(_encoderContext, pPacket);

                if (error == 0)
                {
                    if (_codecID == AVCodecID.AV_CODEC_ID_H264)
                    {
                        // TODO: Work out how to use the FFmpeg H264 bit stream parser to extract the NALs.
                        // Currently it's being done in the RTPSession class.
                        byte[] arr = new byte[pPacket->size];
                        Marshal.Copy((IntPtr)pPacket->data, arr, 0, pPacket->size);
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

        public byte[]? Decode(AVCodecID codecID, byte[] buffer, out int width, out int height)
        {
            AVPacket* packet = ffmpeg.av_packet_alloc();
            //ffmpeg.av_init_packet(packet);

            try
            {
                var paddedBuffer = new byte[buffer.Length + ffmpeg.AV_INPUT_BUFFER_PADDING_SIZE];
                Buffer.BlockCopy(buffer, 0, paddedBuffer, 0, buffer.Length);

                fixed (byte* pBuffer = paddedBuffer)
                {
                    ffmpeg.av_packet_from_data(packet, pBuffer, paddedBuffer.Length).ThrowExceptionIfError();

                    //packet->pts = ffmpeg.AV_NOPTS_VALUE;
                    //packet->dts = ffmpeg.AV_NOPTS_VALUE;
                    //packet->duration = 3000;

                    return Decode(codecID, packet, out width, out height);
                }
            }
            finally
            {
                //ffmpeg.av_packet_free(&packet);
            }
        }

        public byte[]? Decode(AVCodecID codecID, AVPacket* packet, out int width, out int height)
        {
            width = 0;
            height = 0;

            if (!_isDecoderInitialised)
            {
                InitialiseDecoder(codecID);
            }

            AVFrame* decodedFrame = ffmpeg.av_frame_alloc();

            try
            {
                ffmpeg.avcodec_send_packet(_decoderContext, packet).ThrowExceptionIfError();

                int recvRes = ffmpeg.avcodec_receive_frame(_decoderContext, decodedFrame);

                if (recvRes == 0)
                {
                    width = decodedFrame->width;
                    height = decodedFrame->height;

                    if (_i420ToRgb == null)
                    {
                        _i420ToRgb = new VideoFrameConverter(
                            width, height,
                            AVPixelFormat.AV_PIX_FMT_YUV420P,
                            width, height,
                            AVPixelFormat.AV_PIX_FMT_RGB24);
                    }

                    return _i420ToRgb.ConvertFrame(decodedFrame);
                }
                else if (recvRes != ffmpeg.AVERROR(ffmpeg.EAGAIN))
                {
                    recvRes.ThrowExceptionIfError();
                }

                return null;
            }
            finally
            {
                ffmpeg.av_frame_free(&decodedFrame);
            }
        }
    }
}
