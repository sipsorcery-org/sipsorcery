//-----------------------------------------------------------------------------
// Filename: encode_mb_aux_unittest.cs
//
// Description: Checks the encode_mb_aux kernels (quantisation, the forward
// DCT and Hadamard transforms, the scans, the single-coefficient cost and the
// non-zero count) against the C build ("encode_mb_aux" rows in
// reference/kernels.tsv, refharness src/kernels_encoder.cpp KernelsEncodeMbAux,
// whose buffer layouts this must follow).
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

public unsafe class encode_mb_aux_unittest
{
    private const string GroupName = "encode_mb_aux";

    public static IEnumerable<object[]> Rows => KernelFixtures.Group(GroupName);

    [Theory]
    [MemberData(nameof(Rows))]
    public void Kernel_MatchesCBuild(string kernel, string kase, string sha1)
    {
        var row = new KernelFixture(GroupName, kernel, kase, sha1);
        string actual = kernel switch
        {
            nameof(WelsQuant4x4_c) => Quant(&WelsQuant4x4_c, 16, row),
            nameof(WelsQuantFour4x4_c) => Quant(&WelsQuantFour4x4_c, 64, row),
            nameof(WelsQuantFour4x4Max_c) => QuantMax(&WelsQuantFour4x4Max_c, row),
            nameof(WelsQuant4x4Dc_c) => QuantDc(&WelsQuant4x4Dc_c, row),
            nameof(WelsHadamardQuant2x2Skip_c) => Skip(&WelsHadamardQuant2x2Skip_c, row, out _),
            nameof(WelsHadamardQuant2x2_c) => Hadamard2x2(&WelsHadamardQuant2x2_c, row),
            nameof(WelsHadamardT4Dc_c) => HadamardT4Dc(&WelsHadamardT4Dc_c, row),
            nameof(WelsDctT4_c) => Dct(&WelsDctT4_c, 4, row),
            nameof(WelsDctFourT4_c) => Dct(&WelsDctFourT4_c, 8, row),
            nameof(WelsScan4x4DcAc_c) => Scan(&WelsScan4x4DcAc_c, row),
            nameof(WelsScan4x4Ac_c) => Scan(&WelsScan4x4Ac_c, row),
            nameof(WelsScan4x4Dc) => Scan(&WelsScan4x4Dc, row),
            nameof(WelsCalculateSingleCtr4x4_c) => Count(&WelsCalculateSingleCtr4x4_c, row, out _),
            nameof(WelsGetNoneZeroCount_c) => Count(&WelsGetNoneZeroCount_c, row, out _),
            _ => throw new ArgumentException($"No C# kernel {kernel}."),
        };
        Assert.Equal(sha1, actual);
    }

    // The FF/MF rows for qp (intra FF is g_kiQuantInterFF + 6) and the
    // (FF << 1, MF >> 1) the encoder passes the DC and 2x2 kernels.
    private static void QuantParams(KernelFixture row, out short* pFF, out short* pMF, out short iFF, out short iMF)
    {
        int qp = row.Param("qp");
        pFF = row.Param("intra") != 0 ? (short*)(g_iQuantIntraFF + qp) : (short*)(g_kiQuantInterFF + qp);
        pMF = (short*)(g_kiQuantMF + qp);
        iFF = (short)(pFF[0] << 1);
        iMF = (short)(pMF[0] >> 1);
    }

    internal static string Quant(delegate*<short*, short*, short*, void> f, int n, KernelFixture row)
    {
        QuantParams(row, out short* pFF, out short* pMF, out _, out _);
        var r = new FixtureRng((uint)row.Param("seed"));
        using var dct = new CoeffBuffer(ref r, n, row.Param("range"));

        f(dct.Data, pFF, pMF);

        using var h = new FixtureHasher();
        h.Add(dct.Bytes);
        return h.Hex();
    }

