//-----------------------------------------------------------------------------
// Filename: vlc_encoder.cs
//
// Description: Port of codec/encoder/core/inc/vlc_encoder.h: the CAVLC
// element writers. The tables it declares are in encoder_data_tables.cs, except
// g_kuiVlcLevelPrefix (commented out in encoder_data_tables.cpp) and
// g_kuiVlcTotalZerosChromaDc422 (never defined), which upstream declares but
// doesn't define. Upstream doesn't call these writers: the residual writer in
// set_mb_syn_cavlc writes the same codes through its own bit cache. They are
// ported with the header and checked like the rest.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 26 Nov 2026  Claude          Created (E3).
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

/************************************************************************/
/* VLC FOR WELS ENCODER                                                 */
/************************************************************************/

internal static unsafe partial class WelsEnc
{
    // #define    CHROMA_DC_NC_OFFSET       17
    public const int CHROMA_DC_NC_OFFSET = 17;

    public static int32_t WriteTotalCoeffTrailingones(SBitStringAux* pBs, uint8_t uiNc, uint8_t uiTotalCoeff,
                                                      uint8_t uiTrailingOnes)
    {
        uint8_t kuiNcIdx = g_kuiEncNcMapTable[uiNc];
        uint8_t* kpCoeffToken = (uint8_t*)&g_kuiVlcCoeffToken[kuiNcIdx][uiTotalCoeff][uiTrailingOnes];
        return BsWriteBits(pBs, kpCoeffToken[1], kpCoeffToken[0]);
    }

    public static int32_t WriteTotalcoeffTrailingonesChroma(SBitStringAux* pBs, uint8_t uiTotalCoeff,
                                                            uint8_t uiTrailingOnes)
    {
        uint8_t* kpCoeffToken = (uint8_t*)&g_kuiVlcCoeffToken[4][uiTotalCoeff][uiTrailingOnes];
        return BsWriteBits(pBs, kpCoeffToken[1], kpCoeffToken[0]);
    }

    //kuiZeroCount = level_prefix;
    public static int32_t WriteLevelPrefix(SBitStringAux* pBs, uint32_t kuiZeroCount)
    {
        BsWriteBits(pBs, (int32_t)(kuiZeroCount + 1), 1);
        return 0;
    }

    public static int32_t WriteTotalZeros(SBitStringAux* pBs, uint32_t uiTotalCoeff, uint32_t uiTotalZeros)
    {
        uint8_t* kpTotalZeros = (uint8_t*)&g_kuiVlcTotalZeros[uiTotalCoeff][(int32_t)uiTotalZeros];
        return BsWriteBits(pBs, kpTotalZeros[1], kpTotalZeros[0]);
    }

    public static int32_t WriteTotalZerosChromaDc(SBitStringAux* pBs, uint32_t uiTotalCoeff, uint32_t uiTotalZeros)
    {
        uint8_t* kpTotalZerosChromaDc = (uint8_t*)&g_kuiVlcTotalZerosChromaDc[uiTotalCoeff][(int32_t)uiTotalZeros];
        return BsWriteBits(pBs, kpTotalZerosChromaDc[1], kpTotalZerosChromaDc[0]);
    }

    public static int32_t WriteRunBefore(SBitStringAux* pBs, uint8_t uiZeroLeft, uint8_t uiRunBefore)
    {
        uint8_t* kpRunBefore = (uint8_t*)&g_kuiVlcRunBefore[uiZeroLeft][uiRunBefore];
        return BsWriteBits(pBs, kpRunBefore[1], kpRunBefore[0]);
    }
}
