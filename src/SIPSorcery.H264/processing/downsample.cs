//-----------------------------------------------------------------------------
// Filename: downsample.cs
//
// Description: Port of codec/processing/src/downsample/downsample.h,
// downsample.cpp and downsamplefuncs.cpp (E7): the downsampling method
// (CDownsampling), which halves through its two sample buffers while it can
// and finishes with the general bilinear downsamplers, and the C kernels
// (dyadic, quarter, one third, and the fast and accurate general bilinear
// ones). A native struct, as the port's other strategies are; of its
// IStrategy base it holds m_eMethod. Only the C kernels are ported, so the
// SIMD wrappers (GeneralBilinearDownsamplerWrap) are not.
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
 *     Copyright (c)  2008-2013, Cisco Systems
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
 *  downsample_yuv.c
 *
 *  Abstract
 *      Implementation for source yuv data downsampling used before spatial encoding.
 *
 *************************************************************************************
 */

// typedef void (HalveDownsampleFunc) (uint8_t* pDst, const int32_t kiDstStride, uint8_t* pSrc,
//     const int32_t kiSrcStride, const int32_t kiSrcWidth, const int32_t kiSrcHeight);
global using unsafe PHalveDownsampleFunc = delegate*<byte*, int, byte*, int, int, int, void>;
// typedef void (SpecificDownsampleFunc) (uint8_t* pDst, const int32_t kiDstStride, uint8_t* pSrc,
//     const int32_t kiSrcStride, const int32_t kiSrcWidth, const int32_t kiHeight);
global using unsafe PSpecificDownsampleFunc = delegate*<byte*, int, byte*, int, int, int, void>;
// typedef void (GeneralDownsampleFunc) (uint8_t* pDst, const int32_t kiDstStride, const int32_t kiDstWidth,
//     const int32_t kiDstHeight, uint8_t* pSrc, const int32_t kiSrcStride, const int32_t kiSrcWidth, const int32_t kiSrcHeight);
global using unsafe PGeneralDownsampleFunc = delegate*<byte*, int, int, int, byte*, int, int, int, void>;

using System.Diagnostics;

namespace SIPSorcery.H264;

internal static unsafe partial class WelsVP
{
    // ---- downsamplefuncs.cpp ----

    public static void DyadicBilinearDownsampler_c(uint8_t* pDst, int32_t kiDstStride,
                                                   uint8_t* pSrc, int32_t kiSrcStride,
                                                   int32_t kiSrcWidth, int32_t kiSrcHeight)
    {
        uint8_t* pDstLine = pDst;
        uint8_t* pSrcLine = pSrc;
        int32_t kiSrcStridex2 = kiSrcStride << 1;
        int32_t kiDstWidth = kiSrcWidth >> 1;
        int32_t kiDstHeight = kiSrcHeight >> 1;

        for (int32_t j = 0; j < kiDstHeight; j++)
        {
            for (int32_t i = 0; i < kiDstWidth; i++)
            {
                int32_t kiSrcX = i << 1;
                int32_t kiTempRow1 = (pSrcLine[kiSrcX] + pSrcLine[kiSrcX + 1] + 1) >> 1;
                int32_t kiTempRow2 = (pSrcLine[kiSrcX + kiSrcStride] + pSrcLine[kiSrcX + kiSrcStride + 1] + 1) >> 1;

                pDstLine[i] = (uint8_t)((kiTempRow1 + kiTempRow2 + 1) >> 1);
            }
            pDstLine += kiDstStride;
            pSrcLine += kiSrcStridex2;
        }
    }

