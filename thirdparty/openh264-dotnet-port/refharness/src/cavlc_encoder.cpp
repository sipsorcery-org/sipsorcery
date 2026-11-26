// cavlc_encoder.cpp: fixtures for the ported CAVLC writer (E3).
//
// Built with the encoder include paths only, like kernels_encoder.cpp (the
// h264ref_enc_kernels object library in CMakeLists.txt).
//
// C# side, one test class per group (test/SIPSorcery.H264.UnitTest):
//   "vlc_encoder"           the element writers of vlc_encoder.h, over every
//                           table entry (vlc_encoder_unittest.cs).
//   "set_mb_syn_cavlc"      CavlcParamCal_c, WriteBlockResidualCavlc, the CAVLC
//                           and CABAC stash/pop pairs and WelsWriteSliceEndSyn
//                           (set_mb_syn_cavlc_unittest.cs).
//   "svc_set_mb_syn_cavlc"  the macroblock writers: WelsSpatialWriteMbPred,
//                           WelsSpatialWriteSubMbPred, WelsWriteMbResidual,
//                           CheckBitstreamBuffer and WelsSpatialWriteMbSyn over
//                           slices of 8 macroblocks
//                           (svc_set_mb_syn_cavlc_unittest.cs).
//
// Bitstream buffers are random-filled and InitBits'd with a logical size; the
// hash covers the whole physical buffer and the writer's cache state, so bytes
// written past the cache (WRITE_BE_32 stores 4 at a time) count too. The C#
// side (CavlcFixtures.cs) must draw every value in the same order as below.

#include <cstdarg>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>
#include <vector>

#include "fixture.h"

#include "wels_common_defs.h"
#include "encoder_context.h"
#include "set_mb_syn_cavlc.h"
#include "svc_enc_frame.h"
#include "svc_enc_golomb.h"
#include "svc_set_mb_syn.h"
#include "vlc_encoder.h"
#include "wels_func_ptr_def.h"

namespace WelsEnc {
// Defined in set_mb_syn_cavlc.cpp and svc_set_mb_syn_cavlc.cpp, but no header
// declares them (upstream reaches them through SWelsFuncPtrList).
void StashMBStatusCavlc (SDynamicSlicingStack* pDss, SSlice* pSlice, int32_t iMbSkipRun);
int32_t StashPopMBStatusCavlc (SDynamicSlicingStack* pDss, SSlice* pSlice);
void StashMBStatusCabac (SDynamicSlicingStack* pDss, SSlice* pSlice, int32_t iMbSkipRun);
int32_t StashPopMBStatusCabac (SDynamicSlicingStack* pDss, SSlice* pSlice);
int32_t CheckBitstreamBuffer (const uint32_t kuiSliceIdx, sWelsEncCtx* pEncCtx, SBitStringAux* pBs);
}  // namespace WelsEnc

using namespace fixture;
using namespace WelsEnc;

