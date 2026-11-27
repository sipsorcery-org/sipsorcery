//-----------------------------------------------------------------------------
// Filename: wels_common_defs.cs
//
// Description: Port of codec/common/inc/wels_common_defs.h.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// The table declarations (extern const ...) are defined in common_tables.cs.
// The IS_* type-test macros return bool; upstream's few `IS_DIR (...) > 0`
// call sites drop the comparison.
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

namespace SIPSorcery.H264;

/// <summary>TagLevelLimits.</summary>
internal struct SLevelLimits
{
    public ELevelIdc uiLevelIdc;  // level idc
    public uint32_t uiMaxMBPS;    // Max macroblock processing rate(MB/s)
    public uint32_t uiMaxFS;      // Max frame sizea(MBs)
    public uint32_t uiMaxDPBMbs;  // Max decoded picture buffer size(MBs)
    public uint32_t uiMaxBR;      // Max video bit rate
    public uint32_t uiMaxCPB;     // Max CPB size
    public int16_t iMinVmv;       // Vertical MV component range upper bound
    public int16_t iMaxVmv;       // Vertical MV component range lower bound
    public uint16_t uiMinCR;      // Min compression ration
    public int16_t iMaxMvsPer2Mb; // Max number of motion vectors per two consecutive MBs

    // Port addition: C's aggregate initializer, for g_ksLevelLimits.
    public SLevelLimits(ELevelIdc uiLevelIdc, uint32_t uiMaxMBPS, uint32_t uiMaxFS, uint32_t uiMaxDPBMbs, uint32_t uiMaxBR,
        uint32_t uiMaxCPB, int16_t iMinVmv, int16_t iMaxVmv, uint16_t uiMinCR, int16_t iMaxMvsPer2Mb)
    {
        this.uiLevelIdc = uiLevelIdc;
        this.uiMaxMBPS = uiMaxMBPS;
        this.uiMaxFS = uiMaxFS;
        this.uiMaxDPBMbs = uiMaxDPBMbs;
        this.uiMaxBR = uiMaxBR;
        this.uiMaxCPB = uiMaxCPB;
        this.iMinVmv = iMinVmv;
        this.iMaxVmv = iMaxVmv;
        this.uiMinCR = uiMinCR;
        this.iMaxMvsPer2Mb = iMaxMvsPer2Mb;
    }
}

/*
 *  NAL Unit Type (5 Bits)
 */
internal enum EWelsNalUnitType
{
    NAL_UNIT_UNSPEC_0 = 0,
    NAL_UNIT_CODED_SLICE = 1,
    NAL_UNIT_CODED_SLICE_DPA = 2,
    NAL_UNIT_CODED_SLICE_DPB = 3,
    NAL_UNIT_CODED_SLICE_DPC = 4,
    NAL_UNIT_CODED_SLICE_IDR = 5,
    NAL_UNIT_SEI = 6,
    NAL_UNIT_SPS = 7,
    NAL_UNIT_PPS = 8,
    NAL_UNIT_AU_DELIMITER = 9,
    NAL_UNIT_END_OF_SEQ = 10,
    NAL_UNIT_END_OF_STR = 11,
    NAL_UNIT_FILLER_DATA = 12,
    NAL_UNIT_SPS_EXT = 13,
    NAL_UNIT_PREFIX = 14,
    NAL_UNIT_SUBSET_SPS = 15,
    NAL_UNIT_DEPTH_PARAM = 16, // NAL_UNIT_RESV_16
    NAL_UNIT_RESV_17 = 17,
    NAL_UNIT_RESV_18 = 18,
    NAL_UNIT_AUX_CODED_SLICE = 19,
    NAL_UNIT_CODED_SLICE_EXT = 20,
    NAL_UNIT_MVC_SLICE_EXT = 21, // NAL_UNIT_RESV_21
    NAL_UNIT_RESV_22 = 22,
    NAL_UNIT_RESV_23 = 23,
    NAL_UNIT_UNSPEC_24 = 24,
    NAL_UNIT_UNSPEC_25 = 25,
    NAL_UNIT_UNSPEC_26 = 26,
    NAL_UNIT_UNSPEC_27 = 27,
    NAL_UNIT_UNSPEC_28 = 28,
    NAL_UNIT_UNSPEC_29 = 29,
    NAL_UNIT_UNSPEC_30 = 30,
    NAL_UNIT_UNSPEC_31 = 31
}