    public static void DyadicBilinearQuarterDownsampler_c(uint8_t* pDst, int32_t kiDstStride,
                                                          uint8_t* pSrc, int32_t kiSrcStride,
                                                          int32_t kiSrcWidth, int32_t kiSrcHeight)
    {
        uint8_t* pDstLine = pDst;
        uint8_t* pSrcLine = pSrc;
        int32_t kiSrcStridex4 = kiSrcStride << 2;
        int32_t kiDstWidth = kiSrcWidth >> 2;
        int32_t kiDstHeight = kiSrcHeight >> 2;

        for (int32_t j = 0; j < kiDstHeight; j++)
        {
            for (int32_t i = 0; i < kiDstWidth; i++)
            {
                int32_t kiSrcX = i << 2;
                int32_t kiTempRow1 = (pSrcLine[kiSrcX] + pSrcLine[kiSrcX + 1] + 1) >> 1;
                int32_t kiTempRow2 = (pSrcLine[kiSrcX + kiSrcStride] + pSrcLine[kiSrcX + kiSrcStride + 1] + 1) >> 1;

                pDstLine[i] = (uint8_t)((kiTempRow1 + kiTempRow2 + 1) >> 1);
            }
            pDstLine += kiDstStride;
            pSrcLine += kiSrcStridex4;
        }
    }

    public static void DyadicBilinearOneThirdDownsampler_c(uint8_t* pDst, int32_t kiDstStride,
                                                           uint8_t* pSrc, int32_t kiSrcStride,
                                                           int32_t kiSrcWidth, int32_t kiDstHeight)
    {
        uint8_t* pDstLine = pDst;
        uint8_t* pSrcLine = pSrc;
        int32_t kiSrcStridex3 = kiSrcStride * 3;
        int32_t kiDstWidth = kiSrcWidth / 3;

        for (int32_t j = 0; j < kiDstHeight; j++)
        {
            for (int32_t i = 0; i < kiDstWidth; i++)
            {
                int32_t kiSrcX = i * 3;
                int32_t kiTempRow1 = (pSrcLine[kiSrcX] + pSrcLine[kiSrcX + 1] + 1) >> 1;
                int32_t kiTempRow2 = (pSrcLine[kiSrcX + kiSrcStride] + pSrcLine[kiSrcX + kiSrcStride + 1] + 1) >> 1;

                pDstLine[i] = (uint8_t)((kiTempRow1 + kiTempRow2 + 1) >> 1);
            }
            pDstLine += kiDstStride;
            pSrcLine += kiSrcStridex3;
        }
    }

