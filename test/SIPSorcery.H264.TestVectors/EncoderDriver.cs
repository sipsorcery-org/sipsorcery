//-----------------------------------------------------------------------------
// Filename: EncoderDriver.cs
//
// Description: The encoder API (CWelsH264SVCEncoder, E8) for the frame by
// frame acceptance tests: IdrTests (E4b, the first frame of each
// configuration), StreamTests (E5b, P streams), RcTests (E6, rate controlled
// streams), PreprocessTests and UpstreamEncoderHashTests (E7, the
// preprocessing) and ApiTests (E8, scripted API calls).
//
// EncoderDriver creates the encoder with WelsCreateSVCEncoder, sends its
// trace to a callback (no stderr), and hands the harness's view of each
// EncodeFrame back: the return code, the frame type, the layers and the
// SHA1s. Two things come from the encoder's internals, as port additions the
// API does not have: the last frame's reconstruction (sWelsEncCtx.pLastFsnr)
// and, from the FreeMemorySvc INFO log, what the encoder's CMemoryAlign still
// counted in use when it freed its memory.
//
// Configurations the port does not code (CABAC, screen content, several
// spatial layers, SM_SIZELIMITED_SLICE, threads) fail Initialize with
// cmInitParaError and an error log naming them.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 26 Nov 2026  Claude          Created (E5b) from IdrTests.cs's IdrEncoder (E4b), with the
//                              reference strategy, the LTR state, the VAA, the MVD cost
//                              table, the spatial pictures and the frame loop for P frames.
// 26 Nov 2026  Claude          Rate control (E6): WelsRcInitModule, the frame skip checks, the
//                              max bit rate windows, AnalyzePictureComplexity, padding and the
//                              last timestamp.
// 27 Nov 2026  Claude          The library's CWelsPreProcess in place of the hand port, Initialize
//                              (api=base), AddPrefixNal and the coded size (E7).
// 27 Nov 2026  Claude          A thin wrapper over the library's CWelsH264SVCEncoder in place of
//                              the hand driven init, frame loop and release (E8). Checks
//                              iFrameSizeInBytes against the NAL units.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using SIPSorcery.H264.Encoder;
using static SIPSorcery.H264.Encoder.WelsEnc;

namespace SIPSorcery.H264.TestVectors;

/// <summary>
/// What EncodeFrame returned and wrote, as the harness prints it: the return codes, the
/// frame type, the temporal id of each VCL layer, every layer's NAL units and the SHA1 of
/// the whole frame. Comparable says whether the reconstruction is what a decoder outputs
/// for these bytes: false for a frame the encoder does not deblock (the non-reference
/// frames of the highest temporal layer) while the slices ask for the filter.
/// InternalReturnCode is sWelsEncCtx.iEncoderError after the call, for the test output only.
/// </summary>
internal sealed record EncodedFrame(int ReturnCode, int InternalReturnCode, string FrameType, string TemporalIds,
                                    string Layers, string Sha1, byte[] Bytes, bool Comparable);

/// <summary>
/// The encoder API for the acceptance tests (see the file header). One instance is one
/// encoder.
/// </summary>
internal sealed unsafe partial class EncoderDriver : IDisposable
{
    private CWelsH264SVCEncoder* _enc;
    private SFrameBSInfo* _info;
    private GCHandle _self;
    private int _iPicWidth;
    private int _iPicHeight;
    private uint _uiMemoryLeft;
    private readonly List<string> _log = new();

    public EncoderDriver()
    {
        _info = (SFrameBSInfo*)NativeMemory.AllocZeroed((nuint)sizeof(SFrameBSInfo));
        CWelsH264SVCEncoder* pEnc = null;
        if (WelsCreateSVCEncoder(&pEnc) != 0)
        {
            throw new InvalidOperationException("WelsCreateSVCEncoder failed.");
        }
        _enc = pEnc;

        // The trace goes to Trace, at INFO so that FreeMemorySvc's memory check reaches it.
        _self = GCHandle.Alloc(this);
        void* ctx = (void*)GCHandle.ToIntPtr(_self);
        delegate*<void*, int, string, void> callback = &Trace;
        int level = codec_app_def.WELS_LOG_INFO;
        _enc->SetOption(ENCODER_OPTION.ENCODER_OPTION_TRACE_CALLBACK_CONTEXT, &ctx);
        _enc->SetOption(ENCODER_OPTION.ENCODER_OPTION_TRACE_CALLBACK, &callback);
        _enc->SetOption(ENCODER_OPTION.ENCODER_OPTION_TRACE_LEVEL, &level);
    }

    /// <summary>The encoder, for the tests that call the API directly.</summary>
    public CWelsH264SVCEncoder* Encoder => _enc;

