// h264ref: the C reference harness for the SIPSorcery.H264 port.
//
//   h264ref conformance <res-dir>   Decode every vector in upstream's
//                                   test/api/decoder_test.cpp table, compare
//                                   SHA1s, and classify each vector's coding
//                                   tools (Constrained Baseline or not).
//   h264ref hash <file.264>         SHA1 of the decoded output, as upstream hashes it.
//   h264ref info <file.264>         Coding tools the stream uses.
//   h264ref yuv <file.264> <out>    Write the decoded frames (cropped I420) to <out>.
//   h264ref headers / headers-damaged <res-dir>, headerdump[-damaged] <file> [variant]
//                                   D1 parser header dumps (headers.cpp).
//   h264ref mbs / mbs-damaged <res-dir>, mbdump[-damaged] <file> [variant]
//                                   D2 macroblock parse dumps (headers.cpp).
//   h264ref decode-damaged <res-dir>, hash-damaged <file> <variant>,
//   yuv-damaged <file> <variant> <out>, trace-damaged <file> <variant>
//                                   D6: the full decoder API over the damaged
//                                   copies (error concealment, ResetDecoder).
//   h264ref kernels                 Fixture hashes for the ported kernels and
//                                   tables (reference/kernels.tsv), see fixture.h.
//   h264ref encoder-conformance <res-dir>
//                                   Encode upstream's test/api/encoder_test.cpp
//                                   table and compare SHA1s (encoder.cpp).
//   h264ref paramsets               E1: the SPS/PPS the encoder writes for a table
//                                   of configurations (encoder.cpp).
//   h264ref idr                     E4b: the first frame (IDR) the encoder writes
//                                   for a table of fixed QP configurations.
//   h264ref idr-dump <name> <out>   That frame for one configuration, to a file.
//   h264ref stream                  E5b: P streams, frame by frame, for a table of
//                                   fixed QP configurations.
//   h264ref stream-dump <name> <out>
//                                   That stream for one configuration, to a file.
//   h264ref rc                      E6: rate controlled streams, frame by frame, for a
//                                   table of configurations (rate control modes, frame
//                                   skip, padding, timestamps).
//   h264ref rc-dump <name> <out>    That stream for one configuration, to a file.
//   h264ref preprocess              E7: camera streams with the preprocessing on (scene
//                                   change and background detection, adaptive
//                                   quantisation, denoising, downsampling).
//   h264ref preprocess-dump <name> <out>
//                                   That stream for one configuration, to a file.
//
// The decode loop and hashing copy upstream's test/api/BaseDecoderTest.cpp and
// decoder_test.cpp exactly (input split on 4-byte start codes, DecodeFrame2,
// end-of-stream flush), so the hashes are comparable with upstream's table.

#include <climits>
#ifdef _WIN32
#include <fcntl.h>
#include <io.h>
#endif
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <fstream>
#include <functional>
#include <string>
#include <vector>

#include "codec_api.h"
#include "codec_app_def.h"
#include "codec_def.h"
#include "codec_ver.h"
#include "fixture.h"
#include "headers.h"
#include "sha1.h"
#include "stream_info.h"

// encoder.cpp
int EncoderConformance (const char* resDir);
int ParamSets();
int Idr();
int IdrDump (const char* name, const char* outPath);
int Stream();
int StreamDump (const char* name, const char* outPath);
int Rc();
int RcDump (const char* name, const char* outPath);
int Preprocess();
int PreprocessDump (const char* name, const char* outPath);

