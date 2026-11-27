//-----------------------------------------------------------------------------
// Filename: H264Codec.cs
//
// Description: SIPSorcery's IVideoEncoder for the pure C# OpenH264 port. It
// encodes I420, NV12 and packed RGB frames to Annex B access units, and
// decodes the access units SIPSorcery's H264 depacketiser produces to I420,
// BGR or RGB. Not part of the OpenH264 port itself.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 25 Nov 2026  Claude          Created (D7).
// 27 Nov 2026  Claude          The encoder (E8).
// 27 Nov 2026  Claude          EncoderThreads (P5.7).
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using SIPSorcery.H264.Decoder;
using SIPSorcery.H264.Encoder;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.H264;

/// <summary>
/// A pure C# H.264 encoder and decoder for SIPSorcery, ported from Cisco's OpenH264. Each
/// encoded frame is one access unit in Annex B format (start code delimited NAL units), ready
/// for SIPSorcery's H264 packetiser. Each call to <see cref="DecodeVideo"/> takes one access
/// unit in the same format, as <c>H264Depacketiser</c> and
/// <c>RTCPeerConnection.OnVideoFrameReceived</c> deliver them, and returns the picture it
/// decodes to, if any.
/// </summary>
/// <remarks>
/// <para>
/// The port covers the profile WebRTC uses, Constrained Baseline (<c>profile-level-id=42e01f</c>).
/// Its encoder and decoder output is checked bit for bit against OpenH264's own. Streams using
/// CABAC or B slices (Main and High profile) are not decoded: their slices fail and are
/// concealed.
/// </para>
/// <para>
/// The encoder is set up as libwebrtc sets up OpenH264: camera content, bitrate mode rate
/// control that may drop a frame to hold the bitrate, and one slice a picture on one thread,
/// unless <see cref="EncoderThreads"/> asks for more. Every IDR access unit starts with the
/// sequence and picture parameter sets, so a receiver can join at any key frame. The stream's
/// level is the lowest that fits the frame size, frame rate and bitrate; a receiver that
/// signals level 3.1 still decodes the higher levels of larger frames in practice, as browsers
/// do. The encoder is created on the first frame and re-created, starting with a key frame,
/// whenever the frame size or the thread count changes. A frame's width and height must be at
/// least 16; an odd width or height loses its last column or row, as in OpenH264.
/// </para>
/// <para>
/// The port runs OpenH264's portable C code, with SIMD kernels in place of its assembly. On one
/// desktop core it encodes camera-like video at about 370 frames a second at 640x480, 120 at
/// 1280x720 and 50 at 1920x1080, and on four <see cref="EncoderThreads"/> at about 730, 270 and
/// 120, so one thread keeps up in real time at 1280x720 and 30 frames a second.
/// </para>
/// <para>
/// I420 frames are encoded as they are. NV12 frames have their chroma planes split, and packed
/// RGB frames (BGRA, RGBA, BGR and RGB) are converted with BT.601 limited range, which is what
/// the decoder and browsers take a stream that signals no colour to be.
/// </para>
/// <para>
/// Decoded pictures are output as soon as their access unit is decoded, with no reordering
/// delay, which Constrained Baseline allows. Damaged or missing data is concealed by copying
/// from the previous picture, and <see cref="KeyFrameRequired"/> says when the receiver should
/// ask the sender for a key frame to recover. BGR and RGB output is converted with the colour
/// matrix and range the stream signals in its sequence parameter set: BT.709 or BT.601, full or
/// limited range. A stream that signals nothing is taken to be BT.601 limited range, as
/// browsers take it.
/// </para>
/// <para>
/// Calls are serialised internally. Dispose the codec to release the encoder's and decoder's
/// native memory.
/// </para>
/// </remarks>
public sealed unsafe class H264Codec : IVideoEncoder
{
    /// <summary>The RTP payload type SIPSorcery suggests for H.264.</summary>
    public const int H264_SUGGESTED_FORMAT_ID = 100;

    /// <summary>
    /// The SDP format parameters: Constrained Baseline, packetization mode 1 (single NAL unit,
    /// STAP-A and FU-A packets), and level 3.1, with any level accepted from the sender.
    /// </summary>
    public const string H264_FORMAT_PARAMETERS = "level-asymmetry-allowed=1;packetization-mode=1;profile-level-id=42e01f";

    /// <summary>The key frame interval of a new codec: one a second at 30 frames a second.</summary>
    public const int DEFAULT_KEYFRAME_INTERVAL_FRAMES = 30;

    /// <summary>The frame rate of a new codec.</summary>
    public const int DEFAULT_FRAME_RATE = 30;

