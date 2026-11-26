//-----------------------------------------------------------------------------
// Filename: svc_motion_estimate.cs
//
// Description: Port of codec/encoder/core/inc/svc_motion_estimate.h and
// codec/encoder/core/src/svc_motion_estimate.cpp, the camera half: the motion
// vector range constants, the ME method flags, SWelsME, the initial point
// test, the diamond search and the SATD costing. The cross and feature
// searches are screen content and follow in Phase 4.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 26 Nov 2026  Claude          Created (E4).
// 26 Nov 2026  Claude          FME_DEFAULT_FEATURE_INDEX (E4b).
// 26 Nov 2026  Claude          The camera motion search (E5).
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
using System.Runtime.InteropServices;

namespace SIPSorcery.H264.Encoder;

internal static partial class WelsEnc
{
    public const int CAMERA_STARTMV_RANGE = 64;
    public const int ITERATIVE_TIMES = 16;
    public const int CAMERA_MV_RANGE = CAMERA_STARTMV_RANGE + ITERATIVE_TIMES;
    public const int CAMERA_MVD_RANGE = (CAMERA_MV_RANGE + 1) << 1; //mvd=mv_range*2;
    public const int BASE_MV_MB_NMB = (2 * CAMERA_MV_RANGE / MB_WIDTH_LUMA) - 1;
    public const int CAMERA_HIGHLAYER_MVD_RANGE = 243;//mvd range;
    public const int EXPANDED_MV_RANGE = 504; //=512-8 rather than 511 to sacrifice same edge point but save complexity in assemblys
    public const int EXPANDED_MVD_RANGE = (504 + 1) << 1;

    public const int ME_DIA = 0x01;     // LITTLE DIAMOND= 0x01
    public const int ME_CROSS = 0x02;   // CROSS=  0x02
    public const int ME_FME = 0x04;     // FME = 0x04
    public const int ME_FULL = 0x10;    // FULL
    // derived ME methods combination
    public const int ME_DIA_CROSS = ME_DIA | ME_CROSS;              // DIA+CROSS
    public const int ME_DIA_CROSS_FME = ME_DIA_CROSS | ME_FME;      // DIA+CROSS+FME

    // #define FME_DEFAULT_FEATURE_INDEX (0), from the feature search basics (E4b, for InitDqLayers).
    public const int FME_DEFAULT_FEATURE_INDEX = 0;
}

/// <summary>union SadPredISatdUnit.</summary>
[StructLayout(LayoutKind.Explicit)]
internal struct SadPredISatdUnit
{
    [FieldOffset(0)] public uint32_t uiSadPred;
    [FieldOffset(0)] public uint32_t uiSatd;    //reuse the sad_pred as a temp satd pData
}

/// <summary>TagWelsME.</summary>
internal unsafe struct SWelsME
{
    /* input */
    public uint16_t* pMvdCost;
    public SadPredISatdUnit uSadPredISatd; //reuse the sad_pred as a temp pData
    public uint32_t uiSadCost;  //used by ME and RC //max SAD should be max_delta*size+lambda*mvdsize = 255*256+91*33*2 = 65280 + 6006 = 71286 > (2^16)-1 = 65535
    public uint32_t uiSatdCost; /* satd + lm * nbits */
    public uint32_t uiSadCostThreshold;
    public int32_t iCurMeBlockPixX;
    public int32_t iCurMeBlockPixY;
    public uint8_t uiBlockSize;   /* BLOCK_WxH */
    public uint8_t uiReserved;

    public uint8_t* pEncMb;
    public uint8_t* pRefMb;
    public uint8_t* pColoRefMb;

    public SMVUnitXY sMvp;
    public SMVUnitXY sMvBase;
    public SMVUnitXY sDirectionalMv;

    public SScreenBlockFeatureStorage* pRefFeatureStorage;

    /* output */
    public SMVUnitXY sMv;
}

