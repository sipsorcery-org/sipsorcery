//-----------------------------------------------------------------------------
// Filename: codec_app_def.cs
//
// Description: Port of:
//  - codec/api/wels/codec_app_def.h
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

/// <summary>Constants (#defines and anonymous enums) of codec_app_def.h.</summary>
internal static class codec_app_def
{
    public const int MAX_TEMPORAL_LAYER_NUM = 4;
    public const int MAX_SPATIAL_LAYER_NUM = 4;
    public const int MAX_QUALITY_LAYER_NUM = 4;

    public const int MAX_LAYER_NUM_OF_FRAME = 128;
    public const int MAX_NAL_UNITS_IN_LAYER = 128;     ///< predetermined here, adjust it later if need

    public const int MAX_RTP_PAYLOAD_LEN = 1000;
    public const int AVERAGE_RTP_PAYLOAD_LEN = 800;

    public const int SAVED_NALUNIT_NUM_TMP = (MAX_SPATIAL_LAYER_NUM * MAX_QUALITY_LAYER_NUM) + 1 + MAX_SPATIAL_LAYER_NUM;  ///< SPS/PPS + SEI/SSEI + PADDING_NAL
    public const int MAX_SLICES_NUM_TMP = (MAX_NAL_UNITS_IN_LAYER - SAVED_NALUNIT_NUM_TMP) / 3;

    public const int AUTO_REF_PIC_COUNT = -1;          ///< encoder selects the number of reference frame automatically
    public const int UNSPECIFIED_BIT_RATE = 0;         ///< to do: add detail comment

    /// Enumerate the type of wels log
    public const int WELS_LOG_QUIET = 0x00;            ///< quiet mode
    public const int WELS_LOG_ERROR = 1 << 0;          ///< error log iLevel
    public const int WELS_LOG_WARNING = 1 << 1;        ///< Warning log iLevel
    public const int WELS_LOG_INFO = 1 << 2;           ///< information log iLevel
    public const int WELS_LOG_DEBUG = 1 << 3;          ///< debug log, critical algo log
    public const int WELS_LOG_DETAIL = 1 << 4;         ///< per packet/frame log
    public const int WELS_LOG_RESV = 1 << 5;           ///< resversed log iLevel
    public const int WELS_LOG_LEVEL_COUNT = 6;
    public const int WELS_LOG_DEFAULT = WELS_LOG_WARNING;   ///< default log iLevel in Wels codec
}

/// <summary>Struct of OpenH264 version.</summary>
internal struct OpenH264Version
{
    public uint uMajor;                  ///< The major version number
    public uint uMinor;                  ///< The minor version number
    public uint uRevision;               ///< The revision number
    public uint uReserved;               ///< The reserved number, it should be 0.
}

/// <summary>Decoding status.</summary>
[Flags]
internal enum DECODING_STATE
{
    /**
    * Errors derived from bitstream parsing
    */
    dsErrorFree = 0x00,           ///< bit stream error-free
    dsFramePending = 0x01,        ///< need more throughput to generate a frame output,
    dsRefLost = 0x02,             ///< layer lost at reference frame with temporal id 0
    dsBitstreamError = 0x04,      ///< error bitstreams(maybe broken internal frame) the decoder cared
    dsDepLayerLost = 0x08,        ///< dependented layer is ever lost
    dsNoParamSets = 0x10,         ///< no parameter set NALs involved
    dsDataErrorConcealed = 0x20,  ///< current data error concealed specified
    dsRefListNullPtrs = 0x40,     ///<ref picure list contains null ptrs within uiRefCount range

    /**
    * Errors derived from logic level
    */
    dsInvalidArgument = 0x1000,   ///< invalid argument specified
    dsInitialOptExpected = 0x2000, ///< initializing operation is expected
    dsOutOfMemory = 0x4000,       ///< out of memory due to new request
    /**
    * ANY OTHERS?
    */
    dsDstBufNeedExpan = 0x8000    ///< actual picture size exceeds size of dst pBuffer feed in decoder, so need expand its size
}

/// <summary>Option types introduced in SVC encoder application.</summary>
internal enum ENCODER_OPTION
{
    ENCODER_OPTION_DATAFORMAT = 0,
    ENCODER_OPTION_IDR_INTERVAL,               ///< IDR period,0/-1 means no Intra period (only the first frame); lager than 0 means the desired IDR period, must be multiple of (2^temporal_layer)
    ENCODER_OPTION_SVC_ENCODE_PARAM_BASE,      ///< structure of Base Param
    ENCODER_OPTION_SVC_ENCODE_PARAM_EXT,       ///< structure of Extension Param
    ENCODER_OPTION_FRAME_RATE,                 ///< maximal input frame rate, current supported range: MAX_FRAME_RATE = 30,MIN_FRAME_RATE = 1
    ENCODER_OPTION_BITRATE,
    ENCODER_OPTION_MAX_BITRATE,
    ENCODER_OPTION_INTER_SPATIAL_PRED,
    ENCODER_OPTION_RC_MODE,
    ENCODER_OPTION_RC_FRAME_SKIP,
    ENCODER_PADDING_PADDING,                   ///< 0:disable padding;1:padding

