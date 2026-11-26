//-----------------------------------------------------------------------------
// Filename: IdrTests.cs
//
// Description: E4b acceptance: the first frame (an IDR) the ported encoder
// writes for a table of fixed QP configurations must match the C-only
// reference build's byte for byte (refharness `h264ref idr`,
// reference/idr.tsv), and its reconstruction (the encoder's deblocked
// reference picture) must equal what the ported decoder decodes from those
// bytes.
//
// The encoder API and its init and frame loops come with E8, and the
// preprocessing, the reference list manager and the VAA with E5 and E7, so
// IdrEncoder drives the ported functions the way CWelsH264SVCEncoder::
// InitializeExt, WelsInitEncoderExt, RequestMemorySvc, BuildSpatialPicList and
// WelsEncoderEncodeExt do for the first frame of one spatial layer, and does
// by hand, as upstream does for an IDR, what the unported pieces do (each one
// is marked below). Configurations outside that scope (CABAC, screen content,
// several spatial layers, SM_SIZELIMITED_SLICE, threads, rate control, the
// denoiser, scaling) throw NotSupportedException; the reference table has none.
//
// Set H264_DUMP_DIR to have a failing row write its bytes there, to compare
// with `h264ref idr-dump <name> <out>`.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 26 Nov 2026  Claude          Created (E4b).
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using SIPSorcery.H264.Encoder;
using Xunit;
using Xunit.Abstractions;
using static SIPSorcery.H264.Encoder.WelsEnc;
using static SIPSorcery.H264.WelsCommon;

namespace SIPSorcery.H264.TestVectors;

/// <summary>One row of reference/idr.tsv.</summary>
public sealed record IdrReference(string Name, string Content, string Settings)
{
    public int InitReturnCode { get; set; } = int.MinValue;
    public int EncodeReturnCode { get; set; }
    public string FrameType { get; set; }
    public string Layers { get; set; }
    public string Sha1 { get; set; }
    public int Size { get; set; }

    public override string ToString() => Name;
}

public static class IdrReferences
{
    public const string TableFile = "idr.tsv";

    private static readonly Lazy<IReadOnlyList<IdrReference>> _rows = new(Load);

    public static IReadOnlyList<IdrReference> Rows => _rows.Value;

    public static IEnumerable<object[]> Names => Rows.Select(r => new object[] { r.Name });

    private static IReadOnlyList<IdrReference> Load()
    {
        string path = Path.Combine(AppContext.BaseDirectory, TableFile);
        var rows = new List<IdrReference>();
        var byName = new Dictionary<string, IdrReference>();
        foreach (string line in File.ReadLines(path))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }
            string[] f = line.Split('\t');
            switch (f[0])
            {
                case "config":
                    var r = new IdrReference(f[1], f[2], f[3]);
                    rows.Add(r);
                    byName.Add(r.Name, r);
                    break;
                case "idr":
                    IdrReference c = byName[f[1]];
                    c.InitReturnCode = int.Parse(f[2], CultureInfo.InvariantCulture);
                    c.EncodeReturnCode = int.Parse(f[3], CultureInfo.InvariantCulture);
                    c.FrameType = f[4];
                    c.Layers = f[5];
                    c.Sha1 = f[6];
                    c.Size = int.Parse(f[7], CultureInfo.InvariantCulture);
                    break;
                default:
                    throw new InvalidDataException($"{TableFile}: unknown row {f[0]}.");
            }
        }
        return rows;
    }
}

public class IdrTests
{
    private const string DumpEnvironmentVariable = "H264_DUMP_DIR";

    private readonly ITestOutputHelper _output;

    public IdrTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void TableIsComplete()
    {
        Assert.NotEmpty(IdrReferences.Rows);
        Assert.All(IdrReferences.Rows, r => Assert.NotEqual(int.MinValue, r.InitReturnCode));
        // Most rows code a frame; the two noise rows at a low QP overflow the bitstream buffer.
        Assert.True(IdrReferences.Rows.Count(r => r.FrameType == "IDR") >= 40);
    }

    [Theory]
    [MemberData(nameof(IdrReferences.Names), MemberType = typeof(IdrReferences))]
    public unsafe void IdrMatchesReference(string name)
    {
        IdrReference expected = IdrReferences.Rows.Single(r => r.Name == name);

        using var enc = new IdrEncoder();
        Assert.Equal(expected.InitReturnCode, enc.Initialize(expected.Settings));
        if (expected.InitReturnCode != 0)
        {
            return;
        }

        IdrResult got = enc.EncodeFirstFrame(expected.Content);
        _output.WriteLine($"{name}: EncodeFrame {got.ReturnCode} (WelsEncoderEncodeExt {got.InternalReturnCode}), " +
                          $"{got.FrameType}, {got.Layers}, {got.Bytes.Length} bytes");
        if (got.Sha1 != expected.Sha1 && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(DumpEnvironmentVariable)))
        {
            // Compare with h264ref idr-dump <name> <out>.
            File.WriteAllBytes(Path.Combine(Environment.GetEnvironmentVariable(DumpEnvironmentVariable), $"{name}.cs.264"),
                               got.Bytes);
        }
        Assert.Equal(expected.EncodeReturnCode, got.ReturnCode);
        Assert.Equal(expected.FrameType, got.FrameType);
        Assert.Equal(expected.Layers, got.Layers);
        Assert.Equal(expected.Sha1, got.Sha1);
        Assert.Equal(expected.Size, got.Bytes.Length);

        if (got.ReturnCode == 0)
        {
            // The reconstruction: the decoder's picture from these bytes is the encoder's
            // deblocked reference picture, cropped as the SPS says.
            var frames = new List<(int W, int H, byte[][] Planes)>();
            DecodeResult d = DecoderHash.DecodeChunks(new[] { got.Bytes }, (y, u, v) =>
                frames.Add((y.Width, y.Height, new[] { Copy(y), Copy(u), Copy(v) })));
            Assert.Equal(0, d.ErrorChunks);
            Assert.Single(frames);
            (int w, int h, byte[][] planes) = frames[0];
            _output.WriteLine($"decoded {w}x{h}");
            for (int i = 0; i < 3; i++)
            {
                int pw = i == 0 ? w : w / 2, ph = i == 0 ? h : h / 2;
                byte[] recon = enc.Reconstruction(i, pw, ph);
                Assert.Equal(recon.Length, planes[i].Length);
                int same = recon.AsSpan().CommonPrefixLength(planes[i]);
                Assert.True(same == recon.Length,
                            $"plane {i}: the reconstruction differs from the decoder's at ({same % pw}, {same / pw}).");
            }
        }

        Assert.Equal(0u, enc.Release());
    }

    private static unsafe byte[] Copy(DecoderHash.Plane p)
    {
        var a = new byte[p.Width * p.Height];
        for (int r = 0; r < p.Height; r++)
        {
            new ReadOnlySpan<byte>(p.Data + r * p.Stride, p.Width).CopyTo(a.AsSpan(r * p.Width));
        }
        return a;
    }
}

/// <summary>
/// The synthetic source pictures of the reference harness (refharness/src/encoder.cpp
/// FillContent): an I420 picture, Y w x h, then U and V (w/2) x (h/2), each packed.
/// </summary>
internal static class IdrContent
{
    // The "lowbias32" finaliser, so a sample does not depend on the order the planes are filled in.
    public static uint Hash32(uint x)
    {
        x ^= x >> 16;
        x *= 0x7feb352du;
        x ^= x >> 15;
        x *= 0x846ca68bu;
        x ^= x >> 16;
        return x;
    }

    public static byte Noise(int plane, int r, int c) => (byte)(Hash32(((uint)plane << 28) | ((uint)r << 13) | (uint)c) >> 24);

    // 24x24 luma regions (12x12 chroma), not macroblock aligned, of six kinds.
    public static byte Scene(int plane, int r, int c)
    {
        int s = plane != 0 ? 12 : 24;
        switch (((c / s) * 7 + (r / s) * 3) % 6)
        {
            case 0: return (byte)((60 + 37 * (c / s + r / s) + plane * 50) & 0xff);
            case 1: return (byte)((c * 3 + plane * 40) & 0xff);
            case 2: return (byte)((r * 5 + plane * 90) & 0xff);
            case 3: return Noise(plane, r, c);
            case 4: return (((r >> 2) + (c >> 2)) & 1) != 0 ? (byte)200 : (byte)40;
            default: return (byte)(((r + c) & 7) * 30 + plane * 7);
        }
    }

