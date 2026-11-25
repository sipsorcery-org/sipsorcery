//-----------------------------------------------------------------------------
// Filename: expand_pic_unittest.cs
//
// Description: Checks the port of codec/common/src/expand_pic.cpp against the
// C build. Mirrors KernelsExpandPic in refharness/src/kernels_common.cpp:
// planes with random interiors and random borders (PaddedPlane), the kernel
// run once, and every plane byte hashed, guard bytes included.
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

public unsafe class expand_pic_unittest
{
    public static IEnumerable<object[]> Rows => KernelFixtures.Group("expand_pic");

    [Theory]
    [MemberData(nameof(Rows))]
    public void Expand_MatchesCBuild(string kernel, string kase, string sha1)
    {
        var row = new KernelFixture("expand_pic", kernel, kase, sha1);
        var r = new FixtureRng((uint)row.Param("seed"));
        SExpandPicFunc funcs;
        InitExpandPictureFunc(&funcs, 0);
        using var h = new FixtureHasher();

        switch (kernel)
        {
            case "ExpandPictureLuma_c":
            case "ExpandPictureChroma_c":
            {
                bool chroma = kernel == "ExpandPictureChroma_c";
                using var p = new PaddedPlane(row.Param("w"), row.Param("h"), chroma ? CHROMA_PADDING_LENGTH : PADDING_LENGTH, 0, ref r);
                if (chroma)
                {
                    funcs.pfExpandChromaPicture[0](p.Origin, p.Stride, p.W, p.H);
                }
                else
                {
                    funcs.pfExpandLumaPicture(p.Origin, p.Stride, p.W, p.H);
                }
                h.Add(p.Buffer.Span);
                break;
            }

            case "ExpandReferencingPicture":
            {
                int w = row.Param("w"), hgt = row.Param("h");
                using var y = new PaddedPlane(w, hgt, PADDING_LENGTH, 0, ref r);
                using var u = new PaddedPlane(w / 2, hgt / 2, CHROMA_PADDING_LENGTH, 0, ref r);
                using var v = new PaddedPlane(w / 2, hgt / 2, CHROMA_PADDING_LENGTH, 8, ref r);
                byte** data = stackalloc byte*[3] { y.Origin, u.Origin, v.Origin };
                int* stride = stackalloc int[3] { y.Stride, u.Stride, v.Stride };
                ExpandReferencingPicture(data, w, hgt, stride, funcs.pfExpandLumaPicture, funcs.pfExpandChromaPicture);
                h.Add(y.Buffer.Span);
                h.Add(u.Buffer.Span);
                h.Add(v.Buffer.Span);
                break;
            }

            case "PadMBLuma_c":
            case "PadMBChroma_c":
            {
                bool chroma = kernel == "PadMBChroma_c";
                int mbSize = chroma ? 8 : 16;
                int mbw = row.Param("mbw"), mbh = row.Param("mbh");
                using var p = new PaddedPlane(mbw * mbSize, mbh * mbSize, chroma ? CHROMA_PADDING_LENGTH : PADDING_LENGTH, 0, ref r);
                for (int mbY = 0; mbY < mbh; ++mbY)
                {
                    for (int mbX = 0; mbX < mbw; ++mbX)
                    {
                        if (chroma)
                        {
                            PadMBChroma_c(p.Origin, p.Stride, p.W, p.H, mbX, mbY, mbw, mbh);
                        }
                        else
                        {
                            PadMBLuma_c(p.Origin, p.Stride, p.W, p.H, mbX, mbY, mbw, mbh);
                        }
                    }
                }
                h.Add(p.Buffer.Span);
                break;
            }

            default:
                throw new ArgumentException($"No C# case for {kernel} ({kase}).");
        }
        Assert.Equal(sha1, h.Hex());
    }

    [Fact]
    public void InitExpandPictureFunc_FillsEverySlot()
    {
        SExpandPicFunc funcs = default;
        InitExpandPictureFunc(&funcs, 0);
        Assert.True(funcs.pfExpandLumaPicture != null);
        Assert.True(funcs.pfExpandChromaPicture[0] != null);
        Assert.True(funcs.pfExpandChromaPicture[1] != null);
        Assert.Equal(3 * sizeof(nint), sizeof(SExpandPicFunc));
    }
}
