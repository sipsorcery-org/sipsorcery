//-----------------------------------------------------------------------------
// Filename: wels_common_basis.cs
//
// Description: Port of codec/decoder/core/inc/wels_common_basis.h: decoder
// basic types (SPosOffset, ProfileIdc, MbType) and the small const tables the
// header defines (zig-zag scans, prediction mode info, macroblock partition
// info, VUI sample aspect ratios).
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// The tables the header only declares extern (g_kuiScan8 and the other scan
// index tables) are defined in decoder_data_tables.cs. The SHIFT_BUFFER and
// POP_BUFFER macros work on the CAVLC bit cache and live in
// parse_mb_syn_cavlc.cs.
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

/* Profile IDC */
global using ProfileIdc = System.Byte;
/* MB Type & Sub-MB Type */
global using MbType = System.UInt32;
global using SubMbType = System.UInt32;

namespace SIPSorcery.H264.Decoder;

/* Position Offset structure */
/// <summary>TagPosOffset.</summary>
internal struct SPosOffset
{
    public int32_t iLeftOffset;
    public int32_t iTopOffset;
    public int32_t iRightOffset;
    public int32_t iBottomOffset;
}

/// <summary>TagI16PredInfo.</summary>
internal struct SI16PredInfo
{
    public int8_t iPredMode;
    public int8_t iLeftAvail;
    public int8_t iTopAvail;
    public int8_t iLeftTopAvail;
}

/// <summary>TagI4PredInfo.</summary>
internal struct SI4PredInfo
{
    public int8_t iPredMode;
    public int8_t iLeftAvail;
    public int8_t iTopAvail;
    public int8_t iLeftTopAvail;
    // int8_t right_top_avail; //when right_top unavailable but top avail, we can pad the right-top with the rightmost pixel of top
}

/// <summary>TagPartMbInfo.</summary>
internal struct SPartMbInfo
{
    public MbType iType;
    public int8_t iPartCount; //P_16*16, P_16*8, P_8*16, P_8*8 based on 8*8 block; P_8*4, P_4*8, P_4*4 based on 4*4 block
    public int8_t iPartWidth; //based on 4*4 block

    // Port addition: C's aggregate initializer, for the tables below.
    public SPartMbInfo(int iType, int8_t iPartCount, int8_t iPartWidth)
    {
        this.iType = (MbType)iType;
        this.iPartCount = iPartCount;
        this.iPartWidth = iPartWidth;
    }
}

/// <summary>TagSar.</summary>
internal struct sSar
{
    public uint32_t uiWidth;
    public uint32_t uiHeight;
}

internal static unsafe partial class WelsDec
{
    public const int I16_LUMA_DC = 1;
    public const int I16_LUMA_AC = 2;
    public const int LUMA_DC_AC = 3;
    public const int CHROMA_DC = 4;
    public const int CHROMA_AC = 5;
    public const int LUMA_DC_AC_8 = 6;
    public const int CHROMA_DC_U = 7;
    public const int CHROMA_DC_V = 8;
    public const int CHROMA_AC_U = 9;
    public const int CHROMA_AC_V = 10;
    public const int LUMA_DC_AC_INTRA = 11;
    public const int LUMA_DC_AC_INTER = 12;
    public const int CHROMA_DC_U_INTER = 13;
    public const int CHROMA_DC_V_INTER = 14;
    public const int CHROMA_AC_U_INTER = 15;
    public const int CHROMA_AC_V_INTER = 16;
    public const int LUMA_DC_AC_INTRA_8 = 17;
    public const int LUMA_DC_AC_INTER_8 = 18;

    public static readonly uint8_t* g_kuiZigzagScan = StaticTable.Of<uint8_t>(16, [ //4*4block residual zig-zag scan order
        0,  1,  4,  8,
        5,  2,  3,  6,
        9, 12, 13, 10,
        7, 11, 14, 15,
    ]);

    public static readonly uint8_t* g_kuiZigzagScan8x8 = StaticTable.Of<uint8_t>(64, [ //8x8 block residual zig-zag scan order
        0,  1,  8,  16, 9,  2,  3,  10,
        17, 24, 32, 25, 18, 11, 4,  5,
        12, 19, 26, 33, 40, 48, 41, 34,
        27, 20, 13, 6,  7,  14, 21, 28,
        35, 42, 49, 56, 57, 50, 43, 36,
        29, 22, 15, 23, 30, 37, 44, 51,
        58, 59, 52, 45, 38, 31, 39, 46,
        53, 60, 61, 54, 47, 55, 62, 63,
    ]);

