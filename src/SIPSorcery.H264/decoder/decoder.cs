//-----------------------------------------------------------------------------
// Filename: decoder.cs
//
// Description: Port of codec/decoder/core/inc/decoder.h and
// src/decoder.cpp: decoder context defaults and the top level decode entry.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// Filled in as the decoder steps need it. So far: the SPS/PPS and last
// decoded picture defaults and GetVclNalTemporalId (D1), InitPredFunc with
// its IdctFourResAddPred_ helper (D3), and the picture buffer (DPB) memory:
// CreatePicBuff, IncreasePicBuff, DecreasePicBuff, DestroyPicBuff,
// ResetReorderingPictureBuffers, GetTargetRefListSize, WelsRequestMem,
// WelsFreeDynamicMemory and SyncPictureResolutionExt (D4). InitPredFunc fills
// only the C kernels; the SIMD branches are not ported, so uiCpuFlag is
// unused. The threaded decoder is not ported.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 25 Nov 2026  Claude          Created.
// 25 Nov 2026  Claude          D3: InitPredFunc.
// 25 Nov 2026  Claude          D4: picture buffer memory.
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
 * \file    decoder.c
 *
 * \brief   Interfaces implementation introduced in decoder system architecture
 *
 * \date    03/10/2009 Created
 *
 *************************************************************************************
 */

namespace SIPSorcery.H264.Decoder;

internal static unsafe partial class WelsDec
{
    private static int32_t CreatePicBuff(SWelsDecoderContext* pCtx, SPicBuff** ppPicBuf, int32_t kiSize,
        int32_t kiPicWidth, int32_t kiPicHeight)
    {
        SPicBuff* pPicBuf = null;
        int32_t iPicIdx = 0;
        if (kiSize <= 0 || kiPicWidth <= 0 || kiPicHeight <= 0)
        {
            return ERR_INFO_INVALID_PARAM;
        }

        CMemoryAlign* pMa = pCtx->pMemAlign;

        pPicBuf = (SPicBuff*)pMa->WelsMallocz((uint32_t)sizeof(SPicBuff), "PPicBuff");

        if (null == pPicBuf)
        {
            return ERR_INFO_OUT_OF_MEMORY;
        }
        pPicBuf->pMa = pMa;

        pPicBuf->ppPic = (SPicture**)pMa->WelsMallocz((uint32_t)(kiSize * sizeof(SPicture*)), "PPicture*");

        if (null == pPicBuf->ppPic)
        {
            pPicBuf->iCapacity = 0;
            DestroyPicBuff(pCtx, &pPicBuf, pMa);
            return ERR_INFO_OUT_OF_MEMORY;
        }

        for (iPicIdx = 0; iPicIdx < kiSize; ++iPicIdx)
        {
            SPicture* pPic = AllocPicture(pCtx, kiPicWidth, kiPicHeight);
            if (null == pPic)
            {
                // init capacity first for free memory
                pPicBuf->iCapacity = iPicIdx;
                DestroyPicBuff(pCtx, &pPicBuf, pMa);
                return ERR_INFO_OUT_OF_MEMORY;
            }
            pPicBuf->ppPic[iPicIdx] = pPic;
        }

        // initialize context in queue
        pPicBuf->iCapacity = kiSize;
        pPicBuf->iCurrentIdx = 0;
        *ppPicBuf = pPicBuf;

        return ERR_NONE;
    }

