//-----------------------------------------------------------------------------
// Filename: error_code.cs
//
// Description: Port of codec/decoder/core/inc/error_code.h: the decoder's error
// numbers. The enums are int constants here, because the ported code returns
// and compares them as int32_t, as C does.
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
 * \file    error_code.h
 *
 * \brief   Error codes used in Wels decoder side
 *
 * \date    3/4/2009 Created
 *
 *************************************************************************************
 */

using System.Runtime.CompilerServices;

namespace SIPSorcery.H264.Decoder;

internal static partial class WelsDec
{
    // typedef enum TagWelsErr { ... } EWelsErr;
    public const int ERR_NONE = 0;
    public const int ERR_INVALID_PARAMETERS = 1;
    public const int ERR_MALLOC_FAILED = 2;
    public const int ERR_API_FAILED = 3;

    public const int ERR_BOUND = 31;

    /*
     * Specified error format:
     * ERR_NO = (ERR_LEVEL_FROM (HIGH WORD) << 16) | (ERR_INFO_FROM (LOW WORD))
     *
     */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int32_t GENERATE_ERROR_NO(int32_t iErrLevel, int32_t iErrInfo) => (iErrLevel << 16) | (iErrInfo & 0xFFFF);

    public const int ERR_INVALID_INTRA4X4_MODE = -1;

    /* ERR_LEVEL */
    //-----------------------------------------------------------------------------------------------------------
    public const int ERR_LEVEL_ACCESS_UNIT = 1;
    public const int ERR_LEVEL_NAL_UNIT_HEADER = 2;
    public const int ERR_LEVEL_PREFIX_NAL = 3;
    public const int ERR_LEVEL_PARAM_SETS = 4;
    public const int ERR_LEVEL_SLICE_HEADER = 5;
    public const int ERR_LEVEL_SLICE_DATA = 6;
    public const int ERR_LEVEL_MB_DATA = 7;

    //-----------------------------------------------------------------------------------------------------------

    /* More detailed error information, maximal value is 65535 */
    //-----------------------------------------------------------------------------------------------------------
    public const int ERR_INFO_COMMON_BASE = 1;
    public const int ERR_INFO_SYNTAX_BASE = 1001;
    public const int ERR_INFO_LOGIC_BASE = 10001;