    public static byte[] Fill(int w, int h, string kind)
    {
        int cw = w >> 1, ch = h >> 1;
        var buf = new byte[w * h + 2 * cw * ch];
        int[] offset = { 0, w * h, w * h + cw * ch };
        int[] pw = { w, cw, cw }, ph = { h, ch, ch };
        for (int plane = 0; plane < 3; plane++)
        {
            for (int r = 0; r < ph[plane]; r++)
            {
                for (int c = 0; c < pw[plane]; c++)
                {
                    buf[offset[plane] + r * pw[plane] + c] = kind switch
                    {
                        "flat" => plane == 0 ? (byte)100 : plane == 1 ? (byte)110 : (byte)150,
                        "gradient" => (byte)((plane == 0 ? c * 4 + r * 4 : plane == 1 ? c * 8 : r * 8) & 0xff),
                        "noise" => Noise(plane, r, c),
                        "scene" => Scene(plane, r, c),
                        _ => throw new InvalidDataException($"Unknown content {kind}."),
                    };
                }
            }
        }
        return buf;
    }
}

/// <summary>What EncodeFrame returned and wrote, as the harness prints it.</summary>
internal sealed record IdrResult(int ReturnCode, int InternalReturnCode, string FrameType, string Layers, string Sha1, byte[] Bytes);

/// <summary>
/// The encoder's init and first frame for one spatial layer, over the ported functions
/// (see the file header). One instance is one encoder.
/// </summary>
internal sealed unsafe class IdrEncoder : IDisposable
{
    private const int cmResultSuccess = 0;
    private const int cmInitParaError = 1;
    private const int cmUnknownReason = 2;
    private const int cmMallocMemeError = 3;
    private const int cmUnsupportedData = 5;

    private sWelsEncCtx* _ctx;
    private CMemoryAlign* _pMa;
    private SPicture* _pSrcPic;     // CWelsPreProcess's current original frame (GetCurrentOrigFrame)
    private SFrameBSInfo* _info;
    private int _iPicWidth;
    private int _iPicHeight;

    public IdrEncoder()
    {
        _info = (SFrameBSInfo*)NativeMemory.AllocZeroed((nuint)sizeof(SFrameBSInfo));
    }

    /// <summary>InitializeExt with the settings applied, as the harness does. Returns the API's code.</summary>
    public int Initialize(string settings)
    {
        ParamSetEncoder.ParseSettings(settings, out SEncParamExt ext, out _, out bool isBase);
        if (isBase)
        {
            throw new NotSupportedException("api=base: Initialize selects RC_QUALITY_MODE, which comes with E6.");
        }
        _iPicWidth = ext.iPicWidth;
        _iPicHeight = ext.iPicHeight;

        // SWelsSvcCodingParam sConfig; (the constructor calls FillDefault)
        var sConfig = default(SWelsSvcCodingParam);
        sConfig.FillDefault();
        if (sConfig.ParamTranscode(ext) != 0)
        {
            return cmInitParaError;
        }
        int iRet = ParamSetEncoder.InitializeInternalChecks(&sConfig);
        if (iRet != 0)
        {
            return iRet;
        }
        return WelsInitEncoderExt(&sConfig) != 0 ? cmInitParaError : cmResultSuccess;
    }

    /// <summary>
    /// EncodeFrame (CWelsH264SVCEncoder::EncodeFrame and EncodeFrameInternal) of the content
    /// as the first frame.
    /// </summary>
    public IdrResult EncodeFirstFrame(string content)
    {
        int w = _iPicWidth, h = _iPicHeight;
        byte[] buf = IdrContent.Fill(w, h, content);
        int iInternal = 0;
        int rv;
        fixed (byte* p = buf)
        {
            SSourcePicture pic = default;
            pic.iPicWidth = w;
            pic.iPicHeight = h;
            pic.iColorFormat = (int)EVideoFormatType.videoFormatI420;
            pic.iStride[0] = w;
            pic.iStride[1] = pic.iStride[2] = w >> 1;
            pic.pData[0] = p;
            pic.pData[1] = p + w * h;
            pic.pData[2] = p + w * h + (w >> 1) * (h >> 1);
            pic.uiTimeStamp = 0;
            *_info = default;

            if (pic.iPicWidth < 16 || pic.iPicHeight < 16)
            {
                rv = cmUnsupportedData;
            }
            else
            {
                iInternal = WelsEncoderEncodeExt(_info, &pic);
                if (iInternal == ENC_RETURN_MEMALLOCERR || iInternal == ENC_RETURN_MEMOVERFLOWFOUND
                    || iInternal == ENC_RETURN_VLCOVERFLOWFOUND)
                {
                    rv = cmMallocMemeError;     // (and WelsUninitEncoderExt)
                }
                else if (iInternal == ENC_RETURN_INVALIDINPUT)
                {
                    rv = cmUnsupportedData;
                }
                else if (iInternal == ENC_RETURN_CORRECTED)
                {
                    rv = cmUnknownReason;
                }
                else
                {
                    rv = cmResultSuccess;
                }
            }
        }

        // The harness's view of the output: every layer's NAL units, then the whole frame.
        var layers = new StringBuilder();
        var frame = new List<byte>();
        SLayerBSInfo* pLayers = (SLayerBSInfo*)&_info->sLayerInfo;
        for (int i = 0; i < _info->iLayerNum; i++)
        {
            SLayerBSInfo* l = &pLayers[i];
            if (layers.Length != 0)
            {
                layers.Append('|');
            }
            layers.Append(l->uiLayerType == (byte)LAYER_TYPE.VIDEO_CODING_LAYER ? "V=" : "N=");
            byte* q = l->pBsBuf;
            for (int j = 0; j < l->iNalCount; j++)
            {
                var nal = new ReadOnlySpan<byte>(q, l->pNalLengthInByte[j]);
                if (j != 0)
                {
                    layers.Append(',');
                }
                layers.Append(CultureInfo.InvariantCulture, $"{nal.Length}:{Sha1(nal)[..8]}");
                frame.AddRange(nal.ToArray());
                q += l->pNalLengthInByte[j];
            }
        }
        byte[] bytes = frame.ToArray();
        return new IdrResult(rv, iInternal, FrameTypeName(_info->eFrameType), layers.Length == 0 ? "-" : layers.ToString(),
                             bytes.Length == 0 ? "-" : Sha1(bytes), bytes);
    }

    /// <summary>The top left w x h of a plane of the reconstructed (deblocked) picture.</summary>
    public byte[] Reconstruction(int plane, int w, int h)
    {
        SPicture* pDecPic = _ctx->pDecPic;
        var a = new byte[w * h];
        for (int r = 0; r < h; r++)
        {
            new ReadOnlySpan<byte>((byte*)pDecPic->pData[plane] + r * pDecPic->iLineSize[plane], w).CopyTo(a.AsSpan(r * w));
        }
        return a;
    }

    private static string Sha1(ReadOnlySpan<byte> d) => Convert.ToHexString(SHA1.HashData(d)).ToLowerInvariant();

    private static string FrameTypeName(EVideoFrameType t) => t switch
    {
        EVideoFrameType.videoFrameTypeInvalid => "invalid",
        EVideoFrameType.videoFrameTypeIDR => "IDR",
        EVideoFrameType.videoFrameTypeI => "I",
        EVideoFrameType.videoFrameTypeP => "P",
        EVideoFrameType.videoFrameTypeSkip => "skip",
        EVideoFrameType.videoFrameTypeIPMixed => "IPMixed",
        _ => "?",
    };

    private static void Require(bool b, string what)
    {
        if (!b)
        {
            throw new NotSupportedException($"IdrEncoder: {what} is not ported yet.");
        }
    }

    // ---- init: WelsInitEncoderExt ----

