//-----------------------------------------------------------------------------
// Filename: BenchCodecs.cs
//
// Description: The encoder and decoder the benchmarks time, driven through the
// port's own API.
//
// The encoder is set up as H264Codec sets it up (libwebrtc's settings: bitrate
// mode rate control with frame skipping, one slice, one thread, H264Codec's
// automatic bitrate). The difference is the timestamps: H264Codec stamps each
// frame with the time it arrives, so a benchmark encoding faster than real time
// would look to the rate control like a higher frame rate, and it would skip
// frames. Here frame n is stamped n / FRAME_RATE seconds, so the rate control
// sees 30 frames a second however fast the frames are encoded, and the output
// is the same on every run.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 27 Nov 2026  Claude          Created (Phase 5).
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Runtime.InteropServices;
using SIPSorcery.H264;
using SIPSorcery.H264.Decoder;
using SIPSorcery.H264.Encoder;
using static SIPSorcery.H264.codec_app_def;
using static SIPSorcery.H264.codec_def;

namespace H264Benchmarks;

internal static class Resolutions
{
    public static (int Width, int Height) Parse(string resolution)
    {
        string[] parts = resolution.Split('x');
        return (int.Parse(parts[0]), int.Parse(parts[1]));
    }
}

internal sealed unsafe class BenchEncoder : IDisposable
{
    public const int FRAME_RATE = 30;

    // H264Codec's automatic bitrate, AUTO_BITS_PER_PIXEL.
    private const double AUTO_BITS_PER_PIXEL = 0.1;

    private CWelsH264SVCEncoder* _encoder;
    private SFrameBSInfo* _info;
    private readonly int _width;
    private readonly int _height;
    private long _frames;

    public BenchEncoder(int width, int height, int keyframeIntervalFrames = FRAME_RATE)
    {
        _width = width;
        _height = height;

        CWelsH264SVCEncoder* encoder = null;
        if (WelsEnc.WelsCreateSVCEncoder(&encoder) != 0 || encoder == null)
        {
            throw new InvalidOperationException("The H.264 encoder could not be created.");
        }
        _encoder = encoder;
        _info = (SFrameBSInfo*)NativeMemory.AllocZeroed((nuint)sizeof(SFrameBSInfo));

        int level = WELS_LOG_ERROR;
        _encoder->SetOption(ENCODER_OPTION.ENCODER_OPTION_TRACE_LEVEL, &level);

        SEncParamExt param = default;
        _encoder->GetDefaultParams(&param);
        int bitrate = (int)(width * (double)height * FRAME_RATE * AUTO_BITS_PER_PIXEL);
        param.iUsageType = EUsageType.CAMERA_VIDEO_REAL_TIME;
        param.iPicWidth = width;
        param.iPicHeight = height;
        param.iTargetBitrate = bitrate;
        param.iMaxBitrate = UNSPECIFIED_BIT_RATE;
        param.iRCMode = RC_MODES.RC_BITRATE_MODE;
        param.fMaxFrameRate = FRAME_RATE;
        param.bEnableFrameSkip = true;
        param.uiIntraPeriod = (uint)keyframeIntervalFrames;
        param.uiMaxNalSize = 0;
        param.eSpsPpsIdStrategy = EParameterSetStrategy.SPS_LISTING;
        param.iMultipleThreadIdc = 1;
        param.iSpatialLayerNum = 1;
        param.iTemporalLayerNum = 1;
        ref SSpatialLayerConfig layer = ref param.sSpatialLayers[0];
        layer.iVideoWidth = width;
        layer.iVideoHeight = height;
        layer.fFrameRate = FRAME_RATE;
        layer.iSpatialBitrate = bitrate;
        layer.iMaxSpatialBitrate = UNSPECIFIED_BIT_RATE;
        layer.sSliceArgument.uiSliceMode = SliceModeEnum.SM_SINGLE_SLICE;

        int rv = _encoder->InitializeExt(&param);
        if (rv != (int)CM_RETURN.cmResultSuccess)
        {
            throw new InvalidOperationException($"The H.264 encoder could not be initialised ({(CM_RETURN)rv}).");
        }
    }

    /// <summary>The frames the rate control skipped.</summary>
    public int Skipped { get; private set; }

    /// <summary>Encodes one I420 frame. Returns the access unit's size, 0 if it was skipped.</summary>
    public int Encode(byte[] frame) => Encode(frame, null);

