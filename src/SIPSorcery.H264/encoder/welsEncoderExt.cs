//-----------------------------------------------------------------------------
// Filename: welsEncoderExt.cs
//
// Description: Port of codec/encoder/plus/inc/welsEncoderExt.h and
// plus/src/welsEncoderExt.cpp: CWelsH264SVCEncoder, the ISVCEncoder
// implementation behind the public encoder API (GetDefaultParams, Initialize,
// InitializeExt, EncodeFrame, EncodeParameterSets, ForceIntraFrame,
// SetOption/GetOption, Uninitialize), plus WelsCreateSVCEncoder and
// WelsDestroySVCEncoder.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// CWelsH264SVCEncoder is a plain struct in native memory, like CWelsDecoder:
// the trace holds its address as the codec instance, so it must not move.
// New/Delete stand for upstream's new/delete and run the constructor and
// destructor; the methods are called through the CWelsH264SVCEncoder*.
//
// What the port does not code yet (screen content, CABAC, several spatial
// layers or simulcast, slice threads, SM_SIZELIMITED_SLICE) is refused by
// WelsInitEncoderExt with an error log, so Initialize, InitializeExt and the
// SetOption calls that reinitialise return cmInitParaError for it.
//
// Not ported, by design: OUTPUT_BIT_STREAM, REC_FRAME_COUNT and
// DUMP_SRC_PICTURE, which upstream builds with off.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 27 Nov 2026  Claude          Created (E8).
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
// The ported code is also covered by the OpenH264 BSD 2-Clause notice below.
//-----------------------------------------------------------------------------

/*!
 * \copy
 *     Copyright (c)  2013, Cisco Systems
 *     All rights reserved.
 *
 *     Redistribution and use in source and binary forms, with or without
 *     modification, are permitted provided that the following conditions
 *     are met:
 *
 *        * Redistributions of source code must retain the above copyright
 *          notice, this list of conditions and the following disclaimer.
 *
 *        * Redistributions in binary form must reproduce the above copyright
 *          notice, this list of conditions and the following disclaimer in
 *          the documentation and/or other materials provided with the
 *          distribution.
 *
 *     THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
 *     "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
 *     LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS
 *     FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE
 *     COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT,
 *     INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING,
 *     BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 *     LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
 *     CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT
 *     LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN
 *     ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE
 *     POSSIBILITY OF SUCH DAMAGE.
 *
 *
 * \file    welsEncoderExt.cpp
 *
 * \brief   core encoder for SVC
 *
 * \date    5/14/2009 Created
 *
 *************************************************************************************
 */

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static SIPSorcery.H264.Encoder.WelsEnc;

namespace SIPSorcery.H264.Encoder;

/// <summary>CWelsH264SVCEncoder (see the file header).</summary>
internal unsafe struct CWelsH264SVCEncoder
{
    /// <summary>version.h's VERSION_NUMBER when GENERATED_VERSION_HEADER is not defined, as upstream builds it.</summary>
    private const string VERSION_NUMBER = "openh264 default: 1.4";

    private sWelsEncCtx* m_pEncContext;

    private welsCodecTrace* m_pWelsTrace;
    private int32_t m_iMaxPicWidth;
    private int32_t m_iMaxPicHeight;

    private int32_t m_iCspInternal;
    private bool m_bInitialFlag;

    /// <summary>this, for the address upstream hands the trace. Valid because the object lives in native memory.</summary>
    private CWelsH264SVCEncoder* This => (CWelsH264SVCEncoder*)Unsafe.AsPointer(ref this);

    /// <summary>
    /// Port addition for SIPSorcery: the encoder context, null before Initialize and after a
    /// memory error. H264Codec reads the SPS it writes and the tests read the reconstruction
    /// (sWelsEncCtx.pLastFsnr) through it.
    /// </summary>
    internal sWelsEncCtx* EncContext => m_pEncContext;

    /// <summary>new CWelsH264SVCEncoder(): allocates the object in native memory and runs the constructor.</summary>
    public static CWelsH264SVCEncoder* New()
    {
        CWelsH264SVCEncoder* p = (CWelsH264SVCEncoder*)NativeMemory.AllocZeroed((nuint)sizeof(CWelsH264SVCEncoder));
        p->Construct();
        return p;
    }

    /// <summary>delete pEncoder: runs the destructor and frees the object.</summary>
    public static void Delete(ref CWelsH264SVCEncoder* p)
    {
        if (p != null)
        {
            p->Destruct();
            NativeMemory.Free(p);
            p = null;
        }
    }

    /*
     *  CWelsH264SVCEncoder class implementation
     */
    private void Construct()
    {
        m_pEncContext = null;
        m_pWelsTrace = null;
        m_iMaxPicWidth = 0;
        m_iMaxPicHeight = 0;
        m_iCspInternal = 0;
        m_bInitialFlag = false;

        InitEncoder();
    }

    private void Destruct()
    {
        if (m_pWelsTrace != null)
        {
            WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_INFO, "CWelsH264SVCEncoder::~CWelsH264SVCEncoder()");
        }

        Uninitialize();