    ENCODER_OPTION_PROFILE,                    ///< assgin the profile for each layer
    ENCODER_OPTION_LEVEL,                      ///< assgin the level for each layer
    ENCODER_OPTION_NUMBER_REF,                 ///< the number of refererence frame
    ENCODER_OPTION_DELIVERY_STATUS,            ///< the delivery info which is a feedback from app level

    ENCODER_LTR_RECOVERY_REQUEST,
    ENCODER_LTR_MARKING_FEEDBACK,
    ENCODER_LTR_MARKING_PERIOD,
    ENCODER_OPTION_LTR,                        ///< 0:disable LTR;larger than 0 enable LTR; LTR number is fixed to be 2 in current encoder
    ENCODER_OPTION_COMPLEXITY,

    ENCODER_OPTION_ENABLE_SSEI,                ///< enable SSEI: true--enable ssei; false--disable ssei
    ENCODER_OPTION_ENABLE_PREFIX_NAL_ADDING,   ///< enable prefix: true--enable prefix; false--disable prefix
    ENCODER_OPTION_SPS_PPS_ID_STRATEGY,        ///< different stategy in adjust ID in SPS/PPS: 0- constant ID, 1-additional ID, 6-mapping and additional

    ENCODER_OPTION_CURRENT_PATH,
    ENCODER_OPTION_DUMP_FILE,                  ///< dump layer reconstruct frame to a specified file
    ENCODER_OPTION_TRACE_LEVEL,                ///< trace info based on the trace level
    ENCODER_OPTION_TRACE_CALLBACK,             ///< a void (*)(void* context, int level, const char* message) function which receives log messages
    ENCODER_OPTION_TRACE_CALLBACK_CONTEXT,     ///< context info of trace callback

    ENCODER_OPTION_GET_STATISTICS,             ///< read only
    ENCODER_OPTION_STATISTICS_LOG_INTERVAL,    ///< log interval in millisecond

    ENCODER_OPTION_IS_LOSSLESS_LINK,           ///< advanced algorithmetic settings

    ENCODER_OPTION_BITS_VARY_PERCENTAGE        ///< bit vary percentage
}

/// <summary>Option types introduced in decoder application.</summary>
internal enum DECODER_OPTION
{
    DECODER_OPTION_END_OF_STREAM = 1,     ///< end of stream flag
    DECODER_OPTION_VCL_NAL,               ///< feedback whether or not have VCL NAL in current AU for application layer
    DECODER_OPTION_TEMPORAL_ID,           ///< feedback temporal id for application layer
    DECODER_OPTION_FRAME_NUM,             ///< feedback current decoded frame number
    DECODER_OPTION_IDR_PIC_ID,            ///< feedback current frame belong to which IDR period
    DECODER_OPTION_LTR_MARKING_FLAG,      ///< feedback wether current frame mark a LTR
    DECODER_OPTION_LTR_MARKED_FRAME_NUM,  ///< feedback frame num marked by current Frame
    DECODER_OPTION_ERROR_CON_IDC,         ///< indicate decoder error concealment method
    DECODER_OPTION_TRACE_LEVEL,
    DECODER_OPTION_TRACE_CALLBACK,        ///< a void (*)(void* context, int level, const char* message) function which receives log messages
    DECODER_OPTION_TRACE_CALLBACK_CONTEXT,///< context info of trace callbac

    DECODER_OPTION_GET_STATISTICS,        ///< feedback decoder statistics
    DECODER_OPTION_GET_SAR_INFO,          ///< feedback decoder Sample Aspect Ratio info in Vui
    DECODER_OPTION_PROFILE,               ///< get current AU profile info, only is used in GetOption
    DECODER_OPTION_LEVEL,                 ///< get current AU level info,only is used in GetOption
    DECODER_OPTION_STATISTICS_LOG_INTERVAL,///< set log output interval
    DECODER_OPTION_IS_REF_PIC,             ///< feedback current frame is ref pic or not
    DECODER_OPTION_NUM_OF_FRAMES_REMAINING_IN_BUFFER,  ///< number of frames remaining in decoder buffer when pictures are required to re-ordered into display-order.
    DECODER_OPTION_NUM_OF_THREADS,         ///< number of decoding threads. The maximum thread count is equal or less than lesser of (cpu core counts and 16).
}

