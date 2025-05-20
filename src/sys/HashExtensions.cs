using System;
using System.Buffers;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace SIPSorcery.Sys
{
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
    }
}