// svc_motion_estimate.cpp, the camera half (E5).
internal static unsafe partial class WelsEnc
{
    // #define COST_MVD(table, mx, my) (table[mx] + table[my])
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int32_t COST_MVD(uint16_t* table, int32_t mx, int32_t my) => table[mx] + table[my];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void UpdateMeResults(SMVUnitXY ksBestMv, uint32_t kiBestSadCost, uint8_t* pRef, SWelsME* pMe)
    {
        pMe->sMv = ksBestMv;
        pMe->pRefMb = pRef;
        pMe->uiSadCost = kiBestSadCost;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void MeEndIntepelSearch(SWelsME* pMe)
    {
        /* -> qpel mv */
        pMe->sMv.iMvX *= (1 << 2);
        pMe->sMv.iMvY *= (1 << 2);
        pMe->uiSatdCost = pMe->uiSadCost;
    }

    public static void WelsInitMeFunc(SWelsFuncPtrList* pFuncList, uint32_t uiCpuFlag, bool bScreenContent)
    {
        pFuncList->pfUpdateFMESwitch = &UpdateFMESwitchNull;

        if (!bScreenContent)
        {
            pFuncList->pfCheckDirectionalMv = &CheckDirectionalMvFalse;
            // pfCalculateBlockFeatureOfFrame[0..1] and pfCalculateSingleBlockFeature[0..1] = NULL: they come with Phase 4.
        }
        else
        {
            // The cross and feature searches are screen content (Phase 4).
            throw new NotSupportedException("Screen content motion estimation is not ported yet (Phase 4).");
        }
    }

    /*!
     * \brief  BL mb motion estimate search
     *
     * \param  enc      Wels encoder context
     * \param  pMe          Wels me information
     *
     * \return  NONE
     */
    public static void WelsMotionEstimateSearch(SWelsFuncPtrList* pFuncList, SDqLayer* pCurDqLayer, SWelsME* pMe, SSlice* pSlice)
    {
        int32_t kiStrideEnc = pCurDqLayer->iEncStride[0];
        int32_t kiStrideRef = pCurDqLayer->pRefPic->iLineSize[0];

        //  Step 1: Initial point prediction
        if (!WelsMotionEstimateInitialPoint(pFuncList, pMe, pSlice, kiStrideEnc, kiStrideRef))
        {
            pFuncList->pfSearchMethod[pMe->uiBlockSize](pFuncList, pMe, pSlice, kiStrideEnc, kiStrideRef);
            MeEndIntepelSearch(pMe);
        }

        pFuncList->pfCalculateSatd(pFuncList->sSampleDealingFuncs.pfSampleSatd[pMe->uiBlockSize], pMe, kiStrideEnc,
                                   kiStrideRef);
    }

    public static void WelsMotionEstimateSearchStatic(SWelsFuncPtrList* pFuncList, SDqLayer* pCurDqLayer, SWelsME* pMe,
                                                      SSlice* pLpslice)
    {
        int32_t kiStrideEnc = pCurDqLayer->iEncStride[0];
        int32_t kiStrideRef = pCurDqLayer->pRefPic->iLineSize[0];

        pMe->sMv.iMvX = pMe->sMv.iMvY = 0;
        pMe->uiSadCost =
            (uint32_t)pFuncList->sSampleDealingFuncs.pfSampleSad[pMe->uiBlockSize](pMe->pEncMb, kiStrideEnc, pMe->pRefMb, kiStrideRef);
        pMe->uiSadCost += (uint32_t)COST_MVD(pMe->pMvdCost, -pMe->sMvp.iMvX, -pMe->sMvp.iMvY);
        MeEndIntepelSearch(pMe);
        pFuncList->pfCalculateSatd(pFuncList->sSampleDealingFuncs.pfSampleSatd[pMe->uiBlockSize], pMe, kiStrideEnc,
                                   kiStrideRef);
    }

    public static void WelsMotionEstimateSearchScrolled(SWelsFuncPtrList* pFuncList, SDqLayer* pCurDqLayer, SWelsME* pMe,
                                                        SSlice* pSlice)
    {
        int32_t kiStrideEnc = pCurDqLayer->iEncStride[0];
        int32_t kiStrideRef = pCurDqLayer->pRefPic->iLineSize[0];

        pMe->sMv = pMe->sDirectionalMv;
        pMe->pRefMb = pMe->pColoRefMb + pMe->sMv.iMvY * kiStrideRef + pMe->sMv.iMvX;
        pMe->uiSadCost =
            (uint32_t)(pFuncList->sSampleDealingFuncs.pfSampleSad[pMe->uiBlockSize](pMe->pEncMb, kiStrideEnc, pMe->pRefMb, kiStrideRef)
                       + COST_MVD(pMe->pMvdCost, (pMe->sMv.iMvX * (1 << 2)) - pMe->sMvp.iMvX, (pMe->sMv.iMvY * (1 << 2)) - pMe->sMvp.iMvY));
        MeEndIntepelSearch(pMe);
        pFuncList->pfCalculateSatd(pFuncList->sSampleDealingFuncs.pfSampleSatd[pMe->uiBlockSize], pMe, kiStrideEnc,
                                   kiStrideRef);
    }

    /*!
     * \brief  EL mb motion estimate initial point testing
     *
     * \param  pix_pFuncList  SSampleDealingFunc
     * \param  pMe          Wels me information
     * \param  mv_range  search range in motion estimate
     * \param  point      the best match point in motion estimation
     *
     * \return  NONE
     */
    public static bool WelsMotionEstimateInitialPoint(SWelsFuncPtrList* pFuncList, SWelsME* pMe, SSlice* pSlice, int32_t iStrideEnc,
                                                      int32_t iStrideRef)
    {
        PSampleSadSatdCostFunc pSad = pFuncList->sSampleDealingFuncs.pfSampleSad[pMe->uiBlockSize];
        uint16_t* kpMvdCost = pMe->pMvdCost;
        uint8_t* kpEncMb = pMe->pEncMb;
        int16_t iMvc0, iMvc1;
        int32_t iSadCost = 0; // uninitialised in C; only CheckDirectionalMv (screen content) writes or reads it
        int32_t iBestSadCost;
        uint8_t* pRefMb;
        uint8_t* pFref2;
        uint32_t i;
        uint32_t kuiMvcNum = pSlice->uiMvcNum;
        SMVUnitXY* kpMvcList = (SMVUnitXY*)&pSlice->sMvc;
        SMVUnitXY ksMvStartMin = pSlice->sMvStartMin;
        SMVUnitXY ksMvStartMax = pSlice->sMvStartMax;
        SMVUnitXY ksMvp = pMe->sMvp;
        SMVUnitXY sMv;

        //  Step 1: Initial point prediction
        // init with sMvp
        sMv.iMvX = (int16_t)WELS_CLIP3((2 + ksMvp.iMvX) >> 2, (int32_t)ksMvStartMin.iMvX, (int32_t)ksMvStartMax.iMvX);
        sMv.iMvY = (int16_t)WELS_CLIP3((2 + ksMvp.iMvY) >> 2, (int32_t)ksMvStartMin.iMvY, (int32_t)ksMvStartMax.iMvY);

        pRefMb = &pMe->pRefMb[sMv.iMvY * iStrideRef + sMv.iMvX];

        iBestSadCost = pSad(kpEncMb, iStrideEnc, pRefMb, iStrideRef);
        iBestSadCost += COST_MVD(kpMvdCost, ((sMv.iMvX) * (1 << 2)) - ksMvp.iMvX, ((sMv.iMvY) * (1 << 2)) - ksMvp.iMvY);

        for (i = 0; i < kuiMvcNum; i++)
        {
            //clipping here is essential since some pOut-of-range MVC may happen here (i.e., refer to baseMV)
            iMvc0 = (int16_t)WELS_CLIP3((2 + kpMvcList[i].iMvX) >> 2, (int32_t)ksMvStartMin.iMvX, (int32_t)ksMvStartMax.iMvX);
            iMvc1 = (int16_t)WELS_CLIP3((2 + kpMvcList[i].iMvY) >> 2, (int32_t)ksMvStartMin.iMvY, (int32_t)ksMvStartMax.iMvY);

            if (((iMvc0 - sMv.iMvX) != 0 || (iMvc1 - sMv.iMvY) != 0))
            {
                pFref2 = &pMe->pRefMb[iMvc1 * iStrideRef + iMvc0];

                iSadCost = pSad(kpEncMb, iStrideEnc, pFref2, iStrideRef) +
                           COST_MVD(kpMvdCost, (iMvc0 * (1 << 2)) - ksMvp.iMvX, (iMvc1 * (1 << 2)) - ksMvp.iMvY);

                if (iSadCost < iBestSadCost)
                {
                    sMv.iMvX = iMvc0;
                    sMv.iMvY = iMvc1;
                    pRefMb = pFref2;
                    iBestSadCost = iSadCost;
                }
            }
        }

        if (pFuncList->pfCheckDirectionalMv
                (pSad, pMe, ksMvStartMin, ksMvStartMax, iStrideEnc, iStrideRef, &iSadCost))
        {
            sMv = pMe->sDirectionalMv;
            pRefMb = &pMe->pColoRefMb[sMv.iMvY * iStrideRef + sMv.iMvX];
            iBestSadCost = iSadCost;
        }

        UpdateMeResults(sMv, (uint32_t)iBestSadCost, pRefMb, pMe);
        if (iBestSadCost < (int32_t)(pMe->uSadPredISatd.uiSadPred))
        {
            //Initial point early Stop
            MeEndIntepelSearch(pMe);
            return true;
        }
        return false;
    }

    public static void CalculateSatdCost(PSampleSadSatdCostFunc pSatd, SWelsME* pMe,
                                         int32_t kiEncStride, int32_t kiRefStride)
    {
        pMe->uSadPredISatd.uiSatd = (uint32_t)pSatd(pMe->pEncMb, kiEncStride, pMe->pRefMb, kiRefStride);
        pMe->uiSatdCost = pMe->uSadPredISatd.uiSatd + (uint32_t)COST_MVD(pMe->pMvdCost, pMe->sMv.iMvX - pMe->sMvp.iMvX,
                                                                        pMe->sMv.iMvY - pMe->sMvp.iMvY);
    }

    public static void NotCalculateSatdCost(PSampleSadSatdCostFunc pSatd, SWelsME* pMe,
                                            int32_t kiEncStride, int32_t kiRefStride)
    {
    }

    /////////////////////////
    // Diamond Search Basics
    /////////////////////////
    public static bool WelsMeSadCostSelect(int32_t* iSadCost, uint16_t* kpMvdCost, int32_t* pBestCost, int32_t kiDx,
                                           int32_t kiDy, int32_t* pIx, int32_t* pIy)
    {
        int32_t* iTempSadCost = stackalloc int32_t[4];
        int32_t iInputSadCost = *pBestCost;
        iTempSadCost[0] = iSadCost[0] + COST_MVD(kpMvdCost, kiDx, kiDy - 4);
        iTempSadCost[1] = iSadCost[1] + COST_MVD(kpMvdCost, kiDx, kiDy + 4);
        iTempSadCost[2] = iSadCost[2] + COST_MVD(kpMvdCost, kiDx - 4, kiDy);
        iTempSadCost[3] = iSadCost[3] + COST_MVD(kpMvdCost, kiDx + 4, kiDy);

        if (iTempSadCost[0] < *pBestCost)
        {
            *pBestCost = iTempSadCost[0];
            *pIx = 0;
            *pIy = 1;
        }

        if (iTempSadCost[1] < *pBestCost)
        {
            *pBestCost = iTempSadCost[1];
            *pIx = 0;
            *pIy = -1;
        }

        if (iTempSadCost[2] < *pBestCost)
        {
            *pBestCost = iTempSadCost[2];
            *pIx = 1;
            *pIy = 0;
        }

        if (iTempSadCost[3] < *pBestCost)
        {
            *pBestCost = iTempSadCost[3];
            *pIx = -1;
            *pIy = 0;
        }
        return (*pBestCost == iInputSadCost);
    }

    public static void WelsDiamondSearch(SWelsFuncPtrList* pFuncList, SWelsME* pMe, SSlice* pSlice,
                                         int32_t kiStrideEnc, int32_t kiStrideRef)
    {
        PSample4SadCostFunc pSad = pFuncList->sSampleDealingFuncs.pfSample4Sad[pMe->uiBlockSize];

        uint8_t* pFref = pMe->pRefMb;
        uint8_t* kpEncMb = pMe->pEncMb;
        uint16_t* kpMvdCost = pMe->pMvdCost;

        SMVUnitXY ksMvStartMin = pSlice->sMvStartMin;
        SMVUnitXY ksMvStartMax = pSlice->sMvStartMax;

        int32_t iMvDx = ((pMe->sMv.iMvX) * (1 << 2)) - pMe->sMvp.iMvX;
        int32_t iMvDy = ((pMe->sMv.iMvY) * (1 << 2)) - pMe->sMvp.iMvY;

        uint8_t* pRefMb = pFref;
        int32_t iBestCost = (int32_t)(pMe->uiSadCost);

        int32_t iTimeThreshold = ITERATIVE_TIMES;
        int32_t* iSadCosts = stackalloc int32_t[4]; // ENFORCE_STACK_ALIGN_1D (int32_t, iSadCosts, 4, 16)

        while (iTimeThreshold-- != 0)
        {
            pMe->sMv.iMvX = (int16_t)((iMvDx + pMe->sMvp.iMvX) >> 2);
            pMe->sMv.iMvY = (int16_t)((iMvDy + pMe->sMvp.iMvY) >> 2);
            if (!CheckMvInRange(pMe->sMv, ksMvStartMin, ksMvStartMax))
                continue;
            pSad(kpEncMb, kiStrideEnc, pRefMb, kiStrideRef, &iSadCosts[0]);

            int32_t iX, iY;

            bool kbIsBestCostWorse = WelsMeSadCostSelect(iSadCosts, kpMvdCost, &iBestCost, iMvDx, iMvDy, &iX, &iY);
            if (kbIsBestCostWorse)
                break;

            iMvDx -= (iX * (1 << 2));
            iMvDy -= (iY * (1 << 2));

            pRefMb -= (iX + iY * kiStrideRef);
        }

        /* integer-pel mv */
        pMe->sMv.iMvX = (int16_t)((iMvDx + pMe->sMvp.iMvX) >> 2);
        pMe->sMv.iMvY = (int16_t)((iMvDy + pMe->sMvp.iMvY) >> 2);
        pMe->uiSatdCost = pMe->uiSadCost = (uint32_t)(iBestCost);
        pMe->pRefMb = pRefMb;
    }

    /////////////////////////
    // DirectionalMv Basics
    /////////////////////////
    public static bool CheckDirectionalMv(PSampleSadSatdCostFunc pSad, SWelsME* pMe,
                                          SMVUnitXY ksMinMv, SMVUnitXY ksMaxMv, int32_t kiEncStride, int32_t kiRefStride,
                                          int32_t* iBestSadCost)
    {
        int16_t kiMvX = pMe->sDirectionalMv.iMvX;
        int16_t kiMvY = pMe->sDirectionalMv.iMvY;

        //Check MV from scrolling detection
        if ((BLOCK_16x16 != pMe->uiBlockSize) //scrolled_MV with P16x16 is checked SKIP checking function
            && (kiMvX | kiMvY) != 0   //(0,0) checked in ordinary initial point checking
            && CheckMvInRange(pMe->sDirectionalMv, ksMinMv, ksMaxMv))
        {
            uint8_t* pRef = &pMe->pColoRefMb[kiMvY * kiRefStride + kiMvX];
            uint32_t uiCurrentSadCost = (uint32_t)(pSad(pMe->pEncMb, kiEncStride, pRef, kiRefStride) +
                                                   COST_MVD(pMe->pMvdCost, (kiMvX * (1 << 2)) - pMe->sMvp.iMvX, (kiMvY * (1 << 2)) - pMe->sMvp.iMvY));
            if (uiCurrentSadCost < pMe->uiSadCost)
            {
                *iBestSadCost = (int32_t)uiCurrentSadCost;
                return true;
            }
        }
        return false;
    }

    public static bool CheckDirectionalMvFalse(PSampleSadSatdCostFunc pSad, SWelsME* vpMe,
                                               SMVUnitXY ksMinMv, SMVUnitXY ksMaxMv, int32_t kiEncStride, int32_t kiRefStride,
                                               int32_t* iBestSadCost)
    {
        return false;
    }

    public static void UpdateFMESwitchNull(SDqLayer* pCurLayer)
    {
    }

    //inline functions
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SetMvWithinIntegerMvRange(int32_t kiMbWidth, int32_t kiMbHeight, int32_t kiMbX,
                                                 int32_t kiMbY,
                                                 int32_t kiMaxMvRange,
                                                 SMVUnitXY* pMvMin, SMVUnitXY* pMvMax)
    {
        pMvMin->iMvX = (int16_t)WELS_MAX(-1 * ((kiMbX + 1) * (1 << 4)) + INTPEL_NEEDED_MARGIN, -1 * kiMaxMvRange);
        pMvMin->iMvY = (int16_t)WELS_MAX(-1 * ((kiMbY + 1) * (1 << 4)) + INTPEL_NEEDED_MARGIN, -1 * kiMaxMvRange);
        pMvMax->iMvX = (int16_t)WELS_MIN(((kiMbWidth - kiMbX) * (1 << 4)) - INTPEL_NEEDED_MARGIN, kiMaxMvRange);
        pMvMax->iMvY = (int16_t)WELS_MIN(((kiMbHeight - kiMbY) * (1 << 4)) - INTPEL_NEEDED_MARGIN, kiMaxMvRange);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool CheckMvInRange(SMVUnitXY ksCurrentMv, SMVUnitXY ksMinMv, SMVUnitXY ksMaxMv)
    {
        return (CheckInRangeCloseOpen(ksCurrentMv.iMvX, ksMinMv.iMvX, ksMaxMv.iMvX)
                && CheckInRangeCloseOpen(ksCurrentMv.iMvY, ksMinMv.iMvY, ksMaxMv.iMvY));
    }
}
