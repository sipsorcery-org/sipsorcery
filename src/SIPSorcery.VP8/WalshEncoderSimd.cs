//-----------------------------------------------------------------------------
// Encoder-side SIMD fast paths for the 4x4 Walsh-Hadamard pair used on the
// Y2 (DC) block in 16x16 intra prediction modes:
//
//   * Walsh4x4   - bit-exact replacement for dct.vp8_short_walsh4x4_c
//                  (forward Walsh, 1 call per MB)
//   * InverseWalsh4x4 - bit-exact replacement for the row/column scalar
//                  variant used by mb_encoder.InverseWalsh4x4Into
//                  (inverse Walsh, 1 call per MB)
//
// Both transforms are tiny (16 coefficients, 1 call per MB) but trivially
// SIMD-able into 4 row/column vectors with the same transpose primitive
// used by IdctEncoderSimd. Scalar fallback is provided for non-SIMD
// platforms; on x86 we require SSE4.1 to stay aligned with IdctEncoderSimd.
//
// Author: Claude Opus 4.7 (commissioned by Aaron Clauson).
//
// License: BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

namespace Vpx.Net
{
    internal static unsafe class WalshEncoderSimd
    {
        public static bool IsSupported => Sse41.IsSupported || AdvSimd.Arm64.IsSupported;

        // ---------------------------------------------------------------
        // Forward 4x4 Walsh (bit-exact with dct.vp8_short_walsh4x4_c).
        // Input layout: 4 rows of 4 shorts at <paramref name="pitch"/>
        // bytes per row. Output: 16 contiguous shorts row-major.
        // ---------------------------------------------------------------

        public static void Walsh4x4(short* input, short* output, int pitch)
        {
            if (Sse41.IsSupported)
            {
                Walsh4x4Sse41(input, output, pitch);
                return;
            }
            if (AdvSimd.Arm64.IsSupported)
            {
                Walsh4x4AdvSimd(input, output, pitch);
                return;
            }

            dct.vp8_short_walsh4x4_c(input, output, pitch);
        }

        // ---------------------------------------------------------------
        // Inverse 4x4 Walsh (bit-exact with the loop in
        // mb_encoder.InverseWalsh4x4Into; identical to libvpx's
        // vp8_short_inv_walsh4x4_c stage shape but writes contiguous
        // shorts rather than into the strided dqcoeff buffer).
        // ---------------------------------------------------------------

        public static void InverseWalsh4x4(short* input, short* output)
        {
            if (Sse41.IsSupported)
            {
                InverseWalsh4x4Sse41(input, output);
                return;
            }
            if (AdvSimd.Arm64.IsSupported)
            {
                InverseWalsh4x4AdvSimd(input, output);
                return;
            }

            InverseWalsh4x4Scalar(input, output);
        }

