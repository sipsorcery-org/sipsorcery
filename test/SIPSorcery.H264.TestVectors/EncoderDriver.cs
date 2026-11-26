//-----------------------------------------------------------------------------
// Filename: EncoderDriver.cs
//
// Description: The encoder's init, frame loop and release for one spatial
// layer, over the ported functions, for the frame by frame acceptance tests:
// IdrTests (E4b, the first frame of each configuration) and StreamTests (E5b,
// P streams).
//
// The encoder API and its init and frame loops come with E8 and the
// preprocessing (CWelsPreProcess) with E7, so EncoderDriver drives the ported
// functions the way CWelsH264SVCEncoder::InitializeExt and EncodeFrame,
// WelsInitEncoderExt, RequestMemorySvc, WelsEncoderEncodeExt and
// FreeMemorySvc do, with a hand port of the camera half of CWelsPreProcess
// for one spatial layer (AllocSpatialPictures, BuildSpatialPicList,
// AnalyzeSpatialPic over the ported VAA statistics, UpdateSpatialPictures).
// Configurations outside that scope (CABAC, screen content, several spatial
// layers, SM_SIZELIMITED_SLICE, threads, rate control, the denoiser, scaling)
// throw NotSupportedException at init, and the analysis features when they
// would run: scene change detection on a T0 P frame, background detection
// and adaptive quantisation on a P frame.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 26 Nov 2026  Claude          Created (E5b) from IdrTests.cs's IdrEncoder (E4b), with the
//                              reference strategy, the LTR state, the VAA, the MVD cost
//                              table, the spatial pictures and the frame loop for P frames.
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

    // g_kuiRefTemporalIdx[MAX_TEMPORAL_LEVEL][MAX_GOP_SIZE] (wels_preprocess.cpp): the spatial
    // picture each coding index of a GOP refers to, by decomposition stages.
    private static readonly byte[][] g_kuiRefTemporalIdx =
    {
        new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 },
        new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 },
        new byte[] { 0, 0, 0, 1, 0, 0, 0, 0 },
        new byte[] { 0, 0, 0, 2, 0, 1, 1, 2 },
    };

    private sWelsEncCtx* _ctx;
    private CMemoryAlign* _pMa;
    private SFrameBSInfo* _info;
    private int _iPicWidth;
    private int _iPicHeight;
    private bool _uninitialised;    // EncodeFrameInternal's WelsUninitEncoderExt after a memory error
    private SPicture* _fsnr;        // the last frame's reconstruction (WelsEncoderEncodeExt's fsnr)
    private bool _fsnrDeblocked;

    // CWelsPreProcess (CWelsPreProcessVideo) for spatial layer 0: m_pSpatialPic[0],
    // m_uiSpatialPicNum[0], m_uiSpatialLayersInTemporal[0], m_pLastSpatialPicture[0][0..1],
    // m_bInitDone and the VAA strategy of m_pInterfaceVp.
    private readonly SPicture*[] _spatialPic = new SPicture*[MAX_REF_PIC_COUNT + 1];
    private int _spatialPicNum;
    private int _spatialLayersInTemporal;
    private SPicture* _lastSpatialPic0;
    private SPicture* _lastSpatialPic1;
    private bool _preprocessInitDone;
    private CVAACalculation _vaaCalculation;

    public EncoderDriver()
    {
        _info = (SFrameBSInfo*)NativeMemory.AllocZeroed((nuint)sizeof(SFrameBSInfo));
    }

    public int PicWidth => _iPicWidth;
    public int PicHeight => _iPicHeight;

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

        // What the port codes by E5b.
        Require(pCodingParam->iUsageType == EUsageType.CAMERA_VIDEO_REAL_TIME, "screen content (Phase 4)");
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
        InitFunctionPointers(pCtx, pCtx->pSvcParam, uiCpuFeatureFlags);

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

        // CWelsPreProcess::CreatePreProcess (CWelsPreProcessVideo; its VP interface's VAA
        // strategy, which WelsPreprocessCreate makes on the first frame upstream) and
        // AllocSpatialPictures.
        _vaaCalculation = new CVAACalculation(0);
        if (AllocSpatialPictures() != 0)
        {
            return 1;
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

    // ---- preprocessing: CWelsPreProcess (CWelsPreProcessVideo), spatial layer 0 ----

    /// <summary>
    /// CWelsPreProcess::AllocSpatialPictures: 2 + max (iHighestTemporalId, 1) pictures for the
    /// temporal structure (the current one last) and one per long term reference.
    /// </summary>
    private int AllocSpatialPictures()
    {
        SWelsSvcCodingParam* pParam = _ctx->pSvcParam;
        int kiPicWidth = pParam->sSpatialLayers[0].iVideoWidth;
        int kiPicHeight = pParam->sSpatialLayers[0].iVideoHeight;
        byte kuiLayerInTemporal = (byte)(2 + Math.Max((int)pParam->sDependencyLayers[0].iHighestTemporalId, 1));
        byte kuiRefNumInTemporal = (byte)(kuiLayerInTemporal + pParam->iLTRRefNum);

        _spatialPicNum = kuiRefNumInTemporal;
        for (int i = 0; i < kuiRefNumInTemporal; i++)
        {
            SPicture* pPic = AllocPicture(_pMa, kiPicWidth, kiPicHeight, false, 0);
            if (pPic == null)
            {
                return 1;
            }
            _spatialPic[i] = pPic;
        }
        _spatialLayersInTemporal = kuiLayerInTemporal;    // camera content
        return 0;
    }

    /// <summary>CWelsPreProcess::FreeSpatialPictures.</summary>
    private void FreeSpatialPictures()
    {
        for (int i = 0; i < _spatialPicNum; i++)
        {
            if (_spatialPic[i] != null)
            {
                SPicture* p = _spatialPic[i];
                FreePicture(_pMa, &p);
                _spatialPic[i] = null;
            }
        }
        _spatialLayersInTemporal = 0;
    }

    /// <summary>CWelsPreProcessVideo::GetCurrentOrigFrame.</summary>
    private SPicture* GetCurrentOrigFrame() => _spatialPic[_spatialLayersInTemporal - 1];

    /// <summary>CWelsPreProcess::InitLastSpatialPictures (camera content).</summary>
    private void InitLastSpatialPictures()
    {
        _lastSpatialPic0 = _spatialPic[_spatialLayersInTemporal - 2];
        _lastSpatialPic1 = null;
    }

    /// <summary>CWelsPreProcess::WelsPreprocessReset, without scaling (WelsInitScaledPic).</summary>
    private int WelsPreprocessReset(int iWidth, int iHeight)
    {
        SWelsSvcCodingParam* pSvcParam = _ctx->pSvcParam;
        //init source width and height
        pSvcParam->SUsedPicRect.iLeft = 0;
        pSvcParam->SUsedPicRect.iTop = 0;
        pSvcParam->SUsedPicRect.iWidth = iWidth;
        pSvcParam->SUsedPicRect.iHeight = iHeight;
        if (iWidth < 16 || iHeight < 16)
        {
            return -1;
        }
        // FreeScaledPic: there is none.
        InitLastSpatialPictures();
        // WelsInitScaledPic: JudgeNeedOfScaling.
        SSpatialLayerInternal* pTop = &pSvcParam->sDependencyLayers[pSvcParam->iSpatialLayerNum - 1];
        Require(pTop->iActualWidth >= iWidth && pTop->iActualHeight >= iHeight, "downsampling the input (E7)");
        return 0;
    }

    /// <summary>CWelsPreProcess::BuildSpatialPicList.</summary>
    private int BuildSpatialPicList(SSourcePicture* kpSrcPic, int* pSpatialNum)
    {
        sWelsEncCtx* pCtx = _ctx;
        SWelsSvcCodingParam* pSvcParam = pCtx->pSvcParam;
        int iWidth = (kpSrcPic->iPicWidth >> 1) << 1;
        int iHeight = (kpSrcPic->iPicHeight >> 1) << 1;
        *pSpatialNum = 0;

        if (!_preprocessInitDone)
        {
            // WelsPreprocessCreate: the VP interface was made at init.
            if (WelsPreprocessReset(iWidth, iHeight) != 0)
            {
                return ENC_RETURN_MEMALLOCERR;
            }
            // m_iAvaliableRefInSpatialPicList: screen content only.
            _preprocessInitDone = true;
        }
        else if (iWidth != pSvcParam->SUsedPicRect.iWidth || iHeight != pSvcParam->SUsedPicRect.iHeight)
        {
            if (WelsPreprocessReset(iWidth, iHeight) != 0)
            {
                return ENC_RETURN_MEMALLOCERR;
            }
        }

        pCtx->pVaa->bSceneChangeFlag = pCtx->pVaa->bIdrPeriodFlag = false;

        return SingleLayerPreprocess(kpSrcPic, pSpatialNum);
    }

    /// <summary>
    /// CWelsPreProcess::SingleLayerPreprocess for one spatial layer without the denoiser or
    /// scaling (rejected at init).
    /// </summary>
    private int SingleLayerPreprocess(SSourcePicture* kpSrc, int* pSpatialNum)
    {
        sWelsEncCtx* pCtx = _ctx;
        SWelsSvcCodingParam* pSvcParam = pCtx->pSvcParam;
        int iDependencyId = pSvcParam->iSpatialLayerNum - 1;
        int iSpatialNum = 0;

        SSpatialLayerInternal* pDlayerParamInternal = &pSvcParam->sDependencyLayers[iDependencyId];
        SSpatialLayerConfig* pDlayerParam = &pSvcParam->sSpatialLayers[iDependencyId];
        int iTargetWidth = pDlayerParam->iVideoWidth;
        int iTargetHeight = pDlayerParam->iVideoHeight;
        int iSrcWidth = pSvcParam->SUsedPicRect.iWidth;
        int iSrcHeight = pSvcParam->SUsedPicRect.iHeight;
        if (pSvcParam->uiIntraPeriod != 0)
        {
            pCtx->pVaa->bIdrPeriodFlag = 1 + pDlayerParamInternal->iFrameIndex >= (int)pSvcParam->uiIntraPeriod;
        }

        *pSpatialNum = 0;
        SPicture* pSrcPic = GetCurrentOrigFrame();   // no pScaledInputPicture
        int iRet = WelsMoveMemoryWrapper(pSvcParam, pSrcPic, kpSrc, iSrcWidth, iSrcHeight);
        if (iRet != ENC_RETURN_SUCCESS)
        {
            return iRet;
        }
        // BilateralDenoising: the denoiser is rejected at init.

        // different scaling in between input picture and dst highest spatial picture.
        int iShrinkWidth = iSrcWidth;
        int iShrinkHeight = iSrcHeight;
        SPicture* pDstPic = pSrcPic;
        DownsamplePadding(pSrcPic, pDstPic, iSrcWidth, iSrcHeight, iShrinkWidth, iShrinkHeight, iTargetWidth, iTargetHeight,
                          false);

        if (pSvcParam->bEnableSceneChangeDetect && !pCtx->pVaa->bIdrPeriodFlag)
        {
            // camera content
            if (!pDlayerParamInternal->bEncCurFrmAsIdrFlag
                && (pDlayerParamInternal->iCodingIndex & (pSvcParam->uiGopSize - 1)) == 0)
            {
                Require(false, "scene change detection (E7)");
            }
        }

        for (int i = 0; i < pSvcParam->iSpatialLayerNum; i++)
        {
            SSpatialLayerInternal* p = &pSvcParam->sDependencyLayers[i];
            if (p->uiCodingIdx2TemporalId[p->iCodingIndex & (pSvcParam->uiGopSize - 1)] != INVALID_TEMPORAL_ID)
            {
                ++iSpatialNum;
            }
        }

        pDlayerParamInternal = &pSvcParam->sDependencyLayers[iDependencyId];
        int iTemporalId = pDlayerParamInternal->uiCodingIdx2TemporalId[pDlayerParamInternal->iCodingIndex & (pSvcParam->uiGopSize - 1)];
        int iActualSpatialNum = iSpatialNum - 1;
        if (iTemporalId != INVALID_TEMPORAL_ID)
        {
            // WelsUpdateSpatialIdxMap
            pCtx->sSpatialIndexMap[iActualSpatialNum].pSrc = pDstPic;
            pCtx->sSpatialIndexMap[iActualSpatialNum].iDid = iDependencyId;
            --iActualSpatialNum;
        }

        _lastSpatialPic1 = GetCurrentOrigFrame();
        // The other spatial layers: one only.

        *pSpatialNum = iSpatialNum;
        return ENC_RETURN_SUCCESS;
    }

    /// <summary>
    /// CWelsPreProcess::DownsamplePadding without the downsampler or the forced copy (several
    /// spatial layers): the source is the destination, padded to the target.
    /// </summary>
    private static void DownsamplePadding(SPicture* pSrc, SPicture* pDstPic, int iSrcWidth, int iSrcHeight,
                                          int iShrinkWidth, int iShrinkHeight, int iTargetWidth, int iTargetHeight,
                                          bool bForceCopy)
    {
        Require(iSrcWidth == iShrinkWidth && iSrcHeight == iShrinkHeight && !bForceCopy, "downsampling (E7)");
        // sDstPicMap = sSrcPixMap
        // get rid of odd line
        iShrinkWidth -= iShrinkWidth & 1;
        iShrinkHeight -= iShrinkHeight & 1;
        Padding(pSrc->pData[0], pSrc->pData[1], pSrc->pData[2], pSrc->iLineSize[0], pSrc->iLineSize[1], iShrinkWidth,
                iTargetWidth, iShrinkHeight, iTargetHeight);
    }

    /// <summary>CWelsPreProcess::AnalyzeSpatialPic (camera content).</summary>
    private int AnalyzeSpatialPic(int kiDidx)
    {
        sWelsEncCtx* pCtx = _ctx;
        SWelsSvcCodingParam* pSvcParam = pCtx->pSvcParam;
        bool bNeededMbAq = pSvcParam->bEnableAdaptiveQuant && pCtx->eSliceType == EWelsSliceType.P_SLICE;
        bool bCalculateBGD = pCtx->eSliceType == EWelsSliceType.P_SLICE && pSvcParam->bEnableBackgroundDetection;
        SSpatialLayerInternal* pParamInternal = &pSvcParam->sDependencyLayers[kiDidx];
        int iCurTemporalIdx = _spatialLayersInTemporal - 1;

        int iRefTemporalIdx = g_kuiRefTemporalIdx[pSvcParam->iDecompStages][pParamInternal->iCodingIndex &
                              (pSvcParam->uiGopSize - 1)];
        if (pCtx->uiTemporalId == 0 && pCtx->pLtr[pCtx->uiDependencyId].bReceivedT0LostFlag)
        {
            iRefTemporalIdx = _spatialLayersInTemporal + pCtx->pVaa->uiValidLongTermPicIdx;
        }

        SPicture* pCurPic = _spatialPic[iCurTemporalIdx];
        bool bCalculateVar = pSvcParam->iRCMode >= RC_MODES.RC_BITRATE_MODE && pCtx->eSliceType == EWelsSliceType.I_SLICE;

        SPicture* pRefPic = _spatialPic[iRefTemporalIdx];   // GetBestRefPic (kiDidx, iRefTemporalIdx)
        SPicture* pLastPic = _lastSpatialPic0;
        bool bCalculateSQDiff = (byte*)pLastPic->pData[0] == (byte*)pRefPic->pData[0] && bNeededMbAq;

        VaaCalculation(pCtx->pVaa, pCurPic, pRefPic, bCalculateSQDiff, bCalculateVar, bCalculateBGD);

        if (pSvcParam->bEnableBackgroundDetection)
        {
            BackgroundDetection(pCtx->pVaa, pCurPic, pRefPic, bCalculateBGD && pRefPic->iPictureType != (int)EWelsSliceType.I_SLICE);
        }

        if (bNeededMbAq)
        {
            Require(false, "adaptive quantisation (E7)");   // AdaptiveQuantCalculation (last[1], last[0])
        }
        return 0;
    }

    /// <summary>
    /// CWelsPreProcess::VaaCalculation, through the VP interface's VAA strategy
    /// (CVpFrameWork::CheckValid passes the encoder's own I420 pictures).
    /// </summary>
    private void VaaCalculation(SVAAFrameInfo* pVaaInfo, SPicture* pCurPicture, SPicture* pRefPicture,
                                bool bCalculateSQDiff, bool bCalculateVar, bool bCalculateBGD)
    {
        pVaaInfo->sVaaCalcInfo.pCurY = pCurPicture->pData[0];
        pVaaInfo->sVaaCalcInfo.pRefY = pRefPicture->pData[0];

        int iMethodIdx = (int)EMethods.METHOD_VAA_STATISTICS;
        SPixMap sCurPixMap = default;
        SPixMap sRefPixMap = default;
        SVAACalcParam calc_param = default;

        sCurPixMap.pPixel[0] = pCurPicture->pData[0];
        sCurPixMap.iSizeInBits = 8;     // g_kiPixMapSizeInBits
        sCurPixMap.sRect.iRectWidth = pCurPicture->iWidthInPixel;
        sCurPixMap.sRect.iRectHeight = pCurPicture->iHeightInPixel;
        sCurPixMap.iStride[0] = pCurPicture->iLineSize[0];
        sCurPixMap.eFormat = EVideoFormat.VIDEO_FORMAT_I420;

        sRefPixMap.pPixel[0] = pRefPicture->pData[0];
        sRefPixMap.iSizeInBits = 8;
        sRefPixMap.sRect.iRectWidth = pRefPicture->iWidthInPixel;
        sRefPixMap.sRect.iRectHeight = pRefPicture->iHeightInPixel;
        sRefPixMap.iStride[0] = pRefPicture->iLineSize[0];
        sRefPixMap.eFormat = EVideoFormat.VIDEO_FORMAT_I420;

        calc_param.iCalcVar = bCalculateVar ? 1 : 0;
        calc_param.iCalcBgd = bCalculateBGD ? 1 : 0;
        calc_param.iCalcSsd = bCalculateSQDiff ? 1 : 0;
        calc_param.pCalcResult = &pVaaInfo->sVaaCalcInfo;

        _vaaCalculation.Set(iMethodIdx, &calc_param);
        _vaaCalculation.Process(iMethodIdx, &sCurPixMap, &sRefPixMap);
    }

    /// <summary>CWelsPreProcess::BackgroundDetection: the detection itself comes with E7.</summary>
    private static void BackgroundDetection(SVAAFrameInfo* pVaaInfo, SPicture* pCurPicture, SPicture* pRefPicture,
                                            bool bDetectFlag)
    {
        Require(!bDetectFlag, "background detection (E7)");
        int iPicWidthInMb = (pCurPicture->iWidthInPixel + 15) >> 4;
        int iPicHeightInMb = (pCurPicture->iHeightInPixel + 15) >> 4;
        new Span<byte>(pVaaInfo->pVaaBackgroundMbFlag, iPicWidthInMb * iPicHeightInMb).Clear();
    }

    /// <summary>CWelsPreProcess::UpdateSpatialPictures (camera content).</summary>
    private int UpdateSpatialPictures(int iCurTid, int kiDidx)
    {
        sWelsEncCtx* pCtx = _ctx;
        SWelsSvcCodingParam* pParam = pCtx->pSvcParam;
        Require(kiDidx == 0, "more than one spatial layer (E7)");

        WelsExchangeSpatialPictures(ref _lastSpatialPic1, ref _lastSpatialPic0);

        int kiCurPos = _spatialLayersInTemporal - 1;     // GetCurPicPosition
        if (iCurTid < kiCurPos || pParam->iDecompStages == 0)
        {
            if (iCurTid >= MAX_TEMPORAL_LEVEL || kiCurPos > MAX_TEMPORAL_LEVEL)
            {
                InitLastSpatialPictures();
                return 1;
            }
            if (pCtx->bRefOfCurTidIsLtr[kiDidx * MAX_TEMPORAL_LEVEL + iCurTid])
            {
                int kiAvailableLtrPos = _spatialLayersInTemporal + pCtx->pVaa->uiMarkLongTermPicIdx;
                WelsExchangeSpatialPictures(ref _spatialPic[kiAvailableLtrPos], ref _spatialPic[iCurTid]);
                pCtx->bRefOfCurTidIsLtr[kiDidx * MAX_TEMPORAL_LEVEL + iCurTid] = false;
            }
            WelsExchangeSpatialPictures(ref _spatialPic[kiCurPos], ref _spatialPic[iCurTid]);
        }
        return 0;
    }

    /// <summary>CWelsPreProcess::WelsExchangeSpatialPictures.</summary>
    private static void WelsExchangeSpatialPictures(ref SPicture* ppPic1, ref SPicture* ppPic2)
    {
        SPicture* tmp = ppPic1;
        ppPic1 = ppPic2;
        ppPic2 = tmp;
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

    // ---- frame: WelsEncoderEncodeExt ----

    /// <summary>GetTimestampForRc.</summary>
    private static long GetTimestampForRc(long uiTimeStamp, long uiLastTimeStamp, float fFrameRate)
    {
        if (uiLastTimeStamp >= uiTimeStamp || (uiTimeStamp == 0 && uiLastTimeStamp != -1))
        {
            return uiLastTimeStamp + (int)(1000.0 / fFrameRate);
        }
        return uiTimeStamp;
    }

    /// <summary>
    /// WelsRcCheckFrameStatus (ratectl.cpp) with rate control off: pfWelsRcPicDelayJudge and
    /// pfWelsCheckSkipBasedMaxbr are null (WelsRcInitFuncPointers), so only bSkipFlag, which
    /// nothing sets, can skip the frame.
    /// </summary>
    private bool WelsRcCheckFrameStatus(long uiTimeStamp, int iSpatialNum)
    {
        sWelsEncCtx* pCtx = _ctx;
        Require(pCtx->pFuncList->pfRc.pfWelsRcPicDelayJudge == null && pCtx->pFuncList->pfRc.pfWelsCheckSkipBasedMaxbr == null,
                "rate control (E6)");
        bool bSkipMustFlag = false;
        for (int i = 0; i < iSpatialNum; i++)
        {
            int iDidIdx = pCtx->sSpatialIndexMap[i].iDid;
            if (pCtx->pWelsSvcRc[iDidIdx].bSkipFlag)
            {
                bSkipMustFlag = true;
                break;
            }
        }
        if (bSkipMustFlag)
        {
            for (int i = 0; i < iSpatialNum; i++)
            {
                int iDidIdx = pCtx->sSpatialIndexMap[i].iDid;
                pCtx->pWelsSvcRc[iDidIdx].uiLastTimeStamp = uiTimeStamp;
                pCtx->pWelsSvcRc[iDidIdx].bSkipFlag = false;
                pCtx->pWelsSvcRc[iDidIdx].iContinualSkipFrames++;
            }
            return true;
        }
        return false;
    }

    /// <summary>
    /// PrepareEncodeFrame (not simulcast): the frame type, the temporal level and, for an IDR,
    /// the parameter sets. pfWelsUpdateBufferWhenSkip is null with rate control off.
    /// </summary>
    private EVideoFrameType PrepareEncodeFrame(ref SLayerBSInfo* pLayerBsInfo, int iSpatialNum, ref sbyte iCurDid,
                                               ref int iCurTid, ref int iLayerNum, ref int iFrameSize, long uiTimeStamp)
    {
        sWelsEncCtx* pCtx = _ctx;
        SWelsSvcCodingParam* pSvcParam = pCtx->pSvcParam;

        bool bSkipFrameFlag = WelsRcCheckFrameStatus(uiTimeStamp, iSpatialNum);
        EVideoFrameType eFrameType = DecideFrameType(pCtx, (sbyte)iSpatialNum, iCurDid, bSkipFrameFlag);
        if (eFrameType != EVideoFrameType.videoFrameTypeSkip)
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
        int iRet = BuildSpatialPicList(pSrcPic, &iSpatialNum);
        if (iRet != ENC_RETURN_SUCCESS)
        {
            return iRet;
        }

        // pfWelsUpdateMaxBrWindowStatus: null with rate control off.
        Require(pCtx->pFuncList->pfRc.pfWelsUpdateMaxBrWindowStatus == null, "rate control (E6)");

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
            AnalyzeSpatialPic(iCurDid);

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
            // AnalyzePictureComplexity: rate control only.
            WelsUpdateRefSyntax(pCtx, pParamInternal->iPOC, (int)eFrameType); //get reordering syntax used for writing slice header and transmit to encoder.
            PrefetchReferencePicture(pCtx, eFrameType); // update reference picture for current pDq layer
            pCtx->pFuncList->pfRc.pfWelsRcPictureInit(pCtx, pFbi->uiTimeStamp);
            PreprocessSliceCoding(pCtx); // MUST be called after pfWelsRcPictureInit() and WelsInitCurrentLayer()

            iLayerSize = 0;
            if (SliceModeEnum.SM_SINGLE_SLICE == pParam->sSliceArgument.uiSliceMode) // only one slice within a sQualityStat layer
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
                iSliceCount = GetCurrentSliceNum(pCtx->pCurDqLayer);
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
            Require(pCtx->pFuncList->pfRc.pfWelsRcPostFrameSkipping == null, "rate control (E6)");

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
            // RcTraceFrameBits: a log.
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

            // WritePadding: rate control only (iPaddingSize stays 0). CalcSliceComplexRatio: threads only.

            pCtx->eLastNalPriority[iCurDid] = eNalRefIdc;
            ++iSpatialIdx;

            // WelsSwapDqLayers: several spatial layers only.

            if (UpdateSpatialPictures(iCurTid, iCurDid) != 0)
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
            UpdateSpatialPictures(iCurTid, pCtx->sSpatialIndexMap[iSpatialIdx].iDid);
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

        // WelsUninitEncoderExt: the preprocessing's spatial pictures, then FreeMemorySvc.
        FreeSpatialPictures();
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
        pMa->WelsFree(pCtx->pWelsSvcRc, "pWelsSvcRc");
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