    private static int32_t IncreasePicBuff(SWelsDecoderContext* pCtx, SPicBuff** ppPicBuf, int32_t kiOldSize,
        int32_t kiPicWidth, int32_t kiPicHeight, int32_t kiNewSize)
    {
        SPicBuff* pPicOldBuf = *ppPicBuf;
        SPicBuff* pPicNewBuf = null;
        int32_t iPicIdx = 0;
        if (kiOldSize <= 0 || kiNewSize <= 0 || kiPicWidth <= 0 || kiPicHeight <= 0)
        {
            return ERR_INFO_INVALID_PARAM;
        }

        CMemoryAlign* pMa = pCtx->pMemAlign;
        CMemoryAlign* pOldMa = (pPicOldBuf != null && pPicOldBuf->pMa != null) ? pPicOldBuf->pMa : pMa;
        pPicNewBuf = (SPicBuff*)pMa->WelsMallocz((uint32_t)sizeof(SPicBuff), "PPicBuff");

        if (null == pPicNewBuf)
        {
            return ERR_INFO_OUT_OF_MEMORY;
        }
        pPicNewBuf->pMa = pMa;

        pPicNewBuf->ppPic = (SPicture**)pMa->WelsMallocz((uint32_t)(kiNewSize * sizeof(SPicture*)), "PPicture*");

        if (null == pPicNewBuf->ppPic)
        {
            pPicNewBuf->iCapacity = 0;
            DestroyPicBuff(pCtx, &pPicNewBuf, pMa);
            return ERR_INFO_OUT_OF_MEMORY;
        }

        // increase new PicBuf
        for (iPicIdx = kiOldSize; iPicIdx < kiNewSize; ++iPicIdx)
        {
            SPicture* pPic = AllocPicture(pCtx, kiPicWidth, kiPicHeight);
            if (null == pPic)
            {
                // Set maximum capacity as the new malloc memory at the tail
                pPicNewBuf->iCapacity = iPicIdx;
                DestroyPicBuff(pCtx, &pPicNewBuf, pMa);
                return ERR_INFO_OUT_OF_MEMORY;
            }
            pPicNewBuf->ppPic[iPicIdx] = pPic;
        }

        // copy old PicBuf to new PicBuf
        memcpy(pPicNewBuf->ppPic, pPicOldBuf->ppPic, kiOldSize * sizeof(SPicture*));

        // initialize context in queue
        pPicNewBuf->iCapacity = kiNewSize;
        pPicNewBuf->iCurrentIdx = pPicOldBuf->iCurrentIdx;
        *ppPicBuf = pPicNewBuf;

        // only initialize new slots; old slots must preserve iRefCount pinned by output buffering
        for (int32_t i = kiOldSize; i < pPicNewBuf->iCapacity; i++)
        {
            pPicNewBuf->ppPic[i]->bUsedAsRef = false;
            pPicNewBuf->ppPic[i]->bIsLongRef = false;
            pPicNewBuf->ppPic[i]->iRefCount = 0;
            pPicNewBuf->ppPic[i]->pSetUnRef = null;
            pPicNewBuf->ppPic[i]->bIsComplete = false;
        }
        // remove old PicBuf
        if (pPicOldBuf->ppPic != null)
        {
            pOldMa->WelsFree(pPicOldBuf->ppPic, "pPicOldBuf->queue");
            pPicOldBuf->ppPic = null;
        }
        pPicOldBuf->iCapacity = 0;
        pPicOldBuf->iCurrentIdx = 0;
        pOldMa->WelsFree(pPicOldBuf, "pPicOldBuf");
        pPicOldBuf = null;
        return ERR_NONE;
    }

