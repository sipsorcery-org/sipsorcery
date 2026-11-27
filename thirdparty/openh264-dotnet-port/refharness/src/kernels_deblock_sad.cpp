// kernels_deblock_sad.cpp: fixtures for the codec/common files ported in C3
// (deblocking_common, sad_common). See fixture.h for the format. The C# side
// is test/SIPSorcery.H264.UnitTest/{deblocking_common,sad_common}_unittest.cs.

#include <cstdio>
#include <string>
#include <vector>

#include "fixture.h"

#include "deblocking_common.h"
#include "sad_common.h"

using namespace fixture;

namespace {

// Deblocking. Random bytes would almost never pass the |p0 - q0| < alpha,
// |p1 - p0| < beta tests, so the planes are "smooth": base plus noise in
// [-spread, spread], clamped to 0..255. The edge sits at (row 4, column 4) of
// a 24-row plane of stride 28, which leaves room for the 4 samples each side
// that the bS == 4 filters read, across either edge direction.
const int kDbStride = 28, kDbRows = 24, kDbOrigin = 4 * kDbStride + 4;

void FillSmooth (Rng& r, std::vector<uint8_t>& p, int base, int spread) {
  for (size_t i = 0; i < p.size(); ++i) {
    int v = base + (int) (r.Next() % (uint32_t) (2 * spread + 1)) - spread;
    p[i] = (uint8_t) (v < 0 ? 0 : (v > 255 ? 255 : v));
  }
}

// Alpha and beta span the table range (alpha up to 255, beta up to 18).
const int kAlphaBeta[][2] = {{17, 4}, {63, 12}, {255, 18}};
// Base and spread: mid-grey at three noise levels, plus four at the ends of
// the range. The filters only clip (WelsClip1) when p0 or q0 sits within a few
// levels of 0 or 255 and tc0 is large, so the last two put half the samples
// at exactly 0 or 255.
const int kSmooth[][2] = {{128, 3}, {128, 10}, {128, 30}, {4, 10}, {251, 10}, {0, 16}, {255, 16}};
// tc0 per 4-sample group (luma) or 2-sample group (chroma). -1 is "no filter"
// for luma; chroma skips anything <= 0.
const int8_t kTc[][4] = {{-1, 0, 2, 5}, {1, 4, 9, 25}};

enum DbKind { kLuma, kChroma, kChroma2 };

struct DbKernel {
  const char* name;
  DbKind kind;
  bool lt4;
  void* f;
};

void RunDeblock (const DbKernel& k) {
  int seed = 0;
  const int tcSets = k.lt4 ? 2 : 1;
  for (int t = 0; t < tcSets; ++t) {
    for (const auto& ab : kAlphaBeta) {
      for (const auto& sm : kSmooth) {
        Rng r (seed);
        std::vector<uint8_t> a ((size_t)kDbStride * kDbRows), b;
        FillSmooth (r, a, sm[0], sm[1]);
        if (k.kind == kChroma) {
          b.resize (a.size());
          FillSmooth (r, b, sm[0], sm[1]);
        }
        int8_t tc[4] = {kTc[t][0], kTc[t][1], kTc[t][2], kTc[t][3]};
        uint8_t* pa = a.data() + kDbOrigin;
        if (k.kind == kChroma) {
          uint8_t* pb = b.data() + kDbOrigin;
          if (k.lt4)
            ((void (*) (uint8_t*, uint8_t*, int32_t, int32_t, int32_t, int8_t*))k.f) (pa, pb, kDbStride, ab[0], ab[1], tc);
          else
            ((void (*) (uint8_t*, uint8_t*, int32_t, int32_t, int32_t))k.f) (pa, pb, kDbStride, ab[0], ab[1]);
        } else {
          if (k.lt4)
            ((void (*) (uint8_t*, int32_t, int32_t, int32_t, int8_t*))k.f) (pa, kDbStride, ab[0], ab[1], tc);
          else
            ((void (*) (uint8_t*, int32_t, int32_t, int32_t))k.f) (pa, kDbStride, ab[0], ab[1]);
        }
        Hasher h;
        h.Add (a.data(), a.size());
        if (k.kind == kChroma) h.Add (b.data(), b.size());
        char kase[128];
        if (k.lt4)
          snprintf (kase, sizeof kase, "alpha=%d,beta=%d,base=%d,spread=%d,tc=%d,seed=%d", ab[0], ab[1], sm[0], sm[1], t, seed);
        else
          snprintf (kase, sizeof kase, "alpha=%d,beta=%d,base=%d,spread=%d,seed=%d", ab[0], ab[1], sm[0], sm[1], seed);
        Emit ("deblock", k.name, kase, h);
        ++seed;
      }
    }
  }
}

// SAD. Both planes have h + 4 rows with the block at (row 2, column 2), so the
// "four" variants can read one sample up, down, left and right of it.
const int kSadStrides[][2] = {{24, 24}, {21, 37}};

}  // namespace

