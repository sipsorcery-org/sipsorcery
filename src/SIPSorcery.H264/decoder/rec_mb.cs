//-----------------------------------------------------------------------------
// Filename: rec_mb.cs
//
// Description: Port of codec/decoder/core/inc/rec_mb.h and src/rec_mb.cpp:
// macroblock reconstruction after parsing, prediction plus inverse transformed
// residual.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// D3 ported the intra half: WelsFillRecNeededMbInfo, the 4x4, 8x8 and 16x16
// intra macroblocks and the chroma residual (RecChroma). D4 adds the P half:
// sMCRefMember, GetRefPic, BaseMC, WeightPrediction and GetInterPred. The B
// slice helpers (BiWeightPrediction, BiPrediction, GetInterBPred) are not
// ported yet. BaseMC's threaded-decoder wait for the reference rows is left
// out, as the threaded decoder is.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 25 Nov 2026  Claude          Created.
// 25 Nov 2026  Claude          D4: P macroblock inter prediction.
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

/// <summary>TagMCRefMember (sMCRefMember).</summary>
internal unsafe struct sMCRefMember
{
    public uint8_t* pDstY;
    public uint8_t* pDstU;
    public uint8_t* pDstV;

    public uint8_t* pSrcY;
    public uint8_t* pSrcU;
    public uint8_t* pSrcV;

    public int32_t iSrcLineLuma;
    public int32_t iSrcLineChroma;

    public int32_t iDstLineLuma;
    public int32_t iDstLineChroma;

    public int32_t iPicWidth;
    public int32_t iPicHeight;
}

internal static unsafe partial class WelsDec
{
    // #define WELS_B_MB_REC_VERIFY(uiRet): return (uint32_t)uiRet when it is not
    // ERR_NONE. Written out at each use as
    // { uint32_t uiRetTmp = (uint32_t)(...); if (uiRetTmp != ERR_NONE) return (int32_t)uiRetTmp; }

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

    //according to current 8*8 block ref_index to gain reference picture
    private static int32_t GetRefPic(sMCRefMember* pMCRefMem, SWelsDecoderContext* pCtx, int8_t iRefIdx,
        int32_t listIdx)
    {
        SPicture* pRefPic;

        if (iRefIdx >= 0)
        {
            pRefPic = pCtx->sRefPic.pRefList[listIdx][iRefIdx];

            if (pRefPic != null)
            {
                pMCRefMem->iSrcLineLuma = pRefPic->iLinesize[0];
                pMCRefMem->iSrcLineChroma = pRefPic->iLinesize[1];

                pMCRefMem->pSrcY = pRefPic->pData[0];
                pMCRefMem->pSrcU = pRefPic->pData[1];
                pMCRefMem->pSrcV = pRefPic->pData[2];
                if (pMCRefMem->pSrcY == null || pMCRefMem->pSrcU == null || pMCRefMem->pSrcV == null)
                {
                    return GENERATE_ERROR_NO(ERR_LEVEL_SLICE_DATA, ERR_INFO_REFERENCE_PIC_LOST);
                }
                return ERR_NONE;
            }
        }
        return GENERATE_ERROR_NO(ERR_LEVEL_SLICE_DATA, ERR_INFO_REFERENCE_PIC_LOST);
    }

