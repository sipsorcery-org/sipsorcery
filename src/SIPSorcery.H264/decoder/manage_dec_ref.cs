//-----------------------------------------------------------------------------
// Filename: manage_dec_ref.cs
//
// Description: Port of codec/decoder/core/inc/manage_dec_ref.h and
// src/manage_dec_ref.cpp: the reference picture lists (initialisation,
// reordering, marking by sliding window or MMCO, reset).
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// LONG_TERM_REF is defined (decoder_context.h), so its branches are ported.
// WelsInitBSliceRefList (B slices) and WelsReorderRefList2 (used only for
// CABAC streams outside the Baseline profile) are not ported yet: B slices and
// CABAC are not, and InitRefPicList fails loudly where it would call them.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 25 Nov 2026  Claude          Created (D4).
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
// The ported code is also covered by the OpenH264 BSD 2-Clause notice below.
//-----------------------------------------------------------------------------

/*!
 * \copy
 *     Copyright (c)  2008-2013, Cisco Systems
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
 *  manage_dec_ref.cpp
 *
 *  Abstract
 *      Implementation for managing reference picture
 *
 *  History
 *      07/21/2008 Created
 *
 *****************************************************************************/

namespace SIPSorcery.H264.Decoder;

internal static unsafe partial class WelsDec
{
    // The [LIST_0] row of one of SRefPic's picture lists, as C's PPicture*.
    private static SPicture** ShortList0(SRefPic* pRefPic) => (SPicture**)&pRefPic->pShortRefList[(int32_t)LIST_0];
    private static SPicture** LongList0(SRefPic* pRefPic) => (SPicture**)&pRefPic->pLongRefList[(int32_t)LIST_0];
    private static SPicture** RefList(SRefPic* pRefPic, int32_t listIdx) => (SPicture**)&pRefPic->pRefList[listIdx];

    private static void SetUnRef(SPicture* pRef)
    {
        if (pRef == null)
        {
            return;
        }

        if (pRef->iRefCount <= 0)
        {
            pRef->bUsedAsRef = false;
            pRef->bIsLongRef = false;
            pRef->iFrameNum = -1;
            pRef->iFrameWrapNum = -1;
            //pRef->iFramePoc = 0;
            pRef->iLongTermFrameIdx = -1;
            pRef->uiLongTermPicNum = 0;
            pRef->uiQualityId = unchecked((uint8_t)(-1));
            pRef->uiTemporalId = unchecked((uint8_t)(-1));
            pRef->uiSpatialId = unchecked((uint8_t)(-1));
            pRef->iSpsId = -1;
            pRef->bIsComplete = false;
            pRef->iRefCount = 0;
            pRef->pSetUnRef = null;

            if (pRef->eSliceType == I_SLICE)
            {
                return;
            }
            int32_t lists = pRef->eSliceType == P_SLICE ? 1 : 2;
            for (int32_t i = 0; i < MAX_DPB_COUNT; ++i)
            {
                for (int32_t list = 0; list < lists; ++list)
                {
                    pRef->pRefPic[list][i] = null;
                }
            }
        }
        else
        {
            pRef->pSetUnRef = &SetUnRef;
        }
    }

    //reset pRefList when
    // 1.sps arrived that is new sequence starting
    // 2.IDR NAL i.e. 1st layer in IDR AU

    public static void WelsResetRefPic(SWelsDecoderContext* pCtx)
    {
        int32_t i = 0;
        SRefPic* pRefPic = &pCtx->sRefPic;
        pCtx->sRefPic.uiLongRefCount[(int32_t)LIST_0] = pCtx->sRefPic.uiShortRefCount[(int32_t)LIST_0] = 0;

        pRefPic->uiRefCount[(int32_t)LIST_0] = 0;
        pRefPic->uiRefCount[(int32_t)LIST_1] = 0;

        SPicture** ppShortRefList = ShortList0(pRefPic);
        for (i = 0; i < MAX_DPB_COUNT; i++)
        {
            if (ppShortRefList[i] != null)
            {
                SetUnRef(ppShortRefList[i]);
                ppShortRefList[i] = null;
            }
        }
        pRefPic->uiShortRefCount[(int32_t)LIST_0] = 0;

        SPicture** ppLongRefList = LongList0(pRefPic);
        for (i = 0; i < MAX_DPB_COUNT; i++)
        {
            if (ppLongRefList[i] != null)
            {
                SetUnRef(ppLongRefList[i]);
                ppLongRefList[i] = null;
            }
        }
        pRefPic->uiLongRefCount[(int32_t)LIST_0] = 0;
    }

    //Release what a whole-struct copy of SRefPic is about to drop. Every picture the destination
    //still lists, and the source does not, leaves the reference lists when the copy lands; without
    //this it keeps bUsedAsRef set with no list entry and no armed unref, and its buffer never
    //returns to the pool.
    //Is this pointer still one of the pictures the buffer owns? A context that has been overtaken
    //by a DPB reallocation can list pictures that no longer exist, and those must not be touched.
    private static bool IsLivePicture(SPicBuff* pPicBuf, SPicture* pPic)
    {
        if (pPicBuf == null || pPic == null)
        {
            return false;
        }
        for (int32_t i = 0; i < pPicBuf->iCapacity; ++i)
        {
            if (pPicBuf->ppPic[i] == pPic)
            {
                return true;
            }
        }
        return false;
    }

