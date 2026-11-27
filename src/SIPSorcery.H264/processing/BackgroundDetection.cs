//-----------------------------------------------------------------------------
// Filename: BackgroundDetection.cs
//
// Description: Port of codec/processing/src/backgrounddetection/
// BackgroundDetection.h and BackgroundDetection.cpp (E7): the background
// detection method (CBackgroundDetection), which divides the picture into
// 16x16 units by their VAA statistics, dilates the foreground and erodes the
// background over the unit neighbours (with a chroma edge check), and writes
// the encoder's background macroblock flags. A native struct, as the port's
// other strategies are; of its IStrategy base it holds m_eMethod.
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

namespace SIPSorcery.H264;

/// <summary>SBackgroundOU.</summary>
internal struct SBackgroundOU
{
    public int32_t iBackgroundFlag;
    public int32_t iSAD;
    public int32_t iSD;
    public int32_t iMAD;
    public int32_t iMinSubMad;
    public int32_t iMaxDiffSubSd;
}

/// <summary>CBackgroundDetection::vBGDParam.</summary>
internal unsafe struct vBGDParam
{
    public PtrArray3<uint8_t> pCur;
    public PtrArray3<uint8_t> pRef;
    public int32_t iBgdWidth;
    public int32_t iBgdHeight;
    public fixed int32_t iStride[3];
    public SBackgroundOU* pOU_array;
    public int8_t* pBackgroundMbFlag;
    public SVAACalcResult* pCalcRes;
}

/// <summary>CBackgroundDetection (see the file header).</summary>
internal unsafe struct CBackgroundDetection
{
    private const int32_t LOG2_BGD_OU_SIZE = 4;
    private const int32_t LOG2_BGD_OU_SIZE_UV = LOG2_BGD_OU_SIZE - 1;
    private const int32_t BGD_OU_SIZE = 1 << LOG2_BGD_OU_SIZE;
    private const int32_t BGD_OU_SIZE_UV = BGD_OU_SIZE >> 1;
    private const int32_t BGD_THD_SAD = 2 * BGD_OU_SIZE * BGD_OU_SIZE;
    private const int32_t BGD_THD_ASD_UV = 4 * BGD_OU_SIZE_UV;
    private const int32_t LOG2_MB_SIZE = 4;
    private const int32_t OU_SIZE_IN_MB = BGD_OU_SIZE >> 4;
    private const int32_t Q_FACTOR = 8;
    private const int32_t BGD_DELTA_QP_THD = 3;

    private const int8_t OU_LEFT = 0x01;
    private const int8_t OU_RIGHT = 0x02;
    private const int8_t OU_TOP = 0x04;
    private const int8_t OU_BOTTOM = 0x08;

    public EMethods m_eMethod;      // IStrategy

    private vBGDParam m_BgdParam;
    private int32_t m_iLargestFrameSize;

    public CBackgroundDetection(int32_t iCpuFlag)
    {
        m_eMethod = EMethods.METHOD_BACKGROUND_DETECTION;
        m_BgdParam = default;
        m_iLargestFrameSize = 0;
    }

    /// <summary>~CBackgroundDetection.</summary>
    public void Destroy()
    {
        WelsVP.WelsFree(m_BgdParam.pOU_array);
        m_BgdParam.pOU_array = null;
    }