/// <summary>Enumerate the type of error concealment methods.</summary>
internal enum ERROR_CON_IDC
{
    ERROR_CON_DISABLE = 0,
    ERROR_CON_FRAME_COPY,
    ERROR_CON_SLICE_COPY,
    ERROR_CON_FRAME_COPY_CROSS_IDR,
    ERROR_CON_SLICE_COPY_CROSS_IDR,
    ERROR_CON_SLICE_COPY_CROSS_IDR_FREEZE_RES_CHANGE,
    ERROR_CON_SLICE_MV_COPY_CROSS_IDR,
    ERROR_CON_SLICE_MV_COPY_CROSS_IDR_FREEZE_RES_CHANGE
}

/// <summary>Feedback that whether or not have VCL NAL in current AU.</summary>
internal enum FEEDBACK_VCL_NAL_IN_AU
{
    FEEDBACK_NON_VCL_NAL = 0,
    FEEDBACK_VCL_NAL,
    FEEDBACK_UNKNOWN_NAL
}

/// <summary>Type of layer being encoded.</summary>
internal enum LAYER_TYPE
{
    NON_VIDEO_CODING_LAYER = 0,
    VIDEO_CODING_LAYER = 1
}

/// <summary>Spatial layer num.</summary>
internal enum LAYER_NUM
{
    SPATIAL_LAYER_0 = 0,
    SPATIAL_LAYER_1 = 1,
    SPATIAL_LAYER_2 = 2,
    SPATIAL_LAYER_3 = 3,
    SPATIAL_LAYER_ALL = 4
}

/// <summary>Enumerate the type of video bitstream which is provided to decoder.</summary>
internal enum VIDEO_BITSTREAM_TYPE
{
    VIDEO_BITSTREAM_AVC = 0,
    VIDEO_BITSTREAM_SVC = 1,
    VIDEO_BITSTREAM_DEFAULT = VIDEO_BITSTREAM_SVC
}

/// <summary>Enumerate the type of key frame request.</summary>
internal enum KEY_FRAME_REQUEST_TYPE
{
    NO_RECOVERY_REQUSET = 0,
    LTR_RECOVERY_REQUEST = 1,
    IDR_RECOVERY_REQUEST = 2,
    NO_LTR_MARKING_FEEDBACK = 3,
    LTR_MARKING_SUCCESS = 4,
    LTR_MARKING_FAILED = 5
}

/// <summary>Structure for LTR recover request.</summary>
internal struct SLTRRecoverRequest
{
    public uint uiFeedbackType;       ///< IDR request or LTR recovery request
    public uint uiIDRPicId;           ///< distinguish request from different IDR
    public int iLastCorrectFrameNum;
    public int iCurrentFrameNum;      ///< specify current decoder frame_num.
    public int iLayerId;              //specify the layer for recovery request
}

/// <summary>Structure for LTR marking feedback.</summary>
internal struct SLTRMarkingFeedback
{
    public uint uiFeedbackType;   ///< mark failed or successful
    public uint uiIDRPicId;       ///< distinguish request from different IDR
    public int iLTRFrameNum;      ///< specify current decoder frame_num
    public int iLayerId;          //specify the layer for LTR marking feedback
}

/// <summary>Structure for LTR configuration.</summary>
internal struct SLTRConfig
{
    public bool bEnableLongTermReference; ///< 1: on, 0: off
    public int iLTRRefNum;                ///< TODO: not supported to set it arbitrary yet
}

/// <summary>Enumerate the type of rate control mode.</summary>
internal enum RC_MODES
{
    RC_QUALITY_MODE = 0,            ///< quality mode
    RC_BITRATE_MODE = 1,            ///< bitrate mode
    RC_BUFFERBASED_MODE = 2,        ///< no bitrate control,only using buffer status,adjust the video quality
    RC_TIMESTAMP_MODE = 3,          //rate control based timestamp
    RC_BITRATE_MODE_POST_SKIP = 4,  ///< this is in-building RC MODE, WILL BE DELETED after algorithm tuning!
    RC_OFF_MODE = -1,               ///< rate control off mode
}

/// <summary>Enumerate the type of profile id.</summary>
internal enum EProfileIdc
{
    PRO_UNKNOWN = 0,
    PRO_BASELINE = 66,
    PRO_MAIN = 77,
    PRO_EXTENDED = 88,
    PRO_HIGH = 100,
    PRO_HIGH10 = 110,
    PRO_HIGH422 = 122,
    PRO_HIGH444 = 144,
    PRO_CAVLC444 = 244,