    public static void GeneralBilinearFastDownsampler_c(uint8_t* pDst, int32_t kiDstStride, int32_t kiDstWidth,
                                                        int32_t kiDstHeight,
                                                        uint8_t* pSrc, int32_t kiSrcStride, int32_t kiSrcWidth, int32_t kiSrcHeight)
    {
        const uint32_t kuiScaleBitWidth = 16, kuiScaleBitHeight = 15;
        const uint32_t kuiScaleWidth = (1 << (int32_t)kuiScaleBitWidth), kuiScaleHeight = (1 << (int32_t)kuiScaleBitHeight);
        // (float)a / (float)b * kuiScaleWidth: a float product (the uint32 converts to float), rounded in double.
        int32_t fScalex = WELS_ROUND((float)kiSrcWidth / (float)kiDstWidth * kuiScaleWidth);
        int32_t fScaley = WELS_ROUND((float)kiSrcHeight / (float)kiDstHeight * kuiScaleHeight);
        uint32_t x;
        int32_t iYInverse, iXInverse;

        uint8_t* pByDst = pDst;
        uint8_t* pByLineDst = pDst;

        iYInverse = 1 << ((int32_t)kuiScaleBitHeight - 1);
        for (int32_t i = 0; i < kiDstHeight - 1; i++)
        {
            int32_t iYy = iYInverse >> (int32_t)kuiScaleBitHeight;
            int32_t fv = iYInverse & (int32_t)(kuiScaleHeight - 1);

            uint8_t* pBySrc = pSrc + iYy * kiSrcStride;

            pByDst = pByLineDst;
            iXInverse = 1 << ((int32_t)kuiScaleBitWidth - 1);
            for (int32_t j = 0; j < kiDstWidth - 1; j++)
            {
                int32_t iXx = iXInverse >> (int32_t)kuiScaleBitWidth;
                int32_t iFu = iXInverse & (int32_t)(kuiScaleWidth - 1);

                uint8_t* pByCurrent = pBySrc + iXx;
                uint8_t a, b, c, d;

                a = *pByCurrent;
                b = *(pByCurrent + 1);
                c = *(pByCurrent + kiSrcStride);
                d = *(pByCurrent + kiSrcStride + 1);

                // uint32 arithmetic throughout, as in C (the int operands convert to unsigned).
                x = (((uint32_t)(kuiScaleWidth - 1 - (uint32_t)iFu)) * (kuiScaleHeight - 1 - (uint32_t)fv) >> (int32_t)kuiScaleBitWidth) * a;
                x += (((uint32_t)(iFu)) * (kuiScaleHeight - 1 - (uint32_t)fv) >> (int32_t)kuiScaleBitWidth) * b;
                x += (((uint32_t)(kuiScaleWidth - 1 - (uint32_t)iFu)) * (uint32_t)(fv) >> (int32_t)kuiScaleBitWidth) * c;
                x += (((uint32_t)(iFu)) * (uint32_t)(fv) >> (int32_t)kuiScaleBitWidth) * d;
                x >>= ((int32_t)kuiScaleBitHeight - 1);
                x += 1;
                x >>= 1;
                //x = (((__int64)(SCALE_BIG - 1 - iFu))*(SCALE_BIG - 1 - fv)*a + ((__int64)iFu)*(SCALE_BIG - 1 -fv)*b + ((__int64)(SCALE_BIG - 1 -iFu))*fv*c +
                // ((__int64)iFu)*fv*d + (1 << (2*SCALE_BIT_BIG-1)) ) >> (2*SCALE_BIT_BIG);
                x = WELS_MIN(WELS_MAX(x, 0u), 255u);
                *pByDst++ = (uint8_t)x;

                iXInverse += fScalex;
            }
            *pByDst = *(pBySrc + (iXInverse >> (int32_t)kuiScaleBitWidth));
            pByLineDst += kiDstStride;
            iYInverse += fScaley;
        }

        // last row special
        {
            int32_t iYy = iYInverse >> (int32_t)kuiScaleBitHeight;
            uint8_t* pBySrc = pSrc + iYy * kiSrcStride;

            pByDst = pByLineDst;
            iXInverse = 1 << ((int32_t)kuiScaleBitWidth - 1);
            for (int32_t j = 0; j < kiDstWidth; j++)
            {
                int32_t iXx = iXInverse >> (int32_t)kuiScaleBitWidth;
                *pByDst++ = *(pBySrc + iXx);

                iXInverse += fScalex;
            }
        }
    }

    public static void GeneralBilinearAccurateDownsampler_c(uint8_t* pDst, int32_t kiDstStride, int32_t kiDstWidth,
                                                            int32_t kiDstHeight,
                                                            uint8_t* pSrc, int32_t kiSrcStride, int32_t kiSrcWidth, int32_t kiSrcHeight)
    {
        const int32_t kiScaleBit = 15;
        const int32_t kiScale = (1 << kiScaleBit);
        int32_t iScalex = WELS_ROUND((float)kiSrcWidth / (float)kiDstWidth * kiScale);
        int32_t iScaley = WELS_ROUND((float)kiSrcHeight / (float)kiDstHeight * kiScale);
        int64_t x;
        int32_t iYInverse, iXInverse;

        uint8_t* pByDst = pDst;
        uint8_t* pByLineDst = pDst;

        iYInverse = 1 << (kiScaleBit - 1);
        for (int32_t i = 0; i < kiDstHeight - 1; i++)
        {
            int32_t iYy = iYInverse >> kiScaleBit;
            int32_t iFv = iYInverse & (kiScale - 1);

            uint8_t* pBySrc = pSrc + iYy * kiSrcStride;

            pByDst = pByLineDst;
            iXInverse = 1 << (kiScaleBit - 1);
            for (int32_t j = 0; j < kiDstWidth - 1; j++)
            {
                int32_t iXx = iXInverse >> kiScaleBit;
                int32_t iFu = iXInverse & (kiScale - 1);

                uint8_t* pByCurrent = pBySrc + iXx;
                uint8_t a, b, c, d;

                a = *pByCurrent;
                b = *(pByCurrent + 1);
                c = *(pByCurrent + kiSrcStride);
                d = *(pByCurrent + kiSrcStride + 1);

                x = (((int64_t)(kiScale - 1 - iFu)) * (kiScale - 1 - iFv) * a + ((int64_t)iFu) * (kiScale - 1 - iFv) * b + ((int64_t)(
                         kiScale - 1 - iFu)) * iFv * c +
                     ((int64_t)iFu) * iFv * d + (int64_t)(1 << (2 * kiScaleBit - 1))) >> (2 * kiScaleBit);
                x = WELS_MIN(WELS_MAX(x, 0L), 255L);
                *pByDst++ = (uint8_t)x;

                iXInverse += iScalex;
            }
            *pByDst = *(pBySrc + (iXInverse >> kiScaleBit));
            pByLineDst += kiDstStride;
            iYInverse += iScaley;
        }

        // last row special
        {
            int32_t iYy = iYInverse >> kiScaleBit;
            uint8_t* pBySrc = pSrc + iYy * kiSrcStride;

            pByDst = pByLineDst;
            iXInverse = 1 << (kiScaleBit - 1);
            for (int32_t j = 0; j < kiDstWidth; j++)
            {
                int32_t iXx = iXInverse >> kiScaleBit;
                *pByDst++ = *(pBySrc + iXx);

                iXInverse += iScalex;
            }
        }
    }
}

