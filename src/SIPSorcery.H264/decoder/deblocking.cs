//-----------------------------------------------------------------------------
// Filename: deblocking.cs
//
// Description: Port of codec/decoder/core/inc/deblocking.h and
// src/deblocking.cpp: the in-loop deblocking filter, per macroblock and per
// slice, over the edge kernels in common/deblocking_common.cs.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// Everything but the B slice boundary strengths is ported: the alpha, beta
// and tc0 tables, the P slice strength derivation (4x4 and 8x8 transforms),
// the edge filters, the intra and inter macroblock filters, WelsDeblockingMb,
// the slice loop and the per-macroblock entry points the threaded decoder
// uses, and DeblockingInit with the C kernels only. The B slice strength
// functions (DeblockingBSliceBsMarginalMBAvcbase and
// DeblockingBSliceBSInsideMBNormal) throw: B slices are not ported yet, and
// this port's B macroblock parser fails on a slice's first macroblock, so a B
// slice reaches WelsTargetSliceConstruction with no macroblock to filter.
//
// The C nBS[2][4][4] strength array is a 32 byte block here, nBS[a][b][c] at
// nBS[16a + 4b + c] (Bs (nBS, a, b) is &nBS[a][b][0]); the ENFORCE_STACK_ALIGN
// arrays are plain stackallocs, which every path writes before reading.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 25 Nov 2026  Claude          Created (D5).
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
// The ported code is also covered by the OpenH264 BSD 2-Clause notice below.
//-----------------------------------------------------------------------------

/*!
 * \copy
 *     Copyright (c)  2010-2013, Cisco Systems
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
 * \file    deblocking.c
 *
 * \brief   Interfaces introduced in frame deblocking filtering
 *
 * \date    08/02/2010
 *
 *************************************************************************************
 */

namespace SIPSorcery.H264.Decoder;

internal static unsafe partial class WelsDec
{
    private const int NO_SUPPORTED_FILTER_IDX = -1;
    private const int LEFT_FLAG_BIT = 0;
    private const int TOP_FLAG_BIT = 1;
    private const int LEFT_FLAG_MASK = 0x01;
    private const int TOP_FLAG_MASK = 0x02;

    // SAME_MB_DIFF_REFIDX is defined upstream; SMB_EDGE_MV below is that variant.
    // g_kuiAlphaTable(x), g_kiBetaTable(x) and g_kiTc0Table(x) index with x + 12.

    // MB_BS_MV: strength 1 across a macroblock edge when the two blocks use
    // different pictures or their vectors differ by a full sample or more.
    private static uint8_t MB_BS_MV(SPicture* pRefPic0, SPicture* pRefPic1, InlineArray16<InlineArray2<int16_t>>* iMotionVector,
        int32_t iMbXy, int32_t iMbBn, int32_t iIndex, int32_t iNeighIndex) =>
        (pRefPic0 != pRefPic1
            || WELS_ABS(iMotionVector[iMbXy][iIndex][0] - iMotionVector[iMbBn][iNeighIndex][0]) >= 4
            || WELS_ABS(iMotionVector[iMbXy][iIndex][1] - iMotionVector[iMbBn][iNeighIndex][1]) >= 4) ? (uint8_t)1 : (uint8_t)0;

    // SMB_EDGE_MV (SAME_MB_DIFF_REFIDX): the same test between two blocks of one
    // macroblock; iMotionVector is that macroblock's 16 vectors.
    private static int32_t SMB_EDGE_MV(SPicture** pRefPics, InlineArray2<int16_t>* iMotionVector, int32_t iIndex, int32_t iNeighIndex) =>
        (pRefPics[iIndex] != pRefPics[iNeighIndex]
            || ((WELS_ABS(iMotionVector[iIndex][0] - iMotionVector[iNeighIndex][0]) & (~3))
                | (WELS_ABS(iMotionVector[iIndex][1] - iMotionVector[iNeighIndex][1]) & (~3))) != 0) ? 1 : 0;

    // BS_EDGE: 2 when either block has coefficients (bsx1), else SMB_EDGE_MV.
    private static uint8_t BS_EDGE(int32_t bsx1, SPicture** pRefPics, InlineArray2<int16_t>* iMotionVector, int32_t iIndex,
        int32_t iNeighIndex) =>
        (uint8_t)((bsx1 | SMB_EDGE_MV(pRefPics, iMotionVector, iIndex, iNeighIndex)) << (bsx1 != 0 ? 1 : 0));

    // GET_ALPHA_BETA_FROM_QP.
    private static void GET_ALPHA_BETA_FROM_QP(int32_t iQp, int32_t iAlphaOffset, int32_t iBetaOffset, out int32_t iIndex,
        out int32_t iAlpha, out int32_t iBeta)
    {
        iIndex = iQp + iAlphaOffset;
        iAlpha = g_kuiAlphaTable[iIndex + 12];
        iBeta = g_kiBetaTable[(iQp + iBetaOffset) + 12];
    }

    // static const uint8_t g_kuiAlphaTable[52 + 24] (Table 8-16 in H.264/AVC).
    private static readonly uint8_t* g_kuiAlphaTable = StaticTable.Of<uint8_t>(52 + 24, [
        0,  0,  0,  0,  0,  0,  0,  0,  0,  0, 0,  0,
        0,  0,  0,  0,  0,  0,  0,  0,  0,  0,
        0,  0,  0,  0,  0,  0,  4,  4,  5,  6,
        7,  8,  9, 10, 12, 13, 15, 17, 20, 22,
        25, 28, 32, 36, 40, 45, 50, 56, 63, 71,
        80, 90, 101, 113, 127, 144, 162, 182, 203, 226,
        255, 255
        , 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255
    ]);

    // static const int8_t g_kiBetaTable[52 + 24] (Table 8-16 in H.264/AVC).
    private static readonly int8_t* g_kiBetaTable = StaticTable.Of<int8_t>(52 + 24, [
        0,  0,  0,  0,  0,  0,  0,  0,  0,  0, 0,  0,
        0,  0,  0,  0,  0,  0,  0,  0,  0,  0,
        0,  0,  0,  0,  0,  0,  2,  2,  2,  3,
        3,  3,  3,  4,  4,  4,  6,  6,  7,  7,
        8,  8,  9,  9, 10, 10, 11, 11, 12, 12,
        13, 13, 14, 14, 15, 15, 16, 16, 17, 17,
        18, 18
        , 18, 18, 18, 18, 18, 18, 18, 18, 18, 18, 18, 18
    ]);

    // static const int8_t g_kiTc0Table[52 + 24][4] (Table 8-17 in H.264/AVC).
    private static readonly InlineArray4<int8_t>* g_kiTc0Table = StaticTable.Of<InlineArray4<int8_t>, int8_t>(52 + 24, [
        -1, 0, 0, 0,   -1, 0, 0, 0,   -1, 0, 0, 0,   -1, 0, 0, 0,   -1, 0, 0, 0,   -1, 0, 0, 0,
        -1, 0, 0, 0,   -1, 0, 0, 0,   -1, 0, 0, 0,   -1, 0, 0, 0,   -1, 0, 0, 0,   -1, 0, 0, 0,
        -1, 0, 0, 0,   -1, 0, 0, 0,   -1, 0, 0, 0,   -1, 0, 0, 0,   -1, 0, 0, 0,   -1, 0, 0, 0,
        -1, 0, 0, 0,   -1, 0, 0, 0,   -1, 0, 0, 0,   -1, 0, 0, 0,   -1, 0, 0, 0,   -1, 0, 0, 0,
        -1, 0, 0, 0,   -1, 0, 0, 0,   -1, 0, 0, 0,   -1, 0, 0, 0,   -1, 0, 0, 0,   -1, 0, 0, 1,
        -1, 0, 0, 1,   -1, 0, 0, 1,   -1, 0, 0, 1,   -1, 0, 1, 1,   -1, 0, 1, 1,   -1, 1, 1, 1,
        -1, 1, 1, 1,   -1, 1, 1, 1,   -1, 1, 1, 1,   -1, 1, 1, 2,   -1, 1, 1, 2,   -1, 1, 1, 2,
        -1, 1, 1, 2,   -1, 1, 2, 3,   -1, 1, 2, 3,   -1, 2, 2, 3,   -1, 2, 2, 4,   -1, 2, 3, 4,
        -1, 2, 3, 4,   -1, 3, 3, 5,   -1, 3, 4, 6,   -1, 3, 4, 6,   -1, 4, 5, 7,   -1, 4, 5, 8,
        -1, 4, 6, 9,   -1, 5, 7, 10,  -1, 6, 8, 11,  -1, 6, 8, 13,  -1, 7, 10, 14, -1, 8, 11, 16,
        -1, 9, 12, 18, -1, 10, 13, 20, -1, 11, 15, 23, -1, 13, 17, 25
        , -1, 13, 17, 25, -1, 13, 17, 25, -1, 13, 17, 25, -1, 13, 17, 25, -1, 13, 17, 25, -1, 13, 17, 25
        , -1, 13, 17, 25, -1, 13, 17, 25, -1, 13, 17, 25, -1, 13, 17, 25, -1, 13, 17, 25, -1, 13, 17, 25
    ]);

