//-----------------------------------------------------------------------------
// Filename: get_intra_predictor.cs
//
// Description: Port of codec/decoder/core/inc/get_intra_predictor.h and
// src/get_intra_predictor.cpp: the decoder's intra predictors, 4x4 and 8x8
// luma, 16x16 luma and 8x8 chroma, each writing its prediction in place over
// the block at pPred from the reconstructed neighbours around it.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// C kernels only; the SIMD variants are not ported. The 8x8 predictors are
// High profile only (transform_size_8x8_flag) but are ported with the rest of
// the file. The C's local uint8_t lists become stackallocs.
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

namespace SIPSorcery.H264.Decoder;

internal static unsafe partial class WelsDec
{
    public const int I4x4_COUNT = 4;
    public const int I8x8_COUNT = 8;
    public const int I16x16_COUNT = 16;

    public static void WelsI4x4LumaPredV_c(uint8_t* pPred, int32_t kiStride)
    {
        uint32_t kuiVal = LD32A4(pPred - kiStride);

        ST32A4(pPred, kuiVal);
        ST32A4(pPred + kiStride, kuiVal);
        ST32A4(pPred + (kiStride << 1), kuiVal);
        ST32A4(pPred + (kiStride << 1) + kiStride, kuiVal);
    }

    public static void WelsI4x4LumaPredH_c(uint8_t* pPred, int32_t kiStride)
    {
        int32_t kiStride2 = kiStride << 1;
        int32_t kiStride3 = kiStride2 + kiStride;
        uint32_t kuiL0 = 0x01010101U * pPred[-1];
        uint32_t kuiL1 = 0x01010101U * pPred[-1 + kiStride];
        uint32_t kuiL2 = 0x01010101U * pPred[-1 + kiStride2];
        uint32_t kuiL3 = 0x01010101U * pPred[-1 + kiStride3];

        ST32A4(pPred, kuiL0);
        ST32A4(pPred + kiStride, kuiL1);
        ST32A4(pPred + kiStride2, kuiL2);
        ST32A4(pPred + kiStride3, kuiL3);
    }

    public static void WelsI4x4LumaPredDc_c(uint8_t* pPred, int32_t kiStride)
    {
        int32_t kiStride2 = kiStride << 1;
        int32_t kiStride3 = kiStride2 + kiStride;
        uint8_t kuiMean = (uint8_t)((pPred[-1] + pPred[-1 + kiStride] + pPred[-1 + kiStride2] + pPred[-1 + kiStride3] +
                                     pPred[-kiStride] + pPred[-kiStride + 1] + pPred[-kiStride + 2] + pPred[-kiStride + 3] + 4) >> 3);
        uint32_t kuiMean32 = 0x01010101U * kuiMean;

        ST32A4(pPred, kuiMean32);
        ST32A4(pPred + kiStride, kuiMean32);
        ST32A4(pPred + kiStride2, kuiMean32);
        ST32A4(pPred + kiStride3, kuiMean32);
    }

    public static void WelsI4x4LumaPredDcLeft_c(uint8_t* pPred, int32_t kiStride)
    {
        int32_t kiStride2 = kiStride << 1;
        int32_t kiStride3 = kiStride2 + kiStride;
        uint8_t kuiMean = (uint8_t)((pPred[-1] + pPred[-1 + kiStride] + pPred[-1 + kiStride2] + pPred[-1 + kiStride3] + 2) >> 2);
        uint32_t kuiMean32 = 0x01010101U * kuiMean;

        ST32A4(pPred, kuiMean32);
        ST32A4(pPred + kiStride, kuiMean32);
        ST32A4(pPred + kiStride2, kuiMean32);
        ST32A4(pPred + kiStride3, kuiMean32);
    }

    public static void WelsI4x4LumaPredDcTop_c(uint8_t* pPred, int32_t kiStride)
    {
        int32_t kiStride2 = kiStride << 1;
        int32_t kiStride3 = kiStride2 + kiStride;
        uint8_t kuiMean = (uint8_t)((pPred[-kiStride] + pPred[-kiStride + 1] + pPred[-kiStride + 2] + pPred[-kiStride + 3] + 2)
                                    >> 2);
        uint32_t kuiMean32 = 0x01010101U * kuiMean;

        ST32A4(pPred, kuiMean32);
        ST32A4(pPred + kiStride, kuiMean32);
        ST32A4(pPred + kiStride2, kuiMean32);
        ST32A4(pPred + kiStride3, kuiMean32);
    }

    public static void WelsI4x4LumaPredDcNA_c(uint8_t* pPred, int32_t kiStride)
    {
        const uint32_t kuiDC32 = 0x80808080U;

        ST32A4(pPred, kuiDC32);
        ST32A4(pPred + kiStride, kuiDC32);
        ST32A4(pPred + (kiStride << 1), kuiDC32);
        ST32A4(pPred + (kiStride << 1) + kiStride, kuiDC32);
    }

    /*down pLeft*/
    public static void WelsI4x4LumaPredDDL_c(uint8_t* pPred, int32_t kiStride)
    {
        int32_t kiStride2 = kiStride << 1;
        int32_t kiStride3 = kiStride + kiStride2;
        /*get pTop*/
        uint8_t* ptop = &pPred[-kiStride];
        uint8_t kuiT0 = *ptop;
        uint8_t kuiT1 = *(ptop + 1);
        uint8_t kuiT2 = *(ptop + 2);
        uint8_t kuiT3 = *(ptop + 3);
        uint8_t kuiT4 = *(ptop + 4);
        uint8_t kuiT5 = *(ptop + 5);
        uint8_t kuiT6 = *(ptop + 6);
        uint8_t kuiT7 = *(ptop + 7);
        uint8_t kuiDDL0 = (uint8_t)((2 + kuiT0 + kuiT2 + (kuiT1 << 1)) >> 2);      // kDDL0
        uint8_t kuiDDL1 = (uint8_t)((2 + kuiT1 + kuiT3 + (kuiT2 << 1)) >> 2);      // kDDL1
        uint8_t kuiDDL2 = (uint8_t)((2 + kuiT2 + kuiT4 + (kuiT3 << 1)) >> 2);      // kDDL2
        uint8_t kuiDDL3 = (uint8_t)((2 + kuiT3 + kuiT5 + (kuiT4 << 1)) >> 2);      // kDDL3
        uint8_t kuiDDL4 = (uint8_t)((2 + kuiT4 + kuiT6 + (kuiT5 << 1)) >> 2);      // kDDL4
        uint8_t kuiDDL5 = (uint8_t)((2 + kuiT5 + kuiT7 + (kuiT6 << 1)) >> 2);      // kDDL5
        uint8_t kuiDDL6 = (uint8_t)((2 + kuiT6 + kuiT7 + (kuiT7 << 1)) >> 2);      // kDDL6
        uint8_t* kuiList = stackalloc uint8_t[8] { kuiDDL0, kuiDDL1, kuiDDL2, kuiDDL3, kuiDDL4, kuiDDL5, kuiDDL6, 0 };

        ST32A4(pPred, LD32(kuiList));
        ST32A4(pPred + kiStride, LD32(kuiList + 1));
        ST32A4(pPred + kiStride2, LD32(kuiList + 2));
        ST32A4(pPred + kiStride3, LD32(kuiList + 3));
    }

    /*down pLeft*/
    public static void WelsI4x4LumaPredDDLTop_c(uint8_t* pPred, int32_t kiStride)
    {
        int32_t kiStride2 = kiStride << 1;
        int32_t kiStride3 = kiStride + kiStride2;
        /*get pTop*/
        uint8_t* ptop = &pPred[-kiStride];
        uint8_t kuiT0 = *ptop;
        uint8_t kuiT1 = *(ptop + 1);
        uint8_t kuiT2 = *(ptop + 2);
        uint8_t kuiT3 = *(ptop + 3);
        uint16_t kuiT01 = (uint16_t)(1 + kuiT0 + kuiT1);
        uint16_t kuiT12 = (uint16_t)(1 + kuiT1 + kuiT2);
        uint16_t kuiT23 = (uint16_t)(1 + kuiT2 + kuiT3);
        uint16_t kuiT33 = (uint16_t)(1 + (kuiT3 << 1));
        uint8_t kuiDLT0 = (uint8_t)((kuiT01 + kuiT12) >> 2);       // kDLT0
        uint8_t kuiDLT1 = (uint8_t)((kuiT12 + kuiT23) >> 2);       // kDLT1
        uint8_t kuiDLT2 = (uint8_t)((kuiT23 + kuiT33) >> 2);       // kDLT2
        uint8_t kuiDLT3 = (uint8_t)(kuiT33 >> 1);                  // kDLT3
        uint8_t* kuiList = stackalloc uint8_t[8] { kuiDLT0, kuiDLT1, kuiDLT2, kuiDLT3, kuiDLT3, kuiDLT3, kuiDLT3, kuiDLT3 };

        ST32A4(pPred, LD32(kuiList));
        ST32A4(pPred + kiStride, LD32(kuiList + 1));
        ST32A4(pPred + kiStride2, LD32(kuiList + 2));
        ST32A4(pPred + kiStride3, LD32(kuiList + 3));
    }