    internal static string QuantMax(delegate*<short*, short*, short*, short*, void> f, KernelFixture row)
    {
        QuantParams(row, out short* pFF, out short* pMF, out _, out _);
        var r = new FixtureRng((uint)row.Param("seed"));
        using var dct = new CoeffBuffer(ref r, 64, row.Param("range"));
        using var max = new CoeffBuffer(ref r, 4, 32768);

        f(dct.Data, pFF, pMF, max.Data);

        using var h = new FixtureHasher();
        h.Add(dct.Bytes);
        h.Add(max.Bytes);
        return h.Hex();
    }

    internal static string QuantDc(delegate*<short*, short, short, void> f, KernelFixture row)
    {
        QuantParams(row, out _, out _, out short iFF, out short iMF);
        var r = new FixtureRng((uint)row.Param("seed"));
        using var dct = new CoeffBuffer(ref r, 16, row.Param("range"));

        f(dct.Data, iFF, iMF);

        using var h = new FixtureHasher();
        h.Add(dct.Bytes);
        return h.Hex();
    }

    // Reads pRs[0, 16, 32, 48]. The "delta" rows sit at the threshold:
    // pRs[0] = threshold + delta and pRs[16, 32, 48] = 0 make all four
    // Hadamard outputs threshold + delta.
    internal static string Skip(delegate*<short*, short, short, int> f, KernelFixture row, out int ret)
    {
        QuantParams(row, out _, out _, out short iFF, out short iMF);
        bool boundary = row.Case.Contains("delta=");
        var r = new FixtureRng(boundary ? 0u : (uint)row.Param("seed"));
        using var rs = new CoeffBuffer(ref r, 64, boundary ? 300 : row.Param("range"));
        if (boundary)
        {
            short iThreshold = (short)(((1 << 16) - 1) / iMF - iFF);
            rs.Data[0] = (short)(iThreshold + row.Param("delta"));
            rs.Data[16] = rs.Data[32] = rs.Data[48] = 0;
        }

        ret = f(rs.Data, iFF, iMF);

        using var h = new FixtureHasher();
        h.AddI32(ret);
        h.Add(rs.Bytes);
        return h.Hex();
    }

    internal static string Hadamard2x2(delegate*<short*, short, short, short*, short*, int> f, KernelFixture row)
    {
        QuantParams(row, out _, out _, out short iFF, out short iMF);
        var r = new FixtureRng((uint)row.Param("seed"));
        using var rs = new CoeffBuffer(ref r, 64, row.Param("range"));
        using var dct = new CoeffBuffer(ref r, 4, 32768);
        using var block = new CoeffBuffer(ref r, 4, 32768);

        int ret = f(rs.Data, iFF, iMF, dct.Data, block.Data);

        using var h = new FixtureHasher();
        h.AddI32(ret);
        h.Add(rs.Bytes);
        h.Add(dct.Bytes);
        h.Add(block.Bytes);
        return h.Hex();
    }

    internal static string HadamardT4Dc(delegate*<short*, short*, void> f, KernelFixture row)
    {
        var r = new FixtureRng((uint)row.Param("seed"));
        using var dct = new CoeffBuffer(ref r, 256, row.Param("range"));
        using var dc = new CoeffBuffer(ref r, 16, 32768);

        f(dc.Data, dct.Data);

        using var h = new FixtureHasher();
        h.Add(dc.Bytes);
        h.Add(dct.Bytes);
        return h.Hex();
    }

    // Random source and prediction planes, the block at their origin.
    internal static string Dct(delegate*<short*, byte*, int, byte*, int, void> f, int n, KernelFixture row)
    {
        int s1 = row.Param("s1"), s2 = row.Param("s2");
        var r = new FixtureRng((uint)row.Param("seed"));
        using var p1 = EncoderFixtures.Plane(ref r, s1 * n);
        using var p2 = EncoderFixtures.Plane(ref r, s2 * n);
        using var dct = new CoeffBuffer(ref r, n * n, 32768);

        f(dct.Data, p1.Ptr, s1, p2.Ptr, s2);

        using var h = new FixtureHasher();
        h.Add(dct.Bytes);
        h.Add(p1.Span);
        h.Add(p2.Span);
        return h.Hex();
    }

