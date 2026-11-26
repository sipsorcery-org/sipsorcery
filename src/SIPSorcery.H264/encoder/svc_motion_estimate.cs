//-----------------------------------------------------------------------------
// Filename: svc_motion_estimate.cs
//
// Description: Port of codec/encoder/core/inc/svc_motion_estimate.h, part 1
// (E4): the motion vector range constants, the ME method flags and the
// SWelsME block that SWelsMD embeds. The motion search functions follow with
// P slices in E5.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 26 Nov 2026  Claude          Created (E4).
// 26 Nov 2026  Claude          FME_DEFAULT_FEATURE_INDEX (E4b).
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

using System.Runtime.InteropServices;

namespace SIPSorcery.H264.Encoder;

internal static partial class WelsEnc
{
    public const int CAMERA_STARTMV_RANGE = 64;
    public const int ITERATIVE_TIMES = 16;
    public const int CAMERA_MV_RANGE = CAMERA_STARTMV_RANGE + ITERATIVE_TIMES;
    public const int CAMERA_MVD_RANGE = (CAMERA_MV_RANGE + 1) << 1; //mvd=mv_range*2;
    public const int BASE_MV_MB_NMB = (2 * CAMERA_MV_RANGE / MB_WIDTH_LUMA) - 1;
    public const int CAMERA_HIGHLAYER_MVD_RANGE = 243;//mvd range;
    public const int EXPANDED_MV_RANGE = 504; //=512-8 rather than 511 to sacrifice same edge point but save complexity in assemblys
    public const int EXPANDED_MVD_RANGE = (504 + 1) << 1;

    public const int ME_DIA = 0x01;     // LITTLE DIAMOND= 0x01
    public const int ME_CROSS = 0x02;   // CROSS=  0x02
    public const int ME_FME = 0x04;     // FME = 0x04
    public const int ME_FULL = 0x10;    // FULL
    // derived ME methods combination
    public const int ME_DIA_CROSS = ME_DIA | ME_CROSS;              // DIA+CROSS
    public const int ME_DIA_CROSS_FME = ME_DIA_CROSS | ME_FME;      // DIA+CROSS+FME

    // #define FME_DEFAULT_FEATURE_INDEX (0), from the feature search basics (E4b, for InitDqLayers).
    public const int FME_DEFAULT_FEATURE_INDEX = 0;
}

/// <summary>union SadPredISatdUnit.</summary>
[StructLayout(LayoutKind.Explicit)]
internal struct SadPredISatdUnit
{
    [FieldOffset(0)] public uint32_t uiSadPred;
    [FieldOffset(0)] public uint32_t uiSatd;    //reuse the sad_pred as a temp satd pData
}

/// <summary>TagWelsME.</summary>
internal unsafe struct SWelsME
{
    /* input */
    public uint16_t* pMvdCost;
    public SadPredISatdUnit uSadPredISatd; //reuse the sad_pred as a temp pData
    public uint32_t uiSadCost;  //used by ME and RC //max SAD should be max_delta*size+lambda*mvdsize = 255*256+91*33*2 = 65280 + 6006 = 71286 > (2^16)-1 = 65535
    public uint32_t uiSatdCost; /* satd + lm * nbits */
    public uint32_t uiSadCostThreshold;
    public int32_t iCurMeBlockPixX;
    public int32_t iCurMeBlockPixY;
    public uint8_t uiBlockSize;   /* BLOCK_WxH */
    public uint8_t uiReserved;

    public uint8_t* pEncMb;
    public uint8_t* pRefMb;
    public uint8_t* pColoRefMb;

    public SMVUnitXY sMvp;
    public SMVUnitXY sMvBase;
    public SMVUnitXY sDirectionalMv;

    public SScreenBlockFeatureStorage* pRefFeatureStorage;

    /* output */
    public SMVUnitXY sMv;
}
