//-----------------------------------------------------------------------------
// Filename: mv_pred.cs
//
// Description: Port of codec/decoder/core/inc/mv_pred.h and src/mv_pred.cpp:
// motion vector prediction and the MB motion info updates.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// The P-slice parts: PredPSkipMvFromNeighbor, PredMv, PredInter8x16Mv,
// PredInter16x8Mv, the UpdateP* functions and GetMbType. The B-slice direct
// prediction (SetRectBlock, CopyRectBlock4Cols, GetColocatedMb,
// PredMvBDirectSpatial, PredBDirectTemporal, FillSpatialDirect8x8Mv,
// FillTemporalDirect8x8Mv, MapColToList0, Update8x8RefIdx) is not ported yet.
// The [LIST_A][30][MV_A] and [LIST_A][30] cache arrays are passed as
// InlineArray30 row pointers, the way the C arrays decay.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 25 Nov 2026  Claude          Created (D2).
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
 * \file    mv_pred.c
 *
 * \brief   Get MV predictor and update motion vector of mb cache
 *
 * \date    05/22/2009 Created
 *
 *************************************************************************************
 */

namespace SIPSorcery.H264.Decoder;

internal static unsafe partial class WelsDec
{
    public static void PredPSkipMvFromNeighbor(SDqLayer* pCurDqLayer, int16_t* iMvp)
    {
        bool bTopAvail, bLeftTopAvail, bRightTopAvail, bLeftAvail;

        int32_t iCurSliceIdc, iTopSliceIdc, iLeftTopSliceIdc, iRightTopSliceIdc, iLeftSliceIdc;
        int32_t iLeftTopType, iRightTopType, iTopType, iLeftType;
        int32_t iCurX, iCurY, iCurXy, iLeftXy = 0, iTopXy = 0, iLeftTopXy = 0, iRightTopXy = 0;

        int8_t iLeftRef;
        int8_t iTopRef;
        int8_t iRightTopRef;
        int8_t iLeftTopRef;
        int8_t iDiagonalRef;
        int8_t iMatchRef;
        int16_t* iMvA = stackalloc int16_t[2];
        int16_t* iMvB = stackalloc int16_t[2];
        int16_t* iMvC = stackalloc int16_t[2];
        int16_t* iMvD = stackalloc int16_t[2];

        iCurXy = pCurDqLayer->iMbXyIndex;
        iCurX = pCurDqLayer->iMbX;
        iCurY = pCurDqLayer->iMbY;
        iCurSliceIdc = pCurDqLayer->pSliceIdc[iCurXy];

        if (iCurX != 0)
        {
            iLeftXy = iCurXy - 1;
            iLeftSliceIdc = pCurDqLayer->pSliceIdc[iLeftXy];
            bLeftAvail = (iLeftSliceIdc == iCurSliceIdc);
        }
        else
        {
            bLeftAvail = false;
            bLeftTopAvail = false;
        }

        if (iCurY != 0)
        {
            iTopXy = iCurXy - pCurDqLayer->iMbWidth;
            iTopSliceIdc = pCurDqLayer->pSliceIdc[iTopXy];
            bTopAvail = (iTopSliceIdc == iCurSliceIdc);
            if (iCurX != 0)
            {
                iLeftTopXy = iTopXy - 1;
                iLeftTopSliceIdc = pCurDqLayer->pSliceIdc[iLeftTopXy];
                bLeftTopAvail = (iLeftTopSliceIdc == iCurSliceIdc);
            }
            else
            {
                bLeftTopAvail = false;
            }
            if (iCurX != (pCurDqLayer->iMbWidth - 1))
            {
                iRightTopXy = iTopXy + 1;
                iRightTopSliceIdc = pCurDqLayer->pSliceIdc[iRightTopXy];
                bRightTopAvail = (iRightTopSliceIdc == iCurSliceIdc);
            }
            else
            {
                bRightTopAvail = false;
            }
        }
        else
        {
            bTopAvail = false;
            bLeftTopAvail = false;
            bRightTopAvail = false;
        }

        iLeftType = ((iCurX != 0 && bLeftAvail) ? (int32_t)GetMbType(pCurDqLayer)[iLeftXy] : 0);
        iTopType = ((iCurY != 0 && bTopAvail) ? (int32_t)GetMbType(pCurDqLayer)[iTopXy] : 0);
        iLeftTopType = ((iCurX != 0 && iCurY != 0 && bLeftTopAvail)
                        ? (int32_t)GetMbType(pCurDqLayer)[iLeftTopXy] : 0);
        iRightTopType = ((iCurX != pCurDqLayer->iMbWidth - 1 && iCurY != 0 && bRightTopAvail)
                         ? (int32_t)GetMbType(pCurDqLayer)[iRightTopXy] : 0);

        /*get neb mv&iRefIdxArray*/
        /*left*/
        if (bLeftAvail && IS_INTER((uint32_t)iLeftType))
        {
            ST32(iMvA, LD32(pCurDqLayer->pDec != null ? &pCurDqLayer->pDec->pMv[0].p[iLeftXy][3] : &pCurDqLayer->pMv[0].p[iLeftXy][3]));
            iLeftRef = pCurDqLayer->pDec != null ? pCurDqLayer->pDec->pRefIndex[0].p[iLeftXy][3] : pCurDqLayer->pRefIndex[0].p[iLeftXy][3];
        }
        else
        {
            ST32(iMvA, 0);
            if (!bLeftAvail)
            { //not available
                iLeftRef = REF_NOT_AVAIL;
            }
            else
            { //available but is intra mb type
                iLeftRef = REF_NOT_IN_LIST;
            }
        }
        if (REF_NOT_AVAIL == iLeftRef ||
            (0 == iLeftRef && 0 == *(int32_t*)iMvA))
        {
            ST32(iMvp, 0);
            return;
        }

        /*top*/
        if (bTopAvail && IS_INTER((uint32_t)iTopType))
        {
            ST32(iMvB, LD32(pCurDqLayer->pDec != null ? &pCurDqLayer->pDec->pMv[0].p[iTopXy][12] : &pCurDqLayer->pMv[0].p[iTopXy][12]));
            iTopRef = pCurDqLayer->pDec != null ? pCurDqLayer->pDec->pRefIndex[0].p[iTopXy][12] : pCurDqLayer->pRefIndex[0].p[iTopXy][12];
        }
        else
        {
            ST32(iMvB, 0);
            if (!bTopAvail)
            { //not available
                iTopRef = REF_NOT_AVAIL;
            }
            else
            { //available but is intra mb type
                iTopRef = REF_NOT_IN_LIST;
            }
        }
        if (REF_NOT_AVAIL == iTopRef ||
            (0 == iTopRef && 0 == *(int32_t*)iMvB))
        {
            ST32(iMvp, 0);
            return;
        }

        /*right_top*/
        if (bRightTopAvail && IS_INTER((uint32_t)iRightTopType))
        {
            ST32(iMvC, LD32(pCurDqLayer->pDec != null ? &pCurDqLayer->pDec->pMv[0].p[iRightTopXy][12] :
                              &pCurDqLayer->pMv[0].p[iRightTopXy][12]));
            iRightTopRef = pCurDqLayer->pDec != null ? pCurDqLayer->pDec->pRefIndex[0].p[iRightTopXy][12] :
                           pCurDqLayer->pRefIndex[0].p[iRightTopXy][12];
        }
        else
        {
            ST32(iMvC, 0);
            if (!bRightTopAvail)
            { //not available
                iRightTopRef = REF_NOT_AVAIL;
            }
            else
            { //available but is intra mb type
                iRightTopRef = REF_NOT_IN_LIST;
            }
        }

        /*left_top*/
        if (bLeftTopAvail && IS_INTER((uint32_t)iLeftTopType))
        {
            ST32(iMvD, LD32(pCurDqLayer->pDec != null ? &pCurDqLayer->pDec->pMv[0].p[iLeftTopXy][15] : &pCurDqLayer->pMv[0].p[iLeftTopXy][15]));
            iLeftTopRef = pCurDqLayer->pDec != null ? pCurDqLayer->pDec->pRefIndex[0].p[iLeftTopXy][15] :
                          pCurDqLayer->pRefIndex[0].p[iLeftTopXy][15];
        }
        else
        {
            ST32(iMvD, 0);
            if (!bLeftTopAvail)
            { //not available
                iLeftTopRef = REF_NOT_AVAIL;
            }
            else
            { //available but is intra mb type
                iLeftTopRef = REF_NOT_IN_LIST;
            }
        }

        iDiagonalRef = iRightTopRef;
        if (REF_NOT_AVAIL == iDiagonalRef)
        {
            iDiagonalRef = iLeftTopRef;
            *(int32_t*)iMvC = *(int32_t*)iMvD;
        }

        if (REF_NOT_AVAIL == iTopRef && REF_NOT_AVAIL == iDiagonalRef && iLeftRef >= REF_NOT_IN_LIST)
        {
            ST32(iMvp, LD32(iMvA));
            return;
        }

        iMatchRef = (int8_t)((0 == iLeftRef ? 1 : 0) + (0 == iTopRef ? 1 : 0) + (0 == iDiagonalRef ? 1 : 0));
        if (1 == iMatchRef)
        {
            if (0 == iLeftRef)
            {
                ST32(iMvp, LD32(iMvA));
            }
            else if (0 == iTopRef)
            {
                ST32(iMvp, LD32(iMvB));
            }
            else
            {
                ST32(iMvp, LD32(iMvC));
            }
        }
        else
        {
            iMvp[0] = (int16_t)WelsMedian(iMvA[0], iMvB[0], iMvC[0]);
            iMvp[1] = (int16_t)WelsMedian(iMvA[1], iMvB[1], iMvC[1]);
        }
    }

