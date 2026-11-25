//-----------------------------------------------------------------------------
// Filename: vlc_decoder.cs
//
// Description: Port of codec/decoder/core/inc/vlc_decoder.h: the CAVLC table
// set.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// The SVlcTable type and InitVlcTable. The tables it points at are defined in
// decoder_data_tables.cpp (decoder_data_tables.cs). WELS_GET_PREFIX_BITS is
// GetPrefixBits (dec_golomb.cs), its non-x86-asm form.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 25 Nov 2026  Claude          Created.
// 25 Nov 2026  Claude          D2: InitVlcTable.
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

internal static unsafe partial class WelsDec
{
    public static void InitVlcTable(SVlcTable* pVlcTable)
    {
        pVlcTable->kpChromaCoeffTokenVlcTable = g_kuiVlcChromaTable;

        pVlcTable->kpCoeffTokenVlcTable[0][0] = g_kuiVlcTable_0;
        pVlcTable->kpCoeffTokenVlcTable[0][1] = g_kuiVlcTable_1;
        pVlcTable->kpCoeffTokenVlcTable[0][2] = g_kuiVlcTable_2;
        pVlcTable->kpCoeffTokenVlcTable[0][3] = g_kuiVlcTable_3;

        pVlcTable->kpCoeffTokenVlcTable[1][0] = g_kuiVlcTable_0_0;
        pVlcTable->kpCoeffTokenVlcTable[1][1] = g_kuiVlcTable_0_1;
        pVlcTable->kpCoeffTokenVlcTable[1][2] = g_kuiVlcTable_0_2;
        pVlcTable->kpCoeffTokenVlcTable[1][3] = g_kuiVlcTable_0_3;

        pVlcTable->kpCoeffTokenVlcTable[2][0] = g_kuiVlcTable_1_0;
        pVlcTable->kpCoeffTokenVlcTable[2][1] = g_kuiVlcTable_1_1;
        pVlcTable->kpCoeffTokenVlcTable[2][2] = g_kuiVlcTable_1_2;
        pVlcTable->kpCoeffTokenVlcTable[2][3] = g_kuiVlcTable_1_3;

        pVlcTable->kpCoeffTokenVlcTable[3][0] = g_kuiVlcTable_2_0;
        pVlcTable->kpCoeffTokenVlcTable[3][1] = g_kuiVlcTable_2_1;
        pVlcTable->kpCoeffTokenVlcTable[3][2] = g_kuiVlcTable_2_2;
        pVlcTable->kpCoeffTokenVlcTable[3][3] = g_kuiVlcTable_2_3;
        pVlcTable->kpCoeffTokenVlcTable[3][4] = g_kuiVlcTable_2_4;
        pVlcTable->kpCoeffTokenVlcTable[3][5] = g_kuiVlcTable_2_5;
        pVlcTable->kpCoeffTokenVlcTable[3][6] = g_kuiVlcTable_2_6;
        pVlcTable->kpCoeffTokenVlcTable[3][7] = g_kuiVlcTable_2_7;

        pVlcTable->kpZeroTable[0] = g_kuiZeroLeftTable0;
        pVlcTable->kpZeroTable[1] = g_kuiZeroLeftTable1;
        pVlcTable->kpZeroTable[2] = g_kuiZeroLeftTable2;
        pVlcTable->kpZeroTable[3] = g_kuiZeroLeftTable3;
        pVlcTable->kpZeroTable[4] = g_kuiZeroLeftTable4;
        pVlcTable->kpZeroTable[5] = g_kuiZeroLeftTable5;
        pVlcTable->kpZeroTable[6] = g_kuiZeroLeftTable6;

        pVlcTable->kpTotalZerosTable[0][0] = g_kuiTotalZerosTable0;
        pVlcTable->kpTotalZerosTable[0][1] = g_kuiTotalZerosTable1;
        pVlcTable->kpTotalZerosTable[0][2] = g_kuiTotalZerosTable2;
        pVlcTable->kpTotalZerosTable[0][3] = g_kuiTotalZerosTable3;
        pVlcTable->kpTotalZerosTable[0][4] = g_kuiTotalZerosTable4;
        pVlcTable->kpTotalZerosTable[0][5] = g_kuiTotalZerosTable5;
        pVlcTable->kpTotalZerosTable[0][6] = g_kuiTotalZerosTable6;
        pVlcTable->kpTotalZerosTable[0][7] = g_kuiTotalZerosTable7;
        pVlcTable->kpTotalZerosTable[0][8] = g_kuiTotalZerosTable8;
        pVlcTable->kpTotalZerosTable[0][9] = g_kuiTotalZerosTable9;
        pVlcTable->kpTotalZerosTable[0][10] = g_kuiTotalZerosTable10;
        pVlcTable->kpTotalZerosTable[0][11] = g_kuiTotalZerosTable11;
        pVlcTable->kpTotalZerosTable[0][12] = g_kuiTotalZerosTable12;
        pVlcTable->kpTotalZerosTable[0][13] = g_kuiTotalZerosTable13;
        pVlcTable->kpTotalZerosTable[0][14] = g_kuiTotalZerosTable14;
        pVlcTable->kpTotalZerosTable[1][0] = g_kuiTotalZerosChromaTable0;
        pVlcTable->kpTotalZerosTable[1][1] = g_kuiTotalZerosChromaTable1;
        pVlcTable->kpTotalZerosTable[1][2] = g_kuiTotalZerosChromaTable2;
    }
}