    public EResult Process(int32_t iType, SPixMap* pSrcPixMap, SPixMap* pRefPixMap)
    {
        EResult eReturn = EResult.RET_INVALIDPARAM;

        if (pSrcPixMap == null || pRefPixMap == null)
        {
            return eReturn;
        }

        m_BgdParam.pCur[0] = pSrcPixMap->pPixel[0];
        m_BgdParam.pCur[1] = pSrcPixMap->pPixel[1];
        m_BgdParam.pCur[2] = pSrcPixMap->pPixel[2];
        m_BgdParam.pRef[0] = pRefPixMap->pPixel[0];
        m_BgdParam.pRef[1] = pRefPixMap->pPixel[1];
        m_BgdParam.pRef[2] = pRefPixMap->pPixel[2];
        m_BgdParam.iBgdWidth = pSrcPixMap->sRect.iRectWidth;
        m_BgdParam.iBgdHeight = pSrcPixMap->sRect.iRectHeight;
        m_BgdParam.iStride[0] = pSrcPixMap->iStride[0];
        m_BgdParam.iStride[1] = pSrcPixMap->iStride[1];
        m_BgdParam.iStride[2] = pSrcPixMap->iStride[2];

        int32_t iCurFrameSize = m_BgdParam.iBgdWidth * m_BgdParam.iBgdHeight;
        if (m_BgdParam.pOU_array == null || iCurFrameSize > m_iLargestFrameSize)
        {
            WelsVP.WelsFree(m_BgdParam.pOU_array);
            m_BgdParam.pOU_array = AllocateOUArrayMemory(m_BgdParam.iBgdWidth, m_BgdParam.iBgdHeight);
            m_iLargestFrameSize = iCurFrameSize;
        }

        if (m_BgdParam.pOU_array == null)
        {
            return eReturn;
        }

        fixed (vBGDParam* pBgdParam = &m_BgdParam)
        {
            BackgroundDetection(pBgdParam);
        }

        return EResult.RET_SUCCESS;
    }

    public EResult Set(int32_t iType, void* pParam)
    {
        if (pParam == null)
        {
            return EResult.RET_INVALIDPARAM;
        }

        SBGDInterface* pInterface = (SBGDInterface*)pParam;

        m_BgdParam.pBackgroundMbFlag = pInterface->pBackgroundMbFlag;
        m_BgdParam.pCalcRes = pInterface->pCalcRes;

        return EResult.RET_SUCCESS;
    }

    private static SBackgroundOU* AllocateOUArrayMemory(int32_t iWidth, int32_t iHeight)
    {
        int32_t iMaxOUWidth = (BGD_OU_SIZE - 1 + iWidth) >> LOG2_BGD_OU_SIZE;
        int32_t iMaxOUHeight = (BGD_OU_SIZE - 1 + iHeight) >> LOG2_BGD_OU_SIZE;
        return (SBackgroundOU*)WelsVP.WelsMalloc((uint32_t)(iMaxOUWidth * iMaxOUHeight * sizeof(SBackgroundOU)));
    }

    private static void GetOUParameters(SVAACalcResult* sVaaCalcInfo, int32_t iMbIndex, int32_t iMbWidth,
                                        SBackgroundOU* pBgdOU)
    {
        int32_t* iSubSD = stackalloc int32_t[4];
        uint8_t* iSubMAD = stackalloc uint8_t[4];
        int32_t* iSubSAD = stackalloc int32_t[4];

        uint8_t* pMad8x8;   // uint8_t (*pMad8x8)[4]
        int32_t* pSad8x8;   // int32_t (*pSad8x8)[4]
        int32_t* pSd8x8;    // int32_t (*pSd8x8)[4]

        pSad8x8 = sVaaCalcInfo->pSad8x8;
        pMad8x8 = sVaaCalcInfo->pMad8x8;
        pSd8x8 = sVaaCalcInfo->pSumOfDiff8x8;

        iSubSAD[0] = pSad8x8[iMbIndex * 4 + 0];
        iSubSAD[1] = pSad8x8[iMbIndex * 4 + 1];
        iSubSAD[2] = pSad8x8[iMbIndex * 4 + 2];
        iSubSAD[3] = pSad8x8[iMbIndex * 4 + 3];

        iSubSD[0] = pSd8x8[iMbIndex * 4 + 0];
        iSubSD[1] = pSd8x8[iMbIndex * 4 + 1];
        iSubSD[2] = pSd8x8[iMbIndex * 4 + 2];
        iSubSD[3] = pSd8x8[iMbIndex * 4 + 3];

        iSubMAD[0] = pMad8x8[iMbIndex * 4 + 0];
        iSubMAD[1] = pMad8x8[iMbIndex * 4 + 1];
        iSubMAD[2] = pMad8x8[iMbIndex * 4 + 2];
        iSubMAD[3] = pMad8x8[iMbIndex * 4 + 3];

        pBgdOU->iSD = iSubSD[0] + iSubSD[1] + iSubSD[2] + iSubSD[3];
        pBgdOU->iSAD = iSubSAD[0] + iSubSAD[1] + iSubSAD[2] + iSubSAD[3];
        pBgdOU->iSD = WELS_ABS(pBgdOU->iSD);

        // get the max absolute difference (MAD) of OU and min value of the MAD of sub-blocks of OU
        pBgdOU->iMAD = WELS_MAX(WELS_MAX(iSubMAD[0], iSubMAD[1]), WELS_MAX(iSubMAD[2], iSubMAD[3]));
        pBgdOU->iMinSubMad = WELS_MIN(WELS_MIN(iSubMAD[0], iSubMAD[1]), WELS_MIN(iSubMAD[2], iSubMAD[3]));

        // get difference between the max and min SD of the SDs of sub-blocks of OU
        pBgdOU->iMaxDiffSubSd = WELS_MAX(WELS_MAX(iSubSD[0], iSubSD[1]), WELS_MAX(iSubSD[2], iSubSD[3])) -
                                WELS_MIN(WELS_MIN(iSubSD[0], iSubSD[1]), WELS_MIN(iSubSD[2], iSubSD[3]));
    }

