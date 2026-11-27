// kernels_decoder.cpp: fixtures for the ported codec/decoder tables and kernels.
//
// "dectable" rows: every decoder table ported so far, byte for byte (C# side:
// test/SIPSorcery.H264.UnitTest/decoder_tables_unittest.cs). Most are static
// const in a header, so this file sees its own copy, which is the same bytes.
//
// "decode_mb_aux" and "get_intra_predictor" rows (D3): the residual IDCTs, the
// 4x4 block offset table and the decoder's intra predictors (C# side:
// decode_mb_aux_unittest.cs, get_intra_predictor_unittest.cs).

#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>
#include <vector>

#include "fixture.h"

#include "dec_golomb.h"
#include "decode_mb_aux.h"
#include "decoder.h"
#include "decoder_context.h"
#include "get_intra_predictor.h"
#include "mb_cache.h"
#include "vlc_decoder.h"
#include "wels_common_basis.h"

using namespace fixture;
using namespace WelsDec;

#define DECTABLE(t)                                                               \
  do {                                                                            \
    Hasher h;                                                                     \
    h.Add (&t, sizeof (t));                                                       \
    Emit ("dectable", #t, "bytes=" + std::to_string (sizeof (t)), h);             \
  } while (0)

void KernelsDecoderTables() {
  // D1: dec_golomb.h, decoder_data_tables.cpp, wels_common_basis.h.
  DECTABLE (g_kuiLeadingZeroTable);
  DECTABLE (g_kuiPrefix8BitsTable);
  DECTABLE (g_kuiZigzagScan);
  DECTABLE (g_kuiZigzagScan8x8);
  DECTABLE (g_kuiIdx2CtxSignificantCoeffFlag8x8);
  DECTABLE (g_kuiIdx2CtxLastSignificantCoeffFlag8x8);
  DECTABLE (g_ksI16PredInfo);
  DECTABLE (g_ksChromaPredInfo);
  DECTABLE (g_ksI4PredInfo);
  DECTABLE (g_kuiI16CbpTable);
  DECTABLE (g_ksInterPMbTypeInfo);
  DECTABLE (g_ksInterBMbTypeInfo);
  DECTABLE (g_ksInterPSubMbTypeInfo);
  DECTABLE (g_ksInterBSubMbTypeInfo);
  DECTABLE (g_ksVuiSampleAspectRatio);

  // D2: the rest of decoder_data_tables.cpp (scan and cache index tables, CBP
  // maps, the CAVLC coeff_token / total_zeros / run_before VLC tables).
  DECTABLE (g_kuiScan4);
  DECTABLE (g_kMbNonZeroCountIdx);
  DECTABLE (g_kCacheNzcScanIdx);
  DECTABLE (g_kCache26ScanIdx);
  DECTABLE (g_kCache30ScanIdx);
  DECTABLE (g_kNonZeroScanIdxC);
  DECTABLE (g_kuiScan8);
  DECTABLE (g_kuiLumaDcZigzagScan);
  DECTABLE (g_kuiChromaDcScan);
  DECTABLE (g_kuiIntra4x4CbpTable);
  DECTABLE (g_kuiIntra4x4CbpTable400);
  DECTABLE (g_kuiInterCbpTable);
  DECTABLE (g_kuiInterCbpTable400);
  DECTABLE (g_kuiVlcChromaTable);
  DECTABLE (g_kuiVlcTable_0);
  DECTABLE (g_kuiVlcTable_0_0);
  DECTABLE (g_kuiVlcTable_0_1);
  DECTABLE (g_kuiVlcTable_0_2);
  DECTABLE (g_kuiVlcTable_0_3);
  DECTABLE (g_kuiVlcTable_1);
  DECTABLE (g_kuiVlcTable_1_0);
  DECTABLE (g_kuiVlcTable_1_1);
  DECTABLE (g_kuiVlcTable_1_2);
  DECTABLE (g_kuiVlcTable_1_3);
  DECTABLE (g_kuiVlcTable_2);
  DECTABLE (g_kuiVlcTable_2_0);
  DECTABLE (g_kuiVlcTable_2_1);
  DECTABLE (g_kuiVlcTable_2_2);
  DECTABLE (g_kuiVlcTable_2_3);
  DECTABLE (g_kuiVlcTable_2_4);
  DECTABLE (g_kuiVlcTable_2_5);
  DECTABLE (g_kuiVlcTable_2_6);
  DECTABLE (g_kuiVlcTable_2_7);
  DECTABLE (g_kuiVlcTable_3);
  DECTABLE (g_kuiVlcTableNeedMoreBitsThread);
  DECTABLE (g_kuiVlcTableMoreBitsCount0);
  DECTABLE (g_kuiVlcTableMoreBitsCount1);
  DECTABLE (g_kuiVlcTableMoreBitsCount2);
  DECTABLE (g_kuiNcMapTable);
  DECTABLE (g_kuiVlcTrailingOneTotalCoeffTable);
  DECTABLE (g_kuiTotalZerosTable0);
  DECTABLE (g_kuiTotalZerosTable1);
  DECTABLE (g_kuiTotalZerosTable2);
  DECTABLE (g_kuiTotalZerosTable3);
  DECTABLE (g_kuiTotalZerosTable4);
  DECTABLE (g_kuiTotalZerosTable5);
  DECTABLE (g_kuiTotalZerosTable6);
  DECTABLE (g_kuiTotalZerosTable7);
  DECTABLE (g_kuiTotalZerosTable8);
  DECTABLE (g_kuiTotalZerosTable9);
  DECTABLE (g_kuiTotalZerosTable10);
  DECTABLE (g_kuiTotalZerosTable11);
  DECTABLE (g_kuiTotalZerosTable12);
  DECTABLE (g_kuiTotalZerosTable13);
  DECTABLE (g_kuiTotalZerosTable14);
  DECTABLE (g_kuiTotalZerosBitNumMap);
  DECTABLE (g_kuiTotalZerosChromaTable0);
  DECTABLE (g_kuiTotalZerosChromaTable1);
  DECTABLE (g_kuiTotalZerosChromaTable2);
  DECTABLE (g_kuiTotalZerosBitNumChromaMap);
  DECTABLE (g_kuiZeroLeftTable0);
  DECTABLE (g_kuiZeroLeftTable1);
  DECTABLE (g_kuiZeroLeftTable2);
  DECTABLE (g_kuiZeroLeftTable3);
  DECTABLE (g_kuiZeroLeftTable4);
  DECTABLE (g_kuiZeroLeftTable5);
  DECTABLE (g_kuiZeroLeftTable6);
  DECTABLE (g_kuiZeroLeftBitNumMap);
}

namespace {

std::string Case (const char* fmt, int a, int b = 0, int c = 0, int d = 0) {
  char s[128];
  snprintf (s, sizeof s, fmt, a, b, c, d);
  return s;
}

// A residual coefficient: uniform in [-range, range], or any int16_t when
// range is 32768 (to reach the int16_t wraps in the transforms).
int16_t Coeff (Rng& r, int range) {
  if (range >= 32768) return (int16_t) (r.Next() >> 16);
  return (int16_t) ((int32_t) (r.Next() % (uint32_t) (2 * range + 1)) - range);
}

}  // namespace

void KernelsDecodeMbAux() {
  // IdctResAddPred_c / IdctResAddPred8x8_c: a random plane of n + 2 rows, the
  // block at row 1, column 2; random coefficients. The hash covers the plane
  // and the coefficients (the kernels must not change them).
  typedef void (*PIdct) (uint8_t*, const int32_t, int16_t*);
  struct IdctKernel {
    const char* name;
    PIdct f;
    int n;
    int strides[3];
  };
  const IdctKernel kIdct[] = {
    {"IdctResAddPred_c", IdctResAddPred_c, 4, {16, 21, 32}},
    {"IdctResAddPred8x8_c", IdctResAddPred8x8_c, 8, {16, 24, 37}},
  };
  const int kRanges[] = {8, 300, 4096, 32768};
  for (const IdctKernel& k : kIdct) {
    for (int stride : k.strides) {
      for (int range : kRanges) {
        for (int seed = 0; seed < 2; ++seed) {
          Rng r (seed);
          std::vector<uint8_t> plane ((size_t)stride * (k.n + 2));
          std::vector<int16_t> rs ((size_t)k.n * k.n);
          r.Fill (plane.data(), plane.size());
          for (int16_t& c : rs) c = Coeff (r, range);
          k.f (plane.data() + stride + 2, stride, rs.data());
          Hasher h;
          h.Add (plane.data(), plane.size());
          h.Add (rs.data(), rs.size() * sizeof (int16_t));
          Emit ("decode_mb_aux", k.name, Case ("stride=%d,range=%d,seed=%d", stride, range, seed), h);
        }
      }
    }
  }

  // IdctFourResAddPred_<IdctResAddPred_c>, file-local in decoder.cpp, reached
  // through InitPredFunc's table. An 8x8 block at row 1, column 2 of a 10-row
  // plane, the generator seeded with nzc. The decimal digits of modes, block 0
  // first, say whether each 4x4 block's coefficients are all zero (0), zero at
  // DC only (1) or random (2); pNzc[0, 1, 4, 5] are the bits of nzc, and the
  // entries the kernel must not read are 7.
  SWelsDecoderContext* pCtx = (SWelsDecoderContext*)calloc (1, sizeof (SWelsDecoderContext));
  InitPredFunc (pCtx, 0);
  const int kFourStride = 24;
  const int kModes[] = {0, 1111, 2222, 120, 2101};
  for (int nzc = 0; nzc < 16; ++nzc) {
    for (int modes : kModes) {
      Rng r (nzc);
      std::vector<uint8_t> plane ((size_t)kFourStride * 10);
      int16_t rs[64];
      r.Fill (plane.data(), plane.size());
      for (int b = 0, div = 1000; b < 4; ++b, div /= 10) {
        const int mode = (modes / div) % 10;
        for (int i = 0; i < 16; ++i) rs[b * 16 + i] = Coeff (r, 300);
        if (mode == 0) memset (&rs[b * 16], 0, 16 * sizeof (int16_t));
        if (mode == 1) rs[b * 16] = 0;
      }
      int8_t pNzc[6] = {(int8_t) (nzc & 1), (int8_t) ((nzc >> 1) & 1), 7, 7, (int8_t) ((nzc >> 2) & 1),
                        (int8_t) ((nzc >> 3) & 1)};
      pCtx->pIdctFourResAddPredFunc (plane.data() + kFourStride + 2, kFourStride, rs, pNzc);
      Hasher h;
      h.Add (plane.data(), plane.size());
      h.Add (rs, sizeof rs);
      Emit ("decode_mb_aux", "IdctFourResAddPred_", Case ("nzc=%d,modes=%d", nzc, modes), h);
    }
  }
  free (pCtx);

  // GetI4LumaIChromaAddrTable: the 24 offsets plus 2 guard entries.
  const int kAddrStrides[][2] = {{16, 8}, {64, 32}, {208, 104}, {1000, 520}};
  for (const auto& s : kAddrStrides) {
    Rng r (0);
    int32_t off[26];
    for (int32_t& v : off) v = (int32_t)r.Next();
    GetI4LumaIChromaAddrTable (off, s[0], s[1]);
    Hasher h;
    for (int32_t v : off) h.AddI32 (v);
    Emit ("decode_mb_aux", "GetI4LumaIChromaAddrTable", Case ("y=%d,uv=%d", s[0], s[1]), h);
  }
}

void KernelsIntraPredDec() {
  // Every predictor writes its n x n block in place from the neighbours above
  // and to the left (and above right). A random plane of n + 2 rows, the block
  // at row 1, column 4; the hash covers the whole plane.
  typedef void (*PPred) (uint8_t*, const int32_t);
  typedef void (*PPred8x8) (uint8_t*, const int32_t, bool, bool);
  struct PredKernel {
    const char* name;
    PPred f;
    int n;
  };
#define K(f, n) {#f, f, n}
  const PredKernel kPred[] = {
    K (WelsI4x4LumaPredV_c, 4),        K (WelsI4x4LumaPredH_c, 4),        K (WelsI4x4LumaPredDc_c, 4),
    K (WelsI4x4LumaPredDcLeft_c, 4),   K (WelsI4x4LumaPredDcTop_c, 4),    K (WelsI4x4LumaPredDcNA_c, 4),
    K (WelsI4x4LumaPredDDL_c, 4),      K (WelsI4x4LumaPredDDLTop_c, 4),   K (WelsI4x4LumaPredDDR_c, 4),
    K (WelsI4x4LumaPredVL_c, 4),       K (WelsI4x4LumaPredVLTop_c, 4),    K (WelsI4x4LumaPredVR_c, 4),
    K (WelsI4x4LumaPredHU_c, 4),       K (WelsI4x4LumaPredHD_c, 4),
    K (WelsIChromaPredV_c, 8),         K (WelsIChromaPredH_c, 8),         K (WelsIChromaPredPlane_c, 8),
    K (WelsIChromaPredDc_c, 8),        K (WelsIChromaPredDcLeft_c, 8),    K (WelsIChromaPredDcTop_c, 8),
    K (WelsIChromaPredDcNA_c, 8),
    K (WelsI16x16LumaPredV_c, 16),     K (WelsI16x16LumaPredH_c, 16),     K (WelsI16x16LumaPredPlane_c, 16),
    K (WelsI16x16LumaPredDc_c, 16),    K (WelsI16x16LumaPredDcTop_c, 16), K (WelsI16x16LumaPredDcLeft_c, 16),
    K (WelsI16x16LumaPredDcNA_c, 16),
  };
#undef K
  for (const PredKernel& k : kPred) {
    // The 4x4 predictors read 8 samples above, the others n: columns 3 .. 11
    // and 3 .. 19 at most.
    const int kStrides[3] = {k.n == 16 ? 24 : 16, k.n == 16 ? 32 : 21, k.n == 16 ? 37 : 32};
    for (int stride : kStrides) {
      for (int seed = 0; seed < 2; ++seed) {
        Rng r (seed);
        std::vector<uint8_t> plane ((size_t)stride * (k.n + 2));
        r.Fill (plane.data(), plane.size());
        k.f (plane.data() + stride + 4, stride);
        Hasher h;
        h.Add (plane.data(), plane.size());
        Emit ("get_intra_predictor", k.name, Case ("stride=%d,seed=%d", stride, seed), h);
      }
    }
  }

  struct Pred8x8Kernel {
    const char* name;
    PPred8x8 f;
  };
#define K(f) {#f, f}
  const Pred8x8Kernel kPred8x8[] = {
    K (WelsI8x8LumaPredV_c),      K (WelsI8x8LumaPredH_c),      K (WelsI8x8LumaPredDc_c),
    K (WelsI8x8LumaPredDcLeft_c), K (WelsI8x8LumaPredDcTop_c),  K (WelsI8x8LumaPredDcNA_c),
    K (WelsI8x8LumaPredDDL_c),    K (WelsI8x8LumaPredDDLTop_c), K (WelsI8x8LumaPredDDR_c),
    K (WelsI8x8LumaPredVL_c),     K (WelsI8x8LumaPredVLTop_c),  K (WelsI8x8LumaPredVR_c),
    K (WelsI8x8LumaPredHU_c),     K (WelsI8x8LumaPredHD_c),
  };
#undef K
  // The 8x8 predictors read 16 samples above: columns 3 .. 19.
  const int k8x8Strides[] = {24, 37, 48};
  for (const Pred8x8Kernel& k : kPred8x8) {
    for (int stride : k8x8Strides) {
      for (int tl = 0; tl < 2; ++tl) {
        for (int tr = 0; tr < 2; ++tr) {
          for (int seed = 0; seed < 2; ++seed) {
            Rng r (seed);
            std::vector<uint8_t> plane ((size_t)stride * 10);
            r.Fill (plane.data(), plane.size());
            k.f (plane.data() + stride + 4, stride, tl != 0, tr != 0);
            Hasher h;
            h.Add (plane.data(), plane.size());
            Emit ("get_intra_predictor", k.name, Case ("stride=%d,tl=%d,tr=%d,seed=%d", stride, tl, tr, seed), h);
          }
        }
      }
    }
  }
}