    // With no target bitrate set, the bitrate is picked from the frame size and rate: 0.1 bits a
    // pixel per frame, a little under 1 Mbps for 640x480 at 30 frames a second.
    private const double AUTO_BITS_PER_PIXEL = 0.1;
    private const int MIN_AUTO_BITRATE_BPS = 100_000;

    // The smallest frame OpenH264 encodes.
    private const int MIN_ENCODE_DIMENSION = 16;

    private static readonly ILogger logger = LogFactory.CreateLogger<H264Codec>();

    private readonly List<VideoFormat> _supportedFormats = new()
    {
        new VideoFormat(VideoCodecsEnum.H264, H264_SUGGESTED_FORMAT_ID, VideoFormat.DEFAULT_CLOCK_RATE, H264_FORMAT_PARAMETERS)
    };

    private readonly object _lock = new();
    private CWelsDecoder* _decoder;
    // DecodeVideoFaster's output, re-used for every picture.
    private byte* _rawBuffer;
    private int _rawBufferSize;

    private CWelsH264SVCEncoder* _encoder;
    // The size _encoder was initialised for, 0 when it needs initialising.
    private int _encWidth;
    private int _encHeight;
    // The encoder's output description, too large for the stack to want it on every frame.
    private SFrameBSInfo* _bsInfo;
    // The I420 planes of converted (RGB) or split (NV12) input.
    private byte* _yuvBuffer;
    private int _yuvBufferSize;
    private readonly ArrayBufferWriter<byte> _output = new(1 << 16);
    // When the first frame was encoded, 0 before then: the encoder's timestamps count from it.
    private long _firstFrameTicks;
    private int _targetBitrateKbps;
    private int _frameRate = DEFAULT_FRAME_RATE;
    private int _keyframeIntervalFrames = DEFAULT_KEYFRAME_INTERVAL_FRAMES;
    private int _encoderThreads = 1;
    private bool _forceKeyFrame;

    private bool _disposed;

    /// <summary>Creates a codec. The encoder and decoder are created when they are first used.</summary>
    public H264Codec()
    { }

    /// <summary>Releases the encoder and decoder if the codec was not disposed.</summary>
    ~H264Codec() => Release();

    /// <inheritdoc/>
    public List<VideoFormat> SupportedFormats => _supportedFormats;