    // GetColocatedMb, PredMvBDirectSpatial, PredBDirectTemporal: not ported yet (B slices).

    //basic iMVs prediction unit for iMVs partition width (4, 2, 1)
    public static void PredMv(InlineArray30<InlineArray2<int16_t>>* iMotionVector, InlineArray30<int8_t>* iRefIndex,
                              int32_t listIdx, int32_t iPartIdx, int32_t iPartWidth, int8_t iRef, int16_t* iMVP)
    {
        uint8_t kuiLeftIdx = (uint8_t)(g_kuiCache30ScanIdx[iPartIdx] - 1);
        uint8_t kuiTopIdx = (uint8_t)(g_kuiCache30ScanIdx[iPartIdx] - 6);
        uint8_t kuiRightTopIdx = (uint8_t)(kuiTopIdx + iPartWidth);
        uint8_t kuiLeftTopIdx = (uint8_t)(kuiTopIdx - 1);

        int8_t kiLeftRef = iRefIndex[listIdx][kuiLeftIdx];
        int8_t kiTopRef = iRefIndex[listIdx][kuiTopIdx];
        int8_t kiRightTopRef = iRefIndex[listIdx][kuiRightTopIdx];
        int8_t kiLeftTopRef = iRefIndex[listIdx][kuiLeftTopIdx];
        int8_t iDiagonalRef = kiRightTopRef;

        int8_t iMatchRef = 0;


        int16_t* iAMV = stackalloc int16_t[2];
        int16_t* iBMV = stackalloc int16_t[2];
        int16_t* iCMV = stackalloc int16_t[2];

        ST32(iAMV, LD32(&iMotionVector[listIdx][kuiLeftIdx]));
        ST32(iBMV, LD32(&iMotionVector[listIdx][kuiTopIdx]));
        ST32(iCMV, LD32(&iMotionVector[listIdx][kuiRightTopIdx]));

        if (REF_NOT_AVAIL == iDiagonalRef)
        {
            iDiagonalRef = kiLeftTopRef;
            ST32(iCMV, LD32(&iMotionVector[listIdx][kuiLeftTopIdx]));
        }

        iMatchRef = (int8_t)((iRef == kiLeftRef ? 1 : 0) + (iRef == kiTopRef ? 1 : 0) + (iRef == iDiagonalRef ? 1 : 0));

        if (REF_NOT_AVAIL == kiTopRef && REF_NOT_AVAIL == iDiagonalRef && kiLeftRef >= REF_NOT_IN_LIST)
        {
            ST32(iMVP, LD32(iAMV));
            return;
        }

        if (1 == iMatchRef)
        {
            if (iRef == kiLeftRef)
            {
                ST32(iMVP, LD32(iAMV));
            }
            else if (iRef == kiTopRef)
            {
                ST32(iMVP, LD32(iBMV));
            }
            else
            {
                ST32(iMVP, LD32(iCMV));
            }
        }
        else
        {
            iMVP[0] = (int16_t)WelsMedian(iAMV[0], iBMV[0], iCMV[0]);
            iMVP[1] = (int16_t)WelsMedian(iAMV[1], iBMV[1], iCMV[1]);
        }
    }
    public static void PredInter8x16Mv(InlineArray30<InlineArray2<int16_t>>* iMotionVector, InlineArray30<int8_t>* iRefIndex,
                                       int32_t listIdx, int32_t iPartIdx, int8_t iRef, int16_t* iMVP)
    {
        if (0 == iPartIdx)
        {
            int8_t kiLeftRef = iRefIndex[listIdx][6];
            if (iRef == kiLeftRef)
            {
                ST32(iMVP, LD32(&iMotionVector[listIdx][6][0]));
                return;
            }
        }
        else
        { // 1 == iPartIdx
            int8_t iDiagonalRef = iRefIndex[listIdx][5]; //top-right
            int8_t index = 5;
            if (REF_NOT_AVAIL == iDiagonalRef)
            {
                iDiagonalRef = iRefIndex[listIdx][2]; //top-left for 8*8 block(index 1)
                index = 2;
            }
            if (iRef == iDiagonalRef)
            {
                ST32(iMVP, LD32(&iMotionVector[listIdx][index][0]));
                return;
            }
        }

        PredMv(iMotionVector, iRefIndex, listIdx, iPartIdx, 2, iRef, iMVP);
    }
    public static void PredInter16x8Mv(InlineArray30<InlineArray2<int16_t>>* iMotionVector, InlineArray30<int8_t>* iRefIndex,
                                       int32_t listIdx, int32_t iPartIdx, int8_t iRef, int16_t* iMVP)
    {
        if (0 == iPartIdx)
        {
            int8_t kiTopRef = iRefIndex[listIdx][1];
            if (iRef == kiTopRef)
            {
                ST32(iMVP, LD32(&iMotionVector[listIdx][1][0]));
                return;
            }
        }
        else
        { // 8 == iPartIdx
            int8_t kiLeftRef = iRefIndex[listIdx][18];
            if (iRef == kiLeftRef)
            {
                ST32(iMVP, LD32(&iMotionVector[listIdx][18][0]));
                return;
            }
        }

        PredMv(iMotionVector, iRefIndex, listIdx, iPartIdx, 4, iRef, iMVP);
    }

