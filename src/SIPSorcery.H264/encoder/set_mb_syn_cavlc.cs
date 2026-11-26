//-----------------------------------------------------------------------------
// Filename: set_mb_syn_cavlc.cs
//
// Description: Port of codec/encoder/core/inc/set_mb_syn_cavlc.h and
// src/set_mb_syn_cavlc.cpp: the CAVLC residual block writer, the dynamic
// slicing stash for CAVLC and CABAC, the bitstream position and slice end
// writers, and InitCoeffFunc.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// C kernels only; CavlcParamCal_sse2/sse42 are not ported. CAVLC_BS_WRITE is
// a macro over the caller's bit cache (pBufPtr, uiCurBits, iLeftBits) that
// also changes its length argument, so it takes all four by ref. The CABAC
// stash, pop and position functions are ported, as they only copy state.
// The CABAC writers they sit beside (WelsSpatialWriteMbSynCabac,
// WelsCabacEncodeFlush, WelsCabacEncodeGetPtr) are not: InitCoeffFunc and
// WelsWriteSliceEndSyn throw NotSupportedException for CABAC.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 26 Nov 2026  Claude          Created (E3).
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
 * \file    set_mb_syn_cavlc.h
 *
 * \brief   Seting all syntax elements of mb and decoding residual with cavlc
 *
 * \date    05/19/2009 Created
 *
 *************************************************************************************
 */

using System;
using System.Runtime.CompilerServices;

namespace SIPSorcery.H264.Encoder;

/// <summary>TagCavlcTableItem.</summary>
internal struct SCavlcTableItem
{
    public uint16_t uiBits;
    public uint8_t uiLen;
    public uint8_t uiSuffixLength;
}

internal static unsafe partial class WelsEnc
{
    // enum ECtxBlockCat
    public const int LUMA_DC = 0;
    public const int LUMA_AC = 1;
    public const int LUMA_4x4 = 2;
    public const int CHROMA_DC = 3;
    public const int CHROMA_AC = 4;

    // #define LUMA_DC_AC    0x04
    public const int LUMA_DC_AC = 0x04;

    // const  ALIGNED_DECLARE (uint8_t, g_kuiZeroLeftMap[16], 16) = {
    public static readonly uint8_t* g_kuiZeroLeftMap = StaticTable.Of<uint8_t>(16, [
        0, 1, 2, 3, 4, 5, 6, 7, 7, 7, 7, 7, 7, 7, 7, 7
    ]);


    /*
     *  Exponential Golomb codes encoding routines
     */

    // CAVLC_BS_INIT / CAVLC_BS_UNINIT copy pBs's cache to and from the caller's
    // locals; CAVLC_BS_WRITE writes through them.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void CAVLC_BS_WRITE(ref int32_t n, int32_t v, ref uint8_t* pBufPtr, ref uint32_t uiCurBits, ref int32_t iLeftBits)
    {
        if ((n) < iLeftBits)
        {
            uiCurBits = (uiCurBits << (n)) | (uint32_t)(v);
            iLeftBits -= (n);
        }
        else
        {
            (n) -= iLeftBits;
            uiCurBits = (uiCurBits << iLeftBits) | (uint32_t)((v) >> (n));
            WRITE_BE_32(pBufPtr, uiCurBits);
            pBufPtr += 4;
            uiCurBits = (uint32_t)((v) & ((1 << (n)) - 1));
            iLeftBits = 32 - (n);
        }
    }


    public static int32_t CavlcParamCal_c(int16_t* pCoffLevel, uint8_t* pRun, int16_t* pLevel, int32_t* pTotalCoeff,
                                          int32_t iLastIndex)
    {
        int32_t iTotalZeros = 0;
        int32_t iTotalCoeffs = 0;

        while (iLastIndex >= 0 && pCoffLevel[iLastIndex] == 0)
        {
            --iLastIndex;
        }

        while (iLastIndex >= 0)
        {
            int32_t iCountZero = 0;
            pLevel[iTotalCoeffs] = pCoffLevel[iLastIndex--];

            while (iLastIndex >= 0 && pCoffLevel[iLastIndex] == 0)
            {
                ++iCountZero;
                --iLastIndex;
            }
            iTotalZeros += iCountZero;
            pRun[iTotalCoeffs++] = (uint8_t)iCountZero;
        }
        *pTotalCoeff = iTotalCoeffs;
        return iTotalZeros;
    }

