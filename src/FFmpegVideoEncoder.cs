using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;
using Microsoft.Extensions.Logging;
using SIPSorceryMedia.Abstractions;

namespace SIPSorceryMedia.FFmpeg
{
    public sealed unsafe class FFmpegVideoEncoder : IVideoEncoder, IDisposable
    {
        public const int DEFAULT_FRAMES_PER_SECOND = 30;
        public const int VP8_FORMATID = 96;
        public const int H264_FORMATID = 100;

        private static readonly List<VideoFormat> _supportedFormats = new List<VideoFormat>
        {
            new VideoFormat(VideoCodecsEnum.VP8, VP8_FORMATID),
            new VideoFormat(VideoCodecsEnum.H264, H264_FORMATID)
        };

        public List<VideoFormat> SupportedFormats
        {
            get => _supportedFormats;
        }

        private readonly Dictionary<string, string> _encoderOptions;

        private AVCodecContext* _encoderContext;
        private AVCodecContext* _decoderContext;
        private AVCodecID _codecID;

        private VideoFrameConverter? _encoderPixelConverter;
        private VideoFrameConverter? _i420ToRgb;
        private bool _isEncoderInitialised = false;
        private bool _isDecoderInitialised = false;
        private Object _encoderLock = new object();
        private Object _decoderLock = new object();

        private bool _forceKeyFrame;
        private int _pts = 0;
        private bool _isDisposed;

        private ILogger logger = SIPSorcery.LogFactory.CreateLogger<FFmpegVideoEncoder>();

        public FFmpegVideoEncoder(Dictionary<string, string>? encoderOptions = null)
        {
            _encoderOptions = encoderOptions ?? new Dictionary<string, string>();
            
            //ffmpeg.av_log_set_level(ffmpeg.AV_LOG_VERBOSE);
            //ffmpeg.av_log_set_level(ffmpeg.AV_LOG_TRACE);
        }

        public byte[] EncodeVideo(int width, int height, byte[] sample, VideoPixelFormatsEnum pixelFormat, VideoCodecsEnum codec)
        {
            AVCodecID codecID = GetAVCodecID(codec);
            if (codecID == AVCodecID.AV_CODEC_ID_NONE)
            {
                throw new NotImplementedException($"Codec {codec} is not supported by the FFmpeg video encoder.");
            }

            //var i420 = PixelConverter.ToI420(width, height, sample, pixelFormat);
            var avPixelFormat = GetAVPixelFormat(pixelFormat);
            if (avPixelFormat == AVPixelFormat.AV_PIX_FMT_NONE)
            {
                throw new NotImplementedException($"No matching FFmpeg pixel format was found for video pixel format {pixelFormat}.");
            }

#pragma warning disable CS8603
            return Encode(codecID, sample, width, height, DEFAULT_FRAMES_PER_SECOND, false, avPixelFormat);
#pragma warning restore
        }

        public void ForceKeyFrame()
        {
            _forceKeyFrame = true;
        }

        public IEnumerable<VideoSample> DecodeVideo(byte[] encodedSample, VideoPixelFormatsEnum pixelFormat, VideoCodecsEnum codec)
        {
            AVCodecID codecID = GetAVCodecID(codec);
            if (codecID == AVCodecID.AV_CODEC_ID_NONE)
            {
                throw new NotImplementedException($"Codec {codec} is not supported by the FFmpeg video decoder.");
            }

            var decodedFrames = Decode(codecID, encodedSample, out int width, out int height);

            if (decodedFrames != null && decodedFrames.Count > 0)
            {
                foreach (var decodedFrame in decodedFrames)
                {
                    yield return new VideoSample { Width = (uint)width, Height = (uint)height, Sample = decodedFrame };
                }
            }
        }

        private AVCodecID GetAVCodecID(VideoCodecsEnum codec)
        {
            AVCodecID codecID = AVCodecID.AV_CODEC_ID_NONE;

            if (codec == VideoCodecsEnum.H264)
            {
                codecID = AVCodecID.AV_CODEC_ID_H264;
            }
            else if (codec == VideoCodecsEnum.VP8)
            {
                codecID = AVCodecID.AV_CODEC_ID_VP8;
            }

            return codecID;
        }

        private AVPixelFormat GetAVPixelFormat(VideoPixelFormatsEnum pixelFormat)
        {
            switch (pixelFormat)
            {
                case VideoPixelFormatsEnum.Bgr:
                    return AVPixelFormat.AV_PIX_FMT_BGR24;
                case VideoPixelFormatsEnum.Bgra:
                    return AVPixelFormat.AV_PIX_FMT_BGRA;
                case VideoPixelFormatsEnum.I420:
                    return AVPixelFormat.AV_PIX_FMT_YUV420P;
                case VideoPixelFormatsEnum.NV12:
                    return AVPixelFormat.AV_PIX_FMT_NV12;
                case VideoPixelFormatsEnum.Rgb:
                    return AVPixelFormat.AV_PIX_FMT_RGB24;
                default:
                    return AVPixelFormat.AV_PIX_FMT_NONE;
            }
        }