namespace {

struct Vector {
  const char* file;
  const char* sha1;
};

// Copied from openh264 test/api/decoder_test.cpp (kFileParamArray) at the pinned commit.
const Vector kDecoderVectors[] = {
  {"Adobe_PDF_sample_a_1024x768_50Frms.264", "9aa9a4d9598eb3e1093311826844f37c43e4c521"},
  {"BA1_FT_C.264", "418d152fb85709b6f172799dcb239038df437cfa"},
  {"BA1_Sony_D.jsv", "d94b5ceed5686a03ea682b53d415dee999d27eb6"},
  {"BAMQ1_JVC_C.264", "613cf662c23e5d9e1d7da7fe880a3c427411d171"},
  {"BAMQ2_JVC_C.264", "11bcf3713f520e606a8326d37e00e5fd6c9fd4a0"},
  {"BA_MW_D.264", "afd7a9765961ca241bb4bdf344b31397bec7465a"},
  {"BANM_MW_D.264", "92d924a857a1a7d7d9b224eaa3887830f15dee7f"},
  {"BASQP1_Sony_C.jsv", "3986c8c9d2876d2f0748b925101b152c6ec8b811"},
  {"CI1_FT_B.264", "cbfec15e17a504678b19a1191992131c92a1ac26"},
  {"CI_MW_D.264", "289f29a103c8d95adf2909c646466904be8b06d7"},
  {"CVFC1_Sony_C.jsv", "4641abd7419a5580b97f16e83fd1d566339229d0"},
  {"CVPCMNL1_SVA_C.264", "c2b0d964de727c64b9fccb58f63b567c82bda95a"},
  {"LS_SVA_D.264", "72118f4d1674cf14e58bed7e67cb3aeed3df62b9"},
  {"MIDR_MW_D.264", "9467030f4786f75644bf06a7fc809c36d1959827"},
  {"MPS_MW_A.264", "67f1cfbef0e8025ed60dedccf8d9558d0636be5f"},
  {"MR1_BT_A.h264", "6e585f8359667a16b03e5f49a06f5ceae8d991e0"},
  {"MR1_MW_A.264", "d9e2bf34e9314dcc171ddaea2c5015d0421479f2"},
  {"MR2_MW_A.264", "628b1d4eff04c2d277f7144e23484957dad63cbe"},
  {"MR2_TANDBERG_E.264", "74d618bc7d9d41998edf4c85d51aa06111db6609"},
  {"NL1_Sony_D.jsv", "e401e30669938443c2f02522fd4d5aa1382931a0"},
  {"NLMQ1_JVC_C.264", "f3265c6ddf8db1b2bf604d8a2954f75532e28cda"},
  {"NLMQ2_JVC_C.264", "350ae86ef9ba09390d63a09b7f9ff54184109ca8"},
  {"NRF_MW_E.264", "20732198c04cd2591350a361e4510892f6eed3f0"},
  {"QCIF_2P_I_allIPCM.264", "8724c0866ebdba7ebb7209a0c0c3ae3ae38a0240"},
  {"SVA_BA1_B.264", "c4543b24823b16c424c673616c36c7f537089b2d"},
  {"SVA_BA2_D.264", "98ff2d67860462d8d8bcc9352097c06cc401d97e"},
  {"SVA_Base_B.264", "91f514d81cd33de9f6fbf5dbefdb189cc2e7ecf4"},
  {"SVA_CL1_E.264", "4fe09ab6cdc965ea10a20f1d6dd38aca954412bb"},
  {"SVA_FM1_E.264", "fad08c4ff7cf2307b6579853d0f4652fc26645d3"},
  {"SVA_NL1_B.264", "6d63f72a0c0d833b1db0ba438afff3b4180fb3e6"},
  {"SVA_NL2_E.264", "70453ef8097c94dd190d6d2d1d5cb83c67e66238"},
  {"SarVui.264", "98ff2d67860462d8d8bcc9352097c06cc401d97e"},
  {"Static.264", "91dd4a7a796805b2cd015cae8fd630d96c663f42"},
  {"Zhling_1280x720.264", "ad99f5eaa2d73ae3840e7da67313de8cfc866ce6"},
  {"sps_subsetsps_bothVUI.264", "d3a47032eb5dcc1963343a68e9bea12435bf1e4c"},
  {"test_cif_I_CABAC_PCM.264", "95fdf21470d3bbcf95505abb2164042063a79d98"},
  {"test_cif_I_CABAC_slice.264", "19121bc67f2b13fb8f030504fc0827e1ac6d0fdb"},
  {"test_cif_P_CABAC_slice.264", "521bbd0ba2422369b724c7054545cf107a56f959"},
  {"test_qcif_cabac.264", "587d1d05943f3cd416bf69469975fdee05361e69"},
  {"test_scalinglist_jm.264", "992a25b4ec98db4a16d61c097e614eb16afe3478"},
  {"test_vd_1d.264", "5827d2338b79ff82cd091c707823e466197281d3"},
  {"test_vd_rc.264", "eea02e97bfec89d0418593a8abaaf55d02eaa1ca"},
  {"Cisco_Men_whisper_640x320_CABAC_Bframe_9.264", "2b349c1bc806b6e0412008747b2463d77b576476"},
  {"Cisco_Men_whisper_640x320_CAVLC_Bframe_9.264", "e5b76ff7e2f44e9b33906f8a4039d0d2bdb1580b"},
  {"Cisco_Adobe_PDF_sample_a_1024x768_CAVLC_Bframe_9.264", "9d758d9e6f4dead0d7b361f3ddf2ee009d0ea190"},
  {"VID_1280x544_cabac_temporal_direct.264", "02299df3b9d83300d244b36601699859c57fe905"},
  {"VID_1280x720_cabac_temporal_direct.264", "0ef0818cb23445d209b8a7632c13f1c7e820cc27"},
  {"VID_1920x1080_cabac_temporal_direct.264", "ad2b1d1456919693e38a1e3e8cd9c21699688cec"},
  {"VID_1280x544_cavlc_temporal_direct.264", "71a12ff2b548b765a34c11f39eef1faa19b38d59"},
  {"VID_1280x720_cavlc_temporal_direct.264", "f39cecb32ba20ca4f3b3a385db9ef46ba340e41f"},
  {"VID_1920x1080_cavlc_temporal_direct.264", "6aae2d569a1ebbe5ae20e2dfc5e709cc05ab1a21"},
};

bool ReadFile (const std::string& path, std::vector<uint8_t>& out) {
  std::ifstream f (path, std::ios::binary);
  if (!f) return false;
  out.assign (std::istreambuf_iterator<char> (f), std::istreambuf_iterator<char>());
  return true;
}

struct Plane {
  const uint8_t* data;
  int width, height, stride;
};

using FrameCallback = std::function<void (const Plane& y, const Plane& u, const Plane& v)>;

struct DecodeResult {
  int frames = 0;
  int chunks = 0;
  int errorChunks = 0;  // DecodeFrame2 returned something other than dsErrorFree
  int firstErrorState = 0;
  int errorStates = 0;  // OR of every failing state, so a dsOutOfMemory (ResetDecoder) shows up
  bool ok = true;
};

void EmitFrame (const SBufferInfo& bi, uint8_t* data[3], const FrameCallback& cb, DecodeResult& r) {
  if (bi.iBufferStatus != 1) return;
  const SSysMEMBuffer& sb = bi.UsrData.sSystemBuffer;
  Plane y {data[0], sb.iWidth, sb.iHeight, sb.iStride[0]};
  Plane u {data[1], sb.iWidth / 2, sb.iHeight / 2, sb.iStride[1]};
  Plane v {data[2], sb.iWidth / 2, sb.iHeight / 2, sb.iStride[1]};
  ++r.frames;
  cb (y, u, v);
}

// Mirrors BaseDecoderTest::SetUp / DecodeFile / DecodeFrame / FlushFrame. The
// input is already split as BaseDecoderTest's ReadFrame splits it (SplitChunks
// in headers.cpp: 4-byte start codes 00 00 00 01, each chunk running from its
// start code to just before the next one).
// Called after every DecodeFrame2 / FlushFrame (call index, input length, state,
// buffer info, output planes), for the trace-damaged mode.
using CallTrace = std::function<void (int call, int len, DECODING_STATE st, const SBufferInfo& bi, uint8_t* data[3])>;

DecodeResult DecodeChunks (const Chunks& chunks, const FrameCallback& cb, const CallTrace* trace = nullptr) {
  int call = 0;
  DecodeResult r;
  ISVCDecoder* dec = nullptr;
  if (WelsCreateDecoder (&dec) != 0 || dec == nullptr) {
    r.ok = false;
    return r;
  }
  int32_t quiet = WELS_LOG_QUIET;
  if (const char* lv = getenv ("H264REF_TRACE_LEVEL")) {
    // Debugging aid, e.g. 8 for WELS_LOG_DEBUG: the decoder's log lines go to
    // stdout, between trace-damaged's per-call lines.
    quiet = atoi (lv);
    WelsTraceCallback logToStdout = [] (void*, int, const char* m) { printf ("log\t%s\n", m); };
    dec->SetOption (DECODER_OPTION_TRACE_CALLBACK, &logToStdout);
  }
  dec->SetOption (DECODER_OPTION_TRACE_LEVEL, &quiet);

  SDecodingParam p;
  memset (&p, 0, sizeof p);
  p.uiTargetDqLayer = UCHAR_MAX;
  p.eEcActiveIdc = ERROR_CON_SLICE_COPY;
  p.sVideoProperty.eVideoBsType = VIDEO_BITSTREAM_DEFAULT;
  if (dec->Initialize (&p) != 0) {
    WelsDestroyDecoder (dec);
    r.ok = false;
    return r;
  }

  auto noteError = [&] (DECODING_STATE st) {
    if (st == dsErrorFree) return;
    if (r.errorChunks++ == 0) r.firstErrorState = st;
    r.errorStates |= st;
  };
  auto decode = [&] (const uint8_t* src, int len) {
    uint8_t* data[3] = {nullptr, nullptr, nullptr};
    SBufferInfo bi;
    memset (&bi, 0, sizeof bi);
    DECODING_STATE st = dec->DecodeFrame2 (src, len, data, &bi);
    noteError (st);
    if (trace) (*trace) (call, len, st, bi, data);
    ++call;
    EmitFrame (bi, data, cb, r);
  };

  for (const std::vector<uint8_t>& c : chunks) {
    ++r.chunks;
    decode (c.data(), (int)c.size());
  }

  int32_t eos = 1;
  dec->SetOption (DECODER_OPTION_END_OF_STREAM, &eos);
  decode (nullptr, 0);
  int32_t remaining = 0;
  dec->GetOption (DECODER_OPTION_NUM_OF_FRAMES_REMAINING_IN_BUFFER, &remaining);
  for (int32_t i = 0; i < remaining; ++i) {
    uint8_t* data[3] = {nullptr, nullptr, nullptr};
    SBufferInfo bi;
    memset (&bi, 0, sizeof bi);
    DECODING_STATE st = dec->FlushFrame (data, &bi);
    noteError (st);
    if (trace) (*trace) (call, -1, st, bi, data);
    ++call;
    EmitFrame (bi, data, cb, r);
  }

  dec->Uninitialize();
  WelsDestroyDecoder (dec);
  return r;
}

DecodeResult DecodeStream (const std::vector<uint8_t>& s, const FrameCallback& cb) {
  return DecodeChunks (SplitChunks (s), cb);
}

std::string HashChunks (const Chunks& chunks, DecodeResult* result) {
  SHA1Context ctx;
  SHA1Reset (&ctx);
  auto addPlane = [&] (const Plane& p) {
    const uint8_t* row = p.data;
    for (int i = 0; i < p.height; ++i, row += p.stride) SHA1Input (&ctx, row, p.width);
  };
  DecodeResult r = DecodeChunks (chunks, [&] (const Plane& y, const Plane& u, const Plane& v) {
    addPlane (y);
    addPlane (u);
    addPlane (v);
  });
  if (result) *result = r;
  unsigned char digest[SHA_DIGEST_LENGTH];
  SHA1Result (&ctx, digest);
  char hex[SHA_DIGEST_LENGTH * 2 + 1];
  for (int i = 0; i < SHA_DIGEST_LENGTH; ++i) snprintf (&hex[i * 2], 3, "%02x", digest[i]);
  return hex;
}

std::string HashStream (const std::vector<uint8_t>& s, DecodeResult* result) {
  return HashChunks (SplitChunks (s), result);
}

int Conformance (const std::string& resDir) {
  printf ("# %s, C-only reference build\n", g_strCodecVer);
  printf ("# result\tCB\tframes\terrors\tfile\tsha1\tfeatures\n");
  int pass = 0, fail = 0, missing = 0, cb = 0;
  for (const Vector& vec : kDecoderVectors) {
    std::vector<uint8_t> s;
    if (!ReadFile (resDir + "/" + vec.file, s)) {
      printf ("MISSING\t-\t-\t-\t%s\t-\t-\n", vec.file);
      ++missing;
      continue;
    }
    DecodeResult r;
    std::string h = HashStream (s, &r);
    StreamInfo info = AnalyseStream (s);
    bool ok = h == vec.sha1;
    ok ? ++pass : ++fail;
    if (info.IsConstrainedBaseline()) ++cb;
    printf ("%s\t%s\t%d\t%d\t%s\t%s\t%s\n", ok ? "PASS" : "FAIL", info.IsConstrainedBaseline() ? "yes" : "no",
            r.frames, r.errorChunks, vec.file, h.c_str(), info.Describe().c_str());
  }
  printf ("# %d passed, %d failed, %d missing; %d vectors use only Constrained Baseline tools\n", pass, fail,
          missing, cb);
  return (fail || missing) ? 1 : 0;
}

// LF line endings on Windows too, so the dumps hash the same as the C# side's.
void BinaryStdout() {
#ifdef _WIN32
  fflush (stdout);
  _setmode (_fileno (stdout), _O_BINARY);
#endif
}

// One row per decoder vector: the size and SHA-1 of its header dump (headers.cpp).
int Headers (const std::string& resDir) {
  BinaryStdout();
  printf ("# %s, C-only reference build. Parser header dumps, see refharness/src/headers.cpp.\n", g_strCodecVer);
  printf ("# file\tnals\taus\tbytes\tsha1\n");
  int missing = 0;
  for (const Vector& vec : kDecoderVectors) {
    std::vector<uint8_t> s;
    if (!ReadFile (resDir + "/" + vec.file, s)) {
      printf ("%s\t-\t-\t-\tMISSING\n", vec.file);
      ++missing;
      continue;
    }
    std::string d;
    int nals = 0, aus = 0;
    if (!HeaderDump (s, d, nals, aus)) {
      printf ("%s\t-\t-\t-\tINITFAIL\n", vec.file);
      ++missing;
      continue;
    }
    printf ("%s\t%d\t%d\t%zu\t%s\n", vec.file, nals, aus, d.size(), Sha1Hex (d).c_str());
  }
  return missing ? 1 : 0;
}

// The header dumps of damaged copies of each vector (DamageChunks in headers.cpp):
// the parsers' error paths, which no clean vector reaches.
const int kDamagedVariants = 3;

int HeadersDamaged (const std::string& resDir) {
  BinaryStdout();
  printf ("# %s, C-only reference build. Parser header dumps of damaged streams, see refharness/src/headers.cpp.\n",
          g_strCodecVer);
  printf ("# file\tvariant\tchunks\tnals\taus\tbytes\tsha1\n");
  int missing = 0;
  for (const Vector& vec : kDecoderVectors) {
    std::vector<uint8_t> s;
    if (!ReadFile (resDir + "/" + vec.file, s)) {
      printf ("%s\t-\t-\t-\t-\t-\tMISSING\n", vec.file);
      ++missing;
      continue;
    }
    Chunks clean = SplitChunks (s);
    for (int v = 0; v < kDamagedVariants; ++v) {
      Chunks c = DamageChunks (clean, (uint32_t)s.size(), v);
      std::string d;
      int nals = 0, aus = 0;
      if (!HeaderDumpChunks (c, d, nals, aus)) {
        printf ("%s\t%d\t-\t-\t-\t-\tINITFAIL\n", vec.file, v);
        ++missing;
        continue;
      }
      printf ("%s\t%d\t%zu\t%d\t%d\t%zu\t%s\n", vec.file, v, c.size(), nals, aus, d.size(), Sha1Hex (d).c_str());
    }
  }
  return missing ? 1 : 0;
}

// One row per decoder vector: the macroblock parse dump (MbDump in headers.cpp).
int Mbs (const std::string& resDir) {
  BinaryStdout();
  printf ("# %s, C-only reference build. Macroblock parse dumps, see refharness/src/headers.cpp.\n", g_strCodecVer);
  printf ("# file\tslices\tmbs\tbytes\tsha1\n");
  int missing = 0;
  for (const Vector& vec : kDecoderVectors) {
    std::vector<uint8_t> s;
    if (!ReadFile (resDir + "/" + vec.file, s)) {
      printf ("%s\t-\t-\t-\tMISSING\n", vec.file);
      ++missing;
      continue;
    }
    std::string d;
    int slices = 0, mbs = 0;
    if (!MbDump (s, d, slices, mbs)) {
      printf ("%s\t-\t-\t-\tINITFAIL\n", vec.file);
      ++missing;
      continue;
    }
    printf ("%s\t%d\t%d\t%zu\t%s\n", vec.file, slices, mbs, d.size(), Sha1Hex (d).c_str());
  }
  return missing ? 1 : 0;
}

// The macroblock parse dumps of the same damaged copies as headers-damaged.
int MbsDamaged (const std::string& resDir) {
  BinaryStdout();
  printf ("# %s, C-only reference build. Macroblock parse dumps of damaged streams, see refharness/src/headers.cpp.\n",
          g_strCodecVer);
  printf ("# file\tvariant\tchunks\tslices\tmbs\tbytes\tsha1\n");
  int missing = 0;
  for (const Vector& vec : kDecoderVectors) {
    std::vector<uint8_t> s;
    if (!ReadFile (resDir + "/" + vec.file, s)) {
      printf ("%s\t-\t-\t-\t-\t-\tMISSING\n", vec.file);
      ++missing;
      continue;
    }
    Chunks clean = SplitChunks (s);
    for (int v = 0; v < kDamagedVariants; ++v) {
      Chunks c = DamageChunks (clean, (uint32_t)s.size(), v);
      std::string d;
      int slices = 0, mbs = 0;
      if (!MbDumpChunks (c, d, slices, mbs)) {
        printf ("%s\t%d\t-\t-\t-\t-\tINITFAIL\n", vec.file, v);
        ++missing;
        continue;
      }
      printf ("%s\t%d\t%zu\t%d\t%d\t%zu\t%s\n", vec.file, v, c.size(), slices, mbs, d.size(), Sha1Hex (d).c_str());
    }
  }
  return missing ? 1 : 0;
}

// The full decoder API over the same damaged copies as headers-damaged and
// mbs-damaged: error concealment, the error returns and ResetDecoder, which no
// clean vector reaches. One row per variant: output SHA1, frame count, how many
// DecodeFrame2/FlushFrame calls failed, the first failing state and the OR of
// all of them.
int DecodeDamaged (const std::string& resDir) {
  BinaryStdout();
  printf ("# %s, C-only reference build. Decoded output of damaged streams (DamageChunks in headers.cpp).\n",
          g_strCodecVer);
  printf ("# file\tvariant\tchunks\tframes\terrors\tfirst\tstates\tsha1\n");
  int missing = 0;
  for (const Vector& vec : kDecoderVectors) {
    std::vector<uint8_t> s;
    if (!ReadFile (resDir + "/" + vec.file, s)) {
      printf ("%s\t-\t-\t-\t-\t-\t-\tMISSING\n", vec.file);
      ++missing;
      continue;
    }
    Chunks clean = SplitChunks (s);
    for (int v = 0; v < kDamagedVariants; ++v) {
      Chunks c = DamageChunks (clean, (uint32_t)s.size(), v);
      DecodeResult r;
      std::string h = HashChunks (c, &r);
      if (!r.ok) {
        printf ("%s\t%d\t-\t-\t-\t-\t-\tINITFAIL\n", vec.file, v);
        ++missing;
        continue;
      }
      printf ("%s\t%d\t%zu\t%d\t%d\t0x%x\t0x%x\t%s\n", vec.file, v, c.size(), r.frames, r.errorChunks,
              r.firstErrorState, r.errorStates, h.c_str());
    }
  }
  return missing ? 1 : 0;
}

int Usage() {
  fprintf (stderr,
           "usage: h264ref conformance <res-dir>\n"
           "       h264ref hash <file>\n"
           "       h264ref info <file>\n"
           "       h264ref yuv <file> <out.yuv>\n"
           "       h264ref headers <res-dir>\n"
           "       h264ref headerdump <file>\n"
           "       h264ref headers-damaged <res-dir>\n"
           "       h264ref headerdump-damaged <file> <variant>\n"
           "       h264ref mbs <res-dir>\n"
           "       h264ref mbdump <file>\n"
           "       h264ref mbs-damaged <res-dir>\n"
           "       h264ref mbdump-damaged <file> <variant>\n"
           "       h264ref decode-damaged <res-dir>\n"
           "       h264ref hash-damaged <file> <variant>\n"
           "       h264ref trace-damaged <file> <variant>\n"
           "       h264ref yuv-damaged <file> <variant> <out.yuv>\n"
           "       h264ref kernels\n"
           "       h264ref encoder-conformance <res-dir>\n"
           "       h264ref paramsets\n"
           "       h264ref idr\n"
           "       h264ref idr-dump <name> <out>\n"
           "       h264ref stream\n"
           "       h264ref stream-dump <name> <out>\n"
           "       h264ref rc\n"
           "       h264ref rc-dump <name> <out>\n");
  return 2;
}

int Kernels() {
  printf ("# %s, C-only reference build. Kernel fixtures, see refharness/src/fixture.h.\n", g_strCodecVer);
  printf ("# group\tkernel\tcase\tsha1\n");
  KernelsTables();
  KernelsMacros();
  KernelsCopyMb();
  KernelsExpandPic();
  KernelsIntraPred();
  KernelsMc();
  KernelsDeblock();
  KernelsSad();
  KernelsDecoderTables();
  KernelsDecodeMbAux();
  KernelsIntraPredDec();
  KernelsEncoderTables();
  KernelsEncodeMbAux();
  KernelsEncDecodeMbAux();
  KernelsEncIntraPred();
  KernelsSample();
  KernelsEncInit();
  KernelsVlcEncoder();
  KernelsSetMbSynCavlc();
  KernelsSvcSetMbSynCavlc();
  KernelsMdIntra();
  KernelsSvcBaseLayerMd();
  KernelsPictureHandle();
  KernelsEncoderExt();
  KernelsSvcEncodeSlice();
  KernelsMvPred();
  KernelsMdInter();
  KernelsSvcEncodePSlice();
  KernelsVaaCalc();
  KernelsComplexity();
  KernelsScd();
  KernelsBgd();
  KernelsAq();
  KernelsDenoise();
  KernelsDownsample();
  KernelsVpFrameWork();
  return 0;
}

}  // namespace

