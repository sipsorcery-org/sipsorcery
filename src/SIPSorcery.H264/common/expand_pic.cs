//-----------------------------------------------------------------------------
// Filename: expand_pic.cs
//
// Description: Port of:
//  - codec/common/inc/expand_pic.h
//  - codec/common/src/expand_pic.cpp
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// Pads a reconstructed picture's borders for motion compensation, either the
// whole picture (ExpandReferencingPicture) or one MB at a time (PadMB*_c).
// The C kernels only; the SIMD variants and the CPU-flag dispatch in
// InitExpandPictureFunc are not ported. Upstream declares these extern "C";
// here they live in WelsCommon with the rest of codec/common.
//
// PadMB*_c and the MBPad* helpers take C++ references (uint8_t*&,
// const int32_t&) that none of them writes through, so they are by value here.
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
 * \file    expand_pic.h
 *
 * \brief   Interface for expanding reconstructed picture to be used for reference
 *
 * \date    06/08/2009
 *************************************************************************************
 */

global using unsafe PExpandPictureFunc = delegate*<byte*, int, int, int, void>;

using System.Runtime.CompilerServices;

namespace SIPSorcery.H264;

/// <summary>TagExpandPicFunc.</summary>
internal unsafe struct SExpandPicFunc
{
    public PExpandPictureFunc pfExpandLumaPicture;
#pragma warning disable CS0649 // written through the pfExpandChromaPicture pointer
    private InlineArray2<nint> _pfExpandChromaPicture;
#pragma warning restore CS0649

    /// <summary>PExpandPictureFunc pfExpandChromaPicture[2].</summary>
    public PExpandPictureFunc* pfExpandChromaPicture
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (PExpandPictureFunc*)Unsafe.AsPointer(ref _pfExpandChromaPicture);
    }
}

internal static unsafe partial class WelsCommon
{
    public const int PADDING_LENGTH = 32; // reference extension
    public const int CHROMA_PADDING_LENGTH = 16; // chroma reference extension

    private static void MBPadTopLeftLuma_c(uint8_t* pDst, int32_t kiStride)
    {
        uint8_t kuiTL = pDst[0];
        int32_t i = 0;
        uint8_t* pTopLeft = pDst;
        do
        {
            pTopLeft -= kiStride;
            // pad pTop
            memcpy(pTopLeft, pDst, 16);           // confirmed_safe_unsafe_usage
            memset(pTopLeft - PADDING_LENGTH, kuiTL, PADDING_LENGTH); //pTop left
        } while (++i < PADDING_LENGTH);
    }

    private static void MBPadTopLuma_c(uint8_t* pDst, int32_t kiStride, int32_t kiMbX)
    {
        uint8_t* pTopLine = pDst + (kiMbX << 4);
        int32_t i = 0;
        uint8_t* pTop = pTopLine;
        do
        {
            pTop -= kiStride;
            // pad pTop
            memcpy(pTop, pTopLine, 16);          // confirmed_safe_unsafe_usage
        } while (++i < PADDING_LENGTH);
    }

    private static void MBPadBottomLuma_c(uint8_t* pDst, int32_t kiStride, int32_t kiMbX, int32_t kiPicH)
    {
        uint8_t* pBottomLine = pDst + (kiPicH - 1) * kiStride + (kiMbX << 4);
        int32_t i = 0;
        uint8_t* pBottom = pBottomLine;
        do
        {
            pBottom += kiStride;
            // pad pBottom
            memcpy(pBottom, pBottomLine, 16);       // confirmed_safe_unsafe_usage
        } while (++i < PADDING_LENGTH);
    }

    private static void MBPadTopRightLuma_c(uint8_t* pDst, int32_t kiStride, int32_t kiPicW)
    {
        uint8_t* pTopRight = pDst + kiPicW;
        uint8_t kuiTR = pTopRight[-1];
        int32_t i = 0;
        uint8_t* pTop = pTopRight;
        do
        {
            pTop -= kiStride;
            // pad pTop
            memcpy(pTop - 16, pTopRight - 16, 16);          // confirmed_safe_unsafe_usage
            memset(pTop, kuiTR, PADDING_LENGTH); //pTop Right
        } while (++i < PADDING_LENGTH);
    }

