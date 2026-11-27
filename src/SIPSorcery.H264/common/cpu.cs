//-----------------------------------------------------------------------------
// Filename: cpu.cs
//
// Description: Port of the feature flags in codec/common/inc/cpu_core.h and of
// WelsCPUFeatureDetect in codec/common/src/cpu.cpp.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// Upstream reads cpuid, or the Arm feature registers, to choose between its C
// kernels and its assembly. The port has no assembly. Its SIMD kernels are C#
// written with System.Runtime.Intrinsics (Phase 5), so the flags come from
// .NET's instruction set classes instead, and the function tables choose on
// them as upstream's do. Each SIMD kernel gives the same result as its C
// kernel, so the flags change the speed and nothing else.
//
// The number of logical processors isn't reported. The C-only build the port
// is checked against reports none either, and the encoder falls back to
// DynamicDetectCpuCores as it does there.
//
// WelsCpuFeatureMask turns kernels off: 0 gives the C kernels throughout. Tests
// and benchmarks use it to compare the two. It is read when an encoder or
// decoder is initialised.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 27 Nov 2026  Claude          Created (P5.2).
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
 */

using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

namespace SIPSorcery.H264;

internal static unsafe partial class WelsCommon
{
    // cpu_core.h, x86.
    public const uint32_t WELS_CPU_MMX = 0x00000001;      /* mmx */
    public const uint32_t WELS_CPU_MMXEXT = 0x00000002;   /* mmx-ext*/
    public const uint32_t WELS_CPU_SSE = 0x00000004;      /* sse */
    public const uint32_t WELS_CPU_SSE2 = 0x00000008;     /* sse 2 */
    public const uint32_t WELS_CPU_SSE3 = 0x00000010;     /* sse 3 */
    public const uint32_t WELS_CPU_SSE41 = 0x00000020;    /* sse 4.1 */
    public const uint32_t WELS_CPU_SSSE3 = 0x00000200;    /* ssse3 */
    public const uint32_t WELS_CPU_SSE42 = 0x00000400;    /* sse 4.2 */
    public const uint32_t WELS_CPU_AVX = 0x00000800;      /* Advanced Vector eXtentions */
    public const uint32_t WELS_CPU_FMA = 0x00020000;      /* AVX VEX FMA instruction sets */
    public const uint32_t WELS_CPU_AVX2 = 0x00040000;     /* AVX2 */

    // cpu_core.h, Arm. These reuse the x86 bits: upstream builds for one or the
    // other, so test them only where AdvSimd is supported.
    public const uint32_t WELS_CPU_ARMv7 = 0x000001;      /* ARMv7 */
    public const uint32_t WELS_CPU_VFPv3 = 0x000002;      /* VFPv3 */
    public const uint32_t WELS_CPU_NEON = 0x000004;       /* NEON */

    /// <summary>
    /// The features WelsCPUFeatureDetect may report. 0 turns every SIMD kernel off. Read when an
    /// encoder or decoder is initialised, so set it before creating one.
    /// </summary>
    internal static uint32_t WelsCpuFeatureMask = uint32_t.MaxValue;

    /// <summary>
    /// cpu.cpp's WelsCPUFeatureDetect: the instruction sets the SIMD kernels may use, from .NET's
    /// instruction set classes, masked by <see cref="WelsCpuFeatureMask"/>. The number of logical
    /// processors is left as it is (see the file header).
    /// </summary>
    public static uint32_t WelsCPUFeatureDetect(int32_t* pNumberOfLogicProcessors)
    {
        uint32_t uiCPU = 0;
        if (Vector128.IsHardwareAccelerated)
        {
            if (Sse2.IsSupported)
            {
                uiCPU |= WELS_CPU_MMX | WELS_CPU_MMXEXT | WELS_CPU_SSE | WELS_CPU_SSE2;
                if (Sse3.IsSupported)
                {
                    uiCPU |= WELS_CPU_SSE3;
                }
                if (Ssse3.IsSupported)
                {
                    uiCPU |= WELS_CPU_SSSE3;
                }
                if (Sse41.IsSupported)
                {
                    uiCPU |= WELS_CPU_SSE41;
                }
                if (Sse42.IsSupported)
                {
                    uiCPU |= WELS_CPU_SSE42;
                }
                if (Avx.IsSupported)
                {
                    uiCPU |= WELS_CPU_AVX;
                }
                if (Fma.IsSupported)
                {
                    uiCPU |= WELS_CPU_FMA;
                }
                if (Avx2.IsSupported)
                {
                    uiCPU |= WELS_CPU_AVX2;
                }
            }
            else if (AdvSimd.Arm64.IsSupported)
            {
                uiCPU |= WELS_CPU_ARMv7 | WELS_CPU_VFPv3 | WELS_CPU_NEON;
            }
        }
        return uiCPU & WelsCpuFeatureMask;
    }

    /// <summary>
    /// Whether <paramref name="uiCpuFlag"/> allows the 128-bit SIMD kernels: SSE2 on x86 and x64,
    /// NEON on Arm64. They are written once, with Vector128 and the few x86 and Arm intrinsics it
    /// lacks, so one set serves both, as upstream's X86_ASM and HAVE_NEON blocks do between them.
    /// </summary>
    public static bool WelsCpuHasSimd128(uint32_t uiCpuFlag) =>
        Sse2.IsSupported ? (uiCpuFlag & WELS_CPU_SSE2) != 0 : AdvSimd.Arm64.IsSupported && (uiCpuFlag & WELS_CPU_NEON) != 0;
}
