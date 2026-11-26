//-----------------------------------------------------------------------------
// Filename: svc_encode_slice.cs
//
// Description: Port of codec/encoder/core/src/svc_encode_slice.cpp, part 1
// (E4a): the I-slice macroblock loop WelsISliceMdEnc and what it calls
// (UpdateNonZeroCountCache, WelsIMbChromaEncode, UpdateQpForOverflow), and
// the macroblock neighbour and cache set up (UpdateMbNeighbor,
// AllocMbCacheAligned, FreeMbCache, InitSliceBoundaryInfo,
// SetSliceBoundaryInfo), then (E4b) the slice header writers,
// WelsCodeOneSlice and the slice lists (InitSliceInLayer and what it calls).
// The P-slice loops follow in E5, and the SM_SIZELIMITED_SLICE coders and
// buffer growth in Phase 4.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// CABAC is not ported: WelsISliceMdEnc throws NotSupportedException when
// iEntropyCodingModeFlag is set.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 26 Nov 2026  Claude          Created (E4).
// 26 Nov 2026  Claude          Slice headers, WelsCodeOneSlice and the slice lists (E4b).
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

using System.Diagnostics;

namespace SIPSorcery.H264.Encoder;

internal static unsafe partial class WelsEnc
{
    public static void UpdateNonZeroCountCache(SMB* pMb, SMbCache* pMbCache)
    {
        ST32(&pMbCache->iNonZeroCoeffCount[9], LD32(&pMb->pNonZeroCount[0]));
        ST32(&pMbCache->iNonZeroCoeffCount[17], LD32(&pMb->pNonZeroCount[4]));
        ST32(&pMbCache->iNonZeroCoeffCount[25], LD32(&pMb->pNonZeroCount[8]));
        ST32(&pMbCache->iNonZeroCoeffCount[33], LD32(&pMb->pNonZeroCount[12]));

        ST16(&pMbCache->iNonZeroCoeffCount[14], LD16(&pMb->pNonZeroCount[16]));
        ST16(&pMbCache->iNonZeroCoeffCount[38], LD16(&pMb->pNonZeroCount[18]));
        ST16(&pMbCache->iNonZeroCoeffCount[22], LD16(&pMb->pNonZeroCount[20]));
        ST16(&pMbCache->iNonZeroCoeffCount[46], LD16(&pMb->pNonZeroCount[22]));
    }

    public static void WelsSliceHeaderScalExtInit(SDqLayer* pCurLayer, SSlice* pSlice)
    {
        SSliceHeaderExt* pSliceHeadExt = &pSlice->sSliceHeaderExt;
        SNalUnitHeaderExt* pNalHeadExt = &pCurLayer->sLayerInfo.sNalHeaderExt;

        uint8_t uiDependencyId = pNalHeadExt->uiDependencyId;

        pSliceHeadExt->bSliceSkipFlag = false;

        if (uiDependencyId > 0)
        { //spatial EL
            //bothe adaptive and default flags should equal to 0.
            pSliceHeadExt->bAdaptiveBaseModeFlag =
                pSliceHeadExt->bAdaptiveMotionPredFlag =
                    pSliceHeadExt->bAdaptiveResidualPredFlag = false;

            pSliceHeadExt->bDefaultBaseModeFlag =
                pSliceHeadExt->bDefaultMotionPredFlag =
                    pSliceHeadExt->bDefaultResidualPredFlag = false;
        }
    }

    public static void WelsSliceHeaderExtInit(sWelsEncCtx* pEncCtx, SDqLayer* pCurLayer, SSlice* pSlice)
    {
        SSliceHeaderExt* pCurSliceExt = &pSlice->sSliceHeaderExt;
        SSliceHeader* pCurSliceHeader = &pCurSliceExt->sSliceHeader;
        SSpatialLayerInternal* pParamInternal = &pEncCtx->pSvcParam->sDependencyLayers[pEncCtx->uiDependencyId];
        pCurSliceHeader->eSliceType = pEncCtx->eSliceType;

        pCurSliceExt->bStoreRefBasePicFlag = false;

        pCurSliceHeader->iFrameNum = pParamInternal->iFrameNum;
        pCurSliceHeader->uiIdrPicId = pParamInternal->uiIdrPicId;
        pCurSliceHeader->iPicOrderCntLsb = pEncCtx->pEncPic->iFramePoc;      // 0

        if (P_SLICE == pEncCtx->eSliceType)
        {
            pCurSliceHeader->uiNumRefIdxL0Active = 1;
            if (pCurSliceHeader->uiRefCount > 0 &&
                pCurSliceHeader->uiRefCount <= pCurLayer->sLayerInfo.pSpsP->iNumRefFrames)
            {
                pCurSliceHeader->bNumRefIdxActiveOverrideFlag = true;
                pCurSliceHeader->uiNumRefIdxL0Active = pCurSliceHeader->uiRefCount;
            }
            //to solve mismatch between debug&release
            else
            {
                pCurSliceHeader->bNumRefIdxActiveOverrideFlag = false;
            }
        }

        pCurSliceHeader->iSliceQpDelta = (int8_t)(pEncCtx->iGlobalQp - pCurLayer->sLayerInfo.pPpsP->iPicInitQp);

        //for deblocking initial
        pCurSliceHeader->uiDisableDeblockingFilterIdc = pCurLayer->iLoopFilterDisableIdc;
        pCurSliceHeader->iSliceAlphaC0Offset =
            pCurLayer->iLoopFilterAlphaC0Offset; // need update iSliceAlphaC0Offset & iSliceBetaOffset for pSlice-header if loop_filter_idc != 1
        pCurSliceHeader->iSliceBetaOffset = pCurLayer->iLoopFilterBetaOffset;
        pCurSliceExt->uiDisableInterLayerDeblockingFilterIdc = pCurLayer->uiDisableInterLayerDeblockingFilterIdc;

        if (pSlice->bSliceHeaderExtFlag)
        {
            WelsSliceHeaderScalExtInit(pCurLayer, pSlice);
        }
        else
        {
            //both adaptive and default flags should equal to 0.
            pCurSliceExt->bAdaptiveBaseModeFlag =
                pCurSliceExt->bAdaptiveMotionPredFlag =
                    pCurSliceExt->bAdaptiveResidualPredFlag = false;

            pCurSliceExt->bDefaultBaseModeFlag =
                pCurSliceExt->bDefaultMotionPredFlag =
                    pCurSliceExt->bDefaultResidualPredFlag = false;
        }
    }

