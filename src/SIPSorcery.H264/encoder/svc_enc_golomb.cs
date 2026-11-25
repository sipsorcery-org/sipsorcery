//-----------------------------------------------------------------------------
// Filename: svc_enc_golomb.cs
//
// Description: Port of codec/encoder/core/inc/svc_enc_golomb.h: the
// encoder's exponential Golomb size helpers and bit writer extras. The
// writers they build on (BsWriteBits, BsWriteUE, ...) are in
// common/golomb_common.cs.
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

using System.Runtime.CompilerServices;

namespace SIPSorcery.H264.Encoder;

internal static unsafe partial class WelsEnc
{
    /*
     *  Get size of unsigned exp golomb codes
     */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint32_t BsSizeUE(uint32_t kiValue)
    {
        if (256 > kiValue)
        {
            return g_kuiGolombUELength[(int)kiValue];
        }
        else
        {
            uint32_t n = 0;
            uint32_t iTmpValue = kiValue + 1;

            if ((iTmpValue & 0xffff0000) != 0)
            {
                iTmpValue >>= 16;
                n += 16;
            }
            if ((iTmpValue & 0xff00) != 0)
            {
                iTmpValue >>= 8;
                n += 8;
            }

            //n += (g_kuiGolombUELength[iTmpValue] >> 1);
            n += (uint32_t)(g_kuiGolombUELength[(int)(iTmpValue - 1)] >> 1);
            return ((n << 1) + 1);
        }
    }

    /*
     *  Get size of signed exp golomb codes
     */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint32_t BsSizeSE(int32_t kiValue)
    {
        uint32_t iTmpValue;
        if (0 == kiValue)
        {
            return 1;
        }
        else if (0 < kiValue)
        {
            iTmpValue = (uint32_t)((kiValue << 1) - 1);
            return BsSizeUE(iTmpValue);
        }
        else
        {
            iTmpValue = (uint32_t)((-kiValue) << 1);
            return BsSizeUE(iTmpValue);
        }
    }

    /*
     *  Write truncated exp golomb codes
     */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void BsWriteTE(SBitStringAux* pBs, int32_t kiX, uint32_t kuiValue)
    {
        if (1 == kiX)
        {
            BsWriteOneBit(pBs, kuiValue == 0 ? 1u : 0u);
        }
        else
        {
            BsWriteUE(pBs, kuiValue);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int32_t BsGetBitsPos(SBitStringAux* pBs)
    {
        return (int32_t)(((pBs->pCurBuf - pBs->pStartBuf) << 3) + 32 - pBs->iLeftBits);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void BsAlign(SBitStringAux* pBs)
    {
        if ((pBs->iLeftBits & 7) != 0)
        {
            pBs->uiCurBits <<= pBs->iLeftBits & 7;
            pBs->uiCurBits |= (uint32_t)((1 << (pBs->iLeftBits & 7)) - 1);
            pBs->iLeftBits &= ~7;
        }
        BsFlush(pBs);
    }
}
