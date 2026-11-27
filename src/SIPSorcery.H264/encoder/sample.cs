//-----------------------------------------------------------------------------
// Filename: sample.cs
//
// Description: Port of codec/encoder/core/inc/sample.h and src/sample.cpp:
// the SATD kernels, the combined intra predict-and-cost kernels and
// WelsInitSampleSadFunc. The SAD kernels themselves are the shared ones in
// common/sad_common.cs.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// Upstream's SIMD variants are assembly and are not ported. The port's own
// SIMD SAD and SATD kernels (P5.2) are in sample_simd.cs and
// common/sad_common_simd.cs. Upstream's C path leaves the five Combined3
// pointers NULL, and so does the port on every machine; the encoder only
// calls them when a SIMD build sets them.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 26 Nov 2026  Claude          Created (E2).
// 27 Nov 2026  Claude          The SIMD SAD and SATD kernels in WelsInitSampleSadFunc (P5.2).
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
 *
 *
 * \file    sample.c
 *
 * \brief   compute SAD and SATD
 *
 * \date    2009.06.02 Created
 *
 *************************************************************************************
 */

namespace SIPSorcery.H264.Encoder;

internal static unsafe partial class WelsEnc
{
    // <limits.h>
    public const int32_t INT_MAX = int32_t.MaxValue;

    public static int32_t WelsSampleSatd4x4_c(uint8_t* pSample1, int32_t iStride1, uint8_t* pSample2, int32_t iStride2)
    {
        int32_t iSatdSum = 0;
        int32_t* pSampleMix = stackalloc int32_t[4 * 4]; // int32_t pSampleMix[4][4] = {{ 0 }}; stackalloc zeroes it
        int32_t iSample0, iSample1, iSample2, iSample3;
        int32_t i = 0;
        uint8_t* pSrc1 = pSample1;
        uint8_t* pSrc2 = pSample2;

        //step 1: get the difference
        for (i = 0; i < 4; i++)
        {
            int32_t* pRow = pSampleMix + (i << 2);
            pRow[0] = pSrc1[0] - pSrc2[0];
            pRow[1] = pSrc1[1] - pSrc2[1];
            pRow[2] = pSrc1[2] - pSrc2[2];
            pRow[3] = pSrc1[3] - pSrc2[3];

            pSrc1 += iStride1;
            pSrc2 += iStride2;
        }

        //step 2: horizontal transform
        for (i = 0; i < 4; i++)
        {
            int32_t* pRow = pSampleMix + (i << 2);
            iSample0 = pRow[0] + pRow[2];
            iSample1 = pRow[1] + pRow[3];
            iSample2 = pRow[0] - pRow[2];
            iSample3 = pRow[1] - pRow[3];

            pRow[0] = iSample0 + iSample1;
            pRow[1] = iSample2 + iSample3;
            pRow[2] = iSample2 - iSample3;
            pRow[3] = iSample0 - iSample1;
        }

        //step 3: vertical transform and get the sum of SATD
        for (i = 0; i < 4; i++)
        {
            // pSampleMix[r][i] is pSampleMix[(r << 2) + i].
            iSample0 = pSampleMix[0 + i] + pSampleMix[8 + i];
            iSample1 = pSampleMix[4 + i] + pSampleMix[12 + i];
            iSample2 = pSampleMix[0 + i] - pSampleMix[8 + i];
            iSample3 = pSampleMix[4 + i] - pSampleMix[12 + i];

            pSampleMix[0 + i] = iSample0 + iSample1;
            pSampleMix[4 + i] = iSample2 + iSample3;
            pSampleMix[8 + i] = iSample2 - iSample3;
            pSampleMix[12 + i] = iSample0 - iSample1;

            iSatdSum += (WELS_ABS(pSampleMix[0 + i]) + WELS_ABS(pSampleMix[4 + i]) + WELS_ABS(pSampleMix[8 + i]) + WELS_ABS(
                             pSampleMix[12 + i]));
        }

        return ((iSatdSum + 1) >> 1);
    }

