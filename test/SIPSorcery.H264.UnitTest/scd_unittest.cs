//-----------------------------------------------------------------------------
// Filename: scd_unittest.cs
//
// Description: Checks the port of the video scene change detection
// (CSceneChangeDetection<CSceneChangeDetectorVideo>, codec/processing/src/
// scenechangedetection) against the C build. Mirrors KernelsScd in
// refharness/src/kernels_processing.cpp: Set, Process and Get on pictures
// whose moved block count sits either side of both thresholds, blocks with
// SADs of 320 and 321, differing strides and a picture with no whole block.
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

public unsafe class scd_unittest
{
    private const int Margin = FixturePlane.Margin;

    public static IEnumerable<object[]> Rows => KernelFixtures.Group("scd");

    [Theory]
    [MemberData(nameof(Rows))]
    public void Scd_MatchesCBuild(string kernel, string kase, string sha1)
    {
        var row = new KernelFixture("scd", kernel, kase, sha1);
        using var hash = new FixtureHasher();
        if (kase == "invalid")
        {
            Invalid(hash);
        }
        else
        {
            Detect(row, hash);
        }
        Assert.Equal(sha1, hash.Hex());
    }

    private static void Detect(KernelFixture row, FixtureHasher hash)
    {
        int w = row.Param("w"), h = row.Param("h"), cs = row.Param("cur"), rs = row.Param("ref");
        int kind = row.Param("kind"), moved = row.Param("moved");
        int bw = w >> 3, n = bw * (h >> 3);
        var r = new FixtureRng((uint)row.Param("seed"));
        using var cur = new NativeBuffer(cs * h + 2 * Margin);
        using var refBuf = new NativeBuffer(rs * h + 2 * Margin);
        r.Fill(cur.Span);
        r.Fill(refBuf.Span);
        byte* pc = cur.Ptr + Margin, pr = refBuf.Ptr + Margin;
        if (kind == 1 || kind == 2)
        {
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int v = pc[y * cs + x];
                    pr[y * rs + x] = kind == 1 ? Clip255(v + (int)(r.Next() % 9) - 4) : (byte)v;
                }
            }
        }
        if (kind == 2)
        {
            for (int b = 0; b < moved; b++)
            {
                int bx = (b % bw) * 8, by = (b / bw) * 8;
                for (int y = 0; y < 8; y++)
                {
                    for (int x = 0; x < 8; x++)
                    {
                        pc[(by + y) * cs + bx + x] = r.NextByte();
                    }
                }
            }
        }
        if (kind == 3)
        {
            for (int b = 0; b < n; b++)
            {
                int bx = (b % bw) * 8, by = (b / bw) * 8;
                for (int y = 0; y < 8; y++)
                {
                    for (int x = 0; x < 8; x++)
                    {
                        pc[(by + y) * cs + bx + x] = 100;
                        pr[(by + y) * rs + bx + x] = 100;
                    }
                }
                for (int x = 0; x < 5; x++)
                {
                    pc[by * cs + bx + x] = 164;
                }
                if ((b & 1) != 0)
                {
                    pc[by * cs + bx + 5] = 101;
                }
            }
        }

        using var idc = new NativeBuffer(n + 2);
        SSceneChangeResult init = NoiseScdResult(ref r, idc.Ptr);
        var scd = new CSceneChangeDetectionVideo(EMethods.METHOD_SCENE_CHANGE_DETECTION_VIDEO, 0);
        SPixMap mc = vaacalc_unittest.PixMap(pc, w, h, cs), mr = vaacalc_unittest.PixMap(pr, w, h, rs);
        hash.AddI32((int)scd.Set(0, &init));
        hash.AddI32((int)scd.Process(0, &mc, &mr));
        SSceneChangeResult o = default;
        hash.AddI32((int)scd.Get(0, &o));
        HashScdResult(hash, o, idc.Ptr);
        hash.AddI32((int)scd.Process(0, &mr, &mc));
        o = default;
        hash.AddI32((int)scd.Get(0, &o));
        HashScdResult(hash, o, idc.Ptr);
    }

    private static void Invalid(FixtureHasher hash)
    {
        var scd = new CSceneChangeDetectionVideo(EMethods.METHOD_SCENE_CHANGE_DETECTION_VIDEO, 0);
        hash.AddI32((int)scd.Set(0, null));
        hash.AddI32((int)scd.Get(0, null));
        SSceneChangeResult o = default;
        o.iMotionBlockNum = 7;
        hash.AddI32((int)scd.Get(0, &o));
        HashScdResult(hash, o, null);
    }
}
