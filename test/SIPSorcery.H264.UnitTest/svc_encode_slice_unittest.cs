//-----------------------------------------------------------------------------
// Filename: svc_encode_slice_unittest.cs
//
// Description: Checks the I-slice coding of svc_encode_slice against the C
// build ("svc_encode_slice" rows in reference/kernels.tsv, refharness
// src/islice_encoder.cpp KernelsSvcEncodeSlice, whose context IsliceFixtures
// follows): the slice boundary tables and macroblock neighbours over each
// slice map, and WelsISliceMdEnc over whole frames, every slice in turn, from
// the intra mode decision through the reconstruction and the CAVLC bitstream;
// and WelsCodePSlice the same way on the P context of PsliceFixtures
// (refharness src/pslice_encoder.cpp KernelsSvcEncodePSlice), from the motion
// search and the inter mode decision through the skips, the inter coding and
// the reconstruction.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 26 Nov 2026  Claude          Created (E4).
// 26 Nov 2026  Claude          WelsCodePSlice (E5).
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using SIPSorcery.H264.Encoder;
using Xunit;
using static SIPSorcery.H264.Encoder.WelsEnc;
using static SIPSorcery.H264.WelsCommon;

namespace SIPSorcery.H264.UnitTest;

public unsafe class svc_encode_slice_unittest
{
    private const string GroupName = "svc_encode_slice";

    public static IEnumerable<object[]> Rows => KernelFixtures.Group(GroupName);

    /// <summary>What a WelsISliceMdEnc row did, beyond its hash.</summary>
    private sealed record FrameStats(int Intra4x4, int Intra16x16, int QpRaised, int[] Returns);

    [Theory]
    [MemberData(nameof(Rows))]
    public void Kernel_MatchesCBuild(string kernel, string kase, string sha1)
    {
        var row = new KernelFixture(GroupName, kernel, kase, sha1);
        string actual = kernel switch
        {
            nameof(InitSliceBoundaryInfo) => Boundary(row),
            nameof(UpdateMbNeighbor) => Neighbours(row),
            nameof(WelsISliceMdEnc) => Frame(row, out _),
            nameof(WelsCodePSlice) => PFrame(row, out _),
            _ => throw new ArgumentException($"No C# kernel {kernel}."),
        };
        Assert.Equal(sha1, actual);
    }

    // The slice count, InitSliceBoundaryInfo's tables and WelsGetFirstMbOfSlice
    // over them.
    private static string Boundary(KernelFixture row)
    {
        using var c = new IsliceCtx(row.Param("w"), row.Param("h"), row.Param("slicing"), 0, 26, 0);
        using var h = new FixtureHasher();
        h.AddI32(c.Dq->sSliceEncCtx.iSliceNumInFrame);
        h.AddI32(c.BoundaryRet);
        for (int i = 0; i < c.SliceNum; i++)
        {
            h.AddI32(c.Dq->pFirstMbIdxOfSlice[i]);
            h.AddI32(c.Dq->pCountMbNumInSlice[i]);
            h.AddI32(WelsGetFirstMbOfSlice(c.Dq, i));
        }
        return h.Hex();
    }

    // Every macroblock's position, slice and neighbour flags.
    private static string Neighbours(KernelFixture row)
    {
        using var c = new IsliceCtx(row.Param("w"), row.Param("h"), row.Param("slicing"), 0, 26, 0);
        using var h = new FixtureHasher();
        for (int i = 0; i < c.MbCount; i++)
        {
            h.AddI32(c.Mbs[i].iMbX);
            h.AddI32(c.Mbs[i].iMbY);
            h.AddI32(c.Mbs[i].uiSliceIdc);
            h.AddI32(c.Mbs[i].uiNeighborAvail);
        }
        return h.Hex();
    }