    public static void WelsReleaseDroppedRefs(SPicBuff* pPicBuf, SRefPic* pDst, SRefPic* pSrc)
    {
        if (pPicBuf == null || pDst == null || pSrc == null || pDst == pSrc)
        {
            return;
        }
        int32_t kiShort = WELS_MIN((int32_t)pDst->uiShortRefCount[(int32_t)LIST_0], MAX_DPB_COUNT);
        int32_t kiLong = WELS_MIN((int32_t)pDst->uiLongRefCount[(int32_t)LIST_0], MAX_DPB_COUNT);
        SPicture** pHeld = stackalloc SPicture*[MAX_DPB_COUNT * 2];
        int32_t iHeld = 0;
        SPicture** ppDstShort = ShortList0(pDst);
        SPicture** ppDstLong = LongList0(pDst);
        for (int32_t i = 0; i < kiShort; ++i)
        {
            if (IsLivePicture(pPicBuf, ppDstShort[i]))
            {
                pHeld[iHeld++] = ppDstShort[i];
            }
        }
        for (int32_t i = 0; i < kiLong; ++i)
        {
            if (IsLivePicture(pPicBuf, ppDstLong[i]))
            {
                pHeld[iHeld++] = ppDstLong[i];
            }
        }

        SPicture** ppSrcShort = ShortList0(pSrc);
        SPicture** ppSrcLong = LongList0(pSrc);
        for (int32_t i = 0; i < iHeld; ++i)
        {
            SPicture* pPic = pHeld[i];
            if (!pPic->bUsedAsRef)
            {
                continue;
            }
            bool bKept = false;
            int32_t kiSrcShort = WELS_MIN((int32_t)pSrc->uiShortRefCount[(int32_t)LIST_0], MAX_DPB_COUNT);
            int32_t kiSrcLong = WELS_MIN((int32_t)pSrc->uiLongRefCount[(int32_t)LIST_0], MAX_DPB_COUNT);
            for (int32_t j = 0; !bKept && j < kiSrcShort; ++j)
            {
                bKept = (ppSrcShort[j] == pPic);
            }
            for (int32_t j = 0; !bKept && j < kiSrcLong; ++j)
            {
                bKept = (ppSrcLong[j] == pPic);
            }
            if (!bKept)
            {
                SetUnRef(pPic);
            }
        }
    }

    public static void WelsResetRefPicWithoutUnRef(SWelsDecoderContext* pCtx)
    {
        int32_t i = 0;
        SRefPic* pRefPic = &pCtx->sRefPic;
        pCtx->sRefPic.uiLongRefCount[(int32_t)LIST_0] = pCtx->sRefPic.uiShortRefCount[(int32_t)LIST_0] = 0;

        pRefPic->uiRefCount[(int32_t)LIST_0] = 0;
        pRefPic->uiRefCount[(int32_t)LIST_1] = 0;

        SPicture** ppShortRefList = ShortList0(pRefPic);
        for (i = 0; i < MAX_DPB_COUNT; i++)
        {
            ppShortRefList[i] = null;
        }
        pRefPic->uiShortRefCount[(int32_t)LIST_0] = 0;

        SPicture** ppLongRefList = LongList0(pRefPic);
        for (i = 0; i < MAX_DPB_COUNT; i++)
        {
            ppLongRefList[i] = null;
        }
        pRefPic->uiLongRefCount[(int32_t)LIST_0] = 0;
    }

