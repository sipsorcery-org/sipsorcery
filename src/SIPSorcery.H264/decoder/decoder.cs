//-----------------------------------------------------------------------------
// Filename: decoder.cs
//
// Description: Port of codec/decoder/core/inc/decoder.h and
// src/decoder.cpp: decoder context defaults and the top level decode entry.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// Filled in as the decoder steps need it. So far: the SPS/PPS and last
// decoded picture defaults and GetVclNalTemporalId (D1), and InitPredFunc
// with its IdctFourResAddPred_ helper (D3). InitPredFunc fills only the C
// kernels; the SIMD branches are not ported, so uiCpuFlag is unused.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 25 Nov 2026  Claude          Created.
// 25 Nov 2026  Claude          D3: InitPredFunc.
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
 * \file    decoder.c
 *
 * \brief   Interfaces implementation introduced in decoder system architecture
 *
 * \date    03/10/2009 Created
 *
 *************************************************************************************
 */

namespace SIPSorcery.H264.Decoder;

internal static unsafe partial class WelsDec
{
    /*
    * fill data fields in SPS and PPS default for decoder context
    */
    public static void WelsDecoderSpsPpsDefaults(SWelsDecoderSpsPpsCTX* sSpsPpsCtx)
    {
        sSpsPpsCtx->bSpsExistAheadFlag = false;
        sSpsPpsCtx->bSubspsExistAheadFlag = false;
        sSpsPpsCtx->bPpsExistAheadFlag = false;
        sSpsPpsCtx->bAvcBasedFlag = true;
        sSpsPpsCtx->iSpsErrorIgnored = 0;
        sSpsPpsCtx->iSubSpsErrorIgnored = 0;
        sSpsPpsCtx->iPpsErrorIgnored = 0;
        sSpsPpsCtx->iPPSInvalidNum = 0;
        sSpsPpsCtx->iPPSLastInvalidId = -1;
        sSpsPpsCtx->iSPSInvalidNum = 0;
        sSpsPpsCtx->iSPSLastInvalidId = -1;
        sSpsPpsCtx->iSubSPSInvalidNum = 0;
        sSpsPpsCtx->iSubSPSLastInvalidId = -1;
        sSpsPpsCtx->iSeqId = -1;
    }

    /*
    * fill last decoded picture info
    */
    public static void WelsDecoderLastDecPicInfoDefaults(SWelsLastDecPicInfo* sLastDecPicInfo)
    {
        sLastDecPicInfo->iPrevPicOrderCntMsb = 0;
        sLastDecPicInfo->iPrevPicOrderCntLsb = 0;
        sLastDecPicInfo->pPreviousDecodedPictureInDpb = null;
        sLastDecPicInfo->iPrevFrameNum = -1;
        sLastDecPicInfo->bLastHasMmco5 = false;
        sLastDecPicInfo->uiDecodingTimeStamp = 0;
    }

    public static void GetVclNalTemporalId(SWelsDecoderContext* pCtx)
    {
        SAccessUnit* pAccessUnit = pCtx->pAccessUnitList;
        int32_t idx = (int32_t)pAccessUnit->uiStartPos;

        pCtx->iFeedbackVclNalInAu = (int32_t)FEEDBACK_VCL_NAL;
        pCtx->iFeedbackTidInAu = pAccessUnit->pNalUnitsList[idx]->sNalHeaderExt.uiTemporalId;
        pCtx->iFeedbackNalRefIdc = pAccessUnit->pNalUnitsList[idx]->sNalHeaderExt.sNalUnitHeader.uiNalRefIdc;
    }

    // template<void pfIdctResAddPred (uint8_t* pPred, int32_t iStride, int16_t* pRs)>
    // void IdctFourResAddPred_ (uint8_t* pPred, int32_t iStride, int16_t* pRs, const int8_t* pNzc)
    // in an anonymous namespace upstream. Only the IdctResAddPred_c instantiation
    // is used without SIMD, so it is written out with that kernel.
    public static void IdctFourResAddPred_(uint8_t* pPred, int32_t iStride, int16_t* pRs, int8_t* pNzc)
    {
        if (pNzc[0] != 0 || pRs[0 * 16] != 0)
            IdctResAddPred_c(pPred + 0 * iStride + 0, iStride, pRs + 0 * 16);
        if (pNzc[1] != 0 || pRs[1 * 16] != 0)
            IdctResAddPred_c(pPred + 0 * iStride + 4, iStride, pRs + 1 * 16);
        if (pNzc[4] != 0 || pRs[2 * 16] != 0)
            IdctResAddPred_c(pPred + 4 * iStride + 0, iStride, pRs + 2 * 16);
        if (pNzc[5] != 0 || pRs[3 * 16] != 0)
            IdctResAddPred_c(pPred + 4 * iStride + 4, iStride, pRs + 3 * 16);
    }

