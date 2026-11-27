//-----------------------------------------------------------------------------
// Filename: codec_def.cs
//
// Description: Port of:
//  - codec/api/wels/codec_def.h
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

/// <summary>Enumerate the type of video format.</summary>
internal enum EVideoFormatType : uint
{
    videoFormatRGB = 1,             ///< rgb color formats
    videoFormatRGBA = 2,
    videoFormatRGB555 = 3,
    videoFormatRGB565 = 4,
    videoFormatBGR = 5,
    videoFormatBGRA = 6,
    videoFormatABGR = 7,
    videoFormatARGB = 8,

    videoFormatYUY2 = 20,           ///< yuv color formats
    videoFormatYVYU = 21,
    videoFormatUYVY = 22,
    videoFormatI420 = 23,           ///< the same as IYUV
    videoFormatYV12 = 24,
    videoFormatInternal = 25,       ///< only used in SVC decoder testbed

    videoFormatNV12 = 26,           ///< new format for output by DXVA decoding

    videoFormatVFlip = 0x80000000
}

/// <summary>Enumerate video frame type.</summary>
internal enum EVideoFrameType
{
    videoFrameTypeInvalid,    ///< encoder not ready or parameters are invalidate
    videoFrameTypeIDR,        ///< IDR frame in H.264
    videoFrameTypeI,          ///< I frame type
    videoFrameTypeP,          ///< P frame type
    videoFrameTypeSkip,       ///< skip the frame based encoder kernel
    videoFrameTypeIPMixed     ///< a frame where I and P slices are mixing, not supported yet
}

/// <summary>Enumerate return type.</summary>
internal enum CM_RETURN
{
    cmResultSuccess,          ///< successful
    cmInitParaError,          ///< parameters are invalid
    cmUnknownReason,
    cmMallocMemeError,        ///< malloc a memory error
    cmInitExpected,           ///< initial action is expected
    cmUnsupportedData
}

/// <summary>Enumulate the nal unit type.</summary>
internal enum ENalUnitType
{
    NAL_UNKNOWN = 0,
    NAL_SLICE = 1,
    NAL_SLICE_DPA = 2,
    NAL_SLICE_DPB = 3,
    NAL_SLICE_DPC = 4,
    NAL_SLICE_IDR = 5,      ///< ref_idc != 0
    NAL_SEI = 6,            ///< ref_idc == 0
    NAL_SPS = 7,
    NAL_PPS = 8
    ///< ref_idc == 0 for 6,9,10,11,12
}

/// <summary>NRI: eNalRefIdc.</summary>
internal enum ENalPriority
{
    NAL_PRIORITY_DISPOSABLE = 0,
    NAL_PRIORITY_LOW = 1,
    NAL_PRIORITY_HIGH = 2,
    NAL_PRIORITY_HIGHEST = 3
}

/// <summary>Information of coded Slice(=NAL)(s).</summary>
internal unsafe struct SliceInfo
{
    public byte* pBufferOfSlices;      ///< base buffer of coded slice(s)
    public int iCodedSliceCount;       ///< number of coded slices
    public uint* pLengthOfSlices;      ///< array of slices length accordingly by number of slice
    public int iFecType;               ///< FEC type[0, 50%FEC, 100%FEC]
    public byte uiSliceIdx;            ///< index of slice in frame [FMO: 0,..,uiSliceCount-1; No FMO: 0]
    public byte uiSliceCount;          ///< count number of slice in frame [FMO: 2-8; No FMO: 1]
    public sbyte iFrameIndex;          ///< index of frame[-1, .., idr_interval-1]
    public byte uiNalRefIdc;           ///< NRI, priority level of slice(NAL)
    public byte uiNalType;             ///< NAL type
    public byte uiContainingFinalNal;  ///< whether final NAL is involved in buffer of coded slices, flag used in Pause feature in T27
}