    private int WelsInitEncoderExt(SWelsSvcCodingParam* pCodingParam)
    {
        short iSliceNum = 1;
        int iCacheLineSize = 16;
        uint uiCpuFeatureFlags = 0;

        int iRet = ParamValidationExt(null, pCodingParam);
        if (iRet != 0)
        {
            return iRet;
        }
        iRet = pCodingParam->DetermineTemporalSettings();
        if (iRet != 0)
        {
            return iRet;
        }
        iRet = GetMultipleThreadIdc(null, pCodingParam, ref iSliceNum, ref iCacheLineSize, ref uiCpuFeatureFlags);
        if (iRet != 0)
        {
            return iRet;
        }

        // What the port codes by E4b.
        Require(pCodingParam->iUsageType == EUsageType.CAMERA_VIDEO_REAL_TIME, "screen content (E5)");
        Require(pCodingParam->iEntropyCodingModeFlag == 0, "CABAC");
        Require(pCodingParam->iSpatialLayerNum == 1 && !pCodingParam->bSimulcastAVC, "more than one spatial layer (E7)");
        Require(!pCodingParam->bPrefixNalAddingCtrl, "AddPrefixNal (E7)");
        Require(pCodingParam->iRCMode == RC_MODES.RC_OFF_MODE, "rate control (E6)");
        Require(pCodingParam->iMultipleThreadIdc == 1, "threading (Phase 4)");
        Require(!pCodingParam->bEnableDenoise, "the denoiser (E7)");
        Require(pCodingParam->sSpatialLayers[0].sSliceArgument.uiSliceMode != SliceModeEnum.SM_SIZELIMITED_SLICE,
                "SM_SIZELIMITED_SLICE (Phase 4)");

        // pCtx = malloc (sizeof (sWelsEncCtx)); memset (pCtx, 0, sizeof (sWelsEncCtx));
        _ctx = (sWelsEncCtx*)NativeMemory.AllocZeroed((nuint)sizeof(sWelsEncCtx));
        sWelsEncCtx* pCtx = _ctx;
        _pMa = WELS_NEW_CMemoryAlign((uint)iCacheLineSize);
        pCtx->pMemAlign = _pMa;

        // AllocCodingParam
        pCtx->pSvcParam = (SWelsSvcCodingParam*)_pMa->WelsMallocz((uint)sizeof(SWelsSvcCodingParam), "SWelsSvcCodingParam");
        *pCtx->pSvcParam = *pCodingParam;

        pCtx->pFuncList = (SWelsFuncPtrList*)_pMa->WelsMallocz((uint)sizeof(SWelsFuncPtrList), "SWelsFuncPtrList");
        InitFunctionPointers(pCtx, pCtx->pSvcParam);

        pCtx->iActiveThreadsNum = (short)pCodingParam->iMultipleThreadIdc;
        pCtx->iMaxSliceCount = iSliceNum;
        iRet = RequestMemorySvc();
        if (iRet != 0)
        {
            return iRet;
        }

        // WelsCabacInit: CAVLC only.
        // WelsRcInitModule: WelsRcInitFuncPointers, then RcInitSequenceParameter, whose bit rate
        // state (E6) nothing reads with rate control off (WelsRcMbInitDisable clips to 0..51,
        // and pWelsSvcRc->bGomRC stays false).
        WelsRcInitFuncPointers(pCtx, pCtx->pSvcParam->iRCMode);

        // CWelsPreProcess::CreatePreProcess and AllocSpatialPictures: the picture
        // GetCurrentOrigFrame gives for the first frame (the others hold earlier frames).
        _pSrcPic = AllocPicture(_pMa, pCtx->pSvcParam->sSpatialLayers[0].iVideoWidth,
                                pCtx->pSvcParam->sSpatialLayers[0].iVideoHeight, false, 0);
        if (_pSrcPic == null)
        {
            return 1;
        }

        pCtx->uiLastTimestamp = -1;
        pCtx->bDeliveryFlag = true;
        return 0;
    }

    /// <summary>InitFunctionPointers, the C paths (uiCpuFlag 0).</summary>
    private static void InitFunctionPointers(sWelsEncCtx* pEncCtx, SWelsSvcCodingParam* pParam)
    {
        SWelsFuncPtrList* pFuncList = pEncCtx->pFuncList;

        pFuncList->pfSetMemZeroSize8 = &WelsSetMemZero_c;
        pFuncList->pfSetMemZeroSize64Aligned16 = &WelsSetMemZero_c;
        pFuncList->pfSetMemZeroSize64 = &WelsSetMemZero_c;

        // InitExpandPictureFunc: the reference expansion follows the bytes (WelsUpdateRefList, E5).
        WelsInitIntraPredFuncs(pFuncList, 0);
        // WelsInitMeFunc: E5.
        WelsInitSampleSadFunc(pFuncList, 0);
        // WelsInitBGDFunc: pfInterMdBackgroundDecision is for P slices (E5).
        if (pParam->bEnableBackgroundDetection)
        {
            pFuncList->pfMdBackgroundInfoUpdate = &WelsMdUpdateBGDInfo;
        }
        else
        {
            pFuncList->pfMdBackgroundInfoUpdate = &WelsMdUpdateBGDInfoNULL;
        }
        // WelsInitSCDPskipFunc: screen content (E5).
        InitIntraAnalysisVaaInfo(pFuncList, 0);
        // InitMcFunc: E5.
        InitCoeffFunc(pFuncList, 0, pParam->iEntropyCodingModeFlag);
        WelsInitEncodingFuncs(pFuncList, 0);
        WelsInitReconstructionFuncs(pFuncList, 0);
        DeblockingInit(&pFuncList->pfDeblocking, 0);
        WelsBlockFuncInit(&pFuncList->pfSetNZCZero, 0);
        // InitFillNeighborCacheInterFunc: E5.
        pFuncList->pParametersetStrategy = IWelsParametersetStrategy.CreateParametersetStrategy(pParam->eSpsPpsIdStrategy,
            pParam->bSimulcastAVC, pParam->iSpatialLayerNum);
    }

    /// <summary>AcquireLayersNals.</summary>
    private int AcquireLayersNals(SWelsSvcCodingParam* pParam, out int iCountLayers, out int iCountNals)
    {
        int iCountNumLayers = 0;
        int iCountNumNals = 0;
        int iNumDependencyLayers = pParam->iSpatialLayerNum;
        int iDIndex = 0;
        iCountLayers = iCountNals = 0;

        do
        {
            SSpatialLayerConfig* pDLayer = &pParam->sSpatialLayers[iDIndex];
            int iOrgNumNals = iCountNumNals;
            if (SliceModeEnum.SM_SIZELIMITED_SLICE == pDLayer->sSliceArgument.uiSliceMode)
            {
                iCountNumNals += MAX_SLICES_NUM;
                // plus prefix NALs
                if (iDIndex == 0)
                {
                    iCountNumNals += MAX_SLICES_NUM;
                }
                if (iCountNumNals - iOrgNumNals > codec_app_def.MAX_NAL_UNITS_IN_LAYER)
                {
                    return 1;
                }
            }
            else
            {
                int kiNumOfSlice = GetInitialSliceNum(&pDLayer->sSliceArgument);
                iCountNumNals += kiNumOfSlice; // for pSlice VCL NALs
                // plus prefix NALs
                if (iDIndex == 0)
                {
                    iCountNumNals += kiNumOfSlice;
                }
                Debug.Assert(iCountNumNals - iOrgNumNals <= codec_app_def.MAX_NAL_UNITS_IN_LAYER);
                if (kiNumOfSlice > MAX_SLICES_NUM)
                {
                    return 1;
                }
            }

            if (iCountNumNals - iOrgNumNals > codec_app_def.MAX_NAL_UNITS_IN_LAYER)
            {
                return 1;
            }

            iCountNumLayers++;
            ++iDIndex;
        } while (iDIndex < iNumDependencyLayers);

        // count parasets
        iCountNumNals += 1 + iNumDependencyLayers + (iCountNumLayers << 1) +
                         iCountNumLayers // plus iCountNumLayers for reserved application
                         + (int)_ctx->pFuncList->pParametersetStrategy->GetAllNeededParasetNum();

        if (iCountNumLayers > codec_app_def.MAX_LAYER_NUM_OF_FRAME)
        {
            return 1;
        }
        iCountLayers = iCountNumLayers;
        iCountNals = iCountNumNals;
        return 0;
    }

