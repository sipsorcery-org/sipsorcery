// kernels_common.cpp: fixtures for the codec/common files ported in C1
// (common_tables, macros, copy_mb, expand_pic). See fixture.h for the format.
// The C# side is test/SIPSorcery.H264.UnitTest/{common_tables,macros,copy_mb,expand_pic}_unittest.cs.

#include <cstring>
#include <string>
#include <vector>

#include "fixture.h"

#include "copy_mb.h"
#include "expand_pic.h"
#include "macros.h"
#include "wels_common_defs.h"

using namespace fixture;
using namespace WelsCommon;

namespace {

std::string Case (const char* fmt, int a, int b, int c) {
  char s[96];
  snprintf (s, sizeof s, fmt, a, b, c);
  return s;
}

// A plane with `pad` bytes of border on every side (what expand_pic fills),
// plus 4 guard columns left and right and one guard row above and below.
struct PaddedPlane {
  static const int kGuardCols = 4;
  int w, h, pad, stride, rows;
  std::vector<uint8_t> buf;
  PaddedPlane (int w_, int h_, int pad_, int extraStride, Rng& r)
    : w (w_), h (h_), pad (pad_), stride (w_ + 2 * pad_ + 2 * kGuardCols + extraStride), rows (h_ + 2 * pad_ + 2),
      buf ((size_t)stride * rows) {
    r.Fill (buf.data(), buf.size());
  }
  uint8_t* Origin() { return buf.data() + (size_t) (1 + pad) * stride + kGuardCols + pad; }
};

}  // namespace