/// <summary>Thresholds of the initial, maximal and minimal rate.</summary>
internal struct SRateThresholds
{
    public int iWidth;                   ///< frame width
    public int iHeight;                  ///< frame height
    public int iThresholdOfInitRate;     ///< threshold of initial rate
    public int iThresholdOfMaxRate;      ///< threshold of maximal rate
    public int iThresholdOfMinRate;      ///< threshold of minimal rate
    public int iMinThresholdFrameRate;   ///< min frame rate min
    public int iSkipFrameRate;           ///< skip to frame rate min
    public int iSkipFrameStep;           ///< how many frames to skip
}

/// <summary>Structure for decoder memery.</summary>
internal unsafe struct SSysMEMBuffer
{
    public int iWidth;                   ///< width of decoded pic for display
    public int iHeight;                  ///< height of decoded pic for display
    public int iFormat;                  ///< type is "EVideoFormatType"
    public fixed int iStride[2];         ///< stride of 2 component
}

/// <summary>The single-member union TagBufferInfo::UsrData.</summary>
internal struct SBufferInfoUsrData
{
    public SSysMEMBuffer sSystemBuffer;  ///<  memory info for one picture
}

/// <summary>Buffer info.</summary>
internal struct SBufferInfo
{
    public int iBufferStatus;            ///< 0: one frame data is not ready; 1: one frame data is ready
    public ulong uiInBsTimeStamp;        ///< input BS timestamp
    public ulong uiOutYuvTimeStamp;      ///< output YUV timestamp, when bufferstatus is 1
    public SBufferInfoUsrData UsrData;   ///<  output buffer info
    public PtrArray3<byte> pDst;         //point to picture YUV data
}

/// <summary>The #defines and anonymous enums of codec_def.h.</summary>
internal static class codec_def
{
    // IS_PARAMETER_SET_NAL / IS_IDR_NAL
    public static bool IS_PARAMETER_SET_NAL(int eNalRefIdc, int eNalType) =>
        (eNalRefIdc == (int)NAL_PRIORITY_HIGHEST) && (eNalType == ((int)NAL_SPS | (int)NAL_PPS) || eNalType == (int)NAL_SPS);

    public static bool IS_IDR_NAL(int eNalRefIdc, int eNalType) =>
        (eNalRefIdc == (int)NAL_PRIORITY_HIGHEST) && (eNalType == (int)NAL_SLICE_IDR);

    public const int FRAME_NUM_PARAM_SET = -1;
    public const int FRAME_NUM_IDR = 0;

    /// eDeblockingIdc
    public const int DEBLOCKING_IDC_0 = 0;
    public const int DEBLOCKING_IDC_1 = 1;
    public const int DEBLOCKING_IDC_2 = 2;
    public const int DEBLOCKING_OFFSET = 6;
    public const int DEBLOCKING_OFFSET_MINUS = -6;

    /* Error Tools definition (ERR_TOOL is unsigned short) */
    public const ushort ET_NONE = 0x00;        ///< NONE Error Tools
    public const ushort ET_IP_SCALE = 0x01;    ///< IP Scalable
    public const ushort ET_FMO = 0x02;         ///< Flexible Macroblock Ordering
    public const ushort ET_IR_R1 = 0x04;       ///< Intra Refresh in predifined 2% MB
    public const ushort ET_IR_R2 = 0x08;       ///< Intra Refresh in predifined 5% MB
    public const ushort ET_IR_R3 = 0x10;       ///< Intra Refresh in predifined 10% MB
    public const ushort ET_FEC_HALF = 0x20;    ///< Forward Error Correction in 50% redundency mode
    public const ushort ET_FEC_FULL = 0x40;    ///< Forward Error Correction in 100% redundency mode
    public const ushort ET_RFS = 0x80;         ///< Reference Frame Selection

    /// In a GOP, multiple of the key frame number, derived from the number of layers(index or array below)
    public static ReadOnlySpan<sbyte> kiKeyNumMultiple => new sbyte[] { 1, 1, 2, 4, 8, 16 };
}