    // static const uint8_t g_kuiTableBIdx[2][8].
    private static readonly InlineArray8<uint8_t>* g_kuiTableBIdx = StaticTable.Of<InlineArray8<uint8_t>, uint8_t>(2, [
        0,  4,  8,  12,
        3,  7,  11, 15,

        0,  1,  2,  3,
        12, 13, 14, 15,
    ]);

    // static const uint8_t g_kuiTableB8x8Idx[2][16].
    private static readonly InlineArray16<uint8_t>* g_kuiTableB8x8Idx = StaticTable.Of<InlineArray16<uint8_t>, uint8_t>(2, [
        0,  1,  4,  5,  8,  9,  12, 13,   // 0   1 |  2  3
        2,  3,  6,  7, 10, 11,  14, 15,   // 4   5 |  6  7
                                          // ------------
                                          // 8   9 | 10 11
                                          // 12 13 | 14 15
        0,  1,  4,  5,  2,  3,  6,  7,
        8,  9,  12, 13, 10, 11, 14, 15
    ]);

    // &nBS[iDir][iEdge][0] of the C uint8_t nBS[2][4][4].
    private static uint8_t* Bs(uint8_t* nBS, int32_t iDir, int32_t iEdge) => nBS + (iDir << 4) + (iEdge << 2);

    //fix Bugzilla 1486223
    // TC0_TBL_LOOKUP.
    private static void TC0_TBL_LOOKUP(int8_t* tc, int32_t iIndexA, uint8_t* pBS, int32_t bChroma)
    {
        InlineArray4<int8_t>* pTc0 = &g_kiTc0Table[iIndexA + 12];
        tc[0] = (int8_t)((*pTc0)[pBS[0] & 3] + bChroma);
        tc[1] = (int8_t)((*pTc0)[pBS[1] & 3] + bChroma);
        tc[2] = (int8_t)((*pTc0)[pBS[2] & 3] + bChroma);
        tc[3] = (int8_t)((*pTc0)[pBS[3] & 3] + bChroma);
    }

    // deblocking.h's GetPNzc: the picture's non-zero counts when it has them,
    // else the layer's.
    public static int8_t* GetPNzc(SDqLayer* pCurDqLayer, int32_t iMbXy)
    {
        if (pCurDqLayer->pDec != null && pCurDqLayer->pDec->pNzc != null)
        {
            return (int8_t*)&pCurDqLayer->pDec->pNzc[iMbXy];
        }
        return (int8_t*)&pCurDqLayer->pNzc[iMbXy];
    }

    private static void DeblockingBSInsideMBAvsbase(int8_t* pNnzTab, uint8_t* nBS, int32_t iLShiftFactor)
    {
        uint32_t uiNnz32b0, uiNnz32b1, uiNnz32b2, uiNnz32b3;

        uiNnz32b0 = *(uint32_t*)(pNnzTab + 0);
        uiNnz32b1 = *(uint32_t*)(pNnzTab + 4);
        uiNnz32b2 = *(uint32_t*)(pNnzTab + 8);
        uiNnz32b3 = *(uint32_t*)(pNnzTab + 12);

        Bs(nBS, 0, 1)[0] = (uint8_t)((pNnzTab[0] | pNnzTab[1]) << iLShiftFactor);
        Bs(nBS, 0, 2)[0] = (uint8_t)((pNnzTab[1] | pNnzTab[2]) << iLShiftFactor);
        Bs(nBS, 0, 3)[0] = (uint8_t)((pNnzTab[2] | pNnzTab[3]) << iLShiftFactor);

        Bs(nBS, 0, 1)[1] = (uint8_t)((pNnzTab[4] | pNnzTab[5]) << iLShiftFactor);
        Bs(nBS, 0, 2)[1] = (uint8_t)((pNnzTab[5] | pNnzTab[6]) << iLShiftFactor);
        Bs(nBS, 0, 3)[1] = (uint8_t)((pNnzTab[6] | pNnzTab[7]) << iLShiftFactor);
        *(uint32_t*)Bs(nBS, 1, 1) = (uiNnz32b0 | uiNnz32b1) << iLShiftFactor;

        Bs(nBS, 0, 1)[2] = (uint8_t)((pNnzTab[8] | pNnzTab[9]) << iLShiftFactor);
        Bs(nBS, 0, 2)[2] = (uint8_t)((pNnzTab[9] | pNnzTab[10]) << iLShiftFactor);
        Bs(nBS, 0, 3)[2] = (uint8_t)((pNnzTab[10] | pNnzTab[11]) << iLShiftFactor);
        *(uint32_t*)Bs(nBS, 1, 2) = (uiNnz32b1 | uiNnz32b2) << iLShiftFactor;

        Bs(nBS, 0, 1)[3] = (uint8_t)((pNnzTab[12] | pNnzTab[13]) << iLShiftFactor);
        Bs(nBS, 0, 2)[3] = (uint8_t)((pNnzTab[13] | pNnzTab[14]) << iLShiftFactor);
        Bs(nBS, 0, 3)[3] = (uint8_t)((pNnzTab[14] | pNnzTab[15]) << iLShiftFactor);
        *(uint32_t*)Bs(nBS, 1, 3) = (uiNnz32b2 | uiNnz32b3) << iLShiftFactor;
    }

    private static void DeblockingBSInsideMBAvsbase8x8(int8_t* pNnzTab, uint8_t* nBS, int32_t iLShiftFactor)
    {
        int8_t* i8x8NnzTab = stackalloc int8_t[4];
        for (int32_t i = 0; i < 4; i++)
        {
            int32_t iBlkIdx = i << 2;
            i8x8NnzTab[i] = (int8_t)((uint8_t)pNnzTab[g_kuiMbCountScan4Idx[iBlkIdx]] | (uint8_t)pNnzTab[g_kuiMbCountScan4Idx[iBlkIdx + 1]] |
                                     (uint8_t)pNnzTab[g_kuiMbCountScan4Idx[iBlkIdx + 2]] | (uint8_t)pNnzTab[g_kuiMbCountScan4Idx[iBlkIdx + 3]]);
        }

        //vertical
        Bs(nBS, 0, 2)[0] = Bs(nBS, 0, 2)[1] = (uint8_t)((i8x8NnzTab[0] | i8x8NnzTab[1]) << iLShiftFactor);
        Bs(nBS, 0, 2)[2] = Bs(nBS, 0, 2)[3] = (uint8_t)((i8x8NnzTab[2] | i8x8NnzTab[3]) << iLShiftFactor);
        //horizontal
        Bs(nBS, 1, 2)[0] = Bs(nBS, 1, 2)[1] = (uint8_t)((i8x8NnzTab[0] | i8x8NnzTab[2]) << iLShiftFactor);
        Bs(nBS, 1, 2)[2] = Bs(nBS, 1, 2)[3] = (uint8_t)((i8x8NnzTab[1] | i8x8NnzTab[3]) << iLShiftFactor);
    }