    // MC_FLOW_SIMPLE_JUDGE is defined to 1 upstream and used nowhere.
    public static void BaseMC(SWelsDecoderContext* pCtx, sMCRefMember* pMCRefMem, int32_t listIdx, int8_t iRefIdx,
        int32_t iXOffset, int32_t iYOffset,
        SMcFunc* pMCFunc,
        int32_t iBlkWidth, int32_t iBlkHeight, int16_t* iMVs)
    {
        int32_t iFullMVx = (iXOffset << 2) + iMVs[0]; //quarter pixel
        int32_t iFullMVy = (iYOffset << 2) + iMVs[1];
        iFullMVx = WELS_CLIP3(iFullMVx, ((-PADDING_LENGTH + 2) * (1 << 2)),
            ((pMCRefMem->iPicWidth + PADDING_LENGTH - 19) * (1 << 2)));
        iFullMVy = WELS_CLIP3(iFullMVy, ((-PADDING_LENGTH + 2) * (1 << 2)),
            ((pMCRefMem->iPicHeight + PADDING_LENGTH - 19) * (1 << 2)));

        // GetThreadCount (pCtx) > 1 && iRefIdx >= 0: waits for the reference rows
        // in the threaded decoder, which is not ported.

        int32_t iSrcPixOffsetLuma = (iFullMVx >> 2) + (iFullMVy >> 2) * pMCRefMem->iSrcLineLuma;
        int32_t iSrcPixOffsetChroma = (iFullMVx >> 3) + (iFullMVy >> 3) * pMCRefMem->iSrcLineChroma;

        int32_t iBlkWidthChroma = iBlkWidth >> 1;
        int32_t iBlkHeightChroma = iBlkHeight >> 1;

        uint8_t* pSrcY = pMCRefMem->pSrcY + iSrcPixOffsetLuma;
        uint8_t* pSrcU = pMCRefMem->pSrcU + iSrcPixOffsetChroma;
        uint8_t* pSrcV = pMCRefMem->pSrcV + iSrcPixOffsetChroma;
        uint8_t* pDstY = pMCRefMem->pDstY;
        uint8_t* pDstU = pMCRefMem->pDstU;
        uint8_t* pDstV = pMCRefMem->pDstV;

        // The MC functions take int16_t motion vectors; C narrows implicitly.
        pMCFunc->pMcLumaFunc(pSrcY, pMCRefMem->iSrcLineLuma, pDstY, pMCRefMem->iDstLineLuma, (int16_t)iFullMVx, (int16_t)iFullMVy, iBlkWidth,
            iBlkHeight);
        pMCFunc->pMcChromaFunc(pSrcU, pMCRefMem->iSrcLineChroma, pDstU, pMCRefMem->iDstLineChroma, (int16_t)iFullMVx, (int16_t)iFullMVy,
            iBlkWidthChroma, iBlkHeightChroma);
        pMCFunc->pMcChromaFunc(pSrcV, pMCRefMem->iSrcLineChroma, pDstV, pMCRefMem->iDstLineChroma, (int16_t)iFullMVx, (int16_t)iFullMVy,
            iBlkWidthChroma, iBlkHeightChroma);
    }

