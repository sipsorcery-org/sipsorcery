//-----------------------------------------------------------------------------
// Filename: mv_pred.cs
//
// Description: Port of codec/encoder/core/src/mv_pred.cpp (E5): the motion
// vector predictors (median, 16x8, 8x16 and skip) and the updates of the
// macroblock's and the cache's motion vectors and reference indices for each
// partition shape.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 26 Nov 2026  Claude          Created (E5).
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

namespace SIPSorcery.H264.Encoder;

internal static unsafe partial class WelsEnc
{
    //basic pMv prediction unit for pMv width (4, 2, 1)
    public static void PredMv(SMVComponentUnit* kpMvComp, int8_t iPartIdx, int8_t iPartW, int32_t iRef, SMVUnitXY* sMvp)
    {
        uint8_t kuiLeftIdx = (uint8_t)(g_kuiCache30ScanIdx[iPartIdx] - 1);
        uint8_t kuiTopIdx = (uint8_t)(g_kuiCache30ScanIdx[iPartIdx] - 6);

        int32_t iMatchRef;
        int32_t iLeftRef = kpMvComp->iRefIndexCache[kuiLeftIdx];
        int32_t iTopRef = kpMvComp->iRefIndexCache[kuiTopIdx];
        int32_t iRightTopRef = kpMvComp->iRefIndexCache[kuiTopIdx + iPartW];
        int32_t iDiagonalRef;
        SMVUnitXY sMvA = kpMvComp->sMotionVectorCache[kuiLeftIdx];
        SMVUnitXY sMvB = kpMvComp->sMotionVectorCache[kuiTopIdx];
        SMVUnitXY sMvC;

        if (REF_NOT_AVAIL == iRightTopRef)
        {
            iDiagonalRef = kpMvComp->iRefIndexCache[kuiTopIdx - 1];// left_top;
            sMvC = kpMvComp->sMotionVectorCache[kuiTopIdx - 1];
        }
        else
        {
            iDiagonalRef = iRightTopRef;// right_top;
            sMvC = kpMvComp->sMotionVectorCache[kuiTopIdx + iPartW];
        }

        if ((REF_NOT_AVAIL == iTopRef) && (REF_NOT_AVAIL == iDiagonalRef) && iLeftRef != REF_NOT_AVAIL)
        {
            *sMvp = sMvA;
            return;
        }

        // b2[diag] b1[top] b0[left] is available!
        iMatchRef = (iRef == iLeftRef ? 1 : 0) << MB_LEFT_BIT;
        iMatchRef |= (iRef == iTopRef ? 1 : 0) << MB_TOP_BIT;
        iMatchRef |= (iRef == iDiagonalRef ? 1 : 0) << MB_TOPRIGHT_BIT;
        switch (iMatchRef)
        {
            case LEFT_MB_POS:// A
                *sMvp = sMvA;
                break;
            case TOP_MB_POS:// B
                *sMvp = sMvB;
                break;
            case TOPRIGHT_MB_POS:// C or D
                *sMvp = sMvC;
                break;
            default:
                sMvp->iMvX = (int16_t)WelsMedian(sMvA.iMvX, sMvB.iMvX, sMvC.iMvX);
                sMvp->iMvY = (int16_t)WelsMedian(sMvA.iMvY, sMvB.iMvY, sMvC.iMvY);
                break;
        }
    }

    public static void PredInter8x16Mv(SMbCache* pMbCache, int32_t iPartIdx, int8_t iRef, SMVUnitXY* sMvp)
    {
        SMVComponentUnit* kpMvComp = &pMbCache->sMvComponents;
        if (0 == iPartIdx)
        {
            int8_t kiLeftRef = kpMvComp->iRefIndexCache[6];
            if (iRef == kiLeftRef)
            {
                *sMvp = kpMvComp->sMotionVectorCache[6];
                return;
            }
        }
        else
        { // 1 == iPartIdx
            int8_t iDiagonalRef = kpMvComp->iRefIndexCache[5]; //top-right
            int8_t iIndex = 5;
            if (REF_NOT_AVAIL == iDiagonalRef)
            {
                iDiagonalRef = kpMvComp->iRefIndexCache[2]; //top-left for 8*8 block(iIndex 1)
                iIndex = 2;
            }
            if (iRef == iDiagonalRef)
            {
                *sMvp = kpMvComp->sMotionVectorCache[iIndex];
                return;
            }
        }

        PredMv(kpMvComp, (int8_t)iPartIdx, 2, iRef, sMvp);
    }

