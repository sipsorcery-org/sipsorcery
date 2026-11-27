// The slice threads' load balancing fixtures (P5.7): the functions of
// codec/encoder/core/src/slice_multi_threading.cpp that resize the slices of a
// threaded SM_FIXEDSLCNUM_SLICE encode from how long each slice took to code.
// The encoder measures those times with WelsTime(), so a stream with load
// balancing on can't be reproduced; these rows give the functions made-up
// times instead. Mirrored by the C# test slice_multi_threading_unittest.cs.
//
//   slice_mt  NeedDynamicAdjust: one to eight slices, times of each kind (none
//             yet, equal, random, one slow slice, small jitter either side of
//             the threshold), and the NULL list and NULL slice.
//             CalcSliceComplexRatio: random slice sizes and times, zero times,
//             times so long every ratio is 0, and equal ones.
//             DynamicAdjustSlicing: frame sizes from one macroblock column to
//             32752x32, one to four slices (only an even count is adjusted),
//             rate control off and on with GOMs of one to three rows and none,
//             even and random partitions, random and lopsided ratios.
//             AdjustBaseLayer: the encoder's order over six frames on content
//             whose top third costs eight times the rest: AdjustBaseLayer, then
//             UpdateMbListNeighborParallel on each slice while the partition is
//             marked changed, each slice's macroblock count as
//             SetSliceBoundaryInfo sets it, the times, CalcSliceComplexRatio.

#include <cstdarg>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>
#include <vector>

#include "fixture.h"
#include "encoder_context.h"
#include "slice_multi_threading.h"
#include "svc_enc_slice_segment.h"
#include "svc_encode_slice.h"

using namespace fixture;
using namespace WelsEnc;

namespace {

void QuietLog (void*, const int32_t, const char*, va_list) {}

std::string Fmt (const char* f, ...) {
  char b[256];
  va_list vl;
  va_start (vl, f);
  vsnprintf (b, sizeof b, f, vl);
  va_end (vl);
  return b;
}

// The layer, its slices and the context the functions read, from calloc as the
// C# side builds them from NativeMemory.
struct Layer {
  int32_t iMbWidth, iMbHeight, iSliceNum;
  std::vector<SSlice> slices;
  std::vector<SSlice*> list;
  std::vector<int32_t> first, count;
  std::vector<uint16_t> map;
  std::vector<SMB> mbs;
  SDqLayer* pDq;
  SDqLayer* pDqList[1];
  SWelsSvcCodingParam* pParam;
  SWelsSvcRc* pRc;
  sWelsEncCtx* pCtx;

  Layer (int32_t w, int32_t h, int32_t n, int32_t iRCMode, int32_t iGom)
    : iMbWidth (w), iMbHeight (h), iSliceNum (n), slices (n), list (n), first (n), count (n), map (w * h),
      mbs (w * h) {
    memset (slices.data(), 0, sizeof (SSlice) * n);
    memset (mbs.data(), 0, sizeof (SMB) * w * h);
    for (int32_t i = 0; i < n; i++) list[i] = &slices[i];
    for (int32_t i = 0; i < w * h; i++) {
      mbs[i].iMbXY = i;
      mbs[i].iMbX = (int16_t) (i % w);
      mbs[i].iMbY = (int16_t) (i / w);
    }
    pDq = (SDqLayer*)calloc (1, sizeof (SDqLayer));
    pDq->sSliceEncCtx.iMbWidth = w;
    pDq->sSliceEncCtx.iMbHeight = h;
    pDq->sSliceEncCtx.iMbNumInFrame = w * h;
    pDq->sSliceEncCtx.iSliceNumInFrame = n;
    pDq->sSliceEncCtx.pOverallMbMap = map.data();
    pDq->ppSliceInLayer = list.data();
    pDq->pFirstMbIdxOfSlice = first.data();
    pDq->pCountMbNumInSlice = count.data();
    pDq->sMbDataP = mbs.data();
    pDqList[0] = pDq;
    pParam = (SWelsSvcCodingParam*)calloc (1, sizeof (SWelsSvcCodingParam));
    pParam->iRCMode = (RC_MODES)iRCMode;
    pRc = (SWelsSvcRc*)calloc (1, sizeof (SWelsSvcRc));
    pRc->iNumberMbGom = iGom;
    pCtx = (sWelsEncCtx*)calloc (1, sizeof (sWelsEncCtx));
    pCtx->pSvcParam = pParam;
    pCtx->pWelsSvcRc = pRc;
    pCtx->ppDqLayerList = pDqList;
    pCtx->sLogCtx.pfLog = QuietLog;
  }
  ~Layer() {
    free (pCtx);
    free (pRc);
    free (pParam);
    free (pDq);
  }