    private static void MBPadBottomLeftLuma_c(uint8_t* pDst, int32_t kiStride, int32_t kiPicH)
    {
        uint8_t* pDstLastLine = pDst + (kiPicH - 1) * kiStride;
        uint8_t kuiBL = pDstLastLine[0];
        int32_t i = 0;
        uint8_t* pBottom = pDstLastLine;
        do
        {
            pBottom += kiStride;
            // pad pBottom
            memcpy(pBottom, pDstLastLine, 16);          // confirmed_safe_unsafe_usage
            memset(pBottom - PADDING_LENGTH, kuiBL, PADDING_LENGTH); //pBottom left
        } while (++i < PADDING_LENGTH);
    }

    private static void MBPadBottomRightLuma_c(uint8_t* pDst, int32_t kiStride, int32_t kiPicW, int32_t kiPicH)
    {
        uint8_t* pDstLastLine = pDst + (kiPicH - 1) * kiStride + kiPicW;
        uint8_t kuiBR = pDstLastLine[-1];
        int32_t i = 0;
        uint8_t* pBottom = pDstLastLine;
        do
        {
            pBottom += kiStride;
            // pad pBottom
            memcpy(pBottom - 16, pDstLastLine - 16, 16);         // confirmed_safe_unsafe_usage
            memset(pBottom, kuiBR, PADDING_LENGTH); //pBottom Right
        } while (++i < PADDING_LENGTH);
    }

    private static void MBPadLeftLuma_c(uint8_t* pDst, int32_t kiStride, int32_t kiMbY)
    {
        uint8_t* pTmp = pDst + (kiMbY << 4) * kiStride;
        for (int32_t i = 0; i < 16; ++i)
        {
            // pad left
            memset(pTmp - PADDING_LENGTH, pTmp[0], PADDING_LENGTH);
            pTmp += kiStride;
        }
    }

    private static void MBPadRightLuma_c(uint8_t* pDst, int32_t kiStride, int32_t kiMbY, int32_t kiPicW)
    {
        uint8_t* pTmp = pDst + (kiMbY << 4) * kiStride + kiPicW;
        for (int32_t i = 0; i < 16; ++i)
        {
            // pad right
            memset(pTmp, pTmp[-1], PADDING_LENGTH);
            pTmp += kiStride;
        }
    }

    private static void MBPadTopChroma_c(uint8_t* pDst, int32_t kiStride, int32_t kiMbX)
    {
        uint8_t* pTopLine = pDst + (kiMbX << 3);
        int32_t i = 0;
        uint8_t* pTop = pTopLine;
        do
        {
            pTop -= kiStride;
            // pad pTop
            memcpy(pTop, pTopLine, 8);         // confirmed_safe_unsafe_usage
        } while (++i < CHROMA_PADDING_LENGTH);
    }

    private static void MBPadBottomChroma_c(uint8_t* pDst, int32_t kiStride, int32_t kiMbX, int32_t kiPicH)
    {
        uint8_t* pBottomLine = pDst + (kiPicH - 1) * kiStride + (kiMbX << 3);
        int32_t i = 0;
        uint8_t* pBottom = pBottomLine;
        do
        {
            pBottom += kiStride;
            // pad pBottom
            memcpy(pBottom, pBottomLine, 8);        // confirmed_safe_unsafe_usage
        } while (++i < CHROMA_PADDING_LENGTH);
    }

    private static void MBPadTopLeftChroma_c(uint8_t* pDst, int32_t kiStride)
    {
        uint8_t kuiTL = pDst[0];
        int32_t i = 0;
        uint8_t* pTopLeft = pDst;
        do
        {
            pTopLeft -= kiStride;
            // pad pTop
            memcpy(pTopLeft, pDst, 8);          // confirmed_safe_unsafe_usage
            memset(pTopLeft - CHROMA_PADDING_LENGTH, kuiTL, CHROMA_PADDING_LENGTH); //pTop left
        } while (++i < CHROMA_PADDING_LENGTH);
    }