    /// <summary>RequestMemorySvc.</summary>
    private int RequestMemorySvc()
    {
        sWelsEncCtx* pCtx = _ctx;
        SWelsSvcCodingParam* pParam = pCtx->pSvcParam;
        CMemoryAlign* pMa = pCtx->pMemAlign;
        int kiNumDependencyLayers = pParam->iSpatialLayerNum;

        if (kiNumDependencyLayers < 1 || kiNumDependencyLayers > MAX_DEPENDENCY_LAYER)
        {
            return 1;
        }
        if (pParam->uiGopSize == 0 || (pParam->uiIntraPeriod != 0 && (pParam->uiIntraPeriod % pParam->uiGopSize) != 0))
        {
            return 1;
        }

        SSpatialLayerConfig* pFinalSpatial = &pParam->sSpatialLayers[kiNumDependencyLayers - 1];
        int iCountMaxMbNum = ((15 + pFinalSpatial->iVideoWidth) >> 4) * ((15 + pFinalSpatial->iVideoHeight) >> 4);

        if (AcquireLayersNals(pParam, out _, out int iCountNals) != 0)
        {
            return 1;
        }

        IWelsParametersetStrategy* pStrategy = pCtx->pFuncList->pParametersetStrategy;
        int kiSpsSize = (int)pStrategy->GetNeededSpsNum() * SPS_BUFFER_SIZE;
        int kiPpsSize = (int)pStrategy->GetNeededPpsNum() * PPS_BUFFER_SIZE;
        int iNonVclLayersBsSizeCount = SSEI_BUFFER_SIZE + kiSpsSize + kiPpsSize;

        int iSliceBufferSize = 0;
        int iMaxSliceBufferSize = 0;
        int iVclLayersBsSizeCount = 0;
        for (int iIndex = 0; iIndex < pParam->iSpatialLayerNum; iIndex++)
        {
            SSpatialLayerConfig* fDlp = &pParam->sSpatialLayers[iIndex];
            float fCompressRatioThr = COMPRESS_RATIO_THR;

            int iLayerBsSize = WELS_ROUND(((3 * fDlp->iVideoWidth * fDlp->iVideoHeight) >> 1) * fCompressRatioThr) +
                               MAX_MACROBLOCK_SIZE_IN_BYTE_x2;
            iLayerBsSize = WELS_ALIGN(iLayerBsSize, 4); // 4 bytes alinged
            int iMaxLayerBsSize;
            SSliceArgument* pSliceArgument = &fDlp->sSliceArgument;
            // SM_SIZELIMITED_SLICE (rejected at init) estimates its slice count instead.
            pCtx->iMaxSliceCount = Math.Max(pCtx->iMaxSliceCount, (int)pSliceArgument->uiSliceNum);
            if (pParam->bUseLoadBalancing)
            {
                iSliceBufferSize = iLayerBsSize + MAX_MACROBLOCK_SIZE_IN_BYTE_x2;
            }
            else
            {
                iSliceBufferSize = (int)(((uint)iLayerBsSize / pSliceArgument->uiSliceNum) << 1) + MAX_MACROBLOCK_SIZE_IN_BYTE_x2;
            }
            iMaxLayerBsSize = (int)((uint)iSliceBufferSize * pSliceArgument->uiSliceNum);
            iMaxLayerBsSize = Math.Max(iMaxLayerBsSize, iLayerBsSize);
            iVclLayersBsSizeCount += iMaxLayerBsSize;
            iMaxSliceBufferSize = Math.Max(iMaxSliceBufferSize, iSliceBufferSize);
            pCtx->iSliceBufferSize[iIndex] = iSliceBufferSize;
        }
        int iTargetSpatialBsSize = iVclLayersBsSizeCount;
        int iCountBsLen = iNonVclLayersBsSizeCount + iVclLayersBsSizeCount;
        iMaxSliceBufferSize = Math.Min(iMaxSliceBufferSize, iTargetSpatialBsSize);
        int iTotalLength = iCountBsLen;

        pParam->iNumRefFrame = WELS_CLIP3(pParam->iNumRefFrame, MIN_REF_PIC_COUNT,
            pParam->iUsageType == EUsageType.CAMERA_VIDEO_REAL_TIME ? MAX_REFERENCE_PICTURE_COUNT_NUM_CAMERA :
            MAX_REFERENCE_PICTURE_COUNT_NUM_SCREEN);

        // Output
        pCtx->pOut = (SWelsEncoderOutput*)pMa->WelsMallocz((uint)sizeof(SWelsEncoderOutput), "SWelsEncoderOutput");
        pCtx->pOut->pBsBuffer = (byte*)pMa->WelsMallocz((uint)iCountBsLen, "pOut->pBsBuffer");
        pCtx->pOut->uiSize = (uint)iCountBsLen;
        pCtx->pOut->sNalList = (SWelsNalRaw*)pMa->WelsMallocz((uint)(iCountNals * sizeof(SWelsNalRaw)), "pOut->sNalList");
        pCtx->pOut->pNalLen = (int*)pMa->WelsMallocz((uint)(iCountNals * sizeof(int)), "pOut->pNalLen");
        pCtx->pOut->iCountNals = iCountNals;
        pCtx->pOut->iNalIndex = 0;
        pCtx->pOut->iLayerBsIndex = 0;

        pCtx->pFrameBs = (byte*)pMa->WelsMalloc((uint)iTotalLength, "pFrameBs");
        pCtx->iFrameBsSize = iTotalLength;
        pCtx->iPosBsBuffer = 0;

        // pDynamicBsBuffer: SM_SIZELIMITED_SLICE with CABAC. RequestMtResource: threads.
        // pReferenceStrategy (CreateReferenceStrategy) comes with E5; WelsEncoderEncodeExt below
        // does what its MarkPic and BuildRefList do for an IDR.

        pCtx->pIntra4x4PredModeBlocks = (sbyte*)pMa->WelsMallocz((uint)(iCountMaxMbNum * INTRA_4x4_MODE_NUM), "pIntra4x4PredModeBlocks");
        pCtx->pNonZeroCountBlocks = (sbyte*)pMa->WelsMallocz((uint)(iCountMaxMbNum * MB_LUMA_CHROMA_BLOCK4x4_NUM), "pNonZeroCountBlocks");
        pCtx->pMvUnitBlock4x4 = (SMVUnitXY*)pMa->WelsMallocz((uint)(iCountMaxMbNum * 2 * MB_BLOCK4x4_NUM * sizeof(SMVUnitXY)), "pMvUnitBlock4x4");
        pCtx->pRefIndexBlock4x4 = (sbyte*)pMa->WelsMallocz((uint)(iCountMaxMbNum * 2 * MB_BLOCK8x8_NUM * sizeof(sbyte)), "pRefIndexBlock4x4");
        pCtx->pSadCostMb = (int*)pMa->WelsMallocz((uint)(iCountMaxMbNum * sizeof(int)), "pSadCostMb");

        pCtx->iGlobalQp = 26;   // global qp in default

        // pLtr (SLTRState, ResetLtrState) comes with E5.

        // stride tables
        if (AllocStrideTables(&pCtx, kiNumDependencyLayers) != 0)
        {
            return 1;
        }

        pCtx->pWelsSvcRc = (SWelsSvcRc*)pMa->WelsMallocz((uint)(kiNumDependencyLayers * sizeof(SWelsSvcRc)), "pWelsSvcRc");

        // pVaa comes with E5 and E7: an IDR at a fixed QP does not read it.

        pCtx->ppRefPicListExt = (SRefList**)pMa->WelsMallocz((uint)(kiNumDependencyLayers * sizeof(SRefList*)), "ppRefPicListExt");
        pCtx->ppDqLayerList = (SDqLayer**)pMa->WelsMallocz((uint)(kiNumDependencyLayers * sizeof(SDqLayer*)), "ppDqLayerList");

        int iResult = InitDqLayers(&pCtx, null);
        if (iResult != 0)
        {
            return iResult;
        }
        if (InitMbListD(&pCtx) != 0)
        {
            return 1;
        }

        // The MVD cost table (GetMvMvdRange, MvdCostInit) comes with E5.

        if (pCtx->ppRefPicListExt[0] != null && !pCtx->ppRefPicListExt[0]->pRef[0].IsNull)
        {
            pCtx->pDecPic = pCtx->ppRefPicListExt[0]->pRef[0];
        }
        else
        {
            pCtx->pDecPic = null; // error here
        }

        pCtx->pSps = &pCtx->pSpsArray[0];
        pCtx->pPps = &pCtx->pPPSArray[0];
        return 0;
    }

    // ---- frame: WelsEncoderEncodeExt ----

