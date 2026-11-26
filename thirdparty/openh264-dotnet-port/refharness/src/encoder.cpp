// Encoder modes of the h264ref harness (E1).
//
//   h264ref encoder-conformance <res-dir>
//       Encode every row of upstream's test/api/encoder_test.cpp table
//       (kFileParamArray) the way BaseEncoderTest does, and compare the SHA1s.
//       Shows the C-only build is the encoder oracle, as `conformance` does for
//       the decoder (reference/encoder-conformance.tsv).
//
//   h264ref paramsets
//       The parameter sets the encoder writes, for a table of configurations
//       (reference/paramsets.tsv). Each configuration is a list of key=value
//       settings applied on top of GetDefaultParams (api=ext) or a zeroed
//       SEncParamBase (api=base), then a script of operations:
//         P  EncodeParameterSets
//         I  ForceIntraFrame(true) then EncodeFrame
//         F  EncodeFrame
//       For each operation the harness prints the frame type and every
//       non-VCL layer it produced (SPS, subset SPS and PPS NAL units, with
//       their start codes), and once per configuration the parameters
//       GetOption(ENCODER_OPTION_SVC_ENCODE_PARAM_EXT) reports after
//       initialisation. The C# test applies the same settings to its port and
//       compares the bytes.
//
//   h264ref idr
//       E4b: the first frame (an IDR) the encoder writes through the real API
//       for a table of fixed QP configurations (kIdrConfigs, reference/idr.tsv):
//       the settings as for paramsets, a synthetic source picture (FillContent),
//       then the return codes, the frame type, every layer's NAL units (length
//       and SHA1 prefix) and the SHA1 of the whole frame. The C# test
//       (IdrTests.cs) encodes the same picture with its port and compares.
//
//   h264ref idr-dump <name> <out>
//       Writes that configuration's frame to <out>, to compare with the C#
//       bytes when a row does not match.
//
//   h264ref stream
//       E5b: P streams, frame by frame, through the real API for a table of
//       fixed QP configurations (kStreamConfigs, reference/stream.tsv): the
//       settings as for idr with the analysis features off, synthetic moving
//       content (StreamSample), then for each frame the return code, the frame
//       type, the temporal ids, the layers and the SHA1 of its bytes, and the
//       SHA1 of the whole stream. The C# test (StreamTests.cs) encodes the same
//       frames with its port and compares.
//
//   h264ref stream-dump <name> <out>
//       Writes that configuration's stream to <out>.

#include <cfloat>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <fstream>
#include <map>
#include <sstream>
#include <string>
#include <vector>

#include "codec_api.h"
#include "codec_app_def.h"
#include "codec_def.h"
#include "codec_ver.h"
#include "sha1.h"

namespace {

// ---------------------------------------------------------------------------
// encoder-conformance
// ---------------------------------------------------------------------------

// Copied from openh264 test/api/encoder_test.cpp (kFileParamArray) at the pinned commit.
struct EncodeFileParam {
  const char* pkcFileName;
  const char* pkcHashStr[2];
  EUsageType eUsageType;
  int iWidth;
  int iHeight;
  float fFrameRate;
  SliceModeEnum eSliceMode;
  bool bDenoise;
  int  iLayerNum;
  bool bLossless;
  bool bEnableLtr;
  bool bCabac;
};

const EncodeFileParam kFileParamArray[] = {
  {"CiscoVT2people_320x192_12fps.yuv", {"672a52fb6b6e6d52b5b3f3480d13d44e88481fb9"}, CAMERA_VIDEO_REAL_TIME, 320, 192, 12.0f, SM_SINGLE_SLICE, false, 1, false, false, false},
  {"CiscoVT2people_160x96_6fps.yuv", {"08ade1853e4e49d50be675393780e75519586143"}, CAMERA_VIDEO_REAL_TIME, 160, 96, 6.0f, SM_SINGLE_SLICE, false, 1, false, false, false},
  {"Static_152_100.yuv", {"e60f12e3c24500d4306d812b0811d3c21855dd1c"}, CAMERA_VIDEO_REAL_TIME, 152, 100, 6.0f, SM_SINGLE_SLICE, false, 1, false, false, false},
  {"CiscoVT2people_320x192_12fps.yuv", {"266de2d059a00ad2f28304e7eb378543ea7d85ab"}, CAMERA_VIDEO_REAL_TIME, 320, 192, 12.0f, SM_RASTER_SLICE, false, 1, false, false, false},
  {"CiscoVT2people_320x192_12fps.yuv", {"913e49c787a0abdb378e9bc55bcffc27da89b965"}, CAMERA_VIDEO_REAL_TIME, 320, 192, 12.0f, SM_SINGLE_SLICE, true, 1, false, false, false},
  {"CiscoVT2people_320x192_12fps.yuv", {"e626f7efa3d54da15794407c15b7c694f7ddd383", "eb4adc831563ce4f02f2942f52c992da760b4113"}, CAMERA_VIDEO_REAL_TIME, 320, 192, 12.0f, SM_SINGLE_SLICE, false, 2, false, false, false},
  {"Cisco_Absolute_Power_1280x720_30fps.yuv", {"53f5681a0c2b7068f4edc94538d6133a657df25d"}, CAMERA_VIDEO_REAL_TIME, 1280, 720, 30.0f, SM_SIZELIMITED_SLICE, false, 1, false, false, false},
  {"Cisco_Absolute_Power_1280x720_30fps.yuv", {"0d4bf6a3b6f09d6de7bbce6daf8002c614ee6241", "a32db3cfa66568e231d1f580d239d6468d26ce9a"}, CAMERA_VIDEO_REAL_TIME, 1280, 720, 30.0f, SM_SINGLE_SLICE, false, 4, false, false, false},
  {"CiscoVT2people_320x192_12fps.yuv", {"fd57470eebb9b334e8edcb8b47f7fb5b5868f111"}, SCREEN_CONTENT_REAL_TIME, 320, 192, 12.0f, SM_SINGLE_SLICE, false, 1, false, false, false},
  {"CiscoVT2people_160x96_6fps.yuv", {"5f63e723c3ec82fad186b48fcbcfb54730ce3b26"}, SCREEN_CONTENT_REAL_TIME, 160, 96, 6.0f, SM_SINGLE_SLICE, false, 1, false, false, false},
  {"Static_152_100.yuv", {"e77a5b0ffb48753556e617544616fb06a049e9be"}, SCREEN_CONTENT_REAL_TIME, 152, 100, 6.0f, SM_SINGLE_SLICE, false, 1, false, false, false},
  {"Cisco_Absolute_Power_1280x720_30fps.yuv", {"f01e41426ca49932a8f1f67ad59a1700a3fa7fee"}, SCREEN_CONTENT_REAL_TIME, 1280, 720, 30.0f, SM_SIZELIMITED_SLICE, false, 1, false, false, false},
  {"Cisco_Absolute_Power_1280x720_30fps.yuv", {"4684962979bc306e35de93bed58cc84938abcdee"}, SCREEN_CONTENT_REAL_TIME, 1280, 720, 30.0f, SM_SIZELIMITED_SLICE, false, 1, true, true, false},
  {"CiscoVT2people_320x192_12fps.yuv", {"d31a72395a4ca760c5b86a06901a2557e0373e76"}, CAMERA_VIDEO_REAL_TIME, 320, 192, 12.0f, SM_SINGLE_SLICE, false, 1, false, false, true},
  {"Cisco_Absolute_Power_1280x720_30fps.yuv", {"8bef37fa5965d5e650c1170d938423269f7406ac"}, CAMERA_VIDEO_REAL_TIME, 1280, 720, 30.0f, SM_SIZELIMITED_SLICE, false, 1, false, false, true},
  {"Cisco_Absolute_Power_1280x720_30fps.yuv", {"c5cb4a6f55c10485aa90f8b237fcb8697ba70d43"}, CAMERA_VIDEO_REAL_TIME, 1280, 720, 30.0f, SM_FIXEDSLCNUM_SLICE, false, 1, false, false, true},
};

// test/api/BaseEncoderTest.cpp InitWithParam, verbatim apart from the names.
int InitWithParam (ISVCEncoder* encoder, const EncodeFileParam& p) {
  SliceModeEnum eSliceMode = p.eSliceMode;
  bool bBaseParamFlag = (SM_SINGLE_SLICE == eSliceMode && !p.bDenoise && p.iLayerNum == 1 && !p.bLossless
                         && !p.bEnableLtr && !p.bCabac) ? true : false;
  if (bBaseParamFlag) {
    SEncParamBase param;
    memset (&param, 0, sizeof (SEncParamBase));
    param.iUsageType     = p.eUsageType;
    param.fMaxFrameRate  = p.fFrameRate;
    param.iPicWidth      = p.iWidth;
    param.iPicHeight     = p.iHeight;
    param.iTargetBitrate = 5000000;
    return encoder->Initialize (&param);
  } else {
    SEncParamExt param;
    encoder->GetDefaultParams (&param);
    param.iUsageType       = p.eUsageType;
    param.fMaxFrameRate    = p.fFrameRate;
    param.iPicWidth        = p.iWidth;
    param.iPicHeight       = p.iHeight;
    param.iTargetBitrate   = 5000000;
    param.bEnableDenoise   = p.bDenoise;
    param.iSpatialLayerNum = p.iLayerNum;
    param.bIsLosslessLink  = p.bLossless;
    param.bEnableLongTermReference = p.bEnableLtr;
    param.iEntropyCodingModeFlag   = p.bCabac ? 1 : 0;
    if (eSliceMode != SM_SINGLE_SLICE && eSliceMode != SM_SIZELIMITED_SLICE)
      param.iMultipleThreadIdc = 2;
    for (int i = 0; i < param.iSpatialLayerNum; i++) {
      param.sSpatialLayers[i].iVideoWidth     = p.iWidth  >> (param.iSpatialLayerNum - 1 - i);
      param.sSpatialLayers[i].iVideoHeight    = p.iHeight >> (param.iSpatialLayerNum - 1 - i);
      param.sSpatialLayers[i].fFrameRate      = p.fFrameRate;
      param.sSpatialLayers[i].iSpatialBitrate = param.iTargetBitrate;
      param.sSpatialLayers[i].sSliceArgument.uiSliceMode = eSliceMode;
      if (eSliceMode == SM_SIZELIMITED_SLICE) {
        param.sSpatialLayers[i].sSliceArgument.uiSliceSizeConstraint = 600;
        param.uiMaxNalSize = 1500;
        param.iMultipleThreadIdc = 4;
        param.bUseLoadBalancing = false;
      }
      if (eSliceMode == SM_FIXEDSLCNUM_SLICE) {
        param.sSpatialLayers[i].sSliceArgument.uiSliceNum = 4;
        param.iMultipleThreadIdc = 4;
        param.bUseLoadBalancing = false;
      }
      if (param.iEntropyCodingModeFlag) {
        param.sSpatialLayers[i].uiProfileIdc = PRO_MAIN;
      }
    }
    param.iTargetBitrate *= param.iSpatialLayerNum;
    return encoder->InitializeExt (&param);
  }
}

const char* SliceModeName (SliceModeEnum m) {
  switch (m) {
  case SM_SINGLE_SLICE: return "single";
  case SM_FIXEDSLCNUM_SLICE: return "fixedslcnum";
  case SM_RASTER_SLICE: return "raster";
  case SM_SIZELIMITED_SLICE: return "sizelimited";
  default: return "?";
  }
}

std::string Hex (const unsigned char* d, int n) {
  static const char* kHex = "0123456789abcdef";
  std::string s;
  for (int i = 0; i < n; i++) {
    s += kHex[d[i] >> 4];
    s += kHex[d[i] & 15];
  }
  return s;
}

}  // namespace