    //update iMVs and iRefIndex cache for current MB, only for P_16*16 (SKIP inclusive)
    /* can be further optimized */
    public static void UpdateP16x16MotionInfo(SDqLayer* pCurDqLayer, int32_t listIdx, int8_t iRef, int16_t* iMVs)
    {
        int16_t kiRef2 = (int16_t)(((uint8_t)iRef << 8) | (uint8_t)iRef);
        int32_t kiMV32 = (int32_t)LD32(iMVs);
        int32_t i;
        int32_t iMbXy = pCurDqLayer->iMbXyIndex;

        for (i = 0; i < 16; i += 4)
        {
            //mb
            uint8_t kuiScan4Idx = g_kuiScan4[i];
            uint8_t kuiScan4IdxPlus4 = (uint8_t)(4 + kuiScan4Idx);
            if (pCurDqLayer->pDec != null)
            {
                ST16(&pCurDqLayer->pDec->pRefIndex[listIdx].p[iMbXy][kuiScan4Idx], (uint16_t)kiRef2);
                ST16(&pCurDqLayer->pDec->pRefIndex[listIdx].p[iMbXy][kuiScan4IdxPlus4], (uint16_t)kiRef2);

                ST32(&pCurDqLayer->pDec->pMv[listIdx].p[iMbXy][kuiScan4Idx], (uint32_t)kiMV32);
                ST32(&pCurDqLayer->pDec->pMv[listIdx].p[iMbXy][1 + kuiScan4Idx], (uint32_t)kiMV32);
                ST32(&pCurDqLayer->pDec->pMv[listIdx].p[iMbXy][kuiScan4IdxPlus4], (uint32_t)kiMV32);
                ST32(&pCurDqLayer->pDec->pMv[listIdx].p[iMbXy][1 + kuiScan4IdxPlus4], (uint32_t)kiMV32);
            }
            else
            {
                ST16(&pCurDqLayer->pRefIndex[listIdx].p[iMbXy][kuiScan4Idx], (uint16_t)kiRef2);
                ST16(&pCurDqLayer->pRefIndex[listIdx].p[iMbXy][kuiScan4IdxPlus4], (uint16_t)kiRef2);

                ST32(&pCurDqLayer->pMv[listIdx].p[iMbXy][kuiScan4Idx], (uint32_t)kiMV32);
                ST32(&pCurDqLayer->pMv[listIdx].p[iMbXy][1 + kuiScan4Idx], (uint32_t)kiMV32);
                ST32(&pCurDqLayer->pMv[listIdx].p[iMbXy][kuiScan4IdxPlus4], (uint32_t)kiMV32);
                ST32(&pCurDqLayer->pMv[listIdx].p[iMbXy][1 + kuiScan4IdxPlus4], (uint32_t)kiMV32);
            }
        }
    }

