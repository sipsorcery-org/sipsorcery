//-----------------------------------------------------------------------------
// Filename: set_mb_syn_cavlc_unittest.cs
//
// Description: Checks set_mb_syn_cavlc against the C build ("set_mb_syn_cavlc"
// rows in reference/kernels.tsv, refharness src/cavlc_encoder.cpp
// KernelsSetMbSynCavlc, whose draws CavlcFixtures follows): CavlcParamCal_c,
// WriteBlockResidualCavlc, the CAVLC and CABAC stash/pop pairs, the slice end,
// and the slots InitCoeffFunc fills. Every residual row the writer accepts is
// also read back with the decoder's CAVLC parser (WelsResidualBlockCavlc),
// which must use the same bits and return the same coefficients.
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
using System.Runtime.InteropServices;
using SIPSorcery.H264.Encoder;
using Xunit;
using static SIPSorcery.H264.Encoder.WelsEnc;
using static SIPSorcery.H264.WelsCommon;
using Dec = SIPSorcery.H264.Decoder.WelsDec;

namespace SIPSorcery.H264.UnitTest;

public unsafe class set_mb_syn_cavlc_unittest
{
    private const string GroupName = "set_mb_syn_cavlc";

    public static IEnumerable<object[]> Rows => KernelFixtures.Group(GroupName);

    [Theory]
    [MemberData(nameof(Rows))]
    public void Kernel_MatchesCBuild(string kernel, string kase, string sha1)
    {
        var row = new KernelFixture(GroupName, kernel, kase, sha1);
        using var c = new WriterCtx(EWelsSliceType.P_SLICE, 1, 0);
        string actual = kernel switch
        {
            nameof(CavlcParamCal_c) => ParamCal(&CavlcParamCal_c, row),
            nameof(WriteBlockResidualCavlc) => Residual(c.Funcs, row),
            nameof(StashMBStatusCavlc) => StashCavlc(&StashMBStatusCavlc, &StashPopMBStatusCavlc, &GetBsPosCavlc, row),
            nameof(StashMBStatusCabac) => StashCabac(row),
            nameof(WelsWriteSliceEndSyn) => SliceEnd(row),
            _ => throw new ArgumentException($"No C# kernel {kernel}."),
        };
        Assert.Equal(sha1, actual);
    }

    // 4 guard entries around each buffer.
    internal static string ParamCal(delegate*<short*, byte*, short*, int*, int, int> f, KernelFixture row)
    {
        int last = row.Param("last");
        var g = new FixtureRng((uint)row.Param("seed"));
        short* coef = stackalloc short[24];
        short* level = stackalloc short[24];
        byte* run = stackalloc byte[24];
        for (int i = 0; i < 24; i++)
        {
            coef[i] = (short)(g.Next() >> 16);
        }
        for (int i = 0; i <= last; i++)
        {
            coef[4 + i] = CavlcFixtures.Coef(ref g, row.Param("density"), row.Param("range"));
        }
        for (int i = 0; i < 24; i++)
        {
            level[i] = (short)(g.Next() >> 16);
        }
        g.Fill(new Span<byte>(run, 24));
        int iTotal = (int)g.Next();

        int iRet = f(coef + 4, run + 4, level + 4, &iTotal, last);

        using var h = new FixtureHasher();
        h.AddI32(iRet);
        h.AddI32(iTotal);
        h.Add(new ReadOnlySpan<byte>(coef, 48));
        h.Add(new ReadOnlySpan<byte>(level, 48));
        h.Add(new ReadOnlySpan<byte>(run, 24));
        return h.Hex();
    }

    /// <summary>
    /// One residual row's inputs: the coefficient buffer (4 guards each side)
    /// and the writer after its pre bits. The block is random ("seed" rows),
    /// the level sweep's ("level") or the count sweep's ("count").
    /// </summary>
    private sealed class ResidualCase : IDisposable
    {
        public readonly short* Coef = (short*)NativeMemory.Alloc(24 * sizeof(short));
        public readonly int End, Prop, Nc, Flag, Pre;
        public readonly FixtureBs W;