/*
 *  NAL Reference IDC (2 Bits)
 */
internal enum EWelsNalRefIdc
{
    NRI_PRI_LOWEST = 0,
    NRI_PRI_LOW = 1,
    NRI_PRI_HIGH = 2,
    NRI_PRI_HIGHEST = 3
}

/*
 * VCL TYPE
 */
internal enum EVclType
{
    NON_VCL = 0,
    VCL = 1,
    NOT_APP = 2
}

/* Base SSlice Types
 * Invalid in case of eSliceType exceeds 9,
 * Need trim when eSliceType > 4 as fixed SliceType(eSliceType-4),
 * meaning mapped version after eSliceType minus 4.
 */
internal enum EWelsSliceType
{
    P_SLICE = 0,
    B_SLICE = 1,
    I_SLICE = 2,
    SP_SLICE = 3,
    SI_SLICE = 4,
    UNKNOWN_SLICE = 5
}

/* SSlice Types in scalable extension */
internal enum ESliceTypeExt
{
    EP_SLICE = 0, // EP_SLICE: 0, 5
    EB_SLICE = 1, // EB_SLICE: 1, 6
    EI_SLICE = 2  // EI_SLICE: 2, 7
}

/* List Index */
internal enum EListIndex
{
    LIST_0 = 0,
    LIST_1 = 1,
    LIST_A = 2
}

/* Motion Vector components */
internal enum EMvComp
{
    MV_X = 0,
    MV_Y = 1,
    MV_A = 2
}

/* Chroma Components */
internal enum EChromaComp
{
    CHROMA_CB = 0,
    CHROMA_CR = 1,
    CHROMA_A = 2
}

/*
 *  Memory Management Control Operation (MMCO) code
 */
internal enum EMmcoCode
{
    MMCO_END = 0,
    MMCO_SHORT2UNUSED = 1,
    MMCO_LONG2UNUSED = 2,
    MMCO_SHORT2LONG = 3,
    MMCO_SET_MAX_LONG = 4,
    MMCO_RESET = 5,
    MMCO_LONG = 6
}

internal enum EVuiVideoFormat
{
    VUI_COMPONENT = 0,
    VUI_PAL = 1,
    VUI_NTSC = 2,
    VUI_SECAM = 3,
    VUI_MAC = 4,
    VUI_UNSPECIFIED = 5,
    VUI_RESERVED1 = 6,
    VUI_RESERVED2 = 7
}

/*
 *  Bit-stream auxiliary reading / writing
 */
/// <summary>TagBitStringAux (PBitStringAux is SBitStringAux*).</summary>
internal unsafe struct SBitStringAux
{
    public uint8_t* pStartBuf;   // buffer to start position
    public uint8_t* pEndBuf;     // buffer + length
    public int32_t iBits;        // count bits of overall bitstreaming input

    public intX_t iIndex;        //only for cavlc usage
    public uint8_t* pCurBuf;     // current reading position
    public uint32_t uiCurBits;
    public int32_t iLeftBits;    // count number of available bits left ([1, 8]),
    // need pointer to next byte start position in case 0 bit left then 8 instead
}

/* NAL Unix Header in AVC, refer to Page 56 in JVT X201wcm */
/// <summary>TagNalUnitHeader (PNalUnitHeader is SNalUnitHeader*).</summary>
internal struct SNalUnitHeader
{
    public uint8_t uiForbiddenZeroBit;
    public uint8_t uiNalRefIdc;
    public EWelsNalUnitType eNalUnitType;
    public uint8_t uiReservedOneByte;                // only padding usage
}

/* NAL Unit Header in scalable extension syntax, refer to Page 390 in JVT X201wcm */
/// <summary>TagNalUnitHeaderExt (PNalUnitHeaderExt is SNalUnitHeaderExt*).</summary>
internal struct SNalUnitHeaderExt
{
    public SNalUnitHeader sNalUnitHeader;

    // uint8_t   reserved_one_bit;
    public bool bIdrFlag;
    public uint8_t uiPriorityId;
    public int8_t iNoInterLayerPredFlag;      // change as int8_t to support 3 values probably in encoder
    public uint8_t uiDependencyId;

    public uint8_t uiQualityId;
    public uint8_t uiTemporalId;
    public bool bUseRefBasePicFlag;
    public bool bDiscardableFlag;

