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
// WelsFreeDynamicMemory and SyncPictureResolutionExt (D4). D6 adds the rest
// the API layer needs: WelsDecoderDefaults, WelsOpenDecoder/WelsCloseDecoder,
// DecoderConfigParam, WelsInitDecoder/WelsEndDecoder, InitDecFuncs,
// WelsDecodeBs and the decoder statistics updates. InitPredFunc and
// InitDecFuncs fill the C kernels, and the port's own SIMD kernels where
// WelsCPUFeatureDetect (common/cpu.cs) reports the instruction sets for them
// (Phase 5). Upstream's SIMD branches are assembly and are not ported, nor are
// the threaded decoder and parse-only mode.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 25 Nov 2026  Claude          Created.
// 25 Nov 2026  Claude          D3: InitPredFunc.
// 25 Nov 2026  Claude          D4: picture buffer memory.
// 25 Nov 2026  Claude          D6: decoder init/teardown, WelsDecodeBs, statistics.
// 27 Nov 2026  Claude          P5.2: WelsCPUFeatureDetect.
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
     * fill data fields in default for decoder context
     */
    public static void WelsDecoderDefaults(SWelsDecoderContext* pCtx, SLogContext* pLogCtx)
    {
        pCtx->sLogCtx = *pLogCtx;

        pCtx->pArgDec = null;

        pCtx->bHaveGotMemory = false;              // not ever request memory blocks for decoder context related
        pCtx->uiCpuFlag = 0;

        pCtx->bAuReadyFlag = false;                  // au data is not ready
        pCtx->bCabacInited = false;

        // WelsCPUFeatureDetect (&iCpuCores): the core count isn't used.
        pCtx->uiCpuFlag = WelsCPUFeatureDetect(null);

        pCtx->iImgWidthInPixel = 0;
        pCtx->iImgHeightInPixel = 0;                  // alloc picture data when picture size is available
        pCtx->iLastImgWidthInPixel = 0;
        pCtx->iLastImgHeightInPixel = 0;
        pCtx->bFreezeOutput = true;

        pCtx->iFrameNum = -1;
        pCtx->pLastDecPicInfo->iPrevFrameNum = -1;
        pCtx->iErrorCode = ERR_NONE;

        pCtx->pDec = null;

        pCtx->pTempDec = null;

        WelsResetRefPic(pCtx);

        pCtx->iActiveFmoNum = 0;

        pCtx->pPicBuff = null;

        //pCtx->sSpsPpsCtx.bAvcBasedFlag             = true;
        pCtx->pLastDecPicInfo->pPreviousDecodedPictureInDpb = null;
        pCtx->pDecoderStatistics->iAvgLumaQp = -1;
        pCtx->pDecoderStatistics->iStatisticsLogInterval = 1000;
        pCtx->bUseScalingList = false;
        pCtx->iFeedbackNalRefIdc = -1; //initialize
        pCtx->pLastDecPicInfo->iPrevPicOrderCntMsb = 0;
        pCtx->pLastDecPicInfo->iPrevPicOrderCntLsb = 0;

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

    /*!
     * \brief   Open decoder
     */
    public static int32_t WelsOpenDecoder(SWelsDecoderContext* pCtx, SLogContext* pLogCtx)
    {
        int iRet = ERR_NONE;
        // function pointers
        InitDecFuncs(pCtx, pCtx->uiCpuFlag);

        // vlc tables
        InitVlcTable(pCtx->pVlcTable);

        // static memory
        iRet = WelsInitStaticMemory(pCtx);
        if (ERR_NONE != iRet)
        {
            pCtx->iErrorCode |= (int32_t)dsOutOfMemory;
            WelsLog(pLogCtx, WELS_LOG_ERROR, "WelsInitStaticMemory() failed in WelsOpenDecoder().");
            return iRet;
        }

        // LONG_TERM_REF
        pCtx->bParamSetsLostFlag = true;
        pCtx->bNewSeqBegin = true;
        pCtx->bPrintFrameErrorTraceFlag = true;
        pCtx->iIgnoredErrorInfoPacketCount = 0;
        pCtx->bFrameFinish = true;
        pCtx->iSeqNum = 0;
        return iRet;
    }

    /*!
     * \brief   Close decoder
     */
    public static void WelsCloseDecoder(SWelsDecoderContext* pCtx)
    {
        WelsFreeDynamicMemory(pCtx);

        WelsFreeStaticMemory(pCtx);

        // LONG_TERM_REF
        pCtx->bParamSetsLostFlag = false;
        pCtx->bNewSeqBegin = false;
        pCtx->bPrintFrameErrorTraceFlag = false;
    }

    /*!
     * \brief   configure decoder parameters
     */
    public static int32_t DecoderConfigParam(SWelsDecoderContext* pCtx, SDecodingParam* kpParam)
    {
        if (null == pCtx || null == kpParam)
            return ERR_INFO_INVALID_PARAM;

        *pCtx->pParam = *kpParam;
        if ((pCtx->pParam->eEcActiveIdc > ERROR_CON_SLICE_MV_COPY_CROSS_IDR_FREEZE_RES_CHANGE)
            || (pCtx->pParam->eEcActiveIdc < ERROR_CON_DISABLE))
        {
            WelsLog(&(pCtx->sLogCtx), WELS_LOG_WARNING,
                     $"eErrorConMethod ({(int32_t)pCtx->pParam->eEcActiveIdc}) not in range: ({(int32_t)ERROR_CON_DISABLE} - {(int32_t)ERROR_CON_SLICE_MV_COPY_CROSS_IDR_FREEZE_RES_CHANGE}). Set as default value: ({(int32_t)ERROR_CON_SLICE_MV_COPY_CROSS_IDR_FREEZE_RES_CHANGE}).");
            pCtx->pParam->eEcActiveIdc = ERROR_CON_SLICE_MV_COPY_CROSS_IDR_FREEZE_RES_CHANGE;
        }

        if (pCtx->pParam->bParseOnly) //parse only, disable EC method
            pCtx->pParam->eEcActiveIdc = ERROR_CON_DISABLE;
        InitErrorCon(pCtx);

        if (VIDEO_BITSTREAM_SVC == pCtx->pParam->sVideoProperty.eVideoBsType ||
            VIDEO_BITSTREAM_AVC == pCtx->pParam->sVideoProperty.eVideoBsType)
        {
            pCtx->eVideoType = pCtx->pParam->sVideoProperty.eVideoBsType;
        }
        else
        {
            pCtx->eVideoType = VIDEO_BITSTREAM_DEFAULT;
        }

        WelsLog(&(pCtx->sLogCtx), WELS_LOG_INFO, $"eVideoType: {(int32_t)pCtx->eVideoType}");

        return ERR_NONE;
    }

    /*!
     *************************************************************************************
     * \brief   Initialize Wels decoder parameters and memory
     *
     * \param   pCtx input context to be initialized at first stage
     *
     * \return  0 - successed
     * \return  1 - failed
     *
     * \note    N/A
     *************************************************************************************
     */
    public static int32_t WelsInitDecoder(SWelsDecoderContext* pCtx, SLogContext* pLogCtx)
    {
        if (pCtx == null)
        {
            return ERR_INFO_INVALID_PTR;
        }

        // open decoder
        return WelsOpenDecoder(pCtx, pLogCtx);
    }

    /*!
     *************************************************************************************
     * \brief   Uninitialize Wels decoder parameters and memory
     *
     * \param   pCtx input context to be uninitialized at release stage
     *
     * \return  NONE
     *
     * \note    N/A
     *************************************************************************************
     */
    public static void WelsEndDecoder(SWelsDecoderContext* pCtx)
    {
        // close decoder
        WelsCloseDecoder(pCtx);
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
     *************************************************************************************
     * \brief   First entrance to decoding core interface.
     *
     * \param   pCtx            decoder context
     * \param   pBufBs          bit streaming buffer
     * \param   kBsLen          size in bytes length of bit streaming buffer input
     * \param   ppDst           picture payload data to be output
     * \param   pDstBufInfo     buf information of ouput data
     *
     * \return  0 - successed
     * \return  1 - failed
     *
     * \note    N/A
     *************************************************************************************
     */
    // Returns false if wrapping sRawData to pHead would overwrite bytes still referenced
    // by a queued NAL's slice bit-reader, indicating the wrap is unsafe.
    private static bool RawDataWrapIsClean(SWelsDecoderContext* pCtx, int32_t iNewBytes)
    {
        SAccessUnit* pAu = pCtx->pAccessUnitList;
        if (pAu == null || pAu->pNalUnitsList == null || pAu->uiAvailUnitsNum == 0) return true;
        uint8_t* pHead = pCtx->sRawData.pHead;
        for (uint32_t i = 0; i < pAu->uiAvailUnitsNum; ++i)
        {
            SNalUnit* pNal = pAu->pNalUnitsList[i];
            if (pNal == null) continue;
            uint8_t* pStart = pNal->sNalData.sVclNal.sSliceBitsRead.pStartBuf;
            if (pStart != null && pStart >= pHead && pStart < pHead + iNewBytes) return false;
        }
        return true;
    }

    // pDstBsInfo is unused here upstream too. The parse-only saved data branch is
    // kept, but parse-only mode cannot be initialised in this port.
    public static int32_t WelsDecodeBs(SWelsDecoderContext* pCtx, uint8_t* kpBsBuf, int32_t kiBsLen,
                                       uint8_t** ppDst, SBufferInfo* pDstBufInfo, SParserBsInfo* pDstBsInfo)
    {
        if (!pCtx->bEndOfStreamFlag)
        {
            SDataBuffer* pRawData = &pCtx->sRawData;
            SDataBuffer* pSavedData = null;

            int32_t iSrcIdx = 0; //the index of source bit-stream till now after parsing one or more NALs
            int32_t iSrcConsumed = 0; // consumed bit count of source bs
            int32_t iDstIdx = 0; //the size of current NAL after 0x03 removal and 00 00 01 removal
            int32_t iSrcLength = 0; //the total size of current AU or NAL
            int32_t iRet = 0;
            int32_t iConsumedBytes = 0;
            int32_t iOffset = 0;

            uint8_t* pSrcNal = null;
            uint8_t* pDstNal = null;
            uint8_t* pNalPayload = null;


            if (null == DetectStartCodePrefix(kpBsBuf, &iOffset,
                                               kiBsLen))
            {  //CAN'T find the 00 00 01 start prefix from the source buffer
                pCtx->iErrorCode |= (int32_t)dsBitstreamError;
                return (int32_t)dsBitstreamError;
            }

            pSrcNal = kpBsBuf + iOffset;
            iSrcLength = kiBsLen - iOffset;

            if ((kiBsLen + 4) > (pRawData->pEnd - pRawData->pCurPos))
            {
                if (!RawDataWrapIsClean(pCtx, kiBsLen + 4))
                {
                    pCtx->iErrorCode |= (int32_t)dsOutOfMemory;
                    return pCtx->iErrorCode;
                }
                pRawData->pCurPos = pRawData->pHead;
            }

            if (pCtx->pParam->bParseOnly)
            {
                pSavedData = &pCtx->sSavedData;
                if ((kiBsLen + 4) > (pSavedData->pEnd - pSavedData->pCurPos))
                {
                    pSavedData->pCurPos = pSavedData->pHead;
                }
            }
            //copy raw data from source buffer (application) to raw data buffer (codec inside)
            //0x03 removal and extract all of NAL Unit from current raw data
            pDstNal = pRawData->pCurPos;

            bool bNalStartBytes = false;

            while (iSrcConsumed < iSrcLength)
            {
                if ((2 + iSrcConsumed < iSrcLength) && (0 == LD16(pSrcNal + iSrcIdx)) && (pSrcNal[2 + iSrcIdx] <= 0x03))
                {
                    if (bNalStartBytes && (pSrcNal[2 + iSrcIdx] != 0x00 && pSrcNal[2 + iSrcIdx] != 0x01))
                    {
                        pCtx->iErrorCode |= (int32_t)dsBitstreamError;
                        return pCtx->iErrorCode;
                    }

                    if (pSrcNal[2 + iSrcIdx] == 0x02)
                    {
                        pCtx->iErrorCode |= (int32_t)dsBitstreamError;
                        return pCtx->iErrorCode;
                    }
                    else if (pSrcNal[2 + iSrcIdx] == 0x00)
                    {
                        pDstNal[iDstIdx++] = pSrcNal[iSrcIdx++];
                        iSrcConsumed++;
                        bNalStartBytes = true;
                    }
                    else if (pSrcNal[2 + iSrcIdx] == 0x03)
                    {
                        if ((3 + iSrcConsumed < iSrcLength) && pSrcNal[3 + iSrcIdx] > 0x03)
                        {
                            /* Just skip */
                        }
                        else
                        {
                            ST16(pDstNal + iDstIdx, 0);
                            iDstIdx += 2;
                        }
                        iSrcIdx += 3;
                        iSrcConsumed += 3;
                    }
                    else
                    { // 0x01
                        bNalStartBytes = false;

                        iConsumedBytes = 0;
                        pDstNal[iDstIdx] = pDstNal[iDstIdx + 1] = pDstNal[iDstIdx + 2] = pDstNal[iDstIdx + 3] =
                                             0; // set 4 reserved bytes to zero
                        pNalPayload = ParseNalHeader(pCtx, &pCtx->sCurNalHead, pDstNal, iDstIdx, pSrcNal - 3, iSrcIdx + 3, &iConsumedBytes);
                        if (pNalPayload != null)
                        { //parse correct
                            if (IS_PARAM_SETS_NALS(pCtx->sCurNalHead.eNalUnitType))
                            {
                                iRet = ParseNonVclNal(pCtx, pNalPayload, iDstIdx - iConsumedBytes, pSrcNal - 3, iSrcIdx + 3);
                            }
                            CheckAndFinishLastPic(pCtx, ppDst, pDstBufInfo);
                            if (pCtx->bAuReadyFlag && pCtx->pAccessUnitList->uiAvailUnitsNum != 0)
                            {
                                if (GetThreadCount(pCtx) <= 1)
                                {
                                    ConstructAccessUnit(pCtx, ppDst, pDstBufInfo);
                                }
                            }
                        }
                        DecodeFinishUpdate(pCtx);

                        if ((((int32_t)dsOutOfMemory | (int32_t)dsNoParamSets) & pCtx->iErrorCode) != 0)
                        {
                            // LONG_TERM_REF
                            pCtx->bParamSetsLostFlag = true;
                            if (((int32_t)dsOutOfMemory & pCtx->iErrorCode) != 0)
                            {
                                return pCtx->iErrorCode;
                            }
                        }
                        if (iRet != 0)
                        {
                            iRet = 0;
                            if (((int32_t)dsNoParamSets & pCtx->iErrorCode) != 0)
                            {
                                // LONG_TERM_REF
                                pCtx->bParamSetsLostFlag = true;
                            }
                            return pCtx->iErrorCode;
                        }

                        if (pCtx->iErrorCode != ERR_NONE && (pCtx->iErrorCode & (int32_t)dsDataErrorConcealed) == 0)
                        {
                            return pCtx->iErrorCode;
                        }

                        pDstNal += (iDstIdx + 4); //init, increase 4 reserved zero bytes, used to store the next NAL
                        if ((iSrcLength - iSrcConsumed + 4) > (pRawData->pEnd - pDstNal))
                        {
                            if (!RawDataWrapIsClean(pCtx, iSrcLength - iSrcConsumed + 4))
                            {
                                pCtx->iErrorCode |= (int32_t)dsOutOfMemory;
                                return pCtx->iErrorCode;
                            }
                            pDstNal = pRawData->pCurPos = pRawData->pHead;
                        }
                        else
                        {
                            pRawData->pCurPos = pDstNal;
                        }

                        pSrcNal += iSrcIdx + 3;
                        iSrcConsumed += 3;
                        iSrcIdx = 0;
                        iDstIdx = 0; //reset 0, used to statistic the length of next NAL
                    }
                    continue;
                }
                pDstNal[iDstIdx++] = pSrcNal[iSrcIdx++];
                iSrcConsumed++;
            }

            //last NAL decoding

            iConsumedBytes = 0;
            pDstNal[iDstIdx] = pDstNal[iDstIdx + 1] = pDstNal[iDstIdx + 2] = pDstNal[iDstIdx + 3] =
                                 0; // set 4 reserved bytes to zero
            pRawData->pCurPos = pDstNal + iDstIdx + 4; //init, increase 4 reserved zero bytes, used to store the next NAL
            pNalPayload = ParseNalHeader(pCtx, &pCtx->sCurNalHead, pDstNal, iDstIdx, pSrcNal - 3, iSrcIdx + 3, &iConsumedBytes);
            if (pNalPayload != null)
            { //parse correct
                if (IS_PARAM_SETS_NALS(pCtx->sCurNalHead.eNalUnitType))
                {
                    iRet = ParseNonVclNal(pCtx, pNalPayload, iDstIdx - iConsumedBytes, pSrcNal - 3, iSrcIdx + 3);
                }
                if (GetThreadCount(pCtx) <= 1)
                {
                    CheckAndFinishLastPic(pCtx, ppDst, pDstBufInfo);
                }
                if (pCtx->bAuReadyFlag && pCtx->pAccessUnitList->uiAvailUnitsNum != 0)
                {
                    if (GetThreadCount(pCtx) <= 1)
                    {
                        ConstructAccessUnit(pCtx, ppDst, pDstBufInfo);
                    }
                }
            }
            DecodeFinishUpdate(pCtx);

            if ((((int32_t)dsOutOfMemory | (int32_t)dsNoParamSets) & pCtx->iErrorCode) != 0)
            {
                // LONG_TERM_REF
                pCtx->bParamSetsLostFlag = true;
                return pCtx->iErrorCode;
            }
            if (iRet != 0)
            {
                iRet = 0;
                if (((int32_t)dsNoParamSets & pCtx->iErrorCode) != 0)
                {
                    // LONG_TERM_REF
                    pCtx->bParamSetsLostFlag = true;
                }
                return pCtx->iErrorCode;
            }
        }
        else
        { /* no supplementary picture payload input, but stored a picture */
            SAccessUnit* pCurAu =
                pCtx->pAccessUnitList; // current access unit, it will never point to NULL after decode's successful initialization

            if (pCurAu->uiAvailUnitsNum == 0)
            {
                return pCtx->iErrorCode;
            }
            else
            {
                pCtx->pAccessUnitList->uiEndPos = pCtx->pAccessUnitList->uiAvailUnitsNum - 1;

                ConstructAccessUnit(pCtx, ppDst, pDstBufInfo);
            }
            DecodeFinishUpdate(pCtx);

            if ((((int32_t)dsOutOfMemory | (int32_t)dsNoParamSets) & pCtx->iErrorCode) != 0)
            {
                // LONG_TERM_REF
                pCtx->bParamSetsLostFlag = true;
                return pCtx->iErrorCode;
            }
        }

        return pCtx->iErrorCode;
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

    // The C kernels only; uiCpuFlag is passed on and unused.
    public static void InitDecFuncs(SWelsDecoderContext* pCtx, uint32_t uiCpuFlag)
    {
        WelsBlockFuncInit(&pCtx->sBlockFunc, (int32_t)uiCpuFlag);
        InitPredFunc(pCtx, uiCpuFlag);
        InitMcFunc(&(pCtx->sMcFunc), uiCpuFlag);
        InitExpandPictureFunc(&(pCtx->sExpandPicFunc), uiCpuFlag);
        DeblockingInit(&pCtx->sDeblockingFunc, (int32_t)uiCpuFlag);
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

    //reset decoder number related statistics info
    public static void ResetDecStatNums(SDecoderStatistics* pDecStat)
    {
        uint32_t uiWidth = pDecStat->uiWidth;
        uint32_t uiHeight = pDecStat->uiHeight;
        int32_t iAvgLumaQp = pDecStat->iAvgLumaQp;
        uint32_t iLogInterval = pDecStat->iStatisticsLogInterval;
        uint32_t uiProfile = pDecStat->uiProfile;
        uint32_t uiLevel = pDecStat->uiLevel;
        *pDecStat = default; // memset (pDecStat, 0, sizeof (SDecoderStatistics))
        pDecStat->uiWidth = uiWidth;
        pDecStat->uiHeight = uiHeight;
        pDecStat->iAvgLumaQp = iAvgLumaQp;
        pDecStat->iStatisticsLogInterval = iLogInterval;
        pDecStat->uiProfile = uiProfile;
        pDecStat->uiLevel = uiLevel;
    }

    //update information when freezing occurs, including IDR/non-IDR number
    public static void UpdateDecStatFreezingInfo(bool kbIdrFlag, SDecoderStatistics* pDecStat)
    {
        if (kbIdrFlag)
            pDecStat->uiFreezingIDRNum++;
        else
            pDecStat->uiFreezingNonIDRNum++;
    }

    //update information when no freezing occurs, including QP, correct IDR number, ECed IDR number
    public static void UpdateDecStatNoFreezingInfo(SWelsDecoderContext* pCtx)
    {
        SDqLayer* pCurDq = pCtx->pCurDqLayer;
        SPicture* pPic = pCtx->pDec;
        SDecoderStatistics* pDecStat = pCtx->pDecoderStatistics;

        if (pDecStat->iAvgLumaQp == -1) //first correct frame received
            pDecStat->iAvgLumaQp = 0;

        //update QP info
        int32_t iTotalQp = 0;
        int32_t kiMbNum = pCurDq->iMbWidth * pCurDq->iMbHeight;
        if (pCtx->pParam->eEcActiveIdc == ERROR_CON_DISABLE)
        { //all correct
            for (int32_t iMb = 0; iMb < kiMbNum; ++iMb)
            {
                iTotalQp += pCurDq->pLumaQp[iMb];
            }
            iTotalQp /= kiMbNum;
        }
        else
        {
            int32_t iCorrectMbNum = 0;
            for (int32_t iMb = 0; iMb < kiMbNum; ++iMb)
            {
                int32_t iCorrect = pCurDq->pMbCorrectlyDecodedFlag[iMb] ? 1 : 0;
                iCorrectMbNum += iCorrect;
                iTotalQp += pCurDq->pLumaQp[iMb] * iCorrect;
            }
            if (iCorrectMbNum == 0) //non MB is correct, should remain QP statistic info
                iTotalQp = pDecStat->iAvgLumaQp;
            else
                iTotalQp /= iCorrectMbNum;
        }
        if (pDecStat->uiDecodedFrameCount + 1 == 0)
        { //maximum uint32_t reached
            ResetDecStatNums(pDecStat);
            pDecStat->iAvgLumaQp = iTotalQp;
        }
        else
            // The sum is unsigned int arithmetic in C, so it wraps at 32 bits before the widening.
            pDecStat->iAvgLumaQp = (int)((uint64_t)((uint32_t)pDecStat->iAvgLumaQp * pDecStat->uiDecodedFrameCount + (uint32_t)iTotalQp) /
                                         (pDecStat->uiDecodedFrameCount + 1));

        //update IDR number
        if (pCurDq->sLayerInfo.sNalHeaderExt.bIdrFlag)
        {
            pDecStat->uiIDRCorrectNum += pPic->bIsComplete ? 1u : 0u;
            if (pCtx->pParam->eEcActiveIdc != ERROR_CON_DISABLE)
                pDecStat->uiEcIDRNum += !pPic->bIsComplete ? 1u : 0u;
        }
    }

    //update decoder statistics information
    public static void UpdateDecStat(SWelsDecoderContext* pCtx, bool kbOutput)
    {
        if (pCtx->bFreezeOutput)
            UpdateDecStatFreezingInfo(pCtx->pCurDqLayer->sLayerInfo.sNalHeaderExt.bIdrFlag, pCtx->pDecoderStatistics);
        else if (kbOutput)
            UpdateDecStatNoFreezingInfo(pCtx);
    }
}