int EncoderConformance (const char* resDir) {
  printf ("# %s, C-only reference build. Upstream test/api/encoder_test.cpp table.\n", g_strCodecVer);
  printf ("# result\tframes\tfile\tusage\tsize\tfps\tslices\tdenoise\tlayers\tlossless\tltr\tcabac\tsha1\texpected\n");
  int failures = 0;
  for (const EncodeFileParam& p : kFileParamArray) {
    std::string path = std::string (resDir) + "/" + p.pkcFileName;
    std::ifstream in (path, std::ios::binary);
    if (!in) {
      fprintf (stderr, "cannot open %s\n", path.c_str());
      return 1;
    }
    ISVCEncoder* encoder = NULL;
    if (WelsCreateSVCEncoder (&encoder) != 0) return 1;
    int iTraceLevel = WELS_LOG_ERROR;
    encoder->SetOption (ENCODER_OPTION_TRACE_LEVEL, &iTraceLevel);
    int rv = InitWithParam (encoder, p);
    if (rv != cmResultSuccess) {
      fprintf (stderr, "init failed %d for %s\n", rv, p.pkcFileName);
      return 1;
    }

    // test/api/BaseEncoderTest.cpp EncodeStream.
    const int frameSize = p.iWidth * p.iHeight * 3 / 2;
    std::vector<unsigned char> buf (frameSize);
    SFrameBSInfo info;
    memset (&info, 0, sizeof (SFrameBSInfo));
    SSourcePicture pic;
    memset (&pic, 0, sizeof (SSourcePicture));
    pic.iPicWidth    = p.iWidth;
    pic.iPicHeight   = p.iHeight;
    pic.iColorFormat = videoFormatI420;
    pic.iStride[0]   = pic.iPicWidth;
    pic.iStride[1]   = pic.iStride[2] = pic.iPicWidth >> 1;
    pic.pData[0]     = buf.data();
    pic.pData[1]     = pic.pData[0] + p.iWidth * p.iHeight;
    pic.pData[2]     = pic.pData[1] + (p.iWidth * p.iHeight >> 2);

    SHA1Context ctx;
    SHA1Reset (&ctx);
    int frames = 0;
    while (in.read ((char*)buf.data(), frameSize) && in.gcount() == frameSize) {
      rv = encoder->EncodeFrame (&pic, &info);
      if (rv != cmResultSuccess) {
        fprintf (stderr, "EncodeFrame failed %d for %s\n", rv, p.pkcFileName);
        return 1;
      }
      if (info.eFrameType != videoFrameTypeSkip) {
        // encoder_test.cpp UpdateHashFromFrame.
        for (int i = 0; i < info.iLayerNum; ++i) {
          const SLayerBSInfo& layerInfo = info.sLayerInfo[i];
          int layerSize = 0;
          for (int j = 0; j < layerInfo.iNalCount; ++j) {
            layerSize += layerInfo.pNalLengthInByte[j];
          }
          SHA1Input (&ctx, layerInfo.pBsBuf, layerSize);
        }
      }
      frames++;
    }
    unsigned char digest[SHA_DIGEST_LENGTH];
    SHA1Result (&ctx, digest);
    std::string got = Hex (digest, SHA_DIGEST_LENGTH);
    bool match = false;
    std::string expected;
    for (const char* h : p.pkcHashStr) {
      if (h == NULL) continue;
      if (!expected.empty()) expected += ",";
      expected += h;
      if (got == h) match = true;
    }
    if (!match) failures++;
    printf ("%s\t%d\t%s\t%s\t%dx%d\t%g\t%s\t%d\t%d\t%d\t%d\t%d\t%s\t%s\n", match ? "PASS" : "FAIL", frames,
            p.pkcFileName, p.eUsageType == CAMERA_VIDEO_REAL_TIME ? "camera" : "screen", p.iWidth, p.iHeight,
            p.fFrameRate, SliceModeName (p.eSliceMode), p.bDenoise, p.iLayerNum, p.bLossless, p.bEnableLtr, p.bCabac,
            got.c_str(), expected.c_str());
    encoder->Uninitialize();
    WelsDestroySVCEncoder (encoder);
  }
  return failures ? 1 : 0;
}