  // Each slice's run, its macroblocks marked in the map, and the slice's count as
  // SetSliceBoundaryInfo sets it.
  void Partition (const std::vector<int32_t>& runs) {
    int32_t iFirst = 0;
    for (int32_t i = 0; i < iSliceNum; i++) {
      first[i] = iFirst;
      count[i] = runs[i];
      for (int32_t k = 0; k < runs[i]; k++) map[iFirst + k] = (uint16_t)i;
      slices[i].iCountMbNumInSlice = runs[i];
      iFirst += runs[i];
    }
  }

  void HashPartition (Hasher& h) {
    for (int32_t i = 0; i < iSliceNum; i++) {
      h.AddI32 (first[i]);
      h.AddI32 (count[i]);
    }
    for (int32_t i = 0; i < iMbWidth * iMbHeight; i++) h.AddI32 (map[i]);
    h.AddI32 (pDq->bNeedAdjustingSlicing ? 1 : 0);
  }
};

// Every slice gets kiMbs / n, the last the rest, as the fixed slices start.
std::vector<int32_t> EvenRuns (int32_t kiMbs, int32_t n) {
  std::vector<int32_t> runs (n, kiMbs / n);
  runs[n - 1] = kiMbs - (n - 1) * (kiMbs / n);
  return runs;
}

// Every slice gets one, then each of the rest goes to a random slice.
std::vector<int32_t> RandomRuns (Rng& r, int32_t kiMbs, int32_t n) {
  std::vector<int32_t> runs (n, 1);
  for (int32_t k = n; k < kiMbs; k++) runs[r.Next() % n]++;
  return runs;
}

void NeedRows() {
  for (int32_t n = 1; n <= 8; n++) {
    for (int32_t kind = 0; kind <= 6; kind++) {
      const int32_t kiSeeds = (kind >= 2 && kind <= 4) ? 4 : 1;
      for (int32_t seed = 0; seed < kiSeeds; seed++) {
        Rng r ((uint32_t) (n * 1000 + kind * 10 + seed));
        Layer l (11, 9, n, RC_OFF_MODE, 0);
        if (kind == 1) {
          const uint32_t t = 1 + r.Next() % 50000;
          for (int32_t i = 0; i < n; i++) l.slices[i].uiSliceConsumeTime = t;
        } else if (kind == 2) {
          for (int32_t i = 0; i < n; i++) l.slices[i].uiSliceConsumeTime = r.Next() % 50001;
        } else if (kind == 3) {
          const uint32_t t = 1 + r.Next() % 50000;
          for (int32_t i = 0; i < n; i++) l.slices[i].uiSliceConsumeTime = t;
          const int32_t iSlow = (int32_t) (r.Next() % n);
          l.slices[iSlow].uiSliceConsumeTime = t * (2 + r.Next() % 4);
        } else if (kind == 4) {
          const int32_t iJitter = (int32_t) (r.Next() % 1601);
          for (int32_t i = 0; i < n; i++)
            l.slices[i].uiSliceConsumeTime = (uint32_t) (10000 - iJitter + (int32_t) (r.Next() % (2 * iJitter + 1)));
        } else if (kind >= 5) {
          for (int32_t i = 0; i < n; i++) l.slices[i].uiSliceConsumeTime = 1 + r.Next() % 50000;
          if (kind == 6) l.list[r.Next() % n] = NULL;
        }
        const int32_t iRet = NeedDynamicAdjust (kind == 5 ? NULL : l.list.data(), n);
        Hasher h;
        h.AddI32 (iRet);
        Emit ("slice_mt", "NeedDynamicAdjust", Fmt ("n=%d,kind=%d,seed=%d", n, kind, seed), h);
      }
    }
  }
}

void RatioRows() {
  const int32_t kiMbs[] = {0, 99, 396, 3600};
  for (int32_t n = 1; n <= 8; n++) {
    for (int32_t m = 0; m < 4; m++) {
      const int32_t iMbs = kiMbs[m] == 0 ? n : kiMbs[m];
      for (int32_t kind = 0; kind <= 3; kind++) {
        for (int32_t seed = 0; seed < 2; seed++) {
          Rng r ((uint32_t) (n * 1000 + m * 100 + kind * 10 + seed));
          Layer l (iMbs, 1, n, RC_OFF_MODE, 0);
          l.Partition (RandomRuns (r, iMbs, n));
          const uint32_t kuiEqual = 1 + r.Next() % 50000;
          for (int32_t i = 0; i < n; i++) {
            uint32_t t;
            if (kind == 0) t = 1 + r.Next() % 50000;
            else if (kind == 1) t = (r.Next() % 4 == 0) ? 0 : 1 + r.Next() % 50000;
            else if (kind == 2) t = 0x100000u + r.Next() % 0x7FF00000u;
            else t = kuiEqual;
            l.slices[i].uiSliceConsumeTime = t;
          }
          CalcSliceComplexRatio (l.pDq);
          Hasher h;
          for (int32_t i = 0; i < n; i++) h.AddI32 (l.slices[i].iSliceComplexRatio);
          Emit ("slice_mt", "CalcSliceComplexRatio", Fmt ("n=%d,mbs=%d,kind=%d,seed=%d", n, iMbs, kind, seed), h);
        }
      }
    }
  }
}

struct Size {
  int32_t w, h;
};
const Size kSizes[] = {{11, 9}, {20, 15}, {40, 30}, {80, 45}, {1, 4}, {2047, 2}};

void AdjustRows() {
  for (const Size& s : kSizes) {
    for (int32_t n = 1; n <= 4; n++) {
      for (int32_t rc = 0; rc <= 4; rc++) {
        // rc 0: off; 1 to 3: on with GOMs of that many rows; 4: on with no GOM.
        const int32_t iRCMode = rc == 0 ? RC_OFF_MODE : RC_BITRATE_MODE;
        const int32_t iGom = rc == 0 ? 0 : rc == 4 ? 0 : rc * s.w;
        for (int32_t kind = 0; kind <= 3; kind++) {
          for (int32_t seed = 0; seed < 2; seed++) {
            const int32_t iMbs = s.w * s.h;
            if (iMbs < n) continue;
            Rng r ((uint32_t) (s.w * 100000 + s.h * 1000 + n * 100 + rc * 10 + kind * 2 + seed));
            Layer l (s.w, s.h, n, iRCMode, iGom);
            // kind 0, 1: even partition; 2, 3: random. Even kinds: random ratios;
            // odd: one slice takes them all.
            l.Partition ((kind < 2) ? EvenRuns (iMbs, n) : RandomRuns (r, iMbs, n));
            const int32_t iHot = (int32_t) (r.Next() % n);
            for (int32_t i = 0; i < n; i++)
              l.slices[i].iSliceComplexRatio = (kind & 1) ? (i == iHot ? 100 : 0) : (int32_t) (r.Next() % 101);
            DynamicAdjustSlicing (l.pCtx, l.pDq, 0);
            Hasher h;
            l.HashPartition (h);
            Emit ("slice_mt", "DynamicAdjustSlicing",
                  Fmt ("w=%d,h=%d,n=%d,rc=%d,kind=%d,seed=%d", s.w, s.h, n, rc, kind, seed), h);
          }
        }
      }
    }
  }
}

void FrameRows() {
  for (const Size& s : kSizes) {
    for (int32_t n = 1; n <= 4; n++) {
      for (int32_t rc = 0; rc <= 1; rc++) {
        const int32_t iMbs = s.w * s.h;
        if (iMbs < n) continue;
        const int32_t iRCMode = rc == 0 ? RC_OFF_MODE : RC_BITRATE_MODE;
        for (int32_t seed = 0; seed < 2; seed++) {
          Rng r ((uint32_t) (s.w * 100000 + s.h * 1000 + n * 100 + rc * 10 + seed));
          Layer l (s.w, s.h, n, iRCMode, rc == 0 ? 0 : s.w);
          l.Partition (EvenRuns (iMbs, n));
          std::vector<uint32_t> cost (iMbs);
          for (int32_t i = 0; i < iMbs; i++) {
            cost[i] = 1 + r.Next() % 20;
            if (i / s.w < (s.h + 2) / 3) cost[i] *= 8;
          }
          Hasher h;
          for (int32_t f = 0; f < 6; f++) {
            h.AddI32 (AdjustBaseLayer (l.pCtx));
            h.AddI32 (l.pCtx->pCurDqLayer == l.pDq ? 1 : 0);
            l.HashPartition (h);
            if (l.pDq->bNeedAdjustingSlicing) {
              for (int32_t i = 0; i < n; i++) UpdateMbListNeighborParallel (l.pDq, l.mbs.data(), i);
              for (int32_t i = 0; i < iMbs; i++) {
                h.AddI32 (l.mbs[i].uiSliceIdc);
                h.AddI32 (l.mbs[i].uiNeighborAvail);
              }
            }
            for (int32_t i = 0; i < n; i++) {
              l.slices[i].iCountMbNumInSlice = l.count[i];
              uint32_t t = r.Next() % 50;
              for (int32_t k = 0; k < l.count[i]; k++) t += cost[l.first[i] + k];
              l.slices[i].uiSliceConsumeTime = t;
            }
            CalcSliceComplexRatio (l.pDq);
            for (int32_t i = 0; i < n; i++) h.AddI32 (l.slices[i].iSliceComplexRatio);
          }
          Emit ("slice_mt", "AdjustBaseLayer", Fmt ("w=%d,h=%d,n=%d,rc=%d,seed=%d", s.w, s.h, n, rc, seed), h);
        }
      }
    }
  }
}

}  // namespace

void KernelsSliceMt() {
  NeedRows();
  RatioRows();
  AdjustRows();
  FrameRows();
}