    /* Error from common system level: 1-1000 */
    public const int ERR_INFO_OUT_OF_MEMORY = ERR_INFO_COMMON_BASE;
    public const int ERR_INFO_INVALID_ACCESS = ERR_INFO_OUT_OF_MEMORY + 1;
    public const int ERR_INFO_INVALID_PTR = ERR_INFO_INVALID_ACCESS + 1;
    public const int ERR_INFO_INVALID_PARAM = ERR_INFO_INVALID_PTR + 1;
    public const int ERR_INFO_FILE_NO_FOUND = ERR_INFO_INVALID_PARAM + 1;
    public const int ERR_INFO_PATH_NO_FOUND = ERR_INFO_FILE_NO_FOUND + 1;
    public const int ERR_INFO_ACCESS_DENIED = ERR_INFO_PATH_NO_FOUND + 1;
    public const int ERR_INFO_NOT_READY = ERR_INFO_ACCESS_DENIED + 1;
    public const int ERR_INFO_WRITE_FAULT = ERR_INFO_NOT_READY + 1;
    public const int ERR_INFO_READ_FAULT = ERR_INFO_WRITE_FAULT + 1;
    public const int ERR_INFO_READ_OVERFLOW = ERR_INFO_READ_FAULT + 1;
    public const int ERR_INFO_READ_LEADING_ZERO = ERR_INFO_READ_OVERFLOW + 1;
    public const int ERR_INFO_UNINIT = ERR_INFO_READ_LEADING_ZERO + 1;
    /* Error from H.264 syntax elements parser: 1001-10000 */
    public const int ERR_INFO_NO_PREFIX_CODE = ERR_INFO_SYNTAX_BASE; // No start prefix code indication
    public const int ERR_INFO_NO_PARAM_SETS = ERR_INFO_NO_PREFIX_CODE + 1; // No SPS and/ PPS before sequence header
    public const int ERR_INFO_PARAM_SETS_NOT_INTEGRATED = ERR_INFO_NO_PARAM_SETS + 1; // Parameters sets (sps/pps) are not integrated at all before to decode VCL nal
    public const int ERR_INFO_SPS_ID_OVERFLOW = ERR_INFO_PARAM_SETS_NOT_INTEGRATED + 1;
    public const int ERR_INFO_PPS_ID_OVERFLOW = ERR_INFO_SPS_ID_OVERFLOW + 1;
    public const int ERR_INFO_INVALID_PROFILE_IDC = ERR_INFO_PPS_ID_OVERFLOW + 1;
    public const int ERR_INFO_UNMATCHED_LEVEL_IDC = ERR_INFO_INVALID_PROFILE_IDC + 1;
    public const int ERR_INFO_INVALID_POC_TYPE = ERR_INFO_UNMATCHED_LEVEL_IDC + 1;
    public const int ERR_INFO_INVALID_MB_SIZE_INFO = ERR_INFO_INVALID_POC_TYPE + 1;
    public const int ERR_INFO_REF_COUNT_OVERFLOW = ERR_INFO_INVALID_MB_SIZE_INFO + 1;
    public const int ERR_INFO_CROPPING_NO_SUPPORTED = ERR_INFO_REF_COUNT_OVERFLOW + 1;
    public const int ERR_INFO_INVALID_CROPPING_DATA = ERR_INFO_CROPPING_NO_SUPPORTED + 1;
    public const int ERR_INFO_UNSUPPORTED_VUI_HRD = ERR_INFO_INVALID_CROPPING_DATA + 1;
    public const int ERR_INFO_INVALID_SLICEGROUP = ERR_INFO_UNSUPPORTED_VUI_HRD + 1;
    public const int ERR_INFO_INVALID_SLICEGROUP_MAP_TYPE = ERR_INFO_INVALID_SLICEGROUP + 1;
    public const int ERR_INFO_INVALID_FRAME_NUM = ERR_INFO_INVALID_SLICEGROUP_MAP_TYPE + 1;
    public const int ERR_INFO_INVALID_IDR_PIC_ID = ERR_INFO_INVALID_FRAME_NUM + 1;
    public const int ERR_INFO_INVALID_REDUNDANT_PIC_CNT = ERR_INFO_INVALID_IDR_PIC_ID + 1;
    public const int ERR_INFO_INVALID_MAX_NUM_REF_FRAMES = ERR_INFO_INVALID_REDUNDANT_PIC_CNT + 1;
    public const int ERR_INFO_INVALID_MAX_MB_SIZE = ERR_INFO_INVALID_MAX_NUM_REF_FRAMES + 1;
    public const int ERR_INFO_INVALID_FIRST_MB_IN_SLICE = ERR_INFO_INVALID_MAX_MB_SIZE + 1;
    public const int ERR_INFO_INVALID_NUM_REF_IDX_L0_ACTIVE_MINUS1 = ERR_INFO_INVALID_FIRST_MB_IN_SLICE + 1;
    public const int ERR_INFO_INVALID_NUM_REF_IDX_L1_ACTIVE_MINUS1 = ERR_INFO_INVALID_NUM_REF_IDX_L0_ACTIVE_MINUS1 + 1;
    public const int ERR_INFO_INVALID_SLICE_ALPHA_C0_OFFSET_DIV2 = ERR_INFO_INVALID_NUM_REF_IDX_L1_ACTIVE_MINUS1 + 1;
    public const int ERR_INFO_INVALID_SLICE_BETA_OFFSET_DIV2 = ERR_INFO_INVALID_SLICE_ALPHA_C0_OFFSET_DIV2 + 1;
    public const int ERR_INFO_FMO_INIT_FAIL = ERR_INFO_INVALID_SLICE_BETA_OFFSET_DIV2 + 1;
    public const int ERR_INFO_SLICE_TYPE_OVERFLOW = ERR_INFO_FMO_INIT_FAIL + 1;
    public const int ERR_INFO_INVALID_CABAC_INIT_IDC = ERR_INFO_SLICE_TYPE_OVERFLOW + 1;
    public const int ERR_INFO_INVALID_QP = ERR_INFO_INVALID_CABAC_INIT_IDC + 1;
    public const int ERR_INFO_INVALID_PIC_INIT_QS = ERR_INFO_INVALID_QP + 1;
    public const int ERR_INFO_INVALID_CHROMA_QP_INDEX_OFFSET = ERR_INFO_INVALID_PIC_INIT_QS + 1;
    public const int ERR_INFO_INVALID_PIC_INIT_QP = ERR_INFO_INVALID_CHROMA_QP_INDEX_OFFSET + 1;
    public const int ERR_INFO_INVALID_LOG2_MAX_FRAME_NUM_MINUS4 = ERR_INFO_INVALID_PIC_INIT_QP + 1;
    public const int ERR_INFO_INVALID_LOG2_MAX_PIC_ORDER_CNT_LSB_MINUS4 = ERR_INFO_INVALID_LOG2_MAX_FRAME_NUM_MINUS4 + 1;
    public const int ERR_INFO_INVALID_NUM_REF_FRAME_IN_PIC_ORDER_CNT_CYCLE = ERR_INFO_INVALID_LOG2_MAX_PIC_ORDER_CNT_LSB_MINUS4 + 1;
    public const int ERR_INFO_INVALID_DBLOCKING_IDC = ERR_INFO_INVALID_NUM_REF_FRAME_IN_PIC_ORDER_CNT_CYCLE + 1;
    public const int ERR_INFO_INVALID_MB_TYPE = ERR_INFO_INVALID_DBLOCKING_IDC + 1;
    public const int ERR_INFO_INVALID_MB_SKIP_RUN = ERR_INFO_INVALID_MB_TYPE + 1;
    public const int ERR_INFO_INVALID_SPS_ID = ERR_INFO_INVALID_MB_SKIP_RUN + 1;
    public const int ERR_INFO_INVALID_PPS_ID = ERR_INFO_INVALID_SPS_ID + 1;
    public const int ERR_INFO_INVALID_SUB_MB_TYPE = ERR_INFO_INVALID_PPS_ID + 1;
    public const int ERR_INFO_UNAVAILABLE_TOP_BLOCK_FOR_INTRA = ERR_INFO_INVALID_SUB_MB_TYPE + 1;
    public const int ERR_INFO_UNAVAILABLE_LEFT_BLOCK_FOR_INTRA = ERR_INFO_UNAVAILABLE_TOP_BLOCK_FOR_INTRA + 1;
    public const int ERR_INFO_INVALID_REF_INDEX = ERR_INFO_UNAVAILABLE_LEFT_BLOCK_FOR_INTRA + 1;
    public const int ERR_INFO_INVALID_CBP = ERR_INFO_INVALID_REF_INDEX + 1;
    public const int ERR_INFO_DQUANT_OUT_OF_RANGE = ERR_INFO_INVALID_CBP + 1;
    public const int ERR_INFO_CAVLC_INVALID_PREFIX = ERR_INFO_DQUANT_OUT_OF_RANGE + 1;
    public const int ERR_INFO_CAVLC_INVALID_LEVEL = ERR_INFO_CAVLC_INVALID_PREFIX + 1;
    public const int ERR_INFO_CAVLC_INVALID_TOTAL_COEFF_OR_TRAILING_ONES = ERR_INFO_CAVLC_INVALID_LEVEL + 1;
    public const int ERR_INFO_CAVLC_INVALID_ZERO_LEFT = ERR_INFO_CAVLC_INVALID_TOTAL_COEFF_OR_TRAILING_ONES + 1;
    public const int ERR_INFO_CAVLC_INVALID_RUN_BEFORE = ERR_INFO_CAVLC_INVALID_ZERO_LEFT + 1;
    public const int ERR_INFO_MV_OUT_OF_RANGE = ERR_INFO_CAVLC_INVALID_RUN_BEFORE + 1;