namespace {

// ---------------------------------------------------------------------------
// paramsets
// ---------------------------------------------------------------------------

struct ParamConfig {
  const char* name;
  const char* settings;   // ';' separated key=value, see ApplySetting
  const char* ops;        // P, I and F, see the file header
};

// api=base: Initialize(SEncParamBase) from a zeroed struct, as BaseEncoderTest does for
// the three single-slice camera hashes. api=ext: InitializeExt on top of GetDefaultParams.
// Layer fields are L<n>.<field>.
const ParamConfig kParamConfigs[] = {
  // The upstream hash configurations (BaseEncoderTest's Initialize path).
  {"base-320x192-12fps", "api=base;iUsageType=0;iPicWidth=320;iPicHeight=192;fMaxFrameRate=12;iTargetBitrate=5000000", "PIFFI"},
  {"base-160x96-6fps", "api=base;iUsageType=0;iPicWidth=160;iPicHeight=96;fMaxFrameRate=6;iTargetBitrate=5000000", "PIF"},
  {"base-152x100-6fps", "api=base;iUsageType=0;iPicWidth=152;iPicHeight=100;fMaxFrameRate=6;iTargetBitrate=5000000", "PIF"},
  {"base-screen-320x192", "api=base;iUsageType=1;iPicWidth=320;iPicHeight=192;fMaxFrameRate=12;iTargetBitrate=5000000", "PIF"},
  {"base-bitrate-mode", "api=base;iUsageType=0;iPicWidth=640;iPicHeight=480;fMaxFrameRate=30;iTargetBitrate=800000;iRCMode=1", "PIFI"},

  // As WebRTC's H264EncoderImpl configures OpenH264: bitrate mode, frame skipping,
  // SPS_LISTING, and SM_FIXEDSLCNUM_SLICE with one slice (which validation turns into
  // SM_SINGLE_SLICE).
  {"webrtc-640x480", "api=ext;iUsageType=0;iPicWidth=640;iPicHeight=480;iTargetBitrate=1000000;iMaxBitrate=0;iRCMode=1;fMaxFrameRate=30;bEnableFrameSkip=1;uiIntraPeriod=3000;uiMaxNalSize=0;iMultipleThreadIdc=1;bEnableDenoise=0;eSpsPpsIdStrategy=2;iSpatialLayerNum=1;iTemporalLayerNum=1;L0.iVideoWidth=640;L0.iVideoHeight=480;L0.fFrameRate=30;L0.iSpatialBitrate=1000000;L0.iMaxSpatialBitrate=0;L0.uiSliceMode=1;L0.uiSliceNum=1", "PIFFIFI"},
  {"webrtc-1280x720", "api=ext;iUsageType=0;iPicWidth=1280;iPicHeight=720;iTargetBitrate=2500000;iMaxBitrate=0;iRCMode=1;fMaxFrameRate=30;bEnableFrameSkip=1;uiIntraPeriod=3000;eSpsPpsIdStrategy=2;L0.iVideoWidth=1280;L0.iVideoHeight=720;L0.fFrameRate=30;L0.iSpatialBitrate=2500000;L0.uiSliceMode=1;L0.uiSliceNum=1", "PIFI"},
  {"webrtc-320x180", "api=ext;iUsageType=0;iPicWidth=320;iPicHeight=180;iTargetBitrate=300000;iRCMode=1;fMaxFrameRate=15;bEnableFrameSkip=1;eSpsPpsIdStrategy=2;L0.iVideoWidth=320;L0.iVideoHeight=180;L0.fFrameRate=15;L0.iSpatialBitrate=300000;L0.uiSliceMode=1;L0.uiSliceNum=1", "PIFI"},
  {"webrtc-l1t2", "api=ext;iUsageType=0;iPicWidth=640;iPicHeight=360;iTargetBitrate=800000;iRCMode=1;fMaxFrameRate=30;eSpsPpsIdStrategy=2;iTemporalLayerNum=2;L0.iVideoWidth=640;L0.iVideoHeight=360;L0.fFrameRate=30;L0.iSpatialBitrate=800000", "PIFFFI"},
  {"webrtc-l1t3", "api=ext;iUsageType=0;iPicWidth=640;iPicHeight=360;iTargetBitrate=800000;iRCMode=1;fMaxFrameRate=30;eSpsPpsIdStrategy=2;iTemporalLayerNum=3;L0.iVideoWidth=640;L0.iVideoHeight=360;L0.fFrameRate=30;L0.iSpatialBitrate=800000", "PIFFFFI"},

  // The five SPS/PPS id strategies.
  {"strategy-constant", "api=ext;iPicWidth=640;iPicHeight=360;iTargetBitrate=800000;iRCMode=1;fMaxFrameRate=30;eSpsPpsIdStrategy=0", "PIFIPIFI"},
  {"strategy-increasing", "api=ext;iPicWidth=640;iPicHeight=360;iTargetBitrate=800000;iRCMode=1;fMaxFrameRate=30;eSpsPpsIdStrategy=1", "PIFIPIFI"},
  {"strategy-sps-listing", "api=ext;iPicWidth=640;iPicHeight=360;iTargetBitrate=800000;iRCMode=1;fMaxFrameRate=30;eSpsPpsIdStrategy=2", "PIFIPIFI"},
  {"strategy-sps-listing-pps-increasing", "api=ext;iPicWidth=640;iPicHeight=360;iTargetBitrate=800000;iRCMode=1;fMaxFrameRate=30;eSpsPpsIdStrategy=3", "PIFIPIFI"},
  {"strategy-sps-pps-listing", "api=ext;iPicWidth=640;iPicHeight=360;iTargetBitrate=800000;iRCMode=1;fMaxFrameRate=30;eSpsPpsIdStrategy=6", "PIFIPIFI"},
  {"strategy-invalid", "api=ext;iPicWidth=640;iPicHeight=360;iTargetBitrate=800000;iRCMode=1;fMaxFrameRate=30;eSpsPpsIdStrategy=5", "PI"},
  // Enough IDRs for the increasing ids to wrap: SPS ids at 32, PPS ids at 57.
  {"strategy-increasing-wrap", "api=ext;iPicWidth=64;iPicHeight=64;iRCMode=-1;fMaxFrameRate=30;eSpsPpsIdStrategy=1",
   "PIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIPI"},
  {"strategy-sps-listing-pps-increasing-wrap", "api=ext;iPicWidth=64;iPicHeight=64;iRCMode=-1;fMaxFrameRate=30;eSpsPpsIdStrategy=3",
   "IIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIPI"},
  {"strategy-sps-pps-listing-idrs", "api=ext;iPicWidth=64;iPicHeight=64;iRCMode=-1;fMaxFrameRate=30;eSpsPpsIdStrategy=6", "IIIPII"},

  // Levels.
  {"level-1", "api=ext;iPicWidth=128;iPicHeight=96;iTargetBitrate=60000;iRCMode=1;fMaxFrameRate=7.5", "PI"},
  {"level-1b", "api=ext;iPicWidth=128;iPicHeight=96;iTargetBitrate=100000;iRCMode=1;fMaxFrameRate=7.5", "PI"},
  {"level-1b-main", "api=ext;iPicWidth=128;iPicHeight=96;iTargetBitrate=100000;iRCMode=1;fMaxFrameRate=7.5;L0.uiProfileIdc=77", "PI"},
  {"level-auto-1080p30", "api=ext;iPicWidth=1920;iPicHeight=1080;iTargetBitrate=4000000;iRCMode=1;fMaxFrameRate=30", "PI"},
  {"level-auto-4k", "api=ext;iPicWidth=3840;iPicHeight=2160;iTargetBitrate=20000000;iRCMode=1;fMaxFrameRate=30", "P"},
  {"level-explicit-low", "api=ext;iPicWidth=1280;iPicHeight=720;iTargetBitrate=2000000;iRCMode=1;fMaxFrameRate=30;L0.uiLevelIdc=30", "P"},
  {"level-explicit-high", "api=ext;iPicWidth=640;iPicHeight=480;iTargetBitrate=1000000;iRCMode=1;fMaxFrameRate=30;L0.uiLevelIdc=41", "P"},
  {"level-explicit-invalid", "api=ext;iPicWidth=640;iPicHeight=480;iTargetBitrate=1000000;iRCMode=1;fMaxFrameRate=30;L0.uiLevelIdc=25", "P"},
  {"level-max-bitrate", "api=ext;iPicWidth=1280;iPicHeight=720;iTargetBitrate=2000000;iMaxBitrate=30000000;iRCMode=1;fMaxFrameRate=30;L0.uiLevelIdc=31;L0.iMaxSpatialBitrate=30000000", "P"},
  {"level-max-bitrate-unspecified", "api=ext;iPicWidth=640;iPicHeight=480;iTargetBitrate=1000000;iRCMode=1;fMaxFrameRate=30;L0.uiLevelIdc=22", "P"},
  {"level-dpb-limit", "api=ext;iPicWidth=1920;iPicHeight=1088;iTargetBitrate=4000000;iRCMode=1;fMaxFrameRate=30;iNumRefFrame=4;L0.uiLevelIdc=40", "P"},

  // Profiles.
  {"profile-main", "api=ext;iPicWidth=640;iPicHeight=480;iTargetBitrate=1000000;iRCMode=1;fMaxFrameRate=30;L0.uiProfileIdc=77", "PI"},
  {"profile-high-cabac", "api=ext;iPicWidth=640;iPicHeight=480;iTargetBitrate=1000000;iRCMode=1;fMaxFrameRate=30;iEntropyCodingModeFlag=1", "PI"},
  {"profile-baseline-no-cabac", "api=ext;iPicWidth=640;iPicHeight=480;iTargetBitrate=1000000;iRCMode=1;fMaxFrameRate=30;iEntropyCodingModeFlag=1;L0.uiProfileIdc=66", "PI"},
  {"profile-invalid", "api=ext;iPicWidth=640;iPicHeight=480;iTargetBitrate=1000000;iRCMode=1;fMaxFrameRate=30;L0.uiProfileIdc=88", "P"},

  // VUI.
  {"vui-bt709-full", "api=ext;iPicWidth=640;iPicHeight=480;iTargetBitrate=1000000;iRCMode=1;fMaxFrameRate=30;L0.bVideoSignalTypePresent=1;L0.uiVideoFormat=0;L0.bFullRange=1;L0.bColorDescriptionPresent=1;L0.uiColorPrimaries=1;L0.uiTransferCharacteristics=1;L0.uiColorMatrix=1", "PI"},
  {"vui-signal-no-colour", "api=ext;iPicWidth=640;iPicHeight=480;iTargetBitrate=1000000;iRCMode=1;fMaxFrameRate=30;L0.bVideoSignalTypePresent=1;L0.uiVideoFormat=5;L0.bFullRange=0;L0.bColorDescriptionPresent=0", "P"},
  {"vui-sar-ext", "api=ext;iPicWidth=640;iPicHeight=480;iTargetBitrate=1000000;iRCMode=1;fMaxFrameRate=30;L0.bAspectRatioPresent=1;L0.eAspectRatio=255;L0.sAspectRatioExtWidth=4;L0.sAspectRatioExtHeight=3", "P"},
  {"vui-sar-16x11", "api=ext;iPicWidth=640;iPicHeight=480;iTargetBitrate=1000000;iRCMode=1;fMaxFrameRate=30;L0.bAspectRatioPresent=1;L0.eAspectRatio=4", "P"},

  // Reference counts, LTR, intra period.
  {"ref-4", "api=ext;iPicWidth=640;iPicHeight=480;iTargetBitrate=1000000;iRCMode=1;fMaxFrameRate=30;iNumRefFrame=4", "PIF"},
  {"ref-out-of-range", "api=ext;iPicWidth=640;iPicHeight=480;iTargetBitrate=1000000;iRCMode=1;fMaxFrameRate=30;iNumRefFrame=40", "P"},
  {"ltr", "api=ext;iPicWidth=640;iPicHeight=480;iTargetBitrate=1000000;iRCMode=1;fMaxFrameRate=30;bEnableLongTermReference=1", "PIF"},
  {"ltr-screen-lossless", "api=ext;iUsageType=1;iPicWidth=640;iPicHeight=480;iTargetBitrate=1000000;iRCMode=1;fMaxFrameRate=30;bEnableLongTermReference=1;bIsLosslessLink=1", "PIF"},
  {"intra-period-4", "api=ext;iPicWidth=176;iPicHeight=144;iTargetBitrate=500000;iRCMode=1;fMaxFrameRate=30;uiIntraPeriod=4;eSpsPpsIdStrategy=1", "PFFFFFFFFF"},
  {"intra-period-rounded", "api=ext;iPicWidth=176;iPicHeight=144;iTargetBitrate=500000;iRCMode=1;fMaxFrameRate=30;uiIntraPeriod=3;iTemporalLayerNum=2", "PFFFFFF"},

  // Sizes and cropping.
  {"crop-odd-330x250", "api=ext;iPicWidth=330;iPicHeight=250;iTargetBitrate=500000;iRCMode=1;fMaxFrameRate=30", "PI"},
  {"crop-off-330x250", "api=ext;iPicWidth=330;iPicHeight=250;iTargetBitrate=500000;iRCMode=1;fMaxFrameRate=30;bEnableFrameCroppingFlag=0", "PI"},
  {"tiny-16x16", "api=ext;iPicWidth=16;iPicHeight=16;iTargetBitrate=100000;iRCMode=1;fMaxFrameRate=30", "PIF"},
  {"tiny-18x10", "api=ext;iPicWidth=18;iPicHeight=10;iTargetBitrate=100000;iRCMode=1;fMaxFrameRate=30", "P"},

  // Rate control modes and QP ranges.
  {"rc-off", "api=ext;iPicWidth=320;iPicHeight=240;iRCMode=-1;fMaxFrameRate=30", "PIF"},
  {"rc-quality-qp", "api=ext;iPicWidth=320;iPicHeight=240;iTargetBitrate=500000;iRCMode=0;fMaxFrameRate=30;iMinQp=0;iMaxQp=0", "PI"},
  {"rc-timestamp", "api=ext;iPicWidth=320;iPicHeight=240;iTargetBitrate=500000;iRCMode=3;fMaxFrameRate=30;iMinQp=20;iMaxQp=40", "PI"},
  {"rc-screen-qp", "api=ext;iUsageType=1;iPicWidth=320;iPicHeight=240;iTargetBitrate=500000;iRCMode=1;fMaxFrameRate=30;iMinQp=0;iMaxQp=0", "PI"},

  // Several spatial layers: SVC (subset SPS) and simulcast AVC.
  {"svc-2-layers", "api=ext;iPicWidth=640;iPicHeight=360;iTargetBitrate=1500000;iRCMode=1;fMaxFrameRate=30;iSpatialLayerNum=2;L0.iVideoWidth=320;L0.iVideoHeight=180;L0.fFrameRate=30;L0.iSpatialBitrate=500000;L1.iVideoWidth=640;L1.iVideoHeight=360;L1.fFrameRate=30;L1.iSpatialBitrate=1000000", "PIFI"},
  {"svc-2-layers-sps-listing", "api=ext;iPicWidth=640;iPicHeight=360;iTargetBitrate=1500000;iRCMode=1;fMaxFrameRate=30;iSpatialLayerNum=2;eSpsPpsIdStrategy=2;L0.iVideoWidth=320;L0.iVideoHeight=180;L0.fFrameRate=30;L0.iSpatialBitrate=500000;L1.iVideoWidth=640;L1.iVideoHeight=360;L1.fFrameRate=30;L1.iSpatialBitrate=1000000", "PI"},
  {"simulcast-2-layers", "api=ext;iPicWidth=640;iPicHeight=360;iTargetBitrate=1500000;iRCMode=1;fMaxFrameRate=30;iSpatialLayerNum=2;bSimulcastAVC=1;L0.iVideoWidth=320;L0.iVideoHeight=180;L0.fFrameRate=30;L0.iSpatialBitrate=500000;L1.iVideoWidth=640;L1.iVideoHeight=360;L1.fFrameRate=30;L1.iSpatialBitrate=1000000", "PIFI"},
  {"simulcast-2-layers-sps-listing", "api=ext;iPicWidth=640;iPicHeight=360;iTargetBitrate=1500000;iRCMode=1;fMaxFrameRate=30;iSpatialLayerNum=2;bSimulcastAVC=1;eSpsPpsIdStrategy=2;L0.iVideoWidth=320;L0.iVideoHeight=180;L0.fFrameRate=30;L0.iSpatialBitrate=500000;L1.iVideoWidth=640;L1.iVideoHeight=360;L1.fFrameRate=30;L1.iSpatialBitrate=1000000", "PI"},

  // Settings initialisation rejects.
  {"err-usage-non-real-time", "api=ext;iUsageType=2;iPicWidth=640;iPicHeight=480;iTargetBitrate=1000000;iRCMode=1;fMaxFrameRate=30", ""},
  {"err-no-bitrate", "api=ext;iPicWidth=640;iPicHeight=480;iRCMode=1;fMaxFrameRate=30", ""},
  {"err-bitrate-below-frame-rate", "api=ext;iPicWidth=640;iPicHeight=480;iTargetBitrate=20;iRCMode=1;fMaxFrameRate=30", ""},
  {"err-max-below-target", "api=ext;iPicWidth=640;iPicHeight=480;iTargetBitrate=1000000;iRCMode=1;fMaxFrameRate=30;L0.iMaxSpatialBitrate=500000", ""},
  {"err-layers-over-total", "api=ext;iPicWidth=640;iPicHeight=480;iTargetBitrate=1000000;iRCMode=1;fMaxFrameRate=30;L0.iSpatialBitrate=2000000", ""},
  {"err-no-size", "api=ext;iPicWidth=0;iPicHeight=0;iTargetBitrate=1000000;iRCMode=1;fMaxFrameRate=30", ""},
  {"err-rc-mode", "api=ext;iPicWidth=640;iPicHeight=480;iTargetBitrate=1000000;iRCMode=7;fMaxFrameRate=30", ""},
  {"err-loop-filter", "api=ext;iPicWidth=640;iPicHeight=480;iTargetBitrate=1000000;iRCMode=1;fMaxFrameRate=30;iLoopFilterDisableIdc=3", ""},
  {"err-screen-svc", "api=ext;iUsageType=1;iPicWidth=640;iPicHeight=360;iTargetBitrate=1500000;iRCMode=1;fMaxFrameRate=30;iSpatialLayerNum=2;L0.iVideoWidth=320;L0.iVideoHeight=180;L0.fFrameRate=30;L0.iSpatialBitrate=500000;L1.iVideoWidth=640;L1.iVideoHeight=360;L1.fFrameRate=30;L1.iSpatialBitrate=1000000", ""},
  {"err-base-no-bitrate", "api=base;iUsageType=0;iPicWidth=320;iPicHeight=240;fMaxFrameRate=30", ""},
};

// Settings keys. The C# test has the same table (ParameterSetTests.cs).
bool ApplySetting (SEncParamExt& e, SEncParamBase& b, bool base, const std::string& key, const std::string& value) {
  const long long v = strtoll (value.c_str(), NULL, 10);
  const float f = (float)strtod (value.c_str(), NULL);
  if (base) {
    if (key == "iUsageType") b.iUsageType = (EUsageType)v;
    else if (key == "iPicWidth") b.iPicWidth = (int)v;
    else if (key == "iPicHeight") b.iPicHeight = (int)v;
    else if (key == "iTargetBitrate") b.iTargetBitrate = (int)v;
    else if (key == "iRCMode") b.iRCMode = (RC_MODES)v;
    else if (key == "fMaxFrameRate") b.fMaxFrameRate = f;
    else return false;
    return true;
  }
  if (key.size() > 3 && key[0] == 'L' && key[2] == '.') {
    SSpatialLayerConfig& l = e.sSpatialLayers[key[1] - '0'];
    const std::string k = key.substr (3);
    if (k == "iVideoWidth") l.iVideoWidth = (int)v;
    else if (k == "iVideoHeight") l.iVideoHeight = (int)v;
    else if (k == "fFrameRate") l.fFrameRate = f;
    else if (k == "iSpatialBitrate") l.iSpatialBitrate = (int)v;
    else if (k == "iMaxSpatialBitrate") l.iMaxSpatialBitrate = (int)v;
    else if (k == "uiProfileIdc") l.uiProfileIdc = (EProfileIdc)v;
    else if (k == "uiLevelIdc") l.uiLevelIdc = (ELevelIdc)v;
    else if (k == "iDLayerQp") l.iDLayerQp = (int)v;
    else if (k == "uiSliceMode") l.sSliceArgument.uiSliceMode = (SliceModeEnum)v;
    else if (k == "uiSliceNum") l.sSliceArgument.uiSliceNum = (unsigned int)v;
    else if (k == "uiSliceSizeConstraint") l.sSliceArgument.uiSliceSizeConstraint = (unsigned int)v;
    else if (k == "uiSliceMbNum") {
      // A comma separated list, for SM_RASTER_SLICE.
      std::stringstream ss (value);
      std::string n;
      for (int i = 0; std::getline (ss, n, ',') && i < MAX_SLICES_NUM_TMP; i++)
        l.sSliceArgument.uiSliceMbNum[i] = (unsigned int)strtoul (n.c_str(), NULL, 10);
    }
    else if (k == "bVideoSignalTypePresent") l.bVideoSignalTypePresent = v != 0;
    else if (k == "uiVideoFormat") l.uiVideoFormat = (unsigned char)v;
    else if (k == "bFullRange") l.bFullRange = v != 0;
    else if (k == "bColorDescriptionPresent") l.bColorDescriptionPresent = v != 0;
    else if (k == "uiColorPrimaries") l.uiColorPrimaries = (unsigned char)v;
    else if (k == "uiTransferCharacteristics") l.uiTransferCharacteristics = (unsigned char)v;
    else if (k == "uiColorMatrix") l.uiColorMatrix = (unsigned char)v;
    else if (k == "bAspectRatioPresent") l.bAspectRatioPresent = v != 0;
    else if (k == "eAspectRatio") l.eAspectRatio = (ESampleAspectRatio)v;
    else if (k == "sAspectRatioExtWidth") l.sAspectRatioExtWidth = (unsigned short)v;
    else if (k == "sAspectRatioExtHeight") l.sAspectRatioExtHeight = (unsigned short)v;
    else return false;
    return true;
  }
  if (key == "iUsageType") e.iUsageType = (EUsageType)v;
  else if (key == "iPicWidth") e.iPicWidth = (int)v;
  else if (key == "iPicHeight") e.iPicHeight = (int)v;
  else if (key == "iTargetBitrate") e.iTargetBitrate = (int)v;
  else if (key == "iRCMode") e.iRCMode = (RC_MODES)v;
  else if (key == "fMaxFrameRate") e.fMaxFrameRate = f;
  else if (key == "iTemporalLayerNum") e.iTemporalLayerNum = (int)v;
  else if (key == "iSpatialLayerNum") e.iSpatialLayerNum = (int)v;
  else if (key == "uiIntraPeriod") e.uiIntraPeriod = (unsigned int)v;
  else if (key == "iNumRefFrame") e.iNumRefFrame = (int)v;
  else if (key == "eSpsPpsIdStrategy") e.eSpsPpsIdStrategy = (EParameterSetStrategy)v;
  else if (key == "bSimulcastAVC") e.bSimulcastAVC = v != 0;
  else if (key == "iEntropyCodingModeFlag") e.iEntropyCodingModeFlag = (int)v;
  else if (key == "bEnableFrameSkip") e.bEnableFrameSkip = v != 0;
  else if (key == "iMaxBitrate") e.iMaxBitrate = (int)v;
  else if (key == "iMaxQp") e.iMaxQp = (int)v;
  else if (key == "iMinQp") e.iMinQp = (int)v;
  else if (key == "uiMaxNalSize") e.uiMaxNalSize = (unsigned int)v;
  else if (key == "bEnableLongTermReference") e.bEnableLongTermReference = v != 0;
  else if (key == "iLTRRefNum") e.iLTRRefNum = (int)v;
  else if (key == "iLtrMarkPeriod") e.iLtrMarkPeriod = (unsigned int)v;
  else if (key == "iMultipleThreadIdc") e.iMultipleThreadIdc = (unsigned short)v;
  else if (key == "iLoopFilterDisableIdc") e.iLoopFilterDisableIdc = (int)v;
  else if (key == "bEnableDenoise") e.bEnableDenoise = v != 0;
  else if (key == "bEnableFrameCroppingFlag") e.bEnableFrameCroppingFlag = v != 0;
  else if (key == "bIsLosslessLink") e.bIsLosslessLink = v != 0;
  else if (key == "iLoopFilterAlphaC0Offset") e.iLoopFilterAlphaC0Offset = (int)v;
  else if (key == "iLoopFilterBetaOffset") e.iLoopFilterBetaOffset = (int)v;
  else if (key == "iComplexityMode") e.iComplexityMode = (ECOMPLEXITY_MODE)v;
  else if (key == "bUseLoadBalancing") e.bUseLoadBalancing = v != 0;
  else if (key == "bEnableBackgroundDetection") e.bEnableBackgroundDetection = v != 0;
  else if (key == "bEnableAdaptiveQuant") e.bEnableAdaptiveQuant = v != 0;
  else if (key == "bEnableSceneChangeDetect") e.bEnableSceneChangeDetect = v != 0;
  else return false;
  return true;
}

// SEncParamExt as GetOption(ENCODER_OPTION_SVC_ENCODE_PARAM_EXT) reports it. Floats
// print as %.9g, which round-trips a float. The C# test formats its own the same way.
std::string DumpParamExt (const SEncParamExt& p) {
  std::ostringstream o;
  char fbuf[64];
  auto F = [&] (float x) {
    snprintf (fbuf, sizeof fbuf, "%.9g", x);
    return std::string (fbuf);
  };
  o << "iUsageType=" << p.iUsageType << ";iPicWidth=" << p.iPicWidth << ";iPicHeight=" << p.iPicHeight
    << ";iTargetBitrate=" << p.iTargetBitrate << ";iRCMode=" << p.iRCMode << ";fMaxFrameRate=" << F (p.fMaxFrameRate)
    << ";iTemporalLayerNum=" << p.iTemporalLayerNum << ";iSpatialLayerNum=" << p.iSpatialLayerNum
    << ";iComplexityMode=" << p.iComplexityMode << ";uiIntraPeriod=" << p.uiIntraPeriod
    << ";iNumRefFrame=" << p.iNumRefFrame << ";eSpsPpsIdStrategy=" << p.eSpsPpsIdStrategy
    << ";bPrefixNalAddingCtrl=" << p.bPrefixNalAddingCtrl << ";bEnableSSEI=" << p.bEnableSSEI
    << ";bSimulcastAVC=" << p.bSimulcastAVC << ";iPaddingFlag=" << p.iPaddingFlag
    << ";iEntropyCodingModeFlag=" << p.iEntropyCodingModeFlag << ";bEnableFrameSkip=" << p.bEnableFrameSkip
    << ";iMaxBitrate=" << p.iMaxBitrate << ";iMaxQp=" << p.iMaxQp << ";iMinQp=" << p.iMinQp
    << ";uiMaxNalSize=" << p.uiMaxNalSize << ";bEnableLongTermReference=" << p.bEnableLongTermReference
    << ";iLTRRefNum=" << p.iLTRRefNum << ";iLtrMarkPeriod=" << p.iLtrMarkPeriod
    << ";iMultipleThreadIdc=" << p.iMultipleThreadIdc << ";bUseLoadBalancing=" << p.bUseLoadBalancing
    << ";iLoopFilterDisableIdc=" << p.iLoopFilterDisableIdc << ";iLoopFilterAlphaC0Offset=" << p.iLoopFilterAlphaC0Offset
    << ";iLoopFilterBetaOffset=" << p.iLoopFilterBetaOffset << ";bEnableDenoise=" << p.bEnableDenoise
    << ";bEnableBackgroundDetection=" << p.bEnableBackgroundDetection << ";bEnableAdaptiveQuant=" << p.bEnableAdaptiveQuant
    << ";bEnableFrameCroppingFlag=" << p.bEnableFrameCroppingFlag << ";bEnableSceneChangeDetect=" << p.bEnableSceneChangeDetect
    << ";bIsLosslessLink=" << p.bIsLosslessLink << ";bFixRCOverShoot=" << p.bFixRCOverShoot
    << ";iIdrBitrateRatio=" << p.iIdrBitrateRatio << ";bPsnrY=" << p.bPsnrY << ";bPsnrU=" << p.bPsnrU
    << ";bPsnrV=" << p.bPsnrV;
  for (int i = 0; i < MAX_SPATIAL_LAYER_NUM; i++) {
    const SSpatialLayerConfig& l = p.sSpatialLayers[i];
    o << ";L" << i << ".iVideoWidth=" << l.iVideoWidth << ";L" << i << ".iVideoHeight=" << l.iVideoHeight
      << ";L" << i << ".fFrameRate=" << F (l.fFrameRate) << ";L" << i << ".iSpatialBitrate=" << l.iSpatialBitrate
      << ";L" << i << ".iMaxSpatialBitrate=" << l.iMaxSpatialBitrate << ";L" << i << ".uiProfileIdc=" << l.uiProfileIdc
      << ";L" << i << ".uiLevelIdc=" << l.uiLevelIdc << ";L" << i << ".iDLayerQp=" << l.iDLayerQp
      << ";L" << i << ".uiSliceMode=" << l.sSliceArgument.uiSliceMode << ";L" << i << ".uiSliceNum=" << l.sSliceArgument.uiSliceNum
      << ";L" << i << ".uiSliceSizeConstraint=" << l.sSliceArgument.uiSliceSizeConstraint
      << ";L" << i << ".uiSliceMbNum0=" << l.sSliceArgument.uiSliceMbNum[0]
      << ";L" << i << ".bVideoSignalTypePresent=" << l.bVideoSignalTypePresent << ";L" << i << ".uiVideoFormat=" << (int)l.uiVideoFormat
      << ";L" << i << ".bFullRange=" << l.bFullRange << ";L" << i << ".bColorDescriptionPresent=" << l.bColorDescriptionPresent
      << ";L" << i << ".uiColorPrimaries=" << (int)l.uiColorPrimaries << ";L" << i << ".uiTransferCharacteristics=" << (int)l.uiTransferCharacteristics
      << ";L" << i << ".uiColorMatrix=" << (int)l.uiColorMatrix << ";L" << i << ".bAspectRatioPresent=" << l.bAspectRatioPresent
      << ";L" << i << ".eAspectRatio=" << l.eAspectRatio << ";L" << i << ".sAspectRatioExtWidth=" << l.sAspectRatioExtWidth
      << ";L" << i << ".sAspectRatioExtHeight=" << l.sAspectRatioExtHeight;
  }
  return o.str();
}

const char* FrameTypeName (int t) {
  switch (t) {
  case videoFrameTypeInvalid: return "invalid";
  case videoFrameTypeIDR: return "IDR";
  case videoFrameTypeI: return "I";
  case videoFrameTypeP: return "P";
  case videoFrameTypeSkip: return "skip";
  case videoFrameTypeIPMixed: return "IPMixed";
  default: return "?";
  }
}

// The non-VCL layers of one output: (spatial,temporal,quality,subseq,frametype)=nal,nal,...
// joined with '|'.
std::string NonVclLayers (const SFrameBSInfo& info) {
  std::string s;
  for (int i = 0; i < info.iLayerNum; i++) {
    const SLayerBSInfo& l = info.sLayerInfo[i];
    if (l.uiLayerType != NON_VIDEO_CODING_LAYER) continue;
    char head[96];
    snprintf (head, sizeof head, "(%d,%d,%d,%d,%s)=", l.uiSpatialId, l.uiTemporalId, l.uiQualityId, l.iSubSeqId,
              FrameTypeName (l.eFrameType));
    if (!s.empty()) s += "|";
    s += head;
    const unsigned char* p = l.pBsBuf;
    for (int j = 0; j < l.iNalCount; j++) {
      if (j) s += ",";
      s += Hex (p, l.pNalLengthInByte[j]);
      p += l.pNalLengthInByte[j];
    }
  }
  return s.empty() ? "-" : s;
}

// A plain moving test picture, so scene change detection sees nothing unusual.
void FillPicture (std::vector<unsigned char>& buf, int w, int h, int frame) {
  unsigned char* y = buf.data();
  unsigned char* u = y + w * h;
  unsigned char* v = u + (w / 2) * (h / 2);
  for (int r = 0; r < h; r++)
    for (int c = 0; c < w; c++)
      y[r * w + c] = (unsigned char) ((c * 4 + r * 4 + frame * 2) & 0xff);
  for (int r = 0; r < h / 2; r++)
    for (int c = 0; c < w / 2; c++) {
      u[r * (w / 2) + c] = (unsigned char) ((c * 8) & 0xff);
      v[r * (w / 2) + c] = (unsigned char) ((r * 8) & 0xff);
    }
}

}  // namespace