    PRO_SCALABLE_BASELINE = 83,
    PRO_SCALABLE_HIGH = 86
}

/// <summary>Enumerate the type of level id.</summary>
internal enum ELevelIdc
{
    LEVEL_UNKNOWN = 0,
    LEVEL_1_0 = 10,
    LEVEL_1_B = 9,
    LEVEL_1_1 = 11,
    LEVEL_1_2 = 12,
    LEVEL_1_3 = 13,
    LEVEL_2_0 = 20,
    LEVEL_2_1 = 21,
    LEVEL_2_2 = 22,
    LEVEL_3_0 = 30,
    LEVEL_3_1 = 31,
    LEVEL_3_2 = 32,
    LEVEL_4_0 = 40,
    LEVEL_4_1 = 41,
    LEVEL_4_2 = 42,
    LEVEL_5_0 = 50,
    LEVEL_5_1 = 51,
    LEVEL_5_2 = 52
}

/// <summary>Enumerate the type of slice mode.</summary>
internal enum SliceModeEnum
{
    SM_SINGLE_SLICE = 0,         ///< | SliceNum==1
    SM_FIXEDSLCNUM_SLICE = 1,    ///< | according to SliceNum        | enabled dynamic slicing for multi-thread
    SM_RASTER_SLICE = 2,         ///< | according to SlicesAssign    | need input of MB numbers each slice. In addition, if other constraint in SSliceArgument is presented, need to follow the constraints. Typically if MB num and slice size are both constrained, re-encoding may be involved.
    SM_SIZELIMITED_SLICE = 3,    ///< | according to SliceSize       | slicing according to size, the slicing will be dynamic(have no idea about slice_nums until encoding current frame)
    SM_RESERVED = 4
}

/// <summary>Structure for slice argument.</summary>
internal unsafe struct SSliceArgument
{
    public SliceModeEnum uiSliceMode;    ///< by default, uiSliceMode will be SM_SINGLE_SLICE
    public uint uiSliceNum;              ///< only used when uiSliceMode=1, when uiSliceNum=0 means auto design it with cpu core number
    public fixed uint uiSliceMbNum[codec_app_def.MAX_SLICES_NUM_TMP]; ///< only used when uiSliceMode=2; when =0 means setting one MB row a slice
    public uint uiSliceSizeConstraint;   ///< now only used when uiSliceMode=4
}

/// <summary>Enumerate the type of video format.</summary>
internal enum EVideoFormatSPS  // EVideoFormat is already defined/used elsewhere!
{
    VF_COMPONENT,
    VF_PAL,
    VF_NTSC,
    VF_SECAM,
    VF_MAC,
    VF_UNDEF,
    VF_NUM_ENUM
}

/// <summary>Enumerate the type of color primaries.</summary>
internal enum EColorPrimaries
{
    CP_RESERVED0,
    CP_BT709,
    CP_UNDEF,
    CP_RESERVED3,
    CP_BT470M,
    CP_BT470BG,
    CP_SMPTE170M,
    CP_SMPTE240M,
    CP_FILM,
    CP_BT2020,
    CP_NUM_ENUM
}

/// <summary>Enumerate the type of transfer characteristics.</summary>
internal enum ETransferCharacteristics
{
    TRC_RESERVED0,
    TRC_BT709,
    TRC_UNDEF,
    TRC_RESERVED3,
    TRC_BT470M,
    TRC_BT470BG,
    TRC_SMPTE170M,
    TRC_SMPTE240M,
    TRC_LINEAR,
    TRC_LOG100,
    TRC_LOG316,
    TRC_IEC61966_2_4,
    TRC_BT1361E,
    TRC_IEC61966_2_1,
    TRC_BT2020_10,
    TRC_BT2020_12,
    TRC_NUM_ENUM
}

/// <summary>Enumerate the type of color matrix.</summary>
internal enum EColorMatrix
{
    CM_GBR,
    CM_BT709,
    CM_UNDEF,
    CM_RESERVED3,
    CM_FCC,
    CM_BT470BG,
    CM_SMPTE170M,
    CM_SMPTE240M,
    CM_YCGCO,
    CM_BT2020NC,
    CM_BT2020C,
    CM_NUM_ENUM
}

/// <summary>Enumerate the type of sample aspect ratio.</summary>
internal enum ESampleAspectRatio
{
    ASP_UNSPECIFIED = 0,
    ASP_1x1 = 1,
    ASP_12x11 = 2,
    ASP_10x11 = 3,
    ASP_16x11 = 4,
    ASP_40x33 = 5,
    ASP_24x11 = 6,
    ASP_20x11 = 7,
    ASP_32x11 = 8,
    ASP_80x33 = 9,
    ASP_18x11 = 10,
    ASP_15x11 = 11,
    ASP_64x33 = 12,
    ASP_160x99 = 13,