    private static int32_t WelsCheckAndRecoverForFutureDecoding(SWelsDecoderContext* pCtx)
    {
        if ((pCtx->sRefPic.uiShortRefCount[(int32_t)LIST_0] + pCtx->sRefPic.uiLongRefCount[(int32_t)LIST_0] <= 0)
            && (pCtx->eSliceType != I_SLICE
                && pCtx->eSliceType != SI_SLICE))
        {
            if (pCtx->pParam->eEcActiveIdc !=
                ERROR_CON_DISABLE)
            { //IDR lost!, recover it for future decoding with data all set to 0
                SPicture* pRef = PrefetchPic(pCtx->pPicBuff);
                if (pRef != null)
                {
                    // IDR lost, set new
                    pRef->bIsComplete = false; // Set complete flag to false for lost IDR ref picture
                    pRef->iSpsId = pCtx->pSps->iSpsId;
                    pRef->iPpsId = pCtx->pPps->iPpsId;
                    if (pCtx->eSliceType == B_SLICE)
                    {
                        //reset reference's references when IDR is lost
                        for (int32_t list = (int32_t)LIST_0; list < (int32_t)LIST_A; ++list)
                        {
                            for (int32_t i = 0; i < MAX_DPB_COUNT; ++i)
                            {
                                pRef->pRefPic[list][i] = null;
                            }
                        }
                    }
                    pCtx->iErrorCode |= (int32_t)dsDataErrorConcealed;
                    bool bCopyPrevious = ((ERROR_CON_FRAME_COPY_CROSS_IDR == pCtx->pParam->eEcActiveIdc)
                                          || (ERROR_CON_SLICE_COPY_CROSS_IDR == pCtx->pParam->eEcActiveIdc)
                                          || (ERROR_CON_SLICE_COPY_CROSS_IDR_FREEZE_RES_CHANGE == pCtx->pParam->eEcActiveIdc)
                                          || (ERROR_CON_SLICE_MV_COPY_CROSS_IDR == pCtx->pParam->eEcActiveIdc)
                                          || (ERROR_CON_SLICE_MV_COPY_CROSS_IDR_FREEZE_RES_CHANGE == pCtx->pParam->eEcActiveIdc))
                                         && (null != pCtx->pLastDecPicInfo->pPreviousDecodedPictureInDpb);
                    bCopyPrevious = bCopyPrevious
                                    && (pRef->iWidthInPixel == pCtx->pLastDecPicInfo->pPreviousDecodedPictureInDpb->iWidthInPixel)
                                    && (pRef->iHeightInPixel == pCtx->pLastDecPicInfo->pPreviousDecodedPictureInDpb->iHeightInPixel);

                    if (!bCopyPrevious)
                    {
                        memset(pRef->pData[0], 128, pRef->iLinesize[0] * pRef->iHeightInPixel);
                        memset(pRef->pData[1], 128, pRef->iLinesize[1] * pRef->iHeightInPixel / 2);
                        memset(pRef->pData[2], 128, pRef->iLinesize[2] * pRef->iHeightInPixel / 2);
                    }
                    else if (pRef == pCtx->pLastDecPicInfo->pPreviousDecodedPictureInDpb)
                    {
                        WelsLog(&(pCtx->sLogCtx), WELS_LOG_WARNING, "WelsInitRefList()::EC memcpy overlap.");
                    }
                    else
                    {
                        memcpy(pRef->pData[0], pCtx->pLastDecPicInfo->pPreviousDecodedPictureInDpb->pData[0],
                            pRef->iLinesize[0] * pRef->iHeightInPixel);
                        memcpy(pRef->pData[1], pCtx->pLastDecPicInfo->pPreviousDecodedPictureInDpb->pData[1],
                            pRef->iLinesize[1] * pRef->iHeightInPixel / 2);
                        memcpy(pRef->pData[2], pCtx->pLastDecPicInfo->pPreviousDecodedPictureInDpb->pData[2],
                            pRef->iLinesize[2] * pRef->iHeightInPixel / 2);
                    }
                    pRef->iFrameNum = 0;
                    pRef->iFramePoc = 0;
                    pRef->uiTemporalId = pRef->uiQualityId = 0;
                    pRef->eSliceType = pCtx->eSliceType;
                    ExpandReferencingPicture((uint8_t**)&pRef->pData, pRef->iWidthInPixel, pRef->iHeightInPixel, pRef->iLinesize,
                        pCtx->sExpandPicFunc.pfExpandLumaPicture, pCtx->sExpandPicFunc.pfExpandChromaPicture);
                    AddShortTermToList(&pCtx->sRefPic, pRef);
                }
                else
                {
                    WelsLog(&(pCtx->sLogCtx), WELS_LOG_ERROR, "WelsInitRefList()::PrefetchPic for EC errors.");
                    pCtx->iErrorCode |= (int32_t)dsOutOfMemory;
                    return ERR_INFO_REF_COUNT_OVERFLOW;
                }
            }
        }
        return ERR_NONE;
    }

    private static void WrapShortRefPicNum(SWelsDecoderContext* pCtx)
    {
        int32_t i;
        SSliceHeader* pSliceHeader = &pCtx->pCurDqLayer->sLayerInfo.sSliceInLayer.sSliceHeaderExt.sSliceHeader;
        int32_t iMaxPicNum = 1 << (int32_t)pSliceHeader->pSps->uiLog2MaxFrameNum;
        SPicture** ppShoreRefList = ShortList0(&pCtx->sRefPic);
        int32_t iShortRefCount = pCtx->sRefPic.uiShortRefCount[(int32_t)LIST_0];
        //wrap pic num
        for (i = 0; i < iShortRefCount; i++)
        {
            if (ppShoreRefList[i] != null)
            {
                if (ppShoreRefList[i]->iFrameNum > pSliceHeader->iFrameNum)
                {
                    ppShoreRefList[i]->iFrameWrapNum = ppShoreRefList[i]->iFrameNum - iMaxPicNum;
                }
                else
                {
                    ppShoreRefList[i]->iFrameWrapNum = ppShoreRefList[i]->iFrameNum;
                }
            }
        }
    }

    // int32_t WelsInitBSliceRefList (PWelsDecoderContext pCtx, int32_t iPoc): B slices, not ported yet.

    /**
     * fills the pRefPic.pRefList.
     */
    public static int32_t WelsInitRefList(SWelsDecoderContext* pCtx, int32_t iPoc)
    {
        int32_t err = WelsCheckAndRecoverForFutureDecoding(pCtx);
        if (err != ERR_NONE)
        {
            return err;
        }

        WrapShortRefPicNum(pCtx);

        SPicture** ppShoreRefList = ShortList0(&pCtx->sRefPic);
        SPicture** ppLongRefList = LongList0(&pCtx->sRefPic);
        SPicture** ppRefList = RefList(&pCtx->sRefPic, (int32_t)LIST_0);
        memset(ppRefList, 0, MAX_DPB_COUNT * sizeof(SPicture*));

        int32_t i, iCount = 0;
        //short
        for (i = 0; i < pCtx->sRefPic.uiShortRefCount[(int32_t)LIST_0] && iCount < MAX_REF_PIC_COUNT; ++i)
        {
            ppRefList[iCount++] = ppShoreRefList[i];
        }

        //long
        for (i = 0; i < pCtx->sRefPic.uiLongRefCount[(int32_t)LIST_0] && iCount < MAX_REF_PIC_COUNT; ++i)
        {
            ppRefList[iCount++] = ppLongRefList[i];
        }
        pCtx->sRefPic.uiRefCount[(int32_t)LIST_0] = (uint8_t)iCount;

        return ERR_NONE;
    }

