// kernels_encoder.cpp: fixtures for the ported codec/encoder tables and kernels (E2).
//
// The encoder's headers share names with the decoder's (decode_mb_aux.h,
// get_intra_predictor.h, wels_const.h, ...), so this file is compiled on its
// own, with only the encoder include paths (the h264ref_enc_kernels object
// library in CMakeLists.txt).
//
// C# side, one test class per group (test/SIPSorcery.H264.UnitTest):
//   "enctable"                every encoder table ported so far, byte for byte
//                             (encoder_tables_unittest.cs).
//   "encode_mb_aux"           quantisation, the forward DCT and Hadamard
//                             transforms, the scans, the single-coefficient cost
//                             and the non-zero count (encode_mb_aux_unittest.cs).
//   "enc_decode_mb_aux"       the encoder's dequantisation, inverse Hadamard
//                             and IDCT reconstruction (enc_decode_mb_aux_unittest.cs).
//   "enc_get_intra_predictor" the encoder's intra predictors, which write a
//                             packed prediction (enc_get_intra_predictor_unittest.cs).
//   "sample"                  SATD and the combined predict-and-cost kernels
//                             (sample_unittest.cs).
//   "enc_init"                every slot of the indexed tables WelsInit*Funcs
//                             fill, called on a fixture (enc_init_unittest.cs).
//
// Coefficient buffers carry kGuard int16_t entries either side of the data,
// drawn from the generator with the data, and the hash covers the whole
// buffer, so a write outside the block fails.

#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>
#include <vector>

#include "fixture.h"

#include "wels_common_defs.h"
#include "decode_mb_aux.h"
#include "encode_mb_aux.h"
#include "get_intra_predictor.h"
#include "intra_pred_common.h"
#include "mb_cache.h"
#include "md.h"
#include "param_svc.h"
#include "sad_common.h"
#include "sample.h"
#include "vlc_encoder.h"
#include "wels_func_ptr_def.h"

namespace WelsEnc {
// Defined in sample.cpp, but sample.h leaves their declarations commented out.
int32_t WelsSampleSatd8x4_c (uint8_t*, int32_t, uint8_t*, int32_t);
int32_t WelsSampleSatd4x8_c (uint8_t*, int32_t, uint8_t*, int32_t);
}  // namespace WelsEnc

using namespace fixture;
using namespace WelsEnc;

