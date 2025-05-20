using System;
using System.Text;

namespace SIPSorcery.Sys;

internal static class EncodingExtensions
{
    extension(global::System.Text.Encoding encoding)
    {
        /// <summary>
        /// Checks if the encoding of the given string matches the provided bytes.
        /// </summary>
        /// <param name="str">The string to encode and compare.</param>
        /// <param name="bytes">The byte sequence to compare against the encoded string.</param>
        /// <returns>
        /// <c>true</c> if the encoded bytes of <paramref name="str"/> exactly match <paramref name="bytes"/>; otherwise, <c>false</c>.
        /// </returns>
        public bool Equals(string? str, ReadOnlySpan<byte> bytes)
        {
            if (str is null)
            {
                return false;
            }

            var byteCount = encoding.GetByteCount(str);
            if (byteCount != bytes.Length)
            {
                return false;
            }

            var charSpan = str.AsSpan();

            if (byteCount <= 1024)
            {
                return EqualsCore(encoding, charSpan, stackalloc byte[byteCount], bytes);
            }
            else
            {
                var pool = System.Buffers.ArrayPool<byte>.Shared;
                var rented = pool.Rent(byteCount);
                try
                {
                    return EqualsCore(encoding, charSpan, rented.AsSpan(0, byteCount), bytes);
                }
                finally
                {
                    pool.Return(rented);
                }
            }

            static bool EqualsCore(Encoding encoding, ReadOnlySpan<char> charSpan, Span<byte> strBytes, ReadOnlySpan<byte> bytes)
            {
                encoding.GetBytes(charSpan, strBytes);
                return strBytes.SequenceEqual(bytes);
            }
        }
    }
}