    /*down right*/
    public static void WelsI4x4LumaPredDDR_c(uint8_t* pPred, int32_t kiStride)
    {
        int32_t kiStride2 = kiStride << 1;
        int32_t kiStride3 = kiStride + kiStride2;
        uint8_t* ptopleft = &pPred[-(kiStride + 1)];
        uint8_t* pleft = &pPred[-1];
        uint8_t kuiLT = *ptopleft;
        /*get pLeft and pTop*/
        uint8_t kuiL0 = *pleft;
        uint8_t kuiL1 = *(pleft + kiStride);
        uint8_t kuiL2 = *(pleft + kiStride2);
        uint8_t kuiL3 = *(pleft + kiStride3);
        uint8_t kuiT0 = *(ptopleft + 1);
        uint8_t kuiT1 = *(ptopleft + 2);
        uint8_t kuiT2 = *(ptopleft + 3);
        uint8_t kuiT3 = *(ptopleft + 4);
        uint16_t kuiTL0 = (uint16_t)(1 + kuiLT + kuiL0);
        uint16_t kuiLT0 = (uint16_t)(1 + kuiLT + kuiT0);
        uint16_t kuiT01 = (uint16_t)(1 + kuiT0 + kuiT1);
        uint16_t kuiT12 = (uint16_t)(1 + kuiT1 + kuiT2);
        uint16_t kuiT23 = (uint16_t)(1 + kuiT2 + kuiT3);
        uint16_t kuiL01 = (uint16_t)(1 + kuiL0 + kuiL1);
        uint16_t kuiL12 = (uint16_t)(1 + kuiL1 + kuiL2);
        uint16_t kuiL23 = (uint16_t)(1 + kuiL2 + kuiL3);
        uint8_t kuiDDR0 = (uint8_t)((kuiTL0 + kuiLT0) >> 2);       // kuiDDR0
        uint8_t kuiDDR1 = (uint8_t)((kuiLT0 + kuiT01) >> 2);       // kuiDDR1
        uint8_t kuiDDR2 = (uint8_t)((kuiT01 + kuiT12) >> 2);       // kuiDDR2
        uint8_t kuiDDR3 = (uint8_t)((kuiT12 + kuiT23) >> 2);       // kuiDDR3
        uint8_t kuiDDR4 = (uint8_t)((kuiTL0 + kuiL01) >> 2);       // kuiDDR4
        uint8_t kuiDDR5 = (uint8_t)((kuiL01 + kuiL12) >> 2);       // kuiDDR5
        uint8_t kuiDDR6 = (uint8_t)((kuiL12 + kuiL23) >> 2);       // kuiDDR6
        uint8_t* kuiList = stackalloc uint8_t[8] { kuiDDR6, kuiDDR5, kuiDDR4, kuiDDR0, kuiDDR1, kuiDDR2, kuiDDR3, 0 };

        ST32A4(pPred, LD32(kuiList + 3));
        ST32A4(pPred + kiStride, LD32(kuiList + 2));
        ST32A4(pPred + kiStride2, LD32(kuiList + 1));
        ST32A4(pPred + kiStride3, LD32(kuiList));
    }


    /*vertical pLeft*/
    public static void WelsI4x4LumaPredVL_c(uint8_t* pPred, int32_t kiStride)
    {
        int32_t kiStride2 = kiStride << 1;
        int32_t kiStride3 = kiStride + kiStride2;
        uint8_t* ptopleft = &pPred[-(kiStride + 1)];
        /*get pTop*/
        uint8_t kuiT0 = *(ptopleft + 1);
        uint8_t kuiT1 = *(ptopleft + 2);
        uint8_t kuiT2 = *(ptopleft + 3);
        uint8_t kuiT3 = *(ptopleft + 4);
        uint8_t kuiT4 = *(ptopleft + 5);
        uint8_t kuiT5 = *(ptopleft + 6);
        uint8_t kuiT6 = *(ptopleft + 7);
        uint16_t kuiT01 = (uint16_t)(1 + kuiT0 + kuiT1);
        uint16_t kuiT12 = (uint16_t)(1 + kuiT1 + kuiT2);
        uint16_t kuiT23 = (uint16_t)(1 + kuiT2 + kuiT3);
        uint16_t kuiT34 = (uint16_t)(1 + kuiT3 + kuiT4);
        uint16_t kuiT45 = (uint16_t)(1 + kuiT4 + kuiT5);
        uint16_t kuiT56 = (uint16_t)(1 + kuiT5 + kuiT6);
        uint8_t kuiVL0 = (uint8_t)(kuiT01 >> 1);                  // kuiVL0
        uint8_t kuiVL1 = (uint8_t)(kuiT12 >> 1);                  // kuiVL1
        uint8_t kuiVL2 = (uint8_t)(kuiT23 >> 1);                  // kuiVL2
        uint8_t kuiVL3 = (uint8_t)(kuiT34 >> 1);                  // kuiVL3
        uint8_t kuiVL4 = (uint8_t)(kuiT45 >> 1);                  // kuiVL4
        uint8_t kuiVL5 = (uint8_t)((kuiT01 + kuiT12) >> 2);       // kuiVL5
        uint8_t kuiVL6 = (uint8_t)((kuiT12 + kuiT23) >> 2);       // kuiVL6
        uint8_t kuiVL7 = (uint8_t)((kuiT23 + kuiT34) >> 2);       // kuiVL7
        uint8_t kuiVL8 = (uint8_t)((kuiT34 + kuiT45) >> 2);       // kuiVL8
        uint8_t kuiVL9 = (uint8_t)((kuiT45 + kuiT56) >> 2);       // kuiVL9
        uint8_t* kuiList = stackalloc uint8_t[10] { kuiVL0, kuiVL1, kuiVL2, kuiVL3, kuiVL4, kuiVL5, kuiVL6, kuiVL7, kuiVL8, kuiVL9 };

        ST32A4(pPred, LD32(kuiList));
        ST32A4(pPred + kiStride, LD32(kuiList + 5));
        ST32A4(pPred + kiStride2, LD32(kuiList + 1));
        ST32A4(pPred + kiStride3, LD32(kuiList + 6));
    }

    /*vertical pLeft*/
    public static void WelsI4x4LumaPredVLTop_c(uint8_t* pPred, int32_t kiStride)
    {
        int32_t kiStride2 = kiStride << 1;
        int32_t kiStride3 = kiStride + kiStride2;
        uint8_t* ptopleft = &pPred[-(kiStride + 1)];
        /*get pTop*/
        uint8_t kuiT0 = *(ptopleft + 1);
        uint8_t kuiT1 = *(ptopleft + 2);
        uint8_t kuiT2 = *(ptopleft + 3);
        uint8_t kuiT3 = *(ptopleft + 4);
        uint16_t kuiT01 = (uint16_t)(1 + kuiT0 + kuiT1);
        uint16_t kuiT12 = (uint16_t)(1 + kuiT1 + kuiT2);
        uint16_t kuiT23 = (uint16_t)(1 + kuiT2 + kuiT3);
        uint16_t kuiT33 = (uint16_t)(1 + (kuiT3 << 1));
        uint8_t kuiVL0 = (uint8_t)(kuiT01 >> 1);
        uint8_t kuiVL1 = (uint8_t)(kuiT12 >> 1);
        uint8_t kuiVL2 = (uint8_t)(kuiT23 >> 1);
        uint8_t kuiVL3 = (uint8_t)(kuiT33 >> 1);
        uint8_t kuiVL4 = (uint8_t)((kuiT01 + kuiT12) >> 2);
        uint8_t kuiVL5 = (uint8_t)((kuiT12 + kuiT23) >> 2);
        uint8_t kuiVL6 = (uint8_t)((kuiT23 + kuiT33) >> 2);
        uint8_t kuiVL7 = kuiVL3;
        uint8_t* kuiList = stackalloc uint8_t[10] { kuiVL0, kuiVL1, kuiVL2, kuiVL3, kuiVL3, kuiVL4, kuiVL5, kuiVL6, kuiVL7, kuiVL7 };

        ST32A4(pPred, LD32(kuiList));
        ST32A4(pPred + kiStride, LD32(kuiList + 5));
        ST32A4(pPred + kiStride2, LD32(kuiList + 1));
        ST32A4(pPred + kiStride3, LD32(kuiList + 6));
    }


