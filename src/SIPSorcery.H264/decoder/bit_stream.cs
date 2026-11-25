//-----------------------------------------------------------------------------
// Filename: bit_stream.cs
//
// Description: Port of codec/decoder/core/inc/bit_stream.h and
// src/bit_stream.cpp: initialising the decoder's bit reader, and inserting
// emulation prevention bytes (RBSP to EBSP).
// OpenH264 commit 78783f19 (v2.7.0-1).
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
 * \file    bit_stream.cpp
 *
 * \brief   Reading / writing bit-stream
 *
 * \date    03/10/2009 Created
 *
 *************************************************************************************
 */

using System.Runtime.CompilerServices;

namespace SIPSorcery.H264.Decoder;

internal static unsafe partial class WelsDec
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint32_t GetValue4Bytes(uint8_t* pDstNal)
    {
        uint32_t uiValue = 0;
        uiValue = (uint32_t)((pDstNal[0] << 24) | (pDstNal[1] << 16) | (pDstNal[2] << 8) | (pDstNal[3]));
        return uiValue;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint32_t GetValue4BytesSafe(uint8_t* pDstNal, intX_t iAvailableBytes)
    {
        uint32_t uiValue = 0;
        for (intX_t i = 0; i < 4; ++i)
        {
            uiValue <<= 8;
            if (i < iAvailableBytes)
            {
                uiValue |= pDstNal[i];
            }
        }
        return uiValue;
    }

    public static int32_t InitReadBits(SBitStringAux* pBitString, intX_t iEndOffset)
    {
        intX_t kiAllowedBytes = (intX_t)(pBitString->pEndBuf - pBitString->pStartBuf) - iEndOffset;
        intX_t kiReadBytes = (intX_t)(pBitString->pCurBuf - pBitString->pStartBuf);
        intX_t kiRemainBytes = kiAllowedBytes - kiReadBytes;
        if (kiRemainBytes <= 0)
        {
            return ERR_INFO_INVALID_ACCESS;
        }
        intX_t kiSeedBytes = (kiRemainBytes < 4) ? kiRemainBytes : 4;
        pBitString->uiCurBits = GetValue4BytesSafe(pBitString->pCurBuf, kiSeedBytes);
        pBitString->pCurBuf += 4;
        pBitString->iLeftBits = -16;
        return ERR_NONE;
    }

    /*!
     * \brief   input bits for decoder or initialize bitstream writing in encoder
     *
     * \param   pBitString  Bit string auxiliary pointer
     * \param   kpBuf       bit-stream buffer
     * \param   kiSize      size in bits for decoder; size in bytes for encoder
     *
     * \return  0: success, other: fail
     */
    public static int32_t DecInitBits(SBitStringAux* pBitString, uint8_t* kpBuf, int32_t kiSize)
    {
        int32_t kiSizeBuf = (kiSize + 7) >> 3;
        uint8_t* pTmp = (uint8_t*)kpBuf;

        if (null == pTmp)
            return ERR_INFO_INVALID_ACCESS;

        pBitString->pStartBuf = pTmp;             // buffer to start position
        pBitString->pEndBuf = pTmp + kiSizeBuf; // buffer + length
        pBitString->iBits = kiSize;           // count bits of overall bitstreaming inputindex;
        pBitString->pCurBuf = pBitString->pStartBuf;
        int32_t iErr = InitReadBits(pBitString, 0);
        if (iErr != 0)
        {
            return iErr;
        }
        return ERR_NONE;
    }

    public static void RBSP2EBSP(uint8_t* pDstBuf, uint8_t* pSrcBuf, int32_t kiSize)
    {
        uint8_t* pSrcPointer = pSrcBuf;
        uint8_t* pDstPointer = pDstBuf;
        uint8_t* pSrcEnd = pSrcBuf + kiSize;
        int32_t iZeroCount = 0;

        while (pSrcPointer < pSrcEnd)
        {
            if (iZeroCount == 2 && *pSrcPointer <= 3)
            {
                //add the code 0x03
                *pDstPointer++ = 3;
                iZeroCount = 0;
            }
            if (*pSrcPointer == 0)
            {
                ++iZeroCount;
            }
            else
            {
                iZeroCount = 0;
            }
            *pDstPointer++ = *pSrcPointer++;
        }
    }
}
