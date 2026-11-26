//-----------------------------------------------------------------------------
// Filename: svc_base_layer_md.cs
//
// Description: Port of codec/encoder/core/src/svc_base_layer_md.cpp, part 1
// (E4): the intra mode tables and the intra mode decision (WelsMdIntraInit,
// WelsMdI16x16, WelsMdI4x4, WelsMdI4x4Fast, WelsMdIntraChroma, the two fine
// partition functions, WelsMdIntraMb and WelsMdIntraSecondaryModesEnc). The
// inter mode decision follows with P slices in E5.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// Upstream's C path leaves the Combined3 kernels NULL (see sample.cs), so the
// Combined3 branches here only run if a caller installs them.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 26 Nov 2026  Claude          Created (E4).
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

using System.Diagnostics;

namespace SIPSorcery.H264.Encoder;

internal static unsafe partial class WelsEnc
{
    // static const ALIGNED_DECLARE (int8_t, g_kiIntra16AvaliMode[8][5], 16) = {
    private static readonly InlineArray5<int8_t>* g_kiIntra16AvaliMode = StaticTable.Of<InlineArray5<int8_t>, int8_t>(8, [
        I16_PRED_DC_128, I16_PRED_INVALID, I16_PRED_INVALID, I16_PRED_INVALID, 1,
        I16_PRED_DC_L,   I16_PRED_H,       I16_PRED_INVALID, I16_PRED_INVALID, 2,
        I16_PRED_DC_T,   I16_PRED_V,       I16_PRED_INVALID, I16_PRED_INVALID, 2,
        I16_PRED_V,      I16_PRED_H,       I16_PRED_DC,      I16_PRED_INVALID, 3,
        I16_PRED_DC_128, I16_PRED_INVALID, I16_PRED_INVALID, I16_PRED_INVALID, 1,
        I16_PRED_DC_L,   I16_PRED_H,       I16_PRED_INVALID, I16_PRED_INVALID, 2,
        I16_PRED_DC_T,   I16_PRED_V,       I16_PRED_INVALID, I16_PRED_INVALID, 2,
        I16_PRED_V,      I16_PRED_H,       I16_PRED_DC,      I16_PRED_P,       4
    ]);

    // static const ALIGNED_DECLARE (uint8_t, g_kiIntra4AvailCount[16], 16) = {
    // (I4_PRED_MODE_EXTEND is not defined upstream)
    private static readonly uint8_t* g_kiIntra4AvailCount = StaticTable.Of<uint8_t>(16, [
        1, 3, 2, 4, 1, 3, 2, 7, 1, 3, 4, 6, 1, 3, 4, 9
    ]);

    //left_avail | (top_avail<<1) | (left_top_avail<<2) | (right_top_avail<<3);
    // static const ALIGNED_DECLARE (uint8_t, g_kiIntra4AvailMode[16][16], 16) = {
    // Each row lists its modes and is padded with I4_PRED_INVALID (0).
    private static readonly InlineArray16<uint8_t>* g_kiIntra4AvailMode = StaticTable.Of<InlineArray16<uint8_t>, uint8_t>(16, [
        I4_PRED_DC_128, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,                                    //  0000
        I4_PRED_DC_L, I4_PRED_H, I4_PRED_HU, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,                    //  0001
        I4_PRED_DC_T, I4_PRED_V, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,                             //  0010
        I4_PRED_DC, I4_PRED_H, I4_PRED_V, I4_PRED_HU, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,              //  0011
        I4_PRED_DC_128, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,                                    //  0100
        I4_PRED_DC_L, I4_PRED_H, I4_PRED_HU, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,                    //  0101
        I4_PRED_DC_T, I4_PRED_V, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,                             //  0110
        I4_PRED_DC, I4_PRED_H, I4_PRED_V, I4_PRED_HU, I4_PRED_DDR, I4_PRED_VR, I4_PRED_HD, 0,
        0, 0, 0, 0, 0, 0, 0, 0,                                                                         //  0111
        I4_PRED_DC_128, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,                                    //  1000
        I4_PRED_DC_L, I4_PRED_H, I4_PRED_HU, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,                    //  1001
        I4_PRED_DC_T, I4_PRED_V, I4_PRED_DDL, I4_PRED_VL, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,          //  1010
        I4_PRED_DC, I4_PRED_H, I4_PRED_V, I4_PRED_HU, I4_PRED_DDL, I4_PRED_VL, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0,                                                                         //  1011
        I4_PRED_DC_128, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,                                    //  1100
        I4_PRED_DC_L, I4_PRED_H, I4_PRED_HU, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,                    //  1101
        I4_PRED_DC_T, I4_PRED_V, I4_PRED_DDL, I4_PRED_VL, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,          //  1110
        I4_PRED_DC, I4_PRED_H, I4_PRED_V, I4_PRED_HU, I4_PRED_DDL, I4_PRED_VL, I4_PRED_DDR, I4_PRED_VR,
        I4_PRED_HD, 0, 0, 0, 0, 0, 0, 0                                                                 //  1111
    ]);

    // static const ALIGNED_DECLARE (int8_t, g_kiIntraChromaAvailMode[8][5], 16) = {
    private static readonly InlineArray5<int8_t>* g_kiIntraChromaAvailMode = StaticTable.Of<InlineArray5<int8_t>, int8_t>(8, [
        C_PRED_DC_128, C_PRED_INVALID, C_PRED_INVALID, C_PRED_INVALID, 1,
        C_PRED_DC_L,   C_PRED_H,       C_PRED_INVALID, C_PRED_INVALID, 2,
        C_PRED_DC_T,   C_PRED_V,       C_PRED_INVALID, C_PRED_INVALID, 2,
        C_PRED_V,      C_PRED_H,       C_PRED_DC,      C_PRED_INVALID, 3,
        C_PRED_DC_128, C_PRED_INVALID, C_PRED_INVALID, C_PRED_INVALID, 1,
        C_PRED_DC_L,   C_PRED_H,       C_PRED_INVALID, C_PRED_INVALID, 2,
        C_PRED_DC_T,   C_PRED_V,       C_PRED_INVALID, C_PRED_INVALID, 2,
        C_PRED_V,      C_PRED_H,       C_PRED_DC,      C_PRED_P,       4
    ]);

    // for cache hit, two table are total sizeof 64 Bytes
    // const int8_t g_kiCoordinateIdx4x4X[16] = {
    public static readonly int8_t* g_kiCoordinateIdx4x4X = StaticTable.Of<int8_t>(16, [
        0, 4, 0, 4,
        8, 12, 8, 12,
        0, 4, 0, 4,
        8, 12, 8, 12
    ]);

