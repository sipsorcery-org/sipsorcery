//-----------------------------------------------------------------------------
// Filename: svc_mode_decision.cs
//
// Description: Port of codec/encoder/core/src/svc_mode_decision.cpp, the
// camera half: the background skip decision with its chroma check, the
// background detection update, and the null screen content decisions. The
// spatial enhancement layer, screen content and mode merge decisions follow
// in Phase 4.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 26 Nov 2026  Claude          Created (E4).
// 26 Nov 2026  Claude          The background skip decision and the null SCD functions (E5).
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

using System.Runtime.CompilerServices;

namespace SIPSorcery.H264.Encoder;

internal static unsafe partial class WelsEnc
{
    // WelsMdSpatialelInterMbIlfmdNoilp, WelsMdInterMbEnhancelayer, GetRefMb and
    // SetMvBaseEnhancelayer (the spatial enhancement layers) come with Phase 4.

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int32_t GetChromaCost(PSampleSadSatdCostFunc* pCalculateFunc,
                                         uint8_t* pSrcChroma, int32_t iSrcStride, uint8_t* pRefChroma, int32_t iRefStride)
    {
        return pCalculateFunc[BLOCK_8x8](pSrcChroma, iSrcStride, pRefChroma, iRefStride);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsCostLessEqualSkipCost(int32_t iCurCost, int32_t iPredPskipSad, int32_t iRefMbType,
                                                SPicture* pRef, int32_t iMbXy, int32_t iSmallestInvisibleTh)
    {
        return ((iPredPskipSad > iSmallestInvisibleTh && iCurCost >= iPredPskipSad) ||
                (pRef->iPictureType == (int32_t)P_SLICE &&
                 iRefMbType == MB_TYPE_SKIP &&
                 pRef->pMbSkipSad[iMbXy] > iSmallestInvisibleTh &&
                 iCurCost >= (pRef->pMbSkipSad[iMbXy])));
    }

    private const int KNOWN_CHROMA_TOO_LARGE = 640;
    private const int SMALLEST_INVISIBLE = 128; //2*64, 2 in pixel maybe the smallest not visible for luma

    public static bool CheckChromaCost(sWelsEncCtx* pEncCtx, SWelsMD* pWelsMd, SMbCache* pMbCache, int32_t iCurMbXy)
    {
        PSampleSadSatdCostFunc* pSad = pEncCtx->pFuncList->sSampleDealingFuncs.pfSampleSad;
        SDqLayer* pCurDqLayer = pEncCtx->pCurDqLayer;

        uint8_t* pCbEnc = pMbCache->SPicData.pEncMb[1];
        uint8_t* pCrEnc = pMbCache->SPicData.pEncMb[2];
        uint8_t* pCbRef = pMbCache->SPicData.pRefMb[1];
        uint8_t* pCrRef = pMbCache->SPicData.pRefMb[2];

        int32_t iCbEncStride = pCurDqLayer->iEncStride[1];
        int32_t iCrEncStride = pCurDqLayer->iEncStride[2];
        int32_t iChromaRefStride = pCurDqLayer->pRefPic->iLineSize[1];

        int32_t iCbSad = GetChromaCost(pSad, pCbEnc, iCbEncStride, pCbRef, iChromaRefStride);
        int32_t iCrSad = GetChromaCost(pSad, pCrEnc, iCrEncStride, pCrRef, iChromaRefStride);

        // (The upstream notes on the visible chroma difference thresholds are omitted.)
        bool bChromaTooLarge = (iCbSad > KNOWN_CHROMA_TOO_LARGE || iCrSad > KNOWN_CHROMA_TOO_LARGE);

        int32_t iChromaSad = iCbSad + iCrSad;
        PredictSadSkip(pMbCache->sMvComponents.iRefIndexCache, pMbCache->bMbTypeSkip, pMbCache->iSadCostSkip, 0,
                       &(pWelsMd->iSadPredSkip));
        bool bChromaCostCannotSkip = IsCostLessEqualSkipCost(iChromaSad, pWelsMd->iSadPredSkip, (int32_t)pMbCache->uiRefMbType,
                                                             pCurDqLayer->pRefPic, iCurMbXy, SMALLEST_INVISIBLE);

        return (!bChromaCostCannotSkip && !bChromaTooLarge);
    }

    //01/17/2013. USE the NEW BGD Pskip with COLOR CHECK for screen content and camera because of color artifact seen in test
    public static bool WelsMdInterJudgeBGDPskip(sWelsEncCtx* pEncCtx, SWelsMD* pWelsMd, SSlice* pSlice, SMB* pCurMb, SMbCache* pMbCache,
                                                bool* bKeepSkip)
    {
        SDqLayer* pCurDqLayer = pEncCtx->pCurDqLayer;

        int32_t kiRefMbQp = pCurDqLayer->pRefPic->pRefMbQp[pCurMb->iMbXY];
        int32_t kiCurMbQp = pCurMb->uiLumaQp;// unsigned -> signed
        int8_t* pVaaBgMbFlag = pEncCtx->pVaa->pVaaBackgroundMbFlag + pCurMb->iMbXY;

        int32_t kiMbWidth = pCurDqLayer->iMbWidth;

        *bKeepSkip = (*bKeepSkip) &&
                     ((pVaaBgMbFlag[-1] == 0) &&
                      (pVaaBgMbFlag[-kiMbWidth] == 0) &&
                      (pVaaBgMbFlag[-kiMbWidth + 1] == 0));

        if (
            *pVaaBgMbFlag != 0
            && !IS_INTRA(pMbCache->uiRefMbType)
            && (kiRefMbQp - kiCurMbQp <= DELTA_QP_BGD_THD || kiRefMbQp <= 26)
        )
        {
            // (The upstream note on plain-chroma background blocks in screen content is omitted.)
            if (CheckChromaCost(pEncCtx, pWelsMd, pMbCache, pCurMb->iMbXY))
            {
                SMVUnitXY sVaaPredSkipMv = default;
                PredSkipMv(pMbCache, &sVaaPredSkipMv);
                WelsMdBackgroundMbEnc(pEncCtx, pWelsMd, pCurMb, pMbCache, pSlice, (LD32(&sVaaPredSkipMv) == 0));
                return true;
            }
        }

        return false;
    }

    public static bool WelsMdInterJudgeBGDPskipFalse(sWelsEncCtx* pCtx, SWelsMD* pMd, SSlice* pSlice, SMB* pCurMb, SMbCache* pMbCache,
                                                     bool* bKeepSkip)
    {
        return false;
    }



    //////
    //  update BGD related info
    //////
    public static void WelsMdUpdateBGDInfo(SDqLayer* pCurLayer, SMB* pCurMb, bool bCollocatedPredFlag,
                                           int32_t iRefPictureType)
    {
        uint8_t* pTargetRefMbQpList = (pCurLayer->pDecPic->pRefMbQp);
        int32_t kiMbXY = pCurMb->iMbXY;

        if (pCurMb->uiCbp != 0 || (int32_t)I_SLICE == iRefPictureType || !bCollocatedPredFlag)
        {
            pTargetRefMbQpList[kiMbXY] = pCurMb->uiLumaQp;
        }
        else
        { //unchange, do not need to evaluation?
            uint8_t* pRefPicRefMbQpList = (pCurLayer->pRefPic->pRefMbQp);
            pTargetRefMbQpList[kiMbXY] = pRefPicRefMbQpList[kiMbXY];
        }

        if (pCurMb->uiMbType == MB_TYPE_BACKGROUND)
        {
            pCurMb->uiMbType = MB_TYPE_SKIP;
        }
    }

    public static void WelsMdUpdateBGDInfoNULL(SDqLayer* pCurLayer, SMB* pCurMb, bool bCollocatedPredFlag,
                                               int32_t iRefPictureType)
    {
        WelsMdUpdateBGDInfo(pCurLayer, pCurMb, bCollocatedPredFlag, iRefPictureType);
    }

    // The screen content decisions (IsMbStatic .. WelsMdInterJudgeSCDPskip) come with Phase 4.

    //////////////
    // MD for screen contents
    public static bool WelsMdInterJudgeSCDPskipFalse(sWelsEncCtx* pEncCtx, SWelsMD* pWelsMd, SSlice* slice, SMB* pCurMb,
                                                     SMbCache* pMbCache)
    {
        return false;
    }


    public static void WelsInitSCDPskipFunc(SWelsFuncPtrList* pFuncList, bool bScrollingDetection)
    {
        if (bScrollingDetection)
        {
            // pFuncList->pfSCDPSkipDecision = WelsMdInterJudgeSCDPskip;
            throw new NotSupportedException("WelsInitSCDPskipFunc: screen content scrolling detection is not ported yet (Phase 4).");
        }
        else
        {
            pFuncList->pfSCDPSkipDecision = &WelsMdInterJudgeSCDPskipFalse;
        }
    }

    // MergeSub16Me, IsSameMv, TryModeMerge, WelsMdInterFinePartitionVaaOnScreen and
    // SetScrollingMvToMd (screen content) come with Phase 4.

    ///////////////////////
    // SubP16x16 Mode Decision for screen content
    ////////////////////////
    //
    public static void SetScrollingMvToMdNull(SVAAFrameInfo* pVaa, SWelsMD* pWelsMd)
    {
    }
}