    /// <summary>
    /// CWelsPreProcess::BuildSpatialPicList for the first frame of one spatial layer:
    /// WelsPreprocessReset, then SingleLayerPreprocess without the denoiser, the scaling or
    /// the scene change detection (which skips an IDR request: bEncCurFrmAsIdrFlag).
    /// </summary>
    private int BuildSpatialPicList(SSourcePicture* kpSrcPic, int* pSpatialNum)
    {
        sWelsEncCtx* pCtx = _ctx;
        SWelsSvcCodingParam* pSvcParam = pCtx->pSvcParam;
        int iWidth = (kpSrcPic->iPicWidth >> 1) << 1;
        int iHeight = (kpSrcPic->iPicHeight >> 1) << 1;
        *pSpatialNum = 0;

        // WelsPreprocessReset
        pSvcParam->SUsedPicRect.iLeft = 0;
        pSvcParam->SUsedPicRect.iTop = 0;
        pSvcParam->SUsedPicRect.iWidth = iWidth;
        pSvcParam->SUsedPicRect.iHeight = iHeight;
        if (iWidth < 16 || iHeight < 16)
        {
            return ENC_RETURN_MEMALLOCERR;
        }
        // WelsInitScaledPic: JudgeNeedOfScaling.
        SSpatialLayerInternal* pTop = &pSvcParam->sDependencyLayers[pSvcParam->iSpatialLayerNum - 1];
        Require(pTop->iActualWidth >= iWidth && pTop->iActualHeight >= iHeight, "downsampling the input (E7)");

        // SingleLayerPreprocess
        int iDependencyId = pSvcParam->iSpatialLayerNum - 1;
        SSpatialLayerInternal* pDlayerParamInternal = &pSvcParam->sDependencyLayers[iDependencyId];
        SSpatialLayerConfig* pDlayerParam = &pSvcParam->sSpatialLayers[iDependencyId];
        int iTargetWidth = pDlayerParam->iVideoWidth;
        int iTargetHeight = pDlayerParam->iVideoHeight;
        int iSrcWidth = pSvcParam->SUsedPicRect.iWidth;
        int iSrcHeight = pSvcParam->SUsedPicRect.iHeight;
        // pVaa->bIdrPeriodFlag (uiIntraPeriod) is false for the first frame.

        SPicture* pSrcPic = _pSrcPic;
        int iRet = WelsMoveMemoryWrapper(pSvcParam, pSrcPic, kpSrcPic, iSrcWidth, iSrcHeight);
        if (iRet != ENC_RETURN_SUCCESS)
        {
            return iRet;
        }

        // DownsamplePadding without scaling: get rid of the odd line, then pad to the target.
        int iShrinkWidth = iSrcWidth - (iSrcWidth & 1);
        int iShrinkHeight = iSrcHeight - (iSrcHeight & 1);
        Padding(pSrcPic->pData[0], pSrcPic->pData[1], pSrcPic->pData[2], pSrcPic->iLineSize[0], pSrcPic->iLineSize[1],
                iShrinkWidth, iTargetWidth, iShrinkHeight, iTargetHeight);

        int iSpatialNum = 0;
        for (int i = 0; i < pSvcParam->iSpatialLayerNum; i++)
        {
            SSpatialLayerInternal* p = &pSvcParam->sDependencyLayers[i];
            if (p->uiCodingIdx2TemporalId[p->iCodingIndex & (pSvcParam->uiGopSize - 1)] != INVALID_TEMPORAL_ID)
            {
                ++iSpatialNum;
            }
        }
        int iTemporalId = pDlayerParamInternal->uiCodingIdx2TemporalId[pDlayerParamInternal->iCodingIndex & (pSvcParam->uiGopSize - 1)];
        if (iTemporalId != INVALID_TEMPORAL_ID)
        {
            // WelsUpdateSpatialIdxMap
            pCtx->sSpatialIndexMap[iSpatialNum - 1].pSrc = pSrcPic;
            pCtx->sSpatialIndexMap[iSpatialNum - 1].iDid = iDependencyId;
        }
        *pSpatialNum = iSpatialNum;
        return ENC_RETURN_SUCCESS;
    }

    /// <summary>CWelsPreProcess::WelsMoveMemoryWrapper.</summary>
    private static int WelsMoveMemoryWrapper(SWelsSvcCodingParam* pSvcParam, SPicture* pDstPic, SSourcePicture* kpSrc,
                                             int kiTargetWidth, int kiTargetHeight)
    {
        if ((int)EVideoFormatType.videoFormatI420 != (kpSrc->iColorFormat & ~unchecked((int)EVideoFormatType.videoFormatVFlip)))
        {
            return ENC_RETURN_INVALIDINPUT;
        }

        int iSrcWidth = kpSrc->iPicWidth;
        int iSrcHeight = kpSrc->iPicHeight;
        if (iSrcHeight > kiTargetHeight)
        {
            iSrcHeight = kiTargetHeight;
        }
        if (iSrcWidth > kiTargetWidth)
        {
            iSrcWidth = kiTargetWidth;
        }
        // copy from fr26 to fix the odd uiSize failed issue
        if ((iSrcWidth & 0x1) != 0)
        {
            --iSrcWidth;
        }
        if ((iSrcHeight & 0x1) != 0)
        {
            --iSrcHeight;
        }

        int kiSrcTopOffsetY = pSvcParam->SUsedPicRect.iTop;
        int kiSrcTopOffsetUV = kiSrcTopOffsetY >> 1;
        int kiSrcLeftOffsetY = pSvcParam->SUsedPicRect.iLeft;
        int kiSrcLeftOffsetUV = kiSrcLeftOffsetY >> 1;
        byte* pSrcY = kpSrc->pData[0] + kpSrc->iStride[0] * kiSrcTopOffsetY + kiSrcLeftOffsetY;
        byte* pSrcU = kpSrc->pData[1] + kpSrc->iStride[1] * kiSrcTopOffsetUV + kiSrcLeftOffsetUV;
        byte* pSrcV = kpSrc->pData[2] + kpSrc->iStride[2] * kiSrcTopOffsetUV + kiSrcLeftOffsetUV;
        int kiSrcStrideY = kpSrc->iStride[0];
        int kiSrcStrideU = kpSrc->iStride[1];
        int kiSrcStrideV = kpSrc->iStride[2];

        byte* pDstY = pDstPic->pData[0];
        byte* pDstU = pDstPic->pData[1];
        byte* pDstV = pDstPic->pData[2];
        int kiDstStrideY = pDstPic->iLineSize[0];
        int kiDstStrideU = pDstPic->iLineSize[1];
        int kiDstStrideV = pDstPic->iLineSize[2];

        if (iSrcWidth <= 0 || iSrcHeight <= 0)
        {
            return ENC_RETURN_INVALIDINPUT;
        }
        if (iSrcWidth > pDstPic->iWidthInPixel || iSrcHeight > pDstPic->iHeightInPixel)
        {
            return ENC_RETURN_INVALIDINPUT;
        }
        if (kiSrcTopOffsetY >= iSrcHeight || kiSrcLeftOffsetY >= iSrcWidth || iSrcWidth > kiSrcStrideY
            || (iSrcWidth >> 1) > kiSrcStrideU || (iSrcWidth >> 1) > kiSrcStrideV)
        {
            return ENC_RETURN_INVALIDINPUT;
        }
        if (kiTargetWidth <= 0 || kiTargetHeight <= 0)
        {
            return ENC_RETURN_INVALIDINPUT;
        }
        if (kiTargetWidth > pDstPic->iWidthInPixel || kiTargetHeight > pDstPic->iHeightInPixel)
        {
            return ENC_RETURN_INVALIDINPUT;
        }
        if (kiTargetWidth > kiDstStrideY || (kiTargetWidth >> 1) > kiDstStrideU || (kiTargetWidth >> 1) > kiDstStrideV)
        {
            return ENC_RETURN_INVALIDINPUT;
        }

        // WelsMoveMemory_c (i420_to_i420_c)
        for (int j = iSrcHeight; j != 0; j--)
        {
            Buffer.MemoryCopy(pSrcY, pDstY, iSrcWidth, iSrcWidth);
            pDstY += kiDstStrideY;
            pSrcY += kiSrcStrideY;
        }
        for (int j = iSrcHeight >> 1; j != 0; j--)
        {
            Buffer.MemoryCopy(pSrcU, pDstU, iSrcWidth >> 1, iSrcWidth >> 1);
            Buffer.MemoryCopy(pSrcV, pDstV, iSrcWidth >> 1, iSrcWidth >> 1);
            pDstU += kiDstStrideU;
            pDstV += kiDstStrideV;
            pSrcU += kiSrcStrideU;
            pSrcV += kiSrcStrideV;
        }

        //in VP Process
        if (kiTargetWidth > iSrcWidth || kiTargetHeight > iSrcHeight)
        {
            Padding(pDstPic->pData[0], pDstPic->pData[1], pDstPic->pData[2], kiDstStrideY, kiDstStrideU, iSrcWidth,
                    kiTargetWidth, iSrcHeight, kiTargetHeight);
        }
        return ENC_RETURN_SUCCESS;
    }

