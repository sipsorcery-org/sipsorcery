using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
#if NET8_0_OR_GREATER
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
#endif

namespace SIPSorcery.Sys;

internal static class BinaryExtensions
{
    public static ushort ReadUInt16BigEndianAndAdvance(ref ReadOnlySpan<byte> buffer, int offset = 0)
    {
        buffer = buffer.Slice(offset);
        var value = BinaryPrimitives.ReadUInt16BigEndian(buffer);
        buffer = buffer.Slice(sizeof(ushort));
        return value;
    }

    public static uint ReadUInt32BigEndianAndAdvance(ref ReadOnlySpan<byte> buffer, int offset = 0)
    {
        buffer = buffer.Slice(offset);
        var value = BinaryPrimitives.ReadUInt32BigEndian(buffer);
        buffer = buffer.Slice(sizeof(uint));
        return value;
    }

    public static void WriteUInt16BigEndianAndAdvance(ref Span<byte> buffer, ushort value, int offset = 0)
    {
        buffer = buffer.Slice(offset);
        BinaryPrimitives.WriteUInt16BigEndian(buffer, value);
        buffer = buffer.Slice(sizeof(ushort));
    }

    public static void WriteUInt32BigEndianAndAdvance(ref Span<byte> buffer, uint value, int offset = 0)
    {
        buffer = buffer.Slice(offset);
        BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
        buffer = buffer.Slice(sizeof(uint));
    }

    public static void Xor(Span<byte> data, ReadOnlySpan<byte> other)
    {
        Xor(data, other, data);
    }

    public static void Xor(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b, Span<byte> output)
    {
        var i = 0;

#if NET8_0_OR_GREATER
            ref var aRef = ref MemoryMarshal.GetReference(a);
            ref var bRef = ref MemoryMarshal.GetReference(b);
            ref var oRef = ref MemoryMarshal.GetReference(output);

            if (Vector512.IsHardwareAccelerated)
            {
                for (; i <= output.Length - 64; i += 64)
                {
                    (Vector512.LoadUnsafe(ref Unsafe.Add(ref aRef, i)) ^
                     Vector512.LoadUnsafe(ref Unsafe.Add(ref bRef, i)))
                        .StoreUnsafe(ref Unsafe.Add(ref oRef, i));
                }
            }

            if (Vector256.IsHardwareAccelerated)
            {
                for (; i <= output.Length - 32; i += 32)
                {
                    (Vector256.LoadUnsafe(ref Unsafe.Add(ref aRef, i)) ^
                     Vector256.LoadUnsafe(ref Unsafe.Add(ref bRef, i)))
                        .StoreUnsafe(ref Unsafe.Add(ref oRef, i));
                }
            }

            if (Vector128.IsHardwareAccelerated)
            {
                for (; i <= output.Length - 16; i += 16)
                {
                    (Vector128.LoadUnsafe(ref Unsafe.Add(ref aRef, i)) ^
                     Vector128.LoadUnsafe(ref Unsafe.Add(ref bRef, i)))
                        .StoreUnsafe(ref Unsafe.Add(ref oRef, i));
                }
            }
#endif

        for (; i <= output.Length - 8; i += 8)
        {
            BinaryPrimitives.WriteUInt64BigEndian(output.Slice(i, 8),
                BinaryPrimitives.ReadUInt64BigEndian(a.Slice(i, 8)) ^
                BinaryPrimitives.ReadUInt64BigEndian(b.Slice(i, 8)));
        }

        if (i <= output.Length - 4)
        {
            BinaryPrimitives.WriteUInt32BigEndian(output.Slice(i, 4),
                BinaryPrimitives.ReadUInt32BigEndian(a.Slice(i, 4)) ^
                BinaryPrimitives.ReadUInt32BigEndian(b.Slice(i, 4)));
            i += 4;
        }

        if (i <= output.Length - 2)
        {
            BinaryPrimitives.WriteUInt16BigEndian(output.Slice(i, 2),
                (ushort)(BinaryPrimitives.ReadUInt16BigEndian(a.Slice(i, 2)) ^
                         BinaryPrimitives.ReadUInt16BigEndian(b.Slice(i, 2))));
            i += 2;
        }

        if (i < output.Length)
        {
            output[i] = (byte)(a[i] ^ b[i]);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Xor128(Span<byte> data, ReadOnlySpan<byte> other)
    {
#if NET8_0_OR_GREATER
            var result = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(data)) ^
                         Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(other));
            result.StoreUnsafe(ref MemoryMarshal.GetReference(data));
#else
        Xor64(data, other);
        Xor64(data.Slice(8), other.Slice(8));
#endif
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Xor64(Span<byte> data, ReadOnlySpan<byte> other)
    {
        Xor64(data, BinaryPrimitives.ReadUInt64BigEndian(other));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Xor64(Span<byte> data, ulong other)
    {
        BinaryPrimitives.WriteUInt64BigEndian(data, BinaryPrimitives.ReadUInt64BigEndian(data) ^ other);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Xor32(Span<byte> data, ReadOnlySpan<byte> other)
    {
        Xor32(data, BinaryPrimitives.ReadUInt32BigEndian(other));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Xor32(Span<byte> data, uint other)
    {
        BinaryPrimitives.WriteUInt32BigEndian(data, BinaryPrimitives.ReadUInt32BigEndian(data) ^ other);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Xor16(Span<byte> data, ReadOnlySpan<byte> other)
    {
        Xor16(data, BinaryPrimitives.ReadUInt16BigEndian(other));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Xor16(Span<byte> data, ushort other)
    {
        BinaryPrimitives.WriteUInt16BigEndian(data, (ushort)(BinaryPrimitives.ReadUInt16BigEndian(data) ^ other));
    }
}
