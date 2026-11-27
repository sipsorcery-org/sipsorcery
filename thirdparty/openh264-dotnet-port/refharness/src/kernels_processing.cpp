// The video processing kernel fixtures (E5b): the VAA statistics of
// codec/processing/src/vaacalc, which the encoder's preprocessing runs on every
// frame (CWelsPreProcess::VaaCalculation). Mirrored by the C# test
// vaacalc_unittest.cs.
//
//   vaacalc  VAACalcSad_c, VAACalcSadVar_c, VAACalcSadSsd_c, VAACalcSadBgd_c,
//            VAACalcSadSsdBgd_c: each on a table of picture sizes and strides
//            (whole and partial macroblocks, the pointer drift a width that is
//            not a multiple of 16 gives) and four kinds of content, the outputs
//            pre-filled with noise so that what a kernel leaves alone is hashed
//            too.
//            CVAACalculation Set and Process: every combination of the three
//            statistics flags, which pick the kernel, and the invalid inputs.
//
//   complexity  (E6) CComplexityAnalysis Set, Process and Get, the frame and GOM
//            complexity the rate control reads: each mode (frame SAD, GOM SAD,
//            GOM variance), with and without the background exclusion, over GOM
//            sizes that do and do not split macroblock rows, on statistics from
//            VAACalcSadVar_c and on raw noise (the unsigned wraps and the sign
//            extensions), with the invalid inputs.
//
// (E7) The rest of the camera preprocessing (CWelsPreProcess), on pictures whose
// reference is built macroblock by macroblock from classes (identical, small
// noise, a shift, larger noise, unrelated), so that each strategy meets both
// of its outcomes. Mirrored by scd_unittest.cs, bgd_unittest.cs,
// aq_unittest.cs, denoise_unittest.cs, downsample_unittest.cs and
// vpframework_unittest.cs.
//
//   scd      CSceneChangeDetection<CSceneChangeDetectorVideo> Set, Process and
//            Get: block counts either side of both thresholds, SADs of 320 and
//            321 (the motion threshold), differing strides, no blocks at all.
//   bgd      CBackgroundDetection Set and Process on the VAA statistics of the
//            pictures, one detector over frames of growing and shrinking size.
//   aq       SampleVariance16x16_c; CAdaptiveQuantization Set, Process and Get
//            in both modes, on the VAA statistics and on its own variances.
//   denoise  BilateralLumaFilter8_c, WaverageChromaFilter8_c, Gauss3x3Filter;
//            CDenoiser on the three planes.
//   downsample  The five kernels; CDownsampling Process down each of its paths
//            (repeated halving, the general ratio after it, and past the
//            sample buffers the half, quarter, third and general steps).
//   vpframework  CVpFrameWork through WelsCreateVpInterface: CheckValid's
//            checks through Process, the methods' Init, Uninit, Flush, Get and
//            Set, and the encoder's sequence of calls for camera content.

#include <cstdarg>
#include <cstdio>
#include <cstring>
#include <string>
#include <utility>
#include <vector>

#include "fixture.h"
#include "vaacalculation.h"
#include "ComplexityAnalysis.h"
#include "SceneChangeDetection.h"
#include "BackgroundDetection.h"
#include "AdaptiveQuantization.h"
#include "denoise.h"
#include "downsample.h"
#include "WelsFrameWork.h"

using namespace fixture;

namespace {

std::string Fmt (const char* fmt, ...) {
  char s[256];
  va_list ap;
  va_start (ap, fmt);
  vsnprintf (s, sizeof s, fmt, ap);
  va_end (ap);
  return s;
}

struct VaaSize {
  int w, h, stride;
};

// Widths and heights that are and are not multiples of 16, and strides wider than the width.
const VaaSize kVaaSizes[] = {
  {16, 16, 16}, {16, 16, 48}, {48, 32, 64}, {33, 17, 48}, {31, 47, 32}, {64, 48, 96}, {176, 144, 240}, {80, 40, 81},
};

// The pictures a kernel reads: a margin before the first row (a width that is not a multiple
// of 16 moves the row pointer back by w % 16 per macroblock row) and after the last.
const int kVaaMargin = 512;

struct VaaPictures {
  std::vector<uint8_t> cur, ref;
  uint8_t* pCur;
  uint8_t* pRef;
};

// kind 0: unrelated; 1: the reference near the current picture (small differences, both
// signs); 2: identical; 3: only 0 and 255, the largest differences.
VaaPictures MakePictures (Rng& r, const VaaSize& s, int kind) {
  VaaPictures p;
  const size_t n = (size_t)s.stride * s.h + 2 * kVaaMargin;
  p.cur.resize (n);
  p.ref.resize (n);
  r.Fill (p.cur.data(), n);
  r.Fill (p.ref.data(), n);
  for (size_t i = 0; i < n; i++) {
    switch (kind) {
    case 1: {
      const int d = (int) (r.Next() % 9) - 4;
      const int v = p.cur[i] + d;
      p.ref[i] = (uint8_t) (v < 0 ? 0 : v > 255 ? 255 : v);
      break;
    }
    case 2:
      p.ref[i] = p.cur[i];
      break;
    case 3:
      p.cur[i] = (p.cur[i] & 1) ? 255 : 0;
      p.ref[i] = (p.ref[i] & 1) ? 255 : 0;
      break;
    default:
      break;
    }
  }
  p.pCur = p.cur.data() + kVaaMargin;
  p.pRef = p.ref.data() + kVaaMargin;
  return p;
}

// The output arrays, one entry (or four) per macroblock of the size rounded up, plus guards,
// filled with noise.
struct VaaOutputs {
  int32_t iFrameSad;
  std::vector<int32_t> sad8x8, sum16x16, sqsum16x16, ssd16x16, sd8x8;
  std::vector<uint8_t> mad8x8;
  explicit VaaOutputs (Rng& r, const VaaSize& s) {
    const int mbs = ((s.w + 15) >> 4) * ((s.h + 15) >> 4) + 2;
    sad8x8.resize (mbs * 4);
    sum16x16.resize (mbs);
    sqsum16x16.resize (mbs);
    ssd16x16.resize (mbs);
    sd8x8.resize (mbs * 4);
    mad8x8.resize (mbs * 4);
    iFrameSad = (int32_t)r.Next();
    r.Fill ((uint8_t*)sad8x8.data(), sad8x8.size() * 4);
    r.Fill ((uint8_t*)sum16x16.data(), sum16x16.size() * 4);
    r.Fill ((uint8_t*)sqsum16x16.data(), sqsum16x16.size() * 4);
    r.Fill ((uint8_t*)ssd16x16.data(), ssd16x16.size() * 4);
    r.Fill ((uint8_t*)sd8x8.data(), sd8x8.size() * 4);
    r.Fill (mad8x8.data(), mad8x8.size());
  }
  void HashInto (Hasher& h) const {
    h.AddI32 (iFrameSad);
    for (int32_t v : sad8x8) h.AddI32 (v);
    for (int32_t v : sum16x16) h.AddI32 (v);
    for (int32_t v : sqsum16x16) h.AddI32 (v);
    for (int32_t v : ssd16x16) h.AddI32 (v);
    for (int32_t v : sd8x8) h.AddI32 (v);
    h.Add (mad8x8.data(), mad8x8.size());
  }
};

enum VaaKernel { kSad, kSadVar, kSadSsd, kSadBgd, kSadSsdBgd };
const char* const kVaaKernelNames[] = {"VAACalcSad_c", "VAACalcSadVar_c", "VAACalcSadSsd_c", "VAACalcSadBgd_c",
                                       "VAACalcSadSsdBgd_c"
                                      };

void RunKernel (int k, const VaaPictures& p, const VaaSize& s, VaaOutputs& o) {
  switch (k) {
  case kSad:
    WelsVP::VAACalcSad_c (p.pCur, p.pRef, s.w, s.h, s.stride, &o.iFrameSad, o.sad8x8.data());
    break;
  case kSadVar:
    WelsVP::VAACalcSadVar_c (p.pCur, p.pRef, s.w, s.h, s.stride, &o.iFrameSad, o.sad8x8.data(), o.sum16x16.data(),
                             o.sqsum16x16.data());
    break;
  case kSadSsd:
    WelsVP::VAACalcSadSsd_c (p.pCur, p.pRef, s.w, s.h, s.stride, &o.iFrameSad, o.sad8x8.data(), o.sum16x16.data(),
                             o.sqsum16x16.data(), o.ssd16x16.data());
    break;
  case kSadBgd:
    WelsVP::VAACalcSadBgd_c (p.pCur, p.pRef, s.w, s.h, s.stride, &o.iFrameSad, o.sad8x8.data(), o.sd8x8.data(),
                             o.mad8x8.data());
    break;
  default:
    WelsVP::VAACalcSadSsdBgd_c (p.pCur, p.pRef, s.w, s.h, s.stride, &o.iFrameSad, o.sad8x8.data(), o.sum16x16.data(),
                                o.sqsum16x16.data(), o.ssd16x16.data(), o.sd8x8.data(), o.mad8x8.data());
    break;
  }
}

// An SVAACalcResult over the outputs (pSumOfDiff8x8 is sd8x8).
SVAACalcResult ResultOf (VaaOutputs& o) {
  SVAACalcResult res;
  memset (&res, 0, sizeof res);
  res.pSad8x8 = (int32_t (*)[4])o.sad8x8.data();
  res.pSsd16x16 = o.ssd16x16.data();
  res.pSum16x16 = o.sum16x16.data();
  res.pSumOfSquare16x16 = o.sqsum16x16.data();
  res.pSumOfDiff8x8 = (int32_t (*)[4])o.sd8x8.data();
  res.pMad8x8 = (uint8_t (*)[4])o.mad8x8.data();
  res.iFrameSad = o.iFrameSad;
  return res;
}

SPixMap PixMapOf (uint8_t* p, const VaaSize& s) {
  SPixMap m;
  memset (&m, 0, sizeof m);
  m.pPixel[0] = p;
  m.iSizeInBits = 8;
  m.iStride[0] = s.stride;
  m.sRect.iRectWidth = s.w;
  m.sRect.iRectHeight = s.h;
  m.eFormat = VIDEO_FORMAT_I420;
  return m;
}

}  // namespace

