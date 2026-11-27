//-----------------------------------------------------------------------------
// Filename: wels_common_basis.cs
//
// Description: Port of codec/encoder/core/inc/wels_common_basis.h: the
// encoder's motion vector, parameter set offset and crop structures and the
// MB position constants.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// SParaSetOffset has no eSpsPpsIdStrategy field; upstream only has it in
// _DEBUG builds and the reference build is Release.
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

/* MB Type & Sub-MB Type */
global using Mb_Type = System.UInt32;
global using WelsErrorType = System.Int32;

namespace SIPSorcery.H264.Encoder;

/// <summary>SMVUnitXY, each 4 Bytes.</summary>
internal struct SMVUnitXY
{
    public int16_t iMvX;
    public int16_t iMvY;

    public SMVUnitXY sDeltaMv(in SMVUnitXY _v0, in SMVUnitXY _v1)
    {
        iMvX = (int16_t)(_v0.iMvX - _v1.iMvX);
        iMvY = (int16_t)(_v0.iMvY - _v1.iMvY);
        return this;
    }

    public SMVUnitXY sAssignMv(in SMVUnitXY _v0)
    {
        iMvX = _v0.iMvX;
        iMvY = _v0.iMvY;
        return this;
    }
}

/// <summary>TagMVComponentUnit (PMVComponentUnit is SMVComponentUnit*), each LIST_0/LIST_1.</summary>
internal unsafe struct SMVComponentUnit
{
    public InlineArray29<SMVUnitXY> sMotionVectorCache; // Luma only: 5 x 6 - 1 = 29 D-Words
    public fixed sbyte iRefIndexCache[5 * 6];           // Luma only: 5 x 6 = 30 bytes
}

/// <summary>TagParaSetOffsetVariable.</summary>
internal unsafe struct SParaSetOffsetVariable
{
    public fixed int iParaSetIdDelta[WelsEnc.MAX_DQ_LAYER_NUM/*+1*/];//mark delta between SPS_ID_in_bs and sps_id_in_encoder, can be minus, for each dq-layer
    //need not extra +1 due no MGS and FMO case so far
    public fixed bool bUsedParaSetIdInBs[WelsEnc.MAX_PPS_COUNT];      //mark the used SPS_ID with 1
    public uint32_t uiNextParaSetIdToUseInBs;               //mark the next SPS_ID_in_bs, for all layers
}

/// <summary>TagParaSetOffset.</summary>
internal unsafe struct SParaSetOffset
{
    //in PS0 design, "sParaSetOffsetVariable" record the previous paras before current IDR, AND NEED to be stacked and recover across IDR
    public InlineArray3<SParaSetOffsetVariable>
    sParaSetOffsetVariable; //PARA_SET_TYPE=3; paraset_type = 0: AVC_SPS; =1: Subset_SPS; =2: PPS
    //in PSO design, "bPpsIdMappingIntoSubsetsps" uses the current para of current IDR period
    public fixed bool
    bPpsIdMappingIntoSubsetsps[WelsEnc.MAX_DQ_LAYER_NUM/*+1*/];   // need not extra +1 due no MGS and FMO case so far

    public InlineArray4<InlineArray57<int32_t>> iPpsIdList; //index0: max pps types; index1: for differnt IDRs, if only index0=1, index1 can reach MAX_PPS_COUNT

    // #if _DEBUG int32_t eSpsPpsIdStrategy; #endif -- not in the Release build.

    public uint32_t uiNeededSpsNum;
    public uint32_t uiNeededSubsetSpsNum;
    public uint32_t uiNeededPpsNum;

    public uint32_t uiInUseSpsNum;
    public uint32_t uiInUseSubsetSpsNum;
    public uint32_t uiInUsePpsNum;
}

/* Position Offset structure */
/// <summary>TagCropOffset.</summary>
internal struct SCropOffset
{
    public int16_t iCropLeft;
    public int16_t iCropRight;
    public int16_t iCropTop;
    public int16_t iCropBottom;
}

/* Transform Type */
internal enum ETransType
{
    T_4x4 = 0,
    T_8x8 = 1,
    T_16x16 = 2,
    T_PCM = 3
}

internal static partial class WelsEnc
{
    // EMbPosition
    public const int LEFT_MB_POS = 0x01;        // A
    public const int TOP_MB_POS = 0x02;         // B
    public const int TOPRIGHT_MB_POS = 0x04;    // C
    public const int TOPLEFT_MB_POS = 0x08;     // D,
    public const int RIGHT_MB_POS = 0x10;       //  add followed four case to reuse when intra up-sample
    public const int BOTTOM_MB_POS = 0x20;      //
    public const int BOTTOMRIGHT_MB_POS = 0x40; //
    public const int BOTTOMLEFT_MB_POS = 0x80;  //
    public const int MB_POS_A = 0x100;

    public const int MB_LEFT_BIT = 0;// add to use in intra up-sample
    public const int MB_TOP_BIT = 1;
    public const int MB_TOPRIGHT_BIT = 2;
    public const int MB_TOPLEFT_BIT = 3;
    public const int MB_RIGHT_BIT = 4;
    public const int MB_BOTTOM_BIT = 5;
    public const int MB_BTMRIGHT_BIT = 6;
    public const int MB_BTMLEFT_BIT = 7;

    public const int MB_TYPE_BACKGROUND = 0x00010000;  // conditional BG skip_mb

    public const int Intra4x4 = 0;
    public const int Intra16x16 = 1;
    public const int Inter16x16 = 2;
    public const int Inter16x8 = 3;
    public const int Inter8x16 = 4;
    public const int Inter8x8 = 5;
    public const int PSkip = 6;
}
