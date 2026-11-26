//-----------------------------------------------------------------------------
// Filename: sample_unittest.cs
//
// Description: Checks the encoder's SATD kernels and the combined intra
// predict-and-cost kernels against the C build ("sample" rows in
// reference/kernels.tsv, refharness src/kernels_encoder.cpp KernelsSample,
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

public unsafe class sample_unittest
{
    private const string GroupName = "sample";

    public static IEnumerable<object[]> Rows => KernelFixtures.Group(GroupName);

    // 4x4 lambdas (2, 1, 0) for (DC, H, V), by the case's lambda index.
    private static readonly int[][] Lambdas4x4 = { new[] { 0, 0, 0 }, new[] { 24, 24, 0 }, new[] { 0, 24, 24 }, new[] { 24, 0, 24 } };

    [Theory]
    [MemberData(nameof(Rows))]
    public void Kernel_MatchesCBuild(string kernel, string kase, string sha1)
    {
        var row = new KernelFixture(GroupName, kernel, kase, sha1);
        string actual = kernel switch
        {
            nameof(WelsSampleSatd4x4_c) => Satd(&WelsSampleSatd4x4_c, 4, 4, row),
            nameof(WelsSampleSatd8x4_c) => Satd(&WelsSampleSatd8x4_c, 8, 4, row),
            nameof(WelsSampleSatd4x8_c) => Satd(&WelsSampleSatd4x8_c, 4, 8, row),
            nameof(WelsSampleSatd8x8_c) => Satd(&WelsSampleSatd8x8_c, 8, 8, row),
            nameof(WelsSampleSatd16x8_c) => Satd(&WelsSampleSatd16x8_c, 16, 8, row),
            nameof(WelsSampleSatd8x16_c) => Satd(&WelsSampleSatd8x16_c, 8, 16, row),
            nameof(WelsSampleSatd16x16_c) => Satd(&WelsSampleSatd16x16_c, 16, 16, row),
            nameof(WelsSampleSatdIntra4x4Combined3_c) => Combined4x4(row, out _),
            nameof(WelsSampleSatdIntra16x16Combined3_c) => Combined16x16(&WelsSampleSatdIntra16x16Combined3_c, row, out _),
            nameof(WelsSampleSadIntra16x16Combined3_c) => Combined16x16(&WelsSampleSadIntra16x16Combined3_c, row, out _),
            nameof(WelsSampleSatdIntra8x8Combined3_c) => Combined8x8(&WelsSampleSatdIntra8x8Combined3_c, row, out _),
            nameof(WelsSampleSadIntra8x8Combined3_c) => Combined8x8(&WelsSampleSadIntra8x8Combined3_c, row, out _),
            _ => throw new ArgumentException($"No C# kernel {kernel}."),
        };
        Assert.Equal(sha1, actual);
    }

    // Random planes, the block at their origin; the second plane is the first
    // plus noise (noise < 256) or independent (noise 256).
    private static string Satd(delegate*<byte*, int, byte*, int, int> f, int w, int hgt, KernelFixture row)
    {
        int s1 = row.Param("s1"), s2 = row.Param("s2"), noise = row.Param("noise");
        var r = new FixtureRng((uint)row.Param("seed"));
        using var p1 = EncoderFixtures.Plane(ref r, s1 * hgt);
        using var p2 = EncoderFixtures.Plane(ref r, s2 * hgt);
        if (noise < 256)
        {
            for (int y = 0; y < hgt; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int v = p1.Ptr[y * s1 + x] + (int)(r.Next() % (uint)(2 * noise + 1)) - noise;
                    p2.Ptr[y * s2 + x] = (byte)Math.Clamp(v, 0, 255);
                }
            }
        }

        int ret = f(p1.Ptr, s1, p2.Ptr, s2);

