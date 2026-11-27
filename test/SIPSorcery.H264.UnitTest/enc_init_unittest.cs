//-----------------------------------------------------------------------------
// Filename: enc_init_unittest.cs
//
// Description: Checks the function tables WelsInitEncodingFuncs,
// WelsInitReconstructionFuncs, WelsInitSampleSadFunc and
// WelsInitIntraPredFuncs fill. The indexed tables are called slot by slot
// against the C build ("enc_init" rows in reference/kernels.tsv, refharness
// src/kernels_encoder.cpp KernelsEncInit); each named slot must reproduce a
// fixture row of the kernel upstream's C path assigns it.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 26 Nov 2026  Claude          Created (E2).
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

public unsafe class enc_init_unittest
{
    private const string GroupName = "enc_init";

    public static IEnumerable<object[]> Rows => KernelFixtures.Group(GroupName);

    private static SWelsFuncPtrList* NewList()
    {
        var pList = (SWelsFuncPtrList*)NativeMemory.AllocZeroed((nuint)sizeof(SWelsFuncPtrList));
        WelsInitEncodingFuncs(pList, 0);
        WelsInitReconstructionFuncs(pList, 0);
        WelsInitSampleSadFunc(pList, 0);
        WelsInitIntraPredFuncs(pList, 0);
        return pList;
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void IndexedSlot_MatchesCBuild(string kernel, string kase, string sha1)
    {
        int i = new KernelFixture(GroupName, kernel, kase, sha1).Param("idx");
        SWelsFuncPtrList* pList = NewList();
        try
        {
            string actual = kernel switch
            {
                "pfGetLumaI16x16Pred" => EncoderFixtures.PredHash(pList->pfGetLumaI16x16Pred[i], 16, 24, i),
                "pfGetLumaI4x4Pred" => EncoderFixtures.PredHash(pList->pfGetLumaI4x4Pred[i], 4, 16, i),
                "pfGetChromaPred" => EncoderFixtures.PredHash(pList->pfGetChromaPred[i], 8, 16, i),
                "pfSampleSad" or "pfSampleSatd" or "pfSample4Sad" => Cost(pList, kernel, i),
                _ => throw new ArgumentException($"No slot {kernel}."),
            };
            Assert.Equal(sha1, actual);
        }
        finally
        {
            NativeMemory.Free(pList);
        }
    }

    // Two random planes of 18 rows at stride 32, the blocks at row 1, column 4.
    private static string Cost(SWelsFuncPtrList* pList, string kernel, int i)
    {
        var r = new FixtureRng((uint)i);
        using var p1 = EncoderFixtures.Plane(ref r, 32 * 18);
        using var p2 = EncoderFixtures.Plane(ref r, 32 * 18);
        int* sad = stackalloc int[6] { 7, 7, 7, 7, 7, 7 };
        byte* a = p1.Ptr + 32 + 4, b = p2.Ptr + 32 + 4;

        using var h = new FixtureHasher();
        switch (kernel)
        {
            case "pfSampleSad":
                h.AddI32(pList->sSampleDealingFuncs.pfSampleSad[i](a, 32, b, 32));
                break;
            case "pfSampleSatd":
                h.AddI32(pList->sSampleDealingFuncs.pfSampleSatd[i](a, 32, b, 32));
                break;
            default:
                pList->sSampleDealingFuncs.pfSample4Sad[i](a, 32, b, 32, sad + 1);
                for (int k = 0; k < 6; k++)
                {
                    h.AddI32(sad[k]);
                }
                break;
        }
        return h.Hex();
    }

    [Fact]
    public void Group_HasEverySlot()
    {
        // 7 + 14 + 7 intra predictors, 3 x 7 cost functions.
        Assert.Equal(49, KernelFixtures.Group(GroupName).Count());
    }

    /// <summary>
    /// The named slots, as upstream's C path assigns them: each must reproduce
    /// its C kernel's first fixture row. (Comparing the function pointers
    /// themselves is not reliable in .NET, CS8909.)
    /// </summary>
    [Fact]
    public void NamedSlots_RunTheCKernels()
    {
        const string C = "copy_mb", E = "encode_mb_aux", D = "enc_decode_mb_aux";
        static KernelFixture First(string group, string kernel) => KernelFixtures.All.First(r => r.Group == group && r.Kernel == kernel);

        SWelsFuncPtrList* p = NewList();
        try
        {
            KernelFixture k;

            // WelsInitEncodingFuncs.
            k = First(C, nameof(WelsCopy8x8_c));
            Assert.Equal(k.Sha1, copy_mb_unittest.Copy(p->pfCopy8x8Aligned, 8, k));
            k = First(C, nameof(WelsCopy16x16_c));
            Assert.Equal(k.Sha1, copy_mb_unittest.Copy(p->pfCopy16x16Aligned, 16, k));
            Assert.Equal(k.Sha1, copy_mb_unittest.Copy(p->pfCopy16x16NotAligned, 16, k));
            k = First(C, nameof(WelsCopy16x8_c));
            Assert.Equal(k.Sha1, copy_mb_unittest.Copy(p->pfCopy16x8NotAligned, 8, k));
            k = First(C, nameof(WelsCopy8x16_c));
            Assert.Equal(k.Sha1, copy_mb_unittest.Copy(p->pfCopy8x16Aligned, 16, k));
            k = First(C, nameof(WelsCopy4x4_c));
            Assert.Equal(k.Sha1, copy_mb_unittest.Copy(p->pfCopy4x4, 4, k));
            k = First(C, nameof(WelsCopy8x4_c));
            Assert.Equal(k.Sha1, copy_mb_unittest.Copy(p->pfCopy8x4, 4, k));
            k = First(C, nameof(WelsCopy4x8_c));
            Assert.Equal(k.Sha1, copy_mb_unittest.Copy(p->pfCopy4x8, 8, k));

            k = First(E, nameof(WelsHadamardQuant2x2_c));
            Assert.Equal(k.Sha1, encode_mb_aux_unittest.Hadamard2x2(p->pfQuantizationHadamard2x2, k));
            k = First(E, nameof(WelsHadamardQuant2x2Skip_c));
            Assert.Equal(k.Sha1, encode_mb_aux_unittest.Skip(p->pfQuantizationHadamard2x2Skip, k, out _));
            k = First(E, nameof(WelsHadamardT4Dc_c));
            Assert.Equal(k.Sha1, encode_mb_aux_unittest.HadamardT4Dc(p->pfTransformHadamard4x4Dc, k));

            k = First(E, nameof(WelsDctT4_c));
            Assert.Equal(k.Sha1, encode_mb_aux_unittest.Dct(p->pfDctT4, 4, k));
            k = First(E, nameof(WelsDctFourT4_c));
            Assert.Equal(k.Sha1, encode_mb_aux_unittest.Dct(p->pfDctFourT4, 8, k));

            k = First(E, nameof(WelsScan4x4DcAc_c));
            Assert.Equal(k.Sha1, encode_mb_aux_unittest.Scan(p->pfScan4x4, k));
            k = First(E, nameof(WelsScan4x4Ac_c));
            Assert.Equal(k.Sha1, encode_mb_aux_unittest.Scan(p->pfScan4x4Ac, k));
            k = First(E, nameof(WelsCalculateSingleCtr4x4_c));
            Assert.Equal(k.Sha1, encode_mb_aux_unittest.Count(p->pfCalculateSingleCtr4x4, k, out _));
            k = First(E, nameof(WelsGetNoneZeroCount_c));
            Assert.Equal(k.Sha1, encode_mb_aux_unittest.Count(p->pfGetNoneZeroCount, k, out _));

            k = First(E, nameof(WelsQuant4x4_c));
            Assert.Equal(k.Sha1, encode_mb_aux_unittest.Quant(p->pfQuantization4x4, 16, k));
            k = First(E, nameof(WelsQuant4x4Dc_c));
            Assert.Equal(k.Sha1, encode_mb_aux_unittest.QuantDc(p->pfQuantizationDc4x4, k));
            k = First(E, nameof(WelsQuantFour4x4_c));
            Assert.Equal(k.Sha1, encode_mb_aux_unittest.Quant(p->pfQuantizationFour4x4, 64, k));
            k = First(E, nameof(WelsQuantFour4x4Max_c));
            Assert.Equal(k.Sha1, encode_mb_aux_unittest.QuantMax(p->pfQuantizationFour4x4Max, k));

            // WelsInitReconstructionFuncs.
            k = First(D, nameof(WelsDequant4x4_c));
            Assert.Equal(k.Sha1, enc_decode_mb_aux_unittest.Dequant(p->pfDequantization4x4, 16, k));
            k = First(D, nameof(WelsDequantFour4x4_c));
            Assert.Equal(k.Sha1, enc_decode_mb_aux_unittest.Dequant(p->pfDequantizationFour4x4, 64, k));
            k = First(D, nameof(WelsDequantIHadamard4x4_c));
            Assert.Equal(k.Sha1, enc_decode_mb_aux_unittest.DequantIHadamard4x4(p->pfDequantizationIHadamard4x4, k));
            k = First(D, nameof(WelsIDctT4Rec_c));
            Assert.Equal(k.Sha1, enc_decode_mb_aux_unittest.Rec(p->pfIDctT4, 4, 16, k));
            k = First(D, nameof(WelsIDctFourT4Rec_c));
            Assert.Equal(k.Sha1, enc_decode_mb_aux_unittest.Rec(p->pfIDctFourT4, 8, 64, k));
            k = First(D, nameof(WelsIDctRecI16x16Dc_c));
            Assert.Equal(k.Sha1, enc_decode_mb_aux_unittest.Rec(p->pfIDctI16x16Dc, 16, 16, k));

            // WelsInitSampleSadFunc leaves the combined kernels to the SIMD builds.
            Assert.True(p->sSampleDealingFuncs.pfIntra4x4Combined3Satd == null);
            Assert.True(p->sSampleDealingFuncs.pfIntra8x8Combined3Satd == null);
            Assert.True(p->sSampleDealingFuncs.pfIntra8x8Combined3Sad == null);
            Assert.True(p->sSampleDealingFuncs.pfIntra16x16Combined3Satd == null);
            Assert.True(p->sSampleDealingFuncs.pfIntra16x16Combined3Sad == null);
        }
        finally
        {
            NativeMemory.Free(p);
        }
    }

    /// <summary>A negative control: a slot pointed at the wrong kernel fails its row.</summary>
    [Fact]
    public void SwappedSlot_FailsItsRow()
    {
        SWelsFuncPtrList* pList = NewList();
        try
        {
            var row = KernelFixtures.Row(GroupName, "pfGetLumaI4x4Pred", $"idx={I4_PRED_DDL}");
            pList->pfGetLumaI4x4Pred[I4_PRED_DDL] = pList->pfGetLumaI4x4Pred[I4_PRED_DDR];
            Assert.NotEqual(row.Sha1, EncoderFixtures.PredHash(pList->pfGetLumaI4x4Pred[I4_PRED_DDL], 4, 16, I4_PRED_DDL));
        }
        finally
        {
            NativeMemory.Free(pList);
        }
    }
}