    /*vertical right*/
    public static void WelsI4x4LumaPredVR_c(uint8_t* pPred, int32_t kiStride)
    {
        int32_t kiStride2 = kiStride << 1;
        int32_t kiStride3 = kiStride + kiStride2;
        uint8_t kuiLT = pPred[-kiStride - 1];
        /*get pLeft and pTop*/
        uint8_t kuiL0 = pPred[-1];
        uint8_t kuiL1 = pPred[kiStride - 1];
        uint8_t kuiL2 = pPred[kiStride2 - 1];
        uint8_t kuiT0 = pPred[-kiStride];
        uint8_t kuiT1 = pPred[1 - kiStride];
        uint8_t kuiT2 = pPred[2 - kiStride];
        uint8_t kuiT3 = pPred[3 - kiStride];
        uint8_t kuiVR0 = (uint8_t)((1 + kuiLT + kuiT0) >> 1);     // kuiVR0
        uint8_t kuiVR1 = (uint8_t)((1 + kuiT0 + kuiT1) >> 1);     // kuiVR1
        uint8_t kuiVR2 = (uint8_t)((1 + kuiT1 + kuiT2) >> 1);     // kuiVR2
        uint8_t kuiVR3 = (uint8_t)((1 + kuiT2 + kuiT3) >> 1);     // kuiVR3
        uint8_t kuiVR4 = (uint8_t)((2 + kuiL0 + (kuiLT << 1) + kuiT0) >> 2);      // kuiVR4
        uint8_t kuiVR5 = (uint8_t)((2 + kuiLT + (kuiT0 << 1) + kuiT1) >> 2);      // kuiVR5
        uint8_t kuiVR6 = (uint8_t)((2 + kuiT0 + (kuiT1 << 1) + kuiT2) >> 2);      // kuiVR6
        uint8_t kuiVR7 = (uint8_t)((2 + kuiT1 + (kuiT2 << 1) + kuiT3) >> 2);      // kuiVR7
        uint8_t kuiVR8 = (uint8_t)((2 + kuiLT + (kuiL0 << 1) + kuiL1) >> 2);      // kuiVR8
        uint8_t kuiVR9 = (uint8_t)((2 + kuiL0 + (kuiL1 << 1) + kuiL2) >> 2);      // kuiVR9
        uint8_t* kuiList = stackalloc uint8_t[10] { kuiVR8, kuiVR0, kuiVR1, kuiVR2, kuiVR3, kuiVR9, kuiVR4, kuiVR5, kuiVR6, kuiVR7 };

        ST32A4(pPred, LD32(kuiList + 1));
        ST32A4(pPred + kiStride, LD32(kuiList + 6));
        ST32A4(pPred + kiStride2, LD32(kuiList));
        ST32A4(pPred + kiStride3, LD32(kuiList + 5));
    }

    /*horizontal up*/
    public static void WelsI4x4LumaPredHU_c(uint8_t* pPred, int32_t kiStride)
    {
        int32_t kiStride2 = kiStride << 1;
        int32_t kiStride3 = kiStride + kiStride2;
        /*get pLeft*/
        uint8_t kuiL0 = pPred[-1];
        uint8_t kuiL1 = pPred[kiStride - 1];
        uint8_t kuiL2 = pPred[kiStride2 - 1];
        uint8_t kuiL3 = pPred[kiStride3 - 1];
        uint16_t kuiL01 = (uint16_t)(1 + kuiL0 + kuiL1);
        uint16_t kuiL12 = (uint16_t)(1 + kuiL1 + kuiL2);
        uint16_t kuiL23 = (uint16_t)(1 + kuiL2 + kuiL3);
        uint8_t kuiHU0 = (uint8_t)(kuiL01 >> 1);
        uint8_t kuiHU1 = (uint8_t)((kuiL01 + kuiL12) >> 2);
        uint8_t kuiHU2 = (uint8_t)(kuiL12 >> 1);
        uint8_t kuiHU3 = (uint8_t)((kuiL12 + kuiL23) >> 2);
        uint8_t kuiHU4 = (uint8_t)(kuiL23 >> 1);
        uint8_t kuiHU5 = (uint8_t)((1 + kuiL23 + (kuiL3 << 1)) >> 2);
        uint8_t* kuiList = stackalloc uint8_t[10] { kuiHU0, kuiHU1, kuiHU2, kuiHU3, kuiHU4, kuiHU5, kuiL3, kuiL3, kuiL3, kuiL3 };

        ST32A4(pPred, LD32(kuiList));
        ST32A4(pPred + kiStride, LD32(kuiList + 2));
        ST32A4(pPred + kiStride2, LD32(kuiList + 4));
        ST32A4(pPred + kiStride3, LD32(kuiList + 6));
    }

    /*horizontal down*/
    public static void WelsI4x4LumaPredHD_c(uint8_t* pPred, int32_t kiStride)
    {
        int32_t kiStride2 = kiStride << 1;
        int32_t kiStride3 = kiStride + kiStride2;
        uint8_t kuiLT = pPred[-(kiStride + 1)];
        /*get pLeft and pTop*/
        uint8_t kuiL0 = pPred[-1];
        uint8_t kuiL1 = pPred[-1 + kiStride];
        uint8_t kuiL2 = pPred[-1 + kiStride2];
        uint8_t kuiL3 = pPred[-1 + kiStride3];
        uint8_t kuiT0 = pPred[-kiStride];
        uint8_t kuiT1 = pPred[-kiStride + 1];
        uint8_t kuiT2 = pPred[-kiStride + 2];
        uint16_t kuiTL0 = (uint16_t)(1 + kuiLT + kuiL0);
        uint16_t kuiLT0 = (uint16_t)(1 + kuiLT + kuiT0);
        uint16_t kuiT01 = (uint16_t)(1 + kuiT0 + kuiT1);
        uint16_t kuiT12 = (uint16_t)(1 + kuiT1 + kuiT2);
        uint16_t kuiL01 = (uint16_t)(1 + kuiL0 + kuiL1);
        uint16_t kuiL12 = (uint16_t)(1 + kuiL1 + kuiL2);
        uint16_t kuiL23 = (uint16_t)(1 + kuiL2 + kuiL3);
        uint8_t kuiHD0 = (uint8_t)(kuiTL0 >> 1);
        uint8_t kuiHD1 = (uint8_t)((kuiTL0 + kuiLT0) >> 2);
        uint8_t kuiHD2 = (uint8_t)((kuiLT0 + kuiT01) >> 2);
        uint8_t kuiHD3 = (uint8_t)((kuiT01 + kuiT12) >> 2);
        uint8_t kuiHD4 = (uint8_t)(kuiL01 >> 1);
        uint8_t kuiHD5 = (uint8_t)((kuiTL0 + kuiL01) >> 2);
        uint8_t kuiHD6 = (uint8_t)(kuiL12 >> 1);
        uint8_t kuiHD7 = (uint8_t)((kuiL01 + kuiL12) >> 2);
        uint8_t kuiHD8 = (uint8_t)(kuiL23 >> 1);
        uint8_t kuiHD9 = (uint8_t)((kuiL12 + kuiL23) >> 2);
        uint8_t* kuiList = stackalloc uint8_t[10] { kuiHD8, kuiHD9, kuiHD6, kuiHD7, kuiHD4, kuiHD5, kuiHD0, kuiHD1, kuiHD2, kuiHD3 };

        ST32A4(pPred, LD32(kuiList + 6));
        ST32A4(pPred + kiStride, LD32(kuiList + 4));
        ST32A4(pPred + kiStride2, LD32(kuiList + 2));
        ST32A4(pPred + kiStride3, LD32(kuiList));
    }

    public static void WelsI8x8LumaPredV_c(uint8_t* pPred, int32_t kiStride, bool bTLAvail, bool bTRAvail)
    {
        uint64_t uiTop = 0;
        int32_t* iStride = stackalloc int32_t[8];
        uint8_t* uiPixelFilterT = stackalloc uint8_t[8];
        int32_t i;

        for (iStride[0] = 0, i = 1; i < 8; i++)
        {
            iStride[i] = iStride[i - 1] + kiStride;
        }

        uiPixelFilterT[0] = (uint8_t)(bTLAvail ? ((pPred[-1 - kiStride] + (pPred[-kiStride] << 1) + pPred[1 - kiStride] + 2) >> 2) : ((
                              pPred[-kiStride] * 3 + pPred[1 - kiStride] + 2) >> 2));
        for (i = 1; i < 7; i++)
        {
            uiPixelFilterT[i] = (uint8_t)((pPred[i - 1 - kiStride] + (pPred[i - kiStride] << 1) + pPred[i + 1 - kiStride] + 2) >> 2);
        }
        uiPixelFilterT[7] = (uint8_t)(bTRAvail ? ((pPred[6 - kiStride] + (pPred[7 - kiStride] << 1) + pPred[8 - kiStride] + 2) >> 2) : ((
                              pPred[6 - kiStride] + pPred[7 - kiStride] * 3 + 2) >> 2));

        // 8-89
        for (i = 7; i >= 0; i--)
        {
            uiTop = ((uiTop << 8) | uiPixelFilterT[i]);
        }

        for (i = 0; i < 8; i++)
        {
            ST64A8(pPred + kiStride * i, uiTop);
        }
    }