    public static void PredInter16x8Mv(SMbCache* pMbCache, int32_t iPartIdx, int8_t iRef, SMVUnitXY* sMvp)
    {
        SMVComponentUnit* kpMvComp = &pMbCache->sMvComponents;
        if (0 == iPartIdx)
        {
            int8_t kiTopRef = kpMvComp->iRefIndexCache[1];
            if (iRef == kiTopRef)
            {
                *sMvp = kpMvComp->sMotionVectorCache[1];
                return;
            }
        }
        else
        { // 8 == iPartIdx
            int8_t kiLeftRef = kpMvComp->iRefIndexCache[18];
            if (iRef == kiLeftRef)
            {
                *sMvp = kpMvComp->sMotionVectorCache[18];
                return;
            }
        }

        PredMv(kpMvComp, (int8_t)iPartIdx, 4, iRef, sMvp);
    }

    public static void PredSkipMv(SMbCache* pMbCache, SMVUnitXY* sMvp)
    {
        SMVComponentUnit* kpMvComp = &pMbCache->sMvComponents;
        int8_t kiLeftRef = kpMvComp->iRefIndexCache[6]; //A
        int8_t kiTopRef = kpMvComp->iRefIndexCache[1]; //B

        if (REF_NOT_AVAIL == kiLeftRef || REF_NOT_AVAIL == kiTopRef ||
            (0 == kiLeftRef && 0 == *(int32_t*)(&((SMVUnitXY*)&kpMvComp->sMotionVectorCache)[6])) ||
            (0 == kiTopRef && 0 == *(int32_t*)(&((SMVUnitXY*)&kpMvComp->sMotionVectorCache)[1])))
        {
            ST32(sMvp, 0);
            return;
        }

        PredMv(kpMvComp, 0, 4, 0, sMvp);
    }

    //update pMv and uiRefIndex cache for current MB, only for P_16*16 (SKIP inclusive)
    public static void UpdateP16x16MotionInfo(SMbCache* pMbCache, SMB* pCurMb, int8_t kiRef, SMVUnitXY* pMv)
    {
        // optimized 11/25/2011
        SMVComponentUnit* pMvComp = &pMbCache->sMvComponents;
        SMVUnitXY* pMvCache = (SMVUnitXY*)&pMvComp->sMotionVectorCache;
        uint32_t kuiMv32 = LD32(pMv);
        uint64_t kuiMv64 = BUTTERFLY4x8(kuiMv32);
        uint16_t kuiRef16 = (uint16_t)BUTTERFLY1x2(kiRef);
        uint32_t kuiRef32 = BUTTERFLY2x4(kuiRef16);

        ST32(pCurMb->pRefIndex, kuiRef32);
        // update pMv range from 0~15
        // memcpy (pCurMb->sMv, uiMvBuf, sizeof (uiMvBuf)): uint64_t uiMvBuf[8], all kuiMv64.
        for (int32_t i = 0; i < 8; i++)
        {
            ST64(pCurMb->sMv + (i << 1), kuiMv64);
        }

        /*
         * blocks 0: 7~10, 1: 13~16, 2: 19~22, 3: 25~28
         */
        pMvComp->iRefIndexCache[7] = kiRef;
        ST16(&pMvComp->iRefIndexCache[8], kuiRef16);
        pMvComp->iRefIndexCache[10] = kiRef;
        pMvComp->iRefIndexCache[13] = kiRef;
        ST16(&pMvComp->iRefIndexCache[14], kuiRef16);
        pMvComp->iRefIndexCache[16] = kiRef;
        pMvComp->iRefIndexCache[19] = kiRef;
        ST16(&pMvComp->iRefIndexCache[20], kuiRef16);
        pMvComp->iRefIndexCache[22] = kiRef;
        pMvComp->iRefIndexCache[25] = kiRef;
        ST16(&pMvComp->iRefIndexCache[26], kuiRef16);
        pMvComp->iRefIndexCache[28] = kiRef;

        /*
        * blocks 0: 7~10, 1: 13~16, 2: 19~22, 3: 25~28
        */
        pMvCache[7] = *pMv;
        ST64(&pMvCache[8], kuiMv64);
        pMvCache[10] = *pMv;
        pMvCache[13] = *pMv;
        ST64(&pMvCache[14], kuiMv64);
        pMvCache[16] = *pMv;
        pMvCache[19] = *pMv;
        ST64(&pMvCache[20], kuiMv64);
        pMvCache[22] = *pMv;
        pMvCache[25] = *pMv;
        ST64(&pMvCache[26], kuiMv64);
        pMvCache[28] = *pMv;
    }

