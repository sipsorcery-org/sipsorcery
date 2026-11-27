//-----------------------------------------------------------------------------
// Filename: wels_task_encoder.cs
//
// Description: Port of codec/encoder/core/inc/wels_task_encoder.h and
// codec/encoder/core/src/wels_task_encoder.cpp (P5.7): the slice tasks the
// task manager runs on its threads. CWelsSliceEncodingTask codes one slice of
// SM_SINGLE_SLICE's neighbours (SM_FIXEDSLCNUM_SLICE, SM_RASTER_SLICE) into
// its own buffer and deblocks it, CWelsLoadBalancingSlicingEncodingTask also
// times it for the load balancing, and CWelsUpdateMbMapTask redoes one
// slice's macroblock neighbours after the load balancing has moved the slice
// boundaries. CWelsConstrainedSizeSlicingEncodingTask is SM_SIZELIMITED_SLICE's
// (Phase 4). The thread buffers are claimed and freed with Interlocked, where
// upstream holds mutexThreadBsBufferUsage, and a task's error ORs into
// iEncoderError with Interlocked.Or, where upstream holds mutexEncoderError.
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
 *     Copyright (c)  2009-2015, Cisco Systems
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
 * \file    wels_task_encoder.h
 *
 * \brief   interface for encoder tasks
 *
 * \date    5/09/2012 Created
 *
 *************************************************************************************
 */

using System.Diagnostics;
using System.Threading;

using static SIPSorcery.H264.Encoder.WelsEnc;

namespace SIPSorcery.H264.Encoder;

internal unsafe class CWelsSliceEncodingTask : CWelsBaseTask
{
    protected WelsErrorType m_eTaskResult;
    protected sWelsEncCtx* m_pCtx;
    // m_pPrivateData, m_pLbi and m_uiDependencyId: unused upstream.
    protected int32_t m_iStartMbIdx;
    protected int32_t m_iEndMbIdx;
    protected EWelsNalUnitType m_eNalType;
    protected EWelsNalRefIdc m_eNalRefIdc;
    protected bool m_bNeedPrefix;
    protected SSlice* m_pSlice;
    protected SWelsSliceBs* m_pSliceBs;
    protected int32_t m_iSliceIdx;
    protected int32_t m_iSliceSize;
    protected int32_t m_iThreadIdx;

    public CWelsSliceEncodingTask(sWelsEncCtx* pCtx, int32_t iSliceIdx)
    {
        m_eTaskResult = ENC_RETURN_SUCCESS;
        m_pCtx = pCtx;
        m_iSliceIdx = iSliceIdx;
    }

    public override WelsErrorType Execute()
    {
        m_eTaskResult = InitTask();
        if (m_eTaskResult != ENC_RETURN_SUCCESS) return m_eTaskResult;

        m_eTaskResult = ExecuteTask();

        FinishTask();

        return m_eTaskResult;
    }

    public WelsErrorType SetBoundary(int32_t iStartIdx, int32_t iEndIdx)
    {
        m_iStartMbIdx = iStartIdx;
        m_iEndMbIdx = iEndIdx;
        return ENC_RETURN_SUCCESS;
    }

    /// <summary>
    /// Claims the first free thread buffer. Upstream does this under mutexThreadBsBufferUsage;
    /// a CompareExchange on each flag does the same here.
    /// </summary>
    protected static int32_t QueryEmptyThread(int32_t* pThreadBsBufferUsage)
    {
        for (int32_t k = 0; k < MAX_THREADS_NUM; k++)
        {
            if (Interlocked.CompareExchange(ref pThreadBsBufferUsage[k], 1, 0) == 0)
            {
                return k;
            }
        }
        return -1;
    }

