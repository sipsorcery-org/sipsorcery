//-----------------------------------------------------------------------------
// Filename: encoder_tables_unittest.cs
//
// Description: Checks every ported encoder table byte for byte against the C
// build ("enctable" rows in reference/kernels.tsv, refharness
// src/kernels_encoder.cpp KernelsEncoderTables).
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 26 Nov 2026  Claude          Created (E2).
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using static SIPSorcery.H264.Encoder.WelsEnc;

namespace SIPSorcery.H264.UnitTest;

public unsafe class encoder_tables_unittest
{
    public static IEnumerable<object[]> Rows => KernelFixtures.Group("enctable");

    private static void* TableOf(string name) => name switch
    {
        // encode_mb_aux.cpp.
        nameof(g_kiQuantInterFF) => g_kiQuantInterFF,
        nameof(g_kiQuantMF) => g_kiQuantMF,
        // encoder_data_tables.cpp.
        nameof(g_kuiSmb4AddrIn256) => g_kuiSmb4AddrIn256,
        nameof(g_kuiCache12_8x8RefIdx) => g_kuiCache12_8x8RefIdx,
        nameof(g_kiQpCostTable) => g_kiQpCostTable,
        nameof(g_kiMapModeI16x16) => g_kiMapModeI16x16,
        nameof(g_kiMapModeIntraChroma) => g_kiMapModeIntraChroma,
        nameof(g_kuiVlcCoeffToken) => g_kuiVlcCoeffToken,
        nameof(g_kuiVlcTotalZeros) => g_kuiVlcTotalZeros,
        nameof(g_kuiVlcTotalZerosChromaDc) => g_kuiVlcTotalZerosChromaDc,
        nameof(g_kuiVlcRunBefore) => g_kuiVlcRunBefore,
        nameof(g_kuiEncNcMapTable) => g_kuiEncNcMapTable,
        nameof(g_kuiTemporalIdListTable) => g_kuiTemporalIdListTable,
        _ => throw new ArgumentException($"No C# table for {name}."),
    };

    [Theory]
    [MemberData(nameof(Rows))]
    public void Table_MatchesCBuild(string kernel, string kase, string sha1)
    {
        void* table = TableOf(kernel);
        int bytes = new KernelFixture("enctable", kernel, kase, sha1).Param("bytes");

        Assert.Equal(bytes, StaticTable.ByteSize(table));
        Assert.Equal(sha1, FixtureHasher.Of(new ReadOnlySpan<byte>(table, bytes)));
    }

    [Fact]
    public void Group_HasEveryPortedTable()
    {
        Assert.Equal(13, KernelFixtures.Group("enctable").Count());
    }

    /// <summary>The intra FF rows are the inter rows six QPs on (#define g_iQuantIntraFF).</summary>
    [Fact]
    public void QuantIntraFF_IsInterFFPlusSix()
    {
        Assert.True(g_iQuantIntraFF == g_kiQuantInterFF + 6);
        Assert.Equal(299, g_iQuantIntraFF[51][0]);
    }
}
