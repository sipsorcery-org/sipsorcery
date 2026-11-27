//-----------------------------------------------------------------------------
// Filename: wels_decoder_thread.cs
//
// Description: Port of codec/decoder/core/inc/wels_decoder_thread.h: the
// decoder's thread, event and semaphore types.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// The port decodes on the caller's thread only (upstream's default,
// iThreadCount = 0), so these are the Windows-shape structs as opaque handles
// for the context fields that hold them; the threading functions are not
// ported.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 25 Nov 2026  Claude          Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
// The ported code is also covered by the OpenH264 BSD 2-Clause notice below.
//-----------------------------------------------------------------------------

/*!
 * \copy
 *     Copyright (c)  2009-2019, Cisco Systems
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
 * \file    wels_decoder_thread.h
 *
 * \brief   Interfaces introduced in thread programming
 *
 * \date    08/06/2018 Created
 *
 *************************************************************************************
 */

namespace SIPSorcery.H264.Decoder;

/// <summary>tagWelsDecSemphore.</summary>
internal unsafe struct SWelsDecSemphore
{
    public void* h;
}

/// <summary>tagWelsDecEvent.</summary>
internal unsafe struct SWelsDecEvent
{
    public void* h;
    public int isSignaled;
}

/// <summary>tagWelsDecThread.</summary>
internal unsafe struct SWelsDecThread
{
    public void* h;
}

internal static unsafe partial class WelsDec
{
    public const int WELS_DEC_MAX_NUM_CPU = 16;
    public const int WELS_DEC_MAX_THREAD_STACK_SIZE = 4096;
    public const int WELS_DEC_THREAD_COMMAND_RUN = 0;
    public const int WELS_DEC_THREAD_COMMAND_ABORT = 1;

    // Bound inter-thread waits so a lost reference cannot hang the decoder.
    public const int WELS_DEC_THREAD_WAIT_TIMEOUT_MS = 3000;
}
