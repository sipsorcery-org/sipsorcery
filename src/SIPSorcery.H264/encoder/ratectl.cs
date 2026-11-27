//-----------------------------------------------------------------------------
// Filename: ratectl.cs
//
// Description: Port of codec/encoder/core/src/ratectl.cpp for camera content:
// the off, buffer based, bit rate (with and without post skip), quality and
// timestamp modes, with the frame skip decisions, the max bit rate check
// windows and padding.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// The screen content functions (WelRcPictureInitScc, WelsRcPictureInfoUpdateScc,
// WelsRcMbInitScc and WelsRcDropFrameUpdate) are not ported: nothing upstream
// installs or calls them. Where the camera functions would read the screen
// content complexity (SVAAFrameInfoExt) they throw NotSupportedException
// (Phase 4). The adaptive quantisation branches read the VAA state the
// preprocessing (CWelsPreProcess, E7) fills.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 26 Nov 2026  Claude          Created (E4).
// 26 Nov 2026  Claude          GomRCInitForOneSlice (E4b).
// 26 Nov 2026  Claude          The adaptive quantisation branches of the Disable functions (E5).
// 26 Nov 2026  Claude          The rate control modes, frame skipping and padding (E6).
// 27 Nov 2026  Claude          Header: the preprocessing exists (E7).
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
 */
namespace SIPSorcery.H264.Encoder;

internal static unsafe partial class WelsEnc
{
    // const int32_t g_kiQpToQstepTable[52]: WELS_ROUND(INT_MULTIPLY*pow (2.0, (iQP - 4.0) / 6.0))
    public static readonly int32_t* g_kiQpToQstepTable = StaticTable.Of<int32_t>(52, [
        63, 71, 79, 89, 100, 112, 126, 141, 159, 178,
        200, 224, 252, 283, 317, 356, 400, 449, 504, 566,
        635, 713, 800, 898, 1008, 1131, 1270, 1425, 1600, 1796,
        2016, 2263, 2540, 2851, 3200, 3592, 4032, 4525, 5080, 5702,
        6400, 7184, 8063, 9051, 10159, 11404, 12800, 14368, 16127, 18102,
        20319, 22807
    ]);

    public static void RcInitLayerMemory(SWelsSvcRc* pWelsSvcRc, CMemoryAlign* pMA, int32_t kiMaxTl)
    {
        int32_t kiGomSize = pWelsSvcRc->iGomSize;
        int32_t kiGomSizeD = kiGomSize * sizeof(double);
        int32_t kiGomSizeI = kiGomSize * sizeof(int32_t);
        int32_t kiLayerRcSize = kiGomSizeD + (kiGomSizeI * 3) + sizeof(SRCTemporal) * kiMaxTl;
        uint8_t* pBaseMem = (uint8_t*)pMA->WelsMalloc((uint32_t)kiLayerRcSize, "pWelsSvcRc->pTemporalOverRc");

        if (null == pBaseMem)
            return;

        pWelsSvcRc->pTemporalOverRc = (SRCTemporal*)pBaseMem;
        pBaseMem += sizeof(SRCTemporal) * kiMaxTl;
        pWelsSvcRc->pGomComplexity = (double*)pBaseMem;
        pBaseMem += kiGomSizeD;
        pWelsSvcRc->pGomForegroundBlockNum = (int32_t*)pBaseMem;
        pBaseMem += kiGomSizeI;
        pWelsSvcRc->pCurrentFrameGomSad = (int32_t*)pBaseMem;
        pBaseMem += kiGomSizeI;
        pWelsSvcRc->pGomCost = (int32_t*)pBaseMem;
    }

    public static void RcFreeLayerMemory(SWelsSvcRc* pWelsSvcRc, CMemoryAlign* pMA)
    {
        if (pWelsSvcRc != null && pWelsSvcRc->pTemporalOverRc != null)
        {
            pMA->WelsFree(pWelsSvcRc->pTemporalOverRc, "pWelsSvcRc->pTemporalOverRc");
            pWelsSvcRc->pTemporalOverRc = null;
            pWelsSvcRc->pGomComplexity = null;
            pWelsSvcRc->pGomForegroundBlockNum = null;
            pWelsSvcRc->pCurrentFrameGomSad = null;
            pWelsSvcRc->pGomCost = null;
        }
    }

    private static int32_t RcConvertQp2QStep(int32_t iQP)
    {
        return g_kiQpToQstepTable[iQP];
    }

    private static int32_t RcConvertQStep2Qp(int32_t iQpStep)
    {
        if (iQpStep <= g_kiQpToQstepTable[0]) //Qp step too small, return qp=0
            return 0;
        // log () of a float: MSVC's <math.h> has only the double log, so the float converts
        // to double, as it does here.
        return WELS_ROUND((6 * Math.Log(iQpStep * 1.0f / INT_MULTIPLY) / Math.Log(2.0) + 4.0));
    }

    public static void RcInitSequenceParameter(sWelsEncCtx* pEncCtx)
    {
        SWelsSvcRc* pWelsSvcRc = null;
        SSpatialLayerConfig* pDLayerParam = null;

        int32_t j = 0;
        int32_t iMbWidth = 0;

        bool bMultiSliceMode = false;
        int32_t iGomRowMode0 = 1, iGomRowMode1 = 1;
        for (j = 0; j < pEncCtx->pSvcParam->iSpatialLayerNum; j++)
        {
            pWelsSvcRc = &pEncCtx->pWelsSvcRc[j];
            pDLayerParam = &pEncCtx->pSvcParam->sSpatialLayers[j];
            iMbWidth = (pDLayerParam->iVideoWidth >> 4);
            pWelsSvcRc->iNumberMbFrame = iMbWidth * (pDLayerParam->iVideoHeight >> 4);

            pWelsSvcRc->iRcVaryPercentage = pEncCtx->pSvcParam->iBitsVaryPercentage; // % -- for temp
            pWelsSvcRc->iRcVaryRatio = pWelsSvcRc->iRcVaryPercentage;

            pWelsSvcRc->iBufferFullnessSkip = 0;
            pWelsSvcRc->uiLastTimeStamp = 0;
            pWelsSvcRc->iCost2BitsIntra = 1;
            pWelsSvcRc->iAvgCost2Bits = 1;
            pWelsSvcRc->iSkipBufferRatio = SKIP_RATIO;
            pWelsSvcRc->iContinualSkipFrames = 0;
            pWelsSvcRc->iQpRangeUpperInFrame = (QP_RANGE_UPPER_MODE1 * MAX_BITS_VARY_PERCENTAGE - ((
                                                   QP_RANGE_UPPER_MODE1 - QP_RANGE_MODE0) *
                                               pWelsSvcRc->iRcVaryRatio)) / MAX_BITS_VARY_PERCENTAGE;
            pWelsSvcRc->iQpRangeLowerInFrame = (QP_RANGE_LOWER_MODE1 * MAX_BITS_VARY_PERCENTAGE - ((
                                                   QP_RANGE_LOWER_MODE1 - QP_RANGE_MODE0) *
                                               pWelsSvcRc->iRcVaryRatio)) / MAX_BITS_VARY_PERCENTAGE;

            if (iMbWidth <= MB_WIDTH_THRESHOLD_90P)
            {
                pWelsSvcRc->iSkipQpValue = SKIP_QP_90P;
                iGomRowMode0 = GOM_ROW_MODE0_90P;
                iGomRowMode1 = GOM_ROW_MODE1_90P;
            }
            else if (iMbWidth <= MB_WIDTH_THRESHOLD_180P)
            {
                pWelsSvcRc->iSkipQpValue = SKIP_QP_180P;
                iGomRowMode0 = GOM_ROW_MODE0_180P;
                iGomRowMode1 = GOM_ROW_MODE1_180P;
            }
            else if (iMbWidth <= MB_WIDTH_THRESHOLD_360P)
            {
                pWelsSvcRc->iSkipQpValue = SKIP_QP_360P;
                iGomRowMode0 = GOM_ROW_MODE0_360P;
                iGomRowMode1 = GOM_ROW_MODE1_360P;
            }
            else
            {
                pWelsSvcRc->iSkipQpValue = SKIP_QP_720P;
                iGomRowMode0 = GOM_ROW_MODE0_720P;
                iGomRowMode1 = GOM_ROW_MODE1_720P;
            }
            iGomRowMode0 = iGomRowMode1 + ((iGomRowMode0 - iGomRowMode1) * pWelsSvcRc->iRcVaryRatio / MAX_BITS_VARY_PERCENTAGE);

            pWelsSvcRc->iNumberMbGom = iMbWidth * iGomRowMode0;

            pWelsSvcRc->iMinQp = pEncCtx->pSvcParam->iMinQp;

            pWelsSvcRc->iMaxQp = pEncCtx->pSvcParam->iMaxQp;

            pWelsSvcRc->iFrameDeltaQpUpper = LAST_FRAME_QP_RANGE_UPPER_MODE1 - ((LAST_FRAME_QP_RANGE_UPPER_MODE1 -
                                             LAST_FRAME_QP_RANGE_UPPER_MODE0) * pWelsSvcRc->iRcVaryRatio / MAX_BITS_VARY_PERCENTAGE);
            pWelsSvcRc->iFrameDeltaQpLower = LAST_FRAME_QP_RANGE_LOWER_MODE1 - ((LAST_FRAME_QP_RANGE_LOWER_MODE1 -
                                             LAST_FRAME_QP_RANGE_LOWER_MODE0) * pWelsSvcRc->iRcVaryRatio / MAX_BITS_VARY_PERCENTAGE);

            pWelsSvcRc->iSkipFrameNum = 0;
            pWelsSvcRc->iGomSize = (pWelsSvcRc->iNumberMbFrame + pWelsSvcRc->iNumberMbGom - 1) / pWelsSvcRc->iNumberMbGom;
            pWelsSvcRc->bEnableGomQp = 1;

            RcInitLayerMemory(pWelsSvcRc, pEncCtx->pMemAlign, 1 + pEncCtx->pSvcParam->sDependencyLayers[j].iHighestTemporalId);

            bMultiSliceMode = ((SliceModeEnum.SM_RASTER_SLICE == pDLayerParam->sSliceArgument.uiSliceMode) ||
                               (SliceModeEnum.SM_SIZELIMITED_SLICE == pDLayerParam->sSliceArgument.uiSliceMode));
            if (bMultiSliceMode)
                pWelsSvcRc->iNumberMbGom = pWelsSvcRc->iNumberMbFrame;
        }
    }

    //Index 0:Virtual GOP size, Index 1:Frame rate
    //double WeightArray[4][4] = { {1.0, 0, 0, 0}, {0.6, 0.4, 0, 0}, {0.4, 0.3, 0.15, 0}, {0.25, 0.15, 0.125, 0.0875}};
    // int32_t iWeightArray[4][4], original*WEIGHT_MULTIPLY
    private static ReadOnlySpan<int32_t> RcTlWeights => [2000, 0, 0, 0, 1200, 800, 0, 0, 800, 600, 300, 0, 500, 300, 250, 175];

    public static void RcInitTlWeight(sWelsEncCtx* pEncCtx)
    {
        SWelsSvcRc* pWelsSvcRc = &pEncCtx->pWelsSvcRc[pEncCtx->uiDependencyId];
        SRCTemporal* pTOverRc = pWelsSvcRc->pTemporalOverRc;
        SSpatialLayerInternal* pDLayerParam = &pEncCtx->pSvcParam->sDependencyLayers[pEncCtx->uiDependencyId];
        int32_t kiDecompositionStages = pDLayerParam->iDecompositionStages;
        int32_t kiHighestTid = pDLayerParam->iHighestTemporalId;

        ReadOnlySpan<int32_t> iWeightArray = RcTlWeights;
        int32_t kiGopSize = (1 << kiDecompositionStages);
        int32_t i, k, n;

        n = 0;
        while (n <= kiHighestTid)
        {
            pTOverRc[n].iTlayerWeight = iWeightArray[kiDecompositionStages * 4 + n];
            pTOverRc[n].iMinQp = pWelsSvcRc->iMinQp + (n << 1);
            pTOverRc[n].iMinQp = WELS_CLIP3(pTOverRc[n].iMinQp, 0, 51);
            pTOverRc[n].iMaxQp = pWelsSvcRc->iMaxQp + (n << 1);
            pTOverRc[n].iMaxQp = WELS_CLIP3(pTOverRc[n].iMaxQp, pTOverRc[n].iMinQp, 51);
            ++n;
        }
        //Calculate the frame index for the current frame and its reference frame
        for (n = 0; n < VGOP_SIZE; n += kiGopSize)
        {
            pWelsSvcRc->iTlOfFrames[n] = 0;
            for (i = 1; i <= kiDecompositionStages; i++)
            {
                for (k = 1 << (kiDecompositionStages - i); k < kiGopSize; k += (kiGopSize >> (i - 1)))
                {
                    pWelsSvcRc->iTlOfFrames[k + n] = (int8_t)i;
                }
            }
        }
        pWelsSvcRc->iPreviousGopSize = kiGopSize;
        pWelsSvcRc->iGopNumberInVGop = VGOP_SIZE / kiGopSize;
    }