#define ENCTABLE(t)                                                               \
  do {                                                                            \
    Hasher h;                                                                     \
    h.Add (&t, sizeof (t));                                                       \
    Emit ("enctable", #t, "bytes=" + std::to_string (sizeof (t)), h);             \
  } while (0)

void KernelsEncoderTables() {
  // encode_mb_aux.cpp.
  ENCTABLE (g_kiQuantInterFF);
  ENCTABLE (g_kiQuantMF);
  // encoder_data_tables.cpp.
  ENCTABLE (g_kuiSmb4AddrIn256);
  ENCTABLE (g_kuiCache12_8x8RefIdx);
  ENCTABLE (g_kiQpCostTable);
  ENCTABLE (g_kiMapModeI16x16);
  ENCTABLE (g_kiMapModeIntraChroma);
  ENCTABLE (g_kuiVlcCoeffToken);
  ENCTABLE (g_kuiVlcTotalZeros);
  ENCTABLE (g_kuiVlcTotalZerosChromaDc);
  ENCTABLE (g_kuiVlcRunBefore);
  ENCTABLE (g_kuiEncNcMapTable);
  ENCTABLE (g_kuiTemporalIdListTable);
}

namespace {

const int kGuard = 4;

std::string Case (const char* fmt, int a, int b = 0, int c = 0, int d = 0, int e = 0) {
  char s[160];
  snprintf (s, sizeof s, fmt, a, b, c, d, e);
  return s;
}

// A coefficient: uniform in [-range, range], or any int16_t when range is
// 32768 (to reach the int16_t wraps). Same as kernels_decoder.cpp.
int16_t Coeff (Rng& r, int range) {
  if (range >= 32768) return (int16_t) (r.Next() >> 16);
  return (int16_t) ((int32_t) (r.Next() % (uint32_t) (2 * range + 1)) - range);
}

// n coefficients with kGuard entries either side, all drawn with Coeff.
std::vector<int16_t> CoeffBuf (Rng& r, int n, int range) {
  std::vector<int16_t> v ((size_t)n + 2 * kGuard);
  for (int16_t& c : v) c = Coeff (r, range);
  return v;
}

void AddBuf (Hasher& h, const std::vector<int16_t>& v) {
  h.Add (v.data(), v.size() * sizeof (int16_t));
}
void AddBuf (Hasher& h, const std::vector<uint8_t>& v) {
  h.Add (v.data(), v.size());
}

std::vector<uint8_t> Plane (Rng& r, size_t n) {
  std::vector<uint8_t> v (n);
  r.Fill (v.data(), n);
  return v;
}

// Emit for a hash already taken (PredHash).
void EmitHex (const char* group, const char* kernel, const std::string& kase, const std::string& hex) {
  printf ("%s\t%s\t%s\t%s\n", group, kernel, kase.c_str(), hex.c_str());
}

const int kQps[] = {0, 12, 26, 39, 51};
const int kRanges[] = {8, 300, 4096, 32768};

}  // namespace

void KernelsEncodeMbAux() {
  // The quantisers, with the real FF/MF rows (intra FF is g_kiQuantInterFF + 6),
  // the DC and 2x2 variants with the (FF << 1, MF >> 1) the encoder passes them.
  typedef void (*PQuant) (int16_t*, const int16_t*, const int16_t*);
  struct QuantKernel {
    const char* name;
    PQuant f;
    int n;
  };
  const QuantKernel kQuant[] = {
    {"WelsQuant4x4_c", WelsQuant4x4_c, 16},
    {"WelsQuantFour4x4_c", WelsQuantFour4x4_c, 64},
  };
  for (int qp : kQps) {
    for (int intra = 0; intra < 2; ++intra) {
      const int16_t* pFF = intra ? g_iQuantIntraFF[qp] : g_kiQuantInterFF[qp];
      const int16_t* pMF = g_kiQuantMF[qp];
      const int16_t iFF = (int16_t) (pFF[0] << 1);
      const int16_t iMF = (int16_t) (pMF[0] >> 1);
      for (int range : kRanges) {
        for (int seed = 0; seed < 2; ++seed) {
          const std::string kase = Case ("qp=%d,intra=%d,range=%d,seed=%d", qp, intra, range, seed);
          for (const QuantKernel& k : kQuant) {
            Rng r (seed);
            std::vector<int16_t> dct = CoeffBuf (r, k.n, range);
            k.f (dct.data() + kGuard, pFF, pMF);
            Hasher h;
            AddBuf (h, dct);
            Emit ("encode_mb_aux", k.name, kase, h);
          }
          {
            Rng r (seed);
            std::vector<int16_t> dct = CoeffBuf (r, 64, range);
            std::vector<int16_t> max = CoeffBuf (r, 4, 32768);
            WelsQuantFour4x4Max_c (dct.data() + kGuard, pFF, pMF, max.data() + kGuard);
            Hasher h;
            AddBuf (h, dct);
            AddBuf (h, max);
            Emit ("encode_mb_aux", "WelsQuantFour4x4Max_c", kase, h);
          }
          {
            Rng r (seed);
            std::vector<int16_t> dct = CoeffBuf (r, 16, range);
            WelsQuant4x4Dc_c (dct.data() + kGuard, iFF, iMF);
            Hasher h;
            AddBuf (h, dct);
            Emit ("encode_mb_aux", "WelsQuant4x4Dc_c", kase, h);
          }
          {
            // Reads pRs[0, 16, 32, 48]; the small ranges at high qp skip.
            Rng r (seed);
            std::vector<int16_t> rs = CoeffBuf (r, 64, range);
            int32_t ret = WelsHadamardQuant2x2Skip_c (rs.data() + kGuard, iFF, iMF);
            Hasher h;
            h.AddI32 (ret);
            AddBuf (h, rs);
            Emit ("encode_mb_aux", "WelsHadamardQuant2x2Skip_c", kase, h);
          }
          {
            Rng r (seed);
            std::vector<int16_t> rs = CoeffBuf (r, 64, range);
            std::vector<int16_t> dct = CoeffBuf (r, 4, 32768);
            std::vector<int16_t> block = CoeffBuf (r, 4, 32768);
            int32_t ret = WelsHadamardQuant2x2_c (rs.data() + kGuard, iFF, iMF, dct.data() + kGuard, block.data() + kGuard);
            Hasher h;
            h.AddI32 (ret);
            AddBuf (h, rs);
            AddBuf (h, dct);
            AddBuf (h, block);
            Emit ("encode_mb_aux", "WelsHadamardQuant2x2_c", kase, h);
          }
        }
      }
    }
  }

  // WelsHadamardQuant2x2Skip_c at its threshold: pRs[0] = threshold + delta
  // and pRs[16, 32, 48] = 0 make all four Hadamard outputs threshold + delta,
  // so delta 0 decides the `>`.
  for (int qp : kQps) {
    for (int intra = 0; intra < 2; ++intra) {
      const int16_t* pFF = intra ? g_iQuantIntraFF[qp] : g_kiQuantInterFF[qp];
      const int16_t iFF = (int16_t) (pFF[0] << 1);
      const int16_t iMF = (int16_t) (g_kiQuantMF[qp][0] >> 1);
      const int16_t iThreshold = (int16_t) (((1 << 16) - 1) / iMF - iFF);
      for (int delta = -1; delta <= 1; ++delta) {
        Rng r (0);
        std::vector<int16_t> rs = CoeffBuf (r, 64, 300);
        rs[kGuard] = (int16_t) (iThreshold + delta);
        rs[kGuard + 16] = rs[kGuard + 32] = rs[kGuard + 48] = 0;
        int32_t ret = WelsHadamardQuant2x2Skip_c (rs.data() + kGuard, iFF, iMF);
        Hasher h;
        h.AddI32 (ret);
        AddBuf (h, rs);
        Emit ("encode_mb_aux", "WelsHadamardQuant2x2Skip_c", Case ("qp=%d,intra=%d,delta=%d", qp, intra, delta), h);
      }
    }
  }

  // WelsHadamardT4Dc_c reads the DC of each of the 16 4x4 blocks of an MB's
  // 256 coefficients; at range 32768 the eight seeds reach both ends of all
  // four of its clips.
  for (int range : kRanges) {
    for (int seed = 0; seed < 8; ++seed) {
      Rng r (seed);
      std::vector<int16_t> dct = CoeffBuf (r, 256, range);
      std::vector<int16_t> dc = CoeffBuf (r, 16, 32768);
      WelsHadamardT4Dc_c (dc.data() + kGuard, dct.data() + kGuard);
      Hasher h;
      AddBuf (h, dc);
      AddBuf (h, dct);
      Emit ("encode_mb_aux", "WelsHadamardT4Dc_c", Case ("range=%d,seed=%d", range, seed), h);
    }
  }

  // The forward DCTs: random source and prediction planes, the block at their
  // origin.
  typedef void (*PDct) (int16_t*, uint8_t*, int32_t, uint8_t*, int32_t);
  struct DctKernel {
    const char* name;
    PDct f;
    int n;
    int strides[4][2];
  };
  const DctKernel kDct[] = {
    {"WelsDctT4_c", WelsDctT4_c, 4, {{4, 4}, {16, 16}, {21, 8}, {32, 16}}},
    {"WelsDctFourT4_c", WelsDctFourT4_c, 8, {{8, 8}, {16, 16}, {24, 8}, {37, 16}}},
  };
  for (const DctKernel& k : kDct) {
    for (const auto& s : k.strides) {
      for (int seed = 0; seed < 3; ++seed) {
        Rng r (seed);
        std::vector<uint8_t> p1 = Plane (r, (size_t)s[0] * k.n);
        std::vector<uint8_t> p2 = Plane (r, (size_t)s[1] * k.n);
        std::vector<int16_t> dct = CoeffBuf (r, k.n * k.n, 32768);
        k.f (dct.data() + kGuard, p1.data(), s[0], p2.data(), s[1]);
        Hasher h;
        AddBuf (h, dct);
        AddBuf (h, p1);
        AddBuf (h, p2);
        Emit ("encode_mb_aux", k.name, Case ("s1=%d,s2=%d,seed=%d", s[0], s[1], seed), h);
      }
    }
  }

  // The zigzag scans.
  typedef void (*PScan) (int16_t*, int16_t*);
  struct ScanKernel {
    const char* name;
    PScan f;
  };
  const ScanKernel kScan[] = {
    {"WelsScan4x4DcAc_c", WelsScan4x4DcAc_c},
    {"WelsScan4x4Ac_c", WelsScan4x4Ac_c},
    {"WelsScan4x4Dc", WelsScan4x4Dc},
  };
  for (const ScanKernel& k : kScan) {
    for (int seed = 0; seed < 4; ++seed) {
      Rng r (seed);
      std::vector<int16_t> level = CoeffBuf (r, 16, 32768);
      std::vector<int16_t> dct = CoeffBuf (r, 16, 32768);
      k.f (level.data() + kGuard, dct.data() + kGuard);
      Hasher h;
      AddBuf (h, level);
      AddBuf (h, dct);
      Emit ("encode_mb_aux", k.name, Case ("seed=%d", seed), h);
    }
  }

  // The single-coefficient cost and the non-zero count, on sparse blocks: each
  // coefficient is kept with probability density/16.
  typedef int32_t (*PCount) (int16_t*);
  struct CountKernel {
    const char* name;
    PCount f;
  };
  const CountKernel kCount[] = {
    {"WelsCalculateSingleCtr4x4_c", WelsCalculateSingleCtr4x4_c},
    {"WelsGetNoneZeroCount_c", WelsGetNoneZeroCount_c},
  };
  const int kDensities[] = {0, 1, 2, 4, 8, 12, 15, 16};
  for (const CountKernel& k : kCount) {
    for (int density : kDensities) {
      for (int seed = 0; seed < 4; ++seed) {
        Rng r (seed);
        std::vector<int16_t> dct = CoeffBuf (r, 16, 300);
        for (int i = 0; i < 16; ++i) {
          if ((int) (r.Next() % 16) >= density) dct[kGuard + i] = 0;
        }
        int32_t ret = k.f (dct.data() + kGuard);
        Hasher h;
        h.AddI32 (ret);
        AddBuf (h, dct);
        Emit ("encode_mb_aux", k.name, Case ("density=%d,seed=%d", density, seed), h);
      }
    }
  }
}

void KernelsEncDecodeMbAux() {
  // The inverse Hadamard transforms and dequantisation, in place.
  for (int range : kRanges) {
    for (int seed = 0; seed < 2; ++seed) {
      Rng r (seed);
      std::vector<int16_t> rs = CoeffBuf (r, 16, range);
      WelsIHadamard4x4Dc (rs.data() + kGuard);
      Hasher h;
      AddBuf (h, rs);
      Emit ("enc_decode_mb_aux", "WelsIHadamard4x4Dc", Case ("range=%d,seed=%d", range, seed), h);
    }
  }
  // WelsDequantLumaDc4x4 is the qp < 12 path.
  for (int qp = 0; qp < 12; ++qp) {
    for (int range : kRanges) {
      Rng r (qp);
      std::vector<int16_t> rs = CoeffBuf (r, 16, range);
      WelsDequantLumaDc4x4 (rs.data() + kGuard, qp);
      Hasher h;
      AddBuf (h, rs);
      Emit ("enc_decode_mb_aux", "WelsDequantLumaDc4x4", Case ("qp=%d,range=%d", qp, range), h);
    }
  }
  // The MF the encoder passes: g_kuiDequantCoeff[qp][0] >> 2 for the 4x4
  // Hadamard (qp >= 12), g_kuiDequantCoeff[qp][0] for the 2x2, the whole row
  // for the 4x4 dequantisers.
  typedef void (*PDequant) (int16_t*, const uint16_t*);
  struct DequantKernel {
    const char* name;
    PDequant f;
    int n;
  };
  const DequantKernel kDequant[] = {
    {"WelsDequant4x4_c", WelsDequant4x4_c, 16},
    {"WelsDequantFour4x4_c", WelsDequantFour4x4_c, 64},
  };
  for (int qp : kQps) {
    for (int range : kRanges) {
      for (int seed = 0; seed < 2; ++seed) {
        const std::string kase = Case ("qp=%d,range=%d,seed=%d", qp, range, seed);
        if (qp >= 12) {
          Rng r (seed);
          std::vector<int16_t> rs = CoeffBuf (r, 16, range);
          WelsDequantIHadamard4x4_c (rs.data() + kGuard, (uint16_t) (g_kuiDequantCoeff[qp][0] >> 2));
          Hasher h;
          AddBuf (h, rs);
          Emit ("enc_decode_mb_aux", "WelsDequantIHadamard4x4_c", kase, h);
        }
        {
          Rng r (seed);
          std::vector<int16_t> dct = CoeffBuf (r, 4, range);
          WelsDequantIHadamard2x2Dc (dct.data() + kGuard, g_kuiDequantCoeff[qp][0]);
          Hasher h;
          AddBuf (h, dct);
          Emit ("enc_decode_mb_aux", "WelsDequantIHadamard2x2Dc", kase, h);
        }
        for (const DequantKernel& k : kDequant) {
          Rng r (seed);
          std::vector<int16_t> rs = CoeffBuf (r, k.n, range);
          k.f (rs.data() + kGuard, g_kuiDequantCoeff[qp]);
          Hasher h;
          AddBuf (h, rs);
          Emit ("enc_decode_mb_aux", k.name, kase, h);
        }
      }
    }
  }

  // The reconstructions: a random plane of n + 2 rows, the block at row 1,
  // column 2; the prediction at the origin of its own plane of n rows.
  typedef void (*PRec) (uint8_t*, int32_t, uint8_t*, int32_t, int16_t*);
  struct RecKernel {
    const char* name;
    PRec f;  // NULL: WelsIDctT4RecOnMb with WelsIDctFourT4Rec_c
    int n;
    int coeffs;
    int strides[3];
    int predStrides[2];
  };
  const RecKernel kRec[] = {
    {"WelsIDctT4Rec_c", WelsIDctT4Rec_c, 4, 16, {16, 21, 32}, {4, 16}},
    {"WelsIDctFourT4Rec_c", WelsIDctFourT4Rec_c, 8, 64, {16, 24, 37}, {8, 16}},
    {"WelsIDctT4RecOnMb", NULL, 16, 256, {24, 32, 37}, {16, 24}},
    {"WelsIDctRecI16x16Dc_c", WelsIDctRecI16x16Dc_c, 16, 16, {24, 32, 37}, {16, 24}},
  };
  for (const RecKernel& k : kRec) {
    for (int stride : k.strides) {
      for (int predStride : k.predStrides) {
        for (int range : kRanges) {
          for (int seed = 0; seed < 2; ++seed) {
            Rng r (seed);
            std::vector<uint8_t> rec = Plane (r, (size_t)stride * (k.n + 2));
            std::vector<uint8_t> pred = Plane (r, (size_t)predStride * k.n);
            std::vector<int16_t> dct = CoeffBuf (r, k.coeffs, range);
            if (k.f)
              k.f (rec.data() + stride + 2, stride, pred.data(), predStride, dct.data() + kGuard);
            else
              WelsIDctT4RecOnMb (rec.data() + stride + 2, stride, pred.data(), predStride, dct.data() + kGuard,
                                 WelsIDctFourT4Rec_c);
            Hasher h;
            AddBuf (h, rec);
            AddBuf (h, pred);
            AddBuf (h, dct);
            Emit ("enc_decode_mb_aux", k.name,
                  Case ("stride=%d,pred=%d,range=%d,seed=%d", stride, predStride, range, seed), h);
          }
        }
      }
    }
  }

  // WelsGetEncBlockStrideOffset: the 24 offsets plus 2 guard entries.
  const int kAddrStrides[][2] = {{16, 8}, {64, 32}, {208, 104}, {1000, 520}};
  for (const auto& s : kAddrStrides) {
    Rng r (0);
    int32_t off[26];
    for (int32_t& v : off) v = (int32_t)r.Next();
    WelsGetEncBlockStrideOffset (off, s[0], s[1]);
    Hasher h;
    for (int32_t v : off) h.AddI32 (v);
    Emit ("enc_decode_mb_aux", "WelsGetEncBlockStrideOffset", Case ("y=%d,uv=%d", s[0], s[1]), h);
  }
}

namespace {

// An encoder predictor's fixture: a random plane of n + 2 rows, the
// neighbours' block at row 1, column 4; the packed n x n prediction with 16
// guard bytes either side. The hash covers the plane and the prediction.
std::string PredHash (PGetIntraPredFunc f, int n, int stride, int seed) {
  Rng r (seed);
  std::vector<uint8_t> plane = Plane (r, (size_t)stride * (n + 2));
  std::vector<uint8_t> pred = Plane (r, (size_t)n * n + 32);
  f (pred.data() + 16, plane.data() + stride + 4, stride);
  Hasher h;
  AddBuf (h, plane);
  AddBuf (h, pred);
  return h.Hex();
}

const int kPredStrides4[] = {16, 21, 32};   // 4x4 and chroma: columns 3 .. 11 at most
const int kPredStrides16[] = {24, 32, 37};  // 16x16: columns 3 .. 19

}  // namespace

void KernelsEncIntraPred() {
  struct PredKernel {
    const char* name;
    PGetIntraPredFunc f;
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
    K (WelsI16x16LumaPredPlane_c, 16), K (WelsI16x16LumaPredDc_c, 16),    K (WelsI16x16LumaPredDcTop_c, 16),
    K (WelsI16x16LumaPredDcLeft_c, 16), K (WelsI16x16LumaPredDcNA_c, 16),
  };
#undef K
  for (const PredKernel& k : kPred) {
    const int* strides = k.n == 16 ? kPredStrides16 : kPredStrides4;
    for (int i = 0; i < 3; ++i) {
      for (int seed = 0; seed < 2; ++seed) {
        EmitHex ("enc_get_intra_predictor", k.name, Case ("stride=%d,seed=%d", strides[i], seed),
                 PredHash (k.f, k.n, strides[i], seed));
      }
    }
  }
}

namespace {

// Target 4 of the combined kernels: the neighbours made flat (the row above,
// from the corner, and the column to the left all set to the sample above),
// so every mode predicts the same block and the costs tie.
void FlattenDec (uint8_t* pDec, int decStride, int n) {
  const uint8_t c = pDec[-decStride];
  for (int x = -1; x < n; ++x) pDec[-decStride + x] = c;
  for (int y = 0; y < n; ++y) pDec[y * decStride - 1] = c;
}

// The combined predict-and-cost kernels' source block, from the neighbours of
// the reconstructed block at pDec: target 0 copies the row above down, 1 the
// column to the left across, 2 (and 4, after FlattenDec) fills with the sample
// above, 3 is random; then noise in [-noise, noise] on every sample (clipped).
void FillEnc (Rng& r, uint8_t* pEnc, int encStride, const uint8_t* pDec, int decStride, int n, int target, int noise) {
  for (int y = 0; y < n; ++y) {
    for (int x = 0; x < n; ++x) {
      int v;
      switch (target) {
      case 0: v = pDec[-decStride + x]; break;
      case 1: v = pDec[y * decStride - 1]; break;
      case 2:
      case 4: v = pDec[-decStride]; break;
      default: v = r.NextByte(); break;
      }
      if (noise) v += (int) (r.Next() % (uint32_t) (2 * noise + 1)) - noise;
      pEnc[y * encStride + x] = (uint8_t) (v < 0 ? 0 : v > 255 ? 255 : v);
    }
  }
}

}  // namespace

void KernelsSample() {
  // SATD: random planes, the block at their origin; the second plane is the
  // first plus noise (noise < 256) or independent (noise 256).
  typedef int32_t (*PCost) (uint8_t*, int32_t, uint8_t*, int32_t);
  struct SatdKernel {
    const char* name;
    PCost f;
    int w, h;
  };
  const SatdKernel kSatd[] = {
    {"WelsSampleSatd4x4_c", WelsSampleSatd4x4_c, 4, 4},     {"WelsSampleSatd8x4_c", WelsSampleSatd8x4_c, 8, 4},
    {"WelsSampleSatd4x8_c", WelsSampleSatd4x8_c, 4, 8},     {"WelsSampleSatd8x8_c", WelsSampleSatd8x8_c, 8, 8},
    {"WelsSampleSatd16x8_c", WelsSampleSatd16x8_c, 16, 8},  {"WelsSampleSatd8x16_c", WelsSampleSatd8x16_c, 8, 16},
    {"WelsSampleSatd16x16_c", WelsSampleSatd16x16_c, 16, 16},
  };
  const int kSatdStrides[][2] = {{16, 16}, {21, 32}, {37, 16}};
  const int kNoise[] = {0, 4, 256};
  for (const SatdKernel& k : kSatd) {
    for (const auto& s : kSatdStrides) {
      for (int noise : kNoise) {
        for (int seed = 0; seed < 2; ++seed) {
          Rng r (seed);
          std::vector<uint8_t> p1 = Plane (r, (size_t)s[0] * k.h);
          std::vector<uint8_t> p2 = Plane (r, (size_t)s[1] * k.h);
          if (noise < 256) {
            for (int y = 0; y < k.h; ++y) {
              for (int x = 0; x < k.w; ++x) {
                int v = p1[y * s[0] + x] + (int) (r.Next() % (uint32_t) (2 * noise + 1)) - noise;
                p2[y * s[1] + x] = (uint8_t) (v < 0 ? 0 : v > 255 ? 255 : v);
              }
            }
          }
          int32_t ret = k.f (p1.data(), s[0], p2.data(), s[1]);
          Hasher h;
          h.AddI32 (ret);
          AddBuf (h, p1);
          AddBuf (h, p2);
          Emit ("sample", k.name, Case ("s1=%d,s2=%d,noise=%d,seed=%d", s[0], s[1], noise, seed), h);
        }
      }
    }
  }

  // The combined kernels. The reconstructed neighbours: a random plane of
  // n + 2 rows, the block at row 1, column 4. The source: its own plane at
  // encStride = decStride + 8, the block at the origin, from FillEnc. The
  // prediction: n x n bytes (two 8x8 for chroma) with 16 guard bytes either
  // side. The hash covers the cost, the mode, the prediction and the planes.
  const int kTargets = 5;
  const int kNoises[] = {0, 3};
  {
    // 4x4: lambdas (2, 1, 0) for (DC, H, V).
    const int kLambdas[][3] = {{0, 0, 0}, {24, 24, 0}, {0, 24, 24}, {24, 0, 24}};
    const int kDecStrides[] = {16, 32};
    for (int decStride : kDecStrides) {
      for (int target = 0; target < kTargets; ++target) {
        for (int noise : kNoises) {
          for (int lam = 0; lam < 4; ++lam) {
            for (int seed = 0; seed < 2; ++seed) {
              const int encStride = decStride + 8;
              Rng r (seed);
              std::vector<uint8_t> dec = Plane (r, (size_t)decStride * 6);
              std::vector<uint8_t> enc = Plane (r, (size_t)encStride * 4);
              std::vector<uint8_t> dst = Plane (r, 16 + 32);
              uint8_t* pDec = dec.data() + decStride + 4;
              if (target == 4) FlattenDec (pDec, decStride, 4);
              FillEnc (r, enc.data(), encStride, pDec, decStride, 4, target, noise);
              int32_t mode = (int32_t)r.Next();
              int32_t ret = WelsSampleSatdIntra4x4Combined3_c (pDec, decStride, enc.data(), encStride, dst.data() + 16, &mode,
                            kLambdas[lam][0], kLambdas[lam][1], kLambdas[lam][2]);
              Hasher h;
              h.AddI32 (ret);
              h.AddI32 (mode);
              AddBuf (h, dst);
              AddBuf (h, dec);
              AddBuf (h, enc);
              Emit ("sample", "WelsSampleSatdIntra4x4Combined3_c",
                    Case ("dec=%d,target=%d,noise=%d,lambda=%d,seed=%d", decStride, target, noise, lam, seed), h);
            }
          }
        }
      }
    }
  }
  const int kLambda1[] = {0, 10, 60};
  {
    typedef int32_t (*P16) (uint8_t*, int32_t, uint8_t*, int32_t, int32_t*, int32_t, uint8_t*);
    struct K16 {
      const char* name;
      P16 f;
    };
    const K16 k16[] = {
      {"WelsSampleSatdIntra16x16Combined3_c", WelsSampleSatdIntra16x16Combined3_c},
      {"WelsSampleSadIntra16x16Combined3_c", WelsSampleSadIntra16x16Combined3_c},
    };
    const int kDecStrides[] = {24, 40};
    for (const K16& k : k16) {
      for (int decStride : kDecStrides) {
        for (int target = 0; target < kTargets; ++target) {
          for (int noise : kNoises) {
            for (int lambda : kLambda1) {
              for (int seed = 0; seed < 2; ++seed) {
                const int encStride = decStride + 8;
                Rng r (seed);
                std::vector<uint8_t> dec = Plane (r, (size_t)decStride * 18);
                std::vector<uint8_t> enc = Plane (r, (size_t)encStride * 16);
                std::vector<uint8_t> dst = Plane (r, 256 + 32);
                uint8_t* pDec = dec.data() + decStride + 4;
                if (target == 4) FlattenDec (pDec, decStride, 16);
                FillEnc (r, enc.data(), encStride, pDec, decStride, 16, target, noise);
                int32_t mode = (int32_t)r.Next();
                int32_t ret = k.f (pDec, decStride, enc.data(), encStride, &mode, lambda, dst.data() + 16);
                Hasher h;
                h.AddI32 (ret);
                h.AddI32 (mode);
                AddBuf (h, dst);
                AddBuf (h, dec);
                AddBuf (h, enc);
                Emit ("sample", k.name,
                      Case ("dec=%d,target=%d,noise=%d,lambda=%d,seed=%d", decStride, target, noise, lambda, seed), h);
              }
            }
          }
        }
      }
    }
  }
  {
    typedef int32_t (*P8) (uint8_t*, int32_t, uint8_t*, int32_t, int32_t*, int32_t, uint8_t*, uint8_t*, uint8_t*);
    struct K8 {
      const char* name;
      P8 f;
    };
    const K8 k8[] = {
      {"WelsSampleSatdIntra8x8Combined3_c", WelsSampleSatdIntra8x8Combined3_c},
      {"WelsSampleSadIntra8x8Combined3_c", WelsSampleSadIntra8x8Combined3_c},
    };
    const int kDecStrides[] = {16, 24};
    for (const K8& k : k8) {
      for (int decStride : kDecStrides) {
        for (int target = 0; target < kTargets; ++target) {
          for (int noise : kNoises) {
            for (int lambda : kLambda1) {
              for (int seed = 0; seed < 2; ++seed) {
                const int encStride = decStride + 8;
                Rng r (seed);
                std::vector<uint8_t> decCb = Plane (r, (size_t)decStride * 10);
                std::vector<uint8_t> decCr = Plane (r, (size_t)decStride * 10);
                std::vector<uint8_t> encCb = Plane (r, (size_t)encStride * 8);
                std::vector<uint8_t> encCr = Plane (r, (size_t)encStride * 8);
                std::vector<uint8_t> dst = Plane (r, 128 + 32);
                uint8_t* pDecCb = decCb.data() + decStride + 4;
                uint8_t* pDecCr = decCr.data() + decStride + 4;
                if (target == 4) {
                  FlattenDec (pDecCb, decStride, 8);
                  FlattenDec (pDecCr, decStride, 8);
                }
                FillEnc (r, encCb.data(), encStride, pDecCb, decStride, 8, target, noise);
                FillEnc (r, encCr.data(), encStride, pDecCr, decStride, 8, target, noise);
                int32_t mode = (int32_t)r.Next();
                int32_t ret = k.f (pDecCb, decStride, encCb.data(), encStride, &mode, lambda, dst.data() + 16, pDecCr,
                                   encCr.data());
                Hasher h;
                h.AddI32 (ret);
                h.AddI32 (mode);
                AddBuf (h, dst);
                AddBuf (h, decCb);
                AddBuf (h, decCr);
                AddBuf (h, encCb);
                AddBuf (h, encCr);
                Emit ("sample", k.name,
                      Case ("dec=%d,target=%d,noise=%d,lambda=%d,seed=%d", decStride, target, noise, lambda, seed), h);
              }
            }
          }
        }
      }
    }
  }
}

void KernelsEncInit() {
  // Every slot of the indexed tables, called on one fixture each, so a slot
  // that points at the wrong kernel (or none) fails. The named slots are
  // checked on the C# side by address.
  SWelsFuncPtrList* pList = (SWelsFuncPtrList*)calloc (1, sizeof (SWelsFuncPtrList));
  WelsInitEncodingFuncs (pList, 0);
  WelsInitReconstructionFuncs (pList, 0);
  WelsInitSampleSadFunc (pList, 0);
  WelsInitIntraPredFuncs (pList, 0);

  for (int i = 0; i < I16_PRED_DC_A; ++i)
    EmitHex ("enc_init", "pfGetLumaI16x16Pred", Case ("idx=%d", i), PredHash (pList->pfGetLumaI16x16Pred[i], 16, 24, i));
  for (int i = 0; i < I4_PRED_A; ++i)
    EmitHex ("enc_init", "pfGetLumaI4x4Pred", Case ("idx=%d", i), PredHash (pList->pfGetLumaI4x4Pred[i], 4, 16, i));
  for (int i = 0; i < C_PRED_A; ++i)
    EmitHex ("enc_init", "pfGetChromaPred", Case ("idx=%d", i), PredHash (pList->pfGetChromaPred[i], 8, 16, i));

  // The cost tables by BLOCK_* index: two random planes of 18 rows at stride
  // 32, the blocks at row 1, column 4 (pfSample4Sad reads one sample around
  // the second).
  for (int i = 0; i < MAX_BLOCK_TYPE; ++i) {
    for (int t = 0; t < 3; ++t) {
      Rng r (i);
      std::vector<uint8_t> p1 = Plane (r, 32 * 18);
      std::vector<uint8_t> p2 = Plane (r, 32 * 18);
      int32_t sad[4 + 2] = {7, 7, 7, 7, 7, 7};
      Hasher h;
      const char* name;
      if (t == 0) {
        name = "pfSampleSad";
        h.AddI32 (pList->sSampleDealingFuncs.pfSampleSad[i] (p1.data() + 32 + 4, 32, p2.data() + 32 + 4, 32));
      } else if (t == 1) {
        name = "pfSampleSatd";
        h.AddI32 (pList->sSampleDealingFuncs.pfSampleSatd[i] (p1.data() + 32 + 4, 32, p2.data() + 32 + 4, 32));
      } else {
        name = "pfSample4Sad";
        pList->sSampleDealingFuncs.pfSample4Sad[i] (p1.data() + 32 + 4, 32, p2.data() + 32 + 4, 32, sad + 1);
        for (int32_t v : sad) h.AddI32 (v);
      }
      Emit ("enc_init", name, Case ("idx=%d", i), h);
    }
  }
  free (pList);
}