    public static int32_t WelsSampleSatd8x4_c(uint8_t* pSample1, int32_t iStride1, uint8_t* pSample2, int32_t iStride2)
    {
        int32_t iSatdSum = 0;
        iSatdSum += WelsSampleSatd4x4_c(pSample1, iStride1, pSample2, iStride2);
        iSatdSum += WelsSampleSatd4x4_c(pSample1 + 4, iStride1, pSample2 + 4, iStride2);
        return iSatdSum;
    }

    public static int32_t WelsSampleSatd4x8_c(uint8_t* pSample1, int32_t iStride1, uint8_t* pSample2, int32_t iStride2)
    {
        int32_t iSatdSum = 0;
        iSatdSum += WelsSampleSatd4x4_c(pSample1, iStride1, pSample2, iStride2);
        iSatdSum += WelsSampleSatd4x4_c(pSample1 + (iStride1 << 2), iStride1, pSample2 + (iStride2 << 2), iStride2);
        return iSatdSum;
    }

    public static int32_t WelsSampleSatd8x8_c(uint8_t* pSample1, int32_t iStride1, uint8_t* pSample2, int32_t iStride2)
    {
        int32_t iSatdSum = 0;
        iSatdSum += WelsSampleSatd4x4_c(pSample1, iStride1, pSample2, iStride2);
        iSatdSum += WelsSampleSatd4x4_c(pSample1 + 4, iStride1, pSample2 + 4, iStride2);
        iSatdSum += WelsSampleSatd4x4_c(pSample1 + (iStride1 << 2), iStride1, pSample2 + (iStride2 << 2), iStride2);
        iSatdSum += WelsSampleSatd4x4_c(pSample1 + (iStride1 << 2) + 4, iStride1, pSample2 + (iStride2 << 2) + 4, iStride2);
        return iSatdSum;
    }

    public static int32_t WelsSampleSatd16x8_c(uint8_t* pSample1, int32_t iStride1, uint8_t* pSample2, int32_t iStride2)
    {
        int32_t iSatdSum = 0;
        iSatdSum += WelsSampleSatd8x8_c(pSample1, iStride1, pSample2, iStride2);
        iSatdSum += WelsSampleSatd8x8_c(pSample1 + 8, iStride1, pSample2 + 8, iStride2);
        return iSatdSum;
    }

    public static int32_t WelsSampleSatd8x16_c(uint8_t* pSample1, int32_t iStride1, uint8_t* pSample2, int32_t iStride2)
    {
        int32_t iSatdSum = 0;
        iSatdSum += WelsSampleSatd8x8_c(pSample1, iStride1, pSample2, iStride2);
        iSatdSum += WelsSampleSatd8x8_c(pSample1 + (iStride1 << 3), iStride1, pSample2 + (iStride2 << 3), iStride2);
        return iSatdSum;
    }

    public static int32_t WelsSampleSatd16x16_c(uint8_t* pSample1, int32_t iStride1, uint8_t* pSample2, int32_t iStride2)
    {
        int32_t iSatdSum = 0;
        iSatdSum += WelsSampleSatd8x8_c(pSample1, iStride1, pSample2, iStride2);
        iSatdSum += WelsSampleSatd8x8_c(pSample1 + 8, iStride1, pSample2 + 8, iStride2);
        iSatdSum += WelsSampleSatd8x8_c(pSample1 + (iStride1 << 3), iStride1, pSample2 + (iStride2 << 3), iStride2);
        iSatdSum += WelsSampleSatd8x8_c(pSample1 + (iStride1 << 3) + 8, iStride1, pSample2 + (iStride2 << 3) + 8, iStride2);
        return iSatdSum;
    }

    // The extern declarations of the WelsI4x4LumaPred*, WelsIChromaPred* and
    // WelsI16x16LumaPredDc_c predictors resolve to encoder/get_intra_predictor.cs.