int ParamSets() {
  printf ("# %s, C-only reference build. Parameter sets, see refharness/src/encoder.cpp.\n", g_strCodecVer);
  printf ("# config\t<name>\t<ops>\t<settings>\n");
  printf ("# init\t<name>\t<return code>\n");
  printf ("# params\t<name>\t<GetOption(ENCODER_OPTION_SVC_ENCODE_PARAM_EXT)>\n");
  printf ("# op\t<name>\t<index>\t<op>\t<return code>\t<frame type>\t<non-VCL layers>\n");

  {
    // What GetDefaultParams gives, which the C# FillDefault must match.
    ISVCEncoder* encoder = NULL;
    if (WelsCreateSVCEncoder (&encoder) != 0) return 1;
    SEncParamExt def;
    memset (&def, 0x5a, sizeof def);
    encoder->GetDefaultParams (&def);
    printf ("defaults\t%s\n", DumpParamExt (def).c_str());
    WelsDestroySVCEncoder (encoder);
  }

  for (const ParamConfig& c : kParamConfigs) {
    printf ("config\t%s\t%s\t%s\n", c.name, c.ops[0] ? c.ops : "-", c.settings);

    ISVCEncoder* encoder = NULL;
    if (WelsCreateSVCEncoder (&encoder) != 0) return 1;
    int iTraceLevel = WELS_LOG_QUIET;
    encoder->SetOption (ENCODER_OPTION_TRACE_LEVEL, &iTraceLevel);

    SEncParamExt ext;
    SEncParamBase base;
    memset (&base, 0, sizeof base);
    encoder->GetDefaultParams (&ext);
    bool isBase = false;
    std::stringstream ss (c.settings);
    std::string item;
    while (std::getline (ss, item, ';')) {
      size_t eq = item.find ('=');
      std::string k = item.substr (0, eq), v = item.substr (eq + 1);
      if (k == "api") {
        isBase = v == "base";
        continue;
      }
      if (!ApplySetting (ext, base, isBase, k, v)) {
        fprintf (stderr, "%s: unknown setting %s\n", c.name, k.c_str());
        return 1;
      }
    }

    int rv = isBase ? encoder->Initialize (&base) : encoder->InitializeExt (&ext);
    printf ("init\t%s\t%d\n", c.name, rv);
    if (rv != cmResultSuccess) {
      WelsDestroySVCEncoder (encoder);
      continue;
    }

    SEncParamExt validated;
    memset (&validated, 0, sizeof validated);
    encoder->GetOption (ENCODER_OPTION_SVC_ENCODE_PARAM_EXT, &validated);
    printf ("params\t%s\t%s\n", c.name, DumpParamExt (validated).c_str());

    const int w = isBase ? base.iPicWidth : ext.iPicWidth;
    const int h = isBase ? base.iPicHeight : ext.iPicHeight;
    const float fps = isBase ? base.fMaxFrameRate : ext.fMaxFrameRate;
    std::vector<unsigned char> buf (w * h * 3 / 2);
    SSourcePicture pic;
    memset (&pic, 0, sizeof pic);
    pic.iPicWidth = w;
    pic.iPicHeight = h;
    pic.iColorFormat = videoFormatI420;
    pic.iStride[0] = w;
    pic.iStride[1] = pic.iStride[2] = w >> 1;
    pic.pData[0] = buf.data();
    pic.pData[1] = pic.pData[0] + w * h;
    pic.pData[2] = pic.pData[1] + (w * h >> 2);

    int frame = 0;
    for (int i = 0; c.ops[i]; i++) {
      SFrameBSInfo info;
      memset (&info, 0, sizeof info);
      const char op = c.ops[i];
      if (op == 'P') {
        rv = encoder->EncodeParameterSets (&info);
      } else {
        if (op == 'I') encoder->ForceIntraFrame (true);
        FillPicture (buf, w, h, frame);
        pic.uiTimeStamp = (long long) (frame * 1000.0 / fps);
        rv = encoder->EncodeFrame (&pic, &info);
        frame++;
      }
      printf ("op\t%s\t%d\t%c\t%d\t%s\t%s\n", c.name, i, op, rv, FrameTypeName (info.eFrameType),
              NonVclLayers (info).c_str());
    }

    encoder->Uninitialize();
    WelsDestroySVCEncoder (encoder);
  }
  return 0;
}

