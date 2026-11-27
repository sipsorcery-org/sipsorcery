//-----------------------------------------------------------------------------
// Filename: wels_preprocess.cs
//
// Description: Port of codec/encoder/core/inc/wels_preprocess.h and
// codec/encoder/core/src/wels_preprocess.cpp, camera half: SVAAFrameInfo, the
// VAA state the mode decision and the rate control read (E5), and the
// preprocessing (E7). CWelsPreProcess builds the spatial pictures of each
// input frame (the source copy, the denoiser, the downsampling and the
// padding, the IDR period and the scene change detection) and analyses them
// for the encoder (the VAA statistics, the background detection, the adaptive
// quantisation and the complexity analysis) through the video processor
// (CVpFrameWork). It is a native struct, as the port's other C++ classes are,
// that CreatePreProcess allocates and DeletePreProcess frees (WELS_NEW_OP and
// WELS_DELETE_OP); m_eClass tags which of the derived classes it is, and the
// virtual GetCurrentOrigFrame and DetectSceneChange switch on it. Only
// CWelsPreProcessVideo is ported: the screen content class
// (CWelsPreProcessScreen), its SVAAFrameInfoExt and the base class functions
// that only the screen content code calls (GetBestRefPic for screen,
// GetRefFrameInfo, UpdateBlockIdcForScreen) come with Phase 4, so
// CreatePreProcess throws NotSupportedException for screen content. The
// colour space conversion is not supported upstream either.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 26 Nov 2026  Claude          Created (E5).
// 27 Nov 2026  Claude          Scaled_Picture, CWelsPreProcess (CWelsPreProcessVideo) and the
//                              free functions of wels_preprocess.cpp (E7).
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
// The ported code is also covered by the OpenH264 BSD 2-Clause notice below.
//-----------------------------------------------------------------------------

/*!
 * \copy
 *     Copyright (c)  2009-2013, Cisco Systems
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
 * \file    wels_preprocess.h
 *
 * \brief   interface of video pre-process plugins
 *
 * \date    20110331 Created
 *
 *************************************************************************************
 */

using System.Diagnostics;
using System.Runtime.InteropServices;
using static SIPSorcery.H264.Encoder.WelsEnc;

namespace SIPSorcery.H264.Encoder;

/// <summary>Scaled_Picture.</summary>
internal unsafe struct Scaled_Picture
{
    public SPicture* pScaledInputPicture;
    public fixed int32_t iScaledWidth[WelsEnc.MAX_DEPENDENCY_LAYER];
    public fixed int32_t iScaledHeight[WelsEnc.MAX_DEPENDENCY_LAYER];
}

/// <summary>TagVAAFrameInfo.</summary>
internal unsafe struct SVAAFrameInfo
{
    public SVAACalcResult sVaaCalcInfo;
    public SAdaptiveQuantizationParam sAdaptiveQuantParam;
    public SComplexityAnalysisParam sComplexityAnalysisParam;

    public int32_t iPicWidth;          // maximal iWidth of picture in samples for svc coding
    public int32_t iPicHeight;         // maximal iHeight of picture in samples for svc coding
    public int32_t iPicStride;         //luma
    public int32_t iPicStrideUV;

    public uint8_t* pRefY; //pRef
    public uint8_t* pCurY; //cur
    public uint8_t* pRefU; //pRef
    public uint8_t* pCurU; //cur
    public uint8_t* pRefV; //pRef
    public uint8_t* pCurV; //cur

    public int8_t* pVaaBackgroundMbFlag;
    public uint8_t uiValidLongTermPicIdx;
    public uint8_t uiMarkLongTermPicIdx;

    public ESceneChangeIdc eSceneChangeIdc;
    public bool bSceneChangeFlag;
    public bool bIdrPeriodFlag;
}

/// <summary>Port addition: which class derived from CWelsPreProcess a CWelsPreProcess is.</summary>
internal enum EWelsPreProcessClass
{
    CWelsPreProcessVideo,
    CWelsPreProcessScreen   // Phase 4
}

internal static unsafe partial class WelsEnc
{
    // ---- anon ns ----

    private static void ClearEndOfLinePadding(uint8_t* pData, int32_t iStride, int32_t iWidth, int32_t iHeight)
    {
        if (iWidth < iStride)
        {
            for (int32_t i = 0; i < iHeight; ++i)
                memset(pData + i * iStride + iWidth, 0, iStride - iWidth);
        }
    }

    //******* table definition ***********************************************************************//
    // const uint8_t g_kuiRefTemporalIdx[MAX_TEMPORAL_LEVEL][MAX_GOP_SIZE], flattened.
    public static ReadOnlySpan<uint8_t> g_kuiRefTemporalIdx =>
    [
        0, 0, 0, 0, 0, 0, 0, 0, // 0
        0, 0, 0, 0, 0, 0, 0, 0, // 1
        0, 0, 0, 1, 0, 0, 0, 0, // 2
        0, 0, 0, 2, 0, 1, 1, 2, // 3
    ];

    public const int32_t g_kiPixMapSizeInBits = sizeof(uint8_t) * 8;

    public static void WelsUpdateSpatialIdxMap(sWelsEncCtx* pEncCtx, int32_t iPos, SPicture* pPic, int32_t iDidx)
    {
        pEncCtx->sSpatialIndexMap[iPos].pSrc = pPic;
        pEncCtx->sSpatialIndexMap[iPos].iDid = iDidx;
    }

    /*!
     * \brief   Whether input picture need be scaled?
     */
    public static bool JudgeNeedOfScaling(SWelsSvcCodingParam* pParam, Scaled_Picture* pScaledPicture)
    {
        int32_t kiInputPicWidth = pParam->SUsedPicRect.iWidth;
        int32_t kiInputPicHeight = pParam->SUsedPicRect.iHeight;
        int32_t kiDstPicWidth = pParam->sDependencyLayers[pParam->iSpatialLayerNum - 1].iActualWidth;
        int32_t kiDstPicHeight = pParam->sDependencyLayers[pParam->iSpatialLayerNum - 1].iActualHeight;
        bool bNeedDownsampling = true;

        int32_t iSpatialIdx = pParam->iSpatialLayerNum - 1;

        if (kiDstPicWidth >= kiInputPicWidth && kiDstPicHeight >= kiInputPicHeight)
        {
            bNeedDownsampling = false;
        }

        for (; iSpatialIdx >= 0; iSpatialIdx--)
        {
            SSpatialLayerInternal* pCurLayer = &pParam->sDependencyLayers[iSpatialIdx];
            int32_t iCurDstWidth = pCurLayer->iActualWidth;
            int32_t iCurDstHeight = pCurLayer->iActualHeight;
            int32_t iInputWidthXDstHeight = kiInputPicWidth * iCurDstHeight;
            int32_t iInputHeightXDstWidth = kiInputPicHeight * iCurDstWidth;

            if (iInputWidthXDstHeight > iInputHeightXDstWidth)
            {
                pScaledPicture->iScaledWidth[iSpatialIdx] = WELS_MAX(iCurDstWidth, 4);
                pScaledPicture->iScaledHeight[iSpatialIdx] = WELS_MAX(iInputHeightXDstWidth / kiInputPicWidth, 4);
            }
            else
            {
                pScaledPicture->iScaledWidth[iSpatialIdx] = WELS_MAX(iInputWidthXDstHeight / kiInputPicHeight, 4);
                pScaledPicture->iScaledHeight[iSpatialIdx] = WELS_MAX(iCurDstHeight, 4);
            }
        }

        return bNeedDownsampling;
    }

    public static int32_t WelsInitScaledPic(SWelsSvcCodingParam* pParam, Scaled_Picture* pScaledPicture, CMemoryAlign* pMemoryAlign)
    {
        bool bInputPicNeedScaling = JudgeNeedOfScaling(pParam, pScaledPicture);
        if (bInputPicNeedScaling)
        {
            pScaledPicture->pScaledInputPicture = AllocPicture(pMemoryAlign, pParam->SUsedPicRect.iWidth,
                                                  pParam->SUsedPicRect.iHeight, false, 0);
            if (pScaledPicture->pScaledInputPicture == null)
                return -1;

            // Avoid valgrind false positives.
            //
            // X86 SIMD downsampling routines may, for convenience, read slightly beyond
            // the input data and into the alignment padding area beyond each line. This
            // causes valgrind to warn about uninitialized values even if these values
            // only affect lanes of a SIMD vector that are effectively never used.
            //
            // Avoid these false positives by zero-initializing the padding area beyond
            // each line of the source buffer used for downsampling.
            SPicture* pPic = pScaledPicture->pScaledInputPicture;
            ClearEndOfLinePadding(pPic->pData[0], pPic->iLineSize[0], pPic->iWidthInPixel, pPic->iHeightInPixel);
            ClearEndOfLinePadding(pPic->pData[1], pPic->iLineSize[1], pPic->iWidthInPixel >> 1, pPic->iHeightInPixel >> 1);
            ClearEndOfLinePadding(pPic->pData[2], pPic->iLineSize[2], pPic->iWidthInPixel >> 1, pPic->iHeightInPixel >> 1);
        }
        return 0;
    }