    public static readonly uint8_t* g_kuiIdx2CtxSignificantCoeffFlag8x8 = StaticTable.Of<uint8_t>(64, [  // Table 9-43, Page 289
        0,  1,  2,  3,  4,  5,  5,  4,
        4,  3,  3,  4,  4,  4,  5,  5,
        4,  4,  4,  4,  3,  3,  6,  7,
        7,  7,  8,  9, 10,  9,  8,  7,
        7,  6, 11, 12, 13, 11,  6,  7,
        8,  9, 14, 10,  9,  8,  6, 11,
        12, 13, 11, 6,  9, 14, 10,  9,
        11, 12, 13, 11, 14, 10, 12, 14,
    ]);

    public static readonly uint8_t* g_kuiIdx2CtxLastSignificantCoeffFlag8x8 = StaticTable.Of<uint8_t>(64, [ // Table 9-43, Page 289
        0,  1,  1,  1,  1,  1,  1,  1,
        1,  1,  1,  1,  1,  1,  1,  1,
        2,  2,  2,  2,  2,  2,  2,  2,
        2,  2,  2,  2,  2,  2,  2,  2,
        3,  3,  3,  3,  3,  3,  3,  3,
        4,  4,  4,  4,  4,  4,  4,  4,
        5,  5,  5,  5,  6,  6,  6,  6,
        7,  7,  7,  7,  8,  8,  8,  8,
    ]);

    public static void GetMbResProperty(int32_t* pMBproperty, int32_t* pResidualProperty, bool bCavlc)
    {
        switch (*pResidualProperty)
        {
            case CHROMA_AC_U:
                *pMBproperty = 1;
                *pResidualProperty = bCavlc ? CHROMA_AC : CHROMA_AC_U;
                break;
            case CHROMA_AC_V:
                *pMBproperty = 2;
                *pResidualProperty = bCavlc ? CHROMA_AC : CHROMA_AC_V;
                break;
            case LUMA_DC_AC_INTRA:
                *pMBproperty = 0;
                *pResidualProperty = LUMA_DC_AC;
                break;
            case CHROMA_DC_U:
                *pMBproperty = 1;
                *pResidualProperty = bCavlc ? CHROMA_DC : CHROMA_DC_U;
                break;
            case CHROMA_DC_V:
                *pMBproperty = 2;
                *pResidualProperty = bCavlc ? CHROMA_DC : CHROMA_DC_V;
                break;
            case I16_LUMA_AC:
                *pMBproperty = 0;
                break;
            case I16_LUMA_DC:
                *pMBproperty = 0;
                break;
            case LUMA_DC_AC_INTER:
                *pMBproperty = 3;
                *pResidualProperty = LUMA_DC_AC;
                break;
            case CHROMA_DC_U_INTER:
                *pMBproperty = 4;
                *pResidualProperty = bCavlc ? CHROMA_DC : CHROMA_DC_U;
                break;
            case CHROMA_DC_V_INTER:
                *pMBproperty = 5;
                *pResidualProperty = bCavlc ? CHROMA_DC : CHROMA_DC_V;
                break;
            case CHROMA_AC_U_INTER:
                *pMBproperty = 4;
                *pResidualProperty = bCavlc ? CHROMA_AC : CHROMA_AC_U;
                break;
            case CHROMA_AC_V_INTER:
                *pMBproperty = 5;
                *pResidualProperty = bCavlc ? CHROMA_AC : CHROMA_AC_V;
                break;
            // Reference to Table 7-2
            case LUMA_DC_AC_INTRA_8:
                *pMBproperty = 6;
                *pResidualProperty = LUMA_DC_AC_8;
                break;
            case LUMA_DC_AC_INTER_8:
                *pMBproperty = 7;
                *pResidualProperty = LUMA_DC_AC_8;
                break;
        }
    }

    public static readonly SI16PredInfo* g_ksI16PredInfo = StaticTable.Of<SI16PredInfo, int8_t>(4, [
        I16_PRED_V, 0, 1, 0,
        I16_PRED_H, 1, 0, 0,
                 0, 0, 0, 0,
        I16_PRED_P, 1, 1, 1,
    ]);

