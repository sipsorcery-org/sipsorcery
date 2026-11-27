//-----------------------------------------------------------------------------
// Filename: encode_mb_aux_simd.cs
//
// Description: SIMD versions of the forward 4x4 transform, the luma DC
// Hadamard transform and the quantisers in encode_mb_aux.cs (P5.5), for the
// slots upstream's X86_ASM and HAVE_NEON blocks fill in WelsInitEncodingFuncs.
// Not a port: upstream's are assembly. Each leaves its outputs as its C kernel
// does (encode_mb_aux_simd_unittest.cs), and reads and writes only what the C
// kernel reads and writes.
//
// - The forward transform. Differences of 8-bit samples, two 4x4 blocks side by
//   side, go through rows then columns as the C does. No value passes +-9180,
//   so 16-bit lanes hold every int16_t the C keeps exactly.
// - The DC Hadamard transform. It sums in int32_t, so it runs in 32-bit lanes,
//   and the saturating pack to 16 bits is its WELS_CLIP3.
// - The quantisers. NEW_QUANT is ((iFF + |x|) * iMF) >> 16 with |x| up to
//   32768. For the FF and MF the tables give (and any from 0 to 32767), iFF + |x|
//   fits 16 bits unsigned and the int32_t product doesn't overflow, so the
//   unsigned high multiply gives it exactly.
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
    //---------------------------------quantisation---------------------------//

    /// <summary>WELS_NEW_QUANT of 8 coefficients; <paramref name="q"/> gets NEW_QUANT, the magnitude.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<short> Quant8(Vector128<short> x, Vector128<ushort> ff, Vector128<ushort> mf, out Vector128<short> q)
    {
        Vector128<short> sign = Vector128.ShiftRightArithmetic(x, 15);
        Vector128<ushort> abs = ((x ^ sign) - sign).AsUInt16();
        q = MultiplyHigh(abs + ff, mf).AsInt16();
        return (q ^ sign) - sign;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Quant16(int16_t* pDct, Vector128<ushort> ff, Vector128<ushort> mf)
    {
        Vector128.Store(Quant8(Vector128.Load(pDct), ff, mf, out _), pDct);
        Vector128.Store(Quant8(Vector128.Load(pDct + 8), ff, mf, out _), pDct + 8);
    }

    public static void WelsQuant4x4_simd(int16_t* pDct, int16_t* pFF, int16_t* pMF) =>
        Quant16(pDct, Vector128.Load((ushort*)pFF), Vector128.Load((ushort*)pMF));

    public static void WelsQuant4x4Dc_simd(int16_t* pDct, int16_t iFF, int16_t iMF) =>
        Quant16(pDct, Vector128.Create((ushort)iFF), Vector128.Create((ushort)iMF));

    public static void WelsQuantFour4x4_simd(int16_t* pDct, int16_t* pFF, int16_t* pMF)
    {
        Vector128<ushort> ff = Vector128.Load((ushort*)pFF), mf = Vector128.Load((ushort*)pMF);
        Quant16(pDct, ff, mf);
        Quant16(pDct + 16, ff, mf);
        Quant16(pDct + 32, ff, mf);
        Quant16(pDct + 48, ff, mf);
    }

    /// <summary>Quantises a block of 16, giving the lane by lane maximum of its magnitudes.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<short> QuantMax16(int16_t* pDct, Vector128<ushort> ff, Vector128<ushort> mf)
    {
        Vector128.Store(Quant8(Vector128.Load(pDct), ff, mf, out Vector128<short> q0), pDct);
        Vector128.Store(Quant8(Vector128.Load(pDct + 8), ff, mf, out Vector128<short> q1), pDct + 8);
        return Vector128.Max(q0, q1);
    }

    public static void WelsQuantFour4x4Max_simd(int16_t* pDct, int16_t* pFF, int16_t* pMF, int16_t* pMax)
    {
        Vector128<ushort> ff = Vector128.Load((ushort*)pFF), mf = Vector128.Load((ushort*)pMF);
        Vector128<short> m0 = QuantMax16(pDct, ff, mf);
        Vector128<short> m1 = QuantMax16(pDct + 16, ff, mf);
        Vector128<short> m2 = QuantMax16(pDct + 32, ff, mf);
        Vector128<short> m3 = QuantMax16(pDct + 48, ff, mf);

        // The four blocks' maxima at once. The magnitudes are 0 to 32766, so the signed maximum
        // is theirs, and none is below C's starting iMaxAbs of 0.
        Vector128<int> p = Vector128.Max(ZipLow(m0, m1), ZipHigh(m0, m1)).AsInt32(); // blocks 0, 1 alternating
        Vector128<int> q = Vector128.Max(ZipLow(m2, m3), ZipHigh(m2, m3)).AsInt32(); // blocks 2, 3
        Vector128<short> r = Vector128.Max(ZipLow(p, q).AsInt16(), ZipHigh(p, q).AsInt16()); // blocks 0 to 3, twice
        r = Vector128.Max(r, ZipHigh(r.AsInt64(), r.AsInt64()).AsInt16());
        Store8((uint8_t*)pMax, r.AsByte());
    }

    //---------------------------------transforms-----------------------------//

    /// <summary>
    /// WelsDctT4_c of the differences of two side by side 4x4 blocks: row r of each in
    /// <paramref name="d0"/>..<paramref name="d3"/> and of the coefficients in
    /// <paramref name="r0"/>..<paramref name="r3"/>, the left block in lanes 0 to 3.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Dct4x4x2(Vector128<short> d0, Vector128<short> d1, Vector128<short> d2, Vector128<short> d3,
                                 out Vector128<short> r0, out Vector128<short> r1, out Vector128<short> r2, out Vector128<short> r3)
    {
        // Along each row.
        Transpose4x4x2(d0, d1, d2, d3, out Vector128<short> c0, out Vector128<short> c1, out Vector128<short> c2, out Vector128<short> c3);
        Vector128<short> s0 = c0 + c3;
        Vector128<short> s3 = c0 - c3;
        Vector128<short> s1 = c1 + c2;
        Vector128<short> s2 = c1 - c2;
        Transpose4x4x2(s0 + s1, s3 + s3 + s2, s0 - s1, s3 - s2 - s2,
                       out Vector128<short> x0, out Vector128<short> x1, out Vector128<short> x2, out Vector128<short> x3);

        // Down each column.
        s0 = x0 + x3;
        s3 = x0 - x3;
        s1 = x1 + x2;
        s2 = x1 - x2;
        r0 = s0 + s1;
        r1 = s3 + s3 + s2;
        r2 = s0 - s1;
        r3 = s3 - s2 - s2;
    }

    public static void WelsDctT4_simd(int16_t* pDct, uint8_t* pPixel1, int32_t iStride1, uint8_t* pPixel2, int32_t iStride2)
    {
        // The right block is all 0, and isn't stored.
        Dct4x4x2(Diff4(pPixel1, pPixel2), Diff4(pPixel1 + iStride1, pPixel2 + iStride2),
                 Diff4(pPixel1 + 2 * iStride1, pPixel2 + 2 * iStride2), Diff4(pPixel1 + 3 * iStride1, pPixel2 + 3 * iStride2),
                 out Vector128<short> r0, out Vector128<short> r1, out Vector128<short> r2, out Vector128<short> r3);
        Vector128.Store(ZipLow(r0.AsInt64(), r1.AsInt64()).AsInt16(), pDct);
        Vector128.Store(ZipLow(r2.AsInt64(), r3.AsInt64()).AsInt16(), pDct + 8);
    }

    /// <summary>Two side by side 4x4 blocks to pDct[0..15] (left) and pDct[16..31] (right).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void DctT4x2(int16_t* pDct, uint8_t* pPixel1, int32_t iStride1, uint8_t* pPixel2, int32_t iStride2)
    {
        Dct4x4x2(Diff8(pPixel1, pPixel2), Diff8(pPixel1 + iStride1, pPixel2 + iStride2),
                 Diff8(pPixel1 + 2 * iStride1, pPixel2 + 2 * iStride2), Diff8(pPixel1 + 3 * iStride1, pPixel2 + 3 * iStride2),
                 out Vector128<short> r0, out Vector128<short> r1, out Vector128<short> r2, out Vector128<short> r3);
        Vector128.Store(ZipLow(r0.AsInt64(), r1.AsInt64()).AsInt16(), pDct);
        Vector128.Store(ZipLow(r2.AsInt64(), r3.AsInt64()).AsInt16(), pDct + 8);
        Vector128.Store(ZipHigh(r0.AsInt64(), r1.AsInt64()).AsInt16(), pDct + 16);
        Vector128.Store(ZipHigh(r2.AsInt64(), r3.AsInt64()).AsInt16(), pDct + 24);
    }

    public static void WelsDctFourT4_simd(int16_t* pDct, uint8_t* pPixel1, int32_t iStride1, uint8_t* pPixel2, int32_t iStride2)
    {
        DctT4x2(pDct, pPixel1, iStride1, pPixel2, iStride2);
        DctT4x2(pDct + 32, pPixel1 + 4 * iStride1, iStride1, pPixel2 + 4 * iStride2, iStride2);
    }

    /// <summary>
    /// The DC coefficients of the 16 blocks (each 16 coefficients, in WelsHadamardT4Dc_c's order)
    /// Hadamard transformed, halved with rounding and clipped to int16_t.
    /// </summary>
    public static void WelsHadamardT4Dc_simd(int16_t* pLumaDc, int16_t* pDct)
    {
        // C's first loop: lane r is its i = 4r, whose DCs are at iIdx + 0, 16, 64 and 80.
        Vector128<int> a = Vector128.Create(pDct[0], pDct[32], pDct[128], pDct[160]);
        Vector128<int> b = Vector128.Create(pDct[16], pDct[48], pDct[144], pDct[176]);
        Vector128<int> c = Vector128.Create(pDct[64], pDct[96], pDct[192], pDct[224]);
        Vector128<int> e = Vector128.Create(pDct[80], pDct[112], pDct[208], pDct[240]);
        Vector128<int> s0 = a + e;
        Vector128<int> s3 = a - e;
        Vector128<int> s1 = b + c;
        Vector128<int> s2 = b - c;
        Vector128<int> pk0 = s0 + s1; // p[4r]
        Vector128<int> pk1 = s3 + s2; // p[4r + 1]
        Vector128<int> pk2 = s0 - s1; // p[4r + 2]
        Vector128<int> pk3 = s3 - s2; // p[4r + 3]

        // Transposed, so lane i of x_r holds p[4r + i].
        Vector128<long> t0 = ZipLow(pk0, pk1).AsInt64();  // p0 p1 p4 p5
        Vector128<long> t1 = ZipHigh(pk0, pk1).AsInt64(); // p8 p9 p12 p13
        Vector128<long> t2 = ZipLow(pk2, pk3).AsInt64();  // p2 p3 p6 p7
        Vector128<long> t3 = ZipHigh(pk2, pk3).AsInt64(); // p10 p11 p14 p15
        Vector128<int> x0 = ZipLow(t0, t2).AsInt32();
        Vector128<int> x1 = ZipHigh(t0, t2).AsInt32();
        Vector128<int> x2 = ZipLow(t1, t3).AsInt32();
        Vector128<int> x3 = ZipHigh(t1, t3).AsInt32();

        // C's second loop, lane i its i.
        Vector128<int> k1 = Vector128.Create(1);
        s0 = x0 + x3 + k1;
        s3 = x0 - x3 + k1;
        s1 = x1 + x2;
        s2 = x1 - x2;
        Vector128.Store(PackSs(Vector128.ShiftRightArithmetic(s0 + s1, 1), Vector128.ShiftRightArithmetic(s3 + s2, 1)), pLumaDc);
        Vector128.Store(PackSs(Vector128.ShiftRightArithmetic(s0 - s1, 1), Vector128.ShiftRightArithmetic(s3 - s2, 1)), pLumaDc + 8);
    }
}
