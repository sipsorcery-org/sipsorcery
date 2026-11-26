//-----------------------------------------------------------------------------
// Filename: mv_pred_unittest.cs
//
// Description: Checks the encoder's mv_pred against the C build ("mv_pred"
// rows in reference/kernels.tsv, refharness src/pslice_encoder.cpp
// KernelsMvPred): PredMv at every partition place it is called for, the
// 16x8, 8x16 and skip predictors, and the motion info updates, into the
// cache and the macroblock or into the cache alone, at every place of their
// shape. Each row runs over several random caches.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 26 Nov 2026  Claude          Created (E5).
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

public unsafe class mv_pred_unittest
{
    private const string GroupName = "mv_pred";

    /// <summary>fixture kUpdates: the motion info updates, in kind order.</summary>
    private static readonly string[] Updates =
    {
        nameof(UpdateP16x16MotionInfo), nameof(UpdateP16x8MotionInfo), nameof(update_P8x16_motion_info),
        nameof(UpdateP8x8MotionInfo), nameof(UpdateP4x4MotionInfo), nameof(UpdateP8x4MotionInfo),
        nameof(UpdateP4x8MotionInfo), nameof(UpdateP16x8Motion2Cache), nameof(UpdateP8x16Motion2Cache),
        nameof(UpdateP8x8Motion2Cache), nameof(UpdateP4x4Motion2Cache), nameof(UpdateP8x4Motion2Cache),
        nameof(UpdateP4x8Motion2Cache),
    };

    public static IEnumerable<object[]> Rows => KernelFixtures.Group(GroupName);

    [Theory]
    [MemberData(nameof(Rows))]
    public void Kernel_MatchesCBuild(string kernel, string kase, string sha1)
    {
        var row = new KernelFixture(GroupName, kernel, kase, sha1);
        string actual = kernel switch
        {
            nameof(PredMv) => Pred(row),
            nameof(PredInter16x8Mv) or nameof(PredInter8x16Mv) or nameof(PredSkipMv) => PredPartition(row),
            _ when Array.IndexOf(Updates, kernel) >= 0 => Update(row, Array.IndexOf(Updates, kernel)),
            _ => throw new ArgumentException($"No C# kernel {kernel}."),
        };
        Assert.Equal(sha1, actual);
    }

    // PredMv on 8 random caches, for both references.
    private static string Pred(KernelFixture row)
    {
        sbyte iPartW = (sbyte)row.Param("w");
        sbyte iPartIdx = (sbyte)row.Param("idx");
        using var h = new FixtureHasher();
        for (int seed = 0; seed < 8; seed++)
        {
            var g = new FixtureRng((uint)seed);
            SMVComponentUnit sMvComp;
            PsliceFixtures.FillMvCache(ref g, &sMvComp);
            for (int iRef = 0; iRef < 2; iRef++)
            {
                SMVUnitXY sMvp;
                g.Fill(new Span<byte>(&sMvp, sizeof(SMVUnitXY)));
                PredMv(&sMvComp, iPartIdx, iPartW, iRef, &sMvp);
                h.Add(new ReadOnlySpan<byte>(&sMvp, sizeof(SMVUnitXY)));
            }
        }
        return h.Hex();
    }

    // PredInter16x8Mv, PredInter8x16Mv or PredSkipMv on 16 random caches.
    private static string PredPartition(KernelFixture row)
    {
        int iPartIdx = row.Param("idx");
        using var h = new FixtureHasher();
        for (int seed = 0; seed < 16; seed++)
        {
            var g = new FixtureRng((uint)seed);
            SMbCache cache = default;
            PsliceFixtures.FillMvCache(ref g, &cache.sMvComponents);
            for (sbyte iRef = 0; iRef < 2; iRef++)
            {
                SMVUnitXY sMvp;
                g.Fill(new Span<byte>(&sMvp, sizeof(SMVUnitXY)));
                if (row.Kernel == nameof(PredInter16x8Mv))
                {
                    PredInter16x8Mv(&cache, iPartIdx, iRef, &sMvp);
                }
                else if (row.Kernel == nameof(PredInter8x16Mv))
                {
                    PredInter8x16Mv(&cache, iPartIdx, iRef, &sMvp);
                }
                else if (iRef == 0)
                {
                    PredSkipMv(&cache, &sMvp);
                }
                h.Add(new ReadOnlySpan<byte>(&sMvp, sizeof(SMVUnitXY)));
            }
        }
        return h.Hex();
    }