void KernelsVaaCalc() {
  for (int k = 0; k < 5; k++) {
    int seed = 0;
    for (const VaaSize& s : kVaaSizes) {
      for (int kind = 0; kind < 4; kind++, seed++) {
        Rng r (seed);
        VaaPictures p = MakePictures (r, s, kind);
        VaaOutputs o (r, s);
        RunKernel (k, p, s, o);
        Hasher h;
        o.HashInto (h);
        Emit ("vaacalc", kVaaKernelNames[k], Fmt ("w=%d,h=%d,stride=%d,kind=%d,seed=%d", s.w, s.h, s.stride, kind, seed),
              h);
      }
    }
  }

  // CVAACalculation: Set, then Process, for each flag combination (iCalcVar, iCalcBgd, iCalcSsd).
  const VaaSize kProcessSizes[] = {{48, 32, 64}, {33, 17, 48}};
  int seed = 100;
  for (const VaaSize& s : kProcessSizes) {
    for (int flags = 0; flags < 8; flags++, seed++) {
      Rng r (seed);
      VaaPictures p = MakePictures (r, s, 1);
      VaaOutputs o (r, s);
      SVAACalcResult res = ResultOf (o);
      SVAACalcParam param;
      memset (&param, 0, sizeof param);
      param.iCalcVar = flags & 1;
      param.iCalcBgd = (flags >> 1) & 1;
      param.iCalcSsd = (flags >> 2) & 1;
      param.pCalcResult = &res;
      WelsVP::CVAACalculation calc (0);
      SPixMap cur = PixMapOf (p.pCur, s), ref = PixMapOf (p.pRef, s);
      Hasher h;
      h.AddI32 (calc.Set (METHOD_VAA_STATISTICS, &param));
      h.AddI32 (calc.Process (METHOD_VAA_STATISTICS, &cur, &ref));
      o.iFrameSad = res.iFrameSad;
      h.AddI32 (res.pCurY == p.pCur);
      h.AddI32 (res.pRefY == p.pRef);
      o.HashInto (h);
      Emit ("vaacalc", "CVAACalculation", Fmt ("w=%d,h=%d,stride=%d,var=%d,bgd=%d,ssd=%d,seed=%d", s.w, s.h, s.stride,
            param.iCalcVar, param.iCalcBgd, param.iCalcSsd, seed), h);
    }
  }

  // The invalid inputs: Set without a parameter or a result, Process without a picture.
  {
    Rng r (200);
    const VaaSize s = {16, 16, 16};
    VaaPictures p = MakePictures (r, s, 0);
    VaaOutputs o (r, s);
    SVAACalcResult res = ResultOf (o);
    SVAACalcParam param;
    memset (&param, 0, sizeof param);
    WelsVP::CVAACalculation calc (0);
    Hasher h;
    h.AddI32 (calc.Set (METHOD_VAA_STATISTICS, NULL));
    h.AddI32 (calc.Set (METHOD_VAA_STATISTICS, &param));   // pCalcResult NULL
    param.pCalcResult = &res;
    h.AddI32 (calc.Set (METHOD_VAA_STATISTICS, &param));
    SPixMap cur = PixMapOf (p.pCur, s), ref = PixMapOf (NULL, s);
    h.AddI32 (calc.Process (METHOD_VAA_STATISTICS, &cur, &ref));
    h.AddI32 (calc.Process (METHOD_VAA_STATISTICS, &ref, &cur));
    o.iFrameSad = res.iFrameSad;
    h.AddI32 (res.pCurY == NULL);
    h.AddI32 (res.pRefY == NULL);
    o.HashInto (h);
    Emit ("vaacalc", "CVAACalculation", "invalid", h);
  }
}

namespace {

// Picture sizes for the complexity analysis, which works in whole macroblocks (w >> 4, h >> 4).
const VaaSize kComplexitySizes[] = {
  {16, 16, 16}, {48, 32, 64}, {33, 17, 48}, {31, 47, 32}, {176, 144, 240}, {80, 40, 81},
};

// Reference macroblock types: none, each of processing's three intra bits, the encoder's other
// bits, and all of them.
const uint32_t kRefMbTypes[] = {0, 1, 2, 4, 8, 0x10, 0x100, 0x7, 0xfffffff8u, 0xffffffffu};

// The per-macroblock inputs and the per-GOM outputs of one row, plus two guards each.
struct ComplexityBuffers {
  std::vector<uint8_t> bgd;
  std::vector<uint32_t> refMbType;
  std::vector<int32_t> gomComplexity, gomForeground;
  ComplexityBuffers (Rng& r, int mbs, int goms) : bgd (mbs + 2), refMbType (mbs + 2), gomComplexity (goms + 2),
    gomForeground (goms + 2) {
    for (size_t i = 0; i < bgd.size(); i++) {
      const uint32_t x = r.Next();
      bgd[i] = (x & 1) ? 0 : (uint8_t) (x >> 8);
    }
    for (size_t i = 0; i < refMbType.size(); i++) refMbType[i] = kRefMbTypes[r.Next() % 10];
    r.Fill ((uint8_t*)gomComplexity.data(), gomComplexity.size() * 4);
    for (size_t i = 0; i < gomForeground.size(); i++) gomForeground[i] = (int32_t) (r.Next() % 1000);
  }
  void HashInto (Hasher& h) const {
    for (int32_t v : gomComplexity) h.AddI32 (v);
    for (int32_t v : gomForeground) h.AddI32 (v);
  }
};

int64_t Noise64 (Rng& r) {
  const uint32_t hi = r.Next();
  return (int64_t) (((uint64_t)hi << 32) | r.Next());
}

}  // namespace

