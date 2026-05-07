//-----------------------------------------------------------------------------
// Filename: FdctEncoderSimd.cs
//
// Description: Encoder-only SIMD implementations of the VP8 4x4 and batched
// 8x4 forward DCT. These are bit-exact with the scalar reference in dct.cs
// (vp8_short_fdct4x4_c) and are dispatched from the Optimized pipeline only;
// the Legacy pipeline must continue to call dct.vp8_short_fdct4x4_c which
// remains a pure scalar implementation.
//
// SIMD: x86 SSE4.1 path (true 4-way 4x4 fdct with two-stage in-register
// transposes; 8x4 form processes both 4x4 halves of an 8x4 strip in one
// shot using the full 128-bit width). ARM AdvSimd path mirrors the same
// shape using Zip/Unzip primitives. Sse41 is used for PMULLD; Sse2-only
// hosts fall back to scalar.
//
// Numeric assumption: int16 inputs/outputs throughout; intermediate
// q-pair sums and differences are widened to int32 to keep parity with
// the scalar reference for residual magnitudes near 512.
//
// Author(s):
// Claude Opus 4.7 (Anthropic AI assistant, model: claude-opus-4-7), commissioned by Aaron Clauson
//
// History:
// 05 May 2026  Claude          Extracted from dct.cs so the scalar reference
//                              is unconditionally used by the Legacy pipeline.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

namespace Vpx.Net
{
    internal static unsafe class FdctEncoderSimd
    {
        /// <summary>True when a SIMD FDCT path is available on the current host.
        /// Sse41 is required because the column pass uses PMULLD via
        /// <see cref="Sse41.MultiplyLow(Vector128{int}, Vector128{int})"/>.</summary>
        public static bool IsSupported => Sse41.IsSupported || AdvSimd.Arm64.IsSupported;

        /// <summary>SIMD 4x4 forward DCT. Bit-exact with
        /// <see cref="dct.vp8_short_fdct4x4_c"/>. Falls back to the scalar
        /// reference when no SIMD path is available.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Fdct4x4(short* input, short* output, int pitch)
        {
            if (Sse41.IsSupported)
            {
                Fdct4x4Sse2(input, output, pitch);
                return;
            }
            if (AdvSimd.Arm64.IsSupported)
            {
                Fdct4x4AdvSimd(input, output, pitch);
                return;
            }

            dct.vp8_short_fdct4x4_c(input, output, pitch);
        }

        /// <summary>SIMD batched 8x4 forward DCT. Writes the LEFT 4x4
        /// (columns 0..3) into <paramref name="outLeft"/> (16 shorts) and the
        /// RIGHT 4x4 (columns 4..7) into <paramref name="outRight"/> (16
        /// shorts). Bit-exact with two side-by-side
        /// <see cref="dct.vp8_short_fdct4x4_c"/> calls.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Fdct8x4Split(short* input, short* outLeft, short* outRight, int pitch)
        {
            if (Sse41.IsSupported)
            {
                Fdct8x4Sse2(input, outLeft, outRight, pitch);
                return;
            }
            if (AdvSimd.Arm64.IsSupported)
            {
                Fdct8x4AdvSimd(input, outLeft, outRight, pitch);
                return;
            }

            dct.vp8_short_fdct4x4_c(input, outLeft, pitch);
            dct.vp8_short_fdct4x4_c(input + 4, outRight, pitch);
        }

        // ---------------------------------------------------------------------
        // SSE2/SSE4.1 - true 4-way 4x4 fdct
        // ---------------------------------------------------------------------