    // One motion info update on 4 random caches and macroblocks.
    private static string Update(KernelFixture row, int kind)
    {
        int iPartIdx = row.Param("idx");
        using var h = new FixtureHasher();
        for (int seed = 0; seed < 4; seed++)
        {
            var g = new FixtureRng((uint)seed);
            SMbCache cache = default;
            PsliceFixtures.FillMvCache(ref g, &cache.sMvComponents);
            SMVUnitXY* sMvs = stackalloc SMVUnitXY[MB_BLOCK4x4_NUM];
            sbyte* iRefIdx = stackalloc sbyte[MB_BLOCK8x8_NUM];
            g.Fill(new Span<byte>(sMvs, MB_BLOCK4x4_NUM * sizeof(SMVUnitXY)));
            g.Fill(new Span<byte>(iRefIdx, MB_BLOCK8x8_NUM));
            SMB sMb = default;
            sMb.sMv = sMvs;
            sMb.pRefIndex = iRefIdx;
            SMVUnitXY sMv;
            g.Fill(new Span<byte>(&sMv, sizeof(SMVUnitXY)));
            sbyte kiRef = PsliceFixtures.CacheRefs[2 + g.Next() % 4];
            switch (kind)
            {
                case 0: UpdateP16x16MotionInfo(&cache, &sMb, kiRef, &sMv); break;
                case 1: UpdateP16x8MotionInfo(&cache, &sMb, iPartIdx, kiRef, &sMv); break;
                case 2: update_P8x16_motion_info(&cache, &sMb, iPartIdx, kiRef, &sMv); break;
                case 3: UpdateP8x8MotionInfo(&cache, &sMb, iPartIdx, kiRef, &sMv); break;
                case 4: UpdateP4x4MotionInfo(&cache, &sMb, iPartIdx, kiRef, &sMv); break;
                case 5: UpdateP8x4MotionInfo(&cache, &sMb, iPartIdx, kiRef, &sMv); break;
                case 6: UpdateP4x8MotionInfo(&cache, &sMb, iPartIdx, kiRef, &sMv); break;
                case 7: UpdateP16x8Motion2Cache(&cache, iPartIdx, kiRef, &sMv); break;
                case 8: UpdateP8x16Motion2Cache(&cache, iPartIdx, kiRef, &sMv); break;
                case 9: UpdateP8x8Motion2Cache(&cache, iPartIdx, kiRef, &sMv); break;
                case 10: UpdateP4x4Motion2Cache(&cache, iPartIdx, kiRef, &sMv); break;
                case 11: UpdateP8x4Motion2Cache(&cache, iPartIdx, kiRef, &sMv); break;
                default: UpdateP4x8Motion2Cache(&cache, iPartIdx, kiRef, &sMv); break;
            }
            PsliceFixtures.HashMvCache(h, &cache.sMvComponents);
            h.Add(new ReadOnlySpan<byte>(sMvs, MB_BLOCK4x4_NUM * sizeof(SMVUnitXY)));
            h.Add(new ReadOnlySpan<byte>(iRefIdx, MB_BLOCK8x8_NUM));
        }
        return h.Hex();
    }

    [Fact]
    public void Group_HasEveryKernel()
    {
        var kernels = KernelFixtures.Group(GroupName).Select(r => (string)r[0]).Distinct().OrderBy(k => k, StringComparer.Ordinal);
        var expected = Updates.Concat(new[] { nameof(PredMv), nameof(PredInter16x8Mv), nameof(PredInter8x16Mv), nameof(PredSkipMv) })
                              .OrderBy(k => k, StringComparer.Ordinal);
        Assert.Equal(expected, kernels);
    }
}