    public static int32_t WelsReorderRefList(SWelsDecoderContext* pCtx)
    {
        if (pCtx->eSliceType == I_SLICE || pCtx->eSliceType == SI_SLICE)
        {
            return ERR_NONE;
        }

        SRefPicListReorderSyn* pRefPicListReorderSyn = pCtx->pCurDqLayer->pRefPicListReordering;
        SNalUnitHeaderExt* pNalHeaderExt = &pCtx->pCurDqLayer->sLayerInfo.sNalHeaderExt;
        SSliceHeader* pSliceHeader = &pCtx->pCurDqLayer->sLayerInfo.sSliceInLayer.sSliceHeaderExt.sSliceHeader;
        int32_t ListCount = 1;
        if (pCtx->eSliceType == B_SLICE)
        {
            ListCount = 2;
        }
        for (int32_t listIdx = 0; listIdx < ListCount; ++listIdx)
        {
            SPicture* pPic = null;
            SPicture** ppRefList = RefList(&pCtx->sRefPic, listIdx);
            int32_t iMaxRefIdx = pCtx->iPicQueueNumber;
            if (iMaxRefIdx > MAX_REF_PIC_COUNT)
            {
                iMaxRefIdx = MAX_REF_PIC_COUNT;
            }
            int32_t iRefCount = pSliceHeader->uiRefCount[listIdx];
            int32_t iPredFrameNum = pSliceHeader->iFrameNum;
            int32_t iMaxPicNum = 1 << (int32_t)pSliceHeader->pSps->uiLog2MaxFrameNum;
            int32_t iAbsDiffPicNum = -1;
            int32_t iReorderingIndex = 0;
            int32_t i = 0;

            if (iRefCount <= 0)
            {
                pCtx->iErrorCode = (int32_t)dsNoParamSets; //No any reference for decoding, SHOULD request IDR
                return ERR_INFO_REFERENCE_PIC_LOST;
            }

            if (pRefPicListReorderSyn->bRefPicListReorderingFlag[listIdx])
            {
                while ((iReorderingIndex <= iMaxRefIdx)
                       && (pRefPicListReorderSyn->sReorderingSyn[listIdx][iReorderingIndex].uiReorderingOfPicNumsIdc != 3))
                {
                    uint16_t uiReorderingOfPicNumsIdc =
                        pRefPicListReorderSyn->sReorderingSyn[listIdx][iReorderingIndex].uiReorderingOfPicNumsIdc;
                    if (uiReorderingOfPicNumsIdc < 2)
                    {
                        iAbsDiffPicNum = (int32_t)(pRefPicListReorderSyn->sReorderingSyn[listIdx][iReorderingIndex].uiAbsDiffPicNumMinus1 + 1);

                        if (uiReorderingOfPicNumsIdc == 0)
                        {
                            iPredFrameNum -= iAbsDiffPicNum;
                        }
                        else
                        {
                            iPredFrameNum += iAbsDiffPicNum;
                        }
                        iPredFrameNum &= iMaxPicNum - 1;

                        for (i = iMaxRefIdx - 1; i >= 0; i--)
                        {
                            if (ppRefList[i] != null && ppRefList[i]->iFrameNum == iPredFrameNum && !ppRefList[i]->bIsLongRef)
                            {
                                if ((pNalHeaderExt->uiQualityId == ppRefList[i]->uiQualityId)
                                    && (pSliceHeader->iSpsId != ppRefList[i]->iSpsId))
                                { //check;
                                    WelsLog(&(pCtx->sLogCtx), WELS_LOG_WARNING,
                                        $"WelsReorderRefList()::::BASE LAYER::::iSpsId:{pSliceHeader->iSpsId}, ref_sps_id:{ppRefList[i]->iSpsId}");
                                    pCtx->iErrorCode = (int32_t)dsNoParamSets; //cross-IDR reference frame selection, SHOULD request IDR.--
                                    return ERR_INFO_REFERENCE_PIC_LOST;
                                }
                                else
                                {
                                    break;
                                }
                            }
                        }

                    }
                    else if (uiReorderingOfPicNumsIdc == 2)
                    {
                        for (i = iMaxRefIdx - 1; i >= 0; i--)
                        {
                            if (ppRefList[i] != null && ppRefList[i]->bIsLongRef
                                && ppRefList[i]->iLongTermFrameIdx ==
                                pRefPicListReorderSyn->sReorderingSyn[listIdx][iReorderingIndex].uiLongTermPicNum)
                            {
                                if ((pNalHeaderExt->uiQualityId == ppRefList[i]->uiQualityId)
                                    && (pSliceHeader->iSpsId != ppRefList[i]->iSpsId))
                                { //check;
                                    WelsLog(&(pCtx->sLogCtx), WELS_LOG_WARNING,
                                        $"WelsReorderRefList()::::BASE LAYER::::iSpsId:{pSliceHeader->iSpsId}, ref_sps_id:{ppRefList[i]->iSpsId}");
                                    pCtx->iErrorCode = (int32_t)dsNoParamSets; //cross-IDR reference frame selection, SHOULD request IDR.--
                                    return ERR_INFO_REFERENCE_PIC_LOST;
                                }
                                else
                                {
                                    break;
                                }
                            }
                        }
                    }
                    if (i < 0)
                    {
                        return ERR_INFO_REFERENCE_PIC_LOST;
                    }
                    pPic = ppRefList[i];
                    if (i > iReorderingIndex)
                    {
                        memmove(&ppRefList[1 + iReorderingIndex], &ppRefList[iReorderingIndex],
                            (i - iReorderingIndex) * sizeof(SPicture*)); //confirmed_safe_unsafe_usage
                    }
                    else if (i < iReorderingIndex)
                    {
                        memmove(&ppRefList[1 + iReorderingIndex], &ppRefList[iReorderingIndex],
                            (iMaxRefIdx - iReorderingIndex) * sizeof(SPicture*));
                    }
                    ppRefList[iReorderingIndex] = pPic;
                    iReorderingIndex++;
                }
            }
        }
        return ERR_NONE;
    }