    public static void RcUpdateBitrateFps(sWelsEncCtx* pEncCtx)
    {
        SWelsSvcRc* pWelsSvcRc = &pEncCtx->pWelsSvcRc[pEncCtx->uiDependencyId];
        SRCTemporal* pTOverRc = pWelsSvcRc->pTemporalOverRc;

        SSpatialLayerConfig* pDLayerParam = &pEncCtx->pSvcParam->sSpatialLayers[pEncCtx->uiDependencyId];
        SSpatialLayerInternal* pDLayerParamInternal = &pEncCtx->pSvcParam->sDependencyLayers[pEncCtx->uiDependencyId];
        int32_t kiGopSize = (1 << pDLayerParamInternal->iDecompositionStages);
        int32_t kiHighestTid = pDLayerParamInternal->iHighestTemporalId;
        int32_t input_iBitsPerFrame = WELS_DIV_ROUND(pDLayerParam->iSpatialBitrate,
                                                     pDLayerParamInternal->fOutputFrameRate);
        int64_t kiGopBits = (int64_t)input_iBitsPerFrame * kiGopSize;
        int32_t i;

        pWelsSvcRc->iBitRate = pDLayerParam->iSpatialBitrate;
        pWelsSvcRc->fFrameRate = pDLayerParamInternal->fOutputFrameRate;

        int32_t iTargetVaryRange = ((MAX_BITS_VARY_PERCENTAGE - pWelsSvcRc->iRcVaryRatio) >> 1);
        int32_t iMinBitsRatio = MAX_BITS_VARY_PERCENTAGE - iTargetVaryRange;
        int32_t iMaxBitsRatio = MAX_BITS_VARY_PERCENTAGE_x3d2;

        for (i = 0; i <= kiHighestTid; i++)
        {
            int64_t kdConstraitBits = kiGopBits * pTOverRc[i].iTlayerWeight;
            pTOverRc[i].iMinBitsTl = WELS_DIV_ROUND(kdConstraitBits * iMinBitsRatio,
                                                    MAX_BITS_VARY_PERCENTAGE * WEIGHT_MULTIPLY);
            pTOverRc[i].iMaxBitsTl = WELS_DIV_ROUND(kdConstraitBits * iMaxBitsRatio,
                                                    MAX_BITS_VARY_PERCENTAGE * WEIGHT_MULTIPLY);
        }
        //When bitrate is changed, pBuffer size should be updated
        pWelsSvcRc->iBufferSizeSkip = WELS_DIV_ROUND(pWelsSvcRc->iBitRate * pWelsSvcRc->iSkipBufferRatio, INT_MULTIPLY);
        pWelsSvcRc->iBufferSizePadding = WELS_DIV_ROUND(pWelsSvcRc->iBitRate * PADDING_BUFFER_RATIO, INT_MULTIPLY);

        //change remaining bits
        if (pWelsSvcRc->iBitsPerFrame > REMAIN_BITS_TH)
        {
            pWelsSvcRc->iRemainingBits = WELS_DIV_ROUND((int64_t)pWelsSvcRc->iRemainingBits * input_iBitsPerFrame,
                                                        pWelsSvcRc->iBitsPerFrame);
        }
        pWelsSvcRc->iBitsPerFrame = input_iBitsPerFrame;
        pWelsSvcRc->iMaxBitsPerFrame = WELS_DIV_ROUND(pDLayerParam->iMaxSpatialBitrate,
                                                      pDLayerParamInternal->fOutputFrameRate);
    }


    public static void RcInitVGop(sWelsEncCtx* pEncCtx)
    {
        int32_t kiDid = pEncCtx->uiDependencyId;
        SWelsSvcRc* pWelsSvcRc = &pEncCtx->pWelsSvcRc[kiDid];
        SRCTemporal* pTOverRc = pWelsSvcRc->pTemporalOverRc;
        int32_t kiHighestTid = pEncCtx->pSvcParam->sDependencyLayers[kiDid].iHighestTemporalId;
        bool fix_rc_overshoot = pEncCtx->pSvcParam->bFixRCOverShoot;

        if (fix_rc_overshoot)
        {
            // subtract unused bits if interrupted in a mid of VGOP
            int32_t iLeftInVGop = pWelsSvcRc->iGopNumberInVGop - pWelsSvcRc->iGopIndexInVGop;
            pWelsSvcRc->iRemainingBits -= iLeftInVGop * (pWelsSvcRc->iLastAllocatedBits / pWelsSvcRc->iGopNumberInVGop);
        }

        if (fix_rc_overshoot && pWelsSvcRc->iRemainingBits < 0)
        {
            // carry over bitrate deficit, so we don't overshoot
            pWelsSvcRc->iRemainingBits += VGOP_SIZE * pWelsSvcRc->iBitsPerFrame;
        }
        else
        {
            // but never more than target bitrate.
            pWelsSvcRc->iRemainingBits = VGOP_SIZE * pWelsSvcRc->iBitsPerFrame;
        }

        if (fix_rc_overshoot)
        {
            // store last allocated bits to correctly recalculate carry over
            pWelsSvcRc->iLastAllocatedBits = pWelsSvcRc->iRemainingBits;
        }
        pWelsSvcRc->iRemainingWeights = pWelsSvcRc->iGopNumberInVGop * WEIGHT_MULTIPLY;

        pWelsSvcRc->iFrameCodedInVGop = 0;
        pWelsSvcRc->iGopIndexInVGop = 0;

        for (int32_t i = 0; i <= kiHighestTid; ++i)
            pTOverRc[i].iGopBitsDq = 0;
        pWelsSvcRc->iSkipFrameInVGop = 0;
    }

    public static void RcInitRefreshParameter(sWelsEncCtx* pEncCtx)
    {
        int32_t kiDid = pEncCtx->uiDependencyId;
        SWelsSvcRc* pWelsSvcRc = &pEncCtx->pWelsSvcRc[kiDid];
        SRCTemporal* pTOverRc = pWelsSvcRc->pTemporalOverRc;
        SSpatialLayerConfig* pDLayerParam = &pEncCtx->pSvcParam->sSpatialLayers[kiDid];
        SSpatialLayerInternal* pDLayerParamInternal = &pEncCtx->pSvcParam->sDependencyLayers[kiDid];
        int32_t kiHighestTid = pDLayerParamInternal->iHighestTemporalId;
        bool fix_rc_overshoot = pEncCtx->pSvcParam->bFixRCOverShoot;
        int32_t i;

        //I frame R-Q Model
        pWelsSvcRc->iIntraComplexity = 0;
        pWelsSvcRc->iIntraMbCount = 0;
        pWelsSvcRc->iIntraComplxMean = 0;
        //P frame R-Q Model
        for (i = 0; i <= kiHighestTid; i++)
        {
            pTOverRc[i].iPFrameNum = 0;
            pTOverRc[i].iLinearCmplx = 0;
            pTOverRc[i].iFrameCmplxMean = 0;
        }

        pWelsSvcRc->iBufferFullnessSkip = 0;
        pWelsSvcRc->iBufferMaxBRFullness[EVEN_TIME_WINDOW] = 0;
        pWelsSvcRc->iBufferMaxBRFullness[ODD_TIME_WINDOW] = 0;
        pWelsSvcRc->iPredFrameBit = 0;
        pWelsSvcRc->iBufferFullnessPadding = 0;

        pWelsSvcRc->iGopIndexInVGop = 0;
        if (fix_rc_overshoot)
        {
            pWelsSvcRc->iLastAllocatedBits = 0;
        }
        pWelsSvcRc->iRemainingBits = 0;
        pWelsSvcRc->iBitsPerFrame = 0;

        //Backup the initial bitrate and fps
        pWelsSvcRc->iPreviousBitrate = pDLayerParam->iSpatialBitrate;
        pWelsSvcRc->dPreviousFps = pDLayerParamInternal->fOutputFrameRate;

        memset(pWelsSvcRc->pCurrentFrameGomSad, 0, pWelsSvcRc->iGomSize * sizeof(int32_t));

        RcInitTlWeight(pEncCtx);
        RcUpdateBitrateFps(pEncCtx);
        RcInitVGop(pEncCtx);
    }

    public static bool RcJudgeBitrateFpsUpdate(sWelsEncCtx* pEncCtx)
    {
        int32_t iCurDid = pEncCtx->uiDependencyId;
        SWelsSvcRc* pWelsSvcRc = &pEncCtx->pWelsSvcRc[iCurDid];
        SSpatialLayerInternal* pDLayerParamInternal = &pEncCtx->pSvcParam->sDependencyLayers[iCurDid];
        SSpatialLayerConfig* pDLayerParam = &pEncCtx->pSvcParam->sSpatialLayers[iCurDid];

        if ((pWelsSvcRc->iPreviousBitrate != pDLayerParam->iSpatialBitrate) ||
            (pWelsSvcRc->dPreviousFps - pDLayerParamInternal->fOutputFrameRate) > EPSN ||
            (pWelsSvcRc->dPreviousFps - pDLayerParamInternal->fOutputFrameRate) < -EPSN)
        {
            pWelsSvcRc->iPreviousBitrate = pDLayerParam->iSpatialBitrate;
            pWelsSvcRc->dPreviousFps = pDLayerParamInternal->fOutputFrameRate;
            return true;
        }
        else
            return false;
    }

    // RcTraceVGopBitrate is GOM_TRACE_FLAG only, which is off.

    public static void RcUpdateTemporalZero(sWelsEncCtx* pEncCtx)
    {
        int32_t kiDid = pEncCtx->uiDependencyId;
        SWelsSvcRc* pWelsSvcRc = &pEncCtx->pWelsSvcRc[kiDid];
        SSpatialLayerInternal* pDLayerParam = &pEncCtx->pSvcParam->sDependencyLayers[kiDid];
        int32_t kiGopSize = (1 << pDLayerParam->iDecompositionStages);

        if (pWelsSvcRc->iPreviousGopSize != kiGopSize)
        {
            RcInitTlWeight(pEncCtx);
            RcInitVGop(pEncCtx);
        }
        else if (pWelsSvcRc->iGopIndexInVGop == pWelsSvcRc->iGopNumberInVGop || pEncCtx->eSliceType == I_SLICE)
        {
            RcInitVGop(pEncCtx);
        }
        pWelsSvcRc->iGopIndexInVGop++;
    }

    // The frame complexity the analysis left: screen content's (SVAAFrameInfoExt) comes with Phase 4.
    private static int64_t RcFrameComplexity(sWelsEncCtx* pEncCtx)
    {
        if (pEncCtx->pSvcParam->iUsageType == SCREEN_CONTENT_REAL_TIME)
        {
            throw new NotSupportedException("Rate control: screen content's complexity (SVAAFrameInfoExt) comes with Phase 4.");
        }
        return pEncCtx->pVaa->sComplexityAnalysisParam.iFrameComplexity;
    }

    //64k@6fps for 90p:     bpp 0.74    QP:24
    //192k@12fps for 180p:  bpp 0.28    QP:26
    //512k@24fps for 360p:  bpp 0.09    QP:30
    //1500k@30fps for 720p: bpp 0.05    QP:32
    // double dBppArray[4][4], int32_t dInitialQPArray[4][5] and int32_t iQpRangeArray[5][2].
    private static ReadOnlySpan<double> RcBppArray => [0.25, 0.5, 0.75, 1.0, 0.1, 0.2, 0.3, 0.4, 0.03, 0.05, 0.09, 0.13, 0.01, 0.03, 0.06, 0.1];
    private static ReadOnlySpan<int32_t> RcInitialQpArray => [34, 28, 26, 24, 22, 36, 30, 28, 26, 24, 36, 32, 30, 28, 26, 36, 34, 32, 30, 28];
    private static ReadOnlySpan<int32_t> RcQpRangeArray => [40, 28, 37, 25, 36, 24, 35, 23, 34, 22];

