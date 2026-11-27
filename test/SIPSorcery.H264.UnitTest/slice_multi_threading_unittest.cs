//-----------------------------------------------------------------------------
// Filename: slice_multi_threading_unittest.cs
//
// Description: Checks the slice threads' load balancing in
// slice_multi_threading.cs against the C build ("slice_mt" rows in
// reference/kernels.tsv, refharness src/kernels_slice_mt.cpp). The encoder
// times its slices with WelsTime, so a stream with the load balancing on
// can't be compared; these rows feed NeedDynamicAdjust,
// CalcSliceComplexRatio, DynamicAdjustSlicing and AdjustBaseLayer made-up
// times instead. The layer set up and the random draws mirror the C file.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 27 Nov 2026  Claude          Created (P5.7).
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
using static SIPSorcery.H264.RC_MODES;

namespace SIPSorcery.H264.UnitTest;

public unsafe class slice_multi_threading_unittest
{
    private const string GroupName = "slice_mt";

    // kernels_slice_mt.cpp kSizes, in macroblocks.
    private static readonly (int W, int H)[] Sizes = { (11, 9), (20, 15), (40, 30), (80, 45), (1, 4), (2047, 2) };

    public static IEnumerable<object[]> Rows => KernelFixtures.Group(GroupName);

    [Theory]
    [MemberData(nameof(Rows))]
    public void Kernel_MatchesCBuild(string kernel, string kase, string sha1)
    {
        var row = new KernelFixture(GroupName, kernel, kase, sha1);
        string actual = kernel switch
        {
            nameof(NeedDynamicAdjust) => Need(row),
            nameof(CalcSliceComplexRatio) => Ratio(row),
            nameof(DynamicAdjustSlicing) => Adjust(row),
            nameof(AdjustBaseLayer) => Frames(row),
            _ => throw new ArgumentException($"No C# kernel {kernel}."),
        };
        Assert.Equal(sha1, actual);
    }

    [Fact]
    public void Group_HasEveryKernel()
    {
        var kernels = KernelFixtures.Group(GroupName).Select(r => (string)r[0]).Distinct().OrderBy(k => k, StringComparer.Ordinal);
        Assert.Equal(new[] { nameof(AdjustBaseLayer), nameof(CalcSliceComplexRatio), nameof(DynamicAdjustSlicing), nameof(NeedDynamicAdjust) }, kernels);
    }

    /// <summary>The C file's Layer: a layer, its slices and the context the functions read.</summary>
    private sealed class Layer : IDisposable
    {
        public readonly int MbWidth, MbHeight, SliceNum;
        public readonly SSlice* Slices;
        public readonly SSlice** List;
        public readonly int* First, Count;
        public readonly ushort* Map;
        public readonly SMB* Mbs;
        public readonly SDqLayer* Dq;
        private readonly SDqLayer** _dqList;
        private readonly SWelsSvcCodingParam* _param;
        private readonly SWelsSvcRc* _rc;
        public readonly sWelsEncCtx* Ctx;

        public Layer(int w, int h, int n, RC_MODES iRCMode, int iGom)
        {
            MbWidth = w;
            MbHeight = h;
            SliceNum = n;
            Slices = Zeroed<SSlice>(n);
            List = (SSlice**)NativeMemory.AllocZeroed((nuint)(n * sizeof(SSlice*)));
            First = Zeroed<int>(n);
            Count = Zeroed<int>(n);
            Map = Zeroed<ushort>(w * h);
            Mbs = Zeroed<SMB>(w * h);
            for (int i = 0; i < n; i++)
            {
                List[i] = &Slices[i];
            }
            for (int i = 0; i < w * h; i++)
            {
                Mbs[i].iMbXY = i;
                Mbs[i].iMbX = (short)(i % w);
                Mbs[i].iMbY = (short)(i / w);
            }
            Dq = Zeroed<SDqLayer>(1);
            Dq->sSliceEncCtx.iMbWidth = (short)w;
            Dq->sSliceEncCtx.iMbHeight = (short)h;
            Dq->sSliceEncCtx.iMbNumInFrame = w * h;
            Dq->sSliceEncCtx.iSliceNumInFrame = n;
            Dq->sSliceEncCtx.pOverallMbMap = Map;
            Dq->ppSliceInLayer = List;
            Dq->pFirstMbIdxOfSlice = First;
            Dq->pCountMbNumInSlice = Count;
            Dq->sMbDataP = Mbs;
            _dqList = (SDqLayer**)NativeMemory.AllocZeroed((nuint)sizeof(SDqLayer*));
            _dqList[0] = Dq;
            _param = Zeroed<SWelsSvcCodingParam>(1);
            _param->iRCMode = iRCMode;
            _rc = Zeroed<SWelsSvcRc>(1);
            _rc->iNumberMbGom = iGom;
            Ctx = Zeroed<sWelsEncCtx>(1);
            Ctx->pSvcParam = _param;
            Ctx->pWelsSvcRc = _rc;
            Ctx->ppDqLayerList = _dqList;
            // sLogCtx.pfLog stays null, so the warnings go nowhere, as the C file's QuietLog.
        }

