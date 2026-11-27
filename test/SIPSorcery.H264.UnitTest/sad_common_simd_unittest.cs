//-----------------------------------------------------------------------------
// Filename: sad_common_simd_unittest.cs
//
// Description: Checks the SIMD SAD kernels (common/sad_common_simd.cs, P5.2)
// against the C ones: on the C build's fixture rows, which must give the same
// hashes, and on random and extreme blocks at random strides, which must give
// the same results as the C# C kernels. Nothing runs where the machine has no
// SIMD kernels (WelsCpuHasSimd128).
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 27 Nov 2026  Claude          Created (P5.2).
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Xunit;
using static SIPSorcery.H264.WelsCommon;

namespace SIPSorcery.H264.UnitTest;

public unsafe class sad_common_simd_unittest
{
    /// <summary>Whether this machine has the SIMD kernels, whatever WelsCpuFeatureMask says.</summary>
    internal static bool Supported => WelsCpuHasSimd128(uint.MaxValue);

    public static IEnumerable<object[]> Rows => KernelFixtures.Group("sad");

    public static IEnumerable<object[]> Kernels()
    {
        foreach (string size in new[] { "16x16", "16x8", "8x16", "8x8", "8x4", "4x8", "4x4" })
        {
            yield return new object[] { $"WelsSampleSad{size}_c" };
            yield return new object[] { $"WelsSampleSadFour{size}_c" };
        }
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void Simd_MatchesCBuild(string kernel, string kase, string sha1)
    {
        if (!Supported)
        {
            return;
        }
        var row = new KernelFixture("sad", kernel, kase, sha1);
        sad_common_unittest.Lookup(kernel, out int h, out _, out _);
        LookupSimd(kernel, out var sad, out var four);
        Assert.Equal(sha1, sad_common_unittest.Run(row, h, sad, four));
    }

    [Theory]
    [MemberData(nameof(Kernels))]
    public void Simd_EqualsC(string kernel)
    {
        if (!Supported)
        {
            return;
        }
        sad_common_unittest.Lookup(kernel, out int h, out var sadC, out var fourC);
        LookupSimd(kernel, out var sadS, out var fourS);
        int w = SimdFill.Width(kernel);

        var r = new FixtureRng(SimdFill.Seed(kernel));
        int* cOut = stackalloc int[4];
        int* sOut = stackalloc int[4];
        for (int trial = 0; trial < 1500; trial++)
        {
            // A sample of margin all round, for the neighbours.
            int s1 = w + (int)(r.Next() % 40);
            int s2 = w + 2 + (int)(r.Next() % 40);
            using var a = new NativeBuffer(s1 * h);
            using var b = new NativeBuffer(s2 * (h + 2));
            SimdFill.Pair(ref r, trial, a.Span, b.Span);
            byte* pa = a.Ptr;
            byte* pb = b.Ptr + s2 + 1;

            if (sadC != null)
            {
                Assert.Equal(sadC(pa, s1, pb, s2), sadS(pa, s1, pb, s2));
            }
            else
            {
                fourC(pa, s1, pb, s2, cOut);
                fourS(pa, s1, pb, s2, sOut);
                for (int i = 0; i < 4; i++)
                {
                    Assert.Equal(cOut[i], sOut[i]);
                }
            }
        }
    }

    private static void LookupSimd(string kernel, out delegate*<byte*, int, byte*, int, int> sad,
                                   out delegate*<byte*, int, byte*, int, int*, void> four)
    {
        sad = null;
        four = null;
        switch (kernel)
        {
            case nameof(WelsSampleSad16x16_c): sad = &WelsSampleSad16x16_simd; break;
            case nameof(WelsSampleSad16x8_c): sad = &WelsSampleSad16x8_simd; break;
            case nameof(WelsSampleSad8x16_c): sad = &WelsSampleSad8x16_simd; break;
            case nameof(WelsSampleSad8x8_c): sad = &WelsSampleSad8x8_simd; break;
            case nameof(WelsSampleSad8x4_c): sad = &WelsSampleSad8x4_simd; break;
            case nameof(WelsSampleSad4x8_c): sad = &WelsSampleSad4x8_simd; break;
            case nameof(WelsSampleSad4x4_c): sad = &WelsSampleSad4x4_simd; break;
            case nameof(WelsSampleSadFour16x16_c): four = &WelsSampleSadFour16x16_simd; break;
            case nameof(WelsSampleSadFour16x8_c): four = &WelsSampleSadFour16x8_simd; break;
            case nameof(WelsSampleSadFour8x16_c): four = &WelsSampleSadFour8x16_simd; break;
            case nameof(WelsSampleSadFour8x8_c): four = &WelsSampleSadFour8x8_simd; break;
            case nameof(WelsSampleSadFour8x4_c): four = &WelsSampleSadFour8x4_simd; break;
            case nameof(WelsSampleSadFour4x8_c): four = &WelsSampleSadFour4x8_simd; break;
            case nameof(WelsSampleSadFour4x4_c): four = &WelsSampleSadFour4x4_simd; break;
            default: throw new ArgumentException($"No SIMD kernel for {kernel}.");
        }
    }
}

/// <summary>Test content for the SIMD equality tests, a different kind each trial.</summary>
internal static class SimdFill
{
    /// <summary>The block width in a kernel name such as WelsSampleSadFour16x8_c.</summary>
    public static int Width(string kernel) => int.Parse(Regex.Match(kernel, @"(\d+)x\d+").Groups[1].Value);

    /// <summary>A seed from the kernel name, the same on every run (string.GetHashCode isn't).</summary>
    public static uint Seed(string kernel)
    {
        uint h = 2166136261u;
        foreach (char c in kernel)
        {
            h = (h ^ c) * 16777619u;
        }
        return h;
    }

    /// <summary>Fills a and b, by <paramref name="trial"/>: random, extremes or near-equal.</summary>
    public static void Pair(ref FixtureRng r, int trial, Span<byte> a, Span<byte> b)
    {
        switch (trial % 6)
        {
            case 0:
                // The largest differences: 255 against 0.
                a.Fill(255);
                b.Fill(0);
                break;
            case 1:
                a.Fill(0);
                b.Fill(255);
                break;
            case 2:
                // Only 0 and 255, at random: differences of -255, 0 and 255.
                for (int i = 0; i < a.Length; i++)
                {
                    a[i] = (byte)((r.Next() & 1) * 255);
                }
                for (int i = 0; i < b.Length; i++)
                {
                    b[i] = (byte)((r.Next() & 1) * 255);
                }
                break;
            case 3:
                // A checkerboard against its inverse.
                for (int i = 0; i < a.Length; i++)
                {
                    a[i] = (byte)(((i ^ (i / 7)) & 1) * 255);
                }
                for (int i = 0; i < b.Length; i++)
                {
                    b[i] = (byte)(255 - (((i ^ (i / 7)) & 1) * 255));
                }
                break;
            case 4:
                // Near-equal: b is random and a within a few levels of it.
                r.Fill(b);
                for (int i = 0; i < a.Length; i++)
                {
                    int v = b[i % b.Length] + (int)(r.Next() % 9) - 4;
                    a[i] = (byte)Math.Clamp(v, 0, 255);
                }
                break;
            default:
                r.Fill(a);
                r.Fill(b);
                break;
        }
    }
}