    public const int ERR_INFO_INVALID_I4x4_PRED_MODE = ERR_INFO_MV_OUT_OF_RANGE + 1;
    public const int ERR_INFO_INVALID_I16x16_PRED_MODE = ERR_INFO_INVALID_I4x4_PRED_MODE + 1;
    public const int ERR_INFO_INVALID_I_CHROMA_PRED_MODE = ERR_INFO_INVALID_I16x16_PRED_MODE + 1;

    public const int ERR_INFO_INVALID_LUMA_LOG2_WEIGHT_DENOM = ERR_INFO_INVALID_I_CHROMA_PRED_MODE + 1;
    public const int ERR_INFO_INVALID_CHROMA_LOG2_WEIGHT_DENOM = ERR_INFO_INVALID_LUMA_LOG2_WEIGHT_DENOM + 1;
    public const int ERR_INFO_INVALID_LUMA_WEIGHT = ERR_INFO_INVALID_CHROMA_LOG2_WEIGHT_DENOM + 1;
    public const int ERR_INFO_INVALID_CHROMA_WEIGHT = ERR_INFO_INVALID_LUMA_WEIGHT + 1;
    public const int ERR_INFO_INVALID_LUMA_OFFSET = ERR_INFO_INVALID_CHROMA_WEIGHT + 1;
    public const int ERR_INFO_INVALID_CHROMA_OFFSET = ERR_INFO_INVALID_LUMA_OFFSET + 1;