    public bool bOutputFlag;
    public uint8_t uiReservedThree2Bits;
    // Derived variable(s)
    public uint8_t uiLayerDqId;
    public bool bNalExtFlag;
}

internal static unsafe partial class WelsCommon
{
    /*common use table*/

    public const int CTX_NA = 0;
    public const int WELS_CONTEXT_COUNT = 460;
    public const int LEVEL_NUMBER = 17;

    public const int CpbBrNalFactor = 1200;  //baseline,main,and extended profiles.

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IS_VCL_NAL(EWelsNalUnitType t, int32_t ext_idx) => g_keTypeMap[(int)t][ext_idx] == VCL;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IS_PARAM_SETS_NALS(EWelsNalUnitType t) => t == NAL_UNIT_SPS || t == NAL_UNIT_PPS || t == NAL_UNIT_SUBSET_SPS;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IS_SPS_NAL(EWelsNalUnitType t) => t == NAL_UNIT_SPS;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IS_SUBSET_SPS_NAL(EWelsNalUnitType t) => t == NAL_UNIT_SUBSET_SPS;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IS_PPS_NAL(EWelsNalUnitType t) => t == NAL_UNIT_PPS;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IS_SEI_NAL(EWelsNalUnitType t) => t == NAL_UNIT_SEI;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IS_AU_DELIMITER_NAL(EWelsNalUnitType t) => t == NAL_UNIT_AU_DELIMITER;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IS_PREFIX_NAL(EWelsNalUnitType t) => t == NAL_UNIT_PREFIX;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IS_SUBSET_SPS_USED(EWelsNalUnitType t) => t == NAL_UNIT_SUBSET_SPS || t == NAL_UNIT_CODED_SLICE_EXT;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IS_VCL_NAL_AVC_BASE(EWelsNalUnitType t) => t == NAL_UNIT_CODED_SLICE || t == NAL_UNIT_CODED_SLICE_IDR;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IS_NEW_INTRODUCED_SVC_NAL(EWelsNalUnitType t) => t == NAL_UNIT_PREFIX || t == NAL_UNIT_CODED_SLICE_EXT;

    /* AVC MB types*/
    public const int MB_TYPE_INTRA4x4 = 0x00000001;
    public const int MB_TYPE_INTRA16x16 = 0x00000002;
    public const int MB_TYPE_INTRA8x8 = 0x00000004;
    public const int MB_TYPE_16x16 = 0x00000008;
    public const int MB_TYPE_16x8 = 0x00000010;
    public const int MB_TYPE_8x16 = 0x00000020;
    public const int MB_TYPE_8x8 = 0x00000040;
    public const int MB_TYPE_8x8_REF0 = 0x00000080;
    public const int MB_TYPE_SKIP = 0x00000100;
    public const int MB_TYPE_INTRA_PCM = 0x00000200;
    public const int MB_TYPE_INTRA_BL = 0x00000400;
    public const int MB_TYPE_DIRECT = 0x00000800;
    public const int MB_TYPE_P0L0 = 0x00001000;
    public const int MB_TYPE_P1L0 = 0x00002000;
    public const int MB_TYPE_P0L1 = 0x00004000;
    public const int MB_TYPE_P1L1 = 0x00008000;
    public const int MB_TYPE_L0 = MB_TYPE_P0L0 | MB_TYPE_P1L0;
    public const int MB_TYPE_L1 = MB_TYPE_P0L1 | MB_TYPE_P1L1;

    public const int SUB_MB_TYPE_8x8 = 0x00000001;
    public const int SUB_MB_TYPE_8x4 = 0x00000002;
    public const int SUB_MB_TYPE_4x8 = 0x00000004;
    public const int SUB_MB_TYPE_4x4 = 0x00000008;