namespace {

// ---------------------------------------------------------------------------
// idr
// ---------------------------------------------------------------------------

// A stateless 32-bit hash (the "lowbias32" finaliser), so a sample does not depend on
// the order the planes are filled in. The C# test has the same functions (IdrTests.cs).
uint32_t Hash32 (uint32_t x) {
  x ^= x >> 16;
  x *= 0x7feb352du;
  x ^= x >> 15;
  x *= 0x846ca68bu;
  x ^= x >> 16;
  return x;
}

unsigned char Noise (int plane, int r, int c) {
  return (unsigned char) (Hash32 (((uint32_t)plane << 28) | ((uint32_t)r << 13) | (uint32_t)c) >> 24);
}

// The "scene" content: 24x24 luma regions (12x12 chroma), not macroblock aligned, of six
// kinds, so that some macroblocks are flat, some smooth, some textured, and the region
// edges fall both on and inside macroblock edges.
unsigned char Scene (int plane, int r, int c) {
  const int s = plane ? 12 : 24;
  switch (((c / s) * 7 + (r / s) * 3) % 6) {
  case 0: return (unsigned char) ((60 + 37 * (c / s + r / s) + plane * 50) & 0xff);
  case 1: return (unsigned char) ((c * 3 + plane * 40) & 0xff);
  case 2: return (unsigned char) ((r * 5 + plane * 90) & 0xff);
  case 3: return Noise (plane, r, c);
  case 4: return (((r >> 2) + (c >> 2)) & 1) ? 200 : 40;
  default: return (unsigned char) (((r + c) & 7) * 30 + plane * 7);
  }
}

// An I420 picture of the given content: Y w x h, U and V (w/2) x (h/2), each packed.
void FillContent (std::vector<unsigned char>& buf, int w, int h, const std::string& kind) {
  const int cw = w >> 1, ch = h >> 1;
  buf.assign (w * h + 2 * cw * ch, 0);
  unsigned char* p[3] = {buf.data(), buf.data() + w * h, buf.data() + w * h + cw * ch};
  const int pw[3] = {w, cw, cw}, ph[3] = {h, ch, ch};
  for (int plane = 0; plane < 3; plane++)
    for (int r = 0; r < ph[plane]; r++)
      for (int c = 0; c < pw[plane]; c++) {
        unsigned char v;
        if (kind == "flat") v = plane == 0 ? 100 : plane == 1 ? 110 : 150;
        else if (kind == "gradient") v = (unsigned char) ((plane == 0 ? c * 4 + r * 4 : plane == 1 ? c * 8 : r * 8) & 0xff);
        else if (kind == "noise") v = Noise (plane, r, c);
        else v = Scene (plane, r, c);
        p[plane][r * pw[plane] + c] = v;
      }
}

struct IdrConfig {
  const char* name;
  const char* content;    // flat, gradient, noise or scene, see FillContent
  const char* settings;   // as kParamConfigs
};

// Rate control off (fixed QP, sSpatialLayers[0].iDLayerQp, 26 by default), one thread,
// CAVLC, camera content, one spatial layer: what the port codes by E4.
#define IDR_BASE "api=ext;iRCMode=-1;iMultipleThreadIdc=1;fMaxFrameRate=30;"
#define QCIF "iPicWidth=176;iPicHeight=144;"
const IdrConfig kIdrConfigs[] = {
  // Content, at the defaults: QP 26 and LOW_COMPLEXITY (the fast coding functions).
  {"scene", "scene", IDR_BASE QCIF},
  {"gradient", "gradient", IDR_BASE QCIF},
  {"noise", "noise", IDR_BASE QCIF},
  {"flat", "flat", IDR_BASE QCIF},

  // QPs.
  {"qp0-scene", "scene", IDR_BASE QCIF "L0.iDLayerQp=0"},
  {"qp10-scene", "scene", IDR_BASE QCIF "L0.iDLayerQp=10"},
  {"qp18-noise", "noise", IDR_BASE QCIF "L0.iDLayerQp=18"},
  {"qp34-scene", "scene", IDR_BASE QCIF "L0.iDLayerQp=34"},
  {"qp40-gradient", "gradient", IDR_BASE QCIF "L0.iDLayerQp=40"},
  {"qp51-scene", "scene", IDR_BASE QCIF "L0.iDLayerQp=51"},
  {"qp51-noise", "noise", IDR_BASE QCIF "L0.iDLayerQp=51"},

  // MEDIUM and HIGH complexity use the normal coding functions.
  {"complexity-medium", "scene", IDR_BASE QCIF "iComplexityMode=1"},
  {"complexity-high", "scene", IDR_BASE QCIF "iComplexityMode=2"},
  {"complexity-high-noise-qp20", "noise", IDR_BASE QCIF "iComplexityMode=2;L0.iDLayerQp=20"},
  {"complexity-high-gradient-qp30", "gradient", IDR_BASE QCIF "iComplexityMode=2;L0.iDLayerQp=30"},

  // Sizes, cropping and padding.
  {"16x16", "scene", IDR_BASE "iPicWidth=16;iPicHeight=16"},
  {"48x32-noise", "noise", IDR_BASE "iPicWidth=48;iPicHeight=32"},
  {"33x17-odd", "scene", IDR_BASE "iPicWidth=33;iPicHeight=17"},
  {"152x100", "scene", IDR_BASE "iPicWidth=152;iPicHeight=100"},
  {"320x192", "scene", IDR_BASE "iPicWidth=320;iPicHeight=192"},
  {"330x250", "scene", IDR_BASE "iPicWidth=330;iPicHeight=250"},
  {"330x250-no-cropping", "scene", IDR_BASE "iPicWidth=330;iPicHeight=250;bEnableFrameCroppingFlag=0"},
  {"640x480-gradient", "gradient", IDR_BASE "iPicWidth=640;iPicHeight=480"},
  {"1280x720", "scene", IDR_BASE "iPicWidth=1280;iPicHeight=720;iComplexityMode=2"},

  // Slices (single threaded, so fixed and raster slices are coded in turn).
  {"slices-fixed-4", "scene", IDR_BASE QCIF "L0.uiSliceMode=1;L0.uiSliceNum=4"},
  {"slices-fixed-3-noise", "noise", IDR_BASE QCIF "L0.uiSliceMode=1;L0.uiSliceNum=3"},
  {"slices-fixed-7-320x192", "scene", IDR_BASE "iPicWidth=320;iPicHeight=192;L0.uiSliceMode=1;L0.uiSliceNum=7;iComplexityMode=2"},
  {"slices-fixed-4-no-load-balancing", "scene", IDR_BASE QCIF "L0.uiSliceMode=1;L0.uiSliceNum=4;bUseLoadBalancing=0"},
  {"slices-raster-rows", "scene", IDR_BASE QCIF "L0.uiSliceMode=2"},
  {"slices-raster-mbs", "scene", IDR_BASE QCIF "L0.uiSliceMode=2;L0.uiSliceMbNum=10,30,59"},
  {"slices-raster-mbs-high", "noise", IDR_BASE QCIF "L0.uiSliceMode=2;L0.uiSliceMbNum=1,11,40,47;iComplexityMode=2"},

  // Deblocking.
  {"deblock-off", "scene", IDR_BASE QCIF "iLoopFilterDisableIdc=1"},
  {"deblock-idc2-single-slice", "scene", IDR_BASE QCIF "iLoopFilterDisableIdc=2"},
  {"deblock-idc2-slices", "scene", IDR_BASE QCIF "iLoopFilterDisableIdc=2;L0.uiSliceMode=1;L0.uiSliceNum=4"},
  {"deblock-idc0-slices", "scene", IDR_BASE QCIF "iLoopFilterDisableIdc=0;L0.uiSliceMode=2"},
  {"deblock-offsets-plus", "scene", IDR_BASE QCIF "iLoopFilterAlphaC0Offset=6;iLoopFilterBetaOffset=6"},
  {"deblock-offsets-minus", "scene", IDR_BASE QCIF "iLoopFilterAlphaC0Offset=-6;iLoopFilterBetaOffset=-6"},
  {"deblock-offsets-mixed-qp40", "gradient", IDR_BASE QCIF "iLoopFilterAlphaC0Offset=-3;iLoopFilterBetaOffset=4;L0.iDLayerQp=40"},

  // Temporal layers (the cascading QP of the T0 IDR), LTR, parameter set strategies and
  // the analysis features, which an IDR does not use.
  {"temporal-2", "scene", IDR_BASE QCIF "iTemporalLayerNum=2"},
  {"temporal-3", "scene", IDR_BASE QCIF "iTemporalLayerNum=3"},
  {"temporal-4", "scene", IDR_BASE QCIF "iTemporalLayerNum=4"},
  {"ltr", "scene", IDR_BASE QCIF "bEnableLongTermReference=1"},
  {"strategy-increasing", "scene", IDR_BASE QCIF "eSpsPpsIdStrategy=1"},
  {"strategy-sps-listing", "scene", IDR_BASE QCIF "eSpsPpsIdStrategy=2"},
  {"strategy-sps-pps-listing", "scene", IDR_BASE QCIF "eSpsPpsIdStrategy=6"},
  {"features-off", "scene", IDR_BASE QCIF "bEnableBackgroundDetection=0;bEnableAdaptiveQuant=0;bEnableSceneChangeDetect=0"},
  {"profile-main", "scene", IDR_BASE QCIF "L0.uiProfileIdc=77"},
};
#undef QCIF
#undef IDR_BASE

std::string Sha1Hex (const unsigned char* d, int n) {
  SHA1Context ctx;
  SHA1Reset (&ctx);
  SHA1Input (&ctx, d, n);
  unsigned char digest[SHA_DIGEST_LENGTH];
  SHA1Result (&ctx, digest);
  return Hex (digest, SHA_DIGEST_LENGTH);
}

// Every layer of one output, '|' separated: V (VCL) or N (non-VCL), then each NAL unit
// (start code included) as <length>:<first 8 hex digits of its SHA1>.
std::string Layers (const SFrameBSInfo& info) {
  std::string s;
  for (int i = 0; i < info.iLayerNum; i++) {
    const SLayerBSInfo& l = info.sLayerInfo[i];
    if (!s.empty()) s += "|";
    s += l.uiLayerType == VIDEO_CODING_LAYER ? "V=" : "N=";
    const unsigned char* p = l.pBsBuf;
    for (int j = 0; j < l.iNalCount; j++) {
      if (j) s += ",";
      s += std::to_string (l.pNalLengthInByte[j]) + ":" + Sha1Hex (p, l.pNalLengthInByte[j]).substr (0, 8);
      p += l.pNalLengthInByte[j];
    }
  }
  return s.empty() ? "-" : s;
}

// Encodes one configuration's first frame (an IDR). Returns InitializeExt's code, and
// EncodeFrame's in *pEncodeRc; the output bytes go to *pFrame.
int EncodeIdr (const IdrConfig& c, int* pEncodeRc, std::string* pFrameType, std::string* pLayers,
               std::vector<unsigned char>* pFrame) {
  ISVCEncoder* encoder = NULL;
  if (WelsCreateSVCEncoder (&encoder) != 0) return -1;
  int iTraceLevel = WELS_LOG_QUIET;
  encoder->SetOption (ENCODER_OPTION_TRACE_LEVEL, &iTraceLevel);

  SEncParamExt ext;
  SEncParamBase base;
  memset (&base, 0, sizeof base);
  encoder->GetDefaultParams (&ext);
  std::stringstream ss (c.settings);
  std::string item;
  while (std::getline (ss, item, ';')) {
    if (item.empty()) continue;
    size_t eq = item.find ('=');
    std::string k = item.substr (0, eq), v = item.substr (eq + 1);
    if (k == "api") continue;
    if (!ApplySetting (ext, base, false, k, v)) {
      fprintf (stderr, "%s: unknown setting %s\n", c.name, k.c_str());
      exit (1);
    }
  }

  int rv = encoder->InitializeExt (&ext);
  *pEncodeRc = 0;
  pFrame->clear();
  *pFrameType = "-";
  *pLayers = "-";
  if (rv == cmResultSuccess) {
    const int w = ext.iPicWidth, h = ext.iPicHeight;
    std::vector<unsigned char> buf;
    FillContent (buf, w, h, c.content);
    SSourcePicture pic;
    memset (&pic, 0, sizeof pic);
    pic.iPicWidth = w;
    pic.iPicHeight = h;
    pic.iColorFormat = videoFormatI420;
    pic.iStride[0] = w;
    pic.iStride[1] = pic.iStride[2] = w >> 1;
    pic.pData[0] = buf.data();
    pic.pData[1] = pic.pData[0] + w * h;
    pic.pData[2] = pic.pData[1] + (w >> 1) * (h >> 1);
    pic.uiTimeStamp = 0;

    SFrameBSInfo info;
    memset (&info, 0, sizeof info);
    *pEncodeRc = encoder->EncodeFrame (&pic, &info);
    *pFrameType = FrameTypeName (info.eFrameType);
    *pLayers = Layers (info);
    for (int i = 0; i < info.iLayerNum; i++) {
      const SLayerBSInfo& l = info.sLayerInfo[i];
      int n = 0;
      for (int j = 0; j < l.iNalCount; j++) n += l.pNalLengthInByte[j];
      pFrame->insert (pFrame->end(), l.pBsBuf, l.pBsBuf + n);
    }
    encoder->Uninitialize();
  }
  WelsDestroySVCEncoder (encoder);
  return rv;
}

}  // namespace

