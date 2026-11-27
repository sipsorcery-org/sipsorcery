//-----------------------------------------------------------------------------
// Filename: AdaptiveQuantization.cs
//
// Description: Port of codec/processing/src/adaptivequantization/
// AdaptiveQuantization.h and AdaptiveQuantization.cpp (E7): the adaptive
// quantisation method (CAdaptiveQuantization), which maps each macroblock's
// motion and texture indices, from the VAA statistics or SampleVariance16x16,
// to a QP offset, and SampleVariance16x16_c. A native struct, as the port's
// other strategies are; of its IStrategy base it holds m_eMethod. Only the C
// kernel is ported.
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

// typedef void (VarFunc) (uint8_t* pRefY, int32_t iRefStrideY, uint8_t* pSrc, int32_t iSrcStrideY,
//                         SMotionTextureUnit* pMotionTexture); typedef VarFunc* PVarFunc;
global using unsafe PVarFunc = delegate*<byte*, int, byte*, int, SIPSorcery.H264.SMotionTextureUnit*, void>;

namespace SIPSorcery.H264;

internal static unsafe partial class WelsVP
{
    public static void SampleVariance16x16_c(uint8_t* pRefY, int32_t iRefStride, uint8_t* pSrcY, int32_t iSrcStride,
                                             SMotionTextureUnit* pMotionTexture)
    {
        uint32_t uiCurSquare = 0, uiSquare = 0;
        uint16_t uiCurSum = 0, uiSum = 0;

        for (int32_t y = 0; y < MB_WIDTH_LUMA; y++)
        {
            for (int32_t x = 0; x < MB_WIDTH_LUMA; x++)
            {
                uint32_t uiDiff = (uint32_t)WELS_ABS(pRefY[x] - pSrcY[x]);
                uiSum += (uint16_t)uiDiff;
                uiSquare += uiDiff * uiDiff;

                uiCurSum += pSrcY[x];
                uiCurSquare += (uint32_t)(pSrcY[x] * pSrcY[x]);
            }
            pRefY += iRefStride;
            pSrcY += iSrcStride;
        }

        uiSum = (uint16_t)(uiSum >> 8);
        pMotionTexture->uiMotionIndex = (uint16_t)((uiSquare >> 8) - (uint32_t)(uiSum * uiSum));

        uiCurSum = (uint16_t)(uiCurSum >> 8);
        pMotionTexture->uiTextureIndex = (uint16_t)((uiCurSquare >> 8) - (uint32_t)(uiCurSum * uiCurSum));
    }
}

/// <summary>CAdaptiveQuantization (see the file header).</summary>
internal unsafe struct CAdaptiveQuantization
{
    private const int32_t AVERAGE_TIME_MOTION = 3000; //0.3046875 // 1/4 + 1/16 - 1/128 ~ 0.3 *AQ_TIME_INT_MULTIPLY
    private const int32_t AVERAGE_TIME_TEXTURE_QUALITYMODE = 10000; //0.5 // 1/2 *AQ_TIME_INT_MULTIPLY
    private const int32_t AVERAGE_TIME_TEXTURE_BITRATEMODE = 8750; //0.5 // 1/2 *AQ_TIME_INT_MULTIPLY
    private const int32_t MODEL_ALPHA = 9910; //1.5 //1.1102 *AQ_TIME_INT_MULTIPLY
    private const int32_t MODEL_TIME = 58185; //9.0 //5.9842 *AQ_TIME_INT_MULTIPLY

    public EMethods m_eMethod;      // IStrategy

    private PVarFunc m_pfVar;
    private int32_t m_CPUFlag;
    private SAdaptiveQuantizationParam m_sAdaptiveQuantParam;

    public CAdaptiveQuantization(int32_t iCpuFlag)
    {
        m_CPUFlag = iCpuFlag;
        m_eMethod = EMethods.METHOD_ADAPTIVE_QUANT;
        m_pfVar = null;
        m_sAdaptiveQuantParam = default;
        WelsInitVarFunc(ref m_pfVar, m_CPUFlag);
    }

