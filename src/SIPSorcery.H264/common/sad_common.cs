//-----------------------------------------------------------------------------
// Filename: sad_common.cs
//
// Description: Port of:
//  - codec/common/inc/sad_common.h
//  - codec/common/src/sad_common.cpp
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// Sum of absolute differences at each partition size, and the "four" variants
// that score the four one-sample neighbours (up, down, left, right) of a
// reference position in one call. The encoder's motion search uses them; the
// decoder does not. C kernels only; upstream's SIMD variants are assembly and
// are not ported. The port's own SIMD kernels (P5.2) are in sad_common_simd.cs.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 24 Nov 2026  Claude          Created.
// 27 Nov 2026  Claude          Pointed at the SIMD versions (P5.2).
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
 * \file    sample.c
 *
 * \brief   compute SAD and SATD
 *
 * \date    2009.06.02 Created
 *
 *************************************************************************************
 */

namespace SIPSorcery.H264;

internal static unsafe partial class WelsCommon
{
    public static int32_t WelsSampleSad4x4_c(uint8_t* pSample1, int32_t iStride1, uint8_t* pSample2, int32_t iStride2)
    {
        int32_t iSadSum = 0;
        int32_t i = 0;
        uint8_t* pSrc1 = pSample1;
        uint8_t* pSrc2 = pSample2;
        for (i = 0; i < 4; i++)
        {
            iSadSum += WELS_ABS((pSrc1[0] - pSrc2[0]));
            iSadSum += WELS_ABS((pSrc1[1] - pSrc2[1]));
            iSadSum += WELS_ABS((pSrc1[2] - pSrc2[2]));
            iSadSum += WELS_ABS((pSrc1[3] - pSrc2[3]));

            pSrc1 += iStride1;
            pSrc2 += iStride2;
        }

        return iSadSum;
    }

    public static int32_t WelsSampleSad8x4_c(uint8_t* pSample1, int32_t iStride1, uint8_t* pSample2, int32_t iStride2)
    {
        int32_t iSadSum = 0;
        iSadSum += WelsSampleSad4x4_c(pSample1, iStride1, pSample2, iStride2);
        iSadSum += WelsSampleSad4x4_c(pSample1 + 4, iStride1, pSample2 + 4, iStride2);
        return iSadSum;
    }

    public static int32_t WelsSampleSad4x8_c(uint8_t* pSample1, int32_t iStride1, uint8_t* pSample2, int32_t iStride2)
    {
        int32_t iSadSum = 0;
        iSadSum += WelsSampleSad4x4_c(pSample1, iStride1, pSample2, iStride2);
        iSadSum += WelsSampleSad4x4_c(pSample1 + (iStride1 << 2), iStride1, pSample2 + (iStride2 << 2), iStride2);
        return iSadSum;
    }

    public static int32_t WelsSampleSad8x8_c(uint8_t* pSample1, int32_t iStride1, uint8_t* pSample2, int32_t iStride2)
    {
        int32_t iSadSum = 0;
        int32_t i = 0;
        uint8_t* pSrc1 = pSample1;
        uint8_t* pSrc2 = pSample2;
        for (i = 0; i < 8; i++)
        {
            iSadSum += WELS_ABS((pSrc1[0] - pSrc2[0]));
            iSadSum += WELS_ABS((pSrc1[1] - pSrc2[1]));
            iSadSum += WELS_ABS((pSrc1[2] - pSrc2[2]));
            iSadSum += WELS_ABS((pSrc1[3] - pSrc2[3]));
            iSadSum += WELS_ABS((pSrc1[4] - pSrc2[4]));
            iSadSum += WELS_ABS((pSrc1[5] - pSrc2[5]));
            iSadSum += WELS_ABS((pSrc1[6] - pSrc2[6]));
            iSadSum += WELS_ABS((pSrc1[7] - pSrc2[7]));

            pSrc1 += iStride1;
            pSrc2 += iStride2;
        }

        return iSadSum;
    }

    public static int32_t WelsSampleSad16x8_c(uint8_t* pSample1, int32_t iStride1, uint8_t* pSample2, int32_t iStride2)
    {
        int32_t iSadSum = 0;

        iSadSum += WelsSampleSad8x8_c(pSample1, iStride1, pSample2, iStride2);
        iSadSum += WelsSampleSad8x8_c(pSample1 + 8, iStride1, pSample2 + 8, iStride2);

        return iSadSum;
    }

    public static int32_t WelsSampleSad8x16_c(uint8_t* pSample1, int32_t iStride1, uint8_t* pSample2, int32_t iStride2)
    {
        int32_t iSadSum = 0;
        iSadSum += WelsSampleSad8x8_c(pSample1, iStride1, pSample2, iStride2);
        iSadSum += WelsSampleSad8x8_c(pSample1 + (iStride1 << 3), iStride1, pSample2 + (iStride2 << 3), iStride2);

        return iSadSum;
    }

    public static int32_t WelsSampleSad16x16_c(uint8_t* pSample1, int32_t iStride1, uint8_t* pSample2, int32_t iStride2)
    {
        int32_t iSadSum = 0;
        iSadSum += WelsSampleSad8x8_c(pSample1, iStride1, pSample2, iStride2);
        iSadSum += WelsSampleSad8x8_c(pSample1 + 8, iStride1, pSample2 + 8, iStride2);
        iSadSum += WelsSampleSad8x8_c(pSample1 + (iStride1 << 3), iStride1, pSample2 + (iStride2 << 3), iStride2);
        iSadSum += WelsSampleSad8x8_c(pSample1 + (iStride1 << 3) + 8, iStride1, pSample2 + (iStride2 << 3) + 8, iStride2);

        return iSadSum;
    }