    public static void UpdateMbNeighbor(SDqLayer* pCurDq, SMB* pMb, int32_t kiMbWidth, uint16_t uiSliceIdc)
    {
        uint32_t uiNeighborAvailFlag = 0;
        int32_t kiMbXY = pMb->iMbXY;
        int32_t kiMbX = pMb->iMbX;
        int32_t kiMbY = pMb->iMbY;
        bool bLeft;
        bool bTop;
        bool bLeftTop;
        bool bRightTop;
        int32_t iLeftXY, iTopXY, iLeftTopXY, iRightTopXY;

        pMb->uiSliceIdc = uiSliceIdc;
        iLeftXY = kiMbXY - 1;
        iTopXY = kiMbXY - kiMbWidth;
        iLeftTopXY = iTopXY - 1;
        iRightTopXY = iTopXY + 1;

        bLeft = (kiMbX > 0) && (uiSliceIdc == WelsMbToSliceIdc(pCurDq, iLeftXY));
        bTop = (kiMbY > 0) && (uiSliceIdc == WelsMbToSliceIdc(pCurDq, iTopXY));
        bLeftTop = (kiMbX > 0) && (kiMbY > 0) && (uiSliceIdc == WelsMbToSliceIdc(pCurDq, iLeftTopXY));
        bRightTop = (kiMbX < (kiMbWidth - 1)) && (kiMbY > 0) && (uiSliceIdc == WelsMbToSliceIdc(pCurDq, iRightTopXY));

        if (bLeft)
        {
            uiNeighborAvailFlag |= LEFT_MB_POS;
        }
        if (bTop)
        {
            uiNeighborAvailFlag |= TOP_MB_POS;
        }
        if (bLeftTop)
        {
            uiNeighborAvailFlag |= TOPLEFT_MB_POS;
        }
        if (bRightTop)
        {
            uiNeighborAvailFlag |= TOPRIGHT_MB_POS;
        }
        pMb->uiNeighborAvail = (uint8_t)uiNeighborAvailFlag;
    }

    // WelsCountMbType is MB_TYPES_CHECK only, which is off upstream.

    /*!
    * \brief    write reference picture list on reordering syntax in Slice header
    */
    public static void WriteReferenceReorder(SBitStringAux* pBs, SSliceHeader* sSliceHeader)
    {
        SRefPicListReorderSyntax* pRefOrdering = &sSliceHeader->sRefReordering;
        uint8_t eSliceType = (uint8_t)((int32_t)sSliceHeader->eSliceType % 5);
        int16_t n = 0;

        if (I_SLICE != (EWelsSliceType)eSliceType && SI_SLICE != (EWelsSliceType)eSliceType)
        { // !I && !SI
            BsWriteOneBit(pBs, true);
            //    {
            uint16_t uiReorderingOfPicNumsIdc;
            do
            {
                uiReorderingOfPicNumsIdc = pRefOrdering->SReorderingSyntax[n].uiReorderingOfPicNumsIdc;
                BsWriteUE(pBs, uiReorderingOfPicNumsIdc);
                if (0 == uiReorderingOfPicNumsIdc || 1 == uiReorderingOfPicNumsIdc)
                {
                    BsWriteUE(pBs, pRefOrdering->SReorderingSyntax[n].uiAbsDiffPicNumMinus1);
                }
                else if (2 == uiReorderingOfPicNumsIdc)
                {
                    BsWriteUE(pBs, pRefOrdering->SReorderingSyntax[n].iLongTermPicNum);
                }

                n++;
            } while (3 != uiReorderingOfPicNumsIdc);
            //    }
        }
    }

    /*!
    * \brief    write reference picture marking syntax in pSlice header
    */
    public static void WriteRefPicMarking(SBitStringAux* pBs, SSliceHeader* pSliceHeader, SNalUnitHeaderExt* pNalHdrExt)
    {
        SRefPicMarking* sRefMarking = &pSliceHeader->sRefMarking;
        int16_t n = 0;

        if (pNalHdrExt->bIdrFlag)
        {
            BsWriteOneBit(pBs, sRefMarking->bNoOutputOfPriorPicsFlag);
            BsWriteOneBit(pBs, sRefMarking->bLongTermRefFlag);
        }
        else
        {
            BsWriteOneBit(pBs, sRefMarking->bAdaptiveRefPicMarkingModeFlag);

            if (sRefMarking->bAdaptiveRefPicMarkingModeFlag)
            {
                int32_t iMmcoType;
                do
                {
                    iMmcoType = sRefMarking->SMmcoRef[n].iMmcoType;
                    BsWriteUE(pBs, (uint32_t)iMmcoType);
                    if (1 == iMmcoType || 3 == iMmcoType)
                    {
                        BsWriteUE(pBs, (uint32_t)(sRefMarking->SMmcoRef[n].iDiffOfPicNum - 1));
                    }

                    if (2 == iMmcoType)
                    {
                        BsWriteUE(pBs, (uint32_t)sRefMarking->SMmcoRef[n].iLongTermPicNum);
                    }

                    if (3 == iMmcoType || 6 == iMmcoType)
                    {
                        BsWriteUE(pBs, (uint32_t)sRefMarking->SMmcoRef[n].iLongTermFrameIdx);
                    }

                    if (4 == iMmcoType)
                    {
                        BsWriteUE(pBs, (uint32_t)(sRefMarking->SMmcoRef[n].iMaxLongTermFrameIdx + 1));
                    }

                    n++;
                } while (0 != iMmcoType);
            }
        }
    }