        /// <summary>SSE2/SSE4.1 4x4 forward DCT. Loads four rows, transposes,
        /// does the row-pass arithmetic with all four rows in parallel (lo 4
        /// lanes used), transposes again, runs the column pass in parallel,
        /// then writes the row-major 16-short output.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Fdct4x4Sse2(short* input, short* output, int pitch)
        {
            int stride = pitch / 2;

            var r0 = Sse2.LoadScalarVector128((double*)(input + 0 * stride)).AsInt16();
            var r1 = Sse2.LoadScalarVector128((double*)(input + 1 * stride)).AsInt16();
            var r2 = Sse2.LoadScalarVector128((double*)(input + 2 * stride)).AsInt16();
            var r3 = Sse2.LoadScalarVector128((double*)(input + 3 * stride)).AsInt16();

            r0 = Sse2.ShiftLeftLogical(r0, 3);
            r1 = Sse2.ShiftLeftLogical(r1, 3);
            r2 = Sse2.ShiftLeftLogical(r2, 3);
            r3 = Sse2.ShiftLeftLogical(r3, 3);

            var ab = Sse2.UnpackLow(r0, r1);
            var cd = Sse2.UnpackLow(r2, r3);
            var c01 = Sse2.UnpackLow(ab.AsInt32(), cd.AsInt32()).AsInt16();
            var c23 = Sse2.UnpackHigh(ab.AsInt32(), cd.AsInt32()).AsInt16();

            var c0 = c01;
            var c1v = Sse2.UnpackHigh(c01.AsInt64(), Vector128<long>.Zero).AsInt16();
            var c2 = c23;
            var c3v = Sse2.UnpackHigh(c23.AsInt64(), Vector128<long>.Zero).AsInt16();

            var a1 = Sse2.Add(c0, c3v);
            var b1 = Sse2.Add(c1v, c2);
            var c1d = Sse2.Subtract(c1v, c2);
            var d1 = Sse2.Subtract(c0, c3v);

            var res0 = Sse2.Add(a1, b1);
            var res2 = Sse2.Subtract(a1, b1);

            var cdInter = Sse2.UnpackLow(c1d, d1);
            var k_2217_5352 = Vector128.Create((short)2217, 5352, 2217, 5352, 2217, 5352, 2217, 5352);
            var k_neg5352_2217 = Vector128.Create((short)-5352, 2217, -5352, 2217, -5352, 2217, -5352, 2217);

            var res1Int = Sse2.MultiplyAddAdjacent(cdInter, k_2217_5352);
            res1Int = Sse2.Add(res1Int, Vector128.Create(14500));
            res1Int = Sse2.ShiftRightArithmetic(res1Int, 12);

            var res3Int = Sse2.MultiplyAddAdjacent(cdInter, k_neg5352_2217);
            res3Int = Sse2.Add(res3Int, Vector128.Create(7500));
            res3Int = Sse2.ShiftRightArithmetic(res3Int, 12);

            var packed13 = Sse2.PackSignedSaturate(res1Int, res3Int);
            var res1 = packed13;
            var res3 = Sse2.UnpackHigh(packed13.AsInt64(), Vector128<long>.Zero).AsInt16();

            var ab2 = Sse2.UnpackLow(res0, res1);
            var cd2 = Sse2.UnpackLow(res2, res3);
            var q01 = Sse2.UnpackLow(ab2.AsInt32(), cd2.AsInt32()).AsInt16();
            var q23 = Sse2.UnpackHigh(ab2.AsInt32(), cd2.AsInt32()).AsInt16();

            // q-pair sums/differences can exceed int16 when |residual| ≈ 512;
            // widen to int32 to keep bit-exactness with the scalar reference.
            var q0 = Sse2.ShiftRightArithmetic(Sse2.UnpackLow(q01, q01).AsInt32(), 16);
            var q1 = Sse2.ShiftRightArithmetic(Sse2.UnpackHigh(q01, q01).AsInt32(), 16);
            var q2 = Sse2.ShiftRightArithmetic(Sse2.UnpackLow(q23, q23).AsInt32(), 16);
            var q3 = Sse2.ShiftRightArithmetic(Sse2.UnpackHigh(q23, q23).AsInt32(), 16);

            var a1c = Sse2.Add(q0, q3);
            var b1c = Sse2.Add(q1, q2);
            var c1c = Sse2.Subtract(q1, q2);
            var d1c = Sse2.Subtract(q0, q3);

            var sevenI32 = Vector128.Create(7);
            var f0Int = Sse2.ShiftRightArithmetic(Sse2.Add(Sse2.Add(a1c, b1c), sevenI32), 4);
            var f2Int = Sse2.ShiftRightArithmetic(Sse2.Add(Sse2.Subtract(a1c, b1c), sevenI32), 4);

            var k2217i = Vector128.Create(2217);
            var k5352i = Vector128.Create(5352);

            var f1Int = Sse2.Add(Sse41.MultiplyLow(c1c, k2217i), Sse41.MultiplyLow(d1c, k5352i));
            f1Int = Sse2.ShiftRightArithmetic(Sse2.Add(f1Int, Vector128.Create(12000)), 16);
            var d1cZero = Sse2.CompareEqual(d1c, Vector128<int>.Zero);
            f1Int = Sse2.Add(f1Int, Sse2.AndNot(d1cZero, Vector128.Create(1)));

            var f3Int = Sse2.Subtract(Sse41.MultiplyLow(d1c, k2217i), Sse41.MultiplyLow(c1c, k5352i));
            f3Int = Sse2.ShiftRightArithmetic(Sse2.Add(f3Int, Vector128.Create(51000)), 16);

            // Output values fit in int16 by construction; saturation is a no-op.
            Sse2.Store(output + 0, Sse2.PackSignedSaturate(f0Int, f1Int));
            Sse2.Store(output + 8, Sse2.PackSignedSaturate(f2Int, f3Int));
        }

