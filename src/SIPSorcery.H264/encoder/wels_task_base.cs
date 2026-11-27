//-----------------------------------------------------------------------------
// Filename: wels_task_base.cs
//
// Description: Port of codec/encoder/core/inc/wels_task_base.h and
// codec/encoder/core/src/wels_task_base.cpp (P5.7): the base of the
// encoder's slice tasks. Upstream it derives from common's IWelsTask, whose
// sink the thread pool calls when a task ends. The port's task manager
// (wels_task_management.cs) runs the tasks itself and counts them down, so
// the sink is not needed.
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
 * \file    wels_task_base.h
 *
 * \brief   interface for encoder tasks
 *
 * \date    5/09/2012 Created
 *
 *************************************************************************************
 */

namespace SIPSorcery.H264.Encoder;

internal abstract class CWelsBaseTask
{
    public enum ETaskType
    {
        WELS_ENC_TASK_ENCODING = 0,
        WELS_ENC_TASK_ENCODE_FIXED_SLICE = WELS_ENC_TASK_ENCODING,
        WELS_ENC_TASK_ENCODE_SLICE_LOADBALANCING = WELS_ENC_TASK_ENCODING,
        WELS_ENC_TASK_ENCODE_SLICE_SIZECONSTRAINED = WELS_ENC_TASK_ENCODING,
        WELS_ENC_TASK_UPDATEMBMAP = 1,
        WELS_ENC_TASK_PREPROCESS = 2,
        WELS_ENC_TASK_ALL = 3,
    }

    /// <summary>IWelsTask::Execute.</summary>
    public abstract WelsErrorType Execute();

    public abstract uint32_t GetTaskType();
}
