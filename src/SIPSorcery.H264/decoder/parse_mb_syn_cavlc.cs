//-----------------------------------------------------------------------------
// Filename: parse_mb_syn_cavlc.cs
//
// Description: Port of codec/decoder/core/inc/parse_mb_syn_cavlc.h and
// src/parse_mb_syn_cavlc.cpp: CAVLC macroblock syntax parsing (neighbour
// caches, intra mode checks, residual blocks, P-slice inter prediction info).
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// The CABAC neighbour fills (WelsFillCacheInterCabac, WelsFillDirectCacheCabac)
// and the B-slice ParseInterBInfo are not ported yet: the port targets
// Constrained Baseline first. SReadBitsCache and the SHIFT_BUFFER/POP_BUFFER
// macros (wels_common_basis.h) live here, next to their only users.
// WELS_GET_PREFIX_BITS is GetPrefixBits (dec_golomb.cs). The [LIST_A][30][MV_A]
// and [LIST_A][30] cache arrays are passed as InlineArray30 row pointers, the
// way the C arrays decay.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 25 Nov 2026  Claude          Created (D2).
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
 * \file    parse_mb_syn_cavlc.c
 *
 * \brief   Interfaces implementation for parsing the syntax of MB
 *
 * \date    03/17/2009 Created
 *
 *************************************************************************************
 */

using System.Runtime.CompilerServices;

namespace SIPSorcery.H264.Decoder;

/// <summary>TagReadBitsCache.</summary>
internal unsafe struct SReadBitsCache
{
    public uint32_t uiCache32Bit;
    public uint8_t uiRemainBits;
    public uint8_t* pBuf;
}

internal static unsafe partial class WelsDec
{
    private const int MAX_LEVEL_PREFIX = 15;

