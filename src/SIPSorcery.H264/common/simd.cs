//-----------------------------------------------------------------------------
// Filename: simd.cs
//
// Description: Helpers for the SIMD kernels (Phase 5): unaligned loads and
// stores of 4, 8 and 16 samples, the interleaves, saturating packs, rounded
// average and unsigned high multiply Vector128 has no portable form of, and the
// SAD step. Not a port:
// upstream's SIMD kernels are assembly.
//
// The kernels are put in the function tables only where WelsCpuHasSimd128 holds,
// that is with SSE2 on x86 and x64 or AdvSimd on Arm64, so each helper has a
// branch for those two. The JIT keeps only the branch for the machine it runs on.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 27 Nov 2026  Claude          Created (P5.2).
// 27 Nov 2026  Claude          Narrow loads and stores, packs and averaging for MC (P5.3).
// 27 Nov 2026  Claude          Byte interleaves for the deblocking transposes (P5.4).
// 27 Nov 2026  Claude          The upper 8 byte store and the unsigned high multiply (P5.5).
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

namespace SIPSorcery.H264;

internal static unsafe class Simd
{
    /// <summary>16 samples.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<byte> Load16(uint8_t* p) => Vector128.Load(p);

    /// <summary>8 samples from each of two rows, the first in the low half.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<byte> Load8x2(uint8_t* p0, uint8_t* p1) =>
        Vector128.Create(Unsafe.ReadUnaligned<ulong>(p0), Unsafe.ReadUnaligned<ulong>(p1)).AsByte();

    /// <summary>4 samples from each of four rows, the first in the lowest lanes.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<byte> Load4x4(uint8_t* p, int32_t iStride) =>
        Vector128.Create(Unsafe.ReadUnaligned<uint>(p), Unsafe.ReadUnaligned<uint>(p + iStride),
            Unsafe.ReadUnaligned<uint>(p + 2 * iStride), Unsafe.ReadUnaligned<uint>(p + 3 * iStride)).AsByte();

    /// <summary>
    /// <paramref name="acc"/> plus the absolute differences of <paramref name="a"/> and
    /// <paramref name="b"/>. Where they land in the lanes differs between the x86 and Arm
    /// branches; <see cref="SadTotal"/> adds them up either way. Each lane takes at most 2040 a
    /// step, so 32 steps can't overflow it.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<ushort> SadAdd(Vector128<ushort> acc, Vector128<byte> a, Vector128<byte> b)
    {
        if (Sse2.IsSupported)
        {
            // psadbw: each 64-bit half's sum in its low 16 bits, the rest zero.
            return Sse2.Add(acc, Sse2.SumAbsoluteDifferences(a, b));
        }
        if (AdvSimd.IsSupported)
        {
            return AdvSimd.AddPairwiseWideningAndAdd(acc, AdvSimd.AbsoluteDifference(a, b));
        }
        Vector128<byte> d = Vector128.Max(a, b) - Vector128.Min(a, b);
        return acc + Vector128.WidenLower(d) + Vector128.WidenUpper(d);
    }

    /// <summary>The sum of the lanes of a <see cref="SadAdd"/> accumulator.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int32_t SadTotal(Vector128<ushort> acc)
    {
        if (Sse2.IsSupported)
        {
            return acc.GetElement(0) + acc.GetElement(4);
        }
        return (int32_t)Vector128.Sum(Vector128.WidenLower(acc) + Vector128.WidenUpper(acc));
    }

    /// <summary>The differences of 8 samples of each row, as 16-bit lanes.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<short> Diff8(uint8_t* p1, uint8_t* p2) =>
        (Vector128.WidenLower(Vector128.CreateScalar(Unsafe.ReadUnaligned<ulong>(p1)).AsByte()) -
         Vector128.WidenLower(Vector128.CreateScalar(Unsafe.ReadUnaligned<ulong>(p2)).AsByte())).AsInt16();

    /// <summary>
    /// The differences of 4 samples of two rows each, as 16-bit lanes: the first row's in lanes
    /// 0 to 3, the second's in lanes 4 to 7.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<short> Diff4x2(uint8_t* p1a, uint8_t* p1b, uint8_t* p2a, uint8_t* p2b) =>
        (Vector128.WidenLower(Vector128.Create(Unsafe.ReadUnaligned<uint>(p1a), Unsafe.ReadUnaligned<uint>(p1b), 0u, 0u).AsByte()) -
         Vector128.WidenLower(Vector128.Create(Unsafe.ReadUnaligned<uint>(p2a), Unsafe.ReadUnaligned<uint>(p2b), 0u, 0u).AsByte())).AsInt16();

