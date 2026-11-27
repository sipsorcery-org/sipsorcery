//-----------------------------------------------------------------------------
// Filename: EncoderDriver.cs
//
// Description: The encoder's init, frame loop and release for one spatial
// layer, over the ported functions, for the frame by frame acceptance tests:
// IdrTests (E4b, the first frame of each configuration), StreamTests (E5b,
// P streams), RcTests (E6, rate controlled streams), and PreprocessTests and
// UpstreamEncoderHashTests (E7, the preprocessing).
//
// The encoder API and its init and frame loops come with E8, so
// EncoderDriver drives the ported functions the way
// CWelsH264SVCEncoder::Initialize, InitializeExt and EncodeFrame,
// WelsInitEncoderExt, RequestMemorySvc, WelsEncoderEncodeExt,
// WelsUninitEncoderExt and FreeMemorySvc do, with the library's
// preprocessing (pCtx->pVpp, CWelsPreProcess) and the timestamp bookkeeping
// of UpdateStatistics. Configurations outside that scope (CABAC, screen
// content, several spatial layers, SM_SIZELIMITED_SLICE, threads) throw
// NotSupportedException at init.
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
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using SIPSorcery.H264.Encoder;
using static SIPSorcery.H264.Encoder.WelsEnc;
using static SIPSorcery.H264.WelsCommon;

namespace SIPSorcery.H264.TestVectors;

/// <summary>
/// What EncodeFrame returned and wrote, as the harness prints it: the return codes, the
/// frame type, the temporal id of each VCL layer, every layer's NAL units and the SHA1 of
/// the whole frame. Comparable says whether the reconstruction is what a decoder outputs
/// for these bytes: false for a frame the encoder does not deblock (the non-reference
/// frames of the highest temporal layer) while the slices ask for the filter.
/// </summary>
internal sealed record EncodedFrame(int ReturnCode, int InternalReturnCode, string FrameType, string TemporalIds,
                                    string Layers, string Sha1, byte[] Bytes, bool Comparable);

/// <summary>
/// The encoder's init, frame loop and release for one spatial layer, over the ported
/// functions (see the file header). One instance is one encoder.
/// </summary>
internal sealed unsafe class EncoderDriver : IDisposable
{
    private const int cmResultSuccess = 0;
    private const int cmInitParaError = 1;
    private const int cmUnknownReason = 2;
    private const int cmMallocMemeError = 3;
    private const int cmUnsupportedData = 5;

    private sWelsEncCtx* _ctx;
    private CMemoryAlign* _pMa;
    private SFrameBSInfo* _info;
    private int _iPicWidth;
    private int _iPicHeight;
    private bool _uninitialised;    // EncodeFrameInternal's WelsUninitEncoderExt after a memory error
    private SPicture* _fsnr;        // the last frame's reconstruction (WelsEncoderEncodeExt's fsnr)
    private bool _fsnrDeblocked;

    public EncoderDriver()
    {
        _info = (SFrameBSInfo*)NativeMemory.AllocZeroed((nuint)sizeof(SFrameBSInfo));
    }

    public int PicWidth => _iPicWidth;
    public int PicHeight => _iPicHeight;

    /// <summary>
    /// The top spatial layer's size (E7): the source's, or smaller when the preprocessing
    /// downsamples the source to the layer. The source's when the encoder is not initialised.
    /// </summary>
    public int CodedWidth => _ctx == null ? _iPicWidth
        : _ctx->pSvcParam->sDependencyLayers[_ctx->pSvcParam->iSpatialLayerNum - 1].iActualWidth;
    public int CodedHeight => _ctx == null ? _iPicHeight
        : _ctx->pSvcParam->sDependencyLayers[_ctx->pSvcParam->iSpatialLayerNum - 1].iActualHeight;

    /// <summary>
    /// Initialize (api=base) or InitializeExt with the settings applied, as the harness does.
    /// Returns the API's code.
    /// </summary>
    public int Initialize(string settings)
    {
        ParamSetEncoder.ParseSettings(settings, out SEncParamExt ext, out SEncParamBase bas, out bool isBase);
        return isBase ? Initialize(bas) : InitializeExt(ext);
    }

    /// <summary>CWelsH264SVCEncoder::Initialize. Returns the API's code.</summary>
    public int Initialize(SEncParamBase bas)
    {
        _iPicWidth = bas.iPicWidth;
        _iPicHeight = bas.iPicHeight;

        // SWelsSvcCodingParam sConfig; (the constructor calls FillDefault)
        var sConfig = default(SWelsSvcCodingParam);
        sConfig.FillDefault();
        if (sConfig.ParamBaseTranscode(bas) != 0)
        {
            return cmInitParaError;
        }
        return InitializeInternal(&sConfig);
    }

    /// <summary>CWelsH264SVCEncoder::InitializeExt. Returns the API's code.</summary>
    public int InitializeExt(SEncParamExt ext)
    {
        _iPicWidth = ext.iPicWidth;
        _iPicHeight = ext.iPicHeight;

        // SWelsSvcCodingParam sConfig; (the constructor calls FillDefault)
        var sConfig = default(SWelsSvcCodingParam);
        sConfig.FillDefault();
        if (sConfig.ParamTranscode(ext) != 0)
        {
            return cmInitParaError;
        }
        return InitializeInternal(&sConfig);
    }

