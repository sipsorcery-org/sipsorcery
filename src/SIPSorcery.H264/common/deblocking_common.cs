//-----------------------------------------------------------------------------
// Filename: deblocking_common.cs
//
// Description: Port of:
//  - codec/common/inc/deblocking_common.h
//  - codec/common/src/deblocking_common.cpp
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// The loop filter edge kernels shared by the encoder and decoder: luma and
// chroma, bS < 4 and bS == 4, across vertical and horizontal edges, and the
// interleaved-chroma (CbCr) variants. Upstream's SIMD variants and their
// transpose wrappers are not ported; deblocking_common_simd.cs has kernels of
// its own in their place. The function-pointer tables that select them are the
// decoder's and the encoder's (their DeblockingInit).
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 24 Nov 2026  Claude          Created.
// 27 Nov 2026  Claude          Header: the SIMD kernels are in deblocking_common_simd.cs (P5.4).
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
// The upstream files carry no notice of their own. The ported code is covered
// by the OpenH264 LICENSE (BSD 2-Clause), reproduced below.
//-----------------------------------------------------------------------------

/*
 * Copyright (c) 2013, Cisco Systems
 * All rights reserved.
 *
 * Redistribution and use in source and binary forms, with or without modification,
 * are permitted provided that the following conditions are met:
 *
 * * Redistributions of source code must retain the above copyright notice, this
 *   list of conditions and the following disclaimer.
 *
 * * Redistributions in binary form must reproduce the above copyright notice, this
 *   list of conditions and the following disclaimer in the documentation and/or
 *   other materials provided with the distribution.
 *
 * THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
 * ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR
 * ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON
 * ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

namespace SIPSorcery.H264;

internal static unsafe partial class WelsCommon
{
    //  C code only. The six strided helpers are file-local in practice (not in
    // the header, called only from this file), so they are private here.
    private static void DeblockLumaLt4_c(uint8_t* pPix, int32_t iStrideX, int32_t iStrideY, int32_t iAlpha, int32_t iBeta,
                                         int8_t* pTc)
    {
        for (int32_t i = 0; i < 16; i++)
        {
            int32_t iTc0 = pTc[i >> 2];
            if (iTc0 >= 0)
            {
                int32_t p0 = pPix[-iStrideX];
                int32_t p1 = pPix[-2 * iStrideX];
                int32_t p2 = pPix[-3 * iStrideX];
                int32_t q0 = pPix[0];
                int32_t q1 = pPix[iStrideX];
                int32_t q2 = pPix[2 * iStrideX];
                bool bDetaP0Q0 = WELS_ABS(p0 - q0) < iAlpha;
                bool bDetaP1P0 = WELS_ABS(p1 - p0) < iBeta;
                bool bDetaQ1Q0 = WELS_ABS(q1 - q0) < iBeta;
                int32_t iTc = iTc0;
                if (bDetaP0Q0 && bDetaP1P0 && bDetaQ1Q0)
                {
                    bool bDetaP2P0 = WELS_ABS(p2 - p0) < iBeta;
                    bool bDetaQ2Q0 = WELS_ABS(q2 - q0) < iBeta;
                    if (bDetaP2P0)
                    {
                        pPix[-2 * iStrideX] = (uint8_t)(p1 + WELS_CLIP3((p2 + ((p0 + q0 + 1) >> 1) - (p1 * (1 << 1))) >> 1, -iTc0, iTc0));
                        iTc++;
                    }
                    if (bDetaQ2Q0)
                    {
                        pPix[iStrideX] = (uint8_t)(q1 + WELS_CLIP3((q2 + ((p0 + q0 + 1) >> 1) - (q1 * (1 << 1))) >> 1, -iTc0, iTc0));
                        iTc++;
                    }
                    int32_t iDeta = WELS_CLIP3((((q0 - p0) * (1 << 2)) + (p1 - q1) + 4) >> 3, -iTc, iTc);
                    pPix[-iStrideX] = WelsClip1(p0 + iDeta);     /* p0' */
                    pPix[0] = WelsClip1(q0 - iDeta);     /* q0' */
                }
            }
            pPix += iStrideY;
        }
    }

    private static void DeblockLumaEq4_c(uint8_t* pPix, int32_t iStrideX, int32_t iStrideY, int32_t iAlpha, int32_t iBeta)
    {
        int32_t p0, p1, p2, q0, q1, q2;
        int32_t iDetaP0Q0;
        bool bDetaP1P0, bDetaQ1Q0;
        for (int32_t i = 0; i < 16; i++)
        {
            p0 = pPix[-iStrideX];
            p1 = pPix[-2 * iStrideX];
            p2 = pPix[-3 * iStrideX];
            q0 = pPix[0];
            q1 = pPix[iStrideX];
            q2 = pPix[2 * iStrideX];
            iDetaP0Q0 = WELS_ABS(p0 - q0);
            bDetaP1P0 = WELS_ABS(p1 - p0) < iBeta;
            bDetaQ1Q0 = WELS_ABS(q1 - q0) < iBeta;
            if ((iDetaP0Q0 < iAlpha) && bDetaP1P0 && bDetaQ1Q0)
            {
                if (iDetaP0Q0 < ((iAlpha >> 2) + 2))
                {
                    bool bDetaP2P0 = WELS_ABS(p2 - p0) < iBeta;
                    bool bDetaQ2Q0 = WELS_ABS(q2 - q0) < iBeta;
                    if (bDetaP2P0)
                    {
                        int32_t p3 = pPix[-4 * iStrideX];
                        pPix[-iStrideX] = (uint8_t)((p2 + (p1 * (1 << 1)) + (p0 * (1 << 1)) + (q0 * (1 << 1)) + q1 + 4) >> 3);   //p0
                        pPix[-2 * iStrideX] = (uint8_t)((p2 + p1 + p0 + q0 + 2) >> 2);                         //p1
                        pPix[-3 * iStrideX] = (uint8_t)(((p3 * (1 << 1)) + p2 + (p2 * (1 << 1)) + p1 + p0 + q0 + 4) >> 3); //p2
                    }
                    else
                    {
                        pPix[-1 * iStrideX] = (uint8_t)(((p1 * (1 << 1)) + p0 + q1 + 2) >> 2);                       //p0
                    }
                    if (bDetaQ2Q0)
                    {
                        int32_t q3 = pPix[3 * iStrideX];
                        pPix[0] = (uint8_t)((p1 + (p0 * (1 << 1)) + (q0 * (1 << 1)) + (q1 * (1 << 1)) + q2 + 4) >> 3);           //q0
                        pPix[iStrideX] = (uint8_t)((p0 + q0 + q1 + q2 + 2) >> 2);                              //q1
                        pPix[2 * iStrideX] = (uint8_t)(((q3 * (1 << 1)) + q2 + (q2 * (1 << 1)) + q1 + q0 + p0 + 4) >> 3);  //q2
                    }
                    else
                    {
                        pPix[0] = (uint8_t)(((q1 * (1 << 1)) + q0 + p1 + 2) >> 2);                                   //q0
                    }
                }
                else
                {
                    pPix[-iStrideX] = (uint8_t)(((p1 * (1 << 1)) + p0 + q1 + 2) >> 2);   //p0
                    pPix[0] = (uint8_t)(((q1 * (1 << 1)) + q0 + p1 + 2) >> 2);          //q0
                }
            }
            pPix += iStrideY;
        }
    }

    public static void DeblockLumaLt4V_c(uint8_t* pPix, int32_t iStride, int32_t iAlpha, int32_t iBeta, int8_t* tc)
    {
        DeblockLumaLt4_c(pPix, iStride, 1, iAlpha, iBeta, tc);
    }

    public static void DeblockLumaLt4H_c(uint8_t* pPix, int32_t iStride, int32_t iAlpha, int32_t iBeta, int8_t* tc)
    {
        DeblockLumaLt4_c(pPix, 1, iStride, iAlpha, iBeta, tc);
    }

    public static void DeblockLumaEq4V_c(uint8_t* pPix, int32_t iStride, int32_t iAlpha, int32_t iBeta)
    {
        DeblockLumaEq4_c(pPix, iStride, 1, iAlpha, iBeta);
    }

    public static void DeblockLumaEq4H_c(uint8_t* pPix, int32_t iStride, int32_t iAlpha, int32_t iBeta)
    {
        DeblockLumaEq4_c(pPix, 1, iStride, iAlpha, iBeta);
    }

    private static void DeblockChromaLt4_c(uint8_t* pPixCb, uint8_t* pPixCr, int32_t iStrideX, int32_t iStrideY, int32_t iAlpha,
                                           int32_t iBeta, int8_t* pTc)
    {
        int32_t p0, p1, q0, q1, iDeta;
        bool bDetaP0Q0, bDetaP1P0, bDetaQ1Q0;

        for (int32_t i = 0; i < 8; i++)
        {
            int32_t iTc0 = pTc[i >> 1];
            if (iTc0 > 0)
            {
                p0 = pPixCb[-iStrideX];
                p1 = pPixCb[-2 * iStrideX];
                q0 = pPixCb[0];
                q1 = pPixCb[iStrideX];

                bDetaP0Q0 = WELS_ABS(p0 - q0) < iAlpha;
                bDetaP1P0 = WELS_ABS(p1 - p0) < iBeta;
                bDetaQ1Q0 = WELS_ABS(q1 - q0) < iBeta;
                if (bDetaP0Q0 && bDetaP1P0 && bDetaQ1Q0)
                {
                    iDeta = WELS_CLIP3((((q0 - p0) * (1 << 2)) + (p1 - q1) + 4) >> 3, -iTc0, iTc0);
                    pPixCb[-iStrideX] = WelsClip1(p0 + iDeta);     /* p0' */
                    pPixCb[0] = WelsClip1(q0 - iDeta);     /* q0' */
                }


                p0 = pPixCr[-iStrideX];
                p1 = pPixCr[-2 * iStrideX];
                q0 = pPixCr[0];
                q1 = pPixCr[iStrideX];

                bDetaP0Q0 = WELS_ABS(p0 - q0) < iAlpha;
                bDetaP1P0 = WELS_ABS(p1 - p0) < iBeta;
                bDetaQ1Q0 = WELS_ABS(q1 - q0) < iBeta;

                if (bDetaP0Q0 && bDetaP1P0 && bDetaQ1Q0)
                {
                    iDeta = WELS_CLIP3((((q0 - p0) * (1 << 2)) + (p1 - q1) + 4) >> 3, -iTc0, iTc0);
                    pPixCr[-iStrideX] = WelsClip1(p0 + iDeta);     /* p0' */
                    pPixCr[0] = WelsClip1(q0 - iDeta);     /* q0' */
                }
            }
            pPixCb += iStrideY;
            pPixCr += iStrideY;
        }
    }

    private static void DeblockChromaEq4_c(uint8_t* pPixCb, uint8_t* pPixCr, int32_t iStrideX, int32_t iStrideY, int32_t iAlpha,
                                           int32_t iBeta)
    {
        int32_t p0, p1, q0, q1;
        bool bDetaP0Q0, bDetaP1P0, bDetaQ1Q0;
        for (int32_t i = 0; i < 8; i++)
        {
            //cb
            p0 = pPixCb[-iStrideX];
            p1 = pPixCb[-2 * iStrideX];
            q0 = pPixCb[0];
            q1 = pPixCb[iStrideX];
            bDetaP0Q0 = WELS_ABS(p0 - q0) < iAlpha;
            bDetaP1P0 = WELS_ABS(p1 - p0) < iBeta;
            bDetaQ1Q0 = WELS_ABS(q1 - q0) < iBeta;
            if (bDetaP0Q0 && bDetaP1P0 && bDetaQ1Q0)
            {
                pPixCb[-iStrideX] = (uint8_t)(((p1 * (1 << 1)) + p0 + q1 + 2) >> 2);     /* p0' */
                pPixCb[0] = (uint8_t)(((q1 * (1 << 1)) + q0 + p1 + 2) >> 2);     /* q0' */
            }

            //cr
            p0 = pPixCr[-iStrideX];
            p1 = pPixCr[-2 * iStrideX];
            q0 = pPixCr[0];
            q1 = pPixCr[iStrideX];
            bDetaP0Q0 = WELS_ABS(p0 - q0) < iAlpha;
            bDetaP1P0 = WELS_ABS(p1 - p0) < iBeta;
            bDetaQ1Q0 = WELS_ABS(q1 - q0) < iBeta;
            if (bDetaP0Q0 && bDetaP1P0 && bDetaQ1Q0)
            {
                pPixCr[-iStrideX] = (uint8_t)(((p1 * (1 << 1)) + p0 + q1 + 2) >> 2);     /* p0' */
                pPixCr[0] = (uint8_t)(((q1 * (1 << 1)) + q0 + p1 + 2) >> 2);     /* q0' */
            }
            pPixCr += iStrideY;
            pPixCb += iStrideY;
        }
    }

    public static void DeblockChromaLt4V_c(uint8_t* pPixCb, uint8_t* pPixCr, int32_t iStride, int32_t iAlpha, int32_t iBeta,
                                           int8_t* tc)
    {
        DeblockChromaLt4_c(pPixCb, pPixCr, iStride, 1, iAlpha, iBeta, tc);
    }

    public static void DeblockChromaLt4H_c(uint8_t* pPixCb, uint8_t* pPixCr, int32_t iStride, int32_t iAlpha, int32_t iBeta,
                                           int8_t* tc)
    {
        DeblockChromaLt4_c(pPixCb, pPixCr, 1, iStride, iAlpha, iBeta, tc);
    }

    public static void DeblockChromaEq4V_c(uint8_t* pPixCb, uint8_t* pPixCr, int32_t iStride, int32_t iAlpha, int32_t iBeta)
    {
        DeblockChromaEq4_c(pPixCb, pPixCr, iStride, 1, iAlpha, iBeta);
    }

    public static void DeblockChromaEq4H_c(uint8_t* pPixCb, uint8_t* pPixCr, int32_t iStride, int32_t iAlpha, int32_t iBeta)
    {
        DeblockChromaEq4_c(pPixCb, pPixCr, 1, iStride, iAlpha, iBeta);
    }

    private static void DeblockChromaLt42_c(uint8_t* pPixCbCr, int32_t iStrideX, int32_t iStrideY, int32_t iAlpha,
                                            int32_t iBeta, int8_t* pTc)
    {
        int32_t p0, p1, q0, q1, iDeta;
        bool bDetaP0Q0, bDetaP1P0, bDetaQ1Q0;

        for (int32_t i = 0; i < 8; i++)
        {
            int32_t iTc0 = pTc[i >> 1];
            if (iTc0 > 0)
            {
                p0 = pPixCbCr[-iStrideX];
                p1 = pPixCbCr[-2 * iStrideX];
                q0 = pPixCbCr[0];
                q1 = pPixCbCr[iStrideX];

                bDetaP0Q0 = WELS_ABS(p0 - q0) < iAlpha;
                bDetaP1P0 = WELS_ABS(p1 - p0) < iBeta;
                bDetaQ1Q0 = WELS_ABS(q1 - q0) < iBeta;
                if (bDetaP0Q0 && bDetaP1P0 && bDetaQ1Q0)
                {
                    iDeta = WELS_CLIP3((((q0 - p0) * (1 << 2)) + (p1 - q1) + 4) >> 3, -iTc0, iTc0);
                    pPixCbCr[-iStrideX] = WelsClip1(p0 + iDeta);     /* p0' */
                    pPixCbCr[0] = WelsClip1(q0 - iDeta);     /* q0' */
                }


            }
            pPixCbCr += iStrideY;
        }
    }

    private static void DeblockChromaEq42_c(uint8_t* pPixCbCr, int32_t iStrideX, int32_t iStrideY, int32_t iAlpha,
                                            int32_t iBeta)
    {
        int32_t p0, p1, q0, q1;
        bool bDetaP0Q0, bDetaP1P0, bDetaQ1Q0;
        for (int32_t i = 0; i < 8; i++)
        {
            p0 = pPixCbCr[-iStrideX];
            p1 = pPixCbCr[-2 * iStrideX];
            q0 = pPixCbCr[0];
            q1 = pPixCbCr[iStrideX];
            bDetaP0Q0 = WELS_ABS(p0 - q0) < iAlpha;
            bDetaP1P0 = WELS_ABS(p1 - p0) < iBeta;
            bDetaQ1Q0 = WELS_ABS(q1 - q0) < iBeta;
            if (bDetaP0Q0 && bDetaP1P0 && bDetaQ1Q0)
            {
                pPixCbCr[-iStrideX] = (uint8_t)(((p1 * (1 << 1)) + p0 + q1 + 2) >> 2);     /* p0' */
                pPixCbCr[0] = (uint8_t)(((q1 * (1 << 1)) + q0 + p1 + 2) >> 2);     /* q0' */
            }

            pPixCbCr += iStrideY;
        }
    }

    public static void DeblockChromaLt4V2_c(uint8_t* pPixCbCr, int32_t iStride, int32_t iAlpha, int32_t iBeta,
                                            int8_t* tc)
    {
        DeblockChromaLt42_c(pPixCbCr, iStride, 1, iAlpha, iBeta, tc);
    }

    public static void DeblockChromaLt4H2_c(uint8_t* pPixCbCr, int32_t iStride, int32_t iAlpha, int32_t iBeta,
                                            int8_t* tc)
    {

        DeblockChromaLt42_c(pPixCbCr, 1, iStride, iAlpha, iBeta, tc);
    }

    public static void DeblockChromaEq4V2_c(uint8_t* pPixCbCr, int32_t iStride, int32_t iAlpha, int32_t iBeta)
    {
        DeblockChromaEq42_c(pPixCbCr, iStride, 1, iAlpha, iBeta);
    }

    public static void DeblockChromaEq4H2_c(uint8_t* pPixCbCr, int32_t iStride, int32_t iAlpha, int32_t iBeta)
    {
        DeblockChromaEq42_c(pPixCbCr, 1, iStride, iAlpha, iBeta);
    }

    public static void WelsNonZeroCount_c(int8_t* pNonZeroCount)
    {
        int32_t i;
        for (i = 0; i < 24; i++)
        {
            pNonZeroCount[i] = (int8_t)(pNonZeroCount[i] != 0 ? 1 : 0);
        }
    }
}
