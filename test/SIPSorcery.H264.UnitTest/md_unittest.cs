//-----------------------------------------------------------------------------
// Filename: md_unittest.cs
//
// Description: Checks the intra halves of md against the C build ("md" rows
// in reference/kernels.tsv, refharness src/islice_encoder.cpp
// KernelsMdIntra): the neighbour cache fill, the two VAA analysis kernels, and
// rows that put MdInterAnalysisVaaInfo_c on ties and MdIntraAnalysisVaaInfo on
// its variance threshold.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 26 Nov 2026  Claude          Created (E4).
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

namespace SIPSorcery.H264.UnitTest;

public unsafe class md_unittest
{
    private const string GroupName = "md";

    private static readonly uint[] NeighbourTypes = { MB_TYPE_INTRA4x4, MB_TYPE_INTRA16x16, MB_TYPE_16x16 };

    public static IEnumerable<object[]> Rows => KernelFixtures.Group(GroupName);

    [Theory]
    [MemberData(nameof(Rows))]
    public void Kernel_MatchesCBuild(string kernel, string kase, string sha1)
    {
        var row = new KernelFixture(GroupName, kernel, kase, sha1);
        string actual = kernel switch
        {
            nameof(AnalysisVaaInfoIntra_c) => VaaIntra(row),
            nameof(MdInterAnalysisVaaInfo_c) => VaaInter(row),
            nameof(FillNeighborCacheIntra) => NeighbourCache(row),
            nameof(MdIntraAnalysisVaaInfo) => VaaIntraThreshold(row),
            _ => throw new ArgumentException($"No C# kernel {kernel}."),
        };
        Assert.Equal(sha1, actual);
    }

    // A 16x16 block of each content kind in a random 48-stride buffer.
    private static string VaaIntra(KernelFixture row)
    {
        var g = new FixtureRng((uint)row.Param("seed"));
        using var buf = new NativeBuffer(48 * 20);
        g.Fill(buf.Span);
        IsliceFixtures.FillPlane(ref g, buf.Ptr + 2 * 48 + 8, 48, 16, 16, row.Param("content"));
        using var h = new FixtureHasher();
        h.AddI32(AnalysisVaaInfoIntra_c(buf.Ptr + 2 * 48 + 8, 48));
        return h.Hex();
    }

    // Four 8x8 SADs below range, or for the tie rows blocks k and k + 3 on the
    // average and the other two d either side of it.
    private static string VaaInter(KernelFixture row)
    {
        var g = new FixtureRng((uint)row.Param("seed"));
        int* iSad = stackalloc int[4];
        if (row.Case.StartsWith("tie=", StringComparison.Ordinal))
        {
            int k = row.Param("tie");
            int avg = 4160 + (int)(g.Next() % 4096);
            int d = 64 * (4 + (int)(g.Next() % 60));
            iSad[k] = avg;
            iSad[(k + 1) % 4] = avg + d;
            iSad[(k + 2) % 4] = avg - d;
            iSad[(k + 3) % 4] = avg;
        }
        else
        {
            int range = row.Param("range");
            for (int i = 0; i < 4; i++)
            {
                iSad[i] = (int)(g.Next() % (uint)range);
            }
        }
        using var h = new FixtureHasher();
        h.AddI32(MdInterAnalysisVaaInfo_c(iSad));
        return h.Hex();
    }

    // Offsets on three flat 4x4 blocks that make the 16x16 variance 149, 150
    // and 151 whatever the base.
    private static readonly int[,] VarianceOffsets = { { 1, 7, 11 }, { 0, 7, 11 }, { 0, 5, 12 } };

