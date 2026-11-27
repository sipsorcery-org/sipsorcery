//-----------------------------------------------------------------------------
// Filename: encoder_ext_unittest.cs
//
// Description: Checks the stride tables of encoder_ext's AllocStrideTables
// against the C build ("encoder_ext" rows in reference/kernels.tsv, refharness
// src/islice_encoder.cpp KernelsEncoderExt).
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

public unsafe class encoder_ext_unittest
{
    private const string GroupName = "encoder_ext";

    // fixture KernelsEncoderExt kLayers: growing spatial layer sets.
    private static readonly (int W, int H)[][] Layers =
    {
        new[] { (176, 144) },
        new[] { (88, 72), (176, 144) },
        new[] { (40, 24), (80, 48), (176, 144) },
        new[] { (16, 16), (40, 24), (88, 72), (320, 240) },
    };

    public static IEnumerable<object[]> Rows => KernelFixtures.Group(GroupName);

    [Theory]
    [MemberData(nameof(Rows))]
    public void Kernel_MatchesCBuild(string kernel, string kase, string sha1)
    {
        var row = new KernelFixture(GroupName, kernel, kase, sha1);
        string actual = kernel switch
        {
            nameof(AllocStrideTables) => StrideTables(row),
            _ => throw new ArgumentException($"No C# kernel {kernel}."),
        };
        Assert.Equal(sha1, actual);
    }

    // Every decoder offset table (or that it is null), the encoder tables
    // (left for InitDqLayers to fill) and the macroblock index maps.
    private static string StrideTables(KernelFixture row)
    {
        int layers = row.Param("layers");
        int temporal = row.Param("temporal");
        (int W, int H)[] sizes = Layers[layers - 1];
        CMemoryAlign* pMa = WELS_NEW_CMemoryAlign(16);
        SWelsSvcCodingParam* pParam = IsliceFixtures.Alloc<SWelsSvcCodingParam>();
        sWelsEncCtx* pCtx = IsliceFixtures.Alloc<sWelsEncCtx>();
        try
        {
            pParam->iSpatialLayerNum = layers;
            pParam->iTemporalLayerNum = temporal;
            for (int d = 0; d < layers; d++)
            {
                pParam->sSpatialLayers[d].iVideoWidth = sizes[d].W;
                pParam->sSpatialLayers[d].iVideoHeight = sizes[d].H;
            }
            pCtx->pMemAlign = pMa;
            pCtx->pSvcParam = pParam;

            using var h = new FixtureHasher();
            h.AddI32(AllocStrideTables(&pCtx, layers));
            SStrideTables* t = pCtx->pStrideTab;
            for (int d = 0; d < MAX_DEPENDENCY_LAYER; d++)
            {
                for (int tid = 0; tid < 2; tid++)
                {
                    int* pDec = t->pStrideDecBlockOffset[d][tid];
                    h.AddI32(pDec != null ? 1 : 0);
                    if (pDec != null)
                    {
                        h.Add(new ReadOnlySpan<byte>(pDec, 24 * sizeof(int)));
                    }
                }
                int* pEnc = t->pStrideEncBlockOffset[d];
                short* pMbX = t->pMbIndexX[d];
                short* pMbY = t->pMbIndexY[d];
                h.AddI32(pEnc != null ? 1 : 0);
                h.AddI32(pMbX != null ? 1 : 0);
                h.AddI32(pMbY != null ? 1 : 0);
                if (d < layers)
                {
                    int kiCount = ((sizes[d].W + 15) >> 4) * ((sizes[d].H + 15) >> 4);
                    h.Add(new ReadOnlySpan<byte>(pEnc, 24 * sizeof(int)));
                    h.Add(new ReadOnlySpan<byte>(pMbX, kiCount * sizeof(short)));
                    h.Add(new ReadOnlySpan<byte>(pMbY, kiCount * sizeof(short)));
                }
            }
            pMa->WelsFree((int*)t->pStrideDecBlockOffset[0][1], "pBase");
            pMa->WelsFree(t, "SStrideTables");
            h.AddI32((int)pMa->WelsGetMemoryUsage());
            return h.Hex();
        }
        finally
        {
            NativeMemory.Free(pCtx);
            NativeMemory.Free(pParam);
            WELS_DELETE_CMemoryAlign(ref pMa);
        }
    }

    [Fact]
    public void Group_HasEveryKernel()
    {
        var kernels = KernelFixtures.Group(GroupName).Select(r => (string)r[0]).Distinct().OrderBy(k => k, StringComparer.Ordinal);
        Assert.Equal(new[] { nameof(AllocStrideTables) }, kernels);
    }
}