        // ===============================================================
        // SSE4.1 paths
        // ===============================================================

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Walsh4x4Sse41(short* input, short* output, int pitch)
        {
            int stride = pitch / 2;

            // Load 4 rows × 4 shorts each, sign-extend to int32.
            var r0 = Sse41.ConvertToVector128Int32(Sse2.LoadScalarVector128((double*)(input + 0 * stride)).AsInt16());
            var r1 = Sse41.ConvertToVector128Int32(Sse2.LoadScalarVector128((double*)(input + 1 * stride)).AsInt16());
            var r2 = Sse41.ConvertToVector128Int32(Sse2.LoadScalarVector128((double*)(input + 2 * stride)).AsInt16());
            var r3 = Sse41.ConvertToVector128Int32(Sse2.LoadScalarVector128((double*)(input + 3 * stride)).AsInt16());

            // Transpose so each c_j has lane r = input[r, j].
            Transpose4x4Sse2(r0, r1, r2, r3, out var c0, out var c1, out var c2, out var c3);

            // Row-pass (lane = row, in parallel across 4 rows).
            // a1 = (c0+c2) << 2, d1 = (c1+c3) << 2, c1v = (c1-c3) << 2, b1 = (c0-c2) << 2
            var a1 = Sse2.ShiftLeftLogical(Sse2.Add(c0, c2), 2);
            var d1 = Sse2.ShiftLeftLogical(Sse2.Add(c1, c3), 2);
            var c1v = Sse2.ShiftLeftLogical(Sse2.Subtract(c1, c3), 2);
            var b1 = Sse2.ShiftLeftLogical(Sse2.Subtract(c0, c2), 2);

            // m0[r] = (a1 + d1) + (a1 != 0 ? 1 : 0)
            var m0 = Sse2.Add(a1, d1);
            var a1Zero = Sse2.CompareEqual(a1, Vector128<int>.Zero);
            m0 = Sse2.Add(m0, Sse2.AndNot(a1Zero, Vector128.Create(1)));

            var m1 = Sse2.Add(b1, c1v);
            var m2 = Sse2.Subtract(b1, c1v);
            var m3 = Sse2.Subtract(a1, d1);

            // Transpose to get t_r[c] = intermediate[r, c].
            Transpose4x4Sse2(m0, m1, m2, m3, out var t0, out var t1, out var t2, out var t3);

            // Column-pass (lane = column).
            var a1c = Sse2.Add(t0, t2);
            var d1c = Sse2.Add(t1, t3);
            var c1c = Sse2.Subtract(t1, t3);
            var b1c = Sse2.Subtract(t0, t2);

            var a2 = Sse2.Add(a1c, d1c);
            var b2 = Sse2.Add(b1c, c1c);
            var cc2 = Sse2.Subtract(b1c, c1c);
            var d2 = Sse2.Subtract(a1c, d1c);

            // round-toward-zero correction: x += (x < 0) ? 1 : 0,
            // implemented as x += (uint)x >> 31 — sign-bit-as-int.
            a2 = Sse2.Add(a2, Sse2.ShiftRightLogical(a2, 31));
            b2 = Sse2.Add(b2, Sse2.ShiftRightLogical(b2, 31));
            cc2 = Sse2.Add(cc2, Sse2.ShiftRightLogical(cc2, 31));
            d2 = Sse2.Add(d2, Sse2.ShiftRightLogical(d2, 31));

            var three = Vector128.Create(3);
            var o0 = Sse2.ShiftRightArithmetic(Sse2.Add(a2, three), 3);
            var o1 = Sse2.ShiftRightArithmetic(Sse2.Add(b2, three), 3);
            var o2 = Sse2.ShiftRightArithmetic(Sse2.Add(cc2, three), 3);
            var o3 = Sse2.ShiftRightArithmetic(Sse2.Add(d2, three), 3);

            // Pack int32 → int16 row-major. o_r is row r; lo 4 lanes hold the row.
            Sse2.Store(output + 0, Sse2.PackSignedSaturate(o0, o1));
            Sse2.Store(output + 8, Sse2.PackSignedSaturate(o2, o3));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void InverseWalsh4x4Sse41(short* input, short* output)
        {
            // 4 rows × 4 shorts at unit stride (16 contiguous shorts).
            var r0 = Sse41.ConvertToVector128Int32(Sse2.LoadScalarVector128((double*)(input + 0)).AsInt16());
            var r1 = Sse41.ConvertToVector128Int32(Sse2.LoadScalarVector128((double*)(input + 4)).AsInt16());
            var r2 = Sse41.ConvertToVector128Int32(Sse2.LoadScalarVector128((double*)(input + 8)).AsInt16());
            var r3 = Sse41.ConvertToVector128Int32(Sse2.LoadScalarVector128((double*)(input + 12)).AsInt16());

            // Stage 1 (rows in scalar; lane = row in SIMD with input as cols).
            // Scalar: a1 = ip[0]+ip[3]; b1 = ip[1]+ip[2]; c1 = ip[1]-ip[2]; d1 = ip[0]-ip[3]
            //         tmp[i*4+0] = a1+b1; tmp[i*4+1] = c1+d1; tmp[i*4+2] = a1-b1; tmp[i*4+3] = d1-c1
            // For SIMD lane-wise across rows: r_i = row i of input. We need ip[0]/[3] of row i = row i col 0/3.
            // Operations are within-row, so transpose first.
            Transpose4x4Sse2(r0, r1, r2, r3, out var c0, out var c1, out var c2, out var c3);

            var a1 = Sse2.Add(c0, c3);
            var b1 = Sse2.Add(c1, c2);
            var c1v = Sse2.Subtract(c1, c2);
            var d1 = Sse2.Subtract(c0, c3);

            var m0 = Sse2.Add(a1, b1);
            var m1 = Sse2.Add(c1v, d1);
            var m2 = Sse2.Subtract(a1, b1);
            var m3 = Sse2.Subtract(d1, c1v);

            // m_c is column c of the row-pass intermediate (lane = row).
            // Stage 2 (columns in scalar): operate on tmp with stride 4 — cols of intermediate.
            // After transpose, t_r holds row r of intermediate (lane = col).
            Transpose4x4Sse2(m0, m1, m2, m3, out var t0, out var t1, out var t2, out var t3);

            // Scalar stage 2 reads tmp[0*4+i], tmp[3*4+i], tmp[1*4+i], tmp[2*4+i]
            // = row 0 col i, row 3 col i, row 1 col i, row 2 col i.
            // a1 = row0 + row3, b1 = row1 + row2, c1 = row1 - row2, d1 = row0 - row3
            var a1b = Sse2.Add(t0, t3);
            var b1b = Sse2.Add(t1, t2);
            var c1b = Sse2.Subtract(t1, t2);
            var d1b = Sse2.Subtract(t0, t3);

            var a2 = Sse2.Add(a1b, b1b);
            var b2 = Sse2.Add(c1b, d1b);
            var cc2 = Sse2.Subtract(a1b, b1b);
            var d2 = Sse2.Subtract(d1b, c1b);

            var three = Vector128.Create(3);
            var o0 = Sse2.ShiftRightArithmetic(Sse2.Add(a2, three), 3);
            var o1 = Sse2.ShiftRightArithmetic(Sse2.Add(b2, three), 3);
            var o2 = Sse2.ShiftRightArithmetic(Sse2.Add(cc2, three), 3);
            var o3 = Sse2.ShiftRightArithmetic(Sse2.Add(d2, three), 3);

            // o_c[r] = output[r, c]. In scalar: out[0*4+i]=a2_r, out[1*4+i]=b2_r, out[2*4+i]=c2_r, out[3*4+i]=d2_r.
            // i.e. row 0 = a2, row 1 = b2, row 2 = c2, row 3 = d2; lane index = column.
            Sse2.Store(output + 0, Sse2.PackSignedSaturate(o0, o1));
            Sse2.Store(output + 8, Sse2.PackSignedSaturate(o2, o3));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Transpose4x4Sse2(
            Vector128<int> r0, Vector128<int> r1, Vector128<int> r2, Vector128<int> r3,
            out Vector128<int> o0, out Vector128<int> o1, out Vector128<int> o2, out Vector128<int> o3)
        {
            var t01_lo = Sse2.UnpackLow(r0, r1);
            var t01_hi = Sse2.UnpackHigh(r0, r1);
            var t23_lo = Sse2.UnpackLow(r2, r3);
            var t23_hi = Sse2.UnpackHigh(r2, r3);

            o0 = Sse2.UnpackLow(t01_lo.AsInt64(), t23_lo.AsInt64()).AsInt32();
            o1 = Sse2.UnpackHigh(t01_lo.AsInt64(), t23_lo.AsInt64()).AsInt32();
            o2 = Sse2.UnpackLow(t01_hi.AsInt64(), t23_hi.AsInt64()).AsInt32();
            o3 = Sse2.UnpackHigh(t01_hi.AsInt64(), t23_hi.AsInt64()).AsInt32();
        }

        // ===============================================================
        // AdvSimd paths
        // ===============================================================

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Walsh4x4AdvSimd(short* input, short* output, int pitch)
        {
            int stride = pitch / 2;

            var r0 = AdvSimd.SignExtendWideningLower(AdvSimd.LoadVector64(input + 0 * stride));
            var r1 = AdvSimd.SignExtendWideningLower(AdvSimd.LoadVector64(input + 1 * stride));
            var r2 = AdvSimd.SignExtendWideningLower(AdvSimd.LoadVector64(input + 2 * stride));
            var r3 = AdvSimd.SignExtendWideningLower(AdvSimd.LoadVector64(input + 3 * stride));

            Transpose4x4AdvSimd(r0, r1, r2, r3, out var c0, out var c1, out var c2, out var c3);

            // No (Vector128<int>, byte) ShiftLeftLogical overload on AdvSimd —
            // multiply by 4 keeps the arithmetic in int32 lanes.
            var four = Vector128.Create(4);
            var a1 = AdvSimd.Multiply(AdvSimd.Add(c0, c2), four);
            var d1 = AdvSimd.Multiply(AdvSimd.Add(c1, c3), four);
            var c1v = AdvSimd.Multiply(AdvSimd.Subtract(c1, c3), four);
            var b1 = AdvSimd.Multiply(AdvSimd.Subtract(c0, c2), four);

            // m0 = a1 + d1 + (a1 != 0 ? 1 : 0).
            // (a1 != 0 ? 1 : 0) computed as (((-a1)|a1) >> 31 logical) — sign bit
            // of the bitwise-or with negation is set iff a1 != 0.
            var a1OrNeg = AdvSimd.Or(a1, AdvSimd.Negate(a1));
            var a1NonZero = AdvSimd.ShiftRightLogical(a1OrNeg.AsUInt32(), 31).AsInt32();
            var m0 = AdvSimd.Add(AdvSimd.Add(a1, d1), a1NonZero);

            var m1 = AdvSimd.Add(b1, c1v);
            var m2 = AdvSimd.Subtract(b1, c1v);
            var m3 = AdvSimd.Subtract(a1, d1);

            Transpose4x4AdvSimd(m0, m1, m2, m3, out var t0, out var t1, out var t2, out var t3);

            var a1c = AdvSimd.Add(t0, t2);
            var d1c = AdvSimd.Add(t1, t3);
            var c1c = AdvSimd.Subtract(t1, t3);
            var b1c = AdvSimd.Subtract(t0, t2);

            var a2 = AdvSimd.Add(a1c, d1c);
            var b2 = AdvSimd.Add(b1c, c1c);
            var cc2 = AdvSimd.Subtract(b1c, c1c);
            var d2 = AdvSimd.Subtract(a1c, d1c);

            // x += (x < 0) ? 1 : 0 — sign-bit-as-int via logical right shift by 31.
            a2 = AdvSimd.Add(a2, AdvSimd.ShiftRightLogical(a2.AsUInt32(), 31).AsInt32());
            b2 = AdvSimd.Add(b2, AdvSimd.ShiftRightLogical(b2.AsUInt32(), 31).AsInt32());
            cc2 = AdvSimd.Add(cc2, AdvSimd.ShiftRightLogical(cc2.AsUInt32(), 31).AsInt32());
            d2 = AdvSimd.Add(d2, AdvSimd.ShiftRightLogical(d2.AsUInt32(), 31).AsInt32());

            var three = Vector128.Create(3);
            var o0 = AdvSimd.ShiftRightArithmetic(AdvSimd.Add(a2, three), 3);
            var o1 = AdvSimd.ShiftRightArithmetic(AdvSimd.Add(b2, three), 3);
            var o2 = AdvSimd.ShiftRightArithmetic(AdvSimd.Add(cc2, three), 3);
            var o3 = AdvSimd.ShiftRightArithmetic(AdvSimd.Add(d2, three), 3);

            var s01 = AdvSimd.ExtractNarrowingSaturateUpper(AdvSimd.ExtractNarrowingSaturateLower(o0), o1);
            var s23 = AdvSimd.ExtractNarrowingSaturateUpper(AdvSimd.ExtractNarrowingSaturateLower(o2), o3);
            AdvSimd.Store(output + 0, s01);
            AdvSimd.Store(output + 8, s23);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void InverseWalsh4x4AdvSimd(short* input, short* output)
        {
            var r0 = AdvSimd.SignExtendWideningLower(AdvSimd.LoadVector64(input + 0));
            var r1 = AdvSimd.SignExtendWideningLower(AdvSimd.LoadVector64(input + 4));
            var r2 = AdvSimd.SignExtendWideningLower(AdvSimd.LoadVector64(input + 8));
            var r3 = AdvSimd.SignExtendWideningLower(AdvSimd.LoadVector64(input + 12));

            Transpose4x4AdvSimd(r0, r1, r2, r3, out var c0, out var c1, out var c2, out var c3);

            var a1 = AdvSimd.Add(c0, c3);
            var b1 = AdvSimd.Add(c1, c2);
            var c1v = AdvSimd.Subtract(c1, c2);
            var d1 = AdvSimd.Subtract(c0, c3);

            var m0 = AdvSimd.Add(a1, b1);
            var m1 = AdvSimd.Add(c1v, d1);
            var m2 = AdvSimd.Subtract(a1, b1);
            var m3 = AdvSimd.Subtract(d1, c1v);

            Transpose4x4AdvSimd(m0, m1, m2, m3, out var t0, out var t1, out var t2, out var t3);

            var a1b = AdvSimd.Add(t0, t3);
            var b1b = AdvSimd.Add(t1, t2);
            var c1b = AdvSimd.Subtract(t1, t2);
            var d1b = AdvSimd.Subtract(t0, t3);

            var a2 = AdvSimd.Add(a1b, b1b);
            var b2 = AdvSimd.Add(c1b, d1b);
            var cc2 = AdvSimd.Subtract(a1b, b1b);
            var d2 = AdvSimd.Subtract(d1b, c1b);

            var three = Vector128.Create(3);
            var o0 = AdvSimd.ShiftRightArithmetic(AdvSimd.Add(a2, three), 3);
            var o1 = AdvSimd.ShiftRightArithmetic(AdvSimd.Add(b2, three), 3);
            var o2 = AdvSimd.ShiftRightArithmetic(AdvSimd.Add(cc2, three), 3);
            var o3 = AdvSimd.ShiftRightArithmetic(AdvSimd.Add(d2, three), 3);

            var s01 = AdvSimd.ExtractNarrowingSaturateUpper(AdvSimd.ExtractNarrowingSaturateLower(o0), o1);
            var s23 = AdvSimd.ExtractNarrowingSaturateUpper(AdvSimd.ExtractNarrowingSaturateLower(o2), o3);
            AdvSimd.Store(output + 0, s01);
            AdvSimd.Store(output + 8, s23);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Transpose4x4AdvSimd(
            Vector128<int> r0, Vector128<int> r1, Vector128<int> r2, Vector128<int> r3,
            out Vector128<int> o0, out Vector128<int> o1, out Vector128<int> o2, out Vector128<int> o3)
        {
            var t01_lo = AdvSimd.Arm64.ZipLow(r0, r1);
            var t01_hi = AdvSimd.Arm64.ZipHigh(r0, r1);
            var t23_lo = AdvSimd.Arm64.ZipLow(r2, r3);
            var t23_hi = AdvSimd.Arm64.ZipHigh(r2, r3);

            o0 = AdvSimd.Arm64.ZipLow(t01_lo.AsInt64(), t23_lo.AsInt64()).AsInt32();
            o1 = AdvSimd.Arm64.ZipHigh(t01_lo.AsInt64(), t23_lo.AsInt64()).AsInt32();
            o2 = AdvSimd.Arm64.ZipLow(t01_hi.AsInt64(), t23_hi.AsInt64()).AsInt32();
            o3 = AdvSimd.Arm64.ZipHigh(t01_hi.AsInt64(), t23_hi.AsInt64()).AsInt32();
        }

        // ===============================================================
        // Scalar fallback for InverseWalsh (used when no SIMD available).
        // Bit-exact with mb_encoder.InverseWalsh4x4Into.
        // ===============================================================

        private static void InverseWalsh4x4Scalar(short* input, short* output)
        {
            int* tmp = stackalloc int[16];
            int a1, b1, c1v, d1, a2, b2, c2, d2;

            for (int i = 0; i < 4; i++)
            {
                a1 = input[i * 4 + 0] + input[i * 4 + 3];
                b1 = input[i * 4 + 1] + input[i * 4 + 2];
                c1v = input[i * 4 + 1] - input[i * 4 + 2];
                d1 = input[i * 4 + 0] - input[i * 4 + 3];

                tmp[i * 4 + 0] = a1 + b1;
                tmp[i * 4 + 1] = c1v + d1;
                tmp[i * 4 + 2] = a1 - b1;
                tmp[i * 4 + 3] = d1 - c1v;
            }

            for (int i = 0; i < 4; i++)
            {
                a1 = tmp[0 * 4 + i] + tmp[3 * 4 + i];
                b1 = tmp[1 * 4 + i] + tmp[2 * 4 + i];
                c1v = tmp[1 * 4 + i] - tmp[2 * 4 + i];
                d1 = tmp[0 * 4 + i] - tmp[3 * 4 + i];

                a2 = a1 + b1;
                b2 = c1v + d1;
                c2 = a1 - b1;
                d2 = d1 - c1v;

                output[0 * 4 + i] = (short)((a2 + 3) >> 3);
                output[1 * 4 + i] = (short)((b2 + 3) >> 3);
                output[2 * 4 + i] = (short)((c2 + 3) >> 3);
                output[3 * 4 + i] = (short)((d2 + 3) >> 3);
            }
        }
    }
}
