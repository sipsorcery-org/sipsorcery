using FFmpeg.AutoGen;
using Microsoft.Extensions.Logging;
using SIPSorceryMedia.Abstractions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace SIPSorceryMedia.FFmpeg
{
    public sealed unsafe class FFmpegVideoEncoder : IVideoEncoder, IDisposable
    {
        private static readonly List<VideoFormat> _supportedFormats = Helper.GetSupportedVideoFormats();

        public List<VideoFormat> SupportedFormats
        {
            get => _supportedFormats;
        }

        private readonly Dictionary<string, string> _codecOptions;
        private Dictionary<string, Dictionary<string, string>>? _codecOptionsByName;

        private Stopwatch _frameTimer;
        public event EventHandler<VideoEncoderStatistics> OnVideoEncoderStatistics;

        private AVCodecContext* _encoderContext;
        private AVCodecContext* _decoderContext;
        private AVCodecID _codecID;
        private AVHWDeviceType _HwDeviceType;
        private AVFrame* _frame;
        private AVFrame* _gpuFrame;

        private AVPixelFormat? _negotiatedPixFmt;

        private VideoFrameConverter? _encoderPixelConverter;
        private VideoFrameConverter? _i420ToRgb;
        private bool _isEncoderInitialised = false;
        private bool _isDecoderInitialised = false;
        private object _encoderLock = new object();
        private object _decoderLock = new object();

        private Dictionary<AVCodecID, IntPtr/* AVCodec* */>? _specificEncoders;
        private string? _wrapName;

        private long? _bit_rate = null;
        private int? _bit_rate_tolerance = null;
        private long? _rc_min_rate = null;
        private long? _rc_max_rate = null;

        private int? _thread_count = null;
        private long _lastFrameTicks = 0;

        private bool _forceKeyFrame;
        private int _pts = 0;
        private bool _isDisposed;

        private ILogger logger = SIPSorcery.LogFactory.CreateLogger<FFmpegVideoEncoder>();

        public FFmpegVideoEncoder(Dictionary<string, string>? encoderOptions = null, AVHWDeviceType HWDeviceType = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE)
        {
            _codecOptions = encoderOptions ?? new Dictionary<string, string>();
            _HwDeviceType = HWDeviceType;
        }

        public byte[]? EncodeVideo(int width, int height, byte[] sample, VideoPixelFormatsEnum pixelFormat, VideoCodecsEnum codec)
        {
            fixed (byte* pSample = sample)
            {
                return EncodeVideo(width, height, pSample, pixelFormat, codec);
            }
        }

        public byte[]? EncodeVideoFaster(RawImage rawImage, VideoCodecsEnum codec)
        {
            byte* pSample = (byte*)rawImage.Sample;
            return EncodeVideo(rawImage.Width, rawImage.Height, pSample, rawImage.PixelFormat, codec);
        }

        private byte[]? EncodeVideo(int width, int height, byte* sample, VideoPixelFormatsEnum pixelFormat, VideoCodecsEnum codec)
        {
            AVCodecID? codecID = FFmpegConvert.GetAVCodecID(codec);

            if (codecID == null)
            {
                throw new NotImplementedException($"Codec {codec} is not supported by the FFmpeg video encoder.");
            }
            var avPixelFormat = GetAVPixelFormat(pixelFormat);
            if (avPixelFormat == AVPixelFormat.AV_PIX_FMT_NONE)
            {
                throw new NotImplementedException($"No matching FFmpeg pixel format was found for video pixel format {pixelFormat}.");
            }

            return Encode(codecID.Value, sample, width, height, Helper.DEFAULT_VIDEO_FRAME_RATE, false, avPixelFormat);
        }

        public void ForceKeyFrame()
        {
            _forceKeyFrame = true;
        }

        public IEnumerable<RawImage> DecodeVideoFaster(byte[] encodedSample, VideoPixelFormatsEnum pixelFormat, VideoCodecsEnum codec)
        {
            AVCodecID? codecID = FFmpegConvert.GetAVCodecID(codec);
            if (codecID == null)
            {
                throw new NotImplementedException($"Codec {codec} is not supported by the FFmpeg video decoder.");
            }

            var decodedFrames = DecodeFaster(codecID.Value, encodedSample, out int width, out int height);
            if (decodedFrames != null && decodedFrames.Count > 0)
                return decodedFrames;

            return new List<RawImage>();
        }

        public IEnumerable<VideoSample> DecodeVideo(byte[] encodedSample, VideoPixelFormatsEnum pixelFormat, VideoCodecsEnum codec)
        {
            var rawImageList = DecodeVideoFaster(encodedSample, pixelFormat, codec);
            foreach (var rawImage in rawImageList)
            {
                yield return new VideoSample { Width = (uint)rawImage.Width, Height = (uint)rawImage.Height, Sample = rawImage.GetBuffer() };
            }
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

        public void SetCodec(string wrapperName)
        {
            _wrapName = wrapperName;

            ResetEncoder();
            ResetDecoder();
        }

        public bool SetCodec(AVCodecID cdc, string name, Dictionary<string, string>? opts = null)
        {
            var codec = ffmpeg.avcodec_find_encoder_by_name(name);

            if (codec == null)
            {
                logger.LogError("Encoder for {id} with name '{name}' is Not Found.", cdc, name);
                return false;
            }

            (_specificEncoders ??= [])[cdc] = (IntPtr)codec;

            if (opts != null)
                (_codecOptionsByName ??= [])[name] = opts;

            return codec != null;
        }

        private AVCodec* GetCodec(AVCodecID codecID, string? wrapName)
        {
            if (wrapName == null)
                return null;

            IntPtr? iterator = null;
            var cdc = ffmpeg.av_codec_iterate((void**)&iterator);
            while (cdc != null)
            {
                if (cdc->id == codecID && GetNameString(cdc->wrapper_name) == wrapName
                    && ffmpeg.av_codec_is_encoder(cdc) != 0)
                    break;

                cdc = ffmpeg.av_codec_iterate((void**)&iterator);
            }

            (_specificEncoders ??= [])[codecID] = (IntPtr)cdc;
            
            if (cdc == null)
                logger.LogWarning("Codec not found for {id} with wrapper {wrap}", codecID, _wrapName);

            return cdc;
        }

        private AVCodec* GetCodec(AVCodecID codecID)
        {
            AVCodec* codec = (AVCodec*)(_specificEncoders?[codecID] ?? IntPtr.Zero);

            if (codec == null)
            {
                codec = GetCodec(codecID, _wrapName);
            }

            if (codec == null)
            {
                codec = ffmpeg.avcodec_find_encoder(codecID);
            }

            return codec;
        }

        private Dictionary<string, string>? GetOptions(string? encName)
            => !string.IsNullOrWhiteSpace(encName) ? _codecOptionsByName?[encName!] ?? _codecOptions : null;

        public void InitialiseEncoder(AVCodecID codecID, int width, int height, int fps)
        {
            if (!_isEncoderInitialised)
            {
                _isEncoderInitialised = true;
                _codecID = codecID;
                
                var codec = GetCodec(codecID);
                var encOpts = GetOptions(GetNameString(codec->name));

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
                _encoderContext->framerate.den = 1;
                _encoderContext->framerate.num = fps;

                _encoderContext->pix_fmt = _negotiatedPixFmt ?? codec->pix_fmts[0];

                if (_bit_rate != null) _encoderContext->bit_rate = (long)_bit_rate;
                if (_bit_rate_tolerance != null) _encoderContext->bit_rate_tolerance = (int)_bit_rate_tolerance;
                if (_rc_min_rate != null) _encoderContext->rc_min_rate = (long)_rc_min_rate;
                if (_rc_max_rate != null) _encoderContext->rc_max_rate = (long)_rc_max_rate;
                if (_thread_count != null) _encoderContext->thread_count = (int)_thread_count;

                // Set Key frame interval
                if (fps < 5)
                    _encoderContext->gop_size = 1;
                else
                    _encoderContext->gop_size = fps;

                // provide tunings for known codecs
                switch (GetNameString(codec->name))
                {
                    case "libx264":
                        ffmpeg.av_opt_set(_encoderContext->priv_data, "profile", "baseline", 0).ThrowExceptionIfError();
                        ffmpeg.av_opt_set(_encoderContext->priv_data, "tune", "zerolatency", 0).ThrowExceptionIfError();
                        break;
                    case "h264_qsv":
                        ffmpeg.av_opt_set(_encoderContext->priv_data, "profile", "66" /* baseline */, 0).ThrowExceptionIfError();
                        ffmpeg.av_opt_set(_encoderContext->priv_data, "preset", "7" /* veryfast */, 0).ThrowExceptionIfError();
                        break;
                    case "libvpx":
                        ffmpeg.av_opt_set(_encoderContext->priv_data, "quality", "realtime", 0).ThrowExceptionIfError();
                        break;
                    default:
                        break;
                }

                foreach (var option in encOpts)
                {
                    try
                    {
                        ffmpeg.av_opt_set(_encoderContext->priv_data, option.Key, option.Value, 0).ThrowExceptionIfError();
                    }
                    catch (Exception excp)
                    {
                        logger.LogWarning("Failed to set encoder option \"{key}\"=\"{val}\", Skipping this option. {msg}", option.Key, option.Value, excp.Message);
                    }
                };

                ffmpeg.avcodec_open2(_encoderContext, codec, null).ThrowExceptionIfError();

                logger.LogDebug("Successfully initialised ffmpeg based image encoder: CodecId:[{id}] - Name:[{name}] - {w}:{h} - {fps} Fps - {fmt}",
                    codecID, GetNameString(codec->name), width, height, fps, _encoderContext->pix_fmt);
            }
        }

        public void SetThreadCount(int? threadCount)
        {
            _thread_count = threadCount;

            ResetEncoder();
        }
        
        public void SetBitrate(long? avgBitrate, int? toleranceBitrate, long? minBitrate, long? maxBitrate)
        {
            _bit_rate = avgBitrate;
            _bit_rate_tolerance = toleranceBitrate;
            _rc_min_rate = minBitrate;
            _rc_max_rate = maxBitrate;

            ResetEncoder();
        }

        private void ResetEncoder()
        {
            // Reset encoder
            lock (_encoderLock)
            {
                if (!_isDisposed && _encoderContext != null && _isEncoderInitialised)
                {
                    _isEncoderInitialised = false;
                    fixed (AVCodecContext** pCtx = &_encoderContext)
                    {
                        ffmpeg.avcodec_free_context(pCtx);
                    }
                    _negotiatedPixFmt = null;
                }
            }
        }

        private void ResetDecoder()
        {
            // Reset decoder
            lock (_decoderLock)
            {
                if (!_isDisposed && _decoderContext != null && _isDecoderInitialised)
                {
                    _isDecoderInitialised = false;
                    fixed (AVCodecContext** pCtx = &_decoderContext)
                    {
                        ffmpeg.avcodec_free_context(pCtx);
                    }
                }
            }
        }

        private void InitialiseDecoder(AVCodecID codecID)
        {
            if (!_isDecoderInitialised)
            {
                _isDecoderInitialised = true;
                _codecID = codecID;

                var codec = GetCodec(codecID);
                var decOpts = GetOptions(GetNameString(codec->name));

                if (codec == null)
                {
                    throw new ApplicationException($"Decoder codec could not be found for {codecID}.");
                }

                _decoderContext = ffmpeg.avcodec_alloc_context3(codec);
                if (_decoderContext == null)
                {
                    throw new ApplicationException("Failed to allocate decoder codec context.");
                }

                if (_HwDeviceType != AVHWDeviceType.AV_HWDEVICE_TYPE_NONE)
                {
                    ffmpeg.av_hwdevice_ctx_create(&_decoderContext->hw_device_ctx, _HwDeviceType, null, null, 0).ThrowExceptionIfError();
                }

                foreach (var option in decOpts)
                {
                    try
                    {
                        ffmpeg.av_opt_set(_encoderContext->priv_data, option.Key, option.Value, 0).ThrowExceptionIfError();
                    }
                    catch (Exception excp)
                    {
                        logger.LogWarning("Failed to set decoder option \"{key}\"=\"{val}\", Skipping this option. {msg}", option.Key, option.Value, excp.Message);
                    }
                };

                ffmpeg.avcodec_open2(_decoderContext, codec, null).ThrowExceptionIfError();

                logger.LogDebug("[InitialiseDecoder] Successfully initialised ffmpeg based image decoder: CodecId:[{id}] - Name:[{name}]",
                    codecID, GetNameString(codec->name));
            }
        }

        public string GetCodecName()
        {
            return ffmpeg.avcodec_get_name(_codecID);
        }

        public AVFrame MakeFrame(byte* sample, int width, int height)
        {
            AVFrame avFrame = new AVFrame
            {
                width = width,
                height = height,
                format = (int)AVPixelFormat.AV_PIX_FMT_YUV420P,
            };


            var data = new byte_ptrArray4();
            var linesize = new int_array4();

            ffmpeg.av_image_fill_arrays(ref data, ref linesize, sample, AVPixelFormat.AV_PIX_FMT_YUV420P, width, height, 1).ThrowExceptionIfError();

            avFrame.data.UpdateFrom(data);
            avFrame.linesize.UpdateFrom(linesize);

            return avFrame;
        }

        private bool CheckDropFrame()
        {
            if (_frameTimer == null)
            {
                _frameTimer = new Stopwatch();
                _frameTimer.Start();
            }
            // Calculate frame interval in ticks based on Stopwatch frequency.
            // frameIntervalMs = 1000 / framerate, convert ms to ticks: frameIntervalTicks = frameIntervalMs * Stopwatch.Frequency / 1000
            long frameIntervalTicks = (long)(1000.0 / _encoderContext->framerate.num * Stopwatch.Frequency / 1000);

            long nowTicks = _frameTimer.ElapsedTicks;
            if (_lastFrameTicks != 0)
            {
                if (nowTicks - _lastFrameTicks < frameIntervalTicks)
                {
                    // Drop frame if not enough time has passed.
                    return true;
                }
            }

            _lastFrameTicks = nowTicks;
            return false;
        }

        public byte[]? Encode(AVCodecID codecID, byte* sample, int width, int height, int fps, bool keyFrame = false, AVPixelFormat pixelFormat = AVPixelFormat.AV_PIX_FMT_YUV420P)
        {
            if (!_isDisposed)
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

                        return Encode(codecID, &avFrame, fps, keyFrame);
                    }
                    else
                    {
                        return null;
                    }
                }
            }
            else
            {
                return null;
            }
        }

        public byte[]? Encode(AVCodecID codecID, AVFrame* avFrame, int fps, bool keyFrame = false)
        {
            if (!_isDisposed)
            {
                if (OnVideoEncoderStatistics != null && CheckDropFrame()) {
                    return null;
                }
                lock (_encoderLock)
                {
                    int width = avFrame->width;
                    int height = avFrame->height;

                    if (!_isEncoderInitialised)
                    {
                        InitialiseEncoder(codecID, width, height, fps);
                    }
                    else if (_encoderContext->width != width || _encoderContext->height != height)
                    {
                        _encoderContext->width = width;
                        _encoderContext->height = height;
                    }

                    //int _linesizeY = width;
                    //int _linesizeU = width / 2;
                    //int _linesizeV = width / 2;

                    //int _ySize = _linesizeY * height;
                    //int _uSize = _linesizeU * height / 2;

                    //if (avFrame->format != (int)_encoderContext->pix_fmt) throw new ArgumentException("Invalid pixel format.", nameof(avFrame));
                    //if (avFrame->width != width) throw new ArgumentException("Invalid width.", nameof(avFrame));
                    //if (avFrame->height != height) throw new ArgumentException("Invalid height.", nameof(avFrame));
                    //if (avFrame->linesize[0] < _linesizeY) throw new ArgumentException("Invalid Y linesize.", nameof(avFrame));
                    //if (avFrame->linesize[1] < _linesizeU) throw new ArgumentException("Invalid U linesize.", nameof(avFrame));
                    //if (avFrame->linesize[2] < _linesizeV) throw new ArgumentException("Invalid V linesize.", nameof(avFrame));

                    if (keyFrame || _forceKeyFrame)
                    {
                        avFrame->flags |= ffmpeg.AV_FRAME_FLAG_KEY;
                        avFrame->pict_type = AVPictureType.AV_PICTURE_TYPE_I;
                        _forceKeyFrame = false;
                    }

                    avFrame->pts = _pts++;

                    var pPacket = ffmpeg.av_packet_alloc();
                    OnVideoEncoderStatistics?.Invoke(this, new VideoEncoderStatistics(width, height, fps, FFmpegConvert.GetVideoCodecEnum(_codecID)));
                    try
                    {
                        ffmpeg.avcodec_send_frame(_encoderContext, avFrame).ThrowExceptionIfError();
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

        public void AdjustStream(int bitrate, int fps)
        {
            
            if (_encoderContext == null)
                return;
            lock (_encoderLock)
            {
                if (_encoderContext == null)
                    return;
                _encoderContext->bit_rate = bitrate;
                _encoderContext->framerate.num = fps;
                _encoderContext->gop_size = Math.Max(5, fps * 2);
                switch(_encoderContext->codec_id)
                {
                    case AVCodecID.AV_CODEC_ID_VP8:
                        _encoderContext->rc_max_rate = (long)(_encoderContext->bit_rate * 1.2);
                        _encoderContext->rc_min_rate = (long)(_encoderContext->bit_rate * 0.75);
                        _encoderContext->bit_rate_tolerance = (int)(_encoderContext->bit_rate * 0.15); // 15% tolerance for slight bitrate fluctuations
                        _encoderContext->rc_buffer_size = (int)(_encoderContext->bit_rate * 1.5); // 1.5x target bitrate buffer
                        break;
                    case AVCodecID.AV_CODEC_ID_H264:
                        
                        break;
                }
                
            }
            
        }

        public List<RawImage>? DecodeFaster(AVCodecID codecID, byte[] buffer, out int width, out int height)
        {
            if ((!_isDisposed) && (buffer != null))
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
                            return DecodeFaster(codecID, packet, out width, out height);
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

        private List<RawImage>? DecodeFaster(AVCodecID codecID, AVPacket* packet, out int width, out int height)
        {
            if (!_isDisposed)
            {
                width = 0;
                height = 0;

                if (!_isDecoderInitialised)
                {
                    InitialiseDecoder(codecID);
                    _frame = ffmpeg.av_frame_alloc();
                    _gpuFrame = ffmpeg.av_frame_alloc();
                }

                List<RawImage> rgbFrames = new List<RawImage>();
                if (ffmpeg.avcodec_send_packet(_decoderContext, packet) < 0)
                {
                    width = height = 0;
                    return null;
                }

                ffmpeg.av_frame_unref(_frame);
                ffmpeg.av_frame_unref(_gpuFrame);
                int recvRes = ffmpeg.avcodec_receive_frame(_decoderContext, _frame);

                while (recvRes == 0)
                {

                    AVFrame* decodedFrame = _frame;
                    if (_decoderContext->hw_device_ctx != null)
                    {
                        // If this is hw accelerated, the data in `frame` resides in the GPU memory
                        // Copy it to the CPU memory (gpuFrame)
                        ffmpeg.av_hwframe_transfer_data(_gpuFrame, _frame, 0).ThrowExceptionIfError();
                        decodedFrame = _gpuFrame;
                    }

                    width = decodedFrame->width;
                    height = decodedFrame->height;

                    if (_i420ToRgb == null ||
                        _i420ToRgb.SourceWidth != width ||
                        _i420ToRgb.SourceHeight != height)
                    {
                        _i420ToRgb = new VideoFrameConverter(
                            width, height,
                            (AVPixelFormat)decodedFrame->format,
                            width, height,
                            AVPixelFormat.AV_PIX_FMT_RGB24);
                    }

                    //logger.LogDebug($"[DecodeFaster]"
                    //    + $" - width:[{decodedFrame->width}]"
                    //    + $" - height:[{decodedFrame->height}]"
                    //    + $" - key_frame:[{decodedFrame->key_frame}]"
                    //    + $" - nb_samples:[{decodedFrame->nb_samples}]"
                    //    + $" - pkt_pos:[{decodedFrame->pkt_pos}]"
                    //    + $" - pict_type:[{decodedFrame->pict_type}]"
                    //    + $" - palette_has_changed:[{decodedFrame->palette_has_changed}]"
                    //    + $" - format:[{decodedFrame->format}]"
                    //    + $" - coded_picture_number:[{decodedFrame->coded_picture_number}]"
                    //    + $" - decode_error_flags:[{decodedFrame->decode_error_flags}]"
                    //    );

                    var frameI420 = _i420ToRgb.Convert(*decodedFrame);
                    if ((frameI420.width != 0) && (frameI420.height != 0))
                    {
                        RawImage imageRawSample = new RawImage
                        {
                            Width = width,
                            Height = height,
                            Stride = frameI420.linesize[0],
                            Sample = (IntPtr)frameI420.data[0],
                            PixelFormat = VideoPixelFormatsEnum.Rgb
                        };
                        rgbFrames.Add(imageRawSample);
                    }

                    ffmpeg.av_frame_unref(_frame);
                    ffmpeg.av_frame_unref(_gpuFrame);
                    recvRes = ffmpeg.avcodec_receive_frame(_decoderContext, _frame);
                }

                if (recvRes < 0 && recvRes != ffmpeg.AVERROR(ffmpeg.EAGAIN))
                {
                    //recvRes.ThrowExceptionIfError();
                }

                return rgbFrames;

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

                if (_frame != null)
                {
                    fixed (AVFrame** pFrame = &_frame)
                    {
                        ffmpeg.av_frame_free(pFrame);
                    }
                }

                if (_gpuFrame != null)
                {
                    fixed (AVFrame** pFrame = &_gpuFrame)
                    {
                        ffmpeg.av_frame_free(pFrame);
                    }
                }

                _frameTimer?.Stop();
            }
        }

        // true:
        //    fmt = match from sourcePixFmts
        // false:
        //    found no matching format, fmt = first of the codec supported formats
        internal bool NegotiatePixelFormat(AVCodecID codecid, int width, int height, int frameRate, AVPixelFormat[]? sourcePixFmts, out AVPixelFormat fmt)
        {
            if (_negotiatedPixFmt != null && _codecID == codecid)
            {
                fmt = (AVPixelFormat)_negotiatedPixFmt;
                return true;
            }

            lock (_encoderLock)
            {
                if (_isEncoderInitialised)
                    ResetEncoder();

                InitialiseEncoder(codecid, width, height, frameRate);

                if (logger.IsEnabled(LogLevel.Trace))
                    logger.LogTrace("Negotiating pixel format for codec [{name}].", GetNameString(_encoderContext->codec->name));

                var fmts = _encoderContext->codec->pix_fmts;
                while (*fmts != AVPixelFormat.AV_PIX_FMT_NONE
                    && sourcePixFmts?.Length > 0 && !sourcePixFmts.Contains(*fmts))
                {
                    if (logger.IsEnabled(LogLevel.Trace))
                        logger.LogTrace("Skipping unsupported pixel format {fmt}.", *fmts);
                    fmts++;
                }

                fmt = *fmts;
                var ok = _encoderContext->codec->pix_fmts[0] != fmt;

                ResetEncoder();
                
                _negotiatedPixFmt = fmt;

                if (logger.IsEnabled(LogLevel.Trace))
                    logger.LogTrace("Negotiated pixel format {fmt}", fmt);

                return ok;
            }
        }

        private static string? GetNameString(byte* name) => Marshal.PtrToStringAnsi((IntPtr)name);
    }
}