    //update uiRefIndex and pMv of both SMB and Mb_cache, only for P16x8
    public static void UpdateP16x8MotionInfo(SMbCache* pMbCache, SMB* pCurMb, int32_t kiPartIdx, int8_t kiRef,
                                             SMVUnitXY* pMv)
    {
        // optimized 11/25/2011
        SMVComponentUnit* pMvComp = &pMbCache->sMvComponents;
        SMVUnitXY* pMvCache = (SMVUnitXY*)&pMvComp->sMotionVectorCache;
        uint32_t kuiMv32 = LD32(pMv);
        uint64_t kuiMv64 = BUTTERFLY4x8(kuiMv32);
        int16_t kiScan4Idx = g_kuiMbCountScan4Idx[kiPartIdx];
        int16_t kiCacheIdx = g_kuiCache30ScanIdx[kiPartIdx];
        int16_t kiCacheIdx1 = (int16_t)(1 + kiCacheIdx);
        int16_t kiCacheIdx3 = (int16_t)(3 + kiCacheIdx);
        int16_t kiCacheIdx6 = (int16_t)(6 + kiCacheIdx);
        int16_t kiCacheIdx7 = (int16_t)(7 + kiCacheIdx);
        int16_t kiCacheIdx9 = (int16_t)(9 + kiCacheIdx);
        uint16_t kuiRef16 = (uint16_t)BUTTERFLY1x2(kiRef);

        ST16(&pCurMb->pRefIndex[(kiPartIdx >> 2)], kuiRef16);
        // memcpy (&pCurMb->sMv[kiScan4Idx], uiMvBuf, sizeof (uiMvBuf)): uint64_t uiMvBuf[4], all kuiMv64.
        for (int32_t i = 0; i < 4; i++)
        {
            ST64(&pCurMb->sMv[kiScan4Idx + (i << 1)], kuiMv64);
        }

        /*
        * blocks 0: g_kuiCache30ScanIdx[iPartIdx]~g_kuiCache30ScanIdx[iPartIdx]+3, 1: g_kuiCache30ScanIdx[iPartIdx]+6~g_kuiCache30ScanIdx[iPartIdx]+9
        */
        pMvComp->iRefIndexCache[kiCacheIdx] = kiRef;
        ST16(&pMvComp->iRefIndexCache[kiCacheIdx1], kuiRef16);
        pMvComp->iRefIndexCache[kiCacheIdx3] = kiRef;
        pMvComp->iRefIndexCache[kiCacheIdx6] = kiRef;
        ST16(&pMvComp->iRefIndexCache[kiCacheIdx7], kuiRef16);
        pMvComp->iRefIndexCache[kiCacheIdx9] = kiRef;

        /*
        * blocks 0: g_kuiCache30ScanIdx[iPartIdx]~g_kuiCache30ScanIdx[iPartIdx]+3, 1: g_kuiCache30ScanIdx[iPartIdx]+6~g_kuiCache30ScanIdx[iPartIdx]+9
        */
        pMvCache[kiCacheIdx] = *pMv;
        ST64(&pMvCache[kiCacheIdx1], kuiMv64);
        pMvCache[kiCacheIdx3] = *pMv;
        pMvCache[kiCacheIdx6] = *pMv;
        ST64(&pMvCache[kiCacheIdx7], kuiMv64);
        pMvCache[kiCacheIdx9] = *pMv;
    }

