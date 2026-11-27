//-----------------------------------------------------------------------------
// Filename: transform_simd.cs
//
// Description: The SIMD 4x4 inverse transform (P5.5) shared by the encoder's
// reconstruction (encoder/decode_mb_aux_simd.cs) and the decoder's
// (decoder/decode_mb_aux_simd.cs). Also the 4x4 transposes those files and the
// forward transform (encoder/encode_mb_aux_simd.cs) use. Not a port: upstream's
// are assembly, a copy in each codec.
//
// Two 4x4 blocks side by side go through at a time, in 16-bit lanes: the left
// block in lanes 0 to 3 of each vector and the right block in lanes 4 to 7.
//
// WelsIDctT4Rec_c and IdctResAddPred_c work out the same residual:
// - The first pass, along each row, keeps its results in int16_t. Sums and
//   differences wrapped to 16 bits are those int16_t casts, so 16-bit lanes give
//   them exactly, for any coefficients.
// - The second pass, down each column, adds four int16_t values in int32_t
//   before (x + 32) >> 6. That can need 18 bits, so it runs in 32-bit lanes.
//   Its result is within +-1792, which the saturating pack back to 16 bits
//   keeps.
// - Adding the prediction then fits 16 bits, and the saturating pack to bytes
//   clips as WelsClip1 does.
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

namespace SIPSorcery.H264;