    public static void WelsI8x8LumaPredH_c(uint8_t* pPred, int32_t kiStride, bool bTLAvail, bool bTRAvail)
    {
        uint64_t uiLeft;
        int32_t* iStride = stackalloc int32_t[8];
        uint8_t* uiPixelFilterL = stackalloc uint8_t[8];
        int32_t i;

        for (iStride[0] = 0, i = 1; i < 8; i++)
        {
            iStride[i] = iStride[i - 1] + kiStride;
        }

        uiPixelFilterL[0] = (uint8_t)(bTLAvail ? ((pPred[-1 - kiStride] + (pPred[-1] << 1) + pPred[-1 + iStride[1]] + 2) >> 2) : ((
                              pPred[-1] * 3 + pPred[-1 + iStride[1]] + 2) >> 2));
        for (i = 1; i < 7; i++)
        {
            uiPixelFilterL[i] = (uint8_t)((pPred[-1 + iStride[i - 1]] + (pPred[-1 + iStride[i]] << 1) + pPred[-1 + iStride[i + 1]] + 2) >>
                                          2);
        }
        uiPixelFilterL[7] = (uint8_t)((pPred[-1 + iStride[6]] + pPred[-1 + iStride[7]] * 3 + 2) >> 2);

        // 8-90
        for (i = 0; i < 8; i++)
        {
            uiLeft = 0x0101010101010101UL * uiPixelFilterL[i];
            ST64A8(pPred + iStride[i], uiLeft);
        }
    }

    public static void WelsI8x8LumaPredDc_c(uint8_t* pPred, int32_t kiStride, bool bTLAvail, bool bTRAvail)
    {
        int32_t* iStride = stackalloc int32_t[8];
        uint8_t* uiPixelFilterL = stackalloc uint8_t[8];
        uint8_t* uiPixelFilterT = stackalloc uint8_t[8];
        uint16_t uiTotal = 0;
        int32_t i;

        for (iStride[0] = 0, i = 1; i < 8; i++)
        {
            iStride[i] = iStride[i - 1] + kiStride;
        }

        uiPixelFilterL[0] = (uint8_t)(bTLAvail ? ((pPred[-1 - kiStride] + (pPred[-1] << 1) + pPred[-1 + iStride[1]] + 2) >> 2) : ((
                              pPred[-1] * 3 + pPred[-1 + iStride[1]] + 2) >> 2));
        uiPixelFilterT[0] = (uint8_t)(bTLAvail ? ((pPred[-1 - kiStride] + (pPred[-kiStride] << 1) + pPred[1 - kiStride] + 2) >> 2) : ((
                              pPred[-kiStride] * 3 + pPred[1 - kiStride] + 2) >> 2));
        for (i = 1; i < 7; i++)
        {
            uiPixelFilterL[i] = (uint8_t)((pPred[-1 + iStride[i - 1]] + (pPred[-1 + iStride[i]] << 1) + pPred[-1 + iStride[i + 1]] + 2) >>
                                          2);
            uiPixelFilterT[i] = (uint8_t)((pPred[i - 1 - kiStride] + (pPred[i - kiStride] << 1) + pPred[i + 1 - kiStride] + 2) >> 2);
        }
        uiPixelFilterL[7] = (uint8_t)((pPred[-1 + iStride[6]] + pPred[-1 + iStride[7]] * 3 + 2) >> 2);
        uiPixelFilterT[7] = (uint8_t)(bTRAvail ? ((pPred[6 - kiStride] + (pPred[7 - kiStride] << 1) + pPred[8 - kiStride] + 2) >> 2) : ((
                              pPred[6 - kiStride] + pPred[7 - kiStride] * 3 + 2) >> 2));

        // 8-91
        for (i = 0; i < 8; i++)
        {
            uiTotal += uiPixelFilterL[i];
            uiTotal += uiPixelFilterT[i];
        }

        uint8_t kuiMean = (uint8_t)((uiTotal + 8) >> 4);
        uint64_t kuiMean64 = 0x0101010101010101UL * kuiMean;

        for (i = 0; i < 8; i++)
        {
            ST64A8(pPred + iStride[i], kuiMean64);
        }
    }

    public static void WelsI8x8LumaPredDcLeft_c(uint8_t* pPred, int32_t kiStride, bool bTLAvail, bool bTRAvail)
    {
        int32_t* iStride = stackalloc int32_t[8];
        uint8_t* uiPixelFilterL = stackalloc uint8_t[8];
        uint16_t uiTotal = 0;
        int32_t i;

        for (iStride[0] = 0, i = 1; i < 8; i++)
        {
            iStride[i] = iStride[i - 1] + kiStride;
        }

        uiPixelFilterL[0] = (uint8_t)(bTLAvail ? ((pPred[-1 - kiStride] + (pPred[-1] << 1) + pPred[-1 + iStride[1]] + 2) >> 2) : ((
                              pPred[-1] * 3 + pPred[-1 + iStride[1]] + 2) >> 2));
        for (i = 1; i < 7; i++)
        {
            uiPixelFilterL[i] = (uint8_t)((pPred[-1 + iStride[i - 1]] + (pPred[-1 + iStride[i]] << 1) + pPred[-1 + iStride[i + 1]] + 2) >>
                                          2);
        }
        uiPixelFilterL[7] = (uint8_t)((pPred[-1 + iStride[6]] + pPred[-1 + iStride[7]] * 3 + 2) >> 2);

        // 8-92
        for (i = 0; i < 8; i++)
        {
            uiTotal += uiPixelFilterL[i];
        }

        uint8_t kuiMean = (uint8_t)((uiTotal + 4) >> 3);
        uint64_t kuiMean64 = 0x0101010101010101UL * kuiMean;

        for (i = 0; i < 8; i++)
        {
            ST64A8(pPred + iStride[i], kuiMean64);
        }
    }

    public static void WelsI8x8LumaPredDcTop_c(uint8_t* pPred, int32_t kiStride, bool bTLAvail, bool bTRAvail)
    {
        int32_t* iStride = stackalloc int32_t[8];
        uint8_t* uiPixelFilterT = stackalloc uint8_t[8];
        uint16_t uiTotal = 0;
        int32_t i;

        for (iStride[0] = 0, i = 1; i < 8; i++)
        {
            iStride[i] = iStride[i - 1] + kiStride;
        }

        uiPixelFilterT[0] = (uint8_t)(bTLAvail ? ((pPred[-1 - kiStride] + (pPred[-kiStride] << 1) + pPred[1 - kiStride] + 2) >> 2) : ((
                              pPred[-kiStride] * 3 + pPred[1 - kiStride] + 2) >> 2));
        for (i = 1; i < 7; i++)
        {
            uiPixelFilterT[i] = (uint8_t)((pPred[i - 1 - kiStride] + (pPred[i - kiStride] << 1) + pPred[i + 1 - kiStride] + 2) >> 2);
        }
        uiPixelFilterT[7] = (uint8_t)(bTRAvail ? ((pPred[6 - kiStride] + (pPred[7 - kiStride] << 1) + pPred[8 - kiStride] + 2) >> 2) : ((
                              pPred[6 - kiStride] + pPred[7 - kiStride] * 3 + 2) >> 2));

        // 8-93
        for (i = 0; i < 8; i++)
        {
            uiTotal += uiPixelFilterT[i];
        }

        uint8_t kuiMean = (uint8_t)((uiTotal + 4) >> 3);
        uint64_t kuiMean64 = 0x0101010101010101UL * kuiMean;

        for (i = 0; i < 8; i++)
        {
            ST64A8(pPred + iStride[i], kuiMean64);
        }
    }

    public static void WelsI8x8LumaPredDcNA_c(uint8_t* pPred, int32_t kiStride, bool bTLAvail, bool bTRAvail)
    {
        // for normal 8 bit depth, 8-94
        const uint64_t kuiDC64 = 0x8080808080808080UL;

        int32_t* iStride = stackalloc int32_t[8];
        int32_t i;
        ST64A8(pPred, kuiDC64);
        for (iStride[0] = 0, i = 1; i < 8; i++)
        {
            iStride[i] = iStride[i - 1] + kiStride;
            ST64A8(pPred + iStride[i], kuiDC64);
        }
    }