    //update iRefIndex cache for current MB, only for P_16*16 (SKIP inclusive)
    /* can be further optimized */
    public static void UpdateP16x16RefIdx(SDqLayer* pCurDqLayer, int32_t listIdx, int8_t iRef)
    {
        int16_t kiRef2 = (int16_t)(((uint8_t)iRef << 8) | (uint8_t)iRef);
        int32_t i;
        int32_t iMbXy = pCurDqLayer->iMbXyIndex;

        for (i = 0; i < 16; i += 4)
        {
            //mb
            uint8_t kuiScan4Idx = g_kuiScan4[i];
            uint8_t kuiScan4IdxPlus4 = (uint8_t)(4 + kuiScan4Idx);

            ST16(&pCurDqLayer->pDec->pRefIndex[listIdx].p[iMbXy][kuiScan4Idx], (uint16_t)kiRef2);
            ST16(&pCurDqLayer->pDec->pRefIndex[listIdx].p[iMbXy][kuiScan4IdxPlus4], (uint16_t)kiRef2);
        }
    }

    //update iMVs only cache for current MB, only for P_16*16 (SKIP inclusive)
    /* can be further optimized */
    public static void UpdateP16x16MotionOnly(SDqLayer* pCurDqLayer, int32_t listIdx, int16_t* iMVs)
    {
        int32_t kiMV32 = (int32_t)LD32(iMVs);
        int32_t i;
        int32_t iMbXy = pCurDqLayer->iMbXyIndex;

        for (i = 0; i < 16; i += 4)
        {
            //mb
            uint8_t kuiScan4Idx = g_kuiScan4[i];
            uint8_t kuiScan4IdxPlus4 = (uint8_t)(4 + kuiScan4Idx);
            if (pCurDqLayer->pDec != null)
            {
                ST32(&pCurDqLayer->pDec->pMv[listIdx].p[iMbXy][kuiScan4Idx], (uint32_t)kiMV32);
                ST32(&pCurDqLayer->pDec->pMv[listIdx].p[iMbXy][1 + kuiScan4Idx], (uint32_t)kiMV32);
                ST32(&pCurDqLayer->pDec->pMv[listIdx].p[iMbXy][kuiScan4IdxPlus4], (uint32_t)kiMV32);
                ST32(&pCurDqLayer->pDec->pMv[listIdx].p[iMbXy][1 + kuiScan4IdxPlus4], (uint32_t)kiMV32);
            }
            else
            {
                ST32(&pCurDqLayer->pMv[listIdx].p[iMbXy][kuiScan4Idx], (uint32_t)kiMV32);
                ST32(&pCurDqLayer->pMv[listIdx].p[iMbXy][1 + kuiScan4Idx], (uint32_t)kiMV32);
                ST32(&pCurDqLayer->pMv[listIdx].p[iMbXy][kuiScan4IdxPlus4], (uint32_t)kiMV32);
                ST32(&pCurDqLayer->pMv[listIdx].p[iMbXy][1 + kuiScan4IdxPlus4], (uint32_t)kiMV32);
            }
        }
    }