/// <summary>SDownsampleFuncs.</summary>
internal unsafe struct SDownsampleFuncs
{
    public PHalveDownsampleFunc pfHalfAverageWidthx32;
    public PHalveDownsampleFunc pfHalfAverageWidthx16;
    public PSpecificDownsampleFunc pfOneThirdDownsampler;
    public PSpecificDownsampleFunc pfQuarterDownsampler;
    public PGeneralDownsampleFunc pfGeneralRatioLuma;
    public PGeneralDownsampleFunc pfGeneralRatioChroma;
}

/// <summary>CDownsampling (see the file header).</summary>
internal unsafe struct CDownsampling
{
    private const int32_t MAX_SAMPLE_WIDTH = 1920;
    private const int32_t MAX_SAMPLE_HEIGHT = 1088;

    public EMethods m_eMethod;      // IStrategy

    private SDownsampleFuncs m_pfDownsample;
    private int32_t m_iCPUFlag;
    private fixed long m_pSampleBuffer[2 * 3];  // uint8_t* m_pSampleBuffer[2][3]
    private bool m_bNoSampleBuffer;

    public CDownsampling(int32_t iCpuFlag)
    {
        m_iCPUFlag = iCpuFlag;
        m_eMethod = EMethods.METHOD_DOWNSAMPLE;
        m_pfDownsample = default;
        InitDownsampleFuncs(ref m_pfDownsample, m_iCPUFlag);
        for (int32_t i = 0; i < 6; i++)
        {
            m_pSampleBuffer[i] = 0;
        }
        m_bNoSampleBuffer = false;
        m_bNoSampleBuffer = AllocateSampleBuffer();
    }

    /// <summary>~CDownsampling.</summary>
    public void Destroy() => FreeSampleBuffer();

    private uint8_t* SampleBuffer(int32_t i, int32_t c) => (uint8_t*)m_pSampleBuffer[i * 3 + c];

    private bool AllocateSampleBuffer()
    {
        for (int32_t i = 0; i < 2; i++)
        {
            m_pSampleBuffer[i * 3 + 0] = (long)WelsVP.WelsMalloc(MAX_SAMPLE_WIDTH * MAX_SAMPLE_HEIGHT);
            if (m_pSampleBuffer[i * 3 + 0] == 0)
                goto FREE_RET;
            m_pSampleBuffer[i * 3 + 1] = (long)WelsVP.WelsMalloc(MAX_SAMPLE_WIDTH * MAX_SAMPLE_HEIGHT / 4);
            if (m_pSampleBuffer[i * 3 + 1] == 0)
                goto FREE_RET;
            m_pSampleBuffer[i * 3 + 2] = (long)WelsVP.WelsMalloc(MAX_SAMPLE_WIDTH * MAX_SAMPLE_HEIGHT / 4);
            if (m_pSampleBuffer[i * 3 + 2] == 0)
                goto FREE_RET;
        }
        return false;
    FREE_RET:
        FreeSampleBuffer();
        return true;
    }