        public ResidualCase(KernelFixture row)
        {
            End = row.Param("end");
            Prop = row.Param("prop");
            Nc = row.Param("nc");
            Flag = row.Param("flag");
            bool bLevel = IsLevelSweep(row), bCount = row.Case.Contains("count=");
            var g = bLevel ? new FixtureRng((uint)(row.Param("suffix") * 10007 + row.Param("level")))
                    : bCount ? new FixtureRng((uint)(Nc * 100 + row.Param("count") * 4 + row.Param("ones")))
                    : new FixtureRng((uint)row.Param("seed"));
            for (int i = 0; i < 24; i++)
            {
                Coef[i] = (short)(g.Next() >> 16);
            }
            if (bLevel)
            {
                SweepBlock(Coef + 4, row.Param("suffix"), row.Param("level"));
            }
            else if (bCount)
            {
                CountBlock(Coef + 4, End, row.Param("count"), row.Param("ones"));
            }
            else
            {
                for (int i = 0; i <= End; i++)
                {
                    Coef[4 + i] = CavlcFixtures.Coef(ref g, row.Param("density"), row.Param("range"));
                }
            }
            Pre = (int)(g.Next() % 32);
            W = new FixtureBs(ref g, 256, 256, Pre);
        }

        // fixture ResidualSweepBlock: the raisers that bring the suffix
        // length to s, then v, then 1, from coefficient 15 down.
        private static void SweepBlock(short* pCoef, int s, int v)
        {
            int[] raisers = { 4, 7, 13, 25, 49 };
            var l = new List<int>();
            if (s == 1)
            {
                l.Add(2);
            }
            for (int i = 0; i + 1 < s; i++)
            {
                l.Add(raisers[i]);
            }
            l.Add(v);
            l.Add(1);
            new Span<short>(pCoef, 16).Clear();
            for (int k = 0; k < l.Count; k++)
            {
                pCoef[15 - k] = (short)l[k];
            }
        }

        // fixture ResidualCountBlock: n coefficients spread over 0..end, the
        // first t (from the top) trailing ones, the rest of magnitude 2 to 4.
        private static void CountBlock(short* pCoef, int end, int n, int t)
        {
            new Span<short>(pCoef, end + 1).Clear();
            for (int k = 0; k < n; k++)
            {
                int p = end - k * (end + 1) / n;
                int v = k < t ? 1 : 2 + (n + k) % 3;
                pCoef[p] = (short)(((n * 3 + k) & 1) != 0 ? -v : v);
            }
        }

        public int Write(SWelsFuncPtrList* funcs) => WriteBlockResidualCavlc(funcs, Coef + 4, End, Flag, Prop, (sbyte)Nc, W.Bs);

        public void Dispose()
        {
            W.Dispose();
            NativeMemory.Free(Coef);
        }
    }

    internal static string Residual(SWelsFuncPtrList* funcs, KernelFixture row)
    {
        using var rc = new ResidualCase(row);
        int iRet = rc.Write(funcs);

        using var h = new FixtureHasher();
        h.AddI32(iRet);
        rc.W.Hash(h);
        h.Add(new ReadOnlySpan<byte>(rc.Coef, 48));
        return h.Hex();
    }

