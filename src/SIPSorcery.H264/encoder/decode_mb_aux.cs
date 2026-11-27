//-----------------------------------------------------------------------------
// Filename: decode_mb_aux.cs
//
// Description: Port of codec/encoder/core/inc/decode_mb_aux.h and
// src/decode_mb_aux.cpp: the encoder's reconstruction kernels, the inverse
// Hadamard transforms, dequantisation and the 4x4 inverse DCT that adds the
// prediction, plus WelsGetEncBlockStrideOffset and WelsInitReconstructionFuncs.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// C kernels only; upstream's SIMD variants are assembly and not ported.
// WelsInitReconstructionFuncs chooses the port's own, in decode_mb_aux_simd.cs,
// where WelsCpuHasSimd128 holds. The C keeps the intermediates in int16_t
// (iTemp, kiSumU, ...) and dequantises in place with `*=` on int16_t, so every
// store into one is an (int16_t) cast here.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 26 Nov 2026  Claude          Created (E2).
// 27 Nov 2026  Claude          The SIMD kernels in WelsInitReconstructionFuncs (P5.5).
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

namespace SIPSorcery.H264.Encoder;

internal static unsafe partial class WelsEnc
{
    /****************************************************************************
     * Dequant and Ihdm functions
     ****************************************************************************/
    public static void WelsIHadamard4x4Dc(int16_t* pRes)
    { //pBuffer size : 4x4
        int16_t* iTemp = stackalloc int16_t[4];
        int32_t i = 4;

        while (--i >= 0)
        {
            int32_t kiIdx = i << 2;
            int32_t kiIdx1 = 1 + kiIdx;
            int32_t kiIdx2 = 1 + kiIdx1;
            int32_t kiIdx3 = 1 + kiIdx2;

            iTemp[0] = (int16_t)(pRes[kiIdx] + pRes[kiIdx2]);
            iTemp[1] = (int16_t)(pRes[kiIdx] - pRes[kiIdx2]);
            iTemp[2] = (int16_t)(pRes[kiIdx1] - pRes[kiIdx3]);
            iTemp[3] = (int16_t)(pRes[kiIdx1] + pRes[kiIdx3]);

            pRes[kiIdx] = (int16_t)(iTemp[0] + iTemp[3]);
            pRes[kiIdx1] = (int16_t)(iTemp[1] + iTemp[2]);
            pRes[kiIdx2] = (int16_t)(iTemp[1] - iTemp[2]);
            pRes[kiIdx3] = (int16_t)(iTemp[0] - iTemp[3]);
        }

        i = 4;
        while (--i >= 0)
        {
            int32_t kiI4 = 4 + i;
            int32_t kiI8 = 4 + kiI4;
            int32_t kiI12 = 4 + kiI8;

            iTemp[0] = (int16_t)(pRes[i] + pRes[kiI8]);
            iTemp[1] = (int16_t)(pRes[i] - pRes[kiI8]);
            iTemp[2] = (int16_t)(pRes[kiI4] - pRes[kiI12]);
            iTemp[3] = (int16_t)(pRes[kiI4] + pRes[kiI12]);

            pRes[i] = (int16_t)(iTemp[0] + iTemp[3]);
            pRes[kiI4] = (int16_t)(iTemp[1] + iTemp[2]);
            pRes[kiI8] = (int16_t)(iTemp[1] - iTemp[2]);
            pRes[kiI12] = (int16_t)(iTemp[0] - iTemp[3]);
        }
    }