    ASP_EXT_SAR = 255
}

/// <summary>Structure for spatial layer configuration.</summary>
internal struct SSpatialLayerConfig
{
    public int iVideoWidth;           ///< width of picture in luminance samples of a layer
    public int iVideoHeight;          ///< height of picture in luminance samples of a layer
    public float fFrameRate;          ///< frame rate specified for a layer
    public int iSpatialBitrate;       ///< target bitrate for a spatial layer, in unit of bps
    public int iMaxSpatialBitrate;    ///< maximum  bitrate for a spatial layer, in unit of bps
    public EProfileIdc uiProfileIdc;  ///< value of profile IDC (PRO_UNKNOWN for auto-detection)
    public ELevelIdc uiLevelIdc;      ///< value of profile IDC (0 for auto-detection)
    public int iDLayerQp;             ///< value of level IDC (0 for auto-detection)

    public SSliceArgument sSliceArgument;

    // Note: members bVideoSignalTypePresent through uiColorMatrix below are also defined in SWelsSPS in parameter_sets.h.
    public bool bVideoSignalTypePresent;  // false => do not write any of the following information to the header
    public byte uiVideoFormat;            // EVideoFormatSPS; 3 bits in header; 0-5 => component, kpal, ntsc, secam, mac, undef
    public bool bFullRange;               // false => analog video data range [16, 235]; true => full data range [0,255]
    public bool bColorDescriptionPresent; // false => do not write any of the following three items to the header
    public byte uiColorPrimaries;         // EColorPrimaries; 8 bits in header; 0 - 9 => ???, bt709, undef, ???, bt470m, bt470bg,
                                          //    smpte170m, smpte240m, film, bt2020
    public byte uiTransferCharacteristics; // ETransferCharacteristics; 8 bits in header; 0 - 15 => ???, bt709, undef, ???, bt470m, bt470bg, smpte170m,
                                          //   smpte240m, linear, log100, log316, iec61966-2-4, bt1361e, iec61966-2-1, bt2020-10, bt2020-12
    public byte uiColorMatrix;            // EColorMatrix; 8 bits in header (corresponds to FFmpeg "colorspace"); 0 - 10 => GBR, bt709,
                                          //   undef, ???, fcc, bt470bg, smpte170m, smpte240m, YCgCo, bt2020nc, bt2020c

    public bool bAspectRatioPresent;      ///< aspect ratio present in VUI
    public ESampleAspectRatio eAspectRatio; ///< aspect ratio idc
    public ushort sAspectRatioExtWidth;   ///< use if aspect ratio idc == 255
    public ushort sAspectRatioExtHeight;  ///< use if aspect ratio idc == 255
}

/// <summary>Encoder usage type.</summary>
internal enum EUsageType
{
    CAMERA_VIDEO_REAL_TIME,      ///< camera video for real-time communication
    SCREEN_CONTENT_REAL_TIME,    ///< screen content signal
    CAMERA_VIDEO_NON_REAL_TIME,
    SCREEN_CONTENT_NON_REAL_TIME,
    INPUT_CONTENT_TYPE_ALL,
}

/// <summary>Enumulate the complexity mode.</summary>
internal enum ECOMPLEXITY_MODE
{
    LOW_COMPLEXITY = 0,          ///< the lowest compleixty,the fastest speed,
    MEDIUM_COMPLEXITY,           ///< medium complexity, medium speed,medium quality
    HIGH_COMPLEXITY              ///< high complexity, lowest speed, high quality
}

/// <summary>Enumulate for the stategy of SPS/PPS strategy.</summary>
internal enum EParameterSetStrategy
{
    CONSTANT_ID = 0,             ///< constant id in SPS/PPS
    INCREASING_ID = 0x01,        ///< SPS/PPS id increases at each IDR
    SPS_LISTING = 0x02,          ///< using SPS in the existing list if possible
    SPS_LISTING_AND_PPS_INCREASING = 0x03,
    SPS_PPS_LISTING = 0x06,
}

/// <summary>SVC Encoding Parameters.</summary>
internal struct SEncParamBase
{
    public EUsageType iUsageType;     ///< application type; please refer to the definition of EUsageType

