using System;
using System.Buffers.Binary;

namespace SIPSorcery.Sys;

internal static class BinaryOperations
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
}
