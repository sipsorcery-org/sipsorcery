//-----------------------------------------------------------------------------
// Filename: set_mb_syn_cabac.cs
//
// Description: Port of codec/encoder/core/inc/set_mb_syn_cabac.h, part 1 (E3):
// the CABAC context structures, which SSlice and SDynamicSlicingStack embed.
// The CABAC encoder itself (set_mb_syn_cabac.cpp, svc_set_mb_syn_cabac.cpp)
// is not ported: the port targets Constrained Baseline, which is CAVLC only.
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
 * \file    set_mb_syn_cabac.h
 *
 * \brief   Seting all syntax elements of mb and encoding residual with cabac
 *
 * \date    09/27/2014 Created
 *
 *************************************************************************************
 */

// typedef uint64_t cabac_low_t;
global using cabac_low_t = System.UInt64;

namespace SIPSorcery.H264.Encoder;

internal static partial class WelsEnc
{
    // #define  WELS_QP_MAX    51
    public const int WELS_QP_MAX = 51;

    // enum { CABAC_LOW_WIDTH = sizeof (cabac_low_t) / sizeof (uint8_t) * 8 };
    public const int CABAC_LOW_WIDTH = sizeof(cabac_low_t) / sizeof(uint8_t) * 8;
}

/// <summary>TagStateCtx.</summary>
internal struct SStateCtx
{
    // Packed representation of state and MPS as state << 1 | MPS.
    public uint8_t m_uiStateMps;

    public readonly uint8_t Mps() => (uint8_t)(m_uiStateMps & 1);
    public readonly uint8_t State() => (uint8_t)(m_uiStateMps >> 1);
    public void Set(uint8_t uiState, uint8_t uiMps) => m_uiStateMps = (uint8_t)(uiState * 2 + uiMps);
}

/// <summary>TagCabacCtx.</summary>
internal unsafe struct SCabacCtx
{
    public cabac_low_t m_uiLow;
    public int32_t m_iLowBitCnt;
    public int32_t m_iRenormCnt;
    public uint32_t m_uiRange;
    public InlineArray460<SStateCtx> m_sStateCtx; // [WELS_CONTEXT_COUNT]
    public uint8_t* m_pBufStart;
    public uint8_t* m_pBufEnd;
    public uint8_t* m_pBufCur;
}
