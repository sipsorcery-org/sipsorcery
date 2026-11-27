//-----------------------------------------------------------------------------
// Filename: wels_task_management.cs
//
// Description: Port of codec/encoder/core/inc/wels_task_management.h and
// codec/encoder/core/src/wels_task_management.cpp (P5.7): the task manager
// that runs a layer's slice tasks on several threads and waits for them.
//
// Upstream queues every task of the list on a process-wide CWelsThreadPool
// (common/src/WelsThreadPool.cpp), a singleton whose thread count is set by
// the first encoder that creates it, and blocks on an event until the last
// task's OnTaskExecuted. The port has no thread pool of its own. The calling
// thread runs the tasks with up to iMultipleThreadIdc - 1 helpers queued on
// the .NET thread pool, and each thread takes the next unclaimed task with
// Interlocked.Increment until none are left. The plan's rule 7 asks for
// Task; the helpers are IThreadPoolWorkItem instead, queued with
// ThreadPool.UnsafeQueueUserWorkItem, so a frame allocates nothing. The
// manager is the work item, so queuing it again is free.
//
// Which thread codes which slice changes from run to run, as it does
// upstream, but the output does not: each slice codes into its own buffers
// and AppendSliceToFrameBs joins them in slice order. Only the load
// balancing's slice times differ between runs, and those move the slice
// boundaries (see slice_multi_threading.cs).
//
// The helper count only ever grows up to the thread count less one, and a
// helper that is still queued or running from the last list counts towards
// it, so no more than iMultipleThreadIdc threads code at once. That is what
// lets each of them claim one of the iMultipleThreadIdc thread bitstream
// buffers. GetThreadPoolThreadNum is iMultipleThreadIdc; upstream it is the
// shared pool's thread count, which can be smaller when another encoder made
// the pool first. An exception in a task is caught, the other tasks still
// run, and ExecuteTasks rethrows the first one once they have all finished.
//
// The sWelsEncCtx is unmanaged, so it holds the manager through a GCHandle
// (pTaskManage), as welsCodecTrace.cs does for the logger.
//
// CWelsTaskManageOne ("for test" upstream) is not ported.
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
 * \file    wels_task_management.h
 *
 * \brief   interface for task management
 *
 * \date    5/14/2012 Created
 *
 *************************************************************************************
 */

using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;

using static SIPSorcery.H264.Encoder.WelsEnc;

namespace SIPSorcery.H264.Encoder;

/// <summary>
/// TASKLIST_TYPE (CWelsNonDuplicatedList&lt;CWelsBaseTask&gt;) with the claim counter of
/// the run in progress.
/// </summary>
internal sealed class CWelsTaskList
{
    public readonly List<CWelsBaseTask> m_pTasks = new List<CWelsBaseTask>();

    /// <summary>The next task to claim; the threads take them with Interlocked.Increment.</summary>
    public int32_t m_iNext;

    public int32_t size() => m_pTasks.Count;
}

internal abstract unsafe class IWelsTaskManage
{
    public abstract WelsErrorType Init(sWelsEncCtx* pEncCtx);
    public abstract void Uninit();
    public virtual void InitFrame(int32_t kiCurDid) { }
    public abstract WelsErrorType ExecuteTasks(CWelsBaseTask.ETaskType iTaskType = CWelsBaseTask.ETaskType.WELS_ENC_TASK_ENCODING);
    public abstract int32_t GetThreadPoolThreadNum();

    /// <summary>
    /// Makes the task manager and returns its GCHandle for sWelsEncCtx.pTaskManage, or 0
    /// when it could not be set up. bNeedLock (upstream's bDynamicSlice) is unused upstream.
    /// </summary>
    public static nint CreateTaskManage(sWelsEncCtx* pCtx, int32_t iSpatialLayer, bool bNeedLock)
    {
        if (null == pCtx)
        {
            return 0;
        }

        IWelsTaskManage pTaskManage = new CWelsTaskManageBase();

        if (ENC_RETURN_SUCCESS != pTaskManage.Init(pCtx))
        {
            pTaskManage.Uninit();
            return 0;
        }
        return GCHandle.ToIntPtr(GCHandle.Alloc(pTaskManage));
    }

    /// <summary>The task manager behind sWelsEncCtx.pTaskManage.</summary>
    public static IWelsTaskManage From(nint pTaskManage) => (IWelsTaskManage)GCHandle.FromIntPtr(pTaskManage).Target;

    /// <summary>WELS_DELETE_OP (pTaskManage): the destructor's Uninit, then the handle.</summary>
    public static void DeleteTaskManage(ref nint pTaskManage)
    {
        GCHandle hTaskManage = GCHandle.FromIntPtr(pTaskManage);
        ((IWelsTaskManage)hTaskManage.Target).Uninit();
        hTaskManage.Free();
        pTaskManage = 0;
    }
}

