//-----------------------------------------------------------------------------
// Filename: enc_decode_mb_aux_unittest.cs
//
// Description: Checks the encoder's decode_mb_aux kernels (dequantisation, the
// inverse Hadamard transforms, the IDCT reconstructions, the block offset
// table) against the C build ("enc_decode_mb_aux" rows in
// reference/kernels.tsv, refharness src/kernels_encoder.cpp
// KernelsEncDecodeMbAux, whose buffer layouts this must follow).
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
using static SIPSorcery.H264.WelsCommon;

namespace SIPSorcery.H264.UnitTest;

public unsafe class enc_decode_mb_aux_unittest
{
    private const string GroupName = "enc_decode_mb_aux";

    public static IEnumerable<object[]> Rows => KernelFixtures.Group(GroupName);

    [Theory]
    [MemberData(nameof(Rows))]
    public void Kernel_MatchesCBuild(string kernel, string kase, string sha1)
    {
        var row = new KernelFixture(GroupName, kernel, kase, sha1);
        string actual = kernel switch
        {
            nameof(WelsIHadamard4x4Dc) => InPlace(row, 16, (uint)row.Param("seed"), p => WelsIHadamard4x4Dc(p)),
            nameof(WelsDequantLumaDc4x4) => InPlace(row, 16, (uint)row.Param("qp"), p => WelsDequantLumaDc4x4(p, row.Param("qp"))),
            nameof(WelsDequantIHadamard4x4_c) => DequantIHadamard4x4(&WelsDequantIHadamard4x4_c, row),
            nameof(WelsDequantIHadamard2x2Dc) => InPlace(row, 4, (uint)row.Param("seed"),
                p => WelsDequantIHadamard2x2Dc(p, g_kuiDequantCoeff[row.Param("qp")][0])),
            nameof(WelsDequant4x4_c) => Dequant(&WelsDequant4x4_c, 16, row),
            nameof(WelsDequantFour4x4_c) => Dequant(&WelsDequantFour4x4_c, 64, row),
            nameof(WelsIDctT4Rec_c) => Rec(&WelsIDctT4Rec_c, 4, 16, row),
            nameof(WelsIDctFourT4Rec_c) => Rec(&WelsIDctFourT4Rec_c, 8, 64, row),
            nameof(WelsIDctT4RecOnMb) => Rec(&RecOnMb, 16, 256, row),
            nameof(WelsIDctRecI16x16Dc_c) => Rec(&WelsIDctRecI16x16Dc_c, 16, 16, row),
            nameof(WelsGetEncBlockStrideOffset) => AddrTable(row),
            _ => throw new ArgumentException($"No C# kernel {kernel}."),
        };
        Assert.Equal(sha1, actual);
    }

    private delegate void InPlaceKernel(short* p);

    // n coefficients drawn with CoeffBuf, changed in place.
    private static string InPlace(KernelFixture row, int n, uint seed, InPlaceKernel f)
    {
        var r = new FixtureRng(seed);
        using var rs = new CoeffBuffer(ref r, n, row.Param("range"));

        f(rs.Data);

        using var h = new FixtureHasher();
        h.Add(rs.Bytes);
        return h.Hex();
    }

    // The MF rows the encoder passes the 4x4 dequantisers.
    internal static string Dequant(delegate*<short*, ushort*, void> f, int n, KernelFixture row)
    {
        ushort* kpMF = (ushort*)(g_kuiDequantCoeff + row.Param("qp"));
        var r = new FixtureRng((uint)row.Param("seed"));
        using var rs = new CoeffBuffer(ref r, n, row.Param("range"));

        f(rs.Data, kpMF);

        using var h = new FixtureHasher();
        h.Add(rs.Bytes);
        return h.Hex();
    }

    // The qp >= 12 path, with the g_kuiDequantCoeff[qp][0] >> 2 the encoder passes.
    internal static string DequantIHadamard4x4(delegate*<short*, ushort, void> f, KernelFixture row)
    {
        ushort kuiMF = (ushort)(g_kuiDequantCoeff[row.Param("qp")][0] >> 2);
        var r = new FixtureRng((uint)row.Param("seed"));
        using var rs = new CoeffBuffer(ref r, 16, row.Param("range"));

        f(rs.Data, kuiMF);

        using var h = new FixtureHasher();
        h.Add(rs.Bytes);
        return h.Hex();
    }

    private static void RecOnMb(byte* pDst, int iDstStride, byte* pPred, int iPredStride, short* pDct) =>
        WelsIDctT4RecOnMb(pDst, iDstStride, pPred, iPredStride, pDct, &WelsIDctFourT4Rec_c);

    // A random plane of n + 2 rows, the block at row 1, column 2; the
    // prediction at the origin of its own plane of n rows.
    internal static string Rec(delegate*<byte*, int, byte*, int, short*, void> f, int n, int coeffs, KernelFixture row)
    {
        int stride = row.Param("stride"), predStride = row.Param("pred");
        var r = new FixtureRng((uint)row.Param("seed"));
        using var rec = EncoderFixtures.Plane(ref r, stride * (n + 2));
        using var pred = EncoderFixtures.Plane(ref r, predStride * n);
        using var dct = new CoeffBuffer(ref r, coeffs, row.Param("range"));

        f(rec.Ptr + stride + 2, stride, pred.Ptr, predStride, dct.Data);

        using var h = new FixtureHasher();
        h.Add(rec.Span);
        h.Add(pred.Span);
        h.Add(dct.Bytes);
        return h.Hex();
    }

    // The 24 offsets plus 2 guard entries.
    private static string AddrTable(KernelFixture row)
    {
        var r = new FixtureRng(0);
        int* off = stackalloc int[26];
        for (int i = 0; i < 26; i++)
        {
            off[i] = (int)r.Next();
        }

        WelsGetEncBlockStrideOffset(off, row.Param("y"), row.Param("uv"));

        using var h = new FixtureHasher();
        for (int i = 0; i < 26; i++)
        {
            h.AddI32(off[i]);
        }
        return h.Hex();
    }

    [Fact]
    public void Group_HasEveryKernel()
    {
        var kernels = KernelFixtures.Group(GroupName).Select(r => (string)r[0]).Distinct().OrderBy(k => k, StringComparer.Ordinal);
        Assert.Equal(
            new[]
            {
                nameof(WelsDequant4x4_c), nameof(WelsDequantFour4x4_c), nameof(WelsDequantIHadamard2x2Dc), nameof(WelsDequantIHadamard4x4_c),
                nameof(WelsDequantLumaDc4x4), nameof(WelsGetEncBlockStrideOffset), nameof(WelsIDctFourT4Rec_c), nameof(WelsIDctRecI16x16Dc_c),
                nameof(WelsIDctT4RecOnMb), nameof(WelsIDctT4Rec_c), nameof(WelsIHadamard4x4Dc),
            },
            kernels);
    }
}