    public static void RcCalculateIdrQp(sWelsEncCtx* pEncCtx)
    {
        double dBpp = 0;
        int32_t i;

        ReadOnlySpan<double> dBppArray = RcBppArray;
        ReadOnlySpan<int32_t> dInitialQPArray = RcInitialQpArray;
        int32_t iBppIndex = 0;
        ReadOnlySpan<int32_t> iQpRangeArray = RcQpRangeArray;
        int64_t iFrameComplexity = RcFrameComplexity(pEncCtx);
        bool fix_rc_overshoot = pEncCtx->pSvcParam->bFixRCOverShoot;
        SWelsSvcRc* pWelsSvcRc = &pEncCtx->pWelsSvcRc[pEncCtx->uiDependencyId];
        SSpatialLayerConfig* pDLayerParam = &pEncCtx->pSvcParam->sSpatialLayers[pEncCtx->uiDependencyId];
        SSpatialLayerInternal* pDLayerParamInternal = &pEncCtx->pSvcParam->sDependencyLayers[pEncCtx->uiDependencyId];
        if (pDLayerParamInternal->fOutputFrameRate > EPSN && pDLayerParam->iVideoWidth != 0 && pDLayerParam->iVideoHeight != 0)
            dBpp = (double)(pDLayerParam->iSpatialBitrate) / (double)(pDLayerParamInternal->fOutputFrameRate *
                   pDLayerParam->iVideoWidth *
                   pDLayerParam->iVideoHeight);
        else
            dBpp = 0.1;
        //Area*2
        if (pDLayerParam->iVideoWidth * pDLayerParam->iVideoHeight <= 28800) // 90p video:160*90
            iBppIndex = 0;
        else if (pDLayerParam->iVideoWidth * pDLayerParam->iVideoHeight <= 115200) // 180p video:320*180
            iBppIndex = 1;
        else if (pDLayerParam->iVideoWidth * pDLayerParam->iVideoHeight <= 460800) // 360p video:640*360
            iBppIndex = 2;
        else
            iBppIndex = 3;

        //Search
        for (i = (fix_rc_overshoot ? 0 : 1); i < 4; i++)
        {
            if (dBpp <= dBppArray[iBppIndex * 4 + i])
                break;
        }
        int32_t iMaxQp = iQpRangeArray[i * 2 + 0];
        int32_t iMinQp = iQpRangeArray[i * 2 + 1];
        iMinQp = WELS_CLIP3(iMinQp, pWelsSvcRc->iMinQp, pWelsSvcRc->iMaxQp);
        iMaxQp = WELS_CLIP3(iMaxQp, pWelsSvcRc->iMinQp, pWelsSvcRc->iMaxQp);
        if (0 == pWelsSvcRc->iIdrNum)
        { //the first IDR frame
            pWelsSvcRc->iInitialQp = dInitialQPArray[iBppIndex * 5 + i];
        }
        else
        {

            //obtain the idr qp using previous idr complexity
            if (pWelsSvcRc->iNumberMbFrame != pWelsSvcRc->iIntraMbCount)
            {
                pWelsSvcRc->iIntraComplexity = (int32_t)((int64_t)pWelsSvcRc->iIntraComplexity * pWelsSvcRc->iNumberMbFrame /
                                                         pWelsSvcRc->iIntraMbCount);
            }

            int64_t iCmplxRatio = WELS_DIV_ROUND64(iFrameComplexity * INT_MULTIPLY,
                                                   pWelsSvcRc->iIntraComplxMean);
            iCmplxRatio = WELS_CLIP3(iCmplxRatio, (int64_t)(INT_MULTIPLY - FRAME_CMPLX_RATIO_RANGE), (int64_t)(INT_MULTIPLY + FRAME_CMPLX_RATIO_RANGE));
            pWelsSvcRc->iQStep = (int32_t)WELS_DIV_ROUND64((int64_t)pWelsSvcRc->iIntraComplexity * iCmplxRatio,
                                                           (int64_t)pWelsSvcRc->iTargetBits * INT_MULTIPLY);
            pWelsSvcRc->iInitialQp = RcConvertQStep2Qp(pWelsSvcRc->iQStep);
        }

        pWelsSvcRc->iInitialQp = WELS_CLIP3(pWelsSvcRc->iInitialQp, iMinQp, iMaxQp);
        pEncCtx->iGlobalQp = pWelsSvcRc->iInitialQp;
        pWelsSvcRc->iQStep = RcConvertQp2QStep(pEncCtx->iGlobalQp);
        pWelsSvcRc->iLastCalculatedQScale = pEncCtx->iGlobalQp;
        pWelsSvcRc->iMinFrameQp = WELS_CLIP3(pEncCtx->iGlobalQp - DELTA_QP_BGD_THD, iMinQp, iMaxQp);
        pWelsSvcRc->iMaxFrameQp = WELS_CLIP3(pEncCtx->iGlobalQp + DELTA_QP_BGD_THD, iMinQp, iMaxQp);

    }

    public static void RcCalculatePictureQp(sWelsEncCtx* pEncCtx)
    {
        SWelsSvcRc* pWelsSvcRc = &pEncCtx->pWelsSvcRc[pEncCtx->uiDependencyId];
        int32_t iTl = pEncCtx->uiTemporalId;
        SRCTemporal* pTOverRc = &pWelsSvcRc->pTemporalOverRc[iTl];
        int32_t iLumaQp = 0;
        int32_t iDeltaQpTemporal = 0;
        int64_t iFrameComplexity = RcFrameComplexity(pEncCtx);
        if (0 == pTOverRc->iPFrameNum)
        {
            iLumaQp = pWelsSvcRc->iInitialQp;
        }
        else if (pWelsSvcRc->iCurrentBitsLevel == BITS_EXCEEDED)
        {
            iLumaQp = pWelsSvcRc->iLastCalculatedQScale + DELTA_QP_BGD_THD;
            //limit QP
            int32_t iLastIdxCodecInVGop = pWelsSvcRc->iFrameCodedInVGop - 1;
            if (iLastIdxCodecInVGop < 0)
                iLastIdxCodecInVGop += VGOP_SIZE;
            int32_t iTlLast = pWelsSvcRc->iTlOfFrames[iLastIdxCodecInVGop];
            iDeltaQpTemporal = iTl - iTlLast;
            if (0 == iTlLast && iTl > 0)
                iDeltaQpTemporal += 1;
            else if (0 == iTl && iTlLast > 0)
                iDeltaQpTemporal -= 1;

        }
        else
        {
            int64_t iCmplxRatio = WELS_DIV_ROUND64(iFrameComplexity * INT_MULTIPLY,
                                                   pTOverRc->iFrameCmplxMean);
            iCmplxRatio = WELS_CLIP3(iCmplxRatio, (int64_t)(INT_MULTIPLY - FRAME_CMPLX_RATIO_RANGE), (int64_t)(INT_MULTIPLY + FRAME_CMPLX_RATIO_RANGE));

            pWelsSvcRc->iQStep = (int32_t)WELS_DIV_ROUND64((int64_t)pTOverRc->iLinearCmplx * iCmplxRatio,
                                                           (int64_t)pWelsSvcRc->iTargetBits * INT_MULTIPLY);
            iLumaQp = RcConvertQStep2Qp(pWelsSvcRc->iQStep);
            WelsLog(&(pEncCtx->sLogCtx), WELS_LOG_DEBUG,
                    $"iCmplxRatio = {(int)iCmplxRatio},frameComplexity = {iFrameComplexity},iFrameCmplxMean = {pTOverRc->iFrameCmplxMean},iQStep = {pWelsSvcRc->iQStep},iLumaQp = {iLumaQp}");
            //limit QP
            int32_t iLastIdxCodecInVGop = pWelsSvcRc->iFrameCodedInVGop - 1;
            if (iLastIdxCodecInVGop < 0)
                iLastIdxCodecInVGop += VGOP_SIZE;
            int32_t iTlLast = pWelsSvcRc->iTlOfFrames[iLastIdxCodecInVGop];
            iDeltaQpTemporal = iTl - iTlLast;
            if (0 == iTlLast && iTl > 0)
                iDeltaQpTemporal += 1;
            else if (0 == iTl && iTlLast > 0)
                iDeltaQpTemporal -= 1;
        }
        pWelsSvcRc->iMinFrameQp = WELS_CLIP3(pWelsSvcRc->iLastCalculatedQScale - pWelsSvcRc->iFrameDeltaQpLower +
                                             iDeltaQpTemporal, pTOverRc->iMinQp, pTOverRc->iMaxQp);
        pWelsSvcRc->iMaxFrameQp = WELS_CLIP3(pWelsSvcRc->iLastCalculatedQScale + pWelsSvcRc->iFrameDeltaQpUpper +
                                             iDeltaQpTemporal, pTOverRc->iMinQp, pTOverRc->iMaxQp);

        iLumaQp = WELS_CLIP3(iLumaQp, pWelsSvcRc->iMinFrameQp, pWelsSvcRc->iMaxFrameQp);

        if (pEncCtx->pSvcParam->bEnableAdaptiveQuant)
        {

            iLumaQp = WELS_DIV_ROUND(iLumaQp * INT_MULTIPLY - pEncCtx->pVaa->sAdaptiveQuantParam.iAverMotionTextureIndexToDeltaQp,
                                     INT_MULTIPLY);
            iLumaQp = WELS_CLIP3(iLumaQp, pWelsSvcRc->iMinFrameQp, pWelsSvcRc->iMaxFrameQp);
        }
        pWelsSvcRc->iQStep = RcConvertQp2QStep(iLumaQp);
        pWelsSvcRc->iLastCalculatedQScale = iLumaQp;
        pEncCtx->iGlobalQp = iLumaQp;
    }

    public static void GomRCInitForOneSlice(SSlice* pSlice, int32_t kiBitsPerMb)
    {
        SRCSlicing* pSOverRc = &pSlice->sSlicingOverRc;
        pSOverRc->iStartMbSlice = pSlice->sSliceHeaderExt.sSliceHeader.iFirstMbInSlice;
        pSOverRc->iEndMbSlice = pSOverRc->iStartMbSlice + pSlice->iCountMbNumInSlice - 1;
        pSOverRc->iTargetBitsSlice = WELS_DIV_ROUND((int64_t)kiBitsPerMb * pSlice->iCountMbNumInSlice,
                                     INT_MULTIPLY);
    }

    public static void RcInitSliceInformation(sWelsEncCtx* pEncCtx)
    {
        SSlice** ppSliceInLayer = pEncCtx->pCurDqLayer->ppSliceInLayer;
        SWelsSvcRc* pWelsSvcRc = &pEncCtx->pWelsSvcRc[pEncCtx->uiDependencyId];
        int32_t kiSliceNum = pEncCtx->pCurDqLayer->iMaxSliceNum;
        pWelsSvcRc->iBitsPerMb = WELS_DIV_ROUND((int64_t)pWelsSvcRc->iTargetBits * INT_MULTIPLY,
                                                pWelsSvcRc->iNumberMbFrame);
        pWelsSvcRc->bGomRC = (RC_OFF_MODE == pEncCtx->pSvcParam->iRCMode ||
                              RC_BUFFERBASED_MODE == pEncCtx->pSvcParam->iRCMode) ? false : true;
        for (int32_t i = 0; i < kiSliceNum; i++)
        {
            SRCSlicing* pSOverRc = &ppSliceInLayer[i]->sSlicingOverRc;
            pSOverRc->iTotalQpSlice = 0;
            pSOverRc->iTotalMbSlice = 0;
            pSOverRc->iFrameBitsSlice = 0;
            pSOverRc->iGomBitsSlice = 0;
            pSOverRc->iStartMbSlice = 0;
            pSOverRc->iEndMbSlice = 0;
            pSOverRc->iTargetBitsSlice = 0;
        }
    }

    public static void RcDecideTargetBits(sWelsEncCtx* pEncCtx)
    {
        SWelsSvcRc* pWelsSvcRc = &pEncCtx->pWelsSvcRc[pEncCtx->uiDependencyId];
        SRCTemporal* pTOverRc = &pWelsSvcRc->pTemporalOverRc[pEncCtx->uiTemporalId];

        pWelsSvcRc->iCurrentBitsLevel = BITS_NORMAL;
        bool fix_rc_overshoot = pEncCtx->pSvcParam->bFixRCOverShoot;
        //allocate bits
        if (pEncCtx->eSliceType == I_SLICE)
        {
            if (pWelsSvcRc->iIdrNum != 0)
            {
                pWelsSvcRc->iTargetBits = (int32_t)((int64_t)pWelsSvcRc->iBitsPerFrame * pEncCtx->pSvcParam->iIdrBitrateRatio / 100);
            }
            else
            {
                pWelsSvcRc->iTargetBits = (int32_t)((int64_t)pWelsSvcRc->iBitsPerFrame * IDR_BITRATE_RATIO);
            }
        }
        else
        {
            if (pWelsSvcRc->iRemainingWeights > pTOverRc->iTlayerWeight ||
                (fix_rc_overshoot && pWelsSvcRc->iRemainingWeights == pTOverRc->iTlayerWeight))
                pWelsSvcRc->iTargetBits = WELS_DIV_ROUND((int64_t)pWelsSvcRc->iRemainingBits * pTOverRc->iTlayerWeight,
                                                         pWelsSvcRc->iRemainingWeights);
            else //this case should be not hit. needs to more test case to verify this
                pWelsSvcRc->iTargetBits = pWelsSvcRc->iRemainingBits;
            if ((pWelsSvcRc->iTargetBits <= 0) && ((pEncCtx->pSvcParam->iRCMode == RC_BITRATE_MODE)
                                                   && (pEncCtx->pSvcParam->bEnableFrameSkip == false)))
            {
                pWelsSvcRc->iCurrentBitsLevel = BITS_EXCEEDED;
            }
            pWelsSvcRc->iTargetBits = WELS_CLIP3(pWelsSvcRc->iTargetBits, pTOverRc->iMinBitsTl, pTOverRc->iMaxBitsTl);
        }
        pWelsSvcRc->iRemainingWeights -= pTOverRc->iTlayerWeight;

    }