    /// <summary>CWelsPreProcess::Padding.</summary>
    private static void Padding(byte* pSrcY, byte* pSrcU, byte* pSrcV, int iStrideY, int iStrideUV,
                                int iActualWidth, int iPaddingWidth, int iActualHeight, int iPaddingHeight)
    {
        if (iPaddingHeight > iActualHeight)
        {
            for (int i = iActualHeight; i < iPaddingHeight; i++)
            {
                new Span<byte>(pSrcY + i * iStrideY, iActualWidth).Fill(0);
                if ((i & 1) == 0)
                {
                    new Span<byte>(pSrcU + i / 2 * iStrideUV, iActualWidth / 2).Fill(0x80);
                    new Span<byte>(pSrcV + i / 2 * iStrideUV, iActualWidth / 2).Fill(0x80);
                }
            }
        }

        if (iPaddingWidth > iActualWidth)
        {
            for (int i = 0; i < iPaddingHeight; i++)
            {
                new Span<byte>(pSrcY + i * iStrideY + iActualWidth, iPaddingWidth - iActualWidth).Fill(0);
                if ((i & 1) == 0)
                {
                    new Span<byte>(pSrcU + i / 2 * iStrideUV + iActualWidth / 2, (iPaddingWidth - iActualWidth) / 2).Fill(0x80);
                    new Span<byte>(pSrcV + i / 2 * iStrideUV + iActualWidth / 2, (iPaddingWidth - iActualWidth) / 2).Fill(0x80);
                }
            }
        }
    }

    /// <summary>
    /// PrepareEncodeFrame for the first frame. WelsRcCheckFrameStatus skips nothing with rate
    /// control off, and DecideFrameType (which reads the VAA, E5) gives an IDR for the first
    /// frame (bEncCurFrmAsIdrFlag), setting iCodingIndex to 0.
    /// </summary>
    private EVideoFrameType PrepareEncodeFrame(ref SLayerBSInfo* pLayerBsInfo, int iSpatialNum, sbyte iCurDid,
                                               ref int iCurTid, ref int iLayerNum, ref int iFrameSize)
    {
        sWelsEncCtx* pCtx = _ctx;
        SWelsSvcCodingParam* pSvcParam = pCtx->pSvcParam;
        SSpatialLayerInternal* pParamInternal = &pSvcParam->sDependencyLayers[iCurDid];

        Assert.True(pParamInternal->bEncCurFrmAsIdrFlag, "the first frame is an IDR");
        EVideoFrameType eFrameType = EVideoFrameType.videoFrameTypeIDR;
        pParamInternal->iCodingIndex = 0;

        iCurTid = GetTemporalLevel(pParamInternal, pParamInternal->iCodingIndex, (int)pSvcParam->uiGopSize);
        pCtx->uiTemporalId = (byte)iCurTid;
        // write parameter sets bitstream or SEI/SSEI (if any) here
        if (((int)EParameterSetStrategy.SPS_LISTING & (int)pSvcParam->eSpsPpsIdStrategy) == 0)
        {
            pCtx->iEncoderError = WriteSsvcParaset(pCtx, iSpatialNum, ref pLayerBsInfo, ref iLayerNum, ref iFrameSize);
        }
        else
        {
            pCtx->iEncoderError = WriteSavcParaset_Listing(pCtx, iSpatialNum, ref pLayerBsInfo, ref iLayerNum, ref iFrameSize);
        }
        return eFrameType;
    }

    /// <summary>
    /// CWelsReference_TemporalLayer::MarkPic (WelsMarkPic) for an IDR: no LTR marking, since
    /// ResetLtrState leaves bLTRMarkEnable false, so WelsMarkMMCORefInfo only clears every
    /// slice's sRefMarking.
    /// </summary>
    private void MarkPicIdr()
    {
        SDqLayer* pCurDq = _ctx->pCurDqLayer;
        for (int i = 0; i < pCurDq->iMaxSliceNum; i++)
        {
            pCurDq->ppSliceInLayer[i]->sSliceHeaderExt.sSliceHeader.sRefMarking = default;
        }
    }

    /// <summary>
    /// CWelsReference_TemporalLayer::BuildRefList (WelsBuildRefList) for an I slice:
    /// WelsResetRefList, ResetLtrState (pLtr comes with E5), no references.
    /// </summary>
    private bool BuildRefListIdr()
    {
        sWelsEncCtx* pCtx = _ctx;
        pCtx->iNumRef0 = 0;
        Assert.Equal(EWelsSliceType.I_SLICE, pCtx->eSliceType);

        // WelsResetRefList
        SRefList* pRefList = pCtx->ppRefPicListExt[pCtx->uiDependencyId];
        for (int i = 0; i < MAX_SHORT_REF_COUNT + 1; i++)
        {
            pRefList->pShortRefList[i] = null;
        }
        for (int i = 0; i < pCtx->pSvcParam->iLTRRefNum + 1; i++)
        {
            pRefList->pLongRefList[i] = null;
        }
        for (int i = 0; i < pCtx->pSvcParam->iNumRefFrame + 1; i++)
        {
            ((SPicture*)pRefList->pRef[i])->SetUnref();
        }
        pRefList->uiLongRefCount = 0;
        pRefList->uiShortRefCount = 0;
        pRefList->pNextBuffer = pRefList->pRef[0];

        // bRefOfCurTidIsLtr comes with E5.
        pCtx->pRefList0[0] = null;
        return true;
    }

    /// <summary>
    /// WelsUpdateRefSyntax (WelsUpdateSliceHeaderSyntax) for an IDR: no references to reorder,
    /// and dec_ref_pic_marking's IDR flags.
    /// </summary>
    private void UpdateRefSyntaxIdr()
    {
        sWelsEncCtx* pCtx = _ctx;
        SDqLayer* pCurDq = pCtx->pCurDqLayer;
        Assert.Equal(0, (int)pCtx->iNumRef0);
        for (int i = 0; i < pCurDq->iMaxSliceNum; i++)
        {
            SSliceHeader* pSliceHdr = &pCurDq->ppSliceInLayer[i]->sSliceHeaderExt.sSliceHeader;
            pSliceHdr->uiRefCount = pCtx->iNumRef0;
            pSliceHdr->sRefMarking.bNoOutputOfPriorPicsFlag = false;
            pSliceHdr->sRefMarking.bLongTermRefFlag = pCtx->pSvcParam->bEnableLongTermReference;
        }
    }

