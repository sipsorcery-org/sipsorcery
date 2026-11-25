//-----------------------------------------------------------------------------
// Filename: decode_mb_aux_unittest.cs
//
// Description: Checks the decode_mb_aux kernels (the 4x4 and 8x8 residual
// IDCTs, the four-block IDCT InitPredFunc installs, the 4x4 block offset
// table) against the C build ("decode_mb_aux" rows in reference/kernels.tsv,
// refharness src/kernels_decoder.cpp KernelsDecodeMbAux, whose buffer layouts
// this must follow).
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
using System.Runtime.InteropServices;
using SIPSorcery.H264.Decoder;
using Xunit;
using static SIPSorcery.H264.Decoder.WelsDec;

namespace SIPSorcery.H264.UnitTest;

public unsafe class decode_mb_aux_unittest
{
    public static IEnumerable<object[]> Rows => KernelFixtures.Group("decode_mb_aux");

    // A residual coefficient: uniform in [-range, range], or any short when
    // range is 32768 (fixture Coeff in kernels_decoder.cpp).
    private static short Coeff(ref FixtureRng r, int range) =>
        range >= 32768 ? (short)(r.Next() >> 16) : (short)((int)(r.Next() % (uint)(2 * range + 1)) - range);

    private static void HashCoeffs(FixtureHasher h, short* rs, int count) =>
        h.Add(new ReadOnlySpan<byte>(rs, count * sizeof(short)));

    [Theory]
    [MemberData(nameof(Rows))]
    public void Kernel_MatchesCBuild(string kernel, string kase, string sha1)
    {
        var row = new KernelFixture("decode_mb_aux", kernel, kase, sha1);
        string actual = kernel switch
        {
            nameof(IdctResAddPred_c) => Idct(&IdctResAddPred_c, 4, row),
            nameof(IdctResAddPred8x8_c) => Idct(&IdctResAddPred8x8_c, 8, row),
            nameof(IdctFourResAddPred_) => IdctFour(row),
            nameof(GetI4LumaIChromaAddrTable) => AddrTable(row),
            _ => throw new ArgumentException($"No C# kernel {kernel}."),
        };
        Assert.Equal(sha1, actual);
    }

    // A random plane of n + 2 rows, the block at row 1, column 2; random
    // coefficients. The hash covers the plane and the coefficients.
    private static string Idct(delegate*<byte*, int, short*, void> f, int n, KernelFixture row)
    {
        int stride = row.Param("stride");
        int range = row.Param("range");
        var r = new FixtureRng((uint)row.Param("seed"));
        using var plane = new NativeBuffer(stride * (n + 2));
        short* rs = stackalloc short[n * n];
        r.Fill(plane.Span);
        for (int i = 0; i < n * n; i++)
        {
            rs[i] = Coeff(ref r, range);
        }

        f(plane.Ptr + stride + 2, stride, rs);

        using var h = new FixtureHasher();
        h.Add(plane.Span);
        HashCoeffs(h, rs, n * n);
        return h.Hex();
    }

    // Through InitPredFunc's table, as the C side. An 8x8 block at row 1,
    // column 2 of a 10-row plane, the generator seeded with nzc. The decimal
    // digits of modes, block 0 first: all zero (0), zero at DC only (1) or
    // random (2) coefficients; pNzc[0, 1, 4, 5] are the bits of nzc.
    private static string IdctFour(KernelFixture row)
    {
        const int kFourStride = 24;
        int nzc = row.Param("nzc");
        int modes = row.Param("modes");
        var r = new FixtureRng((uint)nzc);
        SWelsDecoderContext* pCtx = (SWelsDecoderContext*)NativeMemory.AllocZeroed((nuint)sizeof(SWelsDecoderContext));
        try
        {
            InitPredFunc(pCtx, 0);
            using var plane = new NativeBuffer(kFourStride * 10);
            short* rs = stackalloc short[64];
            r.Fill(plane.Span);
            for (int b = 0, div = 1000; b < 4; b++, div /= 10)
            {
                int mode = modes / div % 10;
                for (int i = 0; i < 16; i++)
                {
                    rs[b * 16 + i] = Coeff(ref r, 300);
                }
                if (mode == 0)
                {
                    new Span<short>(rs + b * 16, 16).Clear();
                }
                if (mode == 1)
                {
                    rs[b * 16] = 0;
                }
            }
            sbyte* pNzc = stackalloc sbyte[6]
            {
                (sbyte)(nzc & 1), (sbyte)((nzc >> 1) & 1), 7, 7, (sbyte)((nzc >> 2) & 1), (sbyte)((nzc >> 3) & 1),
            };

            pCtx->pIdctFourResAddPredFunc(plane.Ptr + kFourStride + 2, kFourStride, rs, pNzc);

            using var h = new FixtureHasher();
            h.Add(plane.Span);
            HashCoeffs(h, rs, 64);
            return h.Hex();
        }
        finally
        {
            NativeMemory.Free(pCtx);
        }
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

        GetI4LumaIChromaAddrTable(off, row.Param("y"), row.Param("uv"));

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
        var kernels = KernelFixtures.Group("decode_mb_aux").Select(r => (string)r[0]).Distinct().OrderBy(k => k, StringComparer.Ordinal);
        Assert.Equal(
            new[] { nameof(GetI4LumaIChromaAddrTable), nameof(IdctFourResAddPred_), nameof(IdctResAddPred8x8_c), nameof(IdctResAddPred_c) },
            kernels);
    }
}