    public static void RcDecideTargetBitsTimestamp(sWelsEncCtx* pEncCtx)
    {
        //decide one frame bits allocated
        SWelsSvcRc* pWelsSvcRc = &pEncCtx->pWelsSvcRc[pEncCtx->uiDependencyId];
        SSpatialLayerConfig* pDLayerParam = &pEncCtx->pSvcParam->sSpatialLayers[pEncCtx->uiDependencyId];
        int32_t iTl = pEncCtx->uiTemporalId;
        SRCTemporal* pTOverRc = &pWelsSvcRc->pTemporalOverRc[iTl];
        pWelsSvcRc->iCurrentBitsLevel = BITS_NORMAL;

        if (pEncCtx->eSliceType == I_SLICE)
        {
            int32_t iBufferTh = (int32_t)(pWelsSvcRc->iBufferSizeSkip - pWelsSvcRc->iBufferFullnessSkip);
            if (iBufferTh <= 0)
            {
                pWelsSvcRc->iCurrentBitsLevel = BITS_EXCEEDED;
                pWelsSvcRc->iTargetBits = pTOverRc->iMinBitsTl;
            }
            else
            {
                int32_t iMaxTh = iBufferTh * 3 / 4;
                // The ternary mixes double and float, so the float quotient widens to double.
                int32_t iMinTh = (int32_t)((pDLayerParam->fFrameRate < 8) ? iBufferTh * 1.0 / 4 : iBufferTh * 2 / pDLayerParam->fFrameRate);
                if (pDLayerParam->fFrameRate < (IDR_BITRATE_RATIO + 1))
                    pWelsSvcRc->iTargetBits = (int32_t)(((double)(pDLayerParam->iSpatialBitrate) / (double)(
                                                pDLayerParam->fFrameRate)));
                else
                    pWelsSvcRc->iTargetBits = (int32_t)(((double)(pDLayerParam->iSpatialBitrate) / (double)(
                                                pDLayerParam->fFrameRate) * IDR_BITRATE_RATIO));
                WelsLog(&(pEncCtx->sLogCtx), WELS_LOG_DEBUG,
                        $"iMaxTh = {iMaxTh},iMinTh = {iMinTh},pWelsSvcRc->iTargetBits = {pWelsSvcRc->iTargetBits},pWelsSvcRc->iBufferSizeSkip = {pWelsSvcRc->iBufferSizeSkip}, pWelsSvcRc->iBufferFullnessSkip= {pWelsSvcRc->iBufferFullnessSkip}");
                pWelsSvcRc->iTargetBits = WELS_CLIP3(pWelsSvcRc->iTargetBits, iMinTh, iMaxTh);
            }

        }
        else
        {
            int32_t iBufferTh = (int32_t)(pWelsSvcRc->iBufferSizeSkip - pWelsSvcRc->iBufferFullnessSkip);
            if (iBufferTh <= 0)
            {
                pWelsSvcRc->iCurrentBitsLevel = BITS_EXCEEDED;
                pWelsSvcRc->iTargetBits = pTOverRc->iMinBitsTl;
                WelsLog(&(pEncCtx->sLogCtx), WELS_LOG_DEBUG,
                        $"iMaxTh = {iBufferTh},pWelsSvcRc->iTargetBits = {pWelsSvcRc->iTargetBits},pWelsSvcRc->iBufferSizeSkip = {pWelsSvcRc->iBufferSizeSkip}, pWelsSvcRc->iBufferFullnessSkip= {pWelsSvcRc->iBufferFullnessSkip}");
            }
            else
            {

                SSpatialLayerInternal* pDLayerParamInternal = &pEncCtx->pSvcParam->sDependencyLayers[pEncCtx->uiDependencyId];
                int32_t kiGopSize = (1 << pDLayerParamInternal->iDecompositionStages);
                int32_t iAverageFrameSize = (int32_t)((double)(pDLayerParam->iSpatialBitrate) / (double)(pDLayerParam->fFrameRate));
                int32_t kiGopBits = iAverageFrameSize * kiGopSize;
                pWelsSvcRc->iTargetBits = WELS_DIV_ROUND(pTOverRc->iTlayerWeight * kiGopBits, INT_MULTIPLY * 10 * 2);

                int32_t iMaxTh = iBufferTh / 2;
                int32_t iMinTh = (int32_t)((pDLayerParam->fFrameRate < 8) ? iBufferTh * 1.0 / 4 : iBufferTh * 2 / pDLayerParam->fFrameRate);
                WelsLog(&(pEncCtx->sLogCtx), WELS_LOG_DEBUG,
                        $"iMaxTh = {iMaxTh},iMinTh = {iMinTh},pWelsSvcRc->iTargetBits = {pWelsSvcRc->iTargetBits},pWelsSvcRc->iBufferSizeSkip = {pWelsSvcRc->iBufferSizeSkip}, pWelsSvcRc->iBufferFullnessSkip= {pWelsSvcRc->iBufferFullnessSkip}");
                pWelsSvcRc->iTargetBits = WELS_CLIP3(pWelsSvcRc->iTargetBits, iMinTh, iMaxTh);
            }
        }
    }

    public static void RcInitGomParameters(sWelsEncCtx* pEncCtx)
    {
        SSlice** ppSliceInLayer = pEncCtx->pCurDqLayer->ppSliceInLayer;
        SWelsSvcRc* pWelsSvcRc = &pEncCtx->pWelsSvcRc[pEncCtx->uiDependencyId];
        SRCSlicing* pSOverRc = &ppSliceInLayer[0]->sSlicingOverRc;
        int32_t kiSliceNum = pEncCtx->pCurDqLayer->iMaxSliceNum;
        int32_t kiGlobalQp = pEncCtx->iGlobalQp;

        pWelsSvcRc->iAverageFrameQp = 0;
        for (int32_t i = 0; i < kiSliceNum; ++i)
        {
            pSOverRc = &ppSliceInLayer[i]->sSlicingOverRc;
            pSOverRc->iComplexityIndexSlice = 0;
            pSOverRc->iCalculatedQpSlice = kiGlobalQp;
        }
        memset(pWelsSvcRc->pGomComplexity, 0, pWelsSvcRc->iGomSize * sizeof(double));
        memset(pWelsSvcRc->pGomCost, 0, pWelsSvcRc->iGomSize * sizeof(int32_t));
    }

    public static void RcCalculateMbQp(sWelsEncCtx* pEncCtx, SSlice* pSlice, SMB* pCurMb)
    {
        SWelsSvcRc* pWelsSvcRc = &pEncCtx->pWelsSvcRc[pEncCtx->uiDependencyId];
        SRCSlicing* pSOverRc = &pSlice->sSlicingOverRc;

        int32_t iLumaQp = pSOverRc->iCalculatedQpSlice;
        SDqLayer* pCurLayer = pEncCtx->pCurDqLayer;
        uint8_t kuiChromaQpIndexOffset = (uint8_t)pCurLayer->sLayerInfo.pPpsP->uiChromaQpIndexOffset;
        if (pEncCtx->pSvcParam->bEnableAdaptiveQuant)
        {
            iLumaQp = (int8_t)WELS_CLIP3(iLumaQp +
                                         pEncCtx->pVaa->sAdaptiveQuantParam.pMotionTextureIndexToDeltaQp[pCurMb->iMbXY], pWelsSvcRc->iMinFrameQp,
                                         pWelsSvcRc->iMaxFrameQp);
        }
        pCurMb->uiChromaQp = g_kuiChromaQpTable[CLIP3_QP_0_51(iLumaQp + kuiChromaQpIndexOffset)];
        pCurMb->uiLumaQp = (uint8_t)iLumaQp;
    }

    public static SWelsSvcRc* RcJudgeBaseUsability(sWelsEncCtx* pEncCtx)
    {
        SWelsSvcRc* pWelsSvcRc = null, pWelsSvcRc_Base = null;
        SSpatialLayerConfig* pDlpBase = null, pDLayerParam = null;
        SSpatialLayerInternal* pDlpBaseInternal = null;
        if (pEncCtx->uiDependencyId <= 0)
            return null;
        pDlpBaseInternal = &pEncCtx->pSvcParam->sDependencyLayers[pEncCtx->uiDependencyId - 1];
        pDlpBase = &pEncCtx->pSvcParam->sSpatialLayers[pEncCtx->uiDependencyId - 1];
        pWelsSvcRc_Base = &pEncCtx->pWelsSvcRc[pEncCtx->uiDependencyId - 1];
        if (pEncCtx->uiTemporalId <= pDlpBaseInternal->iDecompositionStages)
        {
            pWelsSvcRc = &pEncCtx->pWelsSvcRc[pEncCtx->uiDependencyId];
            pWelsSvcRc_Base = &pEncCtx->pWelsSvcRc[pEncCtx->uiDependencyId - 1];
            pDLayerParam = &pEncCtx->pSvcParam->sSpatialLayers[pEncCtx->uiDependencyId];
            pDlpBase = &pEncCtx->pSvcParam->sSpatialLayers[pEncCtx->uiDependencyId - 1];
            if ((pDLayerParam->iVideoWidth * pDLayerParam->iVideoHeight / pWelsSvcRc->iNumberMbGom) ==
                (pDlpBase->iVideoWidth * pDlpBase->iVideoHeight / pWelsSvcRc_Base->iNumberMbGom))
                return pWelsSvcRc_Base;
            else
                return null;
        }
        else
            return null;
    }

    public static void RcGomTargetBits(sWelsEncCtx* pEncCtx, SSlice* pSlice)
    {
        SWelsSvcRc* pWelsSvcRc = &pEncCtx->pWelsSvcRc[pEncCtx->uiDependencyId];
        SWelsSvcRc* pWelsSvcRc_Base = null;
        SRCSlicing* pSOverRc = &pSlice->sSlicingOverRc;

        int32_t iAllocateBits = 0;
        int32_t iSumSad = 0;
        int32_t iLastGomIndex = 0;
        int32_t iLeftBits = 0;
        int32_t kiComplexityIndex = pSOverRc->iComplexityIndexSlice;
        int32_t i;

        iLastGomIndex = pSOverRc->iEndMbSlice / pWelsSvcRc->iNumberMbGom;
        iLeftBits = pSOverRc->iTargetBitsSlice - pSOverRc->iFrameBitsSlice;
        if (iLeftBits <= 0)
        {
            pSOverRc->iGomTargetBits = 0;
            return;
        }
        else if (kiComplexityIndex >= iLastGomIndex)
        {
            iAllocateBits = iLeftBits;
        }
        else
        {
            pWelsSvcRc_Base = RcJudgeBaseUsability(pEncCtx);
            pWelsSvcRc_Base = (pWelsSvcRc_Base != null) ? pWelsSvcRc_Base : pWelsSvcRc;
            for (i = kiComplexityIndex + 1; i <= iLastGomIndex; i++)
            {
                iSumSad += pWelsSvcRc_Base->pCurrentFrameGomSad[i];
            }

            if (0 == iSumSad)
                iAllocateBits = WELS_DIV_ROUND(iLeftBits, (iLastGomIndex - kiComplexityIndex));
            else
                iAllocateBits = WELS_DIV_ROUND((int64_t)iLeftBits * pWelsSvcRc_Base->pCurrentFrameGomSad[kiComplexityIndex + 1],
                                               iSumSad);
        }
        pSOverRc->iGomTargetBits = iAllocateBits;
    }



    public static void RcCalculateGomQp(sWelsEncCtx* pEncCtx, SSlice* pSlice, SMB* pCurMb)
    {
        SWelsSvcRc* pWelsSvcRc = &pEncCtx->pWelsSvcRc[pEncCtx->uiDependencyId];
        SRCSlicing* pSOverRc = &pSlice->sSlicingOverRc;
        int64_t iBitsRatio = 1;

        int64_t iLeftBits = pSOverRc->iTargetBitsSlice - pSOverRc->iFrameBitsSlice;
        int64_t iTargetLeftBits = iLeftBits + pSOverRc->iGomBitsSlice - pSOverRc->iGomTargetBits;
        if ((iLeftBits <= 0) || (iTargetLeftBits <= 0))
        {
            pSOverRc->iCalculatedQpSlice += 2;
        }
        else
        {
            //globe decision
            iBitsRatio = 10000 * iLeftBits / (iTargetLeftBits + 1);
            if (iBitsRatio < 8409)              //2^(-1.5/6)*10000
                pSOverRc->iCalculatedQpSlice += 2;
            else if (iBitsRatio < 9439)         //2^(-0.5/6)*10000
                pSOverRc->iCalculatedQpSlice += 1;
            else if (iBitsRatio > 10600)        //2^(0.5/6)*10000
                pSOverRc->iCalculatedQpSlice -= 1;
            else if (iBitsRatio > 11900)        //2^(1.5/6)*10000 (unreachable upstream too)
                pSOverRc->iCalculatedQpSlice -= 2;
        }
        pSOverRc->iCalculatedQpSlice = WELS_CLIP3(pSOverRc->iCalculatedQpSlice, pWelsSvcRc->iMinFrameQp,
                                                  pWelsSvcRc->iMaxFrameQp);
        // WelsLog (& (pEncCtx->sLogCtx), WELS_LOG_DEBUG,"iCalculatedQpSlice =%d,iBitsRatio = %d\n",pSOverRc->iCalculatedQpSlice,iBitsRatio);
        pSOverRc->iGomBitsSlice = 0;

    }

