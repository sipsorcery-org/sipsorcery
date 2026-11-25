// kernels_decoder.cpp: fixtures for the ported codec/decoder tables and kernels.
//
// "dectable" rows: every decoder table ported so far, byte for byte (C# side:
// test/SIPSorcery.H264.UnitTest/decoder_tables_unittest.cs). Most are static
// const in a header, so this file sees its own copy, which is the same bytes.

#include <cstdio>
#include <string>

#include "fixture.h"

#include "dec_golomb.h"
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
}