    public static void FreeScaledPic(Scaled_Picture* pScaledPicture, CMemoryAlign* pMemoryAlign)
    {
        if (pScaledPicture->pScaledInputPicture != null)
        {
            FreePicture(pMemoryAlign, &pScaledPicture->pScaledInputPicture);
            pScaledPicture->pScaledInputPicture = null;
        }
    }

    //TODO: may opti later
    //TODO: not use this func?
    public static void* WelsMemcpy(void* dst, void* kpSrc, uint32_t uiSize)
    {
        memcpy(dst, kpSrc, (nint)uiSize);
        return dst;
    }

    public static void* WelsMemset(void* p, int32_t val, uint32_t uiSize)
    {
        memset(p, val, (nint)uiSize);
        return p;
    }

    //i420_to_i420_c
    public static void WelsMoveMemory_c(uint8_t* pDstY, uint8_t* pDstU, uint8_t* pDstV,
                                        int32_t iDstStrideY, int32_t iDstStrideU,
                                        int32_t iDstStrideV, uint8_t* pSrcY, uint8_t* pSrcU,
                                        uint8_t* pSrcV, int32_t iSrcStrideY, int32_t iSrcStrideU,
                                        int32_t iSrcStrideV, int32_t iWidth, int32_t iHeight)
    {
        int32_t iWidth2 = iWidth >> 1;
        int32_t iHeight2 = iHeight >> 1;
        int32_t j;

        for (j = iHeight; j != 0; j--)
        {
            WelsMemcpy(pDstY, pSrcY, (uint32_t)iWidth);
            pDstY += iDstStrideY;
            pSrcY += iSrcStrideY;
        }

        for (j = iHeight2; j != 0; j--)
        {
            WelsMemcpy(pDstU, pSrcU, (uint32_t)iWidth2);
            WelsMemcpy(pDstV, pSrcV, (uint32_t)iWidth2);
            pDstU += iDstStrideU;
            pDstV += iDstStrideV;
            pSrcU += iSrcStrideU;
            pSrcV += iSrcStrideV;
        }
    }
}

/// <summary>CWelsPreProcess, with CWelsPreProcessVideo (see the file header).</summary>
internal unsafe struct CWelsPreProcess
{
    public EWelsPreProcessClass m_eClass;

    // protected
    private CVpFrameWork* m_pInterfaceVp;  // IWelsVP*
    private sWelsEncCtx* m_pEncCtx;
    private fixed uint8_t m_uiSpatialLayersInTemporal[WelsEnc.MAX_DEPENDENCY_LAYER];

    // private
    private Scaled_Picture m_sScaledPicture;
    private InlineArray4<PtrArray2<SPicture>> m_pLastSpatialPicture;   // [MAX_DEPENDENCY_LAYER][2]
    private bool m_bInitDone;
    private fixed uint8_t m_uiSpatialPicNum[WelsEnc.MAX_DEPENDENCY_LAYER];

    // protected
    /* For Downsampling & VAA I420 based source pictures */
    private InlineArray4<PtrArray17<SPicture>> m_pSpatialPic;          // [MAX_DEPENDENCY_LAYER][MAX_REF_PIC_COUNT + 1]
    // need memory requirement with total number of num_of_ref + 1, "+1" is for current frame
    private int32_t m_iAvaliableRefInSpatialPicList;

    /***************************************************************************
    *
    *   implement of the interface
    *
    ***************************************************************************/

    public static CWelsPreProcess* CreatePreProcess(sWelsEncCtx* pEncCtx)
    {
        CWelsPreProcess* pPreProcess = null;
        switch (pEncCtx->pSvcParam->iUsageType)
        {
            case SCREEN_CONTENT_REAL_TIME:
                // WELS_NEW_OP (CWelsPreProcessScreen (pEncCtx), CWelsPreProcessScreen)
                throw new NotSupportedException("CWelsPreProcessScreen: screen content comes with Phase 4.");
            default:
                // WELS_NEW_OP (CWelsPreProcessVideo (pEncCtx), CWelsPreProcessVideo)
                pPreProcess = (CWelsPreProcess*)NativeMemory.AllocZeroed((nuint)sizeof(CWelsPreProcess));
                if (pPreProcess != null)
                {
                    pPreProcess->CWelsPreProcess_ctor(pEncCtx, EWelsPreProcessClass.CWelsPreProcessVideo);
                }
                break;
        }
        if (pPreProcess == null)    // WELS_VERIFY_RETURN_IF (NULL, NULL == pPreProcess)
            return null;
        return pPreProcess;
    }

    /// <summary>Port addition: WELS_DELETE_OP (pPreProcess).</summary>
    public static void DeletePreProcess(ref CWelsPreProcess* pPreProcess)
    {
        if (pPreProcess != null)
        {
            pPreProcess->CWelsPreProcess_dtor();
            NativeMemory.Free(pPreProcess);
            pPreProcess = null;
        }
    }

    private void CWelsPreProcess_ctor(sWelsEncCtx* pEncCtx, EWelsPreProcessClass eClass)
    {
        m_eClass = eClass;
        m_pInterfaceVp = null;
        m_bInitDone = false;
        m_pEncCtx = pEncCtx;
        m_sScaledPicture = default;
        m_pSpatialPic = default;
        for (int32_t i = 0; i < WelsEnc.MAX_DEPENDENCY_LAYER; i++)
        {
            m_uiSpatialLayersInTemporal[i] = 0;
            m_uiSpatialPicNum[i] = 0;
        }
    }

    private void CWelsPreProcess_dtor()
    {
        fixed (Scaled_Picture* pScaledPicture = &m_sScaledPicture)
        {
            WelsEnc.FreeScaledPic(pScaledPicture, m_pEncCtx->pMemAlign);
        }
        WelsPreprocessDestroy();
    }

    // virtual SPicture* GetCurrentOrigFrame (int32_t iDIdx) = 0;
    public SPicture* GetCurrentOrigFrame(int32_t iDIdx) => m_eClass switch
    {
        EWelsPreProcessClass.CWelsPreProcessVideo => m_pSpatialPic[iDIdx][GetCurPicPosition(iDIdx)],
        _ => throw NotPorted(),
    };

    // virtual ESceneChangeIdc DetectSceneChange (SPicture* pCurPicture, SPicture* pRefPicture = NULL) = 0;
    private ESceneChangeIdc DetectSceneChange(SPicture* pCurPicture, SPicture* pRefPicture = null) => m_eClass switch
    {
        EWelsPreProcessClass.CWelsPreProcessVideo => DetectSceneChangeVideo(pCurPicture, pRefPicture),
        _ => throw NotPorted(),
    };

    private static NotSupportedException NotPorted() =>
        new NotSupportedException("CWelsPreProcessScreen: screen content comes with Phase 4.");

    private int32_t WelsPreprocessCreate()
    {
        if (m_pInterfaceVp == null)
        {
            CVpFrameWork* pInterfaceVp = null;
            WelsVP.WelsCreateVpInterface(&pInterfaceVp, WelsVP.WELSVP_INTERFACE_VERION);
            m_pInterfaceVp = pInterfaceVp;
            if (m_pInterfaceVp == null)
                goto exit;
        }
        else
            goto exit;

        return 0;

    exit:
        WelsPreprocessDestroy();
        return 1;
    }

    private int32_t WelsPreprocessDestroy()
    {
        WelsVP.WelsDestroyVpInterface(m_pInterfaceVp, WelsVP.WELSVP_INTERFACE_VERION);
        m_pInterfaceVp = null;

        return 0;
    }

    public int32_t WelsPreprocessReset(sWelsEncCtx* pCtx, int32_t iWidth, int32_t iHeight)
    {
        int32_t iRet = -1;
        SWelsSvcCodingParam* pSvcParam = pCtx->pSvcParam;
        //init source width and height
        pSvcParam->SUsedPicRect.iLeft = 0;
        pSvcParam->SUsedPicRect.iTop = 0;
        pSvcParam->SUsedPicRect.iWidth = iWidth;
        pSvcParam->SUsedPicRect.iHeight = iHeight;
        if ((iWidth < 16) || ((iHeight < 16)))
        {
            WelsLog(&(pCtx->sLogCtx), WELS_LOG_ERROR, $"Don't support width({iWidth}) or height({iHeight}) which is less than 16 ");
            return iRet;
        }
        if (pCtx != null)
        {
            fixed (Scaled_Picture* pScaledPicture = &m_sScaledPicture)
            {
                WelsEnc.FreeScaledPic(pScaledPicture, pCtx->pMemAlign);
                iRet = InitLastSpatialPictures(pCtx);
                iRet = WelsEnc.WelsInitScaledPic(pCtx->pSvcParam, pScaledPicture, pCtx->pMemAlign);
            }
        }

        return iRet;
    }

