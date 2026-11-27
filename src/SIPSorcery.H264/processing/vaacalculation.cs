//-----------------------------------------------------------------------------
// Filename: vaacalculation.cs
//
// Description: Port of codec/processing/src/vaacalc/vaacalculation.h and
// vaacalculation.cpp (E5b): the VAA statistics method (CVAACalculation),
// which picks one of the vaacalcfuncs kernels by the statistics asked for:
// the C kernels, or with SIMD the ones in vaacalcfuncs_simd.cs (P5.6), which
// give the same results. CVAACalculation is a native struct, as the
// port's other C++ classes are; of its IStrategy base it holds m_eMethod, and
// CVpFrameWork (WelsFrameWork.cs) holds it and dispatches to it.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 26 Nov 2026  Claude          Created (E5b).
// 27 Nov 2026  Claude          Header: CVpFrameWork exists (E7).
// 27 Nov 2026  Claude          The SIMD kernels in InitVaaFuncs (P5.6).
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

// typedef void (VAACalcSadBgdFunc) (...); typedef VAACalcSadBgdFunc* PVAACalcSadBgdFunc; and the others.
global using unsafe PVAACalcSadBgdFunc = delegate*<byte*, byte*, int, int, int, int*, int*, int*, byte*, void>;
global using unsafe PVAACalcSadSsdBgdFunc = delegate*<byte*, byte*, int, int, int, int*, int*, int*, int*, int*, int*, byte*, void>;
global using unsafe PVAACalcSadFunc = delegate*<byte*, byte*, int, int, int, int*, int*, void>;
global using unsafe PVAACalcSadVarFunc = delegate*<byte*, byte*, int, int, int, int*, int*, int*, int*, void>;
global using unsafe PVAACalcSadSsdFunc = delegate*<byte*, byte*, int, int, int, int*, int*, int*, int*, int*, void>;

namespace SIPSorcery.H264;

/// <summary>TagVaaFuncs.</summary>
internal unsafe struct SVaaFuncs
{
    public PVAACalcSadBgdFunc pfVAACalcSadBgd;
    public PVAACalcSadSsdBgdFunc pfVAACalcSadSsdBgd;
    public PVAACalcSadFunc pfVAACalcSad;
    public PVAACalcSadVarFunc pfVAACalcSadVar;
    public PVAACalcSadSsdFunc pfVAACalcSadSsd;
}

/// <summary>CVAACalculation (see the file header).</summary>
internal unsafe struct CVAACalculation
{
    public EMethods m_eMethod;      // IStrategy

    private SVaaFuncs m_sVaaFuncs;
    private int32_t m_iCPUFlag;
    private SVAACalcParam m_sCalcParam;

    /// <summary>CVAACalculation::CVAACalculation (int32_t iCpuFlag).</summary>
    public CVAACalculation(int32_t iCpuFlag)
    {
        m_iCPUFlag = iCpuFlag;
        m_eMethod = EMethods.METHOD_VAA_STATISTICS;

        m_sCalcParam = default;
        m_sVaaFuncs = default;
        InitVaaFuncs(ref m_sVaaFuncs, m_iCPUFlag);
    }

    internal static void InitVaaFuncs(ref SVaaFuncs sVaaFuncs, int32_t iCpuFlag)
    {
        sVaaFuncs.pfVAACalcSad = &WelsVP.VAACalcSad_c;
        sVaaFuncs.pfVAACalcSadBgd = &WelsVP.VAACalcSadBgd_c;
        sVaaFuncs.pfVAACalcSadSsd = &WelsVP.VAACalcSadSsd_c;
        sVaaFuncs.pfVAACalcSadSsdBgd = &WelsVP.VAACalcSadSsdBgd_c;
        sVaaFuncs.pfVAACalcSadVar = &WelsVP.VAACalcSadVar_c;

        // In place of the X86_ASM (SSE2, AVX2) and HAVE_NEON versions, the kernels in
        // vaacalcfuncs_simd.cs. The MMI and LASX versions have no counterpart.
        if (WelsCpuHasSimd128((uint32_t)iCpuFlag))
        {
            sVaaFuncs.pfVAACalcSad = &WelsVP.VAACalcSad_simd;
            sVaaFuncs.pfVAACalcSadBgd = &WelsVP.VAACalcSadBgd_simd;
            sVaaFuncs.pfVAACalcSadSsd = &WelsVP.VAACalcSadSsd_simd;
            sVaaFuncs.pfVAACalcSadSsdBgd = &WelsVP.VAACalcSadSsdBgd_simd;
            sVaaFuncs.pfVAACalcSadVar = &WelsVP.VAACalcSadVar_simd;
        }
    }

    public EResult Process(int32_t iType, SPixMap* pSrcPixMap, SPixMap* pRefPixMap)
    {
        uint8_t* pCurData = pSrcPixMap->pPixel[0];
        uint8_t* pRefData = pRefPixMap->pPixel[0];
        int32_t iPicWidth = pSrcPixMap->sRect.iRectWidth;
        int32_t iPicHeight = pSrcPixMap->sRect.iRectHeight;
        int32_t iPicStride = pSrcPixMap->iStride[0];

        SVAACalcResult* pResult = m_sCalcParam.pCalcResult;

        if (pCurData == null || pRefData == null)
        {
            return EResult.RET_INVALIDPARAM;
        }

        pResult->pCurY = pCurData;
        pResult->pRefY = pRefData;
        if (m_sCalcParam.iCalcBgd != 0)
        {
            if (m_sCalcParam.iCalcSsd != 0)
            {
                m_sVaaFuncs.pfVAACalcSadSsdBgd(pCurData, pRefData, iPicWidth, iPicHeight, iPicStride, &pResult->iFrameSad,
                                               pResult->pSad8x8, pResult->pSum16x16, pResult->pSumOfSquare16x16, pResult->pSsd16x16,
                                               pResult->pSumOfDiff8x8, pResult->pMad8x8);
            }
            else
            {
                m_sVaaFuncs.pfVAACalcSadBgd(pCurData, pRefData, iPicWidth, iPicHeight, iPicStride, &pResult->iFrameSad,
                                            pResult->pSad8x8, pResult->pSumOfDiff8x8, pResult->pMad8x8);
            }
        }
        else
        {
            if (m_sCalcParam.iCalcSsd != 0)
            {
                m_sVaaFuncs.pfVAACalcSadSsd(pCurData, pRefData, iPicWidth, iPicHeight, iPicStride, &pResult->iFrameSad,
                                            pResult->pSad8x8, pResult->pSum16x16, pResult->pSumOfSquare16x16, pResult->pSsd16x16);
            }
            else
            {
                if (m_sCalcParam.iCalcVar != 0)
                {
                    m_sVaaFuncs.pfVAACalcSadVar(pCurData, pRefData, iPicWidth, iPicHeight, iPicStride, &pResult->iFrameSad,
                                                pResult->pSad8x8, pResult->pSum16x16, pResult->pSumOfSquare16x16);
                }
                else
                {
                    m_sVaaFuncs.pfVAACalcSad(pCurData, pRefData, iPicWidth, iPicHeight, iPicStride, &pResult->iFrameSad,
                                             pResult->pSad8x8);
                }
            }
        }

        return EResult.RET_SUCCESS;
    }

    public EResult Set(int32_t iType, void* pParam)
    {
        if (pParam == null || ((SVAACalcParam*)pParam)->pCalcResult == null)
        {
            return EResult.RET_INVALIDPARAM;
        }

        m_sCalcParam = *(SVAACalcParam*)pParam;

        return EResult.RET_SUCCESS;
    }
}
