//-----------------------------------------------------------------------------
// Filename: decoder_core.cs
//
// Description: Port of codec/decoder/core/inc/decoder_core.h and
// src/decoder_core.cpp: the decoder framework core.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// Filled in as the decoder steps need it. So far (D1): slice header parsing
// (pred weight table, ref pic list reordering, dec ref pic marking, SVC
// extension), the bitstream buffers, and the access unit list management that
// ParseNalHeader and the AU start/end path use. Upstream defines LONG_TERM_REF
// for the decoder, so UpdateAccessUnit uses bParamSetsLostFlag. D2 adds the DQ
// layer memory (InitialDqLayersContext, UninitialDqLayersContext) and the
// per-slice layer setup DecodeCurrentAccessUnit does before WelsDecodeSlice
// (InitDqLayerInfo, WelsDqLayerDecodeStart, InitCurDqLayerData);
// DecodeCurrentAccessUnit itself comes with reconstruction.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 25 Nov 2026  Claude          Created.
// 25 Nov 2026  Claude          D2: DQ layer memory and per-slice layer setup.
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
 *      decoder_core.c: Wels decoder framework core implementation
 */

namespace SIPSorcery.H264.Decoder;

internal static unsafe partial class WelsDec
{
    public static int32_t ParsePredWeightedTable(SBitStringAux* pBs, SSliceHeader* pSh)
    {
        uint32_t uiRetTmp;
        uint32_t uiCode;
        int32_t iList = 0;
        int32_t iCode;

        if ((uiRetTmp = (uint32_t)BsGetUe(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp;
        if (WELS_CHECK_SE_BOTH_ERROR_NOLOG(uiCode, 0, 7)) // "luma_log2_weight_denom"
            return GENERATE_ERROR_NO(ERR_LEVEL_SLICE_HEADER, ERR_INFO_INVALID_LUMA_LOG2_WEIGHT_DENOM);
        pSh->sPredWeightTable.uiLumaLog2WeightDenom = uiCode;
        if (pSh->pSps->uiChromaArrayType != 0)
        {
            if ((uiRetTmp = (uint32_t)BsGetUe(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp;
            if (WELS_CHECK_SE_BOTH_ERROR_NOLOG(uiCode, 0, 7)) // "chroma_log2_weight_denom"
                return GENERATE_ERROR_NO(ERR_LEVEL_SLICE_HEADER, ERR_INFO_INVALID_CHROMA_LOG2_WEIGHT_DENOM);
            pSh->sPredWeightTable.uiChromaLog2WeightDenom = uiCode;
        }

        if ((pSh->sPredWeightTable.uiLumaLog2WeightDenom | pSh->sPredWeightTable.uiChromaLog2WeightDenom) > 7)
            return ERR_NONE;

        do
        {

            for (int i = 0; i < pSh->uiRefCount[iList]; i++)
            {
                //luma
                if ((uiRetTmp = (uint32_t)BsGetOneBit(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp;
                if (uiCode != 0)
                {

                    if ((uiRetTmp = (uint32_t)BsGetSe(pBs, &iCode)) != ERR_NONE) return (int32_t)uiRetTmp;
                    if (WELS_CHECK_SE_BOTH_ERROR_NOLOG(iCode, -128, 127)) // "luma_weight"
                        return GENERATE_ERROR_NO(ERR_LEVEL_SLICE_HEADER, ERR_INFO_INVALID_LUMA_WEIGHT);
                    pSh->sPredWeightTable.sPredList[iList].iLumaWeight[i] = iCode;

                    if ((uiRetTmp = (uint32_t)BsGetSe(pBs, &iCode)) != ERR_NONE) return (int32_t)uiRetTmp;
                    if (WELS_CHECK_SE_BOTH_ERROR_NOLOG(iCode, -128, 127)) // "luma_offset"
                        return GENERATE_ERROR_NO(ERR_LEVEL_SLICE_HEADER, ERR_INFO_INVALID_LUMA_OFFSET);
                    pSh->sPredWeightTable.sPredList[iList].iLumaOffset[i] = iCode;
                }
                else
                {
                    pSh->sPredWeightTable.sPredList[iList].iLumaWeight[i] = 1 << (int32_t)(pSh->sPredWeightTable.uiLumaLog2WeightDenom);
                    pSh->sPredWeightTable.sPredList[iList].iLumaOffset[i] = 0;

                }
                //chroma
                if (pSh->pSps->uiChromaArrayType == 0)
                    continue;

                if ((uiRetTmp = (uint32_t)BsGetOneBit(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp;
                if (uiCode != 0)
                {
                    for (int j = 0; j < 2; j++)
                    {


                        if ((uiRetTmp = (uint32_t)BsGetSe(pBs, &iCode)) != ERR_NONE) return (int32_t)uiRetTmp;
                        if (WELS_CHECK_SE_BOTH_ERROR_NOLOG(iCode, -128, 127)) // "chroma_weight"
                            return GENERATE_ERROR_NO(ERR_LEVEL_SLICE_HEADER, ERR_INFO_INVALID_CHROMA_WEIGHT);
                        pSh->sPredWeightTable.sPredList[iList].iChromaWeight[i][j] = iCode;

                        if ((uiRetTmp = (uint32_t)BsGetSe(pBs, &iCode)) != ERR_NONE) return (int32_t)uiRetTmp;
                        if (WELS_CHECK_SE_BOTH_ERROR_NOLOG(iCode, -128, 127)) // "chroma_offset"
                            return GENERATE_ERROR_NO(ERR_LEVEL_SLICE_HEADER, ERR_INFO_INVALID_CHROMA_OFFSET);
                        pSh->sPredWeightTable.sPredList[iList].iChromaOffset[i][j] = iCode;
                    }
                }
                else
                {
                    for (int j = 0; j < 2; j++)
                    {


                        pSh->sPredWeightTable.sPredList[iList].iChromaWeight[i][j] = 1 << (int32_t)(pSh->sPredWeightTable.uiChromaLog2WeightDenom);
                        pSh->sPredWeightTable.sPredList[iList].iChromaOffset[i][j] = 0;
                    }
                }

            }
            ++iList;
            if (pSh->eSliceType != B_SLICE)
            {
                break;
            }
        } while (iList < (int32_t)LIST_A);//TODO: SUPPORT LIST_A
        return ERR_NONE;
    }

    /*
     *  Predeclared function routines ..
     */
    public static int32_t ParseRefPicListReordering(SBitStringAux* pBs, SSliceHeader* pSh)
    {
        uint32_t uiRetTmp;
        int32_t iList = 0;
        EWelsSliceType keSt = pSh->eSliceType;
        SRefPicListReorderSyn* pRefPicListReordering = &pSh->pRefPicListReordering;
        SSps* pSps = pSh->pSps;
        uint32_t uiCode;
        if (keSt == I_SLICE || keSt == SI_SLICE)
            return ERR_NONE;

        // Common syntaxs for P or B slices: list0, list1 followed if B slices used.
        do
        {
            if ((uiRetTmp = (uint32_t)BsGetOneBit(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //ref_pic_list_modification_flag_l0
            pRefPicListReordering->bRefPicListReorderingFlag[iList] = uiCode != 0;

            if (pRefPicListReordering->bRefPicListReorderingFlag[iList])
            {
                int32_t iIdx = 0;
                do
                {
                    if ((uiRetTmp = (uint32_t)BsGetUe(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //modification_of_pic_nums_idc
                    uint32_t kuiIdc = uiCode;

                    //Fixed the referrence list reordering crash issue.(fault kIdc value > 3 case)---
                    if (((iIdx >= MAX_REF_PIC_COUNT) && (kuiIdc != 3)) || (kuiIdc > 3))
                    {
                        return GENERATE_ERROR_NO(ERR_LEVEL_SLICE_HEADER, ERR_INFO_INVALID_REF_REORDERING);
                    }
                    pRefPicListReordering->sReorderingSyn[iList][iIdx].uiReorderingOfPicNumsIdc = (uint16_t)kuiIdc;
                    if (kuiIdc == 3)
                        break;

                    if (iIdx >= pSh->uiRefCount[iList] || iIdx >= MAX_REF_PIC_COUNT)
                        return GENERATE_ERROR_NO(ERR_LEVEL_SLICE_HEADER, ERR_INFO_INVALID_REF_REORDERING);

                    if (kuiIdc == 0 || kuiIdc == 1)
                    {
                        // abs_diff_pic_num_minus1 should be in range 0 to MaxPicNum-1, MaxPicNum is derived as
                        // 2^(4+log2_max_frame_num_minus4)
                        if ((uiRetTmp = (uint32_t)BsGetUe(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //abs_diff_pic_num_minus1
                        if (WELS_CHECK_SE_UPPER_ERROR_NOLOG(uiCode, (uint32_t)(1 << (int32_t)pSps->uiLog2MaxFrameNum))) // "abs_diff_pic_num_minus1"
                            return GENERATE_ERROR_NO(ERR_LEVEL_SLICE_HEADER, ERR_INFO_INVALID_REF_REORDERING);
                        pRefPicListReordering->sReorderingSyn[iList][iIdx].uiAbsDiffPicNumMinus1 = uiCode; // uiAbsDiffPicNumMinus1
                    }
                    else if (kuiIdc == 2)
                    {
                        if ((uiRetTmp = (uint32_t)BsGetUe(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //long_term_pic_num
                        pRefPicListReordering->sReorderingSyn[iList][iIdx].uiLongTermPicNum = (uint16_t)uiCode;
                    }

                    ++iIdx;
                } while (true);
            }
            if (keSt != B_SLICE)
                break;
            ++iList;
        } while (iList < (int32_t)LIST_A);

        return ERR_NONE;
    }

    public static int32_t ParseDecRefPicMarking(SWelsDecoderContext* pCtx, SBitStringAux* pBs, SSliceHeader* pSh, SSps* pSps,
                                                bool kbIdrFlag)
    {
        uint32_t uiRetTmp;
        SRefPicMarking* kpRefMarking = &pSh->sRefMarking;
        uint32_t uiCode;
        if (kbIdrFlag)
        {
            if ((uiRetTmp = (uint32_t)BsGetOneBit(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //no_output_of_prior_pics_flag
            kpRefMarking->bNoOutputOfPriorPicsFlag = uiCode != 0;
            if ((uiRetTmp = (uint32_t)BsGetOneBit(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //long_term_reference_flag
            kpRefMarking->bLongTermRefFlag = uiCode != 0;
        }
        else
        {
            if ((uiRetTmp = (uint32_t)BsGetOneBit(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //adaptive_ref_pic_marking_mode_flag
            kpRefMarking->bAdaptiveRefPicMarkingModeFlag = uiCode != 0;
            if (kpRefMarking->bAdaptiveRefPicMarkingModeFlag)
            {
                int32_t iIdx = 0;
                bool bAllowMmco5 = true, bMmco4Exist = false, bMmco5Exist = false, bMmco6Exist = false;
                do
                {
                    if ((uiRetTmp = (uint32_t)BsGetUe(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //memory_management_control_operation
                    uint32_t kuiMmco = uiCode;

                    kpRefMarking->sMmcoRef[iIdx].uiMmcoType = kuiMmco;
                    if (kuiMmco == (uint32_t)MMCO_END)
                        break;

                    if (kuiMmco == (uint32_t)MMCO_SHORT2UNUSED || kuiMmco == (uint32_t)MMCO_SHORT2LONG)
                    {
                        bAllowMmco5 = false;
                        if ((uiRetTmp = (uint32_t)BsGetUe(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //difference_of_pic_nums_minus1
                        kpRefMarking->sMmcoRef[iIdx].iDiffOfPicNum = (int32_t)(1 + uiCode);
                        kpRefMarking->sMmcoRef[iIdx].iShortFrameNum = (pSh->iFrameNum - kpRefMarking->sMmcoRef[iIdx].iDiffOfPicNum) & ((
                              1 << (int32_t)pSps->uiLog2MaxFrameNum) - 1);
                    }
                    else if (kuiMmco == (uint32_t)MMCO_LONG2UNUSED)
                    {
                        bAllowMmco5 = false;
                        if ((uiRetTmp = (uint32_t)BsGetUe(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //long_term_pic_num
                        kpRefMarking->sMmcoRef[iIdx].uiLongTermPicNum = uiCode;
                    }
                    if (kuiMmco == (uint32_t)MMCO_SHORT2LONG || kuiMmco == (uint32_t)MMCO_LONG)
                    {
                        if (kuiMmco == (uint32_t)MMCO_LONG)
                        {
                            if (bMmco6Exist) return -1; // WELS_VERIFY_RETURN_IF
                            bMmco6Exist = true;
                        }
                        if ((uiRetTmp = (uint32_t)BsGetUe(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //long_term_frame_idx
                        kpRefMarking->sMmcoRef[iIdx].iLongTermFrameIdx = (int32_t)uiCode;
                    }
                    else if (kuiMmco == (uint32_t)MMCO_SET_MAX_LONG)
                    {
                        if (bMmco4Exist) return -1; // WELS_VERIFY_RETURN_IF
                        bMmco4Exist = true;
                        if ((uiRetTmp = (uint32_t)BsGetUe(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //max_long_term_frame_idx_plus1
                        int32_t iMaxLongTermFrameIdx = (int32_t)(uiCode - 1u);
                        if (iMaxLongTermFrameIdx > pSps->iNumRefFrames)
                        {
                            //ISO/IEC 14496-10:2009(E) 7.4.3.3 Decoded reference picture marking semantics page 96
                            return GENERATE_ERROR_NO(ERR_LEVEL_SLICE_HEADER, ERR_INFO_INVALID_REF_MARKING);
                        }
                        kpRefMarking->sMmcoRef[iIdx].iMaxLongTermFrameIdx = iMaxLongTermFrameIdx;
                    }
                    else if (kuiMmco == (uint32_t)MMCO_RESET)
                    {
                        if (!bAllowMmco5 || bMmco5Exist) return -1; // WELS_VERIFY_RETURN_IF
                        bMmco5Exist = true;

                        pCtx->pLastDecPicInfo->iPrevPicOrderCntLsb = 0;
                        pCtx->pLastDecPicInfo->iPrevPicOrderCntMsb = 0;
                        pSh->iPicOrderCntLsb = 0;
                        if (pCtx->pSliceHeader != null)
                            pCtx->pSliceHeader->iPicOrderCntLsb = 0;
                    }
                    ++iIdx;

                } while (iIdx < MAX_MMCO_COUNT);
            }
        }

        return ERR_NONE;
    }

    public static bool FillDefaultSliceHeaderExt(SSliceHeaderExt* pShExt, SNalUnitHeaderExt* pNalExt)
    {
        if (pShExt == null || pNalExt == null)
            return false;

        if (pNalExt->iNoInterLayerPredFlag != 0 || pNalExt->uiQualityId > 0)
            pShExt->bBasePredWeightTableFlag = false;
        else
            pShExt->bBasePredWeightTableFlag = true;
        pShExt->uiRefLayerDqId = unchecked((uint8_t)(-1));
        pShExt->uiDisableInterLayerDeblockingFilterIdc = 0;
        pShExt->iInterLayerSliceAlphaC0Offset = 0;
        pShExt->iInterLayerSliceBetaOffset = 0;
        pShExt->bConstrainedIntraResamplingFlag = false;
        pShExt->uiRefLayerChromaPhaseXPlus1Flag = 0;
        pShExt->uiRefLayerChromaPhaseYPlus1 = 1;
        //memset(&pShExt->sScaledRefLayer, 0, sizeof(SPosOffset));

        pShExt->iScaledRefLayerPicWidthInSampleLuma = pShExt->sSliceHeader.iMbWidth << 4;
        pShExt->iScaledRefLayerPicHeightInSampleLuma = pShExt->sSliceHeader.iMbHeight << 4;

        pShExt->bSliceSkipFlag = false;
        pShExt->bAdaptiveBaseModeFlag = false;
        pShExt->bDefaultBaseModeFlag = false;
        pShExt->bAdaptiveMotionPredFlag = false;
        pShExt->bDefaultMotionPredFlag = false;
        pShExt->bAdaptiveResidualPredFlag = false;
        pShExt->bDefaultResidualPredFlag = false;
        pShExt->bTCoeffLevelPredFlag = false;
        pShExt->uiScanIdxStart = 0;
        pShExt->uiScanIdxEnd = 15;

        return true;
    }

    public static int32_t InitBsBuffer(SWelsDecoderContext* pCtx)
    {
        if (pCtx == null)
            return ERR_INFO_INVALID_PTR;

        CMemoryAlign* pMa = pCtx->pMemAlign;

        pCtx->iMaxBsBufferSizeInByte = MIN_ACCESS_UNIT_CAPACITY * MAX_BUFFERED_NUM;
        if ((pCtx->sRawData.pHead = (uint8_t*)(pMa->WelsMallocz((uint32_t)pCtx->iMaxBsBufferSizeInByte,
                                    "pCtx->sRawData.pHead"))) == null)
        {
            return ERR_INFO_OUT_OF_MEMORY;
        }
        pCtx->sRawData.pStartPos = pCtx->sRawData.pCurPos = pCtx->sRawData.pHead;
        pCtx->sRawData.pEnd = pCtx->sRawData.pHead + pCtx->iMaxBsBufferSizeInByte;
        if (pCtx->pParam->bParseOnly)
        {
            pCtx->pParserBsInfo = (SParserBsInfo*)(pMa->WelsMallocz((uint32_t)sizeof(SParserBsInfo), "pCtx->pParserBsInfo"));
            if (pCtx->pParserBsInfo == null)
            {
                return ERR_INFO_OUT_OF_MEMORY;
            }
            memset(pCtx->pParserBsInfo, 0, sizeof(SParserBsInfo));
            pCtx->pParserBsInfo->pDstBuff = (uint8_t*)(pMa->WelsMallocz(MAX_ACCESS_UNIT_CAPACITY * sizeof(uint8_t),
                                            "pCtx->pParserBsInfo->pDstBuff"));
            if (pCtx->pParserBsInfo->pDstBuff == null)
            {
                return ERR_INFO_OUT_OF_MEMORY;
            }
            memset(pCtx->pParserBsInfo->pDstBuff, 0, MAX_ACCESS_UNIT_CAPACITY * sizeof(uint8_t));

            if ((pCtx->sSavedData.pHead = (uint8_t*)(pMa->WelsMallocz((uint32_t)pCtx->iMaxBsBufferSizeInByte,
                                          "pCtx->sSavedData.pHead"))) == null)
            {
                return ERR_INFO_OUT_OF_MEMORY;
            }
            pCtx->sSavedData.pStartPos = pCtx->sSavedData.pCurPos = pCtx->sSavedData.pHead;
            pCtx->sSavedData.pEnd = pCtx->sSavedData.pHead + pCtx->iMaxBsBufferSizeInByte;

            pCtx->iMaxNalNum = MAX_NAL_UNITS_IN_LAYER + 2; //2 reserved for SPS+PPS
            pCtx->pParserBsInfo->pNalLenInByte = (int*)(pMa->WelsMallocz((uint32_t)(pCtx->iMaxNalNum * sizeof(int)),
                                                 "pCtx->pParserBsInfo->pNalLenInByte"));
            if (pCtx->pParserBsInfo->pNalLenInByte == null)
            {
                return ERR_INFO_OUT_OF_MEMORY;
            }
        }
        return ERR_NONE;
    }

    public static int32_t ExpandBsBuffer(SWelsDecoderContext* pCtx, int kiSrcLen)
    {
        if (pCtx == null)
            return ERR_INFO_INVALID_PTR;
        int32_t iExpandStepShift = 1;
        int32_t iNewBuffLen = WELS_MAX((kiSrcLen * MAX_BUFFERED_NUM), (pCtx->iMaxBsBufferSizeInByte << iExpandStepShift));
        //allocate new bs buffer
        CMemoryAlign* pMa = pCtx->pMemAlign;

        //Realloc sRawData
        uint8_t* pNewBsBuff = (uint8_t*)(pMa->WelsMallocz((uint32_t)iNewBuffLen, "pCtx->sRawData.pHead"));
        if (pNewBsBuff == null)
        {
            WelsLog(&(pCtx->sLogCtx), WELS_LOG_ERROR, $"ExpandBsBuffer() Failed for malloc pNewBsBuff ({iNewBuffLen})");
            pCtx->iErrorCode |= (int32_t)dsOutOfMemory;
            return ERR_INFO_OUT_OF_MEMORY;
        }

        // Retarget all queued NAL units in current AU list. uiAvailUnitsNum is the
        // queued-NAL count that can be consumed later; using uiActualUnitsNum here
        // can leave queued entries pointing to freed old buffer.
        uint32_t kuiRetargetNum = pCtx->pAccessUnitList->uiAvailUnitsNum;
        for (uint32_t i = 0; i < kuiRetargetNum; ++i)
        {
            SNalUnit* pNal = pCtx->pAccessUnitList->pNalUnitsList[i];
            if (pNal == null)
            {
                continue;
            }
            SBitStringAux* pSliceBitsRead = &pNal->sNalData.sVclNal.sSliceBitsRead;
            pSliceBitsRead->pStartBuf = pSliceBitsRead->pStartBuf - pCtx->sRawData.pHead + pNewBsBuff;
            pSliceBitsRead->pEndBuf = pSliceBitsRead->pEndBuf - pCtx->sRawData.pHead + pNewBsBuff;
            pSliceBitsRead->pCurBuf = pSliceBitsRead->pCurBuf - pCtx->sRawData.pHead + pNewBsBuff;
        }

        //Copy current buffer status to new buffer
        memcpy(pNewBsBuff, pCtx->sRawData.pHead, pCtx->iMaxBsBufferSizeInByte);
        pCtx->sRawData.pStartPos = pNewBsBuff + (pCtx->sRawData.pStartPos - pCtx->sRawData.pHead);
        pCtx->sRawData.pCurPos = pNewBsBuff + (pCtx->sRawData.pCurPos - pCtx->sRawData.pHead);
        pCtx->sRawData.pEnd = pNewBsBuff + iNewBuffLen;
        pMa->WelsFree(pCtx->sRawData.pHead, "pCtx->sRawData.pHead");
        pCtx->sRawData.pHead = pNewBsBuff;

        if (pCtx->pParam->bParseOnly)
        {
            //Realloc sSavedData
            uint8_t* pOldSavedBsBuff = pCtx->sSavedData.pHead;
            uint8_t* pNewSavedBsBuff = (uint8_t*)(pMa->WelsMallocz((uint32_t)iNewBuffLen, "pCtx->sSavedData.pHead"));
            if (pNewSavedBsBuff == null)
            {
                WelsLog(&(pCtx->sLogCtx), WELS_LOG_ERROR, $"ExpandBsBuffer() Failed for malloc pNewSavedBsBuff ({iNewBuffLen})");
                pCtx->iErrorCode |= (int32_t)dsOutOfMemory;
                return ERR_INFO_OUT_OF_MEMORY;
            }

            //Copy current buffer status to new buffer
            memcpy(pNewSavedBsBuff, pCtx->sSavedData.pHead, pCtx->iMaxBsBufferSizeInByte);
            pCtx->sSavedData.pStartPos = pNewSavedBsBuff + (pCtx->sSavedData.pStartPos - pCtx->sSavedData.pHead);
            pCtx->sSavedData.pCurPos = pNewSavedBsBuff + (pCtx->sSavedData.pCurPos - pCtx->sSavedData.pHead);
            pCtx->sSavedData.pEnd = pNewSavedBsBuff + iNewBuffLen;

            // Retarget pNalPos for all queued NALs (current AU + next AU) before freeing.
            uintptr_t kuiOldStart = (uintptr_t)pOldSavedBsBuff;
            uintptr_t kuiOldEnd = kuiOldStart + (uintptr_t)pCtx->iMaxBsBufferSizeInByte;
            for (uint32_t i = 0; i < kuiRetargetNum; ++i)
            {
                SNalUnit* pNal = pCtx->pAccessUnitList->pNalUnitsList[i];
                if (pNal == null || pNal->sNalData.sVclNal.pNalPos == null)
                {
                    continue;
                }
                uintptr_t kuiNalPos = (uintptr_t)pNal->sNalData.sVclNal.pNalPos;
                if (kuiNalPos >= kuiOldStart && kuiNalPos < kuiOldEnd)
                {
                    pNal->sNalData.sVclNal.pNalPos = pNewSavedBsBuff + (kuiNalPos - kuiOldStart);
                }
            }

            pMa->WelsFree(pCtx->sSavedData.pHead, "pCtx->sSavedData.pHead");
            pCtx->sSavedData.pHead = pNewSavedBsBuff;
        }

        pCtx->iMaxBsBufferSizeInByte = iNewBuffLen;
        return ERR_NONE;
    }

    public static int32_t ExpandBsLenBuffer(SWelsDecoderContext* pCtx, int kiCurrLen)
    {
        SParserBsInfo* pParser = pCtx->pParserBsInfo;
        if (pParser->pNalLenInByte == null)
            return ERR_INFO_INVALID_ACCESS;

        int iNewLen = kiCurrLen;
        if (kiCurrLen >= MAX_MB_SIZE + 2)
        { //exceeds the max MB number of level 5.2
            WelsLog(&(pCtx->sLogCtx), WELS_LOG_WARNING, $"Current nal num ({kiCurrLen}) exceededs {MAX_MB_SIZE}.");
            pCtx->iErrorCode |= (int32_t)dsOutOfMemory;
            return ERR_INFO_OUT_OF_MEMORY;
        }
        else
        {
            iNewLen = kiCurrLen << 1;
            iNewLen = WELS_MIN(iNewLen, MAX_MB_SIZE + 2);
        }

        CMemoryAlign* pMa = pCtx->pMemAlign;
        int* pNewLenBuffer = (int*)(pMa->WelsMallocz((uint32_t)(iNewLen * sizeof(int)),
                                    "pCtx->pParserBsInfo->pNalLenInByte"));
        if (pNewLenBuffer == null)
        {
            pCtx->iErrorCode |= (int32_t)dsOutOfMemory;
            return ERR_INFO_OUT_OF_MEMORY;
        }

        //copy existing data from old length buffer to new
        memcpy(pNewLenBuffer, pParser->pNalLenInByte, pCtx->iMaxNalNum * sizeof(int));
        pMa->WelsFree(pParser->pNalLenInByte, "pCtx->pParserBsInfo->pNalLenInByte");
        pParser->pNalLenInByte = pNewLenBuffer;
        pCtx->iMaxNalNum = iNewLen;
        return ERR_NONE;
    }

    public static int32_t CheckBsBuffer(SWelsDecoderContext* pCtx, int32_t kiSrcLen)
    {
        if (kiSrcLen > MAX_ACCESS_UNIT_CAPACITY)
        { //exceeds max allowed data
            WelsLog(&(pCtx->sLogCtx), WELS_LOG_WARNING, $"Max AU size exceeded. Allowed size = {MAX_ACCESS_UNIT_CAPACITY}, current size = {kiSrcLen}");
            pCtx->iErrorCode |= (int32_t)dsBitstreamError;
            return ERR_INFO_INVALID_ACCESS;
        }
        else if (kiSrcLen > pCtx->iMaxBsBufferSizeInByte /
                 MAX_BUFFERED_NUM)
        { //may lead to buffer overwrite, prevent it by expanding buffer
            if (ExpandBsBuffer(pCtx, kiSrcLen) != 0)
            {
                return ERR_INFO_OUT_OF_MEMORY;
            }
        }

        return ERR_NONE;
    }

    /*
     * WelsInitStaticMemory
     * Memory request for new introduced data
     * Especially for:
     * rbsp_au_buffer, cur_dq_layer_ptr and ref_dq_layer_ptr in MB info cache.
     * return:
     *  0 - success; otherwise returned error_no defined in error_no.h.
    */
    public static int32_t WelsInitStaticMemory(SWelsDecoderContext* pCtx)
    {
        if (pCtx == null)
        {
            return ERR_INFO_INVALID_PTR;
        }

        if (MemInitNalList(&pCtx->pAccessUnitList, MAX_NAL_UNIT_NUM_IN_AU, pCtx->pMemAlign) != 0)
            return ERR_INFO_OUT_OF_MEMORY;

        if (InitBsBuffer(pCtx) != 0)
            return ERR_INFO_OUT_OF_MEMORY;

        pCtx->uiTargetDqId = unchecked((uint8_t)(-1));
        pCtx->bEndOfStreamFlag = false;

        return ERR_NONE;
    }

    /*
     * WelsFreeStaticMemory
     * Free memory introduced in WelsInitStaticMemory at destruction of decoder.
     *
     */
    public static void WelsFreeStaticMemory(SWelsDecoderContext* pCtx)
    {
        if (pCtx == null)
            return;

        CMemoryAlign* pMa = pCtx->pMemAlign;

        MemFreeNalList(&pCtx->pAccessUnitList, pMa);

        if (pCtx->sRawData.pHead != null)
        {
            pMa->WelsFree(pCtx->sRawData.pHead, "pCtx->sRawData->pHead");
        }
        pCtx->sRawData.pHead = null;
        pCtx->sRawData.pEnd = null;
        pCtx->sRawData.pStartPos = null;
        pCtx->sRawData.pCurPos = null;
        if (pCtx->pParam->bParseOnly)
        {
            if (pCtx->sSavedData.pHead != null)
            {
                pMa->WelsFree(pCtx->sSavedData.pHead, "pCtx->sSavedData->pHead");
            }
            pCtx->sSavedData.pHead = null;
            pCtx->sSavedData.pEnd = null;
            pCtx->sSavedData.pStartPos = null;
            pCtx->sSavedData.pCurPos = null;
            if (pCtx->pParserBsInfo != null)
            {
                if (pCtx->pParserBsInfo->pNalLenInByte != null)
                {
                    pMa->WelsFree(pCtx->pParserBsInfo->pNalLenInByte, "pCtx->pParserBsInfo->pNalLenInByte");
                    pCtx->pParserBsInfo->pNalLenInByte = null;
                    pCtx->iMaxNalNum = 0;
                }
                if (pCtx->pParserBsInfo->pDstBuff != null)
                {
                    pMa->WelsFree(pCtx->pParserBsInfo->pDstBuff, "pCtx->pParserBsInfo->pDstBuff");
                    pCtx->pParserBsInfo->pDstBuff = null;
                }
                pMa->WelsFree(pCtx->pParserBsInfo, "pCtx->pParserBsInfo");
                pCtx->pParserBsInfo = null;
            }
        }

        if (null != pCtx->pParam)
        {
            pMa->WelsFree(pCtx->pParam, "pCtx->pParam");

            pCtx->pParam = null;
        }
    }
    /*
     *  DecodeNalHeaderExt
     *  Trigger condition: NAL_UNIT_TYPE = NAL_UNIT_PREFIX or NAL_UNIT_CODED_SLICE_EXT
     *  Parameter:
     *  pNal:   target NALUnit ptr
     *  pSrc:   NAL Unit bitstream
     */
    public static void DecodeNalHeaderExt(SNalUnit* pNal, uint8_t* pSrc)
    {
        SNalUnitHeaderExt* pHeaderExt = &pNal->sNalHeaderExt;

        uint8_t uiCurByte = *pSrc;
        pHeaderExt->bIdrFlag = (uiCurByte & 0x40) != 0;
        pHeaderExt->uiPriorityId = (uint8_t)(uiCurByte & 0x3F);

        uiCurByte = *(++pSrc);
        pHeaderExt->iNoInterLayerPredFlag = (int8_t)(uiCurByte >> 7);
        pHeaderExt->uiDependencyId = (uint8_t)((uiCurByte & 0x70) >> 4);
        pHeaderExt->uiQualityId = (uint8_t)(uiCurByte & 0x0F);
        uiCurByte = *(++pSrc);
        pHeaderExt->uiTemporalId = (uint8_t)(uiCurByte >> 5);
        pHeaderExt->bUseRefBasePicFlag = (uiCurByte & 0x10) != 0;
        pHeaderExt->bDiscardableFlag = (uiCurByte & 0x08) != 0;
        pHeaderExt->bOutputFlag = (uiCurByte & 0x04) != 0;
        pHeaderExt->uiReservedThree2Bits = (uint8_t)(uiCurByte & 0x03);
        pHeaderExt->uiLayerDqId = (uint8_t)((pHeaderExt->uiDependencyId << 4) | pHeaderExt->uiQualityId);
    }


    public static void UpdateDecoderStatisticsForActiveParaset(SDecoderStatistics* pDecoderStatistics,
                                                               SSps* pSps, SPps* pPps)
    {
        pDecoderStatistics->iCurrentActiveSpsId = pSps->iSpsId;

        pDecoderStatistics->iCurrentActivePpsId = pPps->iPpsId;
        pDecoderStatistics->uiProfile = (uint)(pSps->uiProfileIdc);
        pDecoderStatistics->uiLevel = pSps->uiLevelIdc;
    }

    public const int SLICE_HEADER_IDR_PIC_ID_MAX = 65535;
    public const int SLICE_HEADER_REDUNDANT_PIC_CNT_MAX = 127;
    public const int SLICE_HEADER_ALPHAC0_BETA_OFFSET_MIN = -12;
    public const int SLICE_HEADER_ALPHAC0_BETA_OFFSET_MAX = 12;
    public const int SLICE_HEADER_INTER_LAYER_ALPHAC0_BETA_OFFSET_MIN = -12;
    public const int SLICE_HEADER_INTER_LAYER_ALPHAC0_BETA_OFFSET_MAX = 12;
    public const int MAX_NUM_REF_IDX_L0_ACTIVE_MINUS1 = 15;
    public const int MAX_NUM_REF_IDX_L1_ACTIVE_MINUS1 = 15;
    public const int SLICE_HEADER_CABAC_INIT_IDC_MAX = 2;
    /*
     *  decode_slice_header_avc
     *  Parse slice header of bitstream in avc for storing data structure
     */
    public static int32_t ParseSliceHeaderSyntaxs(SWelsDecoderContext* pCtx, SBitStringAux* pBs, bool kbExtensionFlag)
    {
        uint32_t uiRetTmp;
        SNalUnit* kpCurNal =
          pCtx->pAccessUnitList->pNalUnitsList[pCtx->pAccessUnitList->uiAvailUnitsNum -
                                                                                      1];

        SNalUnitHeaderExt* pNalHeaderExt = null;
        SSliceHeader* pSliceHead = null;
        SSliceHeaderExt* pSliceHeadExt = null;
        SSubsetSps* pSubsetSps = null;
        SSps* pSps = null;
        SPps* pPps = null;
        EWelsNalUnitType eNalType = (EWelsNalUnitType)(0);
        int32_t iPpsId = 0;
        int32_t iRet = ERR_NONE;
        uint8_t uiSliceType = 0;
        uint8_t uiQualityId = BASE_QUALITY_ID;
        bool bIdrFlag = false;
        bool bSgChangeCycleInvolved = false;        // involved slice group change cycle ?
        uint32_t uiCode;
        int32_t iCode;
        SLogContext* pLogCtx = &(pCtx->sLogCtx);

        if (kpCurNal == null)
        {
            return ERR_INFO_OUT_OF_MEMORY;
        }

        pNalHeaderExt = &kpCurNal->sNalHeaderExt;
        pSliceHead = &kpCurNal->sNalData.sVclNal.sSliceHeaderExt.sSliceHeader;
        eNalType = pNalHeaderExt->sNalUnitHeader.eNalUnitType;

        pSliceHeadExt = &kpCurNal->sNalData.sVclNal.sSliceHeaderExt;

        if (pSliceHeadExt != null)
        {
            SRefBasePicMarking sBaseMarking;
            bool kbStoreRefBaseFlag = pSliceHeadExt->bStoreRefBasePicFlag;
            memcpy(&sBaseMarking, &pSliceHeadExt->sRefBasePicMarking, sizeof(SRefBasePicMarking)); //confirmed_safe_unsafe_usage
            memset(pSliceHeadExt, 0, sizeof(SSliceHeaderExt));
            pSliceHeadExt->bStoreRefBasePicFlag = kbStoreRefBaseFlag;
            memcpy(&pSliceHeadExt->sRefBasePicMarking, &sBaseMarking, sizeof(SRefBasePicMarking)); //confirmed_safe_unsafe_usage
        }

        kpCurNal->sNalData.sVclNal.bSliceHeaderExtFlag = kbExtensionFlag;

        // first_mb_in_slice
        if ((uiRetTmp = (uint32_t)BsGetUe(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //first_mb_in_slice
        if (WELS_CHECK_SE_UPPER_ERROR(pCtx, uiCode, 36863u, "first_mb_in_slice"))
            return GENERATE_ERROR_NO(ERR_LEVEL_SLICE_HEADER, ERR_INFO_INVALID_FIRST_MB_IN_SLICE);
        pSliceHead->iFirstMbInSlice = (int32_t)uiCode;

        if ((uiRetTmp = (uint32_t)BsGetUe(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //slice_type
        uiSliceType = (uint8_t)uiCode;
        if (uiSliceType > 9)
        {
            WelsLog(pLogCtx, WELS_LOG_WARNING, $"slice type too large ({uiSliceType}) at first_mb({pSliceHead->iFirstMbInSlice})");
            return GENERATE_ERROR_NO(ERR_LEVEL_SLICE_HEADER, ERR_INFO_INVALID_SLICE_TYPE);
        }
        if (uiSliceType > 4)
            uiSliceType -= 5;

        if ((NAL_UNIT_CODED_SLICE_IDR == eNalType) && (I_SLICE != (EWelsSliceType)uiSliceType))
        {
            WelsLog(pLogCtx, WELS_LOG_WARNING, $"Invalid slice type({uiSliceType}) in IDR picture. ");
            return GENERATE_ERROR_NO(ERR_LEVEL_SLICE_HEADER, ERR_INFO_INVALID_SLICE_TYPE);
        }

        if (kbExtensionFlag)
        {
            if (uiSliceType > 2)
            {
                WelsLog(pLogCtx, WELS_LOG_WARNING, $"Invalid slice type({uiSliceType}).");
                return GENERATE_ERROR_NO(ERR_LEVEL_SLICE_HEADER, ERR_INFO_INVALID_SLICE_TYPE);
            }
        }

        pSliceHead->eSliceType = (EWelsSliceType)(uiSliceType);

        if ((uiRetTmp = (uint32_t)BsGetUe(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //pic_parameter_set_id
        if (WELS_CHECK_SE_UPPER_ERROR(pCtx, uiCode, (MAX_PPS_COUNT - 1), "iPpsId out of range"))
            return GENERATE_ERROR_NO(ERR_LEVEL_SLICE_HEADER,
                                     ERR_INFO_PPS_ID_OVERFLOW);
        iPpsId = (int32_t)uiCode;

        //add check PPS available here
        if (pCtx->sSpsPpsCtx.bPpsAvailFlags[iPpsId] == false)
        {
            pCtx->pDecoderStatistics->iPpsReportErrorNum++;
            if (pCtx->sSpsPpsCtx.iPPSLastInvalidId != iPpsId)
            {
                WelsLog(pLogCtx, WELS_LOG_ERROR, $"PPS id ({iPpsId}) is invalid, previous id ({pCtx->sSpsPpsCtx.iPPSLastInvalidId}) error ignored ({pCtx->sSpsPpsCtx.iPPSInvalidNum})!");
                pCtx->sSpsPpsCtx.iPPSLastInvalidId = iPpsId;
                pCtx->sSpsPpsCtx.iPPSInvalidNum = 0;
            }
            else
            {
                pCtx->sSpsPpsCtx.iPPSInvalidNum++;
            }
            pCtx->iErrorCode |= (int32_t)dsNoParamSets;
            return GENERATE_ERROR_NO(ERR_LEVEL_SLICE_HEADER, ERR_INFO_INVALID_PPS_ID);
        }
        pCtx->sSpsPpsCtx.iPPSLastInvalidId = -1;

        pPps = &pCtx->sSpsPpsCtx.sPpsBuffer[iPpsId];

        if (pPps->uiNumSliceGroups == 0)
        {
            WelsLog(pLogCtx, WELS_LOG_WARNING, "Invalid PPS referenced");
            pCtx->iErrorCode |= (int32_t)dsNoParamSets;
            return GENERATE_ERROR_NO(ERR_LEVEL_SLICE_HEADER, ERR_INFO_NO_PARAM_SETS);
        }

        if (kbExtensionFlag)
        {
            pSubsetSps = &pCtx->sSpsPpsCtx.sSubsetSpsBuffer[pPps->iSpsId];
            pSps = &pSubsetSps->sSps;
            if (pCtx->sSpsPpsCtx.bSubspsAvailFlags[pPps->iSpsId] == false)
            {
                pCtx->pDecoderStatistics->iSubSpsReportErrorNum++;
                if (pCtx->sSpsPpsCtx.iSubSPSLastInvalidId != pPps->iSpsId)
                {
                    WelsLog(pLogCtx, WELS_LOG_ERROR, $"Sub SPS id ({pPps->iSpsId}) is invalid, previous id ({pCtx->sSpsPpsCtx.iSubSPSLastInvalidId}) error ignored ({pCtx->sSpsPpsCtx.iSubSPSInvalidNum})!");
                    pCtx->sSpsPpsCtx.iSubSPSLastInvalidId = pPps->iSpsId;
                    pCtx->sSpsPpsCtx.iSubSPSInvalidNum = 0;
                }
                else
                {
                    pCtx->sSpsPpsCtx.iSubSPSInvalidNum++;
                }
                pCtx->iErrorCode |= (int32_t)dsNoParamSets;
                return GENERATE_ERROR_NO(ERR_LEVEL_SLICE_HEADER, ERR_INFO_INVALID_SPS_ID);
            }
            pCtx->sSpsPpsCtx.iSubSPSLastInvalidId = -1;
        }
        else
        {
            if (pCtx->sSpsPpsCtx.bSpsAvailFlags[pPps->iSpsId] == false)
            {
                pCtx->pDecoderStatistics->iSpsReportErrorNum++;
                if (pCtx->sSpsPpsCtx.iSPSLastInvalidId != pPps->iSpsId)
                {
                    WelsLog(pLogCtx, WELS_LOG_ERROR, $"SPS id ({pPps->iSpsId}) is invalid, previous id ({pCtx->sSpsPpsCtx.iSPSLastInvalidId}) error ignored ({pCtx->sSpsPpsCtx.iSPSInvalidNum})!");
                    pCtx->sSpsPpsCtx.iSPSLastInvalidId = pPps->iSpsId;
                    pCtx->sSpsPpsCtx.iSPSInvalidNum = 0;
                }
                else
                {
                    pCtx->sSpsPpsCtx.iSPSInvalidNum++;
                }
                pCtx->iErrorCode |= (int32_t)dsNoParamSets;
                return GENERATE_ERROR_NO(ERR_LEVEL_SLICE_HEADER, ERR_INFO_INVALID_SPS_ID);
            }
            pCtx->sSpsPpsCtx.iSPSLastInvalidId = -1;
            pSps = &pCtx->sSpsPpsCtx.sSpsBuffer[pPps->iSpsId];
        }
        pSliceHead->iPpsId = iPpsId;
        pSliceHead->iSpsId = pPps->iSpsId;
        pSliceHead->pPps = pPps;
        pSliceHead->pSps = pSps;

        pSliceHeadExt->pSubsetSps = pSubsetSps;

        if (pSps->iNumRefFrames == 0)
        {
            if (((EWelsSliceType)uiSliceType != I_SLICE) && ((EWelsSliceType)uiSliceType != SI_SLICE))
            {
                WelsLog(pLogCtx, WELS_LOG_WARNING, $"slice_type ({uiSliceType}) not supported for num_ref_frames = 0.");
                return GENERATE_ERROR_NO(ERR_LEVEL_SLICE_HEADER, ERR_INFO_INVALID_SLICE_TYPE);
            }
        }

        bIdrFlag = (!kbExtensionFlag && eNalType == NAL_UNIT_CODED_SLICE_IDR) || (kbExtensionFlag && pNalHeaderExt->bIdrFlag);
        pSliceHead->bIdrFlag = bIdrFlag;

        if (pSps->uiLog2MaxFrameNum == 0)
        {
            WelsLog(pLogCtx, WELS_LOG_WARNING, "non existing SPS referenced");
            return GENERATE_ERROR_NO(ERR_LEVEL_SLICE_HEADER, ERR_INFO_NO_PARAM_SETS);
        }
        // check first_mb_in_slice
        if (WELS_CHECK_SE_UPPER_ERROR(pCtx, (uint32_t)(pSliceHead->iFirstMbInSlice), (pSps->uiTotalMbCount - 1), "first_mb_in_slice"))
            return GENERATE_ERROR_NO(ERR_LEVEL_SLICE_HEADER, ERR_INFO_INVALID_FIRST_MB_IN_SLICE);
        if ((uiRetTmp = (uint32_t)BsGetBits(pBs, (int32_t)pSps->uiLog2MaxFrameNum, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //frame_num
        pSliceHead->iFrameNum = (int32_t)uiCode;

        pSliceHead->bFieldPicFlag = false;
        pSliceHead->bBottomFiledFlag = false;
        if (!pSps->bFrameMbsOnlyFlag)
        {
            WelsLog(pLogCtx, WELS_LOG_WARNING, $"ParseSliceHeaderSyntaxs(): frame_mbs_only_flag = {(pSps->bFrameMbsOnlyFlag ? 1 : 0)} not supported. ");
            return GENERATE_ERROR_NO(ERR_LEVEL_SLICE_HEADER, ERR_INFO_UNSUPPORTED_MBAFF);
        }
        pSliceHead->iMbWidth = (int32_t)pSps->iMbWidth;
        pSliceHead->iMbHeight = (int32_t)(pSps->iMbHeight / (uint32_t)(1 + (pSliceHead->bFieldPicFlag ? 1 : 0)));

        if (bIdrFlag)
        {
            if (pSliceHead->iFrameNum != 0)
            {
                WelsLog(pLogCtx, WELS_LOG_WARNING,
                        $"ParseSliceHeaderSyntaxs(), invaild frame number: {pSliceHead->iFrameNum} due to IDR frame introduced!");
                return GENERATE_ERROR_NO(ERR_LEVEL_SLICE_HEADER, ERR_INFO_INVALID_FRAME_NUM);
            }
            if ((uiRetTmp = (uint32_t)BsGetUe(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //idr_pic_id
            // standard 7.4.3 idr_pic_id should be in range 0 to 65535, inclusive.
            if (WELS_CHECK_SE_UPPER_ERROR(pCtx, uiCode, SLICE_HEADER_IDR_PIC_ID_MAX, "idr_pic_id"))
                return GENERATE_ERROR_NO(ERR_LEVEL_SLICE_HEADER, ERR_INFO_INVALID_IDR_PIC_ID);
            pSliceHead->uiIdrPicId = (uint16_t)uiCode; /* uiIdrPicId */
            // LONG_TERM_REF
            pCtx->uiCurIdrPicId = pSliceHead->uiIdrPicId;
        }

        pSliceHead->iDeltaPicOrderCntBottom = 0;
        pSliceHead->iDeltaPicOrderCnt[0] =
          pSliceHead->iDeltaPicOrderCnt[1] = 0;
        if (pSps->uiPocType == 0)
        {
            if ((uiRetTmp = (uint32_t)BsGetBits(pBs, pSps->iLog2MaxPocLsb, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //pic_order_cnt_lsb
            int32_t iMaxPocLsb = 1 << (pSps->iLog2MaxPocLsb);
            pSliceHead->iPicOrderCntLsb = (int32_t)uiCode;
            if (pPps->bPicOrderPresentFlag && !pSliceHead->bFieldPicFlag)
            {
                if ((uiRetTmp = (uint32_t)BsGetSe(pBs, &iCode)) != ERR_NONE) return (int32_t)uiRetTmp; //delta_pic_order_cnt_bottom
                pSliceHead->iDeltaPicOrderCntBottom = iCode;
            }
            //Calculate poc if necessary
            int32_t pocLsb = pSliceHead->iPicOrderCntLsb;
            if (pSliceHead->bIdrFlag || kpCurNal->sNalHeaderExt.sNalUnitHeader.eNalUnitType == NAL_UNIT_CODED_SLICE_IDR)
            {
                pCtx->pLastDecPicInfo->iPrevPicOrderCntMsb = 0;
                pCtx->pLastDecPicInfo->iPrevPicOrderCntLsb = 0;
            }
            int32_t pocMsb;
            if (pocLsb < pCtx->pLastDecPicInfo->iPrevPicOrderCntLsb
                && pCtx->pLastDecPicInfo->iPrevPicOrderCntLsb - pocLsb >= iMaxPocLsb / 2)
                pocMsb = pCtx->pLastDecPicInfo->iPrevPicOrderCntMsb + iMaxPocLsb;
            else if (pocLsb > pCtx->pLastDecPicInfo->iPrevPicOrderCntLsb
                     && pocLsb - pCtx->pLastDecPicInfo->iPrevPicOrderCntLsb > iMaxPocLsb / 2)
                pocMsb = pCtx->pLastDecPicInfo->iPrevPicOrderCntMsb - iMaxPocLsb;
            else
                pocMsb = pCtx->pLastDecPicInfo->iPrevPicOrderCntMsb;
            pSliceHead->iPicOrderCntLsb = pocMsb + pocLsb;

            if (pPps->bPicOrderPresentFlag && !pSliceHead->bFieldPicFlag)
            {
                pSliceHead->iPicOrderCntLsb += pSliceHead->iDeltaPicOrderCntBottom;
            }

            if (kpCurNal->sNalHeaderExt.sNalUnitHeader.uiNalRefIdc != 0)
            {
                pCtx->pLastDecPicInfo->iPrevPicOrderCntLsb = pocLsb;
                pCtx->pLastDecPicInfo->iPrevPicOrderCntMsb = pocMsb;
            }
            //End of Calculating poc
        }
        else if (pSps->uiPocType == 1 && !pSps->bDeltaPicOrderAlwaysZeroFlag)
        {
            if ((uiRetTmp = (uint32_t)BsGetSe(pBs, &iCode)) != ERR_NONE) return (int32_t)uiRetTmp; //delta_pic_order_cnt[ 0 ]
            pSliceHead->iDeltaPicOrderCnt[0] = iCode;
            if (pPps->bPicOrderPresentFlag && !pSliceHead->bFieldPicFlag)
            {
                if ((uiRetTmp = (uint32_t)BsGetSe(pBs, &iCode)) != ERR_NONE) return (int32_t)uiRetTmp; //delta_pic_order_cnt[ 1 ]
                pSliceHead->iDeltaPicOrderCnt[1] = iCode;
            }
        }
        pSliceHead->iRedundantPicCnt = 0;
        if (pPps->bRedundantPicCntPresentFlag)
        {
            if ((uiRetTmp = (uint32_t)BsGetUe(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //redundant_pic_cnt
            // standard section 7.4.3, redundant_pic_cnt should be in range 0 to 127, inclusive.
            if (WELS_CHECK_SE_UPPER_ERROR(pCtx, uiCode, SLICE_HEADER_REDUNDANT_PIC_CNT_MAX, "redundant_pic_cnt"))
                return GENERATE_ERROR_NO(ERR_LEVEL_SLICE_HEADER, ERR_INFO_INVALID_REDUNDANT_PIC_CNT);
            pSliceHead->iRedundantPicCnt = (int32_t)uiCode;
            if (pSliceHead->iRedundantPicCnt > 0)
            {
                WelsLog(pLogCtx, WELS_LOG_WARNING, "Redundant picture not supported!");
                return GENERATE_ERROR_NO(ERR_LEVEL_SLICE_HEADER, ERR_INFO_INVALID_REDUNDANT_PIC_CNT);
            }
        }

        if (B_SLICE == (EWelsSliceType)uiSliceType)
        {
            //fix me: it needs to use the this flag somewhere for B-Sclice
            if ((uiRetTmp = (uint32_t)BsGetOneBit(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //direct_spatial_mv_pred_flag
            pSliceHead->iDirectSpatialMvPredFlag = (int32_t)uiCode;
        }

        //set defaults, might be overriden a few line later
        pSliceHead->uiRefCount[0] = (int32_t)pPps->uiNumRefIdxL0Active;
        pSliceHead->uiRefCount[1] = (int32_t)pPps->uiNumRefIdxL1Active;

        bool bReadNumRefFlag = (P_SLICE == (EWelsSliceType)uiSliceType || B_SLICE == (EWelsSliceType)uiSliceType);
        if (kbExtensionFlag)
        {
            bReadNumRefFlag &= (BASE_QUALITY_ID == pNalHeaderExt->uiQualityId);
        }
        if (bReadNumRefFlag)
        {
            if ((uiRetTmp = (uint32_t)BsGetOneBit(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //num_ref_idx_active_override_flag
            pSliceHead->bNumRefIdxActiveOverrideFlag = uiCode != 0;
            if (pSliceHead->bNumRefIdxActiveOverrideFlag)
            {
                if ((uiRetTmp = (uint32_t)BsGetUe(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //num_ref_idx_l0_active_minus1
                if (WELS_CHECK_SE_UPPER_ERROR(pCtx, uiCode, MAX_NUM_REF_IDX_L0_ACTIVE_MINUS1, "num_ref_idx_l0_active_minus1"))
                    return GENERATE_ERROR_NO(ERR_LEVEL_SLICE_HEADER, ERR_INFO_INVALID_NUM_REF_IDX_L0_ACTIVE_MINUS1);
                pSliceHead->uiRefCount[0] = (int32_t)(1 + uiCode);
                if (B_SLICE == (EWelsSliceType)uiSliceType)
                {
                    if ((uiRetTmp = (uint32_t)BsGetUe(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //num_ref_idx_l1_active_minus1
                    if (WELS_CHECK_SE_UPPER_ERROR(pCtx, uiCode, MAX_NUM_REF_IDX_L1_ACTIVE_MINUS1, "num_ref_idx_l1_active_minus1"))
                        return GENERATE_ERROR_NO(ERR_LEVEL_SLICE_HEADER, ERR_INFO_INVALID_NUM_REF_IDX_L1_ACTIVE_MINUS1);
                    pSliceHead->uiRefCount[1] = (int32_t)(1 + uiCode);
                }
            }
        }

        if (pSliceHead->uiRefCount[0] > MAX_REF_PIC_COUNT || pSliceHead->uiRefCount[1] > MAX_REF_PIC_COUNT)
        {
            WelsLog(pLogCtx, WELS_LOG_WARNING, "reference overflow");
            return GENERATE_ERROR_NO(ERR_LEVEL_SLICE_HEADER, ERR_INFO_REF_COUNT_OVERFLOW);
        }

        if (BASE_QUALITY_ID == uiQualityId)
        {
            iRet = ParseRefPicListReordering(pBs, pSliceHead);
            if (iRet != ERR_NONE)
            {
                WelsLog(pLogCtx, WELS_LOG_WARNING, "invalid ref pPic list reordering syntaxs!");
                return iRet;
            }

            if ((pPps->bWeightedPredFlag && (EWelsSliceType)uiSliceType == P_SLICE) || (pPps->uiWeightedBipredIdc == 1 && (EWelsSliceType)uiSliceType == B_SLICE))
            {
                iRet = ParsePredWeightedTable(pBs, pSliceHead);
                if (iRet != ERR_NONE)
                {
                    WelsLog(pLogCtx, WELS_LOG_WARNING, "invalid weighted prediction syntaxs!");
                    return iRet;
                }
            }

            if (kbExtensionFlag)
            {
                if (pNalHeaderExt->iNoInterLayerPredFlag != 0 || pNalHeaderExt->uiQualityId > 0)
                    pSliceHeadExt->bBasePredWeightTableFlag = false;
                else
                    pSliceHeadExt->bBasePredWeightTableFlag = true;
            }

            if (kpCurNal->sNalHeaderExt.sNalUnitHeader.uiNalRefIdc != 0)
            {
                iRet = ParseDecRefPicMarking(pCtx, pBs, pSliceHead, pSps, bIdrFlag);
                if (iRet != ERR_NONE)
                {
                    return iRet;
                }

                if (kbExtensionFlag && !pSubsetSps->sSpsSvcExt.bSliceHeaderRestrictionFlag)
                {
                    if ((uiRetTmp = (uint32_t)BsGetOneBit(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //store_ref_base_pic_flag
                    pSliceHeadExt->bStoreRefBasePicFlag = uiCode != 0;
                    if ((pNalHeaderExt->bUseRefBasePicFlag || pSliceHeadExt->bStoreRefBasePicFlag) && !bIdrFlag)
                    {
                        WelsLog(pLogCtx, WELS_LOG_WARNING,
                                "ParseSliceHeaderSyntaxs(): bUseRefBasePicFlag or bStoreRefBasePicFlag = 1 not supported.");
                        return GENERATE_ERROR_NO(ERR_LEVEL_SLICE_HEADER, ERR_INFO_UNSUPPORTED_ILP);
                    }
                }
            }
        }

        if (pPps->bEntropyCodingModeFlag)
        {
            if (pSliceHead->eSliceType != I_SLICE && pSliceHead->eSliceType != SI_SLICE)
            {
                if ((uiRetTmp = (uint32_t)BsGetUe(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp;
                if (WELS_CHECK_SE_UPPER_ERROR(pCtx, uiCode, SLICE_HEADER_CABAC_INIT_IDC_MAX, "cabac_init_idc"))
                    return ERR_INFO_INVALID_CABAC_INIT_IDC;
                pSliceHead->iCabacInitIdc = (int32_t)uiCode;
            }
            else
                pSliceHead->iCabacInitIdc = 0;
        }

        if ((uiRetTmp = (uint32_t)BsGetSe(pBs, &iCode)) != ERR_NONE) return (int32_t)uiRetTmp; //slice_qp_delta
        pSliceHead->iSliceQpDelta = iCode;
        pSliceHead->iSliceQp = pPps->iPicInitQp + pSliceHead->iSliceQpDelta;
        if (pSliceHead->iSliceQp < 0 || pSliceHead->iSliceQp > 51)
        {
            WelsLog(pLogCtx, WELS_LOG_WARNING, $"QP {pSliceHead->iSliceQp} out of range");
            return GENERATE_ERROR_NO(ERR_LEVEL_SLICE_HEADER, ERR_INFO_INVALID_QP);
        }

        //FIXME qscale / qp ... stuff
        if (!kbExtensionFlag)
        {
            if ((EWelsSliceType)uiSliceType == SP_SLICE || (EWelsSliceType)uiSliceType == SI_SLICE)
            {
                WelsLog(pLogCtx, WELS_LOG_WARNING, "SP/SI not supported");
                return GENERATE_ERROR_NO(ERR_LEVEL_SLICE_HEADER, ERR_INFO_UNSUPPORTED_SPSI);
            }
        }

        pSliceHead->uiDisableDeblockingFilterIdc = 0;
        pSliceHead->iSliceAlphaC0Offset = 0;
        pSliceHead->iSliceBetaOffset = 0;
        if (pPps->bDeblockingFilterControlPresentFlag)
        {
            if ((uiRetTmp = (uint32_t)BsGetUe(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //disable_deblocking_filter_idc
            pSliceHead->uiDisableDeblockingFilterIdc = uiCode;
            //refer to JVT-X201wcm1.doc G.7.4.3.4--2010.4.20
            if (pSliceHead->uiDisableDeblockingFilterIdc > 6)
            {
                WelsLog(pLogCtx, WELS_LOG_WARNING, $"disable_deblock_filter_idc ({pSliceHead->uiDisableDeblockingFilterIdc}) out of range [0, 6]");
                return GENERATE_ERROR_NO(ERR_LEVEL_SLICE_HEADER, ERR_INFO_INVALID_DBLOCKING_IDC);
            }
            if (pSliceHead->uiDisableDeblockingFilterIdc != 1)
            {
                if ((uiRetTmp = (uint32_t)BsGetSe(pBs, &iCode)) != ERR_NONE) return (int32_t)uiRetTmp; //slice_alpha_c0_offset_div2
                pSliceHead->iSliceAlphaC0Offset = iCode * 2;
                if (WELS_CHECK_SE_BOTH_ERROR(pCtx, pSliceHead->iSliceAlphaC0Offset, SLICE_HEADER_ALPHAC0_BETA_OFFSET_MIN,
                                             SLICE_HEADER_ALPHAC0_BETA_OFFSET_MAX, "slice_alpha_c0_offset_div2 * 2"))
                    return GENERATE_ERROR_NO(ERR_LEVEL_SLICE_HEADER,
                                             ERR_INFO_INVALID_SLICE_ALPHA_C0_OFFSET_DIV2);
                if ((uiRetTmp = (uint32_t)BsGetSe(pBs, &iCode)) != ERR_NONE) return (int32_t)uiRetTmp; //slice_beta_offset_div2
                pSliceHead->iSliceBetaOffset = iCode * 2;
                if (WELS_CHECK_SE_BOTH_ERROR(pCtx, pSliceHead->iSliceBetaOffset, SLICE_HEADER_ALPHAC0_BETA_OFFSET_MIN,
                                             SLICE_HEADER_ALPHAC0_BETA_OFFSET_MAX, "slice_beta_offset_div2 * 2"))
                    return GENERATE_ERROR_NO(ERR_LEVEL_SLICE_HEADER,
                                             ERR_INFO_INVALID_SLICE_BETA_OFFSET_DIV2);
            }
        }

        bSgChangeCycleInvolved = (pPps->uiNumSliceGroups > 1 && pPps->uiSliceGroupMapType >= 3
                                  && pPps->uiSliceGroupMapType <= 5);
        if (kbExtensionFlag && bSgChangeCycleInvolved)
            bSgChangeCycleInvolved = (bSgChangeCycleInvolved && (uiQualityId == BASE_QUALITY_ID));
        if (bSgChangeCycleInvolved)
        {
            if (pPps->uiSliceGroupChangeRate > 0)
            {
                int32_t kiNumBits = (int32_t)WELS_CEIL(Math.Log((double)(1 + pPps->uiPicSizeInMapUnits /
                                    pPps->uiSliceGroupChangeRate)));
                if ((uiRetTmp = (uint32_t)BsGetBits(pBs, kiNumBits, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //lice_group_change_cycle
                pSliceHead->iSliceGroupChangeCycle = (int32_t)uiCode;
            }
            else
                pSliceHead->iSliceGroupChangeCycle = 0;
        }

        if (!kbExtensionFlag)
        {
            FillDefaultSliceHeaderExt(pSliceHeadExt, pNalHeaderExt);
        }
        else
        {
            /* Extra syntax elements newly introduced */
            pSliceHeadExt->pSubsetSps = pSubsetSps;

            if (pNalHeaderExt->iNoInterLayerPredFlag == 0 && BASE_QUALITY_ID == uiQualityId)
            {
                //the following should be deleted for CODE_CLEAN
                if ((uiRetTmp = (uint32_t)BsGetUe(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //ref_layer_dq_id
                pSliceHeadExt->uiRefLayerDqId = (uint8_t)uiCode;
                if (pSubsetSps->sSpsSvcExt.bInterLayerDeblockingFilterCtrlPresentFlag)
                {
                    if ((uiRetTmp = (uint32_t)BsGetUe(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //disable_inter_layer_deblocking_filter_idc
                    pSliceHeadExt->uiDisableInterLayerDeblockingFilterIdc = uiCode;
                    //refer to JVT-X201wcm1.doc G.7.4.3.4--2010.4.20
                    if (pSliceHeadExt->uiDisableInterLayerDeblockingFilterIdc > 6)
                    {
                        WelsLog(&(pCtx->sLogCtx), WELS_LOG_WARNING, $"disable_inter_layer_deblock_filter_idc ({pSliceHeadExt->uiDisableInterLayerDeblockingFilterIdc}) out of range [0, 6]");
                        return GENERATE_ERROR_NO(ERR_LEVEL_SLICE_HEADER, ERR_INFO_INVALID_DBLOCKING_IDC);
                    }
                    if (pSliceHeadExt->uiDisableInterLayerDeblockingFilterIdc != 1)
                    {
                        if ((uiRetTmp = (uint32_t)BsGetSe(pBs, &iCode)) != ERR_NONE) return (int32_t)uiRetTmp; //inter_layer_slice_alpha_c0_offset_div2
                        pSliceHeadExt->iInterLayerSliceAlphaC0Offset = iCode * 2;
                        if (WELS_CHECK_SE_BOTH_ERROR(pCtx, pSliceHeadExt->iInterLayerSliceAlphaC0Offset,
                                                     SLICE_HEADER_INTER_LAYER_ALPHAC0_BETA_OFFSET_MIN, SLICE_HEADER_INTER_LAYER_ALPHAC0_BETA_OFFSET_MAX,
                                                     "inter_layer_alpha_c0_offset_div2 * 2"))
                            return GENERATE_ERROR_NO(ERR_LEVEL_SLICE_HEADER,
                                                     ERR_INFO_INVALID_SLICE_ALPHA_C0_OFFSET_DIV2);
                        if ((uiRetTmp = (uint32_t)BsGetSe(pBs, &iCode)) != ERR_NONE) return (int32_t)uiRetTmp; //inter_layer_slice_beta_offset_div2
                        pSliceHeadExt->iInterLayerSliceBetaOffset = iCode * 2;
                        if (WELS_CHECK_SE_BOTH_ERROR(pCtx, pSliceHeadExt->iInterLayerSliceBetaOffset, SLICE_HEADER_INTER_LAYER_ALPHAC0_BETA_OFFSET_MIN,
                                                     SLICE_HEADER_INTER_LAYER_ALPHAC0_BETA_OFFSET_MAX, "inter_layer_slice_beta_offset_div2 * 2"))
                            return GENERATE_ERROR_NO(ERR_LEVEL_SLICE_HEADER, ERR_INFO_INVALID_SLICE_BETA_OFFSET_DIV2);
                    }
                }

                pSliceHeadExt->uiRefLayerChromaPhaseXPlus1Flag = pSubsetSps->sSpsSvcExt.uiSeqRefLayerChromaPhaseXPlus1Flag;
                pSliceHeadExt->uiRefLayerChromaPhaseYPlus1 = pSubsetSps->sSpsSvcExt.uiSeqRefLayerChromaPhaseYPlus1;

                if ((uiRetTmp = (uint32_t)BsGetOneBit(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //constrained_intra_resampling_flag
                pSliceHeadExt->bConstrainedIntraResamplingFlag = uiCode != 0;

                {
                    SPosOffset pos;
                    int32_t iFrameMbsOnly = pSps->bFrameMbsOnlyFlag ? 1 : 0;
                    int32_t iFieldPic = pSliceHead->bFieldPicFlag ? 1 : 0;
                    pos.iLeftOffset = pSubsetSps->sSpsSvcExt.sSeqScaledRefLayer.iLeftOffset;
                    pos.iTopOffset = pSubsetSps->sSpsSvcExt.sSeqScaledRefLayer.iTopOffset * (2 - iFrameMbsOnly);
                    pos.iRightOffset = pSubsetSps->sSpsSvcExt.sSeqScaledRefLayer.iRightOffset;
                    pos.iBottomOffset = pSubsetSps->sSpsSvcExt.sSeqScaledRefLayer.iBottomOffset * (2 - iFrameMbsOnly);
                    //memcpy(&pSliceHeadExt->sScaledRefLayer, &pos, sizeof(SPosOffset));//confirmed_safe_unsafe_usage
                    pSliceHeadExt->iScaledRefLayerPicWidthInSampleLuma = (pSliceHead->iMbWidth << 4) -
                        (pos.iLeftOffset + pos.iRightOffset);
                    pSliceHeadExt->iScaledRefLayerPicHeightInSampleLuma = (pSliceHead->iMbHeight << 4) -
                        (pos.iTopOffset + pos.iBottomOffset) / (1 + iFieldPic);
                }
            }
            else if (uiQualityId > BASE_QUALITY_ID)
            {
                WelsLog(pLogCtx, WELS_LOG_WARNING, "MGS not supported.");
                return GENERATE_ERROR_NO(ERR_LEVEL_SLICE_HEADER, ERR_INFO_UNSUPPORTED_MGS);
            }
            else
            {
                pSliceHeadExt->uiRefLayerDqId = unchecked((uint8_t)(-1));
            }

            pSliceHeadExt->bSliceSkipFlag = false;
            pSliceHeadExt->bAdaptiveBaseModeFlag = false;
            pSliceHeadExt->bDefaultBaseModeFlag = false;
            pSliceHeadExt->bAdaptiveMotionPredFlag = false;
            pSliceHeadExt->bDefaultMotionPredFlag = false;
            pSliceHeadExt->bAdaptiveResidualPredFlag = false;
            pSliceHeadExt->bDefaultResidualPredFlag = false;
            if (pNalHeaderExt->iNoInterLayerPredFlag != 0)
                pSliceHeadExt->bTCoeffLevelPredFlag = false;
            else
                pSliceHeadExt->bTCoeffLevelPredFlag = pSubsetSps->sSpsSvcExt.bSeqTCoeffLevelPredFlag;

            if (pNalHeaderExt->iNoInterLayerPredFlag == 0)
            {
                if ((uiRetTmp = (uint32_t)BsGetOneBit(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //slice_skip_flag
                pSliceHeadExt->bSliceSkipFlag = uiCode != 0;
                if (pSliceHeadExt->bSliceSkipFlag)
                {
                    WelsLog(pLogCtx, WELS_LOG_WARNING, "bSliceSkipFlag == 1 not supported.");
                    return GENERATE_ERROR_NO(ERR_LEVEL_SLICE_HEADER, ERR_INFO_UNSUPPORTED_SLICESKIP);
                }
                else
                {
                    if ((uiRetTmp = (uint32_t)BsGetOneBit(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //adaptive_base_mode_flag
                    pSliceHeadExt->bAdaptiveBaseModeFlag = uiCode != 0;
                    if (!pSliceHeadExt->bAdaptiveBaseModeFlag)
                    {
                        if ((uiRetTmp = (uint32_t)BsGetOneBit(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //default_base_mode_flag
                        pSliceHeadExt->bDefaultBaseModeFlag = uiCode != 0;
                    }
                    if (!pSliceHeadExt->bDefaultBaseModeFlag)
                    {
                        if ((uiRetTmp = (uint32_t)BsGetOneBit(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //adaptive_motion_prediction_flag
                        pSliceHeadExt->bAdaptiveMotionPredFlag = uiCode != 0;
                        if (!pSliceHeadExt->bAdaptiveMotionPredFlag)
                        {
                            if ((uiRetTmp = (uint32_t)BsGetOneBit(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //default_motion_prediction_flag
                            pSliceHeadExt->bDefaultMotionPredFlag = uiCode != 0;
                        }
                    }

                    if ((uiRetTmp = (uint32_t)BsGetOneBit(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //adaptive_residual_prediction_flag
                    pSliceHeadExt->bAdaptiveResidualPredFlag = uiCode != 0;
                    if (!pSliceHeadExt->bAdaptiveResidualPredFlag)
                    {
                        if ((uiRetTmp = (uint32_t)BsGetOneBit(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //default_residual_prediction_flag
                        pSliceHeadExt->bDefaultResidualPredFlag = uiCode != 0;
                    }
                }
                if (pSubsetSps->sSpsSvcExt.bAdaptiveTCoeffLevelPredFlag)
                {
                    if ((uiRetTmp = (uint32_t)BsGetOneBit(pBs, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //tcoeff_level_prediction_flag
                    pSliceHeadExt->bTCoeffLevelPredFlag = uiCode != 0;
                }
            }

            if (!pSubsetSps->sSpsSvcExt.bSliceHeaderRestrictionFlag)
            {
                if ((uiRetTmp = (uint32_t)BsGetBits(pBs, 4, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //scan_idx_start
                pSliceHeadExt->uiScanIdxStart = (uint8_t)uiCode;
                if ((uiRetTmp = (uint32_t)BsGetBits(pBs, 4, &uiCode)) != ERR_NONE) return (int32_t)uiRetTmp; //scan_idx_end
                pSliceHeadExt->uiScanIdxEnd = (uint8_t)uiCode;
                if (pSliceHeadExt->uiScanIdxStart != 0 || pSliceHeadExt->uiScanIdxEnd != 15)
                {
                    WelsLog(pLogCtx, WELS_LOG_WARNING, $"uiScanIdxStart ({pSliceHeadExt->uiScanIdxStart}) != 0 and uiScanIdxEnd ({pSliceHeadExt->uiScanIdxEnd}) !=15 not supported here");
                    return GENERATE_ERROR_NO(ERR_LEVEL_SLICE_HEADER, ERR_INFO_UNSUPPORTED_MGS);
                }
            }
            else
            {
                pSliceHeadExt->uiScanIdxStart = 0;
                pSliceHeadExt->uiScanIdxEnd = 15;
            }
        }

        return ERR_NONE;
    }

    /*
     *  Copy relative syntax elements of NALUnitHeaderExt, sRefPicBaseMarking and bStoreRefBasePicFlag in prefix nal unit.
     *  pSrc:   mark as decoded prefix NAL
     *  ppDst:  succeeded VCL NAL based AVC (I/P Slice)
     */
    public static bool PrefetchNalHeaderExtSyntax(SWelsDecoderContext* pCtx, SNalUnit* kppDst, SNalUnit* kpSrc)
    {
        SNalUnitHeaderExt* pNalHdrExtD = null, pNalHdrExtS = null;
        SSliceHeaderExt* pShExtD = null;
        SPrefixNalUnit* pPrefixS = null;
        SSps* pSps = null;
        int32_t iIdx = 0;

        if (kppDst == null || kpSrc == null)
            return false;

        pNalHdrExtD = &kppDst->sNalHeaderExt;
        pNalHdrExtS = &kpSrc->sNalHeaderExt;
        pShExtD = &kppDst->sNalData.sVclNal.sSliceHeaderExt;
        pPrefixS = &kpSrc->sNalData.sPrefixNal;
        pSps = &pCtx->sSpsPpsCtx.sSpsBuffer[pCtx->sSpsPpsCtx.sPpsBuffer[pShExtD->sSliceHeader.iPpsId].iSpsId];

        pNalHdrExtD->uiDependencyId = pNalHdrExtS->uiDependencyId;
        pNalHdrExtD->uiQualityId = pNalHdrExtS->uiQualityId;
        pNalHdrExtD->uiTemporalId = pNalHdrExtS->uiTemporalId;
        pNalHdrExtD->uiPriorityId = pNalHdrExtS->uiPriorityId;
        pNalHdrExtD->bIdrFlag = pNalHdrExtS->bIdrFlag;
        pNalHdrExtD->iNoInterLayerPredFlag = pNalHdrExtS->iNoInterLayerPredFlag;
        pNalHdrExtD->bDiscardableFlag = pNalHdrExtS->bDiscardableFlag;
        pNalHdrExtD->bOutputFlag = pNalHdrExtS->bOutputFlag;
        pNalHdrExtD->bUseRefBasePicFlag = pNalHdrExtS->bUseRefBasePicFlag;
        pNalHdrExtD->uiLayerDqId = pNalHdrExtS->uiLayerDqId;

        pShExtD->bStoreRefBasePicFlag = pPrefixS->bStoreRefBasePicFlag;
        memcpy(&pShExtD->sRefBasePicMarking, &pPrefixS->sRefPicBaseMarking,
               sizeof(SRefBasePicMarking)); //confirmed_safe_unsafe_usage
        if (pShExtD->sRefBasePicMarking.bAdaptiveRefBasePicMarkingModeFlag)
        {
            SRefBasePicMarking* pRefBasePicMarking = &pShExtD->sRefBasePicMarking;
            iIdx = 0;
            do
            {
                if (pRefBasePicMarking->mmco_base[iIdx].uiMmcoType == (uint32_t)MMCO_END)
                    break;
                if (pRefBasePicMarking->mmco_base[iIdx].uiMmcoType == (uint32_t)MMCO_SHORT2UNUSED)
                    pRefBasePicMarking->mmco_base[iIdx].iShortFrameNum = (int32_t)((uint32_t)(pShExtD->sSliceHeader.iFrameNum -
                        pRefBasePicMarking->mmco_base[iIdx].uiDiffOfPicNums) & (uint32_t)((1 << (int32_t)pSps->uiLog2MaxFrameNum) - 1));
                ++iIdx;
            } while (iIdx < MAX_MMCO_COUNT);
        }

        return true;
    }



    public static int32_t UpdateAccessUnit(SWelsDecoderContext* pCtx)
    {
        SAccessUnit* pCurAu = pCtx->pAccessUnitList;
        int32_t iIdx = (int32_t)pCurAu->uiEndPos;

        // Conversed iterator
        pCtx->uiTargetDqId = pCurAu->pNalUnitsList[iIdx]->sNalHeaderExt.uiLayerDqId;
        pCurAu->uiActualUnitsNum = (uint32_t)(iIdx + 1);
        pCurAu->bCompletedAuFlag = true;

        // Added for mosaic avoidance, 11/19/2009
        // LONG_TERM_REF
        if (pCtx->bParamSetsLostFlag || pCtx->bNewSeqBegin)
        {
            uint32_t uiActualIdx = 0;
            while (uiActualIdx < pCurAu->uiActualUnitsNum)
            {
                SNalUnit* nal = pCurAu->pNalUnitsList[uiActualIdx];

                if (nal->sNalHeaderExt.sNalUnitHeader.eNalUnitType == NAL_UNIT_CODED_SLICE_IDR || nal->sNalHeaderExt.bIdrFlag)
                {
                    break;
                }
                ++uiActualIdx;
            }
            if (uiActualIdx ==
                pCurAu->uiActualUnitsNum)
            { // no found IDR nal within incoming AU, need exit to avoid mosaic issue, 11/19/2009

                pCtx->pDecoderStatistics->uiIDRLostNum++;
                if (!pCtx->bParamSetsLostFlag)
                    WelsLog(&(pCtx->sLogCtx), WELS_LOG_WARNING,
                            "UpdateAccessUnit():::::Key frame lost.....CAN NOT find IDR from current AU.");
                pCtx->iErrorCode |= (int32_t)dsRefLost;
                if (pCtx->pParam->eEcActiveIdc == ERROR_CON_DISABLE)
                {
                    // LONG_TERM_REF
                    pCtx->iErrorCode |= (int32_t)dsNoParamSets;
                    return (int32_t)dsNoParamSets;
                }
            }
        }

        return ERR_NONE;
    }

    // uiMbCount stands for C's repeated pCtx->sMb.iMbWidth * pCtx->sMb.iMbHeight.
    public static int32_t InitialDqLayersContext(SWelsDecoderContext* pCtx, int32_t kiMaxWidth, int32_t kiMaxHeight)
    {
        int32_t i = 0;
        if (null == pCtx || kiMaxWidth <= 0 || kiMaxHeight <= 0)
            return ERR_INFO_INVALID_PARAM;
        pCtx->sMb.iMbWidth = (uint32_t)((kiMaxWidth + 15) >> 4);
        pCtx->sMb.iMbHeight = (uint32_t)((kiMaxHeight + 15) >> 4);

        if (pCtx->bInitialDqLayersMem && kiMaxWidth <= pCtx->iPicWidthReq
            && kiMaxHeight <= pCtx->iPicHeightReq) // have same dimension memory, skipped
            return ERR_NONE;

        CMemoryAlign* pMa = pCtx->pMemAlign;

        UninitialDqLayersContext(pCtx);

        uint32_t uiMbCount = pCtx->sMb.iMbWidth * pCtx->sMb.iMbHeight;
        do
        {
            SDqLayer* pDq = (SDqLayer*)pMa->WelsMallocz((uint32_t)sizeof(SDqLayer), "PDqLayer");

            if (pDq == null)
                return ERR_INFO_OUT_OF_MEMORY;

            pCtx->pDqLayersList[i] = pDq; //to keep consistence with in UninitialDqLayersContext()
            memset(pDq, 0, sizeof(SDqLayer));

            pCtx->sMb.pMbType[i] = (uint32_t*)pMa->WelsMallocz(uiMbCount * sizeof(uint32_t),
                                   "pCtx->sMb.pMbType[]");
            pCtx->sMb.pMv[i][(int32_t)LIST_0] = (InlineArray16<InlineArray2<int16_t>>*)pMa->WelsMallocz((uint32_t)(uiMbCount * sizeof(
                                         int16_t) * (int32_t)MV_A * MB_BLOCK4x4_NUM), "pCtx->sMb.pMv[][]");
            pCtx->sMb.pMv[i][(int32_t)LIST_1] = (InlineArray16<InlineArray2<int16_t>>*)pMa->WelsMallocz((uint32_t)(uiMbCount * sizeof(
                                         int16_t) * (int32_t)MV_A * MB_BLOCK4x4_NUM), "pCtx->sMb.pMv[][]");

            pCtx->sMb.pRefIndex[i][(int32_t)LIST_0] = (InlineArray16<int8_t>*)pMa->WelsMallocz((uint32_t)(uiMbCount *
                                             sizeof(
                                               int8_t) * MB_BLOCK4x4_NUM), "pCtx->sMb.pRefIndex[][]");
            pCtx->sMb.pRefIndex[i][(int32_t)LIST_1] = (InlineArray16<int8_t>*)pMa->WelsMallocz((uint32_t)(uiMbCount *
                                             sizeof(
                                               int8_t) * MB_BLOCK4x4_NUM), "pCtx->sMb.pRefIndex[][]");
            pCtx->sMb.pDirect[i] = (InlineArray16<int8_t>*)pMa->WelsMallocz((uint32_t)(uiMbCount *
                                   sizeof(
                                     int8_t) * MB_BLOCK4x4_NUM), "pCtx->sMb.pDirect[]");
            pCtx->sMb.pLumaQp[i] = (int8_t*)pMa->WelsMallocz(uiMbCount * sizeof(int8_t),
                                   "pCtx->sMb.pLumaQp[]");
            pCtx->sMb.pNoSubMbPartSizeLessThan8x8Flag[i] = (bool*)pMa->WelsMallocz(uiMbCount *
                sizeof(
                  bool),
                "pCtx->sMb.pNoSubMbPartSizeLessThan8x8Flag[]");
            pCtx->sMb.pTransformSize8x8Flag[i] = (bool*)pMa->WelsMallocz(uiMbCount * sizeof(
                                                   bool),
                                                 "pCtx->sMb.pTransformSize8x8Flag[]");
            pCtx->sMb.pChromaQp[i] = (InlineArray2<int8_t>*)pMa->WelsMallocz(uiMbCount * sizeof(
                                       int8_t) * 2,
                                     "pCtx->sMb.pChromaQp[]");
            pCtx->sMb.pMvd[i][(int32_t)LIST_0] = (InlineArray16<InlineArray2<int16_t>>*)pMa->WelsMallocz((uint32_t)(uiMbCount * sizeof(
                                          int16_t) * (int32_t)MV_A * MB_BLOCK4x4_NUM), "pCtx->sMb.pMvd[][]");
            pCtx->sMb.pMvd[i][(int32_t)LIST_1] = (InlineArray16<InlineArray2<int16_t>>*)pMa->WelsMallocz((uint32_t)(uiMbCount * sizeof(
                                          int16_t) * (int32_t)MV_A * MB_BLOCK4x4_NUM), "pCtx->sMb.pMvd[][]");
            pCtx->sMb.pCbfDc[i] = (uint16_t*)pMa->WelsMallocz(uiMbCount * sizeof(uint16_t),
                                  "pCtx->sMb.pCbfDc[]");
            pCtx->sMb.pNzc[i] = (InlineArray24<int8_t>*)pMa->WelsMallocz(uiMbCount * sizeof(
                                  int8_t) * 24,
                                "pCtx->sMb.pNzc[]");
            pCtx->sMb.pNzcRs[i] = (InlineArray24<int8_t>*)pMa->WelsMallocz(uiMbCount * sizeof(
                                    int8_t) * 24,
                                  "pCtx->sMb.pNzcRs[]");
            pCtx->sMb.pScaledTCoeff[i] = (InlineArray384<int16_t>*)pMa->WelsMallocz((uint32_t)(uiMbCount *
                                         sizeof(int16_t) * MB_COEFF_LIST_SIZE), "pCtx->sMb.pScaledTCoeff[]");
            pCtx->sMb.pIntraPredMode[i] = (InlineArray8<int8_t>*)pMa->WelsMallocz(uiMbCount * sizeof(
                                            int8_t) * 8,
                                          "pCtx->sMb.pIntraPredMode[]");
            pCtx->sMb.pIntra4x4FinalMode[i] = (InlineArray16<int8_t>*)pMa->WelsMallocz((uint32_t)(uiMbCount *
                                              sizeof(int8_t) * MB_BLOCK4x4_NUM), "pCtx->sMb.pIntra4x4FinalMode[]");
            pCtx->sMb.pIntraNxNAvailFlag[i] = (uint8_t*)pMa->WelsMallocz(uiMbCount * sizeof(
                                                int8_t),
                                              "pCtx->sMb.pIntraNxNAvailFlag");
            pCtx->sMb.pChromaPredMode[i] = (int8_t*)pMa->WelsMallocz(uiMbCount * sizeof(int8_t),
                                           "pCtx->sMb.pChromaPredMode[]");
            pCtx->sMb.pCbp[i] = (int8_t*)pMa->WelsMallocz(uiMbCount * sizeof(int8_t),
                                "pCtx->sMb.pCbp[]");
            pCtx->sMb.pSubMbType[i] = (InlineArray4<uint32_t>*)pMa->WelsMallocz((uint32_t)(uiMbCount *
                                      sizeof(
                                        uint32_t) * MB_PARTITION_SIZE), "pCtx->sMb.pSubMbType[]");
            pCtx->sMb.pSliceIdc[i] = (int32_t*)pMa->WelsMallocz(uiMbCount * sizeof(int32_t),
                                     "pCtx->sMb.pSliceIdc[]"); // using int32_t for slice_idc, 4/21/2010
            pCtx->sMb.pResidualPredFlag[i] = (int8_t*)pMa->WelsMallocz(uiMbCount * sizeof(
                                               int8_t),
                                             "pCtx->sMb.pResidualPredFlag[]");
            pCtx->sMb.pInterPredictionDoneFlag[i] = (int8_t*)pMa->WelsMallocz(uiMbCount *
                                                    sizeof(
                                                        int8_t), "pCtx->sMb.pInterPredictionDoneFlag[]");

            pCtx->sMb.pMbCorrectlyDecodedFlag[i] = (bool*)pMa->WelsMallocz(uiMbCount * sizeof(
                bool),
                                                   "pCtx->sMb.pMbCorrectlyDecodedFlag[]");
            pCtx->sMb.pMbRefConcealedFlag[i] = (bool*)pMa->WelsMallocz(uiMbCount * sizeof(
                                                 bool),
                                               "pCtx->pMbRefConcealedFlag[]");

            // check memory block valid due above allocated..
            if ((null == pCtx->sMb.pMbType[i].p) ||
                (null == pCtx->sMb.pMv[i][(int32_t)LIST_0].p) ||
                (null == pCtx->sMb.pMv[i][(int32_t)LIST_1].p) ||
                (null == pCtx->sMb.pRefIndex[i][(int32_t)LIST_0].p) ||
                (null == pCtx->sMb.pRefIndex[i][(int32_t)LIST_1].p) ||
                (null == pCtx->sMb.pDirect[i].p) ||
                (null == pCtx->sMb.pLumaQp[i].p) ||
                (null == pCtx->sMb.pNoSubMbPartSizeLessThan8x8Flag[i].p) ||
                (null == pCtx->sMb.pTransformSize8x8Flag[i].p) ||
                (null == pCtx->sMb.pChromaQp[i].p) ||
                (null == pCtx->sMb.pMvd[i][(int32_t)LIST_0].p) ||
                (null == pCtx->sMb.pMvd[i][(int32_t)LIST_1].p) ||
                (null == pCtx->sMb.pCbfDc[i].p) ||
                (null == pCtx->sMb.pNzc[i].p) ||
                (null == pCtx->sMb.pNzcRs[i].p) ||
                (null == pCtx->sMb.pScaledTCoeff[i].p) ||
                (null == pCtx->sMb.pIntraPredMode[i].p) ||
                (null == pCtx->sMb.pIntra4x4FinalMode[i].p) ||
                (null == pCtx->sMb.pIntraNxNAvailFlag[i].p) ||
                (null == pCtx->sMb.pChromaPredMode[i].p) ||
                (null == pCtx->sMb.pCbp[i].p) ||
                (null == pCtx->sMb.pSubMbType[i].p) ||
                (null == pCtx->sMb.pSliceIdc[i].p) ||
                (null == pCtx->sMb.pResidualPredFlag[i].p) ||
                (null == pCtx->sMb.pInterPredictionDoneFlag[i].p) ||
                (null == pCtx->sMb.pMbRefConcealedFlag[i].p) ||
                (null == pCtx->sMb.pMbCorrectlyDecodedFlag[i].p)
               )
                return ERR_INFO_OUT_OF_MEMORY;

            memset(pCtx->sMb.pSliceIdc[i], 0xff, (int32_t)(uiMbCount * sizeof(int32_t)));

            ++i;
        } while (i < LAYER_NUM_EXCHANGEABLE);

        pCtx->bInitialDqLayersMem = true;
        pCtx->iPicWidthReq = kiMaxWidth;
        pCtx->iPicHeightReq = kiMaxHeight;

        return ERR_NONE;
    }



    public static void UninitialDqLayersContext(SWelsDecoderContext* pCtx)
    {
        int32_t i = 0;
        CMemoryAlign* pMa = pCtx->pMemAlign;

        do
        {
            SDqLayer* pDq = pCtx->pDqLayersList[i];
            if (pDq == null)
            {
                ++i;
                continue;
            }

            if (pCtx->sMb.pMbType[i].p != null)
            {
                pMa->WelsFree(pCtx->sMb.pMbType[i], "pCtx->sMb.pMbType[]");

                pCtx->sMb.pMbType[i] = null;
            }

            for (int32_t listIdx = (int32_t)LIST_0; listIdx < (int32_t)LIST_A; ++listIdx)
            {
                if (pCtx->sMb.pMv[i][listIdx].p != null)
                {
                    pMa->WelsFree(pCtx->sMb.pMv[i][listIdx], "pCtx->sMb.pMv[][]");
                    pCtx->sMb.pMv[i][listIdx] = null;
                }

                if (pCtx->sMb.pRefIndex[i][listIdx].p != null)
                {
                    pMa->WelsFree(pCtx->sMb.pRefIndex[i][listIdx], "pCtx->sMb.pRefIndex[][]");
                    pCtx->sMb.pRefIndex[i][listIdx] = null;
                }

                if (pCtx->sMb.pDirect[i].p != null)
                {
                    pMa->WelsFree(pCtx->sMb.pDirect[i], "pCtx->sMb.pDirect[]");
                    pCtx->sMb.pDirect[i] = null;
                }

                if (pCtx->sMb.pMvd[i][listIdx].p != null)
                {
                    pMa->WelsFree(pCtx->sMb.pMvd[i][listIdx], "pCtx->sMb.pMvd[][]");
                    pCtx->sMb.pMvd[i][listIdx] = null;
                }
            }

            if (pCtx->sMb.pNoSubMbPartSizeLessThan8x8Flag[i].p != null)
            {
                pMa->WelsFree(pCtx->sMb.pNoSubMbPartSizeLessThan8x8Flag[i], "pCtx->sMb.pNoSubMbPartSizeLessThan8x8Flag[]");

                pCtx->sMb.pNoSubMbPartSizeLessThan8x8Flag[i] = null;
            }

            if (pCtx->sMb.pTransformSize8x8Flag[i].p != null)
            {
                pMa->WelsFree(pCtx->sMb.pTransformSize8x8Flag[i], "pCtx->sMb.pTransformSize8x8Flag[]");

                pCtx->sMb.pTransformSize8x8Flag[i] = null;
            }

            if (pCtx->sMb.pLumaQp[i].p != null)
            {
                pMa->WelsFree(pCtx->sMb.pLumaQp[i], "pCtx->sMb.pLumaQp[]");

                pCtx->sMb.pLumaQp[i] = null;
            }

            if (pCtx->sMb.pChromaQp[i].p != null)
            {
                pMa->WelsFree(pCtx->sMb.pChromaQp[i], "pCtx->sMb.pChromaQp[]");

                pCtx->sMb.pChromaQp[i] = null;
            }

            if (pCtx->sMb.pCbfDc[i].p != null)
            {
                pMa->WelsFree(pCtx->sMb.pCbfDc[i], "pCtx->sMb.pCbfDc[]");
                pCtx->sMb.pCbfDc[i] = null;
            }

            if (pCtx->sMb.pNzc[i].p != null)
            {
                pMa->WelsFree(pCtx->sMb.pNzc[i], "pCtx->sMb.pNzc[]");

                pCtx->sMb.pNzc[i] = null;
            }

            if (pCtx->sMb.pNzcRs[i].p != null)
            {
                pMa->WelsFree(pCtx->sMb.pNzcRs[i], "pCtx->sMb.pNzcRs[]");

                pCtx->sMb.pNzcRs[i] = null;
            }

            if (pCtx->sMb.pScaledTCoeff[i].p != null)
            {
                pMa->WelsFree(pCtx->sMb.pScaledTCoeff[i], "pCtx->sMb.pScaledTCoeff[]");

                pCtx->sMb.pScaledTCoeff[i] = null;
            }

            if (pCtx->sMb.pIntraPredMode[i].p != null)
            {
                pMa->WelsFree(pCtx->sMb.pIntraPredMode[i], "pCtx->sMb.pIntraPredMode[]");

                pCtx->sMb.pIntraPredMode[i] = null;
            }

            if (pCtx->sMb.pIntra4x4FinalMode[i].p != null)
            {
                pMa->WelsFree(pCtx->sMb.pIntra4x4FinalMode[i], "pCtx->sMb.pIntra4x4FinalMode[]");

                pCtx->sMb.pIntra4x4FinalMode[i] = null;
            }

            if (pCtx->sMb.pIntraNxNAvailFlag[i].p != null)
            {
                pMa->WelsFree(pCtx->sMb.pIntraNxNAvailFlag[i], "pCtx->sMb.pIntraNxNAvailFlag");

                pCtx->sMb.pIntraNxNAvailFlag[i] = null;
            }

            if (pCtx->sMb.pChromaPredMode[i].p != null)
            {
                pMa->WelsFree(pCtx->sMb.pChromaPredMode[i], "pCtx->sMb.pChromaPredMode[]");

                pCtx->sMb.pChromaPredMode[i] = null;
            }

            if (pCtx->sMb.pCbp[i].p != null)
            {
                pMa->WelsFree(pCtx->sMb.pCbp[i], "pCtx->sMb.pCbp[]");

                pCtx->sMb.pCbp[i] = null;
            }

            //      if (pCtx->sMb.pMotionPredFlag[i])
            //{
            //  pMa->WelsFree( pCtx->sMb.pMotionPredFlag[i], "pCtx->sMb.pMotionPredFlag[]" );

            //  pCtx->sMb.pMotionPredFlag[i] = NULL;
            //}

            if (pCtx->sMb.pSubMbType[i].p != null)
            {
                pMa->WelsFree(pCtx->sMb.pSubMbType[i], "pCtx->sMb.pSubMbType[]");

                pCtx->sMb.pSubMbType[i] = null;
            }

            if (pCtx->sMb.pSliceIdc[i].p != null)
            {
                pMa->WelsFree(pCtx->sMb.pSliceIdc[i], "pCtx->sMb.pSliceIdc[]");

                pCtx->sMb.pSliceIdc[i] = null;
            }

            if (pCtx->sMb.pResidualPredFlag[i].p != null)
            {
                pMa->WelsFree(pCtx->sMb.pResidualPredFlag[i], "pCtx->sMb.pResidualPredFlag[]");

                pCtx->sMb.pResidualPredFlag[i] = null;
            }

            if (pCtx->sMb.pInterPredictionDoneFlag[i].p != null)
            {
                pMa->WelsFree(pCtx->sMb.pInterPredictionDoneFlag[i], "pCtx->sMb.pInterPredictionDoneFlag[]");

                pCtx->sMb.pInterPredictionDoneFlag[i] = null;
            }

            if (pCtx->sMb.pMbCorrectlyDecodedFlag[i].p != null)
            {
                pMa->WelsFree(pCtx->sMb.pMbCorrectlyDecodedFlag[i], "pCtx->sMb.pMbCorrectlyDecodedFlag[]");
                pCtx->sMb.pMbCorrectlyDecodedFlag[i] = null;
            }

            if (pCtx->sMb.pMbRefConcealedFlag[i].p != null)
            {
                pMa->WelsFree(pCtx->sMb.pMbRefConcealedFlag[i], "pCtx->sMb.pMbRefConcealedFlag[]");
                pCtx->sMb.pMbRefConcealedFlag[i] = null;
            }
            pMa->WelsFree(pDq, "pDq");

            pDq = null;
            pCtx->pDqLayersList[i] = null;

            ++i;
        } while (i < LAYER_NUM_EXCHANGEABLE);

        pCtx->iPicWidthReq = 0;
        pCtx->iPicHeightReq = 0;
        pCtx->bInitialDqLayersMem = false;
    }

    public static void ResetCurrentAccessUnit(SWelsDecoderContext* pCtx)
    {
        SAccessUnit* pCurAu = pCtx->pAccessUnitList;
        pCurAu->uiStartPos = 0;
        pCurAu->uiEndPos = 0;
        pCurAu->bCompletedAuFlag = false;
        if (pCurAu->uiActualUnitsNum > 0)
        {
            uint32_t iIdx = 0;
            uint32_t kuiActualNum = pCurAu->uiActualUnitsNum;
            // a more simpler method to do nal units list management prefered here
            uint32_t kuiAvailNum = pCurAu->uiAvailUnitsNum;
            // Guard: counter mismatch after timeout-early-return can cause unsigned underflow.
            if (kuiActualNum > kuiAvailNum)
            {
                pCurAu->uiActualUnitsNum = pCurAu->uiAvailUnitsNum = 0;
                return;
            }
            uint32_t kuiLeftNum = kuiAvailNum - kuiActualNum;
            // Guard: swap must stay within allocated list capacity.
            uint32_t kuiSwapLimit = (kuiAvailNum <= pCurAu->uiCountUnitsNum) ? kuiLeftNum : 0;

            // Swapping active nal unit nodes of succeeding AU with leading of list
            while (iIdx < kuiSwapLimit)
            {
                SNalUnit* t = pCurAu->pNalUnitsList[kuiActualNum + iIdx];
                pCurAu->pNalUnitsList[kuiActualNum + iIdx] = pCurAu->pNalUnitsList[iIdx];
                pCurAu->pNalUnitsList[iIdx] = t;
                ++iIdx;
            }
            pCurAu->uiActualUnitsNum = pCurAu->uiAvailUnitsNum = kuiSwapLimit;
        }
    }

    /*!
     * \brief   Force reset current Acess Unit Nal list in case error parsing/decoding in current AU
     * \author
     * \history 11/16/2009
     */
    public static void ForceResetCurrentAccessUnit(SAccessUnit* pAu)
    {
        uint32_t uiSucAuIdx = pAu->uiEndPos + 1;
        uint32_t uiCurAuIdx = 0;

        // swap the succeeding AU's nal units to the front
        while (uiSucAuIdx < pAu->uiAvailUnitsNum)
        {
            SNalUnit* t = pAu->pNalUnitsList[uiSucAuIdx];
            pAu->pNalUnitsList[uiSucAuIdx] = pAu->pNalUnitsList[uiCurAuIdx];
            pAu->pNalUnitsList[uiCurAuIdx] = t;
            ++uiSucAuIdx;
            ++uiCurAuIdx;
        }

        // Update avail/actual units num accordingly for next AU parsing
        if (pAu->uiAvailUnitsNum > pAu->uiEndPos)
            pAu->uiAvailUnitsNum -= (pAu->uiEndPos + 1);
        else
            pAu->uiAvailUnitsNum = 0;
        pAu->uiActualUnitsNum = 0;
        pAu->uiStartPos = 0;
        pAu->uiEndPos = 0;
        pAu->bCompletedAuFlag = false;
    }

    //clear current corrupted NAL from pNalUnitsList
    public static void ForceClearCurrentNal(SAccessUnit* pAu)
    {
        if (pAu->uiAvailUnitsNum > 0)
            --pAu->uiAvailUnitsNum;
    }

    public static void ForceResetParaSetStatusAndAUList(SWelsDecoderContext* pCtx)
    {
        pCtx->sSpsPpsCtx.bSpsExistAheadFlag = false;
        pCtx->sSpsPpsCtx.bSubspsExistAheadFlag = false;
        pCtx->sSpsPpsCtx.bPpsExistAheadFlag = false;

        // Force clear the AU list
        pCtx->pAccessUnitList->uiAvailUnitsNum = 0;
        pCtx->pAccessUnitList->uiActualUnitsNum = 0;
        pCtx->pAccessUnitList->uiStartPos = 0;
        pCtx->pAccessUnitList->uiEndPos = 0;
        pCtx->pAccessUnitList->bCompletedAuFlag = false;
    }

    public static void CheckAvailNalUnitsListContinuity(SWelsDecoderContext* pCtx, int32_t iStartIdx, int32_t iEndIdx)
    {
        SAccessUnit* pCurAu = pCtx->pAccessUnitList;

        uint8_t uiLastNuDependencyId, uiLastNuLayerDqId;
        uint8_t uiCurNuDependencyId, uiCurNuQualityId, uiCurNuLayerDqId, uiCurNuRefLayerDqId;

        int32_t iCurNalUnitIdx = 0;

        //check the continuity of pNalUnitsList forwards (from pIdxNoInterLayerPred to end_postion)
        uiLastNuDependencyId = pCurAu->pNalUnitsList[iStartIdx]->sNalHeaderExt.uiDependencyId;//starting nal unit
        uiLastNuLayerDqId = pCurAu->pNalUnitsList[iStartIdx]->sNalHeaderExt.uiLayerDqId;//starting nal unit
        iCurNalUnitIdx = iStartIdx + 1;//current nal unit
        while (iCurNalUnitIdx <= iEndIdx)
        {
            uiCurNuDependencyId = pCurAu->pNalUnitsList[iCurNalUnitIdx]->sNalHeaderExt.uiDependencyId;
            uiCurNuQualityId = pCurAu->pNalUnitsList[iCurNalUnitIdx]->sNalHeaderExt.uiQualityId;
            uiCurNuLayerDqId = pCurAu->pNalUnitsList[iCurNalUnitIdx]->sNalHeaderExt.uiLayerDqId;
            uiCurNuRefLayerDqId = pCurAu->pNalUnitsList[iCurNalUnitIdx]->sNalData.sVclNal.sSliceHeaderExt.uiRefLayerDqId;

            if (uiCurNuDependencyId == uiLastNuDependencyId)
            {
                uiLastNuLayerDqId = uiCurNuLayerDqId;
                ++iCurNalUnitIdx;
            }
            else
            { //uiCurNuDependencyId != uiLastNuDependencyId, new dependency arrive
                if (uiCurNuQualityId == 0)
                {
                    uiLastNuDependencyId = uiCurNuDependencyId;
                    if (uiCurNuRefLayerDqId == uiLastNuLayerDqId)
                    {
                        uiLastNuLayerDqId = uiCurNuLayerDqId;
                        ++iCurNalUnitIdx;
                    }
                    else
                    { //cur_nu_layer_id != next_nu_ref_layer_dq_id, the chain is broken at this point
                        break;
                    }
                }
                else
                { //new dependency arrive, but no base quality layer, so we must stop in this point
                    break;
                }
            }
        }

        --iCurNalUnitIdx;
        pCurAu->uiEndPos = (uint32_t)iCurNalUnitIdx;
        pCtx->uiTargetDqId = pCurAu->pNalUnitsList[iCurNalUnitIdx]->sNalHeaderExt.uiLayerDqId;
    }

    //main purpose: to support multi-slice and to include all slice which have the same uiDependencyId, uiQualityId and frame_num
    //for single slice, pIdxNoInterLayerPred SHOULD NOT be modified
    public static void RefineIdxNoInterLayerPred(SAccessUnit* pCurAu, int32_t* pIdxNoInterLayerPred)
    {
        int32_t iLastNalDependId = pCurAu->pNalUnitsList[*pIdxNoInterLayerPred]->sNalHeaderExt.uiDependencyId;
        int32_t iLastNalQualityId = pCurAu->pNalUnitsList[*pIdxNoInterLayerPred]->sNalHeaderExt.uiQualityId;
        uint8_t uiLastNalTId = pCurAu->pNalUnitsList[*pIdxNoInterLayerPred]->sNalHeaderExt.uiTemporalId;
        int32_t iLastNalFrameNum =
          pCurAu->pNalUnitsList[*pIdxNoInterLayerPred]->sNalData.sVclNal.sSliceHeaderExt.sSliceHeader.iFrameNum;
        int32_t iLastNalPoc =
          pCurAu->pNalUnitsList[*pIdxNoInterLayerPred]->sNalData.sVclNal.sSliceHeaderExt.sSliceHeader.iPicOrderCntLsb;
        int32_t iLastNalFirstMb =
          pCurAu->pNalUnitsList[*pIdxNoInterLayerPred]->sNalData.sVclNal.sSliceHeaderExt.sSliceHeader.iFirstMbInSlice;
        int32_t iCurNalDependId, iCurNalQualityId, iCurNalTId, iCurNalFrameNum, iCurNalPoc, iCurNalFirstMb, iCurIdx,
                iFinalIdxNoInterLayerPred;

        bool bMultiSliceFind = false;

        iFinalIdxNoInterLayerPred = 0;
        iCurIdx = *pIdxNoInterLayerPred - 1;
        while (iCurIdx >= 0)
        {
            if (pCurAu->pNalUnitsList[iCurIdx]->sNalHeaderExt.iNoInterLayerPredFlag != 0)
            {
                iCurNalDependId = pCurAu->pNalUnitsList[iCurIdx]->sNalHeaderExt.uiDependencyId;
                iCurNalQualityId = pCurAu->pNalUnitsList[iCurIdx]->sNalHeaderExt.uiQualityId;
                iCurNalTId = pCurAu->pNalUnitsList[iCurIdx]->sNalHeaderExt.uiTemporalId;
                iCurNalFrameNum = pCurAu->pNalUnitsList[iCurIdx]->sNalData.sVclNal.sSliceHeaderExt.sSliceHeader.iFrameNum;
                iCurNalPoc = pCurAu->pNalUnitsList[iCurIdx]->sNalData.sVclNal.sSliceHeaderExt.sSliceHeader.iPicOrderCntLsb;
                iCurNalFirstMb = pCurAu->pNalUnitsList[iCurIdx]->sNalData.sVclNal.sSliceHeaderExt.sSliceHeader.iFirstMbInSlice;

                if (iCurNalDependId == iLastNalDependId &&
                    iCurNalQualityId == iLastNalQualityId &&
                    iCurNalTId == uiLastNalTId &&
                    iCurNalFrameNum == iLastNalFrameNum &&
                    iCurNalPoc == iLastNalPoc &&
                    iCurNalFirstMb != iLastNalFirstMb)
                {
                    bMultiSliceFind = true;
                    iFinalIdxNoInterLayerPred = iCurIdx;
                    --iCurIdx;
                    continue;
                }
                else
                {
                    break;
                }
            }
            --iCurIdx;
        }

        if (bMultiSliceFind && *pIdxNoInterLayerPred != iFinalIdxNoInterLayerPred)
        {
            *pIdxNoInterLayerPred = iFinalIdxNoInterLayerPred;
        }
    }

    public static bool CheckPocOfCurValidNalUnits(SAccessUnit* pCurAu, int32_t pIdxNoInterLayerPred)
    {
        int32_t iEndIdx = (int32_t)pCurAu->uiEndPos;
        int32_t iCurAuPoc =
          pCurAu->pNalUnitsList[pIdxNoInterLayerPred]->sNalData.sVclNal.sSliceHeaderExt.sSliceHeader.iPicOrderCntLsb;
        int32_t iTmpPoc, i;
        for (i = pIdxNoInterLayerPred + 1; i < iEndIdx; i++)
        {
            iTmpPoc = pCurAu->pNalUnitsList[i]->sNalData.sVclNal.sSliceHeaderExt.sSliceHeader.iPicOrderCntLsb;
            if (iTmpPoc != iCurAuPoc)
            {
                return false;
            }
        }

        return true;
    }

    public static bool CheckIntegrityNalUnitsList(SWelsDecoderContext* pCtx)
    {
        SAccessUnit* pCurAu = pCtx->pAccessUnitList;
        int32_t kiEndPos = (int32_t)pCurAu->uiEndPos;
        int32_t iIdxNoInterLayerPred = 0;

        if (!pCurAu->bCompletedAuFlag)
            return false;

        if (pCtx->bNewSeqBegin)
        {
            pCurAu->uiStartPos = 0;
            //step1: search the pNalUnit whose iNoInterLayerPredFlag equal to 1 backwards (from uiEndPos to 0)
            iIdxNoInterLayerPred = kiEndPos;
            while (iIdxNoInterLayerPred >= 0)
            {
                if (pCurAu->pNalUnitsList[iIdxNoInterLayerPred]->sNalHeaderExt.iNoInterLayerPredFlag != 0)
                {
                    break;
                }
                --iIdxNoInterLayerPred;
            }
            if (iIdxNoInterLayerPred < 0)
            {
                //can not find the Nal Unit whose no_inter_pred_falg equal to 1, MUST STOP decode
                return false;
            }

            //step2: support multi-slice, to include all base layer slice
            RefineIdxNoInterLayerPred(pCurAu, &iIdxNoInterLayerPred);
            pCurAu->uiStartPos = (uint32_t)iIdxNoInterLayerPred;
            CheckAvailNalUnitsListContinuity(pCtx, iIdxNoInterLayerPred, kiEndPos);

            if (!CheckPocOfCurValidNalUnits(pCurAu, iIdxNoInterLayerPred))
            {
                return false;
            }

            pCtx->iCurSeqIntervalTargetDependId = pCurAu->pNalUnitsList[pCurAu->uiEndPos]->sNalHeaderExt.uiDependencyId;
            pCtx->iCurSeqIntervalMaxPicWidth =
              pCurAu->pNalUnitsList[pCurAu->uiEndPos]->sNalData.sVclNal.sSliceHeaderExt.sSliceHeader.iMbWidth << 4;
            pCtx->iCurSeqIntervalMaxPicHeight =
              pCurAu->pNalUnitsList[pCurAu->uiEndPos]->sNalData.sVclNal.sSliceHeaderExt.sSliceHeader.iMbHeight << 4;
        }
        else
        { //P_SLICE
            //step 1: search uiDependencyId equal to pCtx->cur_seq_interval_target_dependency_id
            bool bGetDependId = false;
            int32_t iIdxDependId = 0;

            iIdxDependId = kiEndPos;
            while (iIdxDependId >= 0)
            {
                if (pCtx->iCurSeqIntervalTargetDependId == pCurAu->pNalUnitsList[iIdxDependId]->sNalHeaderExt.uiDependencyId)
                {
                    bGetDependId = true;
                    break;
                }
                else
                {
                    --iIdxDependId;
                }
            }

            //step 2: switch according to whether or not find the index of pNalUnit whose uiDependencyId equal to iCurSeqIntervalTargetDependId
            if (bGetDependId)
            { //get the index of pNalUnit whose uiDependencyId equal to iCurSeqIntervalTargetDependId
                bool bGetNoInterPredFront = false;
                //step 2a: search iNoInterLayerPredFlag [0....iIdxDependId]
                iIdxNoInterLayerPred = iIdxDependId;
                while (iIdxNoInterLayerPred >= 0)
                {
                    if (pCurAu->pNalUnitsList[iIdxNoInterLayerPred]->sNalHeaderExt.iNoInterLayerPredFlag != 0)
                    {
                        bGetNoInterPredFront = true;
                        break;
                    }
                    --iIdxNoInterLayerPred;
                }
                //step 2b: switch, whether or not find the NAL unit whose no_inter_pred_flag equal to 1 among [0....iIdxDependId]
                if (bGetNoInterPredFront)
                { //YES
                    RefineIdxNoInterLayerPred(pCurAu, &iIdxNoInterLayerPred);
                    pCurAu->uiStartPos = (uint32_t)iIdxNoInterLayerPred;
                    CheckAvailNalUnitsListContinuity(pCtx, iIdxNoInterLayerPred, iIdxDependId);

                    if (!CheckPocOfCurValidNalUnits(pCurAu, iIdxNoInterLayerPred))
                    {
                        return false;
                    }
                }
                else
                { //NO, should find the NAL unit whose no_inter_pred_flag equal to 1 among [iIdxDependId....uiEndPos]
                    iIdxNoInterLayerPred = iIdxDependId;
                    while (iIdxNoInterLayerPred <= kiEndPos)
                    {
                        if (pCurAu->pNalUnitsList[iIdxNoInterLayerPred]->sNalHeaderExt.iNoInterLayerPredFlag != 0)
                        {
                            break;
                        }
                        ++iIdxNoInterLayerPred;
                    }

                    if (iIdxNoInterLayerPred > kiEndPos)
                    {
                        return false; //cann't find the index of pNalUnit whose no_inter_pred_flag = 1
                    }

                    RefineIdxNoInterLayerPred(pCurAu, &iIdxNoInterLayerPred);
                    pCurAu->uiStartPos = (uint32_t)iIdxNoInterLayerPred;
                    CheckAvailNalUnitsListContinuity(pCtx, iIdxNoInterLayerPred, kiEndPos);

                    if (!CheckPocOfCurValidNalUnits(pCurAu, iIdxNoInterLayerPred))
                    {
                        return false;
                    }
                }
            }
            else
            { //without the index of pNalUnit, should process this AU as common case
                iIdxNoInterLayerPred = kiEndPos;
                while (iIdxNoInterLayerPred >= 0)
                {
                    if (pCurAu->pNalUnitsList[iIdxNoInterLayerPred]->sNalHeaderExt.iNoInterLayerPredFlag != 0)
                    {
                        break;
                    }
                    --iIdxNoInterLayerPred;
                }
                if (iIdxNoInterLayerPred < 0)
                {
                    return false; //cann't find the index of pNalUnit whose iNoInterLayerPredFlag = 1
                }

                RefineIdxNoInterLayerPred(pCurAu, &iIdxNoInterLayerPred);
                pCurAu->uiStartPos = (uint32_t)iIdxNoInterLayerPred;
                CheckAvailNalUnitsListContinuity(pCtx, iIdxNoInterLayerPred, kiEndPos);

                if (!CheckPocOfCurValidNalUnits(pCurAu, iIdxNoInterLayerPred))
                {
                    return false;
                }
            }
        }

        return true;
    }

    public static void CheckOnlyOneLayerInAu(SWelsDecoderContext* pCtx)
    {
        SAccessUnit* pCurAu = pCtx->pAccessUnitList;

        int32_t iEndIdx = (int32_t)pCurAu->uiEndPos;
        int32_t iCurIdx = (int32_t)pCurAu->uiStartPos;
        uint8_t uiDId = pCurAu->pNalUnitsList[iCurIdx]->sNalHeaderExt.uiDependencyId;
        uint8_t uiQId = pCurAu->pNalUnitsList[iCurIdx]->sNalHeaderExt.uiQualityId;
        uint8_t uiTId = pCurAu->pNalUnitsList[iCurIdx]->sNalHeaderExt.uiTemporalId;

        uint8_t uiCurDId, uiCurQId, uiCurTId;

        pCtx->bOnlyOneLayerInCurAuFlag = true;

        if (iEndIdx == iCurIdx)
        { //only one NAL in pNalUnitsList
            return;
        }

        ++iCurIdx;
        while (iCurIdx <= iEndIdx)
        {
            uiCurDId = pCurAu->pNalUnitsList[iCurIdx]->sNalHeaderExt.uiDependencyId;
            uiCurQId = pCurAu->pNalUnitsList[iCurIdx]->sNalHeaderExt.uiQualityId;
            uiCurTId = pCurAu->pNalUnitsList[iCurIdx]->sNalHeaderExt.uiTemporalId;

            if (uiDId != uiCurDId || uiQId != uiCurQId || uiTId != uiCurTId)
            {
                pCtx->bOnlyOneLayerInCurAuFlag = false;
                return;
            }

            ++iCurIdx;
        }
    }

    public static int32_t WelsDecodeAccessUnitStart(SWelsDecoderContext* pCtx)
    {
        // Roll back NAL units not being belong to current access unit list for proceeded access unit
        int32_t iRet = UpdateAccessUnit(pCtx);
        if (iRet != ERR_NONE)
            return iRet;

        pCtx->pAccessUnitList->uiStartPos = 0;
        if (!pCtx->sSpsPpsCtx.bAvcBasedFlag && !CheckIntegrityNalUnitsList(pCtx))
        {
            pCtx->iErrorCode |= (int32_t)dsBitstreamError;
            return (int32_t)dsBitstreamError;
        }

        //check current AU has only one layer or not
        //If YES, can use deblocking based on AVC
        if (!pCtx->sSpsPpsCtx.bAvcBasedFlag)
        {
            CheckOnlyOneLayerInAu(pCtx);
        }

        return ERR_NONE;
    }

    public static void WelsDecodeAccessUnitEnd(SWelsDecoderContext* pCtx)
    {
        //save previous header info
        SAccessUnit* pCurAu = pCtx->pAccessUnitList;
        SNalUnit* pCurNal = pCurAu->pNalUnitsList[pCurAu->uiEndPos];
        memcpy(&pCtx->pLastDecPicInfo->sLastNalHdrExt, &pCurNal->sNalHeaderExt, sizeof(SNalUnitHeaderExt));
        memcpy(&pCtx->pLastDecPicInfo->sLastSliceHeader,
               &pCurNal->sNalData.sVclNal.sSliceHeaderExt.sSliceHeader, sizeof(SSliceHeader));
        // uninitialize context of current access unit and rbsp buffer clean
        ResetCurrentAccessUnit(pCtx);
    }

    /* CheckNewSeqBeginAndUpdateActiveLayerSps
     * return:
     * true - the AU to be construct is the start of new sequence; false - not
     */
    private static bool CheckNewSeqBeginAndUpdateActiveLayerSps(SWelsDecoderContext* pCtx)
    {
        bool bNewSeq = false;
        SAccessUnit* pCurAu = pCtx->pAccessUnitList;
        SSps** pTmpLayerSps = stackalloc SSps*[MAX_LAYER_NUM];
        for (int i = 0; i < MAX_LAYER_NUM; i++)
        {
            pTmpLayerSps[i] = null;
        }
        // track the layer sps for the current au
        for (uint i = pCurAu->uiStartPos; i <= pCurAu->uiEndPos; i++)
        {
            uint32_t uiDid = pCurAu->pNalUnitsList[i]->sNalHeaderExt.uiDependencyId;
            pTmpLayerSps[uiDid] = pCurAu->pNalUnitsList[i]->sNalData.sVclNal.sSliceHeaderExt.sSliceHeader.pSps;
            if ((pCurAu->pNalUnitsList[i]->sNalHeaderExt.sNalUnitHeader.eNalUnitType == NAL_UNIT_CODED_SLICE_IDR)
                || (pCurAu->pNalUnitsList[i]->sNalHeaderExt.bIdrFlag))
                bNewSeq = true;
        }
        int iMaxActiveLayer = 0, iMaxCurrentLayer = 0;
        for (int i = MAX_LAYER_NUM - 1; i >= 0; i--)
        {
            if (pCtx->sSpsPpsCtx.pActiveLayerSps[i].p != null)
            {
                iMaxActiveLayer = i;
                break;
            }
        }
        for (int i = MAX_LAYER_NUM - 1; i >= 0; i--)
        {
            if (pTmpLayerSps[i] != null)
            {
                iMaxCurrentLayer = i;
                break;
            }
        }
        if ((iMaxCurrentLayer != iMaxActiveLayer)
            || (pTmpLayerSps[iMaxCurrentLayer] != pCtx->sSpsPpsCtx.pActiveLayerSps[iMaxActiveLayer].p))
        {
            bNewSeq = true;
        }
        // fill active sps if the current sps is not null while active layer is null
        if (!bNewSeq)
        {
            for (int i = 0; i < MAX_LAYER_NUM; i++)
            {
                if (pCtx->sSpsPpsCtx.pActiveLayerSps[i].p == null && pTmpLayerSps[i] != null)
                {
                    pCtx->sSpsPpsCtx.pActiveLayerSps[i] = pTmpLayerSps[i];
                }
            }
        }
        else
        {
            // UpdateActiveLayerSps if new sequence start
            memcpy(&pCtx->sSpsPpsCtx.pActiveLayerSps, &pTmpLayerSps[0], MAX_LAYER_NUM * sizeof(SSps*));
        }
        return bNewSeq;
    }

    private static void WriteBackActiveParameters(SWelsDecoderContext* pCtx)
    {
        if ((pCtx->sSpsPpsCtx.iOverwriteFlags & OVERWRITE_PPS) != 0)
        {
            memcpy(&pCtx->sSpsPpsCtx.sPpsBuffer[pCtx->sSpsPpsCtx.sPpsBuffer[MAX_PPS_COUNT].iPpsId],
                   &pCtx->sSpsPpsCtx.sPpsBuffer[MAX_PPS_COUNT], sizeof(SPps));
        }
        if ((pCtx->sSpsPpsCtx.iOverwriteFlags & OVERWRITE_SPS) != 0)
        {
            memcpy(&pCtx->sSpsPpsCtx.sSpsBuffer[pCtx->sSpsPpsCtx.sSpsBuffer[MAX_SPS_COUNT].iSpsId],
                   &pCtx->sSpsPpsCtx.sSpsBuffer[MAX_SPS_COUNT], sizeof(SSps));
            pCtx->bNewSeqBegin = true;
        }
        if ((pCtx->sSpsPpsCtx.iOverwriteFlags & OVERWRITE_SUBSETSPS) != 0)
        {
            memcpy(&pCtx->sSpsPpsCtx.sSubsetSpsBuffer[pCtx->sSpsPpsCtx.sSubsetSpsBuffer[MAX_SPS_COUNT].sSps.iSpsId],
                   &pCtx->sSpsPpsCtx.sSubsetSpsBuffer[MAX_SPS_COUNT], sizeof(SSubsetSps));
            pCtx->bNewSeqBegin = true;
        }
        pCtx->sSpsPpsCtx.iOverwriteFlags = OVERWRITE_NONE;
    }

    /*
     * DecodeFinishUpdate
     * decoder finish decoding, update active parameter sets and new seq status
     *
     */

    public static void DecodeFinishUpdate(SWelsDecoderContext* pCtx)
    {
        pCtx->bNewSeqBegin = false;
        WriteBackActiveParameters(pCtx);
        pCtx->bNewSeqBegin = pCtx->bNewSeqBegin || pCtx->bNextNewSeqBegin;
        pCtx->bNextNewSeqBegin = false; // reset it
        if (pCtx->bNewSeqBegin)
            ResetActiveSPSForEachLayer(pCtx);
    }

    /*
    * WelsDecodeInitAccessUnitStart
    * check and (re)allocate picture buffers on new sequence begin
    *  bit_len:    size in bit length of data
    *  buf_len:    size in byte length of data
    *  coded_au:   mark an Access Unit decoding finished
    * return:
    *  0 - success; otherwise returned error_no defined in error_no.h
    */
    public static int32_t WelsDecodeInitAccessUnitStart(SWelsDecoderContext* pCtx, SBufferInfo* pDstInfo)
    {
        int32_t iErr = ERR_NONE;
        SAccessUnit* pCurAu = pCtx->pAccessUnitList;
        pCtx->bAuReadyFlag = false;
        pCtx->pLastDecPicInfo->bLastHasMmco5 = false;
        bool bTmpNewSeqBegin = CheckNewSeqBeginAndUpdateActiveLayerSps(pCtx);
        if (bTmpNewSeqBegin)
        {
            if (pCtx->pStreamSeqNum != null)
                (*pCtx->pStreamSeqNum)++;
            else
                pCtx->iSeqNum++;
        }
        pCtx->bNewSeqBegin = pCtx->bNewSeqBegin || bTmpNewSeqBegin;
        if (pCtx->pStreamSeqNum != null)
            pCtx->iSeqNum = *pCtx->pStreamSeqNum;
        iErr = WelsDecodeAccessUnitStart(pCtx);
        GetVclNalTemporalId(pCtx);

        if (ERR_NONE != iErr)
        {
            ForceResetCurrentAccessUnit(pCtx->pAccessUnitList);
            if (!pCtx->pParam->bParseOnly)
                pDstInfo->iBufferStatus = 0;
            pCtx->bNewSeqBegin = pCtx->bNewSeqBegin || pCtx->bNextNewSeqBegin;
            pCtx->bNextNewSeqBegin = false; // reset it
            if (pCtx->bNewSeqBegin)
                ResetActiveSPSForEachLayer(pCtx);
            return iErr;
        }

        pCtx->pSps = pCurAu->pNalUnitsList[pCurAu->uiStartPos]->sNalData.sVclNal.sSliceHeaderExt.sSliceHeader.pSps;
        pCtx->pPps = pCurAu->pNalUnitsList[pCurAu->uiStartPos]->sNalData.sVclNal.sSliceHeaderExt.sSliceHeader.pPps;

        return iErr;
    }

    // static inline in C. internal here (the class is) so the slice-parse
    // test driver can call it, as DecodeCurrentAccessUnit does.
    public static void InitDqLayerInfo(SDqLayer* pDqLayer, SLayerInfo* pLayerInfo, SNalUnit* pNalUnit, SPicture* pPicDec)
    {
        SNalUnitHeaderExt* pNalHdrExt = &pNalUnit->sNalHeaderExt;
        SSliceHeaderExt* pShExt = &pNalUnit->sNalData.sVclNal.sSliceHeaderExt;
        SSliceHeader* pSh = &pShExt->sSliceHeader;
        uint8_t kuiQualityId = pNalHdrExt->uiQualityId;

        memcpy(&pDqLayer->sLayerInfo, pLayerInfo, sizeof(SLayerInfo)); //confirmed_safe_unsafe_usage

        pDqLayer->pDec = pPicDec;
        pDqLayer->iMbWidth = pSh->iMbWidth;        // MB width of this picture
        pDqLayer->iMbHeight = pSh->iMbHeight;// MB height of this picture

        pDqLayer->iSliceIdcBackup = (pSh->iFirstMbInSlice << 7) | (pNalHdrExt->uiDependencyId << 4) | (pNalHdrExt->uiQualityId);

        /* Common syntax elements across all slices of a DQLayer */
        pDqLayer->uiPpsId = (uint32_t)pLayerInfo->pPps->iPpsId;
        pDqLayer->uiDisableInterLayerDeblockingFilterIdc = pShExt->uiDisableInterLayerDeblockingFilterIdc;
        pDqLayer->iInterLayerSliceAlphaC0Offset = pShExt->iInterLayerSliceAlphaC0Offset;
        pDqLayer->iInterLayerSliceBetaOffset = pShExt->iInterLayerSliceBetaOffset;
        pDqLayer->iSliceGroupChangeCycle = pSh->iSliceGroupChangeCycle;
        pDqLayer->bStoreRefBasePicFlag = pShExt->bStoreRefBasePicFlag;
        pDqLayer->bTCoeffLevelPredFlag = pShExt->bTCoeffLevelPredFlag;
        pDqLayer->bConstrainedIntraResamplingFlag = pShExt->bConstrainedIntraResamplingFlag;
        pDqLayer->uiRefLayerDqId = pShExt->uiRefLayerDqId;
        pDqLayer->uiRefLayerChromaPhaseXPlus1Flag = pShExt->uiRefLayerChromaPhaseXPlus1Flag;
        pDqLayer->uiRefLayerChromaPhaseYPlus1 = pShExt->uiRefLayerChromaPhaseYPlus1;
        pDqLayer->bUseWeightPredictionFlag = false;
        pDqLayer->bUseWeightedBiPredIdc = false;
        //memcpy(&pDqLayer->sScaledRefLayer, &pShExt->sScaledRefLayer, sizeof(SPosOffset));//confirmed_safe_unsafe_usage

        if (kuiQualityId == BASE_QUALITY_ID)
        {
            pDqLayer->pRefPicListReordering = &pSh->pRefPicListReordering;
            pDqLayer->pRefPicMarking = &pSh->sRefMarking;

            pDqLayer->bUseWeightPredictionFlag = pSh->pPps->bWeightedPredFlag;
            pDqLayer->bUseWeightedBiPredIdc = pSh->pPps->uiWeightedBipredIdc != 0;
            if (pSh->pPps->bWeightedPredFlag || pSh->pPps->uiWeightedBipredIdc != 0)
            {
                pDqLayer->pPredWeightTable = &pSh->sPredWeightTable;
            }
            pDqLayer->pRefPicBaseMarking = &pShExt->sRefBasePicMarking;
        }

        pDqLayer->uiLayerDqId = pNalHdrExt->uiLayerDqId;      // dq_id of current layer
        pDqLayer->bUseRefBasePicFlag = pNalHdrExt->bUseRefBasePicFlag;
    }

    public static void WelsDqLayerDecodeStart(SWelsDecoderContext* pCtx, SNalUnit* pCurNal, SSps* pSps, SPps* pPps)
    {
        SSliceHeader* pSh = &pCurNal->sNalData.sVclNal.sSliceHeaderExt.sSliceHeader;

        pCtx->eSliceType = pSh->eSliceType;
        pCtx->pSliceHeader = pSh;
        pCtx->bUsedAsRef = false;

        pCtx->iFrameNum = pSh->iFrameNum;
        UpdateDecoderStatisticsForActiveParaset(pCtx->pDecoderStatistics, pSps, pPps);
    }

    public static void InitCurDqLayerData(SWelsDecoderContext* pCtx, SDqLayer* pCurDq)
    {
        if (null != pCtx && null != pCurDq)
        {
            pCurDq->pMbType = pCtx->sMb.pMbType[0];
            pCurDq->pSliceIdc = pCtx->sMb.pSliceIdc[0];
            pCurDq->pMv[(int32_t)LIST_0] = pCtx->sMb.pMv[0][(int32_t)LIST_0];
            pCurDq->pMv[(int32_t)LIST_1] = pCtx->sMb.pMv[0][(int32_t)LIST_1];
            pCurDq->pRefIndex[(int32_t)LIST_0] = pCtx->sMb.pRefIndex[0][(int32_t)LIST_0];
            pCurDq->pRefIndex[(int32_t)LIST_1] = pCtx->sMb.pRefIndex[0][(int32_t)LIST_1];
            pCurDq->pDirect = pCtx->sMb.pDirect[0];
            pCurDq->pNoSubMbPartSizeLessThan8x8Flag = pCtx->sMb.pNoSubMbPartSizeLessThan8x8Flag[0];
            pCurDq->pTransformSize8x8Flag = pCtx->sMb.pTransformSize8x8Flag[0];
            pCurDq->pLumaQp = pCtx->sMb.pLumaQp[0];
            pCurDq->pChromaQp = pCtx->sMb.pChromaQp[0];
            pCurDq->pMvd[(int32_t)LIST_0] = pCtx->sMb.pMvd[0][(int32_t)LIST_0];
            pCurDq->pMvd[(int32_t)LIST_1] = pCtx->sMb.pMvd[0][(int32_t)LIST_1];
            pCurDq->pCbfDc = pCtx->sMb.pCbfDc[0];
            pCurDq->pNzc = pCtx->sMb.pNzc[0];
            pCurDq->pNzcRs = pCtx->sMb.pNzcRs[0];
            pCurDq->pScaledTCoeff = pCtx->sMb.pScaledTCoeff[0];
            pCurDq->pIntraPredMode = pCtx->sMb.pIntraPredMode[0];
            pCurDq->pIntra4x4FinalMode = pCtx->sMb.pIntra4x4FinalMode[0];
            pCurDq->pIntraNxNAvailFlag = pCtx->sMb.pIntraNxNAvailFlag[0];
            pCurDq->pChromaPredMode = pCtx->sMb.pChromaPredMode[0];
            pCurDq->pCbp = pCtx->sMb.pCbp[0];
            pCurDq->pSubMbType = pCtx->sMb.pSubMbType[0];
            pCurDq->pInterPredictionDoneFlag = pCtx->sMb.pInterPredictionDoneFlag[0];
            pCurDq->pResidualPredFlag = pCtx->sMb.pResidualPredFlag[0];
            pCurDq->pMbCorrectlyDecodedFlag = pCtx->sMb.pMbCorrectlyDecodedFlag[0];
            pCurDq->pMbRefConcealedFlag = pCtx->sMb.pMbRefConcealedFlag[0];
        }
    }
}