    /* for qp < 12 */
    public static void WelsDequantLumaDc4x4(int16_t* pRes, int32_t kiQp)
    {
        int32_t i = 15;
        uint16_t kuiDequantValue = g_kuiDequantCoeff[kiQp % 6][0];
        int16_t kiQF0 = (int16_t)(kiQp / 6);
        int16_t kiQF1 = (int16_t)(2 - kiQF0);
        int16_t kiQF0S = (int16_t)(1 << (1 - kiQF0));

        while (i >= 0)
        {
            pRes[i] = (int16_t)((pRes[i] * kuiDequantValue + kiQF0S) >> kiQF1);
            pRes[i - 1] = (int16_t)((pRes[i - 1] * kuiDequantValue + kiQF0S) >> kiQF1);
            pRes[i - 2] = (int16_t)((pRes[i - 2] * kuiDequantValue + kiQF0S) >> kiQF1);
            pRes[i - 3] = (int16_t)((pRes[i - 3] * kuiDequantValue + kiQF0S) >> kiQF1);
            i -= 4;
        }
    }

    /* for qp >= 12 */
    public static void WelsDequantIHadamard4x4_c(int16_t* pRes, uint16_t kuiMF)
    {
        int16_t* iTemp = stackalloc int16_t[4];
        int32_t i;

        for (i = 0; i < 16; i += 4)
        {
            iTemp[0] = (int16_t)(pRes[i] + pRes[i + 2]);
            iTemp[1] = (int16_t)(pRes[i] - pRes[i + 2]);
            iTemp[2] = (int16_t)(pRes[i + 1] - pRes[i + 3]);
            iTemp[3] = (int16_t)(pRes[i + 1] + pRes[i + 3]);

            pRes[i] = (int16_t)(iTemp[0] + iTemp[3]);
            pRes[i + 1] = (int16_t)(iTemp[1] + iTemp[2]);
            pRes[i + 2] = (int16_t)(iTemp[1] - iTemp[2]);
            pRes[i + 3] = (int16_t)(iTemp[0] - iTemp[3]);
        }

        for (i = 0; i < 4; i++)
        {
            iTemp[0] = (int16_t)(pRes[i] + pRes[i + 8]);
            iTemp[1] = (int16_t)(pRes[i] - pRes[i + 8]);
            iTemp[2] = (int16_t)(pRes[i + 4] - pRes[i + 12]);
            iTemp[3] = (int16_t)(pRes[i + 4] + pRes[i + 12]);

            pRes[i] = (int16_t)((iTemp[0] + iTemp[3]) * kuiMF);
            pRes[i + 4] = (int16_t)((iTemp[1] + iTemp[2]) * kuiMF);
            pRes[i + 8] = (int16_t)((iTemp[1] - iTemp[2]) * kuiMF);
            pRes[i + 12] = (int16_t)((iTemp[0] - iTemp[3]) * kuiMF);
        }
    }

    public static void WelsDequantIHadamard2x2Dc(int16_t* pDct, uint16_t kuiMF)
    {
        int16_t kiSumU = (int16_t)(pDct[0] + pDct[2]);
        int16_t kiDelU = (int16_t)(pDct[0] - pDct[2]);
        int16_t kiSumD = (int16_t)(pDct[1] + pDct[3]);
        int16_t kiDelD = (int16_t)(pDct[1] - pDct[3]);

        pDct[0] = (int16_t)(((kiSumU + kiSumD) * kuiMF) >> 1);
        pDct[1] = (int16_t)(((kiSumU - kiSumD) * kuiMF) >> 1);
        pDct[2] = (int16_t)(((kiDelU + kiDelD) * kuiMF) >> 1);
        pDct[3] = (int16_t)(((kiDelU - kiDelD) * kuiMF) >> 1);
    }

    public static void WelsDequant4x4_c(int16_t* pRes, uint16_t* kpMF)
    {
        int32_t i;
        for (i = 0; i < 8; i++)
        {
            pRes[i] = (int16_t)(pRes[i] * kpMF[i]);
            pRes[i + 8] = (int16_t)(pRes[i + 8] * kpMF[i]);
        }
    }