namespace {

std::string Fmt (const char* fmt, ...) {
  char s[256];
  va_list ap;
  va_start (ap, fmt);
  vsnprintf (s, sizeof s, fmt, ap);
  va_end (ap);
  return s;
}

// A symmetric draw in [-r, r].
int32_t Sym (Rng& g, int32_t r) {
  return (int32_t) (g.Next() % (uint32_t) (2 * r + 1)) - r;
}

// A coefficient that is non-zero with probability density/16, of magnitude
// 1..range.
int16_t Coef (Rng& g, int density, int range) {
  if ((int32_t) (g.Next() % 16) >= density) return 0;
  int32_t v = 1 + (int32_t) (g.Next() % (uint32_t)range);
  if (g.Next() & 1) v = -v;
  return (int16_t)v;
}

// A random physical buffer with a writer over its first logical bytes, and
// pre random bits already written.
struct Bs {
  std::vector<uint8_t> buf;
  SBitStringAux bs;
  Bs (Rng& g, size_t physical, int32_t logical, int32_t pre) : buf (physical) {
    g.Fill (buf.data(), physical);
    InitBits (&bs, buf.data(), logical);
    if (pre > 0) BsWriteBits (&bs, pre, g.Next() >> (32 - pre));
  }
  void Hash (Hasher& h) {
    h.AddI32 ((int32_t) (bs.pCurBuf - bs.pStartBuf));
    h.AddI32 ((int32_t)bs.uiCurBits);
    h.AddI32 (bs.iLeftBits);
    h.Add (buf.data(), buf.size());
  }
};

// Everything one macroblock write reads, filled by FillMb.
struct MbFixture {
  SMB mb;
  SMVUnitXY mv[16];
  int8_t ref[4];
  bool prevFlag[16];
  int8_t remMode[16];
  SDCTCoeff dct;
};

const uint32_t kSubTypes[4] = {SUB_MB_TYPE_8x8, SUB_MB_TYPE_8x4, SUB_MB_TYPE_4x8, SUB_MB_TYPE_4x4};

// Draws one macroblock of the given type into f and the slice's cache. The
// motion vectors are mvp + d stored as int16_t, with |d| <= 16383 so every
// difference fits the 32-bit writer; the 8x8 type keeps |mv| <= 16382, as its
// differences are taken in int.
void FillMb (Rng& g, MbFixture& f, SMbCache* pCache, uint32_t uiMbType, int32_t iRefs, int32_t iMvRange,
             int32_t iDensity, int32_t iRange) {
  memset (&f.mb, 0, sizeof f.mb);
  f.mb.uiMbType = uiMbType;
  f.mb.sMv = f.mv;
  f.mb.pRefIndex = f.ref;

  const uint32_t a = g.Next(), b = g.Next();
  int32_t iCbp = IS_INTRA16x16 (uiMbType) ? (int32_t) (((b % 3) << 4) | ((a & 1) ? 15 : 0))
                                          : (int32_t) (((b % 3) << 4) | (a % 16));
  if (IS_SKIP (uiMbType)) iCbp = 0;
  f.mb.uiCbp = (uint8_t)iCbp;
  f.mb.uiLumaQp = (uint8_t) (g.Next() % 52);
  pCache->uiLumaI16x16Mode = (uint8_t) (g.Next() % 7);
  pCache->uiChmaI8x8Mode = (uint8_t) (g.Next() % 7);
  for (int i = 0; i < 16; i++) {
    f.prevFlag[i] = (g.Next() & 1) != 0;
    f.remMode[i] = (int8_t) (g.Next() % 8);
  }
  for (int i = 0; i < 4; i++) f.mb.uiSubMbType[i] = (uint8_t)kSubTypes[g.Next() % 4];
  const bool bRef0 = (g.Next() % 3) == 0;
  for (int i = 0; i < 4; i++) f.ref[i] = bRef0 ? 0 : (int8_t) (g.Next() % (uint32_t)iRefs);

  const bool b8x8 = IS_Inter_8x8 (uiMbType);
  const int32_t kiRp = b8x8 ? WELS_MIN (iMvRange, 8191) : iMvRange;
  const int32_t kiRd = b8x8 ? WELS_MIN (iMvRange, 8191) : WELS_MIN (iMvRange, 16383);
  for (int i = 0; i < 16; i++) {
    SMVUnitXY& p = pCache->sMbMvp[i];
    p.iMvX = (int16_t)Sym (g, kiRp);
    f.mv[i].iMvX = (int16_t) (p.iMvX + Sym (g, kiRd));
    p.iMvY = (int16_t)Sym (g, kiRp);
    f.mv[i].iMvY = (int16_t) (p.iMvY + Sym (g, kiRd));
  }

  for (int i = 0; i < 48; i++) pCache->iNonZeroCoeffCount[i] = (int8_t) ((int32_t) (g.Next() % 18) - 1);
  for (int k = 0; k < 16; k++)
    for (int j = 0; j < 16; j++) f.dct.iLumaBlock[k][j] = Coef (g, iDensity, iRange);
  for (int j = 0; j < 16; j++) f.dct.iLumaI16x16Dc[j] = Coef (g, iDensity, iRange);
  for (int k = 0; k < 8; k++)
    for (int j = 0; j < 16; j++) f.dct.iChromaBlock[k][j] = Coef (g, iDensity, iRange);
  for (int k = 0; k < 2; k++)
    for (int j = 0; j < 4; j++) f.dct.iChromaDc[k][j] = Coef (g, iDensity, iRange);

  // Each coded block's own count, over the coefficients the writer reads.
  const int kiLumaEnd = IS_INTRA16x16 (uiMbType) ? 14 : 15;
  for (int k = 0; k < 16; k++) {
    int n = 0;
    for (int j = 0; j <= kiLumaEnd; j++) n += f.dct.iLumaBlock[k][j] != 0;
    pCache->iNonZeroCoeffCount[g_kuiCache48CountScan4Idx[k]] = (int8_t)n;
  }
  for (int k = 0; k < 8; k++) {
    int n = 0;
    for (int j = 0; j <= 14; j++) n += f.dct.iChromaBlock[k][j] != 0;
    pCache->iNonZeroCoeffCount[(k < 4 ? 0 : 24) + g_kuiCache48CountScan4Idx[16 + (k & 3)]] = (int8_t)n;
  }

  pCache->pPrevIntra4x4PredModeFlag = f.prevFlag;
  pCache->pRemIntra4x4PredModeFlag = f.remMode;
  pCache->pDct = &f.dct;
}

// The context a macroblock write reads: the function list from InitCoeffFunc,
// the DQ layer's PPS, the slice and its writer.
struct WriterCtx {
  SWelsFuncPtrList funcs;
  SWelsPPS pps;
  SDqLayer* pDq;
  sWelsEncCtx* pCtx;
  SSlice* pSlice;
  WriterCtx (EWelsSliceType eType, int32_t iRefs, uint8_t uiChromaQpOffset) {
    memset (&funcs, 0, sizeof funcs);
    InitCoeffFunc (&funcs, 0, 0);
    memset (&pps, 0, sizeof pps);
    pps.uiChromaQpIndexOffset = uiChromaQpOffset;
    pDq = (SDqLayer*)calloc (1, sizeof (SDqLayer));
    pDq->sLayerInfo.pPpsP = &pps;
    pCtx = (sWelsEncCtx*)calloc (1, sizeof (sWelsEncCtx));
    pCtx->pFuncList = &funcs;
    pCtx->pCurDqLayer = pDq;
    pCtx->eSliceType = eType;
    pSlice = (SSlice*)calloc (1, sizeof (SSlice));
    pSlice->sSliceHeaderExt.sSliceHeader.eSliceType = eType;
    pSlice->sSliceHeaderExt.sSliceHeader.uiNumRefIdxL0Active = (uint8_t)iRefs;
  }
  ~WriterCtx() {
    free (pSlice);
    free (pCtx);
    free (pDq);
  }
};

// The level sweep's 4x4 block: the raisers that bring the suffix length to s,
// then v, then 1, from coefficient 15 down; the rest 0.
void ResidualSweepBlock (int16_t* pCoef, int s, int v) {
  const int kiRaisers[5] = {4, 7, 13, 25, 49};
  std::vector<int> l;
  if (s == 1) l.push_back (2);
  for (int i = 0; i + 1 < s; i++) l.push_back (kiRaisers[i]);
  l.push_back (v);
  l.push_back (1);
  for (int j = 0; j < 16; j++) pCoef[j] = 0;
  for (size_t k = 0; k < l.size(); k++) pCoef[15 - k] = (int16_t)l[k];
}

// The count sweep's block: n coefficients spread over 0..end, the first t
// (from the top) trailing ones, the rest of magnitude 2 to 4.
void ResidualCountBlock (int16_t* pCoef, int end, int n, int t) {
  for (int j = 0; j <= end; j++) pCoef[j] = 0;
  for (int k = 0; k < n; k++) {
    const int p = end - k * (end + 1) / n;
    const int v = k < t ? 1 : 2 + (n + k) % 3;
    pCoef[p] = (int16_t) (((n * 3 + k) & 1) ? -v : v);
  }
}

const uint32_t kPTypes[7] = {MB_TYPE_SKIP, MB_TYPE_INTRA4x4, MB_TYPE_INTRA16x16, MB_TYPE_16x16,
                             MB_TYPE_16x8, MB_TYPE_8x16, MB_TYPE_8x8};
const uint32_t kPredTypes[5] = {MB_TYPE_INTRA4x4, MB_TYPE_INTRA16x16, MB_TYPE_16x16, MB_TYPE_16x8, MB_TYPE_8x16};
const EWelsSliceType kSliceTypes[3] = {P_SLICE, B_SLICE, I_SLICE};  // by EWelsSliceType value

}  // namespace