    public static void WelsSliceHeaderWrite(sWelsEncCtx* pCtx, SBitStringAux* pBs, SDqLayer* pCurLayer, SSlice* pSlice,
        IWelsParametersetStrategy* pParametersetStrategy)
    {
        SWelsSPS* pSps = pCurLayer->sLayerInfo.pSpsP;
        SWelsPPS* pPps = pCurLayer->sLayerInfo.pPpsP;
        SSliceHeader* pSliceHeader = &pSlice->sSliceHeaderExt.sSliceHeader;
        SNalUnitHeaderExt* pNalHead = &pCurLayer->sLayerInfo.sNalHeaderExt;

        BsWriteUE(pBs, (uint32_t)pSliceHeader->iFirstMbInSlice);
        BsWriteUE(pBs, (uint32_t)pSliceHeader->eSliceType);    /* same type things */

        BsWriteUE(pBs, pSliceHeader->pPps->iPpsId + (uint32_t)pParametersetStrategy->GetPpsIdOffset((int32_t)pSliceHeader->pPps->iPpsId));

        BsWriteBits(pBs, (int32_t)pSps->uiLog2MaxFrameNum, (uint32_t)pSliceHeader->iFrameNum);

        if (pNalHead->bIdrFlag)
        { /* NAL IDR */
            BsWriteUE(pBs, pSliceHeader->uiIdrPicId);
        }

        if (pSps->uiPocType == 0)
        {
            BsWriteBits(pBs, pSps->iLog2MaxPocLsb, (uint32_t)pSliceHeader->iPicOrderCntLsb);
        }
        else if (pSps->uiPocType == 1)
        {
            // TODO: implement.
            Debug.Assert(false);
        }
        else
        {
            // no-op for uiPocType == 2.
        }

        if (P_SLICE == pSliceHeader->eSliceType)
        {
            BsWriteOneBit(pBs, pSliceHeader->bNumRefIdxActiveOverrideFlag);
            if (pSliceHeader->bNumRefIdxActiveOverrideFlag)
            {
                BsWriteUE(pBs, (uint32_t)WELS_CLIP3(pSliceHeader->uiNumRefIdxL0Active - 1, 0, MAX_REF_PIC_COUNT));
            }
        }

        if (!pNalHead->bIdrFlag)
        {
            WriteReferenceReorder(pBs, pSliceHeader);
        }

        if (pNalHead->sNalUnitHeader.uiNalRefIdc != 0)
        {
            WriteRefPicMarking(pBs, pSliceHeader, pNalHead);
        }

        if (pPps->bEntropyCodingModeFlag && pSliceHeader->eSliceType != I_SLICE)
        {
            BsWriteUE(pBs, (uint32_t)pSlice->iCabacInitIdc);
        }
        BsWriteSE(pBs, pSliceHeader->iSliceQpDelta);       /* pSlice qp delta */

        if (pPps->bDeblockingFilterControlPresentFlag)
        {
            switch (pSliceHeader->uiDisableDeblockingFilterIdc)
            {
                case 0:
                case 3:
                case 4:
                case 6:
                    BsWriteUE(pBs, 0);
                    break;
                case 1:
                    BsWriteUE(pBs, 1);
                    break;
                case 2:
                case 5:
                    BsWriteUE(pBs, 2);
                    break;
                default:
                    WelsLog(&pCtx->sLogCtx, WELS_LOG_ERROR, $"Invalid uiDisableDeblockingFilterIdc {pSliceHeader->uiDisableDeblockingFilterIdc}");
                    break;
            }
            if (1 != pSliceHeader->uiDisableDeblockingFilterIdc)
            {
                BsWriteSE(pBs, pSliceHeader->iSliceAlphaC0Offset >> 1);
                BsWriteSE(pBs, pSliceHeader->iSliceBetaOffset >> 1);
            }
        }
    }

    public static void WelsSliceHeaderExtWrite(sWelsEncCtx* pCtx, SBitStringAux* pBs, SDqLayer* pCurLayer, SSlice* pSlice,
        IWelsParametersetStrategy* pParametersetStrategy)
    {
        SWelsSPS* pSps = pCurLayer->sLayerInfo.pSpsP;
        SWelsPPS* pPps = pCurLayer->sLayerInfo.pPpsP;
        SSubsetSps* pSubSps = pCurLayer->sLayerInfo.pSubsetSpsP;
        SSliceHeaderExt* pSliceHeadExt = &pSlice->sSliceHeaderExt;
        SSliceHeader* pSliceHeader = &pSliceHeadExt->sSliceHeader;
        SNalUnitHeaderExt* pNalHead = &pCurLayer->sLayerInfo.sNalHeaderExt;

        BsWriteUE(pBs, (uint32_t)pSliceHeader->iFirstMbInSlice);
        BsWriteUE(pBs, (uint32_t)pSliceHeader->eSliceType);    /* same type things */

        BsWriteUE(pBs, pSliceHeader->pPps->iPpsId +
                       (uint32_t)pParametersetStrategy->GetPpsIdOffset((int32_t)pSliceHeader->pPps->iPpsId));

        BsWriteBits(pBs, (int32_t)pSps->uiLog2MaxFrameNum, (uint32_t)pSliceHeader->iFrameNum);

        if (pNalHead->bIdrFlag)
        { /* NAL IDR */
            BsWriteUE(pBs, pSliceHeader->uiIdrPicId);
        }

        if (pSps->uiPocType == 0)
        {
            BsWriteBits(pBs, pSps->iLog2MaxPocLsb, (uint32_t)pSliceHeader->iPicOrderCntLsb);
        }
        else if (pSps->uiPocType == 1)
        {
            // TODO: implement.
            Debug.Assert(false);
        }
        else
        {
            // no-op for uiPocType == 2.
        }
        //  {
        if (P_SLICE == pSliceHeader->eSliceType)
        {
            BsWriteOneBit(pBs, pSliceHeader->bNumRefIdxActiveOverrideFlag);
            if (pSliceHeader->bNumRefIdxActiveOverrideFlag)
            {
                BsWriteUE(pBs, (uint32_t)WELS_CLIP3(pSliceHeader->uiNumRefIdxL0Active - 1, 0, MAX_REF_PIC_COUNT));
            }
        }

        if (!pNalHead->bIdrFlag)
        {
            WriteReferenceReorder(pBs, pSliceHeader);
        }

        if (pNalHead->sNalUnitHeader.uiNalRefIdc != 0)
        {
            WriteRefPicMarking(pBs, pSliceHeader, pNalHead);

            if (!pSubSps->sSpsSvcExt.bSliceHeaderRestrictionFlag)
            {
                BsWriteOneBit(pBs, pSliceHeadExt->bStoreRefBasePicFlag);
            }
        }
        //  }


        if (pPps->bEntropyCodingModeFlag && pSliceHeader->eSliceType != I_SLICE)
        {
            BsWriteUE(pBs, (uint32_t)pSlice->iCabacInitIdc);
        }

        BsWriteSE(pBs, pSliceHeader->iSliceQpDelta);       /* pSlice qp delta */

        if (pPps->bDeblockingFilterControlPresentFlag)
        {
            BsWriteUE(pBs, pSliceHeader->uiDisableDeblockingFilterIdc);
            if (1 != pSliceHeader->uiDisableDeblockingFilterIdc)
            {
                BsWriteSE(pBs, pSliceHeader->iSliceAlphaC0Offset >> 1);
                BsWriteSE(pBs, pSliceHeader->iSliceBetaOffset >> 1);
            }
        }

        // #if !defined(DISABLE_FMO_FEATURE) slice_group_change_cycle #endif: the encoder builds with
        // DISABLE_FMO_FEATURE.

        // if (false) { the slice skip, adaptive and default flags }: dead upstream.

        if (!pSubSps->sSpsSvcExt.bSliceHeaderRestrictionFlag)
        {
            BsWriteBits(pBs, 4, 0);
            BsWriteBits(pBs, 4, 15);
        }
    }

    // WelsInterMbEncode comes with E5.

