//-----------------------------------------------------------------------------
// Filename: md.cs
//
// Description: Port of codec/encoder/core/inc/md.h and
// codec/encoder/core/src/md.cpp, part 1 (E4): SWelsMD, the VAA constants,
// the intra neighbour cache, the VAA analysis kernels and
// InitIntraAnalysisVaaInfo. The inter neighbour cache, the sub-pel
// refinement and the SAD prediction follow with P slices in E5.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// C kernels only. The anonymous sMe struct in SWelsMD is named SMeEntry.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 26 Nov 2026  Claude          Created (E4).
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

namespace SIPSorcery.H264.Encoder;

internal static unsafe partial class WelsEnc
{
    public const int ME_REFINE_BUF_STRIDE = 32;
    public const int ME_REFINE_BUF_WIDTH_BLK4 = 8;
    public const int ME_REFINE_BUF_WIDTH_BLK8 = 16;
    public const int ME_REFINE_BUF_STRIDE_BLK4 = 160;
    public const int ME_REFINE_BUF_STRIDE_BLK8 = 320;

    public const int REFINE_ME_NO_BEST_HALF_PIXEL = 0; //( 0,  0)
    public const int REFINE_ME_HALF_PIXEL_LEFT = 3;    //(-2,  0)
    public const int REFINE_ME_HALF_PIXEL_RIGHT = 4;   //( 2,  0)
    public const int REFINE_ME_HALF_PIXEL_TOP = 1;     //( 0, -2)
    public const int REFINE_ME_HALF_PIXEL_BOTTOM = 2;  //( 0,  2)

    public const int ME_NO_BEST_QUAR_PIXEL = 1; //( 0,  0) or best half pixel
    public const int ME_QUAR_PIXEL_LEFT = 2;    //(-1,  0)
    public const int ME_QUAR_PIXEL_RIGHT = 3;   //( 1,  0)
    public const int ME_QUAR_PIXEL_TOP = 4;     //( 0, -1)
    public const int ME_QUAR_PIXEL_BOTTOM = 5;  //( 0,  1)

    public const int NO_BEST_FRAC_PIX = 1; // REFINE_ME_NO_BEST_HALF_PIXEL + ME_NO_BEST_QUAR_PIXEL

    //for vaa constants
    public const int MBVAASIGN_FLAT = 15;
    public const int MBVAASIGN_HOR1 = 3;
    public const int MBVAASIGN_HOR2 = 12;
    public const int MBVAASIGN_VER1 = 5;
    public const int MBVAASIGN_VER2 = 10;
    public const int MBVAASIGN_CMPX1 = 6;
    public const int MBVAASIGN_CMPX2 = 9;

    // md.cpp
    public const int INTRA_VARIANCE_SAD_THRESHOLD = 150;
    public const int INTER_VARIANCE_SAD_THRESHOLD = 20;

