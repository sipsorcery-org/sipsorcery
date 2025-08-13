using System;
using System.Text;

namespace SIPSorcery.Sys
{
    internal static class EncodingExtensions
    {
#if NETSTANDARD2_0 || NETFRAMEWORK
        public unsafe static string GetString(this Encoding encoding, ReadOnlySpan<byte> bytes)
        {
            fixed (byte* ptr = bytes)
            {
                return encoding.GetString(ptr, bytes.Length);
            }
        }

        public unsafe static int GetBytes(this Encoding encoding, Span<char> chars, Span<byte> bytes)
        {
            return encoding.GetBytes((ReadOnlySpan<char>)chars, bytes);
        }

        public unsafe static int GetBytes(this Encoding encoding, ReadOnlySpan<char> chars, Span<byte> bytes)
        {
            fixed (char* pChars = chars)
            fixed (byte* pBytes = bytes)
            {
                return encoding.GetBytes(pChars, chars.Length, pBytes, bytes.Length);
            }
        }

        public unsafe static int GetByteCount(this Encoding encoding, ReadOnlySpan<char> chars)
        {
            fixed (char* pChars = chars)
            {
                return encoding.GetByteCount(pChars, chars.Length);
            }
        }
#endif

        /// <summary>
        /// Checks if the encoding of the given string matches the provided bytes.
        /// </summary>
        /// <param name="encoding">The <see cref="Encoding"/> to use for comparison.</param>
        /// <param name="str">The string to encode and compare.</param>
        /// <param name="bytes">The byte sequence to compare against the encoded string.</param>
        /// <returns>
        /// <c>true</c> if the encoded bytes of <paramref name="str"/> exactly match <paramref name="bytes"/>; otherwise, <c>false</c>.
        /// </returns>
        public static bool Equals(this Encoding encoding, string? str, ReadOnlySpan<byte> bytes)
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

            if (byteCount <= 256)
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
                    pool.Return(rented, clearArray: true);
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