    public static int32_t WelsSampleSatdIntra4x4Combined3_c(uint8_t* pDec, int32_t iDecStride, uint8_t* pEnc, int32_t iEncStride,
                                                           uint8_t* pDst,
                                                           int32_t* pBestMode, int32_t iLambda2, int32_t iLambda1, int32_t iLambda0)
    {
        int32_t iBestMode = -1;
        int32_t iCurCost, iBestCost = INT_MAX;

        // ENFORCE_STACK_ALIGN_2D (uint8_t, uiLocalBuffer, 3, 16, 16); uiLocalBuffer[k] is uiLocalBuffer + 16 * k.
        uint8_t* uiLocalBuffer = stackalloc uint8_t[3 * 16];

        WelsI4x4LumaPredDc_c(uiLocalBuffer + 2 * 16, pDec, iDecStride);
        iCurCost = WelsSampleSatd4x4_c(uiLocalBuffer + 2 * 16, 4, pEnc, iEncStride) + iLambda2;
        if (iCurCost < iBestCost)
        {
            iBestMode = 2;
            iBestCost = iCurCost;
        }

        WelsI4x4LumaPredH_c(uiLocalBuffer + 1 * 16, pDec, iDecStride);
        iCurCost = WelsSampleSatd4x4_c(uiLocalBuffer + 1 * 16, 4, pEnc, iEncStride) + iLambda1;
        if (iCurCost < iBestCost)
        {
            iBestMode = 1;
            iBestCost = iCurCost;
        }
        WelsI4x4LumaPredV_c(uiLocalBuffer + 0 * 16, pDec, iDecStride);
        iCurCost = WelsSampleSatd4x4_c(uiLocalBuffer + 0 * 16, 4, pEnc, iEncStride) + iLambda0;
        if (iCurCost < iBestCost)
        {
            iBestMode = 0;
            iBestCost = iCurCost;
        }

        memcpy(pDst, uiLocalBuffer + iBestMode * 16, 16 * sizeof(uint8_t)); // confirmed_safe_unsafe_usage
        *pBestMode = iBestMode;

        return iBestCost;
    }

    public static int32_t WelsSampleSatdIntra8x8Combined3_c(uint8_t* pDecCb, int32_t iDecStride, uint8_t* pEncCb, int32_t iEncStride,
                                                           int32_t* pBestMode, int32_t iLambda, uint8_t* pDstChroma, uint8_t* pDecCr, uint8_t* pEncCr)
    {
        int32_t iBestMode = -1;
        int32_t iCurCost, iBestCost = INT_MAX;

        WelsIChromaPredV_c(pDstChroma, pDecCb, iDecStride);
        WelsIChromaPredV_c(pDstChroma + 64, pDecCr, iDecStride);
        iCurCost = WelsSampleSatd8x8_c(pDstChroma, 8, pEncCb, iEncStride);
        iCurCost += WelsSampleSatd8x8_c(pDstChroma + 64, 8, pEncCr, iEncStride) + iLambda * 2;

        if (iCurCost < iBestCost)
        {
            iBestMode = 2;
            iBestCost = iCurCost;
        }

        WelsIChromaPredH_c(pDstChroma, pDecCb, iDecStride);
        WelsIChromaPredH_c(pDstChroma + 64, pDecCr, iDecStride);
        iCurCost = WelsSampleSatd8x8_c(pDstChroma, 8, pEncCb, iEncStride);
        iCurCost += WelsSampleSatd8x8_c(pDstChroma + 64, 8, pEncCr, iEncStride) + iLambda * 2;
        if (iCurCost < iBestCost)
        {
            iBestMode = 1;
            iBestCost = iCurCost;
        }
        WelsIChromaPredDc_c(pDstChroma, pDecCb, iDecStride);
        WelsIChromaPredDc_c(pDstChroma + 64, pDecCr, iDecStride);
        iCurCost = WelsSampleSatd8x8_c(pDstChroma, 8, pEncCb, iEncStride);
        iCurCost += WelsSampleSatd8x8_c(pDstChroma + 64, 8, pEncCr, iEncStride);
        if (iCurCost < iBestCost)
        {
            iBestMode = 0;
            iBestCost = iCurCost;
        }

        *pBestMode = iBestMode;

        return iBestCost;
    }