int Idr() {
  printf ("# %s, C-only reference build. First frame (IDR) per configuration, see refharness/src/encoder.cpp.\n",
          g_strCodecVer);
  printf ("# config\t<name>\t<content>\t<settings>\n");
  printf ("# idr\t<name>\t<InitializeExt>\t<EncodeFrame>\t<frame type>\t<layers>\t<sha1 of the frame>\t<size>\n");
  for (const IdrConfig& c : kIdrConfigs) {
    printf ("config\t%s\t%s\t%s\n", c.name, c.content, c.settings);
    int encodeRc = 0;
    std::string frameType, layers;
    std::vector<unsigned char> frame;
    int rv = EncodeIdr (c, &encodeRc, &frameType, &layers, &frame);
    printf ("idr\t%s\t%d\t%d\t%s\t%s\t%s\t%d\n", c.name, rv, encodeRc, frameType.c_str(), layers.c_str(),
            frame.empty() ? "-" : Sha1Hex (frame.data(), (int)frame.size()).c_str(), (int)frame.size());
  }
  return 0;
}

int IdrDump (const char* name, const char* outPath) {
  for (const IdrConfig& c : kIdrConfigs) {
    if (std::string (c.name) != name) continue;
    int encodeRc = 0;
    std::string frameType, layers;
    std::vector<unsigned char> frame;
    EncodeIdr (c, &encodeRc, &frameType, &layers, &frame);
    std::ofstream out (outPath, std::ios::binary);
    out.write ((const char*)frame.data(), (std::streamsize)frame.size());
    return out ? 0 : 1;
  }
  fprintf (stderr, "no idr configuration %s\n", name);
  return 1;
}