void KernelsComplexity() {
  int seed = 300;
  for (const VaaSize& s : kComplexitySizes) {
    const int mbw = s.w >> 4, mbs = mbw * (s.h >> 4);
    const int gomSizes[] = {1, mbw, 2 * mbw, mbw + 1, mbs + 3};
    for (int gom : gomSizes) {
      const int goms = (mbs + gom - 1) / gom;
      for (int data = 0; data < 2; data++) {
        for (int mode = FRAME_SAD; mode >= GOM_VAR; mode--) {
          for (int bgd = 0; bgd < 2; bgd++, seed++) {
            // data 0: the statistics VAACalcSadVar_c gives; 1: the noise the outputs start with.
            Rng r (seed);
            VaaPictures p = MakePictures (r, s, seed & 3);
            VaaOutputs o (r, s);
            if (data == 0) RunKernel (kSadVar, p, s, o);
            SVAACalcResult res = ResultOf (o);
            ComplexityBuffers b (r, mbs, goms);
            SComplexityAnalysisParam param;
            memset (&param, 0, sizeof param);
            param.iComplexityAnalysisMode = mode;
            param.iCalcBgd = bgd;
            param.iMbNumInGom = gom;
            param.iFrameComplexity = Noise64 (r);
            param.pGomComplexity = b.gomComplexity.data();
            param.pGomForegroundBlockNum = b.gomForeground.data();
            param.pBackgroundMbFlag = (int8_t*)b.bgd.data();
            param.uiRefMbType = b.refMbType.data();
            param.pCalcResult = &res;
            SComplexityAnalysisParam out;
            memset (&out, 0, sizeof out);
            out.iFrameComplexity = Noise64 (r);

            WelsVP::CComplexityAnalysis ca (0);
            SPixMap cur = PixMapOf (p.pCur, s), ref = PixMapOf (p.pRef, s);
            Hasher h;
            h.AddI32 (ca.Set (METHOD_COMPLEXITY_ANALYSIS, &param));
            h.AddI32 (ca.Process (METHOD_COMPLEXITY_ANALYSIS, &cur, &ref));
            h.AddI32 (ca.Get (METHOD_COMPLEXITY_ANALYSIS, &out));
            h.AddI64 (out.iFrameComplexity);
            h.AddI64 (param.iFrameComplexity);   // Set copies the parameters; Process works on the copy
            b.HashInto (h);
            Emit ("complexity", "CComplexityAnalysis", Fmt ("w=%d,h=%d,stride=%d,gom=%d,data=%d,mode=%d,bgd=%d,seed=%d", s.w,
                  s.h, s.stride, gom, data, mode, bgd, seed), h);
          }
        }
      }
    }
  }

  // The invalid inputs: Set and Get without a parameter, Process in a mode that is not one,
  // and Get after it (the complexity Set gave).
  {
    Rng r (400);
    const VaaSize s = {48, 32, 64};
    VaaPictures p = MakePictures (r, s, 1);
    VaaOutputs o (r, s);
    SVAACalcResult res = ResultOf (o);
    ComplexityBuffers b (r, 6, 3);
    SComplexityAnalysisParam param;
    memset (&param, 0, sizeof param);
    param.iMbNumInGom = 2;
    param.iFrameComplexity = Noise64 (r);
    param.pGomComplexity = b.gomComplexity.data();
    param.pGomForegroundBlockNum = b.gomForeground.data();
    param.pBackgroundMbFlag = (int8_t*)b.bgd.data();
    param.uiRefMbType = b.refMbType.data();
    param.pCalcResult = &res;
    SComplexityAnalysisParam out;
    memset (&out, 0, sizeof out);
    WelsVP::CComplexityAnalysis ca (0);
    SPixMap cur = PixMapOf (p.pCur, s), ref = PixMapOf (p.pRef, s);
    Hasher h;
    h.AddI32 (ca.Set (METHOD_COMPLEXITY_ANALYSIS, NULL));
    h.AddI32 (ca.Get (METHOD_COMPLEXITY_ANALYSIS, NULL));
    const int kModes[] = {1, -3, 7};
    for (int mode : kModes) {
      param.iComplexityAnalysisMode = mode;
      out.iFrameComplexity = Noise64 (r);
      h.AddI32 (ca.Set (METHOD_COMPLEXITY_ANALYSIS, &param));
      h.AddI32 (ca.Process (METHOD_COMPLEXITY_ANALYSIS, &cur, &ref));
      h.AddI32 (ca.Get (METHOD_COMPLEXITY_ANALYSIS, &out));
      h.AddI64 (out.iFrameComplexity);
    }
    b.HashInto (h);
    Emit ("complexity", "CComplexityAnalysis", "invalid", h);
  }
}

// ---- E7: the strategies the encoder's preprocessing runs for camera content ----