    /*down pLeft*/
    public static void WelsI8x8LumaPredDDL_c(uint8_t* pPred, int32_t kiStride, bool bTLAvail, bool bTRAvail)
    {
        // Top and Top-right available
        int32_t* iStride = stackalloc int32_t[8];
        uint8_t* uiPixelFilterT = stackalloc uint8_t[16];
        int32_t i, j;

        for (iStride[0] = 0, i = 1; i < 8; i++)
        {
            iStride[i] = iStride[i - 1] + kiStride;
        }

        uiPixelFilterT[0] = (uint8_t)(bTLAvail ? ((pPred[-1 - kiStride] + (pPred[-kiStride] << 1) + pPred[1 - kiStride] + 2) >> 2) : ((
                              pPred[-kiStride] * 3 + pPred[1 - kiStride] + 2) >> 2));
        for (i = 1; i < 15; i++)
        {
            uiPixelFilterT[i] = (uint8_t)((pPred[i - 1 - kiStride] + (pPred[i - kiStride] << 1) + pPred[i + 1 - kiStride] + 2) >> 2);
        }
        uiPixelFilterT[15] = (uint8_t)((pPred[14 - kiStride] + pPred[15 - kiStride] * 3 + 2) >> 2);

        for (i = 0; i < 8; i++)
        { // y
            for (j = 0; j < 8; j++)
            { // x
                if (i == 7 && j == 7)
                { // 8-95
                    pPred[j + iStride[i]] = (uint8_t)((uiPixelFilterT[14] + 3 * uiPixelFilterT[15] + 2) >> 2);
                }
                else
                { // 8-96
                    pPred[j + iStride[i]] = (uint8_t)((uiPixelFilterT[i + j] + (uiPixelFilterT[i + j + 1] << 1) + uiPixelFilterT[i + j + 2] + 2) >> 2);
                }
            }
        }
    }

    /*down pLeft*/
    public static void WelsI8x8LumaPredDDLTop_c(uint8_t* pPred, int32_t kiStride, bool bTLAvail, bool bTRAvail)
    {
        // Top available and Top-right unavailable
        int32_t* iStride = stackalloc int32_t[8];
        uint8_t* uiPixelFilterT = stackalloc uint8_t[16];
        int32_t i, j;

        for (iStride[0] = 0, i = 1; i < 8; i++)
        {
            iStride[i] = iStride[i - 1] + kiStride;
        }

        uiPixelFilterT[0] = (uint8_t)(bTLAvail ? ((pPred[-1 - kiStride] + (pPred[-kiStride] << 1) + pPred[1 - kiStride] + 2) >> 2) : ((
                              pPred[-kiStride] * 3 + pPred[1 - kiStride] + 2) >> 2));
        for (i = 1; i < 7; i++)
        {
            uiPixelFilterT[i] = (uint8_t)((pPred[i - 1 - kiStride] + (pPred[i - kiStride] << 1) + pPred[i + 1 - kiStride] + 2) >> 2);
        }
        // p[x, -1] x=8...15 are replaced with p[7, -1]
        uiPixelFilterT[7] = (uint8_t)((pPred[6 - kiStride] + pPred[7 - kiStride] * 3 + 2) >> 2);
        for (i = 8; i < 16; i++)
        {
            uiPixelFilterT[i] = pPred[7 - kiStride];
        }

        for (i = 0; i < 8; i++)
        { // y
            for (j = 0; j < 8; j++)
            { // x
                if (i == 7 && j == 7)
                { // 8-95
                    pPred[j + iStride[i]] = (uint8_t)((uiPixelFilterT[14] + 3 * uiPixelFilterT[15] + 2) >> 2);
                }
                else
                { // 8-96
                    pPred[j + iStride[i]] = (uint8_t)((uiPixelFilterT[i + j] + (uiPixelFilterT[i + j + 1] << 1) + uiPixelFilterT[i + j + 2] + 2) >> 2);
                }
            }
        }
    }

    /*down right*/
    public static void WelsI8x8LumaPredDDR_c(uint8_t* pPred, int32_t kiStride, bool bTLAvail, bool bTRAvail)
    {
        // The TopLeft, Top, Left are all available under this mode
        int32_t* iStride = stackalloc int32_t[8];
        uint8_t uiPixelFilterTL;
        uint8_t* uiPixelFilterL = stackalloc uint8_t[8];
        uint8_t* uiPixelFilterT = stackalloc uint8_t[8];
        int32_t i, j;

        for (iStride[0] = 0, i = 1; i < 8; i++)
        {
            iStride[i] = iStride[i - 1] + kiStride;
        }

        uiPixelFilterTL = (uint8_t)((pPred[-1] + (pPred[-1 - kiStride] << 1) + pPred[-kiStride] + 2) >> 2);

        uiPixelFilterL[0] = (uint8_t)((pPred[-1 - kiStride] + (pPred[-1] << 1) + pPred[-1 + iStride[1]] + 2) >> 2);
        uiPixelFilterT[0] = (uint8_t)((pPred[-1 - kiStride] + (pPred[-kiStride] << 1) + pPred[1 - kiStride] + 2) >> 2);
        for (i = 1; i < 7; i++)
        {
            uiPixelFilterL[i] = (uint8_t)((pPred[-1 + iStride[i - 1]] + (pPred[-1 + iStride[i]] << 1) + pPred[-1 + iStride[i + 1]] + 2) >>
                                          2);
            uiPixelFilterT[i] = (uint8_t)((pPred[i - 1 - kiStride] + (pPred[i - kiStride] << 1) + pPred[i + 1 - kiStride] + 2) >> 2);
        }
        uiPixelFilterL[7] = (uint8_t)((pPred[-1 + iStride[6]] + pPred[-1 + iStride[7]] * 3 + 2) >> 2);
        uiPixelFilterT[7] = (uint8_t)(bTRAvail ? ((pPred[6 - kiStride] + (pPred[7 - kiStride] << 1) + pPred[8 - kiStride] + 2) >> 2) : ((
                              pPred[6 - kiStride] + pPred[7 - kiStride] * 3 + 2) >> 2));

        for (i = 0; i < 8; i++)
        { // y
            // 8-98, x < y-1
            for (j = 0; j < (i - 1); j++)
            {
                pPred[j + iStride[i]] = (uint8_t)((uiPixelFilterL[i - j - 2] + (uiPixelFilterL[i - j - 1] << 1) + uiPixelFilterL[i - j] + 2) >> 2);
            }
            // 8-98, special case, x == y-1
            if (i >= 1)
            {
                j = i - 1;
                pPred[j + iStride[i]] = (uint8_t)((uiPixelFilterTL + (uiPixelFilterL[0] << 1) + uiPixelFilterL[1] + 2) >> 2);
            }
            // 8-99, x==y
            j = i;
            pPred[j + iStride[i]] = (uint8_t)((uiPixelFilterT[0] + (uiPixelFilterTL << 1) + uiPixelFilterL[0] + 2) >> 2);
            // 8-97, special case, x == y+1
            if (i < 7)
            {
                j = i + 1;
                pPred[j + iStride[i]] = (uint8_t)((uiPixelFilterTL + (uiPixelFilterT[0] << 1) + uiPixelFilterT[1] + 2) >> 2);
            }
            for (j = i + 2; j < 8; j++)
            { // 8-97, x > y+1
                pPred[j + iStride[i]] = (uint8_t)((uiPixelFilterT[j - i - 2] + (uiPixelFilterT[j - i - 1] << 1) + uiPixelFilterT[j - i] + 2) >> 2);
            }
        }
    }

    /*vertical pLeft*/
    public static void WelsI8x8LumaPredVL_c(uint8_t* pPred, int32_t kiStride, bool bTLAvail, bool bTRAvail)
    {
        // Top and Top-right available
        int32_t* iStride = stackalloc int32_t[8];
        uint8_t* uiPixelFilterT = stackalloc uint8_t[16];
        int32_t i, j;

        for (iStride[0] = 0, i = 1; i < 8; i++)
        {
            iStride[i] = iStride[i - 1] + kiStride;
        }

        uiPixelFilterT[0] = (uint8_t)(bTLAvail ? ((pPred[-1 - kiStride] + (pPred[-kiStride] << 1) + pPred[1 - kiStride] + 2) >> 2) : ((
                              pPred[-kiStride] * 3 + pPred[1 - kiStride] + 2) >> 2));
        for (i = 1; i < 15; i++)
        {
            uiPixelFilterT[i] = (uint8_t)((pPred[i - 1 - kiStride] + (pPred[i - kiStride] << 1) + pPred[i + 1 - kiStride] + 2) >> 2);
        }
        uiPixelFilterT[15] = (uint8_t)((pPred[14 - kiStride] + pPred[15 - kiStride] * 3 + 2) >> 2);

        for (i = 0; i < 8; i++)
        { // y
            if ((i & 0x01) == 0)
            { // 8-108
                for (j = 0; j < 8; j++)
                { // x
                    pPred[j + iStride[i]] = (uint8_t)((uiPixelFilterT[j + (i >> 1)] + uiPixelFilterT[j + (i >> 1) + 1] + 1) >> 1);
                }
            }
            else
            {  // 8-109
                for (j = 0; j < 8; j++)
                { // x
                    pPred[j + iStride[i]] = (uint8_t)((uiPixelFilterT[j + (i >> 1)] + (uiPixelFilterT[j + (i >> 1) + 1] << 1) + uiPixelFilterT[j +
                                                       (i >> 1) + 2] + 2) >> 2);
                }
            }
        }
    }