    // Stash the slice's writer state, write on, pop.
    internal static string StashCavlc(delegate*<SDynamicSlicingStack*, SSlice*, int, void> stash, delegate*<SDynamicSlicingStack*, SSlice*, int> pop,
                                      delegate*<SSlice*, int> pos, KernelFixture row)
    {
        var g = new FixtureRng((uint)row.Param("seed"));
        using var c = new WriterCtx(EWelsSliceType.P_SLICE, 1, 0);
        using var w = new FixtureBs(ref g, 256, 256, (int)(g.Next() % 32));
        c.Slice->pSliceBsa = w.Bs;
        c.Slice->uiLastMbQp = (byte)(g.Next() % 52);
        var dss = default(SDynamicSlicingStack);
        int kiSkipRun = (int)(g.Next() % 100);
        stash(&dss, c.Slice, kiSkipRun);

        using var h = new FixtureHasher();
        h.AddI32((int)(dss.pBsStackBufPtr - w.Bs->pStartBuf));
        h.AddI32((int)dss.uiBsStackCurBits);
        h.AddI32(dss.iBsStackLeftBits);
        h.AddI32(dss.uiLastMbQp);
        h.AddI32(dss.iMbSkipRunStack);
        BsWriteBits(w.Bs, 20, g.Next() >> 12);
        BsWriteBits(w.Bs, 30, g.Next() >> 2);
        c.Slice->uiLastMbQp = (byte)(g.Next() % 52);
        h.AddI32(pos(c.Slice));
        h.AddI32(pop(&dss, c.Slice));
        h.AddI32(pos(c.Slice));
        h.AddI32(c.Slice->uiLastMbQp);
        w.Hash(h);
        return h.Hex();
    }

    // The CABAC pair copies the context and, with a restore buffer, the bytes
    // written since iStartPos.
    private static string StashCabac(KernelFixture row)
    {
        var g = new FixtureRng((uint)row.Param("seed"));
        using var c = new WriterCtx(EWelsSliceType.P_SLICE, 1, 0);
        using var buf = new NativeBuffer(512);
        using var saved = new NativeBuffer(512);
        g.Fill(buf.Span);
        g.Fill(saved.Span);
        SCabacCtx* cc = &c.Slice->sCabacCtx;
        cc->m_uiLow = ((ulong)g.Next() << 32) | g.Next();
        cc->m_iLowBitCnt = 9 + (int)(g.Next() % 40);
        cc->m_iRenormCnt = (int)(g.Next() % 8);
        cc->m_uiRange = 256 + g.Next() % 256;
        for (int i = 0; i < WELS_CONTEXT_COUNT; i++)
        {
            cc->m_sStateCtx[i].m_uiStateMps = (byte)g.Next();
        }
        cc->m_pBufStart = buf.Ptr;
        cc->m_pBufEnd = buf.Ptr + 512;
        cc->m_pBufCur = buf.Ptr + 32 + g.Next() % 200;
        c.Slice->uiLastMbQp = (byte)(g.Next() % 52);
        var dss = default(SDynamicSlicingStack);
        dss.iStartPos = (int)(g.Next() % 64);
        dss.pRestoreBuffer = row.Param("restore") != 0 ? saved.Ptr : null;
        int kiSkipRun = (int)(g.Next() % 100);
        StashMBStatusCabac(&dss, c.Slice, kiSkipRun);

        using var h = new FixtureHasher();
        h.AddI32(GetBsPosCabac(c.Slice));
        h.AddI64((long)dss.sStoredCabac.m_uiLow);
        h.AddI32(dss.sStoredCabac.m_iLowBitCnt);
        h.AddI32(dss.sStoredCabac.m_iRenormCnt);
        h.AddI32((int)dss.sStoredCabac.m_uiRange);
        h.Add(new ReadOnlySpan<byte>(&dss.sStoredCabac.m_sStateCtx, WELS_CONTEXT_COUNT));
        h.AddI32((int)(dss.sStoredCabac.m_pBufCur - buf.Ptr));
        h.AddI32(dss.uiLastMbQp);
        h.AddI32(dss.iMbSkipRunStack);
        h.Add(saved.Span);
        // Write on: move the context and scribble over the slice's buffer.
        cc->m_uiLow = g.Next();
        cc->m_iLowBitCnt = 9 + (int)(g.Next() % 40);
        cc->m_pBufCur += g.Next() % 100;
        for (int i = 0; i < WELS_CONTEXT_COUNT; i++)
        {
            cc->m_sStateCtx[i].m_uiStateMps = (byte)g.Next();
        }
        g.Fill(buf.Span);
        c.Slice->uiLastMbQp = (byte)(g.Next() % 52);
        h.AddI32(StashPopMBStatusCabac(&dss, c.Slice));
        h.AddI32(GetBsPosCabac(c.Slice));
        h.AddI64((long)cc->m_uiLow);
        h.AddI32(cc->m_iLowBitCnt);
        h.Add(new ReadOnlySpan<byte>(&cc->m_sStateCtx, WELS_CONTEXT_COUNT));
        h.AddI32((int)(cc->m_pBufCur - buf.Ptr));
        h.AddI32(c.Slice->uiLastMbQp);
        h.Add(buf.Span);
        return h.Hex();
    }