    private static int32_t DecreasePicBuff(SWelsDecoderContext* pCtx, SPicBuff** ppPicBuf, int32_t kiOldSize,
        int32_t kiPicWidth, int32_t kiPicHeight, int32_t kiNewSize)
    {
        SPicBuff* pPicOldBuf = *ppPicBuf;
        SPicBuff* pPicNewBuf = null;
        int32_t iPicIdx = 0;
        if (kiOldSize <= 0 || kiNewSize <= 0 || kiPicWidth <= 0 || kiPicHeight <= 0)
        {
            return ERR_INFO_INVALID_PARAM;
        }

        CMemoryAlign* pMa = pCtx->pMemAlign;
        CMemoryAlign* pOldMa = (pPicOldBuf != null && pPicOldBuf->pMa != null) ? pPicOldBuf->pMa : pMa;

        pPicNewBuf = (SPicBuff*)pMa->WelsMallocz((uint32_t)sizeof(SPicBuff), "PPicBuff");

        if (null == pPicNewBuf)
        {
            return ERR_INFO_OUT_OF_MEMORY;
        }
        pPicNewBuf->pMa = pMa;

        pPicNewBuf->ppPic = (SPicture**)pMa->WelsMallocz((uint32_t)(kiNewSize * sizeof(SPicture*)), "PPicture*");

        if (null == pPicNewBuf->ppPic)
        {
            pPicNewBuf->iCapacity = 0;
            DestroyPicBuff(pCtx, &pPicNewBuf, pMa);
            return ERR_INFO_OUT_OF_MEMORY;
        }

        ResetReorderingPictureBuffers(pCtx->pPictReoderingStatus, pCtx->pPictInfoList, false);

        int32_t iPrevPicIdx = -1;
        for (iPrevPicIdx = 0; iPrevPicIdx < kiOldSize; ++iPrevPicIdx)
        {
            if (pCtx->pLastDecPicInfo->pPreviousDecodedPictureInDpb == pPicOldBuf->ppPic[iPrevPicIdx])
            {
                break;
            }
        }
        int32_t iDelIdx;
        if (iPrevPicIdx < kiOldSize && iPrevPicIdx >= kiNewSize)
        {
            // found pPreviousDecodedPictureInDpb,
            pPicNewBuf->ppPic[0] = pPicOldBuf->ppPic[iPrevPicIdx];
            pPicNewBuf->iCurrentIdx = 0;
            memcpy(pPicNewBuf->ppPic + 1, pPicOldBuf->ppPic, (kiNewSize - 1) * sizeof(SPicture*));
            iDelIdx = kiNewSize - 1;
        }
        else
        {
            memcpy(pPicNewBuf->ppPic, pPicOldBuf->ppPic, kiNewSize * sizeof(SPicture*));
            pPicNewBuf->iCurrentIdx = iPrevPicIdx < kiNewSize ? iPrevPicIdx : 0;
            iDelIdx = kiNewSize;
        }

        //update references due to allocation changes
        //all references' references have to be reset oss-buzz 14423
        for (int32_t i = 0; i < kiNewSize; i++)
        {
            for (int32_t listIdx = (int32_t)LIST_0; listIdx < (int32_t)LIST_A; ++listIdx)
            {
                int32_t j = -1;
                while (++j < MAX_DPB_COUNT && pPicNewBuf->ppPic[i]->pRefPic[listIdx][j].p != null)
                {
                    pPicNewBuf->ppPic[i]->pRefPic[listIdx][j] = null;
                }
            }
        }

        for (iPicIdx = iDelIdx; iPicIdx < kiOldSize; iPicIdx++)
        {
            if (iPrevPicIdx != iPicIdx)
            {
                if (pPicOldBuf->ppPic[iPicIdx] != null)
                {
                    FreePicture(pPicOldBuf->ppPic[iPicIdx], pOldMa);
                    pPicOldBuf->ppPic[iPicIdx] = null;
                }
            }
        }

        // initialize context in queue
        pPicNewBuf->iCapacity = kiNewSize;
        *ppPicBuf = pPicNewBuf;

        for (int32_t i = 0; i < pPicNewBuf->iCapacity; i++)
        {
            pPicNewBuf->ppPic[i]->bUsedAsRef = false;
            pPicNewBuf->ppPic[i]->bIsLongRef = false;
            pPicNewBuf->ppPic[i]->iRefCount = 0;
            pPicNewBuf->ppPic[i]->pSetUnRef = null;
            pPicNewBuf->ppPic[i]->bIsComplete = false;
        }
        // remove old PicBuf
        if (pPicOldBuf->ppPic != null)
        {
            pOldMa->WelsFree(pPicOldBuf->ppPic, "pPicOldBuf->queue");
            pPicOldBuf->ppPic = null;
        }
        pPicOldBuf->iCapacity = 0;
        pPicOldBuf->iCurrentIdx = 0;
        pOldMa->WelsFree(pPicOldBuf, "pPicOldBuf");
        pPicOldBuf = null;

        return ERR_NONE;
    }

    public static void DestroyPicBuff(SWelsDecoderContext* pCtx, SPicBuff** ppPicBuf, CMemoryAlign* pMa)
    {
        SPicBuff* pPicBuf = null;

        ResetReorderingPictureBuffers(pCtx->pPictReoderingStatus, pCtx->pPictInfoList, false);

        if (null == ppPicBuf || null == *ppPicBuf)
        {
            return;
        }

        pPicBuf = *ppPicBuf;
        // Free through the allocator that created this buffer.
        if (pPicBuf->pMa != null)
        {
            pMa = pPicBuf->pMa;
        }
        while (pPicBuf->ppPic != null)
        {
            int32_t iPicIdx = 0;
            while (iPicIdx < pPicBuf->iCapacity)
            {
                SPicture* pPic = pPicBuf->ppPic[iPicIdx];
                if (pPic != null)
                {
                    FreePicture(pPic, pMa);
                }
                pPic = null;
                ++iPicIdx;
            }

            pMa->WelsFree(pPicBuf->ppPic, "pPicBuf->queue");

            pPicBuf->ppPic = null;
        }
        pPicBuf->iCapacity = 0;
        pPicBuf->iCurrentIdx = 0;

        pMa->WelsFree(pPicBuf, "pPicBuf");

        pPicBuf = null;
        *ppPicBuf = null;
    }

