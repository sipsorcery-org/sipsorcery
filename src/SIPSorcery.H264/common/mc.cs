//-----------------------------------------------------------------------------
// Filename: mc.cs
//
// Description: Port of the C paths of:
//  - codec/common/inc/mc.h
//  - codec/common/src/mc.cpp (lines 1-380 and InitMcFunc; the rest of the
//    file is SSE2/SSSE3/AVX2/NEON/MMI/MSA/LSX wrappers, not ported)
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// Luma quarter-pel interpolation (the 6-tap filter and its averages), chroma
// eighth-pel bilinear interpolation, plain copies and sample averaging.
// Upstream's file-local anonymous-namespace functions are private here.
// The uint8_t/int16_t scratch arrays are stackalloc'd. C leaves them
// uninitialised and every element read is written first, so the zeroing C#
// adds changes nothing.
//
// Integer semantics kept from C: FilterInput8bitWithStride_c sums in uint32_t
// and the result wraps to int32_t; McHorVer22_c stores the vertical filter
// result in an int16_t (it always fits: -2550..10710).
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 24 Nov 2026  Claude          Created.
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
 * \file    mc.c
 *
 * \brief   Interfaces implementation for motion compensation
 *
 * \date    03/17/2009 Created
 *
 *************************************************************************************
 */

global using unsafe PWelsMcFunc = delegate*<byte*, int, byte*, int, short, short, int, int, void>;
global using unsafe PWelsLumaHalfpelMcFunc = delegate*<byte*, int, byte*, int, int, int, void>;
global using unsafe PWelsSampleAveragingFunc = delegate*<byte*, int, byte*, int, byte*, int, int, int, void>;

// mc.cpp's anonymous namespace.
using unsafe PWelsMcWidthHeightFunc = delegate*<byte*, int, byte*, int, int, int, void>;

namespace SIPSorcery.H264;

/// <summary>TagMcFunc.</summary>
internal unsafe struct SMcFunc
{
    public PWelsLumaHalfpelMcFunc pfLumaHalfpelHor;
    public PWelsLumaHalfpelMcFunc pfLumaHalfpelVer;
    public PWelsLumaHalfpelMcFunc pfLumaHalfpelCen;
    public PWelsMcFunc pMcChromaFunc;

    public PWelsMcFunc pMcLumaFunc;
    public PWelsSampleAveragingFunc pfSampleAveraging;
}

internal static unsafe partial class WelsCommon
{
    /*------------------weight for chroma fraction pixel interpolation------------------*/
    //iA = (8 - dx) * (8 - dy);
    //iB = dx * (8 - dy);
    //iC = (8 - dx) * dy;
    //iD = dx * dy
    // static const uint8_t g_kuiABCD[8][8][4] = { //g_kA[dy][dx], g_kB[dy][dx], g_kC[dy][dx], g_kD[dy][dx]
    private static readonly InlineArray8<InlineArray4<uint8_t>>* g_kuiABCD = StaticTable.Of<InlineArray8<InlineArray4<uint8_t>>, uint8_t>(8, [
        64, 0, 0, 0, 56, 8, 0, 0, 48, 16, 0, 0, 40, 24, 0, 0,
        32, 32, 0, 0, 24, 40, 0, 0, 16, 48, 0, 0, 8, 56, 0, 0,

        56, 0, 8, 0, 49, 7, 7, 1, 42, 14, 6, 2, 35, 21, 5, 3,
        28, 28, 4, 4, 21, 35, 3, 5, 14, 42, 2, 6, 7, 49, 1, 7,

        48, 0, 16, 0, 42, 6, 14, 2, 36, 12, 12, 4, 30, 18, 10, 6,
        24, 24, 8, 8, 18, 30, 6, 10, 12, 36, 4, 12, 6, 42, 2, 14,

        40, 0, 24, 0, 35, 5, 21, 3, 30, 10, 18, 6, 25, 15, 15, 9,
        20, 20, 12, 12, 15, 25, 9, 15, 10, 30, 6, 18, 5, 35, 3, 21,

        32, 0, 32, 0, 28, 4, 28, 4, 24, 8, 24, 8, 20, 12, 20, 12,
        16, 16, 16, 16, 12, 20, 12, 20, 8, 24, 8, 24, 4, 28, 4, 28,

        24, 0, 40, 0, 21, 3, 35, 5, 18, 6, 30, 10, 15, 9, 25, 15,
        12, 12, 20, 20, 9, 15, 15, 25, 6, 18, 10, 30, 3, 21, 5, 35,

        16, 0, 48, 0, 14, 2, 42, 6, 12, 4, 36, 12, 10, 6, 30, 18,
        8, 8, 24, 24, 6, 10, 18, 30, 4, 12, 12, 36, 2, 14, 6, 42,

        8, 0, 56, 0, 7, 1, 49, 7, 6, 2, 42, 14, 5, 3, 35, 21,
        4, 4, 28, 28, 3, 5, 21, 35, 2, 6, 14, 42, 1, 7, 7, 49
    ]);