    private static void MBPadTopRightChroma_c(uint8_t* pDst, int32_t kiStride, int32_t kiPicW)
    {
        uint8_t* pTopRight = pDst + kiPicW;
        uint8_t kuiTR = pTopRight[-1];
        int32_t i = 0;
        uint8_t* pTop = pTopRight;
        do
        {
            pTop -= kiStride;
            // pad pTop
            memcpy(pTop - 8, pTopRight - 8, 8);         // confirmed_safe_unsafe_usage
            memset(pTop, kuiTR, CHROMA_PADDING_LENGTH); //pTop Right
        } while (++i < CHROMA_PADDING_LENGTH);
    }

    private static void MBPadBottomLeftChroma_c(uint8_t* pDst, int32_t kiStride, int32_t kiPicH)
    {
        uint8_t* pDstLastLine = pDst + (kiPicH - 1) * kiStride;
        uint8_t kuiBL = pDstLastLine[0];
        int32_t i = 0;
        uint8_t* pBottom = pDstLastLine;
        do
        {
            pBottom += kiStride;
            // pad pBottom
            memcpy(pBottom, pDstLastLine, 8);         // confirmed_safe_unsafe_usage
            memset(pBottom - CHROMA_PADDING_LENGTH, kuiBL, CHROMA_PADDING_LENGTH); //pBottom left
        } while (++i < CHROMA_PADDING_LENGTH);
    }

    private static void MBPadBottomRightChroma_c(uint8_t* pDst, int32_t kiStride, int32_t kiPicW, int32_t kiPicH)
    {
        uint8_t* pDstLastLine = pDst + (kiPicH - 1) * kiStride + kiPicW;
        uint8_t kuiBR = pDstLastLine[-1];
        int32_t i = 0;
        uint8_t* pBottom = pDstLastLine;
        do
        {
            pBottom += kiStride;
            // pad pBottom
            memcpy(pBottom - 8, pDstLastLine - 8, 8);       // confirmed_safe_unsafe_usage
            memset(pBottom, kuiBR, CHROMA_PADDING_LENGTH); //pBottom Right
        } while (++i < CHROMA_PADDING_LENGTH);
    }

    private static void MBPadLeftChroma_c(uint8_t* pDst, int32_t kiStride, int32_t kiMbY)
    {
        uint8_t* pTmp = pDst + (kiMbY << 3) * kiStride;
        for (int32_t i = 0; i < 8; ++i)
        {
            // pad left
            memset(pTmp - CHROMA_PADDING_LENGTH, pTmp[0], CHROMA_PADDING_LENGTH);
            pTmp += kiStride;
        }
    }

    private static void MBPadRightChroma_c(uint8_t* pDst, int32_t kiStride, int32_t kiMbY, int32_t kiPicW)
    {
        uint8_t* pTmp = pDst + (kiMbY << 3) * kiStride + kiPicW;
        for (int32_t i = 0; i < 8; ++i)
        {
            // pad right
            memset(pTmp, pTmp[-1], CHROMA_PADDING_LENGTH);
            pTmp += kiStride;
        }
    }

    public static void PadMBLuma_c(uint8_t* pDst, int32_t kiStride, int32_t kiPicW, int32_t kiPicH,
        int32_t kiMbX, int32_t kiMbY, int32_t kiMBWidth, int32_t kiMBHeight)
    {
        if (kiMbX == 0 && kiMbY == 0)
        {
            MBPadTopLeftLuma_c(pDst, kiStride);
        }
        else if (kiMbY == 0 && kiMbX == kiMBWidth - 1)
        {
            MBPadTopRightLuma_c(pDst, kiStride, kiPicW);
        }
        else if (kiMbY == kiMBHeight - 1 && kiMbX == 0)
        {
            MBPadBottomLeftLuma_c(pDst, kiStride, kiPicH);
        }
        else if (kiMbY == kiMBHeight - 1 && kiMbX == kiMBWidth - 1)
        {
            MBPadBottomRightLuma_c(pDst, kiStride, kiPicW, kiPicH);
        }
        if (kiMbX == 0)
        {
            MBPadLeftLuma_c(pDst, kiStride, kiMbY);
        }
        else if (kiMbX == kiMBWidth - 1)
        {
            MBPadRightLuma_c(pDst, kiStride, kiMbY, kiPicW);
        }
        if (kiMbY == 0 && kiMbX > 0 && kiMbX < kiMBWidth - 1)
        {
            MBPadTopLuma_c(pDst, kiStride, kiMbX);
        }
        else if (kiMbY == kiMBHeight - 1 && kiMbX > 0 && kiMbX < kiMBWidth - 1)
        {
            MBPadBottomLuma_c(pDst, kiStride, kiMbX, kiPicH);
        }
    }