    //reset picture reodering buffer list
    public static void ResetReorderingPictureBuffers(SPictReoderingStatus* pPictReoderingStatus, SPictInfo* pPictInfo,
        bool fullReset)
    {
        if (pPictReoderingStatus != null && pPictInfo != null)
        {
            int32_t pictInfoListCount = fullReset ? 16 : (pPictReoderingStatus->iLargestBufferedPicIndex + 1);
            pPictReoderingStatus->iPictInfoIndex = 0;
            pPictReoderingStatus->iMinPOC = IMinInt32;
            pPictReoderingStatus->iNumOfPicts = 0;
            pPictReoderingStatus->iLastWrittenPOC = IMinInt32;
            pPictReoderingStatus->iLargestBufferedPicIndex = 0;
            for (int32_t i = 0; i < pictInfoListCount; ++i)
            {
                pPictInfo[i].iPOC = IMinInt32;
                pPictInfo[i].iPicBuffIdx = -1; //ensure a deterministic invalid sentinel so error-path decoding cannot leave heap garbage
            }
            pPictInfo->sBufferInfo.iBufferStatus = 0;
            pPictReoderingStatus->bHasBSlice = false;
        }
    }

    /*
    * fill data fields in SPS and PPS default for decoder context
    */
    public static void WelsDecoderSpsPpsDefaults(SWelsDecoderSpsPpsCTX* sSpsPpsCtx)
    {
        sSpsPpsCtx->bSpsExistAheadFlag = false;
        sSpsPpsCtx->bSubspsExistAheadFlag = false;
        sSpsPpsCtx->bPpsExistAheadFlag = false;
        sSpsPpsCtx->bAvcBasedFlag = true;
        sSpsPpsCtx->iSpsErrorIgnored = 0;
        sSpsPpsCtx->iSubSpsErrorIgnored = 0;
        sSpsPpsCtx->iPpsErrorIgnored = 0;
        sSpsPpsCtx->iPPSInvalidNum = 0;
        sSpsPpsCtx->iPPSLastInvalidId = -1;
        sSpsPpsCtx->iSPSInvalidNum = 0;
        sSpsPpsCtx->iSPSLastInvalidId = -1;
        sSpsPpsCtx->iSubSPSInvalidNum = 0;
        sSpsPpsCtx->iSubSPSLastInvalidId = -1;
        sSpsPpsCtx->iSeqId = -1;
    }

    /*
    * fill last decoded picture info
    */
    public static void WelsDecoderLastDecPicInfoDefaults(SWelsLastDecPicInfo* sLastDecPicInfo)
    {
        sLastDecPicInfo->iPrevPicOrderCntMsb = 0;
        sLastDecPicInfo->iPrevPicOrderCntLsb = 0;
        sLastDecPicInfo->pPreviousDecodedPictureInDpb = null;
        sLastDecPicInfo->iPrevFrameNum = -1;
        sLastDecPicInfo->bLastHasMmco5 = false;
        sLastDecPicInfo->uiDecodingTimeStamp = 0;
    }

    /*
     *  get size of reference picture list in target layer incoming, = (iNumRefFrames
     */
    private static int32_t GetTargetRefListSize(SWelsDecoderContext* pCtx)
    {
        int32_t iNumRefFrames = 0;
        // +2 for EC MV Copy buffer exchange
        if ((pCtx == null) || (pCtx->pSps == null))
        {
            iNumRefFrames = MAX_REF_PIC_COUNT + 2;
        }
        else
        {
            iNumRefFrames = pCtx->pSps->iNumRefFrames + 2;
            int32_t iThreadCount = GetThreadCount(pCtx);
            if (iThreadCount > 1)
            {
                //due to thread and reordering buffering, it needs more dpb space
                iNumRefFrames = MAX_DPB_COUNT + iThreadCount;
            }
        }

        // LONG_TERM_REF
        //pic_queue size minimum set 2
        if (iNumRefFrames < 2)
        {
            iNumRefFrames = 2;
        }

        return iNumRefFrames;
    }