    private void FreeSampleBuffer()
    {
        for (int32_t i = 0; i < 2; i++)
        {
            WelsVP.WelsFree((void*)m_pSampleBuffer[i * 3 + 0]);
            m_pSampleBuffer[i * 3 + 0] = 0;
            WelsVP.WelsFree((void*)m_pSampleBuffer[i * 3 + 1]);
            m_pSampleBuffer[i * 3 + 1] = 0;
            WelsVP.WelsFree((void*)m_pSampleBuffer[i * 3 + 2]);
            m_pSampleBuffer[i * 3 + 2] = 0;
        }
    }

    // The SIMD versions (X86_ASM, HAVE_NEON, ...) are not ported: the reference is the C-only build.
    private static void InitDownsampleFuncs(ref SDownsampleFuncs sDownsampleFunc, int32_t iCpuFlag)
    {
        sDownsampleFunc.pfHalfAverageWidthx32 = &WelsVP.DyadicBilinearDownsampler_c;
        sDownsampleFunc.pfHalfAverageWidthx16 = &WelsVP.DyadicBilinearDownsampler_c;
        sDownsampleFunc.pfOneThirdDownsampler = &WelsVP.DyadicBilinearOneThirdDownsampler_c;
        sDownsampleFunc.pfQuarterDownsampler = &WelsVP.DyadicBilinearQuarterDownsampler_c;
        sDownsampleFunc.pfGeneralRatioChroma = &WelsVP.GeneralBilinearAccurateDownsampler_c;
        sDownsampleFunc.pfGeneralRatioLuma = &WelsVP.GeneralBilinearFastDownsampler_c;
    }

