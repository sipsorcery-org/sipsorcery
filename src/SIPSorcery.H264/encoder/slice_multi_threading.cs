//-----------------------------------------------------------------------------
// Filename: slice_multi_threading.cs
//
// Description: Port of codec/encoder/core/src/slice_multi_threading.cpp and
// the SSliceThreading state from codec/encoder/core/inc/mt_defs.h (P5.7):
// the slice threads' resources (RequestMtResource, ReleaseMtResource), the
// per-slice bitstream handling (SetOneSliceBsBufferUnderMultithread,
// WriteSliceBs, AppendSliceToFrameBs) and the load balancing that resizes
// SM_FIXEDSLCNUM_SLICE slices from the time each one took to code
// (CalcSliceComplexRatio, NeedDynamicAdjust, DynamicAdjustSlicing,
// AdjustBaseLayer, AdjustEnhanceLayer, UpdateMbListNeighborParallel).
// DynamicDetectCpuCores is in encoder_ext.cs. The threads themselves are the
// task manager's .NET thread pool work items (wels_task_management.cs), so
// upstream's per-thread events, handles and their names are not needed. The
// mutexes become Interlocked operations: bThreadBsBufferUsage claims with
// CompareExchange and the task results OR into iEncoderError with
// Interlocked.Or. The MT_DEBUG tracing (TrackSliceComplexities,
// TrackSliceConsumeTime) is not ported.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 27 Nov 2026  Claude          Created (P5.7).
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
 * \file    slice_multi_threading.c
 *
 * \brief   slice based multiple threading
 *
 * \date    04/16/2010 Created
 *
 *************************************************************************************
 */

using System.Diagnostics;
using System.Threading;

namespace SIPSorcery.H264.Encoder;

internal unsafe struct SSliceThreadPrivateData
{
    public void* pWelsPEncCtx;
    public SFrameBSInfo* pFrameBsInfo;
    public int32_t iSliceIndex;     // slice index, zero based
    public int32_t iThreadIndex;    // thread index, zero based
}

internal unsafe struct SSliceThreading
{
    public SSliceThreadPrivateData* pThreadPEncCtx; // thread context, [iThreadIdx]
    // eventNamespace, pThreadHandles and the pSliceCoded/pReadySliceCoding/pUpdateMbList/
    // pFinUpdateMbList events: the thread pool's work items stand in for upstream's threads
    // (see the file header).
    // mutexSliceNumUpdate is for SM_SIZELIMITED_SLICE's threads (Phase 4).

    public PtrArray4<uint8_t> pThreadBsBuffer;              // [MAX_THREADS_NUM] actual memory for slice buffer
    public fixed int32_t bThreadBsBufferUsage[WelsEnc.MAX_THREADS_NUM]; // bool upstream: an int for Interlocked
    // mutexThreadBsBufferUsage: CWelsSliceEncodingTask claims and frees bThreadBsBufferUsage
    // with Interlocked. mutexEvent and mutexThreadSlcBuffReallocate: the events are not ported
    // and the slice buffers are reallocated by SM_SIZELIMITED_SLICE only.
}

internal static unsafe partial class WelsEnc
{
    public const float THRESHOLD_RMSE_CORE8 = 0.0320f; // v1.1: 0.0320f; v1.0: 0.02f
    public const float THRESHOLD_RMSE_CORE4 = 0.0215f; // v1.1: 0.0215f; v1.0: 0.03f
    public const float THRESHOLD_RMSE_CORE2 = 0.0200f; // v1.1: 0.0200f; v1.0: 0.04f

    /// <summary>
    /// WELS_DIV_ROUND with an int dividend and a uint32_t divisor, which C works out in
    /// unsigned arithmetic. Named apart so it does not hide the common overloads.
    /// </summary>
    private static int32_t WELS_DIV_ROUND_U32(int32_t x, uint32_t y) =>
        unchecked((int32_t)(y == 0 ? (uint32_t)x / (y + 1) : (y / 2 + (uint32_t)x) / y));