    // int32_t WelsReorderRefList2 (PWelsDecoderContext pCtx): upstream's "test code", used
    // by InitRefPicList only for CABAC streams outside the Baseline profile; not ported yet.

    public static int32_t WelsMarkAsRef(SWelsDecoderContext* pCtx, SPicture* pLastDec = null)
    {
        SPicture* pDec = pLastDec;
        bool isThreadCtx = true;
        if (pDec == null)
        {
            pDec = pCtx->pDec;
            isThreadCtx = false;
        }
        SRefPic* pRefPic = isThreadCtx ? &pCtx->sTmpRefPic : &pCtx->sRefPic;
        SRefPicMarking* pRefPicMarking = pCtx->pCurDqLayer->pRefPicMarking;
        SAccessUnit* pCurAU = pCtx->pAccessUnitList;
        bool bIsIDRAU = false;
        uint32_t j;

        int32_t iRet = ERR_NONE;

        pDec->uiQualityId = pCtx->pCurDqLayer->sLayerInfo.sNalHeaderExt.uiQualityId;
        pDec->uiTemporalId = pCtx->pCurDqLayer->sLayerInfo.sNalHeaderExt.uiTemporalId;
        pDec->iSpsId = pCtx->pSps->iSpsId;
        pDec->iPpsId = pCtx->pPps->iPpsId;

        for (j = pCurAU->uiStartPos; j <= pCurAU->uiEndPos; j++)
        {
            if (pCurAU->pNalUnitsList[j]->sNalHeaderExt.sNalUnitHeader.eNalUnitType == NAL_UNIT_CODED_SLICE_IDR
                || pCurAU->pNalUnitsList[j]->sNalHeaderExt.bIdrFlag)
            {
                bIsIDRAU = true;
                break;
            }
        }
        if (bIsIDRAU)
        {
            if (pRefPicMarking->bLongTermRefFlag)
            {
                pRefPic->iMaxLongTermFrameIdx = 0;
                AddLongTermToList(pRefPic, pDec, 0, 0);
            }
            else
            {
                pRefPic->iMaxLongTermFrameIdx = -1;
            }
        }
        else
        {
            if (pRefPicMarking->bAdaptiveRefPicMarkingModeFlag)
            {
                iRet = MMCO(pCtx, pRefPic, pRefPicMarking);
                if (iRet != ERR_NONE)
                {
                    if (pCtx->pParam->eEcActiveIdc != ERROR_CON_DISABLE)
                    {
                        iRet = RemainOneBufferInDpbForEC(pCtx, pRefPic);
                        if (iRet != 0)
                        {
                            return iRet; // WELS_VERIFY_RETURN_IF (iRet, iRet)
                        }
                    }
                    else
                    {
                        return iRet;
                    }
                }

                if (pCtx->pLastDecPicInfo->bLastHasMmco5)
                {
                    pDec->iFrameNum = 0;
                    pDec->iFramePoc = 0;
                }

            }
            else
            {
                iRet = SlidingWindow(pCtx, pRefPic);
                if (iRet != ERR_NONE)
                {
                    if (pCtx->pParam->eEcActiveIdc != ERROR_CON_DISABLE)
                    {
                        iRet = RemainOneBufferInDpbForEC(pCtx, pRefPic);
                        if (iRet != 0)
                        {
                            return iRet; // WELS_VERIFY_RETURN_IF (iRet, iRet)
                        }
                    }
                    else
                    {
                        return iRet;
                    }
                }
            }
        }

        if (!pDec->bIsLongRef)
        {
            if (pRefPic->uiLongRefCount[(int32_t)LIST_0] + pRefPic->uiShortRefCount[(int32_t)LIST_0] >= WELS_MAX(1, pCtx->pSps->iNumRefFrames))
            {
                if (pCtx->pParam->eEcActiveIdc != ERROR_CON_DISABLE)
                {
                    iRet = RemainOneBufferInDpbForEC(pCtx, pRefPic);
                    if (iRet != 0)
                    {
                        return iRet; // WELS_VERIFY_RETURN_IF (iRet, iRet)
                    }
                }
                else
                {
                    return ERR_INFO_INVALID_MMCO_REF_NUM_OVERFLOW;
                }
            }
            iRet = AddShortTermToList(pRefPic, pDec);
        }

        return iRet;
    }

    private static int32_t MMCO(SWelsDecoderContext* pCtx, SRefPic* pRefPic, SRefPicMarking* pRefPicMarking)
    {
        SSps* pSps = pCtx->pCurDqLayer->sLayerInfo.pSps;
        int32_t i = 0;
        int32_t iRet = ERR_NONE;
        for (i = 0; i < MAX_MMCO_COUNT && pRefPicMarking->sMmcoRef[i].uiMmcoType != (uint32_t)MMCO_END; i++)
        {
            uint32_t uiMmcoType = pRefPicMarking->sMmcoRef[i].uiMmcoType;
            int32_t iShortFrameNum = (pCtx->iFrameNum - pRefPicMarking->sMmcoRef[i].iDiffOfPicNum) & ((
                                         1 << (int32_t)pSps->uiLog2MaxFrameNum) - 1);
            uint32_t uiLongTermPicNum = pRefPicMarking->sMmcoRef[i].uiLongTermPicNum;
            int32_t iLongTermFrameIdx = pRefPicMarking->sMmcoRef[i].iLongTermFrameIdx;
            int32_t iMaxLongTermFrameIdx = pRefPicMarking->sMmcoRef[i].iMaxLongTermFrameIdx;
            if (uiMmcoType > (uint32_t)MMCO_LONG)
            {
                return ERR_INFO_INVALID_MMCO_OPCODE_BASE;
            }
            iRet = MMCOProcess(pCtx, pRefPic, uiMmcoType, iShortFrameNum, uiLongTermPicNum, iLongTermFrameIdx,
                iMaxLongTermFrameIdx);
            if (iRet != ERR_NONE)
            {
                return iRet;
            }
        }
        if (i == MAX_MMCO_COUNT)
        { //although Rec does not handle this condition, we here prohibit too many MMCO op
            return ERR_INFO_INVALID_MMCO_NUM;
        }

        return ERR_NONE;
    }