    private static void DeblockingBSInsideMBNormal(SDeblockingFilter* pFilter, SDqLayer* pCurDqLayer, uint8_t* nBS,
        int8_t* pNnzTab, int32_t iMbXy)
    {
        uint32_t uiNnz32b0, uiNnz32b1, uiNnz32b2, uiNnz32b3;
        int8_t* iRefIdx = (int8_t*)&pCurDqLayer->pDec->pRefIndex[(int)LIST_0].p[iMbXy];
        SPicture** iRefs = stackalloc SPicture*[MB_BLOCK4x4_NUM];
        int i;
        uint8_t* uiBsx4 = stackalloc uint8_t[4];
        InlineArray2<int16_t>* pMv = (InlineArray2<int16_t>*)&pCurDqLayer->pDec->pMv[(int)LIST_0].p[iMbXy];

        int8_t* i8x8NnzTab = stackalloc int8_t[4];

        /* Look up each reference picture based on indices */
        for (i = 0; i < MB_BLOCK4x4_NUM; i++)
        {
            if (iRefIdx[i] > REF_NOT_IN_LIST)
            {
                iRefs[i] = pFilter->pRefPics[(int)LIST_0].p[iRefIdx[i]];
            }
            else
            {
                iRefs[i] = null;
            }
        }

        if (pCurDqLayer->pTransformSize8x8Flag[iMbXy])
        {
            for (int32_t j = 0; j < 4; j++)
            {
                int32_t iBlkIdx = j << 2;
                i8x8NnzTab[j] = (int8_t)((uint8_t)pNnzTab[g_kuiMbCountScan4Idx[iBlkIdx]] | (uint8_t)pNnzTab[g_kuiMbCountScan4Idx[iBlkIdx + 1]] |
                                         (uint8_t)pNnzTab[g_kuiMbCountScan4Idx[iBlkIdx + 2]] | (uint8_t)pNnzTab[g_kuiMbCountScan4Idx[iBlkIdx + 3]]);
            }
            //vertical
            Bs(nBS, 0, 2)[0] = Bs(nBS, 0, 2)[1] = BS_EDGE(i8x8NnzTab[0] | i8x8NnzTab[1], iRefs, pMv,
                                                          g_kuiMbCountScan4Idx[1 << 2], g_kuiMbCountScan4Idx[0]);
            Bs(nBS, 0, 2)[2] = Bs(nBS, 0, 2)[3] = BS_EDGE(i8x8NnzTab[2] | i8x8NnzTab[3], iRefs, pMv,
                                                          g_kuiMbCountScan4Idx[3 << 2], g_kuiMbCountScan4Idx[2 << 2]);

            //horizontal
            Bs(nBS, 1, 2)[0] = Bs(nBS, 1, 2)[1] = BS_EDGE(i8x8NnzTab[0] | i8x8NnzTab[2], iRefs, pMv,
                                                          g_kuiMbCountScan4Idx[2 << 2], g_kuiMbCountScan4Idx[0]);
            Bs(nBS, 1, 2)[2] = Bs(nBS, 1, 2)[3] = BS_EDGE(i8x8NnzTab[1] | i8x8NnzTab[3], iRefs, pMv,
                                                          g_kuiMbCountScan4Idx[3 << 2], g_kuiMbCountScan4Idx[1 << 2]);
        }
        else
        {
            uiNnz32b0 = *(uint32_t*)(pNnzTab + 0);
            uiNnz32b1 = *(uint32_t*)(pNnzTab + 4);
            uiNnz32b2 = *(uint32_t*)(pNnzTab + 8);
            uiNnz32b3 = *(uint32_t*)(pNnzTab + 12);

            for (int j = 0; j < 3; j++)
            {
                uiBsx4[j] = (uint8_t)(pNnzTab[j] | pNnzTab[j + 1]);
            }
            Bs(nBS, 0, 1)[0] = BS_EDGE(uiBsx4[0], iRefs, pMv, 1, 0);
            Bs(nBS, 0, 2)[0] = BS_EDGE(uiBsx4[1], iRefs, pMv, 2, 1);
            Bs(nBS, 0, 3)[0] = BS_EDGE(uiBsx4[2], iRefs, pMv, 3, 2);

            for (int j = 0; j < 3; j++)
            {
                uiBsx4[j] = (uint8_t)(pNnzTab[4 + j] | pNnzTab[4 + j + 1]);
            }
            Bs(nBS, 0, 1)[1] = BS_EDGE(uiBsx4[0], iRefs, pMv, 5, 4);
            Bs(nBS, 0, 2)[1] = BS_EDGE(uiBsx4[1], iRefs, pMv, 6, 5);
            Bs(nBS, 0, 3)[1] = BS_EDGE(uiBsx4[2], iRefs, pMv, 7, 6);

            for (int j = 0; j < 3; j++)
            {
                uiBsx4[j] = (uint8_t)(pNnzTab[8 + j] | pNnzTab[8 + j + 1]);
            }
            Bs(nBS, 0, 1)[2] = BS_EDGE(uiBsx4[0], iRefs, pMv, 9, 8);
            Bs(nBS, 0, 2)[2] = BS_EDGE(uiBsx4[1], iRefs, pMv, 10, 9);
            Bs(nBS, 0, 3)[2] = BS_EDGE(uiBsx4[2], iRefs, pMv, 11, 10);

            for (int j = 0; j < 3; j++)
            {
                uiBsx4[j] = (uint8_t)(pNnzTab[12 + j] | pNnzTab[12 + j + 1]);
            }
            Bs(nBS, 0, 1)[3] = BS_EDGE(uiBsx4[0], iRefs, pMv, 13, 12);
            Bs(nBS, 0, 2)[3] = BS_EDGE(uiBsx4[1], iRefs, pMv, 14, 13);
            Bs(nBS, 0, 3)[3] = BS_EDGE(uiBsx4[2], iRefs, pMv, 15, 14);

            // horizontal
            *(uint32_t*)uiBsx4 = (uiNnz32b0 | uiNnz32b1);
            Bs(nBS, 1, 1)[0] = BS_EDGE(uiBsx4[0], iRefs, pMv, 4, 0);
            Bs(nBS, 1, 1)[1] = BS_EDGE(uiBsx4[1], iRefs, pMv, 5, 1);
            Bs(nBS, 1, 1)[2] = BS_EDGE(uiBsx4[2], iRefs, pMv, 6, 2);
            Bs(nBS, 1, 1)[3] = BS_EDGE(uiBsx4[3], iRefs, pMv, 7, 3);

            *(uint32_t*)uiBsx4 = (uiNnz32b1 | uiNnz32b2);
            Bs(nBS, 1, 2)[0] = BS_EDGE(uiBsx4[0], iRefs, pMv, 8, 4);
            Bs(nBS, 1, 2)[1] = BS_EDGE(uiBsx4[1], iRefs, pMv, 9, 5);
            Bs(nBS, 1, 2)[2] = BS_EDGE(uiBsx4[2], iRefs, pMv, 10, 6);
            Bs(nBS, 1, 2)[3] = BS_EDGE(uiBsx4[3], iRefs, pMv, 11, 7);

            *(uint32_t*)uiBsx4 = (uiNnz32b2 | uiNnz32b3);
            Bs(nBS, 1, 3)[0] = BS_EDGE(uiBsx4[0], iRefs, pMv, 12, 8);
            Bs(nBS, 1, 3)[1] = BS_EDGE(uiBsx4[1], iRefs, pMv, 13, 9);
            Bs(nBS, 1, 3)[2] = BS_EDGE(uiBsx4[2], iRefs, pMv, 14, 10);
            Bs(nBS, 1, 3)[3] = BS_EDGE(uiBsx4[3], iRefs, pMv, 15, 11);
        }
    }

    // Not ported (B slices); unreachable, see the file header.
    private static void DeblockingBSliceBSInsideMBNormal(SDeblockingFilter* pFilter, SDqLayer* pCurDqLayer, uint8_t* nBS,
        int8_t* pNnzTab, int32_t iMbXy) =>
        throw new NotSupportedException("DeblockingBSliceBSInsideMBNormal: B slices are not supported by this port yet.");

