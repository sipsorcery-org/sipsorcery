//-----------------------------------------------------------------------------
// Filename: svc_set_mb_syn_cavlc_unittest.cs
//
// Description: Checks the CAVLC macroblock writers of svc_set_mb_syn_cavlc
// against the C build ("svc_set_mb_syn_cavlc" rows in reference/kernels.tsv,
// refharness src/cavlc_encoder.cpp KernelsSvcSetMbSynCavlc, whose draws
// CavlcFixtures follows): the prediction and sub-macroblock prediction syntax,
// the residual, the buffer check, and slices of 8 macroblocks through
// WelsSpatialWriteMbSyn.
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
using SIPSorcery.H264.Encoder;
using Xunit;
using static SIPSorcery.H264.Encoder.WelsEnc;
using static SIPSorcery.H264.WelsCommon;

namespace SIPSorcery.H264.UnitTest;

public unsafe class svc_set_mb_syn_cavlc_unittest
{
    private const string GroupName = "svc_set_mb_syn_cavlc";

    private static readonly uint[] ResTypes = { MB_TYPE_INTRA16x16, MB_TYPE_INTRA4x4, MB_TYPE_16x16 };

    public static IEnumerable<object[]> Rows => KernelFixtures.Group(GroupName);

    [Theory]
    [MemberData(nameof(Rows))]
    public void Kernel_MatchesCBuild(string kernel, string kase, string sha1)
    {
        var row = new KernelFixture(GroupName, kernel, kase, sha1);
        string actual = kernel switch
        {
            nameof(WelsSpatialWriteMbPred) => MbPred(row),
            nameof(WelsSpatialWriteSubMbPred) => SubMbPred(row),
            nameof(WelsWriteMbResidual) => MbResidual(row),
            nameof(CheckBitstreamBuffer) => BufferCheck(row, out _),
            nameof(WelsSpatialWriteMbSyn) => MbSyn(row, useSlot: false),
            _ => throw new ArgumentException($"No C# kernel {kernel}."),
        };
        Assert.Equal(sha1, actual);
    }

    private static string MbPred(KernelFixture row)
    {
        int refs = row.Param("refs");
        var g = new FixtureRng((uint)row.Param("seed"));
        using var c = new WriterCtx(CavlcFixtures.SliceTypes[row.Param("slice")], refs, 0);
        using var f = new MbFixture();
        CavlcFixtures.FillMb(ref g, f, &c.Slice->sMbCacheInfo, CavlcFixtures.PredTypes[row.Param("type")], refs, 32767, 4, 20);
        using var w = new FixtureBs(ref g, 512, 512, (int)(g.Next() % 32));
        c.Slice->pSliceBsa = w.Bs;
        WelsSpatialWriteMbPred(c.Ctx, c.Slice, f.Mb);

        using var h = new FixtureHasher();
        w.Hash(h);
        return h.Hex();
    }

    // Random sub types, references (a third all 0, which is P_8x8ref0) and
    // motion vector differences.
    private static string SubMbPred(KernelFixture row)
    {
        int refs = row.Param("refs");
        var g = new FixtureRng((uint)row.Param("seed"));
        using var c = new WriterCtx(EWelsSliceType.P_SLICE, refs, 0);
        using var f = new MbFixture();
        CavlcFixtures.FillMb(ref g, f, &c.Slice->sMbCacheInfo, MB_TYPE_8x8, refs, 8191, 4, 20);
        using var w = new FixtureBs(ref g, 512, 512, (int)(g.Next() % 32));
        c.Slice->pSliceBsa = w.Bs;
        WelsSpatialWriteSubMbPred(c.Ctx, c.Slice, f.Mb);

        using var h = new FixtureHasher();
        w.Hash(h);
        return h.Hex();
    }

    private static string MbResidual(KernelFixture row)
    {
        var g = new FixtureRng((uint)row.Param("seed"));
        using var c = new WriterCtx(EWelsSliceType.P_SLICE, 1, 0);
        using var f = new MbFixture();
        CavlcFixtures.FillMb(ref g, f, &c.Slice->sMbCacheInfo, ResTypes[row.Param("type")], 1, 64, row.Param("density"), row.Param("range"));
        using var w = new FixtureBs(ref g, 4096, 4096, (int)(g.Next() % 32));
        int iRet = WelsWriteMbResidual(c.Funcs, &c.Slice->sMbCacheInfo, f.Mb, w.Bs);

        using var h = new FixtureHasher();
        h.AddI32(iRet);
        w.Hash(h);
        return h.Hex();
    }