    private static int32_t MMCOProcess(SWelsDecoderContext* pCtx, SRefPic* pRefPic, uint32_t uiMmcoType,
        int32_t iShortFrameNum, uint32_t uiLongTermPicNum, int32_t iLongTermFrameIdx, int32_t iMaxLongTermFrameIdx)
    {
        SPicture* pPic = null;
        int32_t i = 0;
        int32_t iRet = ERR_NONE;

        switch (uiMmcoType)
        {
            case (uint32_t)MMCO_SHORT2UNUSED:
                pPic = WelsDelShortFromListSetUnref(pRefPic, iShortFrameNum);
                if (pPic == null)
                {
                    WelsLog(&(pCtx->sLogCtx), WELS_LOG_WARNING, "MMCO_SHORT2UNUSED: delete an empty entry from short term list");
                }
                break;
            case (uint32_t)MMCO_LONG2UNUSED:
                pPic = WelsDelLongFromListSetUnref(pRefPic, uiLongTermPicNum);
                if (pPic == null)
                {
                    WelsLog(&(pCtx->sLogCtx), WELS_LOG_WARNING, "MMCO_LONG2UNUSED: delete an empty entry from long term list");
                }
                break;
            case (uint32_t)MMCO_SHORT2LONG:
                if (iLongTermFrameIdx > pRefPic->iMaxLongTermFrameIdx)
                {
                    return ERR_INFO_INVALID_MMCO_LONG_TERM_IDX_EXCEED_MAX;
                }
                pPic = WelsDelShortFromList(pRefPic, iShortFrameNum);
                if (pPic == null)
                {
                    WelsLog(&(pCtx->sLogCtx), WELS_LOG_WARNING, "MMCO_LONG2LONG: delete an empty entry from short term list");
                    break;
                }
                WelsDelLongFromListSetUnref(pRefPic, (uint32_t)iLongTermFrameIdx);
                // LONG_TERM_REF
                pCtx->bCurAuContainLtrMarkSeFlag = true;
                pCtx->iFrameNumOfAuMarkedLtr = iShortFrameNum;
                WelsLog(&(pCtx->sLogCtx), WELS_LOG_INFO, $"ex_mark_avc():::MMCO_SHORT2LONG:::LTR marking....iFrameNum: {pCtx->iFrameNumOfAuMarkedLtr}");

                MarkAsLongTerm(pRefPic, iShortFrameNum, iLongTermFrameIdx, uiLongTermPicNum);
                break;
            case (uint32_t)MMCO_SET_MAX_LONG:
                pRefPic->iMaxLongTermFrameIdx = iMaxLongTermFrameIdx;
                for (i = 0; i < pRefPic->uiLongRefCount[(int32_t)LIST_0]; i++)
                {
                    SPicture* pLong = LongList0(pRefPic)[i];
                    if (pLong->iLongTermFrameIdx > pRefPic->iMaxLongTermFrameIdx)
                    {
                        WelsDelLongFromListSetUnref(pRefPic, (uint32_t)pLong->iLongTermFrameIdx);
                    }
                }
                break;
            case (uint32_t)MMCO_RESET:
                WelsResetRefPic(pCtx);
                if (pRefPic != &pCtx->sRefPic)
                {
                    // WelsResetRefPic() hard-codes pCtx->sRefPic and does not
                    // touch the caller's active reference list. In the threaded predecessor
                    // handoff path (decoder_core.cpp), pRefPic points at pCtx->sTmpRefPic, a
                    // snapshot taken from pCtx->sRefPic before this call and later published
                    // to the successor thread context. Left untouched here, sTmpRefPic would
                    // keep pointers to pictures that WelsResetRefPic() just unreferenced
                    // (and that may already be recycled by PrefetchPic()), so the successor
                    // frame would inherit a stale/dangling reference list. Re-sync it to the
                    // freshly-cleared sRefPic; the entries were already unreffed once by
                    // WelsResetRefPic() above, so do not call SetUnRef again here.
                    *pRefPic = pCtx->sRefPic;
                }
                pCtx->pLastDecPicInfo->bLastHasMmco5 = true;
                break;
            case (uint32_t)MMCO_LONG:
                if (iLongTermFrameIdx > pRefPic->iMaxLongTermFrameIdx)
                {
                    return ERR_INFO_INVALID_MMCO_LONG_TERM_IDX_EXCEED_MAX;
                }
                WelsDelLongFromListSetUnref(pRefPic, (uint32_t)iLongTermFrameIdx);
                if (pRefPic->uiLongRefCount[(int32_t)LIST_0] + pRefPic->uiShortRefCount[(int32_t)LIST_0] >= WELS_MAX(1, pCtx->pSps->iNumRefFrames))
                {
                    return ERR_INFO_INVALID_MMCO_REF_NUM_OVERFLOW;
                }
                // LONG_TERM_REF
                pCtx->bCurAuContainLtrMarkSeFlag = true;
                pCtx->iFrameNumOfAuMarkedLtr = pCtx->iFrameNum;
                WelsLog(&(pCtx->sLogCtx), WELS_LOG_INFO, $"ex_mark_avc():::MMCO_LONG:::LTR marking....iFrameNum: {pCtx->iFrameNum}");
                iRet = AddLongTermToList(pRefPic, pCtx->pDec, iLongTermFrameIdx, uiLongTermPicNum);
                break;
            default:
                break;
        }

        return iRet;
    }