        private static T* Zeroed<T>(int count) where T : unmanaged =>
            (T*)NativeMemory.AllocZeroed((nuint)(Math.Max(count, 1) * sizeof(T)));

        /// <summary>Each slice's run, its macroblocks marked in the map, and the slice's count.</summary>
        public void Partition(int[] runs)
        {
            int iFirst = 0;
            for (int i = 0; i < SliceNum; i++)
            {
                First[i] = iFirst;
                Count[i] = runs[i];
                for (int k = 0; k < runs[i]; k++)
                {
                    Map[iFirst + k] = (ushort)i;
                }
                Slices[i].iCountMbNumInSlice = runs[i];
                iFirst += runs[i];
            }
        }

        public void HashPartition(FixtureHasher h)
        {
            for (int i = 0; i < SliceNum; i++)
            {
                h.AddI32(First[i]);
                h.AddI32(Count[i]);
            }
            for (int i = 0; i < MbWidth * MbHeight; i++)
            {
                h.AddI32(Map[i]);
            }
            h.AddI32(Dq->bNeedAdjustingSlicing ? 1 : 0);
        }

        public void Dispose()
        {
            NativeMemory.Free(Ctx);
            NativeMemory.Free(_rc);
            NativeMemory.Free(_param);
            NativeMemory.Free(_dqList);
            NativeMemory.Free(Dq);
            NativeMemory.Free(Mbs);
            NativeMemory.Free(Map);
            NativeMemory.Free(Count);
            NativeMemory.Free(First);
            NativeMemory.Free(List);
            NativeMemory.Free(Slices);
        }
    }

    private static int[] EvenRuns(int kiMbs, int n)
    {
        var runs = Enumerable.Repeat(kiMbs / n, n).ToArray();
        runs[n - 1] = kiMbs - (n - 1) * (kiMbs / n);
        return runs;
    }

    private static int[] RandomRuns(ref FixtureRng r, int kiMbs, int n)
    {
        var runs = Enumerable.Repeat(1, n).ToArray();
        for (int k = n; k < kiMbs; k++)
        {
            runs[r.Next() % (uint)n]++;
        }
        return runs;
    }

    private static string Need(KernelFixture row)
    {
        int n = row.Param("n"), kind = row.Param("kind"), seed = row.Param("seed");
        var r = new FixtureRng((uint)(n * 1000 + kind * 10 + seed));
        using var l = new Layer(11, 9, n, RC_OFF_MODE, 0);
        if (kind == 1)
        {
            uint t = 1 + r.Next() % 50000;
            for (int i = 0; i < n; i++)
            {
                l.Slices[i].uiSliceConsumeTime = t;
            }
        }
        else if (kind == 2)
        {
            for (int i = 0; i < n; i++)
            {
                l.Slices[i].uiSliceConsumeTime = r.Next() % 50001;
            }
        }
        else if (kind == 3)
        {
            uint t = 1 + r.Next() % 50000;
            for (int i = 0; i < n; i++)
            {
                l.Slices[i].uiSliceConsumeTime = t;
            }
            int iSlow = (int)(r.Next() % (uint)n);
            l.Slices[iSlow].uiSliceConsumeTime = t * (2 + r.Next() % 4);
        }
        else if (kind == 4)
        {
            int iJitter = (int)(r.Next() % 1601);
            for (int i = 0; i < n; i++)
            {
                l.Slices[i].uiSliceConsumeTime = (uint)(10000 - iJitter + (int)(r.Next() % (uint)(2 * iJitter + 1)));
            }
        }
        else if (kind >= 5)
        {
            for (int i = 0; i < n; i++)
            {
                l.Slices[i].uiSliceConsumeTime = 1 + r.Next() % 50000;
            }
            if (kind == 6)
            {
                l.List[r.Next() % (uint)n] = null;
            }
        }
        int iRet = NeedDynamicAdjust(kind == 5 ? null : l.List, n);
        using var h = new FixtureHasher();
        h.AddI32(iRet);
        return h.Hex();
    }

