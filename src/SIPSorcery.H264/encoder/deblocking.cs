//-----------------------------------------------------------------------------
// Filename: deblocking.cs
//
// Description: Port of codec/encoder/core/inc/deblocking.h and
// codec/encoder/core/src/deblocking.cpp (E4b): the encoder's loop filter over
// its reconstruction, frame by frame (DeblockingFilterFrameAvcbase) or slice
// by slice (DeblockingFilterSliceAvcbase), with the boundary strengths worked
// out from the macroblock types, non-zero counts and motion vectors. The edge
// filters themselves are common's (deblocking_common.cs).
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// The encoder builds with SINGLE_REF_FRAME (as264_common.h), so the
// reference index test in DeblockingBSMarginalMBAvcbase is compiled out, and
// only the _c paths of WelsBlockFuncInit and DeblockingBSCalc are ported
// (DeblockingInit chooses the SIMD edge filters of
// common/deblocking_common_simd.cs where upstream has its own). Unlike
// the decoder's, the encoder's alpha, beta and tc0 tables have their 12 pad
// entries at the end and the index is clipped to [0, 51].
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 26 Nov 2026  Claude          Created (E4b).
// 27 Nov 2026  Claude          DeblockingInit chooses the SIMD edge filters (P5.4).
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
 * \file    deblocking.h
 *
 * \brief   Interfaces introduced in frame deblocking filtering
 *
 * \date    08/02/2010
 *
 *************************************************************************************
 */

using System.Diagnostics;

namespace SIPSorcery.H264.Encoder;

//struct tagDeblockingFunc;

/// <summary>TagDeblockingFilter.</summary>
internal unsafe struct SDeblockingFilter
{
    public PtrArray3<uint8_t> pCsData;          // pointer to reconstructed picture pData
    public fixed int iCsStride[3];              // Cs iStride
    public int16_t iMbStride;
    public int8_t iSliceAlphaC0Offset;
    public int8_t iSliceBetaOffset;
    public uint8_t uiLumaQP;
    public uint8_t uiChromaQP;
    public uint8_t uiFilterIdc;
    public uint8_t uiReserved;
}

internal static unsafe partial class WelsEnc
{
    // #define g_kuiAlphaTable(x) g_kuiAlphaTable[(x)], and likewise g_kiBetaTable and g_kiTc0Table.

    // MB_BS_MV.
    private static bool MB_BS_MV(SMVUnitXY* sCurMv, SMVUnitXY* sNeighMv, int32_t uiBIdx, int32_t uiBnIdx) =>
        (WELS_ABS(sCurMv[uiBIdx].iMvX - sNeighMv[uiBnIdx].iMvX) >= 4) ||
        (WELS_ABS(sCurMv[uiBIdx].iMvY - sNeighMv[uiBnIdx].iMvY) >= 4);

    // SMB_EDGE_MV (uiRefIndex is unused).
    private static int32_t SMB_EDGE_MV(SMVUnitXY* sMotionVector, int32_t uiBIdx, int32_t uiBnIdx) =>
        ((WELS_ABS(sMotionVector[uiBIdx].iMvX - sMotionVector[uiBnIdx].iMvX) & (~3))
         | (WELS_ABS(sMotionVector[uiBIdx].iMvY - sMotionVector[uiBnIdx].iMvY) & (~3))) != 0 ? 1 : 0;

    // BS_EDGE (uiRefIndex is unused).
    private static uint8_t BS_EDGE(int32_t bsx1, SMVUnitXY* sMotionVector, int32_t uiBIdx, int32_t uiBnIdx) =>
        (uint8_t)((bsx1 | SMB_EDGE_MV(sMotionVector, uiBIdx, uiBnIdx)) << (bsx1 != 0 ? 1 : 0));

    // GET_ALPHA_BETA_FROM_QP.
    private static void GET_ALPHA_BETA_FROM_QP(int32_t QP, int32_t iAlphaOffset, int32_t iBetaOffset, out int32_t iIdexA,
        out int32_t iAlpha, out int32_t iBeta)
    {
        iIdexA = (QP + iAlphaOffset);
        iIdexA = CLIP3_QP_0_51(iIdexA);
        iAlpha = g_kuiAlphaTableEnc[iIdexA];
        iBeta = g_kiBetaTableEnc[(CLIP3_QP_0_51(QP + iBetaOffset))];
    }

    // static const uint8_t g_kuiAlphaTable[52 + 12] (Table 8-16 in H.264/AVC).
    private static readonly uint8_t* g_kuiAlphaTableEnc = StaticTable.Of<uint8_t>(52 + 12, [ //this table refers to Table 8-16 in H.264/AVC standard
        0,  0,  0,  0,  0,  0,  0,  0,  0,  0,
        0,  0,  0,  0,  0,  0,  4,  4,  5,  6,
        7,  8,  9, 10, 12, 13, 15, 17, 20, 22,
        25, 28, 32, 36, 40, 45, 50, 56, 63, 71,
        80, 90, 101, 113, 127, 144, 162, 182, 203, 226,
        255, 255
        , 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255
    ]);

    // static const int8_t g_kiBetaTable[52 + 12] (Table 8-16 in H.264/AVC).
    private static readonly int8_t* g_kiBetaTableEnc = StaticTable.Of<int8_t>(52 + 12, [ //this table refers to Table 8-16 in H.264/AVC standard
        0,  0,  0,  0,  0,  0,  0,  0,  0,  0,
        0,  0,  0,  0,  0,  0,  2,  2,  2,  3,
        3,  3,  3,  4,  4,  4,  6,  6,  7,  7,
        8,  8,  9,  9, 10, 10, 11, 11, 12, 12,
        13, 13, 14, 14, 15, 15, 16, 16, 17, 17,
        18, 18
        , 18, 18, 18, 18, 18, 18, 18, 18, 18, 18, 18, 18
    ]);