    public int iPicWidth;             ///< width of picture in luminance samples (the maximum of all layers if multiple spatial layers presents)
    public int iPicHeight;            ///< height of picture in luminance samples((the maximum of all layers if multiple spatial layers presents)
    public int iTargetBitrate;        ///< target bitrate desired, in unit of bps
    public RC_MODES iRCMode;          ///< rate control mode
    public float fMaxFrameRate;       ///< maximal input frame rate
}

/// <summary>SVC Encoding Parameters extention.</summary>
internal struct SEncParamExt
{
    public EUsageType iUsageType;          ///< same as in TagEncParamBase

    public int iPicWidth;                  ///< same as in TagEncParamBase
    public int iPicHeight;                 ///< same as in TagEncParamBase
    public int iTargetBitrate;             ///< same as in TagEncParamBase
    public RC_MODES iRCMode;               ///< same as in TagEncParamBase
    public float fMaxFrameRate;            ///< same as in TagEncParamBase

    public int iTemporalLayerNum;          ///< temporal layer number, max temporal layer = 4
    public int iSpatialLayerNum;           ///< spatial layer number,1<= iSpatialLayerNum <= MAX_SPATIAL_LAYER_NUM, MAX_SPATIAL_LAYER_NUM = 4
    public InlineArray4<SSpatialLayerConfig> sSpatialLayers;

    public ECOMPLEXITY_MODE iComplexityMode;
    public uint uiIntraPeriod;             ///< period of Intra frame
    public int iNumRefFrame;               ///< number of reference frame used
    public EParameterSetStrategy eSpsPpsIdStrategy;  ///< different stategy in adjust ID in SPS/PPS: 0- constant ID, 1-additional ID, 6-mapping and additional
    public bool bPrefixNalAddingCtrl;      ///< false:not use Prefix NAL; true: use Prefix NAL
    public bool bEnableSSEI;               ///< false:not use SSEI; true: use SSEI -- TODO: planning to remove the interface of SSEI
    public bool bSimulcastAVC;             ///< (when encoding more than 1 spatial layer) false: use SVC syntax for higher layers; true: use Simulcast AVC
    public int iPaddingFlag;               ///< 0:disable padding;1:padding
    public int iEntropyCodingModeFlag;     ///< 0:CAVLC  1:CABAC.

    /* rc control */
    public bool bEnableFrameSkip;          ///< False: don't skip frame even if VBV buffer overflow.True: allow skipping frames to keep the bitrate within limits
    public int iMaxBitrate;                ///< the maximum bitrate, in unit of bps, set it to UNSPECIFIED_BIT_RATE if not needed
    public int iMaxQp;                     ///< the maximum QP encoder supports
    public int iMinQp;                     ///< the minmum QP encoder supports
    public uint uiMaxNalSize;              ///< the maximum NAL size.  This value should be not 0 for dynamic slice mode

    /*LTR settings*/
    public bool bEnableLongTermReference;  ///< 1: on, 0: off
    public int iLTRRefNum;                 ///< the number of LTR(long term reference),TODO: not supported to set it arbitrary yet
    public uint iLtrMarkPeriod;            ///< the LTR marked period that is used in feedback.
    /* multi-thread settings*/
    public ushort iMultipleThreadIdc;      ///< 1 # 0: auto(dynamic imp. internal encoder); 1: multiple threads imp. disabled; lager than 1: count number of threads;
    public bool bUseLoadBalancing;         ///< only used when uiSliceMode=1 or 3, will change slicing of a picture during the run-time of multi-thread encoding, so the result of each run may be different

    /* Deblocking loop filter */
    public int iLoopFilterDisableIdc;      ///< 0: on, 1: off, 2: on except for slice boundaries
    public int iLoopFilterAlphaC0Offset;   ///< AlphaOffset: valid range [-6, 6], default 0
    public int iLoopFilterBetaOffset;      ///< BetaOffset: valid range [-6, 6], default 0
    /*pre-processing feature*/
    public bool bEnableDenoise;            ///< denoise control
    public bool bEnableBackgroundDetection; ///< background detection control //VAA_BACKGROUND_DETECTION //BGD cmd
    public bool bEnableAdaptiveQuant;      ///< adaptive quantization control
    public bool bEnableFrameCroppingFlag;  ///< enable frame cropping flag: TRUE always in application
    public bool bEnableSceneChangeDetect;

    public bool bIsLosslessLink;           ///< LTR advanced setting
    public bool bFixRCOverShoot;           ///< fix rate control overshooting
    public int iIdrBitrateRatio;           ///< the target bits of IDR is (idr_bitrate_ratio/100) * average target bit per frame.
    public bool bPsnrY;                    ///< get Y PSNR stats for the whole video sequence
    public bool bPsnrU;                    ///< get U PSNR stats for the whole video sequence
    public bool bPsnrV;                    ///< get V PSNR stats for the whole video sequence
}

