//-----------------------------------------------------------------------------
// Filename: macros.cs
//
// Description: Port of codec/common/inc/macros.h.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// The function-like macros become small inlined methods in WelsCommon.
//  - WELS_MAX, WELS_MIN and WELS_CLIP3 only ever return one of their operands,
//    so they are generic: C# infers the wider operand type, and a mix that C
//    would convert to unsigned (int with uint) fails to compile instead of
//    silently comparing differently. Fix those at the call site.
//  - WELS_ABS computes a value, so it has int/long/float/double overloads that
//    match C's integer promotion (an int16_t argument is negated as int).
//  - WELS_NON_ZERO_COUNT_AVERAGE assigns its first argument, so it takes ref.
// Not ported: ENFORCE_STACK_ALIGN_1D/2D (call sites use stackalloc),
// ALIGNED_DECLARE (alignment only matters for SIMD), DISALLOW_COPY_AND_ASSIGN,
// WELS_GCC_UNUSED, and WELS_VERIFY_RETURN_IF/IFNEQ/PROC_IF, which return from
// the caller, so call sites expand them inline.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 24 Nov 2026  Claude          Created.
// 26 Nov 2026  Claude          WELS_DIV_ROUND with a float divisor, and EPSN (E6).
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
 * \file    macros.h
 *
 * \brief   MACRO based tool utilization
 *
 * \date    3/13/2009 Created
 *
 *************************************************************************************
 */

using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace SIPSorcery.H264;