    // Either side of the limit (MAX_MACROBLOCK_SIZE_IN_BYTE_x2 bytes left).
    private static string BufferCheck(KernelFixture row, out int iRet)
    {
        int left = row.Param("left");
        var g = new FixtureRng((uint)left);
        using var c = new WriterCtx(EWelsSliceType.P_SLICE, 1, 0);
        using var w = new FixtureBs(ref g, 4096, 4096, 0);
        w.Bs->pCurBuf += g.Next() % 1000;
        w.Bs->pEndBuf = w.Bs->pCurBuf + left + 1;
        iRet = CheckBitstreamBuffer(0, c.Ctx, w.Bs);

        using var h = new FixtureHasher();
        h.AddI32(iRet);
        return h.Hex();
    }

    /// <summary>
    /// A slice of 8 macroblocks, each type drawn, then the slice end; with
    /// useSlot the macroblocks go through the slot InitCoeffFunc filled.
    /// </summary>
    internal static string MbSyn(KernelFixture row, bool useSlot)
    {
        int refs = row.Param("refs");
        int skip = row.Param("skip");
        int mv = row.Param("mv");
        int range = row.Param("range");
        int density = row.Param("density");
        EWelsSliceType eType = CavlcFixtures.SliceTypes[row.Param("slice")];
        var g = new FixtureRng((uint)row.Param("seed"));
        using var c = new WriterCtx(eType, refs, (byte)row.Param("offset"));
        using var w = new FixtureBs(ref g, 16384, row.Param("end"), (int)(g.Next() % 32));
        c.Slice->pSliceBsa = w.Bs;
        c.Slice->uiLastMbQp = (byte)(g.Next() % 52);
        delegate*<sWelsEncCtx*, SSlice*, SMB*, int> write = useSlot ? c.Funcs->pfWelsSpatialWriteMbSyn : &WelsSpatialWriteMbSyn;

        using var h = new FixtureHasher();
        using var f = new MbFixture();
        for (int m = 0; m < 8; m++)
        {
            uint uiType;
            if (eType == EWelsSliceType.I_SLICE)
            {
                uiType = (g.Next() & 1) != 0 ? (uint)MB_TYPE_INTRA16x16 : MB_TYPE_INTRA4x4;
            }
            else if (skip != 0 && (g.Next() & 1) != 0)
            {
                uiType = (uint)MB_TYPE_SKIP;
            }
            else
            {
                uiType = CavlcFixtures.PTypes[g.Next() % 7];
            }
            CavlcFixtures.FillMb(ref g, f, &c.Slice->sMbCacheInfo, uiType, refs, mv, density, range);
            h.AddI32(write(c.Ctx, c.Slice, f.Mb));
            h.AddI32(f.Mb->uiLumaQp);
            h.AddI32(f.Mb->uiChromaQp);
            h.AddI32(c.Slice->iMbSkipRun);
            h.AddI32(c.Slice->uiLastMbQp);
        }
        WelsWriteSliceEndSyn(c.Slice, false);
        w.Hash(h);
        return h.Hex();
    }

    /// <summary>The buffer check rows land on both sides of the limit.</summary>
    [Fact]
    public void CheckBitstreamBuffer_RowsDecideBothWays()
    {
        var results = KernelFixtures.All.Where(r => r.Group == GroupName && r.Kernel == nameof(CheckBitstreamBuffer))
                                        .Select(r => { BufferCheck(r, out int iRet); return iRet; })
                                        .ToList();
        Assert.Contains(ENC_RETURN_SUCCESS, results);
        Assert.Contains(results, r => r != ENC_RETURN_SUCCESS);
    }

    [Fact]
    public void Group_HasEveryKernel()
    {
        var kernels = KernelFixtures.Group(GroupName).Select(r => (string)r[0]).Distinct().OrderBy(k => k, StringComparer.Ordinal);
        Assert.Equal(
            new[]
            {
                nameof(CheckBitstreamBuffer), nameof(WelsSpatialWriteMbPred), nameof(WelsSpatialWriteMbSyn),
                nameof(WelsSpatialWriteSubMbPred), nameof(WelsWriteMbResidual),
            },
            kernels);
    }
}