/// <summary>Define a new struct to show the property of video bitstream.</summary>
internal struct SVideoProperty
{
    public uint size;                          ///< size of the struct
    public VIDEO_BITSTREAM_TYPE eVideoBsType;  ///< video stream type (AVC/SVC)
}

/// <summary>SVC Decoding Parameters, reserved here and potential applicable in the future.</summary>
internal unsafe struct SDecodingParam
{
    public sbyte* pFileNameRestructed;     ///< file name of reconstructed frame used for PSNR calculation based debug

    public uint uiCpuLoad;                 ///< CPU load
    public byte uiTargetDqLayer;           ///< setting target dq layer id

    public ERROR_CON_IDC eEcActiveIdc;     ///< whether active error concealment feature in decoder
    public bool bParseOnly;                ///< decoder for parse only, no reconstruction. When it is true, SPS/PPS size should not exceed SPS_PPS_BS_SIZE (128). Otherwise, it will return error info

    public SVideoProperty sVideoProperty;  ///< video stream property
}

/// <summary>Bitstream inforamtion of a layer being encoded.</summary>
internal unsafe struct SLayerBSInfo
{
    public byte uiTemporalId;
    public byte uiSpatialId;
    public byte uiQualityId;
    public EVideoFrameType eFrameType;
    public byte uiLayerType;

    /**
     * The sub sequence layers are ordered hierarchically based on their dependency on each other so that any picture in a layer shall not be
     * predicted from any picture on any higher layer.
    */
    public int iSubSeqId;                ///< refer to D.2.11 Sub-sequence information SEI message semantics
    public int iNalCount;                ///< count number of NAL coded already
    public int* pNalLengthInByte;        ///< length of NAL size in byte from 0 to iNalCount-1
    public byte* pBsBuf;                 ///< buffer of bitstream contained
    public fixed float rPsnr[3];         ///< PSNR values for Y/U/V
}

/// <summary>Frame bit stream info.</summary>
internal struct SFrameBSInfo
{
    public int iLayerNum;
    public InlineArray128<SLayerBSInfo> sLayerInfo;

    public EVideoFrameType eFrameType;
    public int iFrameSizeInBytes;
    public long uiTimeStamp;
}

/// <summary>Structure for source picture.</summary>
internal unsafe struct SSourcePicture
{
    public int iColorFormat;             ///< color space type
    public fixed int iStride[4];         ///< stride for each plane pData
    public PtrArray4<byte> pData;        ///< plane pData
    public int iPicWidth;                ///< luma picture width in x coordinate
    public int iPicHeight;               ///< luma picture height in y coordinate
    public long uiTimeStamp;             ///< timestamp of the source picture, unit: millisecond
    public bool bPsnrY;                  ///< get Y PSNR for this frame
    public bool bPsnrU;                  ///< get U PSNR for this frame
    public bool bPsnrV;                  ///< get V PSNR for this frame
}

/// <summary>Structure for bit rate info.</summary>
internal struct SBitrateInfo
{
    public LAYER_NUM iLayer;
    public int iBitrate;                 ///< the maximum bitrate
}

/// <summary>Structure for dump layer info.</summary>
internal unsafe struct SDumpLayer
{
    public int iLayer;
    public sbyte* pFileName;
}

/// <summary>Structure for profile info in layer.</summary>
internal struct SProfileInfo
{
    public int iLayer;
    public EProfileIdc uiProfileIdc;     ///< the profile info
}

/// <summary>Structure for level info in layer.</summary>
internal struct SLevelInfo
{
    public int iLayer;
    public ELevelIdc uiLevelIdc;         ///< the level info
}

/// <summary>Structure for dilivery status.</summary>
internal struct SDeliveryStatus
{
    public bool bDeliveryFlag;           ///< 0: the previous frame isn't delivered,1: the previous frame is delivered
    public int iDropFrameType;           ///< the frame type that is dropped; reserved
    public int iDropFrameSize;           ///< the frame size that is dropped; reserved
}

/// <summary>The capability of decoder, for SDP negotiation.</summary>
internal struct SDecoderCapability
{
    public int iProfileIdc;     ///< profile_idc
    public int iProfileIop;     ///< profile-iop
    public int iLevelIdc;       ///< level_idc
    public int iMaxMbps;        ///< max-mbps
    public int iMaxFs;          ///< max-fs
    public int iMaxCpb;         ///< max-cpb
    public int iMaxDpb;         ///< max-dpb
    public int iMaxBr;          ///< max-br
    public bool bRedPicCap;     ///< redundant-pic-cap
}

