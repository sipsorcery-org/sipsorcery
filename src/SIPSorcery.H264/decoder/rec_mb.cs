//-----------------------------------------------------------------------------
// Filename: rec_mb.cs
//
// Description: Port of codec/decoder/core/inc/rec_mb.h and src/rec_mb.cpp:
// macroblock reconstruction after parsing, prediction plus inverse transformed
// residual.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// So far (D3) the intra half: WelsFillRecNeededMbInfo, the 4x4, 8x8 and
// 16x16 intra macroblocks and the chroma residual (RecChroma). The inter half
// (sMCRefMember, GetRefPic, BaseMC, the weighted and bi-prediction helpers,
// GetInterPred, GetInterBPred) comes with the P-slice step.
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
 * \file    rec_mb.c
 *
 * \brief   implementation for all macroblock decoding process after mb syntax parsing and residual decoding with cavlc.
 *
 * \date    3/18/2009 Created
 *
 *************************************************************************************
 */

namespace SIPSorcery.H264.Decoder;

internal static unsafe partial class WelsDec
{
    public static void WelsFillRecNeededMbInfo(SWelsDecoderContext* pCtx, bool bOutput, SDqLayer* pCurDqLayer)
    {
        SPicture* pCurPic = pCtx->pDec;
        int32_t iLumaStride = pCurPic->iLinesize[0];
        int32_t iChromaStride = pCurPic->iLinesize[1];
        int32_t iMbX = pCurDqLayer->iMbX;
        int32_t iMbY = pCurDqLayer->iMbY;

        pCurDqLayer->iLumaStride = iLumaStride;
        pCurDqLayer->iChromaStride = iChromaStride;

        if (bOutput)
        {
            pCurDqLayer->pPred[0] = pCurPic->pData[0] + ((iMbY * iLumaStride + iMbX) << 4);
            pCurDqLayer->pPred[1] = pCurPic->pData[1] + ((iMbY * iChromaStride + iMbX) << 3);
            pCurDqLayer->pPred[2] = pCurPic->pData[2] + ((iMbY * iChromaStride + iMbX) << 3);
        }
    }

    public static int32_t RecI8x8Mb(int32_t iMbXy, SWelsDecoderContext* pCtx, int16_t* pScoeffLevel, SDqLayer* pDqLayer)
    {
        RecI8x8Luma(iMbXy, pCtx, pScoeffLevel, pDqLayer);
        RecI4x4Chroma(iMbXy, pCtx, pScoeffLevel, pDqLayer);
        return ERR_NONE;
    }

    public static int32_t RecI8x8Luma(int32_t iMbXy, SWelsDecoderContext* pCtx, int16_t* pScoeffLevel, SDqLayer* pDqLayer)
    {
        /*****get local variable from outer variable********/
        /*prediction info*/
        uint8_t* pPred = pDqLayer->pPred[0];

        int32_t iLumaStride = pDqLayer->iLumaStride;
        int32_t* pBlockOffset = pCtx->iDecBlockOffsetArray;
        PGetIntraPred8x8Func* pGetI8x8LumaPredFunc = pCtx->pGetI8x8LumaPredFunc;

        int8_t* pIntra8x8PredMode = (int8_t*)&pDqLayer->pIntra4x4FinalMode[iMbXy]; // I_NxN
        int16_t* pRS = pScoeffLevel;
        /*itransform info*/
        PIdctResAddPredFunc pIdctResAddPredFunc = pCtx->pIdctResAddPredFunc8x8;

        /*************local variable********************/
        uint8_t i = 0;
        bool* bTLAvail = stackalloc bool[4];
        bool* bTRAvail = stackalloc bool[4];
        // Top-Right : Left : Top-Left : Top
        bTLAvail[0] = (pDqLayer->pIntraNxNAvailFlag[iMbXy] & 0x02) != 0;
        bTLAvail[1] = (pDqLayer->pIntraNxNAvailFlag[iMbXy] & 0x01) != 0;
        bTLAvail[2] = (pDqLayer->pIntraNxNAvailFlag[iMbXy] & 0x04) != 0;
        bTLAvail[3] = true;

        bTRAvail[0] = (pDqLayer->pIntraNxNAvailFlag[iMbXy] & 0x01) != 0;
        bTRAvail[1] = (pDqLayer->pIntraNxNAvailFlag[iMbXy] & 0x08) != 0;
        bTRAvail[2] = true;
        bTRAvail[3] = false;

        /*************real process*********************/
        for (i = 0; i < 4; i++)
        {

            uint8_t* pPredI8x8 = pPred + pBlockOffset[i << 2];
            uint8_t uiMode = (uint8_t)pIntra8x8PredMode[g_kuiScan4[i << 2]];

            pGetI8x8LumaPredFunc[uiMode](pPredI8x8, iLumaStride, bTLAvail[i], bTRAvail[i]);

            int32_t iIndex = g_kuiMbCountScan4Idx[i << 2];
            if (pDqLayer->pNzc[iMbXy][iIndex] != 0 || pDqLayer->pNzc[iMbXy][iIndex + 1] != 0 || pDqLayer->pNzc[iMbXy][iIndex + 4] != 0
                || pDqLayer->pNzc[iMbXy][iIndex + 5] != 0)
            {
                int16_t* pRSI8x8 = &pRS[i << 6];
                pIdctResAddPredFunc(pPredI8x8, iLumaStride, pRSI8x8);
            }
        }

        return ERR_NONE;
    }

