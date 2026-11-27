//-----------------------------------------------------------------------------
// Filename: memory.cs
//
// Description: Port of codec/processing/src/common/memory.h and memory.cpp
// (E7): the processing library's own allocator, zero filled and aligned to
// ALIGNBYTES (16). It is the common WelsMallocz/WelsFree (memory_align.cs),
// which lay the block out the same way and count it for the leak tests.
// WelsRealloc is not ported: nothing in the encoder calls it.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 27 Nov 2026  Claude          Created (E7).
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

namespace SIPSorcery.H264;

internal static unsafe partial class WelsVP
{
    /// <summary>WelsMalloc (memory.cpp): malloc with zero filled utilization in Wels.</summary>
    public static void* WelsMalloc(uint32_t kuiSize, string pTag = null) => WelsMallocz(kuiSize, pTag);

    /// <summary>WelsFree (memory.cpp).</summary>
    public static void WelsFree(void* pPointer, string pTag = null) => WelsCommon.WelsFree(pPointer, pTag);
}
