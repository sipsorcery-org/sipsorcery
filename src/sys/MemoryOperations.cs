using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace SIPSorcery.Sys;

internal static class MemoryOperations
{
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
}