    public EResult Process(int32_t iType, SPixMap* pSrcPixMap, SPixMap* pRefPixMap)
    {
        EResult eReturn = EResult.RET_INVALIDPARAM;

        int32_t iWidth = pSrcPixMap->sRect.iRectWidth;
        int32_t iHeight = pSrcPixMap->sRect.iRectHeight;
        int32_t iMbWidth = iWidth >> 4;
        int32_t iMbHeight = iHeight >> 4;
        int32_t iMbTotalNum = iMbWidth * iMbHeight;

        SMotionTextureUnit* pMotionTexture = null;
        SVAACalcResult* pVaaCalcResults = null;
        int32_t iMotionTextureIndexToDeltaQp = 0;
        int32_t iAverMotionTextureIndexToDeltaQp = 0;  // double to uint32
        int64_t iAverageMotionIndex = 0;      // double to float
        int64_t iAverageTextureIndex = 0;

        int64_t iQStep = 0;
        int64_t iLumaMotionDeltaQp = 0;
        int64_t iLumaTextureDeltaQp = 0;

        uint8_t* pRefFrameY = null, pCurFrameY = null;
        int32_t iRefStride = 0, iCurStride = 0;

        uint8_t* pRefFrameTmp = null, pCurFrameTmp = null;
        int32_t i = 0, j = 0;

        pRefFrameY = pRefPixMap->pPixel[0];
        pCurFrameY = pSrcPixMap->pPixel[0];

        iRefStride = pRefPixMap->iStride[0];
        iCurStride = pSrcPixMap->iStride[0];

        /////////////////////////////////////// motion //////////////////////////////////
        //  motion MB residual variance
        iAverageMotionIndex = 0;
        iAverageTextureIndex = 0;
        pMotionTexture = m_sAdaptiveQuantParam.pMotionTextureUnit;
        pVaaCalcResults = m_sAdaptiveQuantParam.pCalcResult;

        if (pVaaCalcResults->pRefY == pRefFrameY && pVaaCalcResults->pCurY == pCurFrameY)
        {
            int32_t iMbIndex = 0;
            int32_t iSumDiff, iSQDiff, uiSum, iSQSum;
            for (j = 0; j < iMbHeight; j++)
            {
                pRefFrameTmp = pRefFrameY;
                pCurFrameTmp = pCurFrameY;
                for (i = 0; i < iMbWidth; i++)
                {
                    iSumDiff = pVaaCalcResults->pSad8x8[iMbIndex * 4 + 0];
                    iSumDiff += pVaaCalcResults->pSad8x8[iMbIndex * 4 + 1];
                    iSumDiff += pVaaCalcResults->pSad8x8[iMbIndex * 4 + 2];
                    iSumDiff += pVaaCalcResults->pSad8x8[iMbIndex * 4 + 3];

                    iSQDiff = pVaaCalcResults->pSsd16x16[iMbIndex];
                    uiSum = pVaaCalcResults->pSum16x16[iMbIndex];
                    iSQSum = pVaaCalcResults->pSumOfSquare16x16[iMbIndex];

                    iSumDiff = iSumDiff >> 8;
                    pMotionTexture->uiMotionIndex = (uint16_t)((iSQDiff >> 8) - (iSumDiff * iSumDiff));

                    uiSum = uiSum >> 8;
                    pMotionTexture->uiTextureIndex = (uint16_t)((iSQSum >> 8) - (uiSum * uiSum));

                    iAverageMotionIndex += pMotionTexture->uiMotionIndex;
                    iAverageTextureIndex += pMotionTexture->uiTextureIndex;
                    pMotionTexture++;
                    ++iMbIndex;
                    pRefFrameTmp += WelsVP.MB_WIDTH_LUMA;
                    pCurFrameTmp += WelsVP.MB_WIDTH_LUMA;
                }
                pRefFrameY += (iRefStride) << 4;
                pCurFrameY += (iCurStride) << 4;
            }
        }
        else
        {
            for (j = 0; j < iMbHeight; j++)
            {
                pRefFrameTmp = pRefFrameY;
                pCurFrameTmp = pCurFrameY;
                for (i = 0; i < iMbWidth; i++)
                {
                    m_pfVar(pRefFrameTmp, iRefStride, pCurFrameTmp, iCurStride, pMotionTexture);
                    iAverageMotionIndex += pMotionTexture->uiMotionIndex;
                    iAverageTextureIndex += pMotionTexture->uiTextureIndex;
                    pMotionTexture++;
                    pRefFrameTmp += WelsVP.MB_WIDTH_LUMA;
                    pCurFrameTmp += WelsVP.MB_WIDTH_LUMA;
                }
                pRefFrameY += (iRefStride) << 4;
                pCurFrameY += (iCurStride) << 4;
            }
        }
        iAverageMotionIndex = WELS_DIV_ROUND64(iAverageMotionIndex * WelsVP.AQ_INT_MULTIPLY, iMbTotalNum);
        iAverageTextureIndex = WELS_DIV_ROUND64(iAverageTextureIndex * WelsVP.AQ_INT_MULTIPLY, iMbTotalNum);
        if ((iAverageMotionIndex <= WelsVP.AQ_PESN) && (iAverageMotionIndex >= -WelsVP.AQ_PESN))
        {
            iAverageMotionIndex = WelsVP.AQ_INT_MULTIPLY;
        }
        if ((iAverageTextureIndex <= WelsVP.AQ_PESN) && (iAverageTextureIndex >= -WelsVP.AQ_PESN))
        {
            iAverageTextureIndex = WelsVP.AQ_INT_MULTIPLY;
        }
        //  motion mb residual map to QP
        //  texture mb original map to QP
        iAverMotionTextureIndexToDeltaQp = 0;
        iAverageMotionIndex = WELS_DIV_ROUND64(AVERAGE_TIME_MOTION * iAverageMotionIndex, WelsVP.AQ_TIME_INT_MULTIPLY);

        if (m_sAdaptiveQuantParam.iAdaptiveQuantMode == (int32_t)EAQModes.AQ_QUALITY_MODE)
        {
            iAverageTextureIndex = WELS_DIV_ROUND64(AVERAGE_TIME_TEXTURE_QUALITYMODE * iAverageTextureIndex, WelsVP.AQ_TIME_INT_MULTIPLY);
        }
        else
        {
            iAverageTextureIndex = WELS_DIV_ROUND64(AVERAGE_TIME_TEXTURE_BITRATEMODE * iAverageTextureIndex, WelsVP.AQ_TIME_INT_MULTIPLY);
        }

        int64_t iAQ_EPSN = -((int64_t)WelsVP.AQ_PESN * WelsVP.AQ_TIME_INT_MULTIPLY * WelsVP.AQ_QSTEP_INT_MULTIPLY / WelsVP.AQ_INT_MULTIPLY);
        pMotionTexture = m_sAdaptiveQuantParam.pMotionTextureUnit;
        for (j = 0; j < iMbHeight; j++)
        {
            for (i = 0; i < iMbWidth; i++)
            {
                int64_t a = WELS_DIV_ROUND64((int64_t)(pMotionTexture->uiTextureIndex) * WelsVP.AQ_INT_MULTIPLY * WelsVP.AQ_TIME_INT_MULTIPLY,
                                             iAverageTextureIndex);
                iQStep = WELS_DIV_ROUND64((a - WelsVP.AQ_TIME_INT_MULTIPLY) * WelsVP.AQ_QSTEP_INT_MULTIPLY, (a + MODEL_ALPHA));
                iLumaTextureDeltaQp = MODEL_TIME * iQStep;// range +- 6

                iMotionTextureIndexToDeltaQp = ((int32_t)(iLumaTextureDeltaQp / (WelsVP.AQ_TIME_INT_MULTIPLY)));

                a = WELS_DIV_ROUND64(((int64_t)pMotionTexture->uiMotionIndex) * WelsVP.AQ_INT_MULTIPLY * WelsVP.AQ_TIME_INT_MULTIPLY,
                                     iAverageMotionIndex);
                iQStep = WELS_DIV_ROUND64((a - WelsVP.AQ_TIME_INT_MULTIPLY) * WelsVP.AQ_QSTEP_INT_MULTIPLY, (a + MODEL_ALPHA));
                iLumaMotionDeltaQp = MODEL_TIME * iQStep;// range +- 6

                if ((m_sAdaptiveQuantParam.iAdaptiveQuantMode == (int32_t)EAQModes.AQ_QUALITY_MODE && iLumaMotionDeltaQp < iAQ_EPSN)
                    || (m_sAdaptiveQuantParam.iAdaptiveQuantMode == (int32_t)EAQModes.AQ_BITRATE_MODE))
                {
                    iMotionTextureIndexToDeltaQp += ((int32_t)(iLumaMotionDeltaQp / (WelsVP.AQ_TIME_INT_MULTIPLY)));
                }

                m_sAdaptiveQuantParam.pMotionTextureIndexToDeltaQp[j * iMbWidth + i] = (int8_t)(iMotionTextureIndexToDeltaQp /
                    WelsVP.AQ_QSTEP_INT_MULTIPLY);
                iAverMotionTextureIndexToDeltaQp += iMotionTextureIndexToDeltaQp;
                pMotionTexture++;
            }
        }

        m_sAdaptiveQuantParam.iAverMotionTextureIndexToDeltaQp = iAverMotionTextureIndexToDeltaQp / iMbTotalNum;

        eReturn = EResult.RET_SUCCESS;

        return eReturn;
    }

    public EResult Set(int32_t iType, void* pParam)
    {
        if (pParam == null)
        {
            return EResult.RET_INVALIDPARAM;
        }

        m_sAdaptiveQuantParam = *(SAdaptiveQuantizationParam*)pParam;

        return EResult.RET_SUCCESS;
    }

    public readonly EResult Get(int32_t iType, void* pParam)
    {
        if (pParam == null)
        {
            return EResult.RET_INVALIDPARAM;
        }

        SAdaptiveQuantizationParam* sAdaptiveQuantParam = (SAdaptiveQuantizationParam*)pParam;

        sAdaptiveQuantParam->iAverMotionTextureIndexToDeltaQp = m_sAdaptiveQuantParam.iAverMotionTextureIndexToDeltaQp;

        return EResult.RET_SUCCESS;
    }

    // The SIMD versions (X86_ASM, HAVE_NEON, ...) are not ported: the reference is the C-only build.
    private static void WelsInitVarFunc(ref PVarFunc pfVar, int32_t iCpuFlag)
    {
        pfVar = &WelsVP.SampleVariance16x16_c;
    }
}
