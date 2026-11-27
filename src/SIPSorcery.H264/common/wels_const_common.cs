//-----------------------------------------------------------------------------
// Filename: wels_const_common.cs
//
// Description: Port of codec/common/inc/wels_const_common.h.
// OpenH264 commit 78783f19 (v2.7.0-1).
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

namespace SIPSorcery.H264;

internal static partial class WelsCommon
{
    // Miscellaneous sizing infos
    public const int MAX_FNAME_LEN = 256;           // maximal length of file name in char size
    public const int WELS_LOG_BUF_SIZE = 4096;
    public const int MAX_TRACE_LOG_SIZE = 50 * (1 << 20);  // max trace log size: 50 MB, overwrite occur if log file size exceeds this size

    /* MB width in pixels for specified colorspace I420 usually used in codec */
    public const int MB_WIDTH_LUMA = 16;
    public const int MB_WIDTH_CHROMA = MB_WIDTH_LUMA >> 1;
    /* MB height in pixels for specified colorspace I420 usually used in codec */
    public const int MB_HEIGHT_LUMA = 16;
    public const int MB_HEIGHT_CHROMA = MB_HEIGHT_LUMA >> 1;
    public const int MB_COEFF_LIST_SIZE = 256 + ((MB_WIDTH_CHROMA * MB_HEIGHT_CHROMA) << 1);
    public const int MB_PARTITION_SIZE = 4;         // Macroblock partition size in 8x8 sub-blocks
    public const int MB_BLOCK4x4_NUM = 16;
    public const int MB_BLOCK8x8_NUM = 4;
    public const int MAX_SPS_COUNT = 32;            // Count number of SPS
    public const int BASE_QUALITY_ID = 0;
}