    public virtual WelsErrorType InitTask()
    {
        m_eNalType = m_pCtx->eNalType;
        m_eNalRefIdc = m_pCtx->eNalPriority;
        m_bNeedPrefix = m_pCtx->bNeedPrefixNalFlag;

        m_iThreadIdx = QueryEmptyThread(m_pCtx->pSliceThreading->bThreadBsBufferUsage);

        WelsLog(&m_pCtx->sLogCtx, WELS_LOG_DEBUG,
                $"[MT] CWelsSliceEncodingTask()InitTask for m_iSliceIdx {m_iSliceIdx}, lock thread {m_iThreadIdx}");
        if (m_iThreadIdx < 0)
        {
            WelsLog(&m_pCtx->sLogCtx, WELS_LOG_WARNING,
                    $"[MT] CWelsSliceEncodingTask InitTask(), Cannot find available thread for m_iSliceIdx = {m_iSliceIdx}");
            return ENC_RETURN_UNEXPECTED;
        }

        int32_t iReturn = InitOneSliceInThread(m_pCtx, ref m_pSlice, m_iThreadIdx, m_pCtx->uiDependencyId, m_iSliceIdx);
        if (iReturn != ENC_RETURN_SUCCESS) return iReturn;
        m_pSliceBs = &m_pSlice->sSliceBs;

        iReturn = SetSliceBoundaryInfo(m_pCtx->pCurDqLayer, m_pSlice, m_iSliceIdx);
        if (iReturn != ENC_RETURN_SUCCESS) return iReturn;

        SetOneSliceBsBufferUnderMultithread(m_pCtx, m_iThreadIdx, m_pSlice);

        Debug.Assert((void*)(&m_pSliceBs->sBsWrite) == (void*)m_pSlice->pSliceBsa);
        InitBits(&m_pSliceBs->sBsWrite, m_pSliceBs->pBsBuffer, (int32_t)m_pSliceBs->uiSize);

        return ENC_RETURN_SUCCESS;
    }

    public virtual void FinishTask()
    {
        // mutexThreadBsBufferUsage: a release write frees the buffer.
        Volatile.Write(ref m_pCtx->pSliceThreading->bThreadBsBufferUsage[m_iThreadIdx], 0);

        WelsLog(&m_pCtx->sLogCtx, WELS_LOG_DEBUG,
                $"[MT] CWelsSliceEncodingTask()FinishTask for m_iSliceIdx {m_iSliceIdx}, unlock thread {m_iThreadIdx}");

        //sync multi-threading error
        if (ENC_RETURN_SUCCESS != m_eTaskResult)
        {
            Interlocked.Or(ref m_pCtx->iEncoderError, m_eTaskResult);
        }
    }

    public virtual WelsErrorType ExecuteTask()
    {
        SSpatialLayerInternal* pParamInternal = &m_pCtx->pSvcParam->sDependencyLayers[m_pCtx->uiDependencyId];
        if (m_bNeedPrefix)
        {
            if (m_eNalRefIdc != NRI_PRI_LOWEST)
            {
                WelsLoadNalForSlice(m_pSliceBs, (int32_t)NAL_UNIT_PREFIX, (int32_t)m_eNalRefIdc);
                WelsWriteSVCPrefixNal(&m_pSliceBs->sBsWrite, (int32_t)m_eNalRefIdc, (NAL_UNIT_CODED_SLICE_IDR == m_eNalType));
                WelsUnloadNalForSlice(m_pSliceBs);
            }
            else // No Prefix NAL Unit RBSP syntax here, but need add NAL Unit Header extension
            {
                WelsLoadNalForSlice(m_pSliceBs, (int32_t)NAL_UNIT_PREFIX, (int32_t)m_eNalRefIdc);
                // No need write any syntax of prefix NAL Unit RBSP here
                WelsUnloadNalForSlice(m_pSliceBs);
            }
        }

        WelsLoadNalForSlice(m_pSliceBs, (int32_t)m_eNalType, (int32_t)m_eNalRefIdc);
        Debug.Assert(m_iSliceIdx == (int)m_pSlice->iSliceIdx);
        int32_t iReturn = WelsCodeOneSlice(m_pCtx, m_pSlice, (int32_t)m_eNalType);
        if (ENC_RETURN_SUCCESS != iReturn)
        {
            return iReturn;
        }
        WelsUnloadNalForSlice(m_pSliceBs);

        m_iSliceSize = 0;
        iReturn = WriteSliceBs(m_pCtx, m_pSliceBs, m_iSliceIdx, ref m_iSliceSize);
        if (ENC_RETURN_SUCCESS != iReturn)
        {
            WelsLog(&m_pCtx->sLogCtx, WELS_LOG_WARNING,
                    $"[MT] CWelsSliceEncodingTask ExecuteTask(), WriteSliceBs not successful: coding_idx {pParamInternal->iCodingIndex}, um_iSliceIdx {m_iSliceIdx}");
            return iReturn;
        }

        m_pCtx->pFuncList->pfDeblocking.pfDeblockingFilterSlice(m_pCtx->pCurDqLayer, m_pCtx->pFuncList, m_pSlice);

        WelsLog(&m_pCtx->sLogCtx, WELS_LOG_DETAIL,
                $"@pSlice={m_iSliceIdx,-6} sliceType:{(m_pCtx->eSliceType == P_SLICE ? 'P' : 'I')} idc:{(int)m_eNalRefIdc} size:{m_iSliceSize,-6}");

        return ENC_RETURN_SUCCESS;
    }