    public static int32_t WelsSampleSadIntra8x8Combined3_c(uint8_t* pDecCb, int32_t iDecStride, uint8_t* pEncCb, int32_t iEncStride,
                                                          int32_t* pBestMode, int32_t iLambda, uint8_t* pDstChroma, uint8_t* pDecCr, uint8_t* pEncCr)
    {
        int32_t iBestMode = -1;
        int32_t iCurCost, iBestCost = INT_MAX;

        WelsIChromaPredV_c(pDstChroma, pDecCb, iDecStride);
        WelsIChromaPredV_c(pDstChroma + 64, pDecCr, iDecStride);
        iCurCost = WelsSampleSad8x8_c(pDstChroma, 8, pEncCb, iEncStride);
        iCurCost += WelsSampleSad8x8_c(pDstChroma + 64, 8, pEncCr, iEncStride) + iLambda * 2;

        if (iCurCost < iBestCost)
        {
            iBestMode = 2;
            iBestCost = iCurCost;
        }

        WelsIChromaPredH_c(pDstChroma, pDecCb, iDecStride);
        WelsIChromaPredH_c(pDstChroma + 64, pDecCr, iDecStride);
        iCurCost = WelsSampleSad8x8_c(pDstChroma, 8, pEncCb, iEncStride);
        iCurCost += WelsSampleSad8x8_c(pDstChroma + 64, 8, pEncCr, iEncStride) + iLambda * 2;
        if (iCurCost < iBestCost)
        {
            iBestMode = 1;
            iBestCost = iCurCost;
        }
        WelsIChromaPredDc_c(pDstChroma, pDecCb, iDecStride);
        WelsIChromaPredDc_c(pDstChroma + 64, pDecCr, iDecStride);
        iCurCost = WelsSampleSad8x8_c(pDstChroma, 8, pEncCb, iEncStride);
        iCurCost += WelsSampleSad8x8_c(pDstChroma + 64, 8, pEncCr, iEncStride);
        if (iCurCost < iBestCost)
        {
            iBestMode = 0;
            iBestCost = iCurCost;
        }

        *pBestMode = iBestMode;

        return iBestCost;
    }

    public static int32_t WelsSampleSatdIntra16x16Combined3_c(uint8_t* pDec, int32_t iDecStride, uint8_t* pEnc, int32_t iEncStride,
                                                             int32_t* pBestMode, int32_t iLambda, uint8_t* pDst)
    {
        int32_t iBestMode = -1;
        int32_t iCurCost, iBestCost = INT_MAX;

        WelsI16x16LumaPredV_c(pDst, pDec, iDecStride);
        iCurCost = WelsSampleSatd16x16_c(pDst, 16, pEnc, iEncStride);

        if (iCurCost < iBestCost)
        {
            iBestMode = 0;
            iBestCost = iCurCost;
        }

        WelsI16x16LumaPredH_c(pDst, pDec, iDecStride);
        iCurCost = WelsSampleSatd16x16_c(pDst, 16, pEnc, iEncStride) + iLambda * 2;
        if (iCurCost < iBestCost)
        {
            iBestMode = 1;
            iBestCost = iCurCost;
        }
        WelsI16x16LumaPredDc_c(pDst, pDec, iDecStride);
        iCurCost = WelsSampleSatd16x16_c(pDst, 16, pEnc, iEncStride) + iLambda * 2;
        if (iCurCost < iBestCost)
        {
            iBestMode = 2;
            iBestCost = iCurCost;
        }
        *pBestMode = iBestMode;

        return iBestCost;
    }