    // const int8_t g_kiCoordinateIdx4x4Y[16] = {
    public static readonly int8_t* g_kiCoordinateIdx4x4Y = StaticTable.Of<int8_t>(16, [
        0, 0, 4, 4,
        0, 0, 4, 4,
        8, 8, 12, 12,
        8, 8, 12, 12
    ]);

    // static const ALIGNED_DECLARE (int8_t, g_kiNeighborIntraToI4x4[16][16], 16) = {
    private static readonly InlineArray16<int8_t>* g_kiNeighborIntraToI4x4 = StaticTable.Of<InlineArray16<int8_t>, int8_t>(16, [
        0,  1,  10, 7,  1,  1,  15, 7,  10, 15, 10, 7,  15, 7,  15, 7,
        1,  1,  15, 7,  1,  1,  15, 7,  15, 15, 15, 7,  15, 7,  15, 7,
        10, 15, 10, 7,  15, 7,  15, 7,  10, 15, 10, 7,  15, 7,  15, 7,
        11, 15, 15, 7,  15, 7,  15, 7,  15, 15, 15, 7,  15, 7,  15, 7,
        4,  1,  10, 7,  1,  1,  15, 7,  10, 15, 10, 7,  15, 7,  15, 7,
        5,  1,  15, 7,  1,  1,  15, 7,  15, 15, 15, 7,  15, 7,  15, 7,
        14, 15, 10, 7,  15, 7,  15, 7,  10, 15, 10, 7,  15, 7,  15, 7,
        15, 15, 15, 7,  15, 7,  15, 7,  15, 15, 15, 7,  15, 7,  15, 7,
        0,  1,  10, 7,  1,  9,  15, 7,  10, 15, 10, 7,  15, 7,  15, 7,
        1,  1,  15, 7,  1,  9,  15, 7,  15, 15, 15, 7,  15, 7,  15, 7,
        10, 15, 10, 7,  15, 15, 15, 7,  10, 15, 10, 7,  15, 7,  15, 7,
        11, 15, 15, 7,  15, 15, 15, 7,  15, 15, 15, 7,  15, 7,  15, 7,
        4,  1,  10, 7,  1,  9,  15, 7,  10, 15, 10, 7,  15, 7,  15, 7,
        5,  1,  15, 7,  1,  9,  15, 7,  15, 15, 15, 7,  15, 7,  15, 7,
        14, 15, 10, 7,  15, 15, 15, 7,  10, 15, 10, 7,  15, 7,  15, 7,
        15, 15, 15, 7,  15, 15, 15, 7,  15, 15, 15, 7,  15, 7,  15, 7
    ]);

    // ALIGNED_DECLARE (const int8_t, g_kiMapModeI4x4[14], 16) = {
    public static readonly int8_t* g_kiMapModeI4x4 = StaticTable.Of<int8_t>(14, [
        0, 1, 2, 3, 4, 5, 6, 7, 8, 2, 2, 2, 3, 7
    ]);

    public static int32_t PredIntra4x4Mode(int8_t* pIntraPredMode, int32_t iIdx4)
    {
        int8_t iTopMode = pIntraPredMode[iIdx4 - 8];
        int8_t iLeftMode = pIntraPredMode[iIdx4 - 1];
        int8_t iBestMode;

        if (-1 == iLeftMode || -1 == iTopMode)
        {
            iBestMode = 2;
        }
        else
        {
            iBestMode = WELS_MIN(iLeftMode, iTopMode);
        }
        return iBestMode;
    }

    public static void WelsMdIntraInit(sWelsEncCtx* pEncCtx, SMB* pCurMb, SMbCache* pMbCache, int32_t iSliceFirstMbXY)
    {
        SDqLayer* pCurLayer = pEncCtx->pCurDqLayer;

        int32_t kiMbX = pCurMb->iMbX;
        int32_t kiMbY = pCurMb->iMbY;
        int32_t kiMbXY = pCurMb->iMbXY;

        // step 3. locating current pEnc and pDec
        // unroll loops here
        if (0 == kiMbX || iSliceFirstMbXY == kiMbXY)
        {
            int32_t iStrideY, iStrideUV;
            int32_t iOffsetY, iOffsetUV;

            iStrideY = pCurLayer->iEncStride[0];
            iStrideUV = pCurLayer->iEncStride[1];
            iOffsetY = (kiMbX + kiMbY * iStrideY) << 4;
            iOffsetUV = (kiMbX + kiMbY * iStrideUV) << 3;
            pMbCache->SPicData.pEncMb[0] = pCurLayer->pEncData[0] + iOffsetY;
            pMbCache->SPicData.pEncMb[1] = pCurLayer->pEncData[1] + iOffsetUV;
            pMbCache->SPicData.pEncMb[2] = pCurLayer->pEncData[2] + iOffsetUV;

            iStrideY = pCurLayer->iCsStride[0];
            iStrideUV = pCurLayer->iCsStride[1];
            iOffsetY = (kiMbX + kiMbY * iStrideY) << 4;
            iOffsetUV = (kiMbX + kiMbY * iStrideUV) << 3;
            pMbCache->SPicData.pCsMb[0] = pCurLayer->pCsData[0] + iOffsetY;
            pMbCache->SPicData.pCsMb[1] = pCurLayer->pCsData[1] + iOffsetUV;
            pMbCache->SPicData.pCsMb[2] = pCurLayer->pCsData[2] + iOffsetUV;

            iStrideY = pCurLayer->pDecPic->iLineSize[0];
            iStrideUV = pCurLayer->pDecPic->iLineSize[1];
            iOffsetY = (kiMbX + kiMbY * iStrideY) << 4;
            iOffsetUV = (kiMbX + kiMbY * iStrideUV) << 3;
            pMbCache->SPicData.pDecMb[0] = pCurLayer->pDecPic->pData[0] + iOffsetY;
            pMbCache->SPicData.pDecMb[1] = pCurLayer->pDecPic->pData[1] + iOffsetUV;
            pMbCache->SPicData.pDecMb[2] = pCurLayer->pDecPic->pData[2] + iOffsetUV;
        }
        else
        {
            pMbCache->SPicData.pEncMb[0] = pMbCache->SPicData.pEncMb[0] + MB_WIDTH_LUMA;
            pMbCache->SPicData.pEncMb[1] = pMbCache->SPicData.pEncMb[1] + MB_WIDTH_CHROMA;
            pMbCache->SPicData.pEncMb[2] = pMbCache->SPicData.pEncMb[2] + MB_WIDTH_CHROMA;

            pMbCache->SPicData.pDecMb[0] = pMbCache->SPicData.pDecMb[0] + MB_WIDTH_LUMA;
            pMbCache->SPicData.pDecMb[1] = pMbCache->SPicData.pDecMb[1] + MB_WIDTH_CHROMA;
            pMbCache->SPicData.pDecMb[2] = pMbCache->SPicData.pDecMb[2] + MB_WIDTH_CHROMA;

            pMbCache->SPicData.pCsMb[0] = pMbCache->SPicData.pCsMb[0] + MB_WIDTH_LUMA;
            pMbCache->SPicData.pCsMb[1] = pMbCache->SPicData.pCsMb[1] + MB_WIDTH_CHROMA;
            pMbCache->SPicData.pCsMb[2] = pMbCache->SPicData.pCsMb[2] + MB_WIDTH_CHROMA;
        }

        //step 2. initial pWelsMd
        pCurMb->uiCbp = 0;

        //step 4: locating scaled_tcoeff

        //step 1. load neighbor cache
        FillNeighborCacheIntra(pMbCache, pCurMb, pCurLayer->iMbWidth);
        pMbCache->pMemPredLuma = pMbCache->pMemPredMb;// in WelsMdI16x16() will be changed, so re-init here!
        pMbCache->pMemPredChroma = pMbCache->pMemPredMb +
                                   256;// Init with default, maybe change in WelsMdI16x16 and svc_md_i16x16_sad
    }

