//-----------------------------------------------------------------------------
// Filename: wels_const.cs
//
// Description: Port of codec/encoder/core/inc/wels_const.h: the encoder's
// constants. The anonymous enums are int constants, as in the decoder port.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// MAX_PPS_COUNT is the encoder's 57 (MAX_PPS_COUNT_LIMITED), not the
// decoder's 256. Code in WelsEnc sees this one.
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

namespace SIPSorcery.H264.Encoder;

internal static partial class WelsEnc
{
    public const int STATISTICS_LOG_INTERVAL_MS = 5000; // output statistics log every 5s

    public const int INTRA_4x4_MODE_NUM = 8;
    public const int MB_LUMA_CHROMA_BLOCK4x4_NUM = 24;

    public const int MAX_PPS_COUNT_LIMITED = 57;    // limit the max ID of PPS because of known limitation of receiver endpoints
    public const int MAX_PPS_COUNT = MAX_PPS_COUNT_LIMITED; //in Standard is 256     // Count number of PPS

    public const int PARA_SET_TYPE = 3;             // SPS+PPS
    public const int PARA_SET_TYPE_AVCSPS = 0;
    public const int PARA_SET_TYPE_SUBSETSPS = 1;
    public const int PARA_SET_TYPE_PPS = 2;

    public const int MAX_VERTICAL_MV_RANGE = 1024;  //TODO, for allocate enough memory for transpose
    public const int MAX_FRAME_RATE = 60;           // maximal frame rate to support
    public const int MIN_FRAME_RATE = 1;            // minimal frame rate need support

    public const int MAX_BIT_RATE = int.MaxValue;   // maximal bit rate to support
    public const int MIN_BIT_RATE = 1;              // minimal bit rate need support

    public const int SVC_QUALITY_BASE_QP = 26;
    public const int MAX_SLICEGROUP_IDS = 8;        // Count number of SSlice Groups
    public const int MAX_THREADS_NUM = 4;           // assume to support up to 4 logical cores(threads)

    public const int INTPEL_NEEDED_MARGIN = 3;      // for safe sub-pel MC

    public const int I420_PLANES = 3;

    public const float COMPRESS_RATIO_THR = 1.0f;   //set to size of the original data, which will be large enough considering MinCR

    public const int SSEI_BUFFER_SIZE = 128;
    public const int SPS_BUFFER_SIZE = 32;
    public const int PPS_BUFFER_SIZE = 16;

    public const int MAX_MACROBLOCK_SIZE_IN_BYTE = 400; //3200/8, 3200 is from Annex A.3.1.(n)
    public const int MAX_MACROBLOCK_SIZE_IN_BYTE_x2 = MAX_MACROBLOCK_SIZE_IN_BYTE << 1;

    // NUM_SPATIAL_LAYERS_CONSTRAINT and NUM_QUALITY_LAYERS_CONSTRAINT are defined.
    public const int MAX_DEPENDENCY_LAYER = MAX_SPATIAL_LAYER_NUM;  // Maximal dependency layer

    //The max temporal level support is equal or less than MAX_TEMPORAL_LAYER_NUM defined @ codec_app_def.h
    public const int MAX_TEMPORAL_LEVEL = MAX_TEMPORAL_LAYER_NUM;   // Maximal temporal level

    public const int MAX_QUALITY_LEVEL = MAX_QUALITY_LAYER_NUM;     // Maximal quality level

    public const int MAX_GOP_SIZE = 1 << (MAX_TEMPORAL_LEVEL - 1);

    public const int MAX_SHORT_REF_COUNT = MAX_GOP_SIZE >> 1;  // 16 in standard, maximal count number of short reference pictures
    public const int LONG_TERM_REF_NUM = 2;
    public const int LONG_TERM_REF_NUM_SCREEN = 4;
    public const int MAX_REF_PIC_COUNT = 16;        // 32 in standard, maximal Short + Long reference pictures
    public const int MIN_REF_PIC_COUNT = 1;         // minimal count number of reference pictures, 1 short + 2 key reference based?
    public const int MAX_MULTI_REF_PIC_COUNT = 1;   //maximum multi-reference number
    public const int MAX_MMCO_COUNT = 66;

    // adjusted numbers reference picture functionality related definition
    public const int MAX_REFERENCE_MMCO_COUNT_NUM = 4;     // adjusted MAX_MMCO_COUNT(66 in standard) definition per encoder design
    public const int MAX_REFERENCE_REORDER_COUNT_NUM = 2;  // adjusted MAX_REF_PIC_COUNT(32 in standard) for reference reordering definition per encoder design
    public const int MAX_REFERENCE_PICTURE_COUNT_NUM_CAMERA = MAX_SHORT_REF_COUNT + LONG_TERM_REF_NUM;        // <= MAX_REF_PIC_COUNT, memory saved if <
    public const int MAX_REFERENCE_PICTURE_COUNT_NUM_SCREEN = MAX_SHORT_REF_COUNT + LONG_TERM_REF_NUM_SCREEN; // <= MAX_REF_PIC_COUNT, memory saved if <

    public const int BASE_DEPENDENCY_ID = 0;
    public const int MAX_DQ_LAYER_NUM = MAX_DEPENDENCY_LAYER /**MAX_QUALITY_LEVEL*/;

    public const int INVALID_ID = -1;

    public const int NAL_HEADER_ADD_0X30BYTES = 20;

    public const int SLICE_NUM_EXPAND_COEF = 2;

    public const int BLOCK_16x16 = 0;
    public const int BLOCK_16x8 = 1;
    public const int BLOCK_8x16 = 2;
    public const int BLOCK_8x8 = 3;
    public const int BLOCK_4x4 = 4;
    public const int BLOCK_8x4 = 5;
    public const int BLOCK_4x8 = 6;
    public const int BLOCK_SIZE_ALL = 7;

    // LTR_MARKING_RECEIVE_STATE
    public const int RECIEVE_UNKOWN = 0;
    public const int RECIEVE_SUCCESS = 1;
    public const int RECIEVE_FAILED = 2;

    public const int CUR_AU_IDX = 0;                // index symbol for current access unit
    public const int SUC_AU_IDX = 1;                // index symbol for successive access unit

    public const int ENC_RETURN_SUCCESS = 0;
    public const int ENC_RETURN_MEMALLOCERR = 0x01; //will free memory and uninit
    public const int ENC_RETURN_UNSUPPORTED_PARA = 0x02; //unsupported setting
    public const int ENC_RETURN_UNEXPECTED = 0x04;  //unexpected value
    public const int ENC_RETURN_CORRECTED = 0x08;   //unexpected value but corrected by encoder
    public const int ENC_RETURN_INVALIDINPUT = 0x10; //invalid input
    public const int ENC_RETURN_MEMOVERFLOWFOUND = 0x20;
    public const int ENC_RETURN_VLCOVERFLOWFOUND = 0x40;
    public const int ENC_RETURN_KNOWN_ISSUE = 0x80;
    //TODO: need to complete the return checking in encoder and fill in more types if needed
}