    public static readonly SI16PredInfo* g_ksChromaPredInfo = StaticTable.Of<SI16PredInfo, int8_t>(4, [
               0, 0, 0, 0,
        C_PRED_H, 1, 0, 0,
        C_PRED_V, 0, 1, 0,
        C_PRED_P, 1, 1, 1,
    ]);

    public static readonly SI4PredInfo* g_ksI4PredInfo = StaticTable.Of<SI4PredInfo, int8_t>(9, [
          I4_PRED_V, 0, 1, 0,
          I4_PRED_H, 1, 0, 0,
                  0, 0, 0, 0,
        I4_PRED_DDL, 0, 1, 0,
        I4_PRED_DDR, 1, 1, 1,
         I4_PRED_VR, 1, 1, 1,
         I4_PRED_HD, 1, 1, 1,
         I4_PRED_VL, 0, 1, 0,
         I4_PRED_HU, 1, 0, 0,
    ]);

    public static readonly uint8_t* g_kuiI16CbpTable = StaticTable.Of<uint8_t>(6, [0, 16, 32, 15, 31, 47]);

    //Table 7.13. Macroblock type values 0 to 4 for P slices.
    public static readonly SPartMbInfo* g_ksInterPMbTypeInfo = StaticTable.Of<SPartMbInfo>(5, [
        new(MB_TYPE_16x16,    1, 4),
        new(MB_TYPE_16x8,     2, 4),
        new(MB_TYPE_8x16,     2, 2),
        new(MB_TYPE_8x8,      4, 4),
        new(MB_TYPE_8x8_REF0, 4, 4), //ref0--ref_idx not present in bit-stream and default as 0
    ]);

    //Table 7.14. Macroblock type values 0 to 22 for B slices.
    public static readonly SPartMbInfo* g_ksInterBMbTypeInfo = StaticTable.Of<SPartMbInfo>(23, [
        //            Part 0        Part 1
        new(MB_TYPE_DIRECT, 1, 4), //B_Direct_16x16
        new(MB_TYPE_16x16 | MB_TYPE_P0L0, 1, 4), //B_L0_16x16
        new(MB_TYPE_16x16 | MB_TYPE_P0L1, 1, 4), //B_L1_16x16
        new(MB_TYPE_16x16 | MB_TYPE_P0L0 | MB_TYPE_P0L1, 1, 4),  //B_Bi_16x16
        new(MB_TYPE_16x8  | MB_TYPE_P0L0 | MB_TYPE_P1L0, 2, 4),    //B_L0_L0_16x8
        new(MB_TYPE_8x16  | MB_TYPE_P0L0 | MB_TYPE_P1L0, 2, 2),    //B_L0_L0_8x16
        new(MB_TYPE_16x8  | MB_TYPE_P0L1 | MB_TYPE_P1L1, 2, 4),    //B_L1_L1_16x8
        new(MB_TYPE_8x16  | MB_TYPE_P0L1 | MB_TYPE_P1L1, 2, 2),    //B_L1_L1_8x16
        new(MB_TYPE_16x8  | MB_TYPE_P0L0 | MB_TYPE_P1L1, 2, 4),    //B_L0_L1_16x8
        new(MB_TYPE_8x16  | MB_TYPE_P0L0 | MB_TYPE_P1L1, 2, 2),    //B_L0_L1_8x16
        new(MB_TYPE_16x8  | MB_TYPE_P0L1 | MB_TYPE_P1L0, 2, 4),    //B_L1_L0_16x8
        new(MB_TYPE_8x16  | MB_TYPE_P0L1 | MB_TYPE_P1L0, 2, 2),    //B_L1_L0_8x16
        new(MB_TYPE_16x8  | MB_TYPE_P0L0 | MB_TYPE_P1L0 | MB_TYPE_P1L1, 2, 4),   //B_L0_Bi_16x8
        new(MB_TYPE_8x16  | MB_TYPE_P0L0 | MB_TYPE_P1L0 | MB_TYPE_P1L1, 2, 2),   //B_L0_Bi_8x16
        new(MB_TYPE_16x8  | MB_TYPE_P0L1 | MB_TYPE_P1L0 | MB_TYPE_P1L1, 2, 4),   //B_L1_Bi_16x8
        new(MB_TYPE_8x16  | MB_TYPE_P0L1 | MB_TYPE_P1L0 | MB_TYPE_P1L1, 2, 2),   //B_L1_Bi_8x16
        new(MB_TYPE_16x8  | MB_TYPE_P0L0 | MB_TYPE_P0L1 | MB_TYPE_P1L0, 2, 4),   //B_Bi_L0_16x8
        new(MB_TYPE_8x16  | MB_TYPE_P0L0 | MB_TYPE_P0L1 | MB_TYPE_P1L0, 2, 2),   //B_Bi_L0_8x16
        new(MB_TYPE_16x8  | MB_TYPE_P0L0 | MB_TYPE_P0L1 | MB_TYPE_P1L1, 2, 4),   //B_Bi_L1_16x8
        new(MB_TYPE_8x16  | MB_TYPE_P0L0 | MB_TYPE_P0L1 | MB_TYPE_P1L1, 2, 2),   //B_Bi_L1_8x16
        new(MB_TYPE_16x8  | MB_TYPE_P0L0 | MB_TYPE_P0L1 | MB_TYPE_P1L0 | MB_TYPE_P1L1, 2, 4),    //B_Bi_Bi_16x8
        new(MB_TYPE_8x16  | MB_TYPE_P0L0 | MB_TYPE_P0L1 | MB_TYPE_P1L0 | MB_TYPE_P1L1, 2, 2),    //B_Bi_Bi_8x16
        new(MB_TYPE_8x8   | MB_TYPE_P0L0 | MB_TYPE_P0L1 | MB_TYPE_P1L0 | MB_TYPE_P1L1,  4, 4)    //B_8x8
    ]);