internal sealed unsafe class CWelsTaskManageBase : IWelsTaskManage, IThreadPoolWorkItem
{
    private sWelsEncCtx* m_pEncCtx;
    // m_pThreadPool: the .NET thread pool (see the file header).
    private readonly CWelsTaskList[][] m_pcAllTaskList = new CWelsTaskList[(int)CWelsBaseTask.ETaskType.WELS_ENC_TASK_ALL][];
    private readonly CWelsTaskList[] m_cEncodingTaskList = new CWelsTaskList[MAX_DEPENDENCY_LAYER];
    private readonly CWelsTaskList[] m_cPreEncodingTaskList = new CWelsTaskList[MAX_DEPENDENCY_LAYER];
    private readonly int32_t[] m_iTaskNum = new int32_t[MAX_DEPENDENCY_LAYER];
    private int32_t m_iThreadNum;
    private int32_t m_iWaitTaskNum;
    private readonly ManualResetEventSlim m_hTaskEvent = new ManualResetEventSlim(false);
    // m_hEventMutex: m_iWaitTaskNum counts down with Interlocked.
    private int32_t m_iCurDid;

    // Port additions: the list the threads are taking tasks from (null between runs), the
    // helpers queued or running, and the first exception a task threw.
    private CWelsTaskList m_pRunningList;
    private int32_t m_iHelperNum;
    private ExceptionDispatchInfo m_pTaskFault;

    public CWelsTaskManageBase()
    {
        m_pEncCtx = null;
        m_iWaitTaskNum = 0;

        for (int32_t iDid = 0; iDid < MAX_DEPENDENCY_LAYER; iDid++)
        {
            m_iTaskNum[iDid] = 0;
            m_cEncodingTaskList[iDid] = new CWelsTaskList();
            m_cPreEncodingTaskList[iDid] = new CWelsTaskList();
        }
        for (int32_t iType = 0; iType < (int)CWelsBaseTask.ETaskType.WELS_ENC_TASK_ALL; iType++)
        {
            m_pcAllTaskList[iType] = new CWelsTaskList[MAX_DEPENDENCY_LAYER];
        }

        // WelsEventOpen (&m_hTaskEvent) and WelsMutexInit (&m_hEventMutex): the fields above.
    }

    public override WelsErrorType Init(sWelsEncCtx* pEncCtx)
    {
        m_pEncCtx = pEncCtx;
        m_iThreadNum = m_pEncCtx->pSvcParam->iMultipleThreadIdc;

        // CWelsThreadPool::SetThreadNum (m_iThreadNum) and AddReference: no pool to set up
        // (see the file header).

        int32_t iReturn = ENC_RETURN_SUCCESS;
        for (int32_t iDid = 0; iDid < MAX_DEPENDENCY_LAYER; iDid++)
        {
            m_pcAllTaskList[(int)CWelsBaseTask.ETaskType.WELS_ENC_TASK_ENCODING][iDid] = m_cEncodingTaskList[iDid];
            m_pcAllTaskList[(int)CWelsBaseTask.ETaskType.WELS_ENC_TASK_UPDATEMBMAP][iDid] = m_cPreEncodingTaskList[iDid];
            iReturn |= CreateTasks(pEncCtx, iDid);
        }

        return iReturn;
    }

    public override void Uninit()
    {
        // m_pThreadPool->RemoveInstance: no pool. A helper still queued from the last run
        // finds no list and returns without touching the tasks.
        Volatile.Write(ref m_pRunningList, null);
        DestroyTasks();

        // WELS_DELETE_OP of the task lists, WelsEventClose and WelsMutexDestroy: the GC.
        // m_hTaskEvent is not disposed: a helper's Set can still be returning after the last
        // ExecuteTaskList woke up.
    }

    private WelsErrorType CreateTasks(sWelsEncCtx* pEncCtx, int32_t kiCurDid)
    {
        CWelsBaseTask pTask = null;
        int32_t kiTaskCount;
        uint32_t uiSliceMode = (uint32_t)pEncCtx->pSvcParam->sSpatialLayers[kiCurDid].sSliceArgument.uiSliceMode;

        if (uiSliceMode != (uint32_t)SM_SIZELIMITED_SLICE)
        {
            kiTaskCount = m_iTaskNum[kiCurDid] = (int32_t)pEncCtx->pSvcParam->sSpatialLayers[kiCurDid].sSliceArgument.uiSliceNum;
        }
        else
        {
            kiTaskCount = m_iTaskNum[kiCurDid] = pEncCtx->iActiveThreadsNum;
        }

        for (int idx = 0; idx < kiTaskCount; idx++)
        {
            pTask = new CWelsUpdateMbMapTask(pEncCtx, idx);
            m_cPreEncodingTaskList[kiCurDid].m_pTasks.Add(pTask);
        }

        for (int idx = 0; idx < kiTaskCount; idx++)
        {
            if (uiSliceMode == (uint32_t)SM_SIZELIMITED_SLICE)
            {
                // CWelsConstrainedSizeSlicingEncodingTask: SM_SIZELIMITED_SLICE is refused at
                // init (Phase 4).
                return ENC_RETURN_UNSUPPORTED_PARA;
            }
            else
            {
                if (pEncCtx->pSvcParam->bUseLoadBalancing)
                {
                    pTask = new CWelsLoadBalancingSlicingEncodingTask(pEncCtx, idx);
                }
                else
                {
                    pTask = new CWelsSliceEncodingTask(pEncCtx, idx);
                }
            }
            m_cEncodingTaskList[kiCurDid].m_pTasks.Add(pTask);
        }

        return ENC_RETURN_SUCCESS;
    }