    //only BaseLayer inter MB and SpatialLayer (uiQualityId = 0) inter MB calling this pFunc.
    //only for I SSlice
    public static void WelsIMbChromaEncode(sWelsEncCtx* pEncCtx, SMB* pCurMb, SMbCache* pMbCache)
    {
        SWelsFuncPtrList* pFunc = pEncCtx->pFuncList;
        SDqLayer* pCurLayer = pEncCtx->pCurDqLayer;
        int32_t kiEncStride = pCurLayer->iEncStride[1];
        int32_t kiCsStride = pCurLayer->iCsStride[1];
        int16_t* pCurRS = pMbCache->pCoeffLevel;
        uint8_t* pBestPred = pMbCache->pBestPredIntraChroma;
        uint8_t* pCsCb = pMbCache->SPicData.pCsMb[1];
        uint8_t* pCsCr = pMbCache->SPicData.pCsMb[2];

        //cb
        pFunc->pfDctFourT4(pCurRS, pMbCache->SPicData.pEncMb[1], kiEncStride, pBestPred, 8);
        WelsEncRecUV(pFunc, pCurMb, pMbCache, pCurRS, 1);
        pFunc->pfIDctFourT4(pCsCb, kiCsStride, pBestPred, 8, pCurRS);

        //cr
        pFunc->pfDctFourT4(pCurRS + 64, pMbCache->SPicData.pEncMb[2], kiEncStride, pBestPred + 64, 8);
        WelsEncRecUV(pFunc, pCurMb, pMbCache, pCurRS + 64, 2);
        pFunc->pfIDctFourT4(pCsCr, kiCsStride, pBestPred + 64, 8, pCurRS + 64);
    }

    // WelsPMbChromaEncode and OutputPMbWithoutConstructCsRsNoCopy come with E5.

    //only BaseLayer inter MB and SpatialLayer (uiQualityId = 0) inter MB calling this pFunc.
    //for P SSlice (intra part + inter part)
    public static void UpdateQpForOverflow(SMB* pCurMb, uint8_t kuiChromaQpIndexOffset)
    {
        pCurMb->uiLumaQp += DELTA_QP;
        pCurMb->uiChromaQp = g_kuiChromaQpTable[CLIP3_QP_0_51(pCurMb->uiLumaQp + kuiChromaQpIndexOffset)];
    }

    // for intra non-dynamic pSlice
    //encapsulate two kinds of reconstruction:
    //first. store base or highest Dependency Layer with only one quality (without CS RS reconstruction)
    //second. lower than highest Dependency Layer, and for every Dependency Layer with one quality layer(single layer)
    public static int32_t WelsISliceMdEnc(sWelsEncCtx* pEncCtx, SSlice* pSlice)
    { //pMd + encoding
        SDqLayer* pCurLayer = pEncCtx->pCurDqLayer;
        SMbCache* pMbCache = &pSlice->sMbCacheInfo;
        SSliceHeaderExt* pSliceHdExt = &pSlice->sSliceHeaderExt;
        SMB* pMbList = pCurLayer->sMbDataP;
        SMB* pCurMb = null;
        int32_t kiSliceFirstMbXY = pSliceHdExt->sSliceHeader.iFirstMbInSlice;
        int32_t iNextMbIdx = kiSliceFirstMbXY;
        int32_t kiTotalNumMb = pCurLayer->iMbWidth * pCurLayer->iMbHeight;
        int32_t iCurMbIdx = 0, iNumMbCoded = 0;
        int32_t kiSliceIdx = pSlice->iSliceIdx;
        uint8_t kuiChromaQpIndexOffset = (uint8_t)pCurLayer->sLayerInfo.pPpsP->uiChromaQpIndexOffset;

        SWelsMD sMd;
        int32_t iEncReturn = ENC_RETURN_SUCCESS;
        SDynamicSlicingStack sDss;
        if (pEncCtx->pSvcParam->iEntropyCodingModeFlag != 0)
        {
            // WelsInitSliceCabac (pEncCtx, pSlice); sDss.pRestoreBuffer = NULL; ...
            throw new NotSupportedException("WelsISliceMdEnc: CABAC encoding is not supported by this port.");
        }
        for (; ; )
        {
            if (pEncCtx->pSvcParam->iEntropyCodingModeFlag == 0)
                pEncCtx->pFuncList->pfStashMBStatus(&sDss, pSlice, 0);
            iCurMbIdx = iNextMbIdx;
            pCurMb = &pMbList[iCurMbIdx];

            pEncCtx->pFuncList->pfRc.pfWelsRcMbInit(pEncCtx, pCurMb, pSlice);
            WelsMdIntraInit(pEncCtx, pCurMb, pMbCache, kiSliceFirstMbXY);

        TRY_REENCODING:
            sMd.iLambda = g_kiQpCostTable[pCurMb->uiLumaQp];
            WelsMdIntraMb(pEncCtx, &sMd, pCurMb, pMbCache);
            UpdateNonZeroCountCache(pCurMb, pMbCache);


            iEncReturn = pEncCtx->pFuncList->pfWelsSpatialWriteMbSyn(pEncCtx, pSlice, pCurMb);
            if (pEncCtx->pSvcParam->iEntropyCodingModeFlag == 0)
            {
                if ((iEncReturn == ENC_RETURN_VLCOVERFLOWFOUND) && (pCurMb->uiLumaQp < 50))
                {
                    pEncCtx->pFuncList->pfStashPopMBStatus(&sDss, pSlice);
                    UpdateQpForOverflow(pCurMb, kuiChromaQpIndexOffset);
                    goto TRY_REENCODING;
                }
            }
            if (ENC_RETURN_SUCCESS != iEncReturn)
                return iEncReturn;

            pCurMb->uiSliceIdc = (uint16_t)kiSliceIdx;

            // WelsCountMbType (MB_TYPES_CHECK) is off upstream.

            pEncCtx->pFuncList->pfMdBackgroundInfoUpdate(pCurLayer, pCurMb, pMbCache->bCollocatedPredFlag, (int32_t)I_SLICE);
            pEncCtx->pFuncList->pfRc.pfWelsRcMbInfoUpdate(pEncCtx, pCurMb, sMd.iCostLuma, pSlice);

            ++iNumMbCoded;
            iNextMbIdx = WelsGetNextMbOfSlice(pCurLayer, iCurMbIdx);
            if (iNextMbIdx == -1 || iNextMbIdx >= kiTotalNumMb || iNumMbCoded >= kiTotalNumMb)
            {
                break;
            }
        }

        return ENC_RETURN_SUCCESS;
    }

    // WelsISliceMdEncDynamic and WelsCodePOverDynamicSlice (SM_SIZELIMITED_SLICE) come with
    // Phase 4, and WelsPSliceMdEnc, WelsPSliceMdEncDynamic and WelsCodePSlice with E5.

    // static const PWelsCodingSliceFunc g_pWelsSliceCoding[2][2] = {
    //   { WelsCodePSlice, WelsCodePOverDynamicSlice }, // P SSlice
    //   { WelsISliceMdEnc, WelsISliceMdEncDynamic }    // I SSlice
    // };
    // Only the I-slice entry is ported so far, so the table is a switch that rejects the rest.
    private static int32_t g_pWelsSliceCoding(bool bIdrFlag, int32_t kiDynamicSliceFlag, sWelsEncCtx* pCtx, SSlice* pSlice) =>
        (bIdrFlag, kiDynamicSliceFlag != 0) switch
        {
            (true, false) => WelsISliceMdEnc(pCtx, pSlice),
            (true, true) => throw new NotSupportedException("WelsISliceMdEncDynamic: SM_SIZELIMITED_SLICE is not ported yet."),
            (false, false) => throw new NotSupportedException("WelsCodePSlice: P slices are not ported yet."),
            (false, true) => throw new NotSupportedException("WelsCodePOverDynamicSlice: P slices are not ported yet."),
        };