    public static uint32_t DeblockingBsMarginalMBAvcbase(SDeblockingFilter* pFilter, SDqLayer* pCurDqLayer, int32_t iEdge,
        int32_t iNeighMb, int32_t iMbXy)
    {
        int32_t i, j;
        uint32_t uiBSx4;
        uint8_t* pBS = (uint8_t*)(&uiBSx4);
        uint8_t* pBIdx = (uint8_t*)&g_kuiTableBIdx[iEdge] + 0;
        uint8_t* pBnIdx = (uint8_t*)&g_kuiTableBIdx[iEdge] + 4;
        uint8_t* pB8x8Idx = (uint8_t*)&g_kuiTableB8x8Idx[iEdge] + 0;
        uint8_t* pBn8x8Idx = (uint8_t*)&g_kuiTableB8x8Idx[iEdge] + 8;
        InlineArray16<int8_t>* iRefIdx = pCurDqLayer->pDec != null ? pCurDqLayer->pDec->pRefIndex[(int)LIST_0].p :
                                         pCurDqLayer->pRefIndex[(int)LIST_0].p;
        InlineArray16<InlineArray2<int16_t>>* pMv = pCurDqLayer->pDec != null ? pCurDqLayer->pDec->pMv[(int)LIST_0].p :
                                                    pCurDqLayer->pMv[(int)LIST_0].p;
        SPicture** pRefPics0 = (SPicture**)pFilter->pRefPics[(int)LIST_0].p;

        uiBSx4 = 0; // every branch writes all four bytes; C leaves it uninitialised

        if (pCurDqLayer->pTransformSize8x8Flag[iMbXy] && pCurDqLayer->pTransformSize8x8Flag[iNeighMb])
        {
            for (i = 0; i < 2; i++)
            {
                uint8_t uiNzc = 0;
                for (j = 0; uiNzc == 0 && j < 4; j++)
                {
                    uiNzc |= (uint8_t)(GetPNzc(pCurDqLayer, iMbXy)[*(pB8x8Idx + j)] | GetPNzc(pCurDqLayer, iNeighMb)[*(pBn8x8Idx + j)]);
                }
                if (uiNzc != 0)
                {
                    pBS[i << 1] = pBS[1 + (i << 1)] = 2;
                }
                else
                {
                    SPicture* ref0, ref1;
                    ref0 = (iRefIdx[iMbXy][*pB8x8Idx] > REF_NOT_IN_LIST) ? pRefPics0[iRefIdx[iMbXy][*pB8x8Idx]] : null;
                    ref1 = (iRefIdx[iNeighMb][*pBn8x8Idx] > REF_NOT_IN_LIST) ? pRefPics0[iRefIdx[iNeighMb][*pBn8x8Idx]] :
                           null;
                    pBS[i << 1] = pBS[1 + (i << 1)] = MB_BS_MV(ref0, ref1, pCurDqLayer->pDec->pMv[(int)LIST_0].p, iMbXy, iNeighMb,
                                                               *pB8x8Idx, *pBn8x8Idx);
                }
                pB8x8Idx += 4;
                pBn8x8Idx += 4;
            }
        }
        else if (pCurDqLayer->pTransformSize8x8Flag[iMbXy])
        {
            for (i = 0; i < 2; i++)
            {
                uint8_t uiNzc = 0;
                for (j = 0; uiNzc == 0 && j < 4; j++)
                {
                    uiNzc |= (uint8_t)GetPNzc(pCurDqLayer, iMbXy)[*(pB8x8Idx + j)];
                }
                for (j = 0; j < 2; j++)
                {
                    if ((uiNzc | (uint8_t)GetPNzc(pCurDqLayer, iNeighMb)[*pBnIdx]) != 0)
                    {
                        pBS[j + (i << 1)] = 2;
                    }
                    else
                    {
                        SPicture* ref0, ref1;
                        ref0 = (iRefIdx[iMbXy][*pB8x8Idx] > REF_NOT_IN_LIST) ? pRefPics0[iRefIdx[iMbXy][*pB8x8Idx]] : null;
                        ref1 = (iRefIdx[iNeighMb][*pBnIdx] > REF_NOT_IN_LIST) ? pRefPics0[iRefIdx[iNeighMb][*pBnIdx]] : null;
                        pBS[j + (i << 1)] = MB_BS_MV(ref0, ref1, pMv, iMbXy, iNeighMb, *pB8x8Idx, *pBnIdx);
                    }
                    pBnIdx++;
                }
                pB8x8Idx += 4;
            }
        }
        else if (pCurDqLayer->pTransformSize8x8Flag[iNeighMb])
        {
            for (i = 0; i < 2; i++)
            {
                uint8_t uiNzc = 0;
                for (j = 0; uiNzc == 0 && j < 4; j++)
                {
                    uiNzc |= (uint8_t)GetPNzc(pCurDqLayer, iNeighMb)[*(pBn8x8Idx + j)];
                }
                for (j = 0; j < 2; j++)
                {
                    if ((uiNzc | (uint8_t)GetPNzc(pCurDqLayer, iMbXy)[*pBIdx]) != 0)
                    {
                        pBS[j + (i << 1)] = 2;
                    }
                    else
                    {
                        SPicture* ref0, ref1;
                        ref0 = (iRefIdx[iMbXy][*pBIdx] > REF_NOT_IN_LIST) ? pRefPics0[iRefIdx[iMbXy][*pBIdx]] : null;
                        ref1 = (iRefIdx[iNeighMb][*pBn8x8Idx] > REF_NOT_IN_LIST) ? pRefPics0[iRefIdx[iNeighMb][*pBn8x8Idx]] :
                               null;
                        pBS[j + (i << 1)] = MB_BS_MV(ref0, ref1, pMv, iMbXy, iNeighMb, *pBIdx, *pBn8x8Idx);
                    }
                    pBIdx++;
                }
                pBn8x8Idx += 4;
            }
        }
        else
        {
            // only 4x4 transform
            for (i = 0; i < 4; i++)
            {
                if ((GetPNzc(pCurDqLayer, iMbXy)[*pBIdx] | GetPNzc(pCurDqLayer, iNeighMb)[*pBnIdx]) != 0)
                {
                    pBS[i] = 2;
                }
                else
                {
                    SPicture* ref0, ref1;
                    ref0 = (iRefIdx[iMbXy][*pBIdx] > REF_NOT_IN_LIST) ? pRefPics0[iRefIdx[iMbXy][*pBIdx]] : null;
                    ref1 = (iRefIdx[iNeighMb][*pBnIdx] > REF_NOT_IN_LIST) ? pRefPics0[iRefIdx[iNeighMb][*pBnIdx]] : null;
                    pBS[i] = MB_BS_MV(ref0, ref1, pMv, iMbXy, iNeighMb, *pBIdx, *pBnIdx);
                }
                pBIdx++;
                pBnIdx++;
            }
        }

        return uiBSx4;
    }

    // Not ported (B slices); unreachable, see the file header.
    public static uint32_t DeblockingBSliceBsMarginalMBAvcbase(SDeblockingFilter* pFilter, SDqLayer* pCurDqLayer, int32_t iEdge,
        int32_t iNeighMb, int32_t iMbXy) =>
        throw new NotSupportedException("DeblockingBSliceBsMarginalMBAvcbase: B slices are not supported by this port yet.");

    public static int32_t DeblockingAvailableNoInterlayer(SDqLayer* pCurDqLayer, int32_t iFilterIdc)
    {
        int32_t iMbY = pCurDqLayer->iMbY;
        int32_t iMbX = pCurDqLayer->iMbX;
        int32_t iMbXy = pCurDqLayer->iMbXyIndex;
        bool bLeftFlag = false;
        bool bTopFlag = false;

        if (2 == iFilterIdc)
        {
            bLeftFlag = (iMbX > 0) && (pCurDqLayer->pSliceIdc[iMbXy] == pCurDqLayer->pSliceIdc[iMbXy - 1]);
            bTopFlag = (iMbY > 0) && (pCurDqLayer->pSliceIdc[iMbXy] == pCurDqLayer->pSliceIdc[iMbXy - pCurDqLayer->iMbWidth]);
        }
        else
        { //if ( 0 == iFilterIdc )
            bLeftFlag = (iMbX > 0);
            bTopFlag = (iMbY > 0);
        }
        return ((bLeftFlag ? 1 : 0) << LEFT_FLAG_BIT) | ((bTopFlag ? 1 : 0) << TOP_FLAG_BIT);
    }

    private static void FilteringEdgeLumaH(SDeblockingFilter* pFilter, uint8_t* pPix, int32_t iStride, uint8_t* pBS)
    {
        int8_t* tc = stackalloc int8_t[4];

        GET_ALPHA_BETA_FROM_QP(pFilter->iLumaQP, pFilter->iSliceAlphaC0Offset, pFilter->iSliceBetaOffset, out int32_t iIndexA,
            out int32_t iAlpha, out int32_t iBeta);

        if ((iAlpha | iBeta) != 0)
        {
            TC0_TBL_LOOKUP(tc, iIndexA, pBS, 0);
            pFilter->pLoopf->pfLumaDeblockingLT4Ver(pPix, iStride, iAlpha, iBeta, tc);
        }
    }

    private static void FilteringEdgeLumaV(SDeblockingFilter* pFilter, uint8_t* pPix, int32_t iStride, uint8_t* pBS)
    {
        int8_t* tc = stackalloc int8_t[4];

        GET_ALPHA_BETA_FROM_QP(pFilter->iLumaQP, pFilter->iSliceAlphaC0Offset, pFilter->iSliceBetaOffset, out int32_t iIndexA,
            out int32_t iAlpha, out int32_t iBeta);

        if ((iAlpha | iBeta) != 0)
        {
            TC0_TBL_LOOKUP(tc, iIndexA, pBS, 0);
            pFilter->pLoopf->pfLumaDeblockingLT4Hor(pPix, iStride, iAlpha, iBeta, tc);
        }
    }

    private static void FilteringEdgeLumaIntraH(SDeblockingFilter* pFilter, uint8_t* pPix, int32_t iStride, uint8_t* pBS)
    {
        GET_ALPHA_BETA_FROM_QP(pFilter->iLumaQP, pFilter->iSliceAlphaC0Offset, pFilter->iSliceBetaOffset, out _,
            out int32_t iAlpha, out int32_t iBeta);

        if ((iAlpha | iBeta) != 0)
        {
            pFilter->pLoopf->pfLumaDeblockingEQ4Ver(pPix, iStride, iAlpha, iBeta);
        }
    }

    private static void FilteringEdgeLumaIntraV(SDeblockingFilter* pFilter, uint8_t* pPix, int32_t iStride, uint8_t* pBS)
    {
        GET_ALPHA_BETA_FROM_QP(pFilter->iLumaQP, pFilter->iSliceAlphaC0Offset, pFilter->iSliceBetaOffset, out _,
            out int32_t iAlpha, out int32_t iBeta);

        if ((iAlpha | iBeta) != 0)
        {
            pFilter->pLoopf->pfLumaDeblockingEQ4Hor(pPix, iStride, iAlpha, iBeta);
        }
    }

