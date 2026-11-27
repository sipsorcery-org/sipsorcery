//-----------------------------------------------------------------------------
// Filename: common_tables_unittest.cs
//
// Description: Checks every table ported from codec/common/src/common_tables.cpp
// byte for byte against the C build (size and SHA1 in reference/kernels.tsv).
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 24 Nov 2026  Claude          Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Xunit;
using static SIPSorcery.H264.WelsCommon;

namespace SIPSorcery.H264.UnitTest;

public unsafe class common_tables_unittest
{
    public static IEnumerable<object[]> Rows => KernelFixtures.Group("table");

    private static void* TableOf(string name) => name switch
    {
        nameof(g_kuiMbCountScan4Idx) => g_kuiMbCountScan4Idx,
        nameof(g_kuiCache48CountScan4Idx) => g_kuiCache48CountScan4Idx,
        nameof(g_kuiMatrixV) => g_kuiMatrixV,
        nameof(g_kuiCache30ScanIdx) => g_kuiCache30ScanIdx,
        nameof(g_kuiChromaQpTable) => g_kuiChromaQpTable,
        nameof(g_keTypeMap) => g_keTypeMap,
        nameof(g_kuiDequantScaling4x4Default) => g_kuiDequantScaling4x4Default,
        nameof(g_kuiDequantScaling8x8Default) => g_kuiDequantScaling8x8Default,
        nameof(g_kuiDequantCoeff) => g_kuiDequantCoeff,
        nameof(g_kuiDequantCoeff8x8) => g_kuiDequantCoeff8x8,
        nameof(g_ksLevelLimits) => g_ksLevelLimits,
        nameof(g_kuiLevelMaps) => g_kuiLevelMaps,
        nameof(g_kiCabacGlobalContextIdx) => g_kiCabacGlobalContextIdx,
        nameof(g_kuiCabacRangeLps) => g_kuiCabacRangeLps,
        nameof(g_kuiStateTransTable) => g_kuiStateTransTable,
        nameof(g_kuiGolombUELength) => g_kuiGolombUELength,
        _ => throw new ArgumentException($"No C# table for {name}."),
    };

    [Theory]
    [MemberData(nameof(Rows))]
    public void Table_MatchesCBuild(string kernel, string kase, string sha1)
    {
        void* table = TableOf(kernel);
        int bytes = new KernelFixture("table", kernel, kase, sha1).Param("bytes");

        Assert.Equal(bytes, StaticTable.ByteSize(table));
        Assert.Equal(sha1, FixtureHasher.Of(new ReadOnlySpan<byte>(table, bytes)));
    }

    [Fact]
    public void MultiDimensionalTables_IndexLikeC()
    {
        // g_kuiMatrixV[1][2][2] = 35, g_kuiDequantCoeff[51][7] = 5888, g_kiCabacGlobalContextIdx[6][0] = {-28, 127}.
        Assert.Equal(35, g_kuiMatrixV[1][2][2]);
        Assert.Equal(5888, g_kuiDequantCoeff[51][7]);
        Assert.Equal(-28, g_kiCabacGlobalContextIdx[6][0][0]);
        Assert.Equal(127, g_kiCabacGlobalContextIdx[6][0][1]);

        // A row pointer, as decoder code takes one: const uint16_t* p = g_kuiDequantCoeff[iQp];
        ushort* row = (ushort*)&g_kuiDequantCoeff[26];
        Assert.Equal(208, row[0]);
        Assert.Equal(320, row[5]);

        Assert.Equal(ELevelIdc.LEVEL_3_1, g_ksLevelLimits[9].uiLevelIdc);
        Assert.Equal(3600u, g_ksLevelLimits[9].uiMaxFS);
        Assert.Equal(EVclType.VCL, g_keTypeMap[20][1]);
        Assert.Equal(EVclType.NOT_APP, g_keTypeMap[2][1]);
        Assert.True(IS_VCL_NAL(EWelsNalUnitType.NAL_UNIT_CODED_SLICE_IDR, 0));
        Assert.False(IS_VCL_NAL(EWelsNalUnitType.NAL_UNIT_SPS, 1));
    }

    [Fact]
    public void StaticTable_ZeroFillsMissingInitializers()
    {
        // C zero-fills the elements a short initializer leaves out.
        int* t = StaticTable.Of<int>(4, [7, 8]);
        Assert.Equal(16, StaticTable.ByteSize(t));
        Assert.Equal(new[] { 7, 8, 0, 0 }, new ReadOnlySpan<int>(t, 4).ToArray());
        Assert.Throws<ArgumentException>(() => { StaticTable.Of<int>(1, [1, 2]); });
    }
}