    /// <summary>The differences of 4 samples of a row in lanes 0 to 3, the other lanes 0.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<short> Diff4(uint8_t* p1, uint8_t* p2) =>
        (Vector128.WidenLower(Vector128.CreateScalar(Unsafe.ReadUnaligned<uint>(p1)).AsByte()) -
         Vector128.WidenLower(Vector128.CreateScalar(Unsafe.ReadUnaligned<uint>(p2)).AsByte())).AsInt16();

    /// <summary>Lanes 0 to 7 of a and b interleaved: a0 b0 a1 b1 ... a7 b7.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<byte> ZipLow(Vector128<byte> a, Vector128<byte> b) =>
        Sse2.IsSupported ? Sse2.UnpackLow(a, b) : AdvSimd.Arm64.IsSupported ? AdvSimd.Arm64.ZipLow(a, b) : throw new PlatformNotSupportedException();

    /// <summary>Lanes 8 to 15 of a and b interleaved.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<byte> ZipHigh(Vector128<byte> a, Vector128<byte> b) =>
        Sse2.IsSupported ? Sse2.UnpackHigh(a, b) : AdvSimd.Arm64.IsSupported ? AdvSimd.Arm64.ZipHigh(a, b) : throw new PlatformNotSupportedException();

    /// <summary>Lanes 0 to 3 of a and b interleaved: a0 b0 a1 b1 a2 b2 a3 b3.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<short> ZipLow(Vector128<short> a, Vector128<short> b) =>
        Sse2.IsSupported ? Sse2.UnpackLow(a, b) : AdvSimd.Arm64.IsSupported ? AdvSimd.Arm64.ZipLow(a, b) : throw new PlatformNotSupportedException();

    /// <summary>Lanes 4 to 7 of a and b interleaved.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<short> ZipHigh(Vector128<short> a, Vector128<short> b) =>
        Sse2.IsSupported ? Sse2.UnpackHigh(a, b) : AdvSimd.Arm64.IsSupported ? AdvSimd.Arm64.ZipHigh(a, b) : throw new PlatformNotSupportedException();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<int> ZipLow(Vector128<int> a, Vector128<int> b) =>
        Sse2.IsSupported ? Sse2.UnpackLow(a, b) : AdvSimd.Arm64.IsSupported ? AdvSimd.Arm64.ZipLow(a, b) : throw new PlatformNotSupportedException();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<int> ZipHigh(Vector128<int> a, Vector128<int> b) =>
        Sse2.IsSupported ? Sse2.UnpackHigh(a, b) : AdvSimd.Arm64.IsSupported ? AdvSimd.Arm64.ZipHigh(a, b) : throw new PlatformNotSupportedException();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<long> ZipLow(Vector128<long> a, Vector128<long> b) =>
        Sse2.IsSupported ? Sse2.UnpackLow(a, b) : AdvSimd.Arm64.IsSupported ? AdvSimd.Arm64.ZipLow(a, b) : throw new PlatformNotSupportedException();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<long> ZipHigh(Vector128<long> a, Vector128<long> b) =>
        Sse2.IsSupported ? Sse2.UnpackHigh(a, b) : AdvSimd.Arm64.IsSupported ? AdvSimd.Arm64.ZipHigh(a, b) : throw new PlatformNotSupportedException();

    /// <summary>The sum of the lanes, widened so it can't overflow.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int32_t SumWidened(Vector128<ushort> v) =>
        (int32_t)Vector128.Sum(Vector128.WidenLower(v) + Vector128.WidenUpper(v));

    /// <summary>8 samples in the low lanes, the rest 0.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<byte> Load8(uint8_t* p) => Vector128.CreateScalar(Unsafe.ReadUnaligned<ulong>(p)).AsByte();

    /// <summary>4 samples in the low lanes, the rest 0.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<byte> Load4(uint8_t* p) => Vector128.CreateScalar(Unsafe.ReadUnaligned<uint>(p)).AsByte();

    /// <summary>Lanes 0 to 7.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Store8(uint8_t* p, Vector128<byte> v) => Unsafe.WriteUnaligned(p, v.AsUInt64().ToScalar());

    /// <summary>Lanes 0 to 3.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Store4(uint8_t* p, Vector128<byte> v) => Unsafe.WriteUnaligned(p, v.AsUInt32().ToScalar());