    private static int32_t SlidingWindow(SWelsDecoderContext* pCtx, SRefPic* pRefPic)
    {
        SPicture* pPic = null;
        int32_t i = 0;

        if (pRefPic->uiShortRefCount[(int32_t)LIST_0] + pRefPic->uiLongRefCount[(int32_t)LIST_0] >= pCtx->pSps->iNumRefFrames)
        {
            if (pRefPic->uiShortRefCount[(int32_t)LIST_0] == 0)
            {
                WelsLog(&(pCtx->sLogCtx), WELS_LOG_ERROR, "No reference picture in short term list when sliding window");
                return ERR_INFO_INVALID_MMCO_REF_NUM_NOT_ENOUGH;
            }
            // Upstream's loop always ends on its first pass (break or return).
#pragma warning disable CS0162 // the i-- is unreachable
            for (i = pRefPic->uiShortRefCount[(int32_t)LIST_0] - 1; i >= 0; i--)
            {
                pPic = WelsDelShortFromList(pRefPic, ShortList0(pRefPic)[i]->iFrameNum);
                if (pPic != null)
                {
                    SetUnRef(pPic);
                    break;
                }
                else
                {
                    return ERR_INFO_INVALID_MMCO_REF_NUM_OVERFLOW;
                }
            }
#pragma warning restore CS0162
        }
        return ERR_NONE;
    }

    private static SPicture* WelsDelShortFromList(SRefPic* pRefPic, int32_t iFrameNum)
    {
        int32_t i = 0;
        int32_t iMoveSize = 0;
        SPicture* pPic = null;
        SPicture** ppShortRefList = ShortList0(pRefPic);

        for (i = 0; i < pRefPic->uiShortRefCount[(int32_t)LIST_0]; i++)
        {
            if (ppShortRefList[i]->iFrameNum == iFrameNum)
            {
                iMoveSize = pRefPic->uiShortRefCount[(int32_t)LIST_0] - i - 1;
                pPic = ppShortRefList[i];
                pPic->bUsedAsRef = false;
                ppShortRefList[i] = null;
                if (iMoveSize > 0)
                {
                    memmove(&ppShortRefList[i], &ppShortRefList[i + 1],
                        iMoveSize * sizeof(SPicture*)); //confirmed_safe_unsafe_usage
                }
                pRefPic->uiShortRefCount[(int32_t)LIST_0]--;
                ppShortRefList[pRefPic->uiShortRefCount[(int32_t)LIST_0]] = null;
                break;
            }
        }
        return pPic;
    }

    private static SPicture* WelsDelShortFromListSetUnref(SRefPic* pRefPic, int32_t iFrameNum)
    {
        SPicture* pPic = WelsDelShortFromList(pRefPic, iFrameNum);
        if (pPic != null)
        {
            SetUnRef(pPic);
        }
        return pPic;
    }

    private static SPicture* WelsDelLongFromList(SRefPic* pRefPic, uint32_t uiLongTermFrameIdx)
    {
        SPicture* pPic = null;
        int32_t i = 0;
        SPicture** ppLongRefList = LongList0(pRefPic);
        for (i = 0; i < pRefPic->uiLongRefCount[(int32_t)LIST_0]; i++)
        {
            pPic = ppLongRefList[i];
            if (pPic->iLongTermFrameIdx == (int32_t)uiLongTermFrameIdx)
            {
                int32_t iMoveSize = pRefPic->uiLongRefCount[(int32_t)LIST_0] - i - 1;
                pPic->bUsedAsRef = false;
                pPic->bIsLongRef = false;
                if (iMoveSize > 0)
                {
                    memmove(&ppLongRefList[i], &ppLongRefList[i + 1],
                        iMoveSize * sizeof(SPicture*)); //confirmed_safe_unsafe_usage
                }
                pRefPic->uiLongRefCount[(int32_t)LIST_0]--;
                ppLongRefList[pRefPic->uiLongRefCount[(int32_t)LIST_0]] = null;
                return pPic;
            }
        }
        return null;
    }

    private static SPicture* WelsDelLongFromListSetUnref(SRefPic* pRefPic, uint32_t uiLongTermFrameIdx)
    {
        SPicture* pPic = WelsDelLongFromList(pRefPic, uiLongTermFrameIdx);
        if (pPic != null)
        {
            SetUnRef(pPic);
        }
        return pPic;
    }

    private static int32_t AddShortTermToList(SRefPic* pRefPic, SPicture* pPic)
    {
        SPicture** ppShortRefList = ShortList0(pRefPic);
        pPic->bUsedAsRef = true;
        pPic->bIsLongRef = false;
        pPic->iLongTermFrameIdx = -1;
        if (pRefPic->uiShortRefCount[(int32_t)LIST_0] > 0)
        {
            // Check the duplicate frame_num in short ref list
            for (int32_t iPos = 0; iPos < pRefPic->uiShortRefCount[(int32_t)LIST_0]; iPos++)
            {
                if (ppShortRefList[iPos] == null)
                {
                    return ERR_INFO_INVALID_PTR;
                }
                if (pPic->iFrameNum == ppShortRefList[iPos]->iFrameNum)
                {
                    // Replace the previous ref pic with the new one with the same frame_num
                    ppShortRefList[iPos] = pPic;
                    return ERR_INFO_DUPLICATE_FRAME_NUM;
                }
            }

            memmove(&ppShortRefList[1], &ppShortRefList[0],
                pRefPic->uiShortRefCount[(int32_t)LIST_0] * sizeof(SPicture*));//confirmed_safe_unsafe_usage
        }
        ppShortRefList[0] = pPic;
        pRefPic->uiShortRefCount[(int32_t)LIST_0]++;        return ERR_NONE;
    }