    /*vertical pLeft*/
    public static void WelsI8x8LumaPredVLTop_c(uint8_t* pPred, int32_t kiStride, bool bTLAvail, bool bTRAvail)
    {
        // Top available and Top-right unavailable
        int32_t* iStride = stackalloc int32_t[8];
        uint8_t* uiPixelFilterT = stackalloc uint8_t[16];
        int32_t i, j;

        for (iStride[0] = 0, i = 1; i < 8; i++)
        {
            iStride[i] = iStride[i - 1] + kiStride;
        }

        uiPixelFilterT[0] = (uint8_t)(bTLAvail ? ((pPred[-1 - kiStride] + (pPred[-kiStride] << 1) + pPred[1 - kiStride] + 2) >> 2) : ((
                              pPred[-kiStride] * 3 + pPred[1 - kiStride] + 2) >> 2));
        for (i = 1; i < 7; i++)
        {
            uiPixelFilterT[i] = (uint8_t)((pPred[i - 1 - kiStride] + (pPred[i - kiStride] << 1) + pPred[i + 1 - kiStride] + 2) >> 2);
        }
        // p[x, -1] x=8...15 are replaced with p[7, -1]
        uiPixelFilterT[7] = (uint8_t)((pPred[6 - kiStride] + pPred[7 - kiStride] * 3 + 2) >> 2);
        for (i = 8; i < 16; i++)
        {
            uiPixelFilterT[i] = pPred[7 - kiStride];
        }

        for (i = 0; i < 8; i++)
        { // y
            if ((i & 0x01) == 0)
            { // 8-108
                for (j = 0; j < 8; j++)
                { // x
                    pPred[j + iStride[i]] = (uint8_t)((uiPixelFilterT[j + (i >> 1)] + uiPixelFilterT[j + (i >> 1) + 1] + 1) >> 1);
                }
            }
            else
            {  // 8-109
                for (j = 0; j < 8; j++)
                { // x
                    pPred[j + iStride[i]] = (uint8_t)((uiPixelFilterT[j + (i >> 1)] + (uiPixelFilterT[j + (i >> 1) + 1] << 1) + uiPixelFilterT[j +
                                                       (i >> 1) + 2] + 2) >> 2);
                }
            }
        }
    }

    /*vertical right*/
    public static void WelsI8x8LumaPredVR_c(uint8_t* pPred, int32_t kiStride, bool bTLAvail, bool bTRAvail)
    {
        // The TopLeft, Top, Left are always available under this mode
        int32_t* iStride = stackalloc int32_t[8];
        uint8_t uiPixelFilterTL;
        uint8_t* uiPixelFilterL = stackalloc uint8_t[8];
        uint8_t* uiPixelFilterT = stackalloc uint8_t[8];
        int32_t i, j;
        int32_t izVR, izVRDiv;

        for (iStride[0] = 0, i = 1; i < 8; i++)
        {
            iStride[i] = iStride[i - 1] + kiStride;
        }

        uiPixelFilterTL = (uint8_t)((pPred[-1] + (pPred[-1 - kiStride] << 1) + pPred[-kiStride] + 2) >> 2);

        uiPixelFilterL[0] = (uint8_t)((pPred[-1 - kiStride] + (pPred[-1] << 1) + pPred[-1 + iStride[1]] + 2) >> 2);
        uiPixelFilterT[0] = (uint8_t)((pPred[-1 - kiStride] + (pPred[-kiStride] << 1) + pPred[1 - kiStride] + 2) >> 2);
        for (i = 1; i < 7; i++)
        {
            uiPixelFilterL[i] = (uint8_t)((pPred[-1 + iStride[i - 1]] + (pPred[-1 + iStride[i]] << 1) + pPred[-1 + iStride[i + 1]] + 2) >>
                                          2);
            uiPixelFilterT[i] = (uint8_t)((pPred[i - 1 - kiStride] + (pPred[i - kiStride] << 1) + pPred[i + 1 - kiStride] + 2) >> 2);
        }
        uiPixelFilterL[7] = (uint8_t)((pPred[-1 + iStride[6]] + pPred[-1 + iStride[7]] * 3 + 2) >> 2);
        uiPixelFilterT[7] = (uint8_t)(bTRAvail ? ((pPred[6 - kiStride] + (pPred[7 - kiStride] << 1) + pPred[8 - kiStride] + 2) >> 2) : ((
                              pPred[6 - kiStride] + pPred[7 - kiStride] * 3 + 2) >> 2));

        for (i = 0; i < 8; i++)
        { // y
            for (j = 0; j < 8; j++)
            { // x
                izVR = (j << 1) - i; // 2 * x - y
                izVRDiv = j - (i >> 1);
                if (izVR >= 0)
                {
                    if ((izVR & 0x01) == 0)
                    {  // 8-100
                        if (izVRDiv > 0)
                        {
                            pPred[j + iStride[i]] = (uint8_t)((uiPixelFilterT[izVRDiv - 1] + uiPixelFilterT[izVRDiv] + 1) >> 1);
                        }
                        else
                        {
                            pPred[j + iStride[i]] = (uint8_t)((uiPixelFilterTL + uiPixelFilterT[0] + 1) >> 1);
                        }
                    }
                    else
                    { // 8-101
                        if (izVRDiv > 1)
                        {
                            pPred[j + iStride[i]] = (uint8_t)((uiPixelFilterT[izVRDiv - 2] + (uiPixelFilterT[izVRDiv - 1] << 1) + uiPixelFilterT[izVRDiv] + 2)
                                                              >> 2);
                        }
                        else
                        {
                            pPred[j + iStride[i]] = (uint8_t)((uiPixelFilterTL + (uiPixelFilterT[0] << 1) + uiPixelFilterT[1] + 2) >> 2);
                        }
                    }
                }
                else if (izVR == -1)
                { // 8-102
                    pPred[j + iStride[i]] = (uint8_t)((uiPixelFilterL[0] + (uiPixelFilterTL << 1) + uiPixelFilterT[0] + 2) >> 2);
                }
                else if (izVR < -2)
                { // 8-103
                    pPred[j + iStride[i]] = (uint8_t)((uiPixelFilterL[-izVR - 1] + (uiPixelFilterL[-izVR - 2] << 1) + uiPixelFilterL[-izVR - 3] + 2)
                                                      >> 2);
                }
                else
                { // izVR==-2, 8-103, special case
                    pPred[j + iStride[i]] = (uint8_t)((uiPixelFilterL[1] + (uiPixelFilterL[0] << 1) + uiPixelFilterTL + 2) >> 2);
                }
            }
        }
    }

    /*horizontal up*/
    public static void WelsI8x8LumaPredHU_c(uint8_t* pPred, int32_t kiStride, bool bTLAvail, bool bTRAvail)
    {
        int32_t* iStride = stackalloc int32_t[8];
        uint8_t* uiPixelFilterL = stackalloc uint8_t[8];
        int32_t i, j;
        int32_t izHU;

        for (iStride[0] = 0, i = 1; i < 8; i++)
        {
            iStride[i] = iStride[i - 1] + kiStride;
        }

        uiPixelFilterL[0] = (uint8_t)(bTLAvail ? ((pPred[-1 - kiStride] + (pPred[-1] << 1) + pPred[-1 + iStride[1]] + 2) >> 2) : ((
                              pPred[-1] * 3 + pPred[-1 + iStride[1]] + 2) >> 2));
        for (i = 1; i < 7; i++)
        {
            uiPixelFilterL[i] = (uint8_t)((pPred[-1 + iStride[i - 1]] + (pPred[-1 + iStride[i]] << 1) + pPred[-1 + iStride[i + 1]] + 2) >>
                                          2);
        }
        uiPixelFilterL[7] = (uint8_t)((pPred[-1 + iStride[6]] + pPred[-1 + iStride[7]] * 3 + 2) >> 2);

        for (i = 0; i < 8; i++)
        { // y
            for (j = 0; j < 8; j++)
            { // x
                izHU = j + (i << 1); // x + 2 * y
                if (izHU < 13)
                {
                    if ((izHU & 0x01) == 0)
                    {  // 8-110
                        pPred[j + iStride[i]] = (uint8_t)((uiPixelFilterL[izHU >> 1] + uiPixelFilterL[1 + (izHU >> 1)] + 1) >> 1);
                    }
                    else
                    { // 8-111
                        pPred[j + iStride[i]] = (uint8_t)((uiPixelFilterL[izHU >> 1] + (uiPixelFilterL[1 + (izHU >> 1)] << 1) + uiPixelFilterL[2 +
                                                           (izHU >> 1)] + 2) >> 2);
                    }
                }
                else if (izHU == 13)
                { // 8-112
                    pPred[j + iStride[i]] = (uint8_t)((uiPixelFilterL[6] + 3 * uiPixelFilterL[7] + 2) >> 2);
                }
                else
                { // 8-113
                    pPred[j + iStride[i]] = uiPixelFilterL[7];
                }
            }
        }
    }

