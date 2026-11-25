//-----------------------------------------------------------------------------
// Filename: pic_queue.cs
//
// Description: Port of codec/decoder/core/inc/pic_queue.h and src/pic_queue.cpp:
// the picture buffer queue and picture allocation.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// The threaded decoder is not ported, so pThreadCtx is always null and
// AllocPicture never creates the per-MB-row ready events; FreePicture keeps the
// event check for the day it is.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 25 Nov 2026  Claude          Created.
// 25 Nov 2026  Claude          D2: pic_queue.cpp.
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

namespace SIPSorcery.H264.Decoder;

/// <summary>TagPicBuff (PPicBuff is SPicBuff*).</summary>
internal unsafe struct SPicBuff
{
    public SPicture** ppPic;
    public int32_t iCapacity;  // capacity size of queue
    public int32_t iCurrentIdx;
    public CMemoryAlign* pMa;  // allocator that owns this buffer's memory; used to free it
}

internal static unsafe partial class WelsDec
{
    public const int PICTURE_RESOLUTION_ALIGNMENT = 32;

    ///////////////////////////////////Recycled queue management for pictures///////////////////////////////////
    /*   ______________________________________
      -->| P0 | P1 | P2 | P3 | P4 | .. | Pn-1 |-->
         --------------------------------------
     *
     *  How does it work?
     *  node <- next; ++ next;
     *
    */



    public static SPicture* AllocPicture(SWelsDecoderContext* pCtx, int32_t kiPicWidth, int32_t kiPicHeight)
    {
        SPicture* pPic = null;
        int32_t iPicWidth = 0;
        int32_t iPicHeight = 0;

        int32_t iPicChromaWidth = 0;
        int32_t iPicChromaHeight = 0;
        int32_t iLumaSize = 0;
        int32_t iChromaSize = 0;
        CMemoryAlign* pMa = pCtx->pMemAlign;

        pPic = (SPicture*)pMa->WelsMallocz((uint32_t)sizeof(SPicture), "PPicture");
        if (null == pPic)
            return null;

        memset(pPic, 0, sizeof(SPicture));

        iPicWidth = WELS_ALIGN(kiPicWidth + (PADDING_LENGTH << 1), PICTURE_RESOLUTION_ALIGNMENT);
        iPicHeight = WELS_ALIGN(kiPicHeight + (PADDING_LENGTH << 1), PICTURE_RESOLUTION_ALIGNMENT);
        iPicChromaWidth = iPicWidth >> 1;
        iPicChromaHeight = iPicHeight >> 1;

        iLumaSize = iPicWidth * iPicHeight;
        iChromaSize = iPicChromaWidth * iPicChromaHeight;

        if (pCtx->pParam->bParseOnly)
        {
            pPic->pBuffer[0] = pPic->pBuffer[1] = pPic->pBuffer[2] = null;
            pPic->pData[0] = pPic->pData[1] = pPic->pData[2] = null;
            pPic->iLinesize[0] = iPicWidth;
            pPic->iLinesize[1] = pPic->iLinesize[2] = iPicChromaWidth;
        }
        else
        {
            pPic->pBuffer[0] = (uint8_t*)pMa->WelsMallocz((uint32_t)(iLumaSize /* luma */
                               + (iChromaSize << 1)) /* Cb,Cr */, "_pic->buffer[0]");
            if (null == pPic->pBuffer[0].p)
            {
                FreePicture(pPic, pMa);
                return null;
            }

            memset(pPic->pBuffer[0], 128, (iLumaSize + (iChromaSize << 1)));
            pPic->iLinesize[0] = iPicWidth;
            pPic->iLinesize[1] = pPic->iLinesize[2] = iPicChromaWidth;
            pPic->pBuffer[1] = pPic->pBuffer[0] + iLumaSize;
            pPic->pBuffer[2] = pPic->pBuffer[1] + iChromaSize;
            pPic->pData[0] = pPic->pBuffer[0] + (1 + pPic->iLinesize[0]) * PADDING_LENGTH;
            pPic->pData[1] = pPic->pBuffer[1] + /*WELS_ALIGN*/ (((1 + pPic->iLinesize[1]) * PADDING_LENGTH) >> 1);
            pPic->pData[2] = pPic->pBuffer[2] + /*WELS_ALIGN*/ (((1 + pPic->iLinesize[2]) * PADDING_LENGTH) >> 1);
        }
        pPic->iPlanes = 3;    // yv12 in default
        pPic->iWidthInPixel = kiPicWidth;
        pPic->iHeightInPixel = kiPicHeight;
        pPic->iFrameNum = -1;
        pPic->iRefCount = 0;
        pPic->pSetUnRef = null;

        uint32_t uiMbWidth = (uint32_t)((kiPicWidth + 15) >> 4);
        uint32_t uiMbHeight = (uint32_t)((kiPicHeight + 15) >> 4);
        uint32_t uiMbCount = uiMbWidth * uiMbHeight;

        pPic->pMbCorrectlyDecodedFlag = (bool*)pMa->WelsMallocz(uiMbCount * sizeof(bool), "pPic->pMbCorrectlyDecodedFlag");
        pPic->pNzc = GetThreadCount(pCtx) > 1 ? (InlineArray24<int8_t>*)pMa->WelsMallocz(uiMbCount * 24, "pPic->pNzc") : null;
        pPic->pMbType = (uint32_t*)pMa->WelsMallocz(uiMbCount * sizeof(uint32_t), "pPic->pMbType");
        pPic->pMv[(int32_t)LIST_0] = (InlineArray16<InlineArray2<int16_t>>*)pMa->WelsMallocz((uint32_t)(uiMbCount * sizeof(
                              int16_t) * (int32_t)MV_A * MB_BLOCK4x4_NUM), "pPic->pMv[]");
        pPic->pMv[(int32_t)LIST_1] = (InlineArray16<InlineArray2<int16_t>>*)pMa->WelsMallocz((uint32_t)(uiMbCount * sizeof(
                              int16_t) * (int32_t)MV_A * MB_BLOCK4x4_NUM), "pPic->pMv[]");
        pPic->pRefIndex[(int32_t)LIST_0] = (InlineArray16<int8_t>*)pMa->WelsMallocz((uint32_t)(uiMbCount * sizeof(
                                    int8_t) * MB_BLOCK4x4_NUM), "pCtx->sMb.pRefIndex[]");
        pPic->pRefIndex[(int32_t)LIST_1] = (InlineArray16<int8_t>*)pMa->WelsMallocz((uint32_t)(uiMbCount * sizeof(
                                    int8_t) * MB_BLOCK4x4_NUM), "pCtx->sMb.pRefIndex[]");
        // pCtx->pThreadCtx is always null here (the threaded decoder is not ported), so no
        // pReadyEvent array: C creates one event per MB row when it is set.
        pPic->pReadyEvent = null;

        return pPic;
    }