    // static const int8_t g_kiTc0Table[52 + 12][4] (Table 8-17 in H.264/AVC).
    private static readonly InlineArray4<int8_t>* g_kiTc0TableEnc = StaticTable.Of<InlineArray4<int8_t>, int8_t>(52 + 12, [ //this table refers Table 8-17 in H.264/AVC standard
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
    private static readonly InlineArray8<uint8_t>* g_kuiTableBIdxEnc = StaticTable.Of<InlineArray8<uint8_t>, uint8_t>(2, [
        0,  4,  8,  12, // g_kuiTableBIdx
        3,  7,  11, 15
        , // table_bn_idx

        0,  1,  2,  3 , // g_kuiTableBIdx
        12, 13, 14, 15
        , // table_bn_idx
    ]);

    // &uiBS[iDir][iEdge][0] of the C uint8_t uiBS[2][4][4].
    private static uint8_t* Bs(uint8_t* uiBS, int32_t iDir, int32_t iEdge) => uiBS + (iDir << 4) + (iEdge << 2);

    // TC0_TBL_LOOKUP.
    private static void TC0_TBL_LOOKUP(int8_t* iTc, int32_t iIdexA, uint8_t* pBS, int32_t bchroma)
    {
        InlineArray4<int8_t>* pTc0 = &g_kiTc0TableEnc[iIdexA];
        iTc[0] = (int8_t)((*pTc0)[pBS[0]] + bchroma);
        iTc[1] = (int8_t)((*pTc0)[pBS[1]] + bchroma);
        iTc[2] = (int8_t)((*pTc0)[pBS[2]] + bchroma);
        iTc[3] = (int8_t)((*pTc0)[pBS[3]] + bchroma);
    }

    private static void DeblockingBSInsideMBAvsbase(int8_t* pNnzTab, uint8_t* uiBS, int32_t iLShiftFactor)
    {
        uint32_t uiNnz32b0, uiNnz32b1, uiNnz32b2, uiNnz32b3;

        uiNnz32b0 = *(uint32_t*)(pNnzTab + 0);
        uiNnz32b1 = *(uint32_t*)(pNnzTab + 4);
        uiNnz32b2 = *(uint32_t*)(pNnzTab + 8);
        uiNnz32b3 = *(uint32_t*)(pNnzTab + 12);

        Bs(uiBS, 0, 1)[0] = (uint8_t)((pNnzTab[0] | pNnzTab[1]) << iLShiftFactor);
        Bs(uiBS, 0, 2)[0] = (uint8_t)((pNnzTab[1] | pNnzTab[2]) << iLShiftFactor);
        Bs(uiBS, 0, 3)[0] = (uint8_t)((pNnzTab[2] | pNnzTab[3]) << iLShiftFactor);

        Bs(uiBS, 0, 1)[1] = (uint8_t)((pNnzTab[4] | pNnzTab[5]) << iLShiftFactor);
        Bs(uiBS, 0, 2)[1] = (uint8_t)((pNnzTab[5] | pNnzTab[6]) << iLShiftFactor);
        Bs(uiBS, 0, 3)[1] = (uint8_t)((pNnzTab[6] | pNnzTab[7]) << iLShiftFactor);
        *(uint32_t*)Bs(uiBS, 1, 1) = (uiNnz32b0 | uiNnz32b1) << iLShiftFactor;

        Bs(uiBS, 0, 1)[2] = (uint8_t)((pNnzTab[8] | pNnzTab[9]) << iLShiftFactor);
        Bs(uiBS, 0, 2)[2] = (uint8_t)((pNnzTab[9] | pNnzTab[10]) << iLShiftFactor);
        Bs(uiBS, 0, 3)[2] = (uint8_t)((pNnzTab[10] | pNnzTab[11]) << iLShiftFactor);
        *(uint32_t*)Bs(uiBS, 1, 2) = (uiNnz32b1 | uiNnz32b2) << iLShiftFactor;

        Bs(uiBS, 0, 1)[3] = (uint8_t)((pNnzTab[12] | pNnzTab[13]) << iLShiftFactor);
        Bs(uiBS, 0, 2)[3] = (uint8_t)((pNnzTab[13] | pNnzTab[14]) << iLShiftFactor);
        Bs(uiBS, 0, 3)[3] = (uint8_t)((pNnzTab[14] | pNnzTab[15]) << iLShiftFactor);
        *(uint32_t*)Bs(uiBS, 1, 3) = (uiNnz32b2 | uiNnz32b3) << iLShiftFactor;
    }

    private static void DeblockingBSInsideMBNormal(SMB* pCurMb, uint8_t* uiBS, int8_t* pNnzTab)
    {
        uint32_t uiNnz32b0, uiNnz32b1, uiNnz32b2, uiNnz32b3;
        uint32_t uiBsx4Storage;
        uint8_t* uiBsx4 = (uint8_t*)&uiBsx4Storage;

        uiNnz32b0 = *(uint32_t*)(pNnzTab + 0);
        uiNnz32b1 = *(uint32_t*)(pNnzTab + 4);
        uiNnz32b2 = *(uint32_t*)(pNnzTab + 8);
        uiNnz32b3 = *(uint32_t*)(pNnzTab + 12);

        for (int i = 0; i < 3; i++)
        {
            uiBsx4[i] = (uint8_t)(pNnzTab[i] | pNnzTab[i + 1]);
        }
        Bs(uiBS, 0, 1)[0] = BS_EDGE(uiBsx4[0], pCurMb->sMv, 1, 0);
        Bs(uiBS, 0, 2)[0] = BS_EDGE(uiBsx4[1], pCurMb->sMv, 2, 1);
        Bs(uiBS, 0, 3)[0] = BS_EDGE(uiBsx4[2], pCurMb->sMv, 3, 2);

        for (int i = 0; i < 3; i++)
        {
            uiBsx4[i] = (uint8_t)(pNnzTab[4 + i] | pNnzTab[4 + i + 1]);
        }
        Bs(uiBS, 0, 1)[1] = BS_EDGE(uiBsx4[0], pCurMb->sMv, 5, 4);
        Bs(uiBS, 0, 2)[1] = BS_EDGE(uiBsx4[1], pCurMb->sMv, 6, 5);
        Bs(uiBS, 0, 3)[1] = BS_EDGE(uiBsx4[2], pCurMb->sMv, 7, 6);

        for (int i = 0; i < 3; i++)
        {
            uiBsx4[i] = (uint8_t)(pNnzTab[8 + i] | pNnzTab[8 + i + 1]);
        }
        Bs(uiBS, 0, 1)[2] = BS_EDGE(uiBsx4[0], pCurMb->sMv, 9, 8);
        Bs(uiBS, 0, 2)[2] = BS_EDGE(uiBsx4[1], pCurMb->sMv, 10, 9);
        Bs(uiBS, 0, 3)[2] = BS_EDGE(uiBsx4[2], pCurMb->sMv, 11, 10);

        for (int i = 0; i < 3; i++)
        {
            uiBsx4[i] = (uint8_t)(pNnzTab[12 + i] | pNnzTab[12 + i + 1]);
        }
        Bs(uiBS, 0, 1)[3] = BS_EDGE(uiBsx4[0], pCurMb->sMv, 13, 12);
        Bs(uiBS, 0, 2)[3] = BS_EDGE(uiBsx4[1], pCurMb->sMv, 14, 13);
        Bs(uiBS, 0, 3)[3] = BS_EDGE(uiBsx4[2], pCurMb->sMv, 15, 14);

        //horizontal
        *(uint32_t*)uiBsx4 = (uiNnz32b0 | uiNnz32b1);
        Bs(uiBS, 1, 1)[0] = BS_EDGE(uiBsx4[0], pCurMb->sMv, 4, 0);
        Bs(uiBS, 1, 1)[1] = BS_EDGE(uiBsx4[1], pCurMb->sMv, 5, 1);
        Bs(uiBS, 1, 1)[2] = BS_EDGE(uiBsx4[2], pCurMb->sMv, 6, 2);
        Bs(uiBS, 1, 1)[3] = BS_EDGE(uiBsx4[3], pCurMb->sMv, 7, 3);

        *(uint32_t*)uiBsx4 = (uiNnz32b1 | uiNnz32b2);
        Bs(uiBS, 1, 2)[0] = BS_EDGE(uiBsx4[0], pCurMb->sMv, 8, 4);
        Bs(uiBS, 1, 2)[1] = BS_EDGE(uiBsx4[1], pCurMb->sMv, 9, 5);
        Bs(uiBS, 1, 2)[2] = BS_EDGE(uiBsx4[2], pCurMb->sMv, 10, 6);
        Bs(uiBS, 1, 2)[3] = BS_EDGE(uiBsx4[3], pCurMb->sMv, 11, 7);

        *(uint32_t*)uiBsx4 = (uiNnz32b2 | uiNnz32b3);
        Bs(uiBS, 1, 3)[0] = BS_EDGE(uiBsx4[0], pCurMb->sMv, 12, 8);
        Bs(uiBS, 1, 3)[1] = BS_EDGE(uiBsx4[1], pCurMb->sMv, 13, 9);
        Bs(uiBS, 1, 3)[2] = BS_EDGE(uiBsx4[2], pCurMb->sMv, 14, 10);
        Bs(uiBS, 1, 3)[3] = BS_EDGE(uiBsx4[3], pCurMb->sMv, 15, 11);
    }

    public static uint32_t DeblockingBSMarginalMBAvcbase(SMB* pCurMb, SMB* pNeighMb, int32_t iEdge)
    {
        int32_t i;
        uint32_t uiBSx4 = 0; // every pass writes one of the four bytes; C leaves it uninitialised
        uint8_t* pBS = (uint8_t*)(&uiBSx4);
        uint8_t* pBIdx = (uint8_t*)&g_kuiTableBIdxEnc[iEdge];
        uint8_t* pBnIdx = (uint8_t*)&g_kuiTableBIdxEnc[iEdge] + 4;

        for (i = 0; i < 4; i++)
        {
            if ((pCurMb->pNonZeroCount[*pBIdx] | pNeighMb->pNonZeroCount[*pBnIdx]) != 0)
            {
                pBS[i] = 2;
            }
            else
            {
                // #ifndef SINGLE_REF_FRAME the reference index test #endif
                pBS[i] = (uint8_t)(MB_BS_MV(pCurMb->sMv, pNeighMb->sMv, *pBIdx, *pBnIdx) ? 1 : 0);
            }
            pBIdx++;
            pBnIdx++;
        }
        return uiBSx4;
    }

    public static void FilteringEdgeLumaH(DeblockingFunc* pfDeblocking, SDeblockingFilter* pFilter, uint8_t* pPix, int32_t iStride,
        uint8_t* pBS)
    {
        int8_t* iTc = stackalloc int8_t[4];

        GET_ALPHA_BETA_FROM_QP(pFilter->uiLumaQP, pFilter->iSliceAlphaC0Offset, pFilter->iSliceBetaOffset, out int32_t iIdexA,
            out int32_t iAlpha, out int32_t iBeta);

        if ((iAlpha | iBeta) != 0)
        {
            TC0_TBL_LOOKUP(iTc, iIdexA, pBS, 0);
            pfDeblocking->pfLumaDeblockingLT4Ver(pPix, iStride, iAlpha, iBeta, iTc);
        }
        return;
    }

    public static void FilteringEdgeLumaV(DeblockingFunc* pfDeblocking, SDeblockingFilter* pFilter, uint8_t* pPix, int32_t iStride,
        uint8_t* pBS)
    {
        int8_t* iTc = stackalloc int8_t[4];

        GET_ALPHA_BETA_FROM_QP(pFilter->uiLumaQP, pFilter->iSliceAlphaC0Offset, pFilter->iSliceBetaOffset, out int32_t iIdexA,
            out int32_t iAlpha, out int32_t iBeta);

        if ((iAlpha | iBeta) != 0)
        {
            TC0_TBL_LOOKUP(iTc, iIdexA, pBS, 0);
            pfDeblocking->pfLumaDeblockingLT4Hor(pPix, iStride, iAlpha, iBeta, iTc);
        }
        return;
    }

    public static void FilteringEdgeLumaIntraH(DeblockingFunc* pfDeblocking, SDeblockingFilter* pFilter, uint8_t* pPix,
        int32_t iStride, uint8_t* pBS)
    {
        GET_ALPHA_BETA_FROM_QP(pFilter->uiLumaQP, pFilter->iSliceAlphaC0Offset, pFilter->iSliceBetaOffset, out _,
            out int32_t iAlpha, out int32_t iBeta);

        if ((iAlpha | iBeta) != 0)
        {
            pfDeblocking->pfLumaDeblockingEQ4Ver(pPix, iStride, iAlpha, iBeta);
        }
        return;
    }

    public static void FilteringEdgeLumaIntraV(DeblockingFunc* pfDeblocking, SDeblockingFilter* pFilter, uint8_t* pPix,
        int32_t iStride, uint8_t* pBS)
    {
        GET_ALPHA_BETA_FROM_QP(pFilter->uiLumaQP, pFilter->iSliceAlphaC0Offset, pFilter->iSliceBetaOffset, out _,
            out int32_t iAlpha, out int32_t iBeta);

        if ((iAlpha | iBeta) != 0)
        {
            pfDeblocking->pfLumaDeblockingEQ4Hor(pPix, iStride, iAlpha, iBeta);
        }
        return;
    }

    public static void FilteringEdgeChromaH(DeblockingFunc* pfDeblocking, SDeblockingFilter* pFilter, uint8_t* pPixCb,
        uint8_t* pPixCr, int32_t iStride, uint8_t* pBS)
    {
        int8_t* iTc = stackalloc int8_t[4];

        GET_ALPHA_BETA_FROM_QP(pFilter->uiChromaQP, pFilter->iSliceAlphaC0Offset, pFilter->iSliceBetaOffset, out int32_t iIdexA,
            out int32_t iAlpha, out int32_t iBeta);

        if ((iAlpha | iBeta) != 0)
        {
            TC0_TBL_LOOKUP(iTc, iIdexA, pBS, 1);
            pfDeblocking->pfChromaDeblockingLT4Ver(pPixCb, pPixCr, iStride, iAlpha, iBeta, iTc);
        }
        return;
    }

    public static void FilteringEdgeChromaV(DeblockingFunc* pfDeblocking, SDeblockingFilter* pFilter, uint8_t* pPixCb,
        uint8_t* pPixCr, int32_t iStride, uint8_t* pBS)
    {
        int8_t* iTc = stackalloc int8_t[4];

        GET_ALPHA_BETA_FROM_QP(pFilter->uiChromaQP, pFilter->iSliceAlphaC0Offset, pFilter->iSliceBetaOffset, out int32_t iIdexA,
            out int32_t iAlpha, out int32_t iBeta);

        if ((iAlpha | iBeta) != 0)
        {
            TC0_TBL_LOOKUP(iTc, iIdexA, pBS, 1);
            pfDeblocking->pfChromaDeblockingLT4Hor(pPixCb, pPixCr, iStride, iAlpha, iBeta, iTc);
        }
        return;
    }

    public static void FilteringEdgeChromaIntraH(DeblockingFunc* pfDeblocking, SDeblockingFilter* pFilter, uint8_t* pPixCb,
        uint8_t* pPixCr, int32_t iStride, uint8_t* pBS)
    {
        GET_ALPHA_BETA_FROM_QP(pFilter->uiChromaQP, pFilter->iSliceAlphaC0Offset, pFilter->iSliceBetaOffset, out _,
            out int32_t iAlpha, out int32_t iBeta);

        if ((iAlpha | iBeta) != 0)
        {
            pfDeblocking->pfChromaDeblockingEQ4Ver(pPixCb, pPixCr, iStride, iAlpha, iBeta);
        }
        return;
    }

    public static void FilteringEdgeChromaIntraV(DeblockingFunc* pfDeblocking, SDeblockingFilter* pFilter, uint8_t* pPixCb,
        uint8_t* pPixCr, int32_t iStride, uint8_t* pBS)
    {
        GET_ALPHA_BETA_FROM_QP(pFilter->uiChromaQP, pFilter->iSliceAlphaC0Offset, pFilter->iSliceBetaOffset, out _,
            out int32_t iAlpha, out int32_t iBeta);

        if ((iAlpha | iBeta) != 0)
        {
            pfDeblocking->pfChromaDeblockingEQ4Hor(pPixCb, pPixCr, iStride, iAlpha, iBeta);
        }
        return;
    }

    public static void DeblockingInterMb(DeblockingFunc* pfDeblocking, SMB* pCurMb, SDeblockingFilter* pFilter, uint8_t* uiBS)
    {
        int8_t iCurLumaQp = (int8_t)pCurMb->uiLumaQp;
        int8_t iCurChromaQp = (int8_t)pCurMb->uiChromaQp;
        int32_t iLineSize = pFilter->iCsStride[0];
        int32_t iLineSizeUV = pFilter->iCsStride[1];
        int32_t iMbStride = pFilter->iMbStride;

        int32_t iMbX = pCurMb->iMbX;
        int32_t iMbY = pCurMb->iMbY;

        bool* bLeftBsValid = stackalloc bool[2] { (iMbX > 0), ((iMbX > 0) && (pCurMb->uiSliceIdc == (pCurMb - 1)->uiSliceIdc)) };
        bool* bTopBsValid = stackalloc bool[2] { (iMbY > 0), ((iMbY > 0) && (pCurMb->uiSliceIdc == (pCurMb - iMbStride)->uiSliceIdc)) };

        bool iLeftFlag = bLeftBsValid[pFilter->uiFilterIdc];
        bool iTopFlag = bTopBsValid[pFilter->uiFilterIdc];

        uint8_t* pDestY, pDestCb, pDestCr;
        pDestY = pFilter->pCsData[0];
        pDestCb = pFilter->pCsData[1];
        pDestCr = pFilter->pCsData[2];

        if (iLeftFlag)
        {
            pFilter->uiLumaQP = (uint8_t)((iCurLumaQp + (pCurMb - 1)->uiLumaQp + 1) >> 1);
            pFilter->uiChromaQP = (uint8_t)((iCurChromaQp + (pCurMb - 1)->uiChromaQp + 1) >> 1);

            if (Bs(uiBS, 0, 0)[0] == 0x04)
            {
                FilteringEdgeLumaIntraV(pfDeblocking, pFilter, pDestY, iLineSize, null);
                FilteringEdgeChromaIntraV(pfDeblocking, pFilter, pDestCb, pDestCr, iLineSizeUV, null);
            }
            else
            {
                if (*(uint32_t*)Bs(uiBS, 0, 0) != 0)
                {
                    FilteringEdgeLumaV(pfDeblocking, pFilter, pDestY, iLineSize, Bs(uiBS, 0, 0));
                    FilteringEdgeChromaV(pfDeblocking, pFilter, pDestCb, pDestCr, iLineSizeUV, Bs(uiBS, 0, 0));
                }
            }
        }

        pFilter->uiLumaQP = (uint8_t)iCurLumaQp;
        pFilter->uiChromaQP = (uint8_t)iCurChromaQp;

        if (*(uint32_t*)Bs(uiBS, 0, 1) != 0)
        {
            FilteringEdgeLumaV(pfDeblocking, pFilter, &pDestY[1 << 2], iLineSize, Bs(uiBS, 0, 1));
        }

        if (*(uint32_t*)Bs(uiBS, 0, 2) != 0)
        {
            FilteringEdgeLumaV(pfDeblocking, pFilter, &pDestY[2 << 2], iLineSize, Bs(uiBS, 0, 2));
            FilteringEdgeChromaV(pfDeblocking, pFilter, &pDestCb[2 << 1], &pDestCr[2 << 1], iLineSizeUV, Bs(uiBS, 0, 2));
        }

        if (*(uint32_t*)Bs(uiBS, 0, 3) != 0)
        {
            FilteringEdgeLumaV(pfDeblocking, pFilter, &pDestY[3 << 2], iLineSize, Bs(uiBS, 0, 3));
        }

        if (iTopFlag)
        {
            pFilter->uiLumaQP = (uint8_t)((iCurLumaQp + (pCurMb - iMbStride)->uiLumaQp + 1) >> 1);
            pFilter->uiChromaQP = (uint8_t)((iCurChromaQp + (pCurMb - iMbStride)->uiChromaQp + 1) >> 1);

            if (Bs(uiBS, 1, 0)[0] == 0x04)
            {
                FilteringEdgeLumaIntraH(pfDeblocking, pFilter, pDestY, iLineSize, null);
                FilteringEdgeChromaIntraH(pfDeblocking, pFilter, pDestCb, pDestCr, iLineSizeUV, null);
            }
            else
            {
                if (*(uint32_t*)Bs(uiBS, 1, 0) != 0)
                {
                    FilteringEdgeLumaH(pfDeblocking, pFilter, pDestY, iLineSize, Bs(uiBS, 1, 0));
                    FilteringEdgeChromaH(pfDeblocking, pFilter, pDestCb, pDestCr, iLineSizeUV, Bs(uiBS, 1, 0));
                }
            }
        }

        pFilter->uiLumaQP = (uint8_t)iCurLumaQp;
        pFilter->uiChromaQP = (uint8_t)iCurChromaQp;

        if (*(uint32_t*)Bs(uiBS, 1, 1) != 0)
        {
            FilteringEdgeLumaH(pfDeblocking, pFilter, &pDestY[(1 << 2) * iLineSize], iLineSize, Bs(uiBS, 1, 1));
        }

        if (*(uint32_t*)Bs(uiBS, 1, 2) != 0)
        {
            FilteringEdgeLumaH(pfDeblocking, pFilter, &pDestY[(2 << 2) * iLineSize], iLineSize, Bs(uiBS, 1, 2));
            FilteringEdgeChromaH(pfDeblocking, pFilter, &pDestCb[(2 << 1) * iLineSizeUV], &pDestCr[(2 << 1) * iLineSizeUV],
                iLineSizeUV, Bs(uiBS, 1, 2));
        }

        if (*(uint32_t*)Bs(uiBS, 1, 3) != 0)
        {
            FilteringEdgeLumaH(pfDeblocking, pFilter, &pDestY[(3 << 2) * iLineSize], iLineSize, Bs(uiBS, 1, 3));
        }
    }

    public static void FilteringEdgeLumaHV(DeblockingFunc* pfDeblocking, SMB* pCurMb, SDeblockingFilter* pFilter)
    {
        int32_t iLineSize = pFilter->iCsStride[0];
        int32_t iMbStride = pFilter->iMbStride;

        uint8_t* pDestY;
        int8_t iCurQp;
        int32_t iIdexA, iAlpha, iBeta;

        int32_t iMbX = pCurMb->iMbX;
        int32_t iMbY = pCurMb->iMbY;

        bool* bLeftBsValid = stackalloc bool[2] { (iMbX > 0), ((iMbX > 0) && (pCurMb->uiSliceIdc == (pCurMb - 1)->uiSliceIdc)) };
        bool* bTopBsValid = stackalloc bool[2] { (iMbY > 0), ((iMbY > 0) && (pCurMb->uiSliceIdc == (pCurMb - iMbStride)->uiSliceIdc)) };

        bool iLeftFlag = bLeftBsValid[pFilter->uiFilterIdc];
        bool iTopFlag = bTopBsValid[pFilter->uiFilterIdc];

        int8_t* iTc = stackalloc int8_t[4];
        uint32_t uiBSx4Storage;
        uint8_t* uiBSx4 = (uint8_t*)&uiBSx4Storage;

        pDestY = pFilter->pCsData[0];
        iCurQp = (int8_t)pCurMb->uiLumaQp;

        *(uint32_t*)uiBSx4 = 0x03030303;

        // luma v
        if (iLeftFlag)
        {
            pFilter->uiLumaQP = (uint8_t)((iCurQp + (pCurMb - 1)->uiLumaQp + 1) >> 1);
            FilteringEdgeLumaIntraV(pfDeblocking, pFilter, pDestY, iLineSize, null);
        }

        pFilter->uiLumaQP = (uint8_t)iCurQp;
        GET_ALPHA_BETA_FROM_QP(pFilter->uiLumaQP, pFilter->iSliceAlphaC0Offset, pFilter->iSliceBetaOffset, out iIdexA, out iAlpha,
            out iBeta);
        if ((iAlpha | iBeta) != 0)
        {
            TC0_TBL_LOOKUP(iTc, iIdexA, uiBSx4, 0);
            pfDeblocking->pfLumaDeblockingLT4Hor(&pDestY[1 << 2], iLineSize, iAlpha, iBeta, iTc);
            pfDeblocking->pfLumaDeblockingLT4Hor(&pDestY[2 << 2], iLineSize, iAlpha, iBeta, iTc);
            pfDeblocking->pfLumaDeblockingLT4Hor(&pDestY[3 << 2], iLineSize, iAlpha, iBeta, iTc);
        }

        // luma h
        if (iTopFlag)
        {
            pFilter->uiLumaQP = (uint8_t)((iCurQp + (pCurMb - iMbStride)->uiLumaQp + 1) >> 1);
            FilteringEdgeLumaIntraH(pfDeblocking, pFilter, pDestY, iLineSize, null);
        }

        pFilter->uiLumaQP = (uint8_t)iCurQp;
        if ((iAlpha | iBeta) != 0)
        {
            pfDeblocking->pfLumaDeblockingLT4Ver(&pDestY[(1 << 2) * iLineSize], iLineSize, iAlpha, iBeta, iTc);
            pfDeblocking->pfLumaDeblockingLT4Ver(&pDestY[(2 << 2) * iLineSize], iLineSize, iAlpha, iBeta, iTc);
            pfDeblocking->pfLumaDeblockingLT4Ver(&pDestY[(3 << 2) * iLineSize], iLineSize, iAlpha, iBeta, iTc);
        }
    }

    public static void FilteringEdgeChromaHV(DeblockingFunc* pfDeblocking, SMB* pCurMb, SDeblockingFilter* pFilter)
    {
        int32_t iLineSize = pFilter->iCsStride[1];
        int32_t iMbStride = pFilter->iMbStride;

        uint8_t* pDestCb, pDestCr;
        int8_t iCurQp;
        int32_t iIdexA, iAlpha, iBeta;

        int32_t iMbX = pCurMb->iMbX;
        int32_t iMbY = pCurMb->iMbY;

        bool* bLeftBsValid = stackalloc bool[2] { (iMbX > 0), ((iMbX > 0) && (pCurMb->uiSliceIdc == (pCurMb - 1)->uiSliceIdc)) };
        bool* bTopBsValid = stackalloc bool[2] { (iMbY > 0), ((iMbY > 0) && (pCurMb->uiSliceIdc == (pCurMb - iMbStride)->uiSliceIdc)) };

        bool iLeftFlag = bLeftBsValid[pFilter->uiFilterIdc];
        bool iTopFlag = bTopBsValid[pFilter->uiFilterIdc];

        int8_t* iTc = stackalloc int8_t[4];
        uint32_t uiBSx4Storage;
        uint8_t* uiBSx4 = (uint8_t*)&uiBSx4Storage;

        pDestCb = pFilter->pCsData[1];
        pDestCr = pFilter->pCsData[2];
        iCurQp = (int8_t)pCurMb->uiChromaQp;
        *(uint32_t*)uiBSx4 = 0x03030303;

        // chroma v
        if (iLeftFlag)
        {
            pFilter->uiChromaQP = (uint8_t)((iCurQp + (pCurMb - 1)->uiChromaQp + 1) >> 1);
            FilteringEdgeChromaIntraV(pfDeblocking, pFilter, pDestCb, pDestCr, iLineSize, null);
        }

        pFilter->uiChromaQP = (uint8_t)iCurQp;
        GET_ALPHA_BETA_FROM_QP(pFilter->uiChromaQP, pFilter->iSliceAlphaC0Offset, pFilter->iSliceBetaOffset, out iIdexA, out iAlpha,
            out iBeta);
        if ((iAlpha | iBeta) != 0)
        {
            TC0_TBL_LOOKUP(iTc, iIdexA, uiBSx4, 1);
            pfDeblocking->pfChromaDeblockingLT4Hor(&pDestCb[2 << 1], &pDestCr[2 << 1], iLineSize, iAlpha, iBeta, iTc);
        }

        // chroma h
        if (iTopFlag)
        {
            pFilter->uiChromaQP = (uint8_t)((iCurQp + (pCurMb - iMbStride)->uiChromaQp + 1) >> 1);
            FilteringEdgeChromaIntraH(pfDeblocking, pFilter, pDestCb, pDestCr, iLineSize, null);
        }

        pFilter->uiChromaQP = (uint8_t)iCurQp;
        if ((iAlpha | iBeta) != 0)
        {
            pfDeblocking->pfChromaDeblockingLT4Ver(&pDestCb[(2 << 1) * iLineSize], &pDestCr[(2 << 1) * iLineSize], iLineSize, iAlpha,
                iBeta, iTc);
        }
    }

    // merge h&v lookup table operation to save performance
    public static void DeblockingIntraMb(DeblockingFunc* pfDeblocking, SMB* pCurMb, SDeblockingFilter* pFilter)
    {
        FilteringEdgeLumaHV(pfDeblocking, pCurMb, pFilter);
        FilteringEdgeChromaHV(pfDeblocking, pCurMb, pFilter);
    }

    // DeblockingBSCalc_neon and DeblockingBSCalc_AArch64_neon are not ported (C only).

    public static void DeblockingBSCalc_c(SWelsFuncPtrList* pFunc, SMB* pCurMb, uint8_t* uiBS, Mb_Type uiCurMbType,
        int32_t iMbStride, int32_t iLeftFlag, int32_t iTopFlag)
    {
        if (iLeftFlag != 0)
        {
            *(uint32_t*)Bs(uiBS, 0, 0) = IS_INTRA((pCurMb - 1)->uiMbType) ? 0x04040404 : DeblockingBSMarginalMBAvcbase(pCurMb,
                pCurMb - 1, 0);
        }
        else
        {
            *(uint32_t*)Bs(uiBS, 0, 0) = 0;
        }
        if (iTopFlag != 0)
        {
            *(uint32_t*)Bs(uiBS, 1, 0) = IS_INTRA((pCurMb - iMbStride)->uiMbType) ? 0x04040404 : DeblockingBSMarginalMBAvcbase(
                pCurMb, (pCurMb - iMbStride), 1);
        }
        else
        {
            *(uint32_t*)Bs(uiBS, 1, 0) = 0;
        }
        //SKIP MB_16x16 or others
        if (uiCurMbType != MB_TYPE_SKIP)
        {
            pFunc->pfSetNZCZero(pCurMb->pNonZeroCount); // set all none-zero nzc to 1; dbk can be opti!

            if (uiCurMbType == MB_TYPE_16x16)
            {
                DeblockingBSInsideMBAvsbase(pCurMb->pNonZeroCount, uiBS, 1);
            }
            else
            {
                DeblockingBSInsideMBNormal(pCurMb, uiBS, pCurMb->pNonZeroCount);
            }
        }
        else
        {
            *(uint32_t*)Bs(uiBS, 0, 1) = *(uint32_t*)Bs(uiBS, 0, 2) = *(uint32_t*)Bs(uiBS, 0, 3) =
                *(uint32_t*)Bs(uiBS, 1, 1) = *(uint32_t*)Bs(uiBS, 1, 2) = *(uint32_t*)Bs(uiBS, 1, 3) = 0;
        }
    }

    public static void DeblockingMbAvcbase(SWelsFuncPtrList* pFunc, SMB* pCurMb, SDeblockingFilter* pFilter)
    {
        uint8_t* uiBS = stackalloc uint8_t[2 * 4 * 4]; // uint8_t uiBS[2][4][4] = {{{ 0 }}}; stackalloc zeroes it

        Mb_Type uiCurMbType = pCurMb->uiMbType;
        int32_t iMbStride = pFilter->iMbStride;

        int32_t iMbX = pCurMb->iMbX;
        int32_t iMbY = pCurMb->iMbY;

        bool* bLeftBsValid = stackalloc bool[2] { (iMbX > 0), ((iMbX > 0) && (pCurMb->uiSliceIdc == (pCurMb - 1)->uiSliceIdc)) };
        bool* bTopBsValid = stackalloc bool[2] { (iMbY > 0), ((iMbY > 0) && (pCurMb->uiSliceIdc == (pCurMb - iMbStride)->uiSliceIdc)) };

        int32_t iLeftFlag = bLeftBsValid[pFilter->uiFilterIdc] ? 1 : 0;
        int32_t iTopFlag = bTopBsValid[pFilter->uiFilterIdc] ? 1 : 0;

        switch (uiCurMbType)
        {
            case MB_TYPE_INTRA4x4:
            case MB_TYPE_INTRA16x16:
            case MB_TYPE_INTRA_PCM:
                DeblockingIntraMb(&pFunc->pfDeblocking, pCurMb, pFilter);
                break;
            default:
                pFunc->pfDeblocking.pfDeblockingBSCalc(pFunc, pCurMb, uiBS, uiCurMbType, iMbStride, iLeftFlag, iTopFlag);
                DeblockingInterMb(&pFunc->pfDeblocking, pCurMb, pFilter, uiBS);
                break;
        }
    }

    public static void DeblockingFilterFrameAvcbase(SDqLayer* pCurDq, SWelsFuncPtrList* pFunc)
    {
        int32_t i, j;
        int32_t kiMbWidth = pCurDq->iMbWidth;
        int32_t kiMbHeight = pCurDq->iMbHeight;
        SMB* pCurrentMbBlock = pCurDq->sMbDataP;
        SSliceHeaderExt* sSliceHeaderExt = &pCurDq->ppSliceInLayer[0]->sSliceHeaderExt;
        SDeblockingFilter pFilter;

        /* Step1: parameters set */
        if (sSliceHeaderExt->sSliceHeader.uiDisableDeblockingFilterIdc == 1)
        {
            return;
        }

        pFilter.uiFilterIdc = (uint8_t)(sSliceHeaderExt->sSliceHeader.uiDisableDeblockingFilterIdc != 0 ? 1 : 0);

        pFilter.iCsStride[0] = pCurDq->pDecPic->iLineSize[0];
        pFilter.iCsStride[1] = pCurDq->pDecPic->iLineSize[1];
        pFilter.iCsStride[2] = pCurDq->pDecPic->iLineSize[2];

        pFilter.iSliceAlphaC0Offset = sSliceHeaderExt->sSliceHeader.iSliceAlphaC0Offset;
        pFilter.iSliceBetaOffset = sSliceHeaderExt->sSliceHeader.iSliceBetaOffset;

        pFilter.iMbStride = (int16_t)kiMbWidth;

        for (j = 0; j < kiMbHeight; ++j)
        {
            pFilter.pCsData[0] = pCurDq->pDecPic->pData[0] + ((j * pFilter.iCsStride[0]) << 4);
            pFilter.pCsData[1] = pCurDq->pDecPic->pData[1] + ((j * pFilter.iCsStride[1]) << 3);
            pFilter.pCsData[2] = pCurDq->pDecPic->pData[2] + ((j * pFilter.iCsStride[2]) << 3);
            for (i = 0; i < kiMbWidth; i++)
            {
                DeblockingMbAvcbase(pFunc, pCurrentMbBlock, &pFilter);
                ++pCurrentMbBlock;
                pFilter.pCsData[0] += MB_WIDTH_LUMA;
                pFilter.pCsData[1] += MB_WIDTH_CHROMA;
                pFilter.pCsData[2] += MB_WIDTH_CHROMA;
            }
        }
    }

    public static void DeblockingFilterSliceAvcbase(SDqLayer* pCurDq, SWelsFuncPtrList* pFunc, SSlice* pSlice)
    {
        SMB* pMbList = pCurDq->sMbDataP;
        SSliceHeaderExt* sSliceHeaderExt = &pSlice->sSliceHeaderExt;
        SMB* pCurrentMbBlock;

        int32_t kiMbWidth = pCurDq->iMbWidth;
        int32_t kiMbHeight = pCurDq->iMbHeight;
        int32_t kiTotalNumMb = kiMbWidth * kiMbHeight;
        int32_t iCurMbIdx = 0, iNextMbIdx = 0, iNumMbFiltered = 0;

        /* Step1: parameters set */
        if (sSliceHeaderExt->sSliceHeader.uiDisableDeblockingFilterIdc == 1)
        {
            return;
        }

        SDeblockingFilter pFilter;

        pFilter.uiFilterIdc = (uint8_t)(sSliceHeaderExt->sSliceHeader.uiDisableDeblockingFilterIdc != 0 ? 1 : 0);
        pFilter.iCsStride[0] = pCurDq->pDecPic->iLineSize[0];
        pFilter.iCsStride[1] = pCurDq->pDecPic->iLineSize[1];
        pFilter.iCsStride[2] = pCurDq->pDecPic->iLineSize[2];
        pFilter.iSliceAlphaC0Offset = sSliceHeaderExt->sSliceHeader.iSliceAlphaC0Offset;
        pFilter.iSliceBetaOffset = sSliceHeaderExt->sSliceHeader.iSliceBetaOffset;
        pFilter.iMbStride = (int16_t)kiMbWidth;

        iNextMbIdx = sSliceHeaderExt->sSliceHeader.iFirstMbInSlice;

        for (; ; )
        {
            iCurMbIdx = iNextMbIdx;
            pCurrentMbBlock = &pMbList[iCurMbIdx];

            pFilter.pCsData[0] = pCurDq->pDecPic->pData[0] + ((pCurrentMbBlock->iMbX + pCurrentMbBlock->iMbY * pFilter.iCsStride[0])
                                 << 4);
            pFilter.pCsData[1] = pCurDq->pDecPic->pData[1] + ((pCurrentMbBlock->iMbX + pCurrentMbBlock->iMbY * pFilter.iCsStride[1])
                                 << 3);
            pFilter.pCsData[2] = pCurDq->pDecPic->pData[2] + ((pCurrentMbBlock->iMbX + pCurrentMbBlock->iMbY * pFilter.iCsStride[2])
                                 << 3);

            DeblockingMbAvcbase(pFunc, pCurrentMbBlock, &pFilter);

            ++iNumMbFiltered;
            iNextMbIdx = WelsGetNextMbOfSlice(pCurDq, iCurMbIdx);
            //whether all of MB in current slice filtered or not
            if (iNextMbIdx == -1 || iNextMbIdx >= kiTotalNumMb || iNumMbFiltered >= kiTotalNumMb)
            {
                break;
            }
        }
    }

    public static void DeblockingFilterSliceAvcbaseNull(SDqLayer* pCurDq, SWelsFuncPtrList* pFunc, SSlice* pSlice)
    {
    }

    public static void PerformDeblockingFilter(sWelsEncCtx* pEnc)
    {
        SDqLayer* pCurLayer = pEnc->pCurDqLayer;
        SSlice* pSlice = null;

        if (pCurLayer->iLoopFilterDisableIdc == 0)
        {
            DeblockingFilterFrameAvcbase(pCurLayer, pEnc->pFuncList);
        }
        else if (pCurLayer->iLoopFilterDisableIdc == 2)
        {
            int32_t iSliceCount = 0;
            int32_t iSliceIdx = 0;

            iSliceCount = GetCurrentSliceNum(pCurLayer);
            do
            {
                pSlice = pCurLayer->ppSliceInLayer[iSliceIdx];
                Debug.Assert(null != pSlice);
                DeblockingFilterSliceAvcbase(pCurLayer, pEnc->pFuncList, pSlice);
                ++iSliceIdx;
            } while (iSliceIdx < iSliceCount);
        }
    }

    public static void WelsBlockFuncInit(PSetNoneZeroCountZeroFunc* pfSetNZCZero, int32_t iCpu)
    {
        *pfSetNZCZero = &WelsNonZeroCount_c;
        // The NEON, SSE2, MMI and MSA versions are not ported (C only).
    }

    public static void DeblockingInit(DeblockingFunc* pFunc, int32_t iCpu)
    {
        pFunc->pfLumaDeblockingLT4Ver = &DeblockLumaLt4V_c;
        pFunc->pfLumaDeblockingEQ4Ver = &DeblockLumaEq4V_c;
        pFunc->pfLumaDeblockingLT4Hor = &DeblockLumaLt4H_c;
        pFunc->pfLumaDeblockingEQ4Hor = &DeblockLumaEq4H_c;

        pFunc->pfChromaDeblockingLT4Ver = &DeblockChromaLt4V_c;
        pFunc->pfChromaDeblockingEQ4Ver = &DeblockChromaEq4V_c;
        pFunc->pfChromaDeblockingLT4Hor = &DeblockChromaLt4H_c;
        pFunc->pfChromaDeblockingEQ4Hor = &DeblockChromaEq4H_c;

        pFunc->pfDeblockingBSCalc = &DeblockingBSCalc_c;

        // In place of the X86_ASM, NEON and AArch64 edge filters, the ones in
        // common/deblocking_common_simd.cs. The SIMD DeblockingBSCalc and the MMI,
        // MSA and LSX versions are not ported.
        if (WelsCpuHasSimd128((uint32_t)iCpu))
        {
            pFunc->pfLumaDeblockingLT4Ver = &DeblockLumaLt4V_simd;
            pFunc->pfLumaDeblockingEQ4Ver = &DeblockLumaEq4V_simd;
            pFunc->pfLumaDeblockingLT4Hor = &DeblockLumaLt4H_simd;
            pFunc->pfLumaDeblockingEQ4Hor = &DeblockLumaEq4H_simd;

            pFunc->pfChromaDeblockingLT4Ver = &DeblockChromaLt4V_simd;
            pFunc->pfChromaDeblockingEQ4Ver = &DeblockChromaEq4V_simd;
            pFunc->pfChromaDeblockingLT4Hor = &DeblockChromaLt4H_simd;
            pFunc->pfChromaDeblockingEQ4Hor = &DeblockChromaEq4H_simd;
        }
    }
}