        /// <summary>SSE2/SSE4.1 batched 8x4 forward DCT - processes the LEFT
        /// and RIGHT 4x4 sub-blocks of an 8x4 input strip in one shot, using
        /// all 8 lanes of each Vector128.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Fdct8x4Sse2(short* input, short* outLeft, short* outRight, int pitch)
        {
            int stride = pitch / 2;

            var r0 = Sse2.LoadVector128(input + 0 * stride);
            var r1 = Sse2.LoadVector128(input + 1 * stride);
            var r2 = Sse2.LoadVector128(input + 2 * stride);
            var r3 = Sse2.LoadVector128(input + 3 * stride);

            r0 = Sse2.ShiftLeftLogical(r0, 3);
            r1 = Sse2.ShiftLeftLogical(r1, 3);
            r2 = Sse2.ShiftLeftLogical(r2, 3);
            r3 = Sse2.ShiftLeftLogical(r3, 3);

            var abL = Sse2.UnpackLow(r0, r1);
            var abR = Sse2.UnpackHigh(r0, r1);
            var cdL = Sse2.UnpackLow(r2, r3);
            var cdR = Sse2.UnpackHigh(r2, r3);
            var lL01 = Sse2.UnpackLow(abL.AsInt32(), cdL.AsInt32()).AsInt16();
            var lL23 = Sse2.UnpackHigh(abL.AsInt32(), cdL.AsInt32()).AsInt16();
            var lR01 = Sse2.UnpackLow(abR.AsInt32(), cdR.AsInt32()).AsInt16();
            var lR23 = Sse2.UnpackHigh(abR.AsInt32(), cdR.AsInt32()).AsInt16();
            var c0 = Sse2.UnpackLow(lL01.AsInt64(), lR01.AsInt64()).AsInt16();
            var c1 = Sse2.UnpackHigh(lL01.AsInt64(), lR01.AsInt64()).AsInt16();
            var c2 = Sse2.UnpackLow(lL23.AsInt64(), lR23.AsInt64()).AsInt16();
            var c3 = Sse2.UnpackHigh(lL23.AsInt64(), lR23.AsInt64()).AsInt16();

            var a1 = Sse2.Add(c0, c3);
            var b1 = Sse2.Add(c1, c2);
            var c1d = Sse2.Subtract(c1, c2);
            var d1 = Sse2.Subtract(c0, c3);

            var res0 = Sse2.Add(a1, b1);
            var res2 = Sse2.Subtract(a1, b1);

            var k_2217_5352 = Vector128.Create((short)2217, 5352, 2217, 5352, 2217, 5352, 2217, 5352);
            var k_neg5352_2217 = Vector128.Create((short)-5352, 2217, -5352, 2217, -5352, 2217, -5352, 2217);

            var cdLo = Sse2.UnpackLow(c1d, d1);
            var cdHi = Sse2.UnpackHigh(c1d, d1);

            var res1Lo = Sse2.MultiplyAddAdjacent(cdLo, k_2217_5352);
            var res1Hi = Sse2.MultiplyAddAdjacent(cdHi, k_2217_5352);
            var c14500 = Vector128.Create(14500);
            res1Lo = Sse2.ShiftRightArithmetic(Sse2.Add(res1Lo, c14500), 12);
            res1Hi = Sse2.ShiftRightArithmetic(Sse2.Add(res1Hi, c14500), 12);

            var res3Lo = Sse2.MultiplyAddAdjacent(cdLo, k_neg5352_2217);
            var res3Hi = Sse2.MultiplyAddAdjacent(cdHi, k_neg5352_2217);
            var c7500 = Vector128.Create(7500);
            res3Lo = Sse2.ShiftRightArithmetic(Sse2.Add(res3Lo, c7500), 12);
            res3Hi = Sse2.ShiftRightArithmetic(Sse2.Add(res3Hi, c7500), 12);

            var res1 = Sse2.PackSignedSaturate(res1Lo, res1Hi);
            var res3 = Sse2.PackSignedSaturate(res3Lo, res3Hi);

            var u01L = Sse2.UnpackLow(res0, res1);
            var u01R = Sse2.UnpackHigh(res0, res1);
            var u23L = Sse2.UnpackLow(res2, res3);
            var u23R = Sse2.UnpackHigh(res2, res3);
            var qLL01 = Sse2.UnpackLow(u01L.AsInt32(), u23L.AsInt32()).AsInt16();
            var qLL23 = Sse2.UnpackHigh(u01L.AsInt32(), u23L.AsInt32()).AsInt16();
            var qLR01 = Sse2.UnpackLow(u01R.AsInt32(), u23R.AsInt32()).AsInt16();
            var qLR23 = Sse2.UnpackHigh(u01R.AsInt32(), u23R.AsInt32()).AsInt16();
            var q0 = Sse2.UnpackLow(qLL01.AsInt64(), qLR01.AsInt64()).AsInt16();
            var q1 = Sse2.UnpackHigh(qLL01.AsInt64(), qLR01.AsInt64()).AsInt16();
            var q2 = Sse2.UnpackLow(qLL23.AsInt64(), qLR23.AsInt64()).AsInt16();
            var q3 = Sse2.UnpackHigh(qLL23.AsInt64(), qLR23.AsInt64()).AsInt16();

            var q0Lo = Sse2.ShiftRightArithmetic(Sse2.UnpackLow(q0, q0).AsInt32(), 16);
            var q0Hi = Sse2.ShiftRightArithmetic(Sse2.UnpackHigh(q0, q0).AsInt32(), 16);
            var q1Lo = Sse2.ShiftRightArithmetic(Sse2.UnpackLow(q1, q1).AsInt32(), 16);
            var q1Hi = Sse2.ShiftRightArithmetic(Sse2.UnpackHigh(q1, q1).AsInt32(), 16);
            var q2Lo = Sse2.ShiftRightArithmetic(Sse2.UnpackLow(q2, q2).AsInt32(), 16);
            var q2Hi = Sse2.ShiftRightArithmetic(Sse2.UnpackHigh(q2, q2).AsInt32(), 16);
            var q3Lo = Sse2.ShiftRightArithmetic(Sse2.UnpackLow(q3, q3).AsInt32(), 16);
            var q3Hi = Sse2.ShiftRightArithmetic(Sse2.UnpackHigh(q3, q3).AsInt32(), 16);

            var k2217i = Vector128.Create(2217);
            var k5352i = Vector128.Create(5352);
            var sevenI32 = Vector128.Create(7);
            var c12000 = Vector128.Create(12000);
            var c51000 = Vector128.Create(51000);
            var one = Vector128.Create(1);

            var a1cL = Sse2.Add(q0Lo, q3Lo);
            var b1cL = Sse2.Add(q1Lo, q2Lo);
            var c1cL = Sse2.Subtract(q1Lo, q2Lo);
            var d1cL = Sse2.Subtract(q0Lo, q3Lo);
            var f0L = Sse2.ShiftRightArithmetic(Sse2.Add(Sse2.Add(a1cL, b1cL), sevenI32), 4);
            var f2L = Sse2.ShiftRightArithmetic(Sse2.Add(Sse2.Subtract(a1cL, b1cL), sevenI32), 4);
            var f1L = Sse2.Add(Sse41.MultiplyLow(c1cL, k2217i), Sse41.MultiplyLow(d1cL, k5352i));
            f1L = Sse2.ShiftRightArithmetic(Sse2.Add(f1L, c12000), 16);
            f1L = Sse2.Add(f1L, Sse2.AndNot(Sse2.CompareEqual(d1cL, Vector128<int>.Zero), one));
            var f3L = Sse2.Subtract(Sse41.MultiplyLow(d1cL, k2217i), Sse41.MultiplyLow(c1cL, k5352i));
            f3L = Sse2.ShiftRightArithmetic(Sse2.Add(f3L, c51000), 16);

            var a1cR = Sse2.Add(q0Hi, q3Hi);
            var b1cR = Sse2.Add(q1Hi, q2Hi);
            var c1cR = Sse2.Subtract(q1Hi, q2Hi);
            var d1cR = Sse2.Subtract(q0Hi, q3Hi);
            var f0R = Sse2.ShiftRightArithmetic(Sse2.Add(Sse2.Add(a1cR, b1cR), sevenI32), 4);
            var f2R = Sse2.ShiftRightArithmetic(Sse2.Add(Sse2.Subtract(a1cR, b1cR), sevenI32), 4);
            var f1R = Sse2.Add(Sse41.MultiplyLow(c1cR, k2217i), Sse41.MultiplyLow(d1cR, k5352i));
            f1R = Sse2.ShiftRightArithmetic(Sse2.Add(f1R, c12000), 16);
            f1R = Sse2.Add(f1R, Sse2.AndNot(Sse2.CompareEqual(d1cR, Vector128<int>.Zero), one));
            var f3R = Sse2.Subtract(Sse41.MultiplyLow(d1cR, k2217i), Sse41.MultiplyLow(c1cR, k5352i));
            f3R = Sse2.ShiftRightArithmetic(Sse2.Add(f3R, c51000), 16);

            Sse2.Store(outLeft + 0, Sse2.PackSignedSaturate(f0L, f1L));
            Sse2.Store(outLeft + 8, Sse2.PackSignedSaturate(f2L, f3L));
            Sse2.Store(outRight + 0, Sse2.PackSignedSaturate(f0R, f1R));
            Sse2.Store(outRight + 8, Sse2.PackSignedSaturate(f2R, f3R));
        }

