//-----------------------------------------------------------------------------
// Filename: copy_mb_unittest.cs
//
// Description: Checks the port of codec/common/src/copy_mb.cpp against the C
// build. Mirrors KernelsCopyMb in refharness/src/kernels_common.cpp: random
// source and destination buffers of h+2 rows, the block at row 1, column 2,
// and the whole destination hashed so stray writes fail too.
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

public unsafe class copy_mb_unittest
{
    public static IEnumerable<object[]> Rows => KernelFixtures.Group("copy_mb");

    [Theory]
    [MemberData(nameof(Rows))]
    public void Copy_MatchesCBuild(string kernel, string kase, string sha1)
    {
        delegate*<byte*, int, byte*, int, void> f;
        int h;
        switch (kernel)
        {
            case nameof(WelsCopy4x4_c): f = &WelsCopy4x4_c; h = 4; break;
            case nameof(WelsCopy8x4_c): f = &WelsCopy8x4_c; h = 4; break;
            case nameof(WelsCopy4x8_c): f = &WelsCopy4x8_c; h = 8; break;
            case nameof(WelsCopy8x8_c): f = &WelsCopy8x8_c; h = 8; break;
            case nameof(WelsCopy8x16_c): f = &WelsCopy8x16_c; h = 16; break;
            case nameof(WelsCopy16x8_c): f = &WelsCopy16x8_c; h = 8; break;
            case nameof(WelsCopy16x16_c): f = &WelsCopy16x16_c; h = 16; break;
            default: throw new ArgumentException($"No C# kernel {kernel}.");
        }
        var row = new KernelFixture("copy_mb", kernel, kase, sha1);
        int dstStride = row.Param("dst"), srcStride = row.Param("src");

        var r = new FixtureRng((uint)row.Param("seed"));
        using var src = new NativeBuffer(srcStride * (h + 2));
        using var dst = new NativeBuffer(dstStride * (h + 2));
        r.Fill(src.Span);
        r.Fill(dst.Span);

        f(dst.Ptr + dstStride + 2, dstStride, src.Ptr + srcStride + 2, srcStride);

        Assert.Equal(sha1, FixtureHasher.Of(dst.Span));
    }
}