    public static void UpdateMbListNeighborParallel(SDqLayer* pCurDq,
                                                    SMB* pMbList,
                                                    int32_t uiSliceIdc)
    {
        SSliceCtx* pSliceCtx = &pCurDq->sSliceEncCtx;
        int32_t kiMbWidth = pSliceCtx->iMbWidth;
        int32_t iIdx = pCurDq->pFirstMbIdxOfSlice[uiSliceIdc];
        int32_t kiEndMbInSlice = iIdx + pCurDq->pCountMbNumInSlice[uiSliceIdc] - 1;

        do
        {
            UpdateMbNeighbor(pCurDq, &pMbList[iIdx], kiMbWidth, (uint16_t)uiSliceIdc);
            ++iIdx;
        } while (iIdx <= kiEndMbInSlice);
    }

    public static void CalcSliceComplexRatio(SDqLayer* pCurDq)
    {
        SSliceCtx* pSliceCtx = &pCurDq->sSliceEncCtx;
        SSlice** ppSliceInLayer = pCurDq->ppSliceInLayer;
        int32_t iSumAv = 0;
        int32_t kiSliceCount = pSliceCtx->iSliceNumInFrame;
        int32_t iSliceIdx = 0;
        int32_t* iAvI = stackalloc int32_t[MAX_SLICES_NUM];

        Debug.Assert(kiSliceCount <= MAX_SLICES_NUM);
        // WelsEmms: no MMX state in the port.

        while (iSliceIdx < kiSliceCount)
        {
            iAvI[iSliceIdx] = WELS_DIV_ROUND_U32(INT_MULTIPLY * ppSliceInLayer[iSliceIdx]->iCountMbNumInSlice,
                                                 ppSliceInLayer[iSliceIdx]->uiSliceConsumeTime);
            // MT_TRACE_LOG: ENABLE_TRACE_MT is off upstream.
            iSumAv += iAvI[iSliceIdx];

            ++iSliceIdx;
        }
        while (--iSliceIdx >= 0)
        {
            ppSliceInLayer[iSliceIdx]->iSliceComplexRatio = WELS_DIV_ROUND(INT_MULTIPLY * iAvI[iSliceIdx], iSumAv);
        }
    }

    public static int32_t NeedDynamicAdjust(SSlice** ppSliceInLayer, int32_t iSliceNum)
    {
        if (null == ppSliceInLayer)
        {
            return 0;
        }

        uint32_t uiTotalConsume = 0;
        int32_t iSliceIdx = 0;
        int32_t iNeedAdj = 0;

        // WelsEmms: no MMX state in the port.

        while (iSliceIdx < iSliceNum)
        {
            if (null == ppSliceInLayer[iSliceIdx])
            {
                return 0;
            }

            uiTotalConsume += ppSliceInLayer[iSliceIdx]->uiSliceConsumeTime;
            iSliceIdx++;
        }
        if (uiTotalConsume == 0)
        {
            return 0;
        }

        iSliceIdx = 0;
        float fThr = EPSN; // threshold for various cores cases
        float fRmse = .0f; // root mean square error of pSlice consume ratios
        float kfMeanRatio = 1.0f / iSliceNum;
        do
        {
            float fRatio = 1.0f * ppSliceInLayer[iSliceIdx]->uiSliceConsumeTime / uiTotalConsume;
            float fDiffRatio = fRatio - kfMeanRatio;
            fRmse += (fDiffRatio * fDiffRatio);
            ++iSliceIdx;
        } while (iSliceIdx + 1 < iSliceNum);
        fRmse = MathF.Sqrt(fRmse / iSliceNum);
        if (iSliceNum >= 8)
        {
            fThr += THRESHOLD_RMSE_CORE8;
        }
        else if (iSliceNum >= 4)
        {
            fThr += THRESHOLD_RMSE_CORE4;
        }
        else if (iSliceNum >= 2)
        {
            fThr += THRESHOLD_RMSE_CORE2;
        }
        else
            fThr = 1.0f;
        if (fRmse > fThr)
            iNeedAdj = 1;

        return iNeedAdj;
    }