    public EResult Process(int32_t iType, SPixMap* pSrcPixMap, SPixMap* pDstPixMap)
    {
        int32_t iSrcWidthY = pSrcPixMap->sRect.iRectWidth;
        int32_t iSrcHeightY = pSrcPixMap->sRect.iRectHeight;
        int32_t iDstWidthY = pDstPixMap->sRect.iRectWidth;
        int32_t iDstHeightY = pDstPixMap->sRect.iRectHeight;

        int32_t iSrcWidthUV = iSrcWidthY >> 1;
        int32_t iSrcHeightUV = iSrcHeightY >> 1;
        int32_t iDstWidthUV = iDstWidthY >> 1;
        int32_t iDstHeightUV = iDstHeightY >> 1;

        if (iSrcWidthY <= iDstWidthY || iSrcHeightY <= iDstHeightY)
        {
            return EResult.RET_INVALIDPARAM;
        }
        if ((iSrcWidthY >> 1) > MAX_SAMPLE_WIDTH || (iSrcHeightY >> 1) > MAX_SAMPLE_HEIGHT || m_bNoSampleBuffer)
        {
            if ((iSrcWidthY >> 1) == iDstWidthY && (iSrcHeightY >> 1) == iDstHeightY)
            {
                // use half average functions
                DownsampleHalfAverage(pDstPixMap->pPixel[0], pDstPixMap->iStride[0],
                                      pSrcPixMap->pPixel[0], pSrcPixMap->iStride[0], iSrcWidthY, iSrcHeightY);
                DownsampleHalfAverage(pDstPixMap->pPixel[1], pDstPixMap->iStride[1],
                                      pSrcPixMap->pPixel[1], pSrcPixMap->iStride[1], iSrcWidthUV, iSrcHeightUV);
                DownsampleHalfAverage(pDstPixMap->pPixel[2], pDstPixMap->iStride[2],
                                      pSrcPixMap->pPixel[2], pSrcPixMap->iStride[2], iSrcWidthUV, iSrcHeightUV);
            }
            else if ((iSrcWidthY >> 2) == iDstWidthY && (iSrcHeightY >> 2) == iDstHeightY)
            {
                m_pfDownsample.pfQuarterDownsampler(pDstPixMap->pPixel[0], pDstPixMap->iStride[0],
                                                    pSrcPixMap->pPixel[0], pSrcPixMap->iStride[0], iSrcWidthY, iSrcHeightY);

                m_pfDownsample.pfQuarterDownsampler(pDstPixMap->pPixel[1], pDstPixMap->iStride[1],
                                                    pSrcPixMap->pPixel[1], pSrcPixMap->iStride[1], iSrcWidthUV, iSrcHeightUV);

                m_pfDownsample.pfQuarterDownsampler(pDstPixMap->pPixel[2], pDstPixMap->iStride[2],
                                                    pSrcPixMap->pPixel[2], pSrcPixMap->iStride[2], iSrcWidthUV, iSrcHeightUV);
            }
            else if ((iSrcWidthY / 3) == iDstWidthY && (iSrcHeightY / 3) == iDstHeightY)
            {
                m_pfDownsample.pfOneThirdDownsampler(pDstPixMap->pPixel[0], pDstPixMap->iStride[0],
                                                     pSrcPixMap->pPixel[0], pSrcPixMap->iStride[0], iSrcWidthY, iDstHeightY);

                m_pfDownsample.pfOneThirdDownsampler(pDstPixMap->pPixel[1], pDstPixMap->iStride[1],
                                                     pSrcPixMap->pPixel[1], pSrcPixMap->iStride[1], iSrcWidthUV, iDstHeightUV);

                m_pfDownsample.pfOneThirdDownsampler(pDstPixMap->pPixel[2], pDstPixMap->iStride[2],
                                                     pSrcPixMap->pPixel[2], pSrcPixMap->iStride[2], iSrcWidthUV, iDstHeightUV);
            }
            else
            {
                m_pfDownsample.pfGeneralRatioLuma(pDstPixMap->pPixel[0], pDstPixMap->iStride[0], iDstWidthY, iDstHeightY,
                                                  pSrcPixMap->pPixel[0], pSrcPixMap->iStride[0], iSrcWidthY, iSrcHeightY);

                m_pfDownsample.pfGeneralRatioChroma(pDstPixMap->pPixel[1], pDstPixMap->iStride[1], iDstWidthUV, iDstHeightUV,
                                                    pSrcPixMap->pPixel[1], pSrcPixMap->iStride[1], iSrcWidthUV, iSrcHeightUV);

                m_pfDownsample.pfGeneralRatioChroma(pDstPixMap->pPixel[2], pDstPixMap->iStride[2], iDstWidthUV, iDstHeightUV,
                                                    pSrcPixMap->pPixel[2], pSrcPixMap->iStride[2], iSrcWidthUV, iSrcHeightUV);
            }
        }
        else
        {
            int32_t iIdx = 0;
            int32_t iHalfSrcWidth = iSrcWidthY >> 1;
            int32_t iHalfSrcHeight = iSrcHeightY >> 1;
            uint8_t* pSrcY = pSrcPixMap->pPixel[0];
            uint8_t* pSrcU = pSrcPixMap->pPixel[1];
            uint8_t* pSrcV = pSrcPixMap->pPixel[2];
            int32_t iSrcStrideY = pSrcPixMap->iStride[0];
            int32_t iSrcStrideU = pSrcPixMap->iStride[1];
            int32_t iSrcStrideV = pSrcPixMap->iStride[2];

            int32_t iDstStrideY = pDstPixMap->iStride[0];
            int32_t iDstStrideU = pDstPixMap->iStride[1];
            int32_t iDstStrideV = pDstPixMap->iStride[2];

            uint8_t* pDstY = SampleBuffer(iIdx, 0);
            uint8_t* pDstU = SampleBuffer(iIdx, 1);
            uint8_t* pDstV = SampleBuffer(iIdx, 2);
            iIdx++;
            do
            {
                if ((iHalfSrcWidth == iDstWidthY) && (iHalfSrcHeight == iDstHeightY))
                { //end
                    // use half average functions
                    DownsampleHalfAverage(pDstPixMap->pPixel[0], pDstPixMap->iStride[0],
                                          pSrcY, iSrcStrideY, iSrcWidthY, iSrcHeightY);
                    DownsampleHalfAverage(pDstPixMap->pPixel[1], pDstPixMap->iStride[1],
                                          pSrcU, iSrcStrideU, iSrcWidthUV, iSrcHeightUV);
                    DownsampleHalfAverage(pDstPixMap->pPixel[2], pDstPixMap->iStride[2],
                                          pSrcV, iSrcStrideV, iSrcWidthUV, iSrcHeightUV);
                    break;
                }
                else if ((iHalfSrcWidth > iDstWidthY) && (iHalfSrcHeight > iDstHeightY))
                {
                    // use half average functions
                    iDstStrideY = WELS_ALIGN(iHalfSrcWidth, 32);
                    iDstStrideU = WELS_ALIGN(iHalfSrcWidth >> 1, 32);
                    iDstStrideV = WELS_ALIGN(iHalfSrcWidth >> 1, 32);
                    DownsampleHalfAverage(pDstY, iDstStrideY,
                                          pSrcY, iSrcStrideY, iSrcWidthY, iSrcHeightY);
                    DownsampleHalfAverage(pDstU, iDstStrideU,
                                          pSrcU, iSrcStrideU, iSrcWidthUV, iSrcHeightUV);
                    DownsampleHalfAverage(pDstV, iDstStrideV,
                                          pSrcV, iSrcStrideV, iSrcWidthUV, iSrcHeightUV);

                    pSrcY = pDstY;
                    pSrcU = pDstU;
                    pSrcV = pDstV;

                    iSrcWidthY = iHalfSrcWidth;
                    iSrcWidthUV = iHalfSrcWidth >> 1;
                    iSrcHeightY = iHalfSrcHeight;
                    iSrcHeightUV = iHalfSrcHeight >> 1;

                    iSrcStrideY = iDstStrideY;
                    iSrcStrideU = iDstStrideU;
                    iSrcStrideV = iDstStrideV;

                    iHalfSrcWidth >>= 1;
                    iHalfSrcHeight >>= 1;

                    iIdx = iIdx % 2;
                    pDstY = SampleBuffer(iIdx, 0);
                    pDstU = SampleBuffer(iIdx, 1);
                    pDstV = SampleBuffer(iIdx, 2);
                    iIdx++;
                }
                else
                {
                    m_pfDownsample.pfGeneralRatioLuma(pDstPixMap->pPixel[0], pDstPixMap->iStride[0], iDstWidthY, iDstHeightY,
                                                      pSrcY, iSrcStrideY, iSrcWidthY, iSrcHeightY);

                    m_pfDownsample.pfGeneralRatioChroma(pDstPixMap->pPixel[1], pDstPixMap->iStride[1], iDstWidthUV, iDstHeightUV,
                                                        pSrcU, iSrcStrideU, iSrcWidthUV, iSrcHeightUV);

                    m_pfDownsample.pfGeneralRatioChroma(pDstPixMap->pPixel[2], pDstPixMap->iStride[2], iDstWidthUV, iDstHeightUV,
                                                        pSrcV, iSrcStrideV, iSrcWidthUV, iSrcHeightUV);
                    break;
                }
            } while (true);
        }
        return EResult.RET_SUCCESS;
    }

    private readonly void DownsampleHalfAverage(uint8_t* pDst, int32_t iDstStride,
                                                uint8_t* pSrc, int32_t iSrcStride, int32_t iSrcWidth, int32_t iSrcHeight)
    {
        if ((iSrcStride & 31) == 0)
        {
            Debug.Assert((iDstStride & 15) == 0);
            m_pfDownsample.pfHalfAverageWidthx32(pDst, iDstStride,
                                                 pSrc, iSrcStride, WELS_ALIGN(iSrcWidth & ~1, 32), iSrcHeight);
        }
        else
        {
            Debug.Assert((iSrcStride & 15) == 0);
            Debug.Assert((iDstStride & 7) == 0);
            m_pfDownsample.pfHalfAverageWidthx16(pDst, iDstStride,
                                                 pSrc, iSrcStride, WELS_ALIGN(iSrcWidth & ~1, 16), iSrcHeight);
        }
    }
}