    // static const PWelsSliceHeaderWriteFunc g_pWelsWriteSliceHeader[2] = {  // 0: for base; 1: for ext;
    //   WelsSliceHeaderWrite,
    //   WelsSliceHeaderExtWrite
    // };
    private static void g_pWelsWriteSliceHeader(bool bSliceHeaderExtFlag, sWelsEncCtx* pCtx, SBitStringAux* pBs, SDqLayer* pCurLayer,
        SSlice* pSlice, IWelsParametersetStrategy* pParametersetStrategy)
    {
        if (bSliceHeaderExtFlag)
        {
            WelsSliceHeaderExtWrite(pCtx, pBs, pCurLayer, pSlice, pParametersetStrategy);
        }
        else
        {
            WelsSliceHeaderWrite(pCtx, pBs, pCurLayer, pSlice, pParametersetStrategy);
        }
    }

    public static int32_t AllocMbCacheAligned(SMbCache* pMbCache, CMemoryAlign* pMa)
    {
        pMbCache->pMemPredMb = (uint8_t*)pMa->WelsMallocz(2 * 256 * sizeof(uint8_t), "pMbCache->pMemPredMb");
        if (null == pMbCache->pMemPredMb)
            return 1;

        pMbCache->pCoeffLevel = (int16_t*)pMa->WelsMallocz(MB_COEFF_LIST_SIZE * sizeof(int16_t), "pMbCache->pCoeffLevel");
        if (null == pMbCache->pCoeffLevel)
            return 1;
        pMbCache->pSkipMb = (uint8_t*)pMa->WelsMallocz(384 * sizeof(uint8_t), "pMbCache->pSkipMb");
        if (null == pMbCache->pSkipMb)
            return 1;
        pMbCache->pMemPredBlk4 = (uint8_t*)pMa->WelsMallocz(2 * 16 * sizeof(uint8_t), "pMbCache->pMemPredBlk4");
        if (null == pMbCache->pMemPredBlk4)
            return 1;
        pMbCache->pBufferInterPredMe = (uint8_t*)pMa->WelsMallocz(4 * 640 * sizeof(uint8_t), "pMbCache->pBufferInterPredMe");
        if (null == pMbCache->pBufferInterPredMe)
            return 1;
        pMbCache->pPrevIntra4x4PredModeFlag = (bool*)pMa->WelsMallocz(16 * sizeof(bool),
                                              "pMbCache->pPrevIntra4x4PredModeFlag");
        if (null == pMbCache->pPrevIntra4x4PredModeFlag)
            return 1;
        pMbCache->pRemIntra4x4PredModeFlag = (int8_t*)pMa->WelsMallocz(16 * sizeof(int8_t),
                                             "pMbCache->pRemIntra4x4PredModeFlag");
        if (null == pMbCache->pRemIntra4x4PredModeFlag)
            return 1;
        pMbCache->pDct = (SDCTCoeff*)pMa->WelsMallocz((uint32_t)sizeof(SDCTCoeff), "pMbCache->pDct");
        if (null == pMbCache->pDct)
            return 1;

        return 0;
    }

    //  Free slice's MB cache buffer
    public static void FreeMbCache(SMbCache* pMbCache, CMemoryAlign* pMa)
    {
        if (null != pMbCache->pCoeffLevel)
        {
            pMa->WelsFree(pMbCache->pCoeffLevel, "pMbCache->pCoeffLevel");
            pMbCache->pCoeffLevel = null;
        }
        if (null != pMbCache->pMemPredMb)
        {
            pMa->WelsFree(pMbCache->pMemPredMb, "pMbCache->pMemPredMb");
            pMbCache->pMemPredMb = null;
        }
        if (null != pMbCache->pSkipMb)
        {
            pMa->WelsFree(pMbCache->pSkipMb, "pMbCache->pSkipMb");
            pMbCache->pSkipMb = null;
        }
        if (null != pMbCache->pMemPredBlk4)
        {
            pMa->WelsFree(pMbCache->pMemPredBlk4, "pMbCache->pMemPredBlk4");
            pMbCache->pMemPredBlk4 = null;
        }
        if (null != pMbCache->pBufferInterPredMe)
        {
            pMa->WelsFree(pMbCache->pBufferInterPredMe, "pMbCache->pBufferInterPredMe");
            pMbCache->pBufferInterPredMe = null;
        }
        if (null != pMbCache->pPrevIntra4x4PredModeFlag)
        {
            pMa->WelsFree(pMbCache->pPrevIntra4x4PredModeFlag, "pMbCache->pPrevIntra4x4PredModeFlag");
            pMbCache->pPrevIntra4x4PredModeFlag = null;
        }
        if (null != pMbCache->pRemIntra4x4PredModeFlag)
        {
            pMa->WelsFree(pMbCache->pRemIntra4x4PredModeFlag, "pMbCache->pRemIntra4x4PredModeFlag");
            pMbCache->pRemIntra4x4PredModeFlag = null;
        }
        if (null != pMbCache->pDct)
        {
            pMa->WelsFree(pMbCache->pDct, "pMbCache->pDct");
            pMbCache->pDct = null;
        }
    }

    //Initialize slice's boundary info)
    public static int32_t InitSliceBoundaryInfo(SDqLayer* pCurLayer,
                                                SSliceArgument* pSliceArgument,
                                                int32_t kiSliceNumInFrame)
    {
        int32_t* kpSlicesAssignList = (int32_t*)&(pSliceArgument->uiSliceMbNum[0]);
        int32_t kiMBWidth = pCurLayer->iMbWidth;
        int32_t kiMBHeight = pCurLayer->iMbHeight;
        int32_t kiCountNumMbInFrame = kiMBWidth * kiMBHeight;
        int32_t iSliceIdx = 0;
        int32_t iFirstMBInSlice = 0;
        int32_t iMbNumInSlice = 0;

        for (; iSliceIdx < kiSliceNumInFrame; iSliceIdx++)
        {
            if (SM_SINGLE_SLICE == pSliceArgument->uiSliceMode)
            {
                iFirstMBInSlice = 0;
                iMbNumInSlice = kiCountNumMbInFrame;

            }
            else if ((SM_RASTER_SLICE == pSliceArgument->uiSliceMode) && (0 == pSliceArgument->uiSliceMbNum[0]))
            {
                iFirstMBInSlice = iSliceIdx * kiMBWidth;
                iMbNumInSlice = kiMBWidth;
            }
            else if (SM_RASTER_SLICE == pSliceArgument->uiSliceMode ||
                     SM_FIXEDSLCNUM_SLICE == pSliceArgument->uiSliceMode)
            {
                int32_t iMbIdx = 0;
                for (int i = 0; i < iSliceIdx; i++)
                {
                    iMbIdx += kpSlicesAssignList[i];
                }

                if (iMbIdx >= kiCountNumMbInFrame)
                {
                    return ENC_RETURN_UNEXPECTED;
                }

                iFirstMBInSlice = iMbIdx;
                iMbNumInSlice = kpSlicesAssignList[iSliceIdx];

            }
            else if (SM_SIZELIMITED_SLICE == pSliceArgument->uiSliceMode)
            {
                iFirstMBInSlice = 0;
                iMbNumInSlice = kiCountNumMbInFrame;

            }
            else
            { // any else uiSliceMode?
                Debug.Assert(false);
            }

            pCurLayer->pCountMbNumInSlice[iSliceIdx] = iMbNumInSlice;
            pCurLayer->pFirstMbIdxOfSlice[iSliceIdx] = iFirstMBInSlice;
        }

        return ENC_RETURN_SUCCESS;
    }

