//-----------------------------------------------------------------------------
// Filename: md.cs
//
// Description: Port of codec/encoder/core/inc/md.h and
// codec/encoder/core/src/md.cpp: SWelsMD, SMeRefinePointer, the VAA
// constants, the intra and inter neighbour caches, the VAA analysis kernels,
// the sub-pel refinement, the MVD cost table and the SAD prediction.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// C kernels only. The anonymous sMe struct in SWelsMD is named SMeEntry.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 26 Nov 2026  Claude          Created (E4).
// 26 Nov 2026  Claude          The inter neighbour cache, refinement and SAD prediction (E5).
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

using System.Runtime.CompilerServices;

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

    public static void FillNeighborCacheInterWithoutBGD(SMbCache* pMbCache, SMB* pCurMb, int32_t iMbWidth, int8_t* pVaaBgMbFlag)
    {
        uint32_t uiNeighborAvail = pCurMb->uiNeighborAvail;
        SMB* pLeftMb = pCurMb - 1;
        SMB* pTopMb = pCurMb - iMbWidth;
        SMB* pLeftTopMb = pCurMb - iMbWidth - 1;
        SMB* iRightTopMb = pCurMb - iMbWidth + 1;
        SMVComponentUnit* pMvComp = &pMbCache->sMvComponents;
        SMVUnitXY* pMvCache = (SMVUnitXY*)&pMvComp->sMotionVectorCache;
        if ((uiNeighborAvail & LEFT_MB_POS) != 0 && IS_SVC_INTER(pLeftMb->uiMbType))
        {
            pMvCache[6] = pLeftMb->sMv[3];
            pMvCache[12] = pLeftMb->sMv[7];
            pMvCache[18] = pLeftMb->sMv[11];
            pMvCache[24] = pLeftMb->sMv[15];
            pMvComp->iRefIndexCache[6] = pLeftMb->pRefIndex[1];
            pMvComp->iRefIndexCache[12] = pLeftMb->pRefIndex[1];
            pMvComp->iRefIndexCache[18] = pLeftMb->pRefIndex[3];
            pMvComp->iRefIndexCache[24] = pLeftMb->pRefIndex[3];
            pMbCache->iSadCost[3] = pLeftMb->pSadCost[0];

            if (pLeftMb->uiMbType == MB_TYPE_SKIP)
            {
                pMbCache->bMbTypeSkip[3] = true;
                pMbCache->iSadCostSkip[3] = pMbCache->pEncSad[-1];
            }
            else
            {
                pMbCache->bMbTypeSkip[3] = false;
                pMbCache->iSadCostSkip[3] = 0;
            }
        }
        else
        { //avail or non-inter
            ST32(&pMvCache[6], 0);
            ST32(&pMvCache[12], 0);
            ST32(&pMvCache[18], 0);
            ST32(&pMvCache[24], 0);
            pMvComp->iRefIndexCache[6] =
                pMvComp->iRefIndexCache[12] =
                    pMvComp->iRefIndexCache[18] =
                        pMvComp->iRefIndexCache[24] = (int8_t)((uiNeighborAvail & LEFT_MB_POS) != 0 ? REF_NOT_IN_LIST : REF_NOT_AVAIL);
            pMbCache->iSadCost[3] = 0;
            pMbCache->bMbTypeSkip[3] = false;
            pMbCache->iSadCostSkip[3] = 0;
        }

        if ((uiNeighborAvail & TOP_MB_POS) != 0 && IS_SVC_INTER(pTopMb->uiMbType))
        { //TOP MB
            ST64(&pMvCache[1], LD64(&pTopMb->sMv[12]));
            ST64(&pMvCache[3], LD64(&pTopMb->sMv[14]));
            pMvComp->iRefIndexCache[1] = pTopMb->pRefIndex[2];
            pMvComp->iRefIndexCache[2] = pTopMb->pRefIndex[2];
            pMvComp->iRefIndexCache[3] = pTopMb->pRefIndex[3];
            pMvComp->iRefIndexCache[4] = pTopMb->pRefIndex[3];
            pMbCache->iSadCost[1] = pTopMb->pSadCost[0];

            if (pTopMb->uiMbType == MB_TYPE_SKIP)
            {
                pMbCache->bMbTypeSkip[1] = true;
                pMbCache->iSadCostSkip[1] = pMbCache->pEncSad[-iMbWidth];
            }
            else
            {
                pMbCache->bMbTypeSkip[1] = false;
                pMbCache->iSadCostSkip[1] = 0;
            }
        }
        else
        { //unavail
            ST64(&pMvCache[1], 0);
            ST64(&pMvCache[3], 0);
            pMvComp->iRefIndexCache[1] =
                pMvComp->iRefIndexCache[2] =
                    pMvComp->iRefIndexCache[3] =
                        pMvComp->iRefIndexCache[4] = (int8_t)((uiNeighborAvail & TOP_MB_POS) != 0 ? REF_NOT_IN_LIST : REF_NOT_AVAIL);
            pMbCache->iSadCost[1] = 0;

            pMbCache->bMbTypeSkip[1] = false;
            pMbCache->iSadCostSkip[1] = 0;
        }

        if ((uiNeighborAvail & TOPLEFT_MB_POS) != 0 && IS_SVC_INTER(pLeftTopMb->uiMbType))
        { //LEFT_TOP MB
            pMvCache[0] = pLeftTopMb->sMv[15];
            pMvComp->iRefIndexCache[0] = pLeftTopMb->pRefIndex[3];
            pMbCache->iSadCost[0] = pLeftTopMb->pSadCost[0];

            if (pLeftTopMb->uiMbType == MB_TYPE_SKIP)
            {
                pMbCache->bMbTypeSkip[0] = true;
                pMbCache->iSadCostSkip[0] = pMbCache->pEncSad[-iMbWidth - 1];
            }
            else
            {
                pMbCache->bMbTypeSkip[0] = false;
                pMbCache->iSadCostSkip[0] = 0;
            }
        }
        else
        { //unavail
            ST32(&pMvCache[0], 0);
            pMvComp->iRefIndexCache[0] = (int8_t)((uiNeighborAvail & TOPLEFT_MB_POS) != 0 ? REF_NOT_IN_LIST : REF_NOT_AVAIL);
            pMbCache->iSadCost[0] = 0;
            pMbCache->bMbTypeSkip[0] = false;
            pMbCache->iSadCostSkip[0] = 0;
        }

        if ((uiNeighborAvail & TOPRIGHT_MB_POS) != 0 && IS_SVC_INTER(iRightTopMb->uiMbType))
        { //RIGHT_TOP MB
            pMvCache[5] = iRightTopMb->sMv[12];
            pMvComp->iRefIndexCache[5] = iRightTopMb->pRefIndex[2];
            pMbCache->iSadCost[2] = iRightTopMb->pSadCost[0];

            if (iRightTopMb->uiMbType == MB_TYPE_SKIP)
            {
                pMbCache->bMbTypeSkip[2] = true;
                pMbCache->iSadCostSkip[2] = pMbCache->pEncSad[-iMbWidth + 1];
            }
            else
            {
                pMbCache->bMbTypeSkip[2] = false;
                pMbCache->iSadCostSkip[2] = 0;
            }
        }
        else
        { //unavail
            ST32(&pMvCache[5], 0);
            pMvComp->iRefIndexCache[5] = (int8_t)((uiNeighborAvail & TOPRIGHT_MB_POS) != 0 ? REF_NOT_IN_LIST : REF_NOT_AVAIL);
            pMbCache->iSadCost[2] = 0;
            pMbCache->bMbTypeSkip[2] = false;
            pMbCache->iSadCostSkip[2] = 0;
        }

        //right-top 4*4 pBlock unavailable
        ST32(&pMvCache[9], 0);
        ST32(&pMvCache[21], 0);
        ST32(&pMvCache[11], 0);
        ST32(&pMvCache[17], 0);
        ST32(&pMvCache[23], 0);
        pMvComp->iRefIndexCache[9] =
            pMvComp->iRefIndexCache[11] =
                pMvComp->iRefIndexCache[17] =
                    pMvComp->iRefIndexCache[21] =
                        pMvComp->iRefIndexCache[23] = REF_NOT_AVAIL;
    }

    public static void FillNeighborCacheInterWithBGD(SMbCache* pMbCache, SMB* pCurMb, int32_t iMbWidth, int8_t* pVaaBgMbFlag)
    {
        uint32_t uiNeighborAvail = pCurMb->uiNeighborAvail;
        SMB* pLeftMb = pCurMb - 1;
        SMB* pTopMb = pCurMb - iMbWidth;
        SMB* pLeftTopMb = pCurMb - iMbWidth - 1;
        SMB* iRightTopMb = pCurMb - iMbWidth + 1;
        SMVComponentUnit* pMvComp = &pMbCache->sMvComponents;
        SMVUnitXY* pMvCache = (SMVUnitXY*)&pMvComp->sMotionVectorCache;

        if ((uiNeighborAvail & LEFT_MB_POS) != 0 && IS_SVC_INTER(pLeftMb->uiMbType))
        {
            pMvCache[6] = pLeftMb->sMv[3];
            pMvCache[12] = pLeftMb->sMv[7];
            pMvCache[18] = pLeftMb->sMv[11];
            pMvCache[24] = pLeftMb->sMv[15];
            pMvComp->iRefIndexCache[6] = pLeftMb->pRefIndex[1];
            pMvComp->iRefIndexCache[12] = pLeftMb->pRefIndex[1];
            pMvComp->iRefIndexCache[18] = pLeftMb->pRefIndex[3];
            pMvComp->iRefIndexCache[24] = pLeftMb->pRefIndex[3];
            pMbCache->iSadCost[3] = pLeftMb->pSadCost[0];

            if (pLeftMb->uiMbType == MB_TYPE_SKIP && pVaaBgMbFlag[-1] == 0)
            {
                pMbCache->bMbTypeSkip[3] = true;
                pMbCache->iSadCostSkip[3] = pMbCache->pEncSad[-1];
            }
            else
            {
                pMbCache->bMbTypeSkip[3] = false;
                pMbCache->iSadCostSkip[3] = 0;
            }
        }
        else
        { //avail or non-inter
            ST32(&pMvCache[6], 0);
            ST32(&pMvCache[12], 0);
            ST32(&pMvCache[18], 0);
            ST32(&pMvCache[24], 0);
            pMvComp->iRefIndexCache[6] =
                pMvComp->iRefIndexCache[12] =
                    pMvComp->iRefIndexCache[18] =
                        pMvComp->iRefIndexCache[24] = (int8_t)((uiNeighborAvail & LEFT_MB_POS) != 0 ? REF_NOT_IN_LIST : REF_NOT_AVAIL);
            pMbCache->iSadCost[3] = 0;
            pMbCache->bMbTypeSkip[3] = false;
            pMbCache->iSadCostSkip[3] = 0;
        }

        if ((uiNeighborAvail & TOP_MB_POS) != 0 && IS_SVC_INTER(pTopMb->uiMbType))
        { //TOP MB
            ST64(&pMvCache[1], LD64(&pTopMb->sMv[12]));
            ST64(&pMvCache[3], LD64(&pTopMb->sMv[14]));
            pMvComp->iRefIndexCache[1] = pTopMb->pRefIndex[2];
            pMvComp->iRefIndexCache[2] = pTopMb->pRefIndex[2];
            pMvComp->iRefIndexCache[3] = pTopMb->pRefIndex[3];
            pMvComp->iRefIndexCache[4] = pTopMb->pRefIndex[3];
            pMbCache->iSadCost[1] = pTopMb->pSadCost[0];
            if (pTopMb->uiMbType == MB_TYPE_SKIP && pVaaBgMbFlag[-iMbWidth] == 0)
            {
                pMbCache->bMbTypeSkip[1] = true;
                pMbCache->iSadCostSkip[1] = pMbCache->pEncSad[-iMbWidth];
            }
            else
            {
                pMbCache->bMbTypeSkip[1] = false;
                pMbCache->iSadCostSkip[1] = 0;
            }
        }
        else
        { //unavail
            ST64(&pMvCache[1], 0);
            ST64(&pMvCache[3], 0);
            pMvComp->iRefIndexCache[1] =
                pMvComp->iRefIndexCache[2] =
                    pMvComp->iRefIndexCache[3] =
                        pMvComp->iRefIndexCache[4] = (int8_t)((uiNeighborAvail & TOP_MB_POS) != 0 ? REF_NOT_IN_LIST : REF_NOT_AVAIL);
            pMbCache->iSadCost[1] = 0;
            pMbCache->bMbTypeSkip[1] = false;
            pMbCache->iSadCostSkip[1] = 0;
        }


        if ((uiNeighborAvail & TOPLEFT_MB_POS) != 0 && IS_SVC_INTER(pLeftTopMb->uiMbType))
        { //LEFT_TOP MB
            pMvCache[0] = pLeftTopMb->sMv[15];
            pMvComp->iRefIndexCache[0] = pLeftTopMb->pRefIndex[3];
            pMbCache->iSadCost[0] = pLeftTopMb->pSadCost[0];

            if (pLeftTopMb->uiMbType == MB_TYPE_SKIP && pVaaBgMbFlag[-iMbWidth - 1] == 0)
            {
                pMbCache->bMbTypeSkip[0] = true;
                pMbCache->iSadCostSkip[0] = pMbCache->pEncSad[-iMbWidth - 1];
            }
            else
            {
                pMbCache->bMbTypeSkip[0] = false;
                pMbCache->iSadCostSkip[0] = 0;
            }
        }
        else
        { //unavail
            ST32(&pMvCache[0], 0);
            pMvComp->iRefIndexCache[0] = (int8_t)((uiNeighborAvail & TOPLEFT_MB_POS) != 0 ? REF_NOT_IN_LIST : REF_NOT_AVAIL);
            pMbCache->iSadCost[0] = 0;
            pMbCache->bMbTypeSkip[0] = false;
            pMbCache->iSadCostSkip[0] = 0;
        }

        if ((uiNeighborAvail & TOPRIGHT_MB_POS) != 0 && IS_SVC_INTER(iRightTopMb->uiMbType))
        { //RIGHT_TOP MB
            pMvCache[5] = iRightTopMb->sMv[12];
            pMvComp->iRefIndexCache[5] = iRightTopMb->pRefIndex[2];
            pMbCache->iSadCost[2] = iRightTopMb->pSadCost[0];

            if (iRightTopMb->uiMbType == MB_TYPE_SKIP && pVaaBgMbFlag[-iMbWidth + 1] == 0)
            {
                pMbCache->bMbTypeSkip[2] = true;
                pMbCache->iSadCostSkip[2] = pMbCache->pEncSad[-iMbWidth + 1];
            }
            else
            {
                pMbCache->bMbTypeSkip[2] = false;
                pMbCache->iSadCostSkip[2] = 0;
            }
        }
        else
        { //unavail
            ST32(&pMvCache[5], 0);
            pMvComp->iRefIndexCache[5] = (int8_t)((uiNeighborAvail & TOPRIGHT_MB_POS) != 0 ? REF_NOT_IN_LIST : REF_NOT_AVAIL);
            pMbCache->iSadCost[2] = 0;
            pMbCache->bMbTypeSkip[2] = false;
            pMbCache->iSadCostSkip[2] = 0;
        }

        //right-top 4*4 pBlock unavailable
        ST32(&pMvCache[9], 0);
        ST32(&pMvCache[21], 0);
        ST32(&pMvCache[11], 0);
        ST32(&pMvCache[17], 0);
        ST32(&pMvCache[23], 0);
        pMvComp->iRefIndexCache[9] =
            pMvComp->iRefIndexCache[11] =
                pMvComp->iRefIndexCache[17] =
                    pMvComp->iRefIndexCache[21] =
                        pMvComp->iRefIndexCache[23] = REF_NOT_AVAIL;
    }

    public static void InitFillNeighborCacheInterFunc(SWelsFuncPtrList* pFuncList, int32_t kiFlag)
    {
        pFuncList->pfFillInterNeighborCache = kiFlag != 0 ? &FillNeighborCacheInterWithBGD : &FillNeighborCacheInterWithoutBGD;
    }

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


    public static void InitMeRefinePointer(SMeRefinePointer* pMeRefine, SMbCache* pMbCache, int32_t iStride)
    {
        pMeRefine->pHalfPixH = &pMbCache->pBufferInterPredMe[0] + iStride;
        pMeRefine->pHalfPixV = &pMbCache->pBufferInterPredMe[640] + iStride;

        pMeRefine->pQuarPixBest = &pMbCache->pBufferInterPredMe[1280] + iStride;
        pMeRefine->pQuarPixTmp = &pMbCache->pBufferInterPredMe[1920] + iStride;
    }

    /// <summary>TagQuarParams.</summary>
    private struct SQuarRefineParams
    {
        public int32_t iBestCost;
        public int32_t iBestHalfPix;
        public int32_t iStrideA;
        public int32_t iStrideB;
        public Ptr<uint8_t> pRef;
        public PtrArray4<uint8_t> pSrcB;
        public PtrArray4<uint8_t> pSrcA;
        public InlineArray4<int32_t> iLms;
        public int32_t iBestQuarPix;
    }

    // #define SWITCH_BEST_TMP_BUF(prev_best, curr_best), here always on pQuarPixBest and pQuarPixTmp.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SWITCH_BEST_TMP_BUF(SMeRefinePointer* pMeRefine, SQuarRefineParams* pParams, int32_t iCurCost)
    {
        pParams->iBestCost = iCurCost;
        uint8_t* pTmp = pMeRefine->pQuarPixBest;
        pMeRefine->pQuarPixBest = pMeRefine->pQuarPixTmp;
        pMeRefine->pQuarPixTmp = pTmp;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void MeRefineQuarPixel(SWelsFuncPtrList* pFunc, SWelsME* pMe, SMeRefinePointer* pMeRefine,
                                          int32_t kiWidth, int32_t kiHeight, SQuarRefineParams* pParams, int32_t iStrideEnc)
    {
        PWelsSampleAveragingFunc pSampleAvg = pFunc->sMcFuncs.pfSampleAveraging;
        int32_t iCurCost;
        uint8_t* pEncMb = pMe->pEncMb;
        uint8_t kuiPixel = pMe->uiBlockSize;
        PSampleSadSatdCostFunc pfMeCost = pFunc->sSampleDealingFuncs.pfMeCost[kuiPixel];

        // #define CALC_COST(me_buf, lm) ( pFunc->sSampleDealingFuncs.pfMeCost[kuiPixel](pEncMb, iStrideEnc, me_buf, ME_REFINE_BUF_STRIDE) + lm )
        pSampleAvg(pMeRefine->pQuarPixTmp, ME_REFINE_BUF_STRIDE, pParams->pSrcA[0], ME_REFINE_BUF_STRIDE,
                   pParams->pSrcB[0], pParams->iStrideA, kiWidth, kiHeight);

        iCurCost = pfMeCost(pEncMb, iStrideEnc, pMeRefine->pQuarPixTmp, ME_REFINE_BUF_STRIDE) + pParams->iLms[0];
        if (iCurCost < pParams->iBestCost)
        {
            pParams->iBestQuarPix = ME_QUAR_PIXEL_TOP;
            SWITCH_BEST_TMP_BUF(pMeRefine, pParams, iCurCost);
        }
        //=========================(0, 1)=======================//
        pSampleAvg(pMeRefine->pQuarPixTmp, ME_REFINE_BUF_STRIDE, pParams->pSrcA[1],
                   ME_REFINE_BUF_STRIDE, pParams->pSrcB[1], pParams->iStrideA, kiWidth, kiHeight);
        iCurCost = pfMeCost(pEncMb, iStrideEnc, pMeRefine->pQuarPixTmp, ME_REFINE_BUF_STRIDE) + pParams->iLms[1];
        if (iCurCost < pParams->iBestCost)
        {
            pParams->iBestQuarPix = ME_QUAR_PIXEL_BOTTOM;
            SWITCH_BEST_TMP_BUF(pMeRefine, pParams, iCurCost);
        }
        //==========================(-1, 0)=========================//
        pSampleAvg(pMeRefine->pQuarPixTmp, ME_REFINE_BUF_STRIDE, pParams->pSrcA[2],
                   ME_REFINE_BUF_STRIDE, pParams->pSrcB[2], pParams->iStrideB, kiWidth, kiHeight);
        iCurCost = pfMeCost(pEncMb, iStrideEnc, pMeRefine->pQuarPixTmp, ME_REFINE_BUF_STRIDE) + pParams->iLms[2];
        if (iCurCost < pParams->iBestCost)
        {
            pParams->iBestQuarPix = ME_QUAR_PIXEL_LEFT;
            SWITCH_BEST_TMP_BUF(pMeRefine, pParams, iCurCost);
        }
        //==========================(1, 0)=========================//
        pSampleAvg(pMeRefine->pQuarPixTmp, ME_REFINE_BUF_STRIDE, pParams->pSrcA[3],
                   ME_REFINE_BUF_STRIDE, pParams->pSrcB[3], pParams->iStrideB, kiWidth, kiHeight);

        iCurCost = pfMeCost(pEncMb, iStrideEnc, pMeRefine->pQuarPixTmp, ME_REFINE_BUF_STRIDE) + pParams->iLms[3];
        if (iCurCost < pParams->iBestCost)
        {
            pParams->iBestQuarPix = ME_QUAR_PIXEL_RIGHT;
            SWITCH_BEST_TMP_BUF(pMeRefine, pParams, iCurCost);
        }
    }

    // static const int32_t iMvQuarAddX[10] = {0, 0, -1, 1, 0, 0, 0, -1, 1, 0};
    private static readonly int32_t* iMvQuarAddX = StaticTable.Of<int32_t>(10, [0, 0, -1, 1, 0, 0, 0, -1, 1, 0]);

    public static void MeRefineFracPixel(sWelsEncCtx* pEncCtx, uint8_t* pMemPredInterMb, SWelsME* pMe,
                                         SMeRefinePointer* pMeRefine, int32_t iWidth, int32_t iHeight)
    {
        SWelsFuncPtrList* pFunc = pEncCtx->pFuncList;
        int16_t iMvx = pMe->sMv.iMvX;
        int16_t iMvy = pMe->sMv.iMvY;

        int16_t iHalfMvx = iMvx;
        int16_t iHalfMvy = iMvy;
        int32_t kiStrideEnc = pEncCtx->pCurDqLayer->iEncStride[0];
        int32_t kiStrideRef = pEncCtx->pCurDqLayer->pRefPic->iLineSize[0];

        uint8_t* pEncData = pMe->pEncMb;
        uint8_t* pRef = pMe->pRefMb;//091010

        int32_t iBestQuarPix = ME_NO_BEST_QUAR_PIXEL;

        SQuarRefineParams sParams = default;
        int32_t* pMvQuarAddY = iMvQuarAddX + 3;
        uint8_t* pBestPredInter = pRef;
        int32_t iInterBlk4Stride = ME_REFINE_BUF_STRIDE;

        int32_t iBestCost;
        int32_t iCurCost;
        int32_t iBestHalfPix;
        PSampleSadSatdCostFunc pfMeCost = pFunc->sSampleDealingFuncs.pfMeCost[pMe->uiBlockSize];

        if (pEncCtx->pCurDqLayer->bSatdInMdFlag)
        {
            iBestCost = (int32_t)pMe->uSadPredISatd.uiSatd + COST_MVD(pMe->pMvdCost, iMvx - pMe->sMvp.iMvX, iMvy - pMe->sMvp.iMvY);
        }
        else
        {
            iBestCost = pfMeCost(pEncData, kiStrideEnc, pRef, kiStrideRef) +
                        COST_MVD(pMe->pMvdCost, iMvx - pMe->sMvp.iMvX, iMvy - pMe->sMvp.iMvY);
        }

        iBestHalfPix = REFINE_ME_NO_BEST_HALF_PIXEL;

        pFunc->sMcFuncs.pfLumaHalfpelVer(pRef - kiStrideRef, kiStrideRef, pMeRefine->pHalfPixV, ME_REFINE_BUF_STRIDE, iWidth,
                                         iHeight + 1);

        //step 1: get [iWidth][iHeight+1] half pixel from vertical filter
        //===========================(0, -2)==============================//
        iCurCost = pfMeCost(pEncData, kiStrideEnc, pMeRefine->pHalfPixV,
                            ME_REFINE_BUF_STRIDE) +
                   COST_MVD(pMe->pMvdCost, iMvx - pMe->sMvp.iMvX, iMvy - 2 - pMe->sMvp.iMvY);
        if (iCurCost < iBestCost)
        {
            iBestCost = iCurCost;
            iBestHalfPix = REFINE_ME_HALF_PIXEL_TOP;
            pBestPredInter = pMeRefine->pHalfPixV;
        }
        //===========================(0, 2)==============================//
        iCurCost = pfMeCost(pEncData, kiStrideEnc,
                            pMeRefine->pHalfPixV + ME_REFINE_BUF_STRIDE, ME_REFINE_BUF_STRIDE) +
                   COST_MVD(pMe->pMvdCost, iMvx - pMe->sMvp.iMvX, iMvy + 2 - pMe->sMvp.iMvY);
        if (iCurCost < iBestCost)
        {
            iBestCost = iCurCost;
            iBestHalfPix = REFINE_ME_HALF_PIXEL_BOTTOM;
            pBestPredInter = pMeRefine->pHalfPixV + ME_REFINE_BUF_STRIDE;
        }
        pFunc->sMcFuncs.pfLumaHalfpelHor(pRef - 1, kiStrideRef, pMeRefine->pHalfPixH, ME_REFINE_BUF_STRIDE, iWidth + 1,
                                         iHeight);
        //step 2: get [iWidth][iHeight+1] half pixel from horizon filter

        //===========================(-2, 0)==============================//
        iCurCost = pfMeCost(pEncData, kiStrideEnc, pMeRefine->pHalfPixH,
                            ME_REFINE_BUF_STRIDE) +
                   COST_MVD(pMe->pMvdCost, iMvx - 2 - pMe->sMvp.iMvX, iMvy - pMe->sMvp.iMvY);
        if (iCurCost < iBestCost)
        {
            iBestCost = iCurCost;
            iBestHalfPix = REFINE_ME_HALF_PIXEL_LEFT;
            pBestPredInter = pMeRefine->pHalfPixH;
        }
        //===========================(2, 0)===============================//
        iCurCost = pfMeCost(pEncData, kiStrideEnc, pMeRefine->pHalfPixH + 1,
                            ME_REFINE_BUF_STRIDE) +
                   COST_MVD(pMe->pMvdCost, iMvx + 2 - pMe->sMvp.iMvX, iMvy - pMe->sMvp.iMvY);
        if (iCurCost < iBestCost)
        {
            iBestCost = iCurCost;
            iBestHalfPix = REFINE_ME_HALF_PIXEL_RIGHT;
            pBestPredInter = pMeRefine->pHalfPixH + 1;
        }

        sParams.iBestCost = iBestCost;
        sParams.iBestHalfPix = iBestHalfPix;
        sParams.pRef = pRef;
        sParams.iBestQuarPix = ME_NO_BEST_QUAR_PIXEL;

        //step 5: if no best half-pixel prediction, try quarter pixel prediction
        //        if yes, must get [X+1][X+1] half-pixel from (2, 2) horizontal and vertical filter
        if (REFINE_ME_NO_BEST_HALF_PIXEL == iBestHalfPix)
        {
            sParams.iStrideA = kiStrideRef;
            sParams.iStrideB = kiStrideRef;
            sParams.pSrcA[0] = pMeRefine->pHalfPixV;
            sParams.pSrcA[1] = pMeRefine->pHalfPixV + ME_REFINE_BUF_STRIDE;
            sParams.pSrcA[2] = pMeRefine->pHalfPixH;
            sParams.pSrcA[3] = pMeRefine->pHalfPixH + 1;

            sParams.pSrcB[0] = sParams.pSrcB[1] = sParams.pSrcB[2] = sParams.pSrcB[3] = pRef;

            sParams.iLms[0] = COST_MVD(pMe->pMvdCost, iHalfMvx - pMe->sMvp.iMvX, iHalfMvy - 1 - pMe->sMvp.iMvY);
            sParams.iLms[1] = COST_MVD(pMe->pMvdCost, iHalfMvx - pMe->sMvp.iMvX, iHalfMvy + 1 - pMe->sMvp.iMvY);
            sParams.iLms[2] = COST_MVD(pMe->pMvdCost, iHalfMvx - 1 - pMe->sMvp.iMvX, iHalfMvy - pMe->sMvp.iMvY);
            sParams.iLms[3] = COST_MVD(pMe->pMvdCost, iHalfMvx + 1 - pMe->sMvp.iMvX, iHalfMvy - pMe->sMvp.iMvY);
        }
        else
        { //must get [X+1][X+1] half-pixel from (2, 2) horizontal and vertical filter
            switch (iBestHalfPix)
            {
                case REFINE_ME_HALF_PIXEL_LEFT:
                    {
                        pMeRefine->pHalfPixHV = pMeRefine->pHalfPixV;//reuse pBuffer, here only h&hv
                        pFunc->sMcFuncs.pfLumaHalfpelCen(pRef - 1 - kiStrideRef, kiStrideRef, pMeRefine->pHalfPixHV, ME_REFINE_BUF_STRIDE,
                                                         iWidth + 1, iHeight + 1);

                        iHalfMvx -= 2;
                        sParams.iStrideA = ME_REFINE_BUF_STRIDE;
                        sParams.iStrideB = kiStrideRef;
                        sParams.pSrcA[0] = pMeRefine->pHalfPixH;
                        sParams.pSrcA[3] = sParams.pSrcA[2] = sParams.pSrcA[1] = sParams.pSrcA[0];
                        sParams.pSrcB[0] = pMeRefine->pHalfPixHV;
                        sParams.pSrcB[1] = pMeRefine->pHalfPixHV + ME_REFINE_BUF_STRIDE;
                        sParams.pSrcB[2] = pRef - 1;
                        sParams.pSrcB[3] = pRef;
                    }
                    break;
                case REFINE_ME_HALF_PIXEL_RIGHT:
                    {
                        pMeRefine->pHalfPixHV = pMeRefine->pHalfPixV;//reuse pBuffer, here only h&hv
                        pFunc->sMcFuncs.pfLumaHalfpelCen(pRef - 1 - kiStrideRef, kiStrideRef, pMeRefine->pHalfPixHV, ME_REFINE_BUF_STRIDE,
                                                         iWidth + 1, iHeight + 1);
                        iHalfMvx += 2;
                        sParams.iStrideA = ME_REFINE_BUF_STRIDE;
                        sParams.iStrideB = kiStrideRef;
                        sParams.pSrcA[0] = pMeRefine->pHalfPixH + 1;
                        sParams.pSrcA[3] = sParams.pSrcA[2] = sParams.pSrcA[1] = sParams.pSrcA[0];
                        sParams.pSrcB[0] = pMeRefine->pHalfPixHV + 1;
                        sParams.pSrcB[1] = pMeRefine->pHalfPixHV + 1 + ME_REFINE_BUF_STRIDE;
                        sParams.pSrcB[2] = pRef;
                        sParams.pSrcB[3] = pRef + 1;
                    }
                    break;
                case REFINE_ME_HALF_PIXEL_TOP:
                    {
                        pMeRefine->pHalfPixHV = pMeRefine->pHalfPixH;//reuse pBuffer, here only v&hv
                        pFunc->sMcFuncs.pfLumaHalfpelCen(pRef - 1 - kiStrideRef, kiStrideRef, pMeRefine->pHalfPixHV, ME_REFINE_BUF_STRIDE,
                                                         iWidth + 1, iHeight + 1);

                        iHalfMvy -= 2;
                        sParams.iStrideA = kiStrideRef;
                        sParams.iStrideB = ME_REFINE_BUF_STRIDE;
                        sParams.pSrcA[0] = pMeRefine->pHalfPixV;
                        sParams.pSrcA[3] = sParams.pSrcA[2] = sParams.pSrcA[1] = sParams.pSrcA[0];
                        sParams.pSrcB[0] = pRef - kiStrideRef;
                        sParams.pSrcB[1] = pRef;
                        sParams.pSrcB[2] = pMeRefine->pHalfPixHV;
                        sParams.pSrcB[3] = pMeRefine->pHalfPixHV + 1;
                    }
                    break;
                case REFINE_ME_HALF_PIXEL_BOTTOM:
                    {
                        pMeRefine->pHalfPixHV = pMeRefine->pHalfPixH;//reuse pBuffer, here only v&hv
                        pFunc->sMcFuncs.pfLumaHalfpelCen(pRef - 1 - kiStrideRef, kiStrideRef, pMeRefine->pHalfPixHV, ME_REFINE_BUF_STRIDE,
                                                         iWidth + 1, iHeight + 1);
                        iHalfMvy += 2;
                        sParams.iStrideA = kiStrideRef;
                        sParams.iStrideB = ME_REFINE_BUF_STRIDE;
                        sParams.pSrcA[0] = pMeRefine->pHalfPixV + ME_REFINE_BUF_STRIDE;
                        sParams.pSrcA[3] = sParams.pSrcA[2] = sParams.pSrcA[1] = sParams.pSrcA[0];
                        sParams.pSrcB[0] = pRef;
                        sParams.pSrcB[1] = pRef + kiStrideRef;
                        sParams.pSrcB[2] = pMeRefine->pHalfPixHV + ME_REFINE_BUF_STRIDE;
                        sParams.pSrcB[3] = pMeRefine->pHalfPixHV + ME_REFINE_BUF_STRIDE + 1;
                    }
                    break;
                default:
                    break;
            }
            sParams.iLms[0] = COST_MVD(pMe->pMvdCost, iHalfMvx - pMe->sMvp.iMvX, iHalfMvy - 1 - pMe->sMvp.iMvY);
            sParams.iLms[1] = COST_MVD(pMe->pMvdCost, iHalfMvx - pMe->sMvp.iMvX, iHalfMvy + 1 - pMe->sMvp.iMvY);
            sParams.iLms[2] = COST_MVD(pMe->pMvdCost, iHalfMvx - 1 - pMe->sMvp.iMvX, iHalfMvy - pMe->sMvp.iMvY);
            sParams.iLms[3] = COST_MVD(pMe->pMvdCost, iHalfMvx + 1 - pMe->sMvp.iMvX, iHalfMvy - pMe->sMvp.iMvY);
        }
        MeRefineQuarPixel(pFunc, pMe, pMeRefine, iWidth, iHeight, &sParams, kiStrideEnc);

        if (iBestCost > sParams.iBestCost)
        {
            pBestPredInter = pMeRefine->pQuarPixBest;
            iBestCost = sParams.iBestCost;
        }
        iBestQuarPix = sParams.iBestQuarPix;

        //update final best MV
        pMe->sMv.iMvX = (int16_t)(iHalfMvx + iMvQuarAddX[iBestQuarPix]);
        pMe->sMv.iMvY = (int16_t)(iHalfMvy + pMvQuarAddY[iBestQuarPix]);
        pMe->uiSatdCost = (uint32_t)iBestCost;

        //No half or quarter pixel best, so do MC with integer pixel MV
        if (iBestHalfPix + iBestQuarPix == NO_BEST_FRAC_PIX)
        {
            pBestPredInter = pRef;
            iInterBlk4Stride = kiStrideRef;
        }
        pMeRefine->pfCopyBlockByMode(pMemPredInterMb, MB_WIDTH_LUMA, pBestPredInter,
                                     iInterBlk4Stride);
    }

    // static const uint8_t kuiStrideX[16] and kuiStrideY[16] of InitBlkStrideWithRef.
    private static readonly uint8_t* kuiStrideX = StaticTable.Of<uint8_t>(16, [
        0, 4, 0, 4,
        8, 12, 8, 12,
        0, 4, 0, 4,
        8, 12, 8, 12
    ]);
    private static readonly uint8_t* kuiStrideY = StaticTable.Of<uint8_t>(16, [
        0, 0, 4, 4,
        0, 0, 4, 4,
        8, 8, 12, 12,
        8, 8, 12, 12
    ]);

    public static void InitBlkStrideWithRef(int32_t* pBlkStride, int32_t kiStrideRef)
    {
        int32_t i;

        for (i = 0; i < 16; i += 4)
        {
            pBlkStride[i] = kuiStrideX[i] + kuiStrideY[i] * kiStrideRef;
            pBlkStride[i + 1] = kuiStrideX[i + 1] + kuiStrideY[i + 1] * kiStrideRef;
            pBlkStride[i + 2] = kuiStrideX[i + 2] + kuiStrideY[i + 2] * kiStrideRef;
            pBlkStride[i + 3] = kuiStrideX[i + 3] + kuiStrideY[i + 3] * kiStrideRef;
        }
    }

    /*
     * iMvdSz = (648*2+1) or (972*2+1);
     */
    public static void MvdCostInit(uint16_t* pMvdCostInter, int32_t kiMvdSz)
    {
        int32_t kiSz = kiMvdSz >> 1;
        uint16_t* pNegMvd = pMvdCostInter;
        uint16_t* pPosMvd = pMvdCostInter + kiSz + 1;
        int32_t* kpQpLambda = &g_kiQpCostTable[0];
        int32_t i, j;

        for (i = 0; i < 52; ++i)
        {
            uint16_t kiLambda = (uint16_t)kpQpLambda[i];
            int32_t iNegSe = -kiSz;
            int32_t iPosSe = 1;

            for (j = 0; j < kiSz; j += 4)
            {
                *pNegMvd++ = (uint16_t)(kiLambda * BsSizeSE(iNegSe++));
                *pNegMvd++ = (uint16_t)(kiLambda * BsSizeSE(iNegSe++));
                *pNegMvd++ = (uint16_t)(kiLambda * BsSizeSE(iNegSe++));
                *pNegMvd++ = (uint16_t)(kiLambda * BsSizeSE(iNegSe++));

                *pPosMvd++ = (uint16_t)(kiLambda * BsSizeSE(iPosSe++));
                *pPosMvd++ = (uint16_t)(kiLambda * BsSizeSE(iPosSe++));
                *pPosMvd++ = (uint16_t)(kiLambda * BsSizeSE(iPosSe++));
                *pPosMvd++ = (uint16_t)(kiLambda * BsSizeSE(iPosSe++));
            }
            *pNegMvd = kiLambda;
            pNegMvd += kiSz + 1;
            pPosMvd += kiSz + 1;
        }
    }

    public static void PredictSad(int8_t* pRefIndexCache, int32_t* pSadCostCache, int32_t uiRef, int32_t* pSadPred)
    {
        int32_t kiRefB = pRefIndexCache[1];//top g_uiCache12_8x8RefIdx[0] - 4
        int32_t iRefC = pRefIndexCache[5];//top-right g_uiCache12_8x8RefIdx[0] - 2
        int32_t kiRefA = pRefIndexCache[6];//left g_uiCache12_8x8RefIdx[0] - 1
        int32_t kiSadB = pSadCostCache[1];
        int32_t iSadC = pSadCostCache[2];
        int32_t kiSadA = pSadCostCache[3];

        int32_t iCount;

        if (iRefC == REF_NOT_AVAIL)
        {
            iRefC = pRefIndexCache[0];//top-left g_uiCache12_8x8RefIdx[0] - 4 - 1
            iSadC = pSadCostCache[0];
        }

        if (kiRefB == REF_NOT_AVAIL && iRefC == REF_NOT_AVAIL && kiRefA != REF_NOT_AVAIL)
        {
            *pSadPred = kiSadA;
        }
        else
        {
            iCount = (uiRef == kiRefA ? 1 : 0) << MB_LEFT_BIT;
            iCount |= (uiRef == kiRefB ? 1 : 0) << MB_TOP_BIT;
            iCount |= (uiRef == iRefC ? 1 : 0) << MB_TOPRIGHT_BIT;
            switch (iCount)
            {
                case LEFT_MB_POS:// A
                    *pSadPred = kiSadA;
                    break;
                case TOP_MB_POS:// B
                    *pSadPred = kiSadB;
                    break;
                case TOPRIGHT_MB_POS:// C or D
                    *pSadPred = iSadC;
                    break;
                default:
                    *pSadPred = WelsMedian(kiSadA, kiSadB, iSadC);
                    break;
            }
        }

        // #define REPLACE_SAD_MULTIPLY(x)   ((x) - (x>>3) + (x >>5))    // it's 0.90625, very close with 0.9
        iCount = (*pSadPred) << 6;  // here *64 will not overflow. SAD range 0~ 255*256(max 2^16), int32_t is enough
        *pSadPred = (((iCount) - (iCount >> 3) + (iCount >> 5)) + 32) >> 6;
    }


    public static void PredictSadSkip(int8_t* pRefIndexCache, bool* pMbSkipCache, int32_t* pSadCostCache, int32_t uiRef,
                                      int32_t* iSadPredSkip)
    {
        int32_t kiRefB = pRefIndexCache[1];//top g_uiCache12_8x8RefIdx[0] - 4
        int32_t iRefC = pRefIndexCache[5];//top-right g_uiCache12_8x8RefIdx[0] - 2
        int32_t kiRefA = pRefIndexCache[6];//left g_uiCache12_8x8RefIdx[0] - 1
        int32_t kiSadB = (pMbSkipCache[1] ? pSadCostCache[1] : 0);
        int32_t iSadC = (pMbSkipCache[2] ? pSadCostCache[2] : 0);
        int32_t kiSadA = (pMbSkipCache[3] ? pSadCostCache[3] : 0);
        int32_t iRefSkip = pMbSkipCache[2] ? 1 : 0;

        int32_t iCount = 0;

        if (iRefC == REF_NOT_AVAIL)
        {
            iRefC = pRefIndexCache[0];//top-left g_uiCache12_8x8RefIdx[0] - 4 - 1
            iSadC = (pMbSkipCache[0] ? pSadCostCache[0] : 0);
            iRefSkip = pMbSkipCache[0] ? 1 : 0;
        }

        if (kiRefB == REF_NOT_AVAIL && iRefC == REF_NOT_AVAIL && kiRefA != REF_NOT_AVAIL)
        {
            *iSadPredSkip = kiSadA;
        }
        else
        {
            iCount = ((uiRef == kiRefA) && pMbSkipCache[3] ? 1 : 0) << MB_LEFT_BIT;
            iCount |= ((uiRef == kiRefB) && pMbSkipCache[1] ? 1 : 0) << MB_TOP_BIT;
            iCount |= ((uiRef == iRefC) && (iRefSkip == 1) ? 1 : 0) << MB_TOPRIGHT_BIT;
            switch (iCount)
            {
                case LEFT_MB_POS:// A
                    *iSadPredSkip = kiSadA;
                    break;
                case TOP_MB_POS:// B
                    *iSadPredSkip = kiSadB;
                    break;
                case TOPRIGHT_MB_POS:// C or D
                    *iSadPredSkip = iSadC;
                    break;
                default:
                    *iSadPredSkip = WelsMedian(kiSadA, kiSadB, iSadC);
                    break;
            }
        }
    }
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

/// <summary>TagMeRefinePointer.</summary>
internal unsafe struct SMeRefinePointer
{
    public uint8_t* pHalfPixH;
    public uint8_t* pHalfPixV;
    public uint8_t* pHalfPixHV;

    public uint8_t* pQuarPixBest;
    public uint8_t* pQuarPixTmp;

    public PCopyFunc pfCopyBlockByMode;
}