    //fill cache of neighbor MB, containing pNonZeroCount, sample_avail, pIntra4x4PredMode
    public static void FillNeighborCacheIntra(SMbCache* pMbCache, SMB* pCurMb, int32_t iMbWidth)
    {
        uint32_t uiNeighborAvail = pCurMb->uiNeighborAvail;
        uint32_t uiNeighborIntra = 0;

        if ((uiNeighborAvail & LEFT_MB_POS) != 0)
        { //LEFT MB
            int8_t* pLeftMbNonZeroCount = pCurMb->pNonZeroCount - MB_LUMA_CHROMA_BLOCK4x4_NUM;
            pMbCache->iNonZeroCoeffCount[8] = pLeftMbNonZeroCount[3];
            pMbCache->iNonZeroCoeffCount[16] = pLeftMbNonZeroCount[7];
            pMbCache->iNonZeroCoeffCount[24] = pLeftMbNonZeroCount[11];
            pMbCache->iNonZeroCoeffCount[32] = pLeftMbNonZeroCount[15];

            pMbCache->iNonZeroCoeffCount[13] = pLeftMbNonZeroCount[17];
            pMbCache->iNonZeroCoeffCount[21] = pLeftMbNonZeroCount[21];
            pMbCache->iNonZeroCoeffCount[37] = pLeftMbNonZeroCount[19];
            pMbCache->iNonZeroCoeffCount[45] = pLeftMbNonZeroCount[23];

            uiNeighborIntra |= LEFT_MB_POS;

            if (IS_INTRA4x4((pCurMb - 1)->uiMbType))
            {
                int8_t* pLeftMbIntra4x4PredMode = pCurMb->pIntra4x4PredMode - INTRA_4x4_MODE_NUM;
                pMbCache->iIntraPredMode[8] = pLeftMbIntra4x4PredMode[4];
                pMbCache->iIntraPredMode[16] = pLeftMbIntra4x4PredMode[5];
                pMbCache->iIntraPredMode[24] = pLeftMbIntra4x4PredMode[6];
                pMbCache->iIntraPredMode[32] = pLeftMbIntra4x4PredMode[3];
            }
            else
            { // if ( 0 == constrained_intra_pred_flag || IS_INTRA16x16((pCurMb-1)->uiMbType ))
                pMbCache->iIntraPredMode[8] =
                  pMbCache->iIntraPredMode[16] =
                    pMbCache->iIntraPredMode[24] =
                      pMbCache->iIntraPredMode[32] = 2; //DC
            }
        }
        else
        {
            pMbCache->iNonZeroCoeffCount[8] =
              pMbCache->iNonZeroCoeffCount[16] =
                pMbCache->iNonZeroCoeffCount[24] =
                  pMbCache->iNonZeroCoeffCount[32] = -1;//unavailable
            pMbCache->iNonZeroCoeffCount[13] =
              pMbCache->iNonZeroCoeffCount[21] =
                pMbCache->iNonZeroCoeffCount[37] =
                  pMbCache->iNonZeroCoeffCount[45] = -1;//unavailable

            pMbCache->iIntraPredMode[8] =
              pMbCache->iIntraPredMode[16] =
                pMbCache->iIntraPredMode[24] =
                  pMbCache->iIntraPredMode[32] = -1;//unavailable
        }

        if ((uiNeighborAvail & TOP_MB_POS) != 0)
        { //TOP MB
            SMB* pTopMb = pCurMb - iMbWidth;
            ST32(&pMbCache->iNonZeroCoeffCount[1], LD32(&pTopMb->pNonZeroCount[12]));

            ST16(&pMbCache->iNonZeroCoeffCount[6], LD16(&pTopMb->pNonZeroCount[20]));
            ST16(&pMbCache->iNonZeroCoeffCount[30], LD16(&pTopMb->pNonZeroCount[22]));

            uiNeighborIntra |= TOP_MB_POS;

            if (IS_INTRA4x4(pTopMb->uiMbType))
            {
                ST32(pMbCache->iIntraPredMode + 1, LD32(&pTopMb->pIntra4x4PredMode[0]));
            }
            else
            { // if ( 0 == constrained_intra_pred_flag || IS_INTRA16x16( pTopMb->uiMbType ))
                const uint32_t kuiDc32 = 0x02020202;
                ST32(pMbCache->iIntraPredMode + 1, kuiDc32);
            }
        }
        else
        {
            const uint32_t kuiUnavail32 = 0xffffffff;
            ST32(pMbCache->iIntraPredMode + 1, kuiUnavail32);
            ST32(&pMbCache->iNonZeroCoeffCount[1], kuiUnavail32);

            ST16(&pMbCache->iNonZeroCoeffCount[6], 0xffff);
            ST16(&pMbCache->iNonZeroCoeffCount[30], 0xffff);
        }

        if ((uiNeighborAvail & TOPLEFT_MB_POS) != 0)
        {
            uiNeighborIntra |= 0x04;
        }


        if ((uiNeighborAvail & TOPRIGHT_MB_POS) != 0)
        {
            uiNeighborIntra |= 0x08;
        }
        pMbCache->uiNeighborIntra = (uint8_t)uiNeighborIntra;
    }

