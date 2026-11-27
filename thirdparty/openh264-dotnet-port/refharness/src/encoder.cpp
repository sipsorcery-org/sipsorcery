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
//
//   h264ref preprocess
//       E7: the same, for camera content with the preprocessing on
//       (kPreprocessConfigs, reference/preprocess.tsv): scene change
//       detection, background detection, adaptive quantisation, the denoiser
//       and the downsampling of a source larger than the layer. The C# test is
//       PreprocessTests.cs.
//
//   h264ref preprocess-dump <name> <out>
//       Writes that configuration's stream to <out>.
//
//   h264ref api
//       E8: scripted calls of the encoder API for a table of configurations
//       (kApiConfigs, reference/api.tsv): ForceIntraFrame, EncodeParameterSets,
//       SetOption and GetOption for each option, Uninitialize and Initialize
//       again, and bad inputs, between the frames. The C# test is ApiTests.cs.
//
//   h264ref api-dump <name> <out>
//       Writes that configuration's EncodeFrame and EncodeParameterSets
//       output to <out>.

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
  else if (key == "iPaddingFlag") e.iPaddingFlag = (int)v;
  else if (key == "bFixRCOverShoot") e.bFixRCOverShoot = v != 0;
  else if (key == "iIdrBitrateRatio") e.iIdrBitrateRatio = (int)v;
  else if (key == "bPrefixNalAddingCtrl") e.bPrefixNalAddingCtrl = v != 0;
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
//   mixed   (E6) in cycles of 30 frames: pan (10 frames), random (4), static (8), block (8),
//           so that the rate control meets easy, impossible and moderate frames in turn
//   cuts    (E7) a scene cut every 6 frames, between the scene and the inverted, shifted
//           scene, each moving slowly: the large scene changes the scene change
//           detection finds mid-GOP
//   partcut (E7) the scene, its top 96 luma rows (48 chroma) inverted and shifted for
//           frames 5 to 8: two medium changes (two thirds of a QCIF picture's blocks)
//   camera  (E7) block over a background with new noise of up to 2 either way each frame,
//           a camera's: what the background detection and the denoiser are for
unsigned char StreamSample (const std::string& kind, int plane, int r, int c, int f) {
  const int sh = plane ? 1 : 0;
  if (kind == "camera") {
    const int bs = 24 >> sh, x0 = (5 * f) >> sh, y0 = (3 * f) >> sh;
    if (r >= y0 && r < y0 + bs && c >= x0 && c < x0 + bs) return Noise (plane + 3, r - y0, c - x0);
    const int n = (int) (Hash32 ((((uint32_t)plane << 28) | ((uint32_t)r << 13) | (uint32_t)c) ^ ((uint32_t) (f + 1) *
                                 0x85ebca6bu)) % 5) - 2;
    return (unsigned char)Clip255 (Scene (plane, r, c) + n);
  }
  if (kind == "cuts") {
    if ((f / 6) & 1) return (unsigned char) (255 - Scene (plane, r + 37 + (f >> sh), c + 91));
    return Scene (plane, r + (f >> sh), c + ((2 * f) >> sh));
  }
  if (kind == "partcut") {
    if (f >= 5 && f < 9 && r < (96 >> sh)) return (unsigned char) (255 - Scene (plane, r + 37, c + 91));
    return Scene (plane, r, c);
  }
  if (kind == "mixed") {
    const int k = f % 30;
    if (k < 10) return StreamSample ("pan", plane, r, c, f);
    if (k < 14) return StreamSample ("random", plane, r, c, f);
    if (k < 22) return Scene (plane, r, c);
    return StreamSample ("block", plane, r, c, 2 * (k - 22));
  }
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
#undef STREAM_BASE

// The rate controlled streams (E6): as STREAM_BASE, with the rate control mode and target
// set per configuration. Camera content, the analysis features off. QCIF at 30 fps is
// 760320 pixels a second, so 64k, 256k, 450k, 650k and 2M bps fall in each of
// RcCalculateIdrQp's five bits-per-pixel bands; the frame sizes cover its four area bands
// and RcInitSequenceParameter's four macroblock width classes.
#define RC_BASE "api=ext;iMultipleThreadIdc=1;fMaxFrameRate=30;bEnableBackgroundDetection=0;" \
  "bEnableAdaptiveQuant=0;bEnableSceneChangeDetect=0;"
#define QUALITY RC_BASE QCIF "iRCMode=0;"
#define BITRATE RC_BASE QCIF "iRCMode=1;"
#define BUFFER RC_BASE QCIF "iRCMode=2;"
#define TIMESTAMP RC_BASE QCIF "iRCMode=3;"
const StreamConfig kRcConfigs[] = {
  // Quality mode: frame SAD complexity for P frames. Frame skipping is on by default.
  {"quality-pan-256k", "pan", 40, QUALITY "iTargetBitrate=256000"},
  {"quality-mixed-128k", "mixed", 60, QUALITY "iTargetBitrate=128000"},
  {"quality-mixed-128k-no-skip", "mixed", 60, QUALITY "iTargetBitrate=128000;bEnableFrameSkip=0"},
  {"quality-subpel-650k-high", "subpel", 30, QUALITY "iTargetBitrate=650000;iComplexityMode=2"},
  {"quality-temporal-3", "pan", 40, QUALITY "iTargetBitrate=200000;iTemporalLayerNum=3"},
  {"quality-pan-maxbr", "pan", 80, QUALITY "fMaxFrameRate=10;ts.num=100;ts.den=1;iTargetBitrate=200000;iMaxBitrate=200000"},

  // Bitrate mode, the five bits-per-pixel bands, then over the content kinds.
  {"bitrate-pan-64k", "pan", 40, BITRATE "iTargetBitrate=64000"},
  {"bitrate-pan-256k", "pan", 40, BITRATE "iTargetBitrate=256000"},
  {"bitrate-pan-450k", "pan", 30, BITRATE "iTargetBitrate=450000"},
  {"bitrate-pan-650k", "pan", 30, BITRATE "iTargetBitrate=650000"},
  {"bitrate-pan-2m", "pan", 30, BITRATE "iTargetBitrate=2000000"},
  {"bitrate-static-128k", "static", 30, BITRATE "iTargetBitrate=128000"},
  {"bitrate-block-128k", "block", 30, BITRATE "iTargetBitrate=128000"},
  {"bitrate-noise-256k", "noise", 30, BITRATE "iTargetBitrate=256000"},
  {"bitrate-fade-128k", "fade", 20, BITRATE "iTargetBitrate=128000"},
  {"bitrate-cut-128k", "cut", 20, BITRATE "iTargetBitrate=128000"},
  {"bitrate-subpel-256k-medium", "subpel", 30, BITRATE "iTargetBitrate=256000;iComplexityMode=1"},

  // Frame skipping: the buffer, the VGOP constraint and the skip counters; without it, the
  // exceeded bits level.
  {"bitrate-mixed-128k-skip", "mixed", 90, BITRATE "iTargetBitrate=128000"},
  {"bitrate-random-48k-skip", "random", 30, BITRATE "iTargetBitrate=48000"},
  {"bitrate-noise-64k-skip-overshoot-off", "noise", 40, BITRATE "iTargetBitrate=64000;bFixRCOverShoot=0"},
  {"bitrate-mixed-96k-no-skip", "mixed", 60, BITRATE "iTargetBitrate=96000;bEnableFrameSkip=0"},
  {"bitrate-random-32k-no-skip", "random", 20, BITRATE "iTargetBitrate=32000;bEnableFrameSkip=0"},
  {"bitrate-pan-64k-no-skip", "pan", 40, BITRATE "iTargetBitrate=64000;bEnableFrameSkip=0"},

  // The max bitrate: the two shifted 5 s check windows, at 10 fps so that the streams cross
  // several. At the target it decides skips; above it, only the windows' bookkeeping runs.
  {"bitrate-pan-maxbr", "pan", 80, BITRATE "fMaxFrameRate=10;ts.num=100;ts.den=1;iTargetBitrate=200000;iMaxBitrate=200000"},
  {"bitrate-pan-maxbr-overshoot-off", "pan", 80, BITRATE "fMaxFrameRate=10;ts.num=100;ts.den=1;iTargetBitrate=200000;"
   "iMaxBitrate=200000;bFixRCOverShoot=0"},
  {"bitrate-mixed-maxbr", "mixed", 90, BITRATE "fMaxFrameRate=10;ts.num=100;ts.den=1;iTargetBitrate=128000;iMaxBitrate=128000"},
  {"bitrate-mixed-maxbr-overshoot-off", "mixed", 80, BITRATE "fMaxFrameRate=10;ts.num=100;ts.den=1;"
   "iTargetBitrate=128000;iMaxBitrate=128000;bFixRCOverShoot=0"},
  {"bitrate-mixed-maxbr-above", "mixed", 60, BITRATE "fMaxFrameRate=10;ts.num=100;ts.den=1;iTargetBitrate=64000;"
   "iMaxBitrate=80000"},
  {"bitrate-pan-maxbr-30fps", "pan", 120, BITRATE "iTargetBitrate=256000;iMaxBitrate=256000"},
  // Longer, across three 5 s windows, where the shifted window's checks decide skips.
  {"bitrate-mixed-maxbr-96k-long", "mixed", 150, BITRATE "fMaxFrameRate=10;ts.num=100;ts.den=1;iTargetBitrate=96000;"
   "iMaxBitrate=96000"},
  {"bitrate-mixed-maxbr-96k-long-overshoot-off", "mixed", 150, BITRATE "fMaxFrameRate=10;ts.num=100;ts.den=1;"
   "iTargetBitrate=96000;iMaxBitrate=96000;bFixRCOverShoot=0"},
  {"bitrate-mixed-maxbr-160k-long", "mixed", 150, BITRATE "fMaxFrameRate=10;ts.num=100;ts.den=1;iTargetBitrate=160000;"
   "iMaxBitrate=160000"},
  {"bitrate-mixed-maxbr-15fps-long", "mixed", 150, BITRATE "fMaxFrameRate=15;ts.num=1000;ts.den=15;iTargetBitrate=128000;"
   "iMaxBitrate=128000"},
  {"bitrate-block-maxbr-long", "block", 150, BITRATE "fMaxFrameRate=10;ts.num=100;ts.den=1;iTargetBitrate=100000;"
   "iMaxBitrate=100000"},
  // The shifted window's reset exactly at 2.5 s decides a skip here.
  {"bitrate-pan-maxbr-64k-long", "pan", 150, BITRATE "fMaxFrameRate=10;ts.num=100;ts.den=1;iTargetBitrate=64000;"
   "iMaxBitrate=64000"},
  {"bitrate-noise-maxbr", "noise", 100, BITRATE "fMaxFrameRate=10;ts.num=100;ts.den=1;iTargetBitrate=128000;"
   "iMaxBitrate=128000"},
  {"quality-mixed-maxbr-long", "mixed", 150, QUALITY "fMaxFrameRate=10;ts.num=100;ts.den=1;iTargetBitrate=128000;"
   "iMaxBitrate=128000"},

  // Timestamps with the max bitrate windows (GetTimestampForRc): a pause, a step back on a
  // 12 fps clock (a step back on the nominal clock is corrected straight back onto it), a
  // timestamp of 0. Jitter does not move these decisions; timestamp-jitter covers it.
  {"bitrate-pan-maxbr-gap", "pan", 80, BITRATE "fMaxFrameRate=10;ts.num=100;ts.den=1;iTargetBitrate=200000;"
   "iMaxBitrate=200000;ts.gap=40:6000"},
  {"bitrate-mixed-maxbr-back", "mixed", 90, BITRATE "fMaxFrameRate=10;ts.num=1000;ts.den=12;iTargetBitrate=128000;"
   "iMaxBitrate=128000;ts.back=40:400"},
  {"bitrate-pan-maxbr-zero", "pan", 60, BITRATE "fMaxFrameRate=10;ts.num=100;ts.den=1;iTargetBitrate=200000;"
   "iMaxBitrate=200000;ts.zero=30"},

  // The IDR target, the overshoot fix and the IDR band search, IDRs in the stream.
  {"bitrate-overshoot-off", "pan", 40, BITRATE "iTargetBitrate=256000;bFixRCOverShoot=0"},
  {"bitrate-overshoot-off-2m", "subpel", 20, BITRATE "iTargetBitrate=2000000;bFixRCOverShoot=0"},
  {"bitrate-intra-period-10", "mixed", 50, BITRATE "iTargetBitrate=128000;uiIntraPeriod=10"},
  {"bitrate-intra-period-8-no-skip", "pan", 40, BITRATE "iTargetBitrate=128000;uiIntraPeriod=8;bEnableFrameSkip=0"},
  {"bitrate-idr-ratio-150", "pan", 40, BITRATE "iTargetBitrate=128000;uiIntraPeriod=8;iIdrBitrateRatio=150"},
  {"bitrate-idr-ratio-800", "mixed", 40, BITRATE "iTargetBitrate=128000;uiIntraPeriod=6;iIdrBitrateRatio=800"},
  // Many IDRs: the intra complexity model's running mean (RcUpdateIntraComplexity).
  {"bitrate-mixed-intra-period-6-no-skip", "mixed", 90, BITRATE "iTargetBitrate=128000;uiIntraPeriod=6;bEnableFrameSkip=0"},
  {"bitrate-mixed-intra-period-4-no-skip", "mixed", 60, BITRATE "iTargetBitrate=200000;uiIntraPeriod=4;bEnableFrameSkip=0"},

  // QP limits.
  {"bitrate-qp-20-30", "mixed", 40, BITRATE "iTargetBitrate=128000;iMinQp=20;iMaxQp=30"},
  {"bitrate-qp-30-32", "noise", 30, BITRATE "iTargetBitrate=64000;iMinQp=30;iMaxQp=32"},
  // iMinQp and iMaxQp both default: camera content resets them to 12 and 42 when either is 0.
  {"bitrate-random-qp-12-36-no-skip", "random", 20, BITRATE "iTargetBitrate=16000;iMinQp=12;iMaxQp=36;bEnableFrameSkip=0"},
  {"bitrate-static-qp-min-30", "static", 30, BITRATE "iTargetBitrate=1000000;iMinQp=30"},

  // Temporal layers: the layer weights, per layer QP ranges and the VGOP.
  {"bitrate-temporal-2", "pan", 40, BITRATE "iTargetBitrate=192000;iTemporalLayerNum=2;bEnableFrameSkip=0"},
  {"bitrate-temporal-3", "mixed", 60, BITRATE "iTargetBitrate=128000;iTemporalLayerNum=3"},
  {"bitrate-temporal-4", "block", 48, BITRATE "iTargetBitrate=192000;iTemporalLayerNum=4"},
  {"bitrate-temporal-3-intra-period-12", "mixed", 48, BITRATE "iTargetBitrate=128000;iTemporalLayerNum=3;uiIntraPeriod=12"},

  // Slices: GOM QP off with more than one slice; one GOM a frame for raster slices.
  {"bitrate-slices-fixed-4", "pan", 30, BITRATE "iTargetBitrate=256000;L0.uiSliceMode=1;L0.uiSliceNum=4;bEnableFrameSkip=0"},
  {"bitrate-slices-raster-rows", "mixed", 30, BITRATE "iTargetBitrate=128000;L0.uiSliceMode=2;bEnableFrameSkip=0"},
  {"bitrate-slices-raster-2", "pan", 30, BITRATE "iTargetBitrate=128000;L0.uiSliceMode=2;L0.uiSliceMbNum=60,39"},

  // Padding: the padding buffer underflowing into filler data.
  {"bitrate-static-padding", "static", 30, BITRATE "iTargetBitrate=1000000;iPaddingFlag=1"},
  {"bitrate-subpel-padding-1m-no-skip", "subpel", 40, BITRATE "iTargetBitrate=1000000;iPaddingFlag=1;bEnableFrameSkip=0"},
  {"bitrate-block-padding-1m-no-skip", "block", 40, BITRATE "iTargetBitrate=1000000;iPaddingFlag=1;bEnableFrameSkip=0"},

  // Frame rates, references and LTR.
  {"bitrate-5fps", "pan", 30, BITRATE "fMaxFrameRate=5;ts.num=200;ts.den=1;iTargetBitrate=64000"},
  {"bitrate-60fps", "pan", 40, BITRATE "fMaxFrameRate=60;ts.num=1000;ts.den=60;iTargetBitrate=256000"},
  {"bitrate-ref-4", "pan", 30, BITRATE "iTargetBitrate=128000;iNumRefFrame=4;bEnableFrameSkip=0"},
  {"bitrate-ltr", "mixed", 40, BITRATE "iTargetBitrate=128000;bEnableLongTermReference=1;iLtrMarkPeriod=4;bEnableFrameSkip=0"},

  // Sizes: the macroblock width classes and the area bands, and cropping.
  {"bitrate-33x17", "pan", 30, RC_BASE "iRCMode=1;iPicWidth=33;iPicHeight=17;iTargetBitrate=20000"},
  {"bitrate-152x100", "mixed", 30, RC_BASE "iRCMode=1;iPicWidth=152;iPicHeight=100;iTargetBitrate=96000;bEnableFrameSkip=0"},
  {"bitrate-320x192", "mixed", 20, RC_BASE "iRCMode=1;iPicWidth=320;iPicHeight=192;iTargetBitrate=300000;bEnableFrameSkip=0"},
  {"bitrate-480x272", "pan", 12, RC_BASE "iRCMode=1;iPicWidth=480;iPicHeight=272;iTargetBitrate=600000;bEnableFrameSkip=0"},
  {"bitrate-640x368", "pan", 10, RC_BASE "iRCMode=1;iPicWidth=640;iPicHeight=368;iTargetBitrate=800000;bEnableFrameSkip=0"},
  {"bitrate-1280x720", "pan", 5, RC_BASE "iRCMode=1;iPicWidth=1280;iPicHeight=720;iTargetBitrate=1500000;bEnableFrameSkip=0"},

  // Buffer based mode: no target, the QP stepping down to the minimum. Random content at
  // QP 24 then overflows the frame's buffer, and the encoder uninitialises itself.
  {"buffer-pan", "pan", 30, BUFFER "iTargetBitrate=128000"},
  {"buffer-mixed-qp-30-40", "mixed", 40, BUFFER "iTargetBitrate=128000;iMinQp=30;iMaxQp=40"},
  {"buffer-mixed-overflow", "mixed", 12, BUFFER "iTargetBitrate=128000;iMinQp=24;iMaxQp=40"},

  // Timestamp mode: the buffer drains by the time between frames.
  {"timestamp-pan-256k", "pan", 40, TIMESTAMP "iTargetBitrate=256000"},
  {"timestamp-mixed-128k", "mixed", 60, TIMESTAMP "iTargetBitrate=128000"},
  {"timestamp-mixed-64k-no-skip", "mixed", 40, TIMESTAMP "iTargetBitrate=64000;bEnableFrameSkip=0"},
  {"timestamp-4fps", "pan", 30, TIMESTAMP "fMaxFrameRate=4;ts.num=250;ts.den=1;iTargetBitrate=64000"},
  {"timestamp-7fps", "mixed", 30, TIMESTAMP "fMaxFrameRate=7;ts.num=1000;ts.den=7;iTargetBitrate=96000"},
  {"timestamp-jitter", "mixed", 60, TIMESTAMP "iTargetBitrate=128000;ts.jitter=20"},
  {"timestamp-gap", "pan", 40, TIMESTAMP "iTargetBitrate=128000;ts.gap=20:1500"},
  {"timestamp-back", "pan", 40, TIMESTAMP "iTargetBitrate=128000;ts.back=20:300"},
  {"timestamp-zero", "pan", 30, TIMESTAMP "iTargetBitrate=128000;ts.zero=15"},
  {"timestamp-temporal-3", "mixed", 48, TIMESTAMP "iTargetBitrate=128000;iTemporalLayerNum=3"},
  {"timestamp-padding", "static", 30, TIMESTAMP "iTargetBitrate=1000000;iPaddingFlag=1"},
  {"timestamp-intra-period-15", "mixed", 45, TIMESTAMP "iTargetBitrate=128000;uiIntraPeriod=15"},
  {"timestamp-intra-period-5", "mixed", 60, TIMESTAMP "iTargetBitrate=160000;uiIntraPeriod=5"},
  // Pauses either side of WelsRcFrameDelayJudgeTimeStamp's 1 s limit (an interval above it
  // counts as one frame period). The two pan rows write the same stream, as the pause falls
  // in a run of skips, but a limit moved by a millisecond either way changes one of them.
  {"timestamp-pause-950", "pan", 40, TIMESTAMP "iTargetBitrate=128000;ts.gap=20:917"},
  {"timestamp-pause-1000", "pan", 40, TIMESTAMP "iTargetBitrate=128000;ts.gap=20:967"},
  {"timestamp-mixed-pause-950", "mixed", 40, TIMESTAMP "iTargetBitrate=96000;ts.gap=12:917"},
  {"timestamp-mixed-pause-1000", "mixed", 40, TIMESTAMP "iTargetBitrate=96000;ts.gap=12:967"},
  // IDR targets: below 5 fps one frame's bits, else four; clipped to the buffer's headroom.
  {"timestamp-pan-4fps-intra-period-2", "pan", 40, TIMESTAMP "fMaxFrameRate=4;ts.num=250;ts.den=1;iTargetBitrate=96000;"
   "uiIntraPeriod=2"},
  {"timestamp-subpel-10fps-intra-period-2", "subpel", 60, TIMESTAMP "fMaxFrameRate=10;ts.num=100;ts.den=1;"
   "iTargetBitrate=150000;uiIntraPeriod=2"},
};

// The preprocessed streams (E7): camera content with GetDefaultParams' analysis on (scene
// change detection, background detection and adaptive quantisation), then each alone, the
// denoiser, rate control, the GOP structures scene changes interact with, the prefix NAL, a
// source larger than the layer (the downsampling's paths), sizes that are not whole
// macroblocks, and Initialize's base API. ParamValidation turns the adaptive quantisation
// off whatever the setting ("algorithms needs to be refactored"), so aq-mixed codes as with
// it off; the strategy itself is checked by the aq kernel rows.
#define PP_BASE "api=ext;iMultipleThreadIdc=1;fMaxFrameRate=30;"
#define PP PP_BASE QCIF
#define SCD_ONLY "bEnableBackgroundDetection=0;bEnableAdaptiveQuant=0;"
#define BGD_ONLY "bEnableAdaptiveQuant=0;bEnableSceneChangeDetect=0;"
#define AQ_ONLY "bEnableBackgroundDetection=0;bEnableSceneChangeDetect=0;"
#define NONE "bEnableBackgroundDetection=0;bEnableAdaptiveQuant=0;bEnableSceneChangeDetect=0;"
#define CIF_TO_QCIF "iPicWidth=352;iPicHeight=288;L0.iVideoWidth=176;L0.iVideoHeight=144;"
const StreamConfig kPreprocessConfigs[] = {
  // Fixed QP, the defaults on.
  {"pp-static", "static", 10, PP "iRCMode=-1"},
  {"pp-pan", "pan", 12, PP "iRCMode=-1"},
  {"pp-block", "block", 16, PP "iRCMode=-1"},
  {"pp-noise", "noise", 8, PP "iRCMode=-1"},
  {"pp-random", "random", 6, PP "iRCMode=-1"},
  {"pp-cut", "cut", 10, PP "iRCMode=-1"},
  {"pp-cuts", "cuts", 24, PP "iRCMode=-1"},
  {"pp-partcut", "partcut", 12, PP "iRCMode=-1"},
  {"pp-fade", "fade", 10, PP "iRCMode=-1"},
  {"pp-subpel-high", "subpel", 10, PP "iRCMode=-1;iComplexityMode=2"},
  {"pp-mixed", "mixed", 60, PP "iRCMode=-1"},
  {"none-camera", "camera", 16, PP "iRCMode=-1;" NONE},

  // One analysis at a time, and the denoiser.
  {"scd-cuts", "cuts", 24, PP "iRCMode=-1;" SCD_ONLY},
  {"scd-partcut", "partcut", 12, PP "iRCMode=-1;" SCD_ONLY},
  {"scd-mixed", "mixed", 45, PP "iRCMode=-1;" SCD_ONLY},
  {"bgd-camera", "camera", 16, PP "iRCMode=-1;" BGD_ONLY},
  {"bgd-camera-medium", "camera", 12, PP "iRCMode=-1;iComplexityMode=1;" BGD_ONLY},
  {"bgd-mixed", "mixed", 40, PP "iRCMode=-1;" BGD_ONLY},
  {"aq-mixed", "mixed", 40, PP "iRCMode=-1;" AQ_ONLY},
  {"denoise-noise", "noise", 10, PP "iRCMode=-1;bEnableDenoise=1"},
  {"denoise-pan", "pan", 10, PP "iRCMode=-1;bEnableDenoise=1;" NONE},
  {"denoise-camera", "camera", 16, PP "iRCMode=-1;bEnableDenoise=1"},
  {"denoise-mixed-high", "mixed", 32, PP "iRCMode=-1;bEnableDenoise=1;iComplexityMode=2"},

  // Rate control: the background flags in the GOM complexity and the mode decision, scene
  // changes against the skip logic.
  {"quality-mixed-128k", "mixed", 60, PP "iRCMode=0;iTargetBitrate=128000"},
  {"quality-camera-96k", "camera", 30, PP "iRCMode=0;iTargetBitrate=96000"},
  {"bitrate-mixed-128k", "mixed", 60, PP "iRCMode=1;iTargetBitrate=128000"},
  {"bitrate-block-96k", "block", 30, PP "iRCMode=1;iTargetBitrate=96000"},
  {"bitrate-camera-96k", "camera", 40, PP "iRCMode=1;iTargetBitrate=96000"},
  {"bitrate-camera-96k-none", "camera", 40, PP "iRCMode=1;iTargetBitrate=96000;" NONE},
  {"bitrate-cuts-128k", "cuts", 36, PP "iRCMode=1;iTargetBitrate=128000"},
  {"bitrate-mixed-bgd-only", "mixed", 45, PP "iRCMode=1;iTargetBitrate=128000;" BGD_ONLY},
  {"bitrate-mixed-denoise", "mixed", 30, PP "iRCMode=1;iTargetBitrate=128000;bEnableDenoise=1"},
  {"bitrate-camera-denoise", "camera", 30, PP "iRCMode=1;iTargetBitrate=96000;bEnableDenoise=1"},
  {"buffer-pan", "pan", 20, PP "iRCMode=2;iTargetBitrate=128000"},
  {"timestamp-mixed-128k", "mixed", 45, PP "iRCMode=3;iTargetBitrate=128000"},

  // Scene changes in GOP structures: intra periods, temporal layers, references, LTR.
  {"cuts-intra-period-8", "cuts", 24, PP "iRCMode=-1;uiIntraPeriod=8"},
  {"cuts-intra-period-6", "cuts", 24, PP "iRCMode=-1;uiIntraPeriod=6"},
  {"cuts-temporal-3", "cuts", 24, PP "iRCMode=-1;iTemporalLayerNum=3"},
  {"mixed-temporal-4", "mixed", 36, PP "iRCMode=-1;iTemporalLayerNum=4"},
  {"bitrate-cuts-temporal-2", "cuts", 30, PP "iRCMode=1;iTargetBitrate=128000;iTemporalLayerNum=2"},
  {"cuts-ref-4", "cuts", 24, PP "iRCMode=-1;iNumRefFrame=4"},
  {"cuts-ltr", "cuts", 24, PP "iRCMode=-1;bEnableLongTermReference=1;iLtrMarkPeriod=4"},
  {"bitrate-mixed-ltr", "mixed", 40, PP "iRCMode=1;iTargetBitrate=128000;bEnableLongTermReference=1;iLtrMarkPeriod=4"},
  {"cuts-slices-fixed-3", "cuts", 18, PP "iRCMode=-1;L0.uiSliceMode=1;L0.uiSliceNum=3"},

  // The prefix NAL ahead of each slice of the AVC base layer.
  {"prefix-nal-pan", "pan", 8, PP "iRCMode=-1;bPrefixNalAddingCtrl=1"},
  {"prefix-nal-temporal-3", "pan", 12, PP "iRCMode=-1;bPrefixNalAddingCtrl=1;iTemporalLayerNum=3"},
  {"prefix-nal-slices", "cuts", 12, PP "iRCMode=-1;bPrefixNalAddingCtrl=1;L0.uiSliceMode=1;L0.uiSliceNum=2"},

  // A source larger than the layer: halving, twice, the general ratio, a halving then the
  // general ratio, odd source sizes, with the denoiser and with rate control.
  {"scale-half", "pan", 8, PP_BASE "iRCMode=-1;" CIF_TO_QCIF},
  {"scale-half-mixed", "mixed", 40, PP_BASE "iRCMode=-1;" CIF_TO_QCIF},
  {"scale-quarter", "pan", 6, PP_BASE "iRCMode=-1;iPicWidth=704;iPicHeight=576;L0.iVideoWidth=176;L0.iVideoHeight=144"},
  {"scale-general", "pan", 8, PP_BASE "iRCMode=-1;iPicWidth=320;iPicHeight=240;L0.iVideoWidth=176;L0.iVideoHeight=144"},
  {"scale-half-general", "block", 6, PP_BASE "iRCMode=-1;iPicWidth=528;iPicHeight=432;L0.iVideoWidth=176;"
   "L0.iVideoHeight=144"},
  {"scale-odd", "mixed", 12, PP_BASE "iRCMode=-1;iPicWidth=357;iPicHeight=291;L0.iVideoWidth=176;L0.iVideoHeight=144"},
  {"scale-to-odd", "pan", 8, PP_BASE "iRCMode=-1;iPicWidth=320;iPicHeight=192;L0.iVideoWidth=150;L0.iVideoHeight=90"},
  {"scale-denoise", "noise", 6, PP_BASE "iRCMode=-1;bEnableDenoise=1;" CIF_TO_QCIF},
  {"scale-bitrate", "mixed", 30, PP_BASE "iRCMode=1;iTargetBitrate=128000;" CIF_TO_QCIF},

  // Sizes: partial macroblocks, the background detection's OU rows, a larger picture.
  {"pp-33x17", "pan", 10, PP_BASE "iRCMode=-1;iPicWidth=33;iPicHeight=17"},
  {"pp-152x100", "mixed", 30, PP_BASE "iRCMode=-1;iPicWidth=152;iPicHeight=100"},
  {"pp-100x60-denoise", "noise", 8, PP_BASE "iRCMode=-1;iPicWidth=100;iPicHeight=60;bEnableDenoise=1"},
  {"bitrate-320x192", "mixed", 20, PP_BASE "iRCMode=1;iPicWidth=320;iPicHeight=192;iTargetBitrate=300000"},

  // Initialize: a zeroed SEncParamBase, so the quality mode and every default.
  {"base-mixed", "mixed", 40, "api=base;iPicWidth=176;iPicHeight=144;iTargetBitrate=256000;fMaxFrameRate=30"},
  {"base-cuts-bitrate", "cuts", 24, "api=base;iPicWidth=176;iPicHeight=144;iTargetBitrate=128000;fMaxFrameRate=30;"
   "iRCMode=1"},
  {"base-152x100", "block", 20, "api=base;iPicWidth=152;iPicHeight=100;iTargetBitrate=200000;fMaxFrameRate=12"},
};
#undef CIF_TO_QCIF
#undef NONE
#undef AQ_ONLY
#undef BGD_ONLY
#undef SCD_ONLY
#undef PP
#undef PP_BASE
#undef TIMESTAMP
#undef BUFFER
#undef BITRATE
#undef QUALITY
#undef RC_BASE
#undef QCIF

struct StreamFrame {
  int rc;
  std::string type, layers, tid;
  std::vector<unsigned char> bytes;
};

// The source timestamps of a configuration (E6): frame f at f * num / den ms, 1000 / 30 by
// default, shaped by the "ts." settings, which go here and not to the encoder:
//   ts.num, ts.den   the period, num / den ms
//   ts.jitter=n      up to n ms either way, from a hash of the frame index
//   ts.gap=f:ms      from frame f on, ms later (a pause)
//   ts.back=f:ms     from frame f on, ms earlier (a step back)
//   ts.zero=f        frame f at 0
// A timestamp below 0 is 0. The C# test has the same schedule (RcTests.cs).
struct TimestampSchedule {
  long long num = 1000, den = 30, gapMs = 0, backMs = 0;
  int jitter = 0, gapFrame = -1, backFrame = -1, zeroFrame = -1;

  static void FrameAndMs (const std::string& v, int* pFrame, long long* pMs) {
    const size_t colon = v.find (':');
    *pFrame = (int)strtol (v.substr (0, colon).c_str(), NULL, 10);
    *pMs = colon == std::string::npos ? 0 : strtoll (v.substr (colon + 1).c_str(), NULL, 10);
  }
  bool Apply (const std::string& k, const std::string& v) {
    if (k == "ts.num") num = strtoll (v.c_str(), NULL, 10);
    else if (k == "ts.den") den = strtoll (v.c_str(), NULL, 10);
    else if (k == "ts.jitter") jitter = (int)strtol (v.c_str(), NULL, 10);
    else if (k == "ts.gap") FrameAndMs (v, &gapFrame, &gapMs);
    else if (k == "ts.back") FrameAndMs (v, &backFrame, &backMs);
    else if (k == "ts.zero") zeroFrame = (int)strtol (v.c_str(), NULL, 10);
    else return false;
    return true;
  }
  long long At (int f) const {
    long long t = (long long)f * num / den;
    if (jitter > 0) t += (long long) (Hash32 ((uint32_t)f ^ 0x5bd1e995u) % (uint32_t) (2 * jitter + 1)) - jitter;
    if (gapFrame >= 0 && f >= gapFrame) t += gapMs;
    if (backFrame >= 0 && f >= backFrame) t -= backMs;
    if (f == zeroFrame) t = 0;
    return t < 0 ? 0 : t;
  }
};

// Encodes one configuration's frames, at the timestamps its schedule gives. Returns
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
  TimestampSchedule ts;
  bool isBase = false;   // api=base (E7): Initialize with a zeroed SEncParamBase
  std::stringstream ss (c.settings);
  std::string item;
  while (std::getline (ss, item, ';')) {
    if (item.empty()) continue;
    size_t eq = item.find ('=');
    std::string k = item.substr (0, eq), v = item.substr (eq + 1);
    if (k == "api") {
      isBase = v == "base";
      continue;
    }
    if (k.compare (0, 3, "ts.") == 0 ? !ts.Apply (k, v) : !ApplySetting (ext, base, isBase, k, v)) {
      fprintf (stderr, "%s: unknown setting %s\n", c.name, k.c_str());
      exit (1);
    }
  }

  int rv = isBase ? encoder->Initialize (&base) : encoder->InitializeExt (&ext);
  if (rv == cmResultSuccess) {
    const int w = isBase ? base.iPicWidth : ext.iPicWidth, h = isBase ? base.iPicHeight : ext.iPicHeight;
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
      pic.uiTimeStamp = ts.At (f);

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

// The frame by frame table of one set of configurations (stream, rc).
static int PrintStreams (const StreamConfig* configs, size_t count, const char* what) {
  printf ("# %s, C-only reference build. Frame by frame %s per configuration, see refharness/src/encoder.cpp.\n",
          g_strCodecVer, what);
  printf ("# config\t<name>\t<content>\t<frames>\t<settings>\n");
  printf ("# frame\t<name>\t<index>\t<EncodeFrame>\t<frame type>\t<temporal ids>\t<layers>\t<sha1 of the frame>\t<size>\n");
  printf ("# stream\t<name>\t<InitializeExt>\t<sha1 of the stream>\t<size>\n");
  for (size_t n = 0; n < count; n++) {
    const StreamConfig& c = configs[n];
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

// One configuration's stream, to a file.
static int DumpStream (const StreamConfig* configs, size_t count, const char* name, const char* outPath) {
  for (size_t n = 0; n < count; n++) {
    const StreamConfig& c = configs[n];
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

int Stream() {
  return PrintStreams (kStreamConfigs, sizeof kStreamConfigs / sizeof kStreamConfigs[0], "P streams");
}

int StreamDump (const char* name, const char* outPath) {
  return DumpStream (kStreamConfigs, sizeof kStreamConfigs / sizeof kStreamConfigs[0], name, outPath);
}

int Rc() {
  return PrintStreams (kRcConfigs, sizeof kRcConfigs / sizeof kRcConfigs[0], "rate controlled streams");
}

int RcDump (const char* name, const char* outPath) {
  return DumpStream (kRcConfigs, sizeof kRcConfigs / sizeof kRcConfigs[0], name, outPath);
}

int Preprocess() {
  return PrintStreams (kPreprocessConfigs, sizeof kPreprocessConfigs / sizeof kPreprocessConfigs[0],
                       "preprocessed streams");
}

int PreprocessDump (const char* name, const char* outPath) {
  return DumpStream (kPreprocessConfigs, sizeof kPreprocessConfigs / sizeof kPreprocessConfigs[0], name, outPath);
}

// ---------------------------------------------------------------------------
// api
// ---------------------------------------------------------------------------

namespace {

// E8: scripted calls of the encoder API. Each configuration is settings (as kStreamConfigs,
// ts. keys included), content (StreamSample) and a script of space separated operations,
// where op*n repeats an operation n times:
//   F            EncodeFrame of the next frame of the content, at the schedule's timestamp
//   F:fmt=n      the same with iColorFormat n
//   F:WxH        the same with a W x H picture (the next frame of the content at that size)
//   I / i        ForceIntraFrame (true) / ForceIntraFrame (false)
//   P            EncodeParameterSets
//   U            Uninitialize
//   R            Initialize or InitializeExt again with the configuration's settings
//   S:key=value  SetOption, see ApiSet
//   G:key        GetOption, see ApiGet
// For each operation the harness prints the return code and what it produced: for F and P
// the frame type, the temporal ids, the layers, the SHA1 and size of the output, for G the
// value. The C# test (ApiTests.cs) runs the same script on its port and compares.
struct ApiConfig {
  const char* name;
  const char* content;
  const char* settings;
  const char* script;
};

#define API_BASE "api=ext;iRCMode=-1;iMultipleThreadIdc=1;fMaxFrameRate=30;bEnableBackgroundDetection=0;" \
  "bEnableAdaptiveQuant=0;bEnableSceneChangeDetect=0;iPicWidth=176;iPicHeight=144;"
#define API_RC "api=ext;iMultipleThreadIdc=1;fMaxFrameRate=30;bEnableBackgroundDetection=0;" \
  "bEnableAdaptiveQuant=0;bEnableSceneChangeDetect=0;iPicWidth=176;iPicHeight=144;"
#define API_PP "api=ext;iMultipleThreadIdc=1;fMaxFrameRate=30;iPicWidth=176;iPicHeight=144;"
const ApiConfig kApiConfigs[] = {
  // ForceIntraFrame: IDRs mid stream and mid GOP, with the coding index, the reference
  // lists, the LTR state, the rate control and the parameter set ids following.
  {"force-idr", "pan", API_BASE, "F*3 I F*2 I I F*3"},
  {"force-idr-first", "pan", API_BASE, "I F*3"},
  {"force-p", "pan", API_BASE, "F*2 i F*2"},
  {"force-idr-temporal-3", "pan", API_BASE "iTemporalLayerNum=3", "F*5 I F*6 I F*3"},
  {"force-idr-temporal-4-intra-8", "block", API_BASE "iTemporalLayerNum=4;uiIntraPeriod=8", "F*3 I F*12"},
  {"force-idr-intra-period-4", "pan", API_BASE "uiIntraPeriod=4", "F*2 I F*7"},
  {"force-idr-ltr", "pan", API_BASE "bEnableLongTermReference=1;iLtrMarkPeriod=3", "F*4 I F*6"},
  {"force-idr-ref-4", "pan", API_BASE "iNumRefFrame=4", "F*5 I F*4"},
  {"force-idr-slices", "pan", API_BASE "L0.uiSliceMode=1;L0.uiSliceNum=4", "F*2 I F*3"},
  {"force-idr-prefix", "pan", API_BASE "bPrefixNalAddingCtrl=1", "F*2 I F*2"},
  {"force-idr-strategy-increasing", "pan", API_BASE "eSpsPpsIdStrategy=1", "F I F I F*2 I F"},
  {"force-idr-strategy-sps-listing", "pan", API_BASE "eSpsPpsIdStrategy=2", "F I F*2 I F"},
  {"force-idr-strategy-sps-pps-listing", "pan", API_BASE "eSpsPpsIdStrategy=6", "F I F*2 I F"},
  {"force-idr-bitrate", "mixed", API_RC "iRCMode=1;iTargetBitrate=256000", "F*8 I F*8 I F*4"},
  {"force-idr-quality", "mixed", API_RC "iRCMode=0;iTargetBitrate=128000", "F*12 I F*10"},
  {"force-idr-timestamp", "mixed", API_RC "iRCMode=3;iTargetBitrate=128000", "F*10 I F*10"},
  {"force-idr-preprocess", "camera", API_PP "iRCMode=1;iTargetBitrate=200000", "F*6 I F*8"},
  {"parameter-sets", "pan", API_BASE "eSpsPpsIdStrategy=1", "P F P F I F P"},

  // SetOption and GetOption, one group of options at a time.
  {"set-idr-interval", "pan", API_BASE, "G:idr F*2 S:idr=3 G:idr F*5 S:idr=0 F*3 S:idr=-5 G:idr F S:idr=4 S:idr=4 F*4"},
  {"set-bitrate", "mixed", API_RC "iRCMode=1;iTargetBitrate=256000",
   "F*10 S:br=4:64000 F*10 S:br=0:500000 F*10 G:br=4 G:br=0 G:maxbr=4 S:br=7:1000 S:br=4:0 G:br=9"},
  {"set-max-bitrate", "mixed", API_RC "iRCMode=1;iTargetBitrate=256000;iMaxBitrate=400000",
   "F*8 S:maxbr=4:300000 F*8 S:maxbr=0:260000 F*8 G:maxbr=4 G:maxbr=0 S:maxbr=4:100000 G:ext"},
  {"set-frame-rate", "mixed", API_RC "iRCMode=1;iTargetBitrate=256000",
   "F*10 S:fps=15 G:fps F*10 S:fps=0 S:fps=100 G:fps S:fps=7.5 G:fps F*5 G:ext"},
  {"set-rc-mode", "mixed", API_RC "iRCMode=0;iTargetBitrate=200000", "F*8 S:rc=1 F*8 S:rc=3 F*8 S:rc=-1 F*4 S:rc=1 F*4"},
  {"set-frame-skip", "mixed", API_RC "iRCMode=1;iTargetBitrate=64000", "F*10 S:skip=0 F*10 S:skip=1 F*10"},
  {"set-frame-skip-rc-off", "pan", API_BASE, "S:skip=0 F*2"},
  {"set-padding", "static", API_RC "iRCMode=1;iTargetBitrate=400000", "F*6 S:pad=1 F*10 S:pad=0 F*4"},
  {"set-ltr", "pan", API_BASE, "F*3 S:ltr=1:2 G:ext F*6 S:ltr=0:0 G:ext F*3"},
  {"set-ltr-period", "pan", API_BASE "bEnableLongTermReference=1;iLtrMarkPeriod=5", "F*3 S:ltrperiod=2 F*8"},
  {"set-ltr-feedback", "block", API_BASE "bEnableLongTermReference=1;iLtrMarkPeriod=3",
   "F*4 S:ltrfb=4:0:0:0 F*3 S:ltrfb=5:0:3:0 F*2 S:ltrreq=1:0:2:5:0 F*3 S:ltrreq=2:0:0:0:0 F*3 S:ltrreq=0:0:0:0:0 F*2"},
  {"set-ltr-feedback-wrong-idr", "block", API_BASE "bEnableLongTermReference=1;iLtrMarkPeriod=3",
   "F*4 S:ltrfb=4:7:0:0 F*3 S:ltrreq=1:7:2:5:0 F*3 S:ltrreq=1:0:-1:5:0 F*2"},
  // IDRs mid GOP that ForceCodingIDR does not start, so only DecideFrameType resets the
  // coding index: an IDR request, and an IDR interval that is not a multiple of the GOP
  // (SetOption does not check it, as InitializeExt does).
  {"set-idr-request-temporal-3", "pan", API_BASE "iTemporalLayerNum=3", "F*5 S:ltrreq=2:0:0:0:0 F*7 S:ltrreq=1:0:3:6:0 F*5"},
  {"set-ltr-idr-request-temporal-3", "pan", API_BASE "iTemporalLayerNum=3;bEnableLongTermReference=1",
   "F*5 S:ltrreq=1:0:-1:6:0 F*6 S:ltrreq=1:1:-1:6:0 F*6 S:ltrreq=1:2:-1:3:0 F*3"},
  {"set-idr-interval-temporal-3", "pan", API_BASE "iTemporalLayerNum=3", "F*2 S:idr=3 F*11"},
  {"set-ltr-feedback-temporal-2", "block", API_BASE "iTemporalLayerNum=2;bEnableLongTermReference=1;iLtrMarkPeriod=2",
   "F*3 S:ltrfb=4:0:0:0 F*3 S:ltrfb=4:0:2:0 F*3 S:ltrfb=5:0:4:0 F*2 S:ltrreq=1:0:2:9:0 F*4"},
  // Feedback that matches: the first IDR's idr_pic_id is 0 and the encoder's is 1 after it.
  // Successful markings move the LTR index round (LONG_TERM_REF_NUM), a recovery request
  // after one makes the next frames reference the LTR, and with the preprocessing on the
  // VAA reads the LTR's source picture.
  {"ltr-marking-success", "pan", API_BASE "bEnableLongTermReference=1;iLtrMarkPeriod=3",
   "F*4 S:ltrfb=4:1:3:0 F*3 S:ltrfb=4:1:6:0 F*3 S:ltrfb=4:1:9:0 F*3 S:ltrfb=4:1:12:0 F*3 S:ltrfb=5:1:15:0 F*3"},
  {"ltr-marking-success-temporal-2", "block", API_BASE "iTemporalLayerNum=2;bEnableLongTermReference=1;iLtrMarkPeriod=2",
   "F*3 S:ltrfb=4:1:1:0 F*2 S:ltrfb=4:1:2:0 F*2 S:ltrfb=4:1:3:0 F*2 S:ltrfb=4:1:4:0 F*2 S:ltrfb=4:1:5:0 F*3"},
  {"ltr-recovery", "pan", API_BASE "bEnableLongTermReference=1;iLtrMarkPeriod=3",
   "F*4 S:ltrfb=4:1:3:0 F*4 S:ltrreq=1:1:3:7:0 F*4 S:ltrfb=4:1:9:0 F*2 S:ltrreq=1:1:8:11:0 F*3 S:ltrreq=1:1:20:22:0 F*2"},
  {"ltr-recovery-preprocess", "camera", API_PP "iRCMode=-1;bEnableLongTermReference=1;iLtrMarkPeriod=3",
   "F*4 S:ltrfb=4:1:3:0 F*4 S:ltrreq=1:1:3:7:0 F*4 S:ltrfb=4:1:9:0 F*2 S:ltrreq=1:1:8:11:0 F*4"},
  {"ltr-recovery-preprocess-temporal-2", "camera",
   API_PP "iRCMode=1;iTargetBitrate=300000;iTemporalLayerNum=2;bEnableLongTermReference=1;iLtrMarkPeriod=2",
   "F*4 S:ltrfb=4:1:2:0 F*3 S:ltrreq=1:1:2:4:0 F*5 S:ltrfb=4:1:6:0 F*2 S:ltrreq=1:1:5:8:0 F*5"},
  {"ltr-recovery-failed", "block", API_BASE "bEnableLongTermReference=1;iLtrMarkPeriod=3",
   "F*4 S:ltrfb=5:1:3:0 F*3 S:ltrreq=1:1:0:5:0 F*3 S:ltrreq=1:1:4:-1:0 F*3"},
  {"set-strategy", "pan", API_BASE, "F S:strategy=1 F I F S:strategy=2 S:strategy=5 F S:strategy=0 F I F G:ext"},
  {"set-strategy-listing", "pan", API_BASE "eSpsPpsIdStrategy=2", "F S:strategy=0 S:strategy=3 S:strategy=2 F I F"},
  {"set-profile-level", "pan", API_BASE,
   "S:profile=0:77 S:level=0:31 G:ext F I F S:profile=5:66 S:level=-1:30 S:profile=0:100 S:level=0:9 G:ext F"},
  {"set-number-ref", "pan", API_BASE, "F S:nref=4 G:ext F*3 S:nref=0 G:ext F*2"},
  {"set-delivery", "mixed", API_RC "iRCMode=1;iTargetBitrate=128000", "F*4 S:delivery=0:0:0 F*4 S:delivery=1:0:0 F*4"},
  {"set-complexity", "pan", API_BASE, "F*2 S:complexity=2 G:complexity F*3 S:complexity=1 G:complexity F*2"},
  {"set-misc", "pan", API_BASE,
   "S:ssei=1 S:prefix=1 F I F S:prefix=0 F S:lossless=1 S:vary=50 S:vary=150 S:fmt=0 S:fmt=23 G:fmt S:interpred=1 "
   "G:interpred S:getstats=0 S:statint=1000 G:statint F G:ext"},
  {"set-unknown", "pan", API_BASE, "S:unknown=0 G:unknown G:ltr F"},

  // Reconfiguration: SVC_ENCODE_PARAM_BASE and _EXT, with and without a reset, and the
  // resolution changes the statistics count.
  {"set-base-resize", "pan", "api=base;iUsageType=0;iPicWidth=176;iPicHeight=144;iTargetBitrate=300000;iRCMode=1;"
   "fMaxFrameRate=30", "F*3 S:base=iPicWidth:160+iPicHeight:96 G:base F*3 S:base=iPicWidth:176+iPicHeight:144 F*2 G:stats"},
  {"set-base-bitrate", "mixed", "api=base;iUsageType=0;iPicWidth=176;iPicHeight=144;iTargetBitrate=300000;iRCMode=1;"
   "fMaxFrameRate=30", "F*6 S:base=iTargetBitrate:100000 G:base F*6 S:base=fMaxFrameRate:15 G:base F*6"},
  {"set-ext-resize", "pan", API_BASE,
   "F*3 S:ext=iPicWidth:160+iPicHeight:96+L0.iVideoWidth:160+L0.iVideoHeight:96 F*3 "
   "S:ext=iPicWidth:176+iPicHeight:144+L0.iVideoWidth:176+L0.iVideoHeight:144 F*2 G:stats G:ext"},
  {"set-ext-resize-sps-listing", "pan", API_BASE "eSpsPpsIdStrategy=2",
   "F*3 S:ext=iPicWidth:160+iPicHeight:96+L0.iVideoWidth:160+L0.iVideoHeight:96 F*3 "
   "S:ext=iPicWidth:176+iPicHeight:144+L0.iVideoWidth:176+L0.iVideoHeight:144 F*2"},
  {"set-ext-resize-sps-pps-listing", "pan", API_BASE "eSpsPpsIdStrategy=6",
   "F*3 S:ext=iPicWidth:160+iPicHeight:96+L0.iVideoWidth:160+L0.iVideoHeight:96 F*3 "
   "S:ext=iPicWidth:176+iPicHeight:144+L0.iVideoWidth:176+L0.iVideoHeight:144 F*2"},
  {"set-ext-no-reset", "mixed", API_RC "iRCMode=1;iTargetBitrate=256000",
   "F*6 S:ext=iTargetBitrate:128000+L0.iSpatialBitrate:128000 G:ext F*6 S:ext=iMaxQp:40+iMinQp:20 F*6"},
  {"set-ext-reset", "pan", API_BASE,
   "F*3 S:ext=iTemporalLayerNum:3 F*5 S:ext=L0.uiSliceMode:1+L0.uiSliceNum:2 F*3 S:ext=iLoopFilterDisableIdc:1 F*2 G:ext"},
  {"set-ext-errors", "pan", API_BASE, "F S:ext=iSpatialLayerNum:0 S:ext=iSpatialLayerNum:5 S:ext=uiIntraPeriod:3+iTemporalLayerNum:3 F"},

  // Statistics, with the log interval short enough to update the frame and bit rates.
  {"statistics", "mixed", API_RC "iRCMode=1;iTargetBitrate=200000;fMaxFrameRate=5;ts.num=200;ts.den=1",
   "S:statint=500 G:stats F*12 G:stats F*12 G:stats I F*6 G:stats"},
  {"statistics-skips", "mixed", API_RC "iRCMode=1;iTargetBitrate=32000;fMaxFrameRate=5;ts.num=200;ts.den=1",
   "S:statint=300 F*25 G:stats"},

  // The life cycle: calls after Uninitialize, Initialize again, and the bad inputs.
  {"lifecycle", "pan", API_BASE, "F U F I i G:idr S:idr=3 S:fps=10 P R F*2 R F G:stats U U R F"},
  {"bad-input", "pan", API_BASE, "F:fmt=23 F:8x8 F:16x8 F F:fmt=22 F"},
  {"memory-error", "mixed", API_RC "iRCMode=2;iTargetBitrate=128000;iMinQp=24;iMaxQp=40",
   "F*12 I G:idr S:idr=3 R F*2 G:stats"},

  // Slice threads (P5.7). With more than one thread each slice is coded and deblocked in
  // a task of its own (bDeblockingParallelFlag), and an iLoopFilterDisableIdc of 0 becomes
  // 2. The thread count is the smaller of the one asked for and the slice count, and at
  // most MAX_THREADS_NUM (4). Load balancing resizes fixed slices from their coding times
  // when there are at least as many threads as slices, which no table can reproduce, so
  // those rows turn it off; with fewer threads than slices, or raster slices, it changes
  // nothing and stays on.
#define API_MT API_BASE "iMultipleThreadIdc=4;bUseLoadBalancing=0;L0.uiSliceMode=1;L0.uiSliceNum=4;"
  {"threads-fixed-4", "pan", API_MT, "F*6 I F*3"},
  {"threads-fixed-4-2-threads", "pan", API_BASE "iMultipleThreadIdc=2;bUseLoadBalancing=0;L0.uiSliceMode=1;L0.uiSliceNum=4",
   "F*6 I F*3"},
  {"threads-fixed-4-3-threads", "pan", API_BASE "iMultipleThreadIdc=3;bUseLoadBalancing=0;L0.uiSliceMode=1;L0.uiSliceNum=4",
   "F*6 I F*3"},
  {"threads-fixed-4-2-threads-balancing", "pan", API_BASE "iMultipleThreadIdc=2;L0.uiSliceMode=1;L0.uiSliceNum=4",
   "F*6 I F*3"},
  {"threads-fixed-3-noise", "noise", API_BASE "iMultipleThreadIdc=4;bUseLoadBalancing=0;L0.uiSliceMode=1;L0.uiSliceNum=3",
   "F*4"},
  {"threads-fixed-7", "subpel", API_BASE "iMultipleThreadIdc=4;L0.uiSliceMode=1;L0.uiSliceNum=7;iComplexityMode=2", "F*4"},
  {"threads-raster-rows", "subpel", API_BASE "iMultipleThreadIdc=2;L0.uiSliceMode=2", "F*5"},
  {"threads-raster-mbs", "block", API_BASE "iMultipleThreadIdc=4;L0.uiSliceMode=2;L0.uiSliceMbNum=10,30,59", "F*5"},
  {"threads-single-slice", "pan", API_BASE "iMultipleThreadIdc=4", "F*4"},
  {"threads-loop-filter-off", "pan", API_MT "iLoopFilterDisableIdc=1", "F*4"},
  {"threads-loop-filter-2", "pan", API_MT "iLoopFilterDisableIdc=2", "F*4"},
  // One thread, the same slices and loop filter: the same stream as the two above.
  {"threads-one-thread-loop-filter-2", "pan", API_BASE "L0.uiSliceMode=1;L0.uiSliceNum=4;iLoopFilterDisableIdc=2",
   "F*4"},
  {"threads-loop-filter-offsets", "pan", API_MT "iLoopFilterAlphaC0Offset=4;iLoopFilterBetaOffset=-3", "F*4"},
  {"threads-prefix", "pan", API_MT "bPrefixNalAddingCtrl=1", "F*3 I F*2"},
  {"threads-temporal-3", "pan", API_MT "iTemporalLayerNum=3", "F*9"},
  {"threads-ltr", "pan", API_MT "bEnableLongTermReference=1;iLtrMarkPeriod=3", "F*4 S:ltrfb=4:1:3:0 F*4"},
  {"threads-rate-control", "mixed", API_RC "iRCMode=1;iTargetBitrate=256000;iMultipleThreadIdc=4;bUseLoadBalancing=0;"
   "L0.uiSliceMode=1;L0.uiSliceNum=4", "F*12 I F*6"},
  {"threads-preprocess", "camera", API_PP "iRCMode=1;iTargetBitrate=200000;iMultipleThreadIdc=4;bUseLoadBalancing=0;"
   "L0.uiSliceMode=1;L0.uiSliceNum=4", "F*10"},
  {"threads-cropped", "subpel", API_MT "iPicWidth=200;iPicHeight=120", "F*4"},
  {"threads-640x480", "camera", API_PP "iRCMode=1;iTargetBitrate=1000000;iMultipleThreadIdc=4;bUseLoadBalancing=0;"
   "L0.uiSliceMode=1;L0.uiSliceNum=4;iPicWidth=640;iPicHeight=480", "F*6"},
  {"threads-lifecycle", "pan", API_MT,
   "F*2 U F R F*2 S:ext=iPicWidth:160+iPicHeight:96+L0.iVideoWidth:160+L0.iVideoHeight:96 F*2 G:ext"},
  {"threads-set-on", "pan", API_BASE,
   "F*2 S:ext=iMultipleThreadIdc:4+bUseLoadBalancing:0+L0.uiSliceMode:1+L0.uiSliceNum:4 F*3 G:ext"},
  {"threads-set-off", "pan", API_MT, "F*2 S:ext=iMultipleThreadIdc:1 F*3 G:ext"},
#undef API_MT
};
#undef API_PP
#undef API_RC
#undef API_BASE

// Parses "a:b:c" into up to n integers.
int ApiInts (const std::string& v, long long* out, int n) {
  std::stringstream ss (v);
  std::string part;
  int i = 0;
  while (i < n && std::getline (ss, part, ':')) out[i++] = strtoll (part.c_str(), NULL, 10);
  return i;
}

std::string ApiFloat (float x) {
  char b[64];
  snprintf (b, sizeof b, "%.9g", x);
  return b;
}

// Applies "k:v+k:v" to the parameters, as ApplySetting.
bool ApiApply (SEncParamExt& e, SEncParamBase& b, bool base, const std::string& v) {
  std::stringstream ss (v);
  std::string item;
  while (std::getline (ss, item, '+')) {
    size_t colon = item.find (':');
    if (!ApplySetting (e, b, base, item.substr (0, colon), item.substr (colon + 1))) return false;
  }
  return true;
}

// SetOption: key=value. Returns SetOption's code; *pOk is false for an unknown key.
int ApiSet (ISVCEncoder* encoder, const std::string& key, const std::string& v, bool* pOk) {
  *pOk = true;
  long long n[5] = {0, 0, 0, 0, 0};
  ApiInts (v, n, 5);
  const int i = (int)n[0];
  if (key == "idr") return encoder->SetOption (ENCODER_OPTION_IDR_INTERVAL, (void*)&i);
  if (key == "rc") return encoder->SetOption (ENCODER_OPTION_RC_MODE, (void*)&i);
  if (key == "pad") return encoder->SetOption (ENCODER_PADDING_PADDING, (void*)&i);
  if (key == "strategy") return encoder->SetOption (ENCODER_OPTION_SPS_PPS_ID_STRATEGY, (void*)&i);
  if (key == "nref") return encoder->SetOption (ENCODER_OPTION_NUMBER_REF, (void*)&i);
  if (key == "complexity") return encoder->SetOption (ENCODER_OPTION_COMPLEXITY, (void*)&i);
  if (key == "statint") return encoder->SetOption (ENCODER_OPTION_STATISTICS_LOG_INTERVAL, (void*)&i);
  if (key == "vary") return encoder->SetOption (ENCODER_OPTION_BITS_VARY_PERCENTAGE, (void*)&i);
  if (key == "fmt") return encoder->SetOption (ENCODER_OPTION_DATAFORMAT, (void*)&i);
  if (key == "interpred") return encoder->SetOption (ENCODER_OPTION_INTER_SPATIAL_PRED, (void*)&i);
  if (key == "unknown") return encoder->SetOption ((ENCODER_OPTION)99, (void*)&i);
  if (key == "ltrperiod") {
    unsigned int u = (unsigned int)n[0];
    return encoder->SetOption (ENCODER_LTR_MARKING_PERIOD, &u);
  }
  if (key == "fps") {
    float f = (float)strtod (v.c_str(), NULL);
    return encoder->SetOption (ENCODER_OPTION_FRAME_RATE, &f);
  }
  if (key == "skip" || key == "ssei" || key == "prefix" || key == "lossless") {
    bool b = i != 0;
    return encoder->SetOption (key == "skip" ? ENCODER_OPTION_RC_FRAME_SKIP : key == "ssei" ? ENCODER_OPTION_ENABLE_SSEI :
                               key == "prefix" ? ENCODER_OPTION_ENABLE_PREFIX_NAL_ADDING : ENCODER_OPTION_IS_LOSSLESS_LINK, &b);
  }
  if (key == "br" || key == "maxbr") {
    SBitrateInfo info;
    info.iLayer = (LAYER_NUM)n[0];
    info.iBitrate = (int)n[1];
    return encoder->SetOption (key == "br" ? ENCODER_OPTION_BITRATE : ENCODER_OPTION_MAX_BITRATE, &info);
  }
  if (key == "ltr") {
    SLTRConfig c;
    c.bEnableLongTermReference = n[0] != 0;
    c.iLTRRefNum = (int)n[1];
    return encoder->SetOption (ENCODER_OPTION_LTR, &c);
  }
  if (key == "ltrreq") {
    SLTRRecoverRequest r;
    r.uiFeedbackType = (unsigned int)n[0];
    r.uiIDRPicId = (unsigned int)n[1];
    r.iLastCorrectFrameNum = (int)n[2];
    r.iCurrentFrameNum = (int)n[3];
    r.iLayerId = (int)n[4];
    return encoder->SetOption (ENCODER_LTR_RECOVERY_REQUEST, &r);
  }
  if (key == "ltrfb") {
    SLTRMarkingFeedback f;
    f.uiFeedbackType = (unsigned int)n[0];
    f.uiIDRPicId = (unsigned int)n[1];
    f.iLTRFrameNum = (int)n[2];
    f.iLayerId = (int)n[3];
    return encoder->SetOption (ENCODER_LTR_MARKING_FEEDBACK, &f);
  }
  if (key == "profile") {
    SProfileInfo p;
    p.iLayer = (int)n[0];
    p.uiProfileIdc = (EProfileIdc)n[1];
    return encoder->SetOption (ENCODER_OPTION_PROFILE, &p);
  }
  if (key == "level") {
    SLevelInfo l;
    l.iLayer = (int)n[0];
    l.uiLevelIdc = (ELevelIdc)n[1];
    return encoder->SetOption (ENCODER_OPTION_LEVEL, &l);
  }
  if (key == "delivery") {
    SDeliveryStatus d;
    d.bDeliveryFlag = n[0] != 0;
    d.iDropFrameType = (int)n[1];
    d.iDropFrameSize = (int)n[2];
    return encoder->SetOption (ENCODER_OPTION_DELIVERY_STATUS, &d);
  }
  if (key == "getstats") {
    SEncoderStatistics s;
    memset (&s, 0, sizeof s);
    return encoder->SetOption (ENCODER_OPTION_GET_STATISTICS, &s);
  }
  if (key == "base") {
    // The current parameters (zeroed when GetOption fails), with the changes.
    SEncParamBase b;
    SEncParamExt e;
    memset (&b, 0, sizeof b);
    encoder->GetOption (ENCODER_OPTION_SVC_ENCODE_PARAM_BASE, &b);
    if (!ApiApply (e, b, true, v)) return *pOk = false, 0;
    return encoder->SetOption (ENCODER_OPTION_SVC_ENCODE_PARAM_BASE, &b);
  }
  if (key == "ext") {
    SEncParamBase b;
    SEncParamExt e;
    memset (&e, 0, sizeof e);
    encoder->GetOption (ENCODER_OPTION_SVC_ENCODE_PARAM_EXT, &e);
    if (!ApiApply (e, b, false, v)) return *pOk = false, 0;
    return encoder->SetOption (ENCODER_OPTION_SVC_ENCODE_PARAM_EXT, &e);
  }
  *pOk = false;
  return 0;
}

// GetOption: key. Returns GetOption's code; the value goes to *pValue ("-" on failure).
int ApiGet (ISVCEncoder* encoder, const std::string& key, std::string* pValue, bool* pOk) {
  *pOk = true;
  *pValue = "-";
  int rv;
  const size_t eq = key.find ('=');
  const std::string k = key.substr (0, eq);
  if (k == "idr" || k == "statint" || k == "complexity" || k == "fmt" || k == "interpred" || k == "unknown" || k == "ltr") {
    int i = 0x5a5a5a5a;
    rv = encoder->GetOption (k == "idr" ? ENCODER_OPTION_IDR_INTERVAL : k == "statint" ? ENCODER_OPTION_STATISTICS_LOG_INTERVAL :
                             k == "complexity" ? ENCODER_OPTION_COMPLEXITY : k == "fmt" ? ENCODER_OPTION_DATAFORMAT :
                             k == "interpred" ? ENCODER_OPTION_INTER_SPATIAL_PRED : k == "ltr" ? ENCODER_OPTION_LTR :
                             (ENCODER_OPTION)99, &i);
    if (rv == 0) *pValue = std::to_string (i);
    return rv;
  }
  if (k == "fps") {
    float f = -1;
    rv = encoder->GetOption (ENCODER_OPTION_FRAME_RATE, &f);
    if (rv == 0) *pValue = ApiFloat (f);
    return rv;
  }
  if (k == "br" || k == "maxbr") {
    SBitrateInfo info;
    info.iLayer = (LAYER_NUM)strtol (key.substr (eq + 1).c_str(), NULL, 10);
    info.iBitrate = -1;
    rv = encoder->GetOption (k == "br" ? ENCODER_OPTION_BITRATE : ENCODER_OPTION_MAX_BITRATE, &info);
    if (rv == 0) *pValue = std::to_string (info.iBitrate);
    return rv;
  }
  if (k == "ext") {
    SEncParamExt e;
    memset (&e, 0, sizeof e);
    rv = encoder->GetOption (ENCODER_OPTION_SVC_ENCODE_PARAM_EXT, &e);
    if (rv == 0) *pValue = DumpParamExt (e);
    return rv;
  }
  if (k == "base") {
    SEncParamBase b;
    memset (&b, 0, sizeof b);
    rv = encoder->GetOption (ENCODER_OPTION_SVC_ENCODE_PARAM_BASE, &b);
    if (rv == 0)
      *pValue = "iUsageType=" + std::to_string (b.iUsageType) + ";iPicWidth=" + std::to_string (b.iPicWidth) +
                ";iPicHeight=" + std::to_string (b.iPicHeight) + ";iTargetBitrate=" + std::to_string (b.iTargetBitrate) +
                ";iRCMode=" + std::to_string (b.iRCMode) + ";fMaxFrameRate=" + ApiFloat (b.fMaxFrameRate);
    return rv;
  }
  if (k == "stats") {
    // fAverageFrameSpeedInMs is the encoder's own timing, so it is left out.
    SEncoderStatistics s;
    memset (&s, 0, sizeof s);
    rv = encoder->GetOption (ENCODER_OPTION_GET_STATISTICS, &s);
    if (rv == 0)
      *pValue = "uiWidth=" + std::to_string (s.uiWidth) + ";uiHeight=" + std::to_string (s.uiHeight) +
                ";fAverageFrameRate=" + ApiFloat (s.fAverageFrameRate) + ";fLatestFrameRate=" + ApiFloat (s.fLatestFrameRate) +
                ";uiBitRate=" + std::to_string (s.uiBitRate) + ";uiAverageFrameQP=" + std::to_string (s.uiAverageFrameQP) +
                ";uiInputFrameCount=" + std::to_string (s.uiInputFrameCount) +
                ";uiSkippedFrameCount=" + std::to_string (s.uiSkippedFrameCount) +
                ";uiResolutionChangeTimes=" + std::to_string (s.uiResolutionChangeTimes) +
                ";uiIDRReqNum=" + std::to_string (s.uiIDRReqNum) + ";uiIDRSentNum=" + std::to_string (s.uiIDRSentNum) +
                ";uiLTRSentNum=" + std::to_string (s.uiLTRSentNum);
    return rv;
  }
  *pOk = false;
  return 0;
}

// The script, with op*n expanded.
std::vector<std::string> ApiOps (const char* script) {
  std::vector<std::string> ops;
  std::stringstream ss (script);
  std::string op;
  while (ss >> op) {
    const size_t star = op.rfind ('*');
    int n = 1;
    if (star != std::string::npos) {
      n = (int)strtol (op.substr (star + 1).c_str(), NULL, 10);
      op = op.substr (0, star);
    }
    for (int i = 0; i < n; i++) ops.push_back (op);
  }
  return ops;
}

// Runs one configuration's script. Returns the first Initialize's code; an op row per
// operation goes to *pRows (when not NULL) and the outputs of F and P to *pStream.
int RunApi (const ApiConfig& c, std::vector<std::string>* pRows, std::vector<unsigned char>* pStream) {
  ISVCEncoder* encoder = NULL;
  if (WelsCreateSVCEncoder (&encoder) != 0) return -1;
  int iTraceLevel = WELS_LOG_QUIET;
  encoder->SetOption (ENCODER_OPTION_TRACE_LEVEL, &iTraceLevel);

  SEncParamExt ext;
  SEncParamBase base;
  memset (&base, 0, sizeof base);
  encoder->GetDefaultParams (&ext);
  TimestampSchedule ts;
  bool isBase = false;
  std::stringstream ss (c.settings);
  std::string item;
  while (std::getline (ss, item, ';')) {
    if (item.empty()) continue;
    size_t eq = item.find ('=');
    std::string k = item.substr (0, eq), v = item.substr (eq + 1);
    if (k == "api") {
      isBase = v == "base";
      continue;
    }
    if (k.compare (0, 3, "ts.") == 0 ? !ts.Apply (k, v) : !ApplySetting (ext, base, isBase, k, v)) {
      fprintf (stderr, "%s: unknown setting %s\n", c.name, k.c_str());
      exit (1);
    }
  }
  const SEncParamExt ext0 = ext;
  const SEncParamBase base0 = base;

  int initRv = isBase ? encoder->Initialize (&base) : encoder->InitializeExt (&ext);
  int w = isBase ? base.iPicWidth : ext.iPicWidth, h = isBase ? base.iPicHeight : ext.iPicHeight;
  int frame = 0;
  std::vector<unsigned char> buf;
  const std::vector<std::string> ops = ApiOps (c.script);
  for (size_t i = 0; i < ops.size(); i++) {
    const std::string& op = ops[i];
    int rv = 0;
    std::string detail = "-";
    bool frameOut = false;
    SFrameBSInfo info;
    memset (&info, 0, sizeof info);
    if (op[0] == 'F') {
      int pw = w, ph = h, fmt = videoFormatI420;
      if (op.compare (0, 6, "F:fmt=") == 0) fmt = (int)strtol (op.c_str() + 6, NULL, 10);
      else if (op.size() > 2) sscanf (op.c_str() + 2, "%dx%d", &pw, &ph);
      FillFrame (buf, pw, ph, c.content, frame);
      SSourcePicture pic;
      memset (&pic, 0, sizeof pic);
      pic.iPicWidth = pw;
      pic.iPicHeight = ph;
      pic.iColorFormat = fmt;
      pic.iStride[0] = pw;
      pic.iStride[1] = pic.iStride[2] = pw >> 1;
      pic.pData[0] = buf.data();
      pic.pData[1] = pic.pData[0] + pw * ph;
      pic.pData[2] = pic.pData[1] + (pw >> 1) * (ph >> 1);
      pic.uiTimeStamp = ts.At (frame);
      rv = encoder->EncodeFrame (&pic, &info);
      frame++;
      frameOut = true;
    } else if (op == "P") {
      rv = encoder->EncodeParameterSets (&info);
      frameOut = true;
    } else if (op == "I" || op == "i") {
      rv = encoder->ForceIntraFrame (op == "I");
    } else if (op == "U") {
      rv = encoder->Uninitialize();
    } else if (op == "R") {
      SEncParamExt e = ext0;
      SEncParamBase b = base0;
      rv = isBase ? encoder->Initialize (&b) : encoder->InitializeExt (&e);
      w = isBase ? b.iPicWidth : e.iPicWidth;
      h = isBase ? b.iPicHeight : e.iPicHeight;
    } else if (op.compare (0, 2, "S:") == 0) {
      const size_t eq = op.find ('=');
      const std::string k = op.substr (2, eq - 2), v = op.substr (eq + 1);
      bool ok;
      rv = ApiSet (encoder, k, v, &ok);
      if (!ok) {
        fprintf (stderr, "%s: unknown op %s\n", c.name, op.c_str());
        exit (1);
      }
      if (rv == 0 && (k == "base" || k == "ext")) {
        // The frames follow the new size.
        SEncParamExt e;
        memset (&e, 0, sizeof e);
        encoder->GetOption (ENCODER_OPTION_SVC_ENCODE_PARAM_EXT, &e);
        w = e.iPicWidth;
        h = e.iPicHeight;
      }
    } else if (op.compare (0, 2, "G:") == 0) {
      bool ok;
      rv = ApiGet (encoder, op.substr (2), &detail, &ok);
      if (!ok) {
        fprintf (stderr, "%s: unknown op %s\n", c.name, op.c_str());
        exit (1);
      }
    } else {
      fprintf (stderr, "%s: unknown op %s\n", c.name, op.c_str());
      exit (1);
    }
    if (frameOut) {
      std::vector<unsigned char> bytes;
      std::string tid;
      for (int j = 0; j < info.iLayerNum; j++) {
        const SLayerBSInfo& l = info.sLayerInfo[j];
        if (l.uiLayerType == VIDEO_CODING_LAYER) tid += (tid.empty() ? "" : ",") + std::to_string (l.uiTemporalId);
        int n = 0;
        for (int k = 0; k < l.iNalCount; k++) n += l.pNalLengthInByte[k];
        bytes.insert (bytes.end(), l.pBsBuf, l.pBsBuf + n);
      }
      pStream->insert (pStream->end(), bytes.begin(), bytes.end());
      detail = std::string (FrameTypeName (info.eFrameType)) + "\t" + (tid.empty() ? "-" : tid) + "\t" + Layers (info) + "\t" +
               (bytes.empty() ? "-" : Sha1Hex (bytes.data(), (int)bytes.size())) + "\t" + std::to_string (bytes.size());
    }
    if (pRows)
      pRows->push_back (std::string ("op\t") + c.name + "\t" + std::to_string (i) + "\t" + op + "\t" + std::to_string (rv) +
                        "\t" + detail);
  }
  WelsDestroySVCEncoder (encoder);
  return initRv;
}

}  // namespace

int Api() {
  printf ("# %s, C-only reference build. Scripted encoder API calls per configuration, see refharness/src/encoder.cpp.\n",
          g_strCodecVer);
  printf ("# config\t<name>\t<content>\t<settings>\t<script>\n");
  printf ("# init\t<name>\t<Initialize or InitializeExt>\n");
  printf ("# op\t<name>\t<index>\t<op>\t<return code>\t<frame type, temporal ids, layers, sha1, size | value | ->\n");
  printf ("# stream\t<name>\t<sha1 of the F and P outputs>\t<size>\n");
  for (const ApiConfig& c : kApiConfigs) {
    printf ("config\t%s\t%s\t%s\t%s\n", c.name, c.content, c.settings, c.script);
    std::vector<unsigned char> stream;
    std::vector<std::string> rows;
    printf ("init\t%s\t%d\n", c.name, RunApi (c, &rows, &stream));
    for (const std::string& r : rows) printf ("%s\n", r.c_str());
    printf ("stream\t%s\t%s\t%d\n", c.name, stream.empty() ? "-" : Sha1Hex (stream.data(), (int)stream.size()).c_str(),
            (int)stream.size());
  }
  return 0;
}

int ApiDump (const char* name, const char* outPath) {
  for (const ApiConfig& c : kApiConfigs) {
    if (std::string (c.name) != name) continue;
    std::vector<unsigned char> stream;
    RunApi (c, NULL, &stream);
    std::ofstream out (outPath, std::ios::binary);
    out.write ((const char*)stream.data(), (std::streamsize)stream.size());
    return out ? 0 : 1;
  }
  fprintf (stderr, "no api configuration %s\n", name);
  return 1;
}