    private static void ForegroundBackgroundDivision(vBGDParam* pBgdParam)
    {
        int32_t iPicWidthInOU = pBgdParam->iBgdWidth >> LOG2_BGD_OU_SIZE;
        int32_t iPicHeightInOU = pBgdParam->iBgdHeight >> LOG2_BGD_OU_SIZE;
        int32_t iPicWidthInMb = (15 + pBgdParam->iBgdWidth) >> 4;

        SBackgroundOU* pBackgroundOU = pBgdParam->pOU_array;

        for (int32_t j = 0; j < iPicHeightInOU; j++)
        {
            for (int32_t i = 0; i < iPicWidthInOU; i++)
            {
                GetOUParameters(pBgdParam->pCalcRes, (j * iPicWidthInMb + i) << (LOG2_BGD_OU_SIZE - LOG2_MB_SIZE), iPicWidthInMb,
                                pBackgroundOU);

                pBackgroundOU->iBackgroundFlag = 0;
                if (pBackgroundOU->iMAD > 63)
                {
                    pBackgroundOU++;
                    continue;
                }
                if ((pBackgroundOU->iMaxDiffSubSd <= pBackgroundOU->iSAD >> 3
                     || pBackgroundOU->iMaxDiffSubSd <= (BGD_OU_SIZE * Q_FACTOR))
                    && pBackgroundOU->iSAD < (BGD_THD_SAD << 1))
                { //BGD_OU_SIZE*BGD_OU_SIZE>>2
                    if (pBackgroundOU->iSAD <= BGD_OU_SIZE * Q_FACTOR)
                    {
                        pBackgroundOU->iBackgroundFlag = 1;
                    }
                    else
                    {
                        pBackgroundOU->iBackgroundFlag = pBackgroundOU->iSAD < BGD_THD_SAD ?
                                                         (pBackgroundOU->iSD < (pBackgroundOU->iSAD * 3) >> 2 ? 1 : 0) :
                                                         (pBackgroundOU->iSD << 1 < pBackgroundOU->iSAD ? 1 : 0);
                    }
                }
                pBackgroundOU++;
            }
        }
    }

    private static int32_t CalculateAsdChromaEdge(uint8_t* pOriRef, uint8_t* pOriCur, int32_t iStride)
    {
        int32_t ASD = 0;
        int32_t idx;
        for (idx = 0; idx < BGD_OU_SIZE_UV; idx++)
        {
            ASD += *pOriCur - *pOriRef;
            pOriRef += iStride;
            pOriCur += iStride;
        }
        return WELS_ABS(ASD);
    }