    public static void FreePicture(SPicture* pPic, CMemoryAlign* pMa)
    {
        if (null != pPic)
        {
            if (pPic->pBuffer[0].p != null)
            {
                pMa->WelsFree(pPic->pBuffer[0], "pPic->pBuffer[0]");
                pPic->pBuffer[0] = null;
            }

            if (pPic->pMbCorrectlyDecodedFlag != null)
            {
                pMa->WelsFree(pPic->pMbCorrectlyDecodedFlag, "pPic->pMbCorrectlyDecodedFlag");
                pPic->pMbCorrectlyDecodedFlag = null;
            }

            if (pPic->pNzc != null)
            {
                pMa->WelsFree(pPic->pNzc, "pPic->pNzc");
                pPic->pNzc = null;
            }

            if (pPic->pMbType != null)
            {
                pMa->WelsFree(pPic->pMbType, "pPic->pMbType");
                pPic->pMbType = null;
            }

            for (int32_t listIdx = (int32_t)LIST_0; listIdx < (int32_t)LIST_A; ++listIdx)
            {
                if (pPic->pMv[listIdx].p != null)
                {
                    pMa->WelsFree(pPic->pMv[listIdx], "pPic->pMv[]");
                    pPic->pMv[listIdx] = null;
                }

                if (pPic->pRefIndex[listIdx].p != null)
                {
                    pMa->WelsFree(pPic->pRefIndex[listIdx], "pPic->pRefIndex[]");
                    pPic->pRefIndex[listIdx] = null;
                }
            }
            if (pPic->pReadyEvent != null)
            {
                // C closes one event per MB row first (CLOSE_EVENT); none are created
                // while the threaded decoder is not ported.
                pMa->WelsFree(pPic->pReadyEvent, "pPic->pReadyEvent");
                pPic->pReadyEvent = null;
            }
            pMa->WelsFree(pPic, "pPic");
        }
    }

    public static SPicture* PrefetchPic(SPicBuff* pPicBuf)
    {
        int32_t iPicIdx = 0;
        SPicture* pPic = null;

        if (pPicBuf->iCapacity == 0)
        {
            return null;
        }

        for (iPicIdx = pPicBuf->iCurrentIdx + 1; iPicIdx < pPicBuf->iCapacity; ++iPicIdx)
        {
            if (pPicBuf->ppPic[iPicIdx] != null && !pPicBuf->ppPic[iPicIdx]->bUsedAsRef
                && pPicBuf->ppPic[iPicIdx]->iRefCount <= 0)
            {
                pPic = pPicBuf->ppPic[iPicIdx];
                break;
            }
        }
        if (pPic != null)
        {
            pPicBuf->iCurrentIdx = iPicIdx;
            pPic->iPicBuffIdx = iPicIdx;
            return pPic;
        }
        for (iPicIdx = 0; iPicIdx <= pPicBuf->iCurrentIdx; ++iPicIdx)
        {
            if (pPicBuf->ppPic[iPicIdx] != null && !pPicBuf->ppPic[iPicIdx]->bUsedAsRef
                && pPicBuf->ppPic[iPicIdx]->iRefCount <= 0)
            {
                pPic = pPicBuf->ppPic[iPicIdx];
                break;
            }
        }

        pPicBuf->iCurrentIdx = iPicIdx;
        if (pPic != null)
        {
            pPic->iPicBuffIdx = iPicIdx;
        }
        return pPic;
    }

    public static SPicture* PrefetchPicForThread(SPicBuff* pPicBuf)
    {
        SPicture* pPic = null;

        if (pPicBuf->iCapacity == 0)
        {
            return null;
        }
        pPic = pPicBuf->ppPic[pPicBuf->iCurrentIdx];
        pPic->iPicBuffIdx = pPicBuf->iCurrentIdx;
        if (++pPicBuf->iCurrentIdx >= pPicBuf->iCapacity)
        {
            pPicBuf->iCurrentIdx = 0;
        }
        return pPic;
    }

    public static SPicture* PrefetchLastPicForThread(SPicBuff* pPicBuf, int32_t iLastPicBuffIdx)
    {
        SPicture* pPic = null;

        if (pPicBuf->iCapacity == 0)
        {
            return null;
        }
        if (iLastPicBuffIdx >= 0 && iLastPicBuffIdx < pPicBuf->iCapacity)
        {
            pPic = pPicBuf->ppPic[iLastPicBuffIdx];
        }
        return pPic;
    }
}