    /*
     *  request memory blocks for decoder avc part
     */
    public static int32_t WelsRequestMem(SWelsDecoderContext* pCtx, int32_t kiMbWidth, int32_t kiMbHeight,
        ref bool bReallocFlag)
    {
        int32_t kiPicWidth = kiMbWidth << 4;
        int32_t kiPicHeight = kiMbHeight << 4;
        int32_t iErr = ERR_NONE;

        int32_t iPicQueueSize = 0;    // adaptive size of picture queue, = (pSps->iNumRefFrames x 2)
        bReallocFlag = false;
        bool bNeedChangePicQueue = true;
        CMemoryAlign* pMa = pCtx->pMemAlign;

        if (null == pCtx || kiPicWidth <= 0 || kiPicHeight <= 0)
        {
            return ERR_INFO_INVALID_PARAM; // WELS_VERIFY_RETURN_IF
        }

        // Fixed the issue about different gop size over last, 5/17/2010
        // get picture queue size currently
        iPicQueueSize = GetTargetRefListSize(pCtx);  // adaptive size of picture queue, = (pSps->iNumRefFrames x 2)
        pCtx->iPicQueueNumber = iPicQueueSize;
        if (pCtx->pPicBuff != null
            && pCtx->pPicBuff->iCapacity ==
            iPicQueueSize) // comparing current picture queue size requested and previous allocation picture queue
        {
            bNeedChangePicQueue = false;
        }
        // HD based pic buffer need consider memory size consumed when switch from 720p to other lower size
        if (pCtx->bHaveGotMemory && (kiPicWidth == pCtx->iImgWidthInPixel
                                     && kiPicHeight == pCtx->iImgHeightInPixel) && (!bNeedChangePicQueue))
        {
            return ERR_NONE; // have same scaled buffer (WELS_VERIFY_RETURN_IF)
        }

        // sync update pRefList
        if (GetThreadCount(pCtx) <= 1)
        {
            WelsResetRefPic(pCtx); // added to sync update ref list due to pictures are free
        }

        if (pCtx->bHaveGotMemory && (kiPicWidth == pCtx->iImgWidthInPixel && kiPicHeight == pCtx->iImgHeightInPixel)
            && pCtx->pPicBuff != null && pCtx->pPicBuff->iCapacity != iPicQueueSize)
        {
            // currently only active for LIST_0 due to have no B frames
            // Actually just need one memory allocation for the PicBuff. While it needs two pointer list (LIST_0 and LIST_1).
            WelsLog(&(pCtx->sLogCtx), WELS_LOG_INFO,
                $"WelsRequestMem(): memory re-alloc for no resolution change (size = {kiPicWidth} * {kiPicHeight}), ref list size change from {pCtx->pPicBuff->iCapacity} to {iPicQueueSize}");
            if (pCtx->pPicBuff->iCapacity < iPicQueueSize)
            {
                iErr = IncreasePicBuff(pCtx, &pCtx->pPicBuff, pCtx->pPicBuff->iCapacity, kiPicWidth, kiPicHeight,
                    iPicQueueSize);
            }
            else
            {
                iErr = DecreasePicBuff(pCtx, &pCtx->pPicBuff, pCtx->pPicBuff->iCapacity, kiPicWidth, kiPicHeight,
                    iPicQueueSize);
            }
        }
        else
        {
            if (pCtx->bHaveGotMemory)
            {
                WelsLog(&(pCtx->sLogCtx), WELS_LOG_INFO,
                    $"WelsRequestMem(): memory re-alloc for resolution change, size change from {pCtx->iImgWidthInPixel} * {pCtx->iImgHeightInPixel} to {kiPicWidth} * {kiPicHeight}, ref list size change from {pCtx->pPicBuff->iCapacity} to {iPicQueueSize}");
            }
            else
            {
                WelsLog(&(pCtx->sLogCtx), WELS_LOG_INFO,
                    $"WelsRequestMem(): memory alloc size = {kiPicWidth} * {kiPicHeight}, ref list size = {iPicQueueSize}");
            }
            // for Recycled_Pic_Queue
            SPicBuff** ppPic = &pCtx->pPicBuff;
            if (null != ppPic && null != *ppPic)
            {
                DestroyPicBuff(pCtx, ppPic, pMa);
            }


            pCtx->pLastDecPicInfo->pPreviousDecodedPictureInDpb = null;

            // currently only active for LIST_0 due to have no B frames
            iErr = CreatePicBuff(pCtx, &pCtx->pPicBuff, iPicQueueSize, kiPicWidth, kiPicHeight);
        }

        if (iErr != ERR_NONE)
        {
            return iErr;
        }


        pCtx->iImgWidthInPixel = kiPicWidth;   // target width of image to be reconstruted while decoding
        pCtx->iImgHeightInPixel = kiPicHeight;  // target height of image to be reconstruted while decoding

        pCtx->bHaveGotMemory = true;         // global memory for decoder context related is requested
        pCtx->pDec = null;         // need prefetch a new pic due to spatial size changed

        if (pCtx->pCabacDecEngine == null)
        {
            pCtx->pCabacDecEngine = (SWelsCabacDecEngine*)pMa->WelsMallocz((uint32_t)sizeof(SWelsCabacDecEngine), "pCtx->pCabacDecEngine");
        }
        if (null == pCtx->pCabacDecEngine)
        {
            return ERR_INFO_OUT_OF_MEMORY; // WELS_VERIFY_RETURN_IF
        }

        bReallocFlag = true;         // memory re-allocation successfully finished
        return ERR_NONE;
    }

