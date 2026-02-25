using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SIPSorcery.Sys;

internal static class MemoryOperations
{
    public static string ToLowerString(this ReadOnlySpan<char> span)
    {
        var buffer = ArrayPool<char>.Shared.Rent(span.Length);

        try
        {
            for (var i = 0; i < span.Length; i++)
            {
                buffer[i] = char.ToLower(span[i]);
            }

            return new string(buffer, 0, span.Length);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }
    }

    public static void ToLittleEndianBytes(this ReadOnlySpan<short> shorts, Span<byte> bytes)
    {
#if NETFRAMEWORK || NETSTANDARD2_0
        if (bytes.Length < shorts.Length * 2)
        {
            throw new ArgumentException("Destination span is too small.", nameof(bytes));
        }

        int byteOffset = 0;
        for (int i = 0; i < shorts.Length; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(bytes.Slice(byteOffset, 2), shorts[i]);
            byteOffset += 2;
        }
#else
        ref var source = ref MemoryMarshal.GetReference(MemoryMarshal.AsBytes(shorts));
        ref var destination = ref MemoryMarshal.GetReference(bytes);

        for (var i = shorts.Length; i > 0; i--)
        {
            var destSpan = MemoryMarshal.CreateSpan(ref destination, 2);
            BinaryPrimitives.WriteInt16LittleEndian(destSpan, source);

            source = ref Unsafe.Add(ref source, 1);
            destination = ref Unsafe.Add(ref destination, 2);
        }
#endif
    }

    public static List<string> SplitToList(this ReadOnlySpan<char> value, char separator)
    {
        var result = new List<string>();

        foreach (var token in value.Split(separator))
        {
            result.Add(value[token].Trim().ToString());
        }

        return result;
    }
}