    public static void WelsDequantFour4x4_c(int16_t* pRes, uint16_t* kpMF)
    {
        int32_t i;
        for (i = 0; i < 8; i++)
        {
            pRes[i] = (int16_t)(pRes[i] * kpMF[i]);
            pRes[i + 8] = (int16_t)(pRes[i + 8] * kpMF[i]);
            pRes[i + 16] = (int16_t)(pRes[i + 16] * kpMF[i]);
            pRes[i + 24] = (int16_t)(pRes[i + 24] * kpMF[i]);
            pRes[i + 32] = (int16_t)(pRes[i + 32] * kpMF[i]);
            pRes[i + 40] = (int16_t)(pRes[i + 40] * kpMF[i]);
            pRes[i + 48] = (int16_t)(pRes[i + 48] * kpMF[i]);
            pRes[i + 56] = (int16_t)(pRes[i + 56] * kpMF[i]);
        }
    }

    /****************************************************************************
     * IDCT functions, final output = prediction(CS) + IDCT(scaled_coeff)
     ****************************************************************************/
    public static void WelsIDctT4Rec_c(uint8_t* pRec, int32_t iStride, uint8_t* pPred, int32_t iPredStride, int16_t* pDct)
    {
        int32_t i;
        int16_t* iTemp = stackalloc int16_t[16];

        int32_t iDstStridex2 = iStride << 1;
        int32_t iDstStridex3 = iStride + iDstStridex2;
        int32_t iPredStridex2 = iPredStride << 1;
        int32_t iPredStridex3 = iPredStride + iPredStridex2;

        for (i = 0; i < 4; i++)
        { //horizon
            int32_t iIdx = i << 2;
            int32_t kiHorSumU = pDct[iIdx] + pDct[iIdx + 2]; // add 0-2
            int32_t kiHorDelU = pDct[iIdx] - pDct[iIdx + 2]; // sub 0-2
            int32_t kiHorSumD = pDct[iIdx + 1] + (pDct[iIdx + 3] >> 1);
            int32_t kiHorDelD = (pDct[iIdx + 1] >> 1) - pDct[iIdx + 3];

            iTemp[iIdx] = (int16_t)(kiHorSumU + kiHorSumD);
            iTemp[iIdx + 1] = (int16_t)(kiHorDelU + kiHorDelD);
            iTemp[iIdx + 2] = (int16_t)(kiHorDelU - kiHorDelD);
            iTemp[iIdx + 3] = (int16_t)(kiHorSumU - kiHorSumD);
        }

        for (i = 0; i < 4; i++)
        { //vertical
            int32_t kiVerSumL = iTemp[i] + iTemp[8 + i];
            int32_t kiVerDelL = iTemp[i] - iTemp[8 + i];
            int32_t kiVerDelR = (iTemp[4 + i] >> 1) - iTemp[12 + i];
            int32_t kiVerSumR = iTemp[4 + i] + (iTemp[12 + i] >> 1);

            pRec[i] = WelsClip1(pPred[i] + ((kiVerSumL + kiVerSumR + 32) >> 6));
            pRec[iStride + i] = WelsClip1(pPred[iPredStride + i] + ((kiVerDelL + kiVerDelR + 32) >> 6));
            pRec[iDstStridex2 + i] = WelsClip1(pPred[iPredStridex2 + i] + ((kiVerDelL - kiVerDelR + 32) >> 6));
            pRec[iDstStridex3 + i] = WelsClip1(pPred[iPredStridex3 + i] + ((kiVerSumL - kiVerSumR + 32) >> 6));
        }
    }

    public static void WelsIDctFourT4Rec_c(uint8_t* pRec, int32_t iStride, uint8_t* pPred, int32_t iPredStride, int16_t* pDct)
    {
        int32_t iDstStridex4 = iStride << 2;
        int32_t iPredStridex4 = iPredStride << 2;
        WelsIDctT4Rec_c(pRec, iStride, pPred, iPredStride, pDct);
        WelsIDctT4Rec_c(&pRec[4], iStride, &pPred[4], iPredStride, pDct + 16);
        WelsIDctT4Rec_c(&pRec[iDstStridex4], iStride, &pPred[iPredStridex4], iPredStride, pDct + 32);
        WelsIDctT4Rec_c(&pRec[iDstStridex4 + 4], iStride, &pPred[iPredStridex4 + 4], iPredStride, pDct + 48);
    }