    /// <summary>The error and warning lines the encoder logged.</summary>
    public IReadOnlyList<string> Log => _log;

    public int PicWidth => _iPicWidth;
    public int PicHeight => _iPicHeight;

    private sWelsEncCtx* Ctx => _enc == null ? null : _enc->EncContext;

    /// <summary>
    /// The top spatial layer's size (E7): the source's, or smaller when the preprocessing
    /// downsamples the source to the layer. The source's when the encoder is not initialised.
    /// </summary>
    public int CodedWidth => Ctx == null ? _iPicWidth
        : Ctx->pSvcParam->sDependencyLayers[Ctx->pSvcParam->iSpatialLayerNum - 1].iActualWidth;
    public int CodedHeight => Ctx == null ? _iPicHeight
        : Ctx->pSvcParam->sDependencyLayers[Ctx->pSvcParam->iSpatialLayerNum - 1].iActualHeight;

    /// <summary>
    /// Initialize (api=base) or InitializeExt with the settings applied, as the harness does.
    /// Returns the API's code.
    /// </summary>
    public int Initialize(string settings)
    {
        ParamSetEncoder.ParseSettings(settings, out SEncParamExt ext, out SEncParamBase bas, out bool isBase);
        return isBase ? Initialize(bas) : InitializeExt(ext);
    }

    /// <summary>ISVCEncoder::Initialize. Returns the API's code.</summary>
    public int Initialize(SEncParamBase bas)
    {
        _iPicWidth = bas.iPicWidth;
        _iPicHeight = bas.iPicHeight;
        return _enc->Initialize(&bas);
    }

    /// <summary>ISVCEncoder::InitializeExt. Returns the API's code.</summary>
    public int InitializeExt(SEncParamExt ext)
    {
        _iPicWidth = ext.iPicWidth;
        _iPicHeight = ext.iPicHeight;
        return _enc->InitializeExt(&ext);
    }

    /// <summary>ISVCEncoder::ForceIntraFrame.</summary>
    public int ForceIntraFrame(bool bIDR) => _enc->ForceIntraFrame(bIDR);

    /// <summary>ISVCEncoder::SetOption with the option's value.</summary>
    public int SetOption<T>(ENCODER_OPTION eOptionId, T value) where T : unmanaged => _enc->SetOption(eOptionId, &value);

    /// <summary>The source picture size EncodeFrame passes, after a SetOption that changes it.</summary>
    public void SetPictureSize(int w, int h)
    {
        _iPicWidth = w;
        _iPicHeight = h;
    }

    /// <summary>ISVCEncoder::GetOption into a value that starts as <paramref name="value"/>.</summary>
    public int GetOption<T>(ENCODER_OPTION eOptionId, ref T value) where T : unmanaged
    {
        fixed (T* p = &value)
        {
            return _enc->GetOption(eOptionId, p);
        }
    }

    /// <summary>ISVCEncoder::EncodeParameterSets, as the harness prints the output.</summary>
    public EncodedFrame EncodeParameterSets()
    {
        *_info = default;
        int rv = _enc->EncodeParameterSets(_info);
        return Output(rv, false);
    }

    /// <summary>
    /// ISVCEncoder::EncodeFrame of a packed I420 picture of the initialised size, as the
    /// harness passes it.
    /// </summary>
    public EncodedFrame EncodeFrame(byte[] buf, long uiTimeStamp) =>
        EncodeFrame(buf, _iPicWidth, _iPicHeight, (int)EVideoFormatType.videoFormatI420, uiTimeStamp);

    /// <summary>ISVCEncoder::EncodeFrame of a packed w x h picture said to be in the given format.</summary>
    public EncodedFrame EncodeFrame(byte[] buf, int w, int h, int iColorFormat, long uiTimeStamp)
    {
        int rv;
        fixed (byte* p = buf)
        {
            SSourcePicture pic = default;
            pic.iPicWidth = w;
            pic.iPicHeight = h;
            pic.iColorFormat = iColorFormat;
            pic.iStride[0] = w;
            pic.iStride[1] = pic.iStride[2] = w >> 1;
            pic.pData[0] = p;
            pic.pData[1] = p + w * h;
            pic.pData[2] = p + w * h + (w >> 1) * (h >> 1);
            pic.uiTimeStamp = uiTimeStamp;
            *_info = default;
            rv = _enc->EncodeFrame(&pic, _info);
        }
        return Output(rv, true);
    }