    public static void PadMBChroma_c(uint8_t* pDst, int32_t kiStride, int32_t kiPicW, int32_t kiPicH,
        int32_t kiMbX, int32_t kiMbY, int32_t kiMBWidth, int32_t kiMBHeight)
    {
        if (kiMbX == 0 && kiMbY == 0)
        {
            MBPadTopLeftChroma_c(pDst, kiStride);
        }
        else if (kiMbY == 0 && kiMbX == kiMBWidth - 1)
        {
            MBPadTopRightChroma_c(pDst, kiStride, kiPicW);
        }
        else if (kiMbY == kiMBHeight - 1 && kiMbX == 0)
        {
            MBPadBottomLeftChroma_c(pDst, kiStride, kiPicH);
        }
        else if (kiMbY == kiMBHeight - 1 && kiMbX == kiMBWidth - 1)
        {
            MBPadBottomRightChroma_c(pDst, kiStride, kiPicW, kiPicH);
        }
        if (kiMbX == 0)
        {
            MBPadLeftChroma_c(pDst, kiStride, kiMbY);
        }
        else if (kiMbX == kiMBWidth - 1)
        {
            MBPadRightChroma_c(pDst, kiStride, kiMbY, kiPicW);
        }
        if (kiMbY == 0 && kiMbX > 0 && kiMbX < kiMBWidth - 1)
        {
            MBPadTopChroma_c(pDst, kiStride, kiMbX);
        }
        else if (kiMbY == kiMBHeight - 1 && kiMbX > 0 && kiMbX < kiMBWidth - 1)
        {
            MBPadBottomChroma_c(pDst, kiStride, kiMbX, kiPicH);
        }
    }

    // rewrite it (split into luma & chroma) that is helpful for mmx/sse2 optimization perform, 9/27/2009
    private static void ExpandPictureLuma_c(uint8_t* pDst, int32_t kiStride, int32_t kiPicW, int32_t kiPicH)
    {
        uint8_t* pTmp = pDst;
        uint8_t* pDstLastLine = pTmp + (kiPicH - 1) * kiStride;
        const int32_t kiPaddingLen = PADDING_LENGTH;
        uint8_t kuiTL = pTmp[0];
        uint8_t kuiTR = pTmp[kiPicW - 1];
        uint8_t kuiBL = pDstLastLine[0];
        uint8_t kuiBR = pDstLastLine[kiPicW - 1];
        int32_t i = 0;

        do
        {
            int32_t kiStrides = (1 + i) * kiStride;
            uint8_t* pTop = pTmp - kiStrides;
            uint8_t* pBottom = pDstLastLine + kiStrides;

            // pad pTop and pBottom
            memcpy(pTop, pTmp, kiPicW);                // confirmed_safe_unsafe_usage
            memcpy(pBottom, pDstLastLine, kiPicW);     // confirmed_safe_unsafe_usage

            // pad corners
            memset(pTop - kiPaddingLen, kuiTL, kiPaddingLen); //pTop left
            memset(pTop + kiPicW, kuiTR, kiPaddingLen); //pTop right
            memset(pBottom - kiPaddingLen, kuiBL, kiPaddingLen); //pBottom left
            memset(pBottom + kiPicW, kuiBR, kiPaddingLen); //pBottom right

            ++i;
        } while (i < kiPaddingLen);

        // pad left and right
        i = 0;
        do
        {
            memset(pTmp - kiPaddingLen, pTmp[0], kiPaddingLen);
            memset(pTmp + kiPicW, pTmp[kiPicW - 1], kiPaddingLen);

            pTmp += kiStride;
            ++i;
        } while (i < kiPicH);
    }

