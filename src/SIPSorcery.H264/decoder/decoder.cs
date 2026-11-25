//-----------------------------------------------------------------------------
// Filename: decoder.cs
//
// Description: Port of codec/decoder/core/inc/decoder.h and
// src/decoder.cpp: decoder context defaults and the top level decode entry.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// Filled in as the decoder steps need it. So far (D1): the SPS/PPS and last
// decoded picture defaults, and GetVclNalTemporalId.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 25 Nov 2026  Claude          Created.
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
}
