//-----------------------------------------------------------------------------
// Filename: denoise.cs
//
// Description: Port of codec/processing/src/denoise/denoise.h, denoise.cpp
// and denoise_filter.cpp (E7): the denoise method (CDenoiser), a bilateral
// filter on luma and a weighted average on chroma, eight samples at a time,
// with a 3x3 Gaussian at the line ends. A native struct, as the port's other
// strategies are; of its IStrategy base it holds m_eMethod. Only the C
// filters are ported.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 27 Nov 2026  Claude          Created (E7).
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
 */

// typedef void (DenoiseFilterFunc) (uint8_t* pixels, int32_t stride); typedef DenoiseFilterFunc* DenoiseFilterFuncPtr;
global using unsafe DenoiseFilterFuncPtr = delegate*<byte*, int, void>;

namespace SIPSorcery.H264;

internal static unsafe partial class WelsVP
{
    public const int32_t DENOISE_GRAY_RADIUS = 1;
    public const int32_t DENOISE_GRAY_SIGMA = 2;
    public const int32_t UV_WINDOWS_RADIUS = 2;
    public const int32_t TAIL_OF_LINE8 = 7;
    public const int32_t DENOISE_Y_COMPONENT = 1;
    public const int32_t DENOISE_U_COMPONENT = 2;
    public const int32_t DENOISE_V_COMPONENT = 4;
    public const int32_t DENOISE_ALL_COMPONENT = 7;

    // ---- denoise_filter.cpp ----

    public static void BilateralLumaFilter8_c(uint8_t* pSample, int32_t iStride)
    {
        int32_t nSum = 0, nTotWeight = 0;
        int32_t iCenterSample = *pSample;
        uint8_t* pCurLine = pSample - iStride - DENOISE_GRAY_RADIUS;
        int32_t x, y;
        int32_t iCurSample, iCurWeight, iGreyDiff;
        uint8_t* aSample = stackalloc uint8_t[8];

        for (int32_t i = 0; i < 8; i++)
        {
            nSum = 0;
            nTotWeight = 0;
            iCenterSample = *pSample;
            pCurLine = pSample - iStride - DENOISE_GRAY_RADIUS;
            for (y = 0; y < 3; y++)
            {
                for (x = 0; x < 3; x++)
                {
                    if (x == 1 && y == 1) continue; // except center point
                    iCurSample = pCurLine[x];
                    iCurWeight = WELS_ABS(iCurSample - iCenterSample);
                    iGreyDiff = 32 - iCurWeight;
                    if (iGreyDiff < 0) continue;
                    else iCurWeight = (iGreyDiff * iGreyDiff) >> 5;
                    nSum += iCurSample * iCurWeight;
                    nTotWeight += iCurWeight;
                }
                pCurLine += iStride;
            }
            nTotWeight = 256 - nTotWeight;
            nSum += iCenterSample * nTotWeight;
            aSample[i] = (uint8_t)(nSum >> 8);
            pSample++;
        }
        memcpy(pSample - 8, aSample, 8);
    }

    /***************************************************************************
    5x5 filter:
    1   1   2   1   1
    1   2   4   2   1
    2   4   20  4   2
    1   2   4   2   1
    1   1   2   1   1
    ***************************************************************************/
    private static int32_t SUM_LINE1(uint8_t* pSample) => pSample[0] + (pSample[1]) + (pSample[2] << 1) + pSample[3] + pSample[4];
    private static int32_t SUM_LINE2(uint8_t* pSample) => pSample[0] + (pSample[1] << 1) + (pSample[2] << 2) + (pSample[3] << 1) + pSample[4];
    private static int32_t SUM_LINE3(uint8_t* pSample) => (pSample[0] << 1) + (pSample[1] << 2) + (pSample[2] * 20) + (pSample[3] << 2) + (pSample[4] << 1);

    public static void WaverageChromaFilter8_c(uint8_t* pSample, int32_t iStride)
    {
        int32_t sum;
        uint8_t* pStartPixels = pSample - UV_WINDOWS_RADIUS * iStride - UV_WINDOWS_RADIUS;
        uint8_t* pCurLine1 = pStartPixels;
        uint8_t* pCurLine2 = pCurLine1 + iStride;
        uint8_t* pCurLine3 = pCurLine2 + iStride;
        uint8_t* pCurLine4 = pCurLine3 + iStride;
        uint8_t* pCurLine5 = pCurLine4 + iStride;
        uint8_t* aSample = stackalloc uint8_t[8];

        for (int32_t i = 0; i < 8; i++)
        {
            sum = SUM_LINE1(pCurLine1 + i) + SUM_LINE2(pCurLine2 + i) + SUM_LINE3(pCurLine3 + i)
                  + SUM_LINE2(pCurLine4 + i) + SUM_LINE1(pCurLine5 + i);
            aSample[i] = (uint8_t)(sum >> 6);
            pSample++;
        }
        memcpy(pSample - 8, aSample, 8);
    }

    /***************************************************************************
    edge of y/uv use a 3x3 Gauss filter, radius = 1:
    1   2   1
    2   4   2
    1   2   1
    ***************************************************************************/
    public static void Gauss3x3Filter(uint8_t* pSrc, int32_t iStride)
    {
        int32_t nSum = 0;
        uint8_t* pCurLine1 = pSrc - iStride - 1;
        uint8_t* pCurLine2 = pCurLine1 + iStride;
        uint8_t* pCurLine3 = pCurLine2 + iStride;

        nSum = pCurLine1[0] + (pCurLine1[1] << 1) + pCurLine1[2] +
               (pCurLine2[0] << 1) + (pCurLine2[1] << 2) + (pCurLine2[2] << 1) +
               pCurLine3[0] + (pCurLine3[1] << 1) + pCurLine3[2];
        *pSrc = (uint8_t)(nSum >> 4);
    }
}