    // Trailing bits and flush, from every bit position.
    private static string SliceEnd(KernelFixture row)
    {
        int pre = row.Param("pre");
        var g = new FixtureRng((uint)pre);
        using var c = new WriterCtx(EWelsSliceType.P_SLICE, 1, 0);
        using var w = new FixtureBs(ref g, 64, 64, 0);
        for (int left = pre; left > 0; left -= Math.Min(left, 24))
        {
            int n = Math.Min(left, 24);
            BsWriteBits(w.Bs, n, g.Next() >> (32 - n));
        }
        c.Slice->pSliceBsa = w.Bs;
        WelsWriteSliceEndSyn(c.Slice, false);

        using var h = new FixtureHasher();
        w.Hash(h);
        return h.Hex();
    }

    /// <summary>
    /// The decoder reads back every block the writer accepted: the same number
    /// of bits, and the coefficients the writer was given. The decoder
    /// dequantises as it parses, so its scaling-list path gets a flat scale
    /// that gives the levels back (16, as (level * 16 + 8) >> 4; 32 for the
    /// chroma DC, whose 2x2 transform is then undone).
    /// </summary>
    [Theory]
    [MemberData(nameof(Rows))]
    public void Residual_RoundTripsThroughTheDecoder(string kernel, string kase, string sha1)
    {
        if (kernel != nameof(WriteBlockResidualCavlc))
        {
            return;
        }
        var row = new KernelFixture(GroupName, kernel, kase, sha1);
        if (row.Param("flag") == 0)
        {
            return; // writes the token for no coefficients, whatever the block holds
        }
        using var c = new WriterCtx(EWelsSliceType.P_SLICE, 1, 0);
        using var rc = new ResidualCase(row);
        if (rc.Write(c.Funcs) != ENC_RETURN_SUCCESS)
        {
            return; // a level the writer can't code
        }
        int iBits = BsGetBitsPos(rc.W.Bs) - rc.Pre;
        BsFlush(rc.W.Bs);

        short[] decoded = ReadBack(rc.W.Buf, rc.W.Length, rc.Pre, rc.End, rc.Prop, rc.Nc, out int iUsed);

        Assert.Equal(iBits, iUsed);
        Assert.Equal(new ReadOnlySpan<short>(rc.Coef + 4, rc.End + 1).ToArray(), decoded);
    }