namespace {

// ---------------------------------------------------------------------------
// stream
// ---------------------------------------------------------------------------

// A triangle wave of period 256 over 0..254, for smooth content that moves by fractions
// of a pixel.
int Tri (int u) {
  u &= 255;
  return u < 128 ? u * 2 : (255 - u) * 2;
}

int Clip255 (int v) {
  return v < 0 ? 0 : v > 255 ? 255 : v;
}

// One sample of frame f of the given content. Chroma coordinates are half the luma ones,
// so a motion of (dx, dy) luma pixels is (dx >> 1, dy >> 1) chroma pixels where it is
// written that way. The C# test has the same functions (StreamTests.cs).
//   static  the idr "scene", every frame the same
//   pan     the scene moving by (3, 2) luma pixels a frame
//   block   the scene, still, with a 24x24 block of noise moving by (5, 3) over it
//   noise   noise moving by (2, 1)
//   random  new noise every frame
//   fade    the scene getting darker and lifting towards grey
//   cut     the scene, then from frame 4 an inverted, shifted scene
//   subpel  smooth waves moving by (3/4, 1/4) of a luma pixel a frame
unsigned char StreamSample (const std::string& kind, int plane, int r, int c, int f) {
  const int sh = plane ? 1 : 0;
  if (kind == "static") return Scene (plane, r, c);
  if (kind == "pan") return Scene (plane, r + ((2 * f) >> sh), c + ((3 * f) >> sh));
  if (kind == "block") {
    const int bs = 24 >> sh, x0 = (5 * f) >> sh, y0 = (3 * f) >> sh;
    if (r >= y0 && r < y0 + bs && c >= x0 && c < x0 + bs) return Noise (plane + 3, r - y0, c - x0);
    return Scene (plane, r, c);
  }
  if (kind == "noise") return Noise (plane, r + (f >> sh), c + ((2 * f) >> sh));
  if (kind == "random")
    return (unsigned char) (Hash32 ((((uint32_t)plane << 28) | ((uint32_t)r << 13) | (uint32_t)c) ^ ((uint32_t) (f + 1) *
                            0x9e3779b9u)) >> 24);
  if (kind == "fade") return (unsigned char)Clip255 (Scene (plane, r, c) * (20 - f) / 20 + 6 * f);
  if (kind == "cut") return f < 4 ? Scene (plane, r, c) : (unsigned char) (255 - Scene (plane, r + 37, c + 91));
  // subpel: luma u = 4c + 3f, so a quarter pixel per unit; chroma 8c + 3f, the same motion.
  const int s = plane ? 8 : 4;
  const int a = Tri (s * c + 3 * f), b = Tri (s * r + f);
  return (unsigned char) (plane ? 64 + ((a + b) >> 2) : (a + b) >> 1);
}

void FillFrame (std::vector<unsigned char>& buf, int w, int h, const std::string& kind, int f) {
  const int cw = w >> 1, ch = h >> 1;
  buf.assign (w * h + 2 * cw * ch, 0);
  unsigned char* p[3] = {buf.data(), buf.data() + w * h, buf.data() + w * h + cw * ch};
  const int pw[3] = {w, cw, cw}, ph[3] = {h, ch, ch};
  for (int plane = 0; plane < 3; plane++)
    for (int r = 0; r < ph[plane]; r++)
      for (int c = 0; c < pw[plane]; c++) p[plane][r * pw[plane] + c] = StreamSample (kind, plane, r, c, f);
}

struct StreamConfig {
  const char* name;
  const char* content;    // see StreamSample
  int frames;
  const char* settings;   // as kParamConfigs
};

// As IDR_BASE, with the analysis features (background detection, adaptive quantisation
// and scene change detection, which upstream enables by default) off: what the port codes
// by E5b. Camera content.
#define STREAM_BASE "api=ext;iRCMode=-1;iMultipleThreadIdc=1;fMaxFrameRate=30;bEnableBackgroundDetection=0;" \
  "bEnableAdaptiveQuant=0;bEnableSceneChangeDetect=0;"
#define QCIF "iPicWidth=176;iPicHeight=144;"
const StreamConfig kStreamConfigs[] = {
  // Content, at the defaults: QP 26, LOW_COMPLEXITY, one reference, no intra period.
  {"static", "static", 8, STREAM_BASE QCIF},
  {"pan", "pan", 10, STREAM_BASE QCIF},
  {"block", "block", 10, STREAM_BASE QCIF},
  {"noise", "noise", 8, STREAM_BASE QCIF},
  {"random", "random", 6, STREAM_BASE QCIF},
  {"fade", "fade", 8, STREAM_BASE QCIF},
  {"cut", "cut", 8, STREAM_BASE QCIF},
  {"subpel", "subpel", 10, STREAM_BASE QCIF},

  // MEDIUM and HIGH complexity: the normal coding functions, sub-pixel search and more
  // partitions.
  {"pan-medium", "pan", 10, STREAM_BASE QCIF "iComplexityMode=1"},
  {"pan-high", "pan", 10, STREAM_BASE QCIF "iComplexityMode=2"},
  {"block-medium", "block", 8, STREAM_BASE QCIF "iComplexityMode=1"},
  {"subpel-high", "subpel", 8, STREAM_BASE QCIF "iComplexityMode=2"},
  {"block-high", "block", 8, STREAM_BASE QCIF "iComplexityMode=2"},
  {"noise-high", "noise", 6, STREAM_BASE QCIF "iComplexityMode=2"},
  {"cut-high", "cut", 8, STREAM_BASE QCIF "iComplexityMode=2"},
  {"fade-medium", "fade", 8, STREAM_BASE QCIF "iComplexityMode=1"},

  // QPs.
  {"pan-qp0", "pan", 4, STREAM_BASE QCIF "L0.iDLayerQp=0"},
  {"pan-qp12", "pan", 6, STREAM_BASE QCIF "L0.iDLayerQp=12"},
  {"fade-qp20", "fade", 6, STREAM_BASE QCIF "L0.iDLayerQp=20"},
  {"subpel-qp20-high", "subpel", 6, STREAM_BASE QCIF "L0.iDLayerQp=20;iComplexityMode=2"},
  {"pan-qp40", "pan", 8, STREAM_BASE QCIF "L0.iDLayerQp=40"},
  {"pan-qp51", "pan", 8, STREAM_BASE QCIF "L0.iDLayerQp=51"},
  {"static-qp51", "static", 6, STREAM_BASE QCIF "L0.iDLayerQp=51"},

  // Intra periods: IDRs in the stream.
  {"intra-period-4", "pan", 12, STREAM_BASE QCIF "uiIntraPeriod=4"},
  {"intra-period-3-high", "subpel", 10, STREAM_BASE QCIF "uiIntraPeriod=3;iComplexityMode=2"},
  {"intra-period-2-static", "static", 6, STREAM_BASE QCIF "uiIntraPeriod=2"},

  // Temporal layers: the reference picture structure, the per layer QPs and the
  // non-reference frames of the highest layer.
  {"temporal-2", "pan", 12, STREAM_BASE QCIF "iTemporalLayerNum=2"},
  {"temporal-3", "pan", 16, STREAM_BASE QCIF "iTemporalLayerNum=3"},
  {"temporal-4", "pan", 18, STREAM_BASE QCIF "iTemporalLayerNum=4"},
  {"temporal-3-high", "subpel", 12, STREAM_BASE QCIF "iTemporalLayerNum=3;iComplexityMode=2"},
  {"temporal-4-intra-period-8", "block", 18, STREAM_BASE QCIF "iTemporalLayerNum=4;uiIntraPeriod=8"},

  // Slices (single threaded).
  {"slices-fixed-4", "pan", 8, STREAM_BASE QCIF "L0.uiSliceMode=1;L0.uiSliceNum=4"},
  {"slices-fixed-3-high", "subpel", 8, STREAM_BASE QCIF "L0.uiSliceMode=1;L0.uiSliceNum=3;iComplexityMode=2"},
  {"slices-fixed-4-no-load-balancing", "pan", 6, STREAM_BASE QCIF "L0.uiSliceMode=1;L0.uiSliceNum=4;bUseLoadBalancing=0"},
  {"slices-raster-rows", "pan", 8, STREAM_BASE QCIF "L0.uiSliceMode=2"},
  {"slices-raster-mbs", "block", 8, STREAM_BASE QCIF "L0.uiSliceMode=2;L0.uiSliceMbNum=10,30,59"},

  // Sizes, cropping and padding (motion vectors pointing outside the picture).
  {"16x16", "pan", 6, STREAM_BASE "iPicWidth=16;iPicHeight=16"},
  {"48x32", "block", 8, STREAM_BASE "iPicWidth=48;iPicHeight=32"},
  {"33x17", "pan", 8, STREAM_BASE "iPicWidth=33;iPicHeight=17"},
  {"33x17-no-cropping", "pan", 6, STREAM_BASE "iPicWidth=33;iPicHeight=17;bEnableFrameCroppingFlag=0"},
  {"152x100", "subpel", 8, STREAM_BASE "iPicWidth=152;iPicHeight=100"},
  {"320x192-high", "pan", 6, STREAM_BASE "iPicWidth=320;iPicHeight=192;iComplexityMode=2"},

  // Deblocking, which the reference pictures carry.
  {"deblock-off", "pan", 8, STREAM_BASE QCIF "iLoopFilterDisableIdc=1"},
  {"deblock-idc2-slices", "pan", 8, STREAM_BASE QCIF "iLoopFilterDisableIdc=2;L0.uiSliceMode=1;L0.uiSliceNum=4"},
  {"deblock-offsets-plus", "subpel", 6, STREAM_BASE QCIF "iLoopFilterAlphaC0Offset=6;iLoopFilterBetaOffset=6"},
  {"deblock-offsets-minus-qp40", "block", 6, STREAM_BASE QCIF "iLoopFilterAlphaC0Offset=-6;iLoopFilterBetaOffset=-6;L0.iDLayerQp=40"},

  // Long term references (camera: marking without feedback).
  {"ltr", "pan", 10, STREAM_BASE QCIF "bEnableLongTermReference=1"},
  {"ltr-mark-3", "pan", 12, STREAM_BASE QCIF "bEnableLongTermReference=1;iLtrMarkPeriod=3"},
  {"ltr-mark-2-temporal-2", "block", 12, STREAM_BASE QCIF "bEnableLongTermReference=1;iLtrMarkPeriod=2;iTemporalLayerNum=2"},
  {"ltr-mark-4-temporal-3-high", "subpel", 12, STREAM_BASE QCIF "bEnableLongTermReference=1;iLtrMarkPeriod=4;iTemporalLayerNum=3;iComplexityMode=2"},
  {"ltr-intra-period-5", "pan", 12, STREAM_BASE QCIF "bEnableLongTermReference=1;iLtrMarkPeriod=3;uiIntraPeriod=5"},

  // Reference counts, parameter set strategies and the main profile.
  {"ref-4", "pan", 10, STREAM_BASE QCIF "iNumRefFrame=4"},
  {"ref-2-temporal-2", "pan", 10, STREAM_BASE QCIF "iNumRefFrame=2;iTemporalLayerNum=2"},
  {"strategy-increasing", "pan", 10, STREAM_BASE QCIF "eSpsPpsIdStrategy=1;uiIntraPeriod=3"},
  {"strategy-sps-listing", "pan", 10, STREAM_BASE QCIF "eSpsPpsIdStrategy=2;uiIntraPeriod=3"},
  {"strategy-sps-pps-listing", "pan", 10, STREAM_BASE QCIF "eSpsPpsIdStrategy=6;uiIntraPeriod=3"},
  {"profile-main", "pan", 8, STREAM_BASE QCIF "L0.uiProfileIdc=77"},
};
#undef QCIF
#undef STREAM_BASE

struct StreamFrame {
  int rc;
  std::string type, layers, tid;
  std::vector<unsigned char> bytes;
};

// Encodes one configuration's frames, the timestamps 1000 / 30 ms apart. Returns
// InitializeExt's code; each frame's outputs go to *pFrames.
int EncodeStream (const StreamConfig& c, std::vector<StreamFrame>* pFrames) {
  pFrames->clear();
  ISVCEncoder* encoder = NULL;
  if (WelsCreateSVCEncoder (&encoder) != 0) return -1;
  int iTraceLevel = WELS_LOG_QUIET;
  encoder->SetOption (ENCODER_OPTION_TRACE_LEVEL, &iTraceLevel);

  SEncParamExt ext;
  SEncParamBase base;
  memset (&base, 0, sizeof base);
  encoder->GetDefaultParams (&ext);
  std::stringstream ss (c.settings);
  std::string item;
  while (std::getline (ss, item, ';')) {
    if (item.empty()) continue;
    size_t eq = item.find ('=');
    std::string k = item.substr (0, eq), v = item.substr (eq + 1);
    if (k == "api") continue;
    if (!ApplySetting (ext, base, false, k, v)) {
      fprintf (stderr, "%s: unknown setting %s\n", c.name, k.c_str());
      exit (1);
    }
  }

  int rv = encoder->InitializeExt (&ext);
  if (rv == cmResultSuccess) {
    const int w = ext.iPicWidth, h = ext.iPicHeight;
    std::vector<unsigned char> buf;
    for (int f = 0; f < c.frames; f++) {
      FillFrame (buf, w, h, c.content, f);
      SSourcePicture pic;
      memset (&pic, 0, sizeof pic);
      pic.iPicWidth = w;
      pic.iPicHeight = h;
      pic.iColorFormat = videoFormatI420;
      pic.iStride[0] = w;
      pic.iStride[1] = pic.iStride[2] = w >> 1;
      pic.pData[0] = buf.data();
      pic.pData[1] = pic.pData[0] + w * h;
      pic.pData[2] = pic.pData[1] + (w >> 1) * (h >> 1);
      pic.uiTimeStamp = (long long)f * 1000 / 30;

      SFrameBSInfo info;
      memset (&info, 0, sizeof info);
      StreamFrame out;
      out.rc = encoder->EncodeFrame (&pic, &info);
      out.type = FrameTypeName (info.eFrameType);
      out.layers = Layers (info);
      for (int i = 0; i < info.iLayerNum; i++) {
        const SLayerBSInfo& l = info.sLayerInfo[i];
        if (l.uiLayerType == VIDEO_CODING_LAYER) out.tid += (out.tid.empty() ? "" : ",") + std::to_string (l.uiTemporalId);
        int n = 0;
        for (int j = 0; j < l.iNalCount; j++) n += l.pNalLengthInByte[j];
        out.bytes.insert (out.bytes.end(), l.pBsBuf, l.pBsBuf + n);
      }
      if (out.tid.empty()) out.tid = "-";
      pFrames->push_back (out);
    }
    encoder->Uninitialize();
  }
  WelsDestroySVCEncoder (encoder);
  return rv;
}

}  // namespace