    public static void RcVBufferCalculationSkip(sWelsEncCtx* pEncCtx)
    {
        SWelsSvcRc* pWelsSvcRc = &pEncCtx->pWelsSvcRc[pEncCtx->uiDependencyId];
        SRCTemporal* pTOverRc = pWelsSvcRc->pTemporalOverRc;
        int32_t kiOutputBits = pWelsSvcRc->iBitsPerFrame;
        int32_t kiOutputMaxBits = pWelsSvcRc->iMaxBitsPerFrame;
        //condition 1: whole pBuffer fullness
        pWelsSvcRc->iBufferFullnessSkip += (pWelsSvcRc->iFrameDqBits - kiOutputBits);
        pWelsSvcRc->iBufferMaxBRFullness[EVEN_TIME_WINDOW] += (pWelsSvcRc->iFrameDqBits - kiOutputMaxBits);
        pWelsSvcRc->iBufferMaxBRFullness[ODD_TIME_WINDOW] += (pWelsSvcRc->iFrameDqBits - kiOutputMaxBits);

        WelsLog(&(pEncCtx->sLogCtx), WELS_LOG_DEBUG,
                $"[Rc] bits in buffer = {pWelsSvcRc->iBufferFullnessSkip}, bits in Max bitrate buffer = {pWelsSvcRc->iBufferMaxBRFullness[EVEN_TIME_WINDOW]}");
        //condition 2: VGOP bits constraint
        int64_t iVGopBitsPred = 0;
        for (int32_t i = pWelsSvcRc->iFrameCodedInVGop + 1; i < VGOP_SIZE; i++)
            iVGopBitsPred += pTOverRc[pWelsSvcRc->iTlOfFrames[i]].iMinBitsTl;
        iVGopBitsPred -= pWelsSvcRc->iRemainingBits;
        double dIncPercent = iVGopBitsPred * 100.0 / (pWelsSvcRc->iBitsPerFrame * VGOP_SIZE) -
                             (double)VGOP_BITS_PERCENTAGE_DIFF;

        if ((pWelsSvcRc->iBufferFullnessSkip > pWelsSvcRc->iBufferSizeSkip
             && pWelsSvcRc->iAverageFrameQp > pWelsSvcRc->iSkipQpValue)
            || (dIncPercent > pWelsSvcRc->iRcVaryPercentage))
        {
            pWelsSvcRc->bSkipFlag = true;
        }
        WelsLog(&(pEncCtx->sLogCtx), WELS_LOG_DEBUG,
                $"[Rc] VBV_Skip,dIncPercent = {dIncPercent:F6},iRcVaryPercentage = {pWelsSvcRc->iRcVaryPercentage},pWelsSvcRc->bSkipFlag = {(pWelsSvcRc->bSkipFlag ? 1 : 0)}");
    }

    public static void CheckFrameSkipBasedMaxbr(sWelsEncCtx* pEncCtx, long uiTimeStamp, int32_t iDidIdx)
    {
        SWelsSvcRc* pWelsSvcRc = &pEncCtx->pWelsSvcRc[iDidIdx];
        SSpatialLayerConfig* pDLayerParam = &pEncCtx->pSvcParam->sSpatialLayers[iDidIdx];
        //SSpatialLayerInternal* pDLayerParamInternal     = &pEncCtx->pSvcParam->sDependencyLayers[pEncCtx->uiDependencyId];
        if (!pEncCtx->pSvcParam->bEnableFrameSkip)
            return;
        int32_t iSentBits = pWelsSvcRc->iBitsPerFrame;
        int32_t kiOutputMaxBits = pWelsSvcRc->iMaxBitsPerFrame;
        int64_t kiMaxSpatialBitRate = pDLayerParam->iMaxSpatialBitrate;

        //estimate allowed continual skipped frames in the sequence
        int32_t iPredSkipFramesTarBr = (WELS_DIV_ROUND(pWelsSvcRc->iBufferFullnessSkip, iSentBits) + 1) >> 1;
        int32_t iPredSkipFramesMaxBr = (WELS_MAX(WELS_DIV_ROUND(pWelsSvcRc->iBufferMaxBRFullness[EVEN_TIME_WINDOW],
                                                                kiOutputMaxBits), 0) + 1) >> 1;

        //calculate the remaining bits in TIME_CHECK_WINDOW
        int32_t iAvailableBitsInTimeWindow = WELS_DIV_ROUND((TIME_CHECK_WINDOW - pEncCtx->iCheckWindowInterval) *
                                                            kiMaxSpatialBitRate, 1000);
        int32_t iAvailableBitsInShiftTimeWindow = WELS_DIV_ROUND((TIME_CHECK_WINDOW - pEncCtx->iCheckWindowIntervalShift)
                                                                 * kiMaxSpatialBitRate, 1000);

        bool* bJudgeMaxBRbSkip = stackalloc bool[TIME_WINDOW_TOTAL];//0: EVEN_TIME_WINDOW; 1: ODD_TIME_WINDOW
        bool fix_rc_overshoot = pEncCtx->pSvcParam->bFixRCOverShoot;

        /* 4 cases for frame skipping
        1:skipping when buffer size larger than target threshold and current continual skip frames is allowed
        2:skipping when MaxBr buffer size + predict frame size - remaining bits in time window < 0 and current continual skip frames is allowed
        3:if in last ODD_TIME_WINDOW the MAX Br is overflowed, make more strict skipping conditions
        4:such as case 3 in the other window
        */
        bool bJudgeBufferFullSkip = (pWelsSvcRc->iContinualSkipFrames <= iPredSkipFramesTarBr)
                                    && (pWelsSvcRc->iBufferFullnessSkip > pWelsSvcRc->iBufferSizeSkip);
        bool bJudgeMaxBRbufferFullSkip = (pWelsSvcRc->iContinualSkipFrames <= iPredSkipFramesMaxBr)
                                         && (pEncCtx->iCheckWindowInterval > TIME_CHECK_WINDOW / 2)
                                         && (pWelsSvcRc->iBufferMaxBRFullness[EVEN_TIME_WINDOW] + pWelsSvcRc->iPredFrameBit - iAvailableBitsInTimeWindow > 0);
        bJudgeMaxBRbSkip[EVEN_TIME_WINDOW] = (pEncCtx->iCheckWindowInterval > TIME_CHECK_WINDOW / 2)
                                             && (pWelsSvcRc->bNeedShiftWindowCheck[EVEN_TIME_WINDOW])
                                             && (pWelsSvcRc->iBufferMaxBRFullness[EVEN_TIME_WINDOW] + pWelsSvcRc->iPredFrameBit - iAvailableBitsInTimeWindow +
                                                 kiOutputMaxBits > 0);
        bJudgeMaxBRbSkip[ODD_TIME_WINDOW] = (pEncCtx->iCheckWindowIntervalShift > TIME_CHECK_WINDOW / 2)
                                            && (pWelsSvcRc->bNeedShiftWindowCheck[ODD_TIME_WINDOW])
                                            && (pWelsSvcRc->iBufferMaxBRFullness[ODD_TIME_WINDOW] + pWelsSvcRc->iPredFrameBit - iAvailableBitsInShiftTimeWindow +
                                                kiOutputMaxBits > 0);

        pWelsSvcRc->bSkipFlag = false;
        if (bJudgeBufferFullSkip || bJudgeMaxBRbufferFullSkip || bJudgeMaxBRbSkip[EVEN_TIME_WINDOW]
            || bJudgeMaxBRbSkip[ODD_TIME_WINDOW])
        {
            pWelsSvcRc->bSkipFlag = true;
            if (!fix_rc_overshoot)
            {
                pWelsSvcRc->iSkipFrameNum++;
                pWelsSvcRc->iSkipFrameInVGop++;
                pWelsSvcRc->iBufferFullnessSkip -= iSentBits;
                pWelsSvcRc->iRemainingBits += iSentBits;
                pWelsSvcRc->iBufferMaxBRFullness[EVEN_TIME_WINDOW] -= kiOutputMaxBits;
                pWelsSvcRc->iBufferMaxBRFullness[ODD_TIME_WINDOW] -= kiOutputMaxBits;
                WelsLog(&(pEncCtx->sLogCtx), WELS_LOG_DEBUG,
                        $"[Rc] bits in buffer = {pWelsSvcRc->iBufferFullnessSkip}, bits in Max bitrate buffer = {pWelsSvcRc->iBufferMaxBRFullness[EVEN_TIME_WINDOW]}, Predict skip frames = {iPredSkipFramesTarBr} and {iPredSkipFramesMaxBr}");
                pWelsSvcRc->iBufferFullnessSkip = WELS_MAX(pWelsSvcRc->iBufferFullnessSkip, 0L);
            }
        }
    }

    public static bool WelsRcCheckFrameStatus(sWelsEncCtx* pEncCtx, long uiTimeStamp, int32_t iSpatialNum, int32_t iCurDid)
    {

        bool bSkipMustFlag = false;

        //simul_cast AVC control
        if (pEncCtx->pSvcParam->bSimulcastAVC)
        {
            //check target_br skip and update info
            int32_t iDidIdx = iCurDid;
            if (pEncCtx->pFuncList->pfRc.pfWelsRcPicDelayJudge != null)
            {
                pEncCtx->pFuncList->pfRc.pfWelsRcPicDelayJudge(pEncCtx, uiTimeStamp, iDidIdx);
            }
            if (true == pEncCtx->pWelsSvcRc[iDidIdx].bSkipFlag)
            {
                bSkipMustFlag = true;
            }
            //check max_br skip
            if (pEncCtx->pFuncList->pfRc.pfWelsCheckSkipBasedMaxbr != null)
            {
                if ((!bSkipMustFlag) && (pEncCtx->pSvcParam->sSpatialLayers[iDidIdx].iMaxSpatialBitrate != UNSPECIFIED_BIT_RATE))
                {
                    pEncCtx->pFuncList->pfRc.pfWelsCheckSkipBasedMaxbr(pEncCtx, uiTimeStamp, iDidIdx);
                    if (true == pEncCtx->pWelsSvcRc[iDidIdx].bSkipFlag)
                    {
                        bSkipMustFlag = true;

                    }
                }
            }
            if (bSkipMustFlag)
            {
                pEncCtx->pWelsSvcRc[iDidIdx].uiLastTimeStamp = uiTimeStamp;
                pEncCtx->pWelsSvcRc[iDidIdx].bSkipFlag = false;
                pEncCtx->pWelsSvcRc[iDidIdx].iContinualSkipFrames++;
                return true;
            }
        }
        else
        { //SVC control
            for (int32_t i = 0; i < iSpatialNum; i++)
            {
                int32_t iDidIdx = pEncCtx->sSpatialIndexMap[i].iDid;
                //check target_br skip and update info

                if (pEncCtx->pFuncList->pfRc.pfWelsRcPicDelayJudge != null)
                {
                    pEncCtx->pFuncList->pfRc.pfWelsRcPicDelayJudge(pEncCtx, uiTimeStamp, iDidIdx);
                }
                if (true == pEncCtx->pWelsSvcRc[iDidIdx].bSkipFlag)
                {
                    bSkipMustFlag = true;
                }
                //check max_br skip
                if (pEncCtx->pFuncList->pfRc.pfWelsCheckSkipBasedMaxbr != null)
                {
                    if ((!bSkipMustFlag) && (pEncCtx->pSvcParam->sSpatialLayers[iDidIdx].iMaxSpatialBitrate != UNSPECIFIED_BIT_RATE))
                    {
                        pEncCtx->pFuncList->pfRc.pfWelsCheckSkipBasedMaxbr(pEncCtx, uiTimeStamp, iDidIdx);
                        if (true == pEncCtx->pWelsSvcRc[iDidIdx].bSkipFlag)
                        {
                            bSkipMustFlag = true;
                        }
                    }
                }
                if (bSkipMustFlag)
                {
                    break;
                }
            }

            if (bSkipMustFlag)
            {
                for (int32_t i = 0; i < iSpatialNum; i++)
                {
                    int32_t iDidIdx = pEncCtx->sSpatialIndexMap[i].iDid;
                    pEncCtx->pWelsSvcRc[iDidIdx].uiLastTimeStamp = uiTimeStamp;
                    pEncCtx->pWelsSvcRc[iDidIdx].bSkipFlag = false;
                    pEncCtx->pWelsSvcRc[iDidIdx].iContinualSkipFrames++;
                }
                return true;
            }
        }
        return false;
    }

