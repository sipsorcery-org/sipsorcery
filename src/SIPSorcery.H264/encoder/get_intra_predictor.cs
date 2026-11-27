//-----------------------------------------------------------------------------
// Filename: get_intra_predictor.cs
//
// Description: Port of codec/encoder/core/inc/get_intra_predictor.h and
// src/get_intra_predictor.cpp: the encoder's intra predictors and
// WelsInitIntraPredFuncs. Unlike the decoder's, these write a packed
// prediction (4x4 at stride 4, chroma 8x8 at stride 8, luma 16x16 at
// stride 16) from the reconstructed neighbours at pRef/kiStride.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// The I16x16 V and H predictors are the shared ones in
// common/intra_pred_common.cs. The C stack arrays (uiSrc, kuiVec1,
// kuiTopMean, ...) are stackalloc here. With SIMD, WelsInitIntraPredFuncs
// puts the 16x16 and chroma plane predictors of get_intra_predictor_simd.cs
// in the table (P5.6).
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 26 Nov 2026  Claude          Created (E2).
// 27 Nov 2026  Claude          The SIMD predictors in WelsInitIntraPredFuncs (P5.6).
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

using System.Runtime.CompilerServices;

namespace SIPSorcery.H264.Encoder;

internal static unsafe partial class WelsEnc
{
    // #define I4x4_COUNT 4, I8x8_COUNT 8, I16x16_COUNT 16 are unused here.

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WelsFillingPred8to16_c(uint8_t* pPred, uint8_t* pSrc)
    {
        ST64(pPred, LD64(pSrc));
        ST64(pPred + 8, LD64(pSrc));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WelsFillingPred8x2to16_c(uint8_t* pPred, uint8_t* pSrc)
    {
        ST64(pPred, LD64(pSrc));
        ST64(pPred + 8, LD64(pSrc + 8));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WelsFillingPred1to16_c(uint8_t* pPred, uint8_t kuiSrc)
    {
        uint8_t* kuiSrc8 = stackalloc uint8_t[8] { kuiSrc, kuiSrc, kuiSrc, kuiSrc, kuiSrc, kuiSrc, kuiSrc, kuiSrc };
        ST64(pPred, LD64(kuiSrc8));
        ST64(pPred + 8, LD64(kuiSrc8));
    }

    // #define I4x4_PRED_STRIDE 4, I4x4_PRED_STRIDE2 8, I4x4_PRED_STRIDE3 12 are unused here.

    public static void WelsI4x4LumaPredV_c(uint8_t* pPred, uint8_t* pRef, int32_t kiStride)
    {
        uint32_t kuiSrc = LD32(&pRef[-kiStride]);
        uint32_t* uiSrcx2 = stackalloc uint32_t[2];
        uiSrcx2[0] = uiSrcx2[1] = kuiSrc;

        WelsFillingPred8to16_c(pPred, (uint8_t*)&uiSrcx2[0]);
    }

    public static void WelsI4x4LumaPredH_c(uint8_t* pPred, uint8_t* pRef, int32_t kiStride)
    {
        uint32_t kiStridex2Left = (uint32_t)((kiStride << 1) - 1);
        uint32_t kiStridex3Left = (uint32_t)kiStride + kiStridex2Left;
        uint8_t kuiHor1 = pRef[-1];
        uint8_t kuiHor2 = pRef[kiStride - 1];
        uint8_t kuiHor3 = pRef[kiStridex2Left];
        uint8_t kuiHor4 = pRef[kiStridex3Left];
        uint8_t* kuiVec1 = stackalloc uint8_t[4] { kuiHor1, kuiHor1, kuiHor1, kuiHor1 };
        uint8_t* kuiVec2 = stackalloc uint8_t[4] { kuiHor2, kuiHor2, kuiHor2, kuiHor2 };
        uint8_t* kuiVec3 = stackalloc uint8_t[4] { kuiHor3, kuiHor3, kuiHor3, kuiHor3 };
        uint8_t* kuiVec4 = stackalloc uint8_t[4] { kuiHor4, kuiHor4, kuiHor4, kuiHor4 };
        uint8_t* uiSrc = stackalloc uint8_t[16]; // TobeCont'd about assign opt as follows
        ST32(&uiSrc[0], LD32(kuiVec1));
        ST32(&uiSrc[4], LD32(kuiVec2));
        ST32(&uiSrc[8], LD32(kuiVec3));
        ST32(&uiSrc[12], LD32(kuiVec4));

        WelsFillingPred8x2to16_c(pPred, uiSrc);
    }

    public static void WelsI4x4LumaPredDc_c(uint8_t* pPred, uint8_t* pRef, int32_t kiStride)
    {
        uint8_t kuiDcValue = (uint8_t)((pRef[-1] + pRef[kiStride - 1] + pRef[(kiStride << 1) - 1] + pRef[(kiStride << 1) +
                                        kiStride - 1] +
                                        pRef[-kiStride] + pRef[1 - kiStride] + pRef[2 - kiStride] + pRef[3 - kiStride] + 4) >> 3);

        WelsFillingPred1to16_c(pPred, kuiDcValue);
    }

    public static void WelsI4x4LumaPredDcLeft_c(uint8_t* pPred, uint8_t* pRef, int32_t kiStride)
    {
        uint8_t kuiDcValue = (uint8_t)((pRef[-1] + pRef[kiStride - 1] + pRef[(kiStride << 1) - 1] + pRef[(kiStride << 1) +
                                        kiStride - 1] + 2) >> 2);

        WelsFillingPred1to16_c(pPred, kuiDcValue);
    }

    public static void WelsI4x4LumaPredDcTop_c(uint8_t* pPred, uint8_t* pRef, int32_t kiStride)
    {
        uint8_t kuiDcValue = (uint8_t)((pRef[-kiStride] + pRef[1 - kiStride] + pRef[2 - kiStride] + pRef[3 - kiStride] + 2) >> 2);
        WelsFillingPred1to16_c(pPred, kuiDcValue);
    }

    public static void WelsI4x4LumaPredDcNA_c(uint8_t* pPred, uint8_t* pRef, int32_t kiStride)
    {
        const uint8_t kuiDcValue = 0x80;
        WelsFillingPred1to16_c(pPred, kuiDcValue);
    }

    /*down pLeft*/
    public static void WelsI4x4LumaPredDDL_c(uint8_t* pPred, uint8_t* pRef, int32_t kiStride)
    {
        /*get pTop*/
        uint8_t kuiT0 = pRef[-kiStride];
        uint8_t kuiT1 = pRef[1 - kiStride];
        uint8_t kuiT2 = pRef[2 - kiStride];
        uint8_t kuiT3 = pRef[3 - kiStride];
        uint8_t kuiT4 = pRef[4 - kiStride];
        uint8_t kuiT5 = pRef[5 - kiStride];
        uint8_t kuiT6 = pRef[6 - kiStride];
        uint8_t kuiT7 = pRef[7 - kiStride];
        uint8_t kuiDDL0 = (uint8_t)((2 + kuiT0 + kuiT2 + (kuiT1 << 1)) >> 2); // uiDDL0
        uint8_t kuiDDL1 = (uint8_t)((2 + kuiT1 + kuiT3 + (kuiT2 << 1)) >> 2); // uiDDL1
        uint8_t kuiDDL2 = (uint8_t)((2 + kuiT2 + kuiT4 + (kuiT3 << 1)) >> 2); // uiDDL2
        uint8_t kuiDDL3 = (uint8_t)((2 + kuiT3 + kuiT5 + (kuiT4 << 1)) >> 2); // uiDDL3
        uint8_t kuiDDL4 = (uint8_t)((2 + kuiT4 + kuiT6 + (kuiT5 << 1)) >> 2); // uiDDL4
        uint8_t kuiDDL5 = (uint8_t)((2 + kuiT5 + kuiT7 + (kuiT6 << 1)) >> 2); // uiDDL5
        uint8_t kuiDDL6 = (uint8_t)((2 + kuiT6 + kuiT7 + (kuiT7 << 1)) >> 2); // uiDDL6
        uint8_t* uiSrc = stackalloc uint8_t[16]; // TobeCont'd about assign opt as follows
        uiSrc[0] = kuiDDL0;
        uiSrc[1] = uiSrc[4] = kuiDDL1;
        uiSrc[2] = uiSrc[5] = uiSrc[8] = kuiDDL2;
        uiSrc[3] = uiSrc[6] = uiSrc[9] = uiSrc[12] = kuiDDL3;
        uiSrc[7] = uiSrc[10] = uiSrc[13] = kuiDDL4;
        uiSrc[11] = uiSrc[14] = kuiDDL5;
        uiSrc[15] = kuiDDL6;

        WelsFillingPred8x2to16_c(pPred, uiSrc);
    }

    /*down pLeft*/
    public static void WelsI4x4LumaPredDDLTop_c(uint8_t* pPred, uint8_t* pRef, int32_t kiStride)
    {
        /*get pTop*/
        uint8_t kuiT0 = pRef[-kiStride];
        uint8_t kuiT1 = pRef[1 - kiStride];
        uint8_t kuiT2 = pRef[2 - kiStride];
        uint8_t kuiT3 = pRef[3 - kiStride];
        uint8_t kuiDLT0 = (uint8_t)((2 + kuiT0 + kuiT2 + (kuiT1 << 1)) >> 2); // uiDLT0
        uint8_t kuiDLT1 = (uint8_t)((2 + kuiT1 + kuiT3 + (kuiT2 << 1)) >> 2); // uiDLT1
        uint8_t kuiDLT2 = (uint8_t)((2 + kuiT2 + kuiT3 + (kuiT3 << 1)) >> 2); // uiDLT2
        uint8_t kuiDLT3 = (uint8_t)((2 + (kuiT3 << 2)) >> 2);                 // uiDLT3
        uint8_t* uiSrc = stackalloc uint8_t[16]; // TobeCont'd about assign opt as follows
        memset(&uiSrc[6], kuiDLT3, 10 * sizeof(uint8_t));
        uiSrc[0] = kuiDLT0;
        uiSrc[1] = uiSrc[4] = kuiDLT1;
        uiSrc[2] = uiSrc[5] = uiSrc[8] = kuiDLT2;
        uiSrc[3] = kuiDLT3;

        WelsFillingPred8x2to16_c(pPred, uiSrc);
    }

    /*down right*/
    public static void WelsI4x4LumaPredDDR_c(uint8_t* pPred, uint8_t* pRef, int32_t kiStride)
    {
        int32_t kiStridex2 = kiStride << 1;
        int32_t kiStridex3 = kiStride + kiStridex2;
        uint8_t kuiLT = pRef[-kiStride - 1]; // pTop-pLeft
        /*get pLeft and pTop*/
        uint8_t kuiL0 = pRef[-1];
        uint8_t kuiL1 = pRef[kiStride - 1];
        uint8_t kuiL2 = pRef[kiStridex2 - 1];
        uint8_t kuiL3 = pRef[kiStridex3 - 1];
        uint8_t kuiT0 = pRef[-kiStride];
        uint8_t kuiT1 = pRef[1 - kiStride];
        uint8_t kuiT2 = pRef[2 - kiStride];
        uint8_t kuiT3 = pRef[3 - kiStride];
        uint16_t kuiTL0 = (uint16_t)(1 + kuiLT + kuiL0);
        uint16_t kuiLT0 = (uint16_t)(1 + kuiLT + kuiT0);
        uint16_t kuiT01 = (uint16_t)(1 + kuiT0 + kuiT1);
        uint16_t kuiT12 = (uint16_t)(1 + kuiT1 + kuiT2);
        uint16_t kuiT23 = (uint16_t)(1 + kuiT2 + kuiT3);
        uint16_t kuiL01 = (uint16_t)(1 + kuiL0 + kuiL1);
        uint16_t kuiL12 = (uint16_t)(1 + kuiL1 + kuiL2);
        uint16_t kuiL23 = (uint16_t)(1 + kuiL2 + kuiL3);
        uint8_t kuiDDR0 = (uint8_t)((kuiTL0 + kuiLT0) >> 2);
        uint8_t kuiDDR1 = (uint8_t)((kuiLT0 + kuiT01) >> 2);
        uint8_t kuiDDR2 = (uint8_t)((kuiT01 + kuiT12) >> 2);
        uint8_t kuiDDR3 = (uint8_t)((kuiT12 + kuiT23) >> 2);
        uint8_t kuiDDR4 = (uint8_t)((kuiTL0 + kuiL01) >> 2);
        uint8_t kuiDDR5 = (uint8_t)((kuiL01 + kuiL12) >> 2);
        uint8_t kuiDDR6 = (uint8_t)((kuiL12 + kuiL23) >> 2);
        uint8_t* uiSrc = stackalloc uint8_t[16]; // TobeCont'd about assign opt as follows
        uiSrc[0] = uiSrc[5] = uiSrc[10] = uiSrc[15] = kuiDDR0;
        uiSrc[1] = uiSrc[6] = uiSrc[11] = kuiDDR1;
        uiSrc[2] = uiSrc[7] = kuiDDR2;
        uiSrc[3] = kuiDDR3;
        uiSrc[4] = uiSrc[9] = uiSrc[14] = kuiDDR4;
        uiSrc[8] = uiSrc[13] = kuiDDR5;
        uiSrc[12] = kuiDDR6;

        WelsFillingPred8x2to16_c(pPred, uiSrc);
    }

    /*vertical pLeft*/
    public static void WelsI4x4LumaPredVL_c(uint8_t* pPred, uint8_t* pRef, int32_t kiStride)
    {
        /*get pTop*/
        uint8_t kuiT0 = pRef[-kiStride];
        uint8_t kuiT1 = pRef[1 - kiStride];
        uint8_t kuiT2 = pRef[2 - kiStride];
        uint8_t kuiT3 = pRef[3 - kiStride];
        uint8_t kuiT4 = pRef[4 - kiStride];
        uint8_t kuiT5 = pRef[5 - kiStride];
        uint8_t kuiT6 = pRef[6 - kiStride];
        uint8_t kuiVL0 = (uint8_t)((1 + kuiT0 + kuiT1) >> 1);              // uiVL0
        uint8_t kuiVL1 = (uint8_t)((1 + kuiT1 + kuiT2) >> 1);              // uiVL1
        uint8_t kuiVL2 = (uint8_t)((1 + kuiT2 + kuiT3) >> 1);              // uiVL2
        uint8_t kuiVL3 = (uint8_t)((1 + kuiT3 + kuiT4) >> 1);              // uiVL3
        uint8_t kuiVL4 = (uint8_t)((1 + kuiT4 + kuiT5) >> 1);              // uiVL4
        uint8_t kuiVL5 = (uint8_t)((2 + kuiT0 + (kuiT1 << 1) + kuiT2) >> 2); // uiVL5
        uint8_t kuiVL6 = (uint8_t)((2 + kuiT1 + (kuiT2 << 1) + kuiT3) >> 2); // uiVL6
        uint8_t kuiVL7 = (uint8_t)((2 + kuiT2 + (kuiT3 << 1) + kuiT4) >> 2); // uiVL7
        uint8_t kuiVL8 = (uint8_t)((2 + kuiT3 + (kuiT4 << 1) + kuiT5) >> 2); // uiVL8
        uint8_t kuiVL9 = (uint8_t)((2 + kuiT4 + (kuiT5 << 1) + kuiT6) >> 2); // uiVL9
        uint8_t* uiSrc = stackalloc uint8_t[16]; // TobeCont'd about assign opt as follows
        uiSrc[0] = kuiVL0;
        uiSrc[1] = uiSrc[8] = kuiVL1;
        uiSrc[2] = uiSrc[9] = kuiVL2;
        uiSrc[3] = uiSrc[10] = kuiVL3;
        uiSrc[4] = kuiVL5;
        uiSrc[5] = uiSrc[12] = kuiVL6;
        uiSrc[6] = uiSrc[13] = kuiVL7;
        uiSrc[7] = uiSrc[14] = kuiVL8;
        uiSrc[11] = kuiVL4;
        uiSrc[15] = kuiVL9;

        WelsFillingPred8x2to16_c(pPred, uiSrc);
    }

    /*vertical pLeft*/
    public static void WelsI4x4LumaPredVLTop_c(uint8_t* pPred, uint8_t* pRef, int32_t kiStride)
    {
        uint8_t* pTopLeft = &pRef[-kiStride - 1]; // pTop-pLeft
        /*get pTop*/
        uint8_t kuiT0 = *(pTopLeft + 1);
        uint8_t kuiT1 = *(pTopLeft + 2);
        uint8_t kuiT2 = *(pTopLeft + 3);
        uint8_t kuiT3 = *(pTopLeft + 4);
        uint8_t kuiVLT0 = (uint8_t)((1 + kuiT0 + kuiT1) >> 1);              // uiVLT0
        uint8_t kuiVLT1 = (uint8_t)((1 + kuiT1 + kuiT2) >> 1);              // uiVLT1
        uint8_t kuiVLT2 = (uint8_t)((1 + kuiT2 + kuiT3) >> 1);              // uiVLT2
        uint8_t kuiVLT3 = (uint8_t)((1 + (kuiT3 << 1)) >> 1);               // uiVLT3
        uint8_t kuiVLT4 = (uint8_t)((2 + kuiT0 + (kuiT1 << 1) + kuiT2) >> 2); // uiVLT4
        uint8_t kuiVLT5 = (uint8_t)((2 + kuiT1 + (kuiT2 << 1) + kuiT3) >> 2); // uiVLT5
        uint8_t kuiVLT6 = (uint8_t)((2 + kuiT2 + (kuiT3 << 1) + kuiT3) >> 2); // uiVLT6
        uint8_t kuiVLT7 = (uint8_t)((2 + (kuiT3 << 2)) >> 2);               // uiVLT7
        uint8_t* uiSrc = stackalloc uint8_t[16]; // TobeCont'd about assign opt as follows
        uiSrc[0] = kuiVLT0;
        uiSrc[1] = uiSrc[8] = kuiVLT1;
        uiSrc[2] = uiSrc[9] = kuiVLT2;
        uiSrc[3] = uiSrc[10] = uiSrc[11] = kuiVLT3;
        uiSrc[4] = kuiVLT4;
        uiSrc[5] = uiSrc[12] = kuiVLT5;
        uiSrc[6] = uiSrc[13] = kuiVLT6;
        uiSrc[7] = uiSrc[14] = uiSrc[15] = kuiVLT7;

        WelsFillingPred8x2to16_c(pPred, uiSrc);
    }

    /*vertical right*/
    public static void WelsI4x4LumaPredVR_c(uint8_t* pPred, uint8_t* pRef, int32_t kiStride)
    {
        int32_t kiStridex2 = kiStride << 1;
        uint8_t kuiLT = pRef[-kiStride - 1]; // pTop-pLeft
        /*get pLeft and pTop*/
        uint8_t kuiL0 = pRef[-1];
        uint8_t kuiL1 = pRef[kiStride - 1];
        uint8_t kuiL2 = pRef[kiStridex2 - 1];
        uint8_t kuiT0 = pRef[-kiStride];
        uint8_t kuiT1 = pRef[1 - kiStride];
        uint8_t kuiT2 = pRef[2 - kiStride];
        uint8_t kuiT3 = pRef[3 - kiStride];
        uint8_t kuiVR0 = (uint8_t)((1 + kuiLT + kuiT0) >> 1);
        uint8_t kuiVR1 = (uint8_t)((1 + kuiT0 + kuiT1) >> 1);
        uint8_t kuiVR2 = (uint8_t)((1 + kuiT1 + kuiT2) >> 1);
        uint8_t kuiVR3 = (uint8_t)((1 + kuiT2 + kuiT3) >> 1);
        uint8_t kuiVR4 = (uint8_t)((2 + kuiL0 + (kuiLT << 1) + kuiT0) >> 2);
        uint8_t kuiVR5 = (uint8_t)((2 + kuiLT + (kuiT0 << 1) + kuiT1) >> 2);
        uint8_t kuiVR6 = (uint8_t)((2 + kuiT0 + (kuiT1 << 1) + kuiT2) >> 2);
        uint8_t kuiVR7 = (uint8_t)((2 + kuiT1 + (kuiT2 << 1) + kuiT3) >> 2);
        uint8_t kuiVR8 = (uint8_t)((2 + kuiLT + (kuiL0 << 1) + kuiL1) >> 2);
        uint8_t kuiVR9 = (uint8_t)((2 + kuiL0 + (kuiL1 << 1) + kuiL2) >> 2);
        uint8_t* uiSrc = stackalloc uint8_t[16]; // TobeCont'd about assign opt as follows
        uiSrc[0] = uiSrc[9] = kuiVR0;
        uiSrc[1] = uiSrc[10] = kuiVR1;
        uiSrc[2] = uiSrc[11] = kuiVR2;
        uiSrc[3] = kuiVR3;
        uiSrc[4] = uiSrc[13] = kuiVR4;
        uiSrc[5] = uiSrc[14] = kuiVR5;
        uiSrc[6] = uiSrc[15] = kuiVR6;
        uiSrc[7] = kuiVR7;
        uiSrc[8] = kuiVR8;
        uiSrc[12] = kuiVR9;

        WelsFillingPred8x2to16_c(pPred, uiSrc);
    }

    /*horizontal up*/
    public static void WelsI4x4LumaPredHU_c(uint8_t* pPred, uint8_t* pRef, int32_t kiStride)
    {
        int32_t kiStridex2 = kiStride << 1;
        int32_t kiStridex3 = kiStride + kiStridex2;
        /*get pLeft*/
        uint8_t kuiL0 = pRef[-1];
        uint8_t kuiL1 = pRef[kiStride - 1];
        uint8_t kuiL2 = pRef[kiStridex2 - 1];
        uint8_t kuiL3 = pRef[kiStridex3 - 1];
        uint16_t kuiL01 = (uint16_t)(1 + kuiL0 + kuiL1);
        uint16_t kuiL12 = (uint16_t)(1 + kuiL1 + kuiL2);
        uint16_t kuiL23 = (uint16_t)(1 + kuiL2 + kuiL3);
        uint8_t kuiHU0 = (uint8_t)(kuiL01 >> 1);
        uint8_t kuiHU1 = (uint8_t)((kuiL01 + kuiL12) >> 2);
        uint8_t kuiHU2 = (uint8_t)(kuiL12 >> 1);
        uint8_t kuiHU3 = (uint8_t)((kuiL12 + kuiL23) >> 2);
        uint8_t kuiHU4 = (uint8_t)(kuiL23 >> 1);
        uint8_t kuiHU5 = (uint8_t)((1 + kuiL23 + (kuiL3 << 1)) >> 2);
        uint8_t* uiSrc = stackalloc uint8_t[16]; // TobeCont'd about assign opt as follows
        uiSrc[0] = kuiHU0;
        uiSrc[1] = kuiHU1;
        uiSrc[2] = uiSrc[4] = kuiHU2;
        uiSrc[3] = uiSrc[5] = kuiHU3;
        uiSrc[6] = uiSrc[8] = kuiHU4;
        uiSrc[7] = uiSrc[9] = kuiHU5;
        memset(&uiSrc[10], kuiL3, 6 * sizeof(uint8_t));

        WelsFillingPred8x2to16_c(pPred, uiSrc);
    }

    /*horizontal down*/
    public static void WelsI4x4LumaPredHD_c(uint8_t* pPred, uint8_t* pRef, int32_t kiStride)
    {
        int32_t kiStridex2 = kiStride << 1;
        int32_t kiStridex3 = kiStride + kiStridex2;
        uint8_t kuiLT = pRef[-kiStride - 1]; // pTop-pLeft
        /*get pLeft and pTop*/
        uint8_t kuiL0 = pRef[-1];
        uint8_t kuiL1 = pRef[kiStride - 1];
        uint8_t kuiL2 = pRef[kiStridex2 - 1];
        uint8_t kuiL3 = pRef[kiStridex3 - 1];
        uint8_t kuiT0 = pRef[-kiStride];
        uint8_t kuiT1 = pRef[1 - kiStride];
        uint8_t kuiT2 = pRef[2 - kiStride];
        uint8_t kuiHD0 = (uint8_t)((1 + kuiLT + kuiL0) >> 1);              // uiHD0
        uint8_t kuiHD1 = (uint8_t)((2 + kuiL0 + (kuiLT << 1) + kuiT0) >> 2); // uiHD1
        uint8_t kuiHD2 = (uint8_t)((2 + kuiLT + (kuiT0 << 1) + kuiT1) >> 2); // uiHD2
        uint8_t kuiHD3 = (uint8_t)((2 + kuiT0 + (kuiT1 << 1) + kuiT2) >> 2); // uiHD3
        uint8_t kuiHD4 = (uint8_t)((1 + kuiL0 + kuiL1) >> 1);              // uiHD4
        uint8_t kuiHD5 = (uint8_t)((2 + kuiLT + (kuiL0 << 1) + kuiL1) >> 2); // uiHD5
        uint8_t kuiHD6 = (uint8_t)((1 + kuiL1 + kuiL2) >> 1);              // uiHD6
        uint8_t kuiHD7 = (uint8_t)((2 + kuiL0 + (kuiL1 << 1) + kuiL2) >> 2); // uiHD7
        uint8_t kuiHD8 = (uint8_t)((1 + kuiL2 + kuiL3) >> 1);              // uiHD8
        uint8_t kuiHD9 = (uint8_t)((2 + kuiL1 + (kuiL2 << 1) + kuiL3) >> 2); // uiHD9
        uint8_t* uiSrc = stackalloc uint8_t[16]; // TobeCont'd about assign opt as follows
        uiSrc[0] = uiSrc[6] = kuiHD0;
        uiSrc[1] = uiSrc[7] = kuiHD1;
        uiSrc[2] = kuiHD2;
        uiSrc[3] = kuiHD3;
        uiSrc[4] = uiSrc[10] = kuiHD4;
        uiSrc[5] = uiSrc[11] = kuiHD5;
        uiSrc[8] = uiSrc[14] = kuiHD6;
        uiSrc[9] = uiSrc[15] = kuiHD7;
        uiSrc[12] = kuiHD8;
        uiSrc[13] = kuiHD9;

        WelsFillingPred8x2to16_c(pPred, uiSrc);
    }

    // #define I8x8_PRED_STRIDE 8
    private const int32_t I8x8_PRED_STRIDE = 8;

    public static void WelsIChromaPredV_c(uint8_t* pPred, uint8_t* pRef, int32_t kiStride)
    {
        uint64_t kuiSrc64 = LD64(&pRef[-kiStride]);

        ST64(pPred, kuiSrc64);
        ST64(pPred + 8, kuiSrc64);
        ST64(pPred + 16, kuiSrc64);
        ST64(pPred + 24, kuiSrc64);
        ST64(pPred + 32, kuiSrc64);
        ST64(pPred + 40, kuiSrc64);
        ST64(pPred + 48, kuiSrc64);
        ST64(pPred + 56, kuiSrc64);
    }

    public static void WelsIChromaPredH_c(uint8_t* pPred, uint8_t* pRef, int32_t kiStride)
    {
        int32_t iStridex7 = (kiStride << 3) - kiStride;
        int32_t iI8x8Stridex7 = (I8x8_PRED_STRIDE << 3) - I8x8_PRED_STRIDE;
        uint8_t i = 7;

        do
        {
            uint8_t kuiLeft = pRef[iStridex7 - 1]; // pLeft value
            uint64_t kuiSrc64 = (uint64_t)(0x0101010101010101UL * kuiLeft);
            ST64(pPred + iI8x8Stridex7, kuiSrc64);

            iStridex7 -= kiStride;
            iI8x8Stridex7 -= I8x8_PRED_STRIDE;
        } while (i-- > 0);
    }

    public static void WelsIChromaPredPlane_c(uint8_t* pPred, uint8_t* pRef, int32_t kiStride)
    {
        int32_t iLTshift = 0, iTopshift = 0, iLeftshift = 0, iTopSum = 0, iLeftSum = 0;
        int32_t i, j;
        uint8_t* pTop = &pRef[-kiStride];
        uint8_t* pLeft = &pRef[-1];

        for (i = 0; i < 4; i++)
        {
            iTopSum += (i + 1) * (pTop[4 + i] - pTop[2 - i]);
            iLeftSum += (i + 1) * (pLeft[(4 + i) * kiStride] - pLeft[(2 - i) * kiStride]);
        }

        iLTshift = (pLeft[7 * kiStride] + pTop[7]) << 4;
        iTopshift = (17 * iTopSum + 16) >> 5;
        iLeftshift = (17 * iLeftSum + 16) >> 5;

        for (i = 0; i < 8; i++)
        {
            for (j = 0; j < 8; j++)
            {
                pPred[j] = WelsClip1((iLTshift + iTopshift * (j - 3) + iLeftshift * (i - 3) + 16) >> 5);
            }
            pPred += I8x8_PRED_STRIDE;
        }
    }

    public static void WelsIChromaPredDc_c(uint8_t* pPred, uint8_t* pRef, int32_t kiStride)
    {
        int32_t kuiL1 = kiStride - 1;
        int32_t kuiL2 = kuiL1 + kiStride;
        int32_t kuiL3 = kuiL2 + kiStride;
        int32_t kuiL4 = kuiL3 + kiStride;
        int32_t kuiL5 = kuiL4 + kiStride;
        int32_t kuiL6 = kuiL5 + kiStride;
        int32_t kuiL7 = kuiL6 + kiStride;
        /*caculate the iMean value*/
        uint8_t kuiMean1 = (uint8_t)((pRef[-kiStride] + pRef[1 - kiStride] + pRef[2 - kiStride] + pRef[3 - kiStride] +
                                      pRef[-1] + pRef[kuiL1] + pRef[kuiL2] + pRef[kuiL3] + 4) >> 3);
        uint32_t kuiSum2 = (uint32_t)(pRef[4 - kiStride] + pRef[5 - kiStride] + pRef[6 - kiStride] + pRef[7 - kiStride]);
        uint32_t kuiSum3 = (uint32_t)(pRef[kuiL4] + pRef[kuiL5] + pRef[kuiL6] + pRef[kuiL7]);
        uint8_t kuiMean2 = (uint8_t)((kuiSum2 + 2) >> 2);
        uint8_t kuiMean3 = (uint8_t)((kuiSum3 + 2) >> 2);
        uint8_t kuiMean4 = (uint8_t)((kuiSum2 + kuiSum3 + 4) >> 3);

        uint8_t* kuiTopMean = stackalloc uint8_t[8] { kuiMean1, kuiMean1, kuiMean1, kuiMean1, kuiMean2, kuiMean2, kuiMean2, kuiMean2 };
        uint8_t* kuiBottomMean = stackalloc uint8_t[8] { kuiMean3, kuiMean3, kuiMean3, kuiMean3, kuiMean4, kuiMean4, kuiMean4, kuiMean4 };
        uint64_t kuiTopMean64 = LD64(kuiTopMean);
        uint64_t kuiBottomMean64 = LD64(kuiBottomMean);

        ST64(pPred, kuiTopMean64);
        ST64(pPred + 8, kuiTopMean64);
        ST64(pPred + 16, kuiTopMean64);
        ST64(pPred + 24, kuiTopMean64);
        ST64(pPred + 32, kuiBottomMean64);
        ST64(pPred + 40, kuiBottomMean64);
        ST64(pPred + 48, kuiBottomMean64);
        ST64(pPred + 56, kuiBottomMean64);
    }

    public static void WelsIChromaPredDcLeft_c(uint8_t* pPred, uint8_t* pRef, int32_t kiStride)
    {
        int32_t kuiL1 = kiStride - 1;
        int32_t kuiL2 = kuiL1 + kiStride;
        int32_t kuiL3 = kuiL2 + kiStride;
        int32_t kuiL4 = kuiL3 + kiStride;
        int32_t kuiL5 = kuiL4 + kiStride;
        int32_t kuiL6 = kuiL5 + kiStride;
        int32_t kuiL7 = kuiL6 + kiStride;
        /*caculate the iMean value*/
        uint8_t kuiTopMean = (uint8_t)((pRef[-1] + pRef[kuiL1] + pRef[kuiL2] + pRef[kuiL3] + 2) >> 2);
        uint8_t kuiBottomMean = (uint8_t)((pRef[kuiL4] + pRef[kuiL5] + pRef[kuiL6] + pRef[kuiL7] + 2) >> 2);
        uint64_t kuiTopMean64 = (uint64_t)(0x0101010101010101UL * kuiTopMean);
        uint64_t kuiBottomMean64 = (uint64_t)(0x0101010101010101UL * kuiBottomMean);

        ST64(pPred, kuiTopMean64);
        ST64(pPred + 8, kuiTopMean64);
        ST64(pPred + 16, kuiTopMean64);
        ST64(pPred + 24, kuiTopMean64);
        ST64(pPred + 32, kuiBottomMean64);
        ST64(pPred + 40, kuiBottomMean64);
        ST64(pPred + 48, kuiBottomMean64);
        ST64(pPred + 56, kuiBottomMean64);
    }

    public static void WelsIChromaPredDcTop_c(uint8_t* pPred, uint8_t* pRef, int32_t kiStride)
    {
        /*caculate the iMean value*/
        uint8_t kuiMean1 = (uint8_t)((pRef[-kiStride] + pRef[1 - kiStride] + pRef[2 - kiStride] + pRef[3 - kiStride] + 2) >> 2);
        uint8_t kuiMean2 = (uint8_t)((pRef[4 - kiStride] + pRef[5 - kiStride] + pRef[6 - kiStride] + pRef[7 - kiStride] + 2) >> 2);
        uint8_t* kuiMean = stackalloc uint8_t[8] { kuiMean1, kuiMean1, kuiMean1, kuiMean1, kuiMean2, kuiMean2, kuiMean2, kuiMean2 };
        uint64_t kuiMean64 = LD64(kuiMean);

        ST64(pPred, kuiMean64);
        ST64(pPred + 8, kuiMean64);
        ST64(pPred + 16, kuiMean64);
        ST64(pPred + 24, kuiMean64);
        ST64(pPred + 32, kuiMean64);
        ST64(pPred + 40, kuiMean64);
        ST64(pPred + 48, kuiMean64);
        ST64(pPred + 56, kuiMean64);
    }

    public static void WelsIChromaPredDcNA_c(uint8_t* pPred, uint8_t* pRef, int32_t kiStride)
    {
        const uint64_t kuiDcValue64 = (uint64_t)0x8080808080808080UL;
        ST64(pPred, kuiDcValue64);
        ST64(pPred + 8, kuiDcValue64);
        ST64(pPred + 16, kuiDcValue64);
        ST64(pPred + 24, kuiDcValue64);
        ST64(pPred + 32, kuiDcValue64);
        ST64(pPred + 40, kuiDcValue64);
        ST64(pPred + 48, kuiDcValue64);
        ST64(pPred + 56, kuiDcValue64);
    }

    public static void WelsI16x16LumaPredPlane_c(uint8_t* pPred, uint8_t* pRef, int32_t kiStride)
    {
        int32_t iLTshift = 0, iTopshift = 0, iLeftshift = 0, iTopSum = 0, iLeftSum = 0;
        int32_t i, j;
        uint8_t* pTop = &pRef[-kiStride];
        uint8_t* pLeft = &pRef[-1];
        int32_t iPredStride = 16;

        for (i = 0; i < 8; i++)
        {
            iTopSum += (i + 1) * (pTop[8 + i] - pTop[6 - i]);
            iLeftSum += (i + 1) * (pLeft[(8 + i) * kiStride] - pLeft[(6 - i) * kiStride]);
        }

        iLTshift = (pLeft[15 * kiStride] + pTop[15]) << 4;
        iTopshift = (5 * iTopSum + 32) >> 6;
        iLeftshift = (5 * iLeftSum + 32) >> 6;

        for (i = 0; i < 16; i++)
        {
            for (j = 0; j < 16; j++)
            {
                pPred[j] = WelsClip1((iLTshift + iTopshift * (j - 7) + iLeftshift * (i - 7) + 16) >> 5);
            }
            pPred += iPredStride;
        }
    }

    public static void WelsI16x16LumaPredDc_c(uint8_t* pPred, uint8_t* pRef, int32_t kiStride)
    {
        int32_t iStridex15 = (kiStride << 4) - kiStride;
        int32_t iSum = 0;
        uint8_t i = 15;
        uint8_t iMean = 0;

        /*caculate the iMean value*/
        do
        {
            iSum += pRef[-1 + iStridex15] + pRef[-kiStride + i];
            iStridex15 -= kiStride;
        } while (i-- > 0);
        iMean = (uint8_t)((16 + iSum) >> 5);
        memset(pPred, iMean, 256);
    }

    public static void WelsI16x16LumaPredDcTop_c(uint8_t* pPred, uint8_t* pRef, int32_t kiStride)
    {
        int32_t iSum = 0;
        uint8_t i = 15;
        uint8_t iMean = 0;

        /*caculate the iMean value*/
        do
        {
            iSum += pRef[-kiStride + i];
        } while (i-- > 0);
        iMean = (uint8_t)((8 + iSum) >> 4);
        memset(pPred, iMean, 256);
    }

    public static void WelsI16x16LumaPredDcLeft_c(uint8_t* pPred, uint8_t* pRef, int32_t kiStride)
    {
        int32_t iStridex15 = (kiStride << 4) - kiStride;
        int32_t iSum = 0;
        uint8_t i = 15;
        uint8_t iMean = 0;

        /*caculate the iMean value*/
        do
        {
            iSum += pRef[-1 + iStridex15];
            iStridex15 -= kiStride;
        } while (i-- > 0);
        iMean = (uint8_t)((8 + iSum) >> 4);
        memset(pPred, iMean, 256);
    }

    public static void WelsI16x16LumaPredDcNA_c(uint8_t* pPred, uint8_t* pRef, int32_t kiStride)
    {
        memset(pPred, 0x80, 256);
    }

    public static void WelsInitIntraPredFuncs(SWelsFuncPtrList* pFuncList, uint32_t kuiCpuFlag)
    {
        pFuncList->pfGetLumaI16x16Pred[I16_PRED_V] = &WelsI16x16LumaPredV_c;
        pFuncList->pfGetLumaI16x16Pred[I16_PRED_H] = &WelsI16x16LumaPredH_c;
        pFuncList->pfGetLumaI16x16Pred[I16_PRED_DC] = &WelsI16x16LumaPredDc_c;
        pFuncList->pfGetLumaI16x16Pred[I16_PRED_P] = &WelsI16x16LumaPredPlane_c;
        pFuncList->pfGetLumaI16x16Pred[I16_PRED_DC_L] = &WelsI16x16LumaPredDcLeft_c;
        pFuncList->pfGetLumaI16x16Pred[I16_PRED_DC_T] = &WelsI16x16LumaPredDcTop_c;
        pFuncList->pfGetLumaI16x16Pred[I16_PRED_DC_128] = &WelsI16x16LumaPredDcNA_c;

        pFuncList->pfGetLumaI4x4Pred[I4_PRED_V] = &WelsI4x4LumaPredV_c;
        pFuncList->pfGetLumaI4x4Pred[I4_PRED_H] = &WelsI4x4LumaPredH_c;
        pFuncList->pfGetLumaI4x4Pred[I4_PRED_DC] = &WelsI4x4LumaPredDc_c;
        pFuncList->pfGetLumaI4x4Pred[I4_PRED_DC_L] = &WelsI4x4LumaPredDcLeft_c;
        pFuncList->pfGetLumaI4x4Pred[I4_PRED_DC_T] = &WelsI4x4LumaPredDcTop_c;
        pFuncList->pfGetLumaI4x4Pred[I4_PRED_DC_128] = &WelsI4x4LumaPredDcNA_c;

        pFuncList->pfGetLumaI4x4Pred[I4_PRED_DDL] = &WelsI4x4LumaPredDDL_c;
        pFuncList->pfGetLumaI4x4Pred[I4_PRED_DDL_TOP] = &WelsI4x4LumaPredDDLTop_c;
        pFuncList->pfGetLumaI4x4Pred[I4_PRED_DDR] = &WelsI4x4LumaPredDDR_c;

        pFuncList->pfGetLumaI4x4Pred[I4_PRED_VL] = &WelsI4x4LumaPredVL_c;
        pFuncList->pfGetLumaI4x4Pred[I4_PRED_VL_TOP] = &WelsI4x4LumaPredVLTop_c;
        pFuncList->pfGetLumaI4x4Pred[I4_PRED_VR] = &WelsI4x4LumaPredVR_c;
        pFuncList->pfGetLumaI4x4Pred[I4_PRED_HU] = &WelsI4x4LumaPredHU_c;
        pFuncList->pfGetLumaI4x4Pred[I4_PRED_HD] = &WelsI4x4LumaPredHD_c;

        pFuncList->pfGetChromaPred[C_PRED_DC] = &WelsIChromaPredDc_c;
        pFuncList->pfGetChromaPred[C_PRED_H] = &WelsIChromaPredH_c;
        pFuncList->pfGetChromaPred[C_PRED_V] = &WelsIChromaPredV_c;
        pFuncList->pfGetChromaPred[C_PRED_P] = &WelsIChromaPredPlane_c;
        pFuncList->pfGetChromaPred[C_PRED_DC_L] = &WelsIChromaPredDcLeft_c;
        pFuncList->pfGetChromaPred[C_PRED_DC_T] = &WelsIChromaPredDcTop_c;
        pFuncList->pfGetChromaPred[C_PRED_DC_128] = &WelsIChromaPredDcNA_c;

        // In place of the X86_ASM and NEON overrides, the 16x16 and chroma plane predictors in
        // get_intra_predictor_simd.cs and common/intra_pred_common_simd.cs. The other chroma
        // and the 4x4 predictors stay C (see that file), as do the MMI and LSX overrides.
        if (WelsCpuHasSimd128(kuiCpuFlag))
        {
            pFuncList->pfGetLumaI16x16Pred[I16_PRED_V] = &WelsI16x16LumaPredV_simd;
            pFuncList->pfGetLumaI16x16Pred[I16_PRED_H] = &WelsI16x16LumaPredH_simd;
            pFuncList->pfGetLumaI16x16Pred[I16_PRED_DC] = &WelsI16x16LumaPredDc_simd;
            pFuncList->pfGetLumaI16x16Pred[I16_PRED_P] = &WelsI16x16LumaPredPlane_simd;
            pFuncList->pfGetLumaI16x16Pred[I16_PRED_DC_L] = &WelsI16x16LumaPredDcLeft_simd;
            pFuncList->pfGetLumaI16x16Pred[I16_PRED_DC_T] = &WelsI16x16LumaPredDcTop_simd;
            pFuncList->pfGetLumaI16x16Pred[I16_PRED_DC_128] = &WelsI16x16LumaPredDcNA_simd;

            pFuncList->pfGetChromaPred[C_PRED_P] = &WelsIChromaPredPlane_simd;
        }
    }
}