    public static int32_t RecI4x4Mb(int32_t iMBXY, SWelsDecoderContext* pCtx, int16_t* pScoeffLevel, SDqLayer* pDqLayer)
    {
        RecI4x4Luma(iMBXY, pCtx, pScoeffLevel, pDqLayer);
        RecI4x4Chroma(iMBXY, pCtx, pScoeffLevel, pDqLayer);
        return ERR_NONE;
    }


    public static int32_t RecI4x4Luma(int32_t iMBXY, SWelsDecoderContext* pCtx, int16_t* pScoeffLevel, SDqLayer* pDqLayer)
    {
        /*****get local variable from outer variable********/
        /*prediction info*/
        uint8_t* pPred = pDqLayer->pPred[0];

        int32_t iLumaStride = pDqLayer->iLumaStride;
        int32_t* pBlockOffset = pCtx->iDecBlockOffsetArray;
        PGetIntraPredFunc* pGetI4x4LumaPredFunc = pCtx->pGetI4x4LumaPredFunc;

        int8_t* pIntra4x4PredMode = (int8_t*)&pDqLayer->pIntra4x4FinalMode[iMBXY];
        int16_t* pRS = pScoeffLevel;
        /*itransform info*/
        PIdctResAddPredFunc pIdctResAddPredFunc = pCtx->pIdctResAddPredFunc;


        /*************local variable********************/
        uint8_t i = 0;

        /*************real process*********************/
        for (i = 0; i < 16; i++)
        {

            uint8_t* pPredI4x4 = pPred + pBlockOffset[i];
            uint8_t uiMode = (uint8_t)pIntra4x4PredMode[g_kuiScan4[i]];

            pGetI4x4LumaPredFunc[uiMode](pPredI4x4, iLumaStride);

            if (pDqLayer->pNzc[iMBXY][g_kuiMbCountScan4Idx[i]] != 0)
            {
                int16_t* pRSI4x4 = &pRS[i << 4];
                pIdctResAddPredFunc(pPredI4x4, iLumaStride, pRSI4x4);
            }
        }

        return ERR_NONE;
    }