    /*
     *  free memory dynamically allocated during decoder
     */
    public static void WelsFreeDynamicMemory(SWelsDecoderContext* pCtx)
    {
        CMemoryAlign* pMa = pCtx->pMemAlign;

        //free dq layer memory
        UninitialDqLayersContext(pCtx);

        //free FMO memory
        ResetFmoList(pCtx);

        //free ref-pic list & picture memory
        WelsResetRefPic(pCtx);

        SPicBuff** pPicBuff = &pCtx->pPicBuff;
        if (null != pPicBuff && null != *pPicBuff)
        {
            DestroyPicBuff(pCtx, pPicBuff, pMa);
        }
        // GetThreadCount (pCtx) > 1: the threaded decoder is not ported.

        if (pCtx->pTempDec != null)
        {
            FreePicture(pCtx->pTempDec, pCtx->pMemAlign);
            pCtx->pTempDec = null;
        }

        // added for safe memory
        pCtx->iImgWidthInPixel = 0;
        pCtx->iImgHeightInPixel = 0;
        pCtx->iLastImgWidthInPixel = 0;
        pCtx->iLastImgHeightInPixel = 0;
        pCtx->bFreezeOutput = true;
        pCtx->bHaveGotMemory = false;

        //free CABAC memory
        pMa->WelsFree(pCtx->pCabacDecEngine, "pCtx->pCabacDecEngine");
    }

    public static void GetVclNalTemporalId(SWelsDecoderContext* pCtx)
    {
        SAccessUnit* pAccessUnit = pCtx->pAccessUnitList;
        int32_t idx = (int32_t)pAccessUnit->uiStartPos;

        pCtx->iFeedbackVclNalInAu = (int32_t)FEEDBACK_VCL_NAL;
        pCtx->iFeedbackTidInAu = pAccessUnit->pNalUnitsList[idx]->sNalHeaderExt.uiTemporalId;
        pCtx->iFeedbackNalRefIdc = pAccessUnit->pNalUnitsList[idx]->sNalHeaderExt.sNalUnitHeader.uiNalRefIdc;
    }