        using var h = new FixtureHasher();
        h.AddI32(ret);
        h.Add(p1.Span);
        h.Add(p2.Span);
        return h.Hex();
    }

    // The combined kernels: the reconstructed neighbours in a random plane of
    // n + 2 rows, the block at row 1, column 4 (made flat for target 4); the
    // source in its own plane at encStride = decStride + 8, the block at the
    // origin, from FillEnc; the prediction with 16 guard bytes either side.
    private static string Combined4x4(KernelFixture row, out int mode)
    {
        int decStride = row.Param("dec"), encStride = decStride + 8;
        int[] lambda = Lambdas4x4[row.Param("lambda")];
        var r = new FixtureRng((uint)row.Param("seed"));
        using var dec = EncoderFixtures.Plane(ref r, decStride * 6);
        using var enc = EncoderFixtures.Plane(ref r, encStride * 4);
        using var dst = EncoderFixtures.Plane(ref r, 16 + 32);
        byte* pDec = dec.Ptr + decStride + 4;
        if (row.Param("target") == 4)
        {
            EncoderFixtures.FlattenDec(pDec, decStride, 4);
        }
        EncoderFixtures.FillEnc(ref r, enc.Ptr, encStride, pDec, decStride, 4, row.Param("target"), row.Param("noise"));
        int iMode = (int)r.Next();

        int ret = WelsSampleSatdIntra4x4Combined3_c(pDec, decStride, enc.Ptr, encStride, dst.Ptr + 16, &iMode,
                                                    lambda[0], lambda[1], lambda[2]);
        mode = iMode;

        using var h = new FixtureHasher();
        h.AddI32(ret);
        h.AddI32(iMode);
        h.Add(dst.Span);
        h.Add(dec.Span);
        h.Add(enc.Span);
        return h.Hex();
    }

    private static string Combined16x16(delegate*<byte*, int, byte*, int, int*, int, byte*, int> f, KernelFixture row, out int mode)
    {
        int decStride = row.Param("dec"), encStride = decStride + 8;
        var r = new FixtureRng((uint)row.Param("seed"));
        using var dec = EncoderFixtures.Plane(ref r, decStride * 18);
        using var enc = EncoderFixtures.Plane(ref r, encStride * 16);
        using var dst = EncoderFixtures.Plane(ref r, 256 + 32);
        byte* pDec = dec.Ptr + decStride + 4;
        if (row.Param("target") == 4)
        {
            EncoderFixtures.FlattenDec(pDec, decStride, 16);
        }
        EncoderFixtures.FillEnc(ref r, enc.Ptr, encStride, pDec, decStride, 16, row.Param("target"), row.Param("noise"));
        int iMode = (int)r.Next();

        int ret = f(pDec, decStride, enc.Ptr, encStride, &iMode, row.Param("lambda"), dst.Ptr + 16);
        mode = iMode;

        using var h = new FixtureHasher();
        h.AddI32(ret);
        h.AddI32(iMode);
        h.Add(dst.Span);
        h.Add(dec.Span);
        h.Add(enc.Span);
        return h.Hex();
    }

    private static string Combined8x8(delegate*<byte*, int, byte*, int, int*, int, byte*, byte*, byte*, int> f, KernelFixture row, out int mode)
    {
        int decStride = row.Param("dec"), encStride = decStride + 8;
        int target = row.Param("target"), noise = row.Param("noise");
        var r = new FixtureRng((uint)row.Param("seed"));
        using var decCb = EncoderFixtures.Plane(ref r, decStride * 10);
        using var decCr = EncoderFixtures.Plane(ref r, decStride * 10);
        using var encCb = EncoderFixtures.Plane(ref r, encStride * 8);
        using var encCr = EncoderFixtures.Plane(ref r, encStride * 8);
        using var dst = EncoderFixtures.Plane(ref r, 128 + 32);
        byte* pDecCb = decCb.Ptr + decStride + 4;
        byte* pDecCr = decCr.Ptr + decStride + 4;
        if (target == 4)
        {
            EncoderFixtures.FlattenDec(pDecCb, decStride, 8);
            EncoderFixtures.FlattenDec(pDecCr, decStride, 8);
        }
        EncoderFixtures.FillEnc(ref r, encCb.Ptr, encStride, pDecCb, decStride, 8, target, noise);
        EncoderFixtures.FillEnc(ref r, encCr.Ptr, encStride, pDecCr, decStride, 8, target, noise);
        int iMode = (int)r.Next();

        int ret = f(pDecCb, decStride, encCb.Ptr, encStride, &iMode, row.Param("lambda"), dst.Ptr + 16, pDecCr, encCr.Ptr);
        mode = iMode;

        using var h = new FixtureHasher();
        h.AddI32(ret);
        h.AddI32(iMode);
        h.Add(dst.Span);
        h.Add(decCb.Span);
        h.Add(decCr.Span);
        h.Add(encCb.Span);
        h.Add(encCr.Span);
        return h.Hex();
    }

    /// <summary>
    /// Each combined kernel's rows must pick every one of its three modes, or
    /// they would not test the decisions.
    /// </summary>
    [Theory]
    [InlineData(nameof(WelsSampleSatdIntra4x4Combined3_c))]
    [InlineData(nameof(WelsSampleSatdIntra16x16Combined3_c))]
    [InlineData(nameof(WelsSampleSadIntra16x16Combined3_c))]
    [InlineData(nameof(WelsSampleSatdIntra8x8Combined3_c))]
    [InlineData(nameof(WelsSampleSadIntra8x8Combined3_c))]
    public void CombinedRows_PickEveryMode(string kernel)
    {
        var modes = new HashSet<int>();
        foreach (var row in KernelFixtures.All.Where(r => r.Group == GroupName && r.Kernel == kernel))
        {
            int mode;
            _ = kernel switch
            {
                nameof(WelsSampleSatdIntra4x4Combined3_c) => Combined4x4(row, out mode),
                nameof(WelsSampleSatdIntra16x16Combined3_c) => Combined16x16(&WelsSampleSatdIntra16x16Combined3_c, row, out mode),
                nameof(WelsSampleSadIntra16x16Combined3_c) => Combined16x16(&WelsSampleSadIntra16x16Combined3_c, row, out mode),
                nameof(WelsSampleSatdIntra8x8Combined3_c) => Combined8x8(&WelsSampleSatdIntra8x8Combined3_c, row, out mode),
                _ => Combined8x8(&WelsSampleSadIntra8x8Combined3_c, row, out mode),
            };
            modes.Add(mode);
        }
        Assert.Equal(new HashSet<int> { 0, 1, 2 }, modes);
    }

    [Fact]
    public void Group_HasEveryKernel()
    {
        Assert.Equal(12, KernelFixtures.Group(GroupName).Select(r => (string)r[0]).Distinct().Count());
    }
}