namespace {

uint8_t Clip255 (int v) {
  return (uint8_t) (v < 0 ? 0 : v > 255 ? 255 : v);
}

// A picture plane with kVaaMargin before and after it, filled with noise.
struct Plane {
  std::vector<uint8_t> buf;
  uint8_t* p = nullptr;
  int w = 0, h = 0, stride = 0;
  void Make (Rng& r, int pw, int ph, int pstride) {
    w = pw;
    h = ph;
    stride = pstride;
    buf.assign ((size_t)pstride * ph + 2 * kVaaMargin, 0);
    r.Fill (buf.data(), buf.size());
    p = buf.data() + kVaaMargin;
  }
  void HashInto (Hasher& hs) const {
    hs.Add (buf.data(), buf.size());
  }
};

struct YuvSize {
  int w, h, strideY, strideUV;
};

// An I420 picture: Y, then U and V of half the width and height.
struct Yuv {
  Plane pl[3];
  void Make (Rng& r, const YuvSize& s) {
    pl[0].Make (r, s.w, s.h, s.strideY);
    pl[1].Make (r, s.w >> 1, s.h >> 1, s.strideUV);
    pl[2].Make (r, s.w >> 1, s.h >> 1, s.strideUV);
  }
  void HashInto (Hasher& hs) const {
    for (const Plane& q : pl) q.HashInto (hs);
  }
  SPixMap Map() {
    SPixMap m;
    memset (&m, 0, sizeof m);
    for (int c = 0; c < 3; c++) {
      m.pPixel[c] = pl[c].p;
      m.iStride[c] = pl[c].stride;
    }
    m.iSizeInBits = 8;
    m.sRect.iRectWidth = pl[0].w;
    m.sRect.iRectHeight = pl[0].h;
    m.eFormat = VIDEO_FORMAT_I420;
    return m;
  }
};

// Reshapes a whole plane, margins included: kind 0 leaves the noise, 1 near 128 (within 8, the
// bilateral filter's middle weights), 2 flat, 3 only 0 and 255.
void Shape (Rng& r, Plane& q, int kind) {
  for (uint8_t& v : q.buf) {
    switch (kind) {
    case 1:
      v = (uint8_t) (128 + (int) (r.Next() % 17) - 8);
      break;
    case 2:
      v = 77;
      break;
    case 3:
      v = (v & 1) ? 255 : 0;
      break;
    default:
      break;
    }
  }
}

// What a macroblock of the reference (and its chroma blocks) is made of: 0 the current picture,
// 1 within 2 of it, 2 shifted up by 2 to 4, 3 within 12, 4 unrelated. kMixes turns Next() % 16
// into a class: a uniform mix, mostly static, all static, all unrelated.
const int kMixes[4][5] = {{4, 7, 10, 13, 16}, {9, 13, 14, 15, 16}, {16, 16, 16, 16, 16}, {0, 0, 0, 0, 16}};

void ApplyClass (Rng& r, int cls, const Plane& cur, Plane& ref, int x0, int y0, int n) {
  for (int y = y0; y < y0 + n && y < cur.h; y++) {
    for (int x = x0; x < x0 + n && x < cur.w; x++) {
      const int c = cur.p[y * cur.stride + x];
      uint8_t& d = ref.p[y * ref.stride + x];
      switch (cls) {
      case 0:
        d = (uint8_t)c;
        break;
      case 1:
        d = Clip255 (c + (int) (r.Next() % 5) - 2);
        break;
      case 2:
        d = Clip255 (c + 3 + (int) (r.Next() % 3) - 1);
        break;
      case 3:
        d = Clip255 (c + (int) (r.Next() % 25) - 12);
        break;
      default:
        break;
      }
    }
  }
}

// A current picture and a reference, macroblock by macroblock (whole or partial) from the mix.
void MakeYuvPair (Rng& r, const YuvSize& s, int mix, Yuv& cur, Yuv& ref) {
  cur.Make (r, s);
  ref.Make (r, s);
  for (int my = 0; my < (s.h + 15) >> 4; my++) {
    for (int mx = 0; mx < (s.w + 15) >> 4; mx++) {
      const int v = (int) (r.Next() % 16);
      int cls = 0;
      while (v >= kMixes[mix][cls]) cls++;
      ApplyClass (r, cls, cur.pl[0], ref.pl[0], mx * 16, my * 16, 16);
      ApplyClass (r, cls, cur.pl[1], ref.pl[1], mx * 8, my * 8, 8);
      ApplyClass (r, cls, cur.pl[2], ref.pl[2], mx * 8, my * 8, 8);
    }
  }
}

// The statistics the encoder computes for camera content with background detection and
// adaptive quantisation on (VAACalcSadSsdBgd_c).
void RunVaa (Yuv& cur, Yuv& ref, const YuvSize& s, VaaOutputs& o) {
  VaaPictures p;
  p.pCur = cur.pl[0].p;
  p.pRef = ref.pl[0].p;
  RunKernel (kSadSsdBgd, p, VaaSize{s.w, s.h, s.strideY}, o);
}

int MbCount (int w, int h) {
  return ((w + 15) >> 4) * ((h + 15) >> 4);
}

void HashScdResult (Hasher& h, const SSceneChangeResult& s, const uint8_t* pStatic) {
  h.AddI32 (s.eSceneChangeIdc);
  h.AddI32 (s.iMotionBlockNum);
  h.AddI64 (s.iFrameComplexity);
  h.AddI32 (s.pStaticBlockIdc == pStatic);
  h.AddI32 (s.sScrollResult.sMaskRect.iRectTop);
  h.AddI32 (s.sScrollResult.sMaskRect.iRectLeft);
  h.AddI32 (s.sScrollResult.sMaskRect.iRectWidth);
  h.AddI32 (s.sScrollResult.sMaskRect.iRectHeight);
  h.AddI32 (s.sScrollResult.bMaskInfoAvailable);
  h.AddI32 (s.sScrollResult.iScrollMvX);
  h.AddI32 (s.sScrollResult.iScrollMvY);
  h.AddI32 (s.sScrollResult.bScrollDetectFlag);
}

SSceneChangeResult NoiseScdResult (Rng& r, uint8_t* pStatic) {
  SSceneChangeResult s;
  memset (&s, 0, sizeof s);
  s.eSceneChangeIdc = (ESceneChangeIdc) (r.Next() % 3);
  s.iMotionBlockNum = (int)r.Next();
  s.iFrameComplexity = Noise64 (r);
  s.pStaticBlockIdc = pStatic;
  s.sScrollResult.sMaskRect.iRectTop = (int)r.Next();
  s.sScrollResult.sMaskRect.iRectLeft = (int)r.Next();
  s.sScrollResult.sMaskRect.iRectWidth = (int)r.Next();
  s.sScrollResult.sMaskRect.iRectHeight = (int)r.Next();
  s.sScrollResult.bMaskInfoAvailable = (r.Next() & 1) != 0;
  s.sScrollResult.iScrollMvX = (int)r.Next();
  s.sScrollResult.iScrollMvY = (int)r.Next();
  s.sScrollResult.bScrollDetectFlag = (r.Next() & 1) != 0;
  return s;
}

typedef WelsVP::CSceneChangeDetection<WelsVP::CSceneChangeDetectorVideo> CSceneChangeDetectionVideo;

struct ScdSize {
  int w, h, curStride, refStride;
};

// Whole and partial 8x8 blocks, a reference stride other than the current one, and a picture
// narrower than a block (no blocks: both thresholds are 0, so the scene has changed).
const ScdSize kScdSizes[] = {
  {16, 16, 16, 16}, {64, 48, 64, 80}, {37, 29, 48, 40}, {176, 144, 176, 192}, {7, 15, 16, 16},
};

}  // namespace

