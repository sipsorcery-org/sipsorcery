//-----------------------------------------------------------------------------
// Encoder-side SIMD fast path for vp8_regular_quantize_b_arrays. Bit-exact
// with the scalar reference in quantize.cs.
//
// Strategy (matches the plan's Tier 5 approach (a)):
//   * Pre-compute, for all 16 coefficients in parallel via int32 SIMD lanes:
//     - abs(z), sign(z), x_signed = (y_raw ^ sz) - sz, y_raw, dq value.
//     This is the heavy arithmetic — 16 multiplies become 4 vector multiplies
//     on each of x86 (PMULLD, SSE4.1+) and ARM (AdvSimd.Multiply on int32).
//   * A short scalar zigzag pass then applies the zbin_thr check including
//     the sequential zrun_zbin_boost lookup, writes qcoeff/dqcoeff at the
//     accepted positions, and computes eob.
//
// The zrun_zbin_boost index advance and the eob/zigzag scan must remain
// sequential to preserve bit-identity with libvpx. The pre-computed values
// for rejected positions are simply discarded.
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
    internal static unsafe class QuantizeEncoderSimd
    {
        public static bool IsSupported => Sse41.IsSupported || AdvSimd.Arm64.IsSupported;

        /// <summary>SIMD bit-exact replacement for
        /// <see cref="quantize.vp8_regular_quantize_b_arrays"/>.</summary>
        public static int RegularQuantizeB(
            short* coeff,
            short* zbin,
            short* zrun_zbin_boost,
            short* round,
            short* quant,
            short* quant_shift,
            short* dequant,
            short* qcoeff,
            short* dqcoeff,
            short zbin_extra)
        {
            // Output buffers must start zero — only accepted positions get written.
            ClearBlockOutputs(qcoeff, dqcoeff);

            int* absX = stackalloc int[16];
            int* sign = stackalloc int[16];
            int* yRaw = stackalloc int[16];
            int* xSigned = stackalloc int[16];
            int* dqValue = stackalloc int[16];

            if (Sse41.IsSupported)
            {
                PrecomputeSse41(coeff, round, quant, quant_shift, dequant,
                                absX, sign, yRaw, xSigned, dqValue);
            }
            else
            {
                PrecomputeAdvSimd(coeff, round, quant, quant_shift, dequant,
                                  absX, sign, yRaw, xSigned, dqValue);
            }

            // Sequential zigzag-order zbin gating + eob/zboost tracking.
            int eob = -1;
            int zbin_boost_idx = 0;
            int extraI = zbin_extra;

            for (int i = 0; i < 16; i++)
            {
                int rc = entropy.vp8_default_zig_zag1d[i];
                int zbinThr = zbin[rc] + zrun_zbin_boost[zbin_boost_idx] + extraI;
                zbin_boost_idx++;

                if (absX[rc] >= zbinThr)
                {
                    int xs = xSigned[rc];
                    qcoeff[rc] = (short)xs;
                    dqcoeff[rc] = (short)dqValue[rc];

                    if (yRaw[rc] != 0)
                    {
                        eob = i;
                        zbin_boost_idx = 0;
                    }
                }
            }

            return eob + 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void StoreInt4Unaligned(int* dst, Vector128<int> v)
        {
            dst[0] = v.GetElement(0);
            dst[1] = v.GetElement(1);
            dst[2] = v.GetElement(2);
            dst[3] = v.GetElement(3);
        }

        // ----------------- SSE4.1 pre-compute -----------------

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void PrecomputeSse41(
            short* coeff, short* round, short* quant, short* quant_shift, short* dequant,
            int* absX, int* sign, int* yRaw, int* xSigned, int* dqValue)
        {
            // Process the 16 coeffs as 4 groups of 4 int32 lanes.
            for (int g = 0; g < 4; g++)
            {
                short* cp = coeff + g * 4;
                short* rp = round + g * 4;
                short* qp = quant + g * 4;
                short* qsp = quant_shift + g * 4;
                short* dqp = dequant + g * 4;

                // Sign-extend 4 shorts → Vector128<int>.
                var z = Sse41.ConvertToVector128Int32(Sse2.LoadScalarVector128((double*)cp).AsInt16());
                var sz = Sse2.ShiftRightArithmetic(z, 31);            // 0 or -1
                var ax = Sse2.Subtract(Sse2.Xor(z, sz), sz);           // |z|

                var rnd = Sse41.ConvertToVector128Int32(Sse2.LoadScalarVector128((double*)rp).AsInt16());
                var x = Sse2.Add(ax, rnd);

                var qv = Sse41.ConvertToVector128Int32(Sse2.LoadScalarVector128((double*)qp).AsInt16());
                var t1 = Sse2.ShiftRightArithmetic(Sse41.MultiplyLow(x, qv), 16);
                var t2 = Sse2.Add(t1, x);

                var qsh = Sse41.ConvertToVector128Int32(Sse2.LoadScalarVector128((double*)qsp).AsInt16());
                var y = Sse2.ShiftRightArithmetic(Sse41.MultiplyLow(t2, qsh), 16);

                var xs = Sse2.Subtract(Sse2.Xor(y, sz), sz);

                var dq = Sse41.ConvertToVector128Int32(Sse2.LoadScalarVector128((double*)dqp).AsInt16());
                var dqv = Sse41.MultiplyLow(xs, dq);

                StoreInt4Unaligned(absX + g * 4, ax);
                StoreInt4Unaligned(sign + g * 4, sz);
                StoreInt4Unaligned(yRaw + g * 4, y);
                StoreInt4Unaligned(xSigned + g * 4, xs);
                StoreInt4Unaligned(dqValue + g * 4, dqv);
            }
        }

        // ----------------- AdvSimd pre-compute -----------------

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void PrecomputeAdvSimd(
            short* coeff, short* round, short* quant, short* quant_shift, short* dequant,
            int* absX, int* sign, int* yRaw, int* xSigned, int* dqValue)
        {
            for (int g = 0; g < 4; g++)
            {
                short* cp = coeff + g * 4;
                short* rp = round + g * 4;
                short* qp = quant + g * 4;
                short* qsp = quant_shift + g * 4;
                short* dqp = dequant + g * 4;

                var z = AdvSimd.SignExtendWideningLower(AdvSimd.LoadVector64(cp));
                var sz = AdvSimd.ShiftRightArithmetic(z, 31);
                var ax = AdvSimd.Subtract(AdvSimd.Xor(z, sz), sz);

                var rnd = AdvSimd.SignExtendWideningLower(AdvSimd.LoadVector64(rp));
                var x = AdvSimd.Add(ax, rnd);

                var qv = AdvSimd.SignExtendWideningLower(AdvSimd.LoadVector64(qp));
                var t1 = AdvSimd.ShiftRightArithmetic(AdvSimd.Multiply(x, qv), 16);
                var t2 = AdvSimd.Add(t1, x);

                var qsh = AdvSimd.SignExtendWideningLower(AdvSimd.LoadVector64(qsp));
                var y = AdvSimd.ShiftRightArithmetic(AdvSimd.Multiply(t2, qsh), 16);

                var xs = AdvSimd.Subtract(AdvSimd.Xor(y, sz), sz);

                var dq = AdvSimd.SignExtendWideningLower(AdvSimd.LoadVector64(dqp));
                var dqv = AdvSimd.Multiply(xs, dq);

                StoreInt4Unaligned(absX + g * 4, ax);
                StoreInt4Unaligned(sign + g * 4, sz);
                StoreInt4Unaligned(yRaw + g * 4, y);
                StoreInt4Unaligned(xSigned + g * 4, xs);
                StoreInt4Unaligned(dqValue + g * 4, dqv);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ClearBlockOutputs(short* qcoeff, short* dqcoeff)
        {
            if (Sse2.IsSupported || AdvSimd.IsSupported)
            {
                var z = Vector128<short>.Zero;
                Unsafe.WriteUnaligned(ref *(byte*)qcoeff, z);
                Unsafe.WriteUnaligned(ref *(byte*)(qcoeff + 8), z);
                Unsafe.WriteUnaligned(ref *(byte*)dqcoeff, z);
                Unsafe.WriteUnaligned(ref *(byte*)(dqcoeff + 8), z);
            }
            else
            {
                for (int i = 0; i < 16; i++) { qcoeff[i] = 0; dqcoeff[i] = 0; }
            }
        }
    }
}