    private static void FilteringEdgeChromaH(SDeblockingFilter* pFilter, uint8_t* pPixCb, uint8_t* pPixCr, int32_t iStride,
        uint8_t* pBS)
    {
        int32_t iIndexA;
        int32_t iAlpha;
        int32_t iBeta;
        int8_t* tc = stackalloc int8_t[4];
        if (pFilter->iChromaQP[0] == pFilter->iChromaQP[1])
        {
            GET_ALPHA_BETA_FROM_QP(pFilter->iChromaQP[0], pFilter->iSliceAlphaC0Offset, pFilter->iSliceBetaOffset, out iIndexA,
                out iAlpha, out iBeta);

            if ((iAlpha | iBeta) != 0)
            {
                TC0_TBL_LOOKUP(tc, iIndexA, pBS, 1);
                pFilter->pLoopf->pfChromaDeblockingLT4Ver(pPixCb, pPixCr, iStride, iAlpha, iBeta, tc);
            }
        }
        else
        {
            for (int i = 0; i < 2; i++)
            {
                GET_ALPHA_BETA_FROM_QP(pFilter->iChromaQP[i], pFilter->iSliceAlphaC0Offset, pFilter->iSliceBetaOffset, out iIndexA,
                    out iAlpha, out iBeta);

                if ((iAlpha | iBeta) != 0)
                {
                    uint8_t* pPixCbCr = (i == 0) ? pPixCb : pPixCr;
                    TC0_TBL_LOOKUP(tc, iIndexA, pBS, 1);
                    pFilter->pLoopf->pfChromaDeblockingLT4Ver2(pPixCbCr, iStride, iAlpha, iBeta, tc);
                }
            }
        }
    }

    private static void FilteringEdgeChromaV(SDeblockingFilter* pFilter, uint8_t* pPixCb, uint8_t* pPixCr, int32_t iStride,
        uint8_t* pBS)
    {
        int32_t iIndexA;
        int32_t iAlpha;
        int32_t iBeta;
        int8_t* tc = stackalloc int8_t[4];
        if (pFilter->iChromaQP[0] == pFilter->iChromaQP[1])
        {
            GET_ALPHA_BETA_FROM_QP(pFilter->iChromaQP[0], pFilter->iSliceAlphaC0Offset, pFilter->iSliceBetaOffset, out iIndexA,
                out iAlpha, out iBeta);

            if ((iAlpha | iBeta) != 0)
            {
                TC0_TBL_LOOKUP(tc, iIndexA, pBS, 1);
                pFilter->pLoopf->pfChromaDeblockingLT4Hor(pPixCb, pPixCr, iStride, iAlpha, iBeta, tc);
            }
        }
        else
        {
            for (int i = 0; i < 2; i++)
            {
                GET_ALPHA_BETA_FROM_QP(pFilter->iChromaQP[i], pFilter->iSliceAlphaC0Offset, pFilter->iSliceBetaOffset, out iIndexA,
                    out iAlpha, out iBeta);

                if ((iAlpha | iBeta) != 0)
                {
                    uint8_t* pPixCbCr = (i == 0) ? pPixCb : pPixCr;
                    TC0_TBL_LOOKUP(tc, iIndexA, pBS, 1);
                    pFilter->pLoopf->pfChromaDeblockingLT4Hor2(pPixCbCr, iStride, iAlpha, iBeta, tc);
                }
            }
        }
    }

    private static void FilteringEdgeChromaIntraH(SDeblockingFilter* pFilter, uint8_t* pPixCb, uint8_t* pPixCr, int32_t iStride,
        uint8_t* pBS)
    {
        int32_t iAlpha;
        int32_t iBeta;
        if (pFilter->iChromaQP[0] == pFilter->iChromaQP[1])
        {
            GET_ALPHA_BETA_FROM_QP(pFilter->iChromaQP[0], pFilter->iSliceAlphaC0Offset, pFilter->iSliceBetaOffset, out _,
                out iAlpha, out iBeta);

            if ((iAlpha | iBeta) != 0)
            {
                pFilter->pLoopf->pfChromaDeblockingEQ4Ver(pPixCb, pPixCr, iStride, iAlpha, iBeta);
            }
        }
        else
        {
            for (int i = 0; i < 2; i++)
            {
                GET_ALPHA_BETA_FROM_QP(pFilter->iChromaQP[i], pFilter->iSliceAlphaC0Offset, pFilter->iSliceBetaOffset, out _,
                    out iAlpha, out iBeta);

                if ((iAlpha | iBeta) != 0)
                {
                    uint8_t* pPixCbCr = (i == 0) ? pPixCb : pPixCr;
                    pFilter->pLoopf->pfChromaDeblockingEQ4Ver2(pPixCbCr, iStride, iAlpha, iBeta);
                }
            }
        }
    }

    private static void FilteringEdgeChromaIntraV(SDeblockingFilter* pFilter, uint8_t* pPixCb, uint8_t* pPixCr, int32_t iStride,
        uint8_t* pBS)
    {
        int32_t iAlpha;
        int32_t iBeta;
        if (pFilter->iChromaQP[0] == pFilter->iChromaQP[1])
        { // QP of cb and cr are the same
            GET_ALPHA_BETA_FROM_QP(pFilter->iChromaQP[0], pFilter->iSliceAlphaC0Offset, pFilter->iSliceBetaOffset, out _,
                out iAlpha, out iBeta);
            if ((iAlpha | iBeta) != 0)
            {
                pFilter->pLoopf->pfChromaDeblockingEQ4Hor(pPixCb, pPixCr, iStride, iAlpha, iBeta);
            }
        }
        else
        {
            for (int i = 0; i < 2; i++)
            {
                GET_ALPHA_BETA_FROM_QP(pFilter->iChromaQP[i], pFilter->iSliceAlphaC0Offset, pFilter->iSliceBetaOffset, out _,
                    out iAlpha, out iBeta);
                if ((iAlpha | iBeta) != 0)
                {
                    uint8_t* pPixCbCr = (i == 0) ? pPixCb : pPixCr;
                    pFilter->pLoopf->pfChromaDeblockingEQ4Hor2(pPixCbCr, iStride, iAlpha, iBeta);
                }
            }
        }
    }