void KernelsScd() {
  int seed = 500;
  for (const ScdSize& s : kScdSizes) {
    const int bw = s.w >> 3, n = bw * (s.h >> 3);
    // The two thresholds, as Process computes them, and the block counts either side of them.
    const int thrL = (int) (0.85f * n + 0.5f + 0.000001);
    const int thrM = (int) (0.50f * n + 0.5f + 0.000001);
    std::vector<int> moved;
    for (int m : {0, thrM - 1, thrM, thrL - 1, thrL, n}) {
      bool seen = false;
      for (int x : moved) seen = seen || x == m;
      if (m >= 0 && m <= n && !seen) moved.push_back (m);
    }
    // kind 0: unrelated; 1: the reference within 4 (no block over the threshold); 2: the first
    // `moved` blocks unrelated, the rest identical; 3: every block's SAD 320 (even) or 321 (odd).
    std::vector<std::pair<int, int>> cases = {{0, 0}, {1, 0}, {3, 0}};
    for (int m : moved) cases.push_back ({2, m});
    for (const std::pair<int, int>& c : cases) {
      const int kind = c.first;
      Rng r (seed);
      std::vector<uint8_t> cur ((size_t)s.curStride * s.h + 2 * kVaaMargin), ref ((size_t)s.refStride * s.h + 2 * kVaaMargin);
      r.Fill (cur.data(), cur.size());
      r.Fill (ref.data(), ref.size());
      uint8_t* pc = cur.data() + kVaaMargin;
      uint8_t* pr = ref.data() + kVaaMargin;
      if (kind == 1 || kind == 2) {
        for (int y = 0; y < s.h; y++) {
          for (int x = 0; x < s.w; x++) {
            const int v = pc[y * s.curStride + x];
            pr[y * s.refStride + x] = kind == 1 ? Clip255 (v + (int) (r.Next() % 9) - 4) : (uint8_t)v;
          }
        }
      }
      if (kind == 2) {
        for (int b = 0; b < c.second; b++) {
          const int bx = (b % bw) * 8, by = (b / bw) * 8;
          for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++) pc[(by + y) * s.curStride + bx + x] = r.NextByte();
        }
      }
      if (kind == 3) {
        for (int b = 0; b < n; b++) {
          const int bx = (b % bw) * 8, by = (b / bw) * 8;
          for (int y = 0; y < 8; y++) {
            for (int x = 0; x < 8; x++) {
              pc[(by + y) * s.curStride + bx + x] = 100;
              pr[(by + y) * s.refStride + bx + x] = 100;
            }
          }
          for (int x = 0; x < 5; x++) pc[by * s.curStride + bx + x] = 164;
          if (b & 1) pc[by * s.curStride + bx + 5] = 101;
        }
      }

      // Set gives a result with noise in every field, Process recomputes three of them, Get
      // copies it all out; then once more with the pictures the other way round.
      std::vector<uint8_t> idc (n + 2);
      SSceneChangeResult init = NoiseScdResult (r, idc.data());
      CSceneChangeDetectionVideo scd (METHOD_SCENE_CHANGE_DETECTION_VIDEO, 0);
      SPixMap mc = PixMapOf (pc, VaaSize{s.w, s.h, s.curStride}), mr = PixMapOf (pr, VaaSize{s.w, s.h, s.refStride});
      Hasher h;
      h.AddI32 (scd.Set (0, &init));
      h.AddI32 (scd.Process (0, &mc, &mr));
      SSceneChangeResult out;
      memset (&out, 0, sizeof out);
      h.AddI32 (scd.Get (0, &out));
      HashScdResult (h, out, idc.data());
      h.AddI32 (scd.Process (0, &mr, &mc));
      memset (&out, 0, sizeof out);
      h.AddI32 (scd.Get (0, &out));
      HashScdResult (h, out, idc.data());
      Emit ("scd", "CSceneChangeDetection", Fmt ("w=%d,h=%d,cur=%d,ref=%d,kind=%d,moved=%d,seed=%d", s.w, s.h, s.curStride,
            s.refStride, kind, c.second, seed), h);
      seed++;
    }
  }

  // The invalid inputs, and Get before any Process (the constructor's zeroed result).
  {
    CSceneChangeDetectionVideo scd (METHOD_SCENE_CHANGE_DETECTION_VIDEO, 0);
    Hasher h;
    h.AddI32 (scd.Set (0, NULL));
    h.AddI32 (scd.Get (0, NULL));
    SSceneChangeResult out;
    memset (&out, 0, sizeof out);
    out.iMotionBlockNum = 7;
    h.AddI32 (scd.Get (0, &out));
    HashScdResult (h, out, NULL);
    Emit ("scd", "CSceneChangeDetection", "invalid", h);
  }
}

namespace {

// Whole macroblocks and partial ones (the background detection works in whole 16x16 units, its
// flags per macroblock of the rounded-up width), strides wider than the picture.
const YuvSize kBgdSizes[] = {
  {16, 16, 16, 8}, {48, 48, 48, 24}, {64, 48, 80, 40}, {40, 40, 48, 24}, {80, 64, 80, 48}, {176, 144, 192, 96},
  {320, 192, 320, 160},
};

void BgdFrame (Rng& r, const YuvSize& s, int mix, WelsVP::CBackgroundDetection& bgd, Hasher& h) {
  Yuv cur, ref;
  MakeYuvPair (r, s, mix, cur, ref);
  VaaOutputs o (r, VaaSize{s.w, s.h, s.strideY});
  RunVaa (cur, ref, s, o);
  SVAACalcResult res = ResultOf (o);
  std::vector<uint8_t> flags (MbCount (s.w, s.h) + 2);
  r.Fill (flags.data(), flags.size());
  SBGDInterface bi;
  bi.pBackgroundMbFlag = (int8_t*)flags.data();
  bi.pCalcRes = &res;
  SPixMap mc = cur.Map(), mr = ref.Map();
  h.AddI32 (bgd.Set (0, &bi));
  h.AddI32 (bgd.Process (0, &mc, &mr));
  h.Add (flags.data(), flags.size());
}

}  // namespace

void KernelsBgd() {
  int seed = 600;
  for (const YuvSize& s : kBgdSizes) {
    for (int mix = 0; mix < 4; mix++, seed++) {
      Rng r (seed);
      WelsVP::CBackgroundDetection bgd (0);
      Hasher h;
      BgdFrame (r, s, mix, bgd, h);
      Emit ("bgd", "CBackgroundDetection", Fmt ("w=%d,h=%d,stride=%d,uvstride=%d,mix=%d,seed=%d", s.w, s.h, s.strideY,
            s.strideUV, mix, seed), h);
    }
  }

  // One detector over frames of different sizes: it reallocates its OU array for a frame larger
  // than any before it and keeps it for smaller ones.
  {
    const YuvSize kSequence[] = {{48, 48, 48, 24}, {176, 144, 192, 96}, {64, 48, 80, 40}, {176, 144, 176, 88}};
    WelsVP::CBackgroundDetection bgd (0);
    Hasher h;
    int s = 700;
    for (const YuvSize& sz : kSequence) {
      Rng r (s++);
      BgdFrame (r, sz, 1, bgd, h);
    }
    Emit ("bgd", "CBackgroundDetection", "sequence", h);
  }

  // The invalid inputs: Set without a parameter, Process without a picture.
  {
    Rng r (710);
    Yuv cur;
    cur.Make (r, YuvSize{16, 16, 16, 8});
    SPixMap mc = cur.Map();
    WelsVP::CBackgroundDetection bgd (0);
    Hasher h;
    h.AddI32 (bgd.Set (0, NULL));
    h.AddI32 (bgd.Process (0, NULL, &mc));
    h.AddI32 (bgd.Process (0, &mc, NULL));
    Emit ("bgd", "CBackgroundDetection", "invalid", h);
  }
}

namespace {

// Whole macroblocks only: the adaptive quantisation works on (w >> 4) x (h >> 4).
const YuvSize kAqSizes[] = {
  {16, 16, 16, 8}, {48, 32, 64, 32}, {40, 40, 48, 24}, {64, 48, 80, 40}, {176, 144, 176, 88},
};

}  // namespace