    // The decoder's call for each of the encoder's block kinds, with nC taken
    // from a cache whose left and top neighbours both hold nc.
    private static short[] ReadBack(byte* pBuf, int iLength, int iStartBit, int iEnd, int iProp, int iNc, out int iUsed)
    {
        var pCtx = CavlcFixtures.Alloc<SIPSorcery.H264.Decoder.SWelsDecoderContext>();
        var pScale = (InlineArray16<ushort>*)NativeMemory.Alloc(52 * 16 * sizeof(ushort));
        try
        {
            ushort scale = (ushort)(iProp == CHROMA_DC ? 32 : 16);
            for (int q = 0; q < 52; q++)
            {
                for (int j = 0; j < 16; j++)
                {
                    pScale[q][j] = scale;
                }
            }
            pCtx->bUseScalingList = true;
            for (int i = 0; i < 6; i++)
            {
                pCtx->pDequant_coeff4x4[i] = pScale;
            }
            var vlc = default(SIPSorcery.H264.Decoder.SVlcTable);
            Dec.InitVlcTable(&vlc);

            int iIndex, iDecProp, iMaxNumCoeff;
            switch (iProp)
            {
                case LUMA_4x4: iIndex = 0; iDecProp = Dec.LUMA_DC_AC_INTRA; iMaxNumCoeff = 16; break;
                case LUMA_AC: iIndex = 0; iDecProp = Dec.I16_LUMA_AC; iMaxNumCoeff = 15; break;
                case CHROMA_AC: iIndex = 16; iDecProp = Dec.CHROMA_AC_U; iMaxNumCoeff = 15; break;
                case CHROMA_DC: iIndex = 16; iDecProp = Dec.CHROMA_DC_U; iMaxNumCoeff = 4; break;
                default: throw new ArgumentException($"No block kind {iProp}.");
            }
            byte* pNzc = stackalloc byte[48];
            int iSlot = g_kuiCache48CountScan4Idx[iIndex];
            pNzc[iSlot - 1] = (byte)iNc;
            pNzc[iSlot - 8] = (byte)iNc;

            // Coefficient j lands at kpZigzag[j]: in order for the 4x4 blocks,
            // at the chroma DC positions for the chroma DC.
            byte* pZigzag = stackalloc byte[16];
            for (int j = 0; j < 16; j++)
            {
                pZigzag[j] = iProp == CHROMA_DC ? (j < 4 ? Dec.g_kuiChromaDcScan[j] : (byte)0) : (byte)j;
            }
            short* pCoeff = stackalloc short[64];
            new Span<short>(pCoeff, 64).Clear();

            var bs = default(SBitStringAux);
            bs.pStartBuf = pBuf;
            bs.pEndBuf = pBuf + iLength;
            bs.iIndex = iStartBit;
            int iRet = Dec.WelsResidualBlockCavlc(&vlc, pNzc, &bs, iIndex, iMaxNumCoeff, pZigzag, iDecProp, pCoeff, 0, pCtx);
            Assert.Equal(0, iRet);
            iUsed = (int)bs.iIndex - iStartBit;

            var levels = new short[iEnd + 1];
            if (iProp == CHROMA_DC)
            {
                // Undo the 2x2 transform (out = A+B+C+D, A-B+C-D, A+B-C-D, A-B-C+D).
                int o0 = pCoeff[0], o1 = pCoeff[16], o2 = pCoeff[32], o3 = pCoeff[48];
                levels[0] = (short)((o0 + o1 + o2 + o3) / 4);
                levels[1] = (short)((o0 - o1 + o2 - o3) / 4);
                levels[2] = (short)((o0 + o1 - o2 - o3) / 4);
                levels[3] = (short)((o0 - o1 - o2 + o3) / 4);
            }
            else
            {
                for (int j = 0; j <= iEnd; j++)
                {
                    levels[j] = pCoeff[j];
                }
            }
            return levels;
        }
        finally
        {
            NativeMemory.Free(pScale);
            NativeMemory.Free(pCtx);
        }
    }

    private static bool IsLevelSweep(KernelFixture row) => row.Case.Contains("level=");