    private void DestroyTaskList(CWelsTaskList pTargetTaskList)
    {
        pTargetTaskList.m_pTasks.Clear();
    }

    private void DestroyTasks()
    {
        for (int32_t iDid = 0; iDid < MAX_DEPENDENCY_LAYER; iDid++)
        {
            if (m_iTaskNum[iDid] > 0)
            {
                DestroyTaskList(m_cEncodingTaskList[iDid]);
                DestroyTaskList(m_cPreEncodingTaskList[iDid]);
                m_iTaskNum[iDid] = 0;
                m_pcAllTaskList[(int)CWelsBaseTask.ETaskType.WELS_ENC_TASK_ENCODING][iDid] = null;
            }
        }
    }

    /// <summary>
    /// Runs the current layer's list of tasks and returns when all of them have finished.
    /// Upstream queues them all on the pool and waits for m_iWaitTaskNum to reach 0.
    /// </summary>
    private WelsErrorType ExecuteTaskList(CWelsTaskList[] pTaskList)
    {
        m_iWaitTaskNum = m_iTaskNum[m_iCurDid];
        CWelsTaskList pTargetTaskList = pTaskList[m_iCurDid];
        if (0 == m_iWaitTaskNum)
        {
            return ENC_RETURN_SUCCESS;
        }

        int32_t iCurrentTaskCount = m_iWaitTaskNum; //if directly use m_iWaitTaskNum in the loop make cause sync problem

        // No helper can still be counting down or setting the event of the last run: the
        // last run ended when its final task was done, and a helper that finished it set the
        // event before this thread went on.
        m_pTaskFault = null;
        m_hTaskEvent.Reset();
        // A release, as m_pRunningList's: a helper of an earlier run that read this list before
        // it ended can claim a task here straight from the counter, and must see the count and
        // the frame's state written before it.
        Volatile.Write(ref pTargetTaskList.m_iNext, 0);
        Volatile.Write(ref m_pRunningList, pTargetTaskList);

        int32_t iHelpers = WELS_MIN(m_iThreadNum, iCurrentTaskCount) - 1;
        while (Volatile.Read(ref m_iHelperNum) < iHelpers)
        {
            Interlocked.Increment(ref m_iHelperNum);
            ThreadPool.UnsafeQueueUserWorkItem(this, false);
        }

        if (!RunTasks(true))
        {
            // WelsEventWait (&m_hTaskEvent, &m_hEventMutex, m_iWaitTaskNum)
            m_hTaskEvent.Wait();
        }
        Volatile.Write(ref m_pRunningList, null);

        m_pTaskFault?.Throw();

        return ENC_RETURN_SUCCESS;
    }

    /// <summary>
    /// Takes and runs tasks of the current list until there are none left to take. Returns
    /// true when this thread finished the list's last task, which only the calling thread
    /// needs to know: a helper that finishes it sets the event instead (OnTaskExecuted).
    /// </summary>
    private bool RunTasks(bool bCaller)
    {
        CWelsTaskList pList;
        while ((pList = Volatile.Read(ref m_pRunningList)) != null)
        {
            int32_t iIdx = Interlocked.Increment(ref pList.m_iNext) - 1;
            if (iIdx >= pList.size())
            {
                break;
            }

            try
            {
                pList.m_pTasks[iIdx].Execute();
            }
            catch (Exception e)
            {
                Interlocked.CompareExchange(ref m_pTaskFault, ExceptionDispatchInfo.Capture(e), null);
            }

            // OnTaskExecuted -> OnTaskMinusOne
            if (Interlocked.Decrement(ref m_iWaitTaskNum) == 0)
            {
                if (bCaller)
                {
                    return true;
                }
                m_hTaskEvent.Set();
                break;
            }
        }
        return false;
    }

    /// <summary>A helper: the thread pool runs it for ExecuteTaskList.</summary>
    void IThreadPoolWorkItem.Execute()
    {
        try
        {
            RunTasks(false);
        }
        finally
        {
            Interlocked.Decrement(ref m_iHelperNum);
        }
    }

    public override void InitFrame(int32_t kiCurDid)
    {
        m_iCurDid = kiCurDid;
        if (m_pEncCtx->pCurDqLayer->bNeedAdjustingSlicing)
        {
            ExecuteTaskList(m_pcAllTaskList[(int)CWelsBaseTask.ETaskType.WELS_ENC_TASK_UPDATEMBMAP]);
        }
    }

    public override WelsErrorType ExecuteTasks(CWelsBaseTask.ETaskType iTaskType = CWelsBaseTask.ETaskType.WELS_ENC_TASK_ENCODING)
    {
        return ExecuteTaskList(m_pcAllTaskList[(int)iTaskType]);
    }

    public override int32_t GetThreadPoolThreadNum()
    {
        return m_iThreadNum;
    }
}