void KernelsAq() {
  int seed = 800;
  const int kStrides[] = {16, 48, 17};
  for (int stride : kStrides) {
    for (int kind = 0; kind < 4; kind++, seed++) {
      Rng r (seed);
      VaaPictures p = MakePictures (r, VaaSize{16, 16, stride}, kind);
      SMotionTextureUnit u;
      u.uiMotionIndex = (uint16_t)r.Next();
      u.uiTextureIndex = (uint16_t)r.Next();
      WelsVP::SampleVariance16x16_c (p.pRef, stride, p.pCur, stride, &u);
      Hasher h;
      h.AddI32 (u.uiMotionIndex);
      h.AddI32 (u.uiTextureIndex);
      Emit ("aq", "SampleVariance16x16_c", Fmt ("stride=%d,kind=%d,seed=%d", stride, kind, seed), h);
    }
  }

  // Set, Process and Get in each mode, on the VAA statistics (when they are of these pictures)
  // and on SampleVariance16x16_c (when they are not).
  seed = 850;
  for (const YuvSize& s : kAqSizes) {
    for (int mix = 0; mix < 4; mix++) {
      for (int mode = AQ_QUALITY_MODE; mode <= AQ_BITRATE_MODE; mode++) {
        for (int vaa = 0; vaa < 2; vaa++, seed++) {
          Rng r (seed);
          Yuv cur, ref;
          MakeYuvPair (r, s, mix, cur, ref);
          VaaOutputs o (r, VaaSize{s.w, s.h, s.strideY});
          RunVaa (cur, ref, s, o);
          SVAACalcResult res = ResultOf (o);
          res.pCurY = vaa ? cur.pl[0].p : NULL;
          res.pRefY = vaa ? ref.pl[0].p : NULL;
          const int mbs = (s.w >> 4) * (s.h >> 4);
          std::vector<SMotionTextureUnit> mt (mbs + 2);
          r.Fill ((uint8_t*)mt.data(), mt.size() * sizeof (SMotionTextureUnit));
          std::vector<uint8_t> dqp (mbs + 2);
          r.Fill (dqp.data(), dqp.size());
          SAdaptiveQuantizationParam param;
          memset (&param, 0, sizeof param);
          param.iAdaptiveQuantMode = mode;
          param.pCalcResult = &res;
          param.pMotionTextureUnit = mt.data();
          param.pMotionTextureIndexToDeltaQp = (int8_t*)dqp.data();
          param.iAverMotionTextureIndexToDeltaQp = (int)r.Next();
          SAdaptiveQuantizationParam out;
          memset (&out, 0, sizeof out);
          out.iAverMotionTextureIndexToDeltaQp = (int)r.Next();

          WelsVP::CAdaptiveQuantization aq (0);
          SPixMap mc = cur.Map(), mr = ref.Map();
          Hasher h;
          h.AddI32 (aq.Set (0, &param));
          h.AddI32 (aq.Process (0, &mc, &mr));
          h.AddI32 (aq.Get (0, &out));
          h.AddI32 (out.iAverMotionTextureIndexToDeltaQp);
          h.AddI32 (param.iAverMotionTextureIndexToDeltaQp);  // Set copies the parameters
          h.Add (mt.data(), mt.size() * sizeof (SMotionTextureUnit));
          h.Add (dqp.data(), dqp.size());
          Emit ("aq", "CAdaptiveQuantization", Fmt ("w=%d,h=%d,stride=%d,mix=%d,mode=%d,vaa=%d,seed=%d", s.w, s.h, s.strideY,
                mix, mode, vaa, seed), h);
        }
      }
    }
  }

  // The invalid inputs: Set and Get without a parameter.
  {
    WelsVP::CAdaptiveQuantization aq (0);
    Hasher h;
    h.AddI32 (aq.Set (0, NULL));
    h.AddI32 (aq.Get (0, NULL));
    Emit ("aq", "CAdaptiveQuantization", "invalid", h);
  }
}

namespace {

const YuvSize kDenoiseSizes[] = {
  {8, 8, 8, 4}, {16, 16, 16, 8}, {24, 18, 32, 16}, {35, 27, 48, 24}, {48, 32, 48, 24}, {176, 144, 176, 88},
  {320, 192, 336, 168},
};

}  // namespace

void KernelsDenoise() {
  // The filters on the 8 samples (Gauss3x3Filter on the one) at row 4, column 3 of a 12-row plane.
  const char* const kNames[] = {"BilateralLumaFilter8_c", "WaverageChromaFilter8_c", "Gauss3x3Filter"};
  const int kStrides[] = {16, 24, 40};
  int seed = 1000;
  for (int k = 0; k < 3; k++) {
    for (int stride : kStrides) {
      for (int kind = 0; kind < 4; kind++, seed++) {
        Rng r (seed);
        Plane q;
        q.Make (r, stride, 12, stride);
        Shape (r, q, kind);
        uint8_t* p = q.p + 4 * stride + 3;
        switch (k) {
        case 0:
          WelsVP::BilateralLumaFilter8_c (p, stride);
          break;
        case 1:
          WelsVP::WaverageChromaFilter8_c (p, stride);
          break;
        default:
          WelsVP::Gauss3x3Filter (p, stride);
          break;
        }
        Hasher h;
        q.HashInto (h);
        Emit ("denoise", kNames[k], Fmt ("stride=%d,kind=%d,seed=%d", stride, kind, seed), h);
      }
    }
  }

  // CDenoiser on all three planes: the filters inside the radius, Gauss3x3Filter for the tail of
  // each row, pictures too small for either.
  seed = 1100;
  for (const YuvSize& s : kDenoiseSizes) {
    for (int kind = 0; kind < 4; kind++, seed++) {
      Rng r (seed);
      Yuv y;
      y.Make (r, s);
      for (Plane& q : y.pl) Shape (r, q, kind);
      WelsVP::CDenoiser dn (0);
      SPixMap src = y.Map(), dst;
      memset (&dst, 0, sizeof dst);
      Hasher h;
      h.AddI32 (dn.Process (0, &src, &dst));
      y.HashInto (h);
      Emit ("denoise", "CDenoiser", Fmt ("w=%d,h=%d,stride=%d,uvstride=%d,kind=%d,seed=%d", s.w, s.h, s.strideY, s.strideUV,
                                         kind, seed), h);
    }
  }

  // The invalid inputs: each plane missing in turn.
  {
    Rng r (1200);
    Yuv y;
    y.Make (r, YuvSize{16, 16, 16, 8});
    WelsVP::CDenoiser dn (0);
    Hasher h;
    for (int c = 0; c < 3; c++) {
      SPixMap src = y.Map(), dst;
      memset (&dst, 0, sizeof dst);
      src.pPixel[c] = NULL;
      h.AddI32 (dn.Process (0, &src, &dst));
    }
    y.HashInto (h);
    Emit ("denoise", "CDenoiser", "invalid", h);
  }
}

namespace {

struct DsCase {
  int dstW, dstH, srcW, srcH;
};

// Per kernel: the dyadic halving, the quarter, the one-third (which takes the destination
// height), and the general ratios (fast for luma, accurate for chroma).
const DsCase kDsHalf[] = {{16, 8, 32, 16}, {32, 16, 64, 32}, {24, 10, 48, 20}, {16, 8, 33, 17}, {1, 1, 2, 2}};
const DsCase kDsQuarter[] = {{16, 8, 64, 32}, {10, 6, 40, 24}, {16, 4, 66, 18}};
const DsCase kDsThird[] = {{16, 8, 48, 24}, {20, 10, 60, 30}, {16, 7, 50, 22}};
const DsCase kDsGeneral[] = {
  {20, 12, 32, 16}, {17, 9, 40, 24}, {100, 60, 176, 144}, {31, 31, 32, 32}, {8, 4, 100, 50}, {120, 68, 160, 90},
  {1, 1, 16, 16},
};

const char* const kDsNames[] = {"DyadicBilinearDownsampler_c", "DyadicBilinearQuarterDownsampler_c",
                                "DyadicBilinearOneThirdDownsampler_c", "GeneralBilinearFastDownsampler_c",
                                "GeneralBilinearAccurateDownsampler_c"
                               };

struct DsProcessCase {
  int dstW, dstH, srcW, srcH, srcStrideY, srcStrideUV, dstStrideY, dstStrideUV;
};

// CDownsampling::Process: within the sample buffers, repeated halving down to the size or to
// where the general ratio takes over (strides multiples of 32, then of 16, as its asserts want);
// past them (a source more than 2 x 1920 wide or 2 x 1088 high), the one step it picks.
const DsProcessCase kDsProcess[] = {
  {32, 24, 64, 48, 64, 32, 32, 16},          // one halving
  {32, 24, 64, 48, 80, 48, 40, 24},          // one halving, the 16-wide path
  {32, 24, 128, 96, 128, 64, 32, 16},        // two
  {32, 16, 256, 128, 256, 128, 32, 16},      // three
  {33, 25, 67, 51, 96, 64, 64, 32},          // odd sizes
  {120, 72, 320, 192, 320, 160, 128, 64},    // a halving, then the general ratio
  {240, 135, 320, 180, 320, 160, 256, 128},  // the general ratio
  {100, 120, 320, 180, 320, 160, 128, 64},   // the width would halve, the height not
  {48, 32, 48, 64, 64, 32, 64, 32},          // not narrower: invalid
  {24, 1100, 48, 2200, 64, 32, 32, 16},      // past the sample buffers: the halving
  {12, 550, 48, 2200, 64, 32, 32, 16},       // the quarter
  {16, 733, 48, 2200, 64, 32, 32, 16},       // the one third
  {40, 2000, 48, 2200, 64, 32, 64, 32},      // the general ratio
  {1925, 4, 3850, 8, 3872, 1952, 1952, 992}, // past them in width: the halving
  {1283, 2, 3850, 8, 3872, 1952, 1312, 672}, // the one third
};

}  // namespace