    // FillNeighborCacheInterWithoutBGD, FillNeighborCacheInterWithBGD and
    // InitFillNeighborCacheInterFunc come with E5.

    public static void UpdateMbMv_c(SMVUnitXY* pMvBuffer, SMVUnitXY ksMv)
    {
        int32_t k = 0;
        for (; k < MB_BLOCK4x4_NUM; k += 4)
        {
            pMvBuffer[k] =
              pMvBuffer[k + 1] =
                pMvBuffer[k + 2] =
                  pMvBuffer[k + 3] = ksMv;
        }
    }


    public static uint8_t MdInterAnalysisVaaInfo_c(int32_t* pSad8x8)
    {
        int32_t* iSadBlock = stackalloc int32_t[4];
        int32_t* iAverageSadBlock = stackalloc int32_t[4];
        int32_t iAverageSad, iVarianceSad;

        iSadBlock[0] = pSad8x8[0];
        iAverageSad = iSadBlock[0];

        iSadBlock[1] = pSad8x8[1];
        iAverageSad += iSadBlock[1];

        iSadBlock[2] = pSad8x8[2];
        iAverageSad += iSadBlock[2];

        iSadBlock[3] = pSad8x8[3];
        iAverageSad += iSadBlock[3];

        iAverageSad = iAverageSad >> 2;

        iAverageSadBlock[0] = (iSadBlock[0] >> 6) - (iAverageSad >> 6);
        iVarianceSad = iAverageSadBlock[0] * iAverageSadBlock[0];

        iAverageSadBlock[1] = (iSadBlock[1] >> 6) - (iAverageSad >> 6);
        iVarianceSad += iAverageSadBlock[1] * iAverageSadBlock[1];

        iAverageSadBlock[2] = (iSadBlock[2] >> 6) - (iAverageSad >> 6);
        iVarianceSad += iAverageSadBlock[2] * iAverageSadBlock[2];

        iAverageSadBlock[3] = (iSadBlock[3] >> 6) - (iAverageSad >> 6);
        iVarianceSad += iAverageSadBlock[3] * iAverageSadBlock[3];

        if (iVarianceSad < INTER_VARIANCE_SAD_THRESHOLD)
        {
            return 15;
        }

        uint8_t uiMbSign = 0;
        if (iSadBlock[0] > iAverageSad)
            uiMbSign |= 0x08;
        if (iSadBlock[1] > iAverageSad)
            uiMbSign |= 0x04;
        if (iSadBlock[2] > iAverageSad)
            uiMbSign |= 0x02;
        if (iSadBlock[3] > iAverageSad)
            uiMbSign |= 0x01;
        return (uiMbSign);
    }

    public static int32_t AnalysisVaaInfoIntra_c(uint8_t* pDataY, int32_t kiLineSize)
    {
        uint16_t* uiAvgBlock = stackalloc uint16_t[16];
        uint16_t* pBlock = &uiAvgBlock[0];
        uint8_t* pEncData = pDataY;
        int32_t kiLineSize2 = kiLineSize << 1;
        int32_t kiLineSize3 = kiLineSize + kiLineSize2;
        int32_t kiLineSize4 = kiLineSize << 2;
        int32_t i = 0, j = 0, num = 0;
        int32_t iSumAvg = 0, iSumSqr = 0;

        //  analysis_vaa_info_intra_core_c( pDataY, iLineSize, pBlock );
        for (; j < 16; j += 4)
        {
            num = 0;
            for (i = 0; i < 16; i += 4, num++)
            {
                pBlock[num] = (uint16_t)(pEncData[i] + pEncData[i + 1] + pEncData[i + 2] + pEncData[i +
                               3]);
                pBlock[num] += (uint16_t)(pEncData[i + kiLineSize] + pEncData[i + kiLineSize + 1] + pEncData[i + kiLineSize + 2] + pEncData[i +
                               kiLineSize + 3]);
                pBlock[num] += (uint16_t)(pEncData[i + kiLineSize2] + pEncData[i + kiLineSize2 + 1] + pEncData[i + kiLineSize2 + 2] + pEncData[i +
                               kiLineSize2 + 3]);
                pBlock[num] += (uint16_t)(pEncData[i + kiLineSize3] + pEncData[i + kiLineSize3 + 1] + pEncData[i + kiLineSize3 + 2] + pEncData[i +
                               kiLineSize3 + 3]);
                pBlock[num] >>= 4;
            }
            pBlock += 4;
            pEncData += kiLineSize4;
        }

        pBlock = &uiAvgBlock[0];
        i = 4;
        for (; i > 0; --i)
        {
            iSumAvg += pBlock[0] + pBlock[1] + pBlock[2] + pBlock[3];
            iSumSqr += pBlock[0] * pBlock[0] + pBlock[1] * pBlock[1] + pBlock[2] * pBlock[2] + pBlock[3] * pBlock[3];

            pBlock += 4;
        }


        return /*variance =*/ (iSumSqr - ((iSumAvg * iSumAvg) >> 4));
    }