    //update iRefIndex and iMVs of Mb, only for P16x8
    /*need further optimization, mb_cache not work */
    public static void UpdateP16x8MotionInfo(SDqLayer* pCurDqLayer, InlineArray30<InlineArray2<int16_t>>* iMotionVector,
                                             InlineArray30<int8_t>* iRefIndex,
                                             int32_t listIdx, int32_t iPartIdx, int8_t iRef, int16_t* iMVs)
    {
        int16_t kiRef2 = (int16_t)(((uint8_t)iRef << 8) | (uint8_t)iRef);
        int32_t kiMV32 = (int32_t)LD32(iMVs);
        int32_t i;
        int32_t iMbXy = pCurDqLayer->iMbXyIndex;
        for (i = 0; i < 2; i++, iPartIdx += 4)
        {
            uint8_t kuiScan4Idx = g_kuiScan4[iPartIdx];
            uint8_t kuiScan4IdxPlus4 = (uint8_t)(4 + kuiScan4Idx);
            uint8_t kuiCacheIdx = g_kuiCache30ScanIdx[iPartIdx];
            uint8_t kuiCacheIdxPlus6 = (uint8_t)(6 + kuiCacheIdx);

            //mb
            if (pCurDqLayer->pDec != null)
            {
                ST16(&pCurDqLayer->pDec->pRefIndex[listIdx].p[iMbXy][kuiScan4Idx], (uint16_t)kiRef2);
                ST16(&pCurDqLayer->pDec->pRefIndex[listIdx].p[iMbXy][kuiScan4IdxPlus4], (uint16_t)kiRef2);
                ST32(&pCurDqLayer->pDec->pMv[listIdx].p[iMbXy][kuiScan4Idx], (uint32_t)kiMV32);
                ST32(&pCurDqLayer->pDec->pMv[listIdx].p[iMbXy][1 + kuiScan4Idx], (uint32_t)kiMV32);
                ST32(&pCurDqLayer->pDec->pMv[listIdx].p[iMbXy][kuiScan4IdxPlus4], (uint32_t)kiMV32);
                ST32(&pCurDqLayer->pDec->pMv[listIdx].p[iMbXy][1 + kuiScan4IdxPlus4], (uint32_t)kiMV32);
            }
            else
            {
                ST16(&pCurDqLayer->pRefIndex[listIdx].p[iMbXy][kuiScan4Idx], (uint16_t)kiRef2);
                ST16(&pCurDqLayer->pRefIndex[listIdx].p[iMbXy][kuiScan4IdxPlus4], (uint16_t)kiRef2);
                ST32(&pCurDqLayer->pMv[listIdx].p[iMbXy][kuiScan4Idx], (uint32_t)kiMV32);
                ST32(&pCurDqLayer->pMv[listIdx].p[iMbXy][1 + kuiScan4Idx], (uint32_t)kiMV32);
                ST32(&pCurDqLayer->pMv[listIdx].p[iMbXy][kuiScan4IdxPlus4], (uint32_t)kiMV32);
                ST32(&pCurDqLayer->pMv[listIdx].p[iMbXy][1 + kuiScan4IdxPlus4], (uint32_t)kiMV32);
            }
            //cache
            ST16(&iRefIndex[listIdx][kuiCacheIdx], (uint16_t)kiRef2);
            ST16(&iRefIndex[listIdx][kuiCacheIdxPlus6], (uint16_t)kiRef2);
            ST32(&iMotionVector[listIdx][kuiCacheIdx], (uint32_t)kiMV32);
            ST32(&iMotionVector[listIdx][1 + kuiCacheIdx], (uint32_t)kiMV32);
            ST32(&iMotionVector[listIdx][kuiCacheIdxPlus6], (uint32_t)kiMV32);
            ST32(&iMotionVector[listIdx][1 + kuiCacheIdxPlus6], (uint32_t)kiMV32);
        }
    }
    //update iRefIndex and iMVs of both Mb and Mb_cache, only for P8x16
    public static void UpdateP8x16MotionInfo(SDqLayer* pCurDqLayer, InlineArray30<InlineArray2<int16_t>>* iMotionVector,
                                             InlineArray30<int8_t>* iRefIndex,
                                             int32_t listIdx, int32_t iPartIdx, int8_t iRef, int16_t* iMVs)
    {
        int16_t kiRef2 = (int16_t)(((uint8_t)iRef << 8) | (uint8_t)iRef);
        int32_t kiMV32 = (int32_t)LD32(iMVs);
        int32_t i;
        int32_t iMbXy = pCurDqLayer->iMbXyIndex;

        for (i = 0; i < 2; i++, iPartIdx += 8)
        {
            uint8_t kuiScan4Idx = g_kuiScan4[iPartIdx];
            uint8_t kuiCacheIdx = g_kuiCache30ScanIdx[iPartIdx];
            uint8_t kuiScan4IdxPlus4 = (uint8_t)(4 + kuiScan4Idx);
            uint8_t kuiCacheIdxPlus6 = (uint8_t)(6 + kuiCacheIdx);

            //mb
            if (pCurDqLayer->pDec != null)
            {
                ST16(&pCurDqLayer->pDec->pRefIndex[listIdx].p[iMbXy][kuiScan4Idx], (uint16_t)kiRef2);
                ST16(&pCurDqLayer->pDec->pRefIndex[listIdx].p[iMbXy][kuiScan4IdxPlus4], (uint16_t)kiRef2);
                ST32(&pCurDqLayer->pDec->pMv[listIdx].p[iMbXy][kuiScan4Idx], (uint32_t)kiMV32);
                ST32(&pCurDqLayer->pDec->pMv[listIdx].p[iMbXy][1 + kuiScan4Idx], (uint32_t)kiMV32);
                ST32(&pCurDqLayer->pDec->pMv[listIdx].p[iMbXy][kuiScan4IdxPlus4], (uint32_t)kiMV32);
                ST32(&pCurDqLayer->pDec->pMv[listIdx].p[iMbXy][1 + kuiScan4IdxPlus4], (uint32_t)kiMV32);
            }
            else
            {
                ST16(&pCurDqLayer->pRefIndex[listIdx].p[iMbXy][kuiScan4Idx], (uint16_t)kiRef2);
                ST16(&pCurDqLayer->pRefIndex[listIdx].p[iMbXy][kuiScan4IdxPlus4], (uint16_t)kiRef2);
                ST32(&pCurDqLayer->pMv[listIdx].p[iMbXy][kuiScan4Idx], (uint32_t)kiMV32);
                ST32(&pCurDqLayer->pMv[listIdx].p[iMbXy][1 + kuiScan4Idx], (uint32_t)kiMV32);
                ST32(&pCurDqLayer->pMv[listIdx].p[iMbXy][kuiScan4IdxPlus4], (uint32_t)kiMV32);
                ST32(&pCurDqLayer->pMv[listIdx].p[iMbXy][1 + kuiScan4IdxPlus4], (uint32_t)kiMV32);
            }
            //cache
            ST16(&iRefIndex[listIdx][kuiCacheIdx], (uint16_t)kiRef2);
            ST16(&iRefIndex[listIdx][kuiCacheIdxPlus6], (uint16_t)kiRef2);
            ST32(&iMotionVector[listIdx][kuiCacheIdx], (uint32_t)kiMV32);
            ST32(&iMotionVector[listIdx][1 + kuiCacheIdx], (uint32_t)kiMV32);
            ST32(&iMotionVector[listIdx][kuiCacheIdxPlus6], (uint32_t)kiMV32);
            ST32(&iMotionVector[listIdx][1 + kuiCacheIdxPlus6], (uint32_t)kiMV32);
        }
    }

    // FillSpatialDirect8x8Mv, FillTemporalDirect8x8Mv, MapColToList0, Update8x8RefIdx:
    // not ported yet (B slices).

    // mv_pred.h (inline, PDqLayer& in C).
    public static uint32_t* GetMbType(SDqLayer* pCurDqLayer)
    {
        if (pCurDqLayer->pDec != null)
        {
            return pCurDqLayer->pDec->pMbType;
        }
        else
        {
            return pCurDqLayer->pMbType;
        }
    }
}