internal static unsafe partial class WelsCommon
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int32_t WELS_ALIGN(int32_t x, int32_t n) => (x + n - 1) & ~(n - 1);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint32_t WELS_ALIGN(uint32_t x, uint32_t n) => (x + n - 1) & ~(n - 1);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int64_t WELS_ALIGN(int64_t x, int64_t n) => (x + n - 1) & ~(n - 1);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T WELS_MAX<T>(T x, T y) where T : IComparisonOperators<T, T, bool> => x > y ? x : y;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T WELS_MIN<T>(T x, T y) where T : IComparisonOperators<T, T, bool> => x < y ? x : y;

    /// <summary>
    /// The macro is <c>(x >= 0 &amp;&amp; y >= 0) ? WELS_MIN(x, y) : WELS_MAX(x, y);</c>
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T WELS_MIN_POSITIVE<T>(T x, T y) where T : INumber<T> =>
        (x >= T.Zero && y >= T.Zero) ? WELS_MIN(x, y) : WELS_MAX(x, y);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double WELS_CEIL(double x) => Math.Ceiling(x);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double WELS_FLOOR(double x) => Math.Floor(x);

    /// <summary><c>((int32_t)(0.5+(x)))</c>: 0.5 is a double, so x is rounded in double.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int32_t WELS_ROUND(double x) => (int32_t)(0.5 + x);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int64_t WELS_ROUND64(double x) => (int64_t)(0.5 + x);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int32_t WELS_DIV_ROUND(int32_t x, int32_t y) => (int32_t)(y == 0 ? x / (y + 1) : (y / 2 + x) / y);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int32_t WELS_DIV_ROUND(int64_t x, int64_t y) => (int32_t)(y == 0 ? x / (y + 1) : (y / 2 + x) / y);

    /// <summary>WELS_DIV_ROUND with a float divisor (a frame rate): the macro then works in float, as C does.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int32_t WELS_DIV_ROUND(int32_t x, float y) => (int32_t)(y == 0 ? x / (y + 1) : (y / 2 + x) / y);

    /// <summary>WELS_DIV_ROUND with a float divisor: C converts the int64 dividend to float.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int32_t WELS_DIV_ROUND(int64_t x, float y) => (int32_t)(y == 0 ? x / (y + 1) : (y / 2 + x) / y);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int64_t WELS_DIV_ROUND64(int64_t x, int64_t y) => (int64_t)(y == 0 ? x / (y + 1) : (y / 2 + x) / y);

    /// <summary>EPSN (typedefs.h): the desired float precision.</summary>
    public const float EPSN = 0.000001f;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WELS_NON_ZERO_COUNT_AVERAGE(ref int8_t nC, int8_t nA, int8_t nB)
    {
        nC = (int8_t)(nA + nB + 1);
        nC >>= (nA != -1 && nB != -1) ? 1 : 0;
        nC = (int8_t)(nC + ((nA == -1 && nB == -1) ? 1 : 0));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int32_t CeilLog2(int32_t i)
    {
        int32_t s = 0;
        i--;
        while (i > 0)
        {
            s++;
            i >>= 1;
        }
        return s;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int32_t WelsMedian(int32_t iX, int32_t iY, int32_t iZ)
    {
        int32_t iMin = iX, iMax = iX;

        if (iY < iMin)
        {
            iMin = iY;
        }
        else
        {
            iMax = iY;
        }

        if (iZ < iMin)
        {
            iMin = iZ;
        }
        else if (iZ > iMax)
        {
            iMax = iZ;
        }

        return (iX + iY + iZ) - (iMin + iMax);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int32_t NEG_NUM(int32_t iX) => 1 + (~iX);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint8_t WelsClip1(int32_t iX)
    {
        uint8_t uiTmp = (uint8_t)((iX & ~255) != 0 ? (-iX >> 31) : iX);
        return uiTmp;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int32_t WELS_SIGN(int32_t iX) => iX >> 31;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int32_t WELS_ABS(int32_t iX) => iX > 0 ? iX : -iX;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int64_t WELS_ABS(int64_t iX) => iX > 0 ? iX : -iX;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float WELS_ABS(float iX) => iX > 0 ? iX : -iX;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double WELS_ABS(double iX) => iX > 0 ? iX : -iX;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T WELS_CLIP3<T>(T iX, T iY, T iZ) where T : IComparisonOperators<T, T, bool> =>
        iX < iY ? iY : (iX > iZ ? iZ : iX);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T WelsClip3<T>(T iX, T iY, T iZ) where T : IComparisonOperators<T, T, bool>
    {
        if (iX < iY)
        {
            return iY;
        }
        if (iX > iZ)
        {
            return iZ;
        }
        return iX;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int32_t WELS_LOG2(uint32_t v)
    {
        int32_t r = 0;
        while ((v >>= 1) != 0)
        {
            ++r;
        }
        return r;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int32_t CLIP3_QP_0_51(int32_t q) => WELS_CLIP3(q, 0, 51);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int32_t CALC_BI_STRIDE(int32_t width, int32_t bitcount) => (((width * bitcount) + 31) & ~31) >> 3;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int32_t BUTTERFLY1x2(int32_t b) => (b << 8) | b;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint32_t BUTTERFLY2x4(uint32_t wd) => (wd << 16) | wd;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint64_t BUTTERFLY4x8(uint32_t dw) => ((uint64_t)dw << 32) | dw;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool WELS_POWER2_IF(uint32_t v) => v != 0 && (v & (v - 1)) == 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool CheckInRangeCloseOpen(int16_t kiCurrent, int16_t kiMin, int16_t kiMax) =>
        (kiCurrent >= kiMin) && (kiCurrent < kiMax);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WelsSetMemUint32_c(uint32_t* pDst, uint32_t iValue, int32_t iSizeOfData)
    {
        for (int i = 0; i < iSizeOfData; i++)
        {
            pDst[i] = iValue;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WelsSetMemUint16_c(uint16_t* pDst, uint16_t iValue, int32_t iSizeOfData)
    {
        for (int i = 0; i < iSizeOfData; i++)
        {
            pDst[i] = iValue;
        }
    }

    public static void WelsSetMemMultiplebytes_c(void* pDst, uint32_t iValue, int32_t iSizeOfData, int32_t iDataLengthOfData)
    {
        Debug.Assert(4 == iDataLengthOfData || 2 == iDataLengthOfData || 1 == iDataLengthOfData);

        // TODO: consider add assembly for these functions
        if (0 != iValue)
        {
            if (4 == iDataLengthOfData)
            {
                WelsSetMemUint32_c((uint32_t*)pDst, iValue, iSizeOfData);
            }
            else if (2 == iDataLengthOfData)
            {
                WelsSetMemUint16_c((uint16_t*)pDst, (uint16_t)iValue, iSizeOfData);
            }
            else
            {
                memset((uint8_t*)pDst, (uint8_t)iValue, iSizeOfData);
            }
        }
        else
        {
            memset((uint8_t*)pDst, 0, iSizeOfData * iDataLengthOfData);
        }
    }
}