    public static void UpdateBufferWhenFrameSkipped(sWelsEncCtx* pEncCtx, int32_t iCurDid)
    {
        SWelsSvcRc* pWelsSvcRc = &pEncCtx->pWelsSvcRc[iCurDid];
        int32_t kiOutputBits = pWelsSvcRc->iBitsPerFrame;
        int32_t kiOutputMaxBits = pWelsSvcRc->iMaxBitsPerFrame;
        pWelsSvcRc->iBufferFullnessSkip = pWelsSvcRc->iBufferFullnessSkip - kiOutputBits;
        pWelsSvcRc->iBufferMaxBRFullness[EVEN_TIME_WINDOW] -= kiOutputMaxBits;
        pWelsSvcRc->iBufferMaxBRFullness[ODD_TIME_WINDOW] -= kiOutputMaxBits;
        WelsLog(&(pEncCtx->sLogCtx), WELS_LOG_DEBUG,
                $"[Rc] iDid = {iCurDid},bits in buffer = {pWelsSvcRc->iBufferFullnessSkip}, bits in Max bitrate buffer = {pWelsSvcRc->iBufferMaxBRFullness[EVEN_TIME_WINDOW]}");

        pWelsSvcRc->iBufferFullnessSkip = WELS_MAX(pWelsSvcRc->iBufferFullnessSkip, 0L);

        pWelsSvcRc->iRemainingBits += kiOutputBits;
        pWelsSvcRc->iSkipFrameNum++;
        pWelsSvcRc->iSkipFrameInVGop++;

        if ((pWelsSvcRc->iContinualSkipFrames % 3) == 0)
        {
            //output a warning when iContinualSkipFrames is large enough, which may indicate subjective quality problem
            //note that here iContinualSkipFrames must be >0, so the log output will be 3/6/....
            WelsLog(&(pEncCtx->sLogCtx), WELS_LOG_WARNING, $"[Rc] iDid = {iCurDid},iContinualSkipFrames({pWelsSvcRc->iContinualSkipFrames}) is large");
        }
    }

    public static void UpdateMaxBrCheckWindowStatus(sWelsEncCtx* pEncCtx, int32_t iSpatialNum, long uiTimeStamp)
    {
        if (pEncCtx->bCheckWindowStatusRefreshFlag)
        {
            pEncCtx->iCheckWindowCurrentTs = uiTimeStamp;
        }
        else
        {
            pEncCtx->iCheckWindowCurrentTs = pEncCtx->iCheckWindowStartTs = uiTimeStamp;
            pEncCtx->bCheckWindowStatusRefreshFlag = true;
            for (int32_t i = 0; i < iSpatialNum; i++)
            {
                int32_t iCurDid = pEncCtx->sSpatialIndexMap[i].iDid;
                pEncCtx->pWelsSvcRc[iCurDid].iBufferFullnessSkip = 0;
                pEncCtx->pWelsSvcRc[iCurDid].iBufferMaxBRFullness[ODD_TIME_WINDOW] = 0;
                pEncCtx->pWelsSvcRc[iCurDid].iBufferMaxBRFullness[EVEN_TIME_WINDOW] = 0;
                pEncCtx->pWelsSvcRc[iCurDid].bNeedShiftWindowCheck[ODD_TIME_WINDOW] = false;
                pEncCtx->pWelsSvcRc[iCurDid].bNeedShiftWindowCheck[EVEN_TIME_WINDOW] = false;
            }

        }
        pEncCtx->iCheckWindowInterval = (int32_t)(pEncCtx->iCheckWindowCurrentTs - pEncCtx->iCheckWindowStartTs);
        if (pEncCtx->iCheckWindowInterval >= (TIME_CHECK_WINDOW >> 1) && !pEncCtx->bCheckWindowShiftResetFlag)
        {
            pEncCtx->bCheckWindowShiftResetFlag = true;
            for (int32_t i = 0; i < iSpatialNum; i++)
            {
                int32_t iCurDid = pEncCtx->sSpatialIndexMap[i].iDid;
                if (pEncCtx->pWelsSvcRc[iCurDid].iBufferMaxBRFullness[ODD_TIME_WINDOW] > 0
                    && pEncCtx->pWelsSvcRc[iCurDid].iBufferMaxBRFullness[ODD_TIME_WINDOW] !=
                    pEncCtx->pWelsSvcRc[iCurDid].iBufferMaxBRFullness[0])
                {
                    pEncCtx->pWelsSvcRc[iCurDid].bNeedShiftWindowCheck[EVEN_TIME_WINDOW] = true;
                }
                else
                {
                    pEncCtx->pWelsSvcRc[iCurDid].bNeedShiftWindowCheck[EVEN_TIME_WINDOW] = false;
                }
                pEncCtx->pWelsSvcRc[iCurDid].iBufferMaxBRFullness[ODD_TIME_WINDOW] = 0;
            }
        }
        pEncCtx->iCheckWindowIntervalShift = pEncCtx->iCheckWindowInterval >= (TIME_CHECK_WINDOW >> 1) ?
                                             pEncCtx->iCheckWindowInterval - (TIME_CHECK_WINDOW >> 1) : pEncCtx->iCheckWindowInterval + (TIME_CHECK_WINDOW >> 1);

        if (pEncCtx->iCheckWindowInterval >= TIME_CHECK_WINDOW || pEncCtx->iCheckWindowInterval == 0)
        {
            pEncCtx->iCheckWindowStartTs = pEncCtx->iCheckWindowCurrentTs;
            pEncCtx->iCheckWindowInterval = 0;
            pEncCtx->bCheckWindowShiftResetFlag = false;
            for (int32_t i = 0; i < iSpatialNum; i++)
            {
                int32_t iCurDid = pEncCtx->sSpatialIndexMap[i].iDid;
                if (pEncCtx->pWelsSvcRc[iCurDid].iBufferMaxBRFullness[EVEN_TIME_WINDOW] > 0)
                {
                    pEncCtx->pWelsSvcRc[iCurDid].bNeedShiftWindowCheck[ODD_TIME_WINDOW] = true;
                }
                else
                {
                    pEncCtx->pWelsSvcRc[iCurDid].bNeedShiftWindowCheck[ODD_TIME_WINDOW] = false;
                }
                pEncCtx->pWelsSvcRc[iCurDid].iBufferMaxBRFullness[EVEN_TIME_WINDOW] = 0;
            }
        }
        return;
    }

    public static bool WelsRcPostFrameSkipping(sWelsEncCtx* pCtx, int32_t iDid, long uiTimeStamp)
    {
        //TODO: put in the decision of rate-control
        return false;
    }

    public static void WelsRcPostFrameSkippedUpdate(sWelsEncCtx* pCtx, int32_t iDid)
    {
        //TODO: do something to update buffers after post-skipping is done
        //let RC know post-skipping happened and adjust strategy accordingly
    }

    public static void RcVBufferCalculationPadding(sWelsEncCtx* pEncCtx)
    {
        SWelsSvcRc* pWelsSvcRc = &pEncCtx->pWelsSvcRc[pEncCtx->uiDependencyId];
        int32_t kiOutputBits = pWelsSvcRc->iBitsPerFrame;
        int32_t kiBufferThreshold = WELS_DIV_ROUND(PADDING_THRESHOLD * (-pWelsSvcRc->iBufferSizePadding), INT_MULTIPLY);

        pWelsSvcRc->iBufferFullnessPadding += (pWelsSvcRc->iFrameDqBits - kiOutputBits);

        if (pWelsSvcRc->iBufferFullnessPadding < kiBufferThreshold)
        {
            pWelsSvcRc->iPaddingSize = -pWelsSvcRc->iBufferFullnessPadding;
            pWelsSvcRc->iPaddingSize >>= 3; // /8
            pWelsSvcRc->iBufferFullnessPadding = 0;
        }
        else
            pWelsSvcRc->iPaddingSize = 0;
    }


    public static void RcTraceFrameBits(sWelsEncCtx* pEncCtx, long uiTimeStamp, int32_t iFrameSize)
    {
        SWelsSvcRc* pWelsSvcRc = &pEncCtx->pWelsSvcRc[pEncCtx->uiDependencyId];
        SSpatialLayerInternal* pParamInternal = &pEncCtx->pSvcParam->sDependencyLayers[pEncCtx->uiDependencyId];
        if (pWelsSvcRc->iPredFrameBit != 0)
            pWelsSvcRc->iPredFrameBit = (int32_t)(LAST_FRAME_PREDICT_WEIGHT * pWelsSvcRc->iFrameDqBits +
                                                  (1 - LAST_FRAME_PREDICT_WEIGHT) * pWelsSvcRc->iPredFrameBit);
        else
            pWelsSvcRc->iPredFrameBit = pWelsSvcRc->iFrameDqBits;

        WelsLog(&(pEncCtx->sLogCtx), WELS_LOG_DEBUG,
                $"[Rc]Layer {pEncCtx->uiDependencyId}: Frame timestamp = {uiTimeStamp}, Frame type = {(int)pEncCtx->eSliceType}, encoding_qp = {pEncCtx->iGlobalQp}, average qp = {pWelsSvcRc->iAverageFrameQp}, max qp = {pWelsSvcRc->iMaxFrameQp}, min qp = {pWelsSvcRc->iMinFrameQp}, index = {pParamInternal->iFrameIndex}, " +
                $"iTid = {pEncCtx->uiTemporalId}, used = {((pWelsSvcRc->iFrameDqBits > 0) ? pWelsSvcRc->iFrameDqBits : (iFrameSize << 3))}, bitsperframe = {pWelsSvcRc->iBitsPerFrame}, target = {pWelsSvcRc->iTargetBits}, remainingbits = {pWelsSvcRc->iRemainingBits}, skipbuffersize = {pWelsSvcRc->iBufferSizeSkip}");

    }

    public static void RcUpdatePictureQpBits(sWelsEncCtx* pEncCtx, int32_t iCodedBits)
    {
        SSlice** ppSliceInLayer = pEncCtx->pCurDqLayer->ppSliceInLayer;
        SRCSlicing* pSOverRc = &ppSliceInLayer[0]->sSlicingOverRc;
        SWelsSvcRc* pWelsSvcRc = &pEncCtx->pWelsSvcRc[pEncCtx->uiDependencyId];
        SSliceCtx* pCurSliceCtx = &pEncCtx->pCurDqLayer->sSliceEncCtx;
        int32_t iTotalQp = 0, iTotalMb = 0;
        int32_t i;

        if (pEncCtx->eSliceType == P_SLICE)
        {
            for (i = 0; i < pCurSliceCtx->iSliceNumInFrame; i++)
            {
                pSOverRc = &ppSliceInLayer[i]->sSlicingOverRc;
                iTotalQp += pSOverRc->iTotalQpSlice;
                iTotalMb += pSOverRc->iTotalMbSlice;
            }
            if (iTotalMb > 0)
                pWelsSvcRc->iAverageFrameQp = WELS_DIV_ROUND(INT_MULTIPLY * iTotalQp, iTotalMb * INT_MULTIPLY);
            else
                pWelsSvcRc->iAverageFrameQp = pEncCtx->iGlobalQp;
        }
        else
        {
            pWelsSvcRc->iAverageFrameQp = pEncCtx->iGlobalQp;
        }
        pWelsSvcRc->iFrameDqBits = iCodedBits;
        pWelsSvcRc->iLastCalculatedQScale = pWelsSvcRc->iAverageFrameQp;
        pWelsSvcRc->pTemporalOverRc[pEncCtx->uiTemporalId].iGopBitsDq += pWelsSvcRc->iFrameDqBits;
    }

