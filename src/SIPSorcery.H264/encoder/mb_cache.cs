//-----------------------------------------------------------------------------
// Filename: mb_cache.cs
//
// Description: Port of codec/encoder/core/inc/mb_cache.h: the coefficient
// buffers and the per-slice macroblock cache. The two scan tables it declares
// are in encoder_data_tables.cs.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// The anonymous SPicData struct is named SPicDataEntry. The C aligns
// sMvComponents and the two count arrays to 16 bytes for the SIMD code; the C
// path doesn't need it.
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

namespace SIPSorcery.H264.Encoder;

/*
 *  MB Cache information, such one cache should be defined within a slice
 */
/*
 * Cache for Luma               Cache for Chroma(Cb, Cr)
 *
 *  TL T T T T                  TL T T
 *   L - - - -                   L - -
 *   L - - - -                   L - - TR
 *   L - - - -
 *   L - - - - TR
 *
 */

/// <summary>TagDCTCoeff.</summary>
internal unsafe struct SDCTCoeff
{
    //ALIGNED_DECLARE( int16_t, residual_ac[16], 16 ); //I_16x16
    public InlineArray16<InlineArray16<int16_t>> iLumaBlock; // [16][16] based on block4x4 luma DC/AC
    //ALIGNED_DECLARE( int16_t, iLumaI16x16Dc[16], 16 ); //I_16x16 DC
    public fixed short iLumaI16x16Dc[16];
    //ALIGNED_DECLARE( int16_t, iChromaDc[2][4], 16 ); //chroma DC
    public InlineArray8<InlineArray16<int16_t>> iChromaBlock; // [8][16] based on block4x4  chroma DC/AC
    public InlineArray2<InlineArray4<int16_t>> iChromaDc;     // [2][4]
}

/// <summary>TagMbCache's anonymous SPicData struct.</summary>
internal struct SPicDataEntry
{
    /* pointer of current mb location in original frame */
    public PtrArray3<uint8_t> pEncMb;
    /* pointer of current mb location in recovery frame */
    public PtrArray3<uint8_t> pDecMb;
    /* pointer of co-located mb location in reference frame */
    public PtrArray3<uint8_t> pRefMb;
    //for SVC
    public PtrArray3<uint8_t> pCsMb; //locating current mb's CS in whole frame
    //              int16_t *p_rs[3];//locating current mb's RS     in whole frame
}

/// <summary>TagMbCache.</summary>
internal unsafe struct SMbCache
{
    //the followed pData now is promised aligned to 16 bytes
    public SMVComponentUnit sMvComponents;

    public fixed sbyte iNonZeroCoeffCount[48];  // Cache line size
    // int8_t iNonZeroCoeffCount[6 * 8];      // Right luma, Chroma(Left Top Cb, Left btm Cr); must follow by iIntraPredMode!
    public fixed sbyte iIntraPredMode[48];
    // must follow with iNonZeroCoeffCount!

    public fixed int iSadCost[4];               //avail 1; unavail 0
    public InlineArray16<SMVUnitXY> sMbMvp;     // [MB_BLOCK4x4_NUM] for write bs

    //for residual decoding (recovery) at the side of Encoder
    public int16_t* pCoeffLevel;                // tmep
    //malloc memory for prediction
    public uint8_t* pSkipMb;

    //ALIGNED_DECLARE(uint8_t, pMemPredMb[2][256],  16);//One: Best I_16x16 Luma and refine frac_pixel pBuffer; another: PingPong I_8x8&&Inter Cb + Cr
    public uint8_t* pMemPredMb;
    public uint8_t* pMemPredLuma;               // inter && intra share same pointer;
    //ALIGNED_DECLARE(uint8_t, pMemPredChroma[2][64*2], 16); //another PingPong pBuffer: Best Cb + Cr;
    public uint8_t* pMemPredChroma;             // inter && intra share same pointer;
    public uint8_t* pBestPredIntraChroma;       //Cb:0~63;   Cr:64~127

    //ALIGNED_DECLARE(uint8_t, pMemPredBlk4[2][16], 16); //I_4x4
    public uint8_t* pMemPredBlk4;

    public uint8_t* pBestPredI4x4Blk4;          //I_4x4

    //ALIGNED_DECLARE(uint8_t, pBufferInterPredMe[4][400], 16);//inter type pBuffer for ME h & v & hv
    public uint8_t* pBufferInterPredMe;         // [4][400] is enough because only h&v or v&hv or h&hv. but if both h&v&hv is needed when 8 quart pixel, future we have to use [5][400].

    //no scan4[] order, just as memory order to store
    //ALIGNED_DECLARE(bool, pPrevIntra4x4PredModeFlag[16], 16);//if 1, means no rem_intra4x4_pred_mode; if 0, means rem_intra4x4_pred_mode != 0
    public bool* pPrevIntra4x4PredModeFlag;
    //ALIGNED_DECLARE(int8_t, pRemIntra4x4PredModeFlag[16], 16);//-1 as default; if pPrevIntra4x4PredModeFlag==0,
    //pRemIntra4x4PredModeFlag or added by 1 is the best pred_mode
    public int8_t* pRemIntra4x4PredModeFlag;

    public fixed int iSadCostSkip[4];           //avail 1; unavail 0
    public fixed bool bMbTypeSkip[4];           //1: skip; 0: non-skip
    public int32_t* pEncSad;

    //for residual encoding at the side of Encoder
    public SDCTCoeff* pDct;

    public uint8_t uiNeighborIntra;             // LEFT_MB_POS:0x01, TOP_MB_POS:0x02, TOPLEFT_MB_POS = 0x04 ,TOPRIGHT_MB_POS = 0x08;
    public uint8_t uiLumaI16x16Mode;
    public uint8_t uiChmaI8x8Mode;

    public bool bCollocatedPredFlag;            //denote if current MB is collocated predicted (MV==0).
    public uint32_t uiRefMbType;

    public SPicDataEntry SPicData;
}