void KernelsVlcEncoder() {
  // Every entry of each writer's table, in index order, after pre bits.
  for (int pre : {0, 7, 19}) {
    {
      Rng g (pre);
      Bs w (g, 4096, 4096, pre);
      int32_t iRet = 0;
      for (int nc = 0; nc < 18; nc++)
        for (int t = 0; t <= 16; t++)
          for (int o = 0; o < 4; o++) iRet |= WriteTotalCoeffTrailingones (&w.bs, nc, t, o);
      BsFlush (&w.bs);
      Hasher h;
      h.AddI32 (iRet);
      w.Hash (h);
      Emit ("vlc_encoder", "WriteTotalCoeffTrailingones", Fmt ("pre=%d", pre), h);
    }
    {
      Rng g (pre);
      Bs w (g, 1024, 1024, pre);
      int32_t iRet = 0;
      for (int t = 0; t <= 16; t++)
        for (int o = 0; o < 4; o++) iRet |= WriteTotalcoeffTrailingonesChroma (&w.bs, t, o);
      BsFlush (&w.bs);
      Hasher h;
      h.AddI32 (iRet);
      w.Hash (h);
      Emit ("vlc_encoder", "WriteTotalcoeffTrailingonesChroma", Fmt ("pre=%d", pre), h);
    }
    {
      Rng g (pre);
      Bs w (g, 1024, 1024, pre);
      int32_t iRet = 0;
      for (uint32_t z = 0; z < 16; z++) iRet |= WriteLevelPrefix (&w.bs, z);
      BsFlush (&w.bs);
      Hasher h;
      h.AddI32 (iRet);
      w.Hash (h);
      Emit ("vlc_encoder", "WriteLevelPrefix", Fmt ("pre=%d", pre), h);
    }
    {
      Rng g (pre);
      Bs w (g, 1024, 1024, pre);
      int32_t iRet = 0;
      for (uint32_t t = 0; t < 16; t++)
        for (uint32_t z = 0; z < 16; z++) iRet |= WriteTotalZeros (&w.bs, t, z);
      BsFlush (&w.bs);
      Hasher h;
      h.AddI32 (iRet);
      w.Hash (h);
      Emit ("vlc_encoder", "WriteTotalZeros", Fmt ("pre=%d", pre), h);
    }
    {
      Rng g (pre);
      Bs w (g, 256, 256, pre);
      int32_t iRet = 0;
      for (uint32_t t = 0; t < 4; t++)
        for (uint32_t z = 0; z < 4; z++) iRet |= WriteTotalZerosChromaDc (&w.bs, t, z);
      BsFlush (&w.bs);
      Hasher h;
      h.AddI32 (iRet);
      w.Hash (h);
      Emit ("vlc_encoder", "WriteTotalZerosChromaDc", Fmt ("pre=%d", pre), h);
    }
    {
      Rng g (pre);
      Bs w (g, 1024, 1024, pre);
      int32_t iRet = 0;
      for (int z = 0; z < 8; z++)
        for (int r = 0; r < 15; r++) iRet |= WriteRunBefore (&w.bs, z, r);
      BsFlush (&w.bs);
      Hasher h;
      h.AddI32 (iRet);
      w.Hash (h);
      Emit ("vlc_encoder", "WriteRunBefore", Fmt ("pre=%d", pre), h);
    }
  }
}