    public static int32_t SetSliceBoundaryInfo(SDqLayer* pCurLayer, SSlice* pSlice, int32_t kiSliceIdx)
    {
        if (null == pCurLayer || null == pSlice ||
            null == pCurLayer->pFirstMbIdxOfSlice ||
            null == pCurLayer->pCountMbNumInSlice)
        {

            return ENC_RETURN_UNEXPECTED;
        }

        pSlice->sSliceHeaderExt.sSliceHeader.iFirstMbInSlice = pCurLayer->pFirstMbIdxOfSlice[kiSliceIdx];
        pSlice->iCountMbNumInSlice = pCurLayer->pCountMbNumInSlice[kiSliceIdx];

        return ENC_RETURN_SUCCESS;
    }

    //Allocate slice's MB info buffer
    public static int32_t AllocateSliceMBBuffer(SSlice* pSlice, CMemoryAlign* pMa)
    {
        if (AllocMbCacheAligned(&pSlice->sMbCacheInfo, pMa) != 0)
        {
            return ENC_RETURN_MEMALLOCERR;
        }

        return ENC_RETURN_SUCCESS;
    }

    // Initialize slice bs buffer info
    public static int32_t InitSliceBsBuffer(SSlice* pSlice,
                                            SBitStringAux* pBsWrite,
                                            bool bIndependenceBsBuffer,
                                            int32_t iMaxSliceBufferSize,
                                            CMemoryAlign* pMa)
    {
        pSlice->sSliceBs.uiSize = (uint32_t)iMaxSliceBufferSize;
        pSlice->sSliceBs.uiBsPos = 0;

        if (bIndependenceBsBuffer)
        {
            pSlice->pSliceBsa = &pSlice->sSliceBs.sBsWrite;
            pSlice->sSliceBs.pBs = (uint8_t*)pMa->WelsMallocz((uint32_t)iMaxSliceBufferSize, "sSliceBs.pBs");
            if (null == pSlice->sSliceBs.pBs)
            {
                pSlice->sSliceBs.uiBsSize = 0;
                return ENC_RETURN_MEMALLOCERR;
            }
            pSlice->sSliceBs.uiBsSize = (uint32_t)iMaxSliceBufferSize;
        }
        else
        {
            pSlice->pSliceBsa = pBsWrite;
            pSlice->sSliceBs.pBs = null;
            pSlice->sSliceBs.uiBsSize = 0;
        }
        return ENC_RETURN_SUCCESS;
    }

    //free slice bs buffer
    public static void FreeSliceBuffer(SSlice** pSliceList, int32_t kiMaxSliceNum, CMemoryAlign* pMa, string kpTag)
    {
        if (null != *pSliceList)
        {
            int32_t iSliceIdx = 0;
            while (iSliceIdx < kiMaxSliceNum)
            {
                SSlice* pSlice = &(*pSliceList)[iSliceIdx];
                FreeMbCache(&pSlice->sMbCacheInfo, pMa);

                //slice bs buffer
                if (null != pSlice->sSliceBs.pBs)
                {
                    pMa->WelsFree(pSlice->sSliceBs.pBs, "sSliceBs.pBs");
                    pSlice->sSliceBs.pBs = null;
                }
                ++iSliceIdx;
            }
            pMa->WelsFree(*pSliceList, kpTag);
            *pSliceList = null;
        }
    }

    // SSlice*& pSliceList is only read, so it is SSlice* here.
    public static int32_t InitSliceList(SSlice* pSliceList,
                                        SBitStringAux* pBsWrite,
                                        int32_t kiMaxSliceNum,
                                        int32_t kiMaxSliceBufferSize,
                                        bool bIndependenceBsBuffer,
                                        CMemoryAlign* pMa)
    {
        int32_t iSliceIdx = 0;
        int32_t iRet = 0;

        if (kiMaxSliceBufferSize <= 0)
        {
            return ENC_RETURN_UNEXPECTED;
        }

        while (iSliceIdx < kiMaxSliceNum)
        {
            SSlice* pSlice = pSliceList + iSliceIdx;
            if (null == pSlice)
            {
                return ENC_RETURN_MEMALLOCERR;
            }

            pSlice->iSliceIdx = iSliceIdx;
            pSlice->uiBufferIdx = 0;
            pSlice->iCountMbNumInSlice = 0;
            pSlice->sSliceHeaderExt.sSliceHeader.iFirstMbInSlice = 0;

            iRet = InitSliceBsBuffer(pSlice,
                                     pBsWrite,
                                     bIndependenceBsBuffer,
                                     kiMaxSliceBufferSize,
                                     pMa);
            if (ENC_RETURN_SUCCESS != iRet)
            {
                return iRet;
            }

            iRet = AllocateSliceMBBuffer(pSlice, pMa);

            if (ENC_RETURN_SUCCESS != iRet)
            {
                return iRet;
            }
            ++iSliceIdx;
        }
        return ENC_RETURN_SUCCESS;
    }

    // InitAllSlicesInThread and InitOneSliceInThread are for the threaded slice coders, which come
    // with the threading work.