/// <summary>Structure for parse only output.</summary>
internal unsafe struct SParserBsInfo
{
    public int iNalNum;                  ///< total NAL number in current AU
    public int* pNalLenInByte;           ///< each nal length
    public byte* pDstBuff;               ///< outputted dst buffer for parsed bitstream
    public int iSpsWidthInPixel;         ///< required SPS width info
    public int iSpsHeightInPixel;        ///< required SPS height info
    public ulong uiInBsTimeStamp;        ///< input BS timestamp
    public ulong uiOutBsTimeStamp;       ///< output BS timestamp
}

/// <summary>Structure for encoder statistics.</summary>
internal struct SEncoderStatistics
{
    public uint uiWidth;                 ///< the width of encoded frame
    public uint uiHeight;                ///< the height of encoded frame
    //following standard, will be 16x aligned, if there are multiple spatial, this is of the highest
    public float fAverageFrameSpeedInMs; ///< average_Encoding_Time

    // rate control related
    public float fAverageFrameRate;      ///< the average frame rate in, calculate since encoding starts, supposed that the input timestamp is in unit of ms
    public float fLatestFrameRate;       ///< the frame rate in, in the last second, supposed that the input timestamp is in unit of ms (? useful for checking BR, but is it easy to calculate?
    public uint uiBitRate;               ///< sendrate in Bits per second, calculated within the set time-window
    public uint uiAverageFrameQP;        ///< the average QP of last encoded frame

    public uint uiInputFrameCount;       ///< number of frames
    public uint uiSkippedFrameCount;     ///< number of frames

    public uint uiResolutionChangeTimes; ///< uiResolutionChangeTimes
    public uint uiIDRReqNum;             ///< number of IDR requests
    public uint uiIDRSentNum;            ///< number of actual IDRs sent
    public uint uiLTRSentNum;            ///< number of LTR sent/marked

    public long iStatisticsTs;           ///< Timestamp of updating the statistics

    public ulong iTotalEncodedBytes;
    public ulong iLastStatisticsBytes;
    public ulong iLastStatisticsFrameCount;
}

/// <summary>Structure for decoder statistics.</summary>
internal struct SDecoderStatistics
{
    public uint uiWidth;                 ///< the width of encode/decode frame
    public uint uiHeight;                ///< the height of encode/decode frame
    public float fAverageFrameSpeedInMs; ///< average_Decoding_Time
    public float fActualAverageFrameSpeedInMs; ///< actual average_Decoding_Time, including freezing pictures
    public uint uiDecodedFrameCount;     ///< number of frames
    public uint uiResolutionChangeTimes; ///< uiResolutionChangeTimes
    public uint uiIDRCorrectNum;         ///< number of correct IDR received
    //EC on related
    public uint uiAvgEcRatio;            ///< when EC is on, the average ratio of total EC areas, can be an indicator of reconstruction quality
    public uint uiAvgEcPropRatio;        ///< when EC is on, the rough average ratio of propogate EC areas, can be an indicator of reconstruction quality
    public uint uiEcIDRNum;              ///< number of actual unintegrity IDR or not received but eced
    public uint uiEcFrameNum;            ///<
    public uint uiIDRLostNum;            ///< number of whole lost IDR
    public uint uiFreezingIDRNum;        ///< number of freezing IDR with error (partly received), under resolution change
    public uint uiFreezingNonIDRNum;     ///< number of freezing non-IDR with error
    public int iAvgLumaQp;               ///< average luma QP. default: -1, no correct frame outputted
    public int iSpsReportErrorNum;       ///< number of Sps Invalid report
    public int iSubSpsReportErrorNum;    ///< number of SubSps Invalid report
    public int iPpsReportErrorNum;       ///< number of Pps Invalid report
    public int iSpsNoExistNalNum;        ///< number of Sps NoExist Nal
    public int iSubSpsNoExistNalNum;     ///< number of SubSps NoExist Nal
    public int iPpsNoExistNalNum;        ///< number of Pps NoExist Nal

    public uint uiProfile;               ///< Profile idc in syntax
    public uint uiLevel;                 ///< level idc according to Annex A-1

    public int iCurrentActiveSpsId;      ///< current active SPS id
    public int iCurrentActivePpsId;      ///< current active PPS id

    public uint iStatisticsLogInterval;  ///< frame interval of statistics log
}

/// <summary>Structure for sample aspect ratio (SAR) info in VUI.</summary>
internal struct SVuiSarInfo
{
    public uint uiSarWidth;              ///< SAR width
    public uint uiSarHeight;             ///< SAR height
    public bool bOverscanAppropriateFlag; ///< SAR overscan flag
}