    public static void DynamicAdjustSlicing(sWelsEncCtx* pCtx,
                                            SDqLayer* pCurDqLayer,
                                            int32_t iCurDid)
    {
        SSliceCtx* pSliceCtx = &pCurDqLayer->sSliceEncCtx;
        SSlice** ppSliceInLayer = pCurDqLayer->ppSliceInLayer;
        int32_t kiCountSliceNum = pSliceCtx->iSliceNumInFrame;
        int32_t kiCountNumMb = pSliceCtx->iMbNumInFrame;
        int32_t iMinimalMbNum =
          pSliceCtx->iMbWidth; // in theory we need only 1 SMB, here let it as one SMB row required
        int32_t iMaximalMbNum = 0; // dynamically assign later
        int32_t iMbNumLeft = kiCountNumMb;
        int32_t* iRunLen = stackalloc int32_t[MAX_THREADS_NUM];
        int32_t iSliceIdx = 0;

        for (int32_t i = 0; i < MAX_THREADS_NUM; i++)
            iRunLen[i] = 0;

        int32_t iNumMbInEachGom = 0;
        SWelsSvcRc* pWelsSvcRc = &pCtx->pWelsSvcRc[iCurDid];
        if (pCtx->pSvcParam->iRCMode != RC_OFF_MODE)
        {
            iNumMbInEachGom = pWelsSvcRc->iNumberMbGom;

            if (iNumMbInEachGom <= 0)
            {
                WelsLog(&(pCtx->sLogCtx), WELS_LOG_ERROR,
                        $"[MT] DynamicAdjustSlicing(), invalid iNumMbInEachGom= {iNumMbInEachGom} from RC, iDid= {iCurDid}, iCountNumMb= {kiCountNumMb}");
                return;
            }

            // do not adjust in case no extra iNumMbInEachGom based left for slicing adjustment,
            // extra MB of non integrated GOM assigned at the last pSlice in default, keep up on early initial result.
            if (iNumMbInEachGom * kiCountSliceNum >= kiCountNumMb)
            {
                return;
            }
            iMinimalMbNum = iNumMbInEachGom;
        }
        else
        {
            // If the number of requested slices equals or exceeds the total macroblocks
            // in the frame, each slice cannot be assigned even 1 macroblock. In this
            // case, do not adjust slice sizes.
            if (kiCountSliceNum >= kiCountNumMb)
            {
                WelsLog(&(pCtx->sLogCtx), WELS_LOG_WARNING,
                        $"[MT] DynamicAdjustSlicing(), requested slice number ({kiCountSliceNum}) equals or exceeds total macroblocks ({kiCountNumMb}), do not adjust");
                return;
            }
            else if (iMinimalMbNum * kiCountSliceNum >= kiCountNumMb)
            {
                // When rate control is off, iMinimalMbNum defaults to 1 macroblock row
                // (iMbWidth). For extreme aspect ratios (such as very wide resolutions),
                // requiring 1 row per slice may exceed total frame capacity. Fall back to
                // 1 macroblock per slice to ensure valid upper bounds and allow proper
                // load balancing across slices.
                iMinimalMbNum = 1;
            }
        }

        if (kiCountSliceNum < 2 || (kiCountSliceNum & 0x01) != 0) // we need suppose uiSliceNum is even for multiple threading
            return;

        iMaximalMbNum = kiCountNumMb - (kiCountSliceNum - 1) * iMinimalMbNum;

        // WelsEmms: no MMX state in the port.

        iSliceIdx = 0;
        while (iSliceIdx + 1 < kiCountSliceNum)
        {
            int32_t iNumMbAssigning = WELS_DIV_ROUND(kiCountNumMb * ppSliceInLayer[iSliceIdx]->iSliceComplexRatio, INT_MULTIPLY);

            // GOM boundary aligned
            if (pCtx->pSvcParam->iRCMode != RC_OFF_MODE)
            {
                iNumMbAssigning = iNumMbAssigning / iNumMbInEachGom * iNumMbInEachGom;
            }

            // make sure one GOM at least in each pSlice for safe
            if (iNumMbAssigning < iMinimalMbNum)
                iNumMbAssigning = iMinimalMbNum;
            else if (iNumMbAssigning > iMaximalMbNum)
                iNumMbAssigning = iMaximalMbNum;

            Debug.Assert(iNumMbAssigning > 0);

            iMbNumLeft -= iNumMbAssigning;
            if (iMbNumLeft <= 0) // error due to we can not support slice_skip now yet, do not adjust this time
            {
                Debug.Assert(false);
                return;
            }
            iRunLen[iSliceIdx] = iNumMbAssigning;
            ++iSliceIdx;
            iMaximalMbNum = iMbNumLeft - (kiCountSliceNum - iSliceIdx - 1) * iMinimalMbNum; // get maximal num_mb in left parts
        }
        iRunLen[iSliceIdx] = iMbNumLeft;
        pCurDqLayer->bNeedAdjustingSlicing = 0 == DynamicAdjustSlicePEncCtxAll(pCurDqLayer, iRunLen);
    }

