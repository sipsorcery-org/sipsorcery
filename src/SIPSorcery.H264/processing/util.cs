//-----------------------------------------------------------------------------
// Filename: util.cs
//
// Description: Port of codec/processing/src/common/util.h (E7): the
// processing library's limits, precision constants and method id helpers.
// MB_WIDTH_LUMA, the macroblock type bits and IS_INTRA came with the
// complexity analysis (ComplexityAnalysis.cs, E6). WELS_MAX, WELS_MIN,
// WELS_ABS and WELS_CLAMP are the common macros' (macros.cs), which give the
// same results.
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
 *     Copyright (c)  2011-2013, Cisco Systems
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
 * \file        :  util.h
 *
 * \brief       :  utility function for wels video processor
 *
 * \date        :  2011/01/04
 *
 * \description :  1. util function
 *
 *************************************************************************************
 */

namespace SIPSorcery.H264;

internal static unsafe partial class WelsVP
{
    public const int32_t MAX_MBS_PER_FRAME = 36864; //in accordance with max level support in Rec

    /// <summary>PESN (util.h): a double, so expressions that add it are evaluated in double, as in C.</summary>
    public const double PESN = 1e-6;       // desired float precision

    public const int32_t AQ_INT_MULTIPLY = 10000000;
    public const int32_t AQ_TIME_INT_MULTIPLY = 10000;
    public const int32_t AQ_QSTEP_INT_MULTIPLY = 100;
    public const int32_t AQ_PESN = 10; // (1e-6)*AQ_INT_MULTIPLY

    public const int32_t ALIGNBYTES = 16;       /* Worst case is requiring alignment to an 16 byte boundary */

    public static int32_t GET_METHOD(int32_t x) => x & 0xff;          // mask method as the lowest 8bits
    public static int32_t GET_SPECIAL(int32_t x) => (x >> 8) & 0xff;  // mask special flag as 8bits

    public static EMethods WelsVpGetValidMethod(int32_t a)
    {
        int32_t iMethod = GET_METHOD(a);
        return (EMethods)WELS_MIN(WELS_MAX(iMethod, (int32_t)EMethods.METHOD_NULL + 1), (int32_t)EMethods.METHOD_MASK - 1);
    }
}