    public int32_t AllocSpatialPictures(sWelsEncCtx* pCtx, SWelsSvcCodingParam* pParam)
    {
        CMemoryAlign* pMa = pCtx->pMemAlign;
        int32_t kiDlayerCount = pParam->iSpatialLayerNum;
        int32_t iDlayerIndex = 0;

        // spatial pictures
        iDlayerIndex = 0;
        do
        {
            int32_t kiPicWidth = pParam->sSpatialLayers[iDlayerIndex].iVideoWidth;
            int32_t kiPicHeight = pParam->sSpatialLayers[iDlayerIndex].iVideoHeight;
            uint8_t kuiLayerInTemporal = (uint8_t)(2 + WELS_MAX((int32_t)pParam->sDependencyLayers[iDlayerIndex].iHighestTemporalId, 1));
            uint8_t kuiRefNumInTemporal = (uint8_t)(kuiLayerInTemporal + pParam->iLTRRefNum);
            uint8_t i = 0;

            m_uiSpatialPicNum[iDlayerIndex] = kuiRefNumInTemporal;
            do
            {
                SPicture* pPic = WelsEnc.AllocPicture(pMa, kiPicWidth, kiPicHeight, false, 0);
                if (null == pPic)   // WELS_VERIFY_RETURN_IF (1, (NULL == pPic))
                    return 1;
                m_pSpatialPic[iDlayerIndex][i] = pPic;
                ++i;
            } while (i < kuiRefNumInTemporal);

            if (pParam->iUsageType == SCREEN_CONTENT_REAL_TIME)
                m_uiSpatialLayersInTemporal[iDlayerIndex] = 1;
            else
                m_uiSpatialLayersInTemporal[iDlayerIndex] = kuiLayerInTemporal;

            ++iDlayerIndex;
        } while (iDlayerIndex < kiDlayerCount);

        return 0;
    }

    public void FreeSpatialPictures(sWelsEncCtx* pCtx)
    {
        CMemoryAlign* pMa = pCtx->pMemAlign;
        int32_t j = 0;
        while (j < pCtx->pSvcParam->iSpatialLayerNum)
        {
            uint8_t i = 0;
            uint8_t uiRefNumInTemporal = m_uiSpatialPicNum[j];

            while (i < uiRefNumInTemporal)
            {
                if (null != (SPicture*)m_pSpatialPic[j][i])
                {
                    SPicture* pPic = m_pSpatialPic[j][i];
                    WelsEnc.FreePicture(pMa, &pPic);
                    m_pSpatialPic[j][i] = pPic;
                }
                ++i;
            }
            m_uiSpatialLayersInTemporal[j] = 0;
            ++j;
        }
    }

    public int32_t BuildSpatialPicList(sWelsEncCtx* pCtx, SSourcePicture* kpSrcPic, int32_t* pSpatialNum)
    {
        SWelsSvcCodingParam* pSvcParam = pCtx->pSvcParam;
        int32_t iWidth = ((kpSrcPic->iPicWidth >> 1) << 1);
        int32_t iHeight = ((kpSrcPic->iPicHeight >> 1) << 1);
        *pSpatialNum = 0;

        if (!m_bInitDone)
        {
            if (WelsPreprocessCreate() != 0)
                return ENC_RETURN_MEMALLOCERR;

            if (WelsPreprocessReset(pCtx, iWidth, iHeight) != 0)
                return ENC_RETURN_MEMALLOCERR;

            m_iAvaliableRefInSpatialPicList = pSvcParam->iNumRefFrame;

            m_bInitDone = true;
        }
        else
        {
            if ((iWidth != pSvcParam->SUsedPicRect.iWidth) || (iHeight != pSvcParam->SUsedPicRect.iHeight))
            {
                if (WelsPreprocessReset(pCtx, iWidth, iHeight) != 0)
                    return ENC_RETURN_MEMALLOCERR;
            }
        }

        if (m_pInterfaceVp == null)
            return ENC_RETURN_MEMALLOCERR;

        pCtx->pVaa->bSceneChangeFlag = pCtx->pVaa->bIdrPeriodFlag = false;

        int32_t iRet;
        fixed (Scaled_Picture* pScaledPicture = &m_sScaledPicture)
        {
            iRet = SingleLayerPreprocess(pCtx, kpSrcPic, pScaledPicture, pSpatialNum);
        }
        if (iRet != ENC_RETURN_SUCCESS)
        {
            return iRet;
        }

        return ENC_RETURN_SUCCESS;
    }

    // SPicture* GetBestRefPic (EUsageType iUsageType, bool bSceneLtr, EWelsSliceType eSliceType,
    //     int32_t kiDidx, int32_t iRefTemporalIdx): screen content (SVAAFrameInfoExt), Phase 4.

    private SPicture* GetBestRefPic(int32_t kiDidx, int32_t iRefTemporalIdx)
    {
        return m_pSpatialPic[kiDidx][iRefTemporalIdx];
    }

    public int32_t AnalyzeSpatialPic(sWelsEncCtx* pCtx, int32_t kiDidx)
    {
        SWelsSvcCodingParam* pSvcParam = pCtx->pSvcParam;
        bool bNeededMbAq = (pSvcParam->bEnableAdaptiveQuant && (pCtx->eSliceType == EWelsSliceType.P_SLICE));
        bool bCalculateBGD = (pCtx->eSliceType == EWelsSliceType.P_SLICE && pSvcParam->bEnableBackgroundDetection);
        SSpatialLayerInternal* pParamInternal = &pSvcParam->sDependencyLayers[kiDidx];
        int32_t iCurTemporalIdx = m_uiSpatialLayersInTemporal[kiDidx] - 1;

        int32_t iRefTemporalIdx = (int32_t)WelsEnc.g_kuiRefTemporalIdx[pSvcParam->iDecompStages * WelsEnc.MAX_GOP_SIZE
                                  + (int32_t)((uint32_t)pParamInternal->iCodingIndex & (pSvcParam->uiGopSize - 1))];
        if (pCtx->uiTemporalId == 0 && pCtx->pLtr[pCtx->uiDependencyId].bReceivedT0LostFlag)
            iRefTemporalIdx = m_uiSpatialLayersInTemporal[kiDidx] + pCtx->pVaa->uiValidLongTermPicIdx;

        SPicture* pCurPic = m_pSpatialPic[kiDidx][iCurTemporalIdx];
        bool bCalculateVar = (pSvcParam->iRCMode >= RC_BITRATE_MODE && pCtx->eSliceType == EWelsSliceType.I_SLICE);

        if (pSvcParam->iUsageType == SCREEN_CONTENT_REAL_TIME)
        {
            // GetBestRefPic (pSvcParam->iUsageType, ...), then the VAA, the background detection
            // and the adaptive quantisation against it: screen content, Phase 4.
            throw NotPorted();
        }
        else
        {
            SPicture* pRefPic = GetBestRefPic(kiDidx, iRefTemporalIdx);
            SPicture* pLastPic = m_pLastSpatialPicture[kiDidx][0];
            bool bCalculateSQDiff = (((uint8_t*)pLastPic->pData[0] == (uint8_t*)pRefPic->pData[0]) && bNeededMbAq);

            VaaCalculation(pCtx->pVaa, pCurPic, pRefPic, bCalculateSQDiff, bCalculateVar, bCalculateBGD);

            if (pSvcParam->bEnableBackgroundDetection)
            {
                BackgroundDetection(pCtx->pVaa, pCurPic, pRefPic, bCalculateBGD && pRefPic->iPictureType != (int32_t)EWelsSliceType.I_SLICE);
            }

            if (bNeededMbAq)
            {
                AdaptiveQuantCalculation(pCtx->pVaa, m_pLastSpatialPicture[kiDidx][1], m_pLastSpatialPicture[kiDidx][0]);
            }
        }
        return 0;
    }

    private int32_t GetCurPicPosition(int32_t kiDidx)
    {
        return (m_uiSpatialLayersInTemporal[kiDidx] - 1);
    }

