//-----------------------------------------------------------------------------
// Filename: decoder_tables_unittest.cs
//
// Description: Checks every table ported from codec/decoder (dec_golomb.h,
// decoder_data_tables.cpp, mb_cache.h, vlc_decoder.h, wels_common_basis.h)
// byte for byte against the C build ("dectable" rows in reference/kernels.tsv,
// refharness src/kernels_decoder.cpp).
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 25 Nov 2026  Claude          Created.
// 25 Nov 2026  Claude          D2: the CAVLC and scan tables.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using static SIPSorcery.H264.Decoder.WelsDec;

namespace SIPSorcery.H264.UnitTest;

public unsafe class decoder_tables_unittest
{
    public static IEnumerable<object[]> Rows => KernelFixtures.Group("dectable");

    private static void* TableOf(string name) => name switch
    {
        nameof(g_kuiLeadingZeroTable) => g_kuiLeadingZeroTable,
        nameof(g_kuiPrefix8BitsTable) => g_kuiPrefix8BitsTable,
        nameof(g_kuiZigzagScan) => g_kuiZigzagScan,
        nameof(g_kuiZigzagScan8x8) => g_kuiZigzagScan8x8,
        nameof(g_kuiIdx2CtxSignificantCoeffFlag8x8) => g_kuiIdx2CtxSignificantCoeffFlag8x8,
        nameof(g_kuiIdx2CtxLastSignificantCoeffFlag8x8) => g_kuiIdx2CtxLastSignificantCoeffFlag8x8,
        nameof(g_ksI16PredInfo) => g_ksI16PredInfo,
        nameof(g_ksChromaPredInfo) => g_ksChromaPredInfo,
        nameof(g_ksI4PredInfo) => g_ksI4PredInfo,
        nameof(g_kuiI16CbpTable) => g_kuiI16CbpTable,
        nameof(g_ksInterPMbTypeInfo) => g_ksInterPMbTypeInfo,
        nameof(g_ksInterBMbTypeInfo) => g_ksInterBMbTypeInfo,
        nameof(g_ksInterPSubMbTypeInfo) => g_ksInterPSubMbTypeInfo,
        nameof(g_ksInterBSubMbTypeInfo) => g_ksInterBSubMbTypeInfo,
        nameof(g_ksVuiSampleAspectRatio) => g_ksVuiSampleAspectRatio,
        // D2.
        nameof(g_kuiScan4) => g_kuiScan4,
        nameof(g_kMbNonZeroCountIdx) => g_kMbNonZeroCountIdx,
        nameof(g_kCacheNzcScanIdx) => g_kCacheNzcScanIdx,
        nameof(g_kCache26ScanIdx) => g_kCache26ScanIdx,
        nameof(g_kCache30ScanIdx) => g_kCache30ScanIdx,
        nameof(g_kNonZeroScanIdxC) => g_kNonZeroScanIdxC,
        nameof(g_kuiScan8) => g_kuiScan8,
        nameof(g_kuiLumaDcZigzagScan) => g_kuiLumaDcZigzagScan,
        nameof(g_kuiChromaDcScan) => g_kuiChromaDcScan,
        nameof(g_kuiIntra4x4CbpTable) => g_kuiIntra4x4CbpTable,
        nameof(g_kuiIntra4x4CbpTable400) => g_kuiIntra4x4CbpTable400,
        nameof(g_kuiInterCbpTable) => g_kuiInterCbpTable,
        nameof(g_kuiInterCbpTable400) => g_kuiInterCbpTable400,
        nameof(g_kuiVlcChromaTable) => g_kuiVlcChromaTable,
        nameof(g_kuiVlcTable_0) => g_kuiVlcTable_0,
        nameof(g_kuiVlcTable_0_0) => g_kuiVlcTable_0_0,
        nameof(g_kuiVlcTable_0_1) => g_kuiVlcTable_0_1,
        nameof(g_kuiVlcTable_0_2) => g_kuiVlcTable_0_2,
        nameof(g_kuiVlcTable_0_3) => g_kuiVlcTable_0_3,
        nameof(g_kuiVlcTable_1) => g_kuiVlcTable_1,
        nameof(g_kuiVlcTable_1_0) => g_kuiVlcTable_1_0,
        nameof(g_kuiVlcTable_1_1) => g_kuiVlcTable_1_1,
        nameof(g_kuiVlcTable_1_2) => g_kuiVlcTable_1_2,
        nameof(g_kuiVlcTable_1_3) => g_kuiVlcTable_1_3,
        nameof(g_kuiVlcTable_2) => g_kuiVlcTable_2,
        nameof(g_kuiVlcTable_2_0) => g_kuiVlcTable_2_0,
        nameof(g_kuiVlcTable_2_1) => g_kuiVlcTable_2_1,
        nameof(g_kuiVlcTable_2_2) => g_kuiVlcTable_2_2,
        nameof(g_kuiVlcTable_2_3) => g_kuiVlcTable_2_3,
        nameof(g_kuiVlcTable_2_4) => g_kuiVlcTable_2_4,
        nameof(g_kuiVlcTable_2_5) => g_kuiVlcTable_2_5,
        nameof(g_kuiVlcTable_2_6) => g_kuiVlcTable_2_6,
        nameof(g_kuiVlcTable_2_7) => g_kuiVlcTable_2_7,
        nameof(g_kuiVlcTable_3) => g_kuiVlcTable_3,
        nameof(g_kuiVlcTableNeedMoreBitsThread) => g_kuiVlcTableNeedMoreBitsThread,
        nameof(g_kuiVlcTableMoreBitsCount0) => g_kuiVlcTableMoreBitsCount0,
        nameof(g_kuiVlcTableMoreBitsCount1) => g_kuiVlcTableMoreBitsCount1,
        nameof(g_kuiVlcTableMoreBitsCount2) => g_kuiVlcTableMoreBitsCount2,
        nameof(g_kuiNcMapTable) => g_kuiNcMapTable,
        nameof(g_kuiVlcTrailingOneTotalCoeffTable) => g_kuiVlcTrailingOneTotalCoeffTable,
        nameof(g_kuiTotalZerosTable0) => g_kuiTotalZerosTable0,
        nameof(g_kuiTotalZerosTable1) => g_kuiTotalZerosTable1,
        nameof(g_kuiTotalZerosTable2) => g_kuiTotalZerosTable2,
        nameof(g_kuiTotalZerosTable3) => g_kuiTotalZerosTable3,
        nameof(g_kuiTotalZerosTable4) => g_kuiTotalZerosTable4,
        nameof(g_kuiTotalZerosTable5) => g_kuiTotalZerosTable5,
        nameof(g_kuiTotalZerosTable6) => g_kuiTotalZerosTable6,
        nameof(g_kuiTotalZerosTable7) => g_kuiTotalZerosTable7,
        nameof(g_kuiTotalZerosTable8) => g_kuiTotalZerosTable8,
        nameof(g_kuiTotalZerosTable9) => g_kuiTotalZerosTable9,
        nameof(g_kuiTotalZerosTable10) => g_kuiTotalZerosTable10,
        nameof(g_kuiTotalZerosTable11) => g_kuiTotalZerosTable11,
        nameof(g_kuiTotalZerosTable12) => g_kuiTotalZerosTable12,
        nameof(g_kuiTotalZerosTable13) => g_kuiTotalZerosTable13,
        nameof(g_kuiTotalZerosTable14) => g_kuiTotalZerosTable14,
        nameof(g_kuiTotalZerosBitNumMap) => g_kuiTotalZerosBitNumMap,
        nameof(g_kuiTotalZerosChromaTable0) => g_kuiTotalZerosChromaTable0,
        nameof(g_kuiTotalZerosChromaTable1) => g_kuiTotalZerosChromaTable1,
        nameof(g_kuiTotalZerosChromaTable2) => g_kuiTotalZerosChromaTable2,
        nameof(g_kuiTotalZerosBitNumChromaMap) => g_kuiTotalZerosBitNumChromaMap,
        nameof(g_kuiZeroLeftTable0) => g_kuiZeroLeftTable0,
        nameof(g_kuiZeroLeftTable1) => g_kuiZeroLeftTable1,
        nameof(g_kuiZeroLeftTable2) => g_kuiZeroLeftTable2,
        nameof(g_kuiZeroLeftTable3) => g_kuiZeroLeftTable3,
        nameof(g_kuiZeroLeftTable4) => g_kuiZeroLeftTable4,
        nameof(g_kuiZeroLeftTable5) => g_kuiZeroLeftTable5,
        nameof(g_kuiZeroLeftTable6) => g_kuiZeroLeftTable6,
        nameof(g_kuiZeroLeftBitNumMap) => g_kuiZeroLeftBitNumMap,
        _ => throw new ArgumentException($"No C# table for {name}."),
    };

    [Theory]
    [MemberData(nameof(Rows))]
    public void Table_MatchesCBuild(string kernel, string kase, string sha1)
    {
        void* table = TableOf(kernel);
        int bytes = new KernelFixture("dectable", kernel, kase, sha1).Param("bytes");

        Assert.Equal(bytes, StaticTable.ByteSize(table));
        Assert.Equal(sha1, FixtureHasher.Of(new ReadOnlySpan<byte>(table, bytes)));
    }

    [Fact]
    public void Group_HasEveryPortedTable()
    {
        Assert.Equal(83, KernelFixtures.Group("dectable").Count());
    }
}
