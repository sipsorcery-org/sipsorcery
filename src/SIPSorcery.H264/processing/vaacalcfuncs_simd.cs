//-----------------------------------------------------------------------------
// Filename: vaacalcfuncs_simd.cs
//
// Description: The SIMD VAA statistics kernels (P5.6), in place of upstream's
// vaa_sse2.asm and vaa_neon.S versions. Not a port: those are assembly. Each
// gives the results of its C kernel (vaacalcfuncs.cs), for any samples, and
// visits the same macroblocks in the same order: whole macroblocks only, the
// rows stepped as the C kernels step them.
//
// A row of a macroblock is one vector of 16 samples, and its two 8-sample halves
// belong to the 8x8 blocks side by side, so each statistic is gathered for two
// blocks at once, 8 rows at a time:
// - The SAD, and the sums of the samples, are the SAD step with each 64-bit
//   half summed on its own (Simd.SadAdd, Simd.SadHalves). A block's sum of
//   differences is its current sum less its reference sum.
// - The squares of the samples and of the differences go to 32-bit lanes
//   (Simd.SquareAdd); a macroblock's are at most 256 * 65025.
// - A block's largest difference is the byte maximum down its rows, then
//   across its half.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 27 Nov 2026  Claude          Created (P5.6).
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using static SIPSorcery.H264.Simd;

namespace SIPSorcery.H264;

internal static unsafe partial class WelsVP
{
    public static void VAACalcSad_simd(uint8_t* pCurData, uint8_t* pRefData, int32_t iPicWidth, int32_t iPicHeight,
        int32_t iPicStride, int32_t* pFrameSad, int32_t* pSad8x8) =>
        *pFrameSad = VaaFrame(pCurData, pRefData, iPicWidth, iPicHeight, iPicStride, false, false, false,
                              pSad8x8, null, null, null, null, null);

    public static void VAACalcSadVar_simd(uint8_t* pCurData, uint8_t* pRefData, int32_t iPicWidth, int32_t iPicHeight,
        int32_t iPicStride, int32_t* pFrameSad, int32_t* pSad8x8, int32_t* pSum16x16, int32_t* psqsum16x16) =>
        *pFrameSad = VaaFrame(pCurData, pRefData, iPicWidth, iPicHeight, iPicStride, true, false, false,
                              pSad8x8, pSum16x16, psqsum16x16, null, null, null);

    public static void VAACalcSadSsd_simd(uint8_t* pCurData, uint8_t* pRefData, int32_t iPicWidth, int32_t iPicHeight,
        int32_t iPicStride, int32_t* pFrameSad, int32_t* pSad8x8, int32_t* pSum16x16, int32_t* psqsum16x16,
        int32_t* psqdiff16x16) =>
        *pFrameSad = VaaFrame(pCurData, pRefData, iPicWidth, iPicHeight, iPicStride, true, true, false,
                              pSad8x8, pSum16x16, psqsum16x16, psqdiff16x16, null, null);

    public static void VAACalcSadBgd_simd(uint8_t* pCurData, uint8_t* pRefData, int32_t iPicWidth, int32_t iPicHeight,
        int32_t iPicStride, int32_t* pFrameSad, int32_t* pSad8x8, int32_t* pSd8x8, uint8_t* pMad8x8) =>
        *pFrameSad = VaaFrame(pCurData, pRefData, iPicWidth, iPicHeight, iPicStride, false, false, true,
                              pSad8x8, null, null, null, pSd8x8, pMad8x8);

    public static void VAACalcSadSsdBgd_simd(uint8_t* pCurData, uint8_t* pRefData, int32_t iPicWidth, int32_t iPicHeight,
        int32_t iPicStride, int32_t* pFrameSad, int32_t* pSad8x8, int32_t* pSum16x16, int32_t* psqsum16x16,
        int32_t* psqdiff16x16, int32_t* pSd8x8, uint8_t* pMad8x8) =>
        *pFrameSad = VaaFrame(pCurData, pRefData, iPicWidth, iPicHeight, iPicStride, true, true, true,
                              pSad8x8, pSum16x16, psqsum16x16, psqdiff16x16, pSd8x8, pMad8x8);

