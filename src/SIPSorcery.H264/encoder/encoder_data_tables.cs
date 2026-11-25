//-----------------------------------------------------------------------------
// Filename: encoder_data_tables.cs
//
// Description: Port of codec/encoder/core/src/encoder_data_tables.cpp,
// part 1 (E1): the temporal id table that param_svc uses. The remaining
// tables follow with the code that reads them.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 25 Nov 2026  Claude          Created (E1).
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

namespace SIPSorcery.H264.Encoder;

internal static unsafe partial class WelsEnc
{
    // const uint8_t   g_kuiTemporalIdListTable[MAX_TEMPORAL_LEVEL][MAX_GOP_SIZE + 1] = {
    public static readonly InlineArray9<uint8_t>* g_kuiTemporalIdListTable = StaticTable.Of<InlineArray9<uint8_t>, uint8_t>(MAX_TEMPORAL_LEVEL, [
        0, 0, 0, 0, 0, 0, 0, 0,
        0,
        // uiGopSize = 1
        0, 1, 0, 0, 0, 0, 0, 0,
        0,
        // uiGopSize = 2
        0, 2, 1, 2, 0, 0, 0, 0,
        0,
        // uiGopSize = 4
        0, 3, 2, 3, 1, 3, 2, 3,
        0
        //uiGopSize = 8
    ]);
}