    public int32_t UpdateSpatialPictures(sWelsEncCtx* pCtx, SWelsSvcCodingParam* pParam, int8_t iCurTid, int32_t kiDidx)
    {
        if (pCtx->pSvcParam->iUsageType == SCREEN_CONTENT_REAL_TIME)
            return 0;

        WelsExchangeSpatialPictures(ref m_pLastSpatialPicture[kiDidx][1], ref m_pLastSpatialPicture[kiDidx][0]);

        int32_t kiCurPos = GetCurPicPosition(kiDidx);
        if (iCurTid < kiCurPos || pParam->iDecompStages == 0)
        {
            if ((iCurTid >= MAX_TEMPORAL_LEVEL) || (kiCurPos > MAX_TEMPORAL_LEVEL))
            {
                InitLastSpatialPictures(pCtx);
                return 1;
            }
            if (pCtx->bRefOfCurTidIsLtr[kiDidx * MAX_TEMPORAL_LEVEL + iCurTid])
            {
                int32_t kiAvailableLtrPos = m_uiSpatialLayersInTemporal[kiDidx] + pCtx->pVaa->uiMarkLongTermPicIdx;
                WelsExchangeSpatialPictures(ref m_pSpatialPic[kiDidx][kiAvailableLtrPos],
                                            ref m_pSpatialPic[kiDidx][iCurTid]);
                pCtx->bRefOfCurTidIsLtr[kiDidx * MAX_TEMPORAL_LEVEL + iCurTid] = false;
            }
            WelsExchangeSpatialPictures(ref m_pSpatialPic[kiDidx][kiCurPos],
                                        ref m_pSpatialPic[kiDidx][iCurTid]);
        }
        return 0;
    }

    /*
     *   SingleLayerPreprocess: down sampling if applicable
     *  @return: exact number of spatial layers need to encoder indeed
     */
    private int32_t SingleLayerPreprocess(sWelsEncCtx* pCtx, SSourcePicture* kpSrc,
                                          Scaled_Picture* pScaledPicture, int32_t* pSpatialNum)
    {
        SWelsSvcCodingParam* pSvcParam = pCtx->pSvcParam;
        int8_t iDependencyId = (int8_t)(pSvcParam->iSpatialLayerNum - 1);

        SPicture* pSrcPic = null; // large
        SPicture* pDstPic = null; // small
        SSpatialLayerConfig* pDlayerParam = null;
        SSpatialLayerInternal* pDlayerParamInternal = null;
        int32_t iSpatialNum = 0;
        int32_t iSrcWidth = 0;
        int32_t iSrcHeight = 0;
        int32_t iTargetWidth = 0;
        int32_t iTargetHeight = 0;
        int32_t iTemporalId = 0;
        int32_t iClosestDid = iDependencyId;
        pDlayerParamInternal = &pSvcParam->sDependencyLayers[iDependencyId];
        pDlayerParam = &pSvcParam->sSpatialLayers[iDependencyId];
        iTargetWidth = pDlayerParam->iVideoWidth;
        iTargetHeight = pDlayerParam->iVideoHeight;

        iSrcWidth = pSvcParam->SUsedPicRect.iWidth;
        iSrcHeight = pSvcParam->SUsedPicRect.iHeight;
        if (pSvcParam->uiIntraPeriod != 0)
        {
            pCtx->pVaa->bIdrPeriodFlag = (1 + pDlayerParamInternal->iFrameIndex >= (int32_t)pSvcParam->uiIntraPeriod) ? true :
                                         false;
            if (pCtx->pVaa->bIdrPeriodFlag)
            {
                WelsLog(&(pCtx->sLogCtx), WELS_LOG_DEBUG,
                        $"pSvcParam->uiIntraPeriod={pSvcParam->uiIntraPeriod}, pCtx->pVaa->bIdrPeriodFlag={(pCtx->pVaa->bIdrPeriodFlag ? 1 : 0)}");
            }
        }

        *pSpatialNum = 0;
        pSrcPic = pScaledPicture->pScaledInputPicture != null ? pScaledPicture->pScaledInputPicture : GetCurrentOrigFrame(
                      iDependencyId);
        int32_t iRet = WelsMoveMemoryWrapper(pSvcParam, pSrcPic, kpSrc, iSrcWidth, iSrcHeight);
        if (iRet != ENC_RETURN_SUCCESS)
        {
            return iRet;
        }

        if (pSvcParam->bEnableDenoise)
            BilateralDenoising(pSrcPic, iSrcWidth, iSrcHeight);

        // different scaling in between input picture and dst highest spatial picture.
        int32_t iShrinkWidth = iSrcWidth;
        int32_t iShrinkHeight = iSrcHeight;
        pDstPic = pSrcPic;
        if (pScaledPicture->pScaledInputPicture != null)
        {
            // for highest downsampling
            pDstPic = GetCurrentOrigFrame(iDependencyId);
            iShrinkWidth = pScaledPicture->iScaledWidth[iDependencyId];
            iShrinkHeight = pScaledPicture->iScaledHeight[iDependencyId];
        }
        DownsamplePadding(pSrcPic, pDstPic, iSrcWidth, iSrcHeight, iShrinkWidth, iShrinkHeight, iTargetWidth, iTargetHeight,
                          false);

        if (pSvcParam->bEnableSceneChangeDetect && !pCtx->pVaa->bIdrPeriodFlag)
        {
            if (pSvcParam->iUsageType == SCREEN_CONTENT_REAL_TIME)
            {
                pCtx->pVaa->eSceneChangeIdc = (pDlayerParamInternal->bEncCurFrmAsIdrFlag ? ESceneChangeIdc.LARGE_CHANGED_SCENE :
                                               DetectSceneChange(pDstPic));
                pCtx->pVaa->bSceneChangeFlag = (ESceneChangeIdc.LARGE_CHANGED_SCENE == pCtx->pVaa->eSceneChangeIdc);
            }
            else
            {
                if ((!pDlayerParamInternal->bEncCurFrmAsIdrFlag)
                    && !(((uint32_t)pDlayerParamInternal->iCodingIndex & (pSvcParam->uiGopSize - 1)) != 0))
                {
                    SPicture* pRefPic = pCtx->pLtr[iDependencyId].bReceivedT0LostFlag ?
                                        m_pSpatialPic[iDependencyId][m_uiSpatialLayersInTemporal[iDependencyId] +
                                            pCtx->pVaa->uiValidLongTermPicIdx] : m_pLastSpatialPicture[iDependencyId][0];
                    //pCtx->pVaa->eSceneChangeIdc = DetectSceneChange (pDstPic, pRefPic);
                    pCtx->pVaa->bSceneChangeFlag = GetSceneChangeFlag(DetectSceneChange(pDstPic, pRefPic));
                }
            }
        }

        for (int32_t i = 0; i < pSvcParam->iSpatialLayerNum; i++)
        {
            pDlayerParamInternal = &pSvcParam->sDependencyLayers[i];
            iTemporalId = pDlayerParamInternal->uiCodingIdx2TemporalId[(int32_t)((uint32_t)pDlayerParamInternal->iCodingIndex &
                          (pSvcParam->uiGopSize - 1))];
            if (iTemporalId != INVALID_TEMPORAL_ID)
            {
                ++iSpatialNum;
            }
        }
        pDlayerParamInternal = &pSvcParam->sDependencyLayers[iDependencyId];
        iTemporalId = pDlayerParamInternal->uiCodingIdx2TemporalId[(int32_t)((uint32_t)pDlayerParamInternal->iCodingIndex &
                      (pSvcParam->uiGopSize - 1))];
        int iActualSpatialNum = iSpatialNum - 1;
        if (iTemporalId != INVALID_TEMPORAL_ID)
        {
            WelsEnc.WelsUpdateSpatialIdxMap(pCtx, iActualSpatialNum, pDstPic, iDependencyId);
            --iActualSpatialNum;
        }

        m_pLastSpatialPicture[iDependencyId][1] = GetCurrentOrigFrame(iDependencyId);
        --iDependencyId;


        // generate other spacial layer
        // pSrc is
        //    -- padded input pic, if downsample should be applied to generate highest layer, [if] block above
        //    -- highest layer, if no downsampling, [else] block above
        if (pSvcParam->iSpatialLayerNum > 1)
        {
            while (iDependencyId >= 0)
            {
                pDlayerParamInternal = &pSvcParam->sDependencyLayers[iDependencyId];
                pDlayerParam = &pSvcParam->sSpatialLayers[iDependencyId];
                SPicture* pLayerSrcPic = m_pLastSpatialPicture[iClosestDid][1]; // large
                iTargetWidth = pDlayerParam->iVideoWidth;
                iTargetHeight = pDlayerParam->iVideoHeight;
                iTemporalId = pDlayerParamInternal->uiCodingIdx2TemporalId[(int32_t)((uint32_t)pDlayerParamInternal->iCodingIndex &
                              (pSvcParam->uiGopSize - 1))];

                // down sampling performed
                int32_t iLayerSrcWidth = pScaledPicture->iScaledWidth[iClosestDid];
                int32_t iLayerSrcHeight = pScaledPicture->iScaledHeight[iClosestDid];
                pDstPic = GetCurrentOrigFrame(iDependencyId); // small
                iShrinkWidth = pScaledPicture->iScaledWidth[iDependencyId];
                iShrinkHeight = pScaledPicture->iScaledHeight[iDependencyId];
                DownsamplePadding(pLayerSrcPic, pDstPic, iLayerSrcWidth, iLayerSrcHeight, iShrinkWidth, iShrinkHeight, iTargetWidth,
                                  iTargetHeight, true);

                if ((iTemporalId != INVALID_TEMPORAL_ID))
                {
                    WelsEnc.WelsUpdateSpatialIdxMap(pCtx, iActualSpatialNum, pDstPic, iDependencyId);
                    iActualSpatialNum--;
                }

                m_pLastSpatialPicture[iDependencyId][1] = pDstPic;

                iClosestDid = iDependencyId;
                --iDependencyId;
            }
        }
        *pSpatialNum = iSpatialNum;
        return ENC_RETURN_SUCCESS;
    }