    public static int32_t InitSliceThreadInfo(sWelsEncCtx* pCtx,
                                              SDqLayer* pDqLayer,
                                              int32_t kiDlayerIndex,
                                              CMemoryAlign* pMa)
    {
        int32_t iThreadNum = pCtx->pSvcParam->iMultipleThreadIdc;
        int32_t iMaxSliceNum = 0;
        int32_t iSlcBufferNum = 0;
        int32_t iIdx = 0;
        int32_t iRet = 0;

        Debug.Assert(iThreadNum > 0);

        //for fixed slice num case, no need to reallocate, so one slice buffer for all thread
        if (pDqLayer->bThreadSlcBufferFlag)
        {
            iMaxSliceNum = pDqLayer->iMaxSliceNum / iThreadNum + 1;
            iSlcBufferNum = iThreadNum;
        }
        else
        {
            iMaxSliceNum = pDqLayer->iMaxSliceNum;
            iSlcBufferNum = 1;
        }

        while (iIdx < iSlcBufferNum)
        {
            pDqLayer->sSliceBufferInfo[iIdx].iMaxSliceNum = iMaxSliceNum;
            pDqLayer->sSliceBufferInfo[iIdx].iCodedSliceNum = 0;
            pDqLayer->sSliceBufferInfo[iIdx].pSliceBuffer = (SSlice*)pMa->WelsMallocz((uint32_t)(sizeof(SSlice) * iMaxSliceNum),
                "pSliceBuffer");
            if (null == pDqLayer->sSliceBufferInfo[iIdx].pSliceBuffer)
            {
                WelsLog(&(pCtx->sLogCtx), WELS_LOG_ERROR,
                        $"CWelsH264SVCEncoder::InitSliceThreadInfo: pSliceThreadInfo->pSliceBuffer[iIdx] is NULL");
                return ENC_RETURN_MEMALLOCERR;
            }
            iRet = InitSliceList(pDqLayer->sSliceBufferInfo[iIdx].pSliceBuffer,
                                 &pCtx->pOut->sBsWrite,
                                 iMaxSliceNum,
                                 pCtx->iSliceBufferSize[kiDlayerIndex],
                                 pDqLayer->bSliceBsBufferFlag,
                                 pMa);
            if (ENC_RETURN_SUCCESS != iRet)
            {
                return iRet;
            }
            iIdx++;
        }

        for (; iIdx < MAX_THREADS_NUM; iIdx++)
        {
            pDqLayer->sSliceBufferInfo[iIdx].iMaxSliceNum = 0;
            pDqLayer->sSliceBufferInfo[iIdx].iCodedSliceNum = 0;
            pDqLayer->sSliceBufferInfo[iIdx].pSliceBuffer = null;
        }

        return ENC_RETURN_SUCCESS;
    }

    public static int32_t InitSliceInLayer(sWelsEncCtx* pCtx,
                                           SDqLayer* pDqLayer,
                                           int32_t kiDlayerIndex,
                                           CMemoryAlign* pMa)
    {
        int32_t iRet = 0;
        int32_t iSliceIdx = 0;
        int32_t iSlcBuffIdx = 0;
        int32_t iStartIdx = 0;
        int32_t iMaxSliceNum = pDqLayer->iMaxSliceNum;
        SSliceArgument* pSliceArgument = &pCtx->pSvcParam->sSpatialLayers[kiDlayerIndex].sSliceArgument;

        //SM_SINGLE_SLICE mode using single-thread bs writer pOut->sBsWrite
        //even though multi-thread is on for other layers
        pDqLayer->bSliceBsBufferFlag = (pCtx->pSvcParam->iMultipleThreadIdc > 1 &&
                                        SM_SINGLE_SLICE != pSliceArgument->uiSliceMode) ? true : false;

        pDqLayer->bThreadSlcBufferFlag = (pCtx->pSvcParam->iMultipleThreadIdc > 1 &&
                                          SM_SIZELIMITED_SLICE == pSliceArgument->uiSliceMode) ? true : false;

        iRet = InitSliceThreadInfo(pCtx,
                                   pDqLayer,
                                   kiDlayerIndex,
                                   pMa);
        if (ENC_RETURN_SUCCESS != iRet)
        {
            return ENC_RETURN_MEMALLOCERR;
        }

        pDqLayer->iMaxSliceNum = 0;
        for (iSlcBuffIdx = 0; iSlcBuffIdx < pCtx->iActiveThreadsNum; iSlcBuffIdx++)
        {
            pDqLayer->iMaxSliceNum += pDqLayer->sSliceBufferInfo[iSlcBuffIdx].iMaxSliceNum;
        }

        pDqLayer->ppSliceInLayer = (SSlice**)pMa->WelsMallocz((uint32_t)(sizeof(SSlice*) * pDqLayer->iMaxSliceNum), "ppSliceInLayer");
        if (null == pDqLayer->ppSliceInLayer)
        {
            WelsLog(&(pCtx->sLogCtx), WELS_LOG_ERROR, $"CWelsH264SVCEncoder::InitSliceInLayer() pDqLayer->ppSliceInLayer is NULL");
            return ENC_RETURN_MEMALLOCERR;
        }

        // sizeof (int32_t*) as upstream: the tables are over-allocated on 64-bit.
        pDqLayer->pFirstMbIdxOfSlice = (int32_t*)pMa->WelsMallocz((uint32_t)(sizeof(int32_t*) * pDqLayer->iMaxSliceNum),
                                       "pFirstMbIdxOfSlice");
        if (null == pDqLayer->pFirstMbIdxOfSlice)
        {
            WelsLog(&(pCtx->sLogCtx), WELS_LOG_ERROR,
                    $"CWelsH264SVCEncoder::InitSliceInLayer() pDqLayer->pFirstMbIdxOfSlice is NULL");
            return ENC_RETURN_MEMALLOCERR;
        }

        pDqLayer->pCountMbNumInSlice = (int32_t*)pMa->WelsMallocz((uint32_t)(sizeof(int32_t*) * pDqLayer->iMaxSliceNum),
                                       "pCountMbNumInSlice");
        if (null == pDqLayer->pCountMbNumInSlice)
        {
            WelsLog(&(pCtx->sLogCtx), WELS_LOG_ERROR,
                    $"CWelsH264SVCEncoder::InitSliceInLayer() pDqLayer->pCountMbNumInSlice is NULL");
            return ENC_RETURN_MEMALLOCERR;
        }

        iRet = InitSliceBoundaryInfo(pDqLayer, pSliceArgument, iMaxSliceNum);
        if (ENC_RETURN_SUCCESS != iRet)
        {
            return iRet;
        }

        iStartIdx = 0;
        for (iSlcBuffIdx = 0; iSlcBuffIdx < pCtx->iActiveThreadsNum; iSlcBuffIdx++)
        {
            for (iSliceIdx = 0; iSliceIdx < pDqLayer->sSliceBufferInfo[iSlcBuffIdx].iMaxSliceNum; iSliceIdx++)
            {
                pDqLayer->ppSliceInLayer[iStartIdx + iSliceIdx] = pDqLayer->sSliceBufferInfo[iSlcBuffIdx].pSliceBuffer + iSliceIdx;
            }

            iStartIdx += pDqLayer->sSliceBufferInfo[iSlcBuffIdx].iMaxSliceNum;
        }

        return ENC_RETURN_SUCCESS;
    }