    // Flat 4x4 blocks of base, the three from place on raised by the offsets,
    // through AnalysisVaaInfoIntra_c and MdIntraAnalysisVaaInfo's threshold.
    private static string VaaIntraThreshold(KernelFixture row)
    {
        int v = row.Param("variance") - 149;
        int @base = row.Param("base");
        int place = row.Param("place");
        using var blk = new NativeBuffer(16 * 32);
        for (int k = 0; k < 16; k++)
        {
            int r = (k - place + 16) % 16;
            int e = r < 3 ? VarianceOffsets[v, r] : 0;
            for (int j = 0; j < 4; j++)
            {
                for (int i = 0; i < 4; i++)
                {
                    blk.Ptr[((k >> 2) * 4 + j) * 32 + (k & 3) * 4 + i] = (byte)(@base + e);
                }
            }
        }
        SWelsFuncPtrList* pFuncs = IsliceFixtures.Alloc<SWelsFuncPtrList>();
        SDqLayer* pDq = IsliceFixtures.Alloc<SDqLayer>();
        sWelsEncCtx* pCtx = IsliceFixtures.Alloc<sWelsEncCtx>();
        try
        {
            InitIntraAnalysisVaaInfo(pFuncs, 0);
            pDq->iEncStride[0] = 32;
            pCtx->pFuncList = pFuncs;
            pCtx->pCurDqLayer = pDq;
            using var h = new FixtureHasher();
            h.AddI32(AnalysisVaaInfoIntra_c(blk.Ptr, 32));
            h.AddI32(MdIntraAnalysisVaaInfo(pCtx, blk.Ptr) ? 1 : 0);
            return h.Hex();
        }
        finally
        {
            NativeMemory.Free(pCtx);
            NativeMemory.Free(pDq);
            NativeMemory.Free(pFuncs);
        }
    }

    // The middle macroblock of a frame's second row, with every neighbour set;
    // neighbours are intra 4x4, intra 16x16 or inter at random, and the cache
    // starts random.
    private static string NeighbourCache(KernelFixture row)
    {
        int iMbWidth = row.Param("width");
        var g = new FixtureRng((uint)row.Param("seed"));
        int kiCount = 2 * iMbWidth;
        SMB* mbs = IsliceFixtures.AllocArray<SMB>(kiCount);
        sbyte* nzc = IsliceFixtures.AllocArray<sbyte>(kiCount * MB_LUMA_CHROMA_BLOCK4x4_NUM);
        sbyte* i4 = IsliceFixtures.AllocArray<sbyte>(kiCount * INTRA_4x4_MODE_NUM);
        SMbCache* cache = IsliceFixtures.Alloc<SMbCache>();
        try
        {
            g.Fill(new Span<byte>(nzc, kiCount * MB_LUMA_CHROMA_BLOCK4x4_NUM));
            g.Fill(new Span<byte>(i4, kiCount * INTRA_4x4_MODE_NUM));
            for (int i = 0; i < kiCount; i++)
            {
                mbs[i].uiMbType = NeighbourTypes[g.Next() % 3];
                mbs[i].pNonZeroCount = &nzc[i * MB_LUMA_CHROMA_BLOCK4x4_NUM];
                mbs[i].pIntra4x4PredMode = &i4[i * INTRA_4x4_MODE_NUM];
            }
            SMB* pCur = &mbs[iMbWidth + iMbWidth / 2];
            pCur->uiNeighborAvail = (byte)row.Param("avail");
            g.Fill(new Span<byte>(cache->iNonZeroCoeffCount, 48));
            g.Fill(new Span<byte>(cache->iIntraPredMode, 48));
            FillNeighborCacheIntra(cache, pCur, iMbWidth);

            using var h = new FixtureHasher();
            h.Add(new ReadOnlySpan<byte>(cache->iNonZeroCoeffCount, 48));
            h.Add(new ReadOnlySpan<byte>(cache->iIntraPredMode, 48));
            h.AddI32(cache->uiNeighborIntra);
            return h.Hex();
        }
        finally
        {
            NativeMemory.Free(cache);
            NativeMemory.Free(i4);
            NativeMemory.Free(nzc);
            NativeMemory.Free(mbs);
        }
    }

    [Fact]
    public void Group_HasEveryKernel()
    {
        var kernels = KernelFixtures.Group(GroupName).Select(r => (string)r[0]).Distinct().OrderBy(k => k, StringComparer.Ordinal);
        Assert.Equal(
            new[] { nameof(AnalysisVaaInfoIntra_c), nameof(FillNeighborCacheIntra), nameof(MdInterAnalysisVaaInfo_c),
                    nameof(MdIntraAnalysisVaaInfo) },
            kernels);
    }
}