    public const int ERR_INFO_UNSUPPORTED_NON_BASELINE = ERR_INFO_INVALID_CHROMA_OFFSET + 1;
    public const int ERR_INFO_UNSUPPORTED_FMOTYPE = ERR_INFO_UNSUPPORTED_NON_BASELINE + 1;
    public const int ERR_INFO_UNSUPPORTED_MBAFF = ERR_INFO_UNSUPPORTED_FMOTYPE + 1;
    public const int ERR_INFO_UNSUPPORTED_ILP = ERR_INFO_UNSUPPORTED_MBAFF + 1;
    public const int ERR_INFO_UNSUPPORTED_CABAC_EL = ERR_INFO_UNSUPPORTED_ILP + 1;
    public const int ERR_INFO_UNSUPPORTED_SPSI = ERR_INFO_UNSUPPORTED_CABAC_EL + 1;
    public const int ERR_INFO_UNSUPPORTED_MGS = ERR_INFO_UNSUPPORTED_SPSI + 1;
    public const int ERR_INFO_UNSUPPORTED_BIPRED = ERR_INFO_UNSUPPORTED_MGS + 1;
    public const int ERR_INFO_UNSUPPORTED_WP = ERR_INFO_UNSUPPORTED_BIPRED + 1;
    public const int ERR_INFO_UNSUPPORTED_SLICESKIP = ERR_INFO_UNSUPPORTED_WP + 1;

    public const int ERR_INFO_FRAMES_LOST = ERR_INFO_UNSUPPORTED_SLICESKIP + 1;
    public const int ERR_INFO_DEPENDENCY_SPATIAL_LAYER_LOST = ERR_INFO_FRAMES_LOST + 1;
    public const int ERR_INFO_DEPENDENCY_QUALIT_LAYER_LOST = ERR_INFO_DEPENDENCY_SPATIAL_LAYER_LOST + 1;
    public const int ERR_INFO_REFERENCE_PIC_LOST = ERR_INFO_DEPENDENCY_QUALIT_LAYER_LOST + 1;
    public const int ERR_INFO_INVALID_REORDERING = ERR_INFO_REFERENCE_PIC_LOST + 1;
    public const int ERR_INFO_INVALID_MARKING = ERR_INFO_INVALID_REORDERING + 1;

    public const int ERR_INFO_FMO_NOT_SUPPORTED_IN_BASE_LAYER = ERR_INFO_INVALID_MARKING + 1;
    public const int ERR_INFO_INVALID_ESS = ERR_INFO_FMO_NOT_SUPPORTED_IN_BASE_LAYER + 1;
    public const int ERR_INFO_INVALID_SLICE_TYPE = ERR_INFO_INVALID_ESS + 1;
    public const int ERR_INFO_INVALID_REF_MARKING = ERR_INFO_INVALID_SLICE_TYPE + 1;
    public const int ERR_INFO_INVALID_REF_REORDERING = ERR_INFO_INVALID_REF_MARKING + 1;