    private int32_t InitLastSpatialPictures(sWelsEncCtx* pCtx)
    {
        SWelsSvcCodingParam* pParam = pCtx->pSvcParam;
        int32_t kiDlayerCount = pParam->iSpatialLayerNum;
        int32_t iDlayerIndex = 0;
        if (pParam->iUsageType == SCREEN_CONTENT_REAL_TIME)
        {
            for (; iDlayerIndex < MAX_DEPENDENCY_LAYER; iDlayerIndex++)
            {
                m_pLastSpatialPicture[iDlayerIndex][0] = m_pLastSpatialPicture[iDlayerIndex][1] = (SPicture*)null;
            }
        }
        else
        {
            for (; iDlayerIndex < kiDlayerCount; iDlayerIndex++)
            {
                int32_t kiLayerInTemporal = m_uiSpatialLayersInTemporal[iDlayerIndex];
                m_pLastSpatialPicture[iDlayerIndex][0] = m_pSpatialPic[iDlayerIndex][kiLayerInTemporal - 2];
                m_pLastSpatialPicture[iDlayerIndex][1] = (SPicture*)null;
            }
            for (; iDlayerIndex < MAX_DEPENDENCY_LAYER; iDlayerIndex++)
            {
                m_pLastSpatialPicture[iDlayerIndex][0] = m_pLastSpatialPicture[iDlayerIndex][1] = (SPicture*)null;
            }
        }
        return 0;
    }
    //*********************************************************************************************************/

    private int32_t ColorspaceConvert(SWelsSvcCodingParam* pSvcParam, SPicture* pDstPic,
                                      SSourcePicture* kpSrc, int32_t kiWidth, int32_t kiHeight)
    {
        return 1;
        //not support yet
    }

    private void BilateralDenoising(SPicture* pSrc, int32_t kiWidth, int32_t kiHeight)
    {
        int32_t iMethodIdx = (int32_t)EMethods.METHOD_DENOISE;
        SPixMap sSrcPixMap = default;
        sSrcPixMap.pPixel[0] = pSrc->pData[0];
        sSrcPixMap.pPixel[1] = pSrc->pData[1];
        sSrcPixMap.pPixel[2] = pSrc->pData[2];
        sSrcPixMap.iSizeInBits = WelsEnc.g_kiPixMapSizeInBits;
        sSrcPixMap.sRect.iRectWidth = kiWidth;
        sSrcPixMap.sRect.iRectHeight = kiHeight;
        sSrcPixMap.iStride[0] = pSrc->iLineSize[0];
        sSrcPixMap.iStride[1] = pSrc->iLineSize[1];
        sSrcPixMap.iStride[2] = pSrc->iLineSize[2];
        sSrcPixMap.eFormat = EVideoFormat.VIDEO_FORMAT_I420;

        m_pInterfaceVp->Process(iMethodIdx, &sSrcPixMap, null);
    }

    /// <summary>CWelsPreProcessVideo::DetectSceneChange.</summary>
    private ESceneChangeIdc DetectSceneChangeVideo(SPicture* pCurPicture, SPicture* pRefPicture)
    {
        int32_t iMethodIdx = (int32_t)EMethods.METHOD_SCENE_CHANGE_DETECTION_VIDEO;
        SSceneChangeResult sSceneChangeDetectResult = default;
        sSceneChangeDetectResult.eSceneChangeIdc = ESceneChangeIdc.SIMILAR_SCENE;
        SPixMap sSrcPixMap = default;
        SPixMap sRefPixMap = default;
        sSrcPixMap.pPixel[0] = pCurPicture->pData[0];
        sSrcPixMap.iSizeInBits = WelsEnc.g_kiPixMapSizeInBits;
        sSrcPixMap.iStride[0] = pCurPicture->iLineSize[0];
        sSrcPixMap.sRect.iRectWidth = pCurPicture->iWidthInPixel;
        sSrcPixMap.sRect.iRectHeight = pCurPicture->iHeightInPixel;
        sSrcPixMap.eFormat = EVideoFormat.VIDEO_FORMAT_I420;

        sRefPixMap.pPixel[0] = pRefPicture->pData[0];
        sRefPixMap.iSizeInBits = WelsEnc.g_kiPixMapSizeInBits;
        sRefPixMap.iStride[0] = pRefPicture->iLineSize[0];
        sRefPixMap.sRect.iRectWidth = pRefPicture->iWidthInPixel;
        sRefPixMap.sRect.iRectHeight = pRefPicture->iHeightInPixel;
        sRefPixMap.eFormat = EVideoFormat.VIDEO_FORMAT_I420;

        int32_t iRet = (int32_t)m_pInterfaceVp->Process(iMethodIdx, &sSrcPixMap, &sRefPixMap);
        if (iRet == 0)
        {
            m_pInterfaceVp->Get(iMethodIdx, (void*)&sSceneChangeDetectResult);
            //bSceneChangeFlag = (sSceneChangeDetectResult.eSceneChangeIdc == LARGE_CHANGED_SCENE) ? true : false;
        }
        return sSceneChangeDetectResult.eSceneChangeIdc;
    }

    private int32_t DownsamplePadding(SPicture* pSrc, SPicture* pDstPic, int32_t iSrcWidth, int32_t iSrcHeight,
                                      int32_t iShrinkWidth, int32_t iShrinkHeight, int32_t iTargetWidth, int32_t iTargetHeight,
                                      bool bForceCopy)
    {
        int32_t iRet = 0;
        SPixMap sSrcPixMap = default;
        SPixMap sDstPicMap = default;
        sSrcPixMap.pPixel[0] = pSrc->pData[0];
        sSrcPixMap.pPixel[1] = pSrc->pData[1];
        sSrcPixMap.pPixel[2] = pSrc->pData[2];
        sSrcPixMap.iSizeInBits = WelsEnc.g_kiPixMapSizeInBits;
        sSrcPixMap.sRect.iRectWidth = iSrcWidth;
        sSrcPixMap.sRect.iRectHeight = iSrcHeight;
        sSrcPixMap.iStride[0] = pSrc->iLineSize[0];
        sSrcPixMap.iStride[1] = pSrc->iLineSize[1];
        sSrcPixMap.iStride[2] = pSrc->iLineSize[2];
        sSrcPixMap.eFormat = EVideoFormat.VIDEO_FORMAT_I420;

        if (iSrcWidth != iShrinkWidth || iSrcHeight != iShrinkHeight || bForceCopy)
        {
            int32_t iMethodIdx = (int32_t)EMethods.METHOD_DOWNSAMPLE;
            sDstPicMap.pPixel[0] = pDstPic->pData[0];
            sDstPicMap.pPixel[1] = pDstPic->pData[1];
            sDstPicMap.pPixel[2] = pDstPic->pData[2];
            sDstPicMap.iSizeInBits = WelsEnc.g_kiPixMapSizeInBits;
            sDstPicMap.sRect.iRectWidth = iShrinkWidth;
            sDstPicMap.sRect.iRectHeight = iShrinkHeight;
            sDstPicMap.iStride[0] = pDstPic->iLineSize[0];
            sDstPicMap.iStride[1] = pDstPic->iLineSize[1];
            sDstPicMap.iStride[2] = pDstPic->iLineSize[2];
            sDstPicMap.eFormat = EVideoFormat.VIDEO_FORMAT_I420;

            if (iSrcWidth != iShrinkWidth || iSrcHeight != iShrinkHeight)
            {
                iRet = (int32_t)m_pInterfaceVp->Process(iMethodIdx, &sSrcPixMap, &sDstPicMap);
            }
            else
            {
                WelsEnc.WelsMoveMemory_c(pDstPic->pData[0], pDstPic->pData[1], pDstPic->pData[2],
                                         pDstPic->iLineSize[0], pDstPic->iLineSize[1],
                                         pDstPic->iLineSize[2], pSrc->pData[0], pSrc->pData[1],
                                         pSrc->pData[2], pSrc->iLineSize[0], pSrc->iLineSize[1],
                                         pSrc->iLineSize[2], iSrcWidth, iSrcHeight);
            }
        }
        else
        {
            sDstPicMap = sSrcPixMap;    // memcpy (&sDstPicMap, &sSrcPixMap, sizeof (sDstPicMap))
        }

        // get rid of odd line
        iShrinkWidth -= (iShrinkWidth & 1);
        iShrinkHeight -= (iShrinkHeight & 1);
        Padding((uint8_t*)sDstPicMap.pPixel[0], (uint8_t*)sDstPicMap.pPixel[1], (uint8_t*)sDstPicMap.pPixel[2],
                sDstPicMap.iStride[0], sDstPicMap.iStride[1], iShrinkWidth, iTargetWidth, iShrinkHeight, iTargetHeight);

        return iRet;
    }