    private static void DeblockingInterMb(SDqLayer* pCurDqLayer, SDeblockingFilter* pFilter, uint8_t* nBS, int32_t iBoundryFlag)
    {
        int32_t iMbXyIndex = pCurDqLayer->iMbXyIndex;
        int32_t iMbX = pCurDqLayer->iMbX;
        int32_t iMbY = pCurDqLayer->iMbY;

        int32_t iCurLumaQp = pCurDqLayer->pLumaQp[iMbXyIndex];
        //int32_t* iCurChromaQp = pCurDqLayer->pChromaQp[iMbXyIndex];
        int8_t* pCurChromaQp = (int8_t*)&pCurDqLayer->pChromaQp[iMbXyIndex];
        int32_t iLineSize = pFilter->iCsStride[0];
        int32_t iLineSizeUV = pFilter->iCsStride[1];

        uint8_t* pDestY, pDestCb, pDestCr;
        pDestY = pFilter->pCsData[0] + ((iMbY * iLineSize + iMbX) << 4);
        pDestCb = pFilter->pCsData[1] + ((iMbY * iLineSizeUV + iMbX) << 3);
        pDestCr = pFilter->pCsData[2] + ((iMbY * iLineSizeUV + iMbX) << 3);

        //Vertical margin
        if ((iBoundryFlag & LEFT_FLAG_MASK) != 0)
        {
            int32_t iLeftXyIndex = iMbXyIndex - 1;
            pFilter->iLumaQP = (int8_t)((iCurLumaQp + pCurDqLayer->pLumaQp[iLeftXyIndex] + 1) >> 1);
            for (int i = 0; i < 2; i++)
            {
                pFilter->iChromaQP[i] = (int8_t)((pCurChromaQp[i] + pCurDqLayer->pChromaQp[iLeftXyIndex][i] + 1) >> 1);
            }
            if (Bs(nBS, 0, 0)[0] == 0x04)
            {
                FilteringEdgeLumaIntraV(pFilter, pDestY, iLineSize, null);
                FilteringEdgeChromaIntraV(pFilter, pDestCb, pDestCr, iLineSizeUV, null);
            }
            else
            {
                if (*(uint32_t*)Bs(nBS, 0, 0) != 0)
                {
                    FilteringEdgeLumaV(pFilter, pDestY, iLineSize, Bs(nBS, 0, 0));
                    FilteringEdgeChromaV(pFilter, pDestCb, pDestCr, iLineSizeUV, Bs(nBS, 0, 0));
                }
            }
        }

        pFilter->iLumaQP = (int8_t)iCurLumaQp;
        pFilter->iChromaQP[0] = pCurChromaQp[0];
        pFilter->iChromaQP[1] = pCurChromaQp[1];

        if (*(uint32_t*)Bs(nBS, 0, 1) != 0 && !pCurDqLayer->pTransformSize8x8Flag[iMbXyIndex])
        {
            FilteringEdgeLumaV(pFilter, &pDestY[1 << 2], iLineSize, Bs(nBS, 0, 1));
        }

        if (*(uint32_t*)Bs(nBS, 0, 2) != 0)
        {
            FilteringEdgeLumaV(pFilter, &pDestY[2 << 2], iLineSize, Bs(nBS, 0, 2));
            FilteringEdgeChromaV(pFilter, &pDestCb[2 << 1], &pDestCr[2 << 1], iLineSizeUV, Bs(nBS, 0, 2));
        }

        if (*(uint32_t*)Bs(nBS, 0, 3) != 0 && !pCurDqLayer->pTransformSize8x8Flag[iMbXyIndex])
        {
            FilteringEdgeLumaV(pFilter, &pDestY[3 << 2], iLineSize, Bs(nBS, 0, 3));
        }

        if ((iBoundryFlag & TOP_FLAG_MASK) != 0)
        {
            int32_t iTopXyIndex = iMbXyIndex - pCurDqLayer->iMbWidth;
            pFilter->iLumaQP = (int8_t)((iCurLumaQp + pCurDqLayer->pLumaQp[iTopXyIndex] + 1) >> 1);
            for (int i = 0; i < 2; i++)
            {
                pFilter->iChromaQP[i] = (int8_t)((pCurChromaQp[i] + pCurDqLayer->pChromaQp[iTopXyIndex][i] + 1) >> 1);
            }

            if (Bs(nBS, 1, 0)[0] == 0x04)
            {
                FilteringEdgeLumaIntraH(pFilter, pDestY, iLineSize, null);
                FilteringEdgeChromaIntraH(pFilter, pDestCb, pDestCr, iLineSizeUV, null);
            }
            else
            {
                if (*(uint32_t*)Bs(nBS, 1, 0) != 0)
                {
                    FilteringEdgeLumaH(pFilter, pDestY, iLineSize, Bs(nBS, 1, 0));
                    FilteringEdgeChromaH(pFilter, pDestCb, pDestCr, iLineSizeUV, Bs(nBS, 1, 0));
                }
            }
        }

        pFilter->iLumaQP = (int8_t)iCurLumaQp;
        pFilter->iChromaQP[0] = pCurChromaQp[0];
        pFilter->iChromaQP[1] = pCurChromaQp[1];

        if (*(uint32_t*)Bs(nBS, 1, 1) != 0 && !pCurDqLayer->pTransformSize8x8Flag[iMbXyIndex])
        {
            FilteringEdgeLumaH(pFilter, &pDestY[(1 << 2) * iLineSize], iLineSize, Bs(nBS, 1, 1));
        }

        if (*(uint32_t*)Bs(nBS, 1, 2) != 0)
        {
            FilteringEdgeLumaH(pFilter, &pDestY[(2 << 2) * iLineSize], iLineSize, Bs(nBS, 1, 2));
            FilteringEdgeChromaH(pFilter, &pDestCb[(2 << 1) * iLineSizeUV], &pDestCr[(2 << 1) * iLineSizeUV], iLineSizeUV,
                Bs(nBS, 1, 2));
        }

        if (*(uint32_t*)Bs(nBS, 1, 3) != 0 && !pCurDqLayer->pTransformSize8x8Flag[iMbXyIndex])
        {
            FilteringEdgeLumaH(pFilter, &pDestY[(3 << 2) * iLineSize], iLineSize, Bs(nBS, 1, 3));
        }
    }

    public static void FilteringEdgeLumaHV(SDqLayer* pCurDqLayer, SDeblockingFilter* pFilter, int32_t iBoundryFlag)
    {
        int32_t iMbXyIndex = pCurDqLayer->iMbXyIndex;
        int32_t iMbX = pCurDqLayer->iMbX;
        int32_t iMbY = pCurDqLayer->iMbY;
        int32_t iMbWidth = pCurDqLayer->iMbWidth;
        int32_t iLineSize = pFilter->iCsStride[0];

        uint8_t* pDestY;
        int32_t iCurQp;

        int8_t* iTc = stackalloc int8_t[4];
        uint8_t* uiBSx4 = stackalloc uint8_t[4];

        pDestY = pFilter->pCsData[0] + ((iMbY * iLineSize + iMbX) << 4);
        iCurQp = pCurDqLayer->pLumaQp[iMbXyIndex];

        *(uint32_t*)uiBSx4 = 0x03030303;

        // luma v
        if ((iBoundryFlag & LEFT_FLAG_MASK) != 0)
        {
            pFilter->iLumaQP = (int8_t)((iCurQp + pCurDqLayer->pLumaQp[iMbXyIndex - 1] + 1) >> 1);
            FilteringEdgeLumaIntraV(pFilter, pDestY, iLineSize, null);
        }

        pFilter->iLumaQP = (int8_t)iCurQp;
        GET_ALPHA_BETA_FROM_QP(pFilter->iLumaQP, pFilter->iSliceAlphaC0Offset, pFilter->iSliceBetaOffset, out int32_t iIndexA,
            out int32_t iAlpha, out int32_t iBeta);
        if ((iAlpha | iBeta) != 0)
        {
            TC0_TBL_LOOKUP(iTc, iIndexA, uiBSx4, 0);

            if (!pCurDqLayer->pTransformSize8x8Flag[iMbXyIndex])
            {
                pFilter->pLoopf->pfLumaDeblockingLT4Hor(&pDestY[1 << 2], iLineSize, iAlpha, iBeta, iTc);
            }

            pFilter->pLoopf->pfLumaDeblockingLT4Hor(&pDestY[2 << 2], iLineSize, iAlpha, iBeta, iTc);

            if (!pCurDqLayer->pTransformSize8x8Flag[iMbXyIndex])
            {
                pFilter->pLoopf->pfLumaDeblockingLT4Hor(&pDestY[3 << 2], iLineSize, iAlpha, iBeta, iTc);
            }
        }

        // luma h
        if ((iBoundryFlag & TOP_FLAG_MASK) != 0)
        {
            pFilter->iLumaQP = (int8_t)((iCurQp + pCurDqLayer->pLumaQp[iMbXyIndex - iMbWidth] + 1) >> 1);
            FilteringEdgeLumaIntraH(pFilter, pDestY, iLineSize, null);
        }

        pFilter->iLumaQP = (int8_t)iCurQp;
        if ((iAlpha | iBeta) != 0)
        {
            if (!pCurDqLayer->pTransformSize8x8Flag[iMbXyIndex])
            {
                pFilter->pLoopf->pfLumaDeblockingLT4Ver(&pDestY[(1 << 2) * iLineSize], iLineSize, iAlpha, iBeta, iTc);
            }

            pFilter->pLoopf->pfLumaDeblockingLT4Ver(&pDestY[(2 << 2) * iLineSize], iLineSize, iAlpha, iBeta, iTc);

            if (!pCurDqLayer->pTransformSize8x8Flag[iMbXyIndex])
            {
                pFilter->pLoopf->pfLumaDeblockingLT4Ver(&pDestY[(3 << 2) * iLineSize], iLineSize, iAlpha, iBeta, iTc);
            }
        }
    }

