//-----------------------------------------------------------------------------
// Filename: vlc_encoder_unittest.cs
//
// Description: Checks the CAVLC element writers of vlc_encoder.h against the C
// build ("vlc_encoder" rows in reference/kernels.tsv, refharness
// src/cavlc_encoder.cpp KernelsVlcEncoder): every table entry of each writer,
// in index order, after 0, 7 or 19 bits.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 26 Nov 2026  Claude          Created (E3).
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

public unsafe class vlc_encoder_unittest
{
    private const string GroupName = "vlc_encoder";

    public static IEnumerable<object[]> Rows => KernelFixtures.Group(GroupName);

    [Theory]
    [MemberData(nameof(Rows))]
    public void Writer_MatchesCBuild(string kernel, string kase, string sha1)
    {
        var row = new KernelFixture(GroupName, kernel, kase, sha1);
        int pre = row.Param("pre");
        var g = new FixtureRng((uint)pre);
        int iRet = 0;
        FixtureBs w;
        switch (kernel)
        {
            case nameof(WriteTotalCoeffTrailingones):
                w = new FixtureBs(ref g, 4096, 4096, pre);
                for (int nc = 0; nc < 18; nc++)
                {
                    for (int t = 0; t <= 16; t++)
                    {
                        for (int o = 0; o < 4; o++)
                        {
                            iRet |= WriteTotalCoeffTrailingones(w.Bs, (byte)nc, (byte)t, (byte)o);
                        }
                    }
                }
                break;
            case nameof(WriteTotalcoeffTrailingonesChroma):
                w = new FixtureBs(ref g, 1024, 1024, pre);
                for (int t = 0; t <= 16; t++)
                {
                    for (int o = 0; o < 4; o++)
                    {
                        iRet |= WriteTotalcoeffTrailingonesChroma(w.Bs, (byte)t, (byte)o);
                    }
                }
                break;
            case nameof(WriteLevelPrefix):
                w = new FixtureBs(ref g, 1024, 1024, pre);
                for (uint z = 0; z < 16; z++)
                {
                    iRet |= WriteLevelPrefix(w.Bs, z);
                }
                break;
            case nameof(WriteTotalZeros):
                w = new FixtureBs(ref g, 1024, 1024, pre);
                for (uint t = 0; t < 16; t++)
                {
                    for (uint z = 0; z < 16; z++)
                    {
                        iRet |= WriteTotalZeros(w.Bs, t, z);
                    }
                }
                break;
            case nameof(WriteTotalZerosChromaDc):
                w = new FixtureBs(ref g, 256, 256, pre);
                for (uint t = 0; t < 4; t++)
                {
                    for (uint z = 0; z < 4; z++)
                    {
                        iRet |= WriteTotalZerosChromaDc(w.Bs, t, z);
                    }
                }
                break;
            case nameof(WriteRunBefore):
                w = new FixtureBs(ref g, 1024, 1024, pre);
                for (int z = 0; z < 8; z++)
                {
                    for (int r = 0; r < 15; r++)
                    {
                        iRet |= WriteRunBefore(w.Bs, (byte)z, (byte)r);
                    }
                }
                break;
            default:
                throw new ArgumentException($"No C# writer {kernel}.");
        }

        using (w)
        {
            BsFlush(w.Bs);
            using var h = new FixtureHasher();
            h.AddI32(iRet);
            w.Hash(h);
            Assert.Equal(sha1, h.Hex());
        }
    }

    [Fact]
    public void Group_HasEveryWriter()
    {
        Assert.Equal(6, KernelFixtures.Group(GroupName).Select(r => (string)r[0]).Distinct().Count());
    }
}