    public static void WelsSampleSadFour16x16_c(uint8_t* iSample1, int32_t iStride1, uint8_t* iSample2, int32_t iStride2,
                                                int32_t* pSad)
    {
        *(pSad) = WelsSampleSad16x16_c(iSample1, iStride1, (iSample2 - iStride2), iStride2);
        *(pSad + 1) = WelsSampleSad16x16_c(iSample1, iStride1, (iSample2 + iStride2), iStride2);
        *(pSad + 2) = WelsSampleSad16x16_c(iSample1, iStride1, (iSample2 - 1), iStride2);
        *(pSad + 3) = WelsSampleSad16x16_c(iSample1, iStride1, (iSample2 + 1), iStride2);
    }

    public static void WelsSampleSadFour16x8_c(uint8_t* iSample1, int32_t iStride1, uint8_t* iSample2, int32_t iStride2, int32_t* pSad)
    {
        *(pSad) = WelsSampleSad16x8_c(iSample1, iStride1, (iSample2 - iStride2), iStride2);
        *(pSad + 1) = WelsSampleSad16x8_c(iSample1, iStride1, (iSample2 + iStride2), iStride2);
        *(pSad + 2) = WelsSampleSad16x8_c(iSample1, iStride1, (iSample2 - 1), iStride2);
        *(pSad + 3) = WelsSampleSad16x8_c(iSample1, iStride1, (iSample2 + 1), iStride2);
    }

    public static void WelsSampleSadFour8x16_c(uint8_t* iSample1, int32_t iStride1, uint8_t* iSample2, int32_t iStride2, int32_t* pSad)
    {
        *(pSad) = WelsSampleSad8x16_c(iSample1, iStride1, (iSample2 - iStride2), iStride2);
        *(pSad + 1) = WelsSampleSad8x16_c(iSample1, iStride1, (iSample2 + iStride2), iStride2);
        *(pSad + 2) = WelsSampleSad8x16_c(iSample1, iStride1, (iSample2 - 1), iStride2);
        *(pSad + 3) = WelsSampleSad8x16_c(iSample1, iStride1, (iSample2 + 1), iStride2);

    }

    public static void WelsSampleSadFour8x8_c(uint8_t* iSample1, int32_t iStride1, uint8_t* iSample2, int32_t iStride2, int32_t* pSad)
    {
        *(pSad) = WelsSampleSad8x8_c(iSample1, iStride1, (iSample2 - iStride2), iStride2);
        *(pSad + 1) = WelsSampleSad8x8_c(iSample1, iStride1, (iSample2 + iStride2), iStride2);
        *(pSad + 2) = WelsSampleSad8x8_c(iSample1, iStride1, (iSample2 - 1), iStride2);
        *(pSad + 3) = WelsSampleSad8x8_c(iSample1, iStride1, (iSample2 + 1), iStride2);
    }

    public static void WelsSampleSadFour4x4_c(uint8_t* iSample1, int32_t iStride1, uint8_t* iSample2, int32_t iStride2, int32_t* pSad)
    {
        *(pSad) = WelsSampleSad4x4_c(iSample1, iStride1, (iSample2 - iStride2), iStride2);
        *(pSad + 1) = WelsSampleSad4x4_c(iSample1, iStride1, (iSample2 + iStride2), iStride2);
        *(pSad + 2) = WelsSampleSad4x4_c(iSample1, iStride1, (iSample2 - 1), iStride2);
        *(pSad + 3) = WelsSampleSad4x4_c(iSample1, iStride1, (iSample2 + 1), iStride2);
    }

    public static void WelsSampleSadFour8x4_c(uint8_t* iSample1, int32_t iStride1, uint8_t* iSample2, int32_t iStride2, int32_t* pSad)
    {
        *(pSad) = WelsSampleSad8x4_c(iSample1, iStride1, (iSample2 - iStride2), iStride2);
        *(pSad + 1) = WelsSampleSad8x4_c(iSample1, iStride1, (iSample2 + iStride2), iStride2);
        *(pSad + 2) = WelsSampleSad8x4_c(iSample1, iStride1, (iSample2 - 1), iStride2);
        *(pSad + 3) = WelsSampleSad8x4_c(iSample1, iStride1, (iSample2 + 1), iStride2);
    }

    public static void WelsSampleSadFour4x8_c(uint8_t* iSample1, int32_t iStride1, uint8_t* iSample2, int32_t iStride2, int32_t* pSad)
    {
        *(pSad) = WelsSampleSad4x8_c(iSample1, iStride1, (iSample2 - iStride2), iStride2);
        *(pSad + 1) = WelsSampleSad4x8_c(iSample1, iStride1, (iSample2 + iStride2), iStride2);
        *(pSad + 2) = WelsSampleSad4x8_c(iSample1, iStride1, (iSample2 - 1), iStride2);
        *(pSad + 3) = WelsSampleSad4x8_c(iSample1, iStride1, (iSample2 + 1), iStride2);
    }
}