    // WelsMdInterInit comes with E5.

    public static int32_t WelsMdI16x16(SWelsFuncPtrList* pFunc, SDqLayer* pCurDqLayer, SMbCache* pMbCache, int32_t iLambda)
    {
        int8_t* kpAvailMode;
        int32_t iAvailCount;
        int32_t iIdx = 0;
        uint8_t** pPredI16x16 = stackalloc uint8_t*[2];
        pPredI16x16[0] = pMbCache->pMemPredMb;
        pPredI16x16[1] = pMbCache->pMemPredMb + 256;
        uint8_t* pDst = pPredI16x16[0];
        uint8_t* pDec = pMbCache->SPicData.pCsMb[0];
        uint8_t* pEnc = pMbCache->SPicData.pEncMb[0];
        int32_t iLineSizeDec = pCurDqLayer->iCsStride[0];
        int32_t iLineSizeEnc = pCurDqLayer->iEncStride[0];
        int32_t i, iCurCost, iCurMode, iBestMode, iBestCost = INT_MAX;

        int32_t iOffset = pMbCache->uiNeighborIntra & 0x07;
        iAvailCount = g_kiIntra16AvaliMode[iOffset][4];
        kpAvailMode = (int8_t*)(g_kiIntra16AvaliMode + iOffset);
        if (iAvailCount > 3 && pFunc->sSampleDealingFuncs.pfIntra16x16Combined3 != null)
        {
            iBestCost = pFunc->sSampleDealingFuncs.pfIntra16x16Combined3(pDec, iLineSizeDec, pEnc, iLineSizeEnc, &iBestMode,
                        iLambda, pDst/*temp*/);
            iCurMode = kpAvailMode[3];
            pFunc->pfGetLumaI16x16Pred[iCurMode](pDst, pDec, iLineSizeDec);
            iCurCost = pFunc->sSampleDealingFuncs.pfMdCost[BLOCK_16x16](pDst, 16, pEnc, iLineSizeEnc) + iLambda * 4;
            if (iCurCost < iBestCost)
            {
                iBestMode = iCurMode;
                iBestCost = iCurCost;
            }
            else
            {
                pFunc->pfGetLumaI16x16Pred[iBestMode](pDst, pDec, iLineSizeDec);
            }
            iIdx = 1;
            iBestCost += iLambda;
        }
        else
        {
            iBestMode = kpAvailMode[0];
            for (i = 0; i < iAvailCount; ++i)
            {
                iCurMode = kpAvailMode[i];

                Debug.Assert(iCurMode >= 0 && iCurMode < 7);

                pFunc->pfGetLumaI16x16Pred[iCurMode](pDst, pDec, iLineSizeDec);
                iCurCost = pFunc->sSampleDealingFuncs.pfMdCost[BLOCK_16x16](pDst, 16, pEnc, iLineSizeEnc);
                iCurCost += (int32_t)(iLambda * (BsSizeUE((uint32_t)g_kiMapModeI16x16[iCurMode])));
                if (iCurCost < iBestCost)
                {
                    iBestMode = iCurMode;
                    iBestCost = iCurCost;
                    iIdx = iIdx ^ 0x01;
                    pDst = pPredI16x16[iIdx];
                }
            }
        }
        pMbCache->pMemPredChroma = pPredI16x16[iIdx];

        pMbCache->pMemPredLuma = pPredI16x16[iIdx ^ 0x01];
        pMbCache->uiLumaI16x16Mode = (uint8_t)iBestMode;
        return iBestCost;
    }