    //***************************************************************************//
    //                          C code implementation                            //
    //***************************************************************************//
    private static void McCopyWidthEq2_c(uint8_t* pSrc, int32_t iSrcStride, uint8_t* pDst, int32_t iDstStride,
        int32_t iHeight)
    {
        int32_t i;
        for (i = 0; i < iHeight; i++)
        { // iWidth == 2 only for chroma
            ST16A2(pDst, LD16(pSrc));
            pDst += iDstStride;
            pSrc += iSrcStride;
        }
    }

    private static void McCopyWidthEq4_c(uint8_t* pSrc, int32_t iSrcStride, uint8_t* pDst, int32_t iDstStride,
        int32_t iHeight)
    {
        int32_t i;
        for (i = 0; i < iHeight; i++)
        {
            ST32A4(pDst, LD32(pSrc));
            pDst += iDstStride;
            pSrc += iSrcStride;
        }
    }

    private static void McCopyWidthEq8_c(uint8_t* pSrc, int32_t iSrcStride, uint8_t* pDst, int32_t iDstStride,
        int32_t iHeight)
    {
        int32_t i;
        for (i = 0; i < iHeight; i++)
        {
            ST64A8(pDst, LD64(pSrc));
            pDst += iDstStride;
            pSrc += iSrcStride;
        }
    }

    private static void McCopyWidthEq16_c(uint8_t* pSrc, int32_t iSrcStride, uint8_t* pDst, int32_t iDstStride,
        int32_t iHeight)
    {
        int32_t i;
        for (i = 0; i < iHeight; i++)
        {
            ST64A8(pDst, LD64(pSrc));
            ST64A8(pDst + 8, LD64(pSrc + 8));
            pDst += iDstStride;
            pSrc += iSrcStride;
        }
    }

    //--------------------Luma sample MC------------------//

    private static int32_t HorFilterInput16bit_c(int16_t* pSrc)
    {
        int32_t iPix05 = pSrc[0] + pSrc[5];
        int32_t iPix14 = pSrc[1] + pSrc[4];
        int32_t iPix23 = pSrc[2] + pSrc[3];

        return iPix05 - (iPix14 * 5) + (iPix23 * 20);
    }

    // h: iOffset=1 / v: iOffset=iSrcStride
    private static int32_t FilterInput8bitWithStride_c(uint8_t* pSrc, int32_t kiOffset)
    {
        int32_t kiOffset1 = kiOffset;
        int32_t kiOffset2 = kiOffset << 1;
        int32_t kiOffset3 = kiOffset + kiOffset2;
        uint32_t kuiPix05 = (uint32_t)(*(pSrc - kiOffset2) + *(pSrc + kiOffset3));
        uint32_t kuiPix14 = (uint32_t)(*(pSrc - kiOffset1) + *(pSrc + kiOffset2));
        uint32_t kuiPix23 = (uint32_t)(*pSrc + *(pSrc + kiOffset1));

        return (int32_t)(kuiPix05 - ((kuiPix14 << 2) + kuiPix14) + (kuiPix23 << 4) + (kuiPix23 << 2));
    }

