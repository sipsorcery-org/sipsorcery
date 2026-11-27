//-----------------------------------------------------------------------------
// Filename: decode_mb_aux_simd.cs
//
// Description: SIMD versions of the dequantisers and the inverse transforms in
// the encoder's decode_mb_aux.cs (P5.5), for the slots upstream's X86_ASM and
// HAVE_NEON blocks fill in WelsInitReconstructionFuncs. Not a port: upstream's
// are assembly. Each leaves its outputs as its C kernel does
// (enc_decode_mb_aux_simd_unittest.cs), and reads and writes only what the C
// kernel reads and writes.
//
// - The dequantisers and the DC Hadamard. C casts each product, sum and
//   difference to int16_t, so the wrapping 16-bit lanes give them exactly.
// - The 4x4 inverse transform. The one common/transform_simd.cs shares with the
//   decoder.
// - The I16x16 DC reconstruction. It works out (dc + 32) >> 6 in 32 bits, as C
//   does, then adds it to the 16 x 16 prediction.
//
// The reconstruction and the prediction are separate buffers wherever the
// encoder calls these.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 27 Nov 2026  Claude          Created (P5.5).
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using static SIPSorcery.H264.Simd;

namespace SIPSorcery.H264.Encoder;

internal static unsafe partial class WelsEnc
{
    //---------------------------------dequantisation-------------------------//

    public static void WelsDequant4x4_simd(int16_t* pRes, uint16_t* kpMF)
    {
        Vector128<short> mf = Vector128.Load((short*)kpMF);
        Vector128.Store(Vector128.Load(pRes) * mf, pRes);
        Vector128.Store(Vector128.Load(pRes + 8) * mf, pRes + 8);
    }

    public static void WelsDequantFour4x4_simd(int16_t* pRes, uint16_t* kpMF)
    {
        Vector128<short> mf = Vector128.Load((short*)kpMF);
        for (int32_t i = 0; i < 64; i += 8)
        {
            Vector128.Store(Vector128.Load(pRes + i) * mf, pRes + i);
        }
    }

    public static void WelsDequantIHadamard4x4_simd(int16_t* pRes, uint16_t kuiMF)
    {
        // Along each row: the block's columns, the right half a copy and unused.
        Vector128<short> a01 = Vector128.Load(pRes), a23 = Vector128.Load(pRes + 8);
        TransposeBlocks4x4x2(a01, a23, a01, a23, out Vector128<short> c0, out Vector128<short> c1, out Vector128<short> c2, out Vector128<short> c3);
        Vector128<short> t0 = c0 + c2;
        Vector128<short> t1 = c0 - c2;
        Vector128<short> t2 = c1 - c3;
        Vector128<short> t3 = c1 + c3;
        Transpose4x4x2(t0 + t3, t1 + t2, t1 - t2, t0 - t3,
                       out Vector128<short> r0, out Vector128<short> r1, out Vector128<short> r2, out Vector128<short> r3);

        // Down each column, times kuiMF.
        Vector128<short> mf = Vector128.Create((short)kuiMF);
        t0 = r0 + r2;
        t1 = r0 - r2;
        t2 = r1 - r3;
        t3 = r1 + r3;
        Vector128.Store(ZipLow(((t0 + t3) * mf).AsInt64(), ((t1 + t2) * mf).AsInt64()).AsInt16(), pRes);
        Vector128.Store(ZipLow(((t1 - t2) * mf).AsInt64(), ((t0 - t3) * mf).AsInt64()).AsInt16(), pRes + 8);
    }

    //---------------------------------reconstruction-------------------------//

    public static void WelsIDctT4Rec_simd(uint8_t* pRec, int32_t iStride, uint8_t* pPred, int32_t iPredStride, int16_t* pDct)
    {
        // The right half works out a copy, unused.
        Vector128<short> a01 = Vector128.Load(pDct), a23 = Vector128.Load(pDct + 8);
        Idct4x4x2(a01, a23, a01, a23, out Vector128<short> o0, out Vector128<short> o1, out Vector128<short> o2, out Vector128<short> o3);
        AddPred4x4(pRec, iStride, pPred, iPredStride, o0, o1, o2, o3);
    }

    /// <summary>The blocks at pDct[0..15] (left) and pDct[16..31] (right), 4 rows of 8.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void IDctT4Recx2(uint8_t* pRec, int32_t iStride, uint8_t* pPred, int32_t iPredStride, int16_t* pDct)
    {
        Idct4x4x2(Vector128.Load(pDct), Vector128.Load(pDct + 8), Vector128.Load(pDct + 16), Vector128.Load(pDct + 24),
                  out Vector128<short> o0, out Vector128<short> o1, out Vector128<short> o2, out Vector128<short> o3);
        AddPred8x4(pRec, iStride, pPred, iPredStride, o0, o1, o2, o3);
    }

    public static void WelsIDctFourT4Rec_simd(uint8_t* pRec, int32_t iStride, uint8_t* pPred, int32_t iPredStride, int16_t* pDct)
    {
        IDctT4Recx2(pRec, iStride, pPred, iPredStride, pDct);
        IDctT4Recx2(pRec + 4 * iStride, iStride, pPred + 4 * iPredStride, iPredStride, pDct + 32);
    }

    public static void WelsIDctRecI16x16Dc_simd(uint8_t* pRec, int32_t iStride, uint8_t* pPred, int32_t iPredStride, int16_t* pDctDc)
    {
        // (dc + 32) >> 6 for the 16 blocks, within +-512.
        Vector128<int> k32 = Vector128.Create(32);
        Vector128<short> d01 = Vector128.Load(pDctDc), d23 = Vector128.Load(pDctDc + 8);
        Vector128<short> q01 = PackSs(Vector128.ShiftRightArithmetic(Vector128.WidenLower(d01) + k32, 6),
                                      Vector128.ShiftRightArithmetic(Vector128.WidenUpper(d01) + k32, 6));
        Vector128<short> q23 = PackSs(Vector128.ShiftRightArithmetic(Vector128.WidenLower(d23) + k32, 6),
                                      Vector128.ShiftRightArithmetic(Vector128.WidenUpper(d23) + k32, 6));

        // Each row of 4 blocks, its values doubled up here and twice more in DcRows.
        DcRows(pRec, iStride, pPred, iPredStride, ZipLow(q01, q01));
        DcRows(pRec + 4 * iStride, iStride, pPred + 4 * iPredStride, iPredStride, ZipHigh(q01, q01));
        DcRows(pRec + 8 * iStride, iStride, pPred + 8 * iPredStride, iPredStride, ZipLow(q23, q23));
        DcRows(pRec + 12 * iStride, iStride, pPred + 12 * iPredStride, iPredStride, ZipHigh(q23, q23));
    }

    /// <summary>4 rows of 16: the prediction plus k0..k3, each over 4 columns (<paramref name="x"/> = k0 k0 k1 k1 k2 k2 k3 k3).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void DcRows(uint8_t* pRec, int32_t iStride, uint8_t* pPred, int32_t iPredStride, Vector128<short> x)
    {
        Vector128<short> lo = ZipLow(x.AsInt32(), x.AsInt32()).AsInt16();  // k0 x 4, k1 x 4
        Vector128<short> hi = ZipHigh(x.AsInt32(), x.AsInt32()).AsInt16(); // k2 x 4, k3 x 4
        for (int32_t i = 0; i < 4; i++)
        {
            Vector128<byte> p = Load16(pPred);
            Vector128.Store(PackUs(Lo16(p) + lo, Hi16(p) + hi), pRec);
            pRec += iStride;
            pPred += iPredStride;
        }
    }
}