        // ---------------------------------------------------------------------
        // ARM AdvSimd - true 4-way 4x4 fdct
        // ---------------------------------------------------------------------

        /// <summary>AdvSimd 4x4 forward DCT mirroring <see cref="Fdct4x4Sse2"/>.
        /// Uses Zip primitives for the int16 transposes and widening multiplies
        /// for the (c*2217 + d*5352) terms.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Fdct4x4AdvSimd(short* input, short* output, int pitch)
        {
            int stride = pitch / 2;

            var r0 = Vector128.Create(AdvSimd.LoadVector64(input + 0 * stride), Vector64<short>.Zero);
            var r1 = Vector128.Create(AdvSimd.LoadVector64(input + 1 * stride), Vector64<short>.Zero);
            var r2 = Vector128.Create(AdvSimd.LoadVector64(input + 2 * stride), Vector64<short>.Zero);
            var r3 = Vector128.Create(AdvSimd.LoadVector64(input + 3 * stride), Vector64<short>.Zero);

            r0 = AdvSimd.ShiftLeftLogical(r0, 3);
            r1 = AdvSimd.ShiftLeftLogical(r1, 3);
            r2 = AdvSimd.ShiftLeftLogical(r2, 3);
            r3 = AdvSimd.ShiftLeftLogical(r3, 3);

            var ab = AdvSimd.Arm64.ZipLow(r0, r1);
            var cd = AdvSimd.Arm64.ZipLow(r2, r3);
            var c01 = AdvSimd.Arm64.ZipLow(ab.AsInt32(), cd.AsInt32()).AsInt16();
            var c23 = AdvSimd.Arm64.ZipHigh(ab.AsInt32(), cd.AsInt32()).AsInt16();

            var c0 = c01;
            var c1v = Vector128.Create(c01.GetUpper(), Vector64<short>.Zero);
            var c2 = c23;
            var c3v = Vector128.Create(c23.GetUpper(), Vector64<short>.Zero);

            var a1 = AdvSimd.Add(c0, c3v);
            var b1 = AdvSimd.Add(c1v, c2);
            var c1d = AdvSimd.Subtract(c1v, c2);
            var d1 = AdvSimd.Subtract(c0, c3v);

            var res0 = AdvSimd.Add(a1, b1);
            var res2 = AdvSimd.Subtract(a1, b1);

            var k2217 = Vector128.Create(2217);
            var k5352 = Vector128.Create(5352);
            var cInt32 = AdvSimd.SignExtendWideningLower(c1d.GetLower());
            var dInt32 = AdvSimd.SignExtendWideningLower(d1.GetLower());

            var res1Int = AdvSimd.Add(AdvSimd.Multiply(cInt32, k2217), AdvSimd.Multiply(dInt32, k5352));
            res1Int = AdvSimd.Add(res1Int, Vector128.Create(14500));
            res1Int = AdvSimd.ShiftRightArithmetic(res1Int, 12);

            var res3Int = AdvSimd.Subtract(AdvSimd.Multiply(dInt32, k2217), AdvSimd.Multiply(cInt32, k5352));
            res3Int = AdvSimd.Add(res3Int, Vector128.Create(7500));
            res3Int = AdvSimd.ShiftRightArithmetic(res3Int, 12);

            var res1 = Vector128.Create(AdvSimd.ExtractNarrowingLower(res1Int), Vector64<short>.Zero);
            var res3 = Vector128.Create(AdvSimd.ExtractNarrowingLower(res3Int), Vector64<short>.Zero);

            var ab2 = AdvSimd.Arm64.ZipLow(res0, res1);
            var cd2 = AdvSimd.Arm64.ZipLow(res2, res3);
            var q01 = AdvSimd.Arm64.ZipLow(ab2.AsInt32(), cd2.AsInt32()).AsInt16();
            var q23 = AdvSimd.Arm64.ZipHigh(ab2.AsInt32(), cd2.AsInt32()).AsInt16();

            var q0 = AdvSimd.SignExtendWideningLower(q01.GetLower());
            var q1 = AdvSimd.SignExtendWideningUpper(q01);
            var q2 = AdvSimd.SignExtendWideningLower(q23.GetLower());
            var q3 = AdvSimd.SignExtendWideningUpper(q23);

            var a1c = AdvSimd.Add(q0, q3);
            var b1c = AdvSimd.Add(q1, q2);
            var c1c = AdvSimd.Subtract(q1, q2);
            var d1c = AdvSimd.Subtract(q0, q3);

            var sevenI32 = Vector128.Create(7);
            var f0Int = AdvSimd.ShiftRightArithmetic(AdvSimd.Add(AdvSimd.Add(a1c, b1c), sevenI32), 4);
            var f2Int = AdvSimd.ShiftRightArithmetic(AdvSimd.Add(AdvSimd.Subtract(a1c, b1c), sevenI32), 4);

            var f1Int = AdvSimd.Add(AdvSimd.Multiply(c1c, k2217), AdvSimd.Multiply(d1c, k5352));
            f1Int = AdvSimd.Add(f1Int, Vector128.Create(12000));
            f1Int = AdvSimd.ShiftRightArithmetic(f1Int, 16);
            var dZero = AdvSimd.CompareEqual(d1c, Vector128<int>.Zero);
            f1Int = AdvSimd.Add(f1Int, AdvSimd.BitwiseClear(Vector128.Create(1), dZero));

            var f3Int = AdvSimd.Subtract(AdvSimd.Multiply(d1c, k2217), AdvSimd.Multiply(c1c, k5352));
            f3Int = AdvSimd.Add(f3Int, Vector128.Create(51000));
            f3Int = AdvSimd.ShiftRightArithmetic(f3Int, 16);

            AdvSimd.Store(output + 0,
                Vector128.Create(AdvSimd.ExtractNarrowingLower(f0Int), AdvSimd.ExtractNarrowingLower(f1Int)));
            AdvSimd.Store(output + 8,
                Vector128.Create(AdvSimd.ExtractNarrowingLower(f2Int), AdvSimd.ExtractNarrowingLower(f3Int)));
        }