    //*********************************************************************************************************/
    private void VaaCalculation(SVAAFrameInfo* pVaaInfo, SPicture* pCurPicture, SPicture* pRefPicture,
                                bool bCalculateSQDiff, bool bCalculateVar, bool bCalculateBGD)
    {
        pVaaInfo->sVaaCalcInfo.pCurY = pCurPicture->pData[0];
        pVaaInfo->sVaaCalcInfo.pRefY = pRefPicture->pData[0];
        {
            int32_t iMethodIdx = (int32_t)EMethods.METHOD_VAA_STATISTICS;
            SPixMap sCurPixMap = default;
            SPixMap sRefPixMap = default;
            SVAACalcParam calc_param = default;

            sCurPixMap.pPixel[0] = pCurPicture->pData[0];
            sCurPixMap.iSizeInBits = WelsEnc.g_kiPixMapSizeInBits;
            sCurPixMap.sRect.iRectWidth = pCurPicture->iWidthInPixel;
            sCurPixMap.sRect.iRectHeight = pCurPicture->iHeightInPixel;
            sCurPixMap.iStride[0] = pCurPicture->iLineSize[0];
            sCurPixMap.eFormat = EVideoFormat.VIDEO_FORMAT_I420;

            sRefPixMap.pPixel[0] = pRefPicture->pData[0];
            sRefPixMap.iSizeInBits = WelsEnc.g_kiPixMapSizeInBits;
            sRefPixMap.sRect.iRectWidth = pRefPicture->iWidthInPixel;
            sRefPixMap.sRect.iRectHeight = pRefPicture->iHeightInPixel;
            sRefPixMap.iStride[0] = pRefPicture->iLineSize[0];
            sRefPixMap.eFormat = EVideoFormat.VIDEO_FORMAT_I420;

            calc_param.iCalcVar = bCalculateVar ? 1 : 0;
            calc_param.iCalcBgd = bCalculateBGD ? 1 : 0;
            calc_param.iCalcSsd = bCalculateSQDiff ? 1 : 0;
            calc_param.pCalcResult = &pVaaInfo->sVaaCalcInfo;

            m_pInterfaceVp->Set(iMethodIdx, &calc_param);
            m_pInterfaceVp->Process(iMethodIdx, &sCurPixMap, &sRefPixMap);
        }
    }

    private void BackgroundDetection(SVAAFrameInfo* pVaaInfo, SPicture* pCurPicture, SPicture* pRefPicture,
                                     bool bDetectFlag)
    {
        if (bDetectFlag)
        {
            pVaaInfo->iPicWidth = pCurPicture->iWidthInPixel;
            pVaaInfo->iPicHeight = pCurPicture->iHeightInPixel;

            pVaaInfo->iPicStride = pCurPicture->iLineSize[0];
            pVaaInfo->iPicStrideUV = pCurPicture->iLineSize[1];
            pVaaInfo->pCurY = pCurPicture->pData[0];
            pVaaInfo->pRefY = pRefPicture->pData[0];
            pVaaInfo->pCurU = pCurPicture->pData[1];
            pVaaInfo->pRefU = pRefPicture->pData[1];
            pVaaInfo->pCurV = pCurPicture->pData[2];
            pVaaInfo->pRefV = pRefPicture->pData[2];

            int32_t iMethodIdx = (int32_t)EMethods.METHOD_BACKGROUND_DETECTION;
            SPixMap sSrcPixMap = default;
            SPixMap sRefPixMap = default;
            SBGDInterface BGDParam = default;

            sSrcPixMap.pPixel[0] = pCurPicture->pData[0];
            sSrcPixMap.pPixel[1] = pCurPicture->pData[1];
            sSrcPixMap.pPixel[2] = pCurPicture->pData[2];
            sSrcPixMap.iSizeInBits = WelsEnc.g_kiPixMapSizeInBits;
            sSrcPixMap.iStride[0] = pCurPicture->iLineSize[0];
            sSrcPixMap.iStride[1] = pCurPicture->iLineSize[1];
            sSrcPixMap.iStride[2] = pCurPicture->iLineSize[2];
            sSrcPixMap.sRect.iRectWidth = pCurPicture->iWidthInPixel;
            sSrcPixMap.sRect.iRectHeight = pCurPicture->iHeightInPixel;
            sSrcPixMap.eFormat = EVideoFormat.VIDEO_FORMAT_I420;

            sRefPixMap.pPixel[0] = pRefPicture->pData[0];
            sRefPixMap.pPixel[1] = pRefPicture->pData[1];
            sRefPixMap.pPixel[2] = pRefPicture->pData[2];
            sRefPixMap.iSizeInBits = WelsEnc.g_kiPixMapSizeInBits;
            sRefPixMap.iStride[0] = pRefPicture->iLineSize[0];
            sRefPixMap.iStride[1] = pRefPicture->iLineSize[1];
            sRefPixMap.iStride[2] = pRefPicture->iLineSize[2];
            sRefPixMap.sRect.iRectWidth = pRefPicture->iWidthInPixel;
            sRefPixMap.sRect.iRectHeight = pRefPicture->iHeightInPixel;
            sRefPixMap.eFormat = EVideoFormat.VIDEO_FORMAT_I420;

            BGDParam.pBackgroundMbFlag = pVaaInfo->pVaaBackgroundMbFlag;
            BGDParam.pCalcRes = &(pVaaInfo->sVaaCalcInfo);
            m_pInterfaceVp->Set(iMethodIdx, (void*)&BGDParam);
            m_pInterfaceVp->Process(iMethodIdx, &sSrcPixMap, &sRefPixMap);
        }
        else
        {
            int32_t iPicWidthInMb = (pCurPicture->iWidthInPixel + 15) >> 4;
            int32_t iPicHeightInMb = (pCurPicture->iHeightInPixel + 15) >> 4;
            memset(pVaaInfo->pVaaBackgroundMbFlag, 0, iPicWidthInMb * iPicHeightInMb);
        }
    }

    private void AdaptiveQuantCalculation(SVAAFrameInfo* pVaaInfo, SPicture* pCurPicture, SPicture* pRefPicture)
    {
        pVaaInfo->sAdaptiveQuantParam.pCalcResult = &(pVaaInfo->sVaaCalcInfo);
        pVaaInfo->sAdaptiveQuantParam.iAverMotionTextureIndexToDeltaQp = 0;

        {
            int32_t iMethodIdx = (int32_t)EMethods.METHOD_ADAPTIVE_QUANT;
            SPixMap pSrc = default;
            SPixMap pRef = default;
            int32_t iRet = 0;

            pSrc.pPixel[0] = pCurPicture->pData[0];
            pSrc.iSizeInBits = WelsEnc.g_kiPixMapSizeInBits;
            pSrc.iStride[0] = pCurPicture->iLineSize[0];
            pSrc.sRect.iRectWidth = pCurPicture->iWidthInPixel;
            pSrc.sRect.iRectHeight = pCurPicture->iHeightInPixel;
            pSrc.eFormat = EVideoFormat.VIDEO_FORMAT_I420;

            pRef.pPixel[0] = pRefPicture->pData[0];
            pRef.iSizeInBits = WelsEnc.g_kiPixMapSizeInBits;
            pRef.iStride[0] = pRefPicture->iLineSize[0];
            pRef.sRect.iRectWidth = pRefPicture->iWidthInPixel;
            pRef.sRect.iRectHeight = pRefPicture->iHeightInPixel;
            pRef.eFormat = EVideoFormat.VIDEO_FORMAT_I420;

            iRet = (int32_t)m_pInterfaceVp->Set(iMethodIdx, (void*)&(pVaaInfo->sAdaptiveQuantParam));
            iRet = (int32_t)m_pInterfaceVp->Process(iMethodIdx, &pSrc, &pRef);
            if (iRet == 0)
                m_pInterfaceVp->Get(iMethodIdx, (void*)&(pVaaInfo->sAdaptiveQuantParam));
        }
    }