    /// <summary>Lanes 4 to 7.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Store4High(uint8_t* p, Vector128<byte> v) => Unsafe.WriteUnaligned(p, v.AsUInt32().GetElement(1));

    /// <summary>Bytes 8 to 15.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Store8High(uint8_t* p, Vector128<byte> v) => Unsafe.WriteUnaligned(p, v.AsUInt64().GetElement(1));

    /// <summary>Lanes 0 to 7 widened to 16 bits.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<short> Lo16(Vector128<byte> v) => Vector128.WidenLower(v).AsInt16();

    /// <summary>Lanes 8 to 15 widened to 16 bits.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<short> Hi16(Vector128<byte> v) => Vector128.WidenUpper(v).AsInt16();

    /// <summary>8 samples widened to 16 bits.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<short> Load8W(uint8_t* p) => Lo16(Load8(p));

    /// <summary>Two rows of 4 samples, each read as one uint, widened to 16 bits: a in lanes 0 to 3, b in 4 to 7.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<short> Rows4x2W(uint a, uint b) => Lo16(Vector128.CreateScalar(a | ((ulong)b << 32)).AsByte());

    /// <summary>
    /// 16-bit lanes to bytes, saturating to 0..255 as WelsClip1 does: <paramref name="lo"/> to
    /// lanes 0 to 7 and <paramref name="hi"/> to 8 to 15.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<byte> PackUs(Vector128<short> lo, Vector128<short> hi)
    {
        if (Sse2.IsSupported)
        {
            return Sse2.PackUnsignedSaturate(lo, hi);
        }
        if (AdvSimd.Arm64.IsSupported)
        {
            return AdvSimd.ExtractNarrowingSaturateUnsignedUpper(AdvSimd.ExtractNarrowingSaturateUnsignedLower(lo), hi);
        }
        Vector128<short> max = Vector128.Create((short)255);
        return Vector128.Narrow(Vector128.Min(Vector128.Max(lo, Vector128<short>.Zero), max).AsUInt16(),
                                Vector128.Min(Vector128.Max(hi, Vector128<short>.Zero), max).AsUInt16());
    }

    /// <summary>32-bit lanes to 16 bits, saturating: <paramref name="lo"/> to lanes 0 to 3 and <paramref name="hi"/> to 4 to 7.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<short> PackSs(Vector128<int> lo, Vector128<int> hi)
    {
        if (Sse2.IsSupported)
        {
            return Sse2.PackSignedSaturate(lo, hi);
        }
        if (AdvSimd.Arm64.IsSupported)
        {
            return AdvSimd.ExtractNarrowingSaturateUpper(AdvSimd.ExtractNarrowingSaturateLower(lo), hi);
        }
        Vector128<int> min = Vector128.Create((int)short.MinValue), max = Vector128.Create((int)short.MaxValue);
        return Vector128.Narrow(Vector128.Min(Vector128.Max(lo, min), max), Vector128.Min(Vector128.Max(hi, min), max));
    }

    /// <summary>(a * b) >> 16, lane by lane, unsigned.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<ushort> MultiplyHigh(Vector128<ushort> a, Vector128<ushort> b)
    {
        if (Sse2.IsSupported)
        {
            return Sse2.MultiplyHigh(a, b);
        }
        if (AdvSimd.Arm64.IsSupported)
        {
            // The products' upper halves are the odd 16-bit lanes.
            return AdvSimd.Arm64.UnzipOdd(AdvSimd.MultiplyWideningLower(a.GetLower(), b.GetLower()).AsUInt16(),
                                          AdvSimd.MultiplyWideningUpper(a, b).AsUInt16());
        }
        return Vector128.Narrow(Vector128.ShiftRightLogical(Vector128.WidenLower(a) * Vector128.WidenLower(b), 16),
                                Vector128.ShiftRightLogical(Vector128.WidenUpper(a) * Vector128.WidenUpper(b), 16));
    }

    /// <summary>(a + b + 1) >> 1, lane by lane.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<byte> Avg(Vector128<byte> a, Vector128<byte> b)
    {
        if (Sse2.IsSupported)
        {
            return Sse2.Average(a, b);
        }
        if (AdvSimd.IsSupported)
        {
            return AdvSimd.FusedAddRoundedHalving(a, b);
        }
        return (a | b) - Vector128.ShiftRightLogical(a ^ b, 1);
    }
}