internal static unsafe partial class WelsCommon
{
    /// <summary>
    /// Transposes each half of four vectors: lane j of <paramref name="x0"/>..<paramref name="x3"/>
    /// (vector k) goes to lane k of vector j, lanes 0 to 3 and 4 to 7 apart. Rows of two side by
    /// side blocks become their columns, and back.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void Transpose4x4x2(Vector128<short> x0, Vector128<short> x1, Vector128<short> x2, Vector128<short> x3,
                                        out Vector128<short> y0, out Vector128<short> y1, out Vector128<short> y2, out Vector128<short> y3)
    {
        Vector128<int> t0 = ZipLow(x0, x1).AsInt32();  // left: x0.0 x1.0 x0.1 x1.1 x0.2 x1.2 x0.3 x1.3
        Vector128<int> t1 = ZipHigh(x0, x1).AsInt32(); // right
        Vector128<int> t2 = ZipLow(x2, x3).AsInt32();  // left: x2.0 x3.0 ...
        Vector128<int> t3 = ZipHigh(x2, x3).AsInt32(); // right
        Vector128<long> u0 = ZipLow(t0, t2).AsInt64();  // left lanes 0 and 1 of x0..x3
        Vector128<long> u1 = ZipHigh(t0, t2).AsInt64(); // left lanes 2 and 3
        Vector128<long> u2 = ZipLow(t1, t3).AsInt64();  // right lanes 0 and 1
        Vector128<long> u3 = ZipHigh(t1, t3).AsInt64(); // right lanes 2 and 3
        y0 = ZipLow(u0, u2).AsInt16();
        y1 = ZipHigh(u0, u2).AsInt16();
        y2 = ZipLow(u1, u3).AsInt16();
        y3 = ZipHigh(u1, u3).AsInt16();
    }

    /// <summary>
    /// The columns of two 4x4 blocks of coefficients as they are stored, 16 a block row by row:
    /// <paramref name="a01"/> holds rows 0 and 1 of the left block, <paramref name="a23"/> rows 2
    /// and 3, and <paramref name="b01"/>, <paramref name="b23"/> the right block's. Column k of
    /// each, rows 0 to 3, goes to <paramref name="c0"/>..<paramref name="c3"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void TransposeBlocks4x4x2(Vector128<short> a01, Vector128<short> a23, Vector128<short> b01, Vector128<short> b23,
                                              out Vector128<short> c0, out Vector128<short> c1, out Vector128<short> c2, out Vector128<short> c3)
    {
        Vector128<short> t0 = ZipLow(a01, a23);  // r0.0 r2.0 r0.1 r2.1 r0.2 r2.2 r0.3 r2.3
        Vector128<short> t1 = ZipHigh(a01, a23); // r1.0 r3.0 r1.1 r3.1 ...
        Vector128<long> ua = ZipLow(t0, t1).AsInt64();   // columns 0 and 1, rows 0 to 3
        Vector128<long> va = ZipHigh(t0, t1).AsInt64();  // columns 2 and 3
        t0 = ZipLow(b01, b23);
        t1 = ZipHigh(b01, b23);
        Vector128<long> ub = ZipLow(t0, t1).AsInt64();
        Vector128<long> vb = ZipHigh(t0, t1).AsInt64();
        c0 = ZipLow(ua, ub).AsInt16();
        c1 = ZipHigh(ua, ub).AsInt16();
        c2 = ZipLow(va, vb).AsInt16();
        c3 = ZipHigh(va, vb).AsInt16();
    }

    /// <summary>
    /// The residual WelsIDctT4Rec_c and IdctResAddPred_c add to the prediction, for two 4x4
    /// blocks of coefficients stored as <see cref="TransposeBlocks4x4x2"/> takes them: row r of
    /// each in <paramref name="o0"/>..<paramref name="o3"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void Idct4x4x2(Vector128<short> a01, Vector128<short> a23, Vector128<short> b01, Vector128<short> b23,
                                   out Vector128<short> o0, out Vector128<short> o1, out Vector128<short> o2, out Vector128<short> o3)
    {
        TransposeBlocks4x4x2(a01, a23, b01, b23, out Vector128<short> c0, out Vector128<short> c1, out Vector128<short> c2, out Vector128<short> c3);

        // Along each row, in 16 bits.
        Vector128<short> sumU = c0 + c2;
        Vector128<short> delU = c0 - c2;
        Vector128<short> sumD = c1 + Vector128.ShiftRightArithmetic(c3, 1);
        Vector128<short> delD = Vector128.ShiftRightArithmetic(c1, 1) - c3;
        Transpose4x4x2(sumU + sumD, delU + delD, delU - delD, sumU - sumD,
                       out Vector128<short> r0, out Vector128<short> r1, out Vector128<short> r2, out Vector128<short> r3);

        // Down each column, in 32 bits.
        Vector128<short> r1h = Vector128.ShiftRightArithmetic(r1, 1);
        Vector128<short> r3h = Vector128.ShiftRightArithmetic(r3, 1);
        IdctColumns(Vector128.WidenLower(r0), Vector128.WidenLower(r1), Vector128.WidenLower(r1h),
                    Vector128.WidenLower(r2), Vector128.WidenLower(r3), Vector128.WidenLower(r3h),
                    out Vector128<int> l0, out Vector128<int> l1, out Vector128<int> l2, out Vector128<int> l3);
        IdctColumns(Vector128.WidenUpper(r0), Vector128.WidenUpper(r1), Vector128.WidenUpper(r1h),
                    Vector128.WidenUpper(r2), Vector128.WidenUpper(r3), Vector128.WidenUpper(r3h),
                    out Vector128<int> h0, out Vector128<int> h1, out Vector128<int> h2, out Vector128<int> h3);
        o0 = PackSs(l0, h0);
        o1 = PackSs(l1, h1);
        o2 = PackSs(l2, h2);
        o3 = PackSs(l3, h3);
    }

    /// <summary>The second pass of the inverse transform on four rows of 4: rows 1 and 3 also halved.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void IdctColumns(Vector128<int> r0, Vector128<int> r1, Vector128<int> r1h, Vector128<int> r2, Vector128<int> r3,
                                    Vector128<int> r3h, out Vector128<int> o0, out Vector128<int> o1, out Vector128<int> o2,
                                    out Vector128<int> o3)
    {
        Vector128<int> k32 = Vector128.Create(32);
        Vector128<int> sumL = r0 + r2 + k32;
        Vector128<int> delL = r0 - r2 + k32;
        Vector128<int> sumR = r1 + r3h;
        Vector128<int> delR = r1h - r3;
        o0 = Vector128.ShiftRightArithmetic(sumL + sumR, 6);
        o1 = Vector128.ShiftRightArithmetic(delL + delR, 6);
        o2 = Vector128.ShiftRightArithmetic(delL - delR, 6);
        o3 = Vector128.ShiftRightArithmetic(sumL - sumR, 6);
    }

    /// <summary>
    /// WelsClip1(prediction + residual) for 4 rows of 8: rows r of two side by side blocks. The
    /// reconstruction may be the prediction itself (the decoder's), as each row is read before
    /// it's written.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void AddPred8x4(uint8_t* pRec, int32_t iStride, uint8_t* pPred, int32_t iPredStride,
                                    Vector128<short> o0, Vector128<short> o1, Vector128<short> o2, Vector128<short> o3)
    {
        Vector128<byte> v01 = PackUs(Load8W(pPred) + o0, Load8W(pPred + iPredStride) + o1);
        Store8(pRec, v01);
        Store8High(pRec + iStride, v01);
        Vector128<byte> v23 = PackUs(Load8W(pPred + 2 * iPredStride) + o2, Load8W(pPred + 3 * iPredStride) + o3);
        Store8(pRec + 2 * iStride, v23);
        Store8High(pRec + 3 * iStride, v23);
    }

    /// <summary>As <see cref="AddPred8x4"/>, for the left block alone.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void AddPred4x4(uint8_t* pRec, int32_t iStride, uint8_t* pPred, int32_t iPredStride,
                                    Vector128<short> o0, Vector128<short> o1, Vector128<short> o2, Vector128<short> o3)
    {
        Vector128<short> p01 = Rows4x2W(Unsafe.ReadUnaligned<uint>(pPred), Unsafe.ReadUnaligned<uint>(pPred + iPredStride));
        Vector128<short> p23 = Rows4x2W(Unsafe.ReadUnaligned<uint>(pPred + 2 * iPredStride), Unsafe.ReadUnaligned<uint>(pPred + 3 * iPredStride));
        Vector128<uint> v = PackUs(p01 + ZipLow(o0.AsInt64(), o1.AsInt64()).AsInt16(),
                                   p23 + ZipLow(o2.AsInt64(), o3.AsInt64()).AsInt16()).AsUInt32();
        Unsafe.WriteUnaligned(pRec, v.GetElement(0));
        Unsafe.WriteUnaligned(pRec + iStride, v.GetElement(1));
        Unsafe.WriteUnaligned(pRec + 2 * iStride, v.GetElement(2));
        Unsafe.WriteUnaligned(pRec + 3 * iStride, v.GetElement(3));
    }
}