    private static string Ratio(KernelFixture row)
    {
        int n = row.Param("n"), iMbs = row.Param("mbs"), kind = row.Param("kind"), seed = row.Param("seed");
        // The C loop's m: the index of the size in {n, 99, 396, 3600}.
        int m = iMbs == 99 ? 1 : iMbs == 396 ? 2 : iMbs == 3600 ? 3 : 0;
        var r = new FixtureRng((uint)(n * 1000 + m * 100 + kind * 10 + seed));
        using var l = new Layer(iMbs, 1, n, RC_OFF_MODE, 0);
        l.Partition(RandomRuns(ref r, iMbs, n));
        uint kuiEqual = 1 + r.Next() % 50000;
        for (int i = 0; i < n; i++)
        {
            uint t;
            if (kind == 0)
            {
                t = 1 + r.Next() % 50000;
            }
            else if (kind == 1)
            {
                t = (r.Next() % 4 == 0) ? 0 : 1 + r.Next() % 50000;
            }
            else if (kind == 2)
            {
                t = 0x100000u + r.Next() % 0x7FF00000u;
            }
            else
            {
                t = kuiEqual;
            }
            l.Slices[i].uiSliceConsumeTime = t;
        }
        CalcSliceComplexRatio(l.Dq);
        using var h = new FixtureHasher();
        for (int i = 0; i < n; i++)
        {
            h.AddI32(l.Slices[i].iSliceComplexRatio);
        }
        return h.Hex();
    }

    private static string Adjust(KernelFixture row)
    {
        int w = row.Param("w"), hgt = row.Param("h"), n = row.Param("n"), rc = row.Param("rc");
        int kind = row.Param("kind"), seed = row.Param("seed");
        // rc 0: off; 1 to 3: on with GOMs of that many rows; 4: on with no GOM.
        RC_MODES iRCMode = rc == 0 ? RC_OFF_MODE : RC_BITRATE_MODE;
        int iGom = rc == 0 ? 0 : rc == 4 ? 0 : rc * w;
        int iMbs = w * hgt;
        var r = new FixtureRng((uint)(w * 100000 + hgt * 1000 + n * 100 + rc * 10 + kind * 2 + seed));
        using var l = new Layer(w, hgt, n, iRCMode, iGom);
        // kind 0, 1: even partition; 2, 3: random. Even kinds: random ratios; odd: one slice takes them all.
        l.Partition(kind < 2 ? EvenRuns(iMbs, n) : RandomRuns(ref r, iMbs, n));
        int iHot = (int)(r.Next() % (uint)n);
        for (int i = 0; i < n; i++)
        {
            l.Slices[i].iSliceComplexRatio = (kind & 1) != 0 ? (i == iHot ? 100 : 0) : (int)(r.Next() % 101);
        }
        DynamicAdjustSlicing(l.Ctx, l.Dq, 0);
        using var h = new FixtureHasher();
        l.HashPartition(h);
        return h.Hex();
    }

    private static string Frames(KernelFixture row)
    {
        int w = row.Param("w"), hgt = row.Param("h"), n = row.Param("n"), rc = row.Param("rc"), seed = row.Param("seed");
        int iMbs = w * hgt;
        RC_MODES iRCMode = rc == 0 ? RC_OFF_MODE : RC_BITRATE_MODE;
        var r = new FixtureRng((uint)(w * 100000 + hgt * 1000 + n * 100 + rc * 10 + seed));
        using var l = new Layer(w, hgt, n, iRCMode, rc == 0 ? 0 : w);
        l.Partition(EvenRuns(iMbs, n));
        var cost = new uint[iMbs];
        for (int i = 0; i < iMbs; i++)
        {
            cost[i] = 1 + r.Next() % 20;
            if (i / w < (hgt + 2) / 3)
            {
                cost[i] *= 8;
            }
        }
        using var h = new FixtureHasher();
        for (int f = 0; f < 6; f++)
        {
            h.AddI32(AdjustBaseLayer(l.Ctx));
            h.AddI32(l.Ctx->pCurDqLayer == l.Dq ? 1 : 0);
            l.HashPartition(h);
            if (l.Dq->bNeedAdjustingSlicing)
            {
                for (int i = 0; i < n; i++)
                {
                    UpdateMbListNeighborParallel(l.Dq, l.Mbs, i);
                }
                for (int i = 0; i < iMbs; i++)
                {
                    h.AddI32(l.Mbs[i].uiSliceIdc);
                    h.AddI32(l.Mbs[i].uiNeighborAvail);
                }
            }
            for (int i = 0; i < n; i++)
            {
                l.Slices[i].iCountMbNumInSlice = l.Count[i];
                uint t = r.Next() % 50;
                for (int k = 0; k < l.Count[i]; k++)
                {
                    t += cost[l.First[i] + k];
                }
                l.Slices[i].uiSliceConsumeTime = t;
            }
            CalcSliceComplexRatio(l.Dq);
            for (int i = 0; i < n; i++)
            {
                h.AddI32(l.Slices[i].iSliceComplexRatio);
            }
        }
        return h.Hex();
    }
}