    public static void WelsIDctT4RecOnMb(uint8_t* pDst, int32_t iDstStride, uint8_t* pPred, int32_t iPredStride, int16_t* pDct,
                                         PIDctFunc pfIDctFourT4)
    {
        int32_t iDstStridex8 = iDstStride << 3;
        int32_t iPredStridex8 = iPredStride << 3;

        pfIDctFourT4(&pDst[0], iDstStride, &pPred[0], iPredStride, pDct);
        pfIDctFourT4(&pDst[8], iDstStride, &pPred[8], iPredStride, pDct + 64);
        pfIDctFourT4(&pDst[iDstStridex8], iDstStride, &pPred[iPredStridex8], iPredStride, pDct + 128);
        pfIDctFourT4(&pDst[iDstStridex8 + 8], iDstStride, &pPred[iPredStridex8 + 8], iPredStride, pDct + 192);
    }

    /*
     * pfIDctI16x16Dc: do luma idct of an MB for I16x16 mode, when only dc value are non-zero
     */
    public static void WelsIDctRecI16x16Dc_c(uint8_t* pRec, int32_t iStride, uint8_t* pPred, int32_t iPredStride, int16_t* pDctDc)
    {
        int32_t i, j;

        for (i = 0; i < 16; i++)
        {
            for (j = 0; j < 16; j++)
            {
                pRec[j] = WelsClip1(pPred[j] + ((pDctDc[(i & 0x0C) + (j >> 2)] + 32) >> 6));
            }
            pRec += iStride;
            pPred += iPredStride;
        }
    }

    public static void WelsGetEncBlockStrideOffset(int32_t* pBlock, int32_t kiStrideY, int32_t kiStrideUV)
    {
        int32_t i, j, k, r;
        for (j = 0; j < 4; j++)
        {
            i = j << 2;
            k = (j & 0x01) << 1;
            r = j & 0x02;

            pBlock[i] = (0 + k + (0 + r) * kiStrideY) << 2;
            pBlock[i + 1] = (1 + k + (0 + r) * kiStrideY) << 2;
            pBlock[i + 2] = (0 + k + (1 + r) * kiStrideY) << 2;
            pBlock[i + 3] = (1 + k + (1 + r) * kiStrideY) << 2;

            pBlock[16 + j] =
                pBlock[20 + j] = ((j & 0x01) + r * kiStrideUV) << 2;
        }
    }

    public static void WelsInitReconstructionFuncs(SWelsFuncPtrList* pFuncList, uint32_t uiCpuFlag)
    {
        pFuncList->pfDequantization4x4 = &WelsDequant4x4_c;
        pFuncList->pfDequantizationFour4x4 = &WelsDequantFour4x4_c;
        pFuncList->pfDequantizationIHadamard4x4 = &WelsDequantIHadamard4x4_c;

        pFuncList->pfIDctT4 = &WelsIDctT4Rec_c;
        pFuncList->pfIDctFourT4 = &WelsIDctFourT4Rec_c;
        pFuncList->pfIDctI16x16Dc = &WelsIDctRecI16x16Dc_c;

        // In place of the X86_ASM and NEON overrides, the kernels in decode_mb_aux_simd.cs.
        // MMI, MSA and LSX/LASX: not ported.
        if (WelsCpuHasSimd128(uiCpuFlag))
        {
            pFuncList->pfDequantization4x4 = &WelsDequant4x4_simd;
            pFuncList->pfDequantizationFour4x4 = &WelsDequantFour4x4_simd;
            pFuncList->pfDequantizationIHadamard4x4 = &WelsDequantIHadamard4x4_simd;

            pFuncList->pfIDctT4 = &WelsIDctT4Rec_simd;
            pFuncList->pfIDctFourT4 = &WelsIDctFourT4Rec_simd;
            pFuncList->pfIDctI16x16Dc = &WelsIDctRecI16x16Dc_simd;
        }
    }
}