    // Each slice of the frame in turn: per slice the return, the bitstream and
    // the macroblock cache, then the whole frame.
    private static string Frame(KernelFixture row, out FrameStats stats)
    {
        int qp = row.Param("qp");
        int buf = row.Param("buf");
        using var c = new IsliceCtx(row.Param("w"), row.Param("h"), row.Param("slicing"), row.Param("fast"), qp,
                                    (byte)row.Param("offset"));
        var g = new FixtureRng((uint)row.Param("seed"));
        c.Fill(ref g, row.Param("content"));
        int kiLogical = buf != 0 ? 1500 : c.MbCount * 1024 + 4096;
        var returns = new int[c.SliceNum];
        using var h = new FixtureHasher();
        for (int iSlice = 0; iSlice < c.SliceNum; iSlice++)
        {
            using var bits = new NativeBuffer(kiLogical + 64);
            g.Fill(bits.Span);
            SBitStringAux bs;
            InitBits(&bs, bits.Ptr, kiLogical);
            c.Slice->pSliceBsa = &bs;
            c.Slice->iSliceIdx = iSlice;
            h.AddI32(SetSliceBoundaryInfo(c.Dq, c.Slice, iSlice));
            c.Slice->uiLastMbQp = (byte)qp;
            c.Slice->iMbSkipRun = 0;
            returns[iSlice] = WelsISliceMdEnc(c.Ctx, c.Slice);
            h.AddI32(returns[iSlice]);
            WelsWriteSliceEndSyn(c.Slice, false);
            h.AddI32((int)(bs.pCurBuf - bs.pStartBuf));
            h.AddI32((int)bs.uiCurBits);
            h.AddI32(bs.iLeftBits);
            h.Add(bits.Span);
            h.AddI32(c.Slice->uiLastMbQp);
            h.AddI32(c.Slice->iMbSkipRun);
            c.HashCache(h);
            c.Slice->pSliceBsa = null;
        }
        c.HashFrame(h);

        int i4 = 0, i16 = 0, raised = 0;
        for (int i = 0; i < c.MbCount; i++)
        {
            i4 += c.Mbs[i].uiMbType == MB_TYPE_INTRA4x4 ? 1 : 0;
            i16 += c.Mbs[i].uiMbType == MB_TYPE_INTRA16x16 ? 1 : 0;
            raised += c.Mbs[i].uiLumaQp != qp ? 1 : 0;
        }
        stats = new FrameStats(i4, i16, raised, returns);
        return h.Hex();
    }

    /// <summary>What a WelsCodePSlice row did, beyond its hash: the macroblocks by type, and the returns.</summary>
    private sealed record PFrameStats(Dictionary<uint, int> Types, int Background, int Fractional, int QpVaried, int QpRaised,
                                      int[] Returns)
    {
        public int Count(uint uiMbType) => Types.TryGetValue(uiMbType, out int n) ? n : 0;
    }

    // Each slice of the frame in turn on the P context: per slice the return,
    // the bitstream, the macroblock cache and the slice's search state, then
    // the whole frame.
    private static string PFrame(KernelFixture row, out PFrameStats stats)
    {
        int qp = row.Param("qp");
        int buf = row.Param("buf");
        using var p = new PsliceCtx(row.Param("w"), row.Param("h"), row.Param("slicing"), row.Param("complexity"),
                                    row.Param("bgd"), row.Param("aq"), qp, (byte)row.Param("offset"), row.Param("mvrange"));
        IsliceCtx c = p.C;
        var g = new FixtureRng((uint)row.Param("seed"));
        p.Fill(ref g, row.Param("content"), row.Param("motion"), row.Param("noise"), row.Param("reftype"), qp);
        int kiLogical = buf != 0 ? 1500 : c.MbCount * 1024 + 4096;
        var returns = new int[c.SliceNum];
        using var h = new FixtureHasher();
        for (int iSlice = 0; iSlice < c.SliceNum; iSlice++)
        {
            using var bits = new NativeBuffer(kiLogical + 64);
            g.Fill(bits.Span);
            SBitStringAux bs;
            InitBits(&bs, bits.Ptr, kiLogical);
            c.Slice->pSliceBsa = &bs;
            c.Slice->iSliceIdx = iSlice;
            h.AddI32(SetSliceBoundaryInfo(c.Dq, c.Slice, iSlice));
            c.Slice->uiLastMbQp = (byte)qp;
            returns[iSlice] = WelsCodePSlice(c.Ctx, c.Slice);
            h.AddI32(returns[iSlice]);
            WelsWriteSliceEndSyn(c.Slice, false);
            h.AddI32((int)(bs.pCurBuf - bs.pStartBuf));
            h.AddI32((int)bs.uiCurBits);
            h.AddI32(bs.iLeftBits);
            h.Add(bits.Span);
            h.AddI32(c.Slice->uiLastMbQp);
            h.AddI32(c.Slice->iMbSkipRun);
            p.HashCache(h);
            p.HashSlice(h);
            c.Slice->pSliceBsa = null;
        }
        p.HashFrame(h);

        // A background skip saves MB_TYPE_BACKGROUND as the reference type
        // before WelsMdUpdateBGDInfo makes it MB_TYPE_SKIP.
        var types = new Dictionary<uint, int>();
        int background = 0, fractional = 0, varied = 0, raised = 0;
        for (int i = 0; i < c.MbCount; i++)
        {
            SMB* pMb = &c.Mbs[i];
            types[pMb->uiMbType] = types.TryGetValue(pMb->uiMbType, out int k) ? k + 1 : 1;
            background += c.DecPic->uiRefMbType[i] == MB_TYPE_BACKGROUND ? 1 : 0;
            fractional += IS_INTER(pMb->uiMbType) && ((pMb->sMv[0].iMvX & 3) | (pMb->sMv[0].iMvY & 3) | (pMb->sMv[15].iMvX & 3) | (pMb->sMv[15].iMvY & 3)) != 0 ? 1 : 0;
            varied += pMb->uiLumaQp != qp ? 1 : 0;
            raised += pMb->uiLumaQp > qp + 6 ? 1 : 0;
        }
        stats = new PFrameStats(types, background, fractional, varied, raised, returns);
        return h.Hex();
    }