    public static int32_t WelsSampleSadIntra16x16Combined3_c(uint8_t* pDec, int32_t iDecStride, uint8_t* pEnc, int32_t iEncStride,
                                                            int32_t* pBestMode, int32_t iLambda, uint8_t* pDst)
    {
        int32_t iBestMode = -1;
        int32_t iCurCost, iBestCost = INT_MAX;

        WelsI16x16LumaPredV_c(pDst, pDec, iDecStride);
        iCurCost = WelsSampleSad16x16_c(pDst, 16, pEnc, iEncStride);

        if (iCurCost < iBestCost)
        {
            iBestMode = 0;
            iBestCost = iCurCost;
        }

        WelsI16x16LumaPredH_c(pDst, pDec, iDecStride);
        iCurCost = WelsSampleSad16x16_c(pDst, 16, pEnc, iEncStride) + iLambda * 2;
        if (iCurCost < iBestCost)
        {
            iBestMode = 1;
            iBestCost = iCurCost;
        }
        WelsI16x16LumaPredDc_c(pDst, pDec, iDecStride);
        iCurCost = WelsSampleSad16x16_c(pDst, 16, pEnc, iEncStride) + iLambda * 2;
        if (iCurCost < iBestCost)
        {
            iBestMode = 2;
            iBestCost = iCurCost;
        }
        *pBestMode = iBestMode;

        return iBestCost;
    }

