//-----------------------------------------------------------------------------
// Filename: sample_simd.cs
//
// Description: SIMD versions of the SATD kernels in sample.cs (P5.2), for the
// slots upstream's X86_ASM and HAVE_NEON blocks fill in WelsInitSampleSadFunc.
// Not a port: upstream's are assembly. Each returns what its C kernel returns,
// for any input (sample_simd_unittest.cs), and reads only the samples the C
// kernel reads.
//
// The C kernels transform each 4x4 block of differences, rows then columns,
// with the 4-point Hadamard butterfly, and add up ((sum of |coefficients|) + 1)
// >> 1 over the blocks. Here four rows of 8 samples, two 4x4 blocks side by side,
// go through at a time: columns first, lane by lane, then a transpose, then rows,
// lane by lane. The transform is linear and exact in 16 bits (|coefficient| <=
// 16 x 255), so the order doesn't change a coefficient. Every coefficient of a
// block is a +-1 sum of all 16 of its differences, so all 16 have that sum's
// parity and their absolute sum is even. The + 1 never carries, and the halving
// can wait until the blocks are added up.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 27 Nov 2026  Claude          Created (P5.2).
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
    /// <summary>
    /// The absolute coefficients of the two 4x4 blocks in four rows of 8 differences, the left
    /// block in lanes 0 to 3 and the right in lanes 4 to 7, added up in each lane over the
    /// four outputs (at most 4 x 4080 a lane).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<ushort> SatdAbs8x4(Vector128<short> r0, Vector128<short> r1, Vector128<short> r2, Vector128<short> r3)
    {
        // Columns: the butterfly down each lane.
        Vector128<short> s0 = r0 + r2;
        Vector128<short> s1 = r1 + r3;
        Vector128<short> d0 = r0 - r2;
        Vector128<short> d1 = r1 - r3;
        Vector128<short> t0 = s0 + s1;
        Vector128<short> t1 = d0 + d1;
        Vector128<short> t2 = d0 - d1;
        Vector128<short> t3 = s0 - s1;

        // Transpose each block, so x(j) holds column j of the left block in lanes 0 to 3 and of
        // the right block in lanes 4 to 7.
        Vector128<int> a = ZipLow(t0, t1).AsInt32();
        Vector128<int> b = ZipLow(t2, t3).AsInt32();
        Vector128<int> c = ZipHigh(t0, t1).AsInt32();
        Vector128<int> d = ZipHigh(t2, t3).AsInt32();
        Vector128<long> p = ZipLow(a, b).AsInt64();   // left columns 0 and 1
        Vector128<long> q = ZipHigh(a, b).AsInt64();  // left columns 2 and 3
        Vector128<long> r = ZipLow(c, d).AsInt64();   // right columns 0 and 1
        Vector128<long> s = ZipHigh(c, d).AsInt64();  // right columns 2 and 3
        Vector128<short> x0 = ZipLow(p, r).AsInt16();
        Vector128<short> x1 = ZipHigh(p, r).AsInt16();
        Vector128<short> x2 = ZipLow(q, s).AsInt16();
        Vector128<short> x3 = ZipHigh(q, s).AsInt16();

        // Rows: the butterfly across the columns, lane by lane.
        s0 = x0 + x2;
        s1 = x1 + x3;
        d0 = x0 - x2;
        d1 = x1 - x3;
        return (Vector128.Abs(s0 + s1) + Vector128.Abs(d0 + d1) + Vector128.Abs(d0 - d1) + Vector128.Abs(s0 - s1)).AsUInt16();
    }

    /// <summary>Two side by side 4x4 blocks of four rows of 8 samples.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<ushort> SatdAbs8x4(uint8_t* pSample1, int32_t iStride1, uint8_t* pSample2, int32_t iStride2) =>
        SatdAbs8x4(Diff8(pSample1, pSample2), Diff8(pSample1 + iStride1, pSample2 + iStride2),
            Diff8(pSample1 + 2 * iStride1, pSample2 + 2 * iStride2), Diff8(pSample1 + 3 * iStride1, pSample2 + 3 * iStride2));

    public static int32_t WelsSampleSatd4x4_simd(uint8_t* pSample1, int32_t iStride1, uint8_t* pSample2, int32_t iStride2)
    {
        // The right block is all 0 and adds nothing.
        Vector128<ushort> acc = SatdAbs8x4(Diff4(pSample1, pSample2), Diff4(pSample1 + iStride1, pSample2 + iStride2),
            Diff4(pSample1 + 2 * iStride1, pSample2 + 2 * iStride2), Diff4(pSample1 + 3 * iStride1, pSample2 + 3 * iStride2));
        return SumWidened(acc) >> 1;
    }

    public static int32_t WelsSampleSatd4x8_simd(uint8_t* pSample1, int32_t iStride1, uint8_t* pSample2, int32_t iStride2)
    {
        // The top block in lanes 0 to 3, the bottom block in lanes 4 to 7.
        int32_t iOff1 = 4 * iStride1;
        int32_t iOff2 = 4 * iStride2;
        Vector128<ushort> acc = SatdAbs8x4(
            Diff4x2(pSample1, pSample1 + iOff1, pSample2, pSample2 + iOff2),
            Diff4x2(pSample1 + iStride1, pSample1 + iStride1 + iOff1, pSample2 + iStride2, pSample2 + iStride2 + iOff2),
            Diff4x2(pSample1 + 2 * iStride1, pSample1 + 2 * iStride1 + iOff1, pSample2 + 2 * iStride2, pSample2 + 2 * iStride2 + iOff2),
            Diff4x2(pSample1 + 3 * iStride1, pSample1 + 3 * iStride1 + iOff1, pSample2 + 3 * iStride2, pSample2 + 3 * iStride2 + iOff2));
        return SumWidened(acc) >> 1;
    }

    public static int32_t WelsSampleSatd8x4_simd(uint8_t* pSample1, int32_t iStride1, uint8_t* pSample2, int32_t iStride2) =>
        SumWidened(SatdAbs8x4(pSample1, iStride1, pSample2, iStride2)) >> 1;

    /// <summary>8 samples wide, <paramref name="iHeight"/> (8 or 16) rows: at most 4 strips a lane.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<ushort> SatdAbs8xN(uint8_t* pSample1, int32_t iStride1, uint8_t* pSample2, int32_t iStride2, int32_t iHeight)
    {
        Vector128<ushort> acc = Vector128<ushort>.Zero;
        for (int32_t i = 0; i < iHeight; i += 4)
        {
            acc += SatdAbs8x4(pSample1, iStride1, pSample2, iStride2);
            pSample1 += 4 * iStride1;
            pSample2 += 4 * iStride2;
        }
        return acc;
    }

    public static int32_t WelsSampleSatd8x8_simd(uint8_t* pSample1, int32_t iStride1, uint8_t* pSample2, int32_t iStride2) =>
        SumWidened(SatdAbs8xN(pSample1, iStride1, pSample2, iStride2, 8)) >> 1;

    public static int32_t WelsSampleSatd8x16_simd(uint8_t* pSample1, int32_t iStride1, uint8_t* pSample2, int32_t iStride2) =>
        SumWidened(SatdAbs8xN(pSample1, iStride1, pSample2, iStride2, 16)) >> 1;

    public static int32_t WelsSampleSatd16x8_simd(uint8_t* pSample1, int32_t iStride1, uint8_t* pSample2, int32_t iStride2) =>
        (SumWidened(SatdAbs8xN(pSample1, iStride1, pSample2, iStride2, 8)) +
         SumWidened(SatdAbs8xN(pSample1 + 8, iStride1, pSample2 + 8, iStride2, 8))) >> 1;

    public static int32_t WelsSampleSatd16x16_simd(uint8_t* pSample1, int32_t iStride1, uint8_t* pSample2, int32_t iStride2) =>
        (SumWidened(SatdAbs8xN(pSample1, iStride1, pSample2, iStride2, 16)) +
         SumWidened(SatdAbs8xN(pSample1 + 8, iStride1, pSample2 + 8, iStride2, 16))) >> 1;
}
