// kernels_mc.cpp: fixtures for the codec/common files ported in C2
// (intra_pred_common, the C paths of mc.cpp). See fixture.h for the format.
// The C# side is test/SIPSorcery.H264.UnitTest/{intra_pred_common,mc}_unittest.cs.
//
// The MC kernels are file-local in mc.cpp, so they are reached through
// InitMcFunc's SMcFunc table, exactly as the decoder and encoder reach them.

#include <string>
#include <vector>

#include "fixture.h"

#include "intra_pred_common.h"
#include "mc.h"

using namespace fixture;

namespace {

// Source: a 40x40 random plane, the block at (8, 8); the 6-tap filters read up
// to 2 samples before and 3 after the block, which stays inside for w, h <= 17.
const int kSrcStride = 40, kSrcRows = 40, kSrcOrigin = 8 * kSrcStride + 8;
// Destination: stride 24 and h + 2 rows, the block at row 1, column 2.
const int kDstStride = 24;

struct McBuffers {
  std::vector<uint8_t> src, dst;
  McBuffers (int h, Rng& r) : src ((size_t)kSrcStride * kSrcRows), dst ((size_t)kDstStride * (h + 2)) {
    r.Fill (src.data(), src.size());
    r.Fill (dst.data(), dst.size());
  }
  uint8_t* Src() { return src.data() + kSrcOrigin; }
  uint8_t* Dst() { return dst.data() + kDstStride + 2; }
};

std::string Case (const char* fmt, int a, int b, int c, int d, int e) {
  char s[128];
  snprintf (s, sizeof s, fmt, a, b, c, d, e);
  return s;
}

const int kBlocks[][2] = {{16, 16}, {16, 8}, {8, 16}, {8, 8}, {8, 4}, {4, 8}, {4, 4}};
const int kChromaBlocks[][2] = {{8, 8}, {8, 4}, {4, 8}, {4, 4}, {4, 2}, {2, 4}, {2, 2}};
const int kHalfpelSizes[] = {4, 5, 8, 9, 16, 17};

}  // namespace

void KernelsIntraPred() {
  typedef void (*PPredFunc) (uint8_t*, uint8_t*, const int32_t);
  struct PredKernel {
    const char* name;
    PPredFunc f;
  };
  const PredKernel kKernels[] = {{"WelsI16x16LumaPredV_c", WelsI16x16LumaPredV_c},
                                 {"WelsI16x16LumaPredH_c", WelsI16x16LumaPredH_c}};
  const int kStrides[] = {32, 21};
  for (const PredKernel& k : kKernels) {
    for (int stride : kStrides) {
      for (int seed = 0; seed < 2; ++seed) {
        // Reference: 18 rows, the block at row 1, column 1 (top row and left column available).
        // Prediction: 16x16 at stride 16 with 16 guard bytes either side.
        Rng r (seed);
        std::vector<uint8_t> ref ((size_t)stride * 18), pred (256 + 32);
        r.Fill (ref.data(), ref.size());
        r.Fill (pred.data(), pred.size());
        k.f (pred.data() + 16, ref.data() + stride + 1, stride);
        Hasher h;
        h.Add (pred.data(), pred.size());
        Emit ("intra_pred", k.name, Case ("stride=%d,seed=%d", stride, seed, 0, 0, 0), h);
      }
    }
  }
}

void KernelsMc() {
  SMcFunc mc;
  WelsCommon::InitMcFunc (&mc, 0);

  // pMcLumaFunc (McLuma_c): every quarter-pel position. iMvX is negative to
  // check the & 0x03 on negative vectors.
  for (const auto& b : kBlocks) {
    int seed = 0;
    for (int dx = 0; dx < 4; ++dx) {
      for (int dy = 0; dy < 4; ++dy, ++seed) {
        const int16_t mvx = (int16_t) (dx - 4), mvy = (int16_t) (dy + 4);
        Rng r (seed);
        McBuffers buf (b[1], r);
        mc.pMcLumaFunc (buf.Src(), kSrcStride, buf.Dst(), kDstStride, mvx, mvy, b[0], b[1]);
        Hasher h;
        h.Add (buf.dst.data(), buf.dst.size());
        Emit ("mc", "McLuma_c", Case ("w=%d,h=%d,mvx=%d,mvy=%d,seed=%d", b[0], b[1], mvx, mvy, seed), h);
      }
    }
  }

  // pMcChromaFunc (McChroma_c): every eighth-pel position.
  for (const auto& b : kChromaBlocks) {
    int seed = 0;
    for (int dx = 0; dx < 8; ++dx) {
      for (int dy = 0; dy < 8; ++dy, ++seed) {
        const int16_t mvx = (int16_t) (dx - 8), mvy = (int16_t) (dy + 8);
        Rng r (seed);
        McBuffers buf (b[1], r);
        mc.pMcChromaFunc (buf.Src(), kSrcStride, buf.Dst(), kDstStride, mvx, mvy, b[0], b[1]);
        Hasher h;
        h.Add (buf.dst.data(), buf.dst.size());
        Emit ("mc", "McChroma_c", Case ("w=%d,h=%d,mvx=%d,mvy=%d,seed=%d", b[0], b[1], mvx, mvy, seed), h);
      }
    }
  }

  // The half-pel filters, at the widths and heights the encoder's ME uses (n and n + 1).
  struct Halfpel {
    const char* name;
    PWelsLumaHalfpelMcFunc f;
  };
  const Halfpel kHalfpel[] = {{"McHorVer20_c", mc.pfLumaHalfpelHor},
                              {"McHorVer02_c", mc.pfLumaHalfpelVer},
                              {"McHorVer22_c", mc.pfLumaHalfpelCen}};
  for (const Halfpel& k : kHalfpel) {
    int seed = 0;
    for (int w : kHalfpelSizes) {
      for (int hgt : kHalfpelSizes) {
        Rng r (seed);
        McBuffers buf (hgt, r);
        k.f (buf.Src(), kSrcStride, buf.Dst(), kDstStride, w, hgt);
        Hasher h;
        h.Add (buf.dst.data(), buf.dst.size());
        Emit ("mc", k.name, Case ("w=%d,h=%d,seed=%d", w, hgt, seed++, 0, 0), h);
      }
    }
  }

  // pfSampleAveraging (PixelAvg_c): A from the source plane, B from a 16-stride buffer.
  {
    int seed = 0;
    for (const auto& b : kBlocks) {
      Rng r (seed);
      McBuffers buf (b[1], r);
      std::vector<uint8_t> srcB (16 * 17);
      r.Fill (srcB.data(), srcB.size());
      mc.pfSampleAveraging (buf.Dst(), kDstStride, buf.Src(), kSrcStride, srcB.data(), 16, b[0], b[1]);
      Hasher h;
      h.Add (buf.dst.data(), buf.dst.size());
      Emit ("mc", "PixelAvg_c", Case ("w=%d,h=%d,seed=%d", b[0], b[1], seed++, 0, 0), h);
    }
  }
}