        if (m_pWelsTrace != null)
        {
            welsCodecTrace.Delete(ref m_pWelsTrace);
        }
    }

    private void InitEncoder()
    {
        m_pWelsTrace = welsCodecTrace.New();
        if (m_pWelsTrace == null)
        {
            return;
        }
        m_pWelsTrace->SetCodecInstance(This);
    }

    /* Interfaces override from ISVCEncoder */

    public int GetDefaultParams(SEncParamExt* argv)
    {
        SWelsSvcCodingParam.FillDefault(ref *argv);
        return (int)cmResultSuccess;
    }

    /*
     *  SVC Encoder Initialization
     */
    public int Initialize(SEncParamBase* argv)
    {
        if (m_pWelsTrace == null)
        {
            return (int)cmMallocMemeError;
        }

        WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_INFO, $"CWelsH264SVCEncoder::InitEncoder(), openh264 codec version = {VERSION_NUMBER}");

        if (null == argv)
        {
            WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_ERROR, $"CWelsH264SVCEncoder::Initialize(), invalid argv= 0x{(nint)argv:X}");
            return (int)cmInitParaError;
        }

        SWelsSvcCodingParam sConfig = default;
        sConfig.FillDefault(); // the constructor
        // Convert SEncParamBase into WelsSVCParamConfig here..
        if (sConfig.ParamBaseTranscode(*argv) != 0)
        {
            WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_ERROR,
                    "CWelsH264SVCEncoder::Initialize(), parameter_translation failed.");
            TraceParamInfo((SEncParamExt*)&sConfig);
            Uninitialize();
            return (int)cmInitParaError;
        }

        return InitializeInternal(&sConfig);
    }

    public int InitializeExt(SEncParamExt* argv)
    {
        if (m_pWelsTrace == null)
        {
            return (int)cmMallocMemeError;
        }

        WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_INFO, $"CWelsH264SVCEncoder::InitEncoder(), openh264 codec version = {VERSION_NUMBER}");

        if (null == argv)
        {
            WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_ERROR, $"CWelsH264SVCEncoder::InitializeExt(), invalid argv= 0x{(nint)argv:X}");
            return (int)cmInitParaError;
        }

        SWelsSvcCodingParam sConfig = default;
        sConfig.FillDefault(); // the constructor
        // Convert SEncParamExt into WelsSVCParamConfig here..
        if (sConfig.ParamTranscode(*argv) != 0)
        {
            WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_ERROR,
                    "CWelsH264SVCEncoder::InitializeExt(), parameter_translation failed.");
            TraceParamInfo((SEncParamExt*)&sConfig);
            Uninitialize();
            return (int)cmInitParaError;
        }

        return InitializeInternal(&sConfig);
    }

    private int InitializeInternal(SWelsSvcCodingParam* pCfg)
    {
        if (null == pCfg)
        {
            WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_ERROR, $"CWelsH264SVCEncoder::Initialize(), invalid argv= 0x{(nint)pCfg:X}.");
            return (int)cmInitParaError;
        }

        if (m_bInitialFlag)
        {
            WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_WARNING,
                    $"CWelsH264SVCEncoder::Initialize(), reinitialize, m_bInitialFlag= {(m_bInitialFlag ? 1 : 0)}.");
            Uninitialize();
        }
        // Check valid parameters
        int32_t iNumOfLayers = pCfg->iSpatialLayerNum;
        if (iNumOfLayers < 1 || iNumOfLayers > MAX_DEPENDENCY_LAYER)
        {
            WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_ERROR,
                    $"CWelsH264SVCEncoder::Initialize(), invalid iSpatialLayerNum= {iNumOfLayers}, valid at range of [1, {MAX_DEPENDENCY_LAYER}].");
            Uninitialize();
            return (int)cmInitParaError;
        }
        if (pCfg->iTemporalLayerNum < 1)
            pCfg->iTemporalLayerNum = 1;
        if (pCfg->iTemporalLayerNum > MAX_TEMPORAL_LEVEL)
        {
            WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_ERROR,
                    $"CWelsH264SVCEncoder::Initialize(), invalid iTemporalLayerNum= {pCfg->iTemporalLayerNum}, valid at range of [1, {MAX_TEMPORAL_LEVEL}].");
            Uninitialize();
            return (int)cmInitParaError;
        }

        // assert( cfg.uiGopSize >= 1 && ( cfg.uiIntraPeriod && (cfg.uiIntraPeriod % cfg.uiGopSize) == 0) );

        if (pCfg->uiGopSize < 1 || pCfg->uiGopSize > MAX_GOP_SIZE)
        {
            WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_ERROR,
                    $"CWelsH264SVCEncoder::Initialize(), invalid uiGopSize= {pCfg->uiGopSize}, valid at range of [1, {MAX_GOP_SIZE}].");
            Uninitialize();
            return (int)cmInitParaError;
        }

        if (!WELS_POWER2_IF(pCfg->uiGopSize))
        {
            WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_ERROR,
                    $"CWelsH264SVCEncoder::Initialize(), invalid uiGopSize= {pCfg->uiGopSize}, valid at range of [1, {MAX_GOP_SIZE}] and yield to power of 2.");
            Uninitialize();
            return (int)cmInitParaError;
        }

        if (pCfg->uiIntraPeriod != 0 && pCfg->uiIntraPeriod < pCfg->uiGopSize)
        {
            WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_ERROR,
                    $"CWelsH264SVCEncoder::Initialize(), invalid uiIntraPeriod= {pCfg->uiIntraPeriod}, valid in case it equals to 0 for unlimited intra period or exceeds specified uiGopSize= {pCfg->uiGopSize}.");
            Uninitialize();
            return (int)cmInitParaError;
        }

        if ((pCfg->uiIntraPeriod != 0 && (pCfg->uiIntraPeriod & (pCfg->uiGopSize - 1)) != 0))
        {
            WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_ERROR,
                    $"CWelsH264SVCEncoder::Initialize(), invalid uiIntraPeriod= {pCfg->uiIntraPeriod}, valid in case it equals to 0 for unlimited intra period or exceeds specified uiGopSize= {pCfg->uiGopSize} also multiple of it.");
            Uninitialize();
            return (int)cmInitParaError;
        }
        if (pCfg->iUsageType == SCREEN_CONTENT_REAL_TIME)
        {
            if (pCfg->bEnableLongTermReference)
            {
                pCfg->iLTRRefNum = LONG_TERM_REF_NUM_SCREEN;
                if (pCfg->iNumRefFrame == AUTO_REF_PIC_COUNT)
                    pCfg->iNumRefFrame = Math.Max(1, WELS_LOG2(pCfg->uiGopSize)) + pCfg->iLTRRefNum;
            }
            else
            {
                pCfg->iLTRRefNum = 0;
                if (pCfg->iNumRefFrame == AUTO_REF_PIC_COUNT)
                    pCfg->iNumRefFrame = (int32_t)Math.Max(1u, pCfg->uiGopSize >> 1);
            }
        }
        else
        {
            pCfg->iLTRRefNum = pCfg->bEnableLongTermReference ? LONG_TERM_REF_NUM : 0;
            if (pCfg->iNumRefFrame == AUTO_REF_PIC_COUNT)
            {
                pCfg->iNumRefFrame = ((pCfg->uiGopSize >> 1) > 1) ? ((int32_t)(pCfg->uiGopSize >> 1) + pCfg->iLTRRefNum) :
                                     (MIN_REF_PIC_COUNT + pCfg->iLTRRefNum);
                pCfg->iNumRefFrame = WELS_CLIP3(pCfg->iNumRefFrame, MIN_REF_PIC_COUNT, MAX_REFERENCE_PICTURE_COUNT_NUM_CAMERA);
            }
        }

        if (pCfg->iLtrMarkPeriod == 0)
        {
            pCfg->iLtrMarkPeriod = 30;
        }

        int32_t kiDecStages = WELS_LOG2(pCfg->uiGopSize);
        pCfg->iTemporalLayerNum = (int8_t)(1 + kiDecStages);
        pCfg->iLoopFilterAlphaC0Offset = WELS_CLIP3(pCfg->iLoopFilterAlphaC0Offset, -6, 6);
        pCfg->iLoopFilterBetaOffset = WELS_CLIP3(pCfg->iLoopFilterBetaOffset, -6, 6);

        // decide property list size between INIT_TYPE_PARAMETER_BASED/INIT_TYPE_CONFIG_BASED
        m_iMaxPicWidth = pCfg->iPicWidth;
        m_iMaxPicHeight = pCfg->iPicHeight;

        TraceParamInfo((SEncParamExt*)pCfg);
        sWelsEncCtx* pEncContext = m_pEncContext;
        int32_t iInitRet = WelsInitEncoderExt(&pEncContext, pCfg, &m_pWelsTrace->m_sLogCtx, null);
        m_pEncContext = pEncContext;
        if (iInitRet != 0)
        {
            WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_ERROR, "CWelsH264SVCEncoder::Initialize(), WelsInitEncoderExt failed.");
            WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_DEBUG,
                    $"Problematic Input Base Param: iUsageType={(int)pCfg->iUsageType}, Resolution={pCfg->iPicWidth}x{pCfg->iPicHeight}, FR={pCfg->fMaxFrameRate:F6}, TLayerNum={pCfg->iTemporalLayerNum}, DLayerNum={pCfg->iSpatialLayerNum}");
            Uninitialize();
            return (int)cmInitParaError;
        }

        m_bInitialFlag = true;

        return (int)cmResultSuccess;
    }

    /*
     *  SVC Encoder Uninitialization
     */
    public int32_t Uninitialize()
    {
        if (!m_bInitialFlag)
        {
            return 0;
        }

        WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_INFO, $"CWelsH264SVCEncoder::Uninitialize(), openh264 codec version = {VERSION_NUMBER}.");

        if (null != m_pEncContext)
        {
            sWelsEncCtx* pEncContext = m_pEncContext;
            WelsUninitEncoderExt(&pEncContext);
            m_pEncContext = null;
        }

        m_bInitialFlag = false;

        return 0;
    }

    /*
     *  SVC core encoding
     */
    public int EncodeFrame(SSourcePicture* kpSrcPic, SFrameBSInfo* pBsInfo)
    {
        if (!(kpSrcPic != null && m_bInitialFlag && pBsInfo != null))
        {
            WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_ERROR, "CWelsH264SVCEncoder::EncodeFrame(), cmInitParaError.");
            return (int)cmInitParaError;
        }
        if (kpSrcPic->iColorFormat != (int)videoFormatI420)
        {
            WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_ERROR, $"CWelsH264SVCEncoder::EncodeFrame(), wrong iColorFormat {kpSrcPic->iColorFormat}");
            return (int)cmInitParaError;
        }

        int32_t kiEncoderReturn = EncodeFrameInternal(kpSrcPic, pBsInfo);

        if (kiEncoderReturn != (int)cmResultSuccess)
        {
            WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_ERROR, $"CWelsH264SVCEncoder::EncodeFrame(), kiEncoderReturn {kiEncoderReturn}");
            return kiEncoderReturn;
        }

        return kiEncoderReturn;
    }

    private int EncodeFrameInternal(SSourcePicture* pSrcPic, SFrameBSInfo* pBsInfo)
    {
        if ((pSrcPic->iPicWidth < 16) || ((pSrcPic->iPicHeight < 16)))
        {
            WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_ERROR,
                    $"Don't support width({pSrcPic->iPicWidth}) or height({pSrcPic->iPicHeight}) which is less than 16!");
            return (int)cmUnsupportedData;
        }

        int64_t kiBeforeFrameUs = WelsTime();
        int32_t kiEncoderReturn = WelsEncoderEncodeExt(m_pEncContext, pBsInfo, pSrcPic);
        int64_t kiCurrentFrameMs = (WelsTime() - kiBeforeFrameUs) / 1000;
        if ((kiEncoderReturn == ENC_RETURN_MEMALLOCERR) || (kiEncoderReturn == ENC_RETURN_MEMOVERFLOWFOUND)
            || (kiEncoderReturn == ENC_RETURN_VLCOVERFLOWFOUND))
        {
            WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_DEBUG, $"CWelsH264SVCEncoder::EncodeFrame() not succeed, err={kiEncoderReturn}");
            sWelsEncCtx* pEncContext = m_pEncContext;
            WelsUninitEncoderExt(&pEncContext);
            m_pEncContext = pEncContext;
            return (int)cmMallocMemeError;
        }
        else if (kiEncoderReturn == ENC_RETURN_INVALIDINPUT)
        {
            WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_ERROR, $"CWelsH264SVCEncoder::EncodeFrame() invalid input, err={kiEncoderReturn}");
            return (int)cmUnsupportedData;
        }
        else if ((kiEncoderReturn != ENC_RETURN_SUCCESS) && (kiEncoderReturn == ENC_RETURN_CORRECTED))
        {
            WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_ERROR, $"unexpected return({kiEncoderReturn}) from EncodeFrameInternal()!");
            return (int)cmUnknownReason;
        }

        UpdateStatistics(pBsInfo, kiCurrentFrameMs);

        return (int)cmResultSuccess;
    }

    public int EncodeParameterSets(SFrameBSInfo* pBsInfo)
    {
        return WelsEncoderEncodeParameterSets(m_pEncContext, pBsInfo);
    }

    /*
     *  Force key frame
     */
    public int ForceIntraFrame(bool bIDR, int iLayerId = -1)
    {
        if (bIDR)
        {
            if (!(m_pEncContext != null && m_bInitialFlag))
            {
                return 1;
            }

            ForceCodingIDR(m_pEncContext, iLayerId);
        }
        else
        {
            WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_INFO,
                    "CWelsH264SVCEncoder::ForceIntraFrame(),nothing to do as bIDR set to false");
        }

        return 0;
    }

    private void TraceParamInfo(SEncParamExt* pParam)
    {
        WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_INFO,
                $"iUsageType = {(int)pParam->iUsageType},iPicWidth= {pParam->iPicWidth};iPicHeight= {pParam->iPicHeight};iTargetBitrate= {pParam->iTargetBitrate};iMaxBitrate= {pParam->iMaxBitrate};iRCMode= {(int)pParam->iRCMode};iPaddingFlag= {pParam->iPaddingFlag};iTemporalLayerNum= {pParam->iTemporalLayerNum};iSpatialLayerNum= {pParam->iSpatialLayerNum};fFrameRate= {pParam->fMaxFrameRate:F6}f;uiIntraPeriod= {pParam->uiIntraPeriod};"
                + $"eSpsPpsIdStrategy = {(int)pParam->eSpsPpsIdStrategy};bPrefixNalAddingCtrl = {B(pParam->bPrefixNalAddingCtrl)};bSimulcastAVC={B(pParam->bSimulcastAVC)};bEnableDenoise= {B(pParam->bEnableDenoise)};bEnableBackgroundDetection= {B(pParam->bEnableBackgroundDetection)};bEnableSceneChangeDetect = {B(pParam->bEnableSceneChangeDetect)};bEnableAdaptiveQuant= {B(pParam->bEnableAdaptiveQuant)};bEnableFrameSkip= {B(pParam->bEnableFrameSkip)};bEnableLongTermReference= {B(pParam->bEnableLongTermReference)};iLtrMarkPeriod= {pParam->iLtrMarkPeriod}, bIsLosslessLink={B(pParam->bIsLosslessLink)};"
                + $"iComplexityMode = {(int)pParam->iComplexityMode};iNumRefFrame = {pParam->iNumRefFrame};iEntropyCodingModeFlag = {pParam->iEntropyCodingModeFlag};uiMaxNalSize = {pParam->uiMaxNalSize};iLTRRefNum = {pParam->iLTRRefNum};iMultipleThreadIdc = {pParam->iMultipleThreadIdc};iLoopFilterDisableIdc = {pParam->iLoopFilterDisableIdc} (offset(alpha/beta): {pParam->iLoopFilterAlphaC0Offset},{pParam->iLoopFilterBetaOffset};iComplexityMode = {(int)pParam->iComplexityMode},iMaxQp = {pParam->iMaxQp};iMinQp = {pParam->iMinQp})");
        int32_t i = 0;
        int32_t iSpatialLayers = (pParam->iSpatialLayerNum < MAX_SPATIAL_LAYER_NUM) ? (pParam->iSpatialLayerNum) :
                                 MAX_SPATIAL_LAYER_NUM;
        while (i < iSpatialLayers)
        {
            SSpatialLayerConfig* pSpatialCfg = &pParam->sSpatialLayers[i];
            WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_INFO,
                    $"sSpatialLayers[{i}]: .iVideoWidth= {pSpatialCfg->iVideoWidth}; .iVideoHeight= {pSpatialCfg->iVideoHeight}; .fFrameRate= {pSpatialCfg->fFrameRate:F6}f; .iSpatialBitrate= {pSpatialCfg->iSpatialBitrate}; .iMaxSpatialBitrate= {pSpatialCfg->iMaxSpatialBitrate}; .sSliceArgument.uiSliceMode= {(int)pSpatialCfg->sSliceArgument.uiSliceMode}; .sSliceArgument.iSliceNum= {pSpatialCfg->sSliceArgument.uiSliceNum}; .sSliceArgument.uiSliceSizeConstraint= {pSpatialCfg->sSliceArgument.uiSliceSizeConstraint};"
                    + $"uiProfileIdc = {(int)pSpatialCfg->uiProfileIdc};uiLevelIdc = {(int)pSpatialCfg->uiLevelIdc};iDLayerQp = {pSpatialCfg->iDLayerQp}");
            ++i;
        }
    }

    private static int B(bool b) => b ? 1 : 0;

    private void LogStatistics(int64_t kiCurrentFrameTs, int32_t iMaxDid)
    {
        for (int32_t iDid = 0; iDid <= iMaxDid; iDid++)
        {
            SEncoderStatistics* pStatistics = &((SEncoderStatistics*)&m_pEncContext->sEncoderStatistics)[iDid];
            WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_INFO,
                    $"EncoderStatistics: SpatialId = {iDid},{pStatistics->uiWidth}x{pStatistics->uiHeight}, SpeedInMs: {pStatistics->fAverageFrameSpeedInMs:F6}, fAverageFrameRate={pStatistics->fAverageFrameRate:F6}, "
                    + $"LastFrameRate={pStatistics->fLatestFrameRate:F6}, LatestBitRate={pStatistics->uiBitRate}, LastFrameQP={pStatistics->uiAverageFrameQP}, uiInputFrameCount={pStatistics->uiInputFrameCount}, uiSkippedFrameCount={pStatistics->uiSkippedFrameCount}, "
                    + $"uiResolutionChangeTimes={pStatistics->uiResolutionChangeTimes}, uIDRReqNum={pStatistics->uiIDRReqNum}, uIDRSentNum={pStatistics->uiIDRSentNum}, uLTRSentNum=NA, iTotalEncodedBytes={pStatistics->iTotalEncodedBytes} at Ts = {kiCurrentFrameTs}");
        }
    }

    private void UpdateStatistics(SFrameBSInfo* pBsInfo, int64_t kiCurrentFrameMs)
    {
        int64_t kiCurrentFrameTs = m_pEncContext->uiLastTimestamp = pBsInfo->uiTimeStamp;
        int64_t kiTimeDiff = kiCurrentFrameTs - m_pEncContext->iLastStatisticsLogTs;

        int32_t iMaxDid = m_pEncContext->pSvcParam->iSpatialLayerNum - 1;
        SLayerBSInfo* pLayerInfo = &pBsInfo->sLayerInfo[0];
        uint32_t iMaxInputFrame = 0;
        float iMaxFrameRate = 0;
        for (int32_t iDid = 0; iDid <= iMaxDid; iDid++)
        {
            EVideoFrameType eFrameType = videoFrameTypeSkip;
            int32_t kiCurrentFrameSize = 0;
            for (int32_t iLayerNum = 0; iLayerNum < pBsInfo->iLayerNum; iLayerNum++)
            {
                pLayerInfo = &pBsInfo->sLayerInfo[iLayerNum];
                if ((pLayerInfo->uiLayerType == (uint8_t)VIDEO_CODING_LAYER) && (pLayerInfo->uiSpatialId == iDid))
                {
                    eFrameType = pLayerInfo->eFrameType;
                    for (int32_t iNalIdx = 0; iNalIdx < pLayerInfo->iNalCount; iNalIdx++)
                    {
                        kiCurrentFrameSize += pLayerInfo->pNalLengthInByte[iNalIdx];
                    }
                }
            }
            SEncoderStatistics* pStatistics = &((SEncoderStatistics*)&m_pEncContext->sEncoderStatistics)[iDid];
            SSpatialLayerInternal* pSpatialLayerInternalParam = &m_pEncContext->pSvcParam->sDependencyLayers[iDid];

            if ((0 != pStatistics->uiWidth && 0 != pStatistics->uiHeight)
                && (pStatistics->uiWidth != (uint32_t)pSpatialLayerInternalParam->iActualWidth
                    || pStatistics->uiHeight != (uint32_t)pSpatialLayerInternalParam->iActualHeight))
            {
                pStatistics->uiResolutionChangeTimes++;
            }
            pStatistics->uiWidth = (uint32_t)pSpatialLayerInternalParam->iActualWidth;
            pStatistics->uiHeight = (uint32_t)pSpatialLayerInternalParam->iActualHeight;

            bool kbCurrentFrameSkipped = (videoFrameTypeSkip == eFrameType);
            pStatistics->uiInputFrameCount++;
            pStatistics->uiSkippedFrameCount += (kbCurrentFrameSkipped ? 1u : 0u);
            iMaxInputFrame = Math.Max(pStatistics->uiInputFrameCount, iMaxInputFrame);
            int32_t iProcessedFrameCount = (int32_t)(pStatistics->uiInputFrameCount - pStatistics->uiSkippedFrameCount);
            if (!kbCurrentFrameSkipped && iProcessedFrameCount != 0)
            {
                pStatistics->fAverageFrameSpeedInMs += (kiCurrentFrameMs - pStatistics->fAverageFrameSpeedInMs) / iProcessedFrameCount;
            }
            // rate control related
            if (0 != m_pEncContext->uiStartTimestamp)
            {
                if (kiCurrentFrameTs > m_pEncContext->uiStartTimestamp + 800)
                {
                    pStatistics->fAverageFrameRate = ((float)(pStatistics->uiInputFrameCount) * 1000 /
                                                      (kiCurrentFrameTs - m_pEncContext->uiStartTimestamp));
                }
            }
            else
            {
                m_pEncContext->uiStartTimestamp = kiCurrentFrameTs;
            }
            iMaxFrameRate = Math.Max(iMaxFrameRate, pStatistics->fAverageFrameRate);
            //pStatistics->fLatestFrameRate = m_pEncContext->pWelsSvcRc->fLatestFrameRate; //TODO: finish the calculation in RC
            //pStatistics->uiBitRate = m_pEncContext->pWelsSvcRc->iActualBitRate; //TODO: finish the calculation in RC
            pStatistics->uiAverageFrameQP = (uint32_t)m_pEncContext->pWelsSvcRc[iDid].iAverageFrameQp;

            if (videoFrameTypeIDR == eFrameType || videoFrameTypeI == eFrameType)
            {
                pStatistics->uiIDRSentNum++;
            }
            if (m_pEncContext->pLtr->bLTRMarkingFlag)
            {
                pStatistics->uiLTRSentNum++;
            }

            pStatistics->iTotalEncodedBytes += (uint64_t)kiCurrentFrameSize;

            int32_t kiDeltaFrames = (int32_t)(pStatistics->uiInputFrameCount -
                                              pStatistics->iLastStatisticsFrameCount);
            if (kiDeltaFrames > (m_pEncContext->pSvcParam->fMaxFrameRate * 2))
            {
                if (kiTimeDiff >= m_pEncContext->iStatisticsLogInterval)
                {
                    float fTimeDiffSec = kiTimeDiff / 1000.0f;
                    pStatistics->fLatestFrameRate = (float)((pStatistics->uiInputFrameCount -
                                                             pStatistics->iLastStatisticsFrameCount) / fTimeDiffSec);
                    pStatistics->uiBitRate = (uint32_t)((pStatistics->iTotalEncodedBytes) * 8 / fTimeDiffSec);

                    if (Math.Abs(pStatistics->fLatestFrameRate - m_pEncContext->pSvcParam->fMaxFrameRate) > 30)
                    {
                        WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_WARNING,
                                $"Actual input fLatestFrameRate = {pStatistics->fLatestFrameRate:F6} is quite different from framerate in setting {m_pEncContext->pSvcParam->fMaxFrameRate:F6}, please check setting or timestamp unit (ms), cur_Ts = {kiCurrentFrameTs} start_Ts = {m_pEncContext->iLastStatisticsLogTs}");
                    }

                    if (m_pEncContext->pSvcParam->iRCMode == RC_QUALITY_MODE || m_pEncContext->pSvcParam->iRCMode == RC_BITRATE_MODE)
                    {
                        if ((pStatistics->fLatestFrameRate > 0)
                            && Math.Abs(m_pEncContext->pSvcParam->fMaxFrameRate - pStatistics->fLatestFrameRate) > 5)
                        {
                            WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_WARNING,
                                    $"Actual input framerate {pStatistics->fLatestFrameRate:F6} is different from framerate in setting {m_pEncContext->pSvcParam->fMaxFrameRate:F6}, suggest to use other rate control modes");
                        }
                    }
                    // update variables
                    pStatistics->iLastStatisticsBytes = pStatistics->iTotalEncodedBytes;
                    pStatistics->iLastStatisticsFrameCount = pStatistics->uiInputFrameCount;
                    m_pEncContext->iLastStatisticsLogTs = kiCurrentFrameTs;
                    LogStatistics(kiCurrentFrameTs, iMaxDid);
                    pStatistics->iTotalEncodedBytes = 0;
                    //TODO: the following statistics will be calculated and added later
                    //pStatistics->uiLTRSentNum
                }
            }
        }
    }

    /************************************************************************
    * InDataFormat, IDRInterval, SVC Encode Param, Frame Rate, Bitrate,..
    ************************************************************************/
    public int SetOption(ENCODER_OPTION eOptionId, void* pOption)
    {
        if (null == pOption)
        {
            return (int)cmInitParaError;
        }

        if ((null == m_pEncContext || false == m_bInitialFlag) && eOptionId != ENCODER_OPTION_TRACE_LEVEL
            && eOptionId != ENCODER_OPTION_TRACE_CALLBACK && eOptionId != ENCODER_OPTION_TRACE_CALLBACK_CONTEXT)
        {
            return (int)cmInitExpected;
        }

        switch (eOptionId)
        {
            case ENCODER_OPTION_INTER_SPATIAL_PRED: // Inter spatial layer prediction flag
            {
                WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_INFO,
                        "ENCODER_OPTION_INTER_SPATIAL_PRED, this feature not supported at present.");
            }
            break;
            case ENCODER_OPTION_DATAFORMAT: // Input color space
            {
                int32_t iValue = *((int32_t*)pOption);
                int32_t iColorspace = iValue;
                if (iColorspace == 0)
                {
                    return (int)cmInitParaError;
                }

                m_iCspInternal = iColorspace;
                WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_INFO,
                        $"CWelsH264SVCEncoder::SetOption():ENCODER_OPTION_DATAFORMAT, m_iCspInternal = 0x{m_iCspInternal:x}");
            }
            break;
            case ENCODER_OPTION_IDR_INTERVAL: // IDR Interval
            {
                int32_t iValue = *((int32_t*)pOption);
                WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_INFO,
                        $"CWelsH264SVCEncoder::SetOption():ENCODER_OPTION_IDR_INTERVAL iValue = {iValue}");
                if (iValue <= -1)
                {
                    iValue = 0;
                }
                if (iValue == (int32_t)m_pEncContext->pSvcParam->uiIntraPeriod)
                {
                    return (int)cmResultSuccess;
                }
                m_pEncContext->pSvcParam->uiIntraPeriod = (uint32_t)iValue;
                WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_INFO,
                        $"CWelsH264SVCEncoder::SetOption():ENCODER_OPTION_IDR_INTERVAL uiIntraPeriod updated to {m_pEncContext->pSvcParam->uiIntraPeriod}");
            }
            break;
            case ENCODER_OPTION_SVC_ENCODE_PARAM_BASE: // SVC Encoding Parameter
            {
                SEncParamBase sEncodingParam;
                SWelsSvcCodingParam sConfig = default;
                sConfig.FillDefault(); // the constructor
                int32_t iTargetWidth = 0;
                int32_t iTargetHeight = 0;

                sEncodingParam = *(SEncParamBase*)pOption; // memcpy
                if (sConfig.ParamBaseTranscode(sEncodingParam) != 0)
                {
                    WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_ERROR,
                            "CWelsH264SVCEncoder::SetOption():ENCODER_OPTION_SVC_ENCODE_PARAM_BASE, ParamTranscode failed!");
                    return (int)cmInitParaError;
                }
                /* New configuration available here */
                iTargetWidth = sConfig.iPicWidth;
                iTargetHeight = sConfig.iPicHeight;
                if (m_iMaxPicWidth != iTargetWidth
                    || m_iMaxPicHeight != iTargetHeight)
                {
                    m_iMaxPicWidth = iTargetWidth;
                    m_iMaxPicHeight = iTargetHeight;
                }
                if (sConfig.DetermineTemporalSettings() != 0)
                {
                    WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_ERROR,
                            "CWelsH264SVCEncoder::SetOption():ENCODER_OPTION_SVC_ENCODE_PARAM_BASE, DetermineTemporalSettings failed!");
                    return (int)cmInitParaError;
                }
                WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_INFO,
                        $"CWelsH264SVCEncoder::SetOption():ENCODER_OPTION_SVC_ENCODE_PARAM_BASE iUsageType = {(int)sEncodingParam.iUsageType},iPicWidth= {sEncodingParam.iPicWidth};iPicHeight= {sEncodingParam.iPicHeight};iTargetBitrate= {sEncodingParam.iTargetBitrate};fMaxFrameRate=  {sEncodingParam.fMaxFrameRate:F6}f;iRCMode= {(int)sEncodingParam.iRCMode}");
                sWelsEncCtx* pEncContext = m_pEncContext;
                int32_t iAdjust = WelsEncoderParamAdjust(&pEncContext, &sConfig);
                m_pEncContext = pEncContext;
                if (iAdjust != 0)
                {
                    return (int)cmInitParaError;
                }

                //LogStatistics
                WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_INFO,
                        "CWelsH264SVCEncoder::SetOption():ENCODER_OPTION_SVC_ENCODE_PARAM_BASE, LogStatisticsBeforeNewEncoding");
                LogStatistics(m_pEncContext->iLastStatisticsLogTs, 0);
            }
            break;

            case ENCODER_OPTION_SVC_ENCODE_PARAM_EXT: // SVC Encoding Parameter
            {
                SEncParamExt sEncodingParam;
                SWelsSvcCodingParam sConfig = default;
                sConfig.FillDefault(); // the constructor
                int32_t iTargetWidth = 0;
                int32_t iTargetHeight = 0;

                sEncodingParam = *(SEncParamExt*)pOption; // memcpy
                TraceParamInfo(&sEncodingParam);
                if (sEncodingParam.iSpatialLayerNum < 1
                    || sEncodingParam.iSpatialLayerNum > MAX_SPATIAL_LAYER_NUM) // verify number of spatial layer
                {
                    WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_ERROR,
                            $"CWelsH264SVCEncoder::SetOption():ENCODER_OPTION_SVC_ENCODE_PARAM_EXT, iSpatialLayerNum({sEncodingParam.iSpatialLayerNum}) failed!");
                    return (int)cmInitParaError;
                }

                if (sConfig.ParamTranscode(sEncodingParam) != 0)
                {
                    WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_ERROR,
                            "CWelsH264SVCEncoder::SetOption():ENCODER_OPTION_SVC_ENCODE_PARAM_EXT, ParamTranscode failed!");
                    return (int)cmInitParaError;
                }
                if (sConfig.iSpatialLayerNum < 1)
                {
                    WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_ERROR,
                            $"CWelsH264SVCEncoder::SetOption():ENCODER_OPTION_SVC_ENCODE_PARAM_EXT, iSpatialLayerNum({sConfig.iSpatialLayerNum}) failed!");
                    return (int)cmInitParaError;
                }
                if (sConfig.DetermineTemporalSettings() != 0)
                {
                    WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_ERROR,
                            "CWelsH264SVCEncoder::SetOption():ENCODER_OPTION_SVC_ENCODE_PARAM_EXT, DetermineTemporalSettings failed!");
                    return (int)cmInitParaError;
                }

                /* New configuration available here */
                iTargetWidth = sConfig.iPicWidth;
                iTargetHeight = sConfig.iPicHeight;
                if (m_iMaxPicWidth != iTargetWidth
                    || m_iMaxPicHeight != iTargetHeight)
                {
                    m_iMaxPicWidth = iTargetWidth;
                    m_iMaxPicHeight = iTargetHeight;
                }
                /* Check every field whether there is new request for memory block changed or else, Oct. 24, 2008 */
                sWelsEncCtx* pEncContext = m_pEncContext;
                int32_t iAdjust = WelsEncoderParamAdjust(&pEncContext, &sConfig);
                m_pEncContext = pEncContext;
                if (iAdjust != 0)
                {
                    return (int)cmInitParaError;
                }

                //LogStatistics
                WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_INFO,
                        "CWelsH264SVCEncoder::SetOption():ENCODER_OPTION_SVC_ENCODE_PARAM_EXT, LogStatisticsBeforeNewEncoding");
                LogStatistics(m_pEncContext->iLastStatisticsLogTs, sEncodingParam.iSpatialLayerNum - 1);
            }
            break;
            case ENCODER_OPTION_FRAME_RATE: // Maximal input frame rate
            {
                float iValue = *((float*)pOption);
                if (iValue <= 0)
                {
                    return (int)cmInitParaError;
                }
                //adjust to valid range
                m_pEncContext->pSvcParam->fMaxFrameRate = WELS_CLIP3(iValue, (float)MIN_FRAME_RATE, (float)MAX_FRAME_RATE);
                WelsEncoderApplyFrameRate(m_pEncContext->pSvcParam);
                WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_INFO,
                        $"CWelsH264SVCEncoder::SetOption():ENCODER_OPTION_FRAME_RATE,m_pEncContext->pSvcParam->fMaxFrameRate= {m_pEncContext->pSvcParam->fMaxFrameRate:F6}");
            }
            break;
            case ENCODER_OPTION_BITRATE: // Target bit-rate
            {
                SBitrateInfo* pInfo = (SBitrateInfo*)pOption;
                int32_t iBitrate = pInfo->iBitrate;
                if (iBitrate <= 0)
                {
                    WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_ERROR,
                            $"CWelsH264SVCEncoder::SetOption():ENCODER_OPTION_BITRATE,iBitrate = {iBitrate}");
                    return (int)cmInitParaError;
                }
                iBitrate = WELS_CLIP3(iBitrate, MIN_BIT_RATE, MAX_BIT_RATE);
                switch (pInfo->iLayer)
                {
                    case SPATIAL_LAYER_ALL:
                        m_pEncContext->pSvcParam->iTargetBitrate = iBitrate;
                        break;
                    case SPATIAL_LAYER_0:
                        m_pEncContext->pSvcParam->sSpatialLayers[0].iSpatialBitrate = iBitrate;
                        break;
                    case SPATIAL_LAYER_1:
                        m_pEncContext->pSvcParam->sSpatialLayers[1].iSpatialBitrate = iBitrate;
                        break;
                    case SPATIAL_LAYER_2:
                        m_pEncContext->pSvcParam->sSpatialLayers[2].iSpatialBitrate = iBitrate;
                        break;
                    case SPATIAL_LAYER_3:
                        m_pEncContext->pSvcParam->sSpatialLayers[3].iSpatialBitrate = iBitrate;
                        break;
                    default:
                        WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_ERROR,
                                $"CWelsH264SVCEncoder::SetOption():ENCODER_OPTION_BITRATE,iLayer = {(int)pInfo->iLayer}");
                        return (int)cmInitParaError;
                }
                //adjust to valid range
                if (WelsEncoderApplyBitRate(&m_pWelsTrace->m_sLogCtx, m_pEncContext->pSvcParam, (int)pInfo->iLayer) != 0)
                {
                    WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_ERROR,
                            $"CWelsH264SVCEncoder::SetOption():ENCODER_OPTION_BITRATE layerId= {(int)pInfo->iLayer},iSpatialBitrate = {iBitrate}");
                    return (int)cmInitParaError;
                }
                else
                {
                    WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_INFO,
                            $"CWelsH264SVCEncoder::SetOption():ENCODER_OPTION_BITRATE layerId= {(int)pInfo->iLayer},iSpatialBitrate = {iBitrate}");
                }
            }
            break;
            case ENCODER_OPTION_MAX_BITRATE: // Target bit-rate
            {
                SBitrateInfo* pInfo = (SBitrateInfo*)pOption;
                int32_t iBitrate = pInfo->iBitrate;
                if (iBitrate <= 0)
                {
                    WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_ERROR,
                            $"CWelsH264SVCEncoder::SetOption():ENCODER_OPTION_MAX_BITRATE,iBitrate = {iBitrate}");
                    return (int)cmInitParaError;
                }
                iBitrate = WELS_CLIP3(iBitrate, MIN_BIT_RATE, MAX_BIT_RATE);
                switch (pInfo->iLayer)
                {
                    case SPATIAL_LAYER_ALL:
                        m_pEncContext->pSvcParam->iMaxBitrate = iBitrate;
                        break;
                    case SPATIAL_LAYER_0:
                        m_pEncContext->pSvcParam->sSpatialLayers[0].iMaxSpatialBitrate = iBitrate;
                        break;
                    case SPATIAL_LAYER_1:
                        m_pEncContext->pSvcParam->sSpatialLayers[1].iMaxSpatialBitrate = iBitrate;
                        break;
                    case SPATIAL_LAYER_2:
                        m_pEncContext->pSvcParam->sSpatialLayers[2].iMaxSpatialBitrate = iBitrate;
                        break;
                    case SPATIAL_LAYER_3:
                        m_pEncContext->pSvcParam->sSpatialLayers[3].iMaxSpatialBitrate = iBitrate;
                        break;
                    default:
                        WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_ERROR,
                                $"CWelsH264SVCEncoder::SetOption():ENCODER_OPTION_MAX_BITRATE,iLayer = {(int)pInfo->iLayer}");
                        return (int)cmInitParaError;
                }
                //adjust to valid range
                if (WelsEncoderApplyBitRate(&m_pWelsTrace->m_sLogCtx, m_pEncContext->pSvcParam, (int)pInfo->iLayer) != 0)
                {
                    WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_ERROR,
                            $"CWelsH264SVCEncoder::SetOption():ENCODER_OPTION_BITRATE layerId= {(int)pInfo->iLayer},iMaxSpatialBitrate = {iBitrate}");
                    return (int)cmInitParaError;
                }
                else
                {
                    WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_INFO,
                            $"CWelsH264SVCEncoder::SetOption():ENCODER_OPTION_BITRATE layerId= {(int)pInfo->iLayer},iMaxSpatialBitrate = {iBitrate}");
                }
            }
            break;
            case ENCODER_OPTION_RC_MODE: // 0:quality mode;1:bit-rate mode;2:bitrate limited mode
            {
                int32_t iValue = *((int32_t*)pOption);
                m_pEncContext->pSvcParam->iRCMode = (RC_MODES)iValue;
                WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_INFO,
                        $"CWelsH264SVCEncoder::SetOption():ENCODER_OPTION_RC_MODE iRCMode= {iValue} (Note: not suggest changing RC-mode in middle of encoding)");
                WelsRcInitFuncPointers(m_pEncContext, m_pEncContext->pSvcParam->iRCMode);
            }
            break;
            case ENCODER_OPTION_RC_FRAME_SKIP: // 0:FRAME-SKIP disabled;1:FRAME-SKIP enabled
            {
                bool bValue = *((bool*)pOption);
                if (m_pEncContext->pSvcParam->iRCMode != RC_OFF_MODE)
                {
                    m_pEncContext->pSvcParam->bEnableFrameSkip = bValue;
                    WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_INFO,
                            $"CWelsH264SVCEncoder::SetOption():ENCODER_OPTION_RC_FRAME_SKIP, frame-skip setting({B(bValue)})");
                }
                else
                {
                    WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_INFO,
                            $"CWelsH264SVCEncoder::SetOption():ENCODER_OPTION_RC_FRAME_SKIP, rc off, frame-skip setting({B(bValue)}) un-useful");
                }
            }
            break;
            case ENCODER_PADDING_PADDING: // 0:disable padding;1:padding
            {
                int32_t iValue = *((int32_t*)pOption);
                m_pEncContext->pSvcParam->iPaddingFlag = iValue;
                WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_INFO,
                        $"CWelsH264SVCEncoder::SetOption():ENCODER_PADDING_PADDING iPaddingFlag= {iValue} ");
            }
            break;
            case ENCODER_LTR_RECOVERY_REQUEST:
            {
                SLTRRecoverRequest* pLTR_Recover_Request = (SLTRRecoverRequest*)pOption;
                FilterLTRRecoveryRequest(m_pEncContext, pLTR_Recover_Request);
            }
            break;
            case ENCODER_LTR_MARKING_FEEDBACK:
            {
                SLTRMarkingFeedback* fb = (SLTRMarkingFeedback*)pOption;
                FilterLTRMarkingFeedback(m_pEncContext, fb);
            }
            break;
            case ENCODER_LTR_MARKING_PERIOD:
            {
                uint32_t iValue = *((uint32_t*)pOption);
                m_pEncContext->pSvcParam->iLtrMarkPeriod = iValue;
                WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_INFO,
                        $"CWelsH264SVCEncoder::SetOption():ENCODER_LTR_MARKING_PERIOD iLtrMarkPeriod= {(int32_t)iValue} ");
            }
            break;
            case ENCODER_OPTION_LTR:
            {
                SLTRConfig* pLTRValue = (SLTRConfig*)pOption;
                sWelsEncCtx* pEncContext = m_pEncContext;
                int32_t iApply = WelsEncoderApplyLTR(&m_pWelsTrace->m_sLogCtx, &pEncContext, pLTRValue);
                m_pEncContext = pEncContext;
                if (iApply != 0)
                {
                    return (int)cmInitParaError;
                }
                WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_INFO,
                        $"CWelsH264SVCEncoder::SetOption():ENCODER_OPTION_LTR,expected bEnableLongTermReference = {B(pLTRValue->bEnableLongTermReference)},expeced iLTRRefNum = {pLTRValue->iLTRRefNum},actual bEnableLongTermReference = {B(m_pEncContext->pSvcParam->bEnableLongTermReference)},actual iLTRRefNum = {m_pEncContext->pSvcParam->iLTRRefNum}");
            }
            break;
            case ENCODER_OPTION_ENABLE_SSEI:
            {
                bool iValue = *((bool*)pOption);
                m_pEncContext->pSvcParam->bEnableSSEI = iValue;
                WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_INFO,
                        $" CWelsH264SVCEncoder::SetOption enable SSEI = {B(m_pEncContext->pSvcParam->bEnableSSEI)} -- this is not supported yet");
            }
            break;
            case ENCODER_OPTION_ENABLE_PREFIX_NAL_ADDING:
            {
                bool iValue = *((bool*)pOption);
                m_pEncContext->pSvcParam->bPrefixNalAddingCtrl = iValue;
                WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_INFO,
                        $" CWelsH264SVCEncoder::SetOption bPrefixNalAddingCtrl = {B(m_pEncContext->pSvcParam->bPrefixNalAddingCtrl)} ");
            }
            break;
            case ENCODER_OPTION_SPS_PPS_ID_STRATEGY:
            {
                int32_t iValue = *((int32_t*)pOption);
                EParameterSetStrategy eNewStrategy = CONSTANT_ID;
                switch (iValue)
                {
                    case 0:
                        eNewStrategy = CONSTANT_ID;
                        break;
                    case 0x01:
                        eNewStrategy = INCREASING_ID;
                        break;
                    case 0x02:
                        eNewStrategy = SPS_LISTING;
                        break;
                    case 0x03:
                        eNewStrategy = SPS_LISTING_AND_PPS_INCREASING;
                        break;
                    case 0x06:
                        eNewStrategy = SPS_PPS_LISTING;
                        break;
                    default:
                        WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_ERROR,
                                $" CWelsH264SVCEncoder::SetOption eSpsPpsIdStrategy({iValue}) not in valid range, unchanged! existing={(int)m_pEncContext->pSvcParam->eSpsPpsIdStrategy}");
                        break;
                }

                if (((((int32_t)eNewStrategy & (int32_t)SPS_LISTING) != 0)
                     || (((int32_t)m_pEncContext->pSvcParam->eSpsPpsIdStrategy & (int32_t)SPS_LISTING) != 0))
                    && m_pEncContext->pSvcParam->eSpsPpsIdStrategy != eNewStrategy)
                {
                    WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_ERROR,
                            $" CWelsH264SVCEncoder::SetOption eSpsPpsIdStrategy changing in the middle of call is NOT allowed for eSpsPpsIdStrategy>INCREASING_ID: existing setting is {(int)m_pEncContext->pSvcParam->eSpsPpsIdStrategy} and the new one is {iValue}");
                    return (int)cmInitParaError;
                }
                SWelsSvcCodingParam sConfig;
                sConfig = *m_pEncContext->pSvcParam; // memcpy
                sConfig.eSpsPpsIdStrategy = eNewStrategy;
                WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_INFO, $" CWelsH264SVCEncoder::SetOption eSpsPpsIdStrategy = {(int)sConfig.eSpsPpsIdStrategy} ");

                sWelsEncCtx* pEncContext = m_pEncContext;
                int32_t iAdjust = WelsEncoderParamAdjust(&pEncContext, &sConfig);
                m_pEncContext = pEncContext;
                if (iAdjust != 0)
                {
                    return (int)cmInitParaError;
                }
            }
            break;
            case ENCODER_OPTION_CURRENT_PATH:
            {
                if (m_pEncContext->pSvcParam != null)
                {
                    sbyte* path = (sbyte*)pOption;
                    m_pEncContext->pSvcParam->pCurPath = path;
                }
            }
            break;
            case ENCODER_OPTION_DUMP_FILE:
            {
                // ENABLE_FRAME_DUMP is off, as upstream builds it.
            }
            break;
            case ENCODER_OPTION_TRACE_LEVEL:
            {
                if (m_pWelsTrace != null)
                {
                    uint32_t level = *((uint32_t*)pOption);
                    m_pWelsTrace->SetTraceLevel((int)level);
                }
            }
            break;
            case ENCODER_OPTION_TRACE_CALLBACK:
            {
                if (m_pWelsTrace != null)
                {
                    delegate*<void*, int, string, void> callback = *((delegate*<void*, int, string, void>*)pOption);
                    m_pWelsTrace->SetTraceCallback(callback);
                    WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_INFO,
                            $"CWelsH264SVCEncoder::SetOption():ENCODER_OPTION_TRACE_CALLBACK callback = 0x{(nint)(void*)callback:X}.");
                }
            }
            break;
            case ENCODER_OPTION_TRACE_CALLBACK_CONTEXT:
            {
                if (m_pWelsTrace != null)
                {
                    void* ctx = *((void**)pOption);
                    m_pWelsTrace->SetTraceCallbackContext(ctx);
                }
            }
            break;
            case ENCODER_OPTION_PROFILE:
            {
                SProfileInfo* pProfileInfo = (SProfileInfo*)pOption;
                if ((pProfileInfo->iLayer < (int)SPATIAL_LAYER_0) || (pProfileInfo->iLayer > (int)SPATIAL_LAYER_3))
                {
                    WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_ERROR,
                            $"CWelsH264SVCEncoder::SetOption():ENCODER_OPTION_PROFILE,iLayer = {pProfileInfo->iLayer}(rang0-3)");
                    return (int)cmInitParaError;
                }
                CheckProfileSetting(&m_pWelsTrace->m_sLogCtx, m_pEncContext->pSvcParam, pProfileInfo->iLayer,
                                    pProfileInfo->uiProfileIdc);
                WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_INFO,
                        $"CWelsH264SVCEncoder::SetOption():ENCODER_OPTION_PROFILE,layerId = {pProfileInfo->iLayer},expected profile = {(int)pProfileInfo->uiProfileIdc},actual profile = {(int)m_pEncContext->pSvcParam->sSpatialLayers[pProfileInfo->iLayer].uiProfileIdc}");
            }
            break;
            case ENCODER_OPTION_LEVEL:
            {
                SLevelInfo* pLevelInfo = (SLevelInfo*)pOption;
                if ((pLevelInfo->iLayer < (int)SPATIAL_LAYER_0) || (pLevelInfo->iLayer > (int)SPATIAL_LAYER_3))
                {
                    WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_ERROR,
                            $"CWelsH264SVCEncoder::SetOption():ENCODER_OPTION_PROFILE,iLayer = {pLevelInfo->iLayer}(rang0-3)");
                    return (int)cmInitParaError;
                }
                CheckLevelSetting(&m_pWelsTrace->m_sLogCtx, m_pEncContext->pSvcParam, pLevelInfo->iLayer, pLevelInfo->uiLevelIdc);
                WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_INFO,
                        $"CWelsH264SVCEncoder::SetOption():ENCODER_OPTION_LEVEL,layerId = {pLevelInfo->iLayer},expected level = {(int)pLevelInfo->uiLevelIdc},actual level = {(int)m_pEncContext->pSvcParam->sSpatialLayers[pLevelInfo->iLayer].uiLevelIdc}");
            }
            break;
            case ENCODER_OPTION_NUMBER_REF:
            {
                int32_t iValue = *((int32_t*)pOption);
                CheckReferenceNumSetting(&m_pWelsTrace->m_sLogCtx, m_pEncContext->pSvcParam, iValue);
                WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_INFO,
                        $"CWelsH264SVCEncoder::SetOption():ENCODER_OPTION_NUMBER_REF,expected refNum = {iValue},actual refnum = {m_pEncContext->pSvcParam->iNumRefFrame}");
            }
            break;
            case ENCODER_OPTION_DELIVERY_STATUS:
            {
                SDeliveryStatus* pValue = (SDeliveryStatus*)pOption;
                m_pEncContext->bDeliveryFlag = pValue->bDeliveryFlag;
                WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_DEBUG,
                        $"CWelsH264SVCEncoder::SetOption():ENCODER_OPTION_DELIVERY_STATUS,bDeliveryFlag = {B(pValue->bDeliveryFlag)}");
            }
            break;
            case ENCODER_OPTION_COMPLEXITY:
            {
                int32_t iValue = *((int32_t*)pOption);
                m_pEncContext->pSvcParam->iComplexityMode = (ECOMPLEXITY_MODE)iValue;
                WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_INFO,
                        $"CWelsH264SVCEncoder::SetOption():ENCODER_OPTION_COMPLEXITY,iComplexityMode = {iValue}");
            }
            break;
            case ENCODER_OPTION_GET_STATISTICS:
            {
                WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_WARNING,
                        "CWelsH264SVCEncoder::SetOption():ENCODER_OPTION_GET_STATISTICS: this option is get-only!");
            }
            break;
            case ENCODER_OPTION_STATISTICS_LOG_INTERVAL:
            {
                int32_t iValue = *((int32_t*)pOption);
                m_pEncContext->iStatisticsLogInterval = iValue;
                WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_INFO,
                        $"CWelsH264SVCEncoder::SetOption():ENCODER_OPTION_STATISTICS_LOG_INTERVAL,iStatisticsLogInterval = {iValue}");
            }
            break;
            case ENCODER_OPTION_IS_LOSSLESS_LINK:
            {
                bool bValue = *((bool*)pOption);
                m_pEncContext->pSvcParam->bIsLosslessLink = bValue;
                WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_INFO,
                        $"CWelsH264SVCEncoder::SetOption():ENCODER_OPTION_IS_LOSSLESS_LINK,bIsLosslessLink = {B(bValue)}");
            }
            break;
            case ENCODER_OPTION_BITS_VARY_PERCENTAGE:
            {
                int32_t iValue = *((int32_t*)pOption);
                m_pEncContext->pSvcParam->iBitsVaryPercentage = WELS_CLIP3(iValue, 0, 100);
                WelsEncoderApplyBitVaryRang(&m_pWelsTrace->m_sLogCtx, m_pEncContext->pSvcParam,
                                            m_pEncContext->pSvcParam->iBitsVaryPercentage);
                WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_INFO,
                        $"CWelsH264SVCEncoder::SetOption():ENCODER_OPTION_BITS_VARY_PERCENTAGE,iBitsVaryPercentage = {iValue}");
            }
            break;

            default:
                return (int)cmInitParaError;
        }

        return 0;
    }

    public int GetOption(ENCODER_OPTION eOptionId, void* pOption)
    {
        if (null == pOption)
        {
            return (int)cmInitParaError;
        }
        if (null == m_pEncContext || false == m_bInitialFlag)
        {
            return (int)cmInitExpected;
        }

        switch (eOptionId)
        {
            case ENCODER_OPTION_INTER_SPATIAL_PRED: // Inter spatial layer prediction flag
            {
                WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_INFO,
                        "ENCODER_OPTION_INTER_SPATIAL_PRED, this feature not supported at present.");
            }
            break;
            case ENCODER_OPTION_DATAFORMAT: // Input color space
            {
                WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_INFO,
                        $"CWelsH264SVCEncoder::GetOption():ENCODER_OPTION_DATAFORMAT, m_iCspInternal= 0x{m_iCspInternal:x}");
                *((int32_t*)pOption) = m_iCspInternal;
            }
            break;
            case ENCODER_OPTION_IDR_INTERVAL: // IDR Interval
            {
                WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_INFO,
                        $"CWelsH264SVCEncoder::GetOption():ENCODER_OPTION_IDR_INTERVAL, uiIntraPeriod= {m_pEncContext->pSvcParam->uiIntraPeriod}");
                *((int32_t*)pOption) = (int32_t)m_pEncContext->pSvcParam->uiIntraPeriod;
            }
            break;
            case ENCODER_OPTION_SVC_ENCODE_PARAM_EXT: // SVC Encoding Parameter
            {
                WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_INFO,
                        "CWelsH264SVCEncoder::GetOption():ENCODER_OPTION_SVC_ENCODE_PARAM_EXT");
                *(SEncParamExt*)pOption = m_pEncContext->pSvcParam->Base(); // memcpy
            }
            break;
            case ENCODER_OPTION_SVC_ENCODE_PARAM_BASE: // SVC Encoding Parameter
            {
                WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_INFO,
                        "CWelsH264SVCEncoder::GetOption():ENCODER_OPTION_SVC_ENCODE_PARAM_BASE");
                m_pEncContext->pSvcParam->GetBaseParams((SEncParamBase*)pOption);
            }
            break;

            case ENCODER_OPTION_FRAME_RATE: // Maximal input frame rate
            {
                WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_INFO,
                        $"CWelsH264SVCEncoder::GetOption():ENCODER_OPTION_FRAME_RATE, fMaxFrameRate = {m_pEncContext->pSvcParam->fMaxFrameRate:F6}f");
                *((float*)pOption) = m_pEncContext->pSvcParam->fMaxFrameRate;
            }
            break;
            case ENCODER_OPTION_BITRATE: // Target bit-rate
            {
                SBitrateInfo* pInfo = (SBitrateInfo*)pOption;
                if ((pInfo->iLayer != SPATIAL_LAYER_ALL) && (pInfo->iLayer != SPATIAL_LAYER_0) && (pInfo->iLayer != SPATIAL_LAYER_1)
                    && (pInfo->iLayer != SPATIAL_LAYER_2) && (pInfo->iLayer != SPATIAL_LAYER_3))
                    return (int)cmInitParaError;
                if (pInfo->iLayer == SPATIAL_LAYER_ALL)
                {
                    pInfo->iBitrate = m_pEncContext->pSvcParam->iTargetBitrate;
                }
                else
                {
                    pInfo->iBitrate = m_pEncContext->pSvcParam->sSpatialLayers[(int)pInfo->iLayer].iSpatialBitrate;
                }
                WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_INFO,
                        $"CWelsH264SVCEncoder::GetOption():ENCODER_OPTION_BITRATE, layerId ={(int)pInfo->iLayer},iBitrate = {pInfo->iBitrate}");
            }
            break;
            case ENCODER_OPTION_MAX_BITRATE: // Target bit-rate
            {
                SBitrateInfo* pInfo = (SBitrateInfo*)pOption;
                if ((pInfo->iLayer != SPATIAL_LAYER_ALL) && (pInfo->iLayer != SPATIAL_LAYER_0) && (pInfo->iLayer != SPATIAL_LAYER_1)
                    && (pInfo->iLayer != SPATIAL_LAYER_2) && (pInfo->iLayer != SPATIAL_LAYER_3))
                    return (int)cmInitParaError;
                if (pInfo->iLayer == SPATIAL_LAYER_ALL)
                {
                    pInfo->iBitrate = m_pEncContext->pSvcParam->iMaxBitrate;
                }
                else
                {
                    pInfo->iBitrate = m_pEncContext->pSvcParam->sSpatialLayers[(int)pInfo->iLayer].iMaxSpatialBitrate;
                }
                WelsLog(&m_pWelsTrace->m_sLogCtx, WELS_LOG_INFO,
                        $"CWelsH264SVCEncoder::GetOption():ENCODER_OPTION_MAX_BITRATE,, layerId ={(int)pInfo->iLayer},iBitrate = {pInfo->iBitrate}");
            }
            break;
            case ENCODER_OPTION_GET_STATISTICS:
            {
                SEncoderStatistics* pStatistics = (SEncoderStatistics*)pOption;
                SEncoderStatistics* pEncStatistics =
                    &((SEncoderStatistics*)&m_pEncContext->sEncoderStatistics)[m_pEncContext->pSvcParam->iSpatialLayerNum - 1];
                pStatistics->uiWidth = pEncStatistics->uiWidth;
                pStatistics->uiHeight = pEncStatistics->uiHeight;
                pStatistics->fAverageFrameSpeedInMs = pEncStatistics->fAverageFrameSpeedInMs;

                // rate control related
                pStatistics->fAverageFrameRate = pEncStatistics->fAverageFrameRate;
                pStatistics->fLatestFrameRate = pEncStatistics->fLatestFrameRate;
                pStatistics->uiBitRate = pEncStatistics->uiBitRate;
                pStatistics->uiAverageFrameQP = pEncStatistics->uiAverageFrameQP;

                pStatistics->uiInputFrameCount = pEncStatistics->uiInputFrameCount;
                pStatistics->uiSkippedFrameCount = pEncStatistics->uiSkippedFrameCount;

                pStatistics->uiResolutionChangeTimes = pEncStatistics->uiResolutionChangeTimes;
                pStatistics->uiIDRReqNum = pEncStatistics->uiIDRReqNum;
                pStatistics->uiIDRSentNum = pEncStatistics->uiIDRSentNum;
                pStatistics->uiLTRSentNum = pEncStatistics->uiLTRSentNum;
            }
            break;
            case ENCODER_OPTION_STATISTICS_LOG_INTERVAL:
            {
                *((int32_t*)pOption) = m_pEncContext->iStatisticsLogInterval;
            }
            break;
            case ENCODER_OPTION_COMPLEXITY:
            {
                *((int32_t*)pOption) = (int32_t)m_pEncContext->pSvcParam->iComplexityMode;
            }
            break;
            default:
                return (int)cmInitParaError;
        }

        return 0;
    }

    // DumpSrcPicture: DUMP_SRC_PICTURE is off, as upstream builds it.
}

internal static unsafe partial class WelsEnc
{
    /// <summary>WelsCreateSVCEncoder. Returns 0 on success.</summary>
    public static int32_t WelsCreateSVCEncoder(CWelsH264SVCEncoder** ppEncoder)
    {
        if ((*ppEncoder = CWelsH264SVCEncoder.New()) != null)
        {
            return 0;
        }

        return 1;
    }

    /// <summary>WelsDestroySVCEncoder.</summary>
    public static void WelsDestroySVCEncoder(CWelsH264SVCEncoder* pEncoder)
    {
        CWelsH264SVCEncoder* pSVCEncoder = pEncoder;

        if (pSVCEncoder != null)
        {
            CWelsH264SVCEncoder.Delete(ref pSVCEncoder);
        }
    }
}