    public const int MB_TYPE_INTRA = MB_TYPE_INTRA4x4 | MB_TYPE_INTRA16x16 | MB_TYPE_INTRA8x8 | MB_TYPE_INTRA_PCM;
    public const int MB_TYPE_INTER = MB_TYPE_16x16 | MB_TYPE_16x8 | MB_TYPE_8x16 | MB_TYPE_8x8 | MB_TYPE_8x8_REF0 | MB_TYPE_SKIP | MB_TYPE_DIRECT;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IS_INTRA4x4(uint32_t type) => MB_TYPE_INTRA4x4 == type;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IS_INTRA8x8(uint32_t type) => MB_TYPE_INTRA8x8 == type;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IS_INTRANxN(uint32_t type) => MB_TYPE_INTRA4x4 == type || MB_TYPE_INTRA8x8 == type;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IS_INTRA16x16(uint32_t type) => MB_TYPE_INTRA16x16 == type;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IS_INTRA(uint32_t type) => (type & MB_TYPE_INTRA) != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IS_INTER(uint32_t type) => (type & MB_TYPE_INTER) != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IS_INTER_16x16(uint32_t type) => (type & MB_TYPE_16x16) != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IS_INTER_16x8(uint32_t type) => (type & MB_TYPE_16x8) != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IS_INTER_8x16(uint32_t type) => (type & MB_TYPE_8x16) != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IS_TYPE_L0(uint32_t type) => (type & MB_TYPE_L0) != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IS_TYPE_L1(uint32_t type) => (type & MB_TYPE_L1) != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IS_DIR(uint32_t a, int32_t part, int32_t list) => (a & (uint32_t)(MB_TYPE_P0L0 << (part + 2 * list))) != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IS_SKIP(uint32_t type) => (type & MB_TYPE_SKIP) != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IS_DIRECT(uint32_t type) => (type & MB_TYPE_DIRECT) != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IS_SVC_INTER(uint32_t type) => IS_INTER(type);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IS_I_BL(uint32_t type) => type == MB_TYPE_INTRA_BL;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IS_SVC_INTRA(uint32_t type) => IS_I_BL(type) || IS_INTRA(type);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IS_Inter_8x8(uint32_t type) => (type & MB_TYPE_8x8) != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IS_SUB_8x8(uint32_t sub_type) => (sub_type & SUB_MB_TYPE_8x8) != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IS_SUB_8x4(uint32_t sub_type) => (sub_type & SUB_MB_TYPE_8x4) != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IS_SUB_4x8(uint32_t sub_type) => (sub_type & SUB_MB_TYPE_4x8) != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IS_SUB_4x4(uint32_t sub_type) => (sub_type & SUB_MB_TYPE_4x4) != 0;

    public const int REF_NOT_AVAIL = -2;
    public const int REF_NOT_IN_LIST = -1;  //intra

    /////////intra16x16  Luma
    public const int I16_PRED_INVALID = -1;
    public const int I16_PRED_V = 0;
    public const int I16_PRED_H = 1;
    public const int I16_PRED_DC = 2;
    public const int I16_PRED_P = 3;

    public const int I16_PRED_DC_L = 4;
    public const int I16_PRED_DC_T = 5;
    public const int I16_PRED_DC_128 = 6;
    public const int I16_PRED_DC_A = 7;
    //////////intra4x4   Luma
    // Here, I8x8 also use these definitions
    public const int I4_PRED_INVALID = 0;
    public const int I4_PRED_V = 0;
    public const int I4_PRED_H = 1;
    public const int I4_PRED_DC = 2;
    public const int I4_PRED_DDL = 3; //diagonal_down_left
    public const int I4_PRED_DDR = 4; //diagonal_down_right
    public const int I4_PRED_VR = 5;  //vertical_right
    public const int I4_PRED_HD = 6;  //horizon_down
    public const int I4_PRED_VL = 7;  //vertical_left
    public const int I4_PRED_HU = 8;  //horizon_up

    public const int I4_PRED_DC_L = 9;
    public const int I4_PRED_DC_T = 10;
    public const int I4_PRED_DC_128 = 11;

    public const int I4_PRED_DDL_TOP = 12; //right-top replacing by padding rightmost pixel of top
    public const int I4_PRED_VL_TOP = 13;  //right-top replacing by padding rightmost pixel of top
    public const int I4_PRED_A = 14;

    //////////intra Chroma
    public const int C_PRED_INVALID = -1;
    public const int C_PRED_DC = 0;
    public const int C_PRED_H = 1;
    public const int C_PRED_V = 2;
    public const int C_PRED_P = 3;

    public const int C_PRED_DC_L = 4;
    public const int C_PRED_DC_T = 5;
    public const int C_PRED_DC_128 = 6;
    public const int C_PRED_A = 7;
}