    public static void FilteringEdgeChromaHV(SDqLayer* pCurDqLayer, SDeblockingFilter* pFilter, int32_t iBoundryFlag)
    {
        int32_t iMbXyIndex = pCurDqLayer->iMbXyIndex;
        int32_t iMbX = pCurDqLayer->iMbX;
        int32_t iMbY = pCurDqLayer->iMbY;
        int32_t iMbWidth = pCurDqLayer->iMbWidth;
        int32_t iLineSize = pFilter->iCsStride[1];

        uint8_t* pDestCb;
        uint8_t* pDestCr;
        //int32_t  iCurQp;
        int8_t* pCurQp;
        int32_t iIndexA, iAlpha, iBeta;

        int8_t* iTc = stackalloc int8_t[4];
        uint8_t* uiBSx4 = stackalloc uint8_t[4];

        pDestCb = pFilter->pCsData[1] + ((iMbY * iLineSize + iMbX) << 3);
        pDestCr = pFilter->pCsData[2] + ((iMbY * iLineSize + iMbX) << 3);
        pCurQp = (int8_t*)&pCurDqLayer->pChromaQp[iMbXyIndex];

        *(uint32_t*)uiBSx4 = 0x03030303;

        // chroma v
        if ((iBoundryFlag & LEFT_FLAG_MASK) != 0)
        {
            for (int i = 0; i < 2; i++)
            {
                pFilter->iChromaQP[i] = (int8_t)((pCurQp[i] + pCurDqLayer->pChromaQp[iMbXyIndex - 1][i] + 1) >> 1);
            }
            FilteringEdgeChromaIntraV(pFilter, pDestCb, pDestCr, iLineSize, null);
        }

        pFilter->iChromaQP[0] = pCurQp[0];
        pFilter->iChromaQP[1] = pCurQp[1];
        if (pFilter->iChromaQP[0] == pFilter->iChromaQP[1])
        {
            GET_ALPHA_BETA_FROM_QP(pFilter->iChromaQP[0], pFilter->iSliceAlphaC0Offset, pFilter->iSliceBetaOffset, out iIndexA,
                out iAlpha, out iBeta);
            if ((iAlpha | iBeta) != 0)
            {
                TC0_TBL_LOOKUP(iTc, iIndexA, uiBSx4, 1);
                pFilter->pLoopf->pfChromaDeblockingLT4Hor(&pDestCb[2 << 1], &pDestCr[2 << 1], iLineSize, iAlpha, iBeta, iTc);
            }
        }
        else
        {
            for (int i = 0; i < 2; i++)
            {
                GET_ALPHA_BETA_FROM_QP(pFilter->iChromaQP[i], pFilter->iSliceAlphaC0Offset, pFilter->iSliceBetaOffset, out iIndexA,
                    out iAlpha, out iBeta);
                if ((iAlpha | iBeta) != 0)
                {
                    uint8_t* pDestCbCr = (i == 0) ? &pDestCb[2 << 1] : &pDestCr[2 << 1];
                    TC0_TBL_LOOKUP(iTc, iIndexA, uiBSx4, 1);
                    pFilter->pLoopf->pfChromaDeblockingLT4Hor2(pDestCbCr, iLineSize, iAlpha, iBeta, iTc);
                }
            }
        }

        // chroma h

        if ((iBoundryFlag & TOP_FLAG_MASK) != 0)
        {
            for (int i = 0; i < 2; i++)
            {
                pFilter->iChromaQP[i] = (int8_t)((pCurQp[i] + pCurDqLayer->pChromaQp[iMbXyIndex - iMbWidth][i] + 1) >> 1);
            }
            FilteringEdgeChromaIntraH(pFilter, pDestCb, pDestCr, iLineSize, null);
        }

        pFilter->iChromaQP[0] = pCurQp[0];
        pFilter->iChromaQP[1] = pCurQp[1];

        if (pFilter->iChromaQP[0] == pFilter->iChromaQP[1])
        {
            GET_ALPHA_BETA_FROM_QP(pFilter->iChromaQP[0], pFilter->iSliceAlphaC0Offset, pFilter->iSliceBetaOffset, out iIndexA,
                out iAlpha, out iBeta);
            if ((iAlpha | iBeta) != 0)
            {
                TC0_TBL_LOOKUP(iTc, iIndexA, uiBSx4, 1);
                pFilter->pLoopf->pfChromaDeblockingLT4Ver(&pDestCb[(2 << 1) * iLineSize], &pDestCr[(2 << 1) * iLineSize], iLineSize,
                    iAlpha, iBeta, iTc);
            }
        }
        else
        {
            for (int i = 0; i < 2; i++)
            {
                GET_ALPHA_BETA_FROM_QP(pFilter->iChromaQP[i], pFilter->iSliceAlphaC0Offset, pFilter->iSliceBetaOffset, out iIndexA,
                    out iAlpha, out iBeta);
                if ((iAlpha | iBeta) != 0)
                {
                    TC0_TBL_LOOKUP(iTc, iIndexA, uiBSx4, 1);
                    uint8_t* pDestCbCr = (i == 0) ? &pDestCb[(2 << 1) * iLineSize] : &pDestCr[(2 << 1) * iLineSize];
                    pFilter->pLoopf->pfChromaDeblockingLT4Ver2(pDestCbCr, iLineSize,
                        iAlpha, iBeta, iTc);
                }
            }
        }
    }

    // merge h&v lookup table operation to save performance
    private static void DeblockingIntraMb(SDqLayer* pCurDqLayer, SDeblockingFilter* pFilter, int32_t iBoundryFlag)
    {
        FilteringEdgeLumaHV(pCurDqLayer, pFilter, iBoundryFlag);
        FilteringEdgeChromaHV(pCurDqLayer, pFilter, iBoundryFlag);
    }

    public static void WelsDeblockingMb(SDqLayer* pCurDqLayer, SDeblockingFilter* pFilter, int32_t iBoundryFlag)
    {
        uint8_t* nBS = stackalloc uint8_t[2 * 4 * 4];
        memset(nBS, 0, 2 * 4 * 4); // = {{{ 0 }}}

        int32_t iMbXyIndex = pCurDqLayer->iMbXyIndex;
        uint32_t iCurMbType = pCurDqLayer->pDec != null ? pCurDqLayer->pDec->pMbType[iMbXyIndex] :
                              pCurDqLayer->pMbType[iMbXyIndex];
        int32_t iMbNb;

        SSlice* pSlice = &pCurDqLayer->sLayerInfo.sSliceInLayer;
        SSliceHeader* pSliceHeader = &pSlice->sSliceHeaderExt.sSliceHeader;
        bool bBSlice = pSliceHeader->eSliceType == B_SLICE;

        switch (iCurMbType)
        {
            case MB_TYPE_INTRA4x4:
            case MB_TYPE_INTRA8x8:
            case MB_TYPE_INTRA16x16:
            case MB_TYPE_INTRA_PCM:
                DeblockingIntraMb(pCurDqLayer, pFilter, iBoundryFlag);
                break;
            default:

                if ((iBoundryFlag & LEFT_FLAG_MASK) != 0)
                {
                    iMbNb = iMbXyIndex - 1;
                    uint32_t uiMbType = pCurDqLayer->pDec != null ? pCurDqLayer->pDec->pMbType[iMbNb] : pCurDqLayer->pMbType[iMbNb];
                    if (bBSlice)
                    {
                        *(uint32_t*)Bs(nBS, 0, 0) = IS_INTRA(uiMbType) ? 0x04040404u :
                                                    DeblockingBSliceBsMarginalMBAvcbase(
                                                        pFilter, pCurDqLayer, 0, iMbNb, iMbXyIndex);
                    }
                    else
                    {
                        *(uint32_t*)Bs(nBS, 0, 0) = IS_INTRA(uiMbType) ? 0x04040404u : DeblockingBsMarginalMBAvcbase(
                                                        pFilter, pCurDqLayer, 0, iMbNb, iMbXyIndex);
                    }
                }
                else
                {
                    *(uint32_t*)Bs(nBS, 0, 0) = 0;
                }
                if ((iBoundryFlag & TOP_FLAG_MASK) != 0)
                {
                    iMbNb = iMbXyIndex - pCurDqLayer->iMbWidth;
                    uint32_t uiMbType = pCurDqLayer->pDec != null ? pCurDqLayer->pDec->pMbType[iMbNb] : pCurDqLayer->pMbType[iMbNb];
                    if (bBSlice)
                    {
                        *(uint32_t*)Bs(nBS, 1, 0) = IS_INTRA(uiMbType) ? 0x04040404u :
                                                    DeblockingBSliceBsMarginalMBAvcbase(
                                                        pFilter, pCurDqLayer, 1, iMbNb, iMbXyIndex);
                    }
                    else
                    {
                        *(uint32_t*)Bs(nBS, 1, 0) = IS_INTRA(uiMbType) ? 0x04040404u : DeblockingBsMarginalMBAvcbase(
                                                        pFilter, pCurDqLayer, 1, iMbNb, iMbXyIndex);
                    }
                }
                else
                {
                    *(uint32_t*)Bs(nBS, 1, 0) = 0;
                }
                //SKIP MB_16x16 or others
                if (IS_SKIP(iCurMbType))
                {
                    *(uint32_t*)Bs(nBS, 0, 1) = *(uint32_t*)Bs(nBS, 0, 2) = *(uint32_t*)Bs(nBS, 0, 3) =
                        *(uint32_t*)Bs(nBS, 1, 1) = *(uint32_t*)Bs(nBS, 1, 2) = *(uint32_t*)Bs(nBS, 1, 3) = 0;
                }
                else
                {
                    if (IS_INTER_16x16(iCurMbType))
                    {
                        if (!pCurDqLayer->pTransformSize8x8Flag[pCurDqLayer->iMbXyIndex])
                        {
                            DeblockingBSInsideMBAvsbase(GetPNzc(pCurDqLayer, iMbXyIndex), nBS, 1);
                        }
                        else
                        {
                            DeblockingBSInsideMBAvsbase8x8(GetPNzc(pCurDqLayer, iMbXyIndex), nBS, 1);
                        }
                    }
                    else
                    {
                        if (bBSlice)
                        {
                            DeblockingBSliceBSInsideMBNormal(pFilter, pCurDqLayer, nBS, GetPNzc(pCurDqLayer, iMbXyIndex), iMbXyIndex);
                        }
                        else
                        {
                            DeblockingBSInsideMBNormal(pFilter, pCurDqLayer, nBS, GetPNzc(pCurDqLayer, iMbXyIndex), iMbXyIndex);
                        }
                    }
                }
                DeblockingInterMb(pCurDqLayer, pFilter, nBS, iBoundryFlag);
                break;
        }
    }