    //update uiRefIndex and pMv of both SMB and Mb_cache, only for P8x16
    public static void update_P8x16_motion_info(SMbCache* pMbCache, SMB* pCurMb, int32_t kiPartIdx, int8_t kiRef,
                                                SMVUnitXY* pMv)
    {
        // optimized 11/25/2011
        SMVComponentUnit* pMvComp = &pMbCache->sMvComponents;
        SMVUnitXY* pMvCache = (SMVUnitXY*)&pMvComp->sMotionVectorCache;
        uint32_t kuiMv32 = LD32(pMv);
        uint64_t kuiMv64 = BUTTERFLY4x8(kuiMv32);
        int16_t kiScan4Idx = g_kuiMbCountScan4Idx[kiPartIdx];
        int16_t kiCacheIdx = g_kuiCache30ScanIdx[kiPartIdx];
        int16_t kiCacheIdx1 = (int16_t)(1 + kiCacheIdx);
        int16_t kiCacheIdx3 = (int16_t)(3 + kiCacheIdx);
        int16_t kiCacheIdx12 = (int16_t)(12 + kiCacheIdx);
        int16_t kiCacheIdx13 = (int16_t)(13 + kiCacheIdx);
        int16_t kiCacheIdx15 = (int16_t)(15 + kiCacheIdx);
        int16_t kiBlkIdx = (int16_t)(kiPartIdx >> 2);
        uint16_t kuiRef16 = (uint16_t)BUTTERFLY1x2(kiRef);

        pCurMb->pRefIndex[kiBlkIdx] = kiRef;
        pCurMb->pRefIndex[2 + kiBlkIdx] = kiRef;
        ST64(&pCurMb->sMv[kiScan4Idx], kuiMv64);
        ST64(&pCurMb->sMv[4 + kiScan4Idx], kuiMv64);
        ST64(&pCurMb->sMv[8 + kiScan4Idx], kuiMv64);
        ST64(&pCurMb->sMv[12 + kiScan4Idx], kuiMv64);

        /*
        * blocks 0: g_kuiCache30ScanIdx[iPartIdx]~g_kuiCache30ScanIdx[iPartIdx]+3, 1: g_kuiCache30ScanIdx[iPartIdx]+6~g_kuiCache30ScanIdx[iPartIdx]+9
        */
        pMvComp->iRefIndexCache[kiCacheIdx] = kiRef;
        ST16(&pMvComp->iRefIndexCache[kiCacheIdx1], kuiRef16);
        pMvComp->iRefIndexCache[kiCacheIdx3] = kiRef;
        pMvComp->iRefIndexCache[kiCacheIdx12] = kiRef;
        ST16(&pMvComp->iRefIndexCache[kiCacheIdx13], kuiRef16);
        pMvComp->iRefIndexCache[kiCacheIdx15] = kiRef;

        /*
        * blocks 0: g_kuiCache30ScanIdx[iPartIdx]~g_kuiCache30ScanIdx[iPartIdx]+3, 1: g_kuiCache30ScanIdx[iPartIdx]+6~g_kuiCache30ScanIdx[iPartIdx]+9
        */
        pMvCache[kiCacheIdx] = *pMv;
        ST64(&pMvCache[kiCacheIdx1], kuiMv64);
        pMvCache[kiCacheIdx3] = *pMv;
        pMvCache[kiCacheIdx12] = *pMv;
        ST64(&pMvCache[kiCacheIdx13], kuiMv64);
        pMvCache[kiCacheIdx15] = *pMv;
    }

    //update uiRefIndex and pMv of both SMB and Mb_cache, only for P8x8
    public static void UpdateP8x8MotionInfo(SMbCache* pMbCache, SMB* pCurMb, int32_t kiPartIdx, int8_t kiRef,
                                            SMVUnitXY* pMv)
    {
        SMVComponentUnit* pMvComp = &pMbCache->sMvComponents;
        SMVUnitXY* pMvCache = (SMVUnitXY*)&pMvComp->sMotionVectorCache;
        uint32_t kuiMv32 = LD32(pMv);
        uint64_t kuiMv64 = BUTTERFLY4x8(kuiMv32);
        int16_t kiScan4Idx = g_kuiMbCountScan4Idx[kiPartIdx];
        int16_t kiCacheIdx = g_kuiCache30ScanIdx[kiPartIdx];
        int16_t kiCacheIdx1 = (int16_t)(1 + kiCacheIdx);
        int16_t kiCacheIdx6 = (int16_t)(6 + kiCacheIdx);
        int16_t kiCacheIdx7 = (int16_t)(7 + kiCacheIdx);

        //mb
        ST64(&pCurMb->sMv[kiScan4Idx], kuiMv64);
        ST64(&pCurMb->sMv[4 + kiScan4Idx], kuiMv64);

        //cache
        pMvComp->iRefIndexCache[kiCacheIdx] =
            pMvComp->iRefIndexCache[kiCacheIdx1] =
                pMvComp->iRefIndexCache[kiCacheIdx6] =
                    pMvComp->iRefIndexCache[kiCacheIdx7] = kiRef;
        pMvCache[kiCacheIdx] =
            pMvCache[kiCacheIdx1] =
                pMvCache[kiCacheIdx6] =
                    pMvCache[kiCacheIdx7] = *pMv;
    }

