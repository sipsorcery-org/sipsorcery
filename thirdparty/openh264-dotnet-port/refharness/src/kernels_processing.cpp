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

#include <cstdarg>
#include <cstdio>
#include <cstring>
#include <string>
#include <vector>

#include "fixture.h"
#include "vaacalculation.h"

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