    private static void SetRefMbType(sWelsEncCtx* pCtx, uint32_t** pRefMbTypeArray, int32_t iRefPicType)
    {
        uint8_t uiTid = pCtx->uiTemporalId;
        uint8_t uiDid = pCtx->uiDependencyId;
        SRefList* pRefPicLlist = pCtx->ppRefPicListExt[uiDid];
        SLTRState* pLtr = &pCtx->pLtr[uiDid];
        uint8_t i = 0;

        if (pCtx->pSvcParam->bEnableLongTermReference && pLtr->bReceivedT0LostFlag && uiTid == 0)
        {
            for (i = 0; i < pRefPicLlist->uiLongRefCount; i++)
            {
                SPicture* pRef = pRefPicLlist->pLongRefList[i];
                if (pRef != null && pRef->uiRecieveConfirmed == 1/*RECIEVE_SUCCESS*/)
                {
                    *pRefMbTypeArray = pRef->uiRefMbType;
                    break;
                }
            }
        }
        else
        {
            for (i = 0; i < pRefPicLlist->uiShortRefCount; i++)
            {
                SPicture* pRef = pRefPicLlist->pShortRefList[i];
                if (pRef != null && pRef->bUsedAsRef && pRef->iFramePoc >= 0 && pRef->uiTemporalId <= uiTid)
                {
                    *pRefMbTypeArray = pRef->uiRefMbType;
                    break;
                }
            }
        }
    }

    public void AnalyzePictureComplexity(sWelsEncCtx* pCtx, SPicture* pCurPicture, SPicture* pRefPicture,
                                         int32_t kiDependencyId, bool bCalculateBGD)
    {
        SWelsSvcCodingParam* pSvcParam = pCtx->pSvcParam;
        int32_t iComplexityAnalysisMode = 0;

        if (pSvcParam->iUsageType == SCREEN_CONTENT_REAL_TIME)
        {
            // The screen content complexity analysis (SVAAFrameInfoExt's sComplexityScreenParam,
            // METHOD_COMPLEXITY_ANALYSIS_SCREEN): Phase 4.
            throw NotPorted();
        }
        else
        {
            SVAAFrameInfo* pVaaInfo = pCtx->pVaa;
            SComplexityAnalysisParam* sComplexityAnalysisParam = &(pVaaInfo->sComplexityAnalysisParam);
            SWelsSvcRc* SWelsSvcRc = &pCtx->pWelsSvcRc[kiDependencyId];

            if (pSvcParam->iRCMode == RC_QUALITY_MODE && pCtx->eSliceType == EWelsSliceType.P_SLICE)
            {
                iComplexityAnalysisMode = (int32_t)EComplexityAnalysisMode.FRAME_SAD;
            }
            else if (((pSvcParam->iRCMode == RC_BITRATE_MODE) || (pSvcParam->iRCMode == RC_TIMESTAMP_MODE))
                     && pCtx->eSliceType == EWelsSliceType.P_SLICE)
            {
                iComplexityAnalysisMode = (int32_t)EComplexityAnalysisMode.GOM_SAD;
            }
            else if (((pSvcParam->iRCMode == RC_BITRATE_MODE) || (pSvcParam->iRCMode == RC_TIMESTAMP_MODE))
                     && pCtx->eSliceType == EWelsSliceType.I_SLICE)
            {
                iComplexityAnalysisMode = (int32_t)EComplexityAnalysisMode.GOM_VAR;
            }
            else
            {
                return;
            }

            sComplexityAnalysisParam->iComplexityAnalysisMode = iComplexityAnalysisMode;
            sComplexityAnalysisParam->pCalcResult = &(pVaaInfo->sVaaCalcInfo);
            sComplexityAnalysisParam->pBackgroundMbFlag = pVaaInfo->pVaaBackgroundMbFlag;
            if (pRefPicture != null)
                SetRefMbType(pCtx, &(sComplexityAnalysisParam->uiRefMbType), pRefPicture->iPictureType);
            sComplexityAnalysisParam->iCalcBgd = bCalculateBGD ? 1 : 0;
            sComplexityAnalysisParam->iFrameComplexity = 0;

            memset(SWelsSvcRc->pGomForegroundBlockNum, 0, SWelsSvcRc->iGomSize * sizeof(int32_t));
            if (iComplexityAnalysisMode != (int32_t)EComplexityAnalysisMode.FRAME_SAD)
                memset(SWelsSvcRc->pCurrentFrameGomSad, 0, SWelsSvcRc->iGomSize * sizeof(int32_t));

            sComplexityAnalysisParam->pGomComplexity = SWelsSvcRc->pCurrentFrameGomSad;
            sComplexityAnalysisParam->pGomForegroundBlockNum = SWelsSvcRc->pGomForegroundBlockNum;
            sComplexityAnalysisParam->iMbNumInGom = SWelsSvcRc->iNumberMbGom;

            {
                int32_t iMethodIdx = (int32_t)EMethods.METHOD_COMPLEXITY_ANALYSIS;
                SPixMap sSrcPixMap = default;
                SPixMap sRefPixMap = default;
                int32_t iRet = 0;

                sSrcPixMap.pPixel[0] = pCurPicture->pData[0];
                sSrcPixMap.iSizeInBits = WelsEnc.g_kiPixMapSizeInBits;
                sSrcPixMap.iStride[0] = pCurPicture->iLineSize[0];
                sSrcPixMap.sRect.iRectWidth = pCurPicture->iWidthInPixel;
                sSrcPixMap.sRect.iRectHeight = pCurPicture->iHeightInPixel;
                sSrcPixMap.eFormat = EVideoFormat.VIDEO_FORMAT_I420;

                if (pRefPicture != null)
                {
                    sRefPixMap.pPixel[0] = pRefPicture->pData[0];
                    sRefPixMap.iSizeInBits = WelsEnc.g_kiPixMapSizeInBits;
                    sRefPixMap.iStride[0] = pRefPicture->iLineSize[0];
                    sRefPixMap.sRect.iRectWidth = pRefPicture->iWidthInPixel;
                    sRefPixMap.sRect.iRectHeight = pRefPicture->iHeightInPixel;
                }
                sRefPixMap.eFormat = EVideoFormat.VIDEO_FORMAT_I420;

                iRet = (int32_t)m_pInterfaceVp->Set(iMethodIdx, (void*)sComplexityAnalysisParam);
                iRet = (int32_t)m_pInterfaceVp->Process(iMethodIdx, &sSrcPixMap, &sRefPixMap);
                if (iRet == 0)
                    m_pInterfaceVp->Get(iMethodIdx, (void*)sComplexityAnalysisParam);
            }
        }
    }

    private static void InitPixMap(SPicture* pPicture, SPixMap* pPixMap)
    {
        pPixMap->pPixel[0] = pPicture->pData[0];
        pPixMap->pPixel[1] = pPicture->pData[1];
        pPixMap->pPixel[2] = pPicture->pData[2];
        pPixMap->iSizeInBits = sizeof(uint8_t);
        pPixMap->iStride[0] = pPicture->iLineSize[0];
        pPixMap->iStride[1] = pPicture->iLineSize[1];
        pPixMap->sRect.iRectWidth = pPicture->iWidthInPixel;
        pPixMap->sRect.iRectHeight = pPicture->iHeightInPixel;

        pPixMap->eFormat = EVideoFormat.VIDEO_FORMAT_I420;
    }

    // int32_t GetRefFrameInfo (int32_t iRefIdx, bool bCurrentFrameIsSceneLtr, SPicture*& pRefOri) and
    // int32_t UpdateBlockIdcForScreen (...): screen content (SVAAFrameInfoExt), Phase 4.

    private static void Padding(uint8_t* pSrcY, uint8_t* pSrcU, uint8_t* pSrcV, int32_t iStrideY, int32_t iStrideUV,
                                int32_t iActualWidth, int32_t iPaddingWidth, int32_t iActualHeight, int32_t iPaddingHeight)
    {
        int32_t i;

        if (iPaddingHeight > iActualHeight)
        {
            for (i = iActualHeight; i < iPaddingHeight; i++)
            {
                memset(pSrcY + i * iStrideY, 0, iActualWidth);

                if ((i & 1) == 0)
                {
                    memset(pSrcU + i / 2 * iStrideUV, 0x80, iActualWidth / 2);
                    memset(pSrcV + i / 2 * iStrideUV, 0x80, iActualWidth / 2);
                }
            }
        }

        if (iPaddingWidth > iActualWidth)
        {
            for (i = 0; i < iPaddingHeight; i++)
            {
                memset(pSrcY + i * iStrideY + iActualWidth, 0, iPaddingWidth - iActualWidth);
                if ((i & 1) == 0)
                {
                    memset(pSrcU + i / 2 * iStrideUV + iActualWidth / 2, 0x80, (iPaddingWidth - iActualWidth) / 2);
                    memset(pSrcV + i / 2 * iStrideUV + iActualWidth / 2, 0x80, (iPaddingWidth - iActualWidth) / 2);
                }
            }
        }
    }