void KernelsDeblock() {
  const DbKernel kKernels[] = {
    {"DeblockLumaLt4V_c", kLuma, true, (void*)DeblockLumaLt4V_c},
    {"DeblockLumaLt4H_c", kLuma, true, (void*)DeblockLumaLt4H_c},
    {"DeblockLumaEq4V_c", kLuma, false, (void*)DeblockLumaEq4V_c},
    {"DeblockLumaEq4H_c", kLuma, false, (void*)DeblockLumaEq4H_c},
    {"DeblockChromaLt4V_c", kChroma, true, (void*)DeblockChromaLt4V_c},
    {"DeblockChromaLt4H_c", kChroma, true, (void*)DeblockChromaLt4H_c},
    {"DeblockChromaEq4V_c", kChroma, false, (void*)DeblockChromaEq4V_c},
    {"DeblockChromaEq4H_c", kChroma, false, (void*)DeblockChromaEq4H_c},
    {"DeblockChromaLt4V2_c", kChroma2, true, (void*)DeblockChromaLt4V2_c},
    {"DeblockChromaLt4H2_c", kChroma2, true, (void*)DeblockChromaLt4H2_c},
    {"DeblockChromaEq4V2_c", kChroma2, false, (void*)DeblockChromaEq4V2_c},
    {"DeblockChromaEq4H2_c", kChroma2, false, (void*)DeblockChromaEq4H2_c},
  };
  for (const DbKernel& k : kKernels) RunDeblock (k);

  // WelsNonZeroCount_c: 24 counts at offset 4 of a 32-byte buffer, about half
  // of them zero.
  for (int seed = 0; seed < 4; ++seed) {
    Rng r (seed);
    int8_t nzc[32];
    for (int i = 0; i < 32; ++i) {
      uint8_t v = r.NextByte();
      nzc[i] = (v & 1) ? (int8_t)v : 0;
    }
    WelsNonZeroCount_c (nzc + 4);
    Hasher h;
    h.Add (nzc, sizeof nzc);
    char kase[32];
    snprintf (kase, sizeof kase, "seed=%d", seed);
    Emit ("deblock", "WelsNonZeroCount_c", kase, h);
  }
}

void KernelsSad() {
  typedef int32_t (*PSad) (uint8_t*, int32_t, uint8_t*, int32_t);
  typedef void (*PSadFour) (uint8_t*, int32_t, uint8_t*, int32_t, int32_t*);
  struct SadKernel {
    const char* name;
    int w, h;
    PSad sad;
    PSadFour four;
  };
  const SadKernel kKernels[] = {
    {"WelsSampleSad16x16_c", 16, 16, WelsSampleSad16x16_c, nullptr},
    {"WelsSampleSad16x8_c", 16, 8, WelsSampleSad16x8_c, nullptr},
    {"WelsSampleSad8x16_c", 8, 16, WelsSampleSad8x16_c, nullptr},
    {"WelsSampleSad8x8_c", 8, 8, WelsSampleSad8x8_c, nullptr},
    {"WelsSampleSad8x4_c", 8, 4, WelsSampleSad8x4_c, nullptr},
    {"WelsSampleSad4x8_c", 4, 8, WelsSampleSad4x8_c, nullptr},
    {"WelsSampleSad4x4_c", 4, 4, WelsSampleSad4x4_c, nullptr},
    {"WelsSampleSadFour16x16_c", 16, 16, nullptr, WelsSampleSadFour16x16_c},
    {"WelsSampleSadFour16x8_c", 16, 8, nullptr, WelsSampleSadFour16x8_c},
    {"WelsSampleSadFour8x16_c", 8, 16, nullptr, WelsSampleSadFour8x16_c},
    {"WelsSampleSadFour8x8_c", 8, 8, nullptr, WelsSampleSadFour8x8_c},
    {"WelsSampleSadFour8x4_c", 8, 4, nullptr, WelsSampleSadFour8x4_c},
    {"WelsSampleSadFour4x8_c", 4, 8, nullptr, WelsSampleSadFour4x8_c},
    {"WelsSampleSadFour4x4_c", 4, 4, nullptr, WelsSampleSadFour4x4_c},
  };
  for (const SadKernel& k : kKernels) {
    int seed = 0;
    for (const auto& s : kSadStrides) {
      for (int rep = 0; rep < 2; ++rep, ++seed) {
        Rng r (seed);
        std::vector<uint8_t> a ((size_t)s[0] * (k.h + 4)), b ((size_t)s[1] * (k.h + 4));
        r.Fill (a.data(), a.size());
        r.Fill (b.data(), b.size());
        uint8_t* pa = a.data() + 2 * s[0] + 2;
        uint8_t* pb = b.data() + 2 * s[1] + 2;
        Hasher h;
        if (k.sad) {
          h.AddI32 (k.sad (pa, s[0], pb, s[1]));
        } else {
          int32_t sad[4] = {-1, -1, -1, -1};
          k.four (pa, s[0], pb, s[1], sad);
          for (int32_t v : sad) h.AddI32 (v);
        }
        char kase[64];
        snprintf (kase, sizeof kase, "stride1=%d,stride2=%d,seed=%d", s[0], s[1], seed);
        Emit ("sad", k.name, kase, h);
      }
    }
  }
}