    public static int32_t RequestMtResource(sWelsEncCtx** ppCtx, SWelsSvcCodingParam* pCodingParam, int32_t iCountBsLen,
                                            int32_t iMaxSliceBufferSize, bool bDynamicSlice)
    {
        CMemoryAlign* pMa = null;
        SWelsSvcCodingParam* pPara = null;
        SSliceThreading* pSmt = null;
        int32_t iNumSpatialLayers = 0;
        int32_t iThreadNum = 0;
        int32_t iIdx = 0;

        if (null == ppCtx || null == pCodingParam || null == *ppCtx || iCountBsLen <= 0)
            return 1;
        pMa = (*ppCtx)->pMemAlign;
        pPara = pCodingParam;
        iNumSpatialLayers = pPara->iSpatialLayerNum;
        iThreadNum = pPara->iMultipleThreadIdc;

        Debug.Assert(iThreadNum > 0);

        pSmt = (SSliceThreading*)pMa->WelsMalloc((uint32_t)sizeof(SSliceThreading), "SSliceThreading");
        if (null == pSmt) return 1;
        memset(pSmt, 0, sizeof(SSliceThreading));
        (*ppCtx)->pSliceThreading = pSmt;
        pSmt->pThreadPEncCtx = (SSliceThreadPrivateData*)pMa->WelsMalloc((uint32_t)(sizeof(SSliceThreadPrivateData) * iThreadNum),
                               "pThreadPEncCtx");
        if (null == pSmt->pThreadPEncCtx) return 1;

        // eventNamespace and MT_DEBUG's pFSliceDiff: not ported (see the file header).

        iIdx = 0;
        while (iIdx < iThreadNum)
        {
            pSmt->pThreadPEncCtx[iIdx].pWelsPEncCtx = (void*)*ppCtx;
            pSmt->pThreadPEncCtx[iIdx].iSliceIndex = iIdx;
            pSmt->pThreadPEncCtx[iIdx].iThreadIndex = iIdx;
            // pThreadHandles and the four events per thread: not ported.
            ++iIdx;
        }

        // pSliceCodedMasterEvent and mutexSliceNumUpdate: not ported.

        (*ppCtx)->pTaskManage = IWelsTaskManage.CreateTaskManage(*ppCtx, iNumSpatialLayers, bDynamicSlice);
        if (0 == (*ppCtx)->pTaskManage) return 1;

        int32_t iThreadBufferNum = WELS_MIN(IWelsTaskManage.From((*ppCtx)->pTaskManage).GetThreadPoolThreadNum(), MAX_THREADS_NUM);

        for (iIdx = 0; iIdx < iThreadBufferNum; iIdx++)
        {
            pSmt->pThreadBsBuffer[iIdx] = (uint8_t*)pMa->WelsMallocz((uint32_t)iCountBsLen, "pSmt->pThreadBsBuffer");
            if (pSmt->pThreadBsBuffer[iIdx].IsNull) return 1;
        }
        // mutexThreadBsBufferUsage, mutexEvent, mutexThreadSlcBuffReallocate and
        // mutexEncoderError: Interlocked instead (see the file header).

        return 0;
    }