    //update uiRefIndex and pMv of both SMB and Mb_cache, only for P4x4
    public static void UpdateP4x4MotionInfo(SMbCache* pMbCache, SMB* pCurMb, int32_t kiPartIdx, int8_t kiRef,
                                            SMVUnitXY* pMv)
    {
        SMVComponentUnit* pMvComp = &pMbCache->sMvComponents;
        SMVUnitXY* pMvCache = (SMVUnitXY*)&pMvComp->sMotionVectorCache;
        int16_t kiScan4Idx = g_kuiMbCountScan4Idx[kiPartIdx];
        int16_t kiCacheIdx = g_kuiCache30ScanIdx[kiPartIdx];

        //mb
        pCurMb->sMv[kiScan4Idx] = *pMv;
        //cache
        pMvComp->iRefIndexCache[kiCacheIdx] = kiRef;
        pMvCache[kiCacheIdx] = *pMv;
    }

    //update uiRefIndex and pMv of both SMB and Mb_cache, only for P8x4
    public static void UpdateP8x4MotionInfo(SMbCache* pMbCache, SMB* pCurMb, int32_t kiPartIdx, int8_t kiRef,
                                            SMVUnitXY* pMv)
    {
        SMVComponentUnit* pMvComp = &pMbCache->sMvComponents;
        SMVUnitXY* pMvCache = (SMVUnitXY*)&pMvComp->sMotionVectorCache;
        int16_t kiScan4Idx = g_kuiMbCountScan4Idx[kiPartIdx];
        int16_t kiCacheIdx = g_kuiCache30ScanIdx[kiPartIdx];

        //mb
        pCurMb->sMv[kiScan4Idx] = *pMv;
        pCurMb->sMv[1 + kiScan4Idx] = *pMv;
        //cache
        pMvComp->iRefIndexCache[kiCacheIdx] = kiRef;
        pMvComp->iRefIndexCache[1 + kiCacheIdx] = kiRef;
        pMvCache[kiCacheIdx] = *pMv;
        pMvCache[1 + kiCacheIdx] = *pMv;
    }

    //update uiRefIndex and pMv of both SMB and Mb_cache, only for P4x8
    public static void UpdateP4x8MotionInfo(SMbCache* pMbCache, SMB* pCurMb, int32_t kiPartIdx, int8_t kiRef,
                                            SMVUnitXY* pMv)
    {
        SMVComponentUnit* pMvComp = &pMbCache->sMvComponents;
        SMVUnitXY* pMvCache = (SMVUnitXY*)&pMvComp->sMotionVectorCache;
        int16_t kiScan4Idx = g_kuiMbCountScan4Idx[kiPartIdx];
        int16_t kiCacheIdx = g_kuiCache30ScanIdx[kiPartIdx];

        //mb
        pCurMb->sMv[kiScan4Idx] = *pMv;
        pCurMb->sMv[4 + kiScan4Idx] = *pMv;
        //cache
        pMvComp->iRefIndexCache[kiCacheIdx] = kiRef;
        pMvComp->iRefIndexCache[6 + kiCacheIdx] = kiRef;
        pMvCache[kiCacheIdx] = *pMv;
        pMvCache[6 + kiCacheIdx] = *pMv;
    }

    //=========================update motion info(MV and ref_idx) into Mb_cache==========================
    //update pMv and uiRefIndex cache only for Mb_cache, only for P_16*16 (SKIP inclusive)

    //update uiRefIndex and pMv of only Mb_cache, only for P16x8
    public static void UpdateP16x8Motion2Cache(SMbCache* pMbCache, int32_t iPartIdx, int8_t iRef, SMVUnitXY* pMv)
    {
        SMVComponentUnit* pMvComp = &pMbCache->sMvComponents;
        SMVUnitXY* pMvCache = (SMVUnitXY*)&pMvComp->sMotionVectorCache;
        int32_t i;

        for (i = 0; i < 2; i++, iPartIdx += 4)
        {
            //cache
            uint8_t kuiCacheIdx = g_kuiCache30ScanIdx[iPartIdx];

            pMvComp->iRefIndexCache[kuiCacheIdx] =
                pMvComp->iRefIndexCache[1 + kuiCacheIdx] =
                    pMvComp->iRefIndexCache[6 + kuiCacheIdx] =
                        pMvComp->iRefIndexCache[7 + kuiCacheIdx] = iRef;
            pMvCache[kuiCacheIdx] =
                pMvCache[1 + kuiCacheIdx] =
                    pMvCache[6 + kuiCacheIdx] =
                        pMvCache[7 + kuiCacheIdx] = *pMv;
        }
    }

