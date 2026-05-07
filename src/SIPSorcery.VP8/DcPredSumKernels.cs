//-----------------------------------------------------------------------------
// SIMD byte-sum reductions used by the encoder's DC_PRED helpers
// (DcPred16x16 / DcPred8x8 in mb_encoder).
//
// Sse2: PSADBW on (v, 0) gives two int16 partial sums in lanes 0 and 4 of
// a Vector128<ushort>; their sum equals the 16-byte sum.
// AdvSimd: ZeroExtendWidening{Lower,Upper} + AddAcross.
//
// These tiny reduces (3 calls/MB) are not a major perf target but match the
// plan's Tier 6 polish layer.
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
    internal static unsafe class DcPredSumKernels
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Sum16(byte* p)
        {
            if (Sse2.IsSupported)
            {
                var v = Sse2.LoadVector128(p);
                var sad = Sse2.SumAbsoluteDifferences(v, Vector128<byte>.Zero);
                return sad.GetElement(0) + sad.GetElement(4);
            }
            if (AdvSimd.Arm64.IsSupported)
            {
                var v = AdvSimd.LoadVector128(p);
                var lo = AdvSimd.ZeroExtendWideningLower(v.GetLower());
                var hi = AdvSimd.ZeroExtendWideningLower(v.GetUpper());
                var sum = AdvSimd.Add(lo, hi);
                return AdvSimd.Arm64.AddAcross(sum).ToScalar();
            }

            int s = 0;
            for (int i = 0; i < 16; i++) s += p[i];
            return s;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Sum8(byte* p)
        {
            if (Sse2.IsSupported)
            {
                // Load 8 bytes into the low qword; PSADBW vs zero leaves the sum in lane 0.
                var v = Sse2.LoadScalarVector128((long*)p).AsByte();
                var sad = Sse2.SumAbsoluteDifferences(v, Vector128<byte>.Zero);
                return sad.GetElement(0);
            }
            if (AdvSimd.Arm64.IsSupported)
            {
                var v = AdvSimd.LoadVector64(p);
                var widened = AdvSimd.ZeroExtendWideningLower(v);
                return AdvSimd.Arm64.AddAcross(widened).ToScalar();
            }

            int s = 0;
            for (int i = 0; i < 8; i++) s += p[i];
            return s;
        }
    }
}