    private static void PixelAvg_c(uint8_t* pDst, int32_t iDstStride, uint8_t* pSrcA, int32_t iSrcAStride,
        uint8_t* pSrcB, int32_t iSrcBStride, int32_t iWidth, int32_t iHeight)
    {
        int32_t i, j;
        for (i = 0; i < iHeight; i++)
        {
            for (j = 0; j < iWidth; j++)
            {
                pDst[j] = (uint8_t)((pSrcA[j] + pSrcB[j] + 1) >> 1);
            }
            pDst += iDstStride;
            pSrcA += iSrcAStride;
            pSrcB += iSrcBStride;
        }
    }

    private static void McCopy_c(uint8_t* pSrc, int32_t iSrcStride, uint8_t* pDst, int32_t iDstStride, int32_t iWidth,
        int32_t iHeight)
    {
        if (iWidth == 16)
        {
            McCopyWidthEq16_c(pSrc, iSrcStride, pDst, iDstStride, iHeight);
        }
        else if (iWidth == 8)
        {
            McCopyWidthEq8_c(pSrc, iSrcStride, pDst, iDstStride, iHeight);
        }
        else if (iWidth == 4)
        {
            McCopyWidthEq4_c(pSrc, iSrcStride, pDst, iDstStride, iHeight);
        }
        else
        { //here iWidth == 2
            McCopyWidthEq2_c(pSrc, iSrcStride, pDst, iDstStride, iHeight);
        }
    }

    //horizontal filter to gain half sample, that is (2, 0) location in quarter sample
    private static void McHorVer20_c(uint8_t* pSrc, int32_t iSrcStride, uint8_t* pDst, int32_t iDstStride,
        int32_t iWidth,
        int32_t iHeight)
    {
        int32_t i, j;
        for (i = 0; i < iHeight; i++)
        {
            for (j = 0; j < iWidth; j++)
            {
                pDst[j] = WelsClip1((FilterInput8bitWithStride_c(pSrc + j, 1) + 16) >> 5);
            }
            pDst += iDstStride;
            pSrc += iSrcStride;
        }
    }

    //vertical filter to gain half sample, that is (0, 2) location in quarter sample
    private static void McHorVer02_c(uint8_t* pSrc, int32_t iSrcStride, uint8_t* pDst, int32_t iDstStride,
        int32_t iWidth,
        int32_t iHeight)
    {
        int32_t i, j;
        for (i = 0; i < iHeight; i++)
        {
            for (j = 0; j < iWidth; j++)
            {
                pDst[j] = WelsClip1((FilterInput8bitWithStride_c(pSrc + j, iSrcStride) + 16) >> 5);
            }
            pDst += iDstStride;
            pSrc += iSrcStride;
        }
    }

    //horizontal and vertical filter to gain half sample, that is (2, 2) location in quarter sample
    private static void McHorVer22_c(uint8_t* pSrc, int32_t iSrcStride, uint8_t* pDst, int32_t iDstStride,
        int32_t iWidth,
        int32_t iHeight)
    {
        int16_t* iTmp = stackalloc int16_t[17 + 5];
        int32_t i, j, k;

        for (i = 0; i < iHeight; i++)
        {
            for (j = 0; j < iWidth + 5; j++)
            {
                iTmp[j] = (int16_t)FilterInput8bitWithStride_c(pSrc - 2 + j, iSrcStride);
            }
            for (k = 0; k < iWidth; k++)
            {
                pDst[k] = WelsClip1((HorFilterInput16bit_c(&iTmp[k]) + 512) >> 10);
            }
            pSrc += iSrcStride;
            pDst += iDstStride;
        }
    }