    //update uiRefIndex and pMv of only Mb_cache, only for P8x16
    public static void UpdateP8x16Motion2Cache(SMbCache* pMbCache, int32_t iPartIdx, int8_t iRef, SMVUnitXY* pMv)
    {
        SMVComponentUnit* pMvComp = &pMbCache->sMvComponents;
        SMVUnitXY* pMvCache = (SMVUnitXY*)&pMvComp->sMotionVectorCache;
        int32_t i;

        for (i = 0; i < 2; i++, iPartIdx += 8)
        {
            //cache
            uint8_t kuiCacheIdx = g_kuiCache30ScanIdx[iPartIdx];

            pMvComp->iRefIndexCache[kuiCacheIdx] =
                pMvComp->iRefIndexCache[1 + kuiCacheIdx] =
                    pMvComp->iRefIndexCache[6 + kuiCacheIdx] =
                        pMvComp->iRefIndexCache[7 + kuiCacheIdx] = iRef;
            pMvCache[kuiCacheIdx] =
                pMvCache[1 + kuiCacheIdx] =
                    pMvCache[6 + kuiCacheIdx] =
                        pMvCache[7 + kuiCacheIdx] = *pMv;
        }
    }

    //update uiRefIndex and pMv of only Mb_cache, only for P8x8
    public static void UpdateP8x8Motion2Cache(SMbCache* pMbCache, int32_t iPartIdx, int8_t pRef, SMVUnitXY* pMv)
    {
        SMVComponentUnit* pMvComp = &pMbCache->sMvComponents;
        SMVUnitXY* pMvCache = (SMVUnitXY*)&pMvComp->sMotionVectorCache;
        uint8_t kuiCacheIdx = g_kuiCache30ScanIdx[iPartIdx];

        pMvComp->iRefIndexCache[kuiCacheIdx] =
            pMvComp->iRefIndexCache[1 + kuiCacheIdx] =
                pMvComp->iRefIndexCache[6 + kuiCacheIdx] =
                    pMvComp->iRefIndexCache[7 + kuiCacheIdx] = pRef;
        pMvCache[kuiCacheIdx] =
            pMvCache[1 + kuiCacheIdx] =
                pMvCache[6 + kuiCacheIdx] =
                    pMvCache[7 + kuiCacheIdx] = *pMv;
    }

    //update uiRefIndex and pMv of only Mb_cache, for P4x4
    public static void UpdateP4x4Motion2Cache(SMbCache* pMbCache, int32_t iPartIdx, int8_t pRef, SMVUnitXY* pMv)
    {
        SMVComponentUnit* pMvComp = &pMbCache->sMvComponents;
        SMVUnitXY* pMvCache = (SMVUnitXY*)&pMvComp->sMotionVectorCache;
        uint8_t kuiCacheIdx = g_kuiCache30ScanIdx[iPartIdx];

        pMvComp->iRefIndexCache[kuiCacheIdx] = pRef;
        pMvCache[kuiCacheIdx] = *pMv;
    }

    //update uiRefIndex and pMv of only Mb_cache, for P8x4
    public static void UpdateP8x4Motion2Cache(SMbCache* pMbCache, int32_t iPartIdx, int8_t pRef, SMVUnitXY* pMv)
    {
        SMVComponentUnit* pMvComp = &pMbCache->sMvComponents;
        SMVUnitXY* pMvCache = (SMVUnitXY*)&pMvComp->sMotionVectorCache;
        uint8_t kuiCacheIdx = g_kuiCache30ScanIdx[iPartIdx];

        pMvComp->iRefIndexCache[kuiCacheIdx] =
            pMvComp->iRefIndexCache[1 + kuiCacheIdx] = pRef;
        pMvCache[kuiCacheIdx] =
            pMvCache[1 + kuiCacheIdx] = *pMv;
    }

    //update uiRefIndex and pMv of only Mb_cache, for P4x8
    public static void UpdateP4x8Motion2Cache(SMbCache* pMbCache, int32_t iPartIdx, int8_t pRef, SMVUnitXY* pMv)
    {
        SMVComponentUnit* pMvComp = &pMbCache->sMvComponents;
        SMVUnitXY* pMvCache = (SMVUnitXY*)&pMvComp->sMotionVectorCache;
        uint8_t kuiCacheIdx = g_kuiCache30ScanIdx[iPartIdx];

        pMvComp->iRefIndexCache[kuiCacheIdx] =
            pMvComp->iRefIndexCache[6 + kuiCacheIdx] = pRef;
        pMvCache[kuiCacheIdx] =
            pMvCache[6 + kuiCacheIdx] = *pMv;
    }
}