void KernelsSetMbSynCavlc() {
  // CavlcParamCal_c over blocks of 4, 15 and 16 coefficients, with 4 guard
  // entries around each buffer.
  for (int last : {3, 14, 15})
    for (int density : {0, 1, 4, 10, 16})
      for (int range : {1, 3, 300})
        for (int seed = 0; seed < 2; seed++) {
          Rng g (seed);
          int16_t coef[24], level[24];
          uint8_t run[24];
          for (int i = 0; i < 24; i++) coef[i] = (int16_t) (g.Next() >> 16);
          for (int i = 0; i <= last; i++) coef[4 + i] = Coef (g, density, range);
          for (int i = 0; i < 24; i++) level[i] = (int16_t) (g.Next() >> 16);
          g.Fill (run, sizeof run);
          int32_t iTotal = (int32_t)g.Next();
          int32_t iRet = CavlcParamCal_c (coef + 4, run + 4, level + 4, &iTotal, last);
          Hasher h;
          h.AddI32 (iRet);
          h.AddI32 (iTotal);
          h.Add (coef, sizeof coef);
          h.Add (level, sizeof level);
          h.Add (run, sizeof run);
          Emit ("set_mb_syn_cavlc", "CavlcParamCal_c",
                Fmt ("last=%d,density=%d,range=%d,seed=%d", last, density, range, seed), h);
        }

  // WriteBlockResidualCavlc for each block kind the macroblock writer uses,
  // each nC bucket, and levels from all trailing ones (range 1) to the
  // escapes and the overflow return (range 3000), after random pre bits.
  SWelsFuncPtrList funcs;
  memset (&funcs, 0, sizeof funcs);
  InitCoeffFunc (&funcs, 0, 0);
  struct Kind {
    int end, prop;
  };
  const Kind kKinds[] = {{15, LUMA_4x4}, {14, LUMA_AC}, {14, CHROMA_AC}, {3, CHROMA_DC}};
  for (const Kind& k : kKinds) {
    std::vector<int> ncs = k.prop == CHROMA_DC ? std::vector<int> {CHROMA_DC_NC_OFFSET} : std::vector<int> {0, 2, 4, 8};
    for (int nc : ncs)
      for (int flag : {0, 1})
        for (int density : {0, 1, 4, 10, 16})
          for (int range : {1, 3, 100, 3000})
            for (int seed = 0; seed < 2; seed++) {
              if (flag == 0 && (density != 10 || range != 3)) continue;
              Rng g (seed);
              int16_t coef[24];
              for (int i = 0; i < 24; i++) coef[i] = (int16_t) (g.Next() >> 16);
              for (int i = 0; i <= k.end; i++) coef[4 + i] = Coef (g, density, range);
              const int32_t kiPre = (int32_t) (g.Next() % 32);
              Bs w (g, 256, 256, kiPre);
              int32_t iRet = WriteBlockResidualCavlc (&funcs, coef + 4, k.end, flag, k.prop, (int8_t)nc, &w.bs);
              Hasher h;
              h.AddI32 (iRet);
              w.Hash (h);
              h.Add (coef, sizeof coef);
              Emit ("set_mb_syn_cavlc", "WriteBlockResidualCavlc",
                    Fmt ("end=%d,prop=%d,nc=%d,flag=%d,density=%d,range=%d,seed=%d", k.end, k.prop, nc, flag, density, range,
                         seed), h);
            }
  }

  // WriteBlockResidualCavlc at the level code's decisions, for each suffix
  // length 0 to 6: either side of the prefix 14 and 15 escapes, of the
  // overflow, and of the suffix length's increment. The level v follows
  // "raisers" that bring the suffix length to s (none for 0, so v is the
  // first level and takes the trailing-ones adjustment), and a final 1 is
  // written with the suffix length v leaves.
  for (int s = 0; s <= 6; s++) {
    std::vector<int> levels;
    auto add = [&levels] (int v) {
      for (int x : levels)
        if (x == v) return;
      levels.push_back (v);
    };
    const int kiEscapes[3] = {14 << s, s == 0 ? 30 : 15 << s, (15 << s) + 2048};
    for (int t : kiEscapes)
      for (int c = t - 2; c <= t + 1; c++) {
        const int raw = c + (s == 0 ? 2 : 0);  // undo the first level's adjustment
        add (raw % 2 == 0 ? raw / 2 + 1 : - (raw + 1) / 2);
      }
    const int kiThreshold = 3 << (WELS_MAX (s, 1) - 1);
    for (int v : {kiThreshold, kiThreshold + 1}) {
      add (v);
      add (-v);
    }
    for (int v : levels) {
      Rng g ((uint32_t) (s * 10007 + v));
      int16_t coef[24];
      for (int i = 0; i < 24; i++) coef[i] = (int16_t) (g.Next() >> 16);
      ResidualSweepBlock (coef + 4, s, v);
      const int32_t kiPre = (int32_t) (g.Next() % 32);
      Bs w (g, 256, 256, kiPre);
      int32_t iRet = WriteBlockResidualCavlc (&funcs, coef + 4, 15, 1, LUMA_4x4, 0, &w.bs);
      Hasher h;
      h.AddI32 (iRet);
      w.Hash (h);
      h.Add (coef, sizeof coef);
      Emit ("set_mb_syn_cavlc", "WriteBlockResidualCavlc",
            Fmt ("end=15,prop=%d,nc=0,flag=1,suffix=%d,level=%d", LUMA_4x4, s, v), h);
    }
  }

  // WriteBlockResidualCavlc for every total coefficient and trailing ones
  // count in each nC table, so the token lookup and the suffix length's start
  // (more than 10 coefficients) are decided at each count.
  struct CountKind {
    int end, prop, nc;
  };
  const CountKind kCountKinds[] = {{15, LUMA_4x4, 0}, {15, LUMA_4x4, 2}, {15, LUMA_4x4, 4}, {15, LUMA_4x4, 8},
                                   {3, CHROMA_DC, CHROMA_DC_NC_OFFSET}};
  for (const CountKind& k : kCountKinds)
    for (int n = 0; n <= k.end + 1; n++)
      for (int t = 0; t <= WELS_MIN (3, n); t++) {
        Rng g ((uint32_t) (k.nc * 100 + n * 4 + t));
        int16_t coef[24];
        for (int i = 0; i < 24; i++) coef[i] = (int16_t) (g.Next() >> 16);
        ResidualCountBlock (coef + 4, k.end, n, t);
        const int32_t kiPre = (int32_t) (g.Next() % 32);
        Bs w (g, 256, 256, kiPre);
        int32_t iRet = WriteBlockResidualCavlc (&funcs, coef + 4, k.end, 1, k.prop, (int8_t)k.nc, &w.bs);
        Hasher h;
        h.AddI32 (iRet);
        w.Hash (h);
        h.Add (coef, sizeof coef);
        Emit ("set_mb_syn_cavlc", "WriteBlockResidualCavlc",
              Fmt ("end=%d,prop=%d,nc=%d,flag=1,count=%d,ones=%d", k.end, k.prop, k.nc, n, t), h);
      }

  // The CAVLC stash and pop: stash the slice's writer state, write on, pop.
  for (int seed = 0; seed < 4; seed++) {
    Rng g (seed);
    WriterCtx c (P_SLICE, 1, 0);
    Bs w (g, 256, 256, (int32_t) (g.Next() % 32));
    c.pSlice->pSliceBsa = &w.bs;
    c.pSlice->uiLastMbQp = (uint8_t) (g.Next() % 52);
    SDynamicSlicingStack dss;
    memset (&dss, 0, sizeof dss);
    const int32_t kiSkipRun = (int32_t) (g.Next() % 100);
    StashMBStatusCavlc (&dss, c.pSlice, kiSkipRun);
    Hasher h;
    h.AddI32 ((int32_t) (dss.pBsStackBufPtr - w.bs.pStartBuf));
    h.AddI32 ((int32_t)dss.uiBsStackCurBits);
    h.AddI32 (dss.iBsStackLeftBits);
    h.AddI32 (dss.uiLastMbQp);
    h.AddI32 (dss.iMbSkipRunStack);
    BsWriteBits (&w.bs, 20, g.Next() >> 12);
    BsWriteBits (&w.bs, 30, g.Next() >> 2);
    c.pSlice->uiLastMbQp = (uint8_t) (g.Next() % 52);
    h.AddI32 (GetBsPosCavlc (c.pSlice));
    h.AddI32 (StashPopMBStatusCavlc (&dss, c.pSlice));
    h.AddI32 (GetBsPosCavlc (c.pSlice));
    h.AddI32 (c.pSlice->uiLastMbQp);
    w.Hash (h);
    Emit ("set_mb_syn_cavlc", "StashMBStatusCavlc", Fmt ("seed=%d", seed), h);
  }

  // The CABAC stash and pop copy the context and, with a restore buffer, the
  // bytes written since iStartPos.
  for (int restore : {0, 1})
    for (int seed = 0; seed < 4; seed++) {
      Rng g (seed);
      WriterCtx c (P_SLICE, 1, 0);
      std::vector<uint8_t> buf (512), saved (512);
      g.Fill (buf.data(), buf.size());
      g.Fill (saved.data(), saved.size());
      SCabacCtx& cc = c.pSlice->sCabacCtx;
      cc.m_uiLow = ((uint64_t)g.Next() << 32) | g.Next();
      cc.m_iLowBitCnt = 9 + (int32_t) (g.Next() % 40);
      cc.m_iRenormCnt = (int32_t) (g.Next() % 8);
      cc.m_uiRange = 256 + g.Next() % 256;
      for (int i = 0; i < WELS_CONTEXT_COUNT; i++) cc.m_sStateCtx[i].m_uiStateMps = (uint8_t)g.Next();
      cc.m_pBufStart = buf.data();
      cc.m_pBufEnd = buf.data() + buf.size();
      cc.m_pBufCur = buf.data() + 32 + g.Next() % 200;
      c.pSlice->uiLastMbQp = (uint8_t) (g.Next() % 52);
      SDynamicSlicingStack dss;
      memset (&dss, 0, sizeof dss);
      dss.iStartPos = (int32_t) (g.Next() % 64);
      dss.pRestoreBuffer = restore ? saved.data() : NULL;
      const int32_t kiSkipRun = (int32_t) (g.Next() % 100);
      StashMBStatusCabac (&dss, c.pSlice, kiSkipRun);
      Hasher h;
      h.AddI32 (GetBsPosCabac (c.pSlice));
      h.AddI64 ((int64_t)dss.sStoredCabac.m_uiLow);
      h.AddI32 (dss.sStoredCabac.m_iLowBitCnt);
      h.AddI32 (dss.sStoredCabac.m_iRenormCnt);
      h.AddI32 ((int32_t)dss.sStoredCabac.m_uiRange);
      h.Add (dss.sStoredCabac.m_sStateCtx, sizeof dss.sStoredCabac.m_sStateCtx);
      h.AddI32 ((int32_t) (dss.sStoredCabac.m_pBufCur - buf.data()));
      h.AddI32 (dss.uiLastMbQp);
      h.AddI32 (dss.iMbSkipRunStack);
      h.Add (saved.data(), saved.size());
      // Write on: move the context and scribble over the slice's buffer.
      cc.m_uiLow = g.Next();
      cc.m_iLowBitCnt = 9 + (int32_t) (g.Next() % 40);
      cc.m_pBufCur += g.Next() % 100;
      for (int i = 0; i < WELS_CONTEXT_COUNT; i++) cc.m_sStateCtx[i].m_uiStateMps = (uint8_t)g.Next();
      g.Fill (buf.data(), buf.size());
      c.pSlice->uiLastMbQp = (uint8_t) (g.Next() % 52);
      h.AddI32 (StashPopMBStatusCabac (&dss, c.pSlice));
      h.AddI32 (GetBsPosCabac (c.pSlice));
      h.AddI64 ((int64_t)cc.m_uiLow);
      h.AddI32 (cc.m_iLowBitCnt);
      h.Add (cc.m_sStateCtx, sizeof cc.m_sStateCtx);
      h.AddI32 ((int32_t) (cc.m_pBufCur - buf.data()));
      h.AddI32 (c.pSlice->uiLastMbQp);
      h.Add (buf.data(), buf.size());
      Emit ("set_mb_syn_cavlc", "StashMBStatusCabac", Fmt ("restore=%d,seed=%d", restore, seed), h);
    }

  // The CAVLC slice end: trailing bits and flush, from every bit position.
  for (int pre = 0; pre < 64; pre += 3) {
    Rng g (pre);
    WriterCtx c (P_SLICE, 1, 0);
    Bs w (g, 64, 64, 0);
    for (int left = pre; left > 0; left -= WELS_MIN (left, 24)) {
      const int n = WELS_MIN (left, 24);
      BsWriteBits (&w.bs, n, g.Next() >> (32 - n));
    }
    c.pSlice->pSliceBsa = &w.bs;
    WelsWriteSliceEndSyn (c.pSlice, false);
    Hasher h;
    w.Hash (h);
    Emit ("set_mb_syn_cavlc", "WelsWriteSliceEndSyn", Fmt ("pre=%d", pre), h);
  }
}