#define TABLE(t)                                                                  \
  do {                                                                            \
    Hasher h;                                                                     \
    h.Add (&t, sizeof (t));                                                       \
    Emit ("table", #t, "bytes=" + std::to_string (sizeof (t)), h);                \
  } while (0)

void KernelsTables() {
  TABLE (g_kuiMbCountScan4Idx);
  TABLE (g_kuiCache48CountScan4Idx);
  TABLE (g_kuiMatrixV);
  TABLE (g_kuiCache30ScanIdx);
  TABLE (g_kuiChromaQpTable);
  TABLE (g_keTypeMap);
  TABLE (g_kuiDequantScaling4x4Default);
  TABLE (g_kuiDequantScaling8x8Default);
  TABLE (g_kuiDequantCoeff);
  TABLE (g_kuiDequantCoeff8x8);
  TABLE (g_ksLevelLimits);
  TABLE (g_kuiLevelMaps);
  TABLE (g_kiCabacGlobalContextIdx);
  TABLE (g_kuiCabacRangeLps);
  TABLE (g_kuiStateTransTable);
  TABLE (g_kuiGolombUELength);
}

void KernelsMacros() {
  {
    Hasher h;
    for (int32_t x = -2048; x < 2048; ++x) {
      uint8_t v = WelsClip1 (x);
      h.Add (&v, 1);
    }
    Emit ("macros", "WelsClip1", "x=-2048..2047", h);
  }
  {
    Hasher h;
    for (int32_t i = -4; i <= 70000; ++i) h.AddI32 (CeilLog2 (i));
    Emit ("macros", "CeilLog2", "i=-4..70000", h);
  }
  {
    Hasher h;
    for (uint32_t v = 0; v <= 70000; ++v) h.AddI32 (WELS_LOG2 (v));
    for (int k = 0; k < 32; ++k) {
      h.AddI32 (WELS_LOG2 (1u << k));
      h.AddI32 (WELS_LOG2 ((1u << k) - 1));
      h.AddI32 (WELS_LOG2 (~0u >> k));
    }
    Emit ("macros", "WELS_LOG2", "v=0..70000,pow2", h);
  }
  {
    Rng r (1);
    Hasher h;
    for (int i = 0; i < 4096; ++i) {
      int32_t x = (int16_t) (r.Next() >> 16);
      int32_t y = (int16_t) (r.Next() >> 16);
      int32_t z = (int16_t) (r.Next() >> 16);
      h.AddI32 (WelsMedian (x, y, z));
    }
    Emit ("macros", "WelsMedian", "seed=1,n=4096", h);
  }
  {
    Rng r (1);
    Hasher h;
    for (int i = 0; i < 4096; ++i) {
      int32_t x = (int32_t) (r.Next() >> 11) - (1 << 20);
      int32_t y = (int32_t) (r.Next() & 1023);
      h.AddI32 (WELS_DIV_ROUND (x, y));
    }
    Emit ("macros", "WELS_DIV_ROUND", "seed=1,n=4096", h);
  }
  {
    Rng r (1);
    Hasher h;
    for (int i = 0; i < 4096; ++i) {
      int64_t x = ((int64_t)r.Next() << 16) - (1LL << 47);
      int64_t y = (int64_t) (r.Next() & 0xFFFFF);
      h.AddI64 (WELS_DIV_ROUND64 (x, y));
    }
    Emit ("macros", "WELS_DIV_ROUND64", "seed=1,n=4096", h);
  }
  {
    Rng r (1);
    Hasher h;
    for (int i = 0; i < 4096; ++i) {
      double x = (int32_t)r.Next() / 4096.0;
      h.AddI32 (WELS_ROUND (x));
    }
    Emit ("macros", "WELS_ROUND", "seed=1,n=4096", h);
  }
  {
    Rng r (1);
    Hasher h;
    const int kLens[] = {1, 2, 4};
    for (int len : kLens) {
      for (int count = 0; count <= 40; ++count) {
        alignas (4) uint8_t buf[4 * 48];
        r.Fill (buf, sizeof buf);
        uint32_t value = (count % 5 == 0) ? 0 : r.Next();
        WelsSetMemMultiplebytes_c (buf + 4, value, count, len);
        h.Add (buf, sizeof buf);
      }
    }
    Emit ("macros", "WelsSetMemMultiplebytes_c", "seed=1", h);
  }
  {
    // The CAVLC nC prediction: nA, nB are neighbour non-zero counts, -1 when unavailable.
    Hasher h;
    for (int a = -1; a <= 16; ++a) {
      for (int b = -1; b <= 16; ++b) {
        int8_t nA = (int8_t)a, nB = (int8_t)b, nC;
        WELS_NON_ZERO_COUNT_AVERAGE (nC, nA, nB);
        h.Add (&nC, 1);
      }
    }
    Emit ("macros", "WELS_NON_ZERO_COUNT_AVERAGE", "nA=-1..16,nB=-1..16", h);
  }
}

void KernelsCopyMb() {
  typedef void (*PCopyFunc) (uint8_t*, int32_t, uint8_t*, int32_t);
  struct CopyKernel {
    const char* name;
    PCopyFunc f;
    int w, h;
  };
  const CopyKernel kKernels[] = {
    {"WelsCopy4x4_c", WelsCopy4x4_c, 4, 4},     {"WelsCopy8x4_c", WelsCopy8x4_c, 8, 4},
    {"WelsCopy4x8_c", WelsCopy4x8_c, 4, 8},     {"WelsCopy8x8_c", WelsCopy8x8_c, 8, 8},
    {"WelsCopy8x16_c", WelsCopy8x16_c, 8, 16},  {"WelsCopy16x8_c", WelsCopy16x8_c, 16, 8},
    {"WelsCopy16x16_c", WelsCopy16x16_c, 16, 16},
  };
  const int kStrides[][2] = {{16, 16}, {32, 48}, {23, 17}, {64, 16}};  // dst, src
  for (const CopyKernel& k : kKernels) {
    for (const auto& s : kStrides) {
      for (int seed = 0; seed < 2; ++seed) {
        // Buffers of h+2 rows; the block starts at row 1, column 2.
        Rng r (seed);
        std::vector<uint8_t> src ((size_t)s[1] * (k.h + 2)), dst ((size_t)s[0] * (k.h + 2));
        r.Fill (src.data(), src.size());
        r.Fill (dst.data(), dst.size());
        k.f (dst.data() + s[0] + 2, s[0], src.data() + s[1] + 2, s[1]);
        Hasher h;
        h.Add (dst.data(), dst.size());
        Emit ("copy_mb", k.name, Case ("dst=%d,src=%d,seed=%d", s[0], s[1], seed), h);
      }
    }
  }
}

void KernelsExpandPic() {
  SExpandPicFunc funcs;
  InitExpandPictureFunc (&funcs, 0);

  const int kLuma[][2] = {{16, 16}, {48, 32}, {176, 144}, {20, 6}};
  for (const auto& sz : kLuma) {
    for (int seed = 0; seed < 2; ++seed) {
      Rng r (seed);
      PaddedPlane p (sz[0], sz[1], PADDING_LENGTH, 0, r);
      funcs.pfExpandLumaPicture (p.Origin(), p.stride, p.w, p.h);
      Hasher h;
      h.Add (p.buf.data(), p.buf.size());
      Emit ("expand_pic", "ExpandPictureLuma_c", Case ("w=%d,h=%d,seed=%d", sz[0], sz[1], seed), h);
    }
  }

  const int kChroma[][2] = {{8, 8}, {24, 16}, {88, 72}, {10, 3}};
  for (const auto& sz : kChroma) {
    for (int seed = 0; seed < 2; ++seed) {
      Rng r (seed);
      PaddedPlane p (sz[0], sz[1], CHROMA_PADDING_LENGTH, 0, r);
      funcs.pfExpandChromaPicture[0] (p.Origin(), p.stride, p.w, p.h);
      Hasher h;
      h.Add (p.buf.data(), p.buf.size());
      Emit ("expand_pic", "ExpandPictureChroma_c", Case ("w=%d,h=%d,seed=%d", sz[0], sz[1], seed), h);
    }
  }

  // Chroma widths 8 (< 16, direct C call), 16 (aligned), 24 and 88 (unaligned).
  // V gets a wider stride than U to check each plane uses its own.
  const int kPictures[][2] = {{16, 16}, {32, 32}, {48, 32}, {176, 144}};
  for (const auto& sz : kPictures) {
    for (int seed = 0; seed < 2; ++seed) {
      Rng r (seed);
      PaddedPlane y (sz[0], sz[1], PADDING_LENGTH, 0, r);
      PaddedPlane u (sz[0] / 2, sz[1] / 2, CHROMA_PADDING_LENGTH, 0, r);
      PaddedPlane v (sz[0] / 2, sz[1] / 2, CHROMA_PADDING_LENGTH, 8, r);
      uint8_t* data[3] = {y.Origin(), u.Origin(), v.Origin()};
      int32_t stride[3] = {y.stride, u.stride, v.stride};
      ExpandReferencingPicture (data, sz[0], sz[1], stride, funcs.pfExpandLumaPicture, funcs.pfExpandChromaPicture);
      Hasher h;
      h.Add (y.buf.data(), y.buf.size());
      h.Add (u.buf.data(), u.buf.size());
      h.Add (v.buf.data(), v.buf.size());
      Emit ("expand_pic", "ExpandReferencingPicture", Case ("w=%d,h=%d,seed=%d", sz[0], sz[1], seed), h);
    }
  }

  // PadMB*: every MB of the grid in raster order, as the decoder calls them.
  const int kGrids[][2] = {{1, 1}, {1, 3}, {3, 1}, {2, 2}, {3, 3}, {4, 3}};
  for (int chroma = 0; chroma < 2; ++chroma) {
    const int mbSize = chroma ? 8 : 16;
    const int pad = chroma ? CHROMA_PADDING_LENGTH : PADDING_LENGTH;
    for (const auto& g : kGrids) {
      for (int seed = 0; seed < 2; ++seed) {
        Rng r (seed);
        PaddedPlane p (g[0] * mbSize, g[1] * mbSize, pad, 0, r);
        uint8_t* origin = p.Origin();
        for (int32_t mbY = 0; mbY < g[1]; ++mbY) {
          for (int32_t mbX = 0; mbX < g[0]; ++mbX) {
            if (chroma)
              PadMBChroma_c (origin, p.stride, p.w, p.h, mbX, mbY, g[0], g[1]);
            else
              PadMBLuma_c (origin, p.stride, p.w, p.h, mbX, mbY, g[0], g[1]);
          }
        }
        Hasher h;
        h.Add (p.buf.data(), p.buf.size());
        Emit ("expand_pic", chroma ? "PadMBChroma_c" : "PadMBLuma_c", Case ("mbw=%d,mbh=%d,seed=%d", g[0], g[1], seed), h);
      }
    }
  }
}