void KernelsDownsample() {
  int seed = 1300;
  for (int k = 0; k < 5; k++) {
    const DsCase* cases = k == 0 ? kDsHalf : k == 1 ? kDsQuarter : k == 2 ? kDsThird : kDsGeneral;
    const int n = k == 0 ? 5 : k == 1 ? 3 : k == 2 ? 3 : 7;
    for (int i = 0; i < n; i++, seed++) {
      const DsCase& c = cases[i];
      Rng r (seed);
      // Rows past the source and columns past both pictures hold noise a kernel may read or
      // must not write.
      Plane src, dst;
      src.Make (r, c.srcW, c.srcH + 2, c.srcW + 16);
      dst.Make (r, c.dstW, c.dstH + 2, c.dstW + 8);
      switch (k) {
      case 0:
        WelsVP::DyadicBilinearDownsampler_c (dst.p, dst.stride, src.p, src.stride, c.srcW, c.srcH);
        break;
      case 1:
        WelsVP::DyadicBilinearQuarterDownsampler_c (dst.p, dst.stride, src.p, src.stride, c.srcW, c.srcH);
        break;
      case 2:
        WelsVP::DyadicBilinearOneThirdDownsampler_c (dst.p, dst.stride, src.p, src.stride, c.srcW, c.dstH);
        break;
      case 3:
        WelsVP::GeneralBilinearFastDownsampler_c (dst.p, dst.stride, c.dstW, c.dstH, src.p, src.stride, c.srcW, c.srcH);
        break;
      default:
        WelsVP::GeneralBilinearAccurateDownsampler_c (dst.p, dst.stride, c.dstW, c.dstH, src.p, src.stride, c.srcW, c.srcH);
        break;
      }
      Hasher h;
      dst.HashInto (h);
      Emit ("downsample", kDsNames[k], Fmt ("dw=%d,dh=%d,sw=%d,sh=%d,seed=%d", c.dstW, c.dstH, c.srcW, c.srcH, seed), h);
    }
  }

  seed = 1400;
  for (const DsProcessCase& c : kDsProcess) {
    Rng r (seed);
    Yuv src, dst;
    src.Make (r, YuvSize{c.srcW, c.srcH, c.srcStrideY, c.srcStrideUV});
    dst.Make (r, YuvSize{c.dstW, c.dstH, c.dstStrideY, c.dstStrideUV});
    WelsVP::CDownsampling ds (0);
    SPixMap ms = src.Map(), md = dst.Map();
    Hasher h;
    h.AddI32 (ds.Process (0, &ms, &md));
    dst.HashInto (h);
    Emit ("downsample", "CDownsampling", Fmt ("dw=%d,dh=%d,sw=%d,sh=%d,ss=%d,ssuv=%d,ds=%d,dsuv=%d,seed=%d", c.dstW, c.dstH,
          c.srcW, c.srcH, c.srcStrideY, c.srcStrideUV, c.dstStrideY, c.dstStrideUV, seed), h);
    seed++;
  }
}

namespace {

// The changes Process's CheckValid is given to the source and destination maps (the buffers
// stay as they are). Valid: 0, 3, 9; and, for every method but the downsampling, the checks
// only it makes (14 to 19). 22 and 23 give a zero width with a negative left, which only the
// width check catches (with a left of 0, the left check catches a zero width too).
const int kMutations = 24;

void Mutate (int m, SPixMap& s, SPixMap& d) {
  switch (m) {
  case 1:
    s.eFormat = VIDEO_FORMAT_RGB;
    break;
  case 2:
    s.eFormat = VIDEO_FORMAT_YV12;
    break;
  case 3:
    s.eFormat = d.eFormat = VIDEO_FORMAT_YV12;
    break;
  case 4:
    s.sRect.iRectWidth = 0;
    break;
  case 5:
    s.sRect.iRectHeight = -1;
    break;
  case 6:
    s.sRect.iRectWidth = s.iStride[0] = 4096;
    s.sRect.iRectHeight = 2305;
    break;
  case 7:
    s.sRect.iRectTop = s.sRect.iRectHeight;
    break;
  case 8:
    s.sRect.iRectLeft = s.sRect.iRectWidth;
    break;
  case 9:
    s.sRect.iRectTop = s.sRect.iRectHeight - 1;
    s.sRect.iRectLeft = s.sRect.iRectWidth - 1;
    break;
  case 10:
    s.iStride[0] = s.sRect.iRectWidth - 1;
    break;
  case 11:
    d.sRect.iRectWidth = 0;
    break;
  case 12:
    d.iStride[0] = d.sRect.iRectWidth - 1;
    break;
  case 13:
    d.eFormat = VIDEO_FORMAT_YV12;
    break;
  case 14:
    s.pPixel[1] = NULL;
    break;
  case 15:
    s.iStride[2] = 0;
    break;
  case 16:
    s.iStride[1] = (s.sRect.iRectWidth >> 1) - 1;
    break;
  case 17:
    d.pPixel[2] = NULL;
    break;
  case 18:
    d.iStride[1] = -1;
    break;
  case 19:
    d.iStride[2] = (d.sRect.iRectWidth >> 1) - 1;
    break;
  case 20:
    s.pPixel[0] = NULL;
    break;
  case 21:
    d.pPixel[0] = NULL;
    break;
  case 22:
    s.sRect.iRectWidth = 0;
    s.sRect.iRectLeft = -1;
    break;
  case 23:
    d.sRect.iRectWidth = 0;
    d.sRect.iRectLeft = -1;
    break;
  default:
    break;
  }
}

}  // namespace