    private static FrameStats StatsOf(string kase)
    {
        KernelFixture row = KernelFixtures.Row(GroupName, nameof(WelsISliceMdEnc), kase);
        Assert.Equal(row.Sha1, Frame(row, out FrameStats stats));
        return stats;
    }

    /// <summary>
    /// The rows reach what they are there for: both macroblock types, the
    /// level overflow retry at low QP, and the bitstream check giving up.
    /// </summary>
    [Fact]
    public void WelsISliceMdEnc_RowsReachEveryPath()
    {
        FrameStats blocky = StatsOf("w=80,h=48,slicing=2,fast=0,qp=0,offset=12,content=2,seed=0,buf=0");
        Assert.True(blocky.Intra4x4 > 0 && blocky.Intra16x16 > 0);
        Assert.True(blocky.QpRaised > 0);
        Assert.All(blocky.Returns, r => Assert.Equal(ENC_RETURN_SUCCESS, r));

        FrameStats small = StatsOf("w=176,h=144,slicing=0,fast=1,qp=12,offset=12,content=0,seed=0,buf=1");
        Assert.Equal(ENC_RETURN_VLCOVERFLOWFOUND, small.Returns[0]);
    }

    private static PFrameStats PStatsOf(string kase)
    {
        KernelFixture row = KernelFixtures.Row(GroupName, nameof(WelsCodePSlice), kase);
        Assert.Equal(row.Sha1, PFrame(row, out PFrameStats stats));
        return stats;
    }

    /// <summary>
    /// The P rows reach what they are there for: every inter partition, intra
    /// macroblocks of both kinds, sub-pel motion vectors, skips and background
    /// skips, adaptive quantisation, the level overflow retry at QP 0, and the
    /// bitstream check giving up.
    /// </summary>
    [Fact]
    public void WelsCodePSlice_RowsReachEveryPath()
    {
        PFrameStats moving = PStatsOf("w=176,h=144,slicing=2,complexity=0,bgd=1,aq=0,qp=0,offset=6,mvrange=64,content=2,motion=3," +
                                      "noise=2,reftype=1,seed=1,buf=0");
        foreach (int t in new[] { MB_TYPE_INTRA4x4, MB_TYPE_16x16, MB_TYPE_16x8, MB_TYPE_8x16, MB_TYPE_8x8 })
        {
            Assert.True(moving.Count((uint)t) > 0, $"No macroblock of type {t:x}.");
        }
        Assert.True(moving.Fractional > 0);
        Assert.True(moving.QpVaried > 0);
        Assert.All(moving.Returns, r => Assert.Equal(ENC_RETURN_SUCCESS, r));

        PFrameStats still = PStatsOf("w=40,h=24,slicing=1,complexity=0,bgd=1,aq=0,qp=40,offset=6,mvrange=64,content=1,motion=0," +
                                     "noise=6,reftype=1,seed=1,buf=0");
        Assert.True(still.Count(MB_TYPE_SKIP) > 0);
        Assert.True(still.Background > 0);

        PFrameStats aq = PStatsOf("w=80,h=48,slicing=2,complexity=0,bgd=0,aq=1,qp=40,offset=0,mvrange=6,content=0,motion=2," +
                                  "noise=0,reftype=0,seed=1,buf=0");
        Assert.True(aq.QpVaried > 0);
        Assert.True(aq.Count(MB_TYPE_INTRA16x16) > 0);

        PFrameStats small = PStatsOf("w=176,h=144,slicing=0,complexity=1,bgd=0,aq=0,qp=12,offset=0,mvrange=64,content=0,motion=3," +
                                     "noise=6,reftype=0,seed=0,buf=1");
        Assert.Equal(ENC_RETURN_VLCOVERFLOWFOUND, small.Returns[0]);
    }

    [Fact]
    public void Group_HasEveryKernel()
    {
        var kernels = KernelFixtures.Group(GroupName).Select(r => (string)r[0]).Distinct().OrderBy(k => k, StringComparer.Ordinal);
        Assert.Equal(new[] { nameof(InitSliceBoundaryInfo), nameof(UpdateMbNeighbor), nameof(WelsCodePSlice), nameof(WelsISliceMdEnc) }, kernels);
    }
}
