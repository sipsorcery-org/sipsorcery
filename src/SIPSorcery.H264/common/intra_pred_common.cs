//-----------------------------------------------------------------------------
// Filename: intra_pred_common.cs
//
// Description: Port of:
//  - codec/common/inc/intra_pred_common.h
//  - codec/common/src/intra_pred_common.cpp
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// The 16x16 luma vertical and horizontal predictors shared by the encoder and
// decoder. C kernels only; the SIMD variants are not ported.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 24 Nov 2026  Claude          Created.
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
 * \file    get_intra_predictor.c
 *
 * \brief   implementation for get intra predictor about 16x16, 4x4, chroma.
 *
 * \date    4/2/2009 Created
 *          9/14/2009 C level based optimization with high performance gained.
 *              [const, using ST32/ST64 to replace memset, memcpy and memmove etc.]
 *
 *************************************************************************************
 */

namespace SIPSorcery.H264;

internal static unsafe partial class WelsCommon
{
    public static void WelsI16x16LumaPredV_c(uint8_t* pPred, uint8_t* pRef, int32_t kiStride)
    {
        uint8_t i = 15;
        int8_t* kpSrc = (int8_t*)&pRef[-kiStride];
        uint64_t kuiT1 = LD64(kpSrc);
        uint64_t kuiT2 = LD64(kpSrc + 8);
        uint8_t* pDst = pPred;

        do
        {
            ST64(pDst, kuiT1);
            ST64(pDst + 8, kuiT2);
            pDst += 16;
        } while (i-- > 0);
    }

    public static void WelsI16x16LumaPredH_c(uint8_t* pPred, uint8_t* pRef, int32_t kiStride)
    {
        int32_t iStridex15 = (kiStride << 4) - kiStride;
        int32_t iPredStride = 16;
        int32_t iPredStridex15 = 240; //(iPredStride<<4)-iPredStride;
        uint8_t i = 15;

        do
        {
            uint8_t kuiSrc8 = pRef[iStridex15 - 1];
            uint64_t kuiV64 = (uint64_t)(0x0101010101010101UL * kuiSrc8);
            ST64(&pPred[iPredStridex15], kuiV64);
            ST64(&pPred[iPredStridex15 + 8], kuiV64);

            iStridex15 -= kiStride;
            iPredStridex15 -= iPredStride;
        } while (i-- > 0);
    }
}