    private static int32_t AddLongTermToList(SRefPic* pRefPic, SPicture* pPic, int32_t iLongTermFrameIdx,
        uint32_t uiLongTermPicNum)
    {
        int32_t i = 0;
        SPicture** ppLongRefList = LongList0(pRefPic);

        pPic->bUsedAsRef = true;
        pPic->bIsLongRef = true;
        pPic->iLongTermFrameIdx = iLongTermFrameIdx;
        pPic->uiLongTermPicNum = uiLongTermPicNum;
        if (pRefPic->uiLongRefCount[(int32_t)LIST_0] == 0)
        {
            ppLongRefList[pRefPic->uiLongRefCount[(int32_t)LIST_0]] = pPic;
        }
        else
        {
            for (i = 0; i < WELS_MIN((int32_t)pRefPic->uiLongRefCount[(int32_t)LIST_0], MAX_REF_PIC_COUNT); i++)
            {
                if (ppLongRefList[i] == null)
                {
                    return ERR_INFO_INVALID_PTR;
                }
                if (ppLongRefList[i]->iLongTermFrameIdx > pPic->iLongTermFrameIdx)
                {
                    break;
                }
            }
            memmove(&ppLongRefList[i + 1], &ppLongRefList[i],
                (pRefPic->uiLongRefCount[(int32_t)LIST_0] - i) * sizeof(SPicture*)); //confirmed_safe_unsafe_usage
            ppLongRefList[i] = pPic;
        }

        if (pRefPic->uiLongRefCount[(int32_t)LIST_0] < MAX_REF_PIC_COUNT)
        {
            pRefPic->uiLongRefCount[(int32_t)LIST_0]++;
        }
        return ERR_NONE;
    }

    private static int32_t MarkAsLongTerm(SRefPic* pRefPic, int32_t iFrameNum, int32_t iLongTermFrameIdx,
        uint32_t uiLongTermPicNum)
    {
        SPicture* pPic = null;
        int32_t i = 0;
        int32_t iRet = ERR_NONE;
        WelsDelLongFromListSetUnref(pRefPic, (uint32_t)iLongTermFrameIdx);

        SPicture** ppRefList = RefList(pRefPic, (int32_t)LIST_0);
        for (i = 0; i < pRefPic->uiRefCount[(int32_t)LIST_0]; i++)
        {
            pPic = ppRefList[i];
            if (pPic->iFrameNum == iFrameNum && !pPic->bIsLongRef)
            {
                iRet = AddLongTermToList(pRefPic, pPic, iLongTermFrameIdx, uiLongTermPicNum);
                break;
            }
        }

        return iRet;
    }

    // LONG_TERM_REF
    public static int32_t GetLTRFrameIndex(SRefPic* pRefPic, int32_t iAncLTRFrameNum)
    {
        int32_t iLTRFrameIndex = -1;
        SPicture* pPic;
        for (int i = 0; i < pRefPic->uiLongRefCount[0]; ++i)
        {
            pPic = LongList0(pRefPic)[i];
            if (pPic->iFrameNum == iAncLTRFrameNum)
            {
                return (pPic->iLongTermFrameIdx);
            }
        }
        return iLTRFrameIndex;
    }

    private static int32_t RemainOneBufferInDpbForEC(SWelsDecoderContext* pCtx, SRefPic* pRefPic)
    {
        int32_t iRet = ERR_NONE;
        if (pRefPic->uiShortRefCount[0] + pRefPic->uiLongRefCount[0] < pCtx->pSps->iNumRefFrames)
        {
            return iRet;
        }

        if (pRefPic->uiShortRefCount[0] > 0)
        {
            iRet = SlidingWindow(pCtx, pRefPic);
        }
        else
        { //all LTR, remove the smallest long_term_frame_idx
            int32_t iLongTermFrameIdx = 0;
            int32_t iMaxLongTermFrameIdx = pRefPic->iMaxLongTermFrameIdx;
            // LONG_TERM_REF
            int32_t iCurrLTRFrameIdx = GetLTRFrameIndex(pRefPic, pCtx->iFrameNumOfAuMarkedLtr);
            while ((pRefPic->uiLongRefCount[0] >= pCtx->pSps->iNumRefFrames) && (iLongTermFrameIdx <= iMaxLongTermFrameIdx))
            {
                // LONG_TERM_REF
                if (iLongTermFrameIdx == iCurrLTRFrameIdx)
                {
                    iLongTermFrameIdx++;
                    continue;
                }
                WelsDelLongFromListSetUnref(pRefPic, (uint32_t)iLongTermFrameIdx);
                iLongTermFrameIdx++;
            }
        }
        if (pRefPic->uiShortRefCount[0] + pRefPic->uiLongRefCount[0] >=
            pCtx->pSps->iNumRefFrames)
        { //fail to remain one empty buffer in DPB
            WelsLog(&(pCtx->sLogCtx), WELS_LOG_WARNING, "RemainOneBufferInDpbForEC(): empty one DPB failed for EC!");
            iRet = ERR_INFO_REF_COUNT_OVERFLOW;
        }

        return iRet;
    }
}
