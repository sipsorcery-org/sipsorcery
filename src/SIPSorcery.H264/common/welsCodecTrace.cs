//-----------------------------------------------------------------------------
// Filename: welsCodecTrace.cs
//
// Description: Port of:
//  - codec/common/inc/welsCodecTrace.h
//  - codec/common/src/welsCodecTrace.cpp
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// welsCodecTrace is a plain struct in native memory (it is held by pointer and
// its m_sLogCtx is copied into the decoder context, so it must not move). The
// trace callback is WelsTraceCallback with the C string as a .NET string. To
// route codec logs to an ILogger, the facade sets m_fpTrace to a static
// trampoline and m_pTraceCtx to a GCHandle for the logger.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 24 Nov 2026  Claude          Created.
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

using System.Runtime.InteropServices;

namespace SIPSorcery.H264;

internal unsafe struct welsCodecTrace
{
    public int m_iTraceLevel;
    public delegate*<void*, int, string, void> m_fpTrace;   // WelsTraceCallback
    public void* m_pTraceCtx;

    public SLogContext m_sLogCtx;

    private static void welsStderrTrace(void* ctx, int level, string str)
    {
        Console.Error.WriteLine(str);
    }

    /// <summary>welsCodecTrace::welsCodecTrace(). Needs the object's final address for m_sLogCtx.pLogCtx.</summary>
    public static void Construct(welsCodecTrace* self)
    {
        self->m_iTraceLevel = WELS_LOG_DEFAULT;
        self->m_fpTrace = &welsStderrTrace;
        self->m_pTraceCtx = null;

        self->m_sLogCtx.pLogCtx = self;
        self->m_sLogCtx.pfLog = &StaticCodecTrace;
        self->m_sLogCtx.pCodecInstance = null;
        self->m_sLogCtx.pfIsEnabled = &StaticIsEnabled;   // port addition, see SLogContext
    }

    /// <summary>Port addition: the m_iTraceLevel test CodecTrace makes, without the message.</summary>
    public static bool StaticIsEnabled(void* pCtx, int iLevel)
    {
        welsCodecTrace* self = (welsCodecTrace*)pCtx;
        return self->m_iTraceLevel >= iLevel && self->m_fpTrace != null;
    }

    /// <summary>new welsCodecTrace().</summary>
    public static welsCodecTrace* New()
    {
        welsCodecTrace* p = (welsCodecTrace*)NativeMemory.AllocZeroed((nuint)sizeof(welsCodecTrace));
        Construct(p);
        return p;
    }

    /// <summary>delete pTrace (~welsCodecTrace sets m_fpTrace to NULL).</summary>
    public static void Delete(ref welsCodecTrace* p)
    {
        if (p != null)
        {
            p->m_fpTrace = null;
            NativeMemory.Free(p);
            p = null;
        }
    }

    public static void StaticCodecTrace(void* pCtx, int iLevel, string Str_Format)
    {
        welsCodecTrace* self = (welsCodecTrace*)pCtx;
        self->CodecTrace(iLevel, Str_Format);
    }

    public void CodecTrace(int iLevel, string Str_Format)
    {
        if (m_iTraceLevel < iLevel)
        {
            return;
        }

        if (m_fpTrace != null)
        {
            m_fpTrace(m_pTraceCtx, iLevel, Str_Format);
        }
    }

    public void SetCodecInstance(void* pCodecInstance)
    {
        m_sLogCtx.pCodecInstance = pCodecInstance;
    }

    public void SetTraceLevel(int iLevel)
    {
        if (iLevel >= 0)
            m_iTraceLevel = iLevel;
    }

    public void SetTraceCallback(delegate*<void*, int, string, void> func)
    {
        m_fpTrace = func;
    }

    public void SetTraceCallbackContext(void* ctx)
    {
        m_pTraceCtx = ctx;
    }
}