/// <summary>TagDenoiseFuncs.</summary>
internal unsafe struct SDenoiseFuncs
{
    public DenoiseFilterFuncPtr pfBilateralLumaFilter8;//on 8 samples
    public DenoiseFilterFuncPtr pfWaverageChromaFilter8;//on 8 samples
}

/// <summary>CDenoiser (see the file header).</summary>
internal unsafe struct CDenoiser
{
    public EMethods m_eMethod;      // IStrategy

#pragma warning disable CS0414 // set and never read upstream too
    private float m_fSigmaGrey;                  //sigma for grey scale similarity, suggestion 2.5-3
#pragma warning restore CS0414
    private uint16_t m_uiSpaceRadius;            //filter windows radius: 1-3x3, 2-5x5,3-7x7. Larger size, slower speed
    private uint16_t m_uiType;                   //do denoising on which component 1-Y, 2-U, 4-V; 7-YUV, 3-YU, 5-YV, 6-UV
    private SDenoiseFuncs m_pfDenoise;
    private int32_t m_CPUFlag;

    public CDenoiser(int32_t iCpuFlag)
    {
        m_CPUFlag = iCpuFlag;
        m_eMethod = EMethods.METHOD_DENOISE;
        m_pfDenoise = default;

        m_uiSpaceRadius = WelsVP.DENOISE_GRAY_RADIUS;
        m_fSigmaGrey = WelsVP.DENOISE_GRAY_SIGMA;
        m_uiType = WelsVP.DENOISE_ALL_COMPONENT;
        InitDenoiseFunc(ref m_pfDenoise, m_CPUFlag);
    }

    // The SIMD versions (X86_ASM) are not ported: the reference is the C-only build.
    private static void InitDenoiseFunc(ref SDenoiseFuncs denoiser, int32_t iCpuFlag)
    {
        denoiser.pfBilateralLumaFilter8 = &WelsVP.BilateralLumaFilter8_c;
        denoiser.pfWaverageChromaFilter8 = &WelsVP.WaverageChromaFilter8_c;
    }

    public readonly EResult Process(int32_t iType, SPixMap* pSrc, SPixMap* dst)
    {
        uint8_t* pSrcY = pSrc->pPixel[0];
        uint8_t* pSrcU = pSrc->pPixel[1];
        uint8_t* pSrcV = pSrc->pPixel[2];
        if (pSrcY == null || pSrcU == null || pSrcV == null)
        {
            return EResult.RET_INVALIDPARAM;
        }

        int32_t iWidthY = pSrc->sRect.iRectWidth;
        int32_t iHeightY = pSrc->sRect.iRectHeight;
        int32_t iWidthUV = iWidthY >> 1;
        int32_t iHeightUV = iHeightY >> 1;

        if ((m_uiType & WelsVP.DENOISE_Y_COMPONENT) != 0)
            BilateralDenoiseLuma(pSrcY, iWidthY, iHeightY, pSrc->iStride[0]);

        if ((m_uiType & WelsVP.DENOISE_U_COMPONENT) != 0)
            WaverageDenoiseChroma(pSrcU, iWidthUV, iHeightUV, pSrc->iStride[1]);

        if ((m_uiType & WelsVP.DENOISE_V_COMPONENT) != 0)
            WaverageDenoiseChroma(pSrcV, iWidthUV, iHeightUV, pSrc->iStride[2]);

        return EResult.RET_SUCCESS;
    }

    private readonly void BilateralDenoiseLuma(uint8_t* pSrcY, int32_t iWidth, int32_t iHeight, int32_t iStride)
    {
        int32_t w;

        pSrcY = pSrcY + m_uiSpaceRadius * iStride;
        for (int32_t h = m_uiSpaceRadius; h < iHeight - m_uiSpaceRadius; h++)
        {
            for (w = m_uiSpaceRadius; w < iWidth - m_uiSpaceRadius - WelsVP.TAIL_OF_LINE8; w += 8)
            {
                m_pfDenoise.pfBilateralLumaFilter8(pSrcY + w, iStride);
            }
            for (; w < iWidth - m_uiSpaceRadius; w++)
            {
                WelsVP.Gauss3x3Filter(pSrcY + w, iStride);
            }
            pSrcY += iStride;
        }
    }

    private readonly void WaverageDenoiseChroma(uint8_t* pSrcUV, int32_t iWidth, int32_t iHeight, int32_t iStride)
    {
        int32_t w;

        pSrcUV = pSrcUV + WelsVP.UV_WINDOWS_RADIUS * iStride;
        for (int32_t h = WelsVP.UV_WINDOWS_RADIUS; h < iHeight - WelsVP.UV_WINDOWS_RADIUS; h++)
        {
            for (w = WelsVP.UV_WINDOWS_RADIUS; w < iWidth - WelsVP.UV_WINDOWS_RADIUS - WelsVP.TAIL_OF_LINE8; w += 8)
            {
                m_pfDenoise.pfWaverageChromaFilter8(pSrcUV + w, iStride);
            }
            for (; w < iWidth - WelsVP.UV_WINDOWS_RADIUS; w++)
            {
                WelsVP.Gauss3x3Filter(pSrcUV + w, iStride);
            }
            pSrcUV += iStride;
        }
    }
}