    public static void RcUpdateIntraComplexity(sWelsEncCtx* pEncCtx)
    {
        SWelsSvcRc* pWelsSvcRc = &pEncCtx->pWelsSvcRc[pEncCtx->uiDependencyId];
        int32_t iAlpha = WELS_DIV_ROUND(INT_MULTIPLY, (1 + pWelsSvcRc->iIdrNum));
        if (iAlpha < (INT_MULTIPLY / 4)) iAlpha = INT_MULTIPLY / 4;
        int32_t iQStep = RcConvertQp2QStep(pWelsSvcRc->iAverageFrameQp);
        int64_t iIntraCmplx = iQStep * (int64_t)pWelsSvcRc->iFrameDqBits;
        int64_t iFrameComplexity = RcFrameComplexity(pEncCtx);
        if (pWelsSvcRc->iIdrNum == 0)
        {
            pWelsSvcRc->iIntraComplexity = iIntraCmplx;
            pWelsSvcRc->iIntraComplxMean = iFrameComplexity;
        }
        else
        {
            pWelsSvcRc->iIntraComplexity = WELS_DIV_ROUND64(((LINEAR_MODEL_DECAY_FACTOR) * pWelsSvcRc->iIntraComplexity +
                                                             (INT_MULTIPLY - LINEAR_MODEL_DECAY_FACTOR) *
                                                             iIntraCmplx), INT_MULTIPLY);


            pWelsSvcRc->iIntraComplxMean = WELS_DIV_ROUND64(((LINEAR_MODEL_DECAY_FACTOR) * (int64_t)(pWelsSvcRc->iIntraComplxMean)
                                                             + (INT_MULTIPLY - LINEAR_MODEL_DECAY_FACTOR) * (iFrameComplexity)),
                                                            INT_MULTIPLY);
        }

        pWelsSvcRc->iIntraMbCount = pWelsSvcRc->iNumberMbFrame;
        pWelsSvcRc->iIdrNum++;
        if (pWelsSvcRc->iIdrNum > 255)
            pWelsSvcRc->iIdrNum = 255;
        WelsLog(&(pEncCtx->sLogCtx), WELS_LOG_DEBUG,
                $"RcUpdateIntraComplexity iFrameDqBits = {pWelsSvcRc->iFrameDqBits},iQStep= {pWelsSvcRc->iQStep},iIntraCmplx = {pWelsSvcRc->iIntraComplexity}");

    }

    public static void RcUpdateFrameComplexity(sWelsEncCtx* pEncCtx)
    {
        SWelsSvcRc* pWelsSvcRc = &pEncCtx->pWelsSvcRc[pEncCtx->uiDependencyId];
        int32_t kiTl = pEncCtx->uiTemporalId;
        SRCTemporal* pTOverRc = &pWelsSvcRc->pTemporalOverRc[kiTl];

        int64_t iFrameComplexity = RcFrameComplexity(pEncCtx);
        int32_t iQStep = RcConvertQp2QStep(pWelsSvcRc->iAverageFrameQp);
        int32_t iAlpha = WELS_DIV_ROUND(INT_MULTIPLY, (1 + pTOverRc->iPFrameNum));
        if (iAlpha < SMOOTH_FACTOR_MIN_VALUE)
            iAlpha = SMOOTH_FACTOR_MIN_VALUE;
        if (0 == pTOverRc->iPFrameNum)
        {
            pTOverRc->iLinearCmplx = ((int64_t)pWelsSvcRc->iFrameDqBits) * iQStep;
            pTOverRc->iFrameCmplxMean = (int32_t)iFrameComplexity;
        }
        else
        {
            pTOverRc->iLinearCmplx = WELS_DIV_ROUND64(((LINEAR_MODEL_DECAY_FACTOR) * (int64_t)pTOverRc->iLinearCmplx
                                                       + (INT_MULTIPLY - LINEAR_MODEL_DECAY_FACTOR) * ((int64_t)pWelsSvcRc->iFrameDqBits * iQStep)),
                                                      INT_MULTIPLY);
            pTOverRc->iFrameCmplxMean = WELS_DIV_ROUND64(((LINEAR_MODEL_DECAY_FACTOR) * (int64_t)(pTOverRc->iFrameCmplxMean)
                                                          + (INT_MULTIPLY - LINEAR_MODEL_DECAY_FACTOR) * iFrameComplexity),
                                                         INT_MULTIPLY);
        }


        pTOverRc->iPFrameNum++;
        if (pTOverRc->iPFrameNum > 255)
            pTOverRc->iPFrameNum = 255;
        WelsLog(&(pEncCtx->sLogCtx), WELS_LOG_DEBUG,
                $"RcUpdateFrameComplexity iFrameDqBits = {pWelsSvcRc->iFrameDqBits},iQStep= {iQStep},pWelsSvcRc->iQStep= {pWelsSvcRc->iQStep},pTOverRc->iLinearCmplx = {pTOverRc->iLinearCmplx}");
        WelsLog(&(pEncCtx->sLogCtx), WELS_LOG_DEBUG, $"iFrameCmplxMean = {pTOverRc->iFrameCmplxMean},iFrameComplexity = {iFrameComplexity}");
    }

    public static int32_t RcCalculateCascadingQp(sWelsEncCtx* pEncCtx, int32_t iQp)
    {
        int32_t iTemporalQp = 0;
        if (pEncCtx->pSvcParam->iDecompStages != 0)
        {
            if (pEncCtx->uiTemporalId == 0)
                iTemporalQp = iQp - 3 - (pEncCtx->pSvcParam->iDecompStages - 1);
            else
                iTemporalQp = iQp - (pEncCtx->pSvcParam->iDecompStages - pEncCtx->uiTemporalId);
            iTemporalQp = WELS_CLIP3(iTemporalQp, 1, 51);
        }
        else
            iTemporalQp = iQp;
        return iTemporalQp;
    }

    public static void WelsRcPictureInitGom(sWelsEncCtx* pEncCtx, long uiTimeStamp)
    {
        SWelsSvcRc* pWelsSvcRc = &pEncCtx->pWelsSvcRc[pEncCtx->uiDependencyId];
        int32_t kiSliceNum = pEncCtx->pCurDqLayer->iMaxSliceNum;
        pWelsSvcRc->iContinualSkipFrames = 0;

        if (pEncCtx->eSliceType == I_SLICE)
        {
            if (0 == pWelsSvcRc->iIdrNum)
            { //iIdrNum == 0 means encoder has been initialed
                RcInitRefreshParameter(pEncCtx);
            }
        }
        if (RcJudgeBitrateFpsUpdate(pEncCtx))
        {
            RcUpdateBitrateFps(pEncCtx);
        }
        if (pEncCtx->uiTemporalId == 0)
        {
            RcUpdateTemporalZero(pEncCtx);
        }
        if (pEncCtx->pSvcParam->iRCMode == RC_TIMESTAMP_MODE)
        {
            RcDecideTargetBitsTimestamp(pEncCtx);
            pWelsSvcRc->uiLastTimeStamp = uiTimeStamp;
        }
        else
        {
            RcDecideTargetBits(pEncCtx);
        }
        //turn off GOM QP when slicenum is larger 1
        if ((kiSliceNum > 1) || ((pEncCtx->pSvcParam->iRCMode == RC_BITRATE_MODE)
                                 && (pEncCtx->eSliceType == I_SLICE)))
        {
            pWelsSvcRc->bEnableGomQp = 0; // false
        }
        else
            pWelsSvcRc->bEnableGomQp = 1; // true

        //decide globe_qp
        if (pEncCtx->eSliceType == I_SLICE)
        {
            RcCalculateIdrQp(pEncCtx);
        }
        else
        {
            RcCalculatePictureQp(pEncCtx);
        }
        RcInitSliceInformation(pEncCtx);
        RcInitGomParameters(pEncCtx);
    }

    public static void WelsRcPictureInfoUpdateGom(sWelsEncCtx* pEncCtx, int32_t iLayerSize)
    {
        SWelsSvcRc* pWelsSvcRc = &pEncCtx->pWelsSvcRc[pEncCtx->uiDependencyId];
        int32_t iCodedBits = (iLayerSize << 3);

        RcUpdatePictureQpBits(pEncCtx, iCodedBits);

        if (pEncCtx->eSliceType == P_SLICE)
        {
            RcUpdateFrameComplexity(pEncCtx);
        }
        else
        {
            RcUpdateIntraComplexity(pEncCtx);
        }
        pWelsSvcRc->iRemainingBits -= pWelsSvcRc->iFrameDqBits;

        if (pEncCtx->pSvcParam->bEnableFrameSkip /*&&
            pEncCtx->uiDependencyId == pEncCtx->pSvcParam->iSpatialLayerNum - 1*/)
        {
            RcVBufferCalculationSkip(pEncCtx);
        }

        if (pEncCtx->pSvcParam->iPaddingFlag != 0)
            RcVBufferCalculationPadding(pEncCtx);
        pWelsSvcRc->iFrameCodedInVGop++;
    }

    public static void WelsRcMbInitGom(sWelsEncCtx* pEncCtx, SMB* pCurMb, SSlice* pSlice)
    {
        SWelsSvcRc* pWelsSvcRc = &pEncCtx->pWelsSvcRc[pEncCtx->uiDependencyId];
        SRCSlicing* pSOverRc = &pSlice->sSlicingOverRc;
        SDqLayer* pCurLayer = pEncCtx->pCurDqLayer;
        uint8_t kuiChromaQpIndexOffset = (uint8_t)pCurLayer->sLayerInfo.pPpsP->uiChromaQpIndexOffset;

        pSOverRc->iBsPosSlice = pEncCtx->pFuncList->pfGetBsPosition(pSlice);
        if (pWelsSvcRc->bEnableGomQp != 0)
        {
            //calculate gom qp and target bits at the beginning of gom
            if (0 == (pCurMb->iMbXY % pWelsSvcRc->iNumberMbGom))
            {
                if (pCurMb->iMbXY != pSOverRc->iStartMbSlice)
                {
                    pSOverRc->iComplexityIndexSlice++;
                    RcCalculateGomQp(pEncCtx, pSlice, pCurMb);
                }
                RcGomTargetBits(pEncCtx, pSlice);
            }

            RcCalculateMbQp(pEncCtx, pSlice, pCurMb);
        }
        else
        {
            pCurMb->uiLumaQp = (uint8_t)pEncCtx->iGlobalQp;
            pCurMb->uiChromaQp = g_kuiChromaQpTable[CLIP3_QP_0_51(pCurMb->uiLumaQp + kuiChromaQpIndexOffset)];
        }

    }

    public static void WelsRcMbInfoUpdateGom(sWelsEncCtx* pEncCtx, SMB* pCurMb, int32_t iCostLuma, SSlice* pSlice)
    {
        SWelsSvcRc* pWelsSvcRc = &pEncCtx->pWelsSvcRc[pEncCtx->uiDependencyId];
        SRCSlicing* pSOverRc = &pSlice->sSlicingOverRc;
        int32_t kiComplexityIndex = pSOverRc->iComplexityIndexSlice;

        int32_t iCurMbBits = pEncCtx->pFuncList->pfGetBsPosition(pSlice) - pSOverRc->iBsPosSlice;
        pSOverRc->iFrameBitsSlice += iCurMbBits;
        pSOverRc->iGomBitsSlice += iCurMbBits;

        pWelsSvcRc->pGomCost[kiComplexityIndex] += iCostLuma;
        if (iCurMbBits > 0)
        {
            pSOverRc->iTotalQpSlice += pCurMb->uiLumaQp;
            pSOverRc->iTotalMbSlice++;
        }
    }

    public static void WelsRcPictureInitDisable(sWelsEncCtx* pEncCtx, long uiTimeStamp)
    {
        SWelsSvcRc* pWelsSvcRc = &pEncCtx->pWelsSvcRc[pEncCtx->uiDependencyId];
        SSpatialLayerConfig* pDLayerParam = &pEncCtx->pSvcParam->sSpatialLayers[pEncCtx->uiDependencyId];
        int32_t kiQp = pDLayerParam->iDLayerQp;

        pEncCtx->iGlobalQp = RcCalculateCascadingQp(pEncCtx, kiQp);

        if (pEncCtx->pSvcParam->bEnableAdaptiveQuant && (pEncCtx->eSliceType == P_SLICE))
        {
            pEncCtx->iGlobalQp = WELS_CLIP3((pEncCtx->iGlobalQp * INT_MULTIPLY -
                                             pEncCtx->pVaa->sAdaptiveQuantParam.iAverMotionTextureIndexToDeltaQp) / INT_MULTIPLY, pWelsSvcRc->iMinQp,
                                            pWelsSvcRc->iMaxQp);
        }
        else
        {
            pEncCtx->iGlobalQp = WELS_CLIP3(pEncCtx->iGlobalQp, 0, 51);
        }

        pWelsSvcRc->iAverageFrameQp = pEncCtx->iGlobalQp;
    }

    public static void WelsRcPictureInfoUpdateDisable(sWelsEncCtx* pEncCtx, int32_t iLayerSize)
    {
    }

    public static void WelsRcMbInitDisable(sWelsEncCtx* pEncCtx, SMB* pCurMb, SSlice* pSlice)
    {
        int32_t iLumaQp = pEncCtx->iGlobalQp;
        SWelsSvcRc* pWelsSvcRc = &pEncCtx->pWelsSvcRc[pEncCtx->uiDependencyId];
        SDqLayer* pCurLayer = pEncCtx->pCurDqLayer;

        uint8_t kuiChromaQpIndexOffset = (uint8_t)pCurLayer->sLayerInfo.pPpsP->uiChromaQpIndexOffset;


        if (pEncCtx->pSvcParam->bEnableAdaptiveQuant && (pEncCtx->eSliceType == P_SLICE))
        {
            iLumaQp = (int8_t)WELS_CLIP3(iLumaQp +
                                         pEncCtx->pVaa->sAdaptiveQuantParam.pMotionTextureIndexToDeltaQp[pCurMb->iMbXY], pWelsSvcRc->iMinQp, pWelsSvcRc->iMaxQp);
        }
        else
        {
            iLumaQp = WELS_CLIP3(iLumaQp, 0, 51);
        }
        pCurMb->uiChromaQp = g_kuiChromaQpTable[CLIP3_QP_0_51(iLumaQp + kuiChromaQpIndexOffset)];
        pCurMb->uiLumaQp = (uint8_t)iLumaQp;
    }