    /////////////////////luma MC//////////////////////////
    private static void McHorVer01_c(uint8_t* pSrc, int32_t iSrcStride, uint8_t* pDst, int32_t iDstStride,
        int32_t iWidth,
        int32_t iHeight)
    {
        uint8_t* uiTmp = stackalloc uint8_t[256];
        McHorVer02_c(pSrc, iSrcStride, uiTmp, 16, iWidth, iHeight);
        PixelAvg_c(pDst, iDstStride, pSrc, iSrcStride, uiTmp, 16, iWidth, iHeight);
    }

    private static void McHorVer03_c(uint8_t* pSrc, int32_t iSrcStride, uint8_t* pDst, int32_t iDstStride,
        int32_t iWidth,
        int32_t iHeight)
    {
        uint8_t* uiTmp = stackalloc uint8_t[256];
        McHorVer02_c(pSrc, iSrcStride, uiTmp, 16, iWidth, iHeight);
        PixelAvg_c(pDst, iDstStride, pSrc + iSrcStride, iSrcStride, uiTmp, 16, iWidth, iHeight);
    }

    private static void McHorVer10_c(uint8_t* pSrc, int32_t iSrcStride, uint8_t* pDst, int32_t iDstStride,
        int32_t iWidth,
        int32_t iHeight)
    {
        uint8_t* uiTmp = stackalloc uint8_t[256];
        McHorVer20_c(pSrc, iSrcStride, uiTmp, 16, iWidth, iHeight);
        PixelAvg_c(pDst, iDstStride, pSrc, iSrcStride, uiTmp, 16, iWidth, iHeight);
    }

    private static void McHorVer11_c(uint8_t* pSrc, int32_t iSrcStride, uint8_t* pDst, int32_t iDstStride,
        int32_t iWidth,
        int32_t iHeight)
    {
        uint8_t* uiHorTmp = stackalloc uint8_t[256];
        uint8_t* uiVerTmp = stackalloc uint8_t[256];
        McHorVer20_c(pSrc, iSrcStride, uiHorTmp, 16, iWidth, iHeight);
        McHorVer02_c(pSrc, iSrcStride, uiVerTmp, 16, iWidth, iHeight);
        PixelAvg_c(pDst, iDstStride, uiHorTmp, 16, uiVerTmp, 16, iWidth, iHeight);
    }

    private static void McHorVer12_c(uint8_t* pSrc, int32_t iSrcStride, uint8_t* pDst, int32_t iDstStride,
        int32_t iWidth,
        int32_t iHeight)
    {
        uint8_t* uiVerTmp = stackalloc uint8_t[256];
        uint8_t* uiCtrTmp = stackalloc uint8_t[256];
        McHorVer02_c(pSrc, iSrcStride, uiVerTmp, 16, iWidth, iHeight);
        McHorVer22_c(pSrc, iSrcStride, uiCtrTmp, 16, iWidth, iHeight);
        PixelAvg_c(pDst, iDstStride, uiVerTmp, 16, uiCtrTmp, 16, iWidth, iHeight);
    }

    private static void McHorVer13_c(uint8_t* pSrc, int32_t iSrcStride, uint8_t* pDst, int32_t iDstStride,
        int32_t iWidth,
        int32_t iHeight)
    {
        uint8_t* uiHorTmp = stackalloc uint8_t[256];
        uint8_t* uiVerTmp = stackalloc uint8_t[256];
        McHorVer20_c(pSrc + iSrcStride, iSrcStride, uiHorTmp, 16, iWidth, iHeight);
        McHorVer02_c(pSrc, iSrcStride, uiVerTmp, 16, iWidth, iHeight);
        PixelAvg_c(pDst, iDstStride, uiHorTmp, 16, uiVerTmp, 16, iWidth, iHeight);
    }

