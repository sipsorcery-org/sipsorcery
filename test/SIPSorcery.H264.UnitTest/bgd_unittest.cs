//-----------------------------------------------------------------------------
// Filename: bgd_unittest.cs
//
// Description: Checks the port of the background detection
// (CBackgroundDetection, codec/processing/src/backgrounddetection) against
// the C build. Mirrors KernelsBgd in refharness/src/kernels_processing.cpp:
// Set and Process on the VAA statistics of picture pairs mixed from static,
// shifted, noisy and unrelated macroblocks, whole and partial, one detector
// over frames of growing and shrinking size, and the invalid inputs.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 27 Nov 2026  Claude          Created (E7).
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Collections.Generic;
using Xunit;
using static SIPSorcery.H264.UnitTest.PreprocessFixtures;

namespace SIPSorcery.H264.UnitTest;

public unsafe class bgd_unittest
{
    public static IEnumerable<object[]> Rows => KernelFixtures.Group("bgd");

    [Theory]
    [MemberData(nameof(Rows))]
    public void Bgd_MatchesCBuild(string kernel, string kase, string sha1)
    {
        using var hash = new FixtureHasher();
        var bgd = new CBackgroundDetection(0);
        try
        {
            if (kase == "invalid")
            {
                var r = new FixtureRng(710);
                using var cur = new FixtureYuv(ref r, new FixtureYuvSize(16, 16, 16, 8));
                SPixMap mc = cur.Map();
                hash.AddI32((int)bgd.Set(0, null));
                hash.AddI32((int)bgd.Process(0, null, &mc));
                hash.AddI32((int)bgd.Process(0, &mc, null));
            }
            else if (kase == "sequence")
            {
                FixtureYuvSize[] sequence =
                [
                    new(48, 48, 48, 24), new(176, 144, 192, 96), new(64, 48, 80, 40), new(176, 144, 176, 88),
                ];
                uint seed = 700;
                foreach (FixtureYuvSize s in sequence)
                {
                    var r = new FixtureRng(seed++);
                    Frame(ref r, s, 1, ref bgd, hash);
                }
            }
            else
            {
                var row = new KernelFixture("bgd", kernel, kase, sha1);
                var r = new FixtureRng((uint)row.Param("seed"));
                var s = new FixtureYuvSize(row.Param("w"), row.Param("h"), row.Param("stride"), row.Param("uvstride"));
                Frame(ref r, s, row.Param("mix"), ref bgd, hash);
            }
        }
        finally
        {
            bgd.Destroy();
        }
        Assert.Equal(sha1, hash.Hex());
    }

    // BgdFrame.
    private static void Frame(ref FixtureRng r, FixtureYuvSize s, int mix, ref CBackgroundDetection bgd, FixtureHasher hash)
    {
        MakeYuvPair(ref r, s, mix, out FixtureYuv cur, out FixtureYuv refPic);
        using (cur)
        using (refPic)
        {
            using var o = new vaacalc_unittest.Outputs(ref r, s.W, s.H);
            RunVaa(cur, refPic, s, o);
            SVAACalcResult res = o.Result();
            using var flags = new NativeBuffer(MbCount(s.W, s.H) + 2);
            r.Fill(flags.Span);
            SBGDInterface bi = default;
            bi.pBackgroundMbFlag = (sbyte*)flags.Ptr;
            bi.pCalcRes = &res;
            SPixMap mc = cur.Map(), mr = refPic.Map();
            hash.AddI32((int)bgd.Set(0, &bi));
            hash.AddI32((int)bgd.Process(0, &mc, &mr));
            hash.Add(flags.Span);
        }
    }
}