    public static int32_t WriteBlockResidualCavlc(SWelsFuncPtrList* pFuncList, int16_t* pCoffLevel, int32_t iEndIdx,
                                                  int32_t iCalRunLevelFlag,
                                                  int32_t iResidualProperty, int8_t iNC, SBitStringAux* pBs)
    {
        int16_t* iLevel = stackalloc int16_t[16];
        uint8_t* uiRun = stackalloc uint8_t[16];

        int32_t iTotalCoeffs = 0;
        int32_t iTrailingOnes = 0;
        int32_t iTotalZeros = 0, iZerosLeft = 0;
        uint32_t uiSign = 0;
        int32_t iLevelCode = 0, iLevelPrefix = 0, iLevelSuffix = 0, uiSuffixLength = 0, iLevelSuffixSize = 0;
        int32_t iValue = 0, iThreshold, iZeroLeft;
        int32_t n = 0;
        int32_t i = 0;


        // CAVLC_BS_INIT (pBs);
        uint8_t* pBufPtr = pBs->pCurBuf;
        uint32_t uiCurBits = pBs->uiCurBits;
        int32_t iLeftBits = pBs->iLeftBits;

        /*Step 1: calculate iLevel and iRun and total */

        if (iCalRunLevelFlag != 0)
        {
            int32_t iCount = 0;
            iTotalZeros = pFuncList->pfCavlcParamCal(pCoffLevel, uiRun, iLevel, &iTotalCoeffs, iEndIdx);
            iCount = (iTotalCoeffs > 3) ? 3 : iTotalCoeffs;
            for (i = 0; i < iCount; i++)
            {
                if (WELS_ABS(iLevel[i]) == 1)
                {
                    iTrailingOnes++;
                    uiSign <<= 1;
                    if (iLevel[i] < 0)
                        uiSign |= 1;
                }
                else
                {
                    break;

                }
            }
        }
        /*Step 3: coeff token */
        uint8_t* upCoeffToken = (uint8_t*)&g_kuiVlcCoeffToken[g_kuiEncNcMapTable[iNC]][iTotalCoeffs][iTrailingOnes];
        iValue = upCoeffToken[0];
        n = upCoeffToken[1];

        if (iTotalCoeffs == 0)
        {
            CAVLC_BS_WRITE(ref n, iValue, ref pBufPtr, ref uiCurBits, ref iLeftBits);

            // CAVLC_BS_UNINIT (pBs);
            pBs->pCurBuf = pBufPtr;
            pBs->uiCurBits = uiCurBits;
            pBs->iLeftBits = iLeftBits;
            return ENC_RETURN_SUCCESS;
        }

        /* Step 4: */
        /*  trailing */
        n += iTrailingOnes;
        iValue = (iValue << iTrailingOnes) + (int32_t)uiSign;
        CAVLC_BS_WRITE(ref n, iValue, ref pBufPtr, ref uiCurBits, ref iLeftBits);

        /*  levels */
        uiSuffixLength = (iTotalCoeffs > 10 && iTrailingOnes < 3) ? 1 : 0;

        for (i = iTrailingOnes; i < iTotalCoeffs; i++)
        {
            int32_t iVal = iLevel[i];

            iLevelCode = (iVal - 1) * (1 << 1);
            uiSign = (uint32_t)(iLevelCode >> 31);
            iLevelCode = (int32_t)(((uint32_t)iLevelCode ^ uiSign) + (uiSign << 1));
            iLevelCode -= ((i == iTrailingOnes) && (iTrailingOnes < 3) ? 1 : 0) << 1;

            iLevelPrefix = iLevelCode >> uiSuffixLength;
            iLevelSuffixSize = uiSuffixLength;
            iLevelSuffix = iLevelCode - (iLevelPrefix << uiSuffixLength);

            if (iLevelPrefix >= 14 && iLevelPrefix < 30 && uiSuffixLength == 0)
            {
                iLevelPrefix = 14;
                iLevelSuffix = iLevelCode - iLevelPrefix;
                iLevelSuffixSize = 4;
            }
            else if (iLevelPrefix >= 15)
            {
                iLevelPrefix = 15;
                iLevelSuffix = iLevelCode - (iLevelPrefix << uiSuffixLength);
                //for baseline profile,overflow when the length of iLevelSuffix is larger than 11.
                if ((iLevelSuffix >> 11) != 0)
                    return ENC_RETURN_VLCOVERFLOWFOUND;
                if (uiSuffixLength == 0)
                {
                    iLevelSuffix -= 15;
                }
                iLevelSuffixSize = 12;
            }

            n = iLevelPrefix + 1 + iLevelSuffixSize;
            iValue = ((1 << iLevelSuffixSize) | iLevelSuffix);
            CAVLC_BS_WRITE(ref n, iValue, ref pBufPtr, ref uiCurBits, ref iLeftBits);

            uiSuffixLength += uiSuffixLength == 0 ? 1 : 0;
            iThreshold = 3 << (uiSuffixLength - 1);
            uiSuffixLength += ((iVal > iThreshold) || (iVal < -iThreshold)) && (uiSuffixLength < 6) ? 1 : 0;

        }

        /* Step 5: total zeros */

        if (iTotalCoeffs < iEndIdx + 1)
        {
            if (CHROMA_DC != iResidualProperty)
            {
                uint8_t* upTotalZeros = (uint8_t*)&g_kuiVlcTotalZeros[iTotalCoeffs][iTotalZeros];
                n = upTotalZeros[1];
                iValue = upTotalZeros[0];
                CAVLC_BS_WRITE(ref n, iValue, ref pBufPtr, ref uiCurBits, ref iLeftBits);
            }
            else
            {
                uint8_t* upTotalZeros = (uint8_t*)&g_kuiVlcTotalZerosChromaDc[iTotalCoeffs][iTotalZeros];
                n = upTotalZeros[1];
                iValue = upTotalZeros[0];
                CAVLC_BS_WRITE(ref n, iValue, ref pBufPtr, ref uiCurBits, ref iLeftBits);
            }
        }

        /* Step 6: pRun before */
        iZerosLeft = iTotalZeros;
        for (i = 0; i + 1 < iTotalCoeffs && iZerosLeft > 0; ++i)
        {
            uint8_t uirun = uiRun[i];
            iZeroLeft = g_kuiZeroLeftMap[iZerosLeft];
            n = g_kuiVlcRunBefore[iZeroLeft][uirun][1];
            iValue = g_kuiVlcRunBefore[iZeroLeft][uirun][0];
            CAVLC_BS_WRITE(ref n, iValue, ref pBufPtr, ref uiCurBits, ref iLeftBits);
            iZerosLeft -= uirun;
        }

        // CAVLC_BS_UNINIT (pBs);
        pBs->pCurBuf = pBufPtr;
        pBs->uiCurBits = uiCurBits;
        pBs->iLeftBits = iLeftBits;
        return ENC_RETURN_SUCCESS;
    }

