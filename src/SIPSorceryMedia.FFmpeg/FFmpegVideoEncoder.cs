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
        // libvpx's realtime deadline defaults to cpu-used 0 (the slowest realtime speed), which
        // cannot keep up at higher resolutions (~12 fps for 1080p). 5 is a balanced realtime default
        // that sustains 1080p30 on typical hardware while keeping good quality. Callers can override
        // it via the encoderOptions dictionary ("cpu-used"), which is applied after this default.
        private const string DEFAULT_LIBVPX_REALTIME_CPU_USED = "5";

        /// <summary>
        /// The threshold frame rate at which to use a key frame rate of 1 (every frame is a key frame).
        /// This is to avoid the encoder lagging behind when the frame rate is very low.
        /// </summary>
        private const int ALL_KEY_FRAMES_FPS_THRESHOLD = 5;

        private static readonly List<VideoFormat> _supportedFormats = Helper.GetSupportedVideoFormats();

        public List<VideoFormat> SupportedFormats
        {
            get => _supportedFormats;
        }

        private readonly Dictionary<string, string> _codecOptions;
        private Dictionary<string, Dictionary<string, string>>? _codecOptionsByName;

        private Stopwatch? _frameTimer;
        public event EventHandler<VideoEncoderStatistics>? OnVideoEncoderStatistics;

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

        private static ILogger logger = SIPSorcery.LogFactory.CreateLogger<FFmpegVideoEncoder>();

        public FFmpegVideoEncoder(Dictionary<string, string>? encoderOptions = null, AVHWDeviceType HWDeviceType = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE)
        {
            FFmpegInit.EnsureBinariesRegistered();
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
            {
                return decodedFrames;
            }

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
            {
                (_codecOptionsByName ??= [])[name] = opts;
            }

            return codec != null;
        }

        private AVCodec* GetCodec(AVCodecID codecID, string? wrapName, bool isEncoder = true)
        {
            if (wrapName == null)
            {
                return null;
            }

            IntPtr? iterator = null;
            var cdc = ffmpeg.av_codec_iterate((void**)&iterator);
            while (cdc != null)
            {
                if (cdc->id == codecID && GetNameString(cdc->wrapper_name) == wrapName
                    && (
                        (isEncoder && ffmpeg.av_codec_is_encoder(cdc) != 0)
                        || (!isEncoder && ffmpeg.av_codec_is_decoder(cdc) != 0)
                        )
                    )
                {
                    break;
                }

                cdc = ffmpeg.av_codec_iterate((void**)&iterator);
            }

            (_specificEncoders ??= [])[codecID] = (IntPtr)cdc;

            if (cdc == null)
            {
                logger.LogWarning("Codec not found for {id} with wrapper {wrap}", codecID, _wrapName);
            }

            return cdc;
        }

        private AVCodec* GetCodec(AVCodecID codecID, bool isEncoder = true)
        {
            AVCodec* codec = null;

            if (_specificEncoders?.TryGetValue(codecID, out var cdc) ?? false)
            {
                codec = (AVCodec*)cdc;
            }

            if (codec == null)
            {
                codec = GetCodec(codecID, _wrapName, isEncoder);
            }

            if (codec == null)
            {
                if (isEncoder)
                {
                    // FFmpeg's default AV1 encoder is libaom-av1, the reference encoder, which is far
                    // too slow for real-time use (~12 fps at 1080p). Prefer SVT-AV1, the realtime
                    // oriented AV1 encoder, when it is present in the FFmpeg build; the per-encoder
                    // realtime tuning in InitialiseEncoder then applies. Fall back to the default
                    // otherwise. An encoder chosen explicitly via SetCodec still takes precedence (it
                    // is handled by the _specificEncoders lookup above, so this is only reached when
                    // the caller has not selected one).
                    if (codecID == AVCodecID.AV_CODEC_ID_AV1)
                    {
                        codec = ffmpeg.avcodec_find_encoder_by_name("libsvtav1");
                    }

                    if (codec == null)
                    {
                        codec = ffmpeg.avcodec_find_encoder(codecID);
                    }
                }
                else
                {
                    codec = ffmpeg.avcodec_find_decoder(codecID);
                }
            }

            return codec;
        }

        private Dictionary<string, string> GetCodecOptions(string? name)
        {
            return (!string.IsNullOrWhiteSpace(name)
                && (_codecOptionsByName?.TryGetValue(name!, out var opt) ?? false))
                ? opt
                : _codecOptions;
        }

        public void InitialiseEncoder(AVCodecID codecID, int width, int height, int fps)
        {
            try
            {
                if (!_isEncoderInitialised)
                {
                    _codecID = codecID;

                    var codec = GetCodec(codecID);
                    if (codec == null)
                    {
                        // A null codec means the loaded FFmpeg build has no encoder for this codec
                        // (e.g. H264/H265 when built without libx264/libx265). Check before
                        // dereferencing codec->name below so this surfaces as a clear, actionable
                        // error rather than a NullReferenceException.
                        throw new ApplicationException(
                            $"The loaded FFmpeg build does not provide an encoder for {codecID}. " +
                            $"For H264/H265 this usually means FFmpeg was built without libx264/libx265; " +
                            $"verify with 'ffmpeg -encoders' or select a codec the build supports (e.g. VP8).");
                    }

                    var cdcname = GetNameString(codec->name);
                    //var encOpts = GetCodecOptions(cdcname);

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

                    var supportedPixFmts = GetSupportedPixelFormats(_encoderContext, codec);
                    if (supportedPixFmts.Length == 0)
                    {
                        throw new ApplicationException($"Encoder {cdcname} does not report any supported pixel formats.");
                    }

                    _encoderContext->pix_fmt = _negotiatedPixFmt ?? supportedPixFmts[0];

                    if (_bit_rate != null) { _encoderContext->bit_rate = (long)_bit_rate; }
                    if (_bit_rate_tolerance != null) { _encoderContext->bit_rate_tolerance = (int)_bit_rate_tolerance; }
                    if (_rc_min_rate != null) { _encoderContext->rc_min_rate = (long)_rc_min_rate; }
                    if (_rc_max_rate != null) { _encoderContext->rc_max_rate = (long)_rc_max_rate; }
                    // Default to auto (0) so encoding uses all available cores; callers can pin a specific
                    // count via SetThreadCount. Single threaded (the libavcodec default if left unset) is far
                    // too slow at higher resolutions.
                    _encoderContext->thread_count = _thread_count ?? 0;

                    // Set Key frame interval
                    _encoderContext->gop_size = (fps < ALL_KEY_FRAMES_FPS_THRESHOLD) ? 1 : fps;

                    try
                    {
                        // Real-time oriented defaults for the common encoders: a fast preset / cpu-used,
                        // low-latency tuning and (via thread_count above) multi-threading. These suit the
                        // typical WebRTC/live use case; callers wanting different trade-offs override them
                        // through encoderOptions, which are applied after this block.
                        switch (cdcname)
                        {
                            case "libx264":
                                ffmpeg.av_opt_set(_encoderContext->priv_data, "profile", "baseline", 0).ThrowExceptionIfError();
                                ffmpeg.av_opt_set(_encoderContext->priv_data, "preset", "veryfast", 0).ThrowExceptionIfError();
                                ffmpeg.av_opt_set(_encoderContext->priv_data, "tune", "zerolatency", 0).ThrowExceptionIfError();
                                break;
                            case "h264_qsv":
                                ffmpeg.av_opt_set(_encoderContext->priv_data, "profile", "66" /* baseline */, 0).ThrowExceptionIfError();
                                ffmpeg.av_opt_set(_encoderContext->priv_data, "preset", "7" /* veryfast */, 0).ThrowExceptionIfError();
                                break;
                            case "libx265":
                                ffmpeg.av_opt_set(_encoderContext->priv_data, "preset", "ultrafast", 0).ThrowExceptionIfError();
                                ffmpeg.av_opt_set(_encoderContext->priv_data, "tune", "zerolatency", 0).ThrowExceptionIfError();
                                break;
                            case "libvpx":
                                ffmpeg.av_opt_set(_encoderContext->priv_data, "quality", "realtime", 0).ThrowExceptionIfError();
                                ffmpeg.av_opt_set(_encoderContext->priv_data, "cpu-used", DEFAULT_LIBVPX_REALTIME_CPU_USED, 0).ThrowExceptionIfError();
                                break;
                            case "libvpx-vp9":
                                ffmpeg.av_opt_set(_encoderContext->priv_data, "quality", "realtime", 0).ThrowExceptionIfError();
                                // VP9 realtime cpu-used range is 0-9 (higher is faster); row-mt enables
                                // tile-row multi-threading which is what makes VP9 keep up at high rates.
                                ffmpeg.av_opt_set(_encoderContext->priv_data, "cpu-used", "8", 0).ThrowExceptionIfError();
                                ffmpeg.av_opt_set(_encoderContext->priv_data, "row-mt", "1", 0).ThrowExceptionIfError();
                                break;
                            case "libaom-av1":
                                // libaom's default (good/best) is far too slow for live use; usage=realtime
                                // plus a high cpu-used (0-11 in realtime, higher is faster) and row-mt makes it
                                // usable, though it is still the slowest of the software encoders.
                                ffmpeg.av_opt_set(_encoderContext->priv_data, "usage", "realtime", 0).ThrowExceptionIfError();
                                ffmpeg.av_opt_set(_encoderContext->priv_data, "cpu-used", "8", 0).ThrowExceptionIfError();
                                ffmpeg.av_opt_set(_encoderContext->priv_data, "row-mt", "1", 0).ThrowExceptionIfError();
                                break;
                            case "libsvtav1":
                                // SVT-AV1 is the realtime-oriented AV1 encoder. preset 0-13 (higher is faster);
                                // the high presets plus a low-latency, low-delay prediction structure suit live.
                                // SVT-AV1's low-delay structure does NOT support VBR rate control, so when a
                                // target bitrate is set the rate control must be CBR (rc=2); otherwise it errors
                                // ("VBR Rate control is currently not supported for LOW_DELAY") and produces no
                                // output. With no bitrate the default constant-quality mode is used, which
                                // low-delay does support.
                                ffmpeg.av_opt_set(_encoderContext->priv_data, "preset", "11", 0).ThrowExceptionIfError();
                                ffmpeg.av_opt_set(_encoderContext->priv_data, "svtav1-params",
                                    _encoderContext->bit_rate > 0 ? "lp=0:pred-struct=1:rc=2" : "lp=0:pred-struct=1", 0).ThrowExceptionIfError();
                                break;
                            case "librav1e":
                                // rav1e exposes speed 0-10 (higher is faster); use a fast, low-latency setting.
                                ffmpeg.av_opt_set(_encoderContext->priv_data, "speed", "10", 0).ThrowExceptionIfError();
                                ffmpeg.av_opt_set(_encoderContext->priv_data, "low_latency", "true", 0).ThrowExceptionIfError();
                                break;
                            default:
                                break;
                        }
                    }
                    catch (ApplicationException ex)
                    {
                        logger.LogCritical(ex, "Failed to set default encoder options for codec {name}. {msg}", cdcname, ex.Message);
                        throw;
                    }

                    foreach (var option in _codecOptions)
                    {
                        var ok = ffmpeg.av_opt_set(_encoderContext->priv_data, option.Key, option.Value, ffmpeg.AV_OPT_SEARCH_CHILDREN);
                        if (ok < 0)
                        {
                            logger.LogWarning("Failed to set encoder option \"{key}\"=\"{val}\", Skipping this option. {msg}", option.Key, option.Value, FFmpegInit.av_strerror(ok));
                        }
                    }

                    ffmpeg.avcodec_open2(_encoderContext, codec, null).ThrowExceptionIfError();

                    logger.LogDebug("Successfully initialised ffmpeg based image encoder: CodecId:[{id}] - Name:[{name}] - {w}:{h} - {fps} Fps - {fmt}",
                        codecID, GetNameString(codec->name), width, height, fps, _encoderContext->pix_fmt);

                    // Only mark initialised once the context is fully built and opened. Setting this
                    // earlier means a failure part-way through latches a half-initialised state, and
                    // every later Encode call then dereferences the null/unopened context (a repeating
                    // NullReferenceException) instead of surfacing the real error.
                    _isEncoderInitialised = true;
                }
            }
            finally
            {
                if (!_isEncoderInitialised && _encoderContext != null)
                {
                    // IF the encoder failed to initialise, free the context so a later attempt can try again.
                    fixed (AVCodecContext** pCtx = &_encoderContext)
                    {
                        ffmpeg.avcodec_free_context(pCtx);
                    }
                }
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

                    UnsafeReset();
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
                    
                    UnsafeReset();
                }
            }
        }

        private void UnsafeReset()
        {
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

            _negotiatedPixFmt = null;
        }

        private void InitialiseDecoder(AVCodecID codecID)
        {
            if (!_isDecoderInitialised)
            {
                _isDecoderInitialised = true;
                _codecID = codecID;

                var codec = GetCodec(codecID, isEncoder: false);
                var decOpts = GetCodecOptions(GetNameString(codec->name));

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
                            //TracePacket(pPacket, "hevc_mp4toannexb");

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

        /*
        // for debugging or bitstream filter example
        bool inits = false;
        unsafe AVBSFContext* bsfctx;
        private void TracePacket(AVPacket* pPacket, string name)
        {
            if (!inits)
            {
                IntPtr ptr = IntPtr.Zero;
                //var bsf = ffmpeg.av_bsf_get_by_name("hevc_mp4toannexb");
                var bsf = ffmpeg.av_bsf_get_by_name(name);
                ffmpeg.av_bsf_alloc(bsf, (AVBSFContext**)&ptr);
                bsfctx = (AVBSFContext*)ptr;
                ffmpeg.avcodec_parameters_from_context(bsfctx->par_in, 
                    (IntPtr)_encoderContext == IntPtr.Zero ? _decoderContext : _encoderContext);
                ffmpeg.av_bsf_init(bsfctx);
                inits = true;
            }

            try
            {
                ffmpeg.av_bsf_send_packet(bsfctx, pPacket).ThrowExceptionIfError();
                ffmpeg.av_bsf_receive_packet(bsfctx, pPacket).ThrowExceptionIfError();
            }
            catch (Exception e)
            {
                logger.LogError("BSF excp: {Log}", e.Message);
            }
        }
        */

        public void AdjustStream(int bitrate, int fps)
        {
            if (_encoderContext == null)
            {
                return;
            }

            lock (_encoderLock)
            {
                if (_encoderContext == null)
                {
                    return;
                }
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

                if (_isDecoderInitialised && _codecID != codecID)
                {
                    ResetDecoder();
                }

                if (!_isDecoderInitialised)
                {
                    InitialiseDecoder(codecID);
                    _frame = ffmpeg.av_frame_alloc();
                    _gpuFrame = ffmpeg.av_frame_alloc();
                }

                //TracePacket(packet, "trace_headers");

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

                    var frameI420 = _i420ToRgb.Convert(decodedFrame);
                    if ((frameI420->width != 0) && (frameI420->height != 0))
                    {
                        RawImage imageRawSample = new RawImage
                        {
                            Width = width,
                            Height = height,
                            Stride = frameI420->linesize[0],
                            Sample = (IntPtr)frameI420->data[0],
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
                {
                    ResetEncoder();
                }

                InitialiseEncoder(codecid, width, height, frameRate);

                if (logger.IsEnabled(LogLevel.Trace))
                {
                    logger.LogTrace("Negotiating pixel format for codec [{name}].", GetNameString(_encoderContext->codec->name));
                }

                var fmts = _encoderContext->codec->pix_fmts;
                while (*fmts != AVPixelFormat.AV_PIX_FMT_NONE
                    && sourcePixFmts?.Length > 0 && !sourcePixFmts.Contains(*fmts))
                {
                    if (logger.IsEnabled(LogLevel.Trace))
                    {
                        logger.LogTrace("Skipping unsupported pixel format {fmt}.", *fmts);
                    }
                    fmts++;
                }

                var ret = false;
                fmt = *fmts;
                if (fmt == AVPixelFormat.AV_PIX_FMT_NONE)
                {
                    fmt = _encoderContext->codec->pix_fmts[0];
                }
                else
                {
                    ret = true;
                }

                ResetEncoder();
                
                _negotiatedPixFmt = fmt;

                    logger.LogTrace("Negotiated pixel format {fmt}", fmt);

                return ret;
            }
        }

        private static AVPixelFormat[] GetSupportedPixelFormats(AVCodecContext* codecContext, AVCodec* codec)
        {
            void* configs = null;
            int configsCount = 0;

            ffmpeg.avcodec_get_supported_config(
                codecContext,
                codec,
                AVCodecConfig.AV_CODEC_CONFIG_PIX_FORMAT,
                0,
                &configs,
                &configsCount).ThrowExceptionIfError();

            if (configs == null || configsCount <= 0)
            {
                return Array.Empty<AVPixelFormat>();
            }

            var pixFmts = (AVPixelFormat*)configs;
            var result = new AVPixelFormat[configsCount];
            for (int i = 0; i < configsCount; i++)
            {
                result[i] = pixFmts[i];
            }

            return result;
        }

        private static string? GetNameString(byte* name) => Marshal.PtrToStringAnsi((IntPtr)name);
    }
}