    /*horizontal down*/
    public static void WelsI8x8LumaPredHD_c(uint8_t* pPred, int32_t kiStride, bool bTLAvail, bool bTRAvail)
    {
        // The TopLeft, Top, Left are all available under this mode
        int32_t* iStride = stackalloc int32_t[8];
        uint8_t uiPixelFilterTL;
        uint8_t* uiPixelFilterL = stackalloc uint8_t[8];
        uint8_t* uiPixelFilterT = stackalloc uint8_t[8];
        int32_t i, j;
        int32_t izHD, izHDDiv;

        for (iStride[0] = 0, i = 1; i < 8; i++)
        {
            iStride[i] = iStride[i - 1] + kiStride;
        }

        uiPixelFilterTL = (uint8_t)((pPred[-1] + (pPred[-1 - kiStride] << 1) + pPred[-kiStride] + 2) >> 2);

        uiPixelFilterL[0] = (uint8_t)((pPred[-1 - kiStride] + (pPred[-1] << 1) + pPred[-1 + iStride[1]] + 2) >> 2);
        uiPixelFilterT[0] = (uint8_t)((pPred[-1 - kiStride] + (pPred[-kiStride] << 1) + pPred[1 - kiStride] + 2) >> 2);
        for (i = 1; i < 7; i++)
        {
            uiPixelFilterL[i] = (uint8_t)((pPred[-1 + iStride[i - 1]] + (pPred[-1 + iStride[i]] << 1) + pPred[-1 + iStride[i + 1]] + 2) >>
                                          2);
            uiPixelFilterT[i] = (uint8_t)((pPred[i - 1 - kiStride] + (pPred[i - kiStride] << 1) + pPred[i + 1 - kiStride] + 2) >> 2);
        }
        uiPixelFilterL[7] = (uint8_t)((pPred[-1 + iStride[6]] + pPred[-1 + iStride[7]] * 3 + 2) >> 2);
        uiPixelFilterT[7] = (uint8_t)(bTRAvail ? ((pPred[6 - kiStride] + (pPred[7 - kiStride] << 1) + pPred[8 - kiStride] + 2) >> 2) : ((
                              pPred[6 - kiStride] + pPred[7 - kiStride] * 3 + 2) >> 2));

        for (i = 0; i < 8; i++)
        { // y
            for (j = 0; j < 8; j++)
            { // x
                izHD = (i << 1) - j; // 2*y - x
                izHDDiv = i - (j >> 1);
                if (izHD >= 0)
                {
                    if ((izHD & 0x01) == 0)
                    {  // 8-104
                        if (izHDDiv == 0)
                        {
                            pPred[j + iStride[i]] = (uint8_t)((uiPixelFilterTL + uiPixelFilterL[0] + 1) >> 1);
                        }
                        else
                        {
                            pPred[j + iStride[i]] = (uint8_t)((uiPixelFilterL[izHDDiv - 1] + uiPixelFilterL[izHDDiv] + 1) >> 1);
                        }
                    }
                    else
                    {  // 8-105
                        if (izHDDiv == 1)
                        {
                            pPred[j + iStride[i]] = (uint8_t)((uiPixelFilterTL + (uiPixelFilterL[0] << 1) + uiPixelFilterL[1] + 2) >> 2);
                        }
                        else
                        {
                            pPred[j + iStride[i]] = (uint8_t)((uiPixelFilterL[izHDDiv - 2] + (uiPixelFilterL[izHDDiv - 1] << 1) + uiPixelFilterL[izHDDiv] + 2)
                                                              >> 2);
                        }
                    }
                }
                else if (izHD == -1)
                { // 8-106
                    pPred[j + iStride[i]] = (uint8_t)((uiPixelFilterL[0] + (uiPixelFilterTL << 1) + uiPixelFilterT[0] + 2) >> 2);
                }
                else if (izHD < -2)
                { // 8-107
                    pPred[j + iStride[i]] = (uint8_t)((uiPixelFilterT[-izHD - 1] + (uiPixelFilterT[-izHD - 2] << 1) + uiPixelFilterT[-izHD - 3] + 2)
                                                      >> 2);
                }
                else
                { // 8-107 special case, izHD==-2
                    pPred[j + iStride[i]] = (uint8_t)((uiPixelFilterT[1] + (uiPixelFilterT[0] << 1) + uiPixelFilterTL + 2) >> 2);
                }
            }
        }
    }


    public static void WelsIChromaPredV_c(uint8_t* pPred, int32_t kiStride)
    {
        uint64_t kuiVal64 = LD64A8(&pPred[-kiStride]);
        int32_t kiStride2 = kiStride << 1;
        int32_t kiStride4 = kiStride2 << 1;

        ST64A8(pPred, kuiVal64);
        ST64A8(pPred + kiStride, kuiVal64);
        ST64A8(pPred + kiStride2, kuiVal64);
        ST64A8(pPred + kiStride2 + kiStride, kuiVal64);
        ST64A8(pPred + kiStride4, kuiVal64);
        ST64A8(pPred + kiStride4 + kiStride, kuiVal64);
        ST64A8(pPred + kiStride4 + kiStride2, kuiVal64);
        ST64A8(pPred + (kiStride << 3) - kiStride, kuiVal64);
    }

    public static void WelsIChromaPredH_c(uint8_t* pPred, int32_t kiStride)
    {
        int32_t iTmp = (kiStride << 3) - kiStride;
        uint8_t i = 7;

        do
        {
            uint8_t kuiVal8 = pPred[iTmp - 1];
            uint64_t kuiVal64 = 0x0101010101010101UL * kuiVal8;

            ST64A8(pPred + iTmp, kuiVal64);

            iTmp -= kiStride;
        } while (i-- > 0);
    }


    public static void WelsIChromaPredPlane_c(uint8_t* pPred, int32_t kiStride)
    {
        int32_t a = 0, b = 0, c = 0, H = 0, V = 0;
        int32_t i, j;
        uint8_t* pTop = &pPred[-kiStride];
        uint8_t* pLeft = &pPred[-1];

        for (i = 0; i < 4; i++)
        {
            H += (i + 1) * (pTop[4 + i] - pTop[2 - i]);
            V += (i + 1) * (pLeft[(4 + i) * kiStride] - pLeft[(2 - i) * kiStride]);
        }

        a = (pLeft[7 * kiStride] + pTop[7]) << 4;
        b = (17 * H + 16) >> 5;
        c = (17 * V + 16) >> 5;

        for (i = 0; i < 8; i++)
        {
            for (j = 0; j < 8; j++)
            {
                int32_t iTmp = (a + b * (j - 3) + c * (i - 3) + 16) >> 5;
                iTmp = WelsClip1(iTmp);
                pPred[j] = (uint8_t)iTmp;
            }
            pPred += kiStride;
        }
    }


    public static void WelsIChromaPredDc_c(uint8_t* pPred, int32_t kiStride)
    {
        int32_t kiL1 = kiStride - 1;
        int32_t kiL2 = kiL1 + kiStride;
        int32_t kiL3 = kiL2 + kiStride;
        int32_t kiL4 = kiL3 + kiStride;
        int32_t kiL5 = kiL4 + kiStride;
        int32_t kiL6 = kiL5 + kiStride;
        int32_t kiL7 = kiL6 + kiStride;
        /*caculate the kMean value*/
        uint8_t kuiM1 = (uint8_t)((pPred[-kiStride] + pPred[1 - kiStride] + pPred[2 - kiStride] + pPred[3 - kiStride] +
                                   pPred[-1] + pPred[kiL1] + pPred[kiL2] + pPred[kiL3] + 4) >> 3);
        uint32_t kuiSum2 = (uint32_t)(pPred[4 - kiStride] + pPred[5 - kiStride] + pPred[6 - kiStride] + pPred[7 - kiStride]);
        uint32_t kuiSum3 = (uint32_t)(pPred[kiL4] + pPred[kiL5] + pPred[kiL6] + pPred[kiL7]);
        uint8_t kuiM2 = (uint8_t)((kuiSum2 + 2) >> 2);
        uint8_t kuiM3 = (uint8_t)((kuiSum3 + 2) >> 2);
        uint8_t kuiM4 = (uint8_t)((kuiSum2 + kuiSum3 + 4) >> 3);
        uint8_t* kuiMUP = stackalloc uint8_t[8] { kuiM1, kuiM1, kuiM1, kuiM1, kuiM2, kuiM2, kuiM2, kuiM2 };
        uint8_t* kuiMDown = stackalloc uint8_t[8] { kuiM3, kuiM3, kuiM3, kuiM3, kuiM4, kuiM4, kuiM4, kuiM4 };
        uint64_t kuiUP64 = LD64(kuiMUP);
        uint64_t kuiDN64 = LD64(kuiMDown);

        ST64A8(pPred, kuiUP64);
        ST64A8(pPred + kiL1 + 1, kuiUP64);
        ST64A8(pPred + kiL2 + 1, kuiUP64);
        ST64A8(pPred + kiL3 + 1, kuiUP64);
        ST64A8(pPred + kiL4 + 1, kuiDN64);
        ST64A8(pPred + kiL5 + 1, kuiDN64);
        ST64A8(pPred + kiL6 + 1, kuiDN64);
        ST64A8(pPred + kiL7 + 1, kuiDN64);
    }