    public static int32_t WelsMdI4x4(sWelsEncCtx* pEncCtx, SWelsMD* pWelsMd, SMB* pCurMb, SMbCache* pMbCache)
    {
        SWelsFuncPtrList* pFunc = pEncCtx->pFuncList;
        SDqLayer* pCurDqLayer = pEncCtx->pCurDqLayer;
        int32_t iLambda = pWelsMd->iLambda;
        int32_t iBestCostLuma = pWelsMd->iCostLuma;
        uint8_t* pEncMb = pMbCache->SPicData.pEncMb[0];
        uint8_t* pDecMb = pMbCache->SPicData.pCsMb[0];
        int32_t kiLineSizeEnc = pCurDqLayer->iEncStride[0];
        int32_t kiLineSizeDec = pCurDqLayer->iCsStride[0];

        uint8_t* pCurEnc, pCurDec, pDst;

        int32_t iPredMode, iCurMode, iBestMode, iFinalMode;
        int32_t iCurCost, iBestCost;
        int32_t iAvailCount;
        uint8_t* kpAvailMode;
        int32_t i, j, iCoordinateX, iCoordinateY, iIdxStrideEnc, iIdxStrideDec;
        int32_t* lambda = stackalloc int32_t[2] { iLambda << 2, iLambda };
        bool* pPrevIntra4x4PredModeFlag = pMbCache->pPrevIntra4x4PredModeFlag;
        int8_t* pRemIntra4x4PredModeFlag = pMbCache->pRemIntra4x4PredModeFlag;
        uint8_t* kpIntra4x4AvailCount = &g_kiIntra4AvailCount[0];
        uint8_t* kpCache48CountScan4 = &g_kuiCache48CountScan4Idx[0];
        int8_t* kpNeighborIntraToI4x4 = (int8_t*)(g_kiNeighborIntraToI4x4 + pMbCache->uiNeighborIntra);
        int8_t* kpCoordinateIdxX = &g_kiCoordinateIdx4x4X[0];
        int8_t* kpCoordinateIdxY = &g_kiCoordinateIdx4x4Y[0];
        int32_t iBestPredBufferNum = 0;
        int32_t iCosti4x4 = 0;

        for (i = 0; i < 16; i++)
        {
            int32_t kiOffset = kpNeighborIntraToI4x4[i];

            //step 1: locating current 4x4 block position in pEnc and pDecMb
            iCoordinateX = kpCoordinateIdxX[i];
            iCoordinateY = kpCoordinateIdxY[i];

            iIdxStrideEnc = (iCoordinateY * kiLineSizeEnc) + iCoordinateX;
            pCurEnc = pEncMb + iIdxStrideEnc;
            iIdxStrideDec = (iCoordinateY * kiLineSizeDec) + iCoordinateX;
            pCurDec = pDecMb + iIdxStrideDec;

            //step 2: get predicted mode from neighbor
            iPredMode = PredIntra4x4Mode(pMbCache->iIntraPredMode, kpCache48CountScan4[i]);

            //step 3: collect candidates of iPredMode
            iAvailCount = kpIntra4x4AvailCount[kiOffset];
            kpAvailMode = (uint8_t*)(g_kiIntra4AvailMode + kiOffset);

            //step 4: gain the best pred mode
            iBestCost = INT_MAX;
            iBestMode = kpAvailMode[0];

            if (pFunc->sSampleDealingFuncs.pfIntra4x4Combined3 != null && (iAvailCount >= 6))
            {
                pDst = &pMbCache->pMemPredBlk4[iBestPredBufferNum << 4];

                iBestCost = pFunc->sSampleDealingFuncs.pfIntra4x4Combined3(pCurDec, kiLineSizeDec, pCurEnc, kiLineSizeEnc, pDst,
                            &iBestMode,
                            lambda[iPredMode == 2 ? 1 : 0], lambda[iPredMode == 1 ? 1 : 0], lambda[iPredMode == 0 ? 1 : 0]);
                //     ST64(&pMbCache->pMemPredBlk4[iBestMode<<4], LD64(mem_pred_blk4_temp));
                //     ST64(&pMbCache->pMemPredBlk4[8+(iBestMode<<4)], LD64(mem_pred_blk4_temp+8));

                for (j = 3; j < iAvailCount; ++j)
                {
                    iCurMode = kpAvailMode[j];

                    Debug.Assert(iCurMode >= 0 && iCurMode < 14);

                    pDst = &pMbCache->pMemPredBlk4[(1 - iBestPredBufferNum) << 4];

                    pFunc->pfGetLumaI4x4Pred[iCurMode](pDst, pCurDec, kiLineSizeDec);
                    iCurCost = pFunc->sSampleDealingFuncs.pfSampleSatd[BLOCK_4x4](pDst, 4, pCurEnc, kiLineSizeEnc) +
                               lambda[iPredMode == g_kiMapModeI4x4[iCurMode] ? 1 : 0];

                    if (iCurCost < iBestCost)
                    {
                        iBestMode = iCurMode;
                        iBestCost = iCurCost;
                        iBestPredBufferNum = 1 - iBestPredBufferNum;
                    }
                }
            }
            else
            {
                for (j = 0; j < iAvailCount; ++j)
                {
                    iCurMode = kpAvailMode[j];

                    Debug.Assert(iCurMode >= 0 && iCurMode < 14);

                    pDst = &pMbCache->pMemPredBlk4[(1 - iBestPredBufferNum) << 4];

                    pFunc->pfGetLumaI4x4Pred[iCurMode](pDst, pCurDec, kiLineSizeDec);
                    iCurCost = pFunc->sSampleDealingFuncs.pfSampleSatd[BLOCK_4x4](pDst, 4, pCurEnc, kiLineSizeEnc) +
                               lambda[iPredMode == g_kiMapModeI4x4[iCurMode] ? 1 : 0];

                    if (iCurCost < iBestCost)
                    {
                        iBestMode = iCurMode;
                        iBestCost = iCurCost;
                        iBestPredBufferNum = 1 - iBestPredBufferNum;
                    }
                }
            }
            pMbCache->pBestPredI4x4Blk4 = &pMbCache->pMemPredBlk4[iBestPredBufferNum << 4];
            iCosti4x4 += iBestCost;
            if (iCosti4x4 >= iBestCostLuma)
            {
                break;
            }

            //step 5: update pred mode and sample avail cache
            iFinalMode = g_kiMapModeI4x4[iBestMode];
            if (iPredMode == iFinalMode)
            {
                *pPrevIntra4x4PredModeFlag++ = true;
            }
            else
            {
                *pPrevIntra4x4PredModeFlag++ = false;
                *pRemIntra4x4PredModeFlag = (int8_t)(iFinalMode < iPredMode ? iFinalMode : (iFinalMode - 1));
            }
            pRemIntra4x4PredModeFlag++;
            // pCurMb->pIntra4x4PredMode[g_kuiMbCountScan4Idx[i]] = iFinalMode;
            pMbCache->iIntraPredMode[kpCache48CountScan4[i]] = (int8_t)iFinalMode;

            //step 6: encoding I_4x4
            WelsEncRecI4x4Y(pEncCtx, pCurMb, pMbCache, (uint8_t)i);
        }
        ST32(pCurMb->pIntra4x4PredMode, LD32(&pMbCache->iIntraPredMode[33]));
        pCurMb->pIntra4x4PredMode[4] = pMbCache->iIntraPredMode[12];
        pCurMb->pIntra4x4PredMode[5] = pMbCache->iIntraPredMode[20];
        pCurMb->pIntra4x4PredMode[6] = pMbCache->iIntraPredMode[28];
        iCosti4x4 += (iLambda << 4) + (iLambda << 3); //4*6*lambda from JVT SATD0
        return iCosti4x4;
    }