    /// <summary>
    /// At every suffix length the level sweep codes the levels just below the
    /// overflow and rejects those at it, so the overflow check is decided
    /// there and not only by chance in the random rows.
    /// </summary>
    [Fact]
    public void LevelSweep_DecidesTheOverflowAtEverySuffixLength()
    {
        using var c = new WriterCtx(EWelsSliceType.P_SLICE, 1, 0);
        var sweep = KernelFixtures.All.Where(r => r.Group == GroupName && r.Kernel == nameof(WriteBlockResidualCavlc) && IsLevelSweep(r))
                                      .ToList();
        for (int s = 0; s <= 6; s++)
        {
            var near = sweep.Where(r => r.Param("suffix") == s && Math.Abs(r.Param("level")) > 1000)
                            .Select(r =>
                            {
                                using var rc = new ResidualCase(r);
                                return rc.Write(c.Funcs);
                            })
                            .ToList();
            Assert.Contains(ENC_RETURN_SUCCESS, near);
            Assert.Contains(ENC_RETURN_VLCOVERFLOWFOUND, near);
        }
    }

    /// <summary>
    /// The random residual rows must reach the overflow return and also code
    /// large levels.
    /// </summary>
    [Fact]
    public void ResidualRows_ReachTheOverflowBothWays()
    {
        using var c = new WriterCtx(EWelsSliceType.P_SLICE, 1, 0);
        int overflow = 0, codedLarge = 0;
        foreach (var row in KernelFixtures.All.Where(r => r.Group == GroupName && r.Kernel == nameof(WriteBlockResidualCavlc) &&
                                                          r.Case.Contains("seed=")))
        {
            using var rc = new ResidualCase(row);
            if (rc.Write(c.Funcs) == ENC_RETURN_VLCOVERFLOWFOUND)
            {
                overflow++;
            }
            else if (row.Param("range") == 3000 && row.Param("density") > 0 && row.Param("flag") == 1)
            {
                codedLarge++;
            }
        }
        Assert.True(overflow > 0);
        Assert.True(codedLarge > 0);
    }

    /// <summary>
    /// The slots InitCoeffFunc fills each reproduce a row of the function
    /// upstream assigns them (function pointers can't be compared reliably).
    /// </summary>
    [Fact]
    public void InitCoeffFunc_FillsTheCavlcSlots()
    {
        using var c = new WriterCtx(EWelsSliceType.P_SLICE, 1, 0);
        var paramCal = KernelFixtures.All.First(r => r.Group == GroupName && r.Kernel == nameof(CavlcParamCal_c) && r.Case.Contains("density=10"));
        Assert.Equal(paramCal.Sha1, ParamCal(c.Funcs->pfCavlcParamCal, paramCal));
        foreach (var stash in KernelFixtures.All.Where(r => r.Group == GroupName && r.Kernel == nameof(StashMBStatusCavlc)))
        {
            Assert.Equal(stash.Sha1, StashCavlc(c.Funcs->pfStashMBStatus, c.Funcs->pfStashPopMBStatus, c.Funcs->pfGetBsPosition, stash));
        }
        var mbSyn = KernelFixtures.All.First(r => r.Group == "svc_set_mb_syn_cavlc" && r.Kernel == nameof(WelsSpatialWriteMbSyn) &&
                                                  r.Case.StartsWith("slice=0,refs=2,skip=1"));
        Assert.Equal(mbSyn.Sha1, svc_set_mb_syn_cavlc_unittest.MbSyn(mbSyn, useSlot: true));
    }

    [Fact]
    public void InitCoeffFunc_RejectsCabac()
    {
        var pList = CavlcFixtures.Alloc<SWelsFuncPtrList>();
        try
        {
            Assert.Throws<NotSupportedException>(() => InitCoeffFunc(pList, 0, 1));
        }
        finally
        {
            NativeMemory.Free(pList);
        }
    }

    [Fact]
    public void Group_HasEveryKernel()
    {
        var kernels = KernelFixtures.Group(GroupName).Select(r => (string)r[0]).Distinct().OrderBy(k => k, StringComparer.Ordinal);
        Assert.Equal(
            new[]
            {
                nameof(CavlcParamCal_c), nameof(StashMBStatusCabac), nameof(StashMBStatusCavlc), nameof(WelsWriteSliceEndSyn),
                nameof(WriteBlockResidualCavlc),
            },
            kernels);
    }
}
