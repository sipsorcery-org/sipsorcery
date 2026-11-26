//-----------------------------------------------------------------------------
// Filename: ratectl.cs
//
// Description: Port of codec/encoder/core/src/ratectl.cpp, part 1 (E4): the
// rate control off path (RcCalculateCascadingQp and the four Disable
// functions), WelsRcInitFuncPointers for RC_OFF_MODE, and GomRCInitForOneSlice
// (E4b), which WelsCodeOneSlice calls when GOM rate control is on. The bit rate,
// buffer based, quality and timestamp modes follow in E6, so
// WelsRcInitFuncPointers rejects them for now.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// The adaptive quantisation branches only run for P slices and read the VAA
// state, which comes with E5; they throw NotSupportedException until then.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 26 Nov 2026  Claude          Created (E4).
// 26 Nov 2026  Claude          GomRCInitForOneSlice (E4b).
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
    public static void GomRCInitForOneSlice(SSlice* pSlice, int32_t kiBitsPerMb)
    {
        SRCSlicing* pSOverRc = &pSlice->sSlicingOverRc;
        pSOverRc->iStartMbSlice = pSlice->sSliceHeaderExt.sSliceHeader.iFirstMbInSlice;
        pSOverRc->iEndMbSlice = pSOverRc->iStartMbSlice + pSlice->iCountMbNumInSlice - 1;
        pSOverRc->iTargetBitsSlice = WELS_DIV_ROUND((int64_t)kiBitsPerMb * pSlice->iCountMbNumInSlice,
                                     INT_MULTIPLY);
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

    public static void WelsRcPictureInitDisable(sWelsEncCtx* pEncCtx, long uiTimeStamp)
    {
        SWelsSvcRc* pWelsSvcRc = &pEncCtx->pWelsSvcRc[pEncCtx->uiDependencyId];
        SSpatialLayerConfig* pDLayerParam = &pEncCtx->pSvcParam->sSpatialLayers[pEncCtx->uiDependencyId];
        int32_t kiQp = pDLayerParam->iDLayerQp;

        pEncCtx->iGlobalQp = RcCalculateCascadingQp(pEncCtx, kiQp);

        if (pEncCtx->pSvcParam->bEnableAdaptiveQuant && (pEncCtx->eSliceType == P_SLICE))
        {
            // pEncCtx->iGlobalQp = WELS_CLIP3 ((pEncCtx->iGlobalQp * INT_MULTIPLY -
            //     pEncCtx->pVaa->sAdaptiveQuantParam.iAverMotionTextureIndexToDeltaQp) / INT_MULTIPLY, pWelsSvcRc->iMinQp,
            //     pWelsSvcRc->iMaxQp);
            throw new NotSupportedException("WelsRcPictureInitDisable: adaptive quantisation needs the VAA state, which comes with E5.");
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
            // iLumaQp   = (int8_t)WELS_CLIP3 (iLumaQp +
            //     pEncCtx->pVaa->sAdaptiveQuantParam.pMotionTextureIndexToDeltaQp[pCurMb->iMbXY], pWelsSvcRc->iMinQp, pWelsSvcRc->iMaxQp);
            throw new NotSupportedException("WelsRcMbInitDisable: adaptive quantisation needs the VAA state, which comes with E5.");
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
            default:
                // RC_BUFFERBASED_MODE, RC_BITRATE_MODE, RC_BITRATE_MODE_POST_SKIP, RC_TIMESTAMP_MODE
                // and RC_QUALITY_MODE come with E6.
                throw new NotSupportedException($"WelsRcInitFuncPointers: rate control mode {iRcMode} is not ported yet.");
        }
    }
}