    /*!
     * \brief   AVC slice deblocking filtering target layer
     *
     * \param   dec         Wels avc decoder context
     *
     * \return  NONE
     */
    public static void WelsDeblockingFilterSlice(SWelsDecoderContext* pCtx, PDeblockingFilterMbFunc pDeblockMb)
    {
        SDqLayer* pCurDqLayer = pCtx->pCurDqLayer;
        SSliceHeaderExt* pSliceHeaderExt = &pCurDqLayer->sLayerInfo.sSliceInLayer.sSliceHeaderExt;
        int32_t iMbWidth = pCurDqLayer->iMbWidth;
        int32_t iTotalMbCount = (int32_t)pSliceHeaderExt->sSliceHeader.pSps->uiTotalMbCount;

        SDeblockingFilter pFilter = default; // memset (&pFilter, 0, sizeof (pFilter))
        SFmo* pFmo = pCtx->pFmo;
        int32_t iNextMbXyIndex = 0;
        int32_t iTotalNumMb = pCurDqLayer->sLayerInfo.sSliceInLayer.iTotalMbInCurSlice;
        int32_t iCountNumMb = 0;
        int32_t iBoundryFlag;
        int32_t iFilterIdc = (int32_t)pCurDqLayer->sLayerInfo.sSliceInLayer.sSliceHeaderExt.sSliceHeader.uiDisableDeblockingFilterIdc;

        /* Step1: parameters set */
        pFilter.pCsData[0] = pCtx->pDec->pData[0];
        pFilter.pCsData[1] = pCtx->pDec->pData[1];
        pFilter.pCsData[2] = pCtx->pDec->pData[2];

        pFilter.iCsStride[0] = pCtx->pDec->iLinesize[0];
        pFilter.iCsStride[1] = pCtx->pDec->iLinesize[1];

        pFilter.eSliceType = (EWelsSliceType)pCurDqLayer->sLayerInfo.sSliceInLayer.eSliceType;

        pFilter.iSliceAlphaC0Offset = (int8_t)pSliceHeaderExt->sSliceHeader.iSliceAlphaC0Offset;
        pFilter.iSliceBetaOffset = (int8_t)pSliceHeaderExt->sSliceHeader.iSliceBetaOffset;

        pFilter.pLoopf = &pCtx->sDeblockingFunc;
        pFilter.pRefPics[0] = (Ptr<SPicture>*)&pCtx->sRefPic.pRefList[0];
        pFilter.pRefPics[1] = (Ptr<SPicture>*)&pCtx->sRefPic.pRefList[1];

        /* Step2: macroblock deblocking */
        if (0 == iFilterIdc || 2 == iFilterIdc)
        {
            iNextMbXyIndex = pSliceHeaderExt->sSliceHeader.iFirstMbInSlice;
            pCurDqLayer->iMbX = iNextMbXyIndex % iMbWidth;
            pCurDqLayer->iMbY = iNextMbXyIndex / iMbWidth;
            pCurDqLayer->iMbXyIndex = iNextMbXyIndex;

            do
            {
                iBoundryFlag = DeblockingAvailableNoInterlayer(pCurDqLayer, iFilterIdc);

                pDeblockMb(pCurDqLayer, &pFilter, iBoundryFlag);

                ++iCountNumMb;
                if (iCountNumMb >= iTotalNumMb)
                {
                    break;
                }

                if (pSliceHeaderExt->sSliceHeader.pPps->uiNumSliceGroups > 1)
                {
                    iNextMbXyIndex = FmoNextMb(pFmo, iNextMbXyIndex);
                }
                else
                {
                    ++iNextMbXyIndex;
                }
                if (-1 == iNextMbXyIndex || iNextMbXyIndex >= iTotalMbCount)
                { // slice group boundary or end of a frame
                    break;
                }

                pCurDqLayer->iMbX = iNextMbXyIndex % iMbWidth;
                pCurDqLayer->iMbY = iNextMbXyIndex / iMbWidth;
                pCurDqLayer->iMbXyIndex = iNextMbXyIndex;
            } while (true);
        }
    }

    /*!
    * \brief   AVC slice init deblocking filtering target layer
    *
    * \in and out param   SDeblockingFilter
    * \in and out param   iFilterIdc
    *
    * \return  NONE
    */
    public static void WelsDeblockingInitFilter(SWelsDecoderContext* pCtx, ref SDeblockingFilter pFilter, ref int32_t iFilterIdc)
    {
        SDqLayer* pCurDqLayer = pCtx->pCurDqLayer;
        SSliceHeaderExt* pSliceHeaderExt = &pCurDqLayer->sLayerInfo.sSliceInLayer.sSliceHeaderExt;

        pFilter = default; // memset (&pFilter, 0, sizeof (pFilter))

        iFilterIdc = (int32_t)pCurDqLayer->sLayerInfo.sSliceInLayer.sSliceHeaderExt.sSliceHeader.uiDisableDeblockingFilterIdc;

        /* Step1: parameters set */
        pFilter.pCsData[0] = pCtx->pDec->pData[0];
        pFilter.pCsData[1] = pCtx->pDec->pData[1];
        pFilter.pCsData[2] = pCtx->pDec->pData[2];

        pFilter.iCsStride[0] = pCtx->pDec->iLinesize[0];
        pFilter.iCsStride[1] = pCtx->pDec->iLinesize[1];

        pFilter.eSliceType = (EWelsSliceType)pCurDqLayer->sLayerInfo.sSliceInLayer.eSliceType;

        pFilter.iSliceAlphaC0Offset = (int8_t)pSliceHeaderExt->sSliceHeader.iSliceAlphaC0Offset;
        pFilter.iSliceBetaOffset = (int8_t)pSliceHeaderExt->sSliceHeader.iSliceBetaOffset;

        pFilter.pLoopf = &pCtx->sDeblockingFunc;
        pFilter.pRefPics[0] = (Ptr<SPicture>*)&pCtx->sRefPic.pRefList[0];
        pFilter.pRefPics[1] = (Ptr<SPicture>*)&pCtx->sRefPic.pRefList[1];
    }

    /*!
    * \brief   AVC MB deblocking filtering target layer
    *
    * \param   DqLayer which has the current location of MB to be deblocked.
    *
    * \return  NONE
    */
    public static void WelsDeblockingFilterMB(SDqLayer* pCurDqLayer, ref SDeblockingFilter pFilter, ref int32_t iFilterIdc,
        PDeblockingFilterMbFunc pDeblockMb)
    {
        /* macroblock deblocking */
        if (0 == iFilterIdc || 2 == iFilterIdc)
        {
            int32_t iBoundryFlag = DeblockingAvailableNoInterlayer(pCurDqLayer, iFilterIdc);
            fixed (SDeblockingFilter* pF = &pFilter)
            {
                pDeblockMb(pCurDqLayer, pF, iBoundryFlag);
            }
        }
    }

    /*!
     * \brief   deblocking module initialize
     *
     * \param   pf
     *          cpu
     *
     * \return  NONE
     */
    public static void DeblockingInit(SDeblockingFunc* pFunc, int32_t iCpu)
    {
        pFunc->pfLumaDeblockingLT4Ver = &DeblockLumaLt4V_c;
        pFunc->pfLumaDeblockingEQ4Ver = &DeblockLumaEq4V_c;
        pFunc->pfLumaDeblockingLT4Hor = &DeblockLumaLt4H_c;
        pFunc->pfLumaDeblockingEQ4Hor = &DeblockLumaEq4H_c;

        pFunc->pfChromaDeblockingLT4Ver = &DeblockChromaLt4V_c;
        pFunc->pfChromaDeblockingEQ4Ver = &DeblockChromaEq4V_c;
        pFunc->pfChromaDeblockingLT4Hor = &DeblockChromaLt4H_c;
        pFunc->pfChromaDeblockingEQ4Hor = &DeblockChromaEq4H_c;

        pFunc->pfChromaDeblockingLT4Ver2 = &DeblockChromaLt4V2_c;
        pFunc->pfChromaDeblockingEQ4Ver2 = &DeblockChromaEq4V2_c;
        pFunc->pfChromaDeblockingLT4Hor2 = &DeblockChromaLt4H2_c;
        pFunc->pfChromaDeblockingEQ4Hor2 = &DeblockChromaEq4H2_c;

        // The X86_ASM, NEON, AArch64, MMI, MSA and LSX kernels are not ported.
    }
}
