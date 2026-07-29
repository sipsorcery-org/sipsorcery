using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace SIPSorcery.Sys;

internal static class HashExtensions
{
    public static byte[] ComputeHash(this HashAlgorithm hashAlgorithm, ReadOnlySpan<byte> buffer)
    {
        var tempBuffer = ArrayPool<byte>.Shared.Rent(buffer.Length);
        try
        {
            buffer.CopyTo(tempBuffer);
            return hashAlgorithm.ComputeHash(tempBuffer, 0 , buffer.Length);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(tempBuffer);
        }
    }

    public static byte[] ComputeHash(this HashAlgorithm hashAlgorithm, ReadOnlyMemory<byte> buffer)
    {
        if (MemoryMarshal.TryGetArray(buffer, out var segment))
        {
            return hashAlgorithm.ComputeHash(segment.Array!, segment.Offset, segment.Count);
        }

        return hashAlgorithm.ComputeHash(buffer.Span);
    }
}
