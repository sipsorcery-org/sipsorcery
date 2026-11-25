//-----------------------------------------------------------------------------
// Filename: H264Codec.cs
//
// Description: SIPSorcery's IVideoEncoder for the pure C# OpenH264 port. It
// decodes the Annex B access units SIPSorcery's H264 depacketiser produces to
// I420, BGR or RGB. Decode only for now: the encoder follows the decoder in the
// port. Not part of the OpenH264 port itself.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 25 Nov 2026  Claude          Created (D7).
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using SIPSorcery.H264.Decoder;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.H264;

/// <summary>
/// A pure C# H.264 decoder for SIPSorcery, ported from Cisco's OpenH264. Each call to
/// <see cref="DecodeVideo"/> takes one access unit in Annex B format (start code delimited
/// NAL units, as <c>H264Depacketiser</c> and <c>RTCPeerConnection.OnVideoFrameReceived</c>
/// deliver them) and returns the picture it decodes to, if any.
/// </summary>
/// <remarks>
/// <para>
/// The port covers the profile WebRTC uses, Constrained Baseline (<c>profile-level-id=42e01f</c>),
/// at any level. Its output is checked bit for bit against OpenH264's own on the conformance
/// streams. Streams using CABAC or B slices (Main and High profile) are not decoded: their
/// slices fail and are concealed.
/// </para>
/// <para>
/// Pictures are output as soon as their access unit is decoded, with no reordering delay, which
/// Constrained Baseline allows. Damaged or missing data is concealed by copying from the
/// previous picture, and <see cref="KeyFrameRequired"/> says when the receiver should ask the
/// sender for a key frame to recover.
/// </para>
/// <para>
/// BGR and RGB output is converted with the colour matrix and range the stream signals in its
/// sequence parameter set: BT.709 or BT.601, full or limited range. A stream that signals
/// nothing is taken to be BT.601 limited range, as browsers take it.
/// </para>
/// <para>
/// Decode only for now: the encode methods throw <see cref="NotSupportedException"/>. Calls are
/// serialised internally. Dispose the codec to release the decoder's native memory.
/// </para>
/// </remarks>
public sealed unsafe class H264Codec : IVideoEncoder
{
    /// <summary>The RTP payload type SIPSorcery suggests for H.264.</summary>
    public const int H264_SUGGESTED_FORMAT_ID = 100;

    /// <summary>
    /// The SDP format parameters for what the decoder handles: Constrained Baseline, packetization
    /// mode 1 (single NAL unit, STAP-A and FU-A packets), and any level from the sender.
    /// </summary>
    public const string H264_FORMAT_PARAMETERS = "level-asymmetry-allowed=1;packetization-mode=1;profile-level-id=42e01f";

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
    private bool _disposed;

    /// <summary>Creates a codec. The decoder itself is created on the first access unit.</summary>
    public H264Codec()
    { }

    /// <summary>Releases the decoder if the codec was not disposed.</summary>
    ~H264Codec() => Release();

    /// <inheritdoc/>
    public List<VideoFormat> SupportedFormats => _supportedFormats;

    /// <summary>
    /// True when the last access unit decoded was not clean: parameter sets were missing, a
    /// reference picture was lost, the data was damaged, or the picture predicts from one that
    /// was. Any picture output was concealed, and the damage carries forward until the next IDR
    /// picture, so the receiver should ask the sender for a key frame (in WebRTC, an RTCP PLI).
    /// False after an access unit that decodes cleanly.
    /// </summary>
    public bool KeyFrameRequired { get; private set; }

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

    /// <summary>Not supported yet: the H.264 encoder has not been ported.</summary>
    public byte[] EncodeVideo(int width, int height, byte[] sample, VideoPixelFormatsEnum pixelFormat, VideoCodecsEnum codec) =>
        throw new NotSupportedException("SIPSorcery.H264 only decodes so far; the encoder has not been ported yet.");

    /// <summary>Not supported yet: the H.264 encoder has not been ported.</summary>
    public byte[] EncodeVideoFaster(RawImage rawImage, VideoCodecsEnum codec) =>
        throw new NotSupportedException("SIPSorcery.H264 only decodes so far; the encoder has not been ported yet.");

    /// <summary>Does nothing until the encoder is ported, so a key frame request can't break a receive-only session.</summary>
    public void ForceKeyFrame()
    { }

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