    /// <summary>The harness's view of the output: every layer's NAL units and each VCL layer's temporal id, then the whole frame.</summary>
    private EncodedFrame Output(int rv, bool frame)
    {
        var layers = new StringBuilder();
        var tids = new StringBuilder();
        var bytes = new List<byte>();
        SLayerBSInfo* pLayers = (SLayerBSInfo*)&_info->sLayerInfo;
        for (int i = 0; i < _info->iLayerNum; i++)
        {
            SLayerBSInfo* l = &pLayers[i];
            if (layers.Length != 0)
            {
                layers.Append('|');
            }
            layers.Append(l->uiLayerType == (byte)LAYER_TYPE.VIDEO_CODING_LAYER ? "V=" : "N=");
            if (l->uiLayerType == (byte)LAYER_TYPE.VIDEO_CODING_LAYER)
            {
                tids.Append(tids.Length == 0 ? "" : ",").Append(l->uiTemporalId.ToString(CultureInfo.InvariantCulture));
            }
            byte* q = l->pBsBuf;
            for (int j = 0; j < l->iNalCount; j++)
            {
                var nal = new ReadOnlySpan<byte>(q, l->pNalLengthInByte[j]);
                if (j != 0)
                {
                    layers.Append(',');
                }
                layers.Append(CultureInfo.InvariantCulture, $"{nal.Length}:{Sha1(nal)[..8]}");
                bytes.AddRange(nal.ToArray());
                q += l->pNalLengthInByte[j];
            }
        }
        byte[] all = bytes.ToArray();
        // The harness prints the sum of the NAL units, but a caller reads iFrameSizeInBytes,
        // which upstream sets to the same sum, so the two must agree.
        if (frame && rv == 0 && _info->iFrameSizeInBytes != all.Length)
        {
            throw new InvalidOperationException(
                $"iFrameSizeInBytes is {_info->iFrameSizeInBytes} but the layers hold {all.Length} bytes.");
        }
        sWelsEncCtx* pCtx = Ctx;
        int iInternal = pCtx == null ? ENC_RETURN_MEMALLOCERR : pCtx->iEncoderError;
        bool comparable = frame && pCtx != null && pCtx->pLastFsnr != null
                          && (pCtx->bLastFsnrDeblocked || pCtx->pSvcParam->iLoopFilterDisableIdc == 1);
        return new EncodedFrame(rv, iInternal, FrameTypeName(_info->eFrameType), tids.Length == 0 ? "-" : tids.ToString(),
                                layers.Length == 0 ? "-" : layers.ToString(), all.Length == 0 ? "-" : Sha1(all), all,
                                comparable);
    }

    /// <summary>The top left w x h of a plane of the last frame's reconstructed picture.</summary>
    public byte[] Reconstruction(int plane, int w, int h)
    {
        SPicture* pDecPic = Ctx->pLastFsnr;
        var a = new byte[w * h];
        for (int r = 0; r < h; r++)
        {
            new ReadOnlySpan<byte>((byte*)pDecPic->pData[plane] + r * pDecPic->iLineSize[plane], w).CopyTo(a.AsSpan(r * w));
        }
        return a;
    }

    internal static string Sha1(ReadOnlySpan<byte> d) => Convert.ToHexString(SHA1.HashData(d)).ToLowerInvariant();

    internal static string FrameTypeName(EVideoFrameType t) => t switch
    {
        EVideoFrameType.videoFrameTypeInvalid => "invalid",
        EVideoFrameType.videoFrameTypeIDR => "IDR",
        EVideoFrameType.videoFrameTypeI => "I",
        EVideoFrameType.videoFrameTypeP => "P",
        EVideoFrameType.videoFrameTypeSkip => "skip",
        EVideoFrameType.videoFrameTypeIPMixed => "IPMixed",
        _ => "?",
    };

    [GeneratedRegex(@"FreeMemorySvc\(\), verify memory usage \((\d+) bytes\) after free")]
    private static partial Regex MemoryUsage();

    private static void Trace(void* ctx, int level, string message)
    {
        var self = (EncoderDriver)GCHandle.FromIntPtr((nint)ctx).Target!;
        Match m = MemoryUsage().Match(message);
        if (m.Success)
        {
            self._uiMemoryLeft = uint.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        }
        if (level <= codec_app_def.WELS_LOG_WARNING)
        {
            self._log.Add(message);
        }
    }

    /// <summary>
    /// ISVCEncoder::Uninitialize. Returns what the encoder's CMemoryAlign counted in use when
    /// FreeMemorySvc freed the memory, which is 0 when every allocation was freed; 0 too
    /// when the encoder was never initialised.
    /// </summary>
    public uint Release()
    {
        if (_enc != null)
        {
            _enc->Uninitialize();
        }
        return _uiMemoryLeft;
    }

    public void Dispose()
    {
        if (_enc != null)
        {
            WelsDestroySVCEncoder(_enc);
            _enc = null;
        }
        if (_self.IsAllocated)
        {
            _self.Free();
        }
        if (_info != null)
        {
            NativeMemory.Free(_info);
            _info = null;
        }
    }
}