    private static void WeightPrediction(SDqLayer* pCurDqLayer, sMCRefMember* pMCRefMem, int32_t listIdx, int32_t iRefIdx,
        int32_t iBlkWidth,
        int32_t iBlkHeight)
    {
        int32_t iLog2denom, iWoc, iOoc;
        int32_t iPredTemp, iLineStride;
        int32_t iPixel = 0;
        uint8_t* pDst;
        //luma
        iLog2denom = (int32_t)pCurDqLayer->pPredWeightTable->uiLumaLog2WeightDenom;
        iWoc = pCurDqLayer->pPredWeightTable->sPredList[listIdx].iLumaWeight[iRefIdx];
        iOoc = pCurDqLayer->pPredWeightTable->sPredList[listIdx].iLumaOffset[iRefIdx];
        iLineStride = pMCRefMem->iDstLineLuma;

        for (int i = 0; i < iBlkHeight; i++)
        {
            for (int j = 0; j < iBlkWidth; j++)
            {
                iPixel = j + i * (iLineStride);
                if (iLog2denom >= 1)
                {
                    iPredTemp = ((pMCRefMem->pDstY[iPixel] * iWoc + (1 << (iLog2denom - 1))) >> iLog2denom) + iOoc;

                    pMCRefMem->pDstY[iPixel] = (uint8_t)WELS_CLIP3(iPredTemp, 0, 255);
                }
                else
                {
                    iPredTemp = pMCRefMem->pDstY[iPixel] * iWoc + iOoc;

                    pMCRefMem->pDstY[iPixel] = (uint8_t)WELS_CLIP3(iPredTemp, 0, 255);

                }
            }
        }


        //UV
        iBlkWidth = iBlkWidth >> 1;
        iBlkHeight = iBlkHeight >> 1;
        iLog2denom = (int32_t)pCurDqLayer->pPredWeightTable->uiChromaLog2WeightDenom;
        iLineStride = pMCRefMem->iDstLineChroma;

        for (int i = 0; i < 2; i++)
        {
            //iLog2denom = pCurDqLayer->pPredWeightTable->uiChromaLog2WeightDenom;
            iWoc = pCurDqLayer->pPredWeightTable->sPredList[listIdx].iChromaWeight[iRefIdx][i];
            iOoc = pCurDqLayer->pPredWeightTable->sPredList[listIdx].iChromaOffset[iRefIdx][i];
            pDst = i != 0 ? pMCRefMem->pDstV : pMCRefMem->pDstU;
            //iLineStride = pMCRefMem->iDstLineChroma;

            // The inner loop's i shadows the plane index in C.
            for (int k = 0; k < iBlkHeight; k++)
            {
                for (int j = 0; j < iBlkWidth; j++)
                {
                    iPixel = j + k * (iLineStride);
                    if (iLog2denom >= 1)
                    {
                        iPredTemp = ((pDst[iPixel] * iWoc + (1 << (iLog2denom - 1))) >> iLog2denom) + iOoc;

                        pDst[iPixel] = (uint8_t)WELS_CLIP3(iPredTemp, 0, 255);
                    }
                    else
                    {
                        iPredTemp = pDst[iPixel] * iWoc + iOoc;

                        pDst[iPixel] = (uint8_t)WELS_CLIP3(iPredTemp, 0, 255);

                    }
                }

            }


        }
    }

    // BiWeightPrediction and BiPrediction: B slices, not ported yet.