    /// <summary>WelsEncoderEncodeExt for the first frame (see the file header).</summary>
    private int WelsEncoderEncodeExt(SFrameBSInfo* pFbi, SSourcePicture* pSrcPic)
    {
        sWelsEncCtx* pCtx = _ctx;
        SLayerBSInfo* pLayerBsInfo = (SLayerBSInfo*)&pFbi->sLayerInfo;
        SWelsSvcCodingParam* pSvcParam = pCtx->pSvcParam;
        int iLayerNum = 0;
        int iLayerSize = 0;
        int iSpatialNum = 0;
        int iSpatialIdx = 0;
        int iFrameSize = 0;
        int iNalIdxInLayer = 0;
        EVideoFrameType eFrameType = EVideoFrameType.videoFrameTypeInvalid;
        EWelsNalUnitType eNalType = EWelsNalUnitType.NAL_UNIT_UNSPEC_0;
        EWelsNalRefIdc eNalRefIdc = EWelsNalRefIdc.NRI_PRI_LOWEST;
        sbyte iCurDid = 0;
        int iCurTid = 0;

        pCtx->iEncoderError = ENC_RETURN_SUCCESS;
        pCtx->bCurFrameMarkedAsSceneLtr = false;
        pFbi->eFrameType = EVideoFrameType.videoFrameTypeSkip;
        pFbi->iLayerNum = 0; // for initialization
        pFbi->uiTimeStamp = pSrcPic->uiTimeStamp;   // GetTimestampForRc: the rate control's (E6)
        for (int iNalIdx = 0; iNalIdx < codec_app_def.MAX_LAYER_NUM_OF_FRAME; iNalIdx++)
        {
            pFbi->sLayerInfo[iNalIdx].eFrameType = EVideoFrameType.videoFrameTypeSkip;
            pFbi->sLayerInfo[iNalIdx].iNalCount = 0;
        }
        // perform csc/denoise/downsample/padding, generate spatial layers
        int iRet = BuildSpatialPicList(pSrcPic, &iSpatialNum);
        if (iRet != ENC_RETURN_SUCCESS)
        {
            return iRet;
        }
        // pfWelsUpdateMaxBrWindowStatus: null with rate control off.
        Assert.True(iSpatialNum >= 1);

        InitBitStream(pCtx);
        pLayerBsInfo->pBsBuf = pCtx->pFrameBs;
        pLayerBsInfo->pNalLengthInByte = pCtx->pOut->pNalLen;
        iCurDid = (sbyte)pCtx->sSpatialIndexMap[0].iDid;
        pCtx->pCurDqLayer = pCtx->ppDqLayerList[iCurDid];
        pCtx->pCurDqLayer->pRefLayer = null;
        eFrameType = PrepareEncodeFrame(ref pLayerBsInfo, iSpatialNum, iCurDid, ref iCurTid, ref iLayerNum, ref iFrameSize);

        while (iSpatialIdx < iSpatialNum)
        {
            iCurDid = (sbyte)pCtx->sSpatialIndexMap[iSpatialIdx].iDid;
            SSpatialLayerConfig* pParam = &pSvcParam->sSpatialLayers[iCurDid];
            SSpatialLayerInternal* pParamInternal = &pSvcParam->sDependencyLayers[iCurDid];
            int iDecompositionStages = pParamInternal->iDecompositionStages;
            pCtx->pCurDqLayer = pCtx->ppDqLayerList[iCurDid];
            pCtx->uiDependencyId = (byte)iCurDid;

            InitFrameCoding(pCtx, eFrameType, iCurDid);
            // AnalyzeSpatialPic: nothing for an I slice with rate control off.

            pCtx->pEncPic = pCtx->sSpatialIndexMap[iSpatialIdx].pSrc;
            pCtx->pEncPic->iPictureType = (int)pCtx->eSliceType;
            pCtx->pEncPic->iFramePoc = pParamInternal->iPOC;

            int iCurWidth = pParam->iVideoWidth;
            int iCurHeight = pParam->iVideoHeight;
            // SM_FIXEDSLCNUM_SLICE adjusts the slicing only with threads; SM_SIZELIMITED_SLICE is rejected.

            if (iLayerNum >= codec_app_def.MAX_LAYER_NUM_OF_FRAME)
            {
                return ENC_RETURN_UNSUPPORTED_PARA;
            }

            iNalIdxInLayer = 0;
            bool bAvcBased = pSvcParam->bSimulcastAVC || iCurDid == BASE_DEPENDENCY_ID;
            pCtx->bNeedPrefixNalFlag = !pSvcParam->bSimulcastAVC && bAvcBased &&
                                       (pSvcParam->bPrefixNalAddingCtrl || pSvcParam->iSpatialLayerNum > 1);

            if (eFrameType == EVideoFrameType.videoFrameTypeP)
            {
                eNalType = bAvcBased ? EWelsNalUnitType.NAL_UNIT_CODED_SLICE : EWelsNalUnitType.NAL_UNIT_CODED_SLICE_EXT;
            }
            else if (eFrameType == EVideoFrameType.videoFrameTypeIDR)
            {
                eNalType = bAvcBased ? EWelsNalUnitType.NAL_UNIT_CODED_SLICE_IDR : EWelsNalUnitType.NAL_UNIT_CODED_SLICE_EXT;
            }
            if (iCurTid == 0 || pCtx->eSliceType == EWelsSliceType.I_SLICE)
            {
                eNalRefIdc = EWelsNalRefIdc.NRI_PRI_HIGHEST;
            }
            else if (iCurTid == iDecompositionStages)
            {
                eNalRefIdc = EWelsNalRefIdc.NRI_PRI_LOWEST;
            }
            else if (1 + iCurTid == iDecompositionStages)
            {
                eNalRefIdc = EWelsNalRefIdc.NRI_PRI_LOW;
            }
            else if (2 + iCurTid == iDecompositionStages)
            {
                eNalRefIdc = EWelsNalRefIdc.NRI_PRI_HIGH;
            }
            else
            {
                eNalRefIdc = EWelsNalRefIdc.NRI_PRI_HIGHEST;
            }
            pCtx->eNalType = eNalType;
            pCtx->eNalPriority = eNalRefIdc;

            pCtx->pDecPic = pCtx->ppRefPicListExt[iCurDid]->pNextBuffer;
            pCtx->pDecPic->iPictureType = (int)pCtx->eSliceType;
            pCtx->pDecPic->iFramePoc = pParamInternal->iPOC;

            WelsInitCurrentLayer(pCtx, iCurWidth, iCurHeight);

            MarkPicIdr();
            if (!BuildRefListIdr())
            {
                return ENC_RETURN_CORRECTED;
            }
            // AnalyzePictureComplexity: rate control only.
            UpdateRefSyntaxIdr();
            PrefetchReferencePicture(pCtx, eFrameType); // update reference picture for current pDq layer
            pCtx->pFuncList->pfRc.pfWelsRcPictureInit(pCtx, pFbi->uiTimeStamp);
            PreprocessSliceCoding(pCtx); // MUST be called after pfWelsRcPictureInit() and WelsInitCurrentLayer()

            iLayerSize = 0;
            if (SliceModeEnum.SM_SINGLE_SLICE == pParam->sSliceArgument.uiSliceMode)
            {
                SSlice* pCurSlice = &pCtx->pCurDqLayer->sSliceBufferInfo[0].pSliceBuffer[0];

                Require(!pCtx->bNeedPrefixNalFlag, "AddPrefixNal (E7)");
                WelsLoadNal(pCtx->pOut, (int)eNalType, (int)eNalRefIdc);
                Debug.Assert(0 == pCurSlice->iSliceIdx);
                pCtx->iEncoderError = SetSliceBoundaryInfo(pCtx->pCurDqLayer, pCurSlice, 0);
                if (pCtx->iEncoderError != ENC_RETURN_SUCCESS)
                {
                    return pCtx->iEncoderError;
                }

                pCtx->iEncoderError = WelsCodeOneSlice(pCtx, pCurSlice, (int)eNalType);
                if (pCtx->iEncoderError != ENC_RETURN_SUCCESS)
                {
                    return pCtx->iEncoderError;
                }

                WelsUnloadNal(pCtx->pOut);

                pCtx->iEncoderError = WelsEncodeNal(&pCtx->pOut->sNalList[pCtx->pOut->iNalIndex - 1],
                                                    &pCtx->pCurDqLayer->sLayerInfo.sNalHeaderExt,
                                                    pCtx->iFrameBsSize - pCtx->iPosBsBuffer,
                                                    pCtx->pFrameBs + pCtx->iPosBsBuffer,
                                                    &pLayerBsInfo->pNalLengthInByte[iNalIdxInLayer]);
                if (pCtx->iEncoderError != ENC_RETURN_SUCCESS)
                {
                    return pCtx->iEncoderError;
                }
                int iSliceSize = pLayerBsInfo->pNalLengthInByte[iNalIdxInLayer];

                iLayerSize += iSliceSize;
                pCtx->iPosBsBuffer += iSliceSize;
                pLayerBsInfo->uiLayerType = (byte)LAYER_TYPE.VIDEO_CODING_LAYER;
                pLayerBsInfo->uiSpatialId = (byte)iCurDid;
                pLayerBsInfo->uiTemporalId = (byte)iCurTid;
                pLayerBsInfo->uiQualityId = 0;
                pLayerBsInfo->iNalCount = ++iNalIdxInLayer;
                pLayerBsInfo->eFrameType = eFrameType;
                pLayerBsInfo->iSubSeqId = GetSubSequenceId(pCtx, eFrameType);
            }
            else
            {
                // SM_SIZELIMITED_SLICE and the threaded branches are rejected at init: the
                // non-dynamic-slicing mode single threading branch.
                bool bNeedPrefix = pCtx->bNeedPrefixNalFlag;
                int iSliceIdx = 0;
                int iSliceCount = GetCurrentSliceNum(pCtx->pCurDqLayer);
                while (iSliceIdx < iSliceCount)
                {
                    Require(!bNeedPrefix, "AddPrefixNal (E7)");
                    WelsLoadNal(pCtx->pOut, (int)eNalType, (int)eNalRefIdc);

                    SSlice* pCurSlice = &pCtx->pCurDqLayer->sSliceBufferInfo[0].pSliceBuffer[iSliceIdx];
                    Debug.Assert(iSliceIdx == pCurSlice->iSliceIdx);
                    pCtx->iEncoderError = SetSliceBoundaryInfo(pCtx->pCurDqLayer, pCurSlice, iSliceIdx);

                    pCtx->iEncoderError = WelsCodeOneSlice(pCtx, pCurSlice, (int)eNalType);
                    if (pCtx->iEncoderError != ENC_RETURN_SUCCESS)
                    {
                        return pCtx->iEncoderError;
                    }

                    WelsUnloadNal(pCtx->pOut);

                    pCtx->iEncoderError = WelsEncodeNal(&pCtx->pOut->sNalList[pCtx->pOut->iNalIndex - 1],
                                                        &pCtx->pCurDqLayer->sLayerInfo.sNalHeaderExt,
                                                        pCtx->iFrameBsSize - pCtx->iPosBsBuffer,
                                                        pCtx->pFrameBs + pCtx->iPosBsBuffer,
                                                        &pLayerBsInfo->pNalLengthInByte[iNalIdxInLayer]);
                    if (pCtx->iEncoderError != ENC_RETURN_SUCCESS)
                    {
                        return pCtx->iEncoderError;
                    }
                    int iSliceSize = pLayerBsInfo->pNalLengthInByte[iNalIdxInLayer];

                    pCtx->iPosBsBuffer += iSliceSize;
                    iLayerSize += iSliceSize;
                    ++iNalIdxInLayer;
                    ++iSliceIdx;
                }

                pLayerBsInfo->uiLayerType = (byte)LAYER_TYPE.VIDEO_CODING_LAYER;
                pLayerBsInfo->uiSpatialId = (byte)iCurDid;
                pLayerBsInfo->uiTemporalId = (byte)iCurTid;
                pLayerBsInfo->uiQualityId = 0;
                pLayerBsInfo->iNalCount = iNalIdxInLayer;
                pLayerBsInfo->eFrameType = eFrameType;
                pLayerBsInfo->iSubSeqId = GetSubSequenceId(pCtx, eFrameType);
            }

            // pfWelsRcPostFrameSkipping: null with rate control off.

            // deblocking filter
            if (!pCtx->pCurDqLayer->bDeblockingParallelFlag &&
                eNalRefIdc != EWelsNalRefIdc.NRI_PRI_LOWEST &&
                (pSvcParam->sDependencyLayers[iCurDid].iHighestTemporalId == 0
                 || iCurTid < pSvcParam->sDependencyLayers[iCurDid].iHighestTemporalId))
            {
                PerformDeblockingFilter(pCtx);
            }

            pCtx->pFuncList->pfRc.pfWelsRcPictureInfoUpdate(pCtx, iLayerSize);
            iFrameSize += iLayerSize;
            pCtx->pDecPic->iFrameAverageQp = pCtx->pWelsSvcRc[iCurDid].iAverageFrameQp;

            // pfUpdateFMESwitch (screen content), the reference list update (WelsUpdateRefList,
            // with the reference expansion) and UpdateSpatialPictures come with E5: they follow
            // the bytes and the reconstruction.

            int iCountNal = pLayerBsInfo->iNalCount;
            ++iLayerNum;
            ++pLayerBsInfo;
            ++pCtx->pOut->iLayerBsIndex;
            pLayerBsInfo->pBsBuf = pCtx->pFrameBs + pCtx->iPosBsBuffer;
            pLayerBsInfo->pNalLengthInByte = (pLayerBsInfo - 1)->pNalLengthInByte + iCountNal;
            // WritePadding: rate control only.
            pCtx->eLastNalPriority[iCurDid] = eNalRefIdc;
            ++iSpatialIdx;
        }

        if (iLayerNum > codec_app_def.MAX_LAYER_NUM_OF_FRAME)
        {
            return 1;
        }
        pFbi->iLayerNum = iLayerNum;
        pLayerBsInfo->eFrameType = eFrameType;
        pFbi->iFrameSizeInBytes = iFrameSize;
        pFbi->eFrameType = eFrameType;
        for (int k = 0; k < pFbi->iLayerNum; k++)
        {
            if (pFbi->eFrameType != pFbi->sLayerInfo[k].eFrameType)
            {
                pFbi->eFrameType = EVideoFrameType.videoFrameTypeIPMixed;
            }
        }
        return ENC_RETURN_SUCCESS;
    }