    public static int32_t WelsMdI4x4Fast(sWelsEncCtx* pEncCtx, SWelsMD* pWelsMd, SMB* pCurMb, SMbCache* pMbCache)
    {
        SWelsFuncPtrList* pFunc = pEncCtx->pFuncList;
        SDqLayer* pCurDqLayer = pEncCtx->pCurDqLayer;
        int32_t iLambda = pWelsMd->iLambda;
        int32_t iBestCostLuma = pWelsMd->iCostLuma;
        uint8_t* pEncMb = pMbCache->SPicData.pEncMb[0];
        uint8_t* pDecMb = pMbCache->SPicData.pCsMb[0];
        int32_t kiLineSizeEnc = pCurDqLayer->iEncStride[0];
        int32_t kiLineSizeDec = pCurDqLayer->iCsStride[0];

        uint8_t* pCurEnc, pCurDec, pDst;
        int8_t iPredMode, iCurMode, iBestMode, iFinalMode;
        int32_t iCurCost, iBestCost;
        int32_t iAvailCount;
        uint8_t* kpAvailMode;
        int32_t i, j, iCoordinateX, iCoordinateY, iIdxStrideEnc, iIdxStrideDec;
        int32_t iCostH, iCostV, iCostVR, iCostHD, iCostVL, iCostHU, iBestModeFake;
        int32_t* lambda = stackalloc int32_t[2] { iLambda << 2, iLambda };
        bool* pPrevIntra4x4PredModeFlag = pMbCache->pPrevIntra4x4PredModeFlag;
        int8_t* pRemIntra4x4PredModeFlag = pMbCache->pRemIntra4x4PredModeFlag;
        uint8_t* kpIntra4x4AvailCount = &g_kiIntra4AvailCount[0];
        uint8_t* kpCache48CountScan4 = &g_kuiCache48CountScan4Idx[0];
        int8_t* kpNeighborIntraToI4x4 = (int8_t*)(g_kiNeighborIntraToI4x4 + pMbCache->uiNeighborIntra);
        int8_t* kpCoordinateIdxX = &g_kiCoordinateIdx4x4X[0];
        int8_t* kpCoordinateIdxY = &g_kiCoordinateIdx4x4Y[0];
        int32_t iBestPredBufferNum = 0;
        int32_t iCosti4x4 = 0;

        for (i = 0; i < 16; i++)
        {
            int32_t kiOffset = kpNeighborIntraToI4x4[i];
            //    const int32_t i_next = (1+i) & 15; // next loop
            //    const uint8_t dummy_byte= pIntra4x4AvailCount[pNeighborIntraToI4x4[i_next]]; // prefetch pIntra4x4AvailCount of next loop to avoid cache missed

            //step 1: locating current 4x4 block position in pEnc and pDecMb
            iCoordinateX = kpCoordinateIdxX[i];
            iCoordinateY = kpCoordinateIdxY[i];

            iIdxStrideEnc = (iCoordinateY * kiLineSizeEnc) + iCoordinateX;
            pCurEnc = pEncMb + iIdxStrideEnc;
            iIdxStrideDec = (iCoordinateY * kiLineSizeDec) + iCoordinateX;
            pCurDec = pDecMb + iIdxStrideDec;

            //step 2: get predicted mode from neighbor
            iPredMode = (int8_t)PredIntra4x4Mode(pMbCache->iIntraPredMode, kpCache48CountScan4[i]);
            //step 3: collect candidates of iPredMode
            iAvailCount = kpIntra4x4AvailCount[kiOffset];
            kpAvailMode = (uint8_t*)(g_kiIntra4AvailMode + kiOffset);

            if (iAvailCount == 9 || iAvailCount == 7)
            {
                //I4_PRED_DC(2)

                iBestMode = I4_PRED_DC;

                pDst = &pMbCache->pMemPredBlk4[iBestPredBufferNum << 4];

                pFunc->pfGetLumaI4x4Pred[I4_PRED_DC](pDst, pCurDec, kiLineSizeDec);
                iBestCost = pFunc->sSampleDealingFuncs.pfMdCost[BLOCK_4x4](pDst, 4, pCurEnc, kiLineSizeEnc) +
                            lambda[iPredMode == g_kiMapModeI4x4[iBestMode] ? 1 : 0];

                //I4_PRED_H(1)
                iCurMode = I4_PRED_H;

                pDst = &pMbCache->pMemPredBlk4[(1 - iBestPredBufferNum) << 4];

                pFunc->pfGetLumaI4x4Pred[iCurMode](pDst, pCurDec, kiLineSizeDec);
                iCostH = iCurCost = pFunc->sSampleDealingFuncs.pfMdCost[BLOCK_4x4](pDst, 4, pCurEnc, kiLineSizeEnc) +
                                    lambda[iPredMode == g_kiMapModeI4x4[iCurMode] ? 1 : 0];

                if (iCurCost < iBestCost)
                {
                    iBestMode = iCurMode;
                    iBestCost = iCurCost;
                    iBestPredBufferNum = 1 - iBestPredBufferNum;
                }

                //I4_PRED_V(0)
                iCurMode = I4_PRED_V;

                pDst = &pMbCache->pMemPredBlk4[(1 - iBestPredBufferNum) << 4];

                pFunc->pfGetLumaI4x4Pred[iCurMode](pDst, pCurDec, kiLineSizeDec);
                iCostV = iCurCost = pFunc->sSampleDealingFuncs.pfMdCost[BLOCK_4x4](pDst, 4, pCurEnc, kiLineSizeEnc) +
                                    lambda[iPredMode == g_kiMapModeI4x4[iCurMode] ? 1 : 0];

                if (iCurCost < iBestCost)
                {
                    iBestMode = iCurMode;
                    iBestCost = iCurCost;
                    iBestPredBufferNum = 1 - iBestPredBufferNum;
                }
                if (iCostV < iCostH)
                {
                    if (iAvailCount == 9)
                    {
                        iBestModeFake = 1; //indicating whether V is the best fake mode

                        //I4_PRED_VR(5) and I4_PRED_VL(7)
                        iCurMode = I4_PRED_VR;

                        pDst = &pMbCache->pMemPredBlk4[(1 - iBestPredBufferNum) << 4];

                        pFunc->pfGetLumaI4x4Pred[iCurMode](pDst, pCurDec, kiLineSizeDec);
                        iCostVR = iCurCost = pFunc->sSampleDealingFuncs.pfMdCost[BLOCK_4x4](pDst, 4, pCurEnc, kiLineSizeEnc) +
                                             lambda[iPredMode == g_kiMapModeI4x4[iCurMode] ? 1 : 0];

                        if (iCurCost < iBestCost)
                        {
                            iBestMode = iCurMode;
                            iBestCost = iCurCost;
                            iBestPredBufferNum = 1 - iBestPredBufferNum;
                        }

                        if (iCurCost < iCostV)
                            iBestModeFake = 0;

                        iCurMode = I4_PRED_VL;

                        pDst = &pMbCache->pMemPredBlk4[(1 - iBestPredBufferNum) << 4];

                        pFunc->pfGetLumaI4x4Pred[iCurMode](pDst, pCurDec, kiLineSizeDec);
                        iCostVL = iCurCost = pFunc->sSampleDealingFuncs.pfMdCost[BLOCK_4x4](pDst, 4, pCurEnc, kiLineSizeEnc) +
                                             lambda[iPredMode == g_kiMapModeI4x4[iCurMode] ? 1 : 0];

                        if (iCurCost < iBestCost)
                        {
                            iBestMode = iCurMode;
                            iBestCost = iCurCost;
                            iBestPredBufferNum = 1 - iBestPredBufferNum;
                        }

                        if (iCurCost < iCostV)
                            iBestModeFake = 0;

                        //Vertical Early Determination
                        if (iBestModeFake == 0)
                        { //Vertical is not the best, go on checking...
                          //select the best one from VL and VR
                            if (iCostVR < iCostVL)
                            {
                                //I4_PRED_DDR(4)
                                iCurMode = I4_PRED_DDR;

                                pDst = &pMbCache->pMemPredBlk4[(1 - iBestPredBufferNum) << 4];

                                pFunc->pfGetLumaI4x4Pred[iCurMode](pDst, pCurDec, kiLineSizeDec);

                                iCurCost = pFunc->sSampleDealingFuncs.pfMdCost[BLOCK_4x4](pDst, 4, pCurEnc, kiLineSizeEnc) +
                                           lambda[iPredMode == g_kiMapModeI4x4[iCurMode] ? 1 : 0];

                                if (iCurCost < iBestCost)
                                {
                                    iBestMode = iCurMode;
                                    iBestCost = iCurCost;
                                    iBestPredBufferNum = 1 - iBestPredBufferNum;
                                }
                            }
                            else
                            {
                                //I4_PRED_DDL(3)
                                iCurMode = I4_PRED_DDL;

                                pDst = &pMbCache->pMemPredBlk4[(1 - iBestPredBufferNum) << 4];

                                pFunc->pfGetLumaI4x4Pred[iCurMode](pDst, pCurDec, kiLineSizeDec);

                                iCurCost = pFunc->sSampleDealingFuncs.pfMdCost[BLOCK_4x4](pDst, 4, pCurEnc, kiLineSizeEnc) +
                                           lambda[iPredMode == g_kiMapModeI4x4[iCurMode] ? 1 : 0];

                                if (iCurCost < iBestCost)
                                {
                                    iBestMode = iCurMode;
                                    iBestCost = iCurCost;
                                    iBestPredBufferNum = 1 - iBestPredBufferNum;
                                }
                            }
                        }
                    }
                    else if (iAvailCount == 7)
                    {
                        iCurMode = I4_PRED_DDR;

                        pDst = &pMbCache->pMemPredBlk4[(1 - iBestPredBufferNum) << 4];

                        pFunc->pfGetLumaI4x4Pred[iCurMode](pDst, pCurDec, kiLineSizeDec);
                        iCurCost = pFunc->sSampleDealingFuncs.pfMdCost[BLOCK_4x4](pDst, 4, pCurEnc, kiLineSizeEnc) +
                                   lambda[iPredMode == g_kiMapModeI4x4[iCurMode] ? 1 : 0];

                        if (iCurCost < iBestCost)
                        {
                            iBestMode = iCurMode;
                            iBestCost = iCurCost;
                            iBestPredBufferNum = 1 - iBestPredBufferNum;
                        }

                        iCurMode = I4_PRED_VR;

                        pDst = &pMbCache->pMemPredBlk4[(1 - iBestPredBufferNum) << 4];

                        pFunc->pfGetLumaI4x4Pred[iCurMode](pDst, pCurDec, kiLineSizeDec);

                        iCurCost = pFunc->sSampleDealingFuncs.pfMdCost[BLOCK_4x4](pDst, 4, pCurEnc, kiLineSizeEnc) +
                                   lambda[iPredMode == g_kiMapModeI4x4[iCurMode] ? 1 : 0];

                        if (iCurCost < iBestCost)
                        {
                            iBestMode = iCurMode;
                            iBestCost = iCurCost;
                            iBestPredBufferNum = 1 - iBestPredBufferNum;
                        }
                    }
                }
                else
                {
                    iBestModeFake = 1; //indicating whether H is the best fake mode
                    //I4_PRED_HD(6) and I4_PRED_HU(8)
                    iCurMode = I4_PRED_HD;

                    pDst = &pMbCache->pMemPredBlk4[(1 - iBestPredBufferNum) << 4];

                    pFunc->pfGetLumaI4x4Pred[iCurMode](pDst, pCurDec, kiLineSizeDec);
                    iCostHD = iCurCost = pFunc->sSampleDealingFuncs.pfMdCost[BLOCK_4x4](pDst, 4, pCurEnc, kiLineSizeEnc) +
                                         lambda[iPredMode == g_kiMapModeI4x4[iCurMode] ? 1 : 0];

                    if (iCurCost < iBestCost)
                    {
                        iBestMode = iCurMode;
                        iBestCost = iCurCost;
                        iBestPredBufferNum = 1 - iBestPredBufferNum;
                    }

                    if (iCurCost < iCostH)
                        iBestModeFake = 0;

                    iCurMode = I4_PRED_HU;

                    pDst = &pMbCache->pMemPredBlk4[(1 - iBestPredBufferNum) << 4];

                    pFunc->pfGetLumaI4x4Pred[iCurMode](pDst, pCurDec, kiLineSizeDec);
                    iCostHU = iCurCost = pFunc->sSampleDealingFuncs.pfMdCost[BLOCK_4x4](pDst, 4, pCurEnc, kiLineSizeEnc) +
                                         lambda[iPredMode == g_kiMapModeI4x4[iCurMode] ? 1 : 0];

                    if (iCurCost < iBestCost)
                    {
                        iBestMode = iCurMode;
                        iBestCost = iCurCost;
                        iBestPredBufferNum = 1 - iBestPredBufferNum;
                    }

                    if (iCurCost < iCostH)
                        iBestModeFake = 0;

                    if (iBestModeFake == 0)
                    { //Horizontal is not the best, go on checking...
                      //select the best one from VL and VR
                        if (iCostHD < iCostHU)
                        {
                            //I4_PRED_DDR(4)
                            iCurMode = I4_PRED_DDR;

                            pDst = &pMbCache->pMemPredBlk4[(1 - iBestPredBufferNum) << 4];

                            pFunc->pfGetLumaI4x4Pred[iCurMode](pDst, pCurDec, kiLineSizeDec);
                            iCurCost = pFunc->sSampleDealingFuncs.pfMdCost[BLOCK_4x4](pDst, 4, pCurEnc, kiLineSizeEnc) +
                                       lambda[iPredMode == g_kiMapModeI4x4[iCurMode] ? 1 : 0];

                            if (iCurCost < iBestCost)
                            {
                                iBestMode = iCurMode;
                                iBestCost = iCurCost;
                                iBestPredBufferNum = 1 - iBestPredBufferNum;
                            }
                        }
                        else if (iAvailCount == 9)
                        {
                            //I4_PRED_DDL(3)
                            iCurMode = I4_PRED_DDL;

                            pDst = &pMbCache->pMemPredBlk4[(1 - iBestPredBufferNum) << 4];
                            pFunc->pfGetLumaI4x4Pred[iCurMode](pDst, pCurDec, kiLineSizeDec);

                            iCurCost = pFunc->sSampleDealingFuncs.pfMdCost[BLOCK_4x4](pDst, 4, pCurEnc, kiLineSizeEnc) +
                                       lambda[iPredMode == g_kiMapModeI4x4[iCurMode] ? 1 : 0];

                            if (iCurCost < iBestCost)
                            {
                                iBestMode = iCurMode;
                                iBestCost = iCurCost;
                                iBestPredBufferNum = 1 - iBestPredBufferNum;
                            }

                        }
                    }
                }
            }
            else
            {
                iBestCost = INT_MAX;
                iBestMode = I4_PRED_INVALID;
                for (j = 0; j < iAvailCount; j++)
                {
                    // I4x4_MODE_CHECK(pAvailMode[j], iCurCost);
                    iCurMode = (int8_t)kpAvailMode[j];

                    pDst = &pMbCache->pMemPredBlk4[(1 - iBestPredBufferNum) << 4];

                    pFunc->pfGetLumaI4x4Pred[iCurMode](pDst, pCurDec, kiLineSizeDec);
                    iCurCost = pFunc->sSampleDealingFuncs.pfMdCost[BLOCK_4x4](pDst, 4, pCurEnc, kiLineSizeEnc) +
                               lambda[iPredMode == g_kiMapModeI4x4[iCurMode] ? 1 : 0];

                    if (iCurCost < iBestCost)
                    {
                        iBestMode = iCurMode;
                        iBestCost = iCurCost;
                        iBestPredBufferNum = 1 - iBestPredBufferNum;
                    }
                }
            }
            pMbCache->pBestPredI4x4Blk4 = &pMbCache->pMemPredBlk4[iBestPredBufferNum << 4];
            iCosti4x4 += iBestCost;
            if (iCosti4x4 >= iBestCostLuma)
            {
                break;
            }

            //step 5: update pred mode and sample avail cache
            iFinalMode = g_kiMapModeI4x4[iBestMode];
            if (iPredMode == iFinalMode)
            {
                *pPrevIntra4x4PredModeFlag++ = true;
            }
            else
            {
                *pPrevIntra4x4PredModeFlag++ = false;
                *pRemIntra4x4PredModeFlag = (int8_t)(iFinalMode < iPredMode ? iFinalMode : (iFinalMode - 1));
            }
            pRemIntra4x4PredModeFlag++;
            // pCurMb->pIntra4x4PredMode[scan4[i]] = iFinalMode;
            pMbCache->iIntraPredMode[kpCache48CountScan4[i]] = iFinalMode;
            //step 6: encoding I_4x4
            WelsEncRecI4x4Y(pEncCtx, pCurMb, pMbCache, (uint8_t)i);
        }
        ST32(pCurMb->pIntra4x4PredMode, LD32(&pMbCache->iIntraPredMode[33]));
        pCurMb->pIntra4x4PredMode[4] = pMbCache->iIntraPredMode[12];
        pCurMb->pIntra4x4PredMode[5] = pMbCache->iIntraPredMode[20];
        pCurMb->pIntra4x4PredMode[6] = pMbCache->iIntraPredMode[28];
        iCosti4x4 += (iLambda << 4) + (iLambda << 3); //4*6*lambda from JVT SATD0
        return iCosti4x4;
    }