    //Table 7.17 Sub-macroblock types in B macroblocks.
    public static readonly SPartMbInfo* g_ksInterPSubMbTypeInfo = StaticTable.Of<SPartMbInfo>(4, [
        new(SUB_MB_TYPE_8x8, 1, 2),
        new(SUB_MB_TYPE_8x4, 2, 2),
        new(SUB_MB_TYPE_4x8, 2, 1),
        new(SUB_MB_TYPE_4x4, 4, 1),
    ]);

    //Table 7.18 Sub-macroblock types in B macroblocks.
    public static readonly SPartMbInfo* g_ksInterBSubMbTypeInfo = StaticTable.Of<SPartMbInfo>(13, [
        new(MB_TYPE_DIRECT,                               1, 2), //B_Direct_8x8
        new(SUB_MB_TYPE_8x8 | MB_TYPE_P0L0,                 1, 2), //B_L0_8x8
        new(SUB_MB_TYPE_8x8 | MB_TYPE_P0L1,                 1, 2), //B_L1_8x8
        new(SUB_MB_TYPE_8x8 | MB_TYPE_P0L0 | MB_TYPE_P0L1,  1, 2), //B_Bi_8x8
        new(SUB_MB_TYPE_8x4 | MB_TYPE_P0L0,                 2, 2), //B_L0_8x4
        new(SUB_MB_TYPE_4x8 | MB_TYPE_P0L0,                 2, 1), //B_L0_4x8
        new(SUB_MB_TYPE_8x4 | MB_TYPE_P0L1,                 2, 2), //B_L1_8x4
        new(SUB_MB_TYPE_4x8 | MB_TYPE_P0L1,                 2, 1), //B_L1_4x8
        new(SUB_MB_TYPE_8x4 | MB_TYPE_P0L0 | MB_TYPE_P0L1,  2, 2), //B_Bi_8x4
        new(SUB_MB_TYPE_4x8 | MB_TYPE_P0L0 | MB_TYPE_P0L1,  2, 1), //B_Bi_4x8
        new(SUB_MB_TYPE_4x4 | MB_TYPE_P0L0,                 4, 1), //B_L0_4x4
        new(SUB_MB_TYPE_4x4 | MB_TYPE_P0L1,                 4, 1), //B_L1_4x4
        new(SUB_MB_TYPE_4x4 | MB_TYPE_P0L0 | MB_TYPE_P0L1,  4, 1)  //B_Bi_4x4
    ]);

    public static readonly sSar* g_ksVuiSampleAspectRatio = StaticTable.Of<sSar, uint32_t>(17, [ //Table E-1
         0,  0,   1,  1,  12, 11,   10, 11,  16, 11, //0~4
        40, 33,  24, 11,  20, 11,   32, 11,  80, 33, //5~9
        18, 11,  15, 11,  64, 33,  160, 99,   4,  3, //10~14
         3,  2,   2,  1                              //15~16
    ]);
}