    /// <summary>
    /// The encoder's target bitrate in kilobits per second. 0, the default, picks it from the
    /// frame size and <see cref="FrameRate"/>: 0.1 bits a pixel per frame, at least 100 kbps.
    /// Changes apply from the next frame.
    /// </summary>
    public int TargetBitrateKbps
    {
        get => _targetBitrateKbps;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            lock (_lock)
            {
                _targetBitrateKbps = value;
                if (_encWidth != 0)
                {
                    SBitrateInfo info = default;
                    info.iLayer = SPATIAL_LAYER_ALL;
                    info.iBitrate = TargetBitrateBps(_encWidth, _encHeight);
                    _encoder->SetOption(ENCODER_OPTION_BITRATE, &info);
                }
            }
        }
    }

    /// <summary>
    /// The rate frames are expected at, from 1 to 60, which the rate control shares the bitrate
    /// out by. The default is 30. Changes apply from the next frame.
    /// </summary>
    public int FrameRate
    {
        get => _frameRate;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, WelsEnc.MIN_FRAME_RATE);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, WelsEnc.MAX_FRAME_RATE);
            lock (_lock)
            {
                _frameRate = value;
                if (_encWidth != 0)
                {
                    float fps = value;
                    _encoder->SetOption(ENCODER_OPTION_FRAME_RATE, &fps);
                    if (_targetBitrateKbps == 0)
                    {
                        // The automatic bitrate follows the frame rate.
                        SBitrateInfo info = default;
                        info.iLayer = SPATIAL_LAYER_ALL;
                        info.iBitrate = TargetBitrateBps(_encWidth, _encHeight);
                        _encoder->SetOption(ENCODER_OPTION_BITRATE, &info);
                    }
                }
            }
        }
    }

    /// <summary>
    /// The number of frames from one key frame (an IDR picture) to the next, counted from the
    /// last one, forced or not. 0 sends only the first, and then key frames only when
    /// <see cref="ForceKeyFrame"/> asks for one. The default is 30, one a second at 30 frames a
    /// second, so a receiver that loses one recovers without having to ask. Changes apply from
    /// the next frame.
    /// </summary>
    public int KeyframeIntervalFrames
    {
        get => _keyframeIntervalFrames;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            lock (_lock)
            {
                _keyframeIntervalFrames = value;
                if (_encWidth != 0)
                {
                    int interval = value;
                    _encoder->SetOption(ENCODER_OPTION_IDR_INTERVAL, &interval);
                }
            }
        }
    }

    /// <summary>
    /// The number of threads the encoder codes a picture on, from 1 to 4, each coding one slice
    /// of it: the calling thread and up to three thread pool threads. The default, 1, codes one
    /// slice a picture on the calling thread, as libwebrtc sets OpenH264 up. More threads encode
    /// a large frame faster but compress a little less well, as no slice predicts from another
    /// and the edges between slices are not deblocked. The slices start out the same size and
    /// are resized from picture to picture, as OpenH264 does, so that each thread takes about as
    /// long, which also means the same frames do not always give the same stream. A small frame
    /// is coded in fewer slices, on as many threads, as OpenH264 sets them: one slice for 128x96
    /// (48 macroblocks) or smaller, and no more than its macroblock rows allow. A change starts
    /// a new stream, with a key frame, from the next frame.
    /// </summary>
    public int EncoderThreads
    {
        get => _encoderThreads;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, WelsEnc.MAX_THREADS_NUM);
            lock (_lock)
            {
                if (_encoderThreads != value)
                {
                    _encoderThreads = value;
                    // The encoder is initialised again on the next frame.
                    _encWidth = _encHeight = 0;
                }
            }
        }
    }

    /// <summary>
    /// True when the last access unit decoded was not clean: parameter sets were missing, a
    /// reference picture was lost, the data was damaged, or the picture predicts from one that
    /// was. Any picture output was concealed, and the damage carries forward until the next IDR
    /// picture, so the receiver should ask the sender for a key frame (in WebRTC, an RTCP PLI).
    /// False after an access unit that decodes cleanly.
    /// </summary>
    public bool KeyFrameRequired { get; private set; }

    /// <summary>Makes the next encoded frame a key frame (an IDR picture, with the parameter sets).</summary>
    public void ForceKeyFrame()
    {
        lock (_lock)
        {
            _forceKeyFrame = true;
        }
    }

    /// <summary>Encodes one frame.</summary>
    /// <param name="width">The frame width, at least 16.</param>
    /// <param name="height">The frame height, at least 16.</param>
    /// <param name="sample">The frame, tightly packed; for I420 and NV12 the chroma plane(s) follow the luma rows.</param>
    /// <param name="pixelFormat">I420, NV12, BGRA, RGBA, BGR or RGB.</param>
    /// <param name="codec">Must be <see cref="VideoCodecsEnum.H264"/>.</param>
    /// <returns>The access unit, in Annex B format, or null if the rate control dropped the frame.</returns>
    public byte[] EncodeVideo(int width, int height, byte[] sample, VideoPixelFormatsEnum pixelFormat, VideoCodecsEnum codec)
    {
        ArgumentNullException.ThrowIfNull(sample);
        CheckCodec(codec);
        lock (_lock)
        {
            _output.ResetWrittenCount();
            int length = EncodeVideo(width, height, sample, RgbToI420.PackedStride(pixelFormat, width), pixelFormat, _output);
            return length == 0 ? null : _output.WrittenSpan.ToArray();
        }
    }

    /// <summary>Encodes one frame from a <see cref="RawImage"/>.</summary>
    /// <param name="rawImage">
    /// The frame, in I420, NV12, BGRA, RGBA, BGR or RGB. Its stride is that of the luma plane or
    /// the packed pixels, 0 for tightly packed; for I420 and NV12 the chroma plane(s) follow the
    /// luma rows.
    /// </param>
    /// <param name="codec">Must be <see cref="VideoCodecsEnum.H264"/>.</param>
    /// <returns>The access unit, in Annex B format, or null if the rate control dropped the frame.</returns>
    public byte[] EncodeVideoFaster(RawImage rawImage, VideoCodecsEnum codec)
    {
        ArgumentNullException.ThrowIfNull(rawImage);
        if (rawImage.Sample == IntPtr.Zero)
        {
            throw new ArgumentException("The image has no pixel data.", nameof(rawImage));
        }
        CheckCodec(codec);
        int stride = rawImage.Stride > 0 ? rawImage.Stride : RgbToI420.PackedStride(rawImage.PixelFormat, rawImage.Width);
        CheckFrame(rawImage.Width, rawImage.Height, stride, rawImage.PixelFormat, nameof(rawImage));
        lock (_lock)
        {
            _output.ResetWrittenCount();
            int length = Encode(rawImage.Width, rawImage.Height, (byte*)rawImage.Sample, stride, rawImage.PixelFormat, _output);
            return length == 0 ? null : _output.WrittenSpan.ToArray();
        }
    }

    /// <summary>
    /// Encodes one frame into a caller-supplied writer, without allocating a result array.
    /// </summary>
    /// <param name="width">The frame width, at least 16.</param>
    /// <param name="height">The frame height, at least 16.</param>
    /// <param name="sample">The frame; for I420 and NV12 the chroma plane(s) follow the luma rows.</param>
    /// <param name="stride">
    /// Bytes between rows of the luma plane or of the packed pixels, 0 for tightly packed. I420's
    /// chroma rows are half that, rounded up, and NV12's are the same.
    /// </param>
    /// <param name="pixelFormat">I420, NV12, BGRA, RGBA, BGR or RGB.</param>
    /// <param name="output">Receives the access unit, in Annex B format.</param>
    /// <returns>The number of bytes written, 0 if the rate control dropped the frame.</returns>
    public int EncodeVideo(int width, int height, ReadOnlySpan<byte> sample, int stride, VideoPixelFormatsEnum pixelFormat,
                           IBufferWriter<byte> output)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (stride <= 0)
        {
            stride = RgbToI420.PackedStride(pixelFormat, width);
        }
        CheckFrame(width, height, stride, pixelFormat, nameof(stride));
        if (sample.Length < RgbToI420.SourceSize(pixelFormat, stride, width, height))
        {
            throw new ArgumentException($"The sample is too small for a {width}x{height} {pixelFormat} frame.", nameof(sample));
        }
        lock (_lock)
        {
            fixed (byte* src = sample)
            {
                return Encode(width, height, src, stride, pixelFormat, output);
            }
        }
    }

    /// <summary>
    /// Decodes one access unit.
    /// </summary>
    /// <param name="encodedSample">The access unit, in Annex B format.</param>
    /// <param name="pixelFormat">
    /// <see cref="VideoPixelFormatsEnum.I420"/>, <see cref="VideoPixelFormatsEnum.Rgb"/> or
    /// <see cref="VideoPixelFormatsEnum.Bgr"/>. Any other format gets BGR; each sample's
    /// <see cref="VideoSample.PixelFormat"/> says which it is.
    /// </param>
    /// <param name="codec">Must be <see cref="VideoCodecsEnum.H264"/>.</param>
    /// <returns>
    /// The decoded picture, or nothing if the access unit did not complete one (parameter sets
    /// alone, or data that could not be decoded at all). I420 samples are the Y plane followed
    /// by the U and V planes, each row packed. BGR and RGB samples are 3 bytes a pixel with rows
    /// packed, so the stride is three times the width.
    /// </returns>
    public IEnumerable<VideoSample> DecodeVideo(byte[] encodedSample, VideoPixelFormatsEnum pixelFormat, VideoCodecsEnum codec)
    {
        Validate(encodedSample, codec);

        lock (_lock)
        {
            if (!Decode(encodedSample, out DecodedPicture pic))
            {
                return new List<VideoSample>();
            }

            VideoPixelFormatsEnum format = pixelFormat switch
            {
                VideoPixelFormatsEnum.I420 => VideoPixelFormatsEnum.I420,
                VideoPixelFormatsEnum.Rgb => VideoPixelFormatsEnum.Rgb,
                _ => VideoPixelFormatsEnum.Bgr,
            };

            byte[] sample;
            if (format == VideoPixelFormatsEnum.I420)
            {
                sample = new byte[pic.I420Size];
                fixed (byte* dst = sample)
                {
                    YuvToRgb.CopyI420(pic, dst);
                }
            }
            else
            {
                int stride = pic.Width * 3;
                sample = new byte[stride * pic.Height];
                fixed (byte* dst = sample)
                {
                    YuvToRgb.ToRgb(pic, dst, stride, format == VideoPixelFormatsEnum.Bgr);
                }
            }

            return new List<VideoSample>
            {
                new VideoSample
                {
                    Width = (uint)pic.Width,
                    Height = (uint)pic.Height,
                    Sample = sample,
                    PixelFormat = format,
                }
            };
        }
    }

    /// <summary>
    /// As <see cref="DecodeVideo"/>, but the picture is converted into a buffer the codec owns
    /// and re-uses, so nothing frame sized is allocated per picture.
    /// </summary>
    /// <param name="encodedSample">The access unit, in Annex B format.</param>
    /// <param name="pixelFormat">
    /// <see cref="VideoPixelFormatsEnum.Rgb"/> or <see cref="VideoPixelFormatsEnum.Bgr"/>. Any
    /// other format, I420 included, gets BGR: a <see cref="RawImage"/> describes one plane, so
    /// use <see cref="DecodeVideo"/> for I420.
    /// </param>
    /// <param name="codec">Must be <see cref="VideoCodecsEnum.H264"/>.</param>
    /// <returns>
    /// The decoded picture, or nothing. Its <see cref="RawImage.Sample"/> points into the codec's
    /// buffer and is only valid until the next decode call or until the codec is disposed.
    /// </returns>
    public IEnumerable<RawImage> DecodeVideoFaster(byte[] encodedSample, VideoPixelFormatsEnum pixelFormat, VideoCodecsEnum codec)
    {
        Validate(encodedSample, codec);

        lock (_lock)
        {
            if (!Decode(encodedSample, out DecodedPicture pic))
            {
                return new List<RawImage>();
            }

            VideoPixelFormatsEnum format = pixelFormat == VideoPixelFormatsEnum.Rgb ? VideoPixelFormatsEnum.Rgb : VideoPixelFormatsEnum.Bgr;
            int stride = pic.Width * 3;
            int size = stride * pic.Height;
            if (_rawBufferSize < size)
            {
                NativeMemory.Free(_rawBuffer);
                _rawBuffer = null;
                _rawBufferSize = 0;
                _rawBuffer = (byte*)NativeMemory.Alloc((nuint)size);
                _rawBufferSize = size;
            }
            YuvToRgb.ToRgb(pic, _rawBuffer, stride, format == VideoPixelFormatsEnum.Bgr);

            return new List<RawImage>
            {
                new RawImage
                {
                    Width = pic.Width,
                    Height = pic.Height,
                    Stride = stride,
                    Sample = (IntPtr)_rawBuffer,
                    PixelFormat = format,
                }
            };
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_lock)
        {
            Release();
        }
        GC.SuppressFinalize(this);
    }

    private void Release()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        if (_decoder != null)
        {
            _decoder->Uninitialize();
            WelsDec.WelsDestroyDecoder(_decoder);
            _decoder = null;
        }

        NativeMemory.Free(_rawBuffer);
        _rawBuffer = null;
        _rawBufferSize = 0;

        if (_encoder != null)
        {
            _encoder->Uninitialize();
            WelsEnc.WelsDestroySVCEncoder(_encoder);
            _encoder = null;
        }
        _encWidth = _encHeight = 0;

        NativeMemory.Free(_bsInfo);
        _bsInfo = null;
        NativeMemory.Free(_yuvBuffer);
        _yuvBuffer = null;
        _yuvBufferSize = 0;
    }

    private static void CheckCodec(VideoCodecsEnum codec)
    {
        if (codec != VideoCodecsEnum.H264)
        {
            throw new NotSupportedException($"SIPSorcery.H264 encodes H264, not {codec}.");
        }
    }

    private static void CheckFrame(int width, int height, int stride, VideoPixelFormatsEnum pixelFormat, string strideName)
    {
        if (!RgbToI420.IsSupported(pixelFormat))
        {
            throw new NotSupportedException($"SIPSorcery.H264 does not encode {pixelFormat} frames; use I420, NV12, BGRA, RGBA, BGR or RGB.");
        }
        if (width < MIN_ENCODE_DIMENSION || height < MIN_ENCODE_DIMENSION)
        {
            throw new ArgumentException($"A {width}x{height} frame is too small to encode; the width and height must be at least {MIN_ENCODE_DIMENSION}.");
        }
        if (stride < RgbToI420.MinStride(pixelFormat, width))
        {
            throw new ArgumentException($"A stride of {stride} is too small for a {width} pixel wide {pixelFormat} frame.", strideName);
        }
    }

    // Now, in milliseconds since the first frame, the unit the encoder's timestamps take. Counted
    // from the first frame rather than from the codec's creation, so time spent waiting for a
    // peer to connect does not show in the encoder's measure of the frame rate.
    private long ArrivalTimeMs()
    {
        if (_firstFrameTicks == 0)
        {
            _firstFrameTicks = Stopwatch.GetTimestamp();
        }
        return (long)Stopwatch.GetElapsedTime(_firstFrameTicks).TotalMilliseconds;
    }

    private int TargetBitrateBps(int width, int height)
    {
        if (_targetBitrateKbps > 0)
        {
            return (int)Math.Min((long)_targetBitrateKbps * 1000, int.MaxValue);
        }
        double auto = (double)width * height * _frameRate * AUTO_BITS_PER_PIXEL;
        return (int)Math.Clamp(auto, MIN_AUTO_BITRATE_BPS, int.MaxValue);
    }

    /// <summary>
    /// Encodes the frame at <paramref name="src"/> and writes the access unit to
    /// <paramref name="output"/>. Called under the lock with the frame checked.
    /// </summary>
    private int Encode(int width, int height, byte* src, int stride, VideoPixelFormatsEnum pixelFormat, IBufferWriter<byte> output)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureEncoder(width, height);

        SSourcePicture pic = default;
        pic.iColorFormat = (int)videoFormatI420;
        pic.iPicWidth = width;
        pic.iPicHeight = height;
        pic.uiTimeStamp = ArrivalTimeMs();

        int uvStride = (width + 1) >> 1;
        int chromaSize = uvStride * ((height + 1) >> 1);
        switch (pixelFormat)
        {
            case VideoPixelFormatsEnum.I420:
            {
                int srcUvStride = (stride + 1) >> 1;
                byte* u = src + (long)stride * height;
                pic.iStride[0] = stride;
                pic.iStride[1] = pic.iStride[2] = srcUvStride;
                pic.pData[0] = src;
                pic.pData[1] = u;
                pic.pData[2] = u + (long)srcUvStride * ((height + 1) >> 1);
                break;
            }
            case VideoPixelFormatsEnum.NV12:
            {
                byte* u = EnsureYuvBuffer(2 * chromaSize);
                byte* v = u + chromaSize;
                RgbToI420.SplitNv12(src, stride, width, height, u, v, uvStride);
                pic.iStride[0] = stride;
                pic.iStride[1] = pic.iStride[2] = uvStride;
                pic.pData[0] = src;
                pic.pData[1] = u;
                pic.pData[2] = v;
                break;
            }
            default:
            {
                int lumaSize = width * height;
                byte* y = EnsureYuvBuffer(lumaSize + 2 * chromaSize);
                byte* u = y + lumaSize;
                byte* v = u + chromaSize;
                RgbToI420.ConvertRgb(src, stride, pixelFormat, width, height, y, width, u, v, uvStride);
                pic.iStride[0] = width;
                pic.iStride[1] = pic.iStride[2] = uvStride;
                pic.pData[0] = y;
                pic.pData[1] = u;
                pic.pData[2] = v;
                break;
            }
        }

        if (_forceKeyFrame)
        {
            _forceKeyFrame = false;
            _encoder->ForceIntraFrame(true);
        }

        SFrameBSInfo* info = _bsInfo;
        int rv = _encoder->EncodeFrame(&pic, info);
        if (rv != (int)cmResultSuccess)
        {
            logger.LogWarning("H264 encode of a {Width}x{Height} frame failed with {Result}.", width, height, (CM_RETURN)rv);
            if (rv == (int)cmMallocMemeError)
            {
                // The encoder freed its context; initialise it again, with a key frame, next time.
                _encWidth = _encHeight = 0;
            }
            return 0;
        }

        if (info->eFrameType == videoFrameTypeSkip || info->eFrameType == videoFrameTypeInvalid)
        {
            return 0;
        }

        // The layers' NAL units, each with its start code, in order: the access unit.
        int length = 0;
        for (int i = 0; i < info->iLayerNum; i++)
        {
            ref SLayerBSInfo layer = ref info->sLayerInfo[i];
            int layerSize = 0;
            for (int n = 0; n < layer.iNalCount; n++)
            {
                layerSize += layer.pNalLengthInByte[n];
            }
            if (layerSize > 0)
            {
                new ReadOnlySpan<byte>(layer.pBsBuf, layerSize).CopyTo(output.GetSpan(layerSize));
                output.Advance(layerSize);
                length += layerSize;
            }
        }
        return length;
    }

    private byte* EnsureYuvBuffer(int size)
    {
        if (_yuvBufferSize < size)
        {
            NativeMemory.Free(_yuvBuffer);
            _yuvBuffer = null;
            _yuvBufferSize = 0;
            _yuvBuffer = (byte*)NativeMemory.Alloc((nuint)size);
            _yuvBufferSize = size;
        }
        return _yuvBuffer;
    }

    /// <summary>
    /// Creates the encoder if there is none yet, and initialises it for the frame size if it
    /// is not already, as libwebrtc's H264EncoderImpl sets OpenH264 up.
    /// </summary>
    private void EnsureEncoder(int width, int height)
    {
        if (_encoder == null)
        {
            CWelsH264SVCEncoder* encoder = null;
            if (WelsEnc.WelsCreateSVCEncoder(&encoder) != 0 || encoder == null)
            {
                throw new InvalidOperationException("The H.264 encoder could not be created.");
            }

            // The encoder's own log lines go to the codec's logger.
            delegate*<void*, int, string, void> callback = &OnEncoderLog;
            encoder->SetOption(ENCODER_OPTION_TRACE_CALLBACK, &callback);
            int level = logger.IsEnabled(LogLevel.Trace) ? WELS_LOG_INFO :
                logger.IsEnabled(LogLevel.Debug) ? WELS_LOG_WARNING : WELS_LOG_ERROR;
            encoder->SetOption(ENCODER_OPTION_TRACE_LEVEL, &level);

            _encoder = encoder;
            _bsInfo = (SFrameBSInfo*)NativeMemory.AllocZeroed((nuint)sizeof(SFrameBSInfo));
        }

        if (_encWidth == width && _encHeight == height)
        {
            return;
        }

        SEncParamExt param = default;
        _encoder->GetDefaultParams(&param);
        int bitrate = TargetBitrateBps(width, height);
        param.iUsageType = CAMERA_VIDEO_REAL_TIME;
        param.iPicWidth = width;
        param.iPicHeight = height;
        param.iTargetBitrate = bitrate;
        // As libwebrtc: OpenH264's maximum bitrate is not the session's, so it is left unset.
        param.iMaxBitrate = UNSPECIFIED_BIT_RATE;
        param.iRCMode = RC_BITRATE_MODE;
        param.fMaxFrameRate = _frameRate;
        param.bEnableFrameSkip = true;
        param.uiIntraPeriod = (uint)_keyframeIntervalFrames;
        param.uiMaxNalSize = 0;
        // The same parameter set ids for the whole stream, rather than new ones at each IDR.
        param.eSpsPpsIdStrategy = SPS_LISTING;
        param.iMultipleThreadIdc = (ushort)_encoderThreads;
        param.iSpatialLayerNum = 1;
        param.iTemporalLayerNum = 1;
        ref SSpatialLayerConfig layer = ref param.sSpatialLayers[0];
        layer.iVideoWidth = width;
        layer.iVideoHeight = height;
        layer.fFrameRate = _frameRate;
        layer.iSpatialBitrate = bitrate;
        layer.iMaxSpatialBitrate = UNSPECIFIED_BIT_RATE;
        uint slices = EncoderSlices(width, height, _encoderThreads);
        if (slices > 1)
        {
            // A slice for each thread, resized from picture to picture (bUseLoadBalancing, on by
            // default) so the threads take about as long. OpenH264 runs as many threads as
            // there are slices.
            layer.sSliceArgument.uiSliceMode = SM_FIXEDSLCNUM_SLICE;
            layer.sSliceArgument.uiSliceNum = slices;
        }
        else
        {
            layer.sSliceArgument.uiSliceMode = SM_SINGLE_SLICE;
        }

        _encWidth = _encHeight = 0;
        int rv = _encoder->InitializeExt(&param);
        if (rv != (int)cmResultSuccess)
        {
            throw new InvalidOperationException($"The H.264 encoder could not be initialised for a {width}x{height} frame ({(CM_RETURN)rv}).");
        }
        _encWidth = width;
        _encHeight = height;
        // Whatever key frame was asked for, a new stream starts with one.
        _forceKeyFrame = false;
    }

    /// <summary>
    /// The number of slices, one a thread, a frame is coded in: as many as OpenH264 takes for
    /// the size under rate control, where a frame of 48 macroblocks or fewer is one slice
    /// (MIN_NUM_MB_PER_SLICE) and each slice must be at least a row group (GOM) of macroblocks.
    /// Where that brings the count down it comes down to an even number, and where it comes
    /// down to 1, which OpenH264 would refuse rather than code, the frame is one slice on one
    /// thread.
    /// </summary>
    internal static uint EncoderSlices(int width, int height, int threads)
    {
        int mbWidth = (width + 15) >> 4, mbHeight = (height + 15) >> 4;
        if (threads <= 1 || mbWidth * mbHeight <= WelsEnc.MIN_NUM_MB_PER_SLICE)
        {
            return 1;
        }
        // SliceArgumentValidationFixedSliceMode's checks under rate control.
        SSliceArgument slicing = default;
        slicing.uiSliceNum = (uint)threads;
        WelsEnc.GomValidCheckSliceNum(mbWidth, mbHeight, &slicing.uiSliceNum);
        return slicing.uiSliceNum > 1 && WelsEnc.GomValidCheckSliceMbNum(mbWidth, mbHeight, &slicing) ? slicing.uiSliceNum : 1;
    }

    private static void OnEncoderLog(void* ctx, int level, string message)
    {
        if (level <= WELS_LOG_ERROR)
        {
            logger.LogWarning("H264 encoder: {Message}", message);
        }
        else if (level <= WELS_LOG_WARNING)
        {
            logger.LogDebug("H264 encoder: {Message}", message);
        }
        else
        {
            logger.LogTrace("H264 encoder: {Message}", message);
        }
    }

    private static void Validate(byte[] encodedSample, VideoCodecsEnum codec)
    {
        if (encodedSample == null)
        {
            throw new ArgumentNullException(nameof(encodedSample));
        }
        if (codec != VideoCodecsEnum.H264)
        {
            throw new NotSupportedException($"SIPSorcery.H264 decodes H264, not {codec}.");
        }
    }

    /// <summary>
    /// Decodes one access unit with <c>DecodeFrameNoDelay</c>, so the picture comes out of the
    /// same call, and returns it if there is one. The picture's planes belong to the decoder and
    /// are valid until the next call. Called under the lock.
    /// </summary>
    private bool Decode(byte[] encodedSample, out DecodedPicture pic)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(H264Codec));
        }

        pic = default;

        // DecodeFrame2 reads an empty buffer as the end of the stream, so it is not passed on.
        if (encodedSample.Length == 0)
        {
            return false;
        }

        EnsureDecoder();

        byte** dst = stackalloc byte*[3];
        dst[0] = dst[1] = dst[2] = null;
        SBufferInfo info = default;
        DECODING_STATE state;
        fixed (byte* src = encodedSample)
        {
            state = _decoder->DecodeFrameNoDelay(src, encodedSample.Length, dst, &info);
        }

        KeyFrameRequired = (state & ~DECODING_STATE.dsFramePending) != DECODING_STATE.dsErrorFree;
        if (KeyFrameRequired)
        {
            logger.LogDebug("H264 decode of a {Length} byte access unit returned state 0x{State:X}.", encodedSample.Length, (int)state);
        }

        if (info.iBufferStatus != 1)
        {
            // The decoder puts the pictures of a stream whose profile_idc is not Baseline through
            // its reordering buffer and holds each one until the next arrives, even with no B
            // slices to reorder: Main profile streams that use only Constrained Baseline tools
            // would come out a picture late. The held picture is taken out straight away.
            dst[0] = dst[1] = dst[2] = null;
            info = default;
            _decoder->ReleaseHeldPicture(dst, &info);
        }

        if (info.iBufferStatus != 1 || dst[0] == null)
        {
            return false;
        }

        pic.Y = dst[0];
        pic.U = dst[1];
        pic.V = dst[2];
        pic.Width = info.UsrData.sSystemBuffer.iWidth;
        pic.Height = info.UsrData.sSystemBuffer.iHeight;
        pic.YStride = info.UsrData.sSystemBuffer.iStride[0];
        pic.UVStride = info.UsrData.sSystemBuffer.iStride[1];
        pic.Matrix = YuvToRgbMatrix.FromSps(_decoder->CurrentSps);
        return pic.Width > 0 && pic.Height > 0;
    }

    private void EnsureDecoder()
    {
        if (_decoder != null)
        {
            return;
        }

        CWelsDecoder* decoder = null;
        if (WelsDec.WelsCreateDecoder(&decoder) != 0 || decoder == null)
        {
            throw new InvalidOperationException("The H.264 decoder could not be created.");
        }

        // The decoder's own log lines go to the codec's logger. Its errors are mostly about
        // damaged data, which a lossy network produces routinely, so they are warnings here.
        delegate*<void*, int, string, void> callback = &OnDecoderLog;
        decoder->SetOption(DECODER_OPTION.DECODER_OPTION_TRACE_CALLBACK, &callback);
        int level = logger.IsEnabled(LogLevel.Trace) ? WELS_LOG_INFO :
            logger.IsEnabled(LogLevel.Debug) ? WELS_LOG_WARNING : WELS_LOG_ERROR;
        decoder->SetOption(DECODER_OPTION.DECODER_OPTION_TRACE_LEVEL, &level);

        // As OpenH264's own decoder tests: all layers, error concealment by slice copy.
        SDecodingParam param = default;
        param.uiTargetDqLayer = byte.MaxValue;
        param.eEcActiveIdc = ERROR_CON_IDC.ERROR_CON_SLICE_COPY;
        param.sVideoProperty.eVideoBsType = VIDEO_BITSTREAM_TYPE.VIDEO_BITSTREAM_DEFAULT;
        if (decoder->Initialize(&param) != 0)
        {
            WelsDec.WelsDestroyDecoder(decoder);
            throw new InvalidOperationException("The H.264 decoder could not be initialised.");
        }

        _decoder = decoder;
    }

    private static void OnDecoderLog(void* ctx, int level, string message)
    {
        if (level <= WELS_LOG_ERROR)
        {
            logger.LogWarning("H264 decoder: {Message}", message);
        }
        else if (level <= WELS_LOG_WARNING)
        {
            logger.LogDebug("H264 decoder: {Message}", message);
        }
        else
        {
            logger.LogTrace("H264 decoder: {Message}", message);
        }
    }
}