    public static void WelsRcMbInfoUpdateDisable(sWelsEncCtx* pEncCtx, SMB* pCurMb, int32_t iCostLuma, SSlice* pSlice)
    {
    }

    public static void WelRcPictureInitBufferBasedQp(sWelsEncCtx* pEncCtx, long uiTimeStamp)
    {

        SVAAFrameInfo* pVaa = pEncCtx->pVaa;
        SWelsSvcRc* pWelsSvcRc = &pEncCtx->pWelsSvcRc[pEncCtx->uiDependencyId];

        int32_t iMinQp = pEncCtx->pSvcParam->iMinQp;
        if (pVaa->eSceneChangeIdc == ESceneChangeIdc.LARGE_CHANGED_SCENE)
            iMinQp += 2;
        else if (pVaa->eSceneChangeIdc == ESceneChangeIdc.MEDIUM_CHANGED_SCENE)
            iMinQp += 1;
        if (pEncCtx->bDeliveryFlag)
            pEncCtx->iGlobalQp -= 1;
        else
            pEncCtx->iGlobalQp += 2;
        pEncCtx->iGlobalQp = WELS_CLIP3(pEncCtx->iGlobalQp, iMinQp, pWelsSvcRc->iMaxQp);
        pWelsSvcRc->iAverageFrameQp = pWelsSvcRc->iMaxFrameQp = pWelsSvcRc->iMinFrameQp = pEncCtx->iGlobalQp;
    }

    // WelRcPictureInitScc, WelsRcDropFrameUpdate, WelsRcPictureInfoUpdateScc and WelsRcMbInitScc are
    // not ported: nothing upstream installs or calls them.

    public static void WelsRcFrameDelayJudgeTimeStamp(sWelsEncCtx* pEncCtx, long uiTimeStamp, int32_t iDidIdx)
    {
        SWelsSvcRc* pWelsSvcRc = &pEncCtx->pWelsSvcRc[iDidIdx];
        SSpatialLayerConfig* pDLayerConfig = &pEncCtx->pSvcParam->sSpatialLayers[iDidIdx];

        int32_t iBitRate = pDLayerConfig->iSpatialBitrate;
        int32_t iEncTimeInv = (pWelsSvcRc->uiLastTimeStamp == 0) ? 0 : (int32_t)(uiTimeStamp - pWelsSvcRc->uiLastTimeStamp);
        if ((iEncTimeInv < 0) || (iEncTimeInv > 1000))
        {
            iEncTimeInv = (int32_t)(1000.0 / pDLayerConfig->fFrameRate);
            pWelsSvcRc->uiLastTimeStamp = uiTimeStamp - iEncTimeInv;
        }
        int32_t iSentBits = (int32_t)((double)iBitRate * iEncTimeInv * (1.0E-3) + 0.5);
        iSentBits = WELS_MAX(iSentBits, 0);

        //When bitrate is changed, pBuffer size should be updated
        pWelsSvcRc->iBufferSizeSkip = WELS_DIV_ROUND(pDLayerConfig->iSpatialBitrate * pWelsSvcRc->iSkipBufferRatio,
                                                     INT_MULTIPLY);
        pWelsSvcRc->iBufferSizePadding = WELS_DIV_ROUND(pDLayerConfig->iSpatialBitrate * PADDING_BUFFER_RATIO, INT_MULTIPLY);

        pWelsSvcRc->iBufferFullnessSkip -= iSentBits;
        pWelsSvcRc->iBufferFullnessSkip = WELS_MAX((int64_t)((-1) * (pDLayerConfig->iSpatialBitrate / 4)),
                                                   pWelsSvcRc->iBufferFullnessSkip);

        if (pEncCtx->pSvcParam->bEnableFrameSkip)
        {
            pWelsSvcRc->bSkipFlag = true;
            if (pWelsSvcRc->iBufferFullnessSkip < pWelsSvcRc->iBufferSizeSkip)
            {
                pWelsSvcRc->bSkipFlag = false;
            }
            if (pWelsSvcRc->bSkipFlag)
            {
                pWelsSvcRc->iSkipFrameNum++;
                pWelsSvcRc->uiLastTimeStamp = uiTimeStamp;
            }
        }
        WelsLog(&(pEncCtx->sLogCtx), WELS_LOG_DEBUG,
                $"WelsRcFrameDelayJudgeTimeStamp iDidIdx = {iDidIdx},iSkipFrameNum = {pWelsSvcRc->iSkipFrameNum},buffer = {pWelsSvcRc->iBufferFullnessSkip},threadhold = {pWelsSvcRc->iBufferSizeSkip},bitrate = {iBitRate},iSentBits = {iSentBits},lasttimestamp = {pWelsSvcRc->uiLastTimeStamp},timestamp={uiTimeStamp}");
    }

    public static void WelsRcPictureInfoUpdateGomTimeStamp(sWelsEncCtx* pEncCtx, int32_t iLayerSize)
    {
        SWelsSvcRc* pWelsSvcRc = &pEncCtx->pWelsSvcRc[pEncCtx->uiDependencyId];
        int32_t iCodedBits = (iLayerSize << 3);

        RcUpdatePictureQpBits(pEncCtx, iCodedBits);
        if (pEncCtx->eSliceType == P_SLICE)
        {
            RcUpdateFrameComplexity(pEncCtx);
        }
        else
        {
            RcUpdateIntraComplexity(pEncCtx);
        }

        pWelsSvcRc->iRemainingBits -= pWelsSvcRc->iFrameDqBits;
        //condition 1: whole pBuffer fullness
        pWelsSvcRc->iBufferFullnessSkip += pWelsSvcRc->iFrameDqBits;

        if (pEncCtx->pSvcParam->iPaddingFlag != 0)
            RcVBufferCalculationPadding(pEncCtx);
        pWelsSvcRc->iFrameCodedInVGop++;
    }

    public static void WelsRcInitFuncPointers(sWelsEncCtx* pEncCtx, RC_MODES iRcMode)
    {
        SWelsRcFunc* pRcf = &pEncCtx->pFuncList->pfRc;
        switch (iRcMode)
        {
            case RC_OFF_MODE:
                pRcf->pfWelsRcPictureInit = &WelsRcPictureInitDisable;
                pRcf->pfWelsRcPicDelayJudge = null;
                pRcf->pfWelsRcPictureInfoUpdate = &WelsRcPictureInfoUpdateDisable;
                pRcf->pfWelsRcMbInit = &WelsRcMbInitDisable;
                pRcf->pfWelsRcMbInfoUpdate = &WelsRcMbInfoUpdateDisable;
                pRcf->pfWelsCheckSkipBasedMaxbr = null;
                pRcf->pfWelsUpdateBufferWhenSkip = null;
                pRcf->pfWelsUpdateMaxBrWindowStatus = null;
                pRcf->pfWelsRcPostFrameSkipping = null;
                break;
            case RC_BUFFERBASED_MODE:
                pRcf->pfWelsRcPictureInit = &WelRcPictureInitBufferBasedQp;
                pRcf->pfWelsRcPicDelayJudge = null;
                pRcf->pfWelsRcPictureInfoUpdate = &WelsRcPictureInfoUpdateDisable;
                pRcf->pfWelsRcMbInit = &WelsRcMbInitDisable;
                pRcf->pfWelsRcMbInfoUpdate = &WelsRcMbInfoUpdateDisable;
                pRcf->pfWelsCheckSkipBasedMaxbr = null;
                pRcf->pfWelsUpdateBufferWhenSkip = null;
                pRcf->pfWelsUpdateMaxBrWindowStatus = null;
                pRcf->pfWelsRcPostFrameSkipping = null;
                break;
            case RC_BITRATE_MODE:
                pRcf->pfWelsRcPictureInit = &WelsRcPictureInitGom;
                pRcf->pfWelsRcPicDelayJudge = null;
                pRcf->pfWelsRcPictureInfoUpdate = &WelsRcPictureInfoUpdateGom;
                pRcf->pfWelsRcMbInit = &WelsRcMbInitGom;
                pRcf->pfWelsRcMbInfoUpdate = &WelsRcMbInfoUpdateGom;
                pRcf->pfWelsCheckSkipBasedMaxbr = &CheckFrameSkipBasedMaxbr;
                pRcf->pfWelsUpdateBufferWhenSkip = &UpdateBufferWhenFrameSkipped;
                pRcf->pfWelsUpdateMaxBrWindowStatus = &UpdateMaxBrCheckWindowStatus;
                pRcf->pfWelsRcPostFrameSkipping = &WelsRcPostFrameSkipping;
                break;
            case RC_BITRATE_MODE_POST_SKIP:
                pRcf->pfWelsRcPictureInit = &WelsRcPictureInitGom;
                pRcf->pfWelsRcPicDelayJudge = null;
                pRcf->pfWelsRcPictureInfoUpdate = &WelsRcPictureInfoUpdateGom;
                pRcf->pfWelsRcMbInit = &WelsRcMbInitGom;
                pRcf->pfWelsRcMbInfoUpdate = &WelsRcMbInfoUpdateGom;
                pRcf->pfWelsCheckSkipBasedMaxbr = &CheckFrameSkipBasedMaxbr;
                pRcf->pfWelsUpdateBufferWhenSkip = &UpdateBufferWhenFrameSkipped;
                pRcf->pfWelsUpdateMaxBrWindowStatus = &UpdateMaxBrCheckWindowStatus;
                pRcf->pfWelsRcPostFrameSkipping = &WelsRcPostFrameSkipping;
                break;
            case RC_TIMESTAMP_MODE:

                pRcf->pfWelsRcPictureInit = &WelsRcPictureInitGom;
                pRcf->pfWelsRcPictureInfoUpdate = &WelsRcPictureInfoUpdateGomTimeStamp;
                pRcf->pfWelsRcMbInit = &WelsRcMbInitGom;
                pRcf->pfWelsRcMbInfoUpdate = &WelsRcMbInfoUpdateGom;

                pRcf->pfWelsRcPicDelayJudge = &WelsRcFrameDelayJudgeTimeStamp;
                pRcf->pfWelsCheckSkipBasedMaxbr = null;
                pRcf->pfWelsUpdateBufferWhenSkip = null;
                pRcf->pfWelsUpdateMaxBrWindowStatus = null;
                pRcf->pfWelsRcPostFrameSkipping = null;
                break;
            case RC_QUALITY_MODE:
            default:
                pRcf->pfWelsRcPictureInit = &WelsRcPictureInitGom;
                pRcf->pfWelsRcPicDelayJudge = null;
                pRcf->pfWelsRcPictureInfoUpdate = &WelsRcPictureInfoUpdateGom;
                pRcf->pfWelsRcMbInit = &WelsRcMbInitGom;
                pRcf->pfWelsRcMbInfoUpdate = &WelsRcMbInfoUpdateGom;
                pRcf->pfWelsCheckSkipBasedMaxbr = &CheckFrameSkipBasedMaxbr;
                pRcf->pfWelsUpdateBufferWhenSkip = &UpdateBufferWhenFrameSkipped;
                pRcf->pfWelsUpdateMaxBrWindowStatus = &UpdateMaxBrCheckWindowStatus;
                pRcf->pfWelsRcPostFrameSkipping = null;
                break;
        }
    }

    public static void WelsRcInitModule(sWelsEncCtx* pEncCtx, RC_MODES iRcMode)
    {
        WelsRcInitFuncPointers(pEncCtx, iRcMode);
        RcInitSequenceParameter(pEncCtx);
    }

    public static void WelsRcFreeMemory(sWelsEncCtx* pEncCtx)
    {
        SWelsSvcRc* pWelsSvcRc = null;
        int32_t i = 0;
        for (i = 0; i < pEncCtx->pSvcParam->iSpatialLayerNum; i++)
        {
            pWelsSvcRc = &pEncCtx->pWelsSvcRc[i];
            RcFreeLayerMemory(pWelsSvcRc, pEncCtx->pMemAlign);
        }
    }

    public static long GetTimestampForRc(long uiTimeStamp, long uiLastTimeStamp, float fFrameRate)
    {
        if ((uiLastTimeStamp >= uiTimeStamp) || ((uiTimeStamp == 0) && (uiLastTimeStamp != -1)))
        {
            return (uiLastTimeStamp + (int32_t)(1000.0 / fFrameRate));
        }
        return uiTimeStamp;
    }
}
