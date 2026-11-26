//-----------------------------------------------------------------------------
// Filename: GlobalUsings.cs
//
// Description: Project-wide usings that let the ported code read like the
// OpenH264 C++ it came from:
//  - the typedefs.h integer names (int32_t, uint8_t, ...),
//  - bare enum members (dsErrorFree rather than DECODING_STATE.dsErrorFree),
//  - bare calls to the ported free functions, which live in the static partial
//    classes WelsCommon / WelsDec (one per upstream C++ namespace).
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 24 Nov 2026  Claude          Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

global using System;

// codec/common/inc/typedefs.h
global using int8_t = System.SByte;
global using uint8_t = System.Byte;
global using int16_t = System.Int16;
global using uint16_t = System.UInt16;
global using int32_t = System.Int32;
global using uint32_t = System.UInt32;
global using int64_t = System.Int64;
global using uint64_t = System.UInt64;
global using intX_t = nint;
global using uintptr_t = nuint;

// codec/api/wels enums, members usable unqualified as in C.
global using static SIPSorcery.H264.EVideoFormatType;
global using static SIPSorcery.H264.EVideoFrameType;
global using static SIPSorcery.H264.CM_RETURN;
global using static SIPSorcery.H264.ENalUnitType;
global using static SIPSorcery.H264.ENalPriority;
global using static SIPSorcery.H264.DECODING_STATE;
global using static SIPSorcery.H264.ENCODER_OPTION;
global using static SIPSorcery.H264.DECODER_OPTION;
global using static SIPSorcery.H264.ERROR_CON_IDC;
global using static SIPSorcery.H264.FEEDBACK_VCL_NAL_IN_AU;
global using static SIPSorcery.H264.LAYER_TYPE;
global using static SIPSorcery.H264.LAYER_NUM;
global using static SIPSorcery.H264.VIDEO_BITSTREAM_TYPE;
global using static SIPSorcery.H264.KEY_FRAME_REQUEST_TYPE;
global using static SIPSorcery.H264.RC_MODES;
global using static SIPSorcery.H264.EProfileIdc;
global using static SIPSorcery.H264.ELevelIdc;
global using static SIPSorcery.H264.SliceModeEnum;
global using static SIPSorcery.H264.EVideoFormatSPS;
global using static SIPSorcery.H264.EColorPrimaries;
global using static SIPSorcery.H264.ETransferCharacteristics;
global using static SIPSorcery.H264.EColorMatrix;
global using static SIPSorcery.H264.ESampleAspectRatio;
global using static SIPSorcery.H264.EUsageType;
global using static SIPSorcery.H264.ECOMPLEXITY_MODE;
global using static SIPSorcery.H264.EParameterSetStrategy;

// #defines and anonymous enums from the api headers.
global using static SIPSorcery.H264.codec_def;
global using static SIPSorcery.H264.codec_app_def;
global using static SIPSorcery.H264.codec_ver;

// codec/common/inc/wels_common_defs.h enums.
global using static SIPSorcery.H264.EWelsNalUnitType;
global using static SIPSorcery.H264.EWelsNalRefIdc;
global using static SIPSorcery.H264.EVclType;
global using static SIPSorcery.H264.EWelsSliceType;
global using static SIPSorcery.H264.ESliceTypeExt;
global using static SIPSorcery.H264.EListIndex;
global using static SIPSorcery.H264.EMvComp;
global using static SIPSorcery.H264.EChromaComp;
global using static SIPSorcery.H264.EMmcoCode;
global using static SIPSorcery.H264.EVuiVideoFormat;
global using static SIPSorcery.H264.ESceneChangeIdc;
global using static SIPSorcery.H264.EStaticBlockIdc;

// libc (memcpy, memset, ...), see common/port_helpers.cs.
global using static SIPSorcery.H264.CRuntime;

// Ported free functions, one static partial class per upstream namespace.
// Function-pointer typedefs are `global using unsafe` aliases declared in the
// file that ports their header (for example PExpandPictureFunc in expand_pic.cs).
global using static SIPSorcery.H264.WelsCommon;
