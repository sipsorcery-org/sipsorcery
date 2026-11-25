//-----------------------------------------------------------------------------
// Filename: decoder_tables_unittest.cs
//
// Description: Checks every table ported from codec/decoder (dec_golomb.h,
// decoder_data_tables.cpp, wels_common_basis.h) byte for byte against the C
// build ("dectable" rows in reference/kernels.tsv, refharness
// src/kernels_decoder.cpp).
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 25 Nov 2026  Claude          Created.
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
        Assert.Equal(15, KernelFixtures.Group("dectable").Count());
    }
}