    internal static string Scan(delegate*<short*, short*, void> f, KernelFixture row)
    {
        var r = new FixtureRng((uint)row.Param("seed"));
        using var level = new CoeffBuffer(ref r, 16, 32768);
        using var dct = new CoeffBuffer(ref r, 16, 32768);

        f(level.Data, dct.Data);

        using var h = new FixtureHasher();
        h.Add(level.Bytes);
        h.Add(dct.Bytes);
        return h.Hex();
    }

    // Sparse blocks: each coefficient is kept with probability density/16.
    internal static string Count(delegate*<short*, int> f, KernelFixture row, out int ret)
    {
        int density = row.Param("density");
        var r = new FixtureRng((uint)row.Param("seed"));
        using var dct = new CoeffBuffer(ref r, 16, 300);
        for (int i = 0; i < 16; i++)
        {
            if ((int)(r.Next() % 16) >= density)
            {
                dct.Data[i] = 0;
            }
        }

        ret = f(dct.Data);

        using var h = new FixtureHasher();
        h.AddI32(ret);
        h.Add(dct.Bytes);
        return h.Hex();
    }

    private static IEnumerable<KernelFixture> RowsOf(string kernel) =>
        KernelFixtures.All.Where(r => r.Group == GroupName && r.Kernel == kernel);

    /// <summary>
    /// The skip rows must reach both answers, and each threshold row must
    /// answer 1 only above the threshold, or they would not test the `>`.
    /// </summary>
    [Fact]
    public void SkipRows_DecideBothWays()
    {
        var answers = RowsOf(nameof(WelsHadamardQuant2x2Skip_c)).Select(r => { Skip(&WelsHadamardQuant2x2Skip_c, r, out int ret); return ret; }).ToHashSet();
        Assert.Equal(new HashSet<int> { 0, 1 }, answers);

        var boundary = RowsOf(nameof(WelsHadamardQuant2x2Skip_c)).Where(r => r.Case.Contains("delta=")).ToList();
        Assert.Equal(30, boundary.Count);
        foreach (var r in boundary)
        {
            Skip(&WelsHadamardQuant2x2Skip_c, r, out int ret);
            Assert.Equal(r.Param("delta") > 0 ? 1 : 0, ret);
        }
    }

    /// <summary>The sparse rows must reach every run length in the cost table, empty and full blocks.</summary>
    [Fact]
    public void CountRows_SpanTheRange()
    {
        var ctr = RowsOf(nameof(WelsCalculateSingleCtr4x4_c)).Select(r => { Count(&WelsCalculateSingleCtr4x4_c, r, out int v); return v; }).ToHashSet();
        var nzc = RowsOf(nameof(WelsGetNoneZeroCount_c)).Select(r => { Count(&WelsGetNoneZeroCount_c, r, out int v); return v; }).ToHashSet();
        Assert.Contains(0, ctr);
        Assert.True(ctr.Count >= 5, $"Only {ctr.Count} distinct costs.");
        Assert.Contains(0, nzc);
        Assert.Contains(16, nzc);
    }

    [Fact]
    public void Group_HasEveryKernel()
    {
        var kernels = KernelFixtures.Group(GroupName).Select(r => (string)r[0]).Distinct().OrderBy(k => k, StringComparer.Ordinal);
        Assert.Equal(
            new[]
            {
                nameof(WelsCalculateSingleCtr4x4_c), nameof(WelsDctFourT4_c), nameof(WelsDctT4_c), nameof(WelsGetNoneZeroCount_c),
                nameof(WelsHadamardQuant2x2Skip_c), nameof(WelsHadamardQuant2x2_c), nameof(WelsHadamardT4Dc_c),
                nameof(WelsQuant4x4Dc_c), nameof(WelsQuant4x4_c), nameof(WelsQuantFour4x4Max_c), nameof(WelsQuantFour4x4_c),
                nameof(WelsScan4x4Ac_c), nameof(WelsScan4x4Dc), nameof(WelsScan4x4DcAc_c),
            },
            kernels);
    }
}