    public static void InitPredFunc(SWelsDecoderContext* pCtx, uint32_t uiCpuFlag)
    {
        pCtx->pGetI16x16LumaPredFunc[I16_PRED_V] = &WelsI16x16LumaPredV_c;
        pCtx->pGetI16x16LumaPredFunc[I16_PRED_H] = &WelsI16x16LumaPredH_c;
        pCtx->pGetI16x16LumaPredFunc[I16_PRED_DC] = &WelsI16x16LumaPredDc_c;
        pCtx->pGetI16x16LumaPredFunc[I16_PRED_P] = &WelsI16x16LumaPredPlane_c;
        pCtx->pGetI16x16LumaPredFunc[I16_PRED_DC_L] = &WelsI16x16LumaPredDcLeft_c;
        pCtx->pGetI16x16LumaPredFunc[I16_PRED_DC_T] = &WelsI16x16LumaPredDcTop_c;
        pCtx->pGetI16x16LumaPredFunc[I16_PRED_DC_128] = &WelsI16x16LumaPredDcNA_c;

        pCtx->pGetI4x4LumaPredFunc[I4_PRED_V] = &WelsI4x4LumaPredV_c;
        pCtx->pGetI4x4LumaPredFunc[I4_PRED_H] = &WelsI4x4LumaPredH_c;
        pCtx->pGetI4x4LumaPredFunc[I4_PRED_DC] = &WelsI4x4LumaPredDc_c;
        pCtx->pGetI4x4LumaPredFunc[I4_PRED_DC_L] = &WelsI4x4LumaPredDcLeft_c;
        pCtx->pGetI4x4LumaPredFunc[I4_PRED_DC_T] = &WelsI4x4LumaPredDcTop_c;
        pCtx->pGetI4x4LumaPredFunc[I4_PRED_DC_128] = &WelsI4x4LumaPredDcNA_c;
        pCtx->pGetI4x4LumaPredFunc[I4_PRED_DDL] = &WelsI4x4LumaPredDDL_c;
        pCtx->pGetI4x4LumaPredFunc[I4_PRED_DDL_TOP] = &WelsI4x4LumaPredDDLTop_c;
        pCtx->pGetI4x4LumaPredFunc[I4_PRED_DDR] = &WelsI4x4LumaPredDDR_c;
        pCtx->pGetI4x4LumaPredFunc[I4_PRED_VL] = &WelsI4x4LumaPredVL_c;
        pCtx->pGetI4x4LumaPredFunc[I4_PRED_VL_TOP] = &WelsI4x4LumaPredVLTop_c;
        pCtx->pGetI4x4LumaPredFunc[I4_PRED_VR] = &WelsI4x4LumaPredVR_c;
        pCtx->pGetI4x4LumaPredFunc[I4_PRED_HU] = &WelsI4x4LumaPredHU_c;
        pCtx->pGetI4x4LumaPredFunc[I4_PRED_HD] = &WelsI4x4LumaPredHD_c;

        pCtx->pGetI8x8LumaPredFunc[I4_PRED_V] = &WelsI8x8LumaPredV_c;
        pCtx->pGetI8x8LumaPredFunc[I4_PRED_H] = &WelsI8x8LumaPredH_c;
        pCtx->pGetI8x8LumaPredFunc[I4_PRED_DC] = &WelsI8x8LumaPredDc_c;
        pCtx->pGetI8x8LumaPredFunc[I4_PRED_DC_L] = &WelsI8x8LumaPredDcLeft_c;
        pCtx->pGetI8x8LumaPredFunc[I4_PRED_DC_T] = &WelsI8x8LumaPredDcTop_c;
        pCtx->pGetI8x8LumaPredFunc[I4_PRED_DC_128] = &WelsI8x8LumaPredDcNA_c;
        pCtx->pGetI8x8LumaPredFunc[I4_PRED_DDL] = &WelsI8x8LumaPredDDL_c;
        pCtx->pGetI8x8LumaPredFunc[I4_PRED_DDL_TOP] = &WelsI8x8LumaPredDDLTop_c;
        pCtx->pGetI8x8LumaPredFunc[I4_PRED_DDR] = &WelsI8x8LumaPredDDR_c;
        pCtx->pGetI8x8LumaPredFunc[I4_PRED_VL] = &WelsI8x8LumaPredVL_c;
        pCtx->pGetI8x8LumaPredFunc[I4_PRED_VL_TOP] = &WelsI8x8LumaPredVLTop_c;
        pCtx->pGetI8x8LumaPredFunc[I4_PRED_VR] = &WelsI8x8LumaPredVR_c;
        pCtx->pGetI8x8LumaPredFunc[I4_PRED_HU] = &WelsI8x8LumaPredHU_c;
        pCtx->pGetI8x8LumaPredFunc[I4_PRED_HD] = &WelsI8x8LumaPredHD_c;

        pCtx->pGetIChromaPredFunc[C_PRED_DC] = &WelsIChromaPredDc_c;
        pCtx->pGetIChromaPredFunc[C_PRED_H] = &WelsIChromaPredH_c;
        pCtx->pGetIChromaPredFunc[C_PRED_V] = &WelsIChromaPredV_c;
        pCtx->pGetIChromaPredFunc[C_PRED_P] = &WelsIChromaPredPlane_c;
        pCtx->pGetIChromaPredFunc[C_PRED_DC_L] = &WelsIChromaPredDcLeft_c;
        pCtx->pGetIChromaPredFunc[C_PRED_DC_T] = &WelsIChromaPredDcTop_c;
        pCtx->pGetIChromaPredFunc[C_PRED_DC_128] = &WelsIChromaPredDcNA_c;

        pCtx->pIdctResAddPredFunc = &IdctResAddPred_c;
        pCtx->pIdctFourResAddPredFunc = &IdctFourResAddPred_;

        pCtx->pIdctResAddPredFunc8x8 = &IdctResAddPred8x8_c;
    }
}