int main (int argc, char** argv) {
  if (argc == 2 && std::string (argv[1]) == "kernels") return Kernels();
  if (argc == 2 && std::string (argv[1]) == "paramsets") {
    BinaryStdout();
    return ParamSets();
  }
  if (argc == 2 && std::string (argv[1]) == "idr") {
    BinaryStdout();
    return Idr();
  }
  if (argc == 4 && std::string (argv[1]) == "idr-dump") return IdrDump (argv[2], argv[3]);
  if (argc == 2 && std::string (argv[1]) == "stream") {
    BinaryStdout();
    return Stream();
  }
  if (argc == 4 && std::string (argv[1]) == "stream-dump") return StreamDump (argv[2], argv[3]);
  if (argc == 2 && std::string (argv[1]) == "rc") {
    BinaryStdout();
    return Rc();
  }
  if (argc == 4 && std::string (argv[1]) == "rc-dump") return RcDump (argv[2], argv[3]);
  if (argc == 2 && std::string (argv[1]) == "preprocess") {
    BinaryStdout();
    return Preprocess();
  }
  if (argc == 4 && std::string (argv[1]) == "preprocess-dump") return PreprocessDump (argv[2], argv[3]);
  if (argc < 3) return Usage();
  std::string cmd = argv[1];
  if (cmd == "conformance") return Conformance (argv[2]);
  if (cmd == "encoder-conformance") {
    BinaryStdout();
    return EncoderConformance (argv[2]);
  }
  if (cmd == "headers") return Headers (argv[2]);
  if (cmd == "headers-damaged") return HeadersDamaged (argv[2]);
  if (cmd == "mbs") return Mbs (argv[2]);
  if (cmd == "mbs-damaged") return MbsDamaged (argv[2]);
  if (cmd == "decode-damaged") return DecodeDamaged (argv[2]);

  std::vector<uint8_t> s;
  if (!ReadFile (argv[2], s)) {
    fprintf (stderr, "cannot read %s\n", argv[2]);
    return 1;
  }
  if (cmd == "hash") {
    DecodeResult r;
    std::string h = HashStream (s, &r);
    printf ("%s\tframes=%d\terrors=%d\n", h.c_str(), r.frames, r.errorChunks);
    return 0;
  }
  if (cmd == "trace-damaged" && argc >= 4) {
    // One line per DecodeFrame2/FlushFrame call: index, input length (-1 for
    // FlushFrame), NAL type of the chunk, state, iBufferStatus and the SHA1 of the
    // output frame. Diff it with DecoderHash.TraceChunks on the C# side.
    Chunks chunks = DamageChunks (SplitChunks (s), (uint32_t)s.size(), atoi (argv[3]));
    BinaryStdout();
    CallTrace t = [&] (int call, int len, DECODING_STATE st, const SBufferInfo& bi, uint8_t* data[3]) {
      int nal = (call < (int)chunks.size() && chunks[call].size() > 4) ? (chunks[call][4] & 0x1f) : -1;
      std::string h = "-";
      if (bi.iBufferStatus == 1) {
        const SSysMEMBuffer& sb = bi.UsrData.sSystemBuffer;
        std::string f;
        for (int i = 0; i < sb.iHeight; ++i) f.append ((const char*)data[0] + (size_t)i * sb.iStride[0], sb.iWidth);
        for (int c = 1; c < 3; ++c)
          for (int i = 0; i < sb.iHeight / 2; ++i) f.append ((const char*)data[c] + (size_t)i * sb.iStride[1], sb.iWidth / 2);
        h = Sha1Hex (f);
      }
      printf ("%d\t%d\t%d\t0x%x\t%d\t%s\n", call, len, nal, (int)st, bi.iBufferStatus, h.c_str());
    };
    DecodeChunks (chunks, [] (const Plane&, const Plane&, const Plane&) {}, &t);
    return 0;
  }
  if (cmd == "hash-damaged" && argc >= 4) {
    DecodeResult r;
    std::string h = HashChunks (DamageChunks (SplitChunks (s), (uint32_t)s.size(), atoi (argv[3])), &r);
    printf ("%s\tframes=%d\terrors=%d\tfirst=0x%x\tstates=0x%x\n", h.c_str(), r.frames, r.errorChunks,
            r.firstErrorState, r.errorStates);
    return 0;
  }
  if (cmd == "headerdump") {
    std::string d;
    int nals = 0, aus = 0;
    if (!HeaderDump (s, d, nals, aus)) return 1;
    BinaryStdout();
    fwrite (d.data(), 1, d.size(), stdout);
    return 0;
  }
  if (cmd == "headerdump-damaged" && argc >= 4) {
    std::string d;
    int nals = 0, aus = 0;
    if (!HeaderDumpChunks (DamageChunks (SplitChunks (s), (uint32_t)s.size(), atoi (argv[3])), d, nals, aus)) return 1;
    BinaryStdout();
    fwrite (d.data(), 1, d.size(), stdout);
    return 0;
  }
  if (cmd == "mbdump") {
    std::string d;
    int slices = 0, mbs = 0;
    if (!MbDump (s, d, slices, mbs)) return 1;
    BinaryStdout();
    fwrite (d.data(), 1, d.size(), stdout);
    return 0;
  }
  if (cmd == "mbdump-damaged" && argc >= 4) {
    std::string d;
    int slices = 0, mbs = 0;
    if (!MbDumpChunks (DamageChunks (SplitChunks (s), (uint32_t)s.size(), atoi (argv[3])), d, slices, mbs)) return 1;
    BinaryStdout();
    fwrite (d.data(), 1, d.size(), stdout);
    return 0;
  }
  if (cmd == "info") {
    StreamInfo info = AnalyseStream (s);
    printf ("CB=%s\t%s\n", info.IsConstrainedBaseline() ? "yes" : "no", info.Describe().c_str());
    return 0;
  }
  bool damagedYuv = cmd == "yuv-damaged" && argc >= 5;
  if ((cmd == "yuv" && argc >= 4) || damagedYuv) {
    const char* outPath = damagedYuv ? argv[4] : argv[3];
    FILE* out = fopen (outPath, "wb");
    if (!out) {
      fprintf (stderr, "cannot write %s\n", outPath);
      return 1;
    }
    auto writePlane = [&] (const Plane& p) {
      const uint8_t* row = p.data;
      for (int i = 0; i < p.height; ++i, row += p.stride) fwrite (row, 1, p.width, out);
    };
    Chunks chunks = damagedYuv ? DamageChunks (SplitChunks (s), (uint32_t)s.size(), atoi (argv[3])) : SplitChunks (s);
    DecodeResult r = DecodeChunks (chunks, [&] (const Plane& y, const Plane& u, const Plane& v) {
      writePlane (y);
      writePlane (u);
      writePlane (v);
    });
    fclose (out);
    printf ("frames=%d errors=%d\n", r.frames, r.errorChunks);
    return 0;
  }
  return Usage();
}