    /// <summary>CWelsH264SVCEncoder::InitializeInternal.</summary>
    private int InitializeInternal(SWelsSvcCodingParam* pCfg)
    {
        int iRet = ParamSetEncoder.InitializeInternalChecks(pCfg);
        if (iRet != 0)
        {
            return iRet;
        }
        return WelsInitEncoderExt(pCfg) != 0 ? cmInitParaError : cmResultSuccess;
    }

    /// <summary>
    /// EncodeFrame (CWelsH264SVCEncoder::EncodeFrame and EncodeFrameInternal) of a packed
    /// I420 picture of the initialised size, as the harness passes it.
    /// </summary>
    public EncodedFrame EncodeFrame(byte[] buf, long uiTimeStamp)
    {
        int w = _iPicWidth, h = _iPicHeight;
        int iInternal = 0;
        int rv;
        _fsnr = null;
        _fsnrDeblocked = false;
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
            pic.uiTimeStamp = uiTimeStamp;
            *_info = default;

            if (pic.iPicWidth < 16 || pic.iPicHeight < 16)
            {
                rv = cmUnsupportedData;
            }
            else
            {
                // After a memory error EncodeFrameInternal has freed the context
                // (WelsUninitEncoderExt), and WelsEncoderEncodeExt (NULL) says so. This driver
                // keeps the memory until Release.
                iInternal = _uninitialised ? ENC_RETURN_MEMALLOCERR : WelsEncoderEncodeExt(_info, &pic);
                if (iInternal == ENC_RETURN_MEMALLOCERR || iInternal == ENC_RETURN_MEMOVERFLOWFOUND
                    || iInternal == ENC_RETURN_VLCOVERFLOWFOUND)
                {
                    rv = cmMallocMemeError;
                    _uninitialised = true;
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
                    // UpdateStatistics: the rate control's last timestamp (the statistics are not ported).
                    _ctx->uiLastTimestamp = _info->uiTimeStamp;
                }
            }
        }

