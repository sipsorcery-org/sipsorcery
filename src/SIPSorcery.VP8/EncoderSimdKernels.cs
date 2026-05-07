//-----------------------------------------------------------------------------
// Span-based block copies and SIMD byte→short residual kernels for the
// optimized VP8 encoder path. Bit-identical to scalar (src[i]-pred[i]):
// x86/x64: AVX2 (256-bit load + dual SSE2 widen/sub) then SSE2; ARM: AdvSimd;
// otherwise scalar.
//-----------------------------------------------------------------------------

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

namespace Vpx.Net
{
    internal static class EncoderSimdKernels
    {
        public static void CopyPlaneRectSpan(byte[] src, int baseOffset, int srcStride, int x, int y, int w, int h, byte[] dst)
        {
            for (int r = 0; r < h; r++)
            {
                int rowStart = baseOffset + (y + r) * srcStride + x;
                src.AsSpan(rowStart, w).CopyTo(dst.AsSpan(r * w, w));
            }
        }

        public static void CopySpan(byte[] src, int srcOffset, byte[] dst, int dstOffset, int count)
        {
            src.AsSpan(srcOffset, count).CopyTo(dst.AsSpan(dstOffset, count));
        }

        public static void CopyColumn(byte[] src, int srcStride, int columnIndex, int firstRow, int rows, byte[] dst)
        {
            for (int r = 0; r < rows; r++)
                dst[r] = src[(firstRow + r) * srcStride + columnIndex];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SubtractFlatSse2Widen16(Vector128<byte> b, Vector128<byte> pLo, Vector128<byte> pHi, ref short dr)
        {
            var z = Vector128<byte>.Zero;
            var lo = Sse2.UnpackLow(b, z);
            var hi = Sse2.UnpackHigh(b, z);
            var dLo = Sse2.Subtract(lo, pLo);
            var dHi = Sse2.Subtract(hi, pHi);
            Vector128.StoreUnsafe(dLo.AsInt16(), ref dr);
            Vector128.StoreUnsafe(dHi.AsInt16(), ref Unsafe.Add(ref dr, 8));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SubtractPerPixelSse2Widen16(Vector128<byte> a, Vector128<byte> b, Vector128<byte> z, ref short dr)
        {
            var aLo = Sse2.UnpackLow(a, z);
            var aHi = Sse2.UnpackHigh(a, z);
            var bLo = Sse2.UnpackLow(b, z);
            var bHi = Sse2.UnpackHigh(b, z);
            var dLo = Sse2.Subtract(aLo, bLo);
            var dHi = Sse2.Subtract(aHi, bHi);
            Vector128.StoreUnsafe(dLo.AsInt16(), ref dr);
            Vector128.StoreUnsafe(dHi.AsInt16(), ref Unsafe.Add(ref dr, 8));
        }

        public static void SubtractFlatToShort(byte[] src, byte pred, short[] dst)
        {
            int n = src.Length;
            int i = 0;
            if (Avx2.IsSupported)
            {
                var predWide = Vector128.Create(pred);
                var z128 = Vector128<byte>.Zero;
                var pLo = Sse2.UnpackLow(predWide, z128);
                var pHi = Sse2.UnpackHigh(predWide, z128);
                for (; i + 32 <= n; i += 32)
                {
                    ref byte sr = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(src), i);
                    var v32 = Vector256.LoadUnsafe(ref sr);
                    ref short dr = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(dst), i);
                    SubtractFlatSse2Widen16(v32.GetLower(), pLo, pHi, ref dr);
                    SubtractFlatSse2Widen16(v32.GetUpper(), pLo, pHi, ref Unsafe.Add(ref dr, 16));
                }
                for (; i + 16 <= n; i += 16)
                {
                    ref byte sr = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(src), i);
                    var b = Vector128.LoadUnsafe(ref sr);
                    ref short dr = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(dst), i);
                    SubtractFlatSse2Widen16(b, pLo, pHi, ref dr);
                }
            }
            else if (Sse2.IsSupported)
            {
                var predWide = Vector128.Create(pred);
                var z = Vector128<byte>.Zero;
                var pLo = Sse2.UnpackLow(predWide, z);
                var pHi = Sse2.UnpackHigh(predWide, z);
                for (; i + 16 <= n; i += 16)
                {
                    ref byte sr = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(src), i);
                    var b = Vector128.LoadUnsafe(ref sr);
                    ref short dr = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(dst), i);
                    SubtractFlatSse2Widen16(b, pLo, pHi, ref dr);
                }
            }
            else if (AdvSimd.IsSupported)
            {
                var predS = Vector128.Create((short)pred);
                for (; i + 16 <= n; i += 16)
                {
                    ref byte sr = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(src), i);
                    var bytes = Vector128.LoadUnsafe(ref sr);
                    var uLo = AdvSimd.ZeroExtendWideningLower(bytes.GetLower());
                    var uHi = AdvSimd.ZeroExtendWideningUpper(bytes);
                    var sLo = uLo.AsInt16();
                    var sHi = uHi.AsInt16();
                    var dLo = AdvSimd.Subtract(sLo, predS);
                    var dHi = AdvSimd.Subtract(sHi, predS);
                    ref short dr = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(dst), i);
                    Vector128.StoreUnsafe(dLo, ref dr);
                    Vector128.StoreUnsafe(dHi, ref Unsafe.Add(ref dr, 8));
                }
            }

            for (; i < n; i++)
                dst[i] = (short)(src[i] - pred);
        }

        public static void SubtractPerPixelToShort(byte[] src, byte[] pred, short[] dst)
        {
            int n = src.Length;
            int i = 0;
            if (Avx2.IsSupported)
            {
                var z = Vector128<byte>.Zero;
                for (; i + 32 <= n; i += 32)
                {
                    ref byte sr = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(src), i);
                    ref byte pr = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(pred), i);
                    var a32 = Vector256.LoadUnsafe(ref sr);
                    var b32 = Vector256.LoadUnsafe(ref pr);
                    ref short dr = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(dst), i);
                    SubtractPerPixelSse2Widen16(a32.GetLower(), b32.GetLower(), z, ref dr);
                    SubtractPerPixelSse2Widen16(a32.GetUpper(), b32.GetUpper(), z, ref Unsafe.Add(ref dr, 16));
                }
                for (; i + 16 <= n; i += 16)
                {
                    ref byte sr = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(src), i);
                    ref byte pr = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(pred), i);
                    var a = Vector128.LoadUnsafe(ref sr);
                    var b = Vector128.LoadUnsafe(ref pr);
                    ref short dr = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(dst), i);
                    SubtractPerPixelSse2Widen16(a, b, z, ref dr);
                }
            }
            else if (Sse2.IsSupported)
            {
                var z = Vector128<byte>.Zero;
                for (; i + 16 <= n; i += 16)
                {
                    ref byte sr = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(src), i);
                    ref byte pr = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(pred), i);
                    var a = Vector128.LoadUnsafe(ref sr);
                    var b = Vector128.LoadUnsafe(ref pr);
                    ref short dr = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(dst), i);
                    SubtractPerPixelSse2Widen16(a, b, z, ref dr);
                }
            }
            else if (AdvSimd.IsSupported)
            {
                for (; i + 16 <= n; i += 16)
                {
                    ref byte sr = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(src), i);
                    ref byte pr = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(pred), i);
                    var va = Vector128.LoadUnsafe(ref sr);
                    var vb = Vector128.LoadUnsafe(ref pr);
                    var uALo = AdvSimd.ZeroExtendWideningLower(va.GetLower());
                    var uAHi = AdvSimd.ZeroExtendWideningUpper(va);
                    var uBLo = AdvSimd.ZeroExtendWideningLower(vb.GetLower());
                    var uBHi = AdvSimd.ZeroExtendWideningUpper(vb);
                    var dLo = AdvSimd.Subtract(uALo.AsInt16(), uBLo.AsInt16());
                    var dHi = AdvSimd.Subtract(uAHi.AsInt16(), uBHi.AsInt16());
                    ref short dr = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(dst), i);
                    Vector128.StoreUnsafe(dLo, ref dr);
                    Vector128.StoreUnsafe(dHi, ref Unsafe.Add(ref dr, 8));
                }
            }

            for (; i < n; i++)
                dst[i] = (short)(src[i] - pred[i]);
        }
    }
}