    private static void McHorVer21_c(uint8_t* pSrc, int32_t iSrcStride, uint8_t* pDst, int32_t iDstStride,
        int32_t iWidth,
        int32_t iHeight)
    {
        uint8_t* uiHorTmp = stackalloc uint8_t[256];
        uint8_t* uiCtrTmp = stackalloc uint8_t[256];
        McHorVer20_c(pSrc, iSrcStride, uiHorTmp, 16, iWidth, iHeight);
        McHorVer22_c(pSrc, iSrcStride, uiCtrTmp, 16, iWidth, iHeight);
        PixelAvg_c(pDst, iDstStride, uiHorTmp, 16, uiCtrTmp, 16, iWidth, iHeight);
    }

    private static void McHorVer23_c(uint8_t* pSrc, int32_t iSrcStride, uint8_t* pDst, int32_t iDstStride,
        int32_t iWidth,
        int32_t iHeight)
    {
        uint8_t* uiHorTmp = stackalloc uint8_t[256];
        uint8_t* uiCtrTmp = stackalloc uint8_t[256];
        McHorVer20_c(pSrc + iSrcStride, iSrcStride, uiHorTmp, 16, iWidth, iHeight);
        McHorVer22_c(pSrc, iSrcStride, uiCtrTmp, 16, iWidth, iHeight);
        PixelAvg_c(pDst, iDstStride, uiHorTmp, 16, uiCtrTmp, 16, iWidth, iHeight);
    }

    private static void McHorVer30_c(uint8_t* pSrc, int32_t iSrcStride, uint8_t* pDst, int32_t iDstStride,
        int32_t iWidth,
        int32_t iHeight)
    {
        uint8_t* uiHorTmp = stackalloc uint8_t[256];
        McHorVer20_c(pSrc, iSrcStride, uiHorTmp, 16, iWidth, iHeight);
        PixelAvg_c(pDst, iDstStride, pSrc + 1, iSrcStride, uiHorTmp, 16, iWidth, iHeight);
    }

    private static void McHorVer31_c(uint8_t* pSrc, int32_t iSrcStride, uint8_t* pDst, int32_t iDstStride,
        int32_t iWidth,
        int32_t iHeight)
    {
        uint8_t* uiHorTmp = stackalloc uint8_t[256];
        uint8_t* uiVerTmp = stackalloc uint8_t[256];
        McHorVer20_c(pSrc, iSrcStride, uiHorTmp, 16, iWidth, iHeight);
        McHorVer02_c(pSrc + 1, iSrcStride, uiVerTmp, 16, iWidth, iHeight);
        PixelAvg_c(pDst, iDstStride, uiHorTmp, 16, uiVerTmp, 16, iWidth, iHeight);
    }

    private static void McHorVer32_c(uint8_t* pSrc, int32_t iSrcStride, uint8_t* pDst, int32_t iDstStride,
        int32_t iWidth,
        int32_t iHeight)
    {
        uint8_t* uiVerTmp = stackalloc uint8_t[256];
        uint8_t* uiCtrTmp = stackalloc uint8_t[256];
        McHorVer02_c(pSrc + 1, iSrcStride, uiVerTmp, 16, iWidth, iHeight);
        McHorVer22_c(pSrc, iSrcStride, uiCtrTmp, 16, iWidth, iHeight);
        PixelAvg_c(pDst, iDstStride, uiVerTmp, 16, uiCtrTmp, 16, iWidth, iHeight);
    }

    private static void McHorVer33_c(uint8_t* pSrc, int32_t iSrcStride, uint8_t* pDst, int32_t iDstStride,
        int32_t iWidth,
        int32_t iHeight)
    {
        uint8_t* uiHorTmp = stackalloc uint8_t[256];
        uint8_t* uiVerTmp = stackalloc uint8_t[256];
        McHorVer20_c(pSrc + iSrcStride, iSrcStride, uiHorTmp, 16, iWidth, iHeight);
        McHorVer02_c(pSrc + 1, iSrcStride, uiVerTmp, 16, iWidth, iHeight);
        PixelAvg_c(pDst, iDstStride, uiHorTmp, 16, uiVerTmp, 16, iWidth, iHeight);
    }