    private static void ExpandPictureChroma_c(uint8_t* pDst, int32_t kiStride, int32_t kiPicW, int32_t kiPicH)
    {
        uint8_t* pTmp = pDst;
        uint8_t* pDstLastLine = pTmp + (kiPicH - 1) * kiStride;
        const int32_t kiPaddingLen = PADDING_LENGTH >> 1;
        uint8_t kuiTL = pTmp[0];
        uint8_t kuiTR = pTmp[kiPicW - 1];
        uint8_t kuiBL = pDstLastLine[0];
        uint8_t kuiBR = pDstLastLine[kiPicW - 1];
        int32_t i = 0;

        do
        {
            int32_t kiStrides = (1 + i) * kiStride;
            uint8_t* pTop = pTmp - kiStrides;
            uint8_t* pBottom = pDstLastLine + kiStrides;

            // pad pTop and pBottom
            memcpy(pTop, pTmp, kiPicW);                // confirmed_safe_unsafe_usage
            memcpy(pBottom, pDstLastLine, kiPicW);     // confirmed_safe_unsafe_usage

            // pad corners
            memset(pTop - kiPaddingLen, kuiTL, kiPaddingLen); //pTop left
            memset(pTop + kiPicW, kuiTR, kiPaddingLen); //pTop right
            memset(pBottom - kiPaddingLen, kuiBL, kiPaddingLen); //pBottom left
            memset(pBottom + kiPicW, kuiBR, kiPaddingLen); //pBottom right

            ++i;
        } while (i < kiPaddingLen);

        // pad left and right
        i = 0;
        do
        {
            memset(pTmp - kiPaddingLen, pTmp[0], kiPaddingLen);
            memset(pTmp + kiPicW, pTmp[kiPicW - 1], kiPaddingLen);

            pTmp += kiStride;
            ++i;
        } while (i < kiPicH);
    }

    public static void InitExpandPictureFunc(SExpandPicFunc* pExpandPicFunc, uint32_t kuiCPUFlag)
    {
        pExpandPicFunc->pfExpandLumaPicture = &ExpandPictureLuma_c;
        pExpandPicFunc->pfExpandChromaPicture[0] = &ExpandPictureChroma_c;
        pExpandPicFunc->pfExpandChromaPicture[1] = &ExpandPictureChroma_c;
        // X86_ASM / HAVE_NEON / HAVE_NEON_AARCH64 / HAVE_MMI variants: not ported.
    }

    //void ExpandReferencingPicture (SPicture* pPic, PExpandPictureFunc pExpLuma, PExpandPictureFunc pExpChrom[2]) {
    public static void ExpandReferencingPicture(uint8_t** pData, int32_t iWidth, int32_t iHeight, int32_t* iStride,
        PExpandPictureFunc pExpLuma, PExpandPictureFunc* pExpChrom)
    {
        /*local variable*/
        uint8_t* pPicY = pData[0];
        uint8_t* pPicCb = pData[1];
        uint8_t* pPicCr = pData[2];
        int32_t kiWidthY = iWidth;
        int32_t kiHeightY = iHeight;
        int32_t kiWidthUV = kiWidthY >> 1;
        int32_t kiHeightUV = kiHeightY >> 1;

        pExpLuma(pPicY, iStride[0], kiWidthY, kiHeightY);
        if (kiWidthUV >= 16)
        {
            // fix coding picture size as 16x16
            bool kbChrAligned = /*(iWidthUV >= 16) && */ ((kiWidthUV & 0x0F) == 0); // chroma planes: (16+iWidthUV) & 15
            pExpChrom[kbChrAligned ? 1 : 0](pPicCb, iStride[1], kiWidthUV, kiHeightUV);
            pExpChrom[kbChrAligned ? 1 : 0](pPicCr, iStride[2], kiWidthUV, kiHeightUV);
        }
        else
        {
            // fix coding picture size as 16x16
            ExpandPictureChroma_c(pPicCb, iStride[1], kiWidthUV, kiHeightUV);
            ExpandPictureChroma_c(pPicCr, iStride[2], kiWidthUV, kiHeightUV);
        }
    }
}
