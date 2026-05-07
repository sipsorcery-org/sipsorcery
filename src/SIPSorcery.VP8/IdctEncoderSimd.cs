//-----------------------------------------------------------------------------
// Encoder-side SIMD fast path for the 4x4 inverse DCT + flat or per-pixel
// prediction add + [0,255] clamp + strided byte store. Bit-exact with
// idctllm.vp8_short_idct4x4llm_c followed by a scalar add-and-clamp.
//
// The decoder keeps using vp8_short_idct4x4llm_c. Only the encoder's
// reconstruct loop (~24 calls per macroblock) opts into this path.
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
    internal static unsafe class IdctEncoderSimd
    {
        private const int cospi8sqrt2minus1 = 20091;
        private const int sinpi8sqrt2 = 35468;

        /// <summary>True when one of the SIMD paths is available and will produce
        /// bit-identical results to the scalar reference. Callers can avoid the
        /// 4x4 stack pred buffer when this is true.</summary>
        public static bool IsSupported => Sse41.IsSupported || AdvSimd.Arm64.IsSupported;

        /// <summary>4x4 inverse DCT + flat-byte prediction add + clamp + store.
        /// Equivalent to <see cref="idctllm.vp8_short_idct4x4llm_c"/> with a
        /// pred buffer of all <paramref name="pred"/>. Used by DC_PRED / TM_PRED-style
        /// modes where the prediction is uniform over the 4x4.</summary>
        public static void Idct4x4AddFlat(short* input, byte pred, byte* dst, int dstStride)
        {
            if (Sse41.IsSupported)
            {
                Idct4x4AddSse41(input, predFlat: pred, predPlane: null, predStride: 0, dst, dstStride);
                return;
            }
            if (AdvSimd.Arm64.IsSupported)
            {
                Idct4x4AddAdvSimd(input, predFlat: pred, predPlane: null, predStride: 0, dst, dstStride);
                return;
            }

            ScalarFallback(input, pred, predPlane: null, predStride: 0, dst, dstStride);
        }

        /// <summary>4x4 inverse DCT + per-pixel prediction add + clamp + store.</summary>
        public static void Idct4x4AddBlock(short* input, byte* pred, int predStride, byte* dst, int dstStride)
        {
            if (Sse41.IsSupported)
            {
                Idct4x4AddSse41(input, predFlat: 0, predPlane: pred, predStride, dst, dstStride);
                return;
            }
            if (AdvSimd.Arm64.IsSupported)
            {
                Idct4x4AddAdvSimd(input, predFlat: 0, predPlane: pred, predStride, dst, dstStride);
                return;
            }

            ScalarFallback(input, predFlat: 0, predPlane: pred, predStride, dst, dstStride);
        }

        // ---------------------------------------------------------------------
        // SSE4.1 - true 4-way 4x4 inverse DCT + add + clamp + store
        // ---------------------------------------------------------------------

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Idct4x4AddSse41(short* input, byte predFlat, byte* predPlane, int predStride, byte* dst, int dstStride)
        {
            // Load 4 rows of input (4 shorts each in lo 4 int16 lanes), widen to int32.
            var r0 = Sse41.ConvertToVector128Int32(Sse2.LoadScalarVector128((double*)(input + 0)).AsInt16());
            var r1 = Sse41.ConvertToVector128Int32(Sse2.LoadScalarVector128((double*)(input + 4)).AsInt16());
            var r2 = Sse41.ConvertToVector128Int32(Sse2.LoadScalarVector128((double*)(input + 8)).AsInt16());
            var r3 = Sse41.ConvertToVector128Int32(Sse2.LoadScalarVector128((double*)(input + 12)).AsInt16());

            // First pass: lane-wise across rows (each lane = column of input).
            // op[shortpitch * 0] = a1 + d1 -> mid row 0
            // op[shortpitch * 1] = b1 + c1 -> mid row 1
            // op[shortpitch * 2] = b1 - c1 -> mid row 2
            // op[shortpitch * 3] = a1 - d1 -> mid row 3
            IdctPassSse41(r0, r1, r2, r3, addRound: 0, shift: 0,
                          out var m0, out var m1, out var m2, out var m3);

            // Transpose 4x4 (int32 form): row-vectors -> column-vectors (lane = row).
            Transpose4x4Sse2(m0, m1, m2, m3, out var t0, out var t1, out var t2, out var t3);

            // Second pass: lane-wise across what are now intermediate rows (each lane = row of intermediate).
            // op[c] = (... + 4) >> 3.
            IdctPassSse41(t0, t1, t2, t3, addRound: 4, shift: 3,
                          out var oc0, out var oc1, out var oc2, out var oc3);

            // Transpose back: column-vectors -> row-vectors (lane = column).
            Transpose4x4Sse2(oc0, oc1, oc2, oc3, out var or0, out var or1, out var or2, out var or3);

            // Load prediction as 4 int32 rows and add.
            Vector128<int> p0, p1, p2, p3;
            if (predPlane == null)
            {
                var pf = Vector128.Create((int)predFlat);
                p0 = pf; p1 = pf; p2 = pf; p3 = pf;
            }
            else
            {
                p0 = LoadPredRowSse41(predPlane + 0 * predStride);
                p1 = LoadPredRowSse41(predPlane + 1 * predStride);
                p2 = LoadPredRowSse41(predPlane + 2 * predStride);
                p3 = LoadPredRowSse41(predPlane + 3 * predStride);
            }

            or0 = Sse2.Add(or0, p0);
            or1 = Sse2.Add(or1, p1);
            or2 = Sse2.Add(or2, p2);
            or3 = Sse2.Add(or3, p3);

            // Pack int32 -> int16 -> uint8 with saturation = clamp to [0, 255].
            // Pair rows for fewer packs: r01 = [r0|r1] in int16, then PackUnsignedSaturate to uint8.
            var s01 = Sse2.PackSignedSaturate(or0, or1);   // 8 int16 = [or0 (4), or1 (4)]
            var s23 = Sse2.PackSignedSaturate(or2, or3);
            var b0123 = Sse2.PackUnsignedSaturate(s01, s23);  // 16 uint8 = rows 0,1,2,3 (4 bytes each)

            // Store 4 bytes per row at stride. b0123 lanes 0..3 = row 0, 4..7 = row 1, 8..11 = row 2, 12..15 = row 3.
            // 4 byte stores; on M1 fewer instructions vs scalar copy.
            *(int*)(dst + 0 * dstStride) = b0123.AsInt32().GetElement(0);
            *(int*)(dst + 1 * dstStride) = b0123.AsInt32().GetElement(1);
            *(int*)(dst + 2 * dstStride) = b0123.AsInt32().GetElement(2);
            *(int*)(dst + 3 * dstStride) = b0123.AsInt32().GetElement(3);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector128<int> LoadPredRowSse41(byte* row)
        {
            // Load 4 bytes -> Vector128<int> with one byte sign-extended (zero-extended) per lane.
            // SSE4.1: Pmovzxbd
            int packed = *(int*)row;
            return Sse41.ConvertToVector128Int32(Vector128.CreateScalar(packed).AsByte());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void IdctPassSse41(
            Vector128<int> r0, Vector128<int> r1, Vector128<int> r2, Vector128<int> r3,
            int addRound, int shift,
            out Vector128<int> o0, out Vector128<int> o1, out Vector128<int> o2, out Vector128<int> o3)
        {
            var k_sin = Vector128.Create(sinpi8sqrt2);
            var k_cosm1 = Vector128.Create(cospi8sqrt2minus1);

            var a1 = Sse2.Add(r0, r2);
            var b1 = Sse2.Subtract(r0, r2);

            var t1a = Sse2.ShiftRightArithmetic(Sse41.MultiplyLow(r1, k_sin), 16);
            var t2a = Sse2.Add(r3, Sse2.ShiftRightArithmetic(Sse41.MultiplyLow(r3, k_cosm1), 16));
            var c1 = Sse2.Subtract(t1a, t2a);

            var t1b = Sse2.Add(r1, Sse2.ShiftRightArithmetic(Sse41.MultiplyLow(r1, k_cosm1), 16));
            var t2b = Sse2.ShiftRightArithmetic(Sse41.MultiplyLow(r3, k_sin), 16);
            var d1 = Sse2.Add(t1b, t2b);

            if (shift == 0)
            {
                o0 = Sse2.Add(a1, d1);
                o1 = Sse2.Add(b1, c1);
                o2 = Sse2.Subtract(b1, c1);
                o3 = Sse2.Subtract(a1, d1);
            }
            else
            {
                var round = Vector128.Create(addRound);
                byte sh = (byte)shift;
                o0 = Sse2.ShiftRightArithmetic(Sse2.Add(Sse2.Add(a1, d1), round), sh);
                o1 = Sse2.ShiftRightArithmetic(Sse2.Add(Sse2.Add(b1, c1), round), sh);
                o2 = Sse2.ShiftRightArithmetic(Sse2.Add(Sse2.Subtract(b1, c1), round), sh);
                o3 = Sse2.ShiftRightArithmetic(Sse2.Add(Sse2.Subtract(a1, d1), round), sh);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Transpose4x4Sse2(
            Vector128<int> r0, Vector128<int> r1, Vector128<int> r2, Vector128<int> r3,
            out Vector128<int> o0, out Vector128<int> o1, out Vector128<int> o2, out Vector128<int> o3)
        {
            var t01_lo = Sse2.UnpackLow(r0, r1);    // [r0[0], r1[0], r0[1], r1[1]]
            var t01_hi = Sse2.UnpackHigh(r0, r1);   // [r0[2], r1[2], r0[3], r1[3]]
            var t23_lo = Sse2.UnpackLow(r2, r3);
            var t23_hi = Sse2.UnpackHigh(r2, r3);

            o0 = Sse2.UnpackLow(t01_lo.AsInt64(), t23_lo.AsInt64()).AsInt32();    // col 0 = [r0[0], r1[0], r2[0], r3[0]]
            o1 = Sse2.UnpackHigh(t01_lo.AsInt64(), t23_lo.AsInt64()).AsInt32();   // col 1
            o2 = Sse2.UnpackLow(t01_hi.AsInt64(), t23_hi.AsInt64()).AsInt32();    // col 2
            o3 = Sse2.UnpackHigh(t01_hi.AsInt64(), t23_hi.AsInt64()).AsInt32();   // col 3
        }

        // ---------------------------------------------------------------------
        // ARM AdvSimd
        // ---------------------------------------------------------------------

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Idct4x4AddAdvSimd(short* input, byte predFlat, byte* predPlane, int predStride, byte* dst, int dstStride)
        {
            var r0 = AdvSimd.SignExtendWideningLower(AdvSimd.LoadVector64(input + 0));
            var r1 = AdvSimd.SignExtendWideningLower(AdvSimd.LoadVector64(input + 4));
            var r2 = AdvSimd.SignExtendWideningLower(AdvSimd.LoadVector64(input + 8));
            var r3 = AdvSimd.SignExtendWideningLower(AdvSimd.LoadVector64(input + 12));

            IdctPassAdvSimd(r0, r1, r2, r3, addRound: 0, shift: 0,
                            out var m0, out var m1, out var m2, out var m3);

            Transpose4x4AdvSimd(m0, m1, m2, m3, out var t0, out var t1, out var t2, out var t3);

            IdctPassAdvSimd(t0, t1, t2, t3, addRound: 4, shift: 3,
                            out var oc0, out var oc1, out var oc2, out var oc3);

            Transpose4x4AdvSimd(oc0, oc1, oc2, oc3, out var or0, out var or1, out var or2, out var or3);

            Vector128<int> p0, p1, p2, p3;
            if (predPlane == null)
            {
                var pf = Vector128.Create((int)predFlat);
                p0 = pf; p1 = pf; p2 = pf; p3 = pf;
            }
            else
            {
                p0 = LoadPredRowAdvSimd(predPlane + 0 * predStride);
                p1 = LoadPredRowAdvSimd(predPlane + 1 * predStride);
                p2 = LoadPredRowAdvSimd(predPlane + 2 * predStride);
                p3 = LoadPredRowAdvSimd(predPlane + 3 * predStride);
            }

            or0 = AdvSimd.Add(or0, p0);
            or1 = AdvSimd.Add(or1, p1);
            or2 = AdvSimd.Add(or2, p2);
            or3 = AdvSimd.Add(or3, p3);

            // Saturating narrow int32 -> uint8: int32 -> int16 with signed-saturation,
            // then int16 -> uint8 with unsigned-saturation = clamp [0, 255].
            var s0 = AdvSimd.ExtractNarrowingSaturateLower(or0);   // 4 int16
            var s1 = AdvSimd.ExtractNarrowingSaturateLower(or1);
            var s2 = AdvSimd.ExtractNarrowingSaturateLower(or2);
            var s3 = AdvSimd.ExtractNarrowingSaturateLower(or3);

            var s01 = Vector128.Create(s0, s1);   // 8 int16 = rows 0,1
            var s23 = Vector128.Create(s2, s3);   // 8 int16 = rows 2,3

            var b01 = AdvSimd.ExtractNarrowingSaturateUnsignedLower(s01);   // 8 uint8 = rows 0,1
            var b23 = AdvSimd.ExtractNarrowingSaturateUnsignedLower(s23);

            // Store 4 bytes per row.
            *(int*)(dst + 0 * dstStride) = b01.AsInt32().GetElement(0);
            *(int*)(dst + 1 * dstStride) = b01.AsInt32().GetElement(1);
            *(int*)(dst + 2 * dstStride) = b23.AsInt32().GetElement(0);
            *(int*)(dst + 3 * dstStride) = b23.AsInt32().GetElement(1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector128<int> LoadPredRowAdvSimd(byte* row)
        {
            int packed = *(int*)row;
            var asBytes = Vector64.CreateScalar(packed).AsByte();
            // Zero-extend 4 bytes -> 4 int16 -> 4 int32.
            var asShorts = AdvSimd.ZeroExtendWideningLower(asBytes);
            return AdvSimd.ZeroExtendWideningLower(asShorts.GetLower()).AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void IdctPassAdvSimd(
            Vector128<int> r0, Vector128<int> r1, Vector128<int> r2, Vector128<int> r3,
            int addRound, int shift,
            out Vector128<int> o0, out Vector128<int> o1, out Vector128<int> o2, out Vector128<int> o3)
        {
            var k_sin = Vector128.Create(sinpi8sqrt2);
            var k_cosm1 = Vector128.Create(cospi8sqrt2minus1);

            var a1 = AdvSimd.Add(r0, r2);
            var b1 = AdvSimd.Subtract(r0, r2);

            var t1a = AdvSimd.ShiftRightArithmetic(AdvSimd.Multiply(r1, k_sin), 16);
            var t2a = AdvSimd.Add(r3, AdvSimd.ShiftRightArithmetic(AdvSimd.Multiply(r3, k_cosm1), 16));
            var c1 = AdvSimd.Subtract(t1a, t2a);

            var t1b = AdvSimd.Add(r1, AdvSimd.ShiftRightArithmetic(AdvSimd.Multiply(r1, k_cosm1), 16));
            var t2b = AdvSimd.ShiftRightArithmetic(AdvSimd.Multiply(r3, k_sin), 16);
            var d1 = AdvSimd.Add(t1b, t2b);

            if (shift == 0)
            {
                o0 = AdvSimd.Add(a1, d1);
                o1 = AdvSimd.Add(b1, c1);
                o2 = AdvSimd.Subtract(b1, c1);
                o3 = AdvSimd.Subtract(a1, d1);
            }
            else
            {
                var round = Vector128.Create(addRound);
                o0 = AdvSimd.ShiftRightArithmetic(AdvSimd.Add(AdvSimd.Add(a1, d1), round), (byte)shift);
                o1 = AdvSimd.ShiftRightArithmetic(AdvSimd.Add(AdvSimd.Add(b1, c1), round), (byte)shift);
                o2 = AdvSimd.ShiftRightArithmetic(AdvSimd.Add(AdvSimd.Subtract(b1, c1), round), (byte)shift);
                o3 = AdvSimd.ShiftRightArithmetic(AdvSimd.Add(AdvSimd.Subtract(a1, d1), round), (byte)shift);
            }
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

        // ---------------------------------------------------------------------
        // Scalar fallback (matches idctllm.vp8_short_idct4x4llm_c + add + clamp).
        // ---------------------------------------------------------------------

        private static void ScalarFallback(short* input, byte predFlat, byte* predPlane, int predStride, byte* dst, int dstStride)
        {
            byte* predBuf = stackalloc byte[16];
            if (predPlane == null)
            {
                for (int i = 0; i < 16; i++) predBuf[i] = predFlat;
            }
            else
            {
                for (int r = 0; r < 4; r++)
                for (int c = 0; c < 4; c++)
                    predBuf[r * 4 + c] = predPlane[r * predStride + c];
            }

            byte* dstBuf = stackalloc byte[16];
            idctllm.vp8_short_idct4x4llm_c(input, predBuf, 4, dstBuf, 4);

            for (int r = 0; r < 4; r++)
            for (int c = 0; c < 4; c++)
                dst[r * dstStride + c] = dstBuf[r * 4 + c];
        }
    }
}