    /// <summary>
    /// Every kernel's loop over the macroblocks, stepped as the C kernels step it. Returns the
    /// frame SAD. Inlined with constant flags, each kernel keeps only the statistics it asks
    /// for: <paramref name="bVar"/> the current samples' sum and sum of squares,
    /// <paramref name="bSsd"/> the sum of the squared differences, <paramref name="bBgd"/> each
    /// 8x8 block's sum of differences and largest difference.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int32_t VaaFrame(uint8_t* pCurData, uint8_t* pRefData, int32_t iPicWidth, int32_t iPicHeight,
        int32_t iPicStride, bool bVar, bool bSsd, bool bBgd, int32_t* pSad8x8, int32_t* pSum16x16,
        int32_t* pSqSum16x16, int32_t* pSqDiff16x16, int32_t* pSd8x8, uint8_t* pMad8x8)
    {
        uint8_t* tmp_ref = pRefData;
        uint8_t* tmp_cur = pCurData;
        int32_t iMbWidth = iPicWidth >> 4;
        int32_t mb_height = iPicHeight >> 4;
        int32_t mb_index = 0;
        int32_t pic_stride_x8 = iPicStride << 3;
        int32_t step = (iPicStride << 4) - iPicWidth;
        int32_t iFrameSad = 0;

        for (int32_t i = 0; i < mb_height; i++)
        {
            for (int32_t j = 0; j < iMbWidth; j++)
            {
                Vector128<ushort> sum = default;
                Vector128<int> sqsum = default, sqdiff = default;

                // The top two 8x8 blocks (k = 0), then the bottom two.
                for (int32_t k = 0; k < 2; k++)
                {
                    uint8_t* pCur = tmp_cur + k * pic_stride_x8;
                    uint8_t* pRef = tmp_ref + k * pic_stride_x8;
                    Vector128<ushort> sad = default, sumCur = default, sumRef = default;
                    Vector128<byte> mad = default;
                    for (int32_t l = 0; l < 8; l++)
                    {
                        Vector128<byte> c = Load16(pCur), r = Load16(pRef);
                        Vector128<byte> d = bSsd || bBgd ? AbsDiff(c, r) : default;
                        sad = SadAdd(sad, c, r);
                        if (bVar)
                        {
                            sum = SadAdd(sum, c, Vector128<byte>.Zero);
                            sqsum = SquareAdd(sqsum, c);
                        }
                        if (bSsd)
                        {
                            sqdiff = SquareAdd(sqdiff, d);
                        }
                        if (bBgd)
                        {
                            sumCur = SadAdd(sumCur, c, Vector128<byte>.Zero);
                            sumRef = SadAdd(sumRef, r, Vector128<byte>.Zero);
                            mad = Vector128.Max(mad, d);
                        }
                        pCur += iPicStride;
                        pRef += iPicStride;
                    }

                    int32_t b = (mb_index << 2) + 2 * k;
                    SadHalves(sad, out int32_t iSadL, out int32_t iSadR);
                    pSad8x8[b] = iSadL;
                    pSad8x8[b + 1] = iSadR;
                    iFrameSad += iSadL + iSadR;
                    if (bBgd)
                    {
                        SadHalves(sumCur, out int32_t iCurL, out int32_t iCurR);
                        SadHalves(sumRef, out int32_t iRefL, out int32_t iRefR);
                        pSd8x8[b] = iCurL - iRefL;
                        pSd8x8[b + 1] = iCurR - iRefR;
                        mad = HalfMax(mad);
                        pMad8x8[b] = mad.GetElement(0);
                        pMad8x8[b + 1] = mad.GetElement(8);
                    }
                }

                if (bVar)
                {
                    pSum16x16[mb_index] = SadTotal(sum);
                    pSqSum16x16[mb_index] = Vector128.Sum(sqsum);
                }
                if (bSsd)
                {
                    pSqDiff16x16[mb_index] = Vector128.Sum(sqdiff);
                }
                tmp_ref += 16;
                tmp_cur += 16;
                ++mb_index;
            }
            tmp_ref += step;
            tmp_cur += step;
        }
        return iFrameSad;
    }

    /// <summary>The largest byte of each 64-bit half, in bytes 0 and 8.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<byte> HalfMax(Vector128<byte> m)
    {
        m = Vector128.Max(m, Vector128.ShiftRightLogical(m.AsUInt64(), 32).AsByte());
        m = Vector128.Max(m, Vector128.ShiftRightLogical(m.AsUInt64(), 16).AsByte());
        return Vector128.Max(m, Vector128.ShiftRightLogical(m.AsUInt64(), 8).AsByte());
    }
}