    private static bool ForegroundDilation23Luma(SBackgroundOU* pBackgroundOU, SBackgroundOU** pOUNeighbours)
    {
        SBackgroundOU* pOU_L = pOUNeighbours[0];
        SBackgroundOU* pOU_R = pOUNeighbours[1];
        SBackgroundOU* pOU_U = pOUNeighbours[2];
        SBackgroundOU* pOU_D = pOUNeighbours[3];

        if (pBackgroundOU->iMAD > pBackgroundOU->iMinSubMad << 1)
        {
            int32_t iMaxNbrForegroundMad;
            int32_t iMaxNbrBackgroundMad;
            int32_t* aBackgroundMad = stackalloc int32_t[4];
            int32_t* aForegroundMad = stackalloc int32_t[4];

            aForegroundMad[0] = (pOU_L->iBackgroundFlag - 1) & pOU_L->iMAD;
            aForegroundMad[1] = (pOU_R->iBackgroundFlag - 1) & pOU_R->iMAD;
            aForegroundMad[2] = (pOU_U->iBackgroundFlag - 1) & pOU_U->iMAD;
            aForegroundMad[3] = (pOU_D->iBackgroundFlag - 1) & pOU_D->iMAD;
            iMaxNbrForegroundMad = WELS_MAX(WELS_MAX(aForegroundMad[0], aForegroundMad[1]), WELS_MAX(aForegroundMad[2],
                                            aForegroundMad[3]));

            aBackgroundMad[0] = ((pOU_L->iBackgroundFlag == 0 ? 1 : 0) - 1) & pOU_L->iMAD;
            aBackgroundMad[1] = ((pOU_R->iBackgroundFlag == 0 ? 1 : 0) - 1) & pOU_R->iMAD;
            aBackgroundMad[2] = ((pOU_U->iBackgroundFlag == 0 ? 1 : 0) - 1) & pOU_U->iMAD;
            aBackgroundMad[3] = ((pOU_D->iBackgroundFlag == 0 ? 1 : 0) - 1) & pOU_D->iMAD;
            iMaxNbrBackgroundMad = WELS_MAX(WELS_MAX(aBackgroundMad[0], aBackgroundMad[1]), WELS_MAX(aBackgroundMad[2],
                                            aBackgroundMad[3]));

            return ((iMaxNbrForegroundMad > pBackgroundOU->iMinSubMad << 2) || (pBackgroundOU->iMAD > iMaxNbrBackgroundMad << 1
                    && pBackgroundOU->iMAD <= (iMaxNbrForegroundMad * 3) >> 1));
        }
        return false;
    }

    private static readonly int8_t[] kaOUPos = { OU_LEFT, OU_RIGHT, OU_TOP, OU_BOTTOM };