    /*!
     * \brief   make sure synchronization picture resolution (get from slice header) among different parts (i.e, memory related and so on)
     *          over decoder internal
     * ( MB coordinate and parts of data within decoder context structure )
     * \param   pCtx        Wels decoder context
     * \param   iMbWidth    MB width
     * \pram    iMbHeight   MB height
     * \return  0 - successful; none 0 - something wrong
     */
    public static int32_t SyncPictureResolutionExt(SWelsDecoderContext* pCtx, int32_t kiMbWidth, int32_t kiMbHeight)
    {
        int32_t iErr = ERR_NONE;
        int32_t kiPicWidth = kiMbWidth << 4;
        int32_t kiPicHeight = kiMbHeight << 4;
        //fix Bugzilla Bug1479656 reallocate temp dec picture
        if (pCtx->pTempDec != null && (pCtx->pTempDec->iWidthInPixel != kiPicWidth
                                       || pCtx->pTempDec->iHeightInPixel != kiPicHeight))
        {
            FreePicture(pCtx->pTempDec, pCtx->pMemAlign);
            pCtx->pTempDec = AllocPicture(pCtx, (int32_t)(pCtx->pSps->iMbWidth << 4), (int32_t)(pCtx->pSps->iMbHeight << 4));
        }
        bool bReallocFlag = false;
        iErr = WelsRequestMem(pCtx, kiMbWidth, kiMbHeight, ref bReallocFlag); // common memory used
        if (ERR_NONE != iErr)
        {
            WelsLog(&(pCtx->sLogCtx), WELS_LOG_ERROR,
                "SyncPictureResolutionExt()::WelsRequestMem--buffer allocated failure.");
            pCtx->iErrorCode |= (int32_t)dsOutOfMemory;
            return iErr;
        }

        iErr = InitialDqLayersContext(pCtx, kiPicWidth, kiPicHeight);
        if (ERR_NONE != iErr)
        {
            WelsLog(&(pCtx->sLogCtx), WELS_LOG_ERROR,
                "SyncPictureResolutionExt()::InitialDqLayersContext--buffer allocated failure.");
            pCtx->iErrorCode |= (int32_t)dsOutOfMemory;
        }
        // MEMORY_MONITOR: not defined.
        return iErr;
    }

    // template<void pfIdctResAddPred (uint8_t* pPred, int32_t iStride, int16_t* pRs)>
    // void IdctFourResAddPred_ (uint8_t* pPred, int32_t iStride, int16_t* pRs, const int8_t* pNzc)
    // in an anonymous namespace upstream. Only the IdctResAddPred_c instantiation
    // is used without SIMD, so it is written out with that kernel.
    public static void IdctFourResAddPred_(uint8_t* pPred, int32_t iStride, int16_t* pRs, int8_t* pNzc)
    {
        if (pNzc[0] != 0 || pRs[0 * 16] != 0)
            IdctResAddPred_c(pPred + 0 * iStride + 0, iStride, pRs + 0 * 16);
        if (pNzc[1] != 0 || pRs[1 * 16] != 0)
            IdctResAddPred_c(pPred + 0 * iStride + 4, iStride, pRs + 1 * 16);
        if (pNzc[4] != 0 || pRs[2 * 16] != 0)
            IdctResAddPred_c(pPred + 4 * iStride + 0, iStride, pRs + 2 * 16);
        if (pNzc[5] != 0 || pRs[3 * 16] != 0)
            IdctResAddPred_c(pPred + 4 * iStride + 4, iStride, pRs + 3 * 16);
    }