    public static int32_t RecI4x4Chroma(int32_t iMBXY, SWelsDecoderContext* pCtx, int16_t* pScoeffLevel, SDqLayer* pDqLayer)
    {
        int32_t iChromaStride = pCtx->pCurDqLayer->pDec->iLinesize[1];

        int8_t iChromaPredMode = pDqLayer->pChromaPredMode[iMBXY];

        PGetIntraPredFunc* pGetIChromaPredFunc = pCtx->pGetIChromaPredFunc;

        uint8_t* pPred = pDqLayer->pPred[1];

        pGetIChromaPredFunc[iChromaPredMode](pPred, iChromaStride);
        pPred = pDqLayer->pPred[2];
        pGetIChromaPredFunc[iChromaPredMode](pPred, iChromaStride);

        RecChroma(iMBXY, pCtx, pScoeffLevel, pDqLayer);

        return ERR_NONE;
    }


    public static int32_t RecI16x16Mb(int32_t iMBXY, SWelsDecoderContext* pCtx, int16_t* pScoeffLevel, SDqLayer* pDqLayer)
    {
        /*decoder use, encoder no use*/
        int8_t iI16x16PredMode = pDqLayer->pIntraPredMode[iMBXY][7];
        int8_t iChromaPredMode = pDqLayer->pChromaPredMode[iMBXY];
        PGetIntraPredFunc* pGetIChromaPredFunc = pCtx->pGetIChromaPredFunc;
        PGetIntraPredFunc* pGetI16x16LumaPredFunc = pCtx->pGetI16x16LumaPredFunc;
        int32_t iUVStride = pCtx->pCurDqLayer->pDec->iLinesize[1];

        /*common use by decoder&encoder*/
        int32_t iYStride = pDqLayer->iLumaStride;
        int16_t* pRS = pScoeffLevel;

        uint8_t* pPred = pDqLayer->pPred[0];

        PIdctFourResAddPredFunc pIdctFourResAddPredFunc = pCtx->pIdctFourResAddPredFunc;

        /*decode i16x16 y*/
        pGetI16x16LumaPredFunc[iI16x16PredMode](pPred, iYStride);

        /*1 mb is divided 16 4x4_block to idct*/
        int8_t* pNzc = (int8_t*)&pDqLayer->pNzc[iMBXY];
        pIdctFourResAddPredFunc(pPred + 0 * iYStride + 0, iYStride, pRS + 0 * 64, pNzc + 0);
        pIdctFourResAddPredFunc(pPred + 0 * iYStride + 8, iYStride, pRS + 1 * 64, pNzc + 2);
        pIdctFourResAddPredFunc(pPred + 8 * iYStride + 0, iYStride, pRS + 2 * 64, pNzc + 8);
        pIdctFourResAddPredFunc(pPred + 8 * iYStride + 8, iYStride, pRS + 3 * 64, pNzc + 10);

        /*decode intra mb cb&cr*/
        pPred = pDqLayer->pPred[1];
        pGetIChromaPredFunc[iChromaPredMode](pPred, iUVStride);
        pPred = pDqLayer->pPred[2];
        pGetIChromaPredFunc[iChromaPredMode](pPred, iUVStride);
        RecChroma(iMBXY, pCtx, pScoeffLevel, pDqLayer);

        return ERR_NONE;
    }

    public static int32_t RecChroma(int32_t iMBXY, SWelsDecoderContext* pCtx, int16_t* pScoeffLevel, SDqLayer* pDqLayer)
    {
        int32_t iChromaStride = pCtx->pCurDqLayer->pDec->iLinesize[1];
        PIdctFourResAddPredFunc pIdctFourResAddPredFunc = pCtx->pIdctFourResAddPredFunc;

        uint8_t i = 0;
        uint8_t uiCbpC = (uint8_t)(pDqLayer->pCbp[iMBXY] >> 4);

        if (1 == uiCbpC || 2 == uiCbpC)
        {
            for (i = 0; i < 2; i++)
            {
                int16_t* pRS = pScoeffLevel + 256 + (i << 6);
                uint8_t* pPred = pDqLayer->pPred[i + 1];
                int8_t* pNzc = (int8_t*)&pDqLayer->pNzc[iMBXY] + 16 + 2 * i;

                /*1 chroma is divided 4 4x4_block to idct*/
                pIdctFourResAddPredFunc(pPred, iChromaStride, pRS, pNzc);
            }
        }

        return ERR_NONE;
    }
}
