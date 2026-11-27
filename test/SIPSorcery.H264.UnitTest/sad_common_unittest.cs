//-----------------------------------------------------------------------------
// Filename: sad_common_unittest.cs
//
// Description: Checks the port of codec/common/src/sad_common.cpp against the
// C build. Mirrors KernelsSad in refharness/src/kernels_deblock_sad.cpp: each
// SAD size and each "four neighbours" variant at matched and mismatched
// strides.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 24 Nov 2026  Claude          Created.
// 27 Nov 2026  Claude          Run and Lookup shared with the SIMD tests (P5.2).
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Xunit;
using static SIPSorcery.H264.WelsCommon;

namespace SIPSorcery.H264.UnitTest;

public unsafe class sad_common_unittest
{
    public static IEnumerable<object[]> Rows => KernelFixtures.Group("sad");

    [Theory]
    [MemberData(nameof(Rows))]
    public void Sad_MatchesCBuild(string kernel, string kase, string sha1)
    {
        var row = new KernelFixture("sad", kernel, kase, sha1);
        Lookup(kernel, out int h, out delegate*<byte*, int, byte*, int, int> sad, out delegate*<byte*, int, byte*, int, int*, void> four);
        Assert.Equal(sha1, Run(row, h, sad, four));
    }

    // The row's hash, from the SAD kernel or the four-neighbours one (the other null). Also
    // run with the SIMD kernels, by sad_common_simd_unittest.
    internal static string Run(KernelFixture row, int h, delegate*<byte*, int, byte*, int, int> sad,
                               delegate*<byte*, int, byte*, int, int*, void> four)
    {
        int s1 = row.Param("stride1"), s2 = row.Param("stride2");

        // Both planes have h + 4 rows with the block at (row 2, column 2).
        var r = new FixtureRng((uint)row.Param("seed"));
        using var a = new NativeBuffer(s1 * (h + 4));
        using var b = new NativeBuffer(s2 * (h + 4));
        r.Fill(a.Span);
        r.Fill(b.Span);
        byte* pa = a.Ptr + 2 * s1 + 2;
        byte* pb = b.Ptr + 2 * s2 + 2;

        using var hash = new FixtureHasher();
        if (sad != null)
        {
            hash.AddI32(sad(pa, s1, pb, s2));
        }
        else
        {
            int* pSad = stackalloc int[4] { -1, -1, -1, -1 };
            four(pa, s1, pb, s2, pSad);
            for (int i = 0; i < 4; i++)
            {
                hash.AddI32(pSad[i]);
            }
        }
        return hash.Hex();
    }

    // Out parameters rather than a tuple: function pointers can't be generic
    // arguments. Only the height matters for the buffers; the strides cover
    // the widest block.
    internal static void Lookup(string kernel, out int h, out delegate*<byte*, int, byte*, int, int> sad,
                               out delegate*<byte*, int, byte*, int, int*, void> four)
    {
        sad = null;
        four = null;
        switch (kernel)
        {
            case nameof(WelsSampleSad16x16_c): h = 16; sad = &WelsSampleSad16x16_c; break;
            case nameof(WelsSampleSad16x8_c): h = 8; sad = &WelsSampleSad16x8_c; break;
            case nameof(WelsSampleSad8x16_c): h = 16; sad = &WelsSampleSad8x16_c; break;
            case nameof(WelsSampleSad8x8_c): h = 8; sad = &WelsSampleSad8x8_c; break;
            case nameof(WelsSampleSad8x4_c): h = 4; sad = &WelsSampleSad8x4_c; break;
            case nameof(WelsSampleSad4x8_c): h = 8; sad = &WelsSampleSad4x8_c; break;
            case nameof(WelsSampleSad4x4_c): h = 4; sad = &WelsSampleSad4x4_c; break;
            case nameof(WelsSampleSadFour16x16_c): h = 16; four = &WelsSampleSadFour16x16_c; break;
            case nameof(WelsSampleSadFour16x8_c): h = 8; four = &WelsSampleSadFour16x8_c; break;
            case nameof(WelsSampleSadFour8x16_c): h = 16; four = &WelsSampleSadFour8x16_c; break;
            case nameof(WelsSampleSadFour8x8_c): h = 8; four = &WelsSampleSadFour8x8_c; break;
            case nameof(WelsSampleSadFour8x4_c): h = 4; four = &WelsSampleSadFour8x4_c; break;
            case nameof(WelsSampleSadFour4x8_c): h = 8; four = &WelsSampleSadFour4x8_c; break;
            case nameof(WelsSampleSadFour4x4_c): h = 4; four = &WelsSampleSadFour4x4_c; break;
            default: throw new ArgumentException($"No C# kernel {kernel}.");
        }
    }
}