    public override uint32_t GetTaskType() => (uint32_t)ETaskType.WELS_ENC_TASK_ENCODE_FIXED_SLICE;
}

internal unsafe sealed class CWelsLoadBalancingSlicingEncodingTask : CWelsSliceEncodingTask
{
    private int64_t m_iSliceStart;

    public CWelsLoadBalancingSlicingEncodingTask(sWelsEncCtx* pCtx, int32_t iSliceIdx)
        : base(pCtx, iSliceIdx)
    {
    }

    public override WelsErrorType InitTask()
    {
        WelsErrorType iReturn = base.InitTask();
        if (ENC_RETURN_SUCCESS != iReturn)
        {
            return iReturn;
        }

        m_iSliceStart = WelsCommon.WelsTime();
        WelsLog(&m_pCtx->sLogCtx, WELS_LOG_DEBUG,
                $"[MT] CWelsLoadBalancingSlicingEncodingTask()InitTask for m_iSliceIdx {m_iSliceIdx} at time={m_iSliceStart}");

        return ENC_RETURN_SUCCESS;
    }

    public override void FinishTask()
    {
        base.FinishTask();
        SSpatialLayerInternal* pParamInternal = &m_pCtx->pSvcParam->sDependencyLayers[m_pCtx->uiDependencyId];
        m_pSlice->uiSliceConsumeTime = (uint32_t)(WelsCommon.WelsTime() - m_iSliceStart);
        WelsLog(&m_pCtx->sLogCtx, WELS_LOG_DEBUG,
                $"[MT] CWelsLoadBalancingSlicingEncodingTask()FinishTask, coding_idx {pParamInternal->iCodingIndex}, um_iSliceIdx {m_iSliceIdx}, uiSliceConsumeTime {m_pSlice->uiSliceConsumeTime}, m_iSliceSize {m_iSliceSize}, iFirstMbInSlice {m_pSlice->sSliceHeaderExt.sSliceHeader.iFirstMbInSlice}, count_num_mb_in_slice {m_pSlice->iCountMbNumInSlice} at time={m_pSlice->uiSliceConsumeTime + m_iSliceStart}");
    }

    public override uint32_t GetTaskType() => (uint32_t)ETaskType.WELS_ENC_TASK_ENCODE_SLICE_LOADBALANCING;
}

// CWelsConstrainedSizeSlicingEncodingTask: SM_SIZELIMITED_SLICE (Phase 4).

internal unsafe sealed class CWelsUpdateMbMapTask : CWelsBaseTask
{
    private readonly sWelsEncCtx* m_pCtx;
    private readonly int32_t m_iSliceIdx;

    public CWelsUpdateMbMapTask(sWelsEncCtx* pCtx, int32_t iSliceIdx)
    {
        m_pCtx = pCtx;
        m_iSliceIdx = iSliceIdx;
    }

    public override WelsErrorType Execute()
    {
        UpdateMbListNeighborParallel(m_pCtx->pCurDqLayer, m_pCtx->pCurDqLayer->sMbDataP, m_iSliceIdx);
        return ENC_RETURN_SUCCESS;
    }

    public override uint32_t GetTaskType() => (uint32_t)ETaskType.WELS_ENC_TASK_UPDATEMBMAP;
}