    // for pfGetVarianceFromIntraVaa function ptr adaptive by CPU features, 6/7/2010
    public static void InitIntraAnalysisVaaInfo(SWelsFuncPtrList* pFuncList, uint32_t kuiCpuFlag)
    {
        pFuncList->pfGetVarianceFromIntraVaa = &AnalysisVaaInfoIntra_c;
        pFuncList->pfGetMbSignFromInterVaa = &MdInterAnalysisVaaInfo_c;
        pFuncList->pfUpdateMbMv = &UpdateMbMv_c;
    }

    public static bool MdIntraAnalysisVaaInfo(sWelsEncCtx* pEncCtx, uint8_t* pEncMb)
    {

        SDqLayer* pCurDqLayer = pEncCtx->pCurDqLayer;
        int32_t kiLineSize = pCurDqLayer->iEncStride[0];
        int32_t kiVariance = pEncCtx->pFuncList->pfGetVarianceFromIntraVaa(pEncMb, kiLineSize);
        return (kiVariance >= INTRA_VARIANCE_SAD_THRESHOLD);
    }

    // InitMeRefinePointer, MeRefineFracPixel, InitBlkStrideWithRef, MvdCostInit,
    // PredictSad and PredictSadSkip come with E5.
}

// if we want keep total sizeof(SWelsMD) <= 256, we maybe need to seperate three member of SWelsME.
/// <summary>The anonymous sMe struct of TagWelsMD.</summary>
internal struct SMeEntry
{
    public SWelsME sMe16x16;                            //adjust each SWelsME for 8 D-word!
    public InlineArray4<SWelsME> sMe8x8;
    public InlineArray2<SWelsME> sMe16x8;
    public InlineArray2<SWelsME> sMe8x16;
    public InlineArray4<InlineArray4<SWelsME>> sMe4x4;
    public InlineArray4<InlineArray2<SWelsME>> sMe8x4;
    public InlineArray4<InlineArray2<SWelsME>> sMe4x8;
    //  SMVUnitXY     i_mvbs[MB_BLOCK8x8_NUM];        //scaled MVB
}

/// <summary>TagWelsMD.</summary>
internal unsafe struct SWelsMD
{
    public int32_t iLambda;
    public uint16_t* pMvdCost;

    public int32_t iCostLuma;
    public int32_t iCostChroma;//satd+lambda(best_pred_mode) //i_sad_chroma;
    public int32_t iSadPredMb;

    public uint8_t uiRef; //uiRefIndex appointed by Encoder, used for MC
    public bool bMdUsingSad;
    public uint16_t uiReserved;

    public int32_t iCostSkipMb;
    public int32_t iSadPredSkip;

    public int32_t iMbPixX;                // pixel position of MB in horizontal axis
    public int32_t iMbPixY;                // pixel position of MB in vertical axis
    public fixed int iBlock8x8StaticIdc[4];

    //NO B frame in our Wels, we can ignore list1

    public SMeEntry sMe;
}