    /* Error from corresponding logic, 10001-65535 */
    public const int ERR_INFO_NO_IDR_PIC = ERR_INFO_LOGIC_BASE; // NO IDR picture available before sequence header
    public const int ERR_INFO_EC_NO_NEIGHBOUR_MBS = ERR_INFO_NO_IDR_PIC + 1;
    public const int ERR_INFO_EC_UNEXPECTED_MB_TYPE = ERR_INFO_EC_NO_NEIGHBOUR_MBS + 1;
    public const int ERR_INFO_EC_NO_ENOUGH_NEIGHBOUR_MBS = ERR_INFO_EC_UNEXPECTED_MB_TYPE + 1;
    public const int ERR_INFO_DUPLICATE_FRAME_NUM = ERR_INFO_EC_NO_ENOUGH_NEIGHBOUR_MBS + 1;
    //for LTR
    public const int ERR_INFO_INVALID_MMCO_NUM = ERR_INFO_DUPLICATE_FRAME_NUM + 1;
    public const int ERR_INFO_INVALID_MMCO_OPCODE_BASE = ERR_INFO_INVALID_MMCO_NUM + 1;
    public const int ERR_INFO_INVALID_MMCO_SHORT2UNUSED = ERR_INFO_INVALID_MMCO_OPCODE_BASE + 1;
    public const int EER_INFO_INVALID_MMCO_LONG2UNUSED = ERR_INFO_INVALID_MMCO_SHORT2UNUSED + 1;
    public const int ERR_INFO_INVALID_MMCO_SHOART2LONG = EER_INFO_INVALID_MMCO_LONG2UNUSED + 1;
    public const int ERR_INFO_INVALID_MMCO_REF_NUM_OVERFLOW = ERR_INFO_INVALID_MMCO_SHOART2LONG + 1;
    public const int ERR_INFO_INVALID_MMCO_REF_NUM_NOT_ENOUGH = ERR_INFO_INVALID_MMCO_REF_NUM_OVERFLOW + 1;
    public const int ERR_INFO_INVALID_MMCO_LONG_TERM_IDX_EXCEED_MAX = ERR_INFO_INVALID_MMCO_REF_NUM_NOT_ENOUGH + 1;
    //for CABAC
    public const int ERR_CABAC_NO_BS_TO_READ = ERR_INFO_INVALID_MMCO_LONG_TERM_IDX_EXCEED_MAX + 1;
    public const int ERR_CABAC_UNEXPECTED_VALUE = ERR_CABAC_NO_BS_TO_READ + 1;
    //for scaling list
    public const int ERR_SCALING_LIST_DELTA_SCALE = ERR_CABAC_UNEXPECTED_VALUE + 1;
    //logic error related to multi-layer
    public const int ERR_INFO_WIDTH_MISMATCH = ERR_SCALING_LIST_DELTA_SCALE + 1;
    //reconstruction error
    public const int ERR_INFO_MB_RECON_FAIL = ERR_INFO_WIDTH_MISMATCH + 1;
    public const int ERR_INFO_MB_NUM_EXCEED_FAIL = ERR_INFO_MB_RECON_FAIL + 1;
    public const int ERR_INFO_BS_INCOMPLETE = ERR_INFO_MB_NUM_EXCEED_FAIL + 1;
    public const int ERR_INFO_MB_NUM_INADEQUATE = ERR_INFO_BS_INCOMPLETE + 1;
    //parse only error
    public const int ERR_INFO_PARSEONLY_PENDING = ERR_INFO_MB_NUM_INADEQUATE + 1;
    public const int ERR_INFO_PARSEONLY_ERROR = ERR_INFO_PARSEONLY_PENDING + 1;
    //-----------------------------------------------------------------------------------------------------------
}
