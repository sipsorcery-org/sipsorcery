//-----------------------------------------------------------------------------
// Filename: ComplexityAnalysis.cs
//
// Description: Port of codec/processing/src/complexityanalysis/ComplexityAnalysis.h
// and ComplexityAnalysis.cpp, camera half (E6): the complexity analysis
// method (CComplexityAnalysis) the rate control reads, by frame SAD, by GOM
// SAD or by GOM variance, with the GOM SAD helpers. CComplexityAnalysis is a
// native struct, as CVAACalculation is; of its IStrategy base it holds
// m_eMethod. CVpFrameWork (WelsFrameWork.cs) holds it and dispatches to it.
// The screen content method (CComplexityAnalysisScreen) comes with Phase 4.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 26 Nov 2026  Claude          Created (E6).
// 27 Nov 2026  Claude          Header: CVpFrameWork exists (E7).
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

// typedef void (GOMSadFunc) (uint32_t* pGomSad, int32_t* pGomForegroundBlockNum, int32_t* pSad8x8, uint8_t pBackgroundMbFlag);
global using unsafe PGOMSadFunc = delegate*<uint*, int*, int*, byte, void>;

namespace SIPSorcery.H264;

internal static unsafe partial class WelsVP
{
    // util.h
    public const int32_t MB_WIDTH_LUMA = 16;
    public const uint32_t MB_TYPE_INTRA4x4 = 0x00000001;
    public const uint32_t MB_TYPE_INTRA16x16 = 0x00000002;
    public const uint32_t MB_TYPE_INTRA_PCM = 0x00000004;
    public const uint32_t MB_TYPE_INTRA = (MB_TYPE_INTRA4x4 | MB_TYPE_INTRA16x16 | MB_TYPE_INTRA_PCM);

    /// <summary>IS_INTRA (util.h): processing's own, which tests the low three bits.</summary>
    public static bool IS_INTRA(uint32_t type) => (type & MB_TYPE_INTRA) != 0;

    public static void InitGomSadFunc(ref PGOMSadFunc pfGomSad, uint8_t iCalcBgd)
    {
        pfGomSad = &GomSampleSad;

        if (iCalcBgd != 0)
        {
            pfGomSad = &GomSampleSadExceptBackground;
        }
    }

    public static void GomSampleSad(uint32_t* pGomSad, int32_t* pGomForegroundBlockNum, int32_t* pSad8x8, uint8_t pBackgroundMbFlag)
    {
        (*pGomForegroundBlockNum)++;
        *pGomSad += (uint32_t)pSad8x8[0];
        *pGomSad += (uint32_t)pSad8x8[1];
        *pGomSad += (uint32_t)pSad8x8[2];
        *pGomSad += (uint32_t)pSad8x8[3];
    }

    public static void GomSampleSadExceptBackground(uint32_t* pGomSad, int32_t* pGomForegroundBlockNum, int32_t* pSad8x8,
                                                    uint8_t pBackgroundMbFlag)
    {
        if (pBackgroundMbFlag == 0)
        {
            (*pGomForegroundBlockNum)++;
            *pGomSad += (uint32_t)pSad8x8[0];
            *pGomSad += (uint32_t)pSad8x8[1];
            *pGomSad += (uint32_t)pSad8x8[2];
            *pGomSad += (uint32_t)pSad8x8[3];
        }
    }
}

/// <summary>CComplexityAnalysis (see the file header).</summary>
internal unsafe struct CComplexityAnalysis
{
    public EMethods m_eMethod;      // IStrategy

    private PGOMSadFunc m_pfGomSad;
    private SComplexityAnalysisParam m_sComplexityAnalysisParam;

    /// <summary>CComplexityAnalysis::CComplexityAnalysis (int32_t iCpuFlag).</summary>
    public CComplexityAnalysis(int32_t iCpuFlag)
    {
        m_eMethod = EMethods.METHOD_COMPLEXITY_ANALYSIS;
        m_pfGomSad = null;
        m_sComplexityAnalysisParam = default;
    }

    public EResult Process(int32_t iType, SPixMap* pSrcPixMap, SPixMap* pRefPixMap)
    {
        EResult eReturn = EResult.RET_SUCCESS;

        switch ((EComplexityAnalysisMode)m_sComplexityAnalysisParam.iComplexityAnalysisMode)
        {
            case EComplexityAnalysisMode.FRAME_SAD:
                AnalyzeFrameComplexityViaSad(pSrcPixMap, pRefPixMap);
                break;
            case EComplexityAnalysisMode.GOM_SAD:
                AnalyzeGomComplexityViaSad(pSrcPixMap, pRefPixMap);
                break;
            case EComplexityAnalysisMode.GOM_VAR:
                AnalyzeGomComplexityViaVar(pSrcPixMap, pRefPixMap);
                break;
            default:
                eReturn = EResult.RET_INVALIDPARAM;
                break;
        }

        return eReturn;
    }


