//-----------------------------------------------------------------------------
// Filename: rc.cs
//
// Description: Port of codec/encoder/core/inc/rc.h, constants only (E1):
// the QP limits and GOM row tables the parameter checks use. The rate
// control structures and functions follow with ratectl.cpp in E6.
// OpenH264 commit 78783f19 (v2.7.0-1).
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
    public const int GOM_TRACE_FLAG = 0;
    public const int GOM_H_SCC = 8;

    public const int BITS_NORMAL = 0;
    public const int BITS_LIMITED = 1;
    public const int BITS_EXCEEDED = 2;

    //virtual gop size
    public const int VGOP_SIZE = 8;

    //qp information
    public const int GOM_MIN_QP_MODE = 12;
    public const int GOM_MAX_QP_MODE = 36;
    public const int MAX_LOW_BR_QP = 42;
    public const int MIN_IDR_QP = 26;
    public const int MAX_IDR_QP = 32;
    public const int MIN_SCREEN_QP = 26;
    public const int MAX_SCREEN_QP = 35;
    public const int DELTA_QP = 2;
    public const int DELTA_QP_BGD_THD = 3;
    public const int QP_MIN_VALUE = 0;
    public const int QP_MAX_VALUE = 51;

    //frame skip constants
    public const int SKIP_QP_90P = 24;
    public const int SKIP_QP_180P = 24;
    public const int SKIP_QP_360P = 31;
    public const int SKIP_QP_720P = 31;
    public const int LAST_FRAME_QP_RANGE_UPPER_MODE0 = 3;
    public const int LAST_FRAME_QP_RANGE_LOWER_MODE0 = 2;
    public const int LAST_FRAME_QP_RANGE_UPPER_MODE1 = 5;
    public const int LAST_FRAME_QP_RANGE_LOWER_MODE1 = 3;

    public const int MB_WIDTH_THRESHOLD_90P = 15;
    public const int MB_WIDTH_THRESHOLD_180P = 30;
    public const int MB_WIDTH_THRESHOLD_360P = 60;

    //Mode 0 parameter
    public const int GOM_ROW_MODE0_90P = 2;
    public const int GOM_ROW_MODE0_180P = 2;
    public const int GOM_ROW_MODE0_360P = 4;
    public const int GOM_ROW_MODE0_720P = 4;
    public const int QP_RANGE_MODE0 = 3;

    //Mode 1 parameter
    public const int GOM_ROW_MODE1_90P = 1;
    public const int GOM_ROW_MODE1_180P = 1;
    public const int GOM_ROW_MODE1_360P = 2;
    public const int GOM_ROW_MODE1_720P = 2;
    public const int QP_RANGE_UPPER_MODE1 = 9;
    public const int QP_RANGE_LOWER_MODE1 = 4;
    public const int QP_RANGE_INTRA_MODE1 = 3;

    //bits allocation
    public const int MAX_BITS_VARY_PERCENTAGE = 100; //bits vary range in percentage
    public const int MAX_BITS_VARY_PERCENTAGE_x3d2 = 150; //bits vary range in percentage * 3/2
    public const int INT_MULTIPLY = 100; // use to multiply in Double to Int Conversion, should be same as AQ_QSTEP_INT_MULTIPLY in WelsVP
    public const int WEIGHT_MULTIPLY = 2000;
    public const int REMAIN_BITS_TH = 1;
    public const int VGOP_BITS_PERCENTAGE_DIFF = 5;
    public const int IDR_BITRATE_RATIO = 4;
    public const int FRAME_iTargetBits_VARY_RANGE = 50; // *INT_MULTIPLY
    //R-Q Model
    public const int LINEAR_MODEL_DECAY_FACTOR = 80; // *INT_MULTIPLY
    public const int FRAME_CMPLX_RATIO_RANGE = 20; // *INT_MULTIPLY
    public const int SMOOTH_FACTOR_MIN_VALUE = 2; // *INT_MULTIPLY
    //#define VGOP_BITS_MIN_RATIO 0.8
    //skip and padding
    public const int TIME_CHECK_WINDOW = 5000; // ms
    public const int SKIP_RATIO = 50; // *INT_MULTIPLY
    public const double LAST_FRAME_PREDICT_WEIGHT = 0.5;
    public const int PADDING_BUFFER_RATIO = 50; // *INT_MULTIPLY
    public const int PADDING_THRESHOLD = 5; //*INT_MULTIPLY

    public const int VIRTUAL_BUFFER_LOW_TH = 120; //*INT_MULTIPLY
    public const int VIRTUAL_BUFFER_HIGH_TH = 180; //*INT_MULTIPLY

    public const int _BITS_RANGE = 0;

    public const int EVEN_TIME_WINDOW = 0;
    public const int ODD_TIME_WINDOW = 1;
    public const int TIME_WINDOW_TOTAL = 2;
}