        private void InitialiseEncoder(AVCodecID codecID, int width, int height, int fps)
        {
            if (!_isEncoderInitialised)
            {
                _isEncoderInitialised = true;

                _codecID = codecID;
                AVCodec* codec = ffmpeg.avcodec_find_encoder(codecID);
                if (codec == null)
                {
                    throw new ApplicationException($"Codec encoder could not be found for {codecID}.");
                }

                _encoderContext = ffmpeg.avcodec_alloc_context3(codec);
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

                    ffmpeg.av_opt_set(_encoderContext->priv_data, "tune", "zerolatency", 0).ThrowExceptionIfError();
                }
                else if ((_codecID == AVCodecID.AV_CODEC_ID_VP8) || (_codecID == AVCodecID.AV_CODEC_ID_VP9))
                {
                    ffmpeg.av_opt_set(_encoderContext->priv_data, "quality", "realtime", 0).ThrowExceptionIfError();
                }

                foreach (var option in _encoderOptions)
                {
                    ffmpeg.av_opt_set(_encoderContext->priv_data, option.Key, option.Value, 0).ThrowExceptionIfError();
                }

                ffmpeg.avcodec_open2(_encoderContext, codec, null).ThrowExceptionIfError();
            }
        }

        private void InitialiseDecoder(AVCodecID codecID)
        {
            if (!_isDecoderInitialised)
            {
                _isDecoderInitialised = true;

                _codecID = codecID;
                AVCodec* codec = ffmpeg.avcodec_find_decoder(codecID);
                if (codec == null)
                {
                    throw new ApplicationException($"Codec encoder could not be found for {codecID}.");
                }

                _decoderContext = ffmpeg.avcodec_alloc_context3(codec);
                if (_decoderContext == null)
                {
                    throw new ApplicationException("Failed to allocate decoder codec context.");
                }

                ffmpeg.avcodec_open2(_decoderContext, codec, null).ThrowExceptionIfError();
            }
        }

        public string GetCodecName()
        {
            //var namePtr = _codec->name;
            //return Marshal.PtrToStringAnsi((IntPtr)namePtr);
            return ffmpeg.avcodec_get_name(_codecID);
        }

        public AVFrame MakeFrame(byte[] buffer, int width, int height)
        {
            AVFrame avFrame = new AVFrame
            {
                width = width,
                height = height,
                format = (int)AVPixelFormat.AV_PIX_FMT_YUV420P,
            };

            fixed (byte* pSrcData = buffer)
            {
                var data = new byte_ptrArray4();
                var linesize = new int_array4();

                ffmpeg.av_image_fill_arrays(ref data, ref linesize, pSrcData, AVPixelFormat.AV_PIX_FMT_YUV420P, width, height, 1).ThrowExceptionIfError();

                avFrame.data.UpdateFrom(data);
                avFrame.linesize.UpdateFrom(linesize);
            }

            return avFrame;
        }

        public byte[]? Encode(AVCodecID codecID, byte[] sample, int width, int height, int fps, bool keyFrame = false, AVPixelFormat pixelFormat = AVPixelFormat.AV_PIX_FMT_YUV420P)
        {
            lock (_encoderLock)
            {
                if (!_isDisposed)
                {
                    if (!_isEncoderInitialised)
                    {
                        InitialiseEncoder(codecID, width, height, fps);
                    }
                    else if (_encoderContext->width != width || _encoderContext->height != height)
                    {
                        _encoderContext->width = width;
                        _encoderContext->height = height;
                    }

                    AVFrame avFrame = new AVFrame();

                    if (pixelFormat == AVPixelFormat.AV_PIX_FMT_YUV420P)
                    {
                        avFrame = MakeFrame(sample, width, height);
                    }
                    else
                    {
                        if (_encoderPixelConverter == null ||
                           _encoderPixelConverter.SourceWidth != width ||
                           _encoderPixelConverter.SourceHeight != height)
                        {
                            _encoderPixelConverter = new VideoFrameConverter(
                               width, height,
                               pixelFormat,
                               width, height,
                               AVPixelFormat.AV_PIX_FMT_YUV420P);
                        }

                        avFrame = _encoderPixelConverter.Convert(sample);
                    }

                    return Encode(codecID, avFrame, fps, keyFrame);
                }
                else
                {
                    return null;
                }
            }
        }