    public EResult Set(int32_t iType, void* pParam)
    {
        if (pParam == null)
        {
            return EResult.RET_INVALIDPARAM;
        }

        m_sComplexityAnalysisParam = *(SComplexityAnalysisParam*)pParam;

        return EResult.RET_SUCCESS;
    }

    public EResult Get(int32_t iType, void* pParam)
    {
        if (pParam == null)
        {
            return EResult.RET_INVALIDPARAM;
        }

        SComplexityAnalysisParam* sComplexityAnalysisParam = (SComplexityAnalysisParam*)pParam;

        sComplexityAnalysisParam->iFrameComplexity = m_sComplexityAnalysisParam.iFrameComplexity;

        return EResult.RET_SUCCESS;
    }


    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void AnalyzeFrameComplexityViaSad(SPixMap* pSrcPixMap, SPixMap* pRefPixMap)
    {
        SVAACalcResult* pVaaCalcResults = null;
        pVaaCalcResults = m_sComplexityAnalysisParam.pCalcResult;

        m_sComplexityAnalysisParam.iFrameComplexity = pVaaCalcResults->iFrameSad;

        if (m_sComplexityAnalysisParam.iCalcBgd != 0)
        { //BGD control
            m_sComplexityAnalysisParam.iFrameComplexity = GetFrameSadExcludeBackground(pSrcPixMap, pRefPixMap);
        }
    }

    private int32_t GetFrameSadExcludeBackground(SPixMap* pSrcPixMap, SPixMap* pRefPixMap)
    {
        int32_t iWidth = pSrcPixMap->sRect.iRectWidth;
        int32_t iHeight = pSrcPixMap->sRect.iRectHeight;
        int32_t iMbWidth = iWidth >> 4;
        int32_t iMbHeight = iHeight >> 4;
        int32_t iMbNum = iMbWidth * iMbHeight;

        int32_t iMbNumInGom = m_sComplexityAnalysisParam.iMbNumInGom;
        int32_t iGomMbNum = (iMbNum + iMbNumInGom - 1) / iMbNumInGom;
        int32_t iGomMbStartIndex = 0, iGomMbEndIndex = 0;

        uint8_t* pBackgroundMbFlag = (uint8_t*)m_sComplexityAnalysisParam.pBackgroundMbFlag;
        uint32_t* uiRefMbType = (uint32_t*)m_sComplexityAnalysisParam.uiRefMbType;
        SVAACalcResult* pVaaCalcResults = m_sComplexityAnalysisParam.pCalcResult;
        int32_t* pGomForegroundBlockNum = m_sComplexityAnalysisParam.pGomForegroundBlockNum;

        uint32_t uiFrameSad = 0;
        for (int32_t j = 0; j < iGomMbNum; j++)
        {
            iGomMbStartIndex = j * iMbNumInGom;
            iGomMbEndIndex = WELS_MIN((j + 1) * iMbNumInGom, iMbNum);

            for (int32_t i = iGomMbStartIndex; i < iGomMbEndIndex; i++)
            {
                if (pBackgroundMbFlag[i] == 0 || WelsVP.IS_INTRA(uiRefMbType[i]))
                {
                    pGomForegroundBlockNum[j]++;
                    // pSad8x8 is int32_t (*)[4], flattened here.
                    uiFrameSad += (uint32_t)pVaaCalcResults->pSad8x8[i * 4 + 0];
                    uiFrameSad += (uint32_t)pVaaCalcResults->pSad8x8[i * 4 + 1];
                    uiFrameSad += (uint32_t)pVaaCalcResults->pSad8x8[i * 4 + 2];
                    uiFrameSad += (uint32_t)pVaaCalcResults->pSad8x8[i * 4 + 3];
                }
            }
        }

        return (int32_t)(uiFrameSad);
    }

