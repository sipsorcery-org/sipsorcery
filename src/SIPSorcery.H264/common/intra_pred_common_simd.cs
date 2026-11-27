//-----------------------------------------------------------------------------
// Filename: intra_pred_common_simd.cs
//
// Description: The SIMD 16x16 luma and the 8x8 chroma plane intra predictors
// (P5.6), shared by the encoder (encoder/get_intra_predictor_simd.cs), which
// predicts into a packed buffer, and the decoder
// (decoder/get_intra_predictor_simd.cs), which predicts in place. Also the
// encoder's 16x16 vertical and horizontal predictors, which upstream keeps in
// common/src/intra_pred_common.cpp. Not a port: upstream's are assembly.
//
// Each helper takes the block's top left sample in the picture (pRef) and its
// stride, and writes the prediction to pDst at iDstStride. The block's
// neighbours, the row above and the column to the left, are all read before
// any of it is written, so pDst may be pRef. Each gives its C predictor's
// samples, for any neighbours:
// - The plane predictors work in 16-bit lanes. With 16x16's b and c within
//   +-717 and 8x8's within +-1355, every a + b * x + c * y + 16 lies between
//   -11456 and 19648, so the lanes are exact, and the saturating pack of the
//   value >> 5 clips as WelsClip1 does.
// - The DC predictors sum the row above with the SAD step against zero and the
//   column to the left sample by sample, as C does.
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

internal static unsafe partial class WelsCommon
{
    public static void WelsI16x16LumaPredV_simd(uint8_t* pPred, uint8_t* pRef, int32_t kiStride) =>
        PredV16x16(pPred, 16, pRef, kiStride);

    public static void WelsI16x16LumaPredH_simd(uint8_t* pPred, uint8_t* pRef, int32_t kiStride) =>
        PredH16x16(pPred, 16, pRef, kiStride);

    /// <summary>16 rows of <paramref name="v"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void PredFill16x16(uint8_t* pDst, int32_t iDstStride, Vector128<byte> v)
    {
        for (int32_t i = 0; i < 16; i++)
        {
            v.Store(pDst);
            pDst += iDstStride;
        }
    }

    /// <summary>The row above, 16 times.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void PredV16x16(uint8_t* pDst, int32_t iDstStride, uint8_t* pRef, int32_t kiStride) =>
        PredFill16x16(pDst, iDstStride, Load16(pRef - kiStride));

    /// <summary>Each row the sample to its left, 16 times.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void PredH16x16(uint8_t* pDst, int32_t iDstStride, uint8_t* pRef, int32_t kiStride)
    {
        for (int32_t i = 0; i < 16; i++)
        {
            Vector128.Create(pRef[i * kiStride - 1]).Store(pDst + i * iDstStride);
        }
    }

    /// <summary>The sum of the 16 samples of the row above.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int32_t PredSumTop16(uint8_t* pRef, int32_t kiStride) =>
        SadTotal(SadAdd(Vector128<ushort>.Zero, Load16(pRef - kiStride), Vector128<byte>.Zero));

    /// <summary>The sum of the 16 samples of the column to the left.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int32_t PredSumLeft16(uint8_t* pRef, int32_t kiStride)
    {
        int32_t iSum = 0;
        uint8_t* pLeft = pRef - 1;
        for (int32_t i = 0; i < 16; i++)
        {
            iSum += pLeft[i * kiStride];
        }
        return iSum;
    }

    /// <summary>WelsI16x16LumaPredPlane_c's prediction.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void PredPlane16x16(uint8_t* pDst, int32_t iDstStride, uint8_t* pRef, int32_t kiStride)
    {
        uint8_t* pTop = pRef - kiStride;
        uint8_t* pLeft = pRef - 1;
        int32_t iH = 0, iV = 0;
        for (int32_t i = 0; i < 8; i++)
        {
            iH += (i + 1) * (pTop[8 + i] - pTop[6 - i]);
            iV += (i + 1) * (pLeft[(8 + i) * kiStride] - pLeft[(6 - i) * kiStride]);
        }
        int32_t a = (pLeft[15 * kiStride] + pTop[15]) << 4;
        int32_t b = (5 * iH + 32) >> 6;
        int32_t c = (5 * iV + 32) >> 6;

        // Row 0: a + b * (j - 7) + c * (0 - 7) + 16 for j = 0 to 15; each row after adds c.
        Vector128<short> vb = Vector128.Create((short)b), vc = Vector128.Create((short)c);
        Vector128<short> row0 = Vector128.Create((short)(a - 7 * c + 16));
        Vector128<short> lo = row0 + vb * Vector128.Create((short)-7, -6, -5, -4, -3, -2, -1, 0);
        Vector128<short> hi = row0 + vb * Vector128.Create((short)1, 2, 3, 4, 5, 6, 7, 8);
        for (int32_t i = 0; i < 16; i++)
        {
            PackUs(Vector128.ShiftRightArithmetic(lo, 5), Vector128.ShiftRightArithmetic(hi, 5)).Store(pDst);
            lo += vc;
            hi += vc;
            pDst += iDstStride;
        }
    }

    /// <summary>WelsIChromaPredPlane_c's prediction, two rows of 8 a vector.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void PredPlane8x8(uint8_t* pDst, int32_t iDstStride, uint8_t* pRef, int32_t kiStride)
    {
        uint8_t* pTop = pRef - kiStride;
        uint8_t* pLeft = pRef - 1;
        int32_t iH = 0, iV = 0;
        for (int32_t i = 0; i < 4; i++)
        {
            iH += (i + 1) * (pTop[4 + i] - pTop[2 - i]);
            iV += (i + 1) * (pLeft[(4 + i) * kiStride] - pLeft[(2 - i) * kiStride]);
        }
        int32_t a = (pLeft[7 * kiStride] + pTop[7]) << 4;
        int32_t b = (17 * iH + 16) >> 5;
        int32_t c = (17 * iV + 16) >> 5;

        // Row 0: a + b * (j - 3) + c * (0 - 3) + 16 for j = 0 to 7; each row after adds c.
        Vector128<short> vc = Vector128.Create((short)c);
        Vector128<short> row = Vector128.Create((short)(a - 3 * c + 16)) +
                               Vector128.Create((short)b) * Vector128.Create((short)-3, -2, -1, 0, 1, 2, 3, 4);
        for (int32_t i = 0; i < 8; i += 2)
        {
            Vector128<byte> v = PackUs(Vector128.ShiftRightArithmetic(row, 5), Vector128.ShiftRightArithmetic(row + vc, 5));
            Store8(pDst, v);
            Store8High(pDst + iDstStride, v);
            row += vc + vc;
            pDst += 2 * iDstStride;
        }
    }
}