    public static int32_t WelsMdIntraChroma(SWelsFuncPtrList* pFunc, SDqLayer* pCurDqLayer, SMbCache* pMbCache, int32_t iLambda)
    {
        int8_t* kpAvailMode;
        int32_t iAvailCount = 0;
        int32_t iChmaIdx = 0;
        uint8_t** pPredIntraChma = stackalloc uint8_t*[2];
        pPredIntraChma[0] = pMbCache->pMemPredChroma;
        pPredIntraChma[1] = pMbCache->pMemPredChroma + 128;
        uint8_t* pDstChma = pPredIntraChma[0];
        uint8_t* pEncCb = pMbCache->SPicData.pEncMb[1];
        uint8_t* pEncCr = pMbCache->SPicData.pEncMb[2];
        uint8_t* pDecCb = pMbCache->SPicData.pCsMb[1];//pMbCache->SPicData.pDecMb[1];
        uint8_t* pDecCr = pMbCache->SPicData.pCsMb[2];//pMbCache->SPicData.pDecMb[2];
        int32_t kiLineSizeEnc = pCurDqLayer->iEncStride[1];
        int32_t kiLineSizeDec = pCurDqLayer->iCsStride[1];//pMbCache->SPicData.i_stride_dec[1];

        int32_t i, iCurMode, iCurCost, iBestMode, iBestCost = INT_MAX;

        int32_t iOffset = pMbCache->uiNeighborIntra & 0x07;
        iAvailCount = g_kiIntraChromaAvailMode[iOffset][4];
        kpAvailMode = (int8_t*)(g_kiIntraChromaAvailMode + iOffset);
        if (iAvailCount > 3 && pFunc->sSampleDealingFuncs.pfIntra8x8Combined3 != null)
        {
            iBestCost = pFunc->sSampleDealingFuncs.pfIntra8x8Combined3(pDecCb, kiLineSizeDec, pEncCb, kiLineSizeEnc, &iBestMode,
                        iLambda, pDstChma, pDecCr, pEncCr);
            iCurMode = kpAvailMode[3];
            pFunc->pfGetChromaPred[iCurMode](pDstChma, pDecCb, kiLineSizeDec); //Cb
            pFunc->pfGetChromaPred[iCurMode](pDstChma + 64, pDecCr, kiLineSizeDec); //Cr

            iCurCost = pFunc->sSampleDealingFuncs.pfMdCost[BLOCK_8x8](pDstChma, 8, pEncCb, kiLineSizeEnc) +
                       pFunc->sSampleDealingFuncs.pfMdCost[BLOCK_8x8](pDstChma + 64, 8, pEncCr, kiLineSizeEnc) +
                       iLambda * 4;
            if (iCurCost < iBestCost)
            {
                iBestMode = iCurMode;
                iBestCost = iCurCost;
            }
            else
            {
                pFunc->pfGetChromaPred[iBestMode](pDstChma, pDecCb, kiLineSizeDec); //Cb
                pFunc->pfGetChromaPred[iBestMode](pDstChma + 64, pDecCr, kiLineSizeDec); //Cr
            }
            iBestCost += iLambda;
            iChmaIdx = 1;
        }
        else
        {
            iBestMode = kpAvailMode[0];
            for (i = 0; i < iAvailCount; ++i)
            {
                iCurMode = kpAvailMode[i];

                Debug.Assert(iCurMode >= 0 && iCurMode < 7);

                // pDstCb = &pMbCache->mem_pred_intra_cb[iCurMode<<6];
                // pDstCr = &pMbCache->mem_pred_intra_cr[iCurMode<<6];
                pFunc->pfGetChromaPred[iCurMode](pDstChma, pDecCb, kiLineSizeDec); //Cb
                iCurCost = pFunc->sSampleDealingFuncs.pfMdCost[BLOCK_8x8](pDstChma, 8, pEncCb, kiLineSizeEnc);

                pFunc->pfGetChromaPred[iCurMode](pDstChma + 64, pDecCr, kiLineSizeDec); //Cr
                iCurCost += pFunc->sSampleDealingFuncs.pfMdCost[BLOCK_8x8](pDstChma + 64, 8, pEncCr, kiLineSizeEnc) +
                            (int32_t)(iLambda * BsSizeUE((uint32_t)g_kiMapModeIntraChroma[iCurMode]));
                if (iCurCost < iBestCost)
                {
                    iBestMode = iCurMode;
                    iBestCost = iCurCost;
                    iChmaIdx = iChmaIdx ^ 0x01;
                    pDstChma = pPredIntraChma[iChmaIdx];
                }
            }
        }

        pMbCache->pBestPredIntraChroma = pPredIntraChma[iChmaIdx ^ 0x01];
        pMbCache->uiChmaI8x8Mode = (uint8_t)iBestMode;
        return iBestCost;
    }