    // wels_common_basis.h. The shifts are by a run-time count, which x86 masks
    // to 5 bits exactly as C# does.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SHIFT_BUFFER(SReadBitsCache* pBitsCache)
    {
        pBitsCache->pBuf += 2;
        pBitsCache->uiRemainBits += 16;
        pBitsCache->uiCache32Bit |= (uint32_t)(((pBitsCache->pBuf[2] << 8) | pBitsCache->pBuf[3]) << (32 - pBitsCache->uiRemainBits));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void POP_BUFFER(SReadBitsCache* pBitsCache, int32_t iCount)
    {
        pBitsCache->uiCache32Bit <<= iCount;
        pBitsCache->uiRemainBits = (uint8_t)(pBitsCache->uiRemainBits - iCount);
    }

    public static void GetNeighborAvailMbType(SWelsNeighAvail* pNeighAvail, SDqLayer* pCurDqLayer)
    {
        int32_t iCurSliceIdc, iTopSliceIdc, iLeftTopSliceIdc, iRightTopSliceIdc, iLeftSliceIdc;
        int32_t iCurXy, iTopXy = 0, iLeftXy = 0, iLeftTopXy = 0, iRightTopXy = 0;
        int32_t iCurX, iCurY;

        iCurXy = pCurDqLayer->iMbXyIndex;
        iCurX = pCurDqLayer->iMbX;
        iCurY = pCurDqLayer->iMbY;
        iCurSliceIdc = pCurDqLayer->pSliceIdc[iCurXy];
        if (iCurX != 0)
        {
            iLeftXy = iCurXy - 1;
            iLeftSliceIdc = pCurDqLayer->pSliceIdc[iLeftXy];
            pNeighAvail->iLeftAvail = (iLeftSliceIdc == iCurSliceIdc) ? 1 : 0;
            pNeighAvail->iLeftCbp = pNeighAvail->iLeftAvail != 0 ? pCurDqLayer->pCbp[iLeftXy] : (int8_t)0;
        }
        else
        {
            pNeighAvail->iLeftAvail = 0;
            pNeighAvail->iLeftTopAvail = 0;
            pNeighAvail->iLeftCbp = 0;
        }

        if (iCurY != 0)
        {
            iTopXy = iCurXy - pCurDqLayer->iMbWidth;
            iTopSliceIdc = pCurDqLayer->pSliceIdc[iTopXy];
            pNeighAvail->iTopAvail = (iTopSliceIdc == iCurSliceIdc) ? 1 : 0;
            pNeighAvail->iTopCbp = pNeighAvail->iTopAvail != 0 ? pCurDqLayer->pCbp[iTopXy] : (int8_t)0;
            if (iCurX != 0)
            {
                iLeftTopXy = iTopXy - 1;
                iLeftTopSliceIdc = pCurDqLayer->pSliceIdc[iLeftTopXy];
                pNeighAvail->iLeftTopAvail = (iLeftTopSliceIdc == iCurSliceIdc) ? 1 : 0;
            }
            else
            {
                pNeighAvail->iLeftTopAvail = 0;
            }
            if (iCurX != (pCurDqLayer->iMbWidth - 1))
            {
                iRightTopXy = iTopXy + 1;
                iRightTopSliceIdc = pCurDqLayer->pSliceIdc[iRightTopXy];
                pNeighAvail->iRightTopAvail = (iRightTopSliceIdc == iCurSliceIdc) ? 1 : 0;
            }
            else
            {
                pNeighAvail->iRightTopAvail = 0;
            }
        }
        else
        {
            pNeighAvail->iTopAvail = 0;
            pNeighAvail->iLeftTopAvail = 0;
            pNeighAvail->iRightTopAvail = 0;
            pNeighAvail->iTopCbp = 0;
        }

        pNeighAvail->iLeftType = (pNeighAvail->iLeftAvail != 0 ? (int32_t)pCurDqLayer->pDec->pMbType[iLeftXy] : 0);
        pNeighAvail->iTopType = (pNeighAvail->iTopAvail != 0 ? (int32_t)pCurDqLayer->pDec->pMbType[iTopXy] : 0);
        pNeighAvail->iLeftTopType = (pNeighAvail->iLeftTopAvail != 0 ? (int32_t)pCurDqLayer->pDec->pMbType[iLeftTopXy] : 0);
        pNeighAvail->iRightTopType = (pNeighAvail->iRightTopAvail != 0 ? (int32_t)pCurDqLayer->pDec->pMbType[iRightTopXy] : 0);
    }

    public static void WelsFillCacheNonZeroCount(SWelsNeighAvail* pNeighAvail, uint8_t* pNonZeroCount,
                                                 SDqLayer* pCurDqLayer)
    { //no matter slice type, intra_pred_constrained_flag
        int32_t iCurXy = pCurDqLayer->iMbXyIndex;
        int32_t iTopXy = 0;
        int32_t iLeftXy = 0;
        if (pNeighAvail->iTopAvail != 0)
        {
            iTopXy = iCurXy - pCurDqLayer->iMbWidth;
        }
        if (pNeighAvail->iLeftAvail != 0)
        {
            iLeftXy = iCurXy - 1;
        }

        //stuff non_zero_coeff_count from pNeighAvail(left and top)
        if (pNeighAvail->iTopAvail != 0)
        {
            ST32(&pNonZeroCount[1], LD32(&pCurDqLayer->pNzc[iTopXy][12]));
            pNonZeroCount[0] = pNonZeroCount[5] = pNonZeroCount[29] = 0;
            ST16(&pNonZeroCount[6], LD16(&pCurDqLayer->pNzc[iTopXy][20]));
            ST16(&pNonZeroCount[30], LD16(&pCurDqLayer->pNzc[iTopXy][22]));
        }
        else
        {
            ST32(&pNonZeroCount[1], 0xFFFFFFFFU);
            pNonZeroCount[0] = pNonZeroCount[5] = pNonZeroCount[29] = 0xFF;
            ST16(&pNonZeroCount[6], 0xFFFF);
            ST16(&pNonZeroCount[30], 0xFFFF);
        }

        if (pNeighAvail->iLeftAvail != 0)
        {
            pNonZeroCount[8 * 1] = (uint8_t)pCurDqLayer->pNzc[iLeftXy][3];
            pNonZeroCount[8 * 2] = (uint8_t)pCurDqLayer->pNzc[iLeftXy][7];
            pNonZeroCount[8 * 3] = (uint8_t)pCurDqLayer->pNzc[iLeftXy][11];
            pNonZeroCount[8 * 4] = (uint8_t)pCurDqLayer->pNzc[iLeftXy][15];

            pNonZeroCount[5 + 8 * 1] = (uint8_t)pCurDqLayer->pNzc[iLeftXy][17];
            pNonZeroCount[5 + 8 * 2] = (uint8_t)pCurDqLayer->pNzc[iLeftXy][21];
            pNonZeroCount[5 + 8 * 4] = (uint8_t)pCurDqLayer->pNzc[iLeftXy][19];
            pNonZeroCount[5 + 8 * 5] = (uint8_t)pCurDqLayer->pNzc[iLeftXy][23];
        }
        else
        {
            pNonZeroCount[8 * 1] =
              pNonZeroCount[8 * 2] =
                pNonZeroCount[8 * 3] =
                  pNonZeroCount[8 * 4] = 0xFF;//unavailable (-1)

            pNonZeroCount[5 + 8 * 1] =
              pNonZeroCount[5 + 8 * 2] = 0xFF;//unavailable (-1)

            pNonZeroCount[5 + 8 * 4] =
              pNonZeroCount[5 + 8 * 5] = 0xFF;//unavailable (-1)
        }
    }

    public static void WelsFillCacheConstrain1IntraNxN(SWelsNeighAvail* pNeighAvail, uint8_t* pNonZeroCount, int8_t* pIntraPredMode,
                                                       SDqLayer* pCurDqLayer)
    { //no matter slice type
        int32_t iCurXy = pCurDqLayer->iMbXyIndex;
        int32_t iTopXy = 0;
        int32_t iLeftXy = 0;

        //stuff non_zero_coeff_count from pNeighAvail(left and top)
        WelsFillCacheNonZeroCount(pNeighAvail, pNonZeroCount, pCurDqLayer);

        if (pNeighAvail->iTopAvail != 0)
        {
            iTopXy = iCurXy - pCurDqLayer->iMbWidth;
        }
        if (pNeighAvail->iLeftAvail != 0)
        {
            iLeftXy = iCurXy - 1;
        }

        //intraNxN_pred_mode
        if (pNeighAvail->iTopAvail != 0 && IS_INTRANxN((uint32_t)pNeighAvail->iTopType))
        { //top
            ST32(pIntraPredMode + 1, LD32(&pCurDqLayer->pIntraPredMode[iTopXy][0]));
        }
        else
        {
            int32_t iPred;
            if (IS_INTRA16x16((uint32_t)pNeighAvail->iTopType) || (MB_TYPE_INTRA_PCM == pNeighAvail->iTopType))
                iPred = 0x02020202;
            else
                iPred = unchecked((int32_t)0xffffffff);
            ST32(pIntraPredMode + 1, (uint32_t)iPred);
        }

        if (pNeighAvail->iLeftAvail != 0 && IS_INTRANxN((uint32_t)pNeighAvail->iLeftType))
        { //left
            pIntraPredMode[0 + 8] = pCurDqLayer->pIntraPredMode[iLeftXy][4];
            pIntraPredMode[0 + 8 * 2] = pCurDqLayer->pIntraPredMode[iLeftXy][5];
            pIntraPredMode[0 + 8 * 3] = pCurDqLayer->pIntraPredMode[iLeftXy][6];
            pIntraPredMode[0 + 8 * 4] = pCurDqLayer->pIntraPredMode[iLeftXy][3];
        }
        else
        {
            int8_t iPred;
            if (IS_INTRA16x16((uint32_t)pNeighAvail->iLeftType) || (MB_TYPE_INTRA_PCM == pNeighAvail->iLeftType))
                iPred = 2;
            else
                iPred = -1;
            pIntraPredMode[0 + 8] =
              pIntraPredMode[0 + 8 * 2] =
                pIntraPredMode[0 + 8 * 3] =
                  pIntraPredMode[0 + 8 * 4] = iPred;
        }
    }

    public static void WelsFillCacheConstrain0IntraNxN(SWelsNeighAvail* pNeighAvail, uint8_t* pNonZeroCount, int8_t* pIntraPredMode,
                                                       SDqLayer* pCurDqLayer)
    { //no matter slice type
        int32_t iCurXy = pCurDqLayer->iMbXyIndex;
        int32_t iTopXy = 0;
        int32_t iLeftXy = 0;

        //stuff non_zero_coeff_count from pNeighAvail(left and top)
        WelsFillCacheNonZeroCount(pNeighAvail, pNonZeroCount, pCurDqLayer);

        if (pNeighAvail->iTopAvail != 0)
        {
            iTopXy = iCurXy - pCurDqLayer->iMbWidth;
        }
        if (pNeighAvail->iLeftAvail != 0)
        {
            iLeftXy = iCurXy - 1;
        }

        //intra4x4_pred_mode
        if (pNeighAvail->iTopAvail != 0 && IS_INTRANxN((uint32_t)pNeighAvail->iTopType))
        { //top
            ST32(pIntraPredMode + 1, LD32(&pCurDqLayer->pIntraPredMode[iTopXy][0]));
        }
        else
        {
            int32_t iPred;
            if (pNeighAvail->iTopAvail != 0)
                iPred = 0x02020202;
            else
                iPred = unchecked((int32_t)0xffffffff);
            ST32(pIntraPredMode + 1, (uint32_t)iPred);
        }

        if (pNeighAvail->iLeftAvail != 0 && IS_INTRANxN((uint32_t)pNeighAvail->iLeftType))
        { //left
            pIntraPredMode[0 + 8 * 1] = pCurDqLayer->pIntraPredMode[iLeftXy][4];
            pIntraPredMode[0 + 8 * 2] = pCurDqLayer->pIntraPredMode[iLeftXy][5];
            pIntraPredMode[0 + 8 * 3] = pCurDqLayer->pIntraPredMode[iLeftXy][6];
            pIntraPredMode[0 + 8 * 4] = pCurDqLayer->pIntraPredMode[iLeftXy][3];
        }
        else
        {
            int8_t iPred;
            if (pNeighAvail->iLeftAvail != 0)
                iPred = 2;
            else
                iPred = -1;
            pIntraPredMode[0 + 8 * 1] =
              pIntraPredMode[0 + 8 * 2] =
                pIntraPredMode[0 + 8 * 3] =
                  pIntraPredMode[0 + 8 * 4] = iPred;
        }
    }

    // WelsFillCacheInterCabac and WelsFillDirectCacheCabac: not ported yet (CABAC).

    public static void WelsFillCacheInter(SWelsNeighAvail* pNeighAvail, uint8_t* pNonZeroCount,
                                          InlineArray30<InlineArray2<int16_t>>* iMvArray, InlineArray30<int8_t>* iRefIdxArray, SDqLayer* pCurDqLayer)
    {
        int32_t iCurXy = pCurDqLayer->iMbXyIndex;
        int32_t iTopXy = 0;
        int32_t iLeftXy = 0;
        int32_t iLeftTopXy = 0;
        int32_t iRightTopXy = 0;

        SSlice* pSlice = &pCurDqLayer->sLayerInfo.sSliceInLayer;
        SSliceHeader* pSliceHeader = &pSlice->sSliceHeaderExt.sSliceHeader;
        int32_t listCount = 1;
        if (pSliceHeader->eSliceType == B_SLICE)
        {
            listCount = 2;
        }

        //stuff non_zero_coeff_count from pNeighAvail(left and top)
        WelsFillCacheNonZeroCount(pNeighAvail, pNonZeroCount, pCurDqLayer);

        if (pNeighAvail->iTopAvail != 0)
        {
            iTopXy = iCurXy - pCurDqLayer->iMbWidth;
        }
        if (pNeighAvail->iLeftAvail != 0)
        {
            iLeftXy = iCurXy - 1;
        }
        if (pNeighAvail->iLeftTopAvail != 0)
        {
            iLeftTopXy = iCurXy - 1 - pCurDqLayer->iMbWidth;
        }
        if (pNeighAvail->iRightTopAvail != 0)
        {
            iRightTopXy = iCurXy + 1 - pCurDqLayer->iMbWidth;
        }

        for (int32_t listIdx = 0; listIdx < listCount; ++listIdx)
        {
            //stuff mv_cache and iRefIdxArray from left and top (inter)
            if (pNeighAvail->iLeftAvail != 0 && IS_INTER((uint32_t)pNeighAvail->iLeftType))
            {
                ST32(&iMvArray[listIdx][6], LD32(&pCurDqLayer->pDec->pMv[listIdx].p[iLeftXy][3]));
                ST32(&iMvArray[listIdx][12], LD32(&pCurDqLayer->pDec->pMv[listIdx].p[iLeftXy][7]));
                ST32(&iMvArray[listIdx][18], LD32(&pCurDqLayer->pDec->pMv[listIdx].p[iLeftXy][11]));
                ST32(&iMvArray[listIdx][24], LD32(&pCurDqLayer->pDec->pMv[listIdx].p[iLeftXy][15]));
                iRefIdxArray[listIdx][6] = pCurDqLayer->pDec->pRefIndex[listIdx].p[iLeftXy][3];
                iRefIdxArray[listIdx][12] = pCurDqLayer->pDec->pRefIndex[listIdx].p[iLeftXy][7];
                iRefIdxArray[listIdx][18] = pCurDqLayer->pDec->pRefIndex[listIdx].p[iLeftXy][11];
                iRefIdxArray[listIdx][24] = pCurDqLayer->pDec->pRefIndex[listIdx].p[iLeftXy][15];
            }
            else
            {
                ST32(&iMvArray[listIdx][6], 0);
                ST32(&iMvArray[listIdx][12], 0);
                ST32(&iMvArray[listIdx][18], 0);
                ST32(&iMvArray[listIdx][24], 0);

                if (0 == pNeighAvail->iLeftAvail)
                { //not available
                    iRefIdxArray[listIdx][6] =
                      iRefIdxArray[listIdx][12] =
                        iRefIdxArray[listIdx][18] =
                          iRefIdxArray[listIdx][24] = REF_NOT_AVAIL;
                }
                else
                { //available but is intra mb type
                    iRefIdxArray[listIdx][6] =
                      iRefIdxArray[listIdx][12] =
                        iRefIdxArray[listIdx][18] =
                          iRefIdxArray[listIdx][24] = REF_NOT_IN_LIST;
                }
            }
            if (pNeighAvail->iLeftTopAvail != 0 && IS_INTER((uint32_t)pNeighAvail->iLeftTopType))
            {
                ST32(&iMvArray[listIdx][0], LD32(&pCurDqLayer->pDec->pMv[listIdx].p[iLeftTopXy][15]));
                iRefIdxArray[listIdx][0] = pCurDqLayer->pDec->pRefIndex[listIdx].p[iLeftTopXy][15];
            }
            else
            {
                ST32(&iMvArray[listIdx][0], 0);
                if (0 == pNeighAvail->iLeftTopAvail)
                { //not available
                    iRefIdxArray[listIdx][0] = REF_NOT_AVAIL;
                }
                else
                { //available but is intra mb type
                    iRefIdxArray[listIdx][0] = REF_NOT_IN_LIST;
                }
            }
            if (pNeighAvail->iTopAvail != 0 && IS_INTER((uint32_t)pNeighAvail->iTopType))
            {
                ST64(&iMvArray[listIdx][1], LD64(&pCurDqLayer->pDec->pMv[listIdx].p[iTopXy][12]));
                ST64(&iMvArray[listIdx][3], LD64(&pCurDqLayer->pDec->pMv[listIdx].p[iTopXy][14]));
                ST32(&iRefIdxArray[listIdx][1], LD32(&pCurDqLayer->pDec->pRefIndex[listIdx].p[iTopXy][12]));
            }
            else
            {
                ST64(&iMvArray[listIdx][1], 0);
                ST64(&iMvArray[listIdx][3], 0);
                if (0 == pNeighAvail->iTopAvail)
                { //not available
                    iRefIdxArray[listIdx][1] =
                      iRefIdxArray[listIdx][2] =
                        iRefIdxArray[listIdx][3] =
                          iRefIdxArray[listIdx][4] = REF_NOT_AVAIL;
                }
                else
                { //available but is intra mb type
                    iRefIdxArray[listIdx][1] =
                      iRefIdxArray[listIdx][2] =
                        iRefIdxArray[listIdx][3] =
                          iRefIdxArray[listIdx][4] = REF_NOT_IN_LIST;
                }
            }
            if (pNeighAvail->iRightTopAvail != 0 && IS_INTER((uint32_t)pNeighAvail->iRightTopType))
            {
                ST32(&iMvArray[listIdx][5], LD32(&pCurDqLayer->pDec->pMv[listIdx].p[iRightTopXy][12]));
                iRefIdxArray[listIdx][5] = pCurDqLayer->pDec->pRefIndex[listIdx].p[iRightTopXy][12];
            }
            else
            {
                ST32(&iMvArray[listIdx][5], 0);
                if (0 == pNeighAvail->iRightTopAvail)
                { //not available
                    iRefIdxArray[listIdx][5] = REF_NOT_AVAIL;
                }
                else
                { //available but is intra mb type
                    iRefIdxArray[listIdx][5] = REF_NOT_IN_LIST;
                }
            }
            //right-top 4*4 block unavailable
            ST32(&iMvArray[listIdx][9], 0);
            ST32(&iMvArray[listIdx][21], 0);
            ST32(&iMvArray[listIdx][11], 0);
            ST32(&iMvArray[listIdx][17], 0);
            ST32(&iMvArray[listIdx][23], 0);
            iRefIdxArray[listIdx][9] =
              iRefIdxArray[listIdx][21] =
                iRefIdxArray[listIdx][11] =
                  iRefIdxArray[listIdx][17] =
                    iRefIdxArray[listIdx][23] = REF_NOT_AVAIL;
        }
    }

    public static int32_t PredIntra4x4Mode(int8_t* pIntraPredMode, int32_t iIdx4)
    {
        int8_t iTopMode = pIntraPredMode[g_kuiScan8[iIdx4] - 8];
        int8_t iLeftMode = pIntraPredMode[g_kuiScan8[iIdx4] - 1];
        int8_t iBestMode;

        if (-1 == iLeftMode || -1 == iTopMode)
        {
            iBestMode = 2;
        }
        else
        {
            iBestMode = iLeftMode < iTopMode ? iLeftMode : iTopMode; // WELS_MIN
        }
        return iBestMode;
    }

    // CHECK_I16_MODE, CHECK_CHROMA_MODE, CHECK_I4_MODE
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool CHECK_I16_MODE(int8_t a, int32_t b, int32_t c, int32_t d) =>
        (a == g_ksI16PredInfo[a].iPredMode) &&
        (b >= g_ksI16PredInfo[a].iLeftAvail) &&
        (c >= g_ksI16PredInfo[a].iTopAvail) &&
        (d >= g_ksI16PredInfo[a].iLeftTopAvail);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool CHECK_CHROMA_MODE(int8_t a, int32_t b, int32_t c, int32_t d) =>
        (a == g_ksChromaPredInfo[a].iPredMode) &&
        (b >= g_ksChromaPredInfo[a].iLeftAvail) &&
        (c >= g_ksChromaPredInfo[a].iTopAvail) &&
        (d >= g_ksChromaPredInfo[a].iLeftTopAvail);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool CHECK_I4_MODE(int8_t a, int32_t b, int32_t c, int32_t d) =>
        (a == g_ksI4PredInfo[a].iPredMode) &&
        (b >= g_ksI4PredInfo[a].iLeftAvail) &&
        (c >= g_ksI4PredInfo[a].iTopAvail) &&
        (d >= g_ksI4PredInfo[a].iLeftTopAvail);


    public static int32_t CheckIntra16x16PredMode(uint8_t uiSampleAvail, int8_t* pMode)
    {
        int32_t iLeftAvail = uiSampleAvail & 0x04;
        int32_t bLeftTopAvail = uiSampleAvail & 0x02;
        int32_t iTopAvail = uiSampleAvail & 0x01;

        if ((*pMode < 0) || (*pMode > MAX_PRED_MODE_ID_I16x16))
        {
            return GENERATE_ERROR_NO(ERR_LEVEL_MB_DATA, ERR_INFO_INVALID_I16x16_PRED_MODE);
        }

        if (I16_PRED_DC == *pMode)
        {
            if (iLeftAvail != 0 && iTopAvail != 0)
            {
                return ERR_NONE;
            }
            else if (iLeftAvail != 0)
            {
                *pMode = I16_PRED_DC_L;
            }
            else if (iTopAvail != 0)
            {
                *pMode = I16_PRED_DC_T;
            }
            else
            {
                *pMode = I16_PRED_DC_128;
            }
        }
        else
        {
            bool bModeAvail = CHECK_I16_MODE(*pMode, iLeftAvail, iTopAvail, bLeftTopAvail);
            if (!bModeAvail)
            {
                return GENERATE_ERROR_NO(ERR_LEVEL_MB_DATA, ERR_INFO_INVALID_I16x16_PRED_MODE);
            }
        }
        return ERR_NONE;
    }


    public static int32_t CheckIntraChromaPredMode(uint8_t uiSampleAvail, int8_t* pMode)
    {
        int32_t iLeftAvail = uiSampleAvail & 0x04;
        int32_t bLeftTopAvail = uiSampleAvail & 0x02;
        int32_t iTopAvail = uiSampleAvail & 0x01;

        if (C_PRED_DC == *pMode)
        {
            if (iLeftAvail != 0 && iTopAvail != 0)
            {
                return ERR_NONE;
            }
            else if (iLeftAvail != 0)
            {
                *pMode = C_PRED_DC_L;
            }
            else if (iTopAvail != 0)
            {
                *pMode = C_PRED_DC_T;
            }
            else
            {
                *pMode = C_PRED_DC_128;
            }
        }
        else
        {
            bool bModeAvail = CHECK_CHROMA_MODE(*pMode, iLeftAvail, iTopAvail, bLeftTopAvail);
            if (!bModeAvail)
            {
                return GENERATE_ERROR_NO(ERR_LEVEL_MB_DATA, ERR_INFO_INVALID_I_CHROMA_PRED_MODE);
            }
        }
        return ERR_NONE;
    }

    public static int32_t CheckIntraNxNPredMode(int32_t* pSampleAvail, int8_t* pMode, int32_t iIndex, bool b8x8)
    {
        int8_t iIdx = (int8_t)g_kuiCache30ScanIdx[iIndex];

        int32_t iLeftAvail = pSampleAvail[iIdx - 1];
        int32_t iTopAvail = pSampleAvail[iIdx - 6];
        int32_t bLeftTopAvail = pSampleAvail[iIdx - 7];
        int32_t bRightTopAvail = pSampleAvail[iIdx - (b8x8 ? 4 : 5)];  // Diff with 4x4 Pred

        int8_t iFinalMode;

        if ((*pMode < 0) || (*pMode > MAX_PRED_MODE_ID_I4x4))
        {
            return GENERATE_ERROR_NO(ERR_LEVEL_MB_DATA, ERR_INVALID_INTRA4X4_MODE);
        }

        if (I4_PRED_DC == *pMode)
        {
            if (iLeftAvail != 0 && iTopAvail != 0)
            {
                return *pMode;
            }
            else if (iLeftAvail != 0)
            {
                iFinalMode = I4_PRED_DC_L;
            }
            else if (iTopAvail != 0)
            {
                iFinalMode = I4_PRED_DC_T;
            }
            else
            {
                iFinalMode = I4_PRED_DC_128;
            }
        }
        else
        {
            bool bModeAvail = CHECK_I4_MODE(*pMode, iLeftAvail, iTopAvail, bLeftTopAvail);
            if (!bModeAvail)
            {
                return GENERATE_ERROR_NO(ERR_LEVEL_MB_DATA, ERR_INVALID_INTRA4X4_MODE);
            }

            iFinalMode = *pMode;

            //if right-top unavailable, modify mode DDL and VL (padding rightmost pixel of top)
            if (I4_PRED_DDL == iFinalMode && 0 == bRightTopAvail)
            {
                iFinalMode = I4_PRED_DDL_TOP;
            }
            else if (I4_PRED_VL == iFinalMode && 0 == bRightTopAvail)
            {
                iFinalMode = I4_PRED_VL_TOP;
            }
        }
        return iFinalMode;
    }

    public static void BsStartCavlc(SBitStringAux* pBs)
    {
        pBs->iIndex = (intX_t)(((pBs->pCurBuf - pBs->pStartBuf) << 3) - (16 - pBs->iLeftBits));
    }
    public static void BsEndCavlc(SBitStringAux* pBs)
    {
        pBs->pCurBuf = pBs->pStartBuf + (pBs->iIndex >> 3);
        uint32_t uiCache32Bit = (uint32_t)((((pBs->pCurBuf[0] << 8) | pBs->pCurBuf[1]) << 16) |
                                            (pBs->pCurBuf[2] << 8) | pBs->pCurBuf[3]);
        pBs->uiCurBits = uiCache32Bit << (int32_t)(pBs->iIndex & 0x07);
        pBs->pCurBuf += 4;
        pBs->iLeftBits = -16 + (int32_t)(pBs->iIndex & 0x07);
    }


    // return: used bits
    private static int32_t CavlcGetTrailingOnesAndTotalCoeff(out uint8_t uiTotalCoeff, out uint8_t uiTrailingOnes,
        SReadBitsCache* pBitsCache, SVlcTable* pVlcTable, bool bChromaDc, int8_t nC)
    {
        int32_t iUsedBits = 0;
        int32_t iIndexVlc, iIndexValue, iNcMapIdx;
        uint32_t uiCount;
        uint32_t uiValue;

        if (bChromaDc)
        {
            uiValue = pBitsCache->uiCache32Bit >> 24;
            iIndexVlc = pVlcTable->kpChromaCoeffTokenVlcTable[uiValue][0];
            uiCount = pVlcTable->kpChromaCoeffTokenVlcTable[uiValue][1];
            POP_BUFFER(pBitsCache, (int32_t)uiCount);
            iUsedBits += (int32_t)uiCount;
            uiTrailingOnes = g_kuiVlcTrailingOneTotalCoeffTable[iIndexVlc][0];
            uiTotalCoeff = g_kuiVlcTrailingOneTotalCoeffTable[iIndexVlc][1];
        }
        else
        { //luma
            iNcMapIdx = g_kuiNcMapTable[nC];
            if (iNcMapIdx <= 2)
            {
                uiValue = pBitsCache->uiCache32Bit >> 24;
                if (uiValue < g_kuiVlcTableNeedMoreBitsThread[iNcMapIdx])
                {
                    // const uint8_t* kpVlcTableMoreBitsCountList[3] = {g_kuiVlcTableMoreBitsCount0, ..1, ..2};
                    uint8_t* kpVlcTableMoreBitsCount = iNcMapIdx == 0 ? g_kuiVlcTableMoreBitsCount0
                        : iNcMapIdx == 1 ? g_kuiVlcTableMoreBitsCount1 : g_kuiVlcTableMoreBitsCount2;
                    POP_BUFFER(pBitsCache, 8);
                    iUsedBits += 8;
                    iIndexValue = (int32_t)(pBitsCache->uiCache32Bit >> (32 - kpVlcTableMoreBitsCount[uiValue]));
                    iIndexVlc = pVlcTable->kpCoeffTokenVlcTable[iNcMapIdx + 1][(int32_t)uiValue].p[iIndexValue][0];
                    uiCount = pVlcTable->kpCoeffTokenVlcTable[iNcMapIdx + 1][(int32_t)uiValue].p[iIndexValue][1];
                    POP_BUFFER(pBitsCache, (int32_t)uiCount);
                    iUsedBits += (int32_t)uiCount;
                }
                else
                {
                    iIndexVlc = pVlcTable->kpCoeffTokenVlcTable[0][iNcMapIdx].p[uiValue][0];
                    uiCount = pVlcTable->kpCoeffTokenVlcTable[0][iNcMapIdx].p[uiValue][1];
                    uiValue = pBitsCache->uiCache32Bit >> (int32_t)(32 - uiCount);
                    POP_BUFFER(pBitsCache, (int32_t)uiCount);
                    iUsedBits += (int32_t)uiCount;
                }
            }
            else
            {
                uiValue = pBitsCache->uiCache32Bit >> (32 - 6);
                POP_BUFFER(pBitsCache, 6);
                iUsedBits += 6;
                iIndexVlc = pVlcTable->kpCoeffTokenVlcTable[0][3].p[uiValue][0];  //differ
            }
            uiTrailingOnes = g_kuiVlcTrailingOneTotalCoeffTable[iIndexVlc][0];
            uiTotalCoeff = g_kuiVlcTrailingOneTotalCoeffTable[iIndexVlc][1];
        }

        return iUsedBits;
    }

    private static int32_t CavlcGetLevelVal(int32_t* iLevel, SReadBitsCache* pBitsCache, uint8_t uiTotalCoeff,
                                            uint8_t uiTrailingOnes)
    {
        int32_t i, iUsedBits = 0;
        int32_t iSuffixLength, iSuffixLengthSize, iLevelPrefix, iPrefixBits, iLevelCode, iThreshold;
        for (i = 0; i < uiTrailingOnes; i++)
        {
            iLevel[i] = (int32_t)(1 - ((pBitsCache->uiCache32Bit >> (30 - i)) & 0x02));
        }
        POP_BUFFER(pBitsCache, uiTrailingOnes);
        iUsedBits += uiTrailingOnes;

        iSuffixLength = (uiTotalCoeff > 10 && uiTrailingOnes < 3) ? 1 : 0;

        for (; i < uiTotalCoeff; i++)
        {
            if (pBitsCache->uiRemainBits <= 16) SHIFT_BUFFER(pBitsCache);
            iPrefixBits = (int32_t)GetPrefixBits(pBitsCache->uiCache32Bit); // WELS_GET_PREFIX_BITS
            if (iPrefixBits > MAX_LEVEL_PREFIX + 1) //iPrefixBits includes leading "0"s and first "1", should +1
                return -1;
            POP_BUFFER(pBitsCache, iPrefixBits);
            iUsedBits += iPrefixBits;
            iLevelPrefix = iPrefixBits - 1;

            iLevelCode = iLevelPrefix << iSuffixLength; //differ
            iSuffixLengthSize = iSuffixLength;

            if (iLevelPrefix >= 14)
            {
                if (14 == iLevelPrefix && 0 == iSuffixLength)
                    iSuffixLengthSize = 4;
                else if (15 == iLevelPrefix)
                {
                    iSuffixLengthSize = 12;
                    if (iSuffixLength == 0)
                        iLevelCode += 15;
                }
            }

            if (iSuffixLengthSize > 0)
            {
                if (pBitsCache->uiRemainBits <= iSuffixLengthSize) SHIFT_BUFFER(pBitsCache);
                iLevelCode += (int32_t)(pBitsCache->uiCache32Bit >> (32 - iSuffixLengthSize));
                POP_BUFFER(pBitsCache, iSuffixLengthSize);
                iUsedBits += iSuffixLengthSize;
            }

            iLevelCode += (((i == uiTrailingOnes) && (uiTrailingOnes < 3)) ? 1 : 0) << 1;
            iLevel[i] = ((iLevelCode + 2) >> 1);
            iLevel[i] -= (iLevel[i] << 1) & (-(iLevelCode & 0x01));

            iSuffixLength += (iSuffixLength == 0) ? 1 : 0;
            iThreshold = 3 << (iSuffixLength - 1);
            iSuffixLength += (((iLevel[i] > iThreshold) || (iLevel[i] < -iThreshold)) && (iSuffixLength < 6)) ? 1 : 0;
        }

        return iUsedBits;
    }

    private static int32_t CavlcGetTotalZeros(out int32_t iZerosLeft, SReadBitsCache* pBitsCache, uint8_t uiTotalCoeff,
                                              SVlcTable* pVlcTable, bool bChromaDc)
    {
        int32_t iCount, iUsedBits = 0;
        uint8_t* kpBitNumMap;
        uint32_t uiValue;

        int32_t iTotalZeroVlcIdx;
        uint8_t uiTableType;
        //chroma_dc (0 < uiTotalCoeff < 4); others (chroma_ac or luma: 0 < uiTotalCoeff < 16)

        if (bChromaDc)
        {
            iTotalZeroVlcIdx = uiTotalCoeff;
            kpBitNumMap = g_kuiTotalZerosBitNumChromaMap;
            uiTableType = 1; // bChromaDc
        }
        else
        {
            iTotalZeroVlcIdx = uiTotalCoeff;
            kpBitNumMap = g_kuiTotalZerosBitNumMap;
            uiTableType = 0;
        }

        iCount = kpBitNumMap[iTotalZeroVlcIdx - 1];
        if (pBitsCache->uiRemainBits < iCount) SHIFT_BUFFER(
            pBitsCache); // if uiRemainBits+16 still smaller than iCount?? potential bug
        uiValue = pBitsCache->uiCache32Bit >> (32 - iCount);
        iCount = pVlcTable->kpTotalZerosTable[uiTableType][iTotalZeroVlcIdx - 1].p[uiValue][1];
        POP_BUFFER(pBitsCache, iCount);
        iUsedBits += iCount;
        iZerosLeft = pVlcTable->kpTotalZerosTable[uiTableType][iTotalZeroVlcIdx - 1].p[uiValue][0];

        return iUsedBits;
    }
    private static int32_t CavlcGetRunBefore(int32_t* iRun, SReadBitsCache* pBitsCache, uint8_t uiTotalCoeff,
                                             SVlcTable* pVlcTable, int32_t iZerosLeft)
    {
        int32_t i, iUsedBits = 0;
        uint32_t uiCount, uiValue, iPrefixBits;

        for (i = 0; i < uiTotalCoeff - 1; i++)
        {
            if (iZerosLeft > 0)
            {
                uiCount = g_kuiZeroLeftBitNumMap[iZerosLeft];
                if (pBitsCache->uiRemainBits < uiCount) SHIFT_BUFFER(pBitsCache);
                uiValue = pBitsCache->uiCache32Bit >> (int32_t)(32 - uiCount);
                if (iZerosLeft < 7)
                {
                    uiCount = pVlcTable->kpZeroTable[iZerosLeft - 1].p[uiValue][1];
                    POP_BUFFER(pBitsCache, (int32_t)uiCount);
                    iUsedBits += (int32_t)uiCount;
                    iRun[i] = pVlcTable->kpZeroTable[iZerosLeft - 1].p[uiValue][0];
                }
                else
                {
                    POP_BUFFER(pBitsCache, (int32_t)uiCount);
                    iUsedBits += (int32_t)uiCount;
                    if (pVlcTable->kpZeroTable[6].p[uiValue][0] < 7)
                    {
                        iRun[i] = pVlcTable->kpZeroTable[6].p[uiValue][0];
                    }
                    else
                    {
                        if (pBitsCache->uiRemainBits < 16) SHIFT_BUFFER(pBitsCache);
                        iPrefixBits = GetPrefixBits(pBitsCache->uiCache32Bit); // WELS_GET_PREFIX_BITS
                        iRun[i] = (int32_t)(iPrefixBits + 6);
                        if (iRun[i] > iZerosLeft)
                            return -1;
                        POP_BUFFER(pBitsCache, (int32_t)iPrefixBits);
                        iUsedBits += (int32_t)iPrefixBits;
                    }
                }
            }
            else
            {
                for (int j = i; j < uiTotalCoeff; j++)
                {
                    iRun[j] = 0;
                }
                return iUsedBits;
            }

            iZerosLeft -= iRun[i];
        }

        iRun[uiTotalCoeff - 1] = iZerosLeft;

        return iUsedBits;
    }

    public static int32_t WelsResidualBlockCavlc(SVlcTable* pVlcTable, uint8_t* pNonZeroCountCache, SBitStringAux* pBs, int32_t iIndex,
                                                 int32_t iMaxNumCoeff,
                                                 uint8_t* kpZigzagTable, int32_t iResidualProperty, int16_t* pTCoeff, uint8_t uiQp,
                                                 SWelsDecoderContext* pCtx)
    {
        int32_t* iLevel = stackalloc int32_t[16];
        int32_t iZerosLeft, iCoeffNum;
        int32_t* iRun = stackalloc int32_t[16];
        int32_t iCurNonZeroCacheIdx, i;


        int32_t iMbResProperty = 0;
        GetMbResProperty(&iMbResProperty, &iResidualProperty, true);
        uint16_t* kpDequantCoeff = pCtx->bUseScalingList ? (uint16_t*)&pCtx->pDequant_coeff4x4[iMbResProperty].p[uiQp] :
                                   (uint16_t*)&g_kuiDequantCoeff[uiQp];

        int8_t nA, nB, nC = 0;
        uint8_t uiTotalCoeff, uiTrailingOnes;
        int32_t iUsedBits = 0;
        intX_t iCurIdx = pBs->iIndex;
        uint8_t* pBuf = ((uint8_t*)pBs->pStartBuf) + (iCurIdx >> 3);
        bool bChromaDc = (CHROMA_DC == iResidualProperty);
        bool bChroma = (bChromaDc || CHROMA_AC == iResidualProperty);
        SReadBitsCache sReadBitsCache;

        uint32_t uiCache32Bit = (uint32_t)((((pBuf[0] << 8) | pBuf[1]) << 16) | (pBuf[2] << 8) | pBuf[3]);
        sReadBitsCache.uiCache32Bit = uiCache32Bit << (int32_t)(iCurIdx & 0x07);
        sReadBitsCache.uiRemainBits = (uint8_t)(32 - (iCurIdx & 0x07));
        sReadBitsCache.pBuf = pBuf;
        //////////////////////////////////////////////////////////////////////////

        if (bChroma)
        {
            iCurNonZeroCacheIdx = g_kuiCache48CountScan4Idx[iIndex];
            nA = (int8_t)pNonZeroCountCache[iCurNonZeroCacheIdx - 1];
            nB = (int8_t)pNonZeroCountCache[iCurNonZeroCacheIdx - 8];
        }
        else
        { //luma
            iCurNonZeroCacheIdx = g_kuiCache48CountScan4Idx[iIndex];
            nA = (int8_t)pNonZeroCountCache[iCurNonZeroCacheIdx - 1];
            nB = (int8_t)pNonZeroCountCache[iCurNonZeroCacheIdx - 8];
        }

        WELS_NON_ZERO_COUNT_AVERAGE(ref nC, nA, nB);

        iUsedBits += CavlcGetTrailingOnesAndTotalCoeff(out uiTotalCoeff, out uiTrailingOnes, &sReadBitsCache, pVlcTable, bChromaDc,
                     nC);

        if (iResidualProperty != CHROMA_DC && iResidualProperty != I16_LUMA_DC)
        {
            pNonZeroCountCache[iCurNonZeroCacheIdx] = uiTotalCoeff;
            //////////////////////////////////////////////////////////////////////////
        }
        if (0 == uiTotalCoeff)
        {
            pBs->iIndex += iUsedBits;
            return ERR_NONE;
        }
        if ((uiTrailingOnes > 3) || (uiTotalCoeff > 16))
        { /////////////////check uiTrailingOnes and uiTotalCoeff
            return GENERATE_ERROR_NO(ERR_LEVEL_MB_DATA, ERR_INFO_CAVLC_INVALID_TOTAL_COEFF_OR_TRAILING_ONES);
        }
        if ((i = CavlcGetLevelVal(iLevel, &sReadBitsCache, uiTotalCoeff, uiTrailingOnes)) == -1)
        {
            return GENERATE_ERROR_NO(ERR_LEVEL_MB_DATA, ERR_INFO_CAVLC_INVALID_LEVEL);
        }
        iUsedBits += i;
        if (uiTotalCoeff < iMaxNumCoeff)
        {
            iUsedBits += CavlcGetTotalZeros(out iZerosLeft, &sReadBitsCache, uiTotalCoeff, pVlcTable, bChromaDc);
        }
        else
        {
            iZerosLeft = 0;
        }

        if ((iZerosLeft < 0) || ((iZerosLeft + uiTotalCoeff) > iMaxNumCoeff))
        {
            return GENERATE_ERROR_NO(ERR_LEVEL_MB_DATA, ERR_INFO_CAVLC_INVALID_ZERO_LEFT);
        }
        if ((i = CavlcGetRunBefore(iRun, &sReadBitsCache, uiTotalCoeff, pVlcTable, iZerosLeft)) == -1)
        {
            return GENERATE_ERROR_NO(ERR_LEVEL_MB_DATA, ERR_INFO_CAVLC_INVALID_RUN_BEFORE);
        }
        iUsedBits += i;
        pBs->iIndex += iUsedBits;
        iCoeffNum = -1;

        if (iResidualProperty == CHROMA_DC)
        {
            //chroma dc scaling process, is kpDequantCoeff[0]? LevelScale(qPdc%6,0,0))<<(qPdc/6-6), the transform is done at construction.
            for (i = uiTotalCoeff - 1; i >= 0; --i)
            {
                //FIXME merge into rundecode?
                int32_t j;
                iCoeffNum += iRun[i] + 1; //FIXME add 1 earlier ?
                j = kpZigzagTable[iCoeffNum];
                pTCoeff[j] = (int16_t)iLevel[i];
            }
            WelsChromaDcIdct(pTCoeff);
            //scaling
            if (!pCtx->bUseScalingList)
            {
                for (int j = 0; j < 4; ++j)
                {
                    pTCoeff[kpZigzagTable[j]] = (int16_t)((pTCoeff[kpZigzagTable[j]] * kpDequantCoeff[0]) >> 1);
                }
            }
            else
            {
                for (int j = 0; j < 4; ++j)
                {
                    pTCoeff[kpZigzagTable[j]] = (int16_t)(((int64_t)pTCoeff[kpZigzagTable[j]] * (int64_t)kpDequantCoeff[0]) >> 5);
                }
            }
        }
        else if (iResidualProperty == I16_LUMA_DC)
        { //DC coefficent, only call in Intra_16x16, base_mode_flag = 0
            for (i = uiTotalCoeff - 1; i >= 0; --i)
            { //FIXME merge into rundecode?
                int32_t j;
                iCoeffNum += iRun[i] + 1; //FIXME add 1 earlier ?
                j = kpZigzagTable[iCoeffNum];
                pTCoeff[j] = (int16_t)iLevel[i];
            }
            WelsLumaDcDequantIdct(pTCoeff, uiQp, pCtx);
        }
        else
        {
            for (i = uiTotalCoeff - 1; i >= 0; --i)
            { //FIXME merge into  rundecode?
                int32_t j;
                iCoeffNum += iRun[i] + 1; //FIXME add 1 earlier ?
                j = kpZigzagTable[iCoeffNum];
                if (!pCtx->bUseScalingList)
                {
                    pTCoeff[j] = (int16_t)(iLevel[i] * kpDequantCoeff[j & 0x07]);
                }
                else
                {
                    pTCoeff[j] = (int16_t)((iLevel[i] * kpDequantCoeff[j] + 8) >> 4);
                }
            }
        }

        return ERR_NONE;
    }

    public static int32_t WelsResidualBlockCavlc8x8(SVlcTable* pVlcTable, uint8_t* pNonZeroCountCache, SBitStringAux* pBs, int32_t iIndex,
                                                    int32_t iMaxNumCoeff, uint8_t* kpZigzagTable, int32_t iResidualProperty,
                                                    int16_t* pTCoeff, int32_t iIdx4x4, uint8_t uiQp,
                                                    SWelsDecoderContext* pCtx)
    {
        int32_t* iLevel = stackalloc int32_t[16];
        int32_t iZerosLeft, iCoeffNum;
        int32_t* iRun = stackalloc int32_t[16];
        int32_t iCurNonZeroCacheIdx, i;

        int32_t iMbResProperty = 0;
        GetMbResProperty(&iMbResProperty, &iResidualProperty, true);

        uint16_t* kpDequantCoeff = pCtx->bUseScalingList ? (uint16_t*)&pCtx->pDequant_coeff8x8[iMbResProperty - 6].p[uiQp] :
                                   (uint16_t*)&g_kuiDequantCoeff8x8[uiQp];

        int8_t nA, nB, nC = 0;
        uint8_t uiTotalCoeff, uiTrailingOnes;
        int32_t iUsedBits = 0;
        intX_t iCurIdx = pBs->iIndex;
        uint8_t* pBuf = ((uint8_t*)pBs->pStartBuf) + (iCurIdx >> 3);
        bool bChromaDc = (CHROMA_DC == iResidualProperty);
        bool bChroma = (bChromaDc || CHROMA_AC == iResidualProperty);
        SReadBitsCache sReadBitsCache;

        uint32_t uiCache32Bit = (uint32_t)((((pBuf[0] << 8) | pBuf[1]) << 16) | (pBuf[2] << 8) | pBuf[3]);
        sReadBitsCache.uiCache32Bit = uiCache32Bit << (int32_t)(iCurIdx & 0x07);
        sReadBitsCache.uiRemainBits = (uint8_t)(32 - (iCurIdx & 0x07));
        sReadBitsCache.pBuf = pBuf;
        //////////////////////////////////////////////////////////////////////////

        if (bChroma)
        {
            iCurNonZeroCacheIdx = g_kuiCache48CountScan4Idx[iIndex];
            nA = (int8_t)pNonZeroCountCache[iCurNonZeroCacheIdx - 1];
            nB = (int8_t)pNonZeroCountCache[iCurNonZeroCacheIdx - 8];
        }
        else
        { //luma
            iCurNonZeroCacheIdx = g_kuiCache48CountScan4Idx[iIndex];
            nA = (int8_t)pNonZeroCountCache[iCurNonZeroCacheIdx - 1];
            nB = (int8_t)pNonZeroCountCache[iCurNonZeroCacheIdx - 8];
        }

        WELS_NON_ZERO_COUNT_AVERAGE(ref nC, nA, nB);

        iUsedBits += CavlcGetTrailingOnesAndTotalCoeff(out uiTotalCoeff, out uiTrailingOnes, &sReadBitsCache, pVlcTable, bChromaDc,
                     nC);

        if (iResidualProperty != CHROMA_DC && iResidualProperty != I16_LUMA_DC)
        {
            pNonZeroCountCache[iCurNonZeroCacheIdx] = uiTotalCoeff;
            //////////////////////////////////////////////////////////////////////////
        }
        if (0 == uiTotalCoeff)
        {
            pBs->iIndex += iUsedBits;
            return ERR_NONE;
        }
        if ((uiTrailingOnes > 3) || (uiTotalCoeff > 16))
        { /////////////////check uiTrailingOnes and uiTotalCoeff
            return GENERATE_ERROR_NO(ERR_LEVEL_MB_DATA, ERR_INFO_CAVLC_INVALID_TOTAL_COEFF_OR_TRAILING_ONES);
        }
        if ((i = CavlcGetLevelVal(iLevel, &sReadBitsCache, uiTotalCoeff, uiTrailingOnes)) == -1)
        {
            return GENERATE_ERROR_NO(ERR_LEVEL_MB_DATA, ERR_INFO_CAVLC_INVALID_LEVEL);
        }
        iUsedBits += i;
        if (uiTotalCoeff < iMaxNumCoeff)
        {
            iUsedBits += CavlcGetTotalZeros(out iZerosLeft, &sReadBitsCache, uiTotalCoeff, pVlcTable, bChromaDc);
        }
        else
        {
            iZerosLeft = 0;
        }

        if ((iZerosLeft < 0) || ((iZerosLeft + uiTotalCoeff) > iMaxNumCoeff))
        {
            return GENERATE_ERROR_NO(ERR_LEVEL_MB_DATA, ERR_INFO_CAVLC_INVALID_ZERO_LEFT);
        }
        if ((i = CavlcGetRunBefore(iRun, &sReadBitsCache, uiTotalCoeff, pVlcTable, iZerosLeft)) == -1)
        {
            return GENERATE_ERROR_NO(ERR_LEVEL_MB_DATA, ERR_INFO_CAVLC_INVALID_RUN_BEFORE);
        }
        iUsedBits += i;
        pBs->iIndex += iUsedBits;
        iCoeffNum = -1;

        for (i = uiTotalCoeff - 1; i >= 0; --i)
        { //FIXME merge into  rundecode?
            int32_t j;
            iCoeffNum += iRun[i] + 1; //FIXME add 1 earlier ?
            j = (iCoeffNum << 2) + iIdx4x4;
            j = kpZigzagTable[j];
            pTCoeff[j] = (int16_t)(uiQp >= 36 ? ((iLevel[i] * kpDequantCoeff[j]) * (1 << (uiQp / 6 - 6)))
                         : ((iLevel[i] * kpDequantCoeff[j] + (1 << (5 - uiQp / 6))) >> (6 - uiQp / 6)));
        }

        return ERR_NONE;
    }

    public static int32_t ParseInterInfo(SWelsDecoderContext* pCtx, InlineArray30<InlineArray2<int16_t>>* iMvArray, InlineArray30<int8_t>* iRefIdxArray,
                                         SBitStringAux* pBs)
    {
        SSlice* pSlice = &pCtx->pCurDqLayer->sLayerInfo.sSliceInLayer;
        SSliceHeader* pSliceHeader = &pSlice->sSliceHeaderExt.sSliceHeader;
        SPicture** ppRefPic = (SPicture**)&pCtx->sRefPic.pRefList[(int32_t)LIST_0];
        int32_t* iRefCount = stackalloc int32_t[2];
        SDqLayer* pCurDqLayer = pCtx->pCurDqLayer;
        int32_t i, j;
        int32_t iMbXy = pCurDqLayer->iMbXyIndex;
        int32_t* iMotionPredFlag = stackalloc int32_t[4];
        int16_t* iMv = stackalloc int16_t[2];
        uint32_t uiCode;
        int32_t iCode;
        uint32_t uiRetTmp; // WELS_READ_VERIFY
        int16_t iMinVmv = pSliceHeader->pSps->pSLevelLimits->iMinVmv;
        int16_t iMaxVmv = pSliceHeader->pSps->pSLevelLimits->iMaxVmv;
        iMotionPredFlag[0] = iMotionPredFlag[1] = iMotionPredFlag[2] = iMotionPredFlag[3] =
                               pSlice->sSliceHeaderExt.bDefaultMotionPredFlag ? 1 : 0;
        iRefCount[0] = pSliceHeader->uiRefCount[0];
        iRefCount[1] = pSliceHeader->uiRefCount[1];

        bool bIsPending = GetThreadCount(pCtx) > 1;

        switch (pCurDqLayer->pDec->pMbType[iMbXy])
        {
            case MB_TYPE_16x16:
                {
                    int32_t iRefIdx = 0;
                    if (pSlice->sSliceHeaderExt.bAdaptiveMotionPredFlag)
                    {
                        if ((uiRetTmp = (uint32_t)BsGetOneBit(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //motion_prediction_flag_l0[ mbPartIdx ]
                        iMotionPredFlag[0] = (int32_t)uiCode;
                    }
                    if (iMotionPredFlag[0] == 0)
                    {
                        if ((uiRetTmp = (uint32_t)BsGetTe0(pBs, iRefCount[0], &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //motion_prediction_flag_l1[ mbPartIdx ]
                        iRefIdx = (int32_t)uiCode;
                        // Security check: iRefIdx should be in range 0 to num_ref_idx_l0_active_minus1, includsive
                        // ref to standard section 7.4.5.1. iRefCount[0] is 1 + num_ref_idx_l0_active_minus1.
                        if ((iRefIdx < 0) || (iRefIdx >= iRefCount[0]) || (ppRefPic[iRefIdx] == null))
                        { //error ref_idx
                            pCtx->bMbRefConcealed = true;
                            if (pCtx->pParam->eEcActiveIdc != ERROR_CON_DISABLE)
                            {
                                iRefIdx = 0;
                                pCtx->iErrorCode |= (int32_t)dsBitstreamError;
                            }
                            else
                            {
                                return GENERATE_ERROR_NO(ERR_LEVEL_MB_DATA, ERR_INFO_INVALID_REF_INDEX);
                            }
                        }
                        pCtx->bMbRefConcealed = pCtx->bRPLRError || pCtx->bMbRefConcealed || !(ppRefPic[iRefIdx] != null
                                                && (ppRefPic[iRefIdx]->bIsComplete || bIsPending));
                    }
                    else
                    {
                        WelsLog(&(pCtx->sLogCtx), WELS_LOG_WARNING, "inter parse: iMotionPredFlag = 1 not supported. ");
                        return GENERATE_ERROR_NO(ERR_LEVEL_MB_DATA, ERR_INFO_UNSUPPORTED_ILP);
                    }
                    PredMv(iMvArray, iRefIdxArray, (int32_t)LIST_0, 0, 4, (int8_t)iRefIdx, iMv);

                    if ((uiRetTmp = (uint32_t)BsGetSe(pBs, &iCode)) != ERR_NONE) return (int32_t)uiRetTmp; //mvd_l0[ mbPartIdx ][ 0 ][ compIdx ]
                    iMv[0] = (int16_t)(iMv[0] + iCode);
                    if ((uiRetTmp = (uint32_t)BsGetSe(pBs, &iCode)) != ERR_NONE) return (int32_t)uiRetTmp; //mvd_l1[ mbPartIdx ][ 0 ][ compIdx ]
                    iMv[1] = (int16_t)(iMv[1] + iCode);
                    WELS_CHECK_SE_BOTH_WARNING(pCtx, iMv[1], iMinVmv, iMaxVmv, "vertical mv");
                    UpdateP16x16MotionInfo(pCurDqLayer, (int32_t)LIST_0, (int8_t)iRefIdx, iMv);
                }
                break;
            case MB_TYPE_16x8:
                {
                    int32_t* iRefIdx = stackalloc int32_t[2];
                    for (i = 0; i < 2; i++)
                    {
                        if (pSlice->sSliceHeaderExt.bAdaptiveMotionPredFlag)
                        {
                            if ((uiRetTmp = (uint32_t)BsGetOneBit(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //motion_prediction_flag_l0[ mbPartIdx ]
                            iMotionPredFlag[i] = (int32_t)uiCode;
                        }
                    }

                    for (i = 0; i < 2; i++)
                    {
                        if (iMotionPredFlag[i] != 0)
                        {
                            WelsLog(&(pCtx->sLogCtx), WELS_LOG_WARNING, "inter parse: iMotionPredFlag = 1 not supported. ");
                            return GENERATE_ERROR_NO(ERR_LEVEL_MB_DATA, ERR_INFO_UNSUPPORTED_ILP);
                        }
                        if ((uiRetTmp = (uint32_t)BsGetTe0(pBs, iRefCount[0], &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //ref_idx_l0[ mbPartIdx ]
                        iRefIdx[i] = (int32_t)uiCode;
                        if ((iRefIdx[i] < 0) || (iRefIdx[i] >= iRefCount[0]) || (ppRefPic[iRefIdx[i]] == null))
                        { //error ref_idx
                            pCtx->bMbRefConcealed = true;
                            if (pCtx->pParam->eEcActiveIdc != ERROR_CON_DISABLE)
                            {
                                iRefIdx[i] = 0;
                                pCtx->iErrorCode |= (int32_t)dsBitstreamError;
                            }
                            else
                            {
                                return GENERATE_ERROR_NO(ERR_LEVEL_MB_DATA, ERR_INFO_INVALID_REF_INDEX);
                            }
                        }
                        pCtx->bMbRefConcealed = pCtx->bRPLRError || pCtx->bMbRefConcealed || !(ppRefPic[iRefIdx[i]] != null
                                                && (ppRefPic[iRefIdx[i]]->bIsComplete || bIsPending));
                    }
                    for (i = 0; i < 2; i++)
                    {
                        PredInter16x8Mv(iMvArray, iRefIdxArray, (int32_t)LIST_0, i << 3, (int8_t)iRefIdx[i], iMv);

                        if ((uiRetTmp = (uint32_t)BsGetSe(pBs, &iCode)) != ERR_NONE) return (int32_t)uiRetTmp; //mvd_l0[ mbPartIdx ][ 0 ][ compIdx ]
                        iMv[0] = (int16_t)(iMv[0] + iCode);
                        if ((uiRetTmp = (uint32_t)BsGetSe(pBs, &iCode)) != ERR_NONE) return (int32_t)uiRetTmp; //mvd_l1[ mbPartIdx ][ 0 ][ compIdx ]
                        iMv[1] = (int16_t)(iMv[1] + iCode);
                        WELS_CHECK_SE_BOTH_WARNING(pCtx, iMv[1], iMinVmv, iMaxVmv, "vertical mv");
                        UpdateP16x8MotionInfo(pCurDqLayer, iMvArray, iRefIdxArray, (int32_t)LIST_0, i << 3, (int8_t)iRefIdx[i], iMv);
                    }
                }
                break;
            case MB_TYPE_8x16:
                {
                    int32_t* iRefIdx = stackalloc int32_t[2];
                    for (i = 0; i < 2; i++)
                    {
                        if (pSlice->sSliceHeaderExt.bAdaptiveMotionPredFlag)
                        {
                            if ((uiRetTmp = (uint32_t)BsGetOneBit(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //motion_prediction_flag_l0[ mbPartIdx ]
                            iMotionPredFlag[i] = (int32_t)uiCode;
                        }
                    }

                    for (i = 0; i < 2; i++)
                    {
                        if (iMotionPredFlag[i] == 0)
                        {
                            if ((uiRetTmp = (uint32_t)BsGetTe0(pBs, iRefCount[0], &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //ref_idx_l0[ mbPartIdx ]
                            iRefIdx[i] = (int32_t)uiCode;
                            if ((iRefIdx[i] < 0) || (iRefIdx[i] >= iRefCount[0]) || (ppRefPic[iRefIdx[i]] == null))
                            { //error ref_idx
                                pCtx->bMbRefConcealed = true;
                                if (pCtx->pParam->eEcActiveIdc != ERROR_CON_DISABLE)
                                {
                                    iRefIdx[i] = 0;
                                    pCtx->iErrorCode |= (int32_t)dsBitstreamError;
                                }
                                else
                                {
                                    return GENERATE_ERROR_NO(ERR_LEVEL_MB_DATA, ERR_INFO_INVALID_REF_INDEX);
                                }
                            }
                            pCtx->bMbRefConcealed = pCtx->bRPLRError || pCtx->bMbRefConcealed || !(ppRefPic[iRefIdx[i]] != null
                                                    && (ppRefPic[iRefIdx[i]]->bIsComplete || bIsPending));
                        }
                        else
                        {
                            WelsLog(&(pCtx->sLogCtx), WELS_LOG_WARNING, "inter parse: iMotionPredFlag = 1 not supported. ");
                            return GENERATE_ERROR_NO(ERR_LEVEL_MB_DATA, ERR_INFO_UNSUPPORTED_ILP);
                        }

                    }
                    for (i = 0; i < 2; i++)
                    {
                        PredInter8x16Mv(iMvArray, iRefIdxArray, (int32_t)LIST_0, i << 2, (int8_t)iRefIdx[i], iMv);

                        if ((uiRetTmp = (uint32_t)BsGetSe(pBs, &iCode)) != ERR_NONE) return (int32_t)uiRetTmp; //mvd_l0[ mbPartIdx ][ 0 ][ compIdx ]
                        iMv[0] = (int16_t)(iMv[0] + iCode);
                        if ((uiRetTmp = (uint32_t)BsGetSe(pBs, &iCode)) != ERR_NONE) return (int32_t)uiRetTmp; //mvd_l1[ mbPartIdx ][ 0 ][ compIdx ]
                        iMv[1] = (int16_t)(iMv[1] + iCode);
                        WELS_CHECK_SE_BOTH_WARNING(pCtx, iMv[1], iMinVmv, iMaxVmv, "vertical mv");
                        UpdateP8x16MotionInfo(pCurDqLayer, iMvArray, iRefIdxArray, (int32_t)LIST_0, i << 2, (int8_t)iRefIdx[i], iMv);
                    }
                }
                break;
            case MB_TYPE_8x8:
            case MB_TYPE_8x8_REF0:
                {
                    int32_t* iRefIdx = stackalloc int32_t[4];
                    int32_t* iSubPartCount = stackalloc int32_t[4];
                    int32_t* iPartWidth = stackalloc int32_t[4];
                    uint32_t uiSubMbType;
                    iRefIdx[0] = iRefIdx[1] = iRefIdx[2] = iRefIdx[3] = 0; // = {0}

                    if (MB_TYPE_8x8_REF0 == pCurDqLayer->pDec->pMbType[iMbXy])
                    {
                        iRefCount[0] =
                          iRefCount[1] = 1;
                    }

                    //uiSubMbType, partition
                    for (i = 0; i < 4; i++)
                    {
                        if ((uiRetTmp = (uint32_t)BsGetUe(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //sub_mb_type[ mbPartIdx ]
                        uiSubMbType = uiCode;
                        if (uiSubMbType >= 4)
                        { //invalid uiSubMbType
                            return GENERATE_ERROR_NO(ERR_LEVEL_MB_DATA, ERR_INFO_INVALID_SUB_MB_TYPE);
                        }
                        pCurDqLayer->pSubMbType[iMbXy][i] = g_ksInterPSubMbTypeInfo[uiSubMbType].iType;
                        iSubPartCount[i] = g_ksInterPSubMbTypeInfo[uiSubMbType].iPartCount;
                        iPartWidth[i] = g_ksInterPSubMbTypeInfo[uiSubMbType].iPartWidth;

                        // Need modification when B picture add in, reference to 7.3.5
                        pCurDqLayer->pNoSubMbPartSizeLessThan8x8Flag[iMbXy] &= (uiSubMbType == 0);
                    }

                    if (pSlice->sSliceHeaderExt.bAdaptiveMotionPredFlag)
                    {
                        for (i = 0; i < 4; i++)
                        {
                            if ((uiRetTmp = (uint32_t)BsGetOneBit(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //motion_prediction_flag_l0[ mbPartIdx ]
                            iMotionPredFlag[i] = (int32_t)uiCode;
                        }
                    }

                    //iRefIdxArray
                    if (MB_TYPE_8x8_REF0 == pCurDqLayer->pDec->pMbType[iMbXy])
                    {
                        memset(&pCurDqLayer->pDec->pRefIndex[0].p[iMbXy], 0, 16);
                    }
                    else
                    {
                        for (i = 0; i < 4; i++)
                        {
                            int16_t iIndex8 = (int16_t)(i << 2);
                            uint8_t uiScan4Idx = g_kuiScan4[iIndex8];

                            if (iMotionPredFlag[i] == 0)
                            {
                                if ((uiRetTmp = (uint32_t)BsGetTe0(pBs, iRefCount[0], &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //ref_idx_l0[ mbPartIdx ]
                                iRefIdx[i] = (int32_t)uiCode;
                                if ((iRefIdx[i] < 0) || (iRefIdx[i] >= iRefCount[0]) || (ppRefPic[iRefIdx[i]] == null))
                                { //error ref_idx
                                    pCtx->bMbRefConcealed = true;
                                    if (pCtx->pParam->eEcActiveIdc != ERROR_CON_DISABLE)
                                    {
                                        iRefIdx[i] = 0;
                                        pCtx->iErrorCode |= (int32_t)dsBitstreamError;
                                    }
                                    else
                                    {
                                        return GENERATE_ERROR_NO(ERR_LEVEL_MB_DATA, ERR_INFO_INVALID_REF_INDEX);
                                    }
                                }
                                pCtx->bMbRefConcealed = pCtx->bRPLRError || pCtx->bMbRefConcealed || !(ppRefPic[iRefIdx[i]] != null
                                                        && (ppRefPic[iRefIdx[i]]->bIsComplete || bIsPending));

                                pCurDqLayer->pDec->pRefIndex[0].p[iMbXy][uiScan4Idx] = pCurDqLayer->pDec->pRefIndex[0].p[iMbXy][uiScan4Idx + 1] =
                                      pCurDqLayer->pDec->pRefIndex[0].p[iMbXy][uiScan4Idx + 4] = pCurDqLayer->pDec->pRefIndex[0].p[iMbXy][uiScan4Idx + 5] =
                                            (int8_t)iRefIdx[i];
                            }
                            else
                            {
                                WelsLog(&(pCtx->sLogCtx), WELS_LOG_WARNING, "inter parse: iMotionPredFlag = 1 not supported. ");
                                return GENERATE_ERROR_NO(ERR_LEVEL_MB_DATA, ERR_INFO_UNSUPPORTED_ILP);
                            }
                        }
                    }

                    //gain mv and update mv cache
                    for (i = 0; i < 4; i++)
                    {
                        int8_t iPartCount = (int8_t)iSubPartCount[i];
                        // C declares a new uiSubMbType and iMv here, shadowing the ones above;
                        // C# reuses them (neither outer value is needed again).
                        uiSubMbType = pCurDqLayer->pSubMbType[iMbXy][i];
                        int16_t iPartIdx, iBlockWidth = (int16_t)iPartWidth[i], iIdx = (int16_t)(i << 2);
                        uint8_t uiScan4Idx, uiCacheIdx;

                        uint8_t uiIdx4Cache = g_kuiCache30ScanIdx[iIdx];

                        iRefIdxArray[0][uiIdx4Cache] = iRefIdxArray[0][uiIdx4Cache + 1] =
                                                         iRefIdxArray[0][uiIdx4Cache + 6] = iRefIdxArray[0][uiIdx4Cache + 7] = (int8_t)iRefIdx[i];

                        for (j = 0; j < iPartCount; j++)
                        {
                            iPartIdx = (int16_t)(iIdx + j * iBlockWidth);
                            uiScan4Idx = g_kuiScan4[iPartIdx];
                            uiCacheIdx = g_kuiCache30ScanIdx[iPartIdx];
                            PredMv(iMvArray, iRefIdxArray, (int32_t)LIST_0, iPartIdx, iBlockWidth, (int8_t)iRefIdx[i], iMv);

                            if ((uiRetTmp = (uint32_t)BsGetSe(pBs, &iCode)) != ERR_NONE) return (int32_t)uiRetTmp; //mvd_l0[ mbPartIdx ][ subMbPartIdx ][ compIdx ]
                            iMv[0] = (int16_t)(iMv[0] + iCode);
                            if ((uiRetTmp = (uint32_t)BsGetSe(pBs, &iCode)) != ERR_NONE) return (int32_t)uiRetTmp; //mvd_l1[ mbPartIdx ][ subMbPartIdx ][ compIdx ]
                            iMv[1] = (int16_t)(iMv[1] + iCode);
                            WELS_CHECK_SE_BOTH_WARNING(pCtx, iMv[1], iMinVmv, iMaxVmv, "vertical mv");
                            if (SUB_MB_TYPE_8x8 == uiSubMbType)
                            {
                                ST32(&pCurDqLayer->pDec->pMv[0].p[iMbXy][uiScan4Idx], LD32(iMv));
                                ST32(&pCurDqLayer->pDec->pMv[0].p[iMbXy][uiScan4Idx + 1], LD32(iMv));
                                ST32(&pCurDqLayer->pDec->pMv[0].p[iMbXy][uiScan4Idx + 4], LD32(iMv));
                                ST32(&pCurDqLayer->pDec->pMv[0].p[iMbXy][uiScan4Idx + 5], LD32(iMv));
                                ST32(&iMvArray[0][uiCacheIdx], LD32(iMv));
                                ST32(&iMvArray[0][uiCacheIdx + 1], LD32(iMv));
                                ST32(&iMvArray[0][uiCacheIdx + 6], LD32(iMv));
                                ST32(&iMvArray[0][uiCacheIdx + 7], LD32(iMv));
                            }
                            else if (SUB_MB_TYPE_8x4 == uiSubMbType)
                            {
                                ST32(&pCurDqLayer->pDec->pMv[0].p[iMbXy][uiScan4Idx], LD32(iMv));
                                ST32(&pCurDqLayer->pDec->pMv[0].p[iMbXy][uiScan4Idx + 1], LD32(iMv));
                                ST32(&iMvArray[0][uiCacheIdx], LD32(iMv));
                                ST32(&iMvArray[0][uiCacheIdx + 1], LD32(iMv));
                            }
                            else if (SUB_MB_TYPE_4x8 == uiSubMbType)
                            {
                                ST32(&pCurDqLayer->pDec->pMv[0].p[iMbXy][uiScan4Idx], LD32(iMv));
                                ST32(&pCurDqLayer->pDec->pMv[0].p[iMbXy][uiScan4Idx + 4], LD32(iMv));
                                ST32(&iMvArray[0][uiCacheIdx], LD32(iMv));
                                ST32(&iMvArray[0][uiCacheIdx + 6], LD32(iMv));
                            }
                            else
                            { //SUB_MB_TYPE_4x4 == uiSubMbType
                                ST32(&pCurDqLayer->pDec->pMv[0].p[iMbXy][uiScan4Idx], LD32(iMv));
                                ST32(&iMvArray[0][uiCacheIdx], LD32(iMv));
                            }
                        }
                    }
                }
                break;
            default:
                break;
        }

        return ERR_NONE;
    }

    // ParseInterBInfo: not ported yet (B slices).
}