    public static void ReleaseMtResource(sWelsEncCtx** ppCtx)
    {
        SSliceThreading* pSmt = null;
        CMemoryAlign* pMa = null;

        if (null == ppCtx || null == *ppCtx)
            return;

        pMa = (*ppCtx)->pMemAlign;
        pSmt = (*ppCtx)->pSliceThreading;

        if (null == pSmt)
            return;

        // The events and mutexes: not ported.
        if (pSmt->pThreadPEncCtx != null)
        {
            pMa->WelsFree(pSmt->pThreadPEncCtx, "pThreadPEncCtx");
            pSmt->pThreadPEncCtx = null;
        }

        for (int i = 0; i < MAX_THREADS_NUM; i++)
        {
            if (!pSmt->pThreadBsBuffer[i].IsNull)
            {
                pMa->WelsFree((uint8_t*)pSmt->pThreadBsBuffer[i], "pSmt->pThreadBsBuffer");
                pSmt->pThreadBsBuffer[i] = (uint8_t*)null;
            }
        }
        for (int i = 0; i < MAX_THREADS_NUM; i++)
            pSmt->bThreadBsBufferUsage[i] = 0;

        if ((*ppCtx)->pTaskManage != 0)
        {
            IWelsTaskManage.DeleteTaskManage(ref (*ppCtx)->pTaskManage);
        }

        pMa->WelsFree((*ppCtx)->pSliceThreading, "SSliceThreading");
        (*ppCtx)->pSliceThreading = null;
    }

    public static int32_t AppendSliceToFrameBs(sWelsEncCtx* pCtx, SLayerBSInfo* pLbi, int32_t iSliceCount)
    {
        SSlice** ppSliceInlayer = pCtx->pCurDqLayer->ppSliceInLayer;
        SWelsSliceBs* pSliceBs = null;
        int32_t iLayerSize = 0;
        int32_t iNalIdxBase = pLbi->iNalCount;
        int32_t iSliceIdx = 0;

        iNalIdxBase = pLbi->iNalCount = 0;
        while (iSliceIdx < iSliceCount)
        {
            pSliceBs = &ppSliceInlayer[iSliceIdx]->sSliceBs;
            if (pSliceBs != null && pSliceBs->uiBsPos > 0)
            {
                int32_t iNalIdx = 0;
                int32_t iCountNal = pSliceBs->iNalIndex;

                if ((uint64_t)pCtx->iPosBsBuffer + pSliceBs->uiBsPos > (uint64_t)pCtx->iFrameBsSize)
                {
                    WelsLog(&pCtx->sLogCtx, WELS_LOG_ERROR,
                            $"AppendSliceToFrameBs(), insufficient memory for the allocation! iPosBsBuffer:{pCtx->iPosBsBuffer}, uiBsPos:{pSliceBs->uiBsPos}, iFrameBsSize:{pCtx->iFrameBsSize}");
                    pCtx->iEncoderError |= ENC_RETURN_MEMALLOCERR;
                    return 0;
                }

                memmove(pCtx->pFrameBs + pCtx->iPosBsBuffer, pSliceBs->pBs, (int)pSliceBs->uiBsPos); // confirmed_safe_unsafe_usage
                pCtx->iPosBsBuffer += (int32_t)pSliceBs->uiBsPos;

                iLayerSize += (int32_t)pSliceBs->uiBsPos;

                while (iNalIdx < iCountNal)
                {
                    pLbi->pNalLengthInByte[iNalIdxBase + iNalIdx] = pSliceBs->iNalLen[iNalIdx];
                    ++iNalIdx;
                }
                pLbi->iNalCount += iCountNal;
                iNalIdxBase += iCountNal;
            }
            ++iSliceIdx;
        }

        return iLayerSize;
    }