    public static void WelsInitSampleSadFunc(SWelsFuncPtrList* pFuncList, uint32_t uiCpuFlag)
    {
        //pfSampleSad init
        pFuncList->sSampleDealingFuncs.pfSampleSad[BLOCK_16x16] = &WelsSampleSad16x16_c;
        pFuncList->sSampleDealingFuncs.pfSampleSad[BLOCK_16x8] = &WelsSampleSad16x8_c;
        pFuncList->sSampleDealingFuncs.pfSampleSad[BLOCK_8x16] = &WelsSampleSad8x16_c;
        pFuncList->sSampleDealingFuncs.pfSampleSad[BLOCK_8x8] = &WelsSampleSad8x8_c;
        pFuncList->sSampleDealingFuncs.pfSampleSad[BLOCK_4x4] = &WelsSampleSad4x4_c;
        pFuncList->sSampleDealingFuncs.pfSampleSad[BLOCK_8x4] = &WelsSampleSad8x4_c;
        pFuncList->sSampleDealingFuncs.pfSampleSad[BLOCK_4x8] = &WelsSampleSad4x8_c;

        //pfSampleSatd init
        pFuncList->sSampleDealingFuncs.pfSampleSatd[BLOCK_16x16] = &WelsSampleSatd16x16_c;
        pFuncList->sSampleDealingFuncs.pfSampleSatd[BLOCK_16x8] = &WelsSampleSatd16x8_c;
        pFuncList->sSampleDealingFuncs.pfSampleSatd[BLOCK_8x16] = &WelsSampleSatd8x16_c;
        pFuncList->sSampleDealingFuncs.pfSampleSatd[BLOCK_8x8] = &WelsSampleSatd8x8_c;
        pFuncList->sSampleDealingFuncs.pfSampleSatd[BLOCK_4x4] = &WelsSampleSatd4x4_c;
        pFuncList->sSampleDealingFuncs.pfSampleSatd[BLOCK_8x4] = &WelsSampleSatd8x4_c;
        pFuncList->sSampleDealingFuncs.pfSampleSatd[BLOCK_4x8] = &WelsSampleSatd4x8_c;

        pFuncList->sSampleDealingFuncs.pfSample4Sad[BLOCK_16x16] = &WelsSampleSadFour16x16_c;
        pFuncList->sSampleDealingFuncs.pfSample4Sad[BLOCK_16x8] = &WelsSampleSadFour16x8_c;
        pFuncList->sSampleDealingFuncs.pfSample4Sad[BLOCK_8x16] = &WelsSampleSadFour8x16_c;
        pFuncList->sSampleDealingFuncs.pfSample4Sad[BLOCK_8x8] = &WelsSampleSadFour8x8_c;
        pFuncList->sSampleDealingFuncs.pfSample4Sad[BLOCK_4x4] = &WelsSampleSadFour4x4_c;
        pFuncList->sSampleDealingFuncs.pfSample4Sad[BLOCK_8x4] = &WelsSampleSadFour8x4_c;
        pFuncList->sSampleDealingFuncs.pfSample4Sad[BLOCK_4x8] = &WelsSampleSadFour4x8_c;

        pFuncList->sSampleDealingFuncs.pfIntra4x4Combined3Satd = null;
        pFuncList->sSampleDealingFuncs.pfIntra8x8Combined3Satd = null;
        pFuncList->sSampleDealingFuncs.pfIntra8x8Combined3Sad = null;
        pFuncList->sSampleDealingFuncs.pfIntra16x16Combined3Satd = null;
        pFuncList->sSampleDealingFuncs.pfIntra16x16Combined3Sad = null;

        // In place of the X86_ASM and HAVE_NEON overrides: the SIMD kernels (P5.2), which give
        // the C kernels' results. The Combined3 slots stay NULL. Upstream's SIMD builds fill
        // some of them, which takes the mode decision down another path, and the port is
        // checked against the C path. The MMI, MSA and LSX/LASX overrides have no counterpart.
        if (WelsCpuHasSimd128(uiCpuFlag))
        {
            pFuncList->sSampleDealingFuncs.pfSampleSad[BLOCK_16x16] = &WelsSampleSad16x16_simd;
            pFuncList->sSampleDealingFuncs.pfSampleSad[BLOCK_16x8] = &WelsSampleSad16x8_simd;
            pFuncList->sSampleDealingFuncs.pfSampleSad[BLOCK_8x16] = &WelsSampleSad8x16_simd;
            pFuncList->sSampleDealingFuncs.pfSampleSad[BLOCK_8x8] = &WelsSampleSad8x8_simd;
            pFuncList->sSampleDealingFuncs.pfSampleSad[BLOCK_4x4] = &WelsSampleSad4x4_simd;
            pFuncList->sSampleDealingFuncs.pfSampleSad[BLOCK_8x4] = &WelsSampleSad8x4_simd;
            pFuncList->sSampleDealingFuncs.pfSampleSad[BLOCK_4x8] = &WelsSampleSad4x8_simd;

            pFuncList->sSampleDealingFuncs.pfSampleSatd[BLOCK_16x16] = &WelsSampleSatd16x16_simd;
            pFuncList->sSampleDealingFuncs.pfSampleSatd[BLOCK_16x8] = &WelsSampleSatd16x8_simd;
            pFuncList->sSampleDealingFuncs.pfSampleSatd[BLOCK_8x16] = &WelsSampleSatd8x16_simd;
            pFuncList->sSampleDealingFuncs.pfSampleSatd[BLOCK_8x8] = &WelsSampleSatd8x8_simd;
            pFuncList->sSampleDealingFuncs.pfSampleSatd[BLOCK_4x4] = &WelsSampleSatd4x4_simd;
            pFuncList->sSampleDealingFuncs.pfSampleSatd[BLOCK_8x4] = &WelsSampleSatd8x4_simd;
            pFuncList->sSampleDealingFuncs.pfSampleSatd[BLOCK_4x8] = &WelsSampleSatd4x8_simd;

            pFuncList->sSampleDealingFuncs.pfSample4Sad[BLOCK_16x16] = &WelsSampleSadFour16x16_simd;
            pFuncList->sSampleDealingFuncs.pfSample4Sad[BLOCK_16x8] = &WelsSampleSadFour16x8_simd;
            pFuncList->sSampleDealingFuncs.pfSample4Sad[BLOCK_8x16] = &WelsSampleSadFour8x16_simd;
            pFuncList->sSampleDealingFuncs.pfSample4Sad[BLOCK_8x8] = &WelsSampleSadFour8x8_simd;
            pFuncList->sSampleDealingFuncs.pfSample4Sad[BLOCK_4x4] = &WelsSampleSadFour4x4_simd;
            pFuncList->sSampleDealingFuncs.pfSample4Sad[BLOCK_8x4] = &WelsSampleSadFour8x4_simd;
            pFuncList->sSampleDealingFuncs.pfSample4Sad[BLOCK_4x8] = &WelsSampleSadFour4x8_simd;
        }
    }
}