    public static int32_t WelsMdIntraFinePartition(sWelsEncCtx* pEncCtx, SWelsMD* pWelsMd, SMB* pCurMb, SMbCache* pMbCache)
    {
        int32_t iCosti4x4 = WelsMdI4x4(pEncCtx, pWelsMd, pCurMb, pMbCache);

        if (iCosti4x4 < pWelsMd->iCostLuma)
        {
            pCurMb->uiMbType = MB_TYPE_INTRA4x4;
            pWelsMd->iCostLuma = iCosti4x4;
        }
        return pWelsMd->iCostLuma;
    }

    public static int32_t WelsMdIntraFinePartitionVaa(sWelsEncCtx* pEncCtx, SWelsMD* pWelsMd, SMB* pCurMb, SMbCache* pMbCache)
    {

        if (MdIntraAnalysisVaaInfo(pEncCtx, pMbCache->SPicData.pEncMb[0]))
        {
            int32_t iCosti4x4 = WelsMdI4x4Fast(pEncCtx, pWelsMd, pCurMb, pMbCache);

            if (iCosti4x4 < pWelsMd->iCostLuma)
            {
                pCurMb->uiMbType = MB_TYPE_INTRA4x4;
                pWelsMd->iCostLuma = iCosti4x4;
            }
        }

        return pWelsMd->iCostLuma;
    }