    public static void WelsIChromaPredDcLeft_c(uint8_t* pPred, int32_t kiStride)
    {
        int32_t kiL1 = -1 + kiStride;
        int32_t kiL2 = kiL1 + kiStride;
        int32_t kiL3 = kiL2 + kiStride;
        int32_t kiL4 = kiL3 + kiStride;
        int32_t kiL5 = kiL4 + kiStride;
        int32_t kiL6 = kiL5 + kiStride;
        int32_t kiL7 = kiL6 + kiStride;
        /*caculate the kMean value*/
        uint8_t kuiMUP = (uint8_t)((pPred[-1] + pPred[kiL1] + pPred[kiL2] + pPred[kiL3] + 2) >> 2);
        uint8_t kuiMDown = (uint8_t)((pPred[kiL4] + pPred[kiL5] + pPred[kiL6] + pPred[kiL7] + 2) >> 2);
        uint64_t kuiUP64 = 0x0101010101010101UL * kuiMUP;
        uint64_t kuiDN64 = 0x0101010101010101UL * kuiMDown;

        ST64A8(pPred, kuiUP64);
        ST64A8(pPred + kiL1 + 1, kuiUP64);
        ST64A8(pPred + kiL2 + 1, kuiUP64);
        ST64A8(pPred + kiL3 + 1, kuiUP64);
        ST64A8(pPred + kiL4 + 1, kuiDN64);
        ST64A8(pPred + kiL5 + 1, kuiDN64);
        ST64A8(pPred + kiL6 + 1, kuiDN64);
        ST64A8(pPred + kiL7 + 1, kuiDN64);
    }

    public static void WelsIChromaPredDcTop_c(uint8_t* pPred, int32_t kiStride)
    {
        int32_t iTmp = (kiStride << 3) - kiStride;
        /*caculate the kMean value*/
        uint8_t kuiM1 = (uint8_t)((pPred[-kiStride] + pPred[1 - kiStride] + pPred[2 - kiStride] + pPred[3 - kiStride] + 2) >> 2);
        uint8_t kuiM2 = (uint8_t)((pPred[4 - kiStride] + pPred[5 - kiStride] + pPred[6 - kiStride] + pPred[7 - kiStride] + 2) >>
                                  2);
        uint8_t* kuiM = stackalloc uint8_t[8] { kuiM1, kuiM1, kuiM1, kuiM1, kuiM2, kuiM2, kuiM2, kuiM2 };

        uint8_t i = 7;

        do
        {
            ST64A8(pPred + iTmp, LD64(kuiM));

            iTmp -= kiStride;
        } while (i-- > 0);
    }

    public static void WelsIChromaPredDcNA_c(uint8_t* pPred, int32_t kiStride)
    {
        int32_t iTmp = (kiStride << 3) - kiStride;
        const uint64_t kuiDC64 = 0x8080808080808080UL;
        uint8_t i = 7;

        do
        {
            ST64A8(pPred + iTmp, kuiDC64);

            iTmp -= kiStride;
        } while (i-- > 0);
    }

    public static void WelsI16x16LumaPredV_c(uint8_t* pPred, int32_t kiStride)
    {
        int32_t iTmp = (kiStride << 4) - kiStride;
        uint64_t kuiTop1 = LD64A8(pPred - kiStride);
        uint64_t kuiTop2 = LD64A8(pPred - kiStride + 8);
        uint8_t i = 15;

        do
        {
            ST64A8(pPred + iTmp, kuiTop1);
            ST64A8(pPred + iTmp + 8, kuiTop2);

            iTmp -= kiStride;
        } while (i-- > 0);
    }

    public static void WelsI16x16LumaPredH_c(uint8_t* pPred, int32_t kiStride)
    {
        int32_t iTmp = (kiStride << 4) - kiStride;
        uint8_t i = 15;

        do
        {
            uint8_t kuiVal8 = pPred[iTmp - 1];
            uint64_t kuiVal64 = 0x0101010101010101UL * kuiVal8;

            ST64A8(pPred + iTmp, kuiVal64);
            ST64A8(pPred + iTmp + 8, kuiVal64);

            iTmp -= kiStride;
        } while (i-- > 0);
    }

    public static void WelsI16x16LumaPredPlane_c(uint8_t* pPred, int32_t kiStride)
    {
        int32_t a = 0, b = 0, c = 0, H = 0, V = 0;
        int32_t i, j;
        uint8_t* pTop = &pPred[-kiStride];
        uint8_t* pLeft = &pPred[-1];

        for (i = 0; i < 8; i++)
        {
            H += (i + 1) * (pTop[8 + i] - pTop[6 - i]);
            V += (i + 1) * (pLeft[(8 + i) * kiStride] - pLeft[(6 - i) * kiStride]);
        }

        a = (pLeft[15 * kiStride] + pTop[15]) << 4;
        b = (5 * H + 32) >> 6;
        c = (5 * V + 32) >> 6;

        for (i = 0; i < 16; i++)
        {
            for (j = 0; j < 16; j++)
            {
                int32_t iTmp = (a + b * (j - 7) + c * (i - 7) + 16) >> 5;
                iTmp = WelsClip1(iTmp);
                pPred[j] = (uint8_t)iTmp;
            }
            pPred += kiStride;
        }
    }

    public static void WelsI16x16LumaPredDc_c(uint8_t* pPred, int32_t kiStride)
    {
        int32_t iTmp = (kiStride << 4) - kiStride;
        int32_t iSum = 0;
        uint8_t i = 15;
        uint8_t uiMean = 0;

        /*caculate the kMean value*/
        do
        {
            iSum += pPred[-1 + iTmp] + pPred[-kiStride + i];
            iTmp -= kiStride;
        } while (i-- > 0);
        uiMean = (uint8_t)((16 + iSum) >> 5);

        iTmp = (kiStride << 4) - kiStride;
        i = 15;
        do
        {
            memset(&pPred[iTmp], uiMean, I16x16_COUNT);
            iTmp -= kiStride;
        } while (i-- > 0);
    }


    public static void WelsI16x16LumaPredDcTop_c(uint8_t* pPred, int32_t kiStride)
    {
        int32_t iTmp = (kiStride << 4) - kiStride;
        int32_t iSum = 0;
        uint8_t i = 15;
        uint8_t uiMean = 0;

        /*caculate the kMean value*/
        do
        {
            iSum += pPred[-kiStride + i];
        } while (i-- > 0);
        uiMean = (uint8_t)((8 + iSum) >> 4);

        i = 15;
        do
        {
            memset(&pPred[iTmp], uiMean, I16x16_COUNT);
            iTmp -= kiStride;
        } while (i-- > 0);
    }

    public static void WelsI16x16LumaPredDcLeft_c(uint8_t* pPred, int32_t kiStride)
    {
        int32_t iTmp = (kiStride << 4) - kiStride;
        int32_t iSum = 0;
        uint64_t uiMean64 = 0;
        uint8_t uiMean = 0;
        uint8_t i = 15;

        /*caculate the kMean value*/
        do
        {
            iSum += pPred[-1 + iTmp];
            iTmp -= kiStride;
        } while (i-- > 0);
        uiMean = (uint8_t)((8 + iSum) >> 4);
        uiMean64 = 0x0101010101010101UL * uiMean;

        iTmp = (kiStride << 4) - kiStride;
        i = 15;
        do
        {
            ST64A8(pPred + iTmp, uiMean64);
            ST64A8(pPred + iTmp + 8, uiMean64);

            iTmp -= kiStride;
        } while (i-- > 0);
    }

    public static void WelsI16x16LumaPredDcNA_c(uint8_t* pPred, int32_t kiStride)
    {
        const uint64_t kuiDC64 = 0x8080808080808080UL;
        int32_t iTmp = (kiStride << 4) - kiStride;
        uint8_t i = 15;

        do
        {
            ST64A8(pPred + iTmp, kuiDC64);
            ST64A8(pPred + iTmp + 8, kuiDC64);

            iTmp -= kiStride;
        } while (i-- > 0);
    }
}