    /// <summary>Encodes one I420 frame, adding the access unit to <paramref name="output"/> if given.</summary>
    public int Encode(byte[] frame, List<byte[]>? output)
    {
        int lumaSize = _width * _height;
        int chromaSize = (_width / 2) * (_height / 2);
        fixed (byte* src = frame)
        {
            SSourcePicture pic = default;
            pic.iColorFormat = (int)EVideoFormatType.videoFormatI420;
            pic.iPicWidth = _width;
            pic.iPicHeight = _height;
            pic.uiTimeStamp = _frames++ * 1000 / FRAME_RATE;
            pic.iStride[0] = _width;
            pic.iStride[1] = pic.iStride[2] = _width / 2;
            pic.pData[0] = src;
            pic.pData[1] = src + lumaSize;
            pic.pData[2] = src + lumaSize + chromaSize;

            int rv = _encoder->EncodeFrame(&pic, _info);
            if (rv != (int)CM_RETURN.cmResultSuccess)
            {
                throw new InvalidOperationException($"The H.264 encode failed ({(CM_RETURN)rv}).");
            }
        }

        if (_info->eFrameType == EVideoFrameType.videoFrameTypeSkip)
        {
            Skipped++;
            return 0;
        }

        int length = 0;
        for (int i = 0; i < _info->iLayerNum; i++)
        {
            ref SLayerBSInfo layer = ref _info->sLayerInfo[i];
            for (int n = 0; n < layer.iNalCount; n++)
            {
                length += layer.pNalLengthInByte[n];
            }
        }
        if (output != null && length > 0)
        {
            // The layers' NAL units, in order: the access unit.
            var au = new byte[length];
            int offset = 0;
            for (int i = 0; i < _info->iLayerNum; i++)
            {
                ref SLayerBSInfo layer = ref _info->sLayerInfo[i];
                int layerSize = 0;
                for (int n = 0; n < layer.iNalCount; n++)
                {
                    layerSize += layer.pNalLengthInByte[n];
                }
                new ReadOnlySpan<byte>(layer.pBsBuf, layerSize).CopyTo(au.AsSpan(offset));
                offset += layerSize;
            }
            output.Add(au);
        }
        return length;
    }

    public void Dispose()
    {
        if (_encoder != null)
        {
            _encoder->Uninitialize();
            WelsEnc.WelsDestroySVCEncoder(_encoder);
            _encoder = null;
            NativeMemory.Free(_info);
            _info = null;
        }
    }
}

internal sealed unsafe class BenchDecoder : IDisposable
{
    private CWelsDecoder* _decoder;

    public BenchDecoder()
    {
        CWelsDecoder* decoder = null;
        if (WelsDec.WelsCreateDecoder(&decoder) != 0 || decoder == null)
        {
            throw new InvalidOperationException("The H.264 decoder could not be created.");
        }
        _decoder = decoder;
        int level = WELS_LOG_ERROR;
        _decoder->SetOption(DECODER_OPTION.DECODER_OPTION_TRACE_LEVEL, &level);

        // As H264Codec: all layers, error concealment by slice copy.
        SDecodingParam param = default;
        param.uiTargetDqLayer = byte.MaxValue;
        param.eEcActiveIdc = ERROR_CON_IDC.ERROR_CON_SLICE_COPY;
        param.sVideoProperty.eVideoBsType = VIDEO_BITSTREAM_TYPE.VIDEO_BITSTREAM_DEFAULT;
        if (_decoder->Initialize(&param) != 0)
        {
            throw new InvalidOperationException("The H.264 decoder could not be initialised.");
        }
    }

    /// <summary>
    /// Decodes one access unit. Returns the picture's luma plane, null if there was none, and its
    /// size and stride.
    /// </summary>
    public byte* Decode(byte[] accessUnit, out int width, out int height, out int stride)
    {
        byte** dst = stackalloc byte*[3];
        dst[0] = dst[1] = dst[2] = null;
        SBufferInfo info = default;
        DECODING_STATE state;
        fixed (byte* src = accessUnit)
        {
            state = _decoder->DecodeFrameNoDelay(src, accessUnit.Length, dst, &info);
        }
        if (state != DECODING_STATE.dsErrorFree)
        {
            throw new InvalidOperationException($"The H.264 decode failed (0x{(int)state:X}).");
        }
        width = info.UsrData.sSystemBuffer.iWidth;
        height = info.UsrData.sSystemBuffer.iHeight;
        stride = info.UsrData.sSystemBuffer.iStride[0];
        return info.iBufferStatus == 1 ? dst[0] : null;
    }

    public void Dispose()
    {
        if (_decoder != null)
        {
            _decoder->Uninitialize();
            WelsDec.WelsDestroyDecoder(_decoder);
            _decoder = null;
        }
    }
}

internal static class Clips
{
    /// <summary>
    /// Encodes the test frames' ping-pong cycle as one GOP, an IDR and then P frames, so a
    /// decoder can play it round and round.
    /// </summary>
    public static List<byte[]> Encode(int width, int height, byte[][] frames)
    {
        int period = 2 * (frames.Length - 1);
        using var encoder = new BenchEncoder(width, height, period);
        var clip = new List<byte[]>(period);
        for (int i = 0; i < period; i++)
        {
            encoder.Encode(frames[TestFrames.PingPong(i, frames.Length)], clip);
        }
        return clip;
    }
}