    public static void StashMBStatusCavlc(SDynamicSlicingStack* pDss, SSlice* pSlice, int32_t iMbSkipRun)
    {
        SBitStringAux* pBs = pSlice->pSliceBsa;
        pDss->pBsStackBufPtr = pBs->pCurBuf;
        pDss->uiBsStackCurBits = pBs->uiCurBits;
        pDss->iBsStackLeftBits = pBs->iLeftBits;
        pDss->uiLastMbQp = pSlice->uiLastMbQp;
        pDss->iMbSkipRunStack = iMbSkipRun;
    }
    public static int32_t StashPopMBStatusCavlc(SDynamicSlicingStack* pDss, SSlice* pSlice)
    {
        SBitStringAux* pBs = pSlice->pSliceBsa;
        pBs->pCurBuf = pDss->pBsStackBufPtr;
        pBs->uiCurBits = pDss->uiBsStackCurBits;
        pBs->iLeftBits = pDss->iBsStackLeftBits;
        pSlice->uiLastMbQp = pDss->uiLastMbQp;
        return pDss->iMbSkipRunStack;
    }
    public static void StashMBStatusCabac(SDynamicSlicingStack* pDss, SSlice* pSlice, int32_t iMbSkipRun)
    {
        SCabacCtx* pCtx = &pSlice->sCabacCtx;
        CRuntime.memcpy(&pDss->sStoredCabac, pCtx, sizeof(SCabacCtx));
        if (pDss->pRestoreBuffer != null)
        {
            int32_t iPosBitOffset = GetBsPosCabac(pSlice) - pDss->iStartPos;
            int32_t iLen = ((iPosBitOffset >> 3) + ((iPosBitOffset & 0x07) != 0 ? 1 : 0));
            CRuntime.memcpy(pDss->pRestoreBuffer, pCtx->m_pBufStart, iLen);
        }
        pDss->uiLastMbQp = pSlice->uiLastMbQp;
        pDss->iMbSkipRunStack = iMbSkipRun;
    }
    public static int32_t StashPopMBStatusCabac(SDynamicSlicingStack* pDss, SSlice* pSlice)
    {
        SCabacCtx* pCtx = &pSlice->sCabacCtx;
        CRuntime.memcpy(pCtx, &pDss->sStoredCabac, sizeof(SCabacCtx));
        if (pDss->pRestoreBuffer != null)
        {
            int32_t iPosBitOffset = GetBsPosCabac(pSlice) - pDss->iStartPos;
            int32_t iLen = ((iPosBitOffset >> 3) + ((iPosBitOffset & 0x07) != 0 ? 1 : 0));
            CRuntime.memcpy(pCtx->m_pBufStart, pDss->pRestoreBuffer, iLen);
        }
        pSlice->uiLastMbQp = pDss->uiLastMbQp;
        return pDss->iMbSkipRunStack;
    }
    public static int32_t GetBsPosCavlc(SSlice* pSlice)
    {
        return BsGetBitsPos(pSlice->pSliceBsa);
    }
    public static int32_t GetBsPosCabac(SSlice* pSlice)
    {
        return (int32_t)((pSlice->sCabacCtx.m_pBufCur - pSlice->sCabacCtx.m_pBufStart) << 3) +
               (pSlice->sCabacCtx.m_iLowBitCnt - 9);
    }
    public static void WelsWriteSliceEndSyn(SSlice* pSlice, bool bEntropyCodingModeFlag)
    {
        SBitStringAux* pBs = pSlice->pSliceBsa;
        if (bEntropyCodingModeFlag)
        {
            // WelsCabacEncodeFlush (&pSlice->sCabacCtx);
            // pBs->pCurBuf = WelsCabacEncodeGetPtr (&pSlice->sCabacCtx);
            throw new NotSupportedException("WelsWriteSliceEndSyn: CABAC encoding is not supported by this port.");
        }
        else
        {
            BsRbspTrailingBits(pBs);
            BsFlush(pBs);
        }
    }
    public static void InitCoeffFunc(SWelsFuncPtrList* pFuncList, uint32_t uiCpuFlag, int32_t iEntropyCodingModeFlag)
    {
        pFuncList->pfCavlcParamCal = &CavlcParamCal_c;

        if (iEntropyCodingModeFlag != 0)
        {
            // pFuncList->pfStashMBStatus = StashMBStatusCabac;
            // pFuncList->pfStashPopMBStatus = StashPopMBStatusCabac;
            // pFuncList->pfWelsSpatialWriteMbSyn = WelsSpatialWriteMbSynCabac;
            // pFuncList->pfGetBsPosition = GetBsPosCabac;
            throw new NotSupportedException("InitCoeffFunc: CABAC encoding is not supported by this port.");
        }
        else
        {
            pFuncList->pfStashMBStatus = &StashMBStatusCavlc;
            pFuncList->pfStashPopMBStatus = &StashPopMBStatusCavlc;
            pFuncList->pfWelsSpatialWriteMbSyn = &WelsSpatialWriteMbSyn;
            pFuncList->pfGetBsPosition = &GetBsPosCavlc;
        }
    }
}