    // ---- release: FreeMemorySvc, for what this driver allocates ----

    /// <summary>
    /// Frees the encoder's memory and returns what its CMemoryAlign still counts in use,
    /// which is 0 when every allocation was freed.
    /// </summary>
    public uint Release()
    {
        if (_ctx == null)
        {
            return 0;
        }
        sWelsEncCtx* pCtx = _ctx;
        CMemoryAlign* pMa = _pMa;
        SWelsSvcCodingParam* pParam = pCtx->pSvcParam;

        if (_pSrcPic != null)
        {
            SPicture* p = _pSrcPic;
            FreePicture(pMa, &p);
            _pSrcPic = null;
        }
        if (pCtx->pStrideTab != null)
        {
            if (!pCtx->pStrideTab->pStrideDecBlockOffset[0][1].IsNull)
            {
                pMa->WelsFree((int*)pCtx->pStrideTab->pStrideDecBlockOffset[0][1], "pBase");
            }
            pMa->WelsFree(pCtx->pStrideTab, "SStrideTables");
            pCtx->pStrideTab = null;
        }
        if (pCtx->pDqIdcMap != null)
        {
            pMa->WelsFree(pCtx->pDqIdcMap, "pDqIdcMap");
            pCtx->pDqIdcMap = null;
        }
        if (pCtx->pOut != null)
        {
            pMa->WelsFree(pCtx->pOut->pBsBuffer, "pOut->pBsBuffer");
            pMa->WelsFree(pCtx->pOut->sNalList, "pOut->sNalList");
            pMa->WelsFree(pCtx->pOut->pNalLen, "pOut->pNalLen");
            pMa->WelsFree(pCtx->pOut, "SWelsEncoderOutput");
            pCtx->pOut = null;
        }
        pMa->WelsFree(pCtx->pFrameBs, "pFrameBs");
        pCtx->pFrameBs = null;
        if (pCtx->ppMbListD != null)
        {
            pMa->WelsFree(pCtx->ppMbListD[0], "ppMbListD[0]");
            pMa->WelsFree(pCtx->ppMbListD, "ppMbListD");
            pCtx->ppMbListD = null;
        }
        if (pCtx->ppDqLayerList != null && pParam != null)
        {
            for (int i = 0; i < pParam->iSpatialLayerNum; i++)
            {
                FreeDqLayer(&pCtx->ppDqLayerList[i], pMa);
            }
            pMa->WelsFree(pCtx->ppDqLayerList, "ppDqLayerList");
            pCtx->ppDqLayerList = null;
        }
        if (pCtx->ppRefPicListExt != null && pParam != null)
        {
            for (int i = 0; i < pParam->iSpatialLayerNum; i++)
            {
                FreeRefList(&pCtx->ppRefPicListExt[i], pMa, pParam->iMaxNumRefFrame);
            }
            pMa->WelsFree(pCtx->ppRefPicListExt, "ppRefPicListExt");
            pCtx->ppRefPicListExt = null;
        }
        pMa->WelsFree(pCtx->pSpsArray, "pSpsArray");
        pMa->WelsFree(pCtx->pSubsetArray, "pSubsetArray");
        pMa->WelsFree(pCtx->pPPSArray, "pPPSArray");
        pCtx->pSpsArray = null;
        pCtx->pSubsetArray = null;
        pCtx->pPPSArray = null;
        pMa->WelsFree(pCtx->pIntra4x4PredModeBlocks, "pIntra4x4PredModeBlocks");
        pMa->WelsFree(pCtx->pNonZeroCountBlocks, "pNonZeroCountBlocks");
        pMa->WelsFree(pCtx->pMvUnitBlock4x4, "pMvUnitBlock4x4");
        pMa->WelsFree(pCtx->pRefIndexBlock4x4, "pRefIndexBlock4x4");
        pMa->WelsFree(pCtx->pSadCostMb, "pSadCostMb");
        pMa->WelsFree(pCtx->pWelsSvcRc, "pWelsSvcRc");
        if (pCtx->pFuncList != null)
        {
            IWelsParametersetStrategy.DeleteParametersetStrategy(ref pCtx->pFuncList->pParametersetStrategy);
            pMa->WelsFree(pCtx->pFuncList, "SWelsFuncPtrList");
            pCtx->pFuncList = null;
        }
        pMa->WelsFree(pCtx->pSvcParam, "SWelsSvcCodingParam");
        pCtx->pSvcParam = null;

        uint uiLeft = pMa->WelsGetMemoryUsage();
        WELS_DELETE_CMemoryAlign(ref _pMa);
        NativeMemory.Free(_ctx);
        _ctx = null;
        return uiLeft;
    }

    public void Dispose()
    {
        Release();
        if (_info != null)
        {
            NativeMemory.Free(_info);
            _info = null;
        }
    }
}
