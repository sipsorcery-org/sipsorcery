//-----------------------------------------------------------------------------
// Filename: mb_cache.cs
//
// Description: Port of codec/decoder/core/inc/mb_cache.h: macroblock neighbour
// availability.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// g_kuiScan4 is defined in decoder_data_tables.cpp and comes with it.
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

namespace SIPSorcery.H264.Decoder;

/// <summary>TagNeighborAvail (PWelsNeighAvail is SWelsNeighAvail*).</summary>
internal unsafe struct SWelsNeighAvail
{
    public int32_t iTopAvail;
    public int32_t iLeftAvail;
    public int32_t iRightTopAvail;
    public int32_t iLeftTopAvail;  //used for check intra_pred_mode avail or not   //1: avail; 0: unavail

    public int32_t iLeftType;
    public int32_t iTopType;
    public int32_t iLeftTopType;
    public int32_t iRightTopType;

    public int8_t iTopCbp;
    public int8_t iLeftCbp;
    public fixed sbyte iDummy[2]; //for align
}

internal static unsafe partial class WelsDec
{
    public const int REF_NOT_AVAIL = -2;
    public const int REF_NOT_IN_LIST = -1;  //intra
}