    /*!
    * \brief    exchange two picture pData planes
    * \param    ppPic1      picture pointer to picture 1
    * \param    ppPic2      picture pointer to picture 2
    * \return   none
    */
    private static void WelsExchangeSpatialPictures(ref Ptr<SPicture> ppPic1, ref Ptr<SPicture> ppPic2)
    {
        Ptr<SPicture> tmp = ppPic1;

        Debug.Assert((SPicture*)ppPic1 != (SPicture*)ppPic2);

        ppPic1 = ppPic2;
        ppPic2 = tmp;
    }

    public void UpdateSrcListLosslessScreenRefSelectionWithLtr(SPicture* pCurPicture, int32_t kiCurDid,
                                                               int32_t kuiMarkLongTermPicIdx, SPicture** pLongRefList)
    {
        ref PtrArray17<SPicture> pLongRefSrcList = ref m_pSpatialPic[kiCurDid];
        for (int32_t i = 0; i < MAX_REF_PIC_COUNT; ++i)
        {
            if (null == (SPicture*)pLongRefSrcList[i + 1] || (null != pLongRefList[i] && pLongRefList[i]->bUsedAsRef
                                                              && pLongRefList[i]->bIsLongRef))
            {
                continue;
            }
            else
            {
                ((SPicture*)pLongRefSrcList[i + 1])->SetUnref();
            }
        }
        WelsExchangeSpatialPictures(ref m_pSpatialPic[kiCurDid][0],
                                    ref m_pSpatialPic[kiCurDid][1 + kuiMarkLongTermPicIdx]);
        m_iAvaliableRefInSpatialPicList = MAX_REF_PIC_COUNT;
        (GetCurrentOrigFrame(kiCurDid))->SetUnref();
    }

    public void UpdateSrcList(SPicture* pCurPicture, int32_t kiCurDid, SPicture** pShortRefList,
                              uint32_t kuiShortRefCount)
    {
        ref PtrArray17<SPicture> pRefSrcList = ref m_pSpatialPic[kiCurDid];

        //pRefSrcList[0] is for current frame
        if (pCurPicture->bUsedAsRef || pCurPicture->bIsLongRef)
        {
            if (pCurPicture->iPictureType == (int32_t)EWelsSliceType.P_SLICE && pCurPicture->uiTemporalId != 0)
            {
                for (int iRefIdx = (int)kuiShortRefCount - 1; iRefIdx >= 0; --iRefIdx)
                {
                    WelsExchangeSpatialPictures(ref pRefSrcList[iRefIdx + 1],
                                                ref pRefSrcList[iRefIdx]);
                }
                m_iAvaliableRefInSpatialPicList = (int32_t)kuiShortRefCount;
            }
            else
            {
                WelsExchangeSpatialPictures(ref pRefSrcList[0], ref pRefSrcList[1]);
                for (int32_t i = MAX_SHORT_REF_COUNT - 1; i > 0; --i)
                {
                    if ((SPicture*)pRefSrcList[i + 1] != null)
                    {
                        ((SPicture*)pRefSrcList[i + 1])->SetUnref();
                    }
                }
                m_iAvaliableRefInSpatialPicList = 1;
            }
        }
        (GetCurrentOrigFrame(kiCurDid))->SetUnref();
    }

    private static int32_t WelsMoveMemoryWrapper(SWelsSvcCodingParam* pSvcParam, SPicture* pDstPic,
                                                 SSourcePicture* kpSrc,
                                                 int32_t kiTargetWidth, int32_t kiTargetHeight)
    {
        if ((uint32_t)EVideoFormat.VIDEO_FORMAT_I420 != ((uint32_t)kpSrc->iColorFormat & (~(uint32_t)EVideoFormat.VIDEO_FORMAT_VFlip)))
            return ENC_RETURN_INVALIDINPUT;

        int32_t iSrcWidth = kpSrc->iPicWidth;
        int32_t iSrcHeight = kpSrc->iPicHeight;

        if (iSrcHeight > kiTargetHeight) iSrcHeight = kiTargetHeight;
        if (iSrcWidth > kiTargetWidth) iSrcWidth = kiTargetWidth;

        // copy from fr26 to fix the odd uiSize failed issue
        if ((iSrcWidth & 0x1) != 0) --iSrcWidth;
        if ((iSrcHeight & 0x1) != 0) --iSrcHeight;

        int32_t kiSrcTopOffsetY = pSvcParam->SUsedPicRect.iTop;
        int32_t kiSrcTopOffsetUV = (kiSrcTopOffsetY >> 1);
        int32_t kiSrcLeftOffsetY = pSvcParam->SUsedPicRect.iLeft;
        int32_t kiSrcLeftOffsetUV = (kiSrcLeftOffsetY >> 1);
        int32_t* iSrcOffset = stackalloc int32_t[3] { 0, 0, 0 };
        iSrcOffset[0] = kpSrc->iStride[0] * kiSrcTopOffsetY + kiSrcLeftOffsetY;
        iSrcOffset[1] = kpSrc->iStride[1] * kiSrcTopOffsetUV + kiSrcLeftOffsetUV;
        iSrcOffset[2] = kpSrc->iStride[2] * kiSrcTopOffsetUV + kiSrcLeftOffsetUV;

        uint8_t* pSrcY = kpSrc->pData[0] + iSrcOffset[0];
        uint8_t* pSrcU = kpSrc->pData[1] + iSrcOffset[1];
        uint8_t* pSrcV = kpSrc->pData[2] + iSrcOffset[2];
        int32_t kiSrcStrideY = kpSrc->iStride[0];
        int32_t kiSrcStrideU = kpSrc->iStride[1];
        int32_t kiSrcStrideV = kpSrc->iStride[2];

        uint8_t* pDstY = pDstPic->pData[0];
        uint8_t* pDstU = pDstPic->pData[1];
        uint8_t* pDstV = pDstPic->pData[2];
        int32_t kiDstStrideY = pDstPic->iLineSize[0];
        int32_t kiDstStrideU = pDstPic->iLineSize[1];
        int32_t kiDstStrideV = pDstPic->iLineSize[2];

        if (pSrcY != null)
        {
            if (iSrcWidth <= 0 || iSrcHeight <= 0)
                return ENC_RETURN_INVALIDINPUT;
            if (iSrcWidth > pDstPic->iWidthInPixel || iSrcHeight > pDstPic->iHeightInPixel)
                return ENC_RETURN_INVALIDINPUT;
            if (kiSrcTopOffsetY >= iSrcHeight || kiSrcLeftOffsetY >= iSrcWidth || iSrcWidth > kiSrcStrideY
                || (iSrcWidth >> 1) > kiSrcStrideU || (iSrcWidth >> 1) > kiSrcStrideV)
                return ENC_RETURN_INVALIDINPUT;
        }
        if (pDstY != null)
        {
            if (kiTargetWidth <= 0 || kiTargetHeight <= 0)
                return ENC_RETURN_INVALIDINPUT;
            if (kiTargetWidth > pDstPic->iWidthInPixel || kiTargetHeight > pDstPic->iHeightInPixel)
                return ENC_RETURN_INVALIDINPUT;
            if (kiTargetWidth > kiDstStrideY || (kiTargetWidth >> 1) > kiDstStrideU || (kiTargetWidth >> 1) > kiDstStrideV)
                return ENC_RETURN_INVALIDINPUT;
        }

        if (pSrcY == null || pSrcU == null || pSrcV == null || pDstY == null || pDstU == null || pDstV == null
            || (iSrcWidth & 1) != 0 || (iSrcHeight & 1) != 0)
        {
            return ENC_RETURN_INVALIDINPUT;
        }
        else
        {
            //i420_to_i420_c
            WelsEnc.WelsMoveMemory_c(pDstY, pDstU, pDstV, kiDstStrideY, kiDstStrideU,
                                     kiDstStrideV, pSrcY, pSrcU, pSrcV, kiSrcStrideY,
                                     kiSrcStrideU, kiSrcStrideV, iSrcWidth, iSrcHeight);

            //in VP Process
            if (kiTargetWidth > iSrcWidth || kiTargetHeight > iSrcHeight)
            {
                Padding(pDstY, pDstU, pDstV, kiDstStrideY, kiDstStrideU, iSrcWidth,
                        kiTargetWidth, iSrcHeight, kiTargetHeight);
            }
        }

        return ENC_RETURN_SUCCESS;
    }

    private static bool GetSceneChangeFlag(ESceneChangeIdc eSceneChangeIdc)
    {
        return ((eSceneChangeIdc == ESceneChangeIdc.LARGE_CHANGED_SCENE) ? true : false);
    }
}