    private static bool ForegroundDilation23Chroma(int8_t iNeighbourForegroundFlags, int32_t iStartSamplePos,
                                                   int32_t iPicStrideUV, vBGDParam* pBgdParam)
    {
        int32_t* aEdgeOffset = stackalloc int32_t[4] { 0, BGD_OU_SIZE_UV - 1, 0, iPicStrideUV * (BGD_OU_SIZE_UV - 1) };
        int32_t* iStride = stackalloc int32_t[4] { iPicStrideUV, iPicStrideUV, 1, 1 };

        // V component first, high probability because V stands for red color and human skin colors have more weight on this component
        for (int32_t i = 0; i < 4; i++)
        {
            if ((iNeighbourForegroundFlags & kaOUPos[i]) != 0)
            {
                uint8_t* pRefC = (uint8_t*)pBgdParam->pRef[2] + iStartSamplePos + aEdgeOffset[i];
                uint8_t* pCurC = (uint8_t*)pBgdParam->pCur[2] + iStartSamplePos + aEdgeOffset[i];
                if (CalculateAsdChromaEdge(pRefC, pCurC, iStride[i]) > BGD_THD_ASD_UV)
                {
                    return true;
                }
            }
        }
        // U component, which stands for blue color, low probability
        for (int32_t i = 0; i < 4; i++)
        {
            if ((iNeighbourForegroundFlags & kaOUPos[i]) != 0)
            {
                uint8_t* pRefC = (uint8_t*)pBgdParam->pRef[1] + iStartSamplePos + aEdgeOffset[i];
                uint8_t* pCurC = (uint8_t*)pBgdParam->pCur[1] + iStartSamplePos + aEdgeOffset[i];
                if (CalculateAsdChromaEdge(pRefC, pCurC, iStride[i]) > BGD_THD_ASD_UV)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static void ForegroundDilation(SBackgroundOU* pBackgroundOU, SBackgroundOU** pOUNeighbours, vBGDParam* pBgdParam,
                                           int32_t iChromaSampleStartPos)
    {
        int32_t iPicStrideUV = pBgdParam->iStride[1];
        int32_t iSumNeighBackgroundFlags = pOUNeighbours[0]->iBackgroundFlag + pOUNeighbours[1]->iBackgroundFlag +
                                           pOUNeighbours[2]->iBackgroundFlag + pOUNeighbours[3]->iBackgroundFlag;

        if (pBackgroundOU->iSAD > BGD_OU_SIZE * Q_FACTOR)
        {
            switch (iSumNeighBackgroundFlags)
            {
                case 0:
                case 1:
                    pBackgroundOU->iBackgroundFlag = 0;
                    break;
                case 2:
                case 3:
                    pBackgroundOU->iBackgroundFlag = ForegroundDilation23Luma(pBackgroundOU, pOUNeighbours) ? 0 : 1;

                    // chroma component check
                    if (pBackgroundOU->iBackgroundFlag == 1)
                    {
                        int8_t iNeighbourForegroundFlags = (int8_t)((pOUNeighbours[0]->iBackgroundFlag == 0 ? 1 : 0)
                                                                    | ((pOUNeighbours[1]->iBackgroundFlag == 0 ? 1 : 0) << 1)
                                                                    | ((pOUNeighbours[2]->iBackgroundFlag == 0 ? 1 : 0) << 2)
                                                                    | ((pOUNeighbours[3]->iBackgroundFlag == 0 ? 1 : 0) << 3));
                        pBackgroundOU->iBackgroundFlag = ForegroundDilation23Chroma(iNeighbourForegroundFlags, iChromaSampleStartPos,
                                                         iPicStrideUV, pBgdParam) ? 0 : 1;
                    }
                    break;
                default:
                    break;
            }
        }
    }

    private static void BackgroundErosion(SBackgroundOU* pBackgroundOU, SBackgroundOU** pOUNeighbours)
    {
        if (pBackgroundOU->iMaxDiffSubSd <= (BGD_OU_SIZE * Q_FACTOR))
        { //BGD_OU_SIZE*BGD_OU_SIZE>>2
            int32_t iSumNeighBackgroundFlags = pOUNeighbours[0]->iBackgroundFlag + pOUNeighbours[1]->iBackgroundFlag +
                                               pOUNeighbours[2]->iBackgroundFlag + pOUNeighbours[3]->iBackgroundFlag;
            int32_t sumNbrBGsad = (pOUNeighbours[0]->iSAD & (-pOUNeighbours[0]->iBackgroundFlag)) + (pOUNeighbours[2]->iSAD &
                                  (-pOUNeighbours[2]->iBackgroundFlag))
                                  + (pOUNeighbours[1]->iSAD & (-pOUNeighbours[1]->iBackgroundFlag)) + (pOUNeighbours[3]->iSAD &
                                      (-pOUNeighbours[3]->iBackgroundFlag));
            if (pBackgroundOU->iSAD * iSumNeighBackgroundFlags <= (3 * sumNbrBGsad) >> 1)
            {
                if (iSumNeighBackgroundFlags == 4)
                {
                    pBackgroundOU->iBackgroundFlag = 1;
                }
                else
                {
                    if ((pOUNeighbours[0]->iBackgroundFlag & pOUNeighbours[1]->iBackgroundFlag) != 0
                        || (pOUNeighbours[2]->iBackgroundFlag & pOUNeighbours[3]->iBackgroundFlag) != 0)
                    {
                        pBackgroundOU->iBackgroundFlag = ForegroundDilation23Luma(pBackgroundOU, pOUNeighbours) ? 0 : 1;
                    }
                }
            }
        }
    }

    private static void SetBackgroundMbFlag(int8_t* pBackgroundMbFlag, int32_t iPicWidthInMb, int32_t iBackgroundMbFlag)
    {
        *pBackgroundMbFlag = (int8_t)iBackgroundMbFlag;
    }

    private static void UpperOUForegroundCheck(SBackgroundOU* pCurOU, int8_t* pBackgroundMbFlag, int32_t iPicWidthInOU,
                                               int32_t iPicWidthInMb)
    {
        if (pCurOU->iSAD > BGD_OU_SIZE * Q_FACTOR)
        {
            SBackgroundOU* pOU_L = pCurOU - 1;
            SBackgroundOU* pOU_R = pCurOU + 1;
            SBackgroundOU* pOU_U = pCurOU - iPicWidthInOU;
            SBackgroundOU* pOU_D = pCurOU + iPicWidthInOU;
            if (pOU_L->iBackgroundFlag + pOU_R->iBackgroundFlag + pOU_U->iBackgroundFlag + pOU_D->iBackgroundFlag <= 1)
            {
                SetBackgroundMbFlag(pBackgroundMbFlag, iPicWidthInMb, 0);
                pCurOU->iBackgroundFlag = 0;
            }
        }
    }

    private static void ForegroundDilationAndBackgroundErosion(vBGDParam* pBgdParam)
    {
        int32_t iPicStrideUV = pBgdParam->iStride[1];
        int32_t iPicWidthInOU = pBgdParam->iBgdWidth >> LOG2_BGD_OU_SIZE;
        int32_t iPicHeightInOU = pBgdParam->iBgdHeight >> LOG2_BGD_OU_SIZE;
        int32_t iOUStrideUV = iPicStrideUV << (LOG2_BGD_OU_SIZE - 1);
        int32_t iPicWidthInMb = (15 + pBgdParam->iBgdWidth) >> 4;

        SBackgroundOU* pBackgroundOU = pBgdParam->pOU_array;
        int8_t* pVaaBackgroundMbFlag = pBgdParam->pBackgroundMbFlag;
        SBackgroundOU** pOUNeighbours = stackalloc SBackgroundOU*[4];//0: left; 1: right; 2: top; 3: bottom

        pOUNeighbours[2] = pBackgroundOU;//top OU
        for (int32_t j = 0; j < iPicHeightInOU; j++)
        {
            int8_t* pRowSkipFlag = pVaaBackgroundMbFlag;
            pOUNeighbours[0] = pBackgroundOU;//left OU
            pOUNeighbours[3] = pBackgroundOU + (iPicWidthInOU & ((j == iPicHeightInOU - 1 ? 1 : 0) - 1)); //bottom OU
            for (int32_t i = 0; i < iPicWidthInOU; i++)
            {
                pOUNeighbours[1] = pBackgroundOU + (i < iPicWidthInOU - 1 ? 1 : 0); //right OU

                if (pBackgroundOU->iBackgroundFlag != 0)
                {
                    ForegroundDilation(pBackgroundOU, pOUNeighbours, pBgdParam, j * iOUStrideUV + (i << LOG2_BGD_OU_SIZE_UV));
                }
                else
                {
                    BackgroundErosion(pBackgroundOU, pOUNeighbours);
                }

                // check the up OU
                if (j > 1 && i > 0 && i < iPicWidthInOU - 1 && pOUNeighbours[2]->iBackgroundFlag == 1)
                {
                    UpperOUForegroundCheck(pOUNeighbours[2], pRowSkipFlag - OU_SIZE_IN_MB * iPicWidthInMb, iPicWidthInOU, iPicWidthInMb);
                }

                SetBackgroundMbFlag(pRowSkipFlag, iPicWidthInMb, pBackgroundOU->iBackgroundFlag);

                // preparation for the next OU
                pRowSkipFlag += OU_SIZE_IN_MB;
                pOUNeighbours[0] = pBackgroundOU;
                pOUNeighbours[2]++;
                pOUNeighbours[3]++;
                pBackgroundOU++;
            }
            pOUNeighbours[2] = pBackgroundOU - iPicWidthInOU;
            pVaaBackgroundMbFlag += OU_SIZE_IN_MB * iPicWidthInMb;
        }
    }

    private static void BackgroundDetection(vBGDParam* pBgdParam)
    {
        // 1st step: foreground/background coarse division
        ForegroundBackgroundDivision(pBgdParam);

        // 2nd step: foreground dilation and background erosion
        ForegroundDilationAndBackgroundErosion(pBgdParam);
    }
}