void KernelsVpFrameWork() {
  // Process through the framework (WelsCreateVpInterface's IWelsVP) for each mutation: the colour
  // space conversion (no strategy), the denoiser (the source alone), the VAA statistics (source
  // and reference) and the downsampling (to half the size). The downsampling would write through
  // a missing destination or read a missing source, so it skips those two (20 and 21).
  const int kMethods[] = {METHOD_COLORSPACE_CONVERT, METHOD_DENOISE, METHOD_VAA_STATISTICS, METHOD_DOWNSAMPLE};
  int seed = 1500;
  for (int method : kMethods) {
    for (int m = 0; m < kMutations; m++, seed++) {
      if (method == METHOD_DOWNSAMPLE && (m == 20 || m == 21)) continue;
      Rng r (seed);
      Yuv src, ref, half;
      MakeYuvPair (r, YuvSize{32, 32, 64, 32}, 0, src, ref);
      half.Make (r, YuvSize{16, 16, 32, 16});
      VaaOutputs o (r, VaaSize{32, 32, 64});
      SVAACalcResult res = ResultOf (o);
      SVAACalcParam vp;
      memset (&vp, 0, sizeof vp);
      vp.iCalcVar = vp.iCalcBgd = vp.iCalcSsd = 1;
      vp.pCalcResult = &res;
      IWelsVP* pVp = NULL;
      Hasher h;
      h.AddI32 (WelsCreateVpInterface ((void**)&pVp, WELSVP_INTERFACE_VERION));
      h.AddI32 (pVp->Set (METHOD_VAA_STATISTICS, &vp));
      SPixMap s = src.Map(), d = method == METHOD_DOWNSAMPLE ? half.Map() : ref.Map();
      Mutate (m, s, d);
      h.AddI32 (pVp->Process (method, &s, &d));
      src.HashInto (h);
      ref.HashInto (h);
      half.HashInto (h);
      o.iFrameSad = res.iFrameSad;
      h.AddI32 (res.pCurY == src.pl[0].p);
      h.AddI32 (res.pRefY == ref.pl[0].p);
      o.HashInto (h);
      h.AddI32 (WelsDestroyVpInterface (pVp, WELSVP_INTERFACE_VERION));
      Emit ("vpframework", "Process", Fmt ("method=%d,mutation=%d,seed=%d", method, m, seed), h);
    }
  }

  // The rest of the interface on one framework: Init, Uninit, Flush, SpecialFeature, Get and Set
  // for the ported methods (GET_METHOD drops the high bits; 0 clamps to the colour space
  // conversion), then the camera preprocessing's calls in the encoder's order.
  {
    Rng r (1600);
    const YuvSize s = {48, 32, 64, 32};
    Yuv src, ref;
    MakeYuvPair (r, s, 1, src, ref);
    VaaOutputs o (r, VaaSize{s.w, s.h, s.strideY});
    SVAACalcResult res = ResultOf (o);
    IWelsVP* pVp = NULL;
    Hasher h;
    h.AddI32 (WelsCreateVpInterface ((void**)&pVp, 0));
    h.AddI32 (WelsCreateVpInterface ((void**)&pVp, WELSVP_INTERFACE_VERION));
    const int kTypes[] = {0, 1, 2, 3, 5, 6, 7, 8, 9, 0x102, 0x7f06};
    for (int t : kTypes) {
      h.AddI32 (pVp->Init (t, NULL));
      h.AddI32 (pVp->Uninit (t));
      h.AddI32 (pVp->Flush (t));
      h.AddI32 (pVp->SpecialFeature (t, NULL, NULL));
      h.AddI32 (pVp->Get (t, NULL));
      h.AddI32 (pVp->Set (t, NULL));
    }
    // The methods with no state of their own to get or set: IStrategy's Get and Set, which leave
    // the parameter alone (and the colour space conversion, which has no strategy).
    int32_t scratch[16];
    for (int t : {METHOD_COLORSPACE_CONVERT, METHOD_DENOISE, METHOD_DOWNSAMPLE, METHOD_VAA_STATISTICS,
                  METHOD_BACKGROUND_DETECTION}) {
      r.Fill ((uint8_t*)scratch, sizeof scratch);
      h.AddI32 (pVp->Get (t, scratch));
      h.Add (scratch, sizeof scratch);
    }
    for (int t : {METHOD_COLORSPACE_CONVERT, METHOD_DENOISE, METHOD_DOWNSAMPLE}) h.AddI32 (pVp->Set (t, scratch));

    SPixMap ms = src.Map(), mr = ref.Map();
    h.AddI32 (pVp->Process (METHOD_COLORSPACE_CONVERT, &ms, &mr));
    h.AddI32 (pVp->Process (METHOD_DENOISE, &ms, NULL));
    h.AddI32 (pVp->Process (METHOD_SCENE_CHANGE_DETECTION_VIDEO, &ms, &mr));
    SSceneChangeResult scr;
    memset (&scr, 0, sizeof scr);
    h.AddI32 (pVp->Get (METHOD_SCENE_CHANGE_DETECTION_VIDEO, &scr));
    HashScdResult (h, scr, NULL);

    SVAACalcParam vp;
    memset (&vp, 0, sizeof vp);
    vp.iCalcVar = vp.iCalcBgd = vp.iCalcSsd = 1;
    vp.pCalcResult = &res;
    h.AddI32 (pVp->Set (METHOD_VAA_STATISTICS, &vp));
    h.AddI32 (pVp->Process (METHOD_VAA_STATISTICS, &ms, &mr));

    const int mbs = MbCount (s.w, s.h);
    std::vector<uint8_t> flags (mbs + 2);
    r.Fill (flags.data(), flags.size());
    SBGDInterface bi;
    bi.pBackgroundMbFlag = (int8_t*)flags.data();
    bi.pCalcRes = &res;
    h.AddI32 (pVp->Set (METHOD_BACKGROUND_DETECTION, &bi));
    h.AddI32 (pVp->Process (METHOD_BACKGROUND_DETECTION, &ms, &mr));

    std::vector<SMotionTextureUnit> mt (mbs + 2);
    r.Fill ((uint8_t*)mt.data(), mt.size() * sizeof (SMotionTextureUnit));
    std::vector<uint8_t> dqp (mbs + 2);
    r.Fill (dqp.data(), dqp.size());
    SAdaptiveQuantizationParam aqp;
    memset (&aqp, 0, sizeof aqp);
    aqp.iAdaptiveQuantMode = AQ_BITRATE_MODE;
    aqp.pCalcResult = &res;
    aqp.pMotionTextureUnit = mt.data();
    aqp.pMotionTextureIndexToDeltaQp = (int8_t*)dqp.data();
    h.AddI32 (pVp->Set (METHOD_ADAPTIVE_QUANT, &aqp));
    h.AddI32 (pVp->Process (METHOD_ADAPTIVE_QUANT, &ms, &mr));
    SAdaptiveQuantizationParam aqo;
    memset (&aqo, 0, sizeof aqo);
    h.AddI32 (pVp->Get (METHOD_ADAPTIVE_QUANT, &aqo));
    h.AddI32 (aqo.iAverMotionTextureIndexToDeltaQp);

    std::vector<int32_t> gomComplexity (4), gomForeground (4);
    std::vector<uint32_t> refMbType (mbs + 2);
    r.Fill ((uint8_t*)gomComplexity.data(), gomComplexity.size() * 4);
    for (size_t i = 0; i < refMbType.size(); i++) refMbType[i] = kRefMbTypes[r.Next() % 10];
    SComplexityAnalysisParam cp;
    memset (&cp, 0, sizeof cp);
    cp.iComplexityAnalysisMode = GOM_SAD;
    cp.iCalcBgd = 1;
    cp.iMbNumInGom = 3;
    cp.pGomComplexity = gomComplexity.data();
    cp.pGomForegroundBlockNum = gomForeground.data();
    cp.pBackgroundMbFlag = (int8_t*)flags.data();
    cp.uiRefMbType = refMbType.data();
    cp.pCalcResult = &res;
    h.AddI32 (pVp->Set (METHOD_COMPLEXITY_ANALYSIS, &cp));
    h.AddI32 (pVp->Process (METHOD_COMPLEXITY_ANALYSIS, &ms, &mr));
    SComplexityAnalysisParam co;
    memset (&co, 0, sizeof co);
    h.AddI32 (pVp->Get (METHOD_COMPLEXITY_ANALYSIS, &co));
    h.AddI64 (co.iFrameComplexity);

    src.HashInto (h);
    ref.HashInto (h);
    o.iFrameSad = res.iFrameSad;
    o.HashInto (h);
    h.Add (flags.data(), flags.size());
    h.Add (mt.data(), mt.size() * sizeof (SMotionTextureUnit));
    h.Add (dqp.data(), dqp.size());
    for (int32_t v : gomComplexity) h.AddI32 (v);
    for (int32_t v : gomForeground) h.AddI32 (v);
    h.AddI32 (WelsDestroyVpInterface (pVp, WELSVP_INTERFACE_VERION));
    Emit ("vpframework", "Dispatch", "camera", h);
  }
}