    private void AnalyzeGomComplexityViaSad(SPixMap* pSrcPixMap, SPixMap* pRefPixMap)
    {
        int32_t iWidth = pSrcPixMap->sRect.iRectWidth;
        int32_t iHeight = pSrcPixMap->sRect.iRectHeight;
        int32_t iMbWidth = iWidth >> 4;
        int32_t iMbHeight = iHeight >> 4;
        int32_t iMbNum = iMbWidth * iMbHeight;

        int32_t iMbNumInGom = m_sComplexityAnalysisParam.iMbNumInGom;
        int32_t iGomMbNum = (iMbNum + iMbNumInGom - 1) / iMbNumInGom;

        int32_t iGomMbStartIndex = 0, iGomMbEndIndex = 0, iGomMbRowNum = 0;
        int32_t iMbStartIndex = 0, iMbEndIndex = 0;

        uint8_t* pBackgroundMbFlag = (uint8_t*)m_sComplexityAnalysisParam.pBackgroundMbFlag;
        uint32_t* uiRefMbType = (uint32_t*)m_sComplexityAnalysisParam.uiRefMbType;
        SVAACalcResult* pVaaCalcResults = m_sComplexityAnalysisParam.pCalcResult;
        int32_t* pGomForegroundBlockNum = (int32_t*)m_sComplexityAnalysisParam.pGomForegroundBlockNum;
        int32_t* pGomComplexity = (int32_t*)m_sComplexityAnalysisParam.pGomComplexity;

        uint32_t uiGomSad = 0, uiFrameSad = 0;
        WelsVP.InitGomSadFunc(ref m_pfGomSad, (uint8_t)m_sComplexityAnalysisParam.iCalcBgd);

        for (int32_t j = 0; j < iGomMbNum; j++)
        {
            uiGomSad = 0;

            iGomMbStartIndex = j * iMbNumInGom;
            iGomMbEndIndex = WELS_MIN((j + 1) * iMbNumInGom, iMbNum);
            iGomMbRowNum = (iGomMbEndIndex + iMbWidth - 1) / iMbWidth - iGomMbStartIndex / iMbWidth;

            iMbStartIndex = iGomMbStartIndex;
            iMbEndIndex = WELS_MIN((iMbStartIndex / iMbWidth + 1) * iMbWidth, iGomMbEndIndex);

            do
            {
                for (int32_t i = iMbStartIndex; i < iMbEndIndex; i++)
                {
                    // The flag is a bool narrowed to uint8_t.
                    m_pfGomSad(&uiGomSad, pGomForegroundBlockNum + j, pVaaCalcResults->pSad8x8 + i * 4,
                               (uint8_t)((pBackgroundMbFlag[i] != 0 && !WelsVP.IS_INTRA(uiRefMbType[i])) ? 1 : 0));
                }

                iMbStartIndex = iMbEndIndex;
                iMbEndIndex = WELS_MIN(iMbEndIndex + iMbWidth, iGomMbEndIndex);

            } while (--iGomMbRowNum != 0);
            pGomComplexity[j] = (int32_t)uiGomSad;
            uiFrameSad += (uint32_t)pGomComplexity[j];
        }
        m_sComplexityAnalysisParam.iFrameComplexity = uiFrameSad;
    }


    private void AnalyzeGomComplexityViaVar(SPixMap* pSrcPixMap, SPixMap* pRefPixMap)
    {
        int32_t iWidth = pSrcPixMap->sRect.iRectWidth;
        int32_t iHeight = pSrcPixMap->sRect.iRectHeight;
        int32_t iMbWidth = iWidth >> 4;
        int32_t iMbHeight = iHeight >> 4;
        int32_t iMbNum = iMbWidth * iMbHeight;

        int32_t iMbNumInGom = m_sComplexityAnalysisParam.iMbNumInGom;
        int32_t iGomMbNum = (iMbNum + iMbNumInGom - 1) / iMbNumInGom;
        int32_t iGomSampleNum = 0;

        int32_t iGomMbStartIndex = 0, iGomMbEndIndex = 0, iGomMbRowNum = 0;
        int32_t iMbStartIndex = 0, iMbEndIndex = 0;

        SVAACalcResult* pVaaCalcResults = m_sComplexityAnalysisParam.pCalcResult;
        int32_t* pGomComplexity = (int32_t*)m_sComplexityAnalysisParam.pGomComplexity;
        uint32_t uiFrameSad = 0;

        uint32_t uiSampleSum = 0, uiSquareSum = 0;

        for (int32_t j = 0; j < iGomMbNum; j++)
        {
            uiSampleSum = 0;
            uiSquareSum = 0;

            iGomMbStartIndex = j * iMbNumInGom;
            iGomMbEndIndex = WELS_MIN((j + 1) * iMbNumInGom, iMbNum);
            iGomMbRowNum = (iGomMbEndIndex + iMbWidth - 1) / iMbWidth - iGomMbStartIndex / iMbWidth;

            iMbStartIndex = iGomMbStartIndex;
            iMbEndIndex = WELS_MIN((iMbStartIndex / iMbWidth + 1) * iMbWidth, iGomMbEndIndex);

            // From the GOM's first row only, as upstream.
            iGomSampleNum = (iMbEndIndex - iMbStartIndex) * WelsVP.MB_WIDTH_LUMA * WelsVP.MB_WIDTH_LUMA;

            do
            {
                for (int32_t i = iMbStartIndex; i < iMbEndIndex; i++)
                {
                    uiSampleSum += (uint32_t)pVaaCalcResults->pSum16x16[i];
                    uiSquareSum += (uint32_t)pVaaCalcResults->pSumOfSquare16x16[i];
                }

                iMbStartIndex = iMbEndIndex;
                iMbEndIndex = WELS_MIN(iMbEndIndex + iMbWidth, iGomMbEndIndex);

            } while (--iGomMbRowNum != 0);

            // uint32_t arithmetic throughout: the int divisor converts to unsigned.
            pGomComplexity[j] = (int32_t)(uiSquareSum - (uiSampleSum * uiSampleSum / (uint32_t)iGomSampleNum));
            uiFrameSad += (uint32_t)pGomComplexity[j];
        }
        m_sComplexityAnalysisParam.iFrameComplexity = uiFrameSad;
    }
}