    public static void InitSliceHeadWithBase(SSlice* pSlice, SSlice* pBaseSlice)
    {
        if (null == pSlice || null == pBaseSlice)
        {
            return;
        }

        SSliceHeaderExt* pBaseSHExt = &pBaseSlice->sSliceHeaderExt;
        SSliceHeaderExt* pSHExt = &pSlice->sSliceHeaderExt;

        pSlice->bSliceHeaderExtFlag = pBaseSlice->bSliceHeaderExtFlag;
        pSHExt->sSliceHeader.iPpsId = pBaseSHExt->sSliceHeader.iPpsId;
        pSHExt->sSliceHeader.pPps = pBaseSHExt->sSliceHeader.pPps;
        pSHExt->sSliceHeader.iSpsId = pBaseSHExt->sSliceHeader.iSpsId;
        pSHExt->sSliceHeader.pSps = pBaseSHExt->sSliceHeader.pSps;
    }

    public static void InitSliceRefInfoWithBase(SSlice* pSlice, SSlice* pBaseSlice, uint8_t kuiRefCount)
    {
        if (null == pSlice || null == pBaseSlice)
        {
            return;
        }

        SSliceHeaderExt* pBaseSHExt = &pBaseSlice->sSliceHeaderExt;
        SSliceHeaderExt* pSHExt = &pSlice->sSliceHeaderExt;

        pSHExt->sSliceHeader.uiRefCount = kuiRefCount;
        pSHExt->sSliceHeader.sRefMarking = pBaseSHExt->sSliceHeader.sRefMarking;
        pSHExt->sSliceHeader.sRefReordering = pBaseSHExt->sSliceHeader.sRefReordering;
    }

    // InitSliceRC (static inline) is only called from ReallocateSliceList.
    private static int32_t InitSliceRC(SSlice* pSlice, int32_t kiGlobalQp)
    {
        if (null == pSlice || kiGlobalQp < 0)
        {
            return ENC_RETURN_INVALIDINPUT;
        }

        pSlice->sSlicingOverRc.iComplexityIndexSlice = 0;
        pSlice->sSlicingOverRc.iCalculatedQpSlice = kiGlobalQp;
        pSlice->sSlicingOverRc.iTotalQpSlice = 0;
        pSlice->sSlicingOverRc.iTotalMbSlice = 0;
        pSlice->sSlicingOverRc.iTargetBitsSlice = 0;
        pSlice->sSlicingOverRc.iFrameBitsSlice = 0;
        pSlice->sSlicingOverRc.iGomBitsSlice = 0;

        return ENC_RETURN_SUCCESS;
    }

    // ReallocateSliceList, CalculateNewSliceNum, ReallocateSliceInThread, ExtendLayerBuffer,
    // ReallocSliceBuffer, CheckAllSliceBuffer and ReOrderSliceInLayer grow the slice buffers for
    // SM_SIZELIMITED_SLICE and come with Phase 4.

    public static int32_t GetCurLayerNalCount(SDqLayer* pCurDq, int32_t kiCodedSliceNum)
    {
        int32_t iTotalNalCount = 0;
        int32_t iSliceIdx = 0;
        SWelsSliceBs* pSliceBs = null;
        for (; iSliceIdx < kiCodedSliceNum; iSliceIdx++)
        {
            pSliceBs = &pCurDq->ppSliceInLayer[iSliceIdx]->sSliceBs;
            if (pSliceBs != null && pSliceBs->uiBsPos > 0)
            {
                iTotalNalCount += pSliceBs->iNalIndex;
            }
        }

        return iTotalNalCount;
    }

    public static int32_t GetTotalCodedNalCount(SFrameBSInfo* pFbi)
    {
        int32_t iTotalCodedNalCount = 0;
        for (int32_t iNalIdx = 0; iNalIdx < MAX_LAYER_NUM_OF_FRAME; iNalIdx++)
        {
            iTotalCodedNalCount += pFbi->sLayerInfo[iNalIdx].iNalCount;
        }

        return iTotalCodedNalCount;
    }

    // FrameBsRealloc and SliceLayerInfoUpdate are for the threaded SM_SIZELIMITED_SLICE path
    // (Phase 4).

    public static int32_t WelsCodeOneSlice(sWelsEncCtx* pEncCtx, SSlice* pCurSlice, int32_t kiNalType)
    {
        SDqLayer* pCurLayer = pEncCtx->pCurDqLayer;
        SWelsSvcRc* pWelsSvcRc = &pEncCtx->pWelsSvcRc[pEncCtx->uiDependencyId];
        SNalUnitHeaderExt* pNalHeadExt = &pCurLayer->sLayerInfo.sNalHeaderExt;
        SBitStringAux* pBs = pCurSlice->pSliceBsa;
        int32_t kiDynamicSliceFlag =
            (pEncCtx->pSvcParam->sSpatialLayers[pEncCtx->uiDependencyId].sSliceArgument.uiSliceMode
             == SM_SIZELIMITED_SLICE) ? 1 : 0;
        if (I_SLICE == pEncCtx->eSliceType)
        {
            pNalHeadExt->bIdrFlag = true;
            pCurSlice->sScaleShift = 0;
        }
        else
        {
            uint32_t kuiTemporalId = pNalHeadExt->uiTemporalId;
            pCurSlice->sScaleShift = (uint8_t)(kuiTemporalId != 0 ? (kuiTemporalId - pEncCtx->pRefPic->uiTemporalId) : 0);
        }

        WelsSliceHeaderExtInit(pEncCtx, pCurLayer, pCurSlice);

        //RomRC init slice by slice
        if (pWelsSvcRc->bGomRC)
        {
            GomRCInitForOneSlice(pCurSlice, pWelsSvcRc->iBitsPerMb);
        }

        g_pWelsWriteSliceHeader(pCurSlice->bSliceHeaderExtFlag, pEncCtx, pBs, pCurLayer, pCurSlice,
            pEncCtx->pFuncList->pParametersetStrategy);

        pCurSlice->uiLastMbQp = (uint8_t)(pCurLayer->sLayerInfo.pPpsP->iPicInitQp + pCurSlice->sSliceHeaderExt.sSliceHeader.iSliceQpDelta);

        int32_t iEncReturn = g_pWelsSliceCoding(pNalHeadExt->bIdrFlag, kiDynamicSliceFlag, pEncCtx, pCurSlice);
        if (ENC_RETURN_SUCCESS != iEncReturn)
        {
            return iEncReturn;
        }

        WelsWriteSliceEndSyn(pCurSlice, pEncCtx->pSvcParam->iEntropyCodingModeFlag != 0);

        return ENC_RETURN_SUCCESS;
    }

    // UpdateMbNeighbourInfoForNextSlice, AddSliceBoundary and DynSlcJudgeSliceBoundaryStepBack
    // (SM_SIZELIMITED_SLICE) come with Phase 4, and WelsInitInterMDStruc, WelsMdInterMbLoop and
    // WelsMdInterMbLoopOverDynamicSlice with E5 and Phase 4.
}