        /// <summary>AdvSimd batched 8x4 fdct - same shape as <see cref="Fdct8x4Sse2"/>.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Fdct8x4AdvSimd(short* input, short* outLeft, short* outRight, int pitch)
        {
            int stride = pitch / 2;

            var r0 = AdvSimd.LoadVector128(input + 0 * stride);
            var r1 = AdvSimd.LoadVector128(input + 1 * stride);
            var r2 = AdvSimd.LoadVector128(input + 2 * stride);
            var r3 = AdvSimd.LoadVector128(input + 3 * stride);

            r0 = AdvSimd.ShiftLeftLogical(r0, 3);
            r1 = AdvSimd.ShiftLeftLogical(r1, 3);
            r2 = AdvSimd.ShiftLeftLogical(r2, 3);
            r3 = AdvSimd.ShiftLeftLogical(r3, 3);

            var abL = AdvSimd.Arm64.ZipLow(r0, r1);
            var abR = AdvSimd.Arm64.ZipHigh(r0, r1);
            var cdL = AdvSimd.Arm64.ZipLow(r2, r3);
            var cdR = AdvSimd.Arm64.ZipHigh(r2, r3);
            var lL01 = AdvSimd.Arm64.ZipLow(abL.AsInt32(), cdL.AsInt32()).AsInt16();
            var lL23 = AdvSimd.Arm64.ZipHigh(abL.AsInt32(), cdL.AsInt32()).AsInt16();
            var lR01 = AdvSimd.Arm64.ZipLow(abR.AsInt32(), cdR.AsInt32()).AsInt16();
            var lR23 = AdvSimd.Arm64.ZipHigh(abR.AsInt32(), cdR.AsInt32()).AsInt16();
            var c0 = AdvSimd.Arm64.ZipLow(lL01.AsInt64(), lR01.AsInt64()).AsInt16();
            var c1 = AdvSimd.Arm64.ZipHigh(lL01.AsInt64(), lR01.AsInt64()).AsInt16();
            var c2 = AdvSimd.Arm64.ZipLow(lL23.AsInt64(), lR23.AsInt64()).AsInt16();
            var c3 = AdvSimd.Arm64.ZipHigh(lL23.AsInt64(), lR23.AsInt64()).AsInt16();

            var a1 = AdvSimd.Add(c0, c3);
            var b1 = AdvSimd.Add(c1, c2);
            var c1d = AdvSimd.Subtract(c1, c2);
            var d1 = AdvSimd.Subtract(c0, c3);

            var res0 = AdvSimd.Add(a1, b1);
            var res2 = AdvSimd.Subtract(a1, b1);

            var k2217 = Vector128.Create(2217);
            var k5352 = Vector128.Create(5352);

            var cLo = AdvSimd.SignExtendWideningLower(c1d.GetLower());
            var cHi = AdvSimd.SignExtendWideningUpper(c1d);
            var dLo = AdvSimd.SignExtendWideningLower(d1.GetLower());
            var dHi = AdvSimd.SignExtendWideningUpper(d1);

            var c14500 = Vector128.Create(14500);
            var c7500 = Vector128.Create(7500);

            var res1Lo = AdvSimd.Add(AdvSimd.Multiply(cLo, k2217), AdvSimd.Multiply(dLo, k5352));
            res1Lo = AdvSimd.ShiftRightArithmetic(AdvSimd.Add(res1Lo, c14500), 12);
            var res1Hi = AdvSimd.Add(AdvSimd.Multiply(cHi, k2217), AdvSimd.Multiply(dHi, k5352));
            res1Hi = AdvSimd.ShiftRightArithmetic(AdvSimd.Add(res1Hi, c14500), 12);

            var res3Lo = AdvSimd.Subtract(AdvSimd.Multiply(dLo, k2217), AdvSimd.Multiply(cLo, k5352));
            res3Lo = AdvSimd.ShiftRightArithmetic(AdvSimd.Add(res3Lo, c7500), 12);
            var res3Hi = AdvSimd.Subtract(AdvSimd.Multiply(dHi, k2217), AdvSimd.Multiply(cHi, k5352));
            res3Hi = AdvSimd.ShiftRightArithmetic(AdvSimd.Add(res3Hi, c7500), 12);

            var res1 = Vector128.Create(AdvSimd.ExtractNarrowingLower(res1Lo), AdvSimd.ExtractNarrowingLower(res1Hi));
            var res3 = Vector128.Create(AdvSimd.ExtractNarrowingLower(res3Lo), AdvSimd.ExtractNarrowingLower(res3Hi));

            var u01L = AdvSimd.Arm64.ZipLow(res0, res1);
            var u01R = AdvSimd.Arm64.ZipHigh(res0, res1);
            var u23L = AdvSimd.Arm64.ZipLow(res2, res3);
            var u23R = AdvSimd.Arm64.ZipHigh(res2, res3);
            var qLL01 = AdvSimd.Arm64.ZipLow(u01L.AsInt32(), u23L.AsInt32()).AsInt16();
            var qLL23 = AdvSimd.Arm64.ZipHigh(u01L.AsInt32(), u23L.AsInt32()).AsInt16();
            var qLR01 = AdvSimd.Arm64.ZipLow(u01R.AsInt32(), u23R.AsInt32()).AsInt16();
            var qLR23 = AdvSimd.Arm64.ZipHigh(u01R.AsInt32(), u23R.AsInt32()).AsInt16();
            var q0 = AdvSimd.Arm64.ZipLow(qLL01.AsInt64(), qLR01.AsInt64()).AsInt16();
            var q1 = AdvSimd.Arm64.ZipHigh(qLL01.AsInt64(), qLR01.AsInt64()).AsInt16();
            var q2 = AdvSimd.Arm64.ZipLow(qLL23.AsInt64(), qLR23.AsInt64()).AsInt16();
            var q3 = AdvSimd.Arm64.ZipHigh(qLL23.AsInt64(), qLR23.AsInt64()).AsInt16();

            var q0L = AdvSimd.SignExtendWideningLower(q0.GetLower());
            var q0R = AdvSimd.SignExtendWideningUpper(q0);
            var q1L = AdvSimd.SignExtendWideningLower(q1.GetLower());
            var q1R = AdvSimd.SignExtendWideningUpper(q1);
            var q2L = AdvSimd.SignExtendWideningLower(q2.GetLower());
            var q2R = AdvSimd.SignExtendWideningUpper(q2);
            var q3L = AdvSimd.SignExtendWideningLower(q3.GetLower());
            var q3R = AdvSimd.SignExtendWideningUpper(q3);

            var sevenI32 = Vector128.Create(7);
            var c12000 = Vector128.Create(12000);
            var c51000 = Vector128.Create(51000);
            var one = Vector128.Create(1);

            var a1cL = AdvSimd.Add(q0L, q3L);
            var b1cL = AdvSimd.Add(q1L, q2L);
            var c1cL = AdvSimd.Subtract(q1L, q2L);
            var d1cL = AdvSimd.Subtract(q0L, q3L);
            var f0L = AdvSimd.ShiftRightArithmetic(AdvSimd.Add(AdvSimd.Add(a1cL, b1cL), sevenI32), 4);
            var f2L = AdvSimd.ShiftRightArithmetic(AdvSimd.Add(AdvSimd.Subtract(a1cL, b1cL), sevenI32), 4);
            var f1L = AdvSimd.Add(AdvSimd.Multiply(c1cL, k2217), AdvSimd.Multiply(d1cL, k5352));
            f1L = AdvSimd.ShiftRightArithmetic(AdvSimd.Add(f1L, c12000), 16);
            f1L = AdvSimd.Add(f1L, AdvSimd.BitwiseClear(one, AdvSimd.CompareEqual(d1cL, Vector128<int>.Zero)));
            var f3L = AdvSimd.Subtract(AdvSimd.Multiply(d1cL, k2217), AdvSimd.Multiply(c1cL, k5352));
            f3L = AdvSimd.ShiftRightArithmetic(AdvSimd.Add(f3L, c51000), 16);

            var a1cR = AdvSimd.Add(q0R, q3R);
            var b1cR = AdvSimd.Add(q1R, q2R);
            var c1cR = AdvSimd.Subtract(q1R, q2R);
            var d1cR = AdvSimd.Subtract(q0R, q3R);
            var f0R = AdvSimd.ShiftRightArithmetic(AdvSimd.Add(AdvSimd.Add(a1cR, b1cR), sevenI32), 4);
            var f2R = AdvSimd.ShiftRightArithmetic(AdvSimd.Add(AdvSimd.Subtract(a1cR, b1cR), sevenI32), 4);
            var f1R = AdvSimd.Add(AdvSimd.Multiply(c1cR, k2217), AdvSimd.Multiply(d1cR, k5352));
            f1R = AdvSimd.ShiftRightArithmetic(AdvSimd.Add(f1R, c12000), 16);
            f1R = AdvSimd.Add(f1R, AdvSimd.BitwiseClear(one, AdvSimd.CompareEqual(d1cR, Vector128<int>.Zero)));
            var f3R = AdvSimd.Subtract(AdvSimd.Multiply(d1cR, k2217), AdvSimd.Multiply(c1cR, k5352));
            f3R = AdvSimd.ShiftRightArithmetic(AdvSimd.Add(f3R, c51000), 16);

            AdvSimd.Store(outLeft + 0,
                Vector128.Create(AdvSimd.ExtractNarrowingLower(f0L), AdvSimd.ExtractNarrowingLower(f1L)));
            AdvSimd.Store(outLeft + 8,
                Vector128.Create(AdvSimd.ExtractNarrowingLower(f2L), AdvSimd.ExtractNarrowingLower(f3L)));
            AdvSimd.Store(outRight + 0,
                Vector128.Create(AdvSimd.ExtractNarrowingLower(f0R), AdvSimd.ExtractNarrowingLower(f1R)));
            AdvSimd.Store(outRight + 8,
                Vector128.Create(AdvSimd.ExtractNarrowingLower(f2R), AdvSimd.ExtractNarrowingLower(f3R)));
        }
    }
}