    public static int32_t GetInterPred(uint8_t* pPredY, uint8_t* pPredCb, uint8_t* pPredCr, SWelsDecoderContext* pCtx)
    {
        sMCRefMember pMCRefMem;
        SDqLayer* pCurDqLayer = pCtx->pCurDqLayer;
        SMcFunc* pMCFunc = &pCtx->sMcFunc;

        int32_t iMBXY = pCurDqLayer->iMbXyIndex;

        int16_t* iMVs = stackalloc int16_t[2] { 0, 0 };

        uint32_t iMBType = pCurDqLayer->pDec->pMbType[iMBXY];

        int32_t iMBOffsetX = pCurDqLayer->iMbX << 4;
        int32_t iMBOffsetY = pCurDqLayer->iMbY << 4;

        int32_t iDstLineLuma = pCtx->pDec->iLinesize[0];
        int32_t iDstLineChroma = pCtx->pDec->iLinesize[1];

        int32_t iBlk8X, iBlk8Y, iBlk4X, iBlk4Y, i, j, iIIdx, iJIdx;

        pMCRefMem = default;
        pMCRefMem.iPicWidth = (pCurDqLayer->sLayerInfo.sSliceInLayer.sSliceHeaderExt.sSliceHeader.iMbWidth << 4);
        pMCRefMem.iPicHeight = (pCurDqLayer->sLayerInfo.sSliceInLayer.sSliceHeaderExt.sSliceHeader.iMbHeight << 4);

        pMCRefMem.pDstY = pPredY;
        pMCRefMem.pDstU = pPredCb;
        pMCRefMem.pDstV = pPredCr;

        pMCRefMem.iDstLineLuma = iDstLineLuma;
        pMCRefMem.iDstLineChroma = iDstLineChroma;

        int8_t iRefIndex = 0;
        uint32_t uiRetTmp;
        InlineArray16<InlineArray2<int16_t>>* pMv = pCurDqLayer->pDec->pMv[0].p + iMBXY;
        InlineArray16<int8_t>* pRefIdx = pCurDqLayer->pDec->pRefIndex[0].p + iMBXY;

        switch (iMBType)
        {
            case MB_TYPE_SKIP:
            case MB_TYPE_16x16:
                iMVs[0] = (*pMv)[0][0];
                iMVs[1] = (*pMv)[0][1];
                iRefIndex = (*pRefIdx)[0];
                uiRetTmp = (uint32_t)GetRefPic(&pMCRefMem, pCtx, iRefIndex, (int32_t)LIST_0);
                if (uiRetTmp != ERR_NONE) return (int32_t)uiRetTmp; // WELS_B_MB_REC_VERIFY
                BaseMC(pCtx, &pMCRefMem, (int32_t)LIST_0, iRefIndex, iMBOffsetX, iMBOffsetY, pMCFunc, 16, 16, iMVs);

                if (pCurDqLayer->bUseWeightPredictionFlag)
                {
                    iRefIndex = (*pRefIdx)[0];
                    WeightPrediction(pCurDqLayer, &pMCRefMem, (int32_t)LIST_0, iRefIndex, 16, 16);
                }
                break;
            case MB_TYPE_16x8:
                iMVs[0] = (*pMv)[0][0];
                iMVs[1] = (*pMv)[0][1];
                iRefIndex = (*pRefIdx)[0];
                uiRetTmp = (uint32_t)GetRefPic(&pMCRefMem, pCtx, iRefIndex, (int32_t)LIST_0);
                if (uiRetTmp != ERR_NONE) return (int32_t)uiRetTmp; // WELS_B_MB_REC_VERIFY
                BaseMC(pCtx, &pMCRefMem, (int32_t)LIST_0, iRefIndex, iMBOffsetX, iMBOffsetY, pMCFunc, 16, 8, iMVs);

                if (pCurDqLayer->bUseWeightPredictionFlag)
                {
                    WeightPrediction(pCurDqLayer, &pMCRefMem, (int32_t)LIST_0, iRefIndex, 16, 8);
                }

                iMVs[0] = (*pMv)[8][0];
                iMVs[1] = (*pMv)[8][1];
                iRefIndex = (*pRefIdx)[8];
                uiRetTmp = (uint32_t)GetRefPic(&pMCRefMem, pCtx, iRefIndex, (int32_t)LIST_0);
                if (uiRetTmp != ERR_NONE) return (int32_t)uiRetTmp; // WELS_B_MB_REC_VERIFY
                pMCRefMem.pDstY = pPredY + (iDstLineLuma << 3);
                pMCRefMem.pDstU = pPredCb + (iDstLineChroma << 2);
                pMCRefMem.pDstV = pPredCr + (iDstLineChroma << 2);
                BaseMC(pCtx, &pMCRefMem, (int32_t)LIST_0, iRefIndex, iMBOffsetX, iMBOffsetY + 8, pMCFunc, 16, 8, iMVs);

                if (pCurDqLayer->bUseWeightPredictionFlag)
                {
                    WeightPrediction(pCurDqLayer, &pMCRefMem, (int32_t)LIST_0, iRefIndex, 16, 8);
                }
                break;
            case MB_TYPE_8x16:
                iMVs[0] = (*pMv)[0][0];
                iMVs[1] = (*pMv)[0][1];
                iRefIndex = (*pRefIdx)[0];
                uiRetTmp = (uint32_t)GetRefPic(&pMCRefMem, pCtx, iRefIndex, (int32_t)LIST_0);
                if (uiRetTmp != ERR_NONE) return (int32_t)uiRetTmp; // WELS_B_MB_REC_VERIFY
                BaseMC(pCtx, &pMCRefMem, (int32_t)LIST_0, iRefIndex, iMBOffsetX, iMBOffsetY, pMCFunc, 8, 16, iMVs);
                if (pCurDqLayer->bUseWeightPredictionFlag)
                {
                    WeightPrediction(pCurDqLayer, &pMCRefMem, (int32_t)LIST_0, iRefIndex, 8, 16);
                }

                iMVs[0] = (*pMv)[2][0];
                iMVs[1] = (*pMv)[2][1];
                iRefIndex = (*pRefIdx)[2];
                uiRetTmp = (uint32_t)GetRefPic(&pMCRefMem, pCtx, iRefIndex, (int32_t)LIST_0);
                if (uiRetTmp != ERR_NONE) return (int32_t)uiRetTmp; // WELS_B_MB_REC_VERIFY
                pMCRefMem.pDstY = pPredY + 8;
                pMCRefMem.pDstU = pPredCb + 4;
                pMCRefMem.pDstV = pPredCr + 4;
                BaseMC(pCtx, &pMCRefMem, (int32_t)LIST_0, iRefIndex, iMBOffsetX + 8, iMBOffsetY, pMCFunc, 8, 16, iMVs);

                if (pCurDqLayer->bUseWeightPredictionFlag)
                {
                    WeightPrediction(pCurDqLayer, &pMCRefMem, (int32_t)LIST_0, iRefIndex, 8, 16);
                }
                break;
            case MB_TYPE_8x8:
            case MB_TYPE_8x8_REF0:
                {
                    uint32_t iSubMBType;
                    int32_t iXOffset, iYOffset;
                    uint8_t* pDstY, pDstU, pDstV;
                    for (i = 0; i < 4; i++)
                    {
                        iSubMBType = pCurDqLayer->pSubMbType[iMBXY][i];
                        iBlk8X = (i & 1) << 3;
                        iBlk8Y = (i >> 1) << 3;
                        iXOffset = iMBOffsetX + iBlk8X;
                        iYOffset = iMBOffsetY + iBlk8Y;

                        iIIdx = ((i >> 1) << 3) + ((i & 1) << 1);
                        iRefIndex = (*pRefIdx)[iIIdx];
                        uiRetTmp = (uint32_t)GetRefPic(&pMCRefMem, pCtx, iRefIndex, (int32_t)LIST_0);
                        if (uiRetTmp != ERR_NONE) return (int32_t)uiRetTmp; // WELS_B_MB_REC_VERIFY
                        pDstY = pPredY + iBlk8X + iBlk8Y * iDstLineLuma;
                        pDstU = pPredCb + (iBlk8X >> 1) + (iBlk8Y >> 1) * iDstLineChroma;
                        pDstV = pPredCr + (iBlk8X >> 1) + (iBlk8Y >> 1) * iDstLineChroma;
                        pMCRefMem.pDstY = pDstY;
                        pMCRefMem.pDstU = pDstU;
                        pMCRefMem.pDstV = pDstV;
                        switch (iSubMBType)
                        {
                            case SUB_MB_TYPE_8x8:
                                iMVs[0] = (*pMv)[iIIdx][0];
                                iMVs[1] = (*pMv)[iIIdx][1];
                                BaseMC(pCtx, &pMCRefMem, (int32_t)LIST_0, iRefIndex, iXOffset, iYOffset, pMCFunc, 8, 8, iMVs);
                                if (pCurDqLayer->bUseWeightPredictionFlag)
                                {

                                    WeightPrediction(pCurDqLayer, &pMCRefMem, (int32_t)LIST_0, iRefIndex, 8, 8);
                                }

                                break;
                            case SUB_MB_TYPE_8x4:
                                iMVs[0] = (*pMv)[iIIdx][0];
                                iMVs[1] = (*pMv)[iIIdx][1];
                                BaseMC(pCtx, &pMCRefMem, (int32_t)LIST_0, iRefIndex, iXOffset, iYOffset, pMCFunc, 8, 4, iMVs);
                                if (pCurDqLayer->bUseWeightPredictionFlag)
                                {

                                    WeightPrediction(pCurDqLayer, &pMCRefMem, (int32_t)LIST_0, iRefIndex, 8, 4);
                                }


                                iMVs[0] = (*pMv)[iIIdx + 4][0];
                                iMVs[1] = (*pMv)[iIIdx + 4][1];
                                pMCRefMem.pDstY += (iDstLineLuma << 2);
                                pMCRefMem.pDstU += (iDstLineChroma << 1);
                                pMCRefMem.pDstV += (iDstLineChroma << 1);
                                BaseMC(pCtx, &pMCRefMem, (int32_t)LIST_0, iRefIndex, iXOffset, iYOffset + 4, pMCFunc, 8, 4, iMVs);
                                if (pCurDqLayer->bUseWeightPredictionFlag)
                                {

                                    WeightPrediction(pCurDqLayer, &pMCRefMem, (int32_t)LIST_0, iRefIndex, 8, 4);
                                }

                                break;
                            case SUB_MB_TYPE_4x8:
                                iMVs[0] = (*pMv)[iIIdx][0];
                                iMVs[1] = (*pMv)[iIIdx][1];
                                BaseMC(pCtx, &pMCRefMem, (int32_t)LIST_0, iRefIndex, iXOffset, iYOffset, pMCFunc, 4, 8, iMVs);
                                if (pCurDqLayer->bUseWeightPredictionFlag)
                                {

                                    WeightPrediction(pCurDqLayer, &pMCRefMem, (int32_t)LIST_0, iRefIndex, 4, 8);
                                }


                                iMVs[0] = (*pMv)[iIIdx + 1][0];
                                iMVs[1] = (*pMv)[iIIdx + 1][1];
                                pMCRefMem.pDstY += 4;
                                pMCRefMem.pDstU += 2;
                                pMCRefMem.pDstV += 2;
                                BaseMC(pCtx, &pMCRefMem, (int32_t)LIST_0, iRefIndex, iXOffset + 4, iYOffset, pMCFunc, 4, 8, iMVs);
                                if (pCurDqLayer->bUseWeightPredictionFlag)
                                {

                                    WeightPrediction(pCurDqLayer, &pMCRefMem, (int32_t)LIST_0, iRefIndex, 4, 8);
                                }

                                break;
                            case SUB_MB_TYPE_4x4:
                                {
                                    for (j = 0; j < 4; j++)
                                    {
                                        int32_t iUVLineStride;
                                        iJIdx = ((j >> 1) << 2) + (j & 1);

                                        iBlk4X = (j & 1) << 2;
                                        iBlk4Y = (j >> 1) << 2;

                                        iUVLineStride = (iBlk4X >> 1) + (iBlk4Y >> 1) * iDstLineChroma;
                                        pMCRefMem.pDstY = pDstY + iBlk4X + iBlk4Y * iDstLineLuma;
                                        pMCRefMem.pDstU = pDstU + iUVLineStride;
                                        pMCRefMem.pDstV = pDstV + iUVLineStride;

                                        iMVs[0] = (*pMv)[iIIdx + iJIdx][0];
                                        iMVs[1] = (*pMv)[iIIdx + iJIdx][1];
                                        BaseMC(pCtx, &pMCRefMem, (int32_t)LIST_0, iRefIndex, iXOffset + iBlk4X, iYOffset + iBlk4Y, pMCFunc, 4, 4, iMVs);
                                        if (pCurDqLayer->bUseWeightPredictionFlag)
                                        {

                                            WeightPrediction(pCurDqLayer, &pMCRefMem, (int32_t)LIST_0, iRefIndex, 4, 4);
                                        }

                                    }
                                }
                                break;
                            default:
                                break;
                        }
                    }
                }
                break;
            default:
                break;
        }
        return ERR_NONE;
    }

    // GetInterBPred: B slices, not ported yet.

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