int Stream() {
  printf ("# %s, C-only reference build. Frame by frame P streams per configuration, see refharness/src/encoder.cpp.\n",
          g_strCodecVer);
  printf ("# config\t<name>\t<content>\t<frames>\t<settings>\n");
  printf ("# frame\t<name>\t<index>\t<EncodeFrame>\t<frame type>\t<temporal ids>\t<layers>\t<sha1 of the frame>\t<size>\n");
  printf ("# stream\t<name>\t<InitializeExt>\t<sha1 of the stream>\t<size>\n");
  for (const StreamConfig& c : kStreamConfigs) {
    printf ("config\t%s\t%s\t%d\t%s\n", c.name, c.content, c.frames, c.settings);
    std::vector<StreamFrame> frames;
    int rv = EncodeStream (c, &frames);
    std::vector<unsigned char> all;
    for (size_t i = 0; i < frames.size(); i++) {
      const StreamFrame& f = frames[i];
      printf ("frame\t%s\t%d\t%d\t%s\t%s\t%s\t%s\t%d\n", c.name, (int)i, f.rc, f.type.c_str(), f.tid.c_str(),
              f.layers.c_str(), f.bytes.empty() ? "-" : Sha1Hex (f.bytes.data(), (int)f.bytes.size()).c_str(),
              (int)f.bytes.size());
      all.insert (all.end(), f.bytes.begin(), f.bytes.end());
    }
    printf ("stream\t%s\t%d\t%s\t%d\n", c.name, rv, all.empty() ? "-" : Sha1Hex (all.data(), (int)all.size()).c_str(),
            (int)all.size());
  }
  return 0;
}

int StreamDump (const char* name, const char* outPath) {
  for (const StreamConfig& c : kStreamConfigs) {
    if (std::string (c.name) != name) continue;
    std::vector<StreamFrame> frames;
    EncodeStream (c, &frames);
    std::ofstream out (outPath, std::ios::binary);
    for (const StreamFrame& f : frames) out.write ((const char*)f.bytes.data(), (std::streamsize)f.bytes.size());
    return out ? 0 : 1;
  }
  fprintf (stderr, "no stream configuration %s\n", name);
  return 1;
}