    public static void InitPredFunc(SWelsDecoderContext* pCtx, uint32_t uiCpuFlag)
    {
        pCtx->pGetI16x16LumaPredFunc[I16_PRED_V] = &WelsI16x16LumaPredV_c;
        pCtx->pGetI16x16LumaPredFunc[I16_PRED_H] = &WelsI16x16LumaPredH_c;
        pCtx->pGetI16x16LumaPredFunc[I16_PRED_DC] = &WelsI16x16LumaPredDc_c;
        pCtx->pGetI16x16LumaPredFunc[I16_PRED_P] = &WelsI16x16LumaPredPlane_c;
        pCtx->pGetI16x16LumaPredFunc[I16_PRED_DC_L] = &WelsI16x16LumaPredDcLeft_c;
        pCtx->pGetI16x16LumaPredFunc[I16_PRED_DC_T] = &WelsI16x16LumaPredDcTop_c;
        pCtx->pGetI16x16LumaPredFunc[I16_PRED_DC_128] = &WelsI16x16LumaPredDcNA_c;

        pCtx->pGetI4x4LumaPredFunc[I4_PRED_V] = &WelsI4x4LumaPredV_c;
        pCtx->pGetI4x4LumaPredFunc[I4_PRED_H] = &WelsI4x4LumaPredH_c;
        pCtx->pGetI4x4LumaPredFunc[I4_PRED_DC] = &WelsI4x4LumaPredDc_c;
        pCtx->pGetI4x4LumaPredFunc[I4_PRED_DC_L] = &WelsI4x4LumaPredDcLeft_c;
        pCtx->pGetI4x4LumaPredFunc[I4_PRED_DC_T] = &WelsI4x4LumaPredDcTop_c;
        pCtx->pGetI4x4LumaPredFunc[I4_PRED_DC_128] = &WelsI4x4LumaPredDcNA_c;
        pCtx->pGetI4x4LumaPredFunc[I4_PRED_DDL] = &WelsI4x4LumaPredDDL_c;
        pCtx->pGetI4x4LumaPredFunc[I4_PRED_DDL_TOP] = &WelsI4x4LumaPredDDLTop_c;
        pCtx->pGetI4x4LumaPredFunc[I4_PRED_DDR] = &WelsI4x4LumaPredDDR_c;
        pCtx->pGetI4x4LumaPredFunc[I4_PRED_VL] = &WelsI4x4LumaPredVL_c;
        pCtx->pGetI4x4LumaPredFunc[I4_PRED_VL_TOP] = &WelsI4x4LumaPredVLTop_c;
        pCtx->pGetI4x4LumaPredFunc[I4_PRED_VR] = &WelsI4x4LumaPredVR_c;
        pCtx->pGetI4x4LumaPredFunc[I4_PRED_HU] = &WelsI4x4LumaPredHU_c;
        pCtx->pGetI4x4LumaPredFunc[I4_PRED_HD] = &WelsI4x4LumaPredHD_c;

        pCtx->pGetI8x8LumaPredFunc[I4_PRED_V] = &WelsI8x8LumaPredV_c;
        pCtx->pGetI8x8LumaPredFunc[I4_PRED_H] = &WelsI8x8LumaPredH_c;
        pCtx->pGetI8x8LumaPredFunc[I4_PRED_DC] = &WelsI8x8LumaPredDc_c;
        pCtx->pGetI8x8LumaPredFunc[I4_PRED_DC_L] = &WelsI8x8LumaPredDcLeft_c;
        pCtx->pGetI8x8LumaPredFunc[I4_PRED_DC_T] = &WelsI8x8LumaPredDcTop_c;
        pCtx->pGetI8x8LumaPredFunc[I4_PRED_DC_128] = &WelsI8x8LumaPredDcNA_c;
        pCtx->pGetI8x8LumaPredFunc[I4_PRED_DDL] = &WelsI8x8LumaPredDDL_c;
        pCtx->pGetI8x8LumaPredFunc[I4_PRED_DDL_TOP] = &WelsI8x8LumaPredDDLTop_c;
        pCtx->pGetI8x8LumaPredFunc[I4_PRED_DDR] = &WelsI8x8LumaPredDDR_c;
        pCtx->pGetI8x8LumaPredFunc[I4_PRED_VL] = &WelsI8x8LumaPredVL_c;
        pCtx->pGetI8x8LumaPredFunc[I4_PRED_VL_TOP] = &WelsI8x8LumaPredVLTop_c;
        pCtx->pGetI8x8LumaPredFunc[I4_PRED_VR] = &WelsI8x8LumaPredVR_c;
        pCtx->pGetI8x8LumaPredFunc[I4_PRED_HU] = &WelsI8x8LumaPredHU_c;
        pCtx->pGetI8x8LumaPredFunc[I4_PRED_HD] = &WelsI8x8LumaPredHD_c;

        pCtx->pGetIChromaPredFunc[C_PRED_DC] = &WelsIChromaPredDc_c;
        pCtx->pGetIChromaPredFunc[C_PRED_H] = &WelsIChromaPredH_c;
        pCtx->pGetIChromaPredFunc[C_PRED_V] = &WelsIChromaPredV_c;
        pCtx->pGetIChromaPredFunc[C_PRED_P] = &WelsIChromaPredPlane_c;
        pCtx->pGetIChromaPredFunc[C_PRED_DC_L] = &WelsIChromaPredDcLeft_c;
        pCtx->pGetIChromaPredFunc[C_PRED_DC_T] = &WelsIChromaPredDcTop_c;
        pCtx->pGetIChromaPredFunc[C_PRED_DC_128] = &WelsIChromaPredDcNA_c;

        pCtx->pIdctResAddPredFunc = &IdctResAddPred_c;
        pCtx->pIdctFourResAddPredFunc = &IdctFourResAddPred_;

        pCtx->pIdctResAddPredFunc8x8 = &IdctResAddPred8x8_c;
    }
}
