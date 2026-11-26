//-----------------------------------------------------------------------------
// Filename: svc_base_layer_md_unittest.cs
//
// Description: Checks PredIntra4x4Mode of svc_base_layer_md against the C
// build ("svc_base_layer_md" rows in reference/kernels.tsv, refharness
// src/islice_encoder.cpp KernelsSvcBaseLayerMd). The intra mode decision as a
// whole is checked through WelsISliceMdEnc (svc_encode_slice_unittest.cs).
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 26 Nov 2026  Claude          Created (E4).
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using static SIPSorcery.H264.Encoder.WelsEnc;
using static SIPSorcery.H264.WelsCommon;

namespace SIPSorcery.H264.UnitTest;

public unsafe class svc_base_layer_md_unittest
{
    private const string GroupName = "svc_base_layer_md";

    public static IEnumerable<object[]> Rows => KernelFixtures.Group(GroupName);

    [Theory]
    [MemberData(nameof(Rows))]
    public void Kernel_MatchesCBuild(string kernel, string kase, string sha1)
    {
        var row = new KernelFixture(GroupName, kernel, kase, sha1);
        string actual = kernel switch
        {
            nameof(PredIntra4x4Mode) => PredMode(row),
            _ => throw new ArgumentException($"No C# kernel {kernel}."),
        };
        Assert.Equal(sha1, actual);
    }

    // Each 4x4 block's place in the 48-entry cache, the modes drawn from -1
    // (unavailable) to 8.
    private static string PredMode(KernelFixture row)
    {
        var g = new FixtureRng((uint)row.Param("seed"));
        sbyte* iModes = stackalloc sbyte[48];
        for (int i = 0; i < 48; i++)
        {
            iModes[i] = (sbyte)((int)(g.Next() % 10) - 1);
        }
        using var h = new FixtureHasher();
        for (int i = 0; i < 16; i++)
        {
            h.AddI32(PredIntra4x4Mode(iModes, g_kuiCache48CountScan4Idx[i]));
        }
        return h.Hex();
    }

    [Fact]
    public void Group_HasEveryKernel()
    {
        var kernels = KernelFixtures.Group(GroupName).Select(r => (string)r[0]).Distinct().OrderBy(k => k, StringComparer.Ordinal);
        Assert.Equal(new[] { nameof(PredIntra4x4Mode) }, kernels);
    }
}
