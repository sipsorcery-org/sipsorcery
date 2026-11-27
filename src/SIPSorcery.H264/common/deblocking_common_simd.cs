//-----------------------------------------------------------------------------
// Filename: deblocking_common_simd.cs
//
// Description: SIMD versions of the loop filter edge kernels in
// deblocking_common.cs (P5.4), for the slots upstream's X86_ASM and HAVE_NEON
// blocks fill in the decoder's and the encoder's DeblockingInit. Not a port:
// upstream's are assembly. Each leaves the picture as its C kernel leaves it,
// for any input (deblocking_common_simd_unittest.cs).
//
// The C kernels decide sample by sample whether to filter and how strongly.
// Here every sample along the edge is worked out, 8 to a vector in 16-bit
// lanes, and the decisions pick between the filtered and the unfiltered
// values, so where C leaves a sample alone the same value is written back. The
// sums are exact in 16 bits, and the saturating pack to bytes clips as
// WelsClip1 does. Where C writes p1 + delta without WelsClip1, the result is
// already in 0..255: p1 + ((p2 + ((p0 + q0 + 1) >> 1) - 2 p1) >> 1) lies
// between the samples it averages, and the clip to tc0 only moves it back
// towards p1.
//
// The V kernels filter across a horizontal edge, so each of p3..q3 is a row,
// loaded as it is. The H kernels filter across a vertical edge, so the 16 rows
// (8 of Cb and 8 of Cr for chroma) are transposed into columns first and back
// after. They read only the samples their C kernels may read, the bS = 4
// kernels' p3 and q3 included (C reads them where it filters strongly), and
// write only the samples C may write. The single-plane chroma kernels run the
// two-plane ones with the plane given twice: both halves of each vector work
// out the same results, so the second write repeats the first.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 27 Nov 2026  Claude          Created (P5.4).
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
    //--------------------the filters, 8 samples at a time------------------//

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<short> AbsDiff(Vector128<short> a, Vector128<short> b) => Vector128.Abs(a - b);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<short> Clip3(Vector128<short> x, Vector128<short> lo, Vector128<short> hi) =>
        Vector128.Min(Vector128.Max(x, lo), hi);

    /// <summary>|p0 - q0| &lt; alpha, |p1 - p0| &lt; beta and |q1 - q0| &lt; beta: all ones where the edge is filtered.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<short> EdgeMask(Vector128<short> p1, Vector128<short> p0, Vector128<short> q0, Vector128<short> q1,
                                             Vector128<short> alpha, Vector128<short> beta) =>
        Vector128.LessThan(AbsDiff(p0, q0), alpha) & Vector128.LessThan(AbsDiff(p1, p0), beta) &
        Vector128.LessThan(AbsDiff(q1, q0), beta);

    /// <summary>
    /// clip3((((q0 - p0) &lt;&lt; 2) + (p1 - q1) + 4) &gt;&gt; 3, -tc, tc), the bS &lt; 4 change to p0 and
    /// q0.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<short> Lt4Delta(Vector128<short> p1, Vector128<short> p0, Vector128<short> q0, Vector128<short> q1,
                                             Vector128<short> tc) =>
        Clip3(Vector128.ShiftRightArithmetic(((q0 - p0) << 2) + (p1 - q1) + Vector128.Create((short)4), 3), -tc, tc);

    /// <summary>DeblockLumaLt4_c for 8 samples; tc0 is each sample's, -1 where bS is 0.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void LumaLt4Core(Vector128<short> p2, ref Vector128<short> p1, ref Vector128<short> p0, ref Vector128<short> q0,
                                    ref Vector128<short> q1, Vector128<short> q2, Vector128<short> alpha, Vector128<short> beta,
                                    Vector128<short> tc0)
    {
        Vector128<short> mask = EdgeMask(p1, p0, q0, q1, alpha, beta) & Vector128.GreaterThanOrEqual(tc0, Vector128<short>.Zero);
        Vector128<short> ap = Vector128.LessThan(AbsDiff(p2, p0), beta) & mask;
        Vector128<short> aq = Vector128.LessThan(AbsDiff(q2, q0), beta) & mask;

        Vector128<short> avg = Vector128.ShiftRightArithmetic(p0 + q0 + Vector128<short>.One, 1);
        Vector128<short> dp1 = Clip3(Vector128.ShiftRightArithmetic(p2 + avg - (p1 << 1), 1), -tc0, tc0);
        Vector128<short> dq1 = Clip3(Vector128.ShiftRightArithmetic(q2 + avg - (q1 << 1), 1), -tc0, tc0);
        // ap and aq are 0 or -1: tc = tc0 + 1 for each side that has its p1 or q1 filtered.
        Vector128<short> d = Lt4Delta(p1, p0, q0, q1, tc0 - ap - aq) & mask;

        p1 += dp1 & ap;
        q1 += dq1 & aq;
        p0 += d;
        q0 -= d;
    }

    /// <summary>DeblockLumaEq4_c for 8 samples.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void LumaEq4Core(Vector128<short> p3, ref Vector128<short> p2, ref Vector128<short> p1, ref Vector128<short> p0,
                                    ref Vector128<short> q0, ref Vector128<short> q1, ref Vector128<short> q2, Vector128<short> q3,
                                    Vector128<short> alpha, Vector128<short> beta, Vector128<short> strongLimit)
    {
        Vector128<short> mask = EdgeMask(p1, p0, q0, q1, alpha, beta);
        Vector128<short> strong = mask & Vector128.LessThan(AbsDiff(p0, q0), strongLimit);
        Vector128<short> ap = strong & Vector128.LessThan(AbsDiff(p2, p0), beta);
        Vector128<short> aq = strong & Vector128.LessThan(AbsDiff(q2, q0), beta);

        Vector128<short> k2 = Vector128.Create((short)2), k4 = Vector128.Create((short)4);
        Vector128<short> p0q0 = p0 + q0;
        // The strong filters, where ap or aq holds.
        Vector128<short> p0s = Vector128.ShiftRightArithmetic(p2 + ((p1 + p0q0) << 1) + q1 + k4, 3);
        Vector128<short> p1s = Vector128.ShiftRightArithmetic(p2 + p1 + p0q0 + k2, 2);
        Vector128<short> p2s = Vector128.ShiftRightArithmetic((p3 << 1) + p2 + (p2 << 1) + p1 + p0q0 + k4, 3);
        Vector128<short> q0s = Vector128.ShiftRightArithmetic(p1 + ((p0q0 + q1) << 1) + q2 + k4, 3);
        Vector128<short> q1s = Vector128.ShiftRightArithmetic(p0q0 + q1 + q2 + k2, 2);
        Vector128<short> q2s = Vector128.ShiftRightArithmetic((q3 << 1) + q2 + (q2 << 1) + q1 + p0q0 + k4, 3);
        // The weak ones, elsewhere on the filtered edge.
        Vector128<short> p0w = Vector128.ShiftRightArithmetic((p1 << 1) + p0 + q1 + k2, 2);
        Vector128<short> q0w = Vector128.ShiftRightArithmetic((q1 << 1) + q0 + p1 + k2, 2);

        Vector128<short> np0 = Vector128.ConditionalSelect(ap, p0s, Vector128.ConditionalSelect(mask, p0w, p0));
        Vector128<short> nq0 = Vector128.ConditionalSelect(aq, q0s, Vector128.ConditionalSelect(mask, q0w, q0));
        p2 = Vector128.ConditionalSelect(ap, p2s, p2);
        p1 = Vector128.ConditionalSelect(ap, p1s, p1);
        q1 = Vector128.ConditionalSelect(aq, q1s, q1);
        q2 = Vector128.ConditionalSelect(aq, q2s, q2);
        p0 = np0;
        q0 = nq0;
    }

    /// <summary>DeblockChromaLt4_c's filter for 8 samples; tc0 is each sample's.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ChromaLt4Core(Vector128<short> p1, ref Vector128<short> p0, ref Vector128<short> q0, Vector128<short> q1,
                                      Vector128<short> alpha, Vector128<short> beta, Vector128<short> tc0)
    {
        Vector128<short> mask = EdgeMask(p1, p0, q0, q1, alpha, beta) & Vector128.GreaterThan(tc0, Vector128<short>.Zero);
        Vector128<short> d = Lt4Delta(p1, p0, q0, q1, tc0) & mask;
        p0 += d;
        q0 -= d;
    }

    /// <summary>DeblockChromaEq4_c's filter for 8 samples.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ChromaEq4Core(Vector128<short> p1, ref Vector128<short> p0, ref Vector128<short> q0, Vector128<short> q1,
                                      Vector128<short> alpha, Vector128<short> beta)
    {
        Vector128<short> mask = EdgeMask(p1, p0, q0, q1, alpha, beta);
        Vector128<short> k2 = Vector128.Create((short)2);
        Vector128<short> np0 = Vector128.ShiftRightArithmetic((p1 << 1) + p0 + q1 + k2, 2);
        Vector128<short> nq0 = Vector128.ShiftRightArithmetic((q1 << 1) + q0 + p1 + k2, 2);
        p0 = Vector128.ConditionalSelect(mask, np0, p0);
        q0 = Vector128.ConditionalSelect(mask, nq0, q0);
    }

    //--------------------16 samples: both halves of the byte vectors------------------//

    /// <summary>The 4 tc0 values of a luma edge, one for each 4 samples, for samples 0 to 7 and 8 to 15.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void LumaTc(int8_t* pTc, out Vector128<short> lo, out Vector128<short> hi)
    {
        Vector128<short> t = Vector128.WidenLower(Vector128.CreateScalar(Unsafe.ReadUnaligned<uint>(pTc)).AsSByte());
        Vector128<int> t2 = ZipLow(t, t).AsInt32(); // t0 t0 t1 t1 t2 t2 t3 t3
        lo = ZipLow(t2, t2).AsInt16();
        hi = ZipHigh(t2, t2).AsInt16();
    }

    /// <summary>The 4 tc0 values of a chroma edge, one for each 2 samples, for samples 0 to 7.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<short> ChromaTc(int8_t* pTc)
    {
        Vector128<short> t = Vector128.WidenLower(Vector128.CreateScalar(Unsafe.ReadUnaligned<uint>(pTc)).AsSByte());
        return ZipLow(t, t);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void LumaLt4x16(Vector128<byte> p2, ref Vector128<byte> p1, ref Vector128<byte> p0, ref Vector128<byte> q0,
                                   ref Vector128<byte> q1, Vector128<byte> q2, int32_t iAlpha, int32_t iBeta, int8_t* pTc)
    {
        Vector128<short> alpha = Vector128.Create((short)iAlpha), beta = Vector128.Create((short)iBeta);
        LumaTc(pTc, out Vector128<short> tcLo, out Vector128<short> tcHi);
        Vector128<short> p1l = Lo16(p1), p0l = Lo16(p0), q0l = Lo16(q0), q1l = Lo16(q1);
        Vector128<short> p1h = Hi16(p1), p0h = Hi16(p0), q0h = Hi16(q0), q1h = Hi16(q1);
        LumaLt4Core(Lo16(p2), ref p1l, ref p0l, ref q0l, ref q1l, Lo16(q2), alpha, beta, tcLo);
        LumaLt4Core(Hi16(p2), ref p1h, ref p0h, ref q0h, ref q1h, Hi16(q2), alpha, beta, tcHi);
        p1 = PackUs(p1l, p1h);
        p0 = PackUs(p0l, p0h);
        q0 = PackUs(q0l, q0h);
        q1 = PackUs(q1l, q1h);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void LumaEq4x16(Vector128<byte> p3, ref Vector128<byte> p2, ref Vector128<byte> p1, ref Vector128<byte> p0,
                                   ref Vector128<byte> q0, ref Vector128<byte> q1, ref Vector128<byte> q2, Vector128<byte> q3,
                                   int32_t iAlpha, int32_t iBeta)
    {
        Vector128<short> alpha = Vector128.Create((short)iAlpha), beta = Vector128.Create((short)iBeta);
        Vector128<short> strongLimit = Vector128.Create((short)((iAlpha >> 2) + 2));
        Vector128<short> p2l = Lo16(p2), p1l = Lo16(p1), p0l = Lo16(p0), q0l = Lo16(q0), q1l = Lo16(q1), q2l = Lo16(q2);
        Vector128<short> p2h = Hi16(p2), p1h = Hi16(p1), p0h = Hi16(p0), q0h = Hi16(q0), q1h = Hi16(q1), q2h = Hi16(q2);
        LumaEq4Core(Lo16(p3), ref p2l, ref p1l, ref p0l, ref q0l, ref q1l, ref q2l, Lo16(q3), alpha, beta, strongLimit);
        LumaEq4Core(Hi16(p3), ref p2h, ref p1h, ref p0h, ref q0h, ref q1h, ref q2h, Hi16(q3), alpha, beta, strongLimit);
        p2 = PackUs(p2l, p2h);
        p1 = PackUs(p1l, p1h);
        p0 = PackUs(p0l, p0h);
        q0 = PackUs(q0l, q0h);
        q1 = PackUs(q1l, q1h);
        q2 = PackUs(q2l, q2h);
    }

    /// <summary>Cb in samples 0 to 7 and Cr in 8 to 15, with the same tc0 values.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ChromaLt4x16(Vector128<byte> p1, ref Vector128<byte> p0, ref Vector128<byte> q0, Vector128<byte> q1,
                                     int32_t iAlpha, int32_t iBeta, int8_t* pTc)
    {
        Vector128<short> alpha = Vector128.Create((short)iAlpha), beta = Vector128.Create((short)iBeta);
        Vector128<short> tc = ChromaTc(pTc);
        Vector128<short> p0l = Lo16(p0), q0l = Lo16(q0), p0h = Hi16(p0), q0h = Hi16(q0);
        ChromaLt4Core(Lo16(p1), ref p0l, ref q0l, Lo16(q1), alpha, beta, tc);
        ChromaLt4Core(Hi16(p1), ref p0h, ref q0h, Hi16(q1), alpha, beta, tc);
        p0 = PackUs(p0l, p0h);
        q0 = PackUs(q0l, q0h);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ChromaEq4x16(Vector128<byte> p1, ref Vector128<byte> p0, ref Vector128<byte> q0, Vector128<byte> q1,
                                     int32_t iAlpha, int32_t iBeta)
    {
        Vector128<short> alpha = Vector128.Create((short)iAlpha), beta = Vector128.Create((short)iBeta);
        Vector128<short> p0l = Lo16(p0), q0l = Lo16(q0), p0h = Hi16(p0), q0h = Hi16(q0);
        ChromaEq4Core(Lo16(p1), ref p0l, ref q0l, Lo16(q1), alpha, beta);
        ChromaEq4Core(Hi16(p1), ref p0h, ref q0h, Hi16(q1), alpha, beta);
        p0 = PackUs(p0l, p0h);
        q0 = PackUs(q0l, q0h);
    }

    //--------------------transposes for the H kernels------------------//

    /// <summary>
    /// 16 rows of 8 samples to 8 columns of 16: a_k holds row k in its low half and row k + 8
    /// in its high half, and c_j comes out as sample j of rows 0 to 15.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Transpose16x8(Vector128<byte> a0, Vector128<byte> a1, Vector128<byte> a2, Vector128<byte> a3,
                                      Vector128<byte> a4, Vector128<byte> a5, Vector128<byte> a6, Vector128<byte> a7,
                                      out Vector128<byte> c0, out Vector128<byte> c1, out Vector128<byte> c2, out Vector128<byte> c3,
                                      out Vector128<byte> c4, out Vector128<byte> c5, out Vector128<byte> c6, out Vector128<byte> c7)
    {
        // Pairs of rows: 16-bit lane j holds sample j of both.
        Vector128<short> b0 = ZipLow(a0, a1).AsInt16(), b1 = ZipHigh(a0, a1).AsInt16(); // rows 0, 1 and 8, 9
        Vector128<short> b2 = ZipLow(a2, a3).AsInt16(), b3 = ZipHigh(a2, a3).AsInt16(); // rows 2, 3 and 10, 11
        Vector128<short> b4 = ZipLow(a4, a5).AsInt16(), b5 = ZipHigh(a4, a5).AsInt16(); // rows 4, 5 and 12, 13
        Vector128<short> b6 = ZipLow(a6, a7).AsInt16(), b7 = ZipHigh(a6, a7).AsInt16(); // rows 6, 7 and 14, 15
        // Fours of rows: 32-bit lane j holds sample j (0 to 3, or 4 to 7) of all four.
        Vector128<int> d0 = ZipLow(b0, b2).AsInt32(), d1 = ZipHigh(b0, b2).AsInt32(); // rows 0-3
        Vector128<int> d2 = ZipLow(b4, b6).AsInt32(), d3 = ZipHigh(b4, b6).AsInt32(); // rows 4-7
        Vector128<int> d4 = ZipLow(b1, b3).AsInt32(), d5 = ZipHigh(b1, b3).AsInt32(); // rows 8-11
        Vector128<int> d6 = ZipLow(b5, b7).AsInt32(), d7 = ZipHigh(b5, b7).AsInt32(); // rows 12-15
        // Eights of rows: each 64-bit half one sample of eight rows.
        Vector128<long> e0 = ZipLow(d0, d2).AsInt64(), e1 = ZipHigh(d0, d2).AsInt64(); // rows 0-7, samples 0, 1 and 2, 3
        Vector128<long> e2 = ZipLow(d4, d6).AsInt64(), e3 = ZipHigh(d4, d6).AsInt64(); // rows 8-15, samples 0, 1 and 2, 3
        Vector128<long> e4 = ZipLow(d1, d3).AsInt64(), e5 = ZipHigh(d1, d3).AsInt64(); // rows 0-7, samples 4, 5 and 6, 7
        Vector128<long> e6 = ZipLow(d5, d7).AsInt64(), e7 = ZipHigh(d5, d7).AsInt64(); // rows 8-15, samples 4, 5 and 6, 7
        c0 = ZipLow(e0, e2).AsByte();
        c1 = ZipHigh(e0, e2).AsByte();
        c2 = ZipLow(e1, e3).AsByte();
        c3 = ZipHigh(e1, e3).AsByte();
        c4 = ZipLow(e4, e6).AsByte();
        c5 = ZipHigh(e4, e6).AsByte();
        c6 = ZipLow(e5, e7).AsByte();
        c7 = ZipHigh(e5, e7).AsByte();
    }

    /// <summary>
    /// Back from 8 columns of 16 to 16 rows of 8: r_k holds rows 2k and 2k + 1, one in each
    /// 64-bit half.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Transpose8x16(Vector128<byte> c0, Vector128<byte> c1, Vector128<byte> c2, Vector128<byte> c3,
                                      Vector128<byte> c4, Vector128<byte> c5, Vector128<byte> c6, Vector128<byte> c7,
                                      out Vector128<ulong> r0, out Vector128<ulong> r1, out Vector128<ulong> r2, out Vector128<ulong> r3,
                                      out Vector128<ulong> r4, out Vector128<ulong> r5, out Vector128<ulong> r6, out Vector128<ulong> r7)
    {
        // Pairs of samples: 16-bit lane k holds them for row k (0 to 7, or 8 to 15).
        Vector128<short> g0 = ZipLow(c0, c1).AsInt16(), g1 = ZipHigh(c0, c1).AsInt16();
        Vector128<short> g2 = ZipLow(c2, c3).AsInt16(), g3 = ZipHigh(c2, c3).AsInt16();
        Vector128<short> g4 = ZipLow(c4, c5).AsInt16(), g5 = ZipHigh(c4, c5).AsInt16();
        Vector128<short> g6 = ZipLow(c6, c7).AsInt16(), g7 = ZipHigh(c6, c7).AsInt16();
        // Fours of samples, for four rows.
        Vector128<int> h0 = ZipLow(g0, g2).AsInt32(), h1 = ZipHigh(g0, g2).AsInt32(); // rows 0-3 and 4-7, samples 0-3
        Vector128<int> h2 = ZipLow(g4, g6).AsInt32(), h3 = ZipHigh(g4, g6).AsInt32(); // rows 0-3 and 4-7, samples 4-7
        Vector128<int> h4 = ZipLow(g1, g3).AsInt32(), h5 = ZipHigh(g1, g3).AsInt32(); // rows 8-11 and 12-15, samples 0-3
        Vector128<int> h6 = ZipLow(g5, g7).AsInt32(), h7 = ZipHigh(g5, g7).AsInt32(); // rows 8-11 and 12-15, samples 4-7
        r0 = ZipLow(h0, h2).AsUInt64();
        r1 = ZipHigh(h0, h2).AsUInt64();
        r2 = ZipLow(h1, h3).AsUInt64();
        r3 = ZipHigh(h1, h3).AsUInt64();
        r4 = ZipLow(h4, h6).AsUInt64();
        r5 = ZipHigh(h4, h6).AsUInt64();
        r6 = ZipLow(h5, h7).AsUInt64();
        r7 = ZipHigh(h5, h7).AsUInt64();
    }

    /// <summary>Samples -3 to 2 of a row, the ones DeblockLumaLt4_c reads, in the low 6 bytes.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong LumaLt4Row(uint8_t* pPix) =>
        Unsafe.ReadUnaligned<uint>(pPix - 3) | ((ulong)Unsafe.ReadUnaligned<ushort>(pPix + 1) << 32);

    /// <summary>Samples -3 to 2 of a row from bytes 1 to 6 of <paramref name="v"/> (samples -4 to 3).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void StoreLumaEq4Row(uint8_t* pPix, ulong v)
    {
        Unsafe.WriteUnaligned(pPix - 3, (uint)(v >> 8));
        Unsafe.WriteUnaligned(pPix + 1, (ushort)(v >> 40));
    }

    //--------------------luma------------------//

    internal static void DeblockLumaLt4V_simd(uint8_t* pPix, int32_t iStride, int32_t iAlpha, int32_t iBeta, int8_t* tc)
    {
        Vector128<byte> p2 = Load16(pPix - 3 * iStride), p1 = Load16(pPix - 2 * iStride), p0 = Load16(pPix - iStride);
        Vector128<byte> q0 = Load16(pPix), q1 = Load16(pPix + iStride), q2 = Load16(pPix + 2 * iStride);
        LumaLt4x16(p2, ref p1, ref p0, ref q0, ref q1, q2, iAlpha, iBeta, tc);
        p1.Store(pPix - 2 * iStride);
        p0.Store(pPix - iStride);
        q0.Store(pPix);
        q1.Store(pPix + iStride);
    }

    internal static void DeblockLumaLt4H_simd(uint8_t* pPix, int32_t iStride, int32_t iAlpha, int32_t iBeta, int8_t* tc)
    {
        uint8_t* pLow = pPix + 8 * iStride;
        Transpose16x8(
            Vector128.Create(LumaLt4Row(pPix), LumaLt4Row(pLow)).AsByte(),
            Vector128.Create(LumaLt4Row(pPix + iStride), LumaLt4Row(pLow + iStride)).AsByte(),
            Vector128.Create(LumaLt4Row(pPix + 2 * iStride), LumaLt4Row(pLow + 2 * iStride)).AsByte(),
            Vector128.Create(LumaLt4Row(pPix + 3 * iStride), LumaLt4Row(pLow + 3 * iStride)).AsByte(),
            Vector128.Create(LumaLt4Row(pPix + 4 * iStride), LumaLt4Row(pLow + 4 * iStride)).AsByte(),
            Vector128.Create(LumaLt4Row(pPix + 5 * iStride), LumaLt4Row(pLow + 5 * iStride)).AsByte(),
            Vector128.Create(LumaLt4Row(pPix + 6 * iStride), LumaLt4Row(pLow + 6 * iStride)).AsByte(),
            Vector128.Create(LumaLt4Row(pPix + 7 * iStride), LumaLt4Row(pLow + 7 * iStride)).AsByte(),
            out Vector128<byte> p2, out Vector128<byte> p1, out Vector128<byte> p0, out Vector128<byte> q0,
            out Vector128<byte> q1, out Vector128<byte> q2, out _, out _);
        LumaLt4x16(p2, ref p1, ref p0, ref q0, ref q1, q2, iAlpha, iBeta, tc);

        // Samples -2 to 1 of each row back, four rows to a vector.
        Vector128<short> e0 = ZipLow(p1, p0).AsInt16(), e1 = ZipHigh(p1, p0).AsInt16();
        Vector128<short> e2 = ZipLow(q0, q1).AsInt16(), e3 = ZipHigh(q0, q1).AsInt16();
        Vector128<uint> f0 = ZipLow(e0, e2).AsUInt32(), f1 = ZipHigh(e0, e2).AsUInt32();
        Vector128<uint> f2 = ZipLow(e1, e3).AsUInt32(), f3 = ZipHigh(e1, e3).AsUInt32();
        uint8_t* p = pPix - 2;
        for (int32_t i = 0; i < 4; i++)
        {
            Unsafe.WriteUnaligned(p + i * iStride, f0.GetElement(i));
            Unsafe.WriteUnaligned(p + (4 + i) * iStride, f1.GetElement(i));
            Unsafe.WriteUnaligned(p + (8 + i) * iStride, f2.GetElement(i));
            Unsafe.WriteUnaligned(p + (12 + i) * iStride, f3.GetElement(i));
        }
    }

    internal static void DeblockLumaEq4V_simd(uint8_t* pPix, int32_t iStride, int32_t iAlpha, int32_t iBeta)
    {
        Vector128<byte> p3 = Load16(pPix - 4 * iStride), p2 = Load16(pPix - 3 * iStride);
        Vector128<byte> p1 = Load16(pPix - 2 * iStride), p0 = Load16(pPix - iStride);
        Vector128<byte> q0 = Load16(pPix), q1 = Load16(pPix + iStride);
        Vector128<byte> q2 = Load16(pPix + 2 * iStride), q3 = Load16(pPix + 3 * iStride);
        LumaEq4x16(p3, ref p2, ref p1, ref p0, ref q0, ref q1, ref q2, q3, iAlpha, iBeta);
        p2.Store(pPix - 3 * iStride);
        p1.Store(pPix - 2 * iStride);
        p0.Store(pPix - iStride);
        q0.Store(pPix);
        q1.Store(pPix + iStride);
        q2.Store(pPix + 2 * iStride);
    }

    internal static void DeblockLumaEq4H_simd(uint8_t* pPix, int32_t iStride, int32_t iAlpha, int32_t iBeta)
    {
        uint8_t* p = pPix - 4;
        uint8_t* pLow = p + 8 * iStride;
        Transpose16x8(
            Load8x2(p, pLow), Load8x2(p + iStride, pLow + iStride),
            Load8x2(p + 2 * iStride, pLow + 2 * iStride), Load8x2(p + 3 * iStride, pLow + 3 * iStride),
            Load8x2(p + 4 * iStride, pLow + 4 * iStride), Load8x2(p + 5 * iStride, pLow + 5 * iStride),
            Load8x2(p + 6 * iStride, pLow + 6 * iStride), Load8x2(p + 7 * iStride, pLow + 7 * iStride),
            out Vector128<byte> p3, out Vector128<byte> p2, out Vector128<byte> p1, out Vector128<byte> p0,
            out Vector128<byte> q0, out Vector128<byte> q1, out Vector128<byte> q2, out Vector128<byte> q3);
        LumaEq4x16(p3, ref p2, ref p1, ref p0, ref q0, ref q1, ref q2, q3, iAlpha, iBeta);
        Transpose8x16(p3, p2, p1, p0, q0, q1, q2, q3,
                      out Vector128<ulong> r0, out Vector128<ulong> r1, out Vector128<ulong> r2, out Vector128<ulong> r3,
                      out Vector128<ulong> r4, out Vector128<ulong> r5, out Vector128<ulong> r6, out Vector128<ulong> r7);
        StoreLumaEq4Row(pPix, r0.GetElement(0));
        StoreLumaEq4Row(pPix + iStride, r0.GetElement(1));
        StoreLumaEq4Row(pPix + 2 * iStride, r1.GetElement(0));
        StoreLumaEq4Row(pPix + 3 * iStride, r1.GetElement(1));
        StoreLumaEq4Row(pPix + 4 * iStride, r2.GetElement(0));
        StoreLumaEq4Row(pPix + 5 * iStride, r2.GetElement(1));
        StoreLumaEq4Row(pPix + 6 * iStride, r3.GetElement(0));
        StoreLumaEq4Row(pPix + 7 * iStride, r3.GetElement(1));
        StoreLumaEq4Row(pPix + 8 * iStride, r4.GetElement(0));
        StoreLumaEq4Row(pPix + 9 * iStride, r4.GetElement(1));
        StoreLumaEq4Row(pPix + 10 * iStride, r5.GetElement(0));
        StoreLumaEq4Row(pPix + 11 * iStride, r5.GetElement(1));
        StoreLumaEq4Row(pPix + 12 * iStride, r6.GetElement(0));
        StoreLumaEq4Row(pPix + 13 * iStride, r6.GetElement(1));
        StoreLumaEq4Row(pPix + 14 * iStride, r7.GetElement(0));
        StoreLumaEq4Row(pPix + 15 * iStride, r7.GetElement(1));
    }

    //--------------------chroma: Cb and Cr together------------------//

    /// <summary>Sample -2 to 1 of each of 8 rows of Cb and of Cr, transposed: Cb in samples 0 to 7, Cr in 8 to 15.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ChromaColumns(uint8_t* pPixCb, uint8_t* pPixCr, int32_t iStride, out Vector128<byte> p1,
                                      out Vector128<byte> p0, out Vector128<byte> q0, out Vector128<byte> q1)
    {
        uint8_t* pb = pPixCb - 2, pr = pPixCr - 2;
        Transpose16x8(
            Vector128.Create((ulong)Unsafe.ReadUnaligned<uint>(pb), Unsafe.ReadUnaligned<uint>(pr)).AsByte(),
            Vector128.Create((ulong)Unsafe.ReadUnaligned<uint>(pb + iStride), Unsafe.ReadUnaligned<uint>(pr + iStride)).AsByte(),
            Vector128.Create((ulong)Unsafe.ReadUnaligned<uint>(pb + 2 * iStride), Unsafe.ReadUnaligned<uint>(pr + 2 * iStride)).AsByte(),
            Vector128.Create((ulong)Unsafe.ReadUnaligned<uint>(pb + 3 * iStride), Unsafe.ReadUnaligned<uint>(pr + 3 * iStride)).AsByte(),
            Vector128.Create((ulong)Unsafe.ReadUnaligned<uint>(pb + 4 * iStride), Unsafe.ReadUnaligned<uint>(pr + 4 * iStride)).AsByte(),
            Vector128.Create((ulong)Unsafe.ReadUnaligned<uint>(pb + 5 * iStride), Unsafe.ReadUnaligned<uint>(pr + 5 * iStride)).AsByte(),
            Vector128.Create((ulong)Unsafe.ReadUnaligned<uint>(pb + 6 * iStride), Unsafe.ReadUnaligned<uint>(pr + 6 * iStride)).AsByte(),
            Vector128.Create((ulong)Unsafe.ReadUnaligned<uint>(pb + 7 * iStride), Unsafe.ReadUnaligned<uint>(pr + 7 * iStride)).AsByte(),
            out p1, out p0, out q0, out q1, out _, out _, out _, out _);
    }

    /// <summary>p0 and q0, samples -1 and 0, back to each of the 8 rows of Cb and of Cr.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void StoreChromaColumns(uint8_t* pPixCb, uint8_t* pPixCr, int32_t iStride, Vector128<byte> p0, Vector128<byte> q0)
    {
        Vector128<ushort> cb = ZipLow(p0, q0).AsUInt16(), cr = ZipHigh(p0, q0).AsUInt16();
        uint8_t* pb = pPixCb - 1, pr = pPixCr - 1;
        for (int32_t i = 0; i < 8; i++)
        {
            Unsafe.WriteUnaligned(pb + i * iStride, cb.GetElement(i));
            Unsafe.WriteUnaligned(pr + i * iStride, cr.GetElement(i));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void StoreChromaRow(uint8_t* pPixCb, uint8_t* pPixCr, Vector128<byte> v)
    {
        Store8(pPixCb, v);
        Unsafe.WriteUnaligned(pPixCr, v.AsUInt64().GetElement(1));
    }

    internal static void DeblockChromaLt4V_simd(uint8_t* pPixCb, uint8_t* pPixCr, int32_t iStride, int32_t iAlpha, int32_t iBeta,
                                                int8_t* tc)
    {
        Vector128<byte> p1 = Load8x2(pPixCb - 2 * iStride, pPixCr - 2 * iStride), p0 = Load8x2(pPixCb - iStride, pPixCr - iStride);
        Vector128<byte> q0 = Load8x2(pPixCb, pPixCr), q1 = Load8x2(pPixCb + iStride, pPixCr + iStride);
        ChromaLt4x16(p1, ref p0, ref q0, q1, iAlpha, iBeta, tc);
        StoreChromaRow(pPixCb - iStride, pPixCr - iStride, p0);
        StoreChromaRow(pPixCb, pPixCr, q0);
    }

    internal static void DeblockChromaLt4H_simd(uint8_t* pPixCb, uint8_t* pPixCr, int32_t iStride, int32_t iAlpha, int32_t iBeta,
                                                int8_t* tc)
    {
        ChromaColumns(pPixCb, pPixCr, iStride, out Vector128<byte> p1, out Vector128<byte> p0, out Vector128<byte> q0,
                      out Vector128<byte> q1);
        ChromaLt4x16(p1, ref p0, ref q0, q1, iAlpha, iBeta, tc);
        StoreChromaColumns(pPixCb, pPixCr, iStride, p0, q0);
    }

    internal static void DeblockChromaEq4V_simd(uint8_t* pPixCb, uint8_t* pPixCr, int32_t iStride, int32_t iAlpha, int32_t iBeta)
    {
        Vector128<byte> p1 = Load8x2(pPixCb - 2 * iStride, pPixCr - 2 * iStride), p0 = Load8x2(pPixCb - iStride, pPixCr - iStride);
        Vector128<byte> q0 = Load8x2(pPixCb, pPixCr), q1 = Load8x2(pPixCb + iStride, pPixCr + iStride);
        ChromaEq4x16(p1, ref p0, ref q0, q1, iAlpha, iBeta);
        StoreChromaRow(pPixCb - iStride, pPixCr - iStride, p0);
        StoreChromaRow(pPixCb, pPixCr, q0);
    }

    internal static void DeblockChromaEq4H_simd(uint8_t* pPixCb, uint8_t* pPixCr, int32_t iStride, int32_t iAlpha, int32_t iBeta)
    {
        ChromaColumns(pPixCb, pPixCr, iStride, out Vector128<byte> p1, out Vector128<byte> p0, out Vector128<byte> q0,
                      out Vector128<byte> q1);
        ChromaEq4x16(p1, ref p0, ref q0, q1, iAlpha, iBeta);
        StoreChromaColumns(pPixCb, pPixCr, iStride, p0, q0);
    }

    //--------------------chroma: one plane------------------//

    internal static void DeblockChromaLt4V2_simd(uint8_t* pPixCbCr, int32_t iStride, int32_t iAlpha, int32_t iBeta, int8_t* tc) =>
        DeblockChromaLt4V_simd(pPixCbCr, pPixCbCr, iStride, iAlpha, iBeta, tc);

    internal static void DeblockChromaLt4H2_simd(uint8_t* pPixCbCr, int32_t iStride, int32_t iAlpha, int32_t iBeta, int8_t* tc) =>
        DeblockChromaLt4H_simd(pPixCbCr, pPixCbCr, iStride, iAlpha, iBeta, tc);

    internal static void DeblockChromaEq4V2_simd(uint8_t* pPixCbCr, int32_t iStride, int32_t iAlpha, int32_t iBeta) =>
        DeblockChromaEq4V_simd(pPixCbCr, pPixCbCr, iStride, iAlpha, iBeta);

    internal static void DeblockChromaEq4H2_simd(uint8_t* pPixCbCr, int32_t iStride, int32_t iAlpha, int32_t iBeta) =>
        DeblockChromaEq4H_simd(pPixCbCr, pPixCbCr, iStride, iAlpha, iBeta);
}