void KernelsSvcSetMbSynCavlc() {
  // WelsSpatialWriteMbPred for each type it writes, in I, P and B slices (B
  // writes nothing), with 1 to 3 active references.
  for (int slice = 0; slice < 3; slice++)
    for (int type = 0; type < 5; type++)
      for (int refs = 1; refs <= 3; refs++)
        for (int seed = 0; seed < 2; seed++) {
          Rng g (seed);
          WriterCtx c (kSliceTypes[slice], refs, 0);
          MbFixture f;
          FillMb (g, f, &c.pSlice->sMbCacheInfo, kPredTypes[type], refs, 32767, 4, 20);
          Bs w (g, 512, 512, (int32_t) (g.Next() % 32));
          c.pSlice->pSliceBsa = &w.bs;
          WelsSpatialWriteMbPred (c.pCtx, c.pSlice, &f.mb);
          Hasher h;
          w.Hash (h);
          Emit ("svc_set_mb_syn_cavlc", "WelsSpatialWriteMbPred",
                Fmt ("slice=%d,type=%d,refs=%d,seed=%d", slice, type, refs, seed), h);
        }

  // WelsSpatialWriteSubMbPred: random sub types, references (a third all 0,
  // which is P_8x8ref0) and motion vector differences.
  for (int refs = 1; refs <= 3; refs++)
    for (int seed = 0; seed < 8; seed++) {
      Rng g (seed);
      WriterCtx c (P_SLICE, refs, 0);
      MbFixture f;
      FillMb (g, f, &c.pSlice->sMbCacheInfo, MB_TYPE_8x8, refs, 8191, 4, 20);
      Bs w (g, 512, 512, (int32_t) (g.Next() % 32));
      c.pSlice->pSliceBsa = &w.bs;
      WelsSpatialWriteSubMbPred (c.pCtx, c.pSlice, &f.mb);
      Hasher h;
      w.Hash (h);
      Emit ("svc_set_mb_syn_cavlc", "WelsSpatialWriteSubMbPred", Fmt ("refs=%d,seed=%d", refs, seed), h);
    }

  // WelsWriteMbResidual for the three residual layouts.
  const uint32_t kResTypes[3] = {MB_TYPE_INTRA16x16, MB_TYPE_INTRA4x4, MB_TYPE_16x16};
  for (int type = 0; type < 3; type++)
    for (int density : {1, 6, 16})
      for (int range : {1, 3, 30, 3000})
        for (int seed = 0; seed < 3; seed++) {
          Rng g (seed);
          WriterCtx c (P_SLICE, 1, 0);
          MbFixture f;
          FillMb (g, f, &c.pSlice->sMbCacheInfo, kResTypes[type], 1, 64, density, range);
          Bs w (g, 4096, 4096, (int32_t) (g.Next() % 32));
          int32_t iRet = WelsWriteMbResidual (&c.funcs, &c.pSlice->sMbCacheInfo, &f.mb, &w.bs);
          Hasher h;
          h.AddI32 (iRet);
          w.Hash (h);
          Emit ("svc_set_mb_syn_cavlc", "WelsWriteMbResidual",
                Fmt ("type=%d,density=%d,range=%d,seed=%d", type, density, range, seed), h);
        }

  // CheckBitstreamBuffer either side of its limit (MAX_MACROBLOCK_SIZE_IN_BYTE_x2
  // bytes left).
  for (int left = MAX_MACROBLOCK_SIZE_IN_BYTE_x2 - 2; left <= MAX_MACROBLOCK_SIZE_IN_BYTE_x2 + 1; left++) {
    Rng g (left);
    WriterCtx c (P_SLICE, 1, 0);
    Bs w (g, 4096, 4096, 0);
    w.bs.pCurBuf += g.Next() % 1000;
    w.bs.pEndBuf = w.bs.pCurBuf + left + 1;
    Hasher h;
    h.AddI32 (CheckBitstreamBuffer (0, c.pCtx, &w.bs));
    Emit ("svc_set_mb_syn_cavlc", "CheckBitstreamBuffer", Fmt ("left=%d", left), h);
  }

  // WelsSpatialWriteMbSyn over a slice of 8 macroblocks, each type drawn
  // (skip=1 makes half of a P slice's skipped), then the slice end. The
  // writer's logical size (end) runs out partway for some rows, so
  // CheckBitstreamBuffer fails them; the loop carries on regardless, on a
  // physical buffer big enough for all 8.
  for (int slice : {2, 0})
    for (int refs : {1, 2, 4})
      for (int skip : {0, 1})
        for (int mv : {64, 32767})
          for (int range : {1, 20, 3000})
            for (int density : {3, 12})
              for (int seed = 0; seed < 2; seed++) {
                const EWelsSliceType eType = kSliceTypes[slice];
                if (eType == I_SLICE && (refs != 1 || skip != 0 || mv != 64)) continue;
                const int kiOffsets[3] = {0, 12, 250};
                const int offset = kiOffsets[(seed + refs + range) % 3];
                const int end = (seed == 1 && density == 12) ? 2400 : 16384;
                Rng g (seed);
                WriterCtx c (eType, refs, (uint8_t)offset);
                Bs w (g, 16384, end, (int32_t) (g.Next() % 32));
                c.pSlice->pSliceBsa = &w.bs;
                c.pSlice->uiLastMbQp = (uint8_t) (g.Next() % 52);
                Hasher h;
                MbFixture f;
                for (int m = 0; m < 8; m++) {
                  uint32_t uiType;
                  if (eType == I_SLICE) {
                    uiType = (g.Next() & 1) ? MB_TYPE_INTRA16x16 : MB_TYPE_INTRA4x4;
                  } else if (skip && (g.Next() & 1)) {
                    uiType = MB_TYPE_SKIP;
                  } else {
                    uiType = kPTypes[g.Next() % 7];
                  }
                  FillMb (g, f, &c.pSlice->sMbCacheInfo, uiType, refs, mv, density, range);
                  h.AddI32 (WelsSpatialWriteMbSyn (c.pCtx, c.pSlice, &f.mb));
                  h.AddI32 (f.mb.uiLumaQp);
                  h.AddI32 (f.mb.uiChromaQp);
                  h.AddI32 (c.pSlice->iMbSkipRun);
                  h.AddI32 (c.pSlice->uiLastMbQp);
                }
                WelsWriteSliceEndSyn (c.pSlice, false);
                w.Hash (h);
                Emit ("svc_set_mb_syn_cavlc", "WelsSpatialWriteMbSyn",
                      Fmt ("slice=%d,refs=%d,skip=%d,mv=%d,range=%d,density=%d,offset=%d,end=%d,seed=%d", slice, refs, skip,
                           mv, range, density, offset, end, seed), h);
              }
}
