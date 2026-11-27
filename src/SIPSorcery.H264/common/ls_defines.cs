//-----------------------------------------------------------------------------
// Filename: ls_defines.cs
//
// Description: Port of codec/common/inc/ls_defines.h.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// The load/store macros. Upstream's GCC branch reads through packed structs and
// its MSVC branch dereferences directly; both are unaligned native-endian
// accesses, which is Unsafe.ReadUnaligned/WriteUnaligned. The A2/A4/A8
// variants only promise alignment, so they are the same operations.
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

using System.Runtime.CompilerServices;

namespace SIPSorcery.H264;

internal static unsafe partial class WelsCommon
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint16_t LD16(void* a) => Unsafe.ReadUnaligned<uint16_t>(a);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint32_t LD32(void* a) => Unsafe.ReadUnaligned<uint32_t>(a);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint64_t LD64(void* a) => Unsafe.ReadUnaligned<uint64_t>(a);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ST16(void* a, uint16_t b) => Unsafe.WriteUnaligned(a, b);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ST32(void* a, uint32_t b) => Unsafe.WriteUnaligned(a, b);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ST64(void* a, uint64_t b) => Unsafe.WriteUnaligned(a, b);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint16_t LD16A2(void* a) => LD16(a);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint32_t LD32A2(void* a) => LD32(a);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint32_t LD32A4(void* a) => LD32(a);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint64_t LD64A2(void* a) => LD64(a);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint64_t LD64A4(void* a) => LD64(a);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint64_t LD64A8(void* a) => LD64(a);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ST16A2(void* a, uint16_t b) => ST16(a, b);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ST32A2(void* a, uint32_t b) => ST32(a, b);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ST32A4(void* a, uint32_t b) => ST32(a, b);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ST64A2(void* a, uint64_t b) => ST64(a, b);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ST64A4(void* a, uint64_t b) => ST64(a, b);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ST64A8(void* a, uint64_t b) => ST64(a, b);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint16_t INTD16(void* a) => LD16(a);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint32_t INTD32(void* a) => LD32(a);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint64_t INTD64(void* a) => LD64(a);
}