        public byte[]? Encode(AVCodecID codecID, AVFrame avFrame, int fps, bool keyFrame = false)
        {
            if (!_isDisposed)
            {
                lock (_encoderLock)
                {
                    int width = avFrame.width;
                    int height = avFrame.height;

                    if (!_isEncoderInitialised)
                    {
                        InitialiseEncoder(codecID, width, height, fps);
                    }

                    int _linesizeY = width;
                    int _linesizeU = width / 2;
                    int _linesizeV = width / 2;

                    int _ySize = _linesizeY * height;
                    int _uSize = _linesizeU * height / 2;

                    if (avFrame.format != (int)_encoderContext->pix_fmt) throw new ArgumentException("Invalid pixel format.", nameof(avFrame));
                    if (avFrame.width != width) throw new ArgumentException("Invalid width.", nameof(avFrame));
                    if (avFrame.height != height) throw new ArgumentException("Invalid height.", nameof(avFrame));
                    if (avFrame.linesize[0] < _linesizeY) throw new ArgumentException("Invalid Y linesize.", nameof(avFrame));
                    if (avFrame.linesize[1] < _linesizeU) throw new ArgumentException("Invalid U linesize.", nameof(avFrame));
                    if (avFrame.linesize[2] < _linesizeV) throw new ArgumentException("Invalid V linesize.", nameof(avFrame));

                    if (keyFrame || _forceKeyFrame)
                    {
                        avFrame.key_frame = 1;
                        _forceKeyFrame = false;
                    }

                    avFrame.pts = _pts++;

                    var pPacket = ffmpeg.av_packet_alloc();

                    try
                    {
                        ffmpeg.avcodec_send_frame(_encoderContext, &avFrame).ThrowExceptionIfError();
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
            }
            else
            {
                return null;
            }
        }

        public List<byte[]>? Decode(AVCodecID codecID, byte[] buffer, out int width, out int height)
        {
            if (!_isDisposed)
            {
                lock (_decoderLock)
                {
                    AVPacket* packet = ffmpeg.av_packet_alloc();

                    try
                    {
                        var paddedBuffer = new byte[buffer.Length + ffmpeg.AV_INPUT_BUFFER_PADDING_SIZE];
                        Buffer.BlockCopy(buffer, 0, paddedBuffer, 0, buffer.Length);

                        fixed (byte* pBuffer = paddedBuffer)
                        {
                            ffmpeg.av_packet_from_data(packet, pBuffer, paddedBuffer.Length).ThrowExceptionIfError();
                            return Decode(codecID, packet, out width, out height);
                        }
                    }
                    finally
                    {
                        ffmpeg.av_packet_from_data(packet, (byte*)IntPtr.Zero, 0);
                        ffmpeg.av_packet_free(&packet);
                    }
                }
            }
            else
            {
                width = height = 0;
                return null;
            }
        }

        private List<byte[]>? Decode(AVCodecID codecID, AVPacket* packet, out int width, out int height)
        {
            if (!_isDisposed)
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
                    List<byte[]> rgbFrames = new List<byte[]>();

                    ffmpeg.avcodec_send_packet(_decoderContext, packet).ThrowExceptionIfError();

                    int recvRes = ffmpeg.avcodec_receive_frame(_decoderContext, decodedFrame);

                    while (recvRes >= 0)
                    {
                        width = decodedFrame->width;
                        height = decodedFrame->height;

                        if (_i420ToRgb == null ||
                            _i420ToRgb.SourceWidth != width ||
                            _i420ToRgb.SourceHeight != height)
                        {
                            _i420ToRgb = new VideoFrameConverter(
                                width, height,
                                AVPixelFormat.AV_PIX_FMT_YUV420P,
                                width, height,
                                AVPixelFormat.AV_PIX_FMT_BGR24);
                        }

                        rgbFrames.Add(_i420ToRgb.ConvertFrame(decodedFrame));

                        recvRes = ffmpeg.avcodec_receive_frame(_decoderContext, decodedFrame);
                    }

                    if (recvRes < 0 && recvRes != ffmpeg.AVERROR(ffmpeg.EAGAIN))
                    {
                        recvRes.ThrowExceptionIfError();
                    }

                    return rgbFrames;
                }
                finally
                {
                    ffmpeg.av_frame_free(&decodedFrame);
                }
            }
            else
            {
                width = height = 0;
                return null;
            }
        }

        public void Dispose()
        {
            logger.LogDebug("VideoEncoder dispose.");

            _isDisposed = true;

            lock (this)
            {
                if (_encoderContext != null)
                {
                    fixed (AVCodecContext** pCtx = &_encoderContext)
                    {
                        ffmpeg.avcodec_free_context(pCtx);
                    }
                }

                if (_decoderContext != null)
                {
                    fixed (AVCodecContext** pCtx = &_decoderContext)
                    {
                        ffmpeg.avcodec_free_context(pCtx);
                    }
                }
            }
        }
    }
}