    // McLuma_c's function-local static table, [x][y] flattened to [x * 4 + y].
    private static readonly PWelsMcWidthHeightFunc[] pWelsMcFunc =
    [
        &McCopy_c, &McHorVer01_c, &McHorVer02_c, &McHorVer03_c,
        &McHorVer10_c, &McHorVer11_c, &McHorVer12_c, &McHorVer13_c,
        &McHorVer20_c, &McHorVer21_c, &McHorVer22_c, &McHorVer23_c,
        &McHorVer30_c, &McHorVer31_c, &McHorVer32_c, &McHorVer33_c,
    ];

    private static void McLuma_c(uint8_t* pSrc, int32_t iSrcStride, uint8_t* pDst, int32_t iDstStride,
        int16_t iMvX, int16_t iMvY, int32_t iWidth, int32_t iHeight)
    //pSrc has been added the offset of mv
    {
        pWelsMcFunc[((iMvX & 0x03) << 2) + (iMvY & 0x03)](pSrc, iSrcStride, pDst, iDstStride, iWidth, iHeight);
    }

    private static void McChromaWithFragMv_c(uint8_t* pSrc, int32_t iSrcStride, uint8_t* pDst, int32_t iDstStride,
        int16_t iMvX, int16_t iMvY, int32_t iWidth, int32_t iHeight)
    {
        int32_t i, j;
        int32_t iA, iB, iC, iD;
        uint8_t* pSrcNext = pSrc + iSrcStride;
        uint8_t* pABCD = (uint8_t*)&g_kuiABCD[iMvY & 0x07][iMvX & 0x07];
        iA = pABCD[0];
        iB = pABCD[1];
        iC = pABCD[2];
        iD = pABCD[3];
        for (i = 0; i < iHeight; i++)
        {
            for (j = 0; j < iWidth; j++)
            {
                pDst[j] = (uint8_t)((iA * pSrc[j] + iB * pSrc[j + 1] + iC * pSrcNext[j] + iD * pSrcNext[j + 1] + 32) >> 6);
            }
            pDst += iDstStride;
            pSrc = pSrcNext;
            pSrcNext += iSrcStride;
        }
    }

    private static void McChroma_c(uint8_t* pSrc, int32_t iSrcStride, uint8_t* pDst, int32_t iDstStride,
        int16_t iMvX, int16_t iMvY, int32_t iWidth, int32_t iHeight)
    //pSrc has been added the offset of mv
    {
        int32_t kiD8x = iMvX & 0x07;
        int32_t kiD8y = iMvY & 0x07;
        if (0 == kiD8x && 0 == kiD8y)
        {
            McCopy_c(pSrc, iSrcStride, pDst, iDstStride, iWidth, iHeight);
        }
        else
        {
            McChromaWithFragMv_c(pSrc, iSrcStride, pDst, iDstStride, iMvX, iMvY, iWidth, iHeight);
        }
    }

    public static void InitMcFunc(SMcFunc* pMcFuncs, uint32_t uiCpuFlag)
    {
        pMcFuncs->pfLumaHalfpelHor = &McHorVer20_c;
        pMcFuncs->pfLumaHalfpelVer = &McHorVer02_c;
        pMcFuncs->pfLumaHalfpelCen = &McHorVer22_c;
        pMcFuncs->pfSampleAveraging = &PixelAvg_c;
        pMcFuncs->pMcChromaFunc = &McChroma_c;
        pMcFuncs->pMcLumaFunc = &McLuma_c;
        // X86_ASM / HAVE_NEON / HAVE_NEON_AARCH64 / HAVE_MMI / HAVE_MSA / HAVE_LSX variants: not ported.
    }
}
