//-----------------------------------------------------------------------------
// Filename: vlc_decoder.cs
//
// Description: Port of codec/decoder/core/inc/vlc_decoder.h: the CAVLC table
// set.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// Only the SVlcTable type for now. The VLC tables it points at are defined in
// decoder_data_tables.cpp, and InitVlcTable comes with CAVLC parsing.
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

/// <summary>TagVlcTable.</summary>
internal unsafe struct SVlcTable
{
    public InlineArray4<PtrArray8<InlineArray2<uint8_t>>> kpCoeffTokenVlcTable;   // const uint8_t (*[4][8])[2]
    public InlineArray2<uint8_t>* kpChromaCoeffTokenVlcTable;                     // const uint8_t (*)[2]
    public PtrArray7<InlineArray2<uint8_t>> kpZeroTable;                          // const uint8_t (*[7])[2]
    public InlineArray2<PtrArray15<InlineArray2<uint8_t>>> kpTotalZerosTable;     // const uint8_t (*[2][15])[2]
}