    public static void WelsMdIntraMb(sWelsEncCtx* pEncCtx, SWelsMD* pWelsMd, SMB* pCurMb, SMbCache* pMbCache)
    {
        //initial prediction memory for I_16x16
        pWelsMd->iCostLuma = WelsMdI16x16(pEncCtx->pFuncList, pEncCtx->pCurDqLayer, pMbCache, pWelsMd->iLambda);
        pCurMb->uiMbType = MB_TYPE_INTRA16x16;

        WelsMdIntraSecondaryModesEnc(pEncCtx, pWelsMd, pCurMb, pMbCache);
    }

    // The inter mode decision (InitMe .. WelsMdInterSecondaryModesEnc) comes with E5.

    public static void WelsMdIntraSecondaryModesEnc(sWelsEncCtx* pEncCtx, SWelsMD* pWelsMd, SMB* pCurMb, SMbCache* pMbCache)
    {
        SWelsFuncPtrList* pFunc = pEncCtx->pFuncList;
        //initial prediction memory for I_4x4
        pFunc->pfIntraFineMd(pEncCtx, pWelsMd, pCurMb, pMbCache); //WelsMdIntraFinePartitionVaa

        //add pEnc&rec to MD--2010.3.15
        if (IS_INTRA16x16(pCurMb->uiMbType))
        {
            pCurMb->uiCbp = 0;
            WelsEncRecI16x16Y(pEncCtx, pCurMb, pMbCache);
        }

        //chroma
        pWelsMd->iCostChroma = WelsMdIntraChroma(pFunc, pEncCtx->pCurDqLayer, pMbCache, pWelsMd->iLambda);
        WelsIMbChromaEncode(pEncCtx, pCurMb, pMbCache);  //add pEnc&rec to MD--2010.3.15
        pCurMb->uiChromPredMode = pMbCache->uiChmaI8x8Mode;
        pCurMb->pSadCost[0] = 0;
    }
}