    public static int32_t WriteSliceBs(sWelsEncCtx* pCtx, SWelsSliceBs* pSliceBs, int32_t iSliceIdx, ref int32_t iSliceSize)
    {
        int32_t kiNalCnt = pSliceBs->iNalIndex;
        int32_t iNalIdx = 0;
        int32_t iNalSize = 0;
        int32_t iReturn = ENC_RETURN_SUCCESS;
        int32_t iTotalLeftLength = (int32_t)(pSliceBs->uiBsSize - pSliceBs->uiBsPos);
        SNalUnitHeaderExt* pNalHdrExt = &pCtx->pCurDqLayer->sLayerInfo.sNalHeaderExt;
        uint8_t* pDst = pSliceBs->pBs;

        Debug.Assert(kiNalCnt <= 2);
        if (kiNalCnt > 2)
            return 0;

        iSliceSize = 0;
        while (iNalIdx < kiNalCnt)
        {
            iNalSize = 0;
            iReturn = WelsEncodeNal((SWelsNalRaw*)&pSliceBs->sNalList + iNalIdx, pNalHdrExt, iTotalLeftLength - iSliceSize,
                                    pDst, &iNalSize);
            if (iReturn != ENC_RETURN_SUCCESS) return iReturn;

            pSliceBs->iNalLen[iNalIdx] = iNalSize;
            iSliceSize += iNalSize;
            pDst += iNalSize;
            ++iNalIdx;
        }
        pSliceBs->uiBsPos = (uint32_t)iSliceSize;

        return iReturn;
    }

    // DynamicDetectCpuCores: encoder_ext.cs.

    public static int32_t AdjustBaseLayer(sWelsEncCtx* pCtx)
    {
        SDqLayer* pCurDq = pCtx->ppDqLayerList[0];
        int32_t iNeedAdj = 1;

        pCtx->pCurDqLayer = pCurDq;

        // do not need adjust due to not different at both slices of consumed time
        iNeedAdj = NeedDynamicAdjust(pCtx->ppDqLayerList[0]->ppSliceInLayer,
                                     pCurDq->sSliceEncCtx.iSliceNumInFrame);
        if (iNeedAdj != 0)
            DynamicAdjustSlicing(pCtx,
                                 pCurDq,
                                 0);

        return iNeedAdj;
    }

    public static int32_t AdjustEnhanceLayer(sWelsEncCtx* pCtx, int32_t iCurDid)
    {
        int32_t iNeedAdj = 1;
        // uiSliceMode of referencing spatial should be SM_FIXEDSLCNUM_SLICE
        // if using spatial base layer for complexity estimation

        bool kbModelingFromSpatial = (pCtx->pCurDqLayer->pRefLayer != null && iCurDid > 0)
                                     && (pCtx->pSvcParam->sSpatialLayers[iCurDid - 1].sSliceArgument.uiSliceMode == SM_FIXEDSLCNUM_SLICE
                                         && pCtx->pSvcParam->iMultipleThreadIdc >= pCtx->pSvcParam->sSpatialLayers[iCurDid -
                                             1].sSliceArgument.uiSliceNum);

        if (kbModelingFromSpatial) // using spatial base layer for complexity estimation
        {
            // do not need adjust due to not different at both slices of consumed time
            iNeedAdj = NeedDynamicAdjust(pCtx->ppDqLayerList[iCurDid - 1]->ppSliceInLayer,
                                         pCtx->pCurDqLayer->sSliceEncCtx.iSliceNumInFrame);
            if (iNeedAdj != 0)
                DynamicAdjustSlicing(pCtx,
                                     pCtx->pCurDqLayer,
                                     iCurDid
                                    );
        }
        else // use temporal layer for complexity estimation
        {
            // do not need adjust due to not different at both slices of consumed time
            iNeedAdj = NeedDynamicAdjust(pCtx->ppDqLayerList[iCurDid]->ppSliceInLayer,
                                         pCtx->pCurDqLayer->sSliceEncCtx.iSliceNumInFrame);
            if (iNeedAdj != 0)
                DynamicAdjustSlicing(pCtx,
                                     pCtx->pCurDqLayer,
                                     iCurDid
                                    );
        }

        return iNeedAdj;
    }

    // TrackSliceComplexities and TrackSliceConsumeTime: MT_DEBUG only.

    public static void SetOneSliceBsBufferUnderMultithread(sWelsEncCtx* pCtx, int32_t kiThreadIdx, SSlice* pSlice)
    {
        SWelsSliceBs* pSliceBs = &pSlice->sSliceBs;
        pSliceBs->pBsBuffer = pCtx->pSliceThreading->pThreadBsBuffer[kiThreadIdx];
        pSliceBs->uiBsPos = 0;
    }
}
