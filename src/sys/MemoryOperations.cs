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


    public static byte[] ToLittleEndianBytes(this ReadOnlySpan<short> shorts)
    {
        var bytes = new byte[shorts.Length * 2];

        ref var source = ref MemoryMarshal.GetReference(MemoryMarshal.AsBytes(shorts));
        var destination = bytes.AsSpan();

        for (var i = shorts.Length; i > 0; i--)
        {
            var destSpan = destination.Slice(0, 2);
            BinaryPrimitives.WriteInt16LittleEndian(destSpan, source);

            source = ref Unsafe.Add(ref source, 1);
            destination = destination.Slice(2);
        }

        return bytes;
    }

    public static byte[] ToBigEndianBytes(this ReadOnlySpan<short> shorts)
    {
        var bytes = new byte[shorts.Length * 2];

        ref var source = ref MemoryMarshal.GetReference(MemoryMarshal.AsBytes(shorts));
        var destination = bytes.AsSpan();

        for (var i = shorts.Length; i > 0; i--)
        {
            var destSpan = destination.Slice(0, 2);
            BinaryPrimitives.WriteInt16BigEndian(destSpan, source);

            source = ref Unsafe.Add(ref source, 1);
            destination = destination.Slice(2);
        }

        return bytes;
    }

    public static List<string> SplitToList(this ReadOnlySpan<char> value, char separator)
    {
        var result = new List<string>();

        while (!value.IsEmpty)
        {
            var index = value.IndexOf(separator);

            if (index == -1)
            {
                result.Add(value.ToString());
                break;
            }

            result.Add(value.Slice(0, index).ToString());
            value = value.Slice(index + 1);
        }

        return result;
    }

#if!NET8_0_OR_GREATER
    public static int Split(this ReadOnlySpan<char> source, Span<Range> destination, char separator, StringSplitOptions options = StringSplitOptions.None)
    {
        var count = 0;
        var start = 0;

        while (start <= source.Length)
        {
            var index = source.Slice(start).IndexOf(separator);
            var end = index != -1 ? start + index : source.Length;

            var isEmpty = end == start;
            if (options == StringSplitOptions.RemoveEmptyEntries && isEmpty)
            {
                start = end + 1;
                continue;
            }

            if (count >= destination.Length)
            {
                break;
            }

            destination[count++] = new Range(start, end);
            start = end + 1;
        }

        destination.Slice(count).Clear(); // Clear unused entries
        return count;
    }

    public static int IndexOfAnyExcept(this ReadOnlySpan<char> span, ReadOnlySpan<char> excludedChars)
    {
        for (var i = 0; i < span.Length; i++)
        {
            var current = span[i];
            var isExcluded = false;

            for (var j = 0; j < excludedChars.Length; j++)
            {
                if (current == excludedChars[j])
                {
                    isExcluded = true;
                    break;
                }
            }

            if (!isExcluded)
            {
                return i;
            }
        }

        return -1; // All characters are in the excluded set
    }
#endif
}