        // The harness's view of the output: every layer's NAL units and each VCL layer's
        // temporal id, then the whole frame.
        var layers = new StringBuilder();
        var tids = new StringBuilder();
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
                frame.AddRange(nal.ToArray());
                q += l->pNalLengthInByte[j];
            }
        }
        byte[] bytes = frame.ToArray();
        bool comparable = _fsnr != null && (_fsnrDeblocked || _ctx->pSvcParam->iLoopFilterDisableIdc == 1);
        return new EncodedFrame(rv, iInternal, FrameTypeName(_info->eFrameType), tids.Length == 0 ? "-" : tids.ToString(),
                                layers.Length == 0 ? "-" : layers.ToString(), bytes.Length == 0 ? "-" : Sha1(bytes), bytes,
                                comparable);
    }

    /// <summary>The top left w x h of a plane of the last frame's reconstructed picture.</summary>
    public byte[] Reconstruction(int plane, int w, int h)
    {
        SPicture* pDecPic = _fsnr;
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
            throw new NotSupportedException($"EncoderDriver: {what} is not ported yet.");
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

        // What the port codes by E7.
        Require(pCodingParam->iUsageType == EUsageType.CAMERA_VIDEO_REAL_TIME, "screen content (Phase 4)");
        Require(pCodingParam->iEntropyCodingModeFlag == 0, "CABAC");
        Require(pCodingParam->iSpatialLayerNum == 1 && !pCodingParam->bSimulcastAVC, "more than one spatial layer (Phase 4)");
        Require(pCodingParam->iMultipleThreadIdc == 1, "threading (Phase 4)");
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
        InitFunctionPointers(pCtx, pCtx->pSvcParam, uiCpuFeatureFlags);

        pCtx->iActiveThreadsNum = (short)pCodingParam->iMultipleThreadIdc;
        pCtx->iMaxSliceCount = iSliceNum;
        iRet = RequestMemorySvc();
        if (iRet != 0)
        {
            return iRet;
        }

        // WelsCabacInit: CAVLC only.
        WelsRcInitModule(pCtx, pCtx->pSvcParam->iRCMode);

        pCtx->pVpp = CWelsPreProcess.CreatePreProcess(pCtx);
        if (pCtx->pVpp == null)
        {
            return 1;
        }
        if ((iRet = pCtx->pVpp->AllocSpatialPictures(pCtx, pCtx->pSvcParam)) != 0)
        {
            return iRet;
        }

        pCtx->uiLastTimestamp = -1;
        pCtx->bDeliveryFlag = true;
        return 0;
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
        pCtx->pReferenceStrategy = IWelsReferenceStrategy.CreateReferenceStrategy(pCtx, pParam->iUsageType,
            pParam->bEnableLongTermReference);
        if (pCtx->pReferenceStrategy == null)
        {
            return 1;
        }

        pCtx->pIntra4x4PredModeBlocks = (sbyte*)pMa->WelsMallocz((uint)(iCountMaxMbNum * INTRA_4x4_MODE_NUM), "pIntra4x4PredModeBlocks");
        pCtx->pNonZeroCountBlocks = (sbyte*)pMa->WelsMallocz((uint)(iCountMaxMbNum * MB_LUMA_CHROMA_BLOCK4x4_NUM), "pNonZeroCountBlocks");
        pCtx->pMvUnitBlock4x4 = (SMVUnitXY*)pMa->WelsMallocz((uint)(iCountMaxMbNum * 2 * MB_BLOCK4x4_NUM * sizeof(SMVUnitXY)), "pMvUnitBlock4x4");
        pCtx->pRefIndexBlock4x4 = (sbyte*)pMa->WelsMallocz((uint)(iCountMaxMbNum * 2 * MB_BLOCK8x8_NUM * sizeof(sbyte)), "pRefIndexBlock4x4");
        pCtx->pSadCostMb = (int*)pMa->WelsMallocz((uint)(iCountMaxMbNum * sizeof(int)), "pSadCostMb");

        pCtx->iGlobalQp = 26;   // global qp in default

        pCtx->pLtr = (SLTRState*)pMa->WelsMallocz((uint)(kiNumDependencyLayers * sizeof(SLTRState)), "SLTRState");
        if (pCtx->pLtr == null)
        {
            return 1;
        }
        for (int i = 0; i < kiNumDependencyLayers; i++)
        {
            ResetLtrState(&pCtx->pLtr[i]);
        }

        // stride tables
        if (AllocStrideTables(&pCtx, kiNumDependencyLayers) != 0)
        {
            return 1;
        }

        pCtx->pWelsSvcRc = (SWelsSvcRc*)pMa->WelsMallocz((uint)(kiNumDependencyLayers * sizeof(SWelsSvcRc)), "pWelsSvcRc");

        // pVaa (camera content: SVAAFrameInfo)
        pCtx->pVaa = (SVAAFrameInfo*)pMa->WelsMallocz((uint)sizeof(SVAAFrameInfo), "pVaa");
        if (pCtx->pVaa == null)
        {
            return 1;
        }
        if (pParam->bEnableAdaptiveQuant) // malloc mem
        {
            pCtx->pVaa->sAdaptiveQuantParam.pMotionTextureUnit = (SMotionTextureUnit*)pMa->WelsMallocz(
                (uint)(iCountMaxMbNum * sizeof(SMotionTextureUnit)), "pVaa->sAdaptiveQuantParam.pMotionTextureUnit");
            pCtx->pVaa->sAdaptiveQuantParam.pMotionTextureIndexToDeltaQp = (sbyte*)pMa->WelsMallocz(
                (uint)(iCountMaxMbNum * sizeof(sbyte)), "pVaa->sAdaptiveQuantParam.pMotionTextureIndexToDeltaQp");
            if (pCtx->pVaa->sAdaptiveQuantParam.pMotionTextureUnit == null
                || pCtx->pVaa->sAdaptiveQuantParam.pMotionTextureIndexToDeltaQp == null)
            {
                return 1;
            }
        }
        pCtx->pVaa->pVaaBackgroundMbFlag = (sbyte*)pMa->WelsMallocz((uint)(iCountMaxMbNum * sizeof(sbyte)),
            "pVaa->pVaaBackgroundMbFlag");
        pCtx->pVaa->sVaaCalcInfo.pSad8x8 = (int*)pMa->WelsMallocz((uint)(iCountMaxMbNum * 4 * sizeof(int)),
            "pVaa->sVaaCalcInfo.sad8x8");
        pCtx->pVaa->sVaaCalcInfo.pSsd16x16 = (int*)pMa->WelsMallocz((uint)(iCountMaxMbNum * sizeof(int)),
            "pVaa->sVaaCalcInfo.pSsd16x16");
        pCtx->pVaa->sVaaCalcInfo.pSum16x16 = (int*)pMa->WelsMallocz((uint)(iCountMaxMbNum * sizeof(int)),
            "pVaa->sVaaCalcInfo.pSum16x16");
        pCtx->pVaa->sVaaCalcInfo.pSumOfSquare16x16 = (int*)pMa->WelsMallocz((uint)(iCountMaxMbNum * sizeof(int)),
            "pVaa->sVaaCalcInfo.pSumOfSquare16x16");
        if (pCtx->pVaa->pVaaBackgroundMbFlag == null || pCtx->pVaa->sVaaCalcInfo.pSad8x8 == null
            || pCtx->pVaa->sVaaCalcInfo.pSsd16x16 == null || pCtx->pVaa->sVaaCalcInfo.pSum16x16 == null
            || pCtx->pVaa->sVaaCalcInfo.pSumOfSquare16x16 == null)
        {
            return 1;
        }
        if (pParam->bEnableBackgroundDetection) // BGD control
        {
            pCtx->pVaa->sVaaCalcInfo.pSumOfDiff8x8 = (int*)pMa->WelsMallocz((uint)(iCountMaxMbNum * 4 * sizeof(int)),
                "pVaa->sVaaCalcInfo.pSumOfDiff8x8");
            pCtx->pVaa->sVaaCalcInfo.pMad8x8 = (byte*)pMa->WelsMallocz((uint)(iCountMaxMbNum * 4 * sizeof(byte)),
                "pVaa->sVaaCalcInfo.pMad8x8");
            if (pCtx->pVaa->sVaaCalcInfo.pSumOfDiff8x8 == null || pCtx->pVaa->sVaaCalcInfo.pMad8x8 == null)
            {
                return 1;
            }
        }

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

        int iMvdRange = 0;
        GetMvMvdRange(pParam, ref pCtx->iMvRange, ref iMvdRange);
        uint kuiMvdInterTableSize = (uint)(iMvdRange << 2); //intepel*4=qpel
        uint kuiMvdInterTableStride = 1 + (kuiMvdInterTableSize << 1); //qpel_mv_range*2=(+/-);
        uint kuiMvdCacheAlignedSize = kuiMvdInterTableStride * sizeof(ushort);
        pCtx->iMvdCostTableSize = (int)kuiMvdInterTableSize;
        pCtx->iMvdCostTableStride = (int)kuiMvdInterTableStride;
        pCtx->pMvdCostTable = (ushort*)pMa->WelsMallocz(52 * kuiMvdCacheAlignedSize, "pMvdCostTable");
        if (pCtx->pMvdCostTable == null)
        {
            return 1;
        }
        MvdCostInit(pCtx->pMvdCostTable, (int)kuiMvdInterTableStride); //should put to a better place?

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
    /// PrepareEncodeFrame (not simulcast): the rate control's skip decision, the frame type,
    /// the temporal level and, for an IDR, the parameter sets.
    /// </summary>
    private EVideoFrameType PrepareEncodeFrame(ref SLayerBSInfo* pLayerBsInfo, int iSpatialNum, ref sbyte iCurDid,
                                               ref int iCurTid, ref int iLayerNum, ref int iFrameSize, long uiTimeStamp)
    {
        sWelsEncCtx* pCtx = _ctx;
        SWelsSvcCodingParam* pSvcParam = pCtx->pSvcParam;

        bool bSkipFrameFlag = WelsRcCheckFrameStatus(pCtx, uiTimeStamp, iSpatialNum, iCurDid);
        EVideoFrameType eFrameType = DecideFrameType(pCtx, (sbyte)iSpatialNum, iCurDid, bSkipFrameFlag);
        if (eFrameType == EVideoFrameType.videoFrameTypeSkip)
        {
            if (pCtx->pFuncList->pfRc.pfWelsUpdateBufferWhenSkip != null)
            {
                for (int i = 0; i < iSpatialNum; i++)
                {
                    pCtx->pFuncList->pfRc.pfWelsUpdateBufferWhenSkip(pCtx, pCtx->sSpatialIndexMap[i].iDid);
                }
            }
            WelsLog(&pCtx->sLogCtx, codec_app_def.WELS_LOG_DEBUG,
                    $"[Rc] Frame timestamp = {uiTimeStamp}, iDid = {iCurDid},skip one frame due to target_br, continual skipped {pCtx->pWelsSvcRc[iCurDid].iContinualSkipFrames} frames");
        }
        else
        {
            SSpatialLayerInternal* pParamInternal = &pSvcParam->sDependencyLayers[iCurDid];

            iCurTid = GetTemporalLevel(pParamInternal, pParamInternal->iCodingIndex, (int)pSvcParam->uiGopSize);
            pCtx->uiTemporalId = (byte)iCurTid;
            if (eFrameType == EVideoFrameType.videoFrameTypeIDR)
            {
                // write parameter sets bitstream or SEI/SSEI (if any) here
                if (((int)EParameterSetStrategy.SPS_LISTING & (int)pSvcParam->eSpsPpsIdStrategy) == 0)
                {
                    pCtx->iEncoderError = WriteSsvcParaset(pCtx, iSpatialNum, ref pLayerBsInfo, ref iLayerNum, ref iFrameSize);
                }
                else
                {
                    pCtx->iEncoderError = WriteSavcParaset_Listing(pCtx, iSpatialNum, ref pLayerBsInfo, ref iLayerNum,
                                                                   ref iFrameSize);
                }
            }
        }
        return eFrameType;
    }

    /// <summary>ForceCodingIDR (not simulcast; sEncoderStatistics is not ported).</summary>
    private static void ForceCodingIDR(sWelsEncCtx* pCtx)
    {
        for (int iDid = 0; iDid < pCtx->pSvcParam->iSpatialLayerNum; iDid++)
        {
            SSpatialLayerInternal* pParamInternal = &pCtx->pSvcParam->sDependencyLayers[iDid];
            pParamInternal->iCodingIndex = 0;
            pParamInternal->iFrameIndex = 0;
            pParamInternal->iFrameNum = 0;
            pParamInternal->iPOC = 0;
            pParamInternal->bEncCurFrmAsIdrFlag = true;
        }
        pCtx->bCheckWindowStatusRefreshFlag = false;
    }

    /// <summary>WelsEncoderEncodeExt for one spatial layer, not simulcast (see the file header).</summary>
    private int WelsEncoderEncodeExt(SFrameBSInfo* pFbi, SSourcePicture* pSrcPic)
    {
        sWelsEncCtx* pCtx = _ctx;
        if (pCtx == null)
        {
            return ENC_RETURN_MEMALLOCERR;
        }
        SLayerBSInfo* pLayerBsInfo = (SLayerBSInfo*)&pFbi->sLayerInfo;
        SWelsSvcCodingParam* pSvcParam = pCtx->pSvcParam;
        SPicture* fsnr = null;
        int iLayerNum = 0;
        int iLayerSize = 0;
        int iSpatialNum = 0;
        int iSpatialIdx = 0;
        int iFrameSize = 0;
        int iNalIdxInLayer = 0;
        int iCountNal = 0;
        EVideoFrameType eFrameType = EVideoFrameType.videoFrameTypeInvalid;
        int iCurWidth = 0;
        int iCurHeight = 0;
        EWelsNalUnitType eNalType = EWelsNalUnitType.NAL_UNIT_UNSPEC_0;
        EWelsNalRefIdc eNalRefIdc = EWelsNalRefIdc.NRI_PRI_LOWEST;
        sbyte iCurDid = 0;
        int iCurTid = 0;
        bool bAvcBased = false;

        pCtx->iEncoderError = ENC_RETURN_SUCCESS;
        pCtx->bCurFrameMarkedAsSceneLtr = false;
        pFbi->eFrameType = EVideoFrameType.videoFrameTypeSkip;
        pFbi->iLayerNum = 0; // for initialization
        pFbi->uiTimeStamp = GetTimestampForRc(pSrcPic->uiTimeStamp, pCtx->uiLastTimestamp,
                                              pSvcParam->sSpatialLayers[pSvcParam->iSpatialLayerNum - 1].fFrameRate);
        for (int iNalIdx = 0; iNalIdx < codec_app_def.MAX_LAYER_NUM_OF_FRAME; iNalIdx++)
        {
            pFbi->sLayerInfo[iNalIdx].eFrameType = EVideoFrameType.videoFrameTypeSkip;
            pFbi->sLayerInfo[iNalIdx].iNalCount = 0;
        }
        // perform csc/denoise/downsample/padding, generate spatial layers
        int iRet = pCtx->pVpp->BuildSpatialPicList(pCtx, pSrcPic, &iSpatialNum);
        if (iRet != ENC_RETURN_SUCCESS)
        {
            return iRet;
        }

        if (pCtx->pFuncList->pfRc.pfWelsUpdateMaxBrWindowStatus != null)
        {
            pCtx->pFuncList->pfRc.pfWelsUpdateMaxBrWindowStatus(pCtx, iSpatialNum, pFbi->uiTimeStamp);
        }

        if (iSpatialNum < 1)
        {
            for (int iDidIdx = 0; iDidIdx < pSvcParam->iSpatialLayerNum; iDidIdx++)
            {
                SSpatialLayerInternal* p = &pSvcParam->sDependencyLayers[iDidIdx];
                p->iCodingIndex++;
            }
            pFbi->eFrameType = EVideoFrameType.videoFrameTypeSkip;
            pLayerBsInfo->eFrameType = EVideoFrameType.videoFrameTypeSkip;
            return ENC_RETURN_SUCCESS;
        }

        InitBitStream(pCtx);
        pLayerBsInfo->pBsBuf = pCtx->pFrameBs;
        pLayerBsInfo->pNalLengthInByte = pCtx->pOut->pNalLen;
        iCurDid = (sbyte)pCtx->sSpatialIndexMap[0].iDid;
        pCtx->pCurDqLayer = pCtx->ppDqLayerList[iCurDid];
        pCtx->pCurDqLayer->pRefLayer = null;
        // not simulcast (rejected at init)
        eFrameType = PrepareEncodeFrame(ref pLayerBsInfo, iSpatialNum, ref iCurDid, ref iCurTid, ref iLayerNum, ref iFrameSize,
                                        pFbi->uiTimeStamp);
        if (eFrameType == EVideoFrameType.videoFrameTypeSkip)
        {
            pFbi->eFrameType = EVideoFrameType.videoFrameTypeSkip;
            pLayerBsInfo->eFrameType = EVideoFrameType.videoFrameTypeSkip;
            return ENC_RETURN_SUCCESS;
        }

        while (iSpatialIdx < iSpatialNum)
        {
            iCurDid = (sbyte)pCtx->sSpatialIndexMap[iSpatialIdx].iDid;
            SSpatialLayerConfig* pParam = &pSvcParam->sSpatialLayers[iCurDid];
            SSpatialLayerInternal* pParamInternal = &pSvcParam->sDependencyLayers[iCurDid];
            int iDecompositionStages = pSvcParam->sDependencyLayers[iCurDid].iDecompositionStages;
            pCtx->pCurDqLayer = pCtx->ppDqLayerList[iCurDid];
            pCtx->uiDependencyId = (byte)iCurDid;

            InitFrameCoding(pCtx, eFrameType, iCurDid);
            pCtx->pVpp->AnalyzeSpatialPic(pCtx, iCurDid);

            SPicture* pEncPic = pCtx->sSpatialIndexMap[iSpatialIdx].pSrc;
            pCtx->pEncPic = pEncPic;
            pCtx->pEncPic->iPictureType = (int)pCtx->eSliceType;
            pCtx->pEncPic->iFramePoc = pParamInternal->iPOC;

            iCurWidth = pParam->iVideoWidth;
            iCurHeight = pParam->iVideoHeight;
            // Encoding this picture might mulitiple sQualityStat layers potentially be encoded as followed
            // SM_FIXEDSLCNUM_SLICE adjusts the slicing (AdjustBaseLayer) only with threads;
            // SM_SIZELIMITED_SLICE is rejected at init.

            /* coding each spatial layer, only one sQualityStat layer within spatial support */
            int iSliceCount = 1;
            if (iLayerNum >= codec_app_def.MAX_LAYER_NUM_OF_FRAME) // check available layer_bs_info writing as follows
            {
                return ENC_RETURN_UNSUPPORTED_PARA;
            }

            iNalIdxInLayer = 0;
            bAvcBased = pSvcParam->bSimulcastAVC || iCurDid == BASE_DEPENDENCY_ID;
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
            else // more details for other temporal layers?
            {
                eNalRefIdc = EWelsNalRefIdc.NRI_PRI_HIGHEST;
            }
            pCtx->eNalType = eNalType;
            pCtx->eNalPriority = eNalRefIdc;

            pCtx->pDecPic = pCtx->ppRefPicListExt[iCurDid]->pNextBuffer;
            fsnr = pCtx->pDecPic;
            pCtx->pDecPic->iPictureType = (int)pCtx->eSliceType;
            pCtx->pDecPic->iFramePoc = pParamInternal->iPOC;

            WelsInitCurrentLayer(pCtx, iCurWidth, iCurHeight);

            pCtx->pReferenceStrategy->MarkPic();
            if (!pCtx->pReferenceStrategy->BuildRefList(pParamInternal->iPOC, 0))
            {
                // WelsBuildRefList failed for P frames. ForceCodingIDR!
                eFrameType = EVideoFrameType.videoFrameTypeIDR;
                pCtx->iEncoderError = ENC_RETURN_CORRECTED;
                break;
            }
            if (pCtx->eSliceType != EWelsSliceType.I_SLICE)
            {
                pCtx->pReferenceStrategy->AfterBuildRefList();
            }
            if (pSvcParam->iRCMode != RC_MODES.RC_OFF_MODE)
            {
                pCtx->pVpp->AnalyzePictureComplexity(pCtx, pCtx->pEncPic,
                    (pCtx->eSliceType == EWelsSliceType.P_SLICE && pCtx->iNumRef0 > 0) ? pCtx->pRefList0[0] : null, iCurDid,
                    pCtx->eSliceType == EWelsSliceType.P_SLICE && pSvcParam->bEnableBackgroundDetection);
            }
            WelsUpdateRefSyntax(pCtx, pParamInternal->iPOC, (int)eFrameType); //get reordering syntax used for writing slice header and transmit to encoder.
            PrefetchReferencePicture(pCtx, eFrameType); // update reference picture for current pDq layer
            pCtx->pFuncList->pfRc.pfWelsRcPictureInit(pCtx, pFbi->uiTimeStamp);
            PreprocessSliceCoding(pCtx); // MUST be called after pfWelsRcPictureInit() and WelsInitCurrentLayer()

            iLayerSize = 0;
            if (SliceModeEnum.SM_SINGLE_SLICE == pParam->sSliceArgument.uiSliceMode) // only one slice within a sQualityStat layer
            {
                SSlice* pCurSlice = &pCtx->pCurDqLayer->sSliceBufferInfo[0].pSliceBuffer[0];

                if (pCtx->bNeedPrefixNalFlag)
                {
                    int iPayloadSize = 0;
                    pCtx->iEncoderError = AddPrefixNal(pCtx, pLayerBsInfo, &pLayerBsInfo->pNalLengthInByte[0], &iNalIdxInLayer,
                                                       eNalType, eNalRefIdc, ref iPayloadSize);
                    if (pCtx->iEncoderError != ENC_RETURN_SUCCESS)
                    {
                        return pCtx->iEncoderError;
                    }
                    iLayerSize += iPayloadSize;
                }

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
                iSliceCount = GetCurrentSliceNum(pCtx->pCurDqLayer);
                while (iSliceIdx < iSliceCount)
                {
                    if (bNeedPrefix)
                    {
                        int iPayloadSize = 0;
                        pCtx->iEncoderError = AddPrefixNal(pCtx, pLayerBsInfo, &pLayerBsInfo->pNalLengthInByte[0], &iNalIdxInLayer,
                                                           eNalType, eNalRefIdc, ref iPayloadSize);
                        if (pCtx->iEncoderError != ENC_RETURN_SUCCESS)
                        {
                            return pCtx->iEncoderError;
                        }
                        iLayerSize += iPayloadSize;
                    }

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

            // WelsRcPostFrameSkipping (the bit rate modes) returns false, so the stack back
            // (StackBackEncoderStatus, ClearFrameBsInfo, WelsRcPostFrameSkippedUpdate) never runs.
            if (pCtx->pFuncList->pfRc.pfWelsRcPostFrameSkipping != null
                && pCtx->pFuncList->pfRc.pfWelsRcPostFrameSkipping(pCtx, iCurDid, pFbi->uiTimeStamp))
            {
                Require(false, "post frame skipping (StackBackEncoderStatus)");
            }

            // deblocking filter
            if (!pCtx->pCurDqLayer->bDeblockingParallelFlag &&
                eNalRefIdc != EWelsNalRefIdc.NRI_PRI_LOWEST &&
                (pSvcParam->sDependencyLayers[iCurDid].iHighestTemporalId == 0
                 || iCurTid < pSvcParam->sDependencyLayers[iCurDid].iHighestTemporalId))
            {
                PerformDeblockingFilter(pCtx);
                _fsnrDeblocked = true;
            }

            pCtx->pFuncList->pfRc.pfWelsRcPictureInfoUpdate(pCtx, iLayerSize);
            iFrameSize += iLayerSize;
            RcTraceFrameBits(pCtx, pFbi->uiTimeStamp, iFrameSize);
            pCtx->pDecPic->iFrameAverageQp = pCtx->pWelsSvcRc[iCurDid].iAverageFrameQp;

            //update scc related
            pCtx->pFuncList->pfUpdateFMESwitch(pCtx->pCurDqLayer);

            // reference picture list update
            if (eNalRefIdc != EWelsNalRefIdc.NRI_PRI_LOWEST)
            {
                if (!pCtx->pReferenceStrategy->UpdateRefList())
                {
                    // WelsUpdateRefList failed. ForceCodingIDR!
                    //the above is to set the next frame to be IDR
                    pCtx->iEncoderError = ENC_RETURN_CORRECTED;
                    break;
                }
            }

            // The MinCr check is a log, and the PSNR (bPsnrY/U/V) output information the harness
            // does not ask for.
            pLayerBsInfo->rPsnr[0] = 0;
            pLayerBsInfo->rPsnr[1] = 0;
            pLayerBsInfo->rPsnr[2] = 0;

            iCountNal = pLayerBsInfo->iNalCount;
            ++iLayerNum;
            ++pLayerBsInfo;
            ++pCtx->pOut->iLayerBsIndex;
            pLayerBsInfo->pBsBuf = pCtx->pFrameBs + pCtx->iPosBsBuffer;
            pLayerBsInfo->pNalLengthInByte = (pLayerBsInfo - 1)->pNalLengthInByte + iCountNal;

            if (pSvcParam->iPaddingFlag != 0 && pCtx->pWelsSvcRc[pCtx->uiDependencyId].iPaddingSize > 0)
            {
                int iPaddingNalSize = 0;
                pCtx->iEncoderError = WritePadding(pCtx, pCtx->pWelsSvcRc[pCtx->uiDependencyId].iPaddingSize, ref iPaddingNalSize);
                if (pCtx->iEncoderError != ENC_RETURN_SUCCESS)
                {
                    return pCtx->iEncoderError;
                }
                if (iPaddingNalSize <= 0)
                {
                    return ENC_RETURN_UNEXPECTED;
                }

                pCtx->pWelsSvcRc[pCtx->uiDependencyId].iPaddingBitrateStat += pCtx->pWelsSvcRc[pCtx->uiDependencyId].iPaddingSize;
                pCtx->pWelsSvcRc[pCtx->uiDependencyId].iPaddingSize = 0;

                pLayerBsInfo->uiSpatialId = 0;
                pLayerBsInfo->uiTemporalId = 0;
                pLayerBsInfo->uiQualityId = 0;
                pLayerBsInfo->uiLayerType = (byte)LAYER_TYPE.NON_VIDEO_CODING_LAYER;
                pLayerBsInfo->iNalCount = 1;
                pLayerBsInfo->pNalLengthInByte[0] = iPaddingNalSize;
                pLayerBsInfo->eFrameType = eFrameType;
                pLayerBsInfo->iSubSeqId = GetSubSequenceId(pCtx, eFrameType);
                ++pLayerBsInfo;
                ++pCtx->pOut->iLayerBsIndex;
                pLayerBsInfo->pBsBuf = pCtx->pFrameBs + pCtx->iPosBsBuffer;
                pLayerBsInfo->pNalLengthInByte = (pLayerBsInfo - 1)->pNalLengthInByte + 1;
                ++iLayerNum;

                iFrameSize += iPaddingNalSize;
            }
            // CalcSliceComplexRatio: threads only.

            pCtx->eLastNalPriority[iCurDid] = eNalRefIdc;
            ++iSpatialIdx;

            // WelsSwapDqLayers: several spatial layers only.

            if (pCtx->pVpp->UpdateSpatialPictures(pCtx, pSvcParam, (sbyte)iCurTid, iCurDid) != 0)
            {
                ForceCodingIDR(pCtx);
                // Logic Error Found in Preprocess updating. ForceCodingIDR!
                pFbi->eFrameType = eFrameType;
                pLayerBsInfo->eFrameType = eFrameType;
                return ENC_RETURN_CORRECTED;
            }

            if (pSvcParam->bEnableLongTermReference && ((pCtx->pLtr[pCtx->uiDependencyId].bLTRMarkingFlag
                && (pCtx->pLtr[pCtx->uiDependencyId].iLTRMarkMode == LTR_DIRECT_MARK)) || eFrameType == EVideoFrameType.videoFrameTypeIDR))
            {
                pCtx->bRefOfCurTidIsLtr[iCurDid * MAX_TEMPORAL_LEVEL + iCurTid] = true;
            }
        } //end of (iSpatialIdx/iSpatialNum)

        _fsnr = fsnr;

        for (int i = 0; i < pSvcParam->iSpatialLayerNum; i++)
        {
            SSpatialLayerInternal* p = &pSvcParam->sDependencyLayers[i];
            p->iCodingIndex++;
        }

        if (ENC_RETURN_CORRECTED == pCtx->iEncoderError)
        {
            pCtx->pVpp->UpdateSpatialPictures(pCtx, pSvcParam, (sbyte)iCurTid, pCtx->sSpatialIndexMap[iSpatialIdx].iDid);
            ForceCodingIDR(pCtx);
            // Logic Error Found in temporal level. ForceCodingIDR!
            pFbi->eFrameType = eFrameType;
            pLayerBsInfo->eFrameType = eFrameType;
            return ENC_RETURN_CORRECTED;
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
        // The _DEBUG checks are not in the release build.
        return ENC_RETURN_SUCCESS;
    }

    // ---- release: WelsUninitEncoderExt and FreeMemorySvc, for what this driver allocates ----

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

        // WelsUninitEncoderExt: the preprocessing, then FreeMemorySvc.
        if (pCtx->pVpp != null)
        {
            pCtx->pVpp->FreeSpatialPictures(pCtx);
            CWelsPreProcess.DeletePreProcess(ref pCtx->pVpp);
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
        if (pCtx->pReferenceStrategy != null)
        {
            IWelsReferenceStrategy.DeleteReferenceStrategy(ref pCtx->pReferenceStrategy);
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
        if (pCtx->pVaa != null)
        {
            SVAAFrameInfo* pVaa = pCtx->pVaa;
            if (pParam->bEnableAdaptiveQuant) //free mem
            {
                pMa->WelsFree(pVaa->sAdaptiveQuantParam.pMotionTextureUnit, "pVaa->sAdaptiveQuantParam.pMotionTextureUnit");
                pMa->WelsFree(pVaa->sAdaptiveQuantParam.pMotionTextureIndexToDeltaQp,
                              "pVaa->sAdaptiveQuantParam.pMotionTextureIndexToDeltaQp");
            }
            pMa->WelsFree(pVaa->pVaaBackgroundMbFlag, "pVaa->pVaaBackgroundMbFlag");
            pMa->WelsFree(pVaa->sVaaCalcInfo.pSad8x8, "pVaa->sVaaCalcInfo.sad8x8");
            pMa->WelsFree(pVaa->sVaaCalcInfo.pSsd16x16, "pVaa->sVaaCalcInfo.pSsd16x16");
            pMa->WelsFree(pVaa->sVaaCalcInfo.pSum16x16, "pVaa->sVaaCalcInfo.pSum16x16");
            pMa->WelsFree(pVaa->sVaaCalcInfo.pSumOfSquare16x16, "pVaa->sVaaCalcInfo.pSumOfSquare16x16");
            if (pParam->bEnableBackgroundDetection) //BGD control
            {
                pMa->WelsFree(pVaa->sVaaCalcInfo.pSumOfDiff8x8, "pVaa->sVaaCalcInfo.pSumOfDiff8x8");
                pMa->WelsFree(pVaa->sVaaCalcInfo.pMad8x8, "pVaa->sVaaCalcInfo.pMad8x8");
            }
            pMa->WelsFree(pVaa, "pVaa");
            pCtx->pVaa = null;
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
        if (pCtx->pLtr != null)
        {
            pMa->WelsFree(pCtx->pLtr, "SLTRState");
            pCtx->pLtr = null;
        }
        if (pCtx->pWelsSvcRc != null)
        {
            WelsRcFreeMemory(pCtx);
            pMa->WelsFree(pCtx->pWelsSvcRc, "pWelsSvcRc");
            pCtx->pWelsSvcRc = null;
        }
        if (pCtx->pMvdCostTable != null)
        {
            pMa->WelsFree(pCtx->pMvdCostTable, "pMvdCostTable");
            pCtx->pMvdCostTable = null;
        }
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
