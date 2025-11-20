using System;
using System.Text;

namespace SIPSorcery.Sys
{
    /// <summary>
    /// Extension methods for <see cref="Encoding"/>.
    /// </summary>
    internal static class EncodingExtensions
    {
#if NETSTANDARD2_0 || NETFRAMEWORK
        /// <summary>
        /// Decodes a sequence of bytes from a read-only span into a string.
        /// </summary>
        /// <param name="encoding">The encoding to use for the conversion.</param>
        /// <param name="bytes">The span containing the sequence of bytes to decode.</param>
        /// <returns>A string containing the decoded characters.</returns>
        public unsafe static string GetString(this Encoding encoding, ReadOnlySpan<byte> bytes)
        {
            fixed (byte* ptr = bytes)
            {
                return encoding.GetString(ptr, bytes.Length);
            }
        }

        /// <summary>
        /// Encodes a set of characters from a read-only span into a sequence of bytes.
        /// </summary>
        /// <param name="encoding">The encoding to use for the conversion.</param>
        /// <param name="chars">The span containing the set of characters to encode.</param>
        /// <param name="bytes">The span to contain the resulting sequence of bytes.</param>
        /// <returns>The actual number of bytes written into the byte span.</returns>
        public unsafe static int GetBytes(this Encoding encoding, ReadOnlySpan<char> chars, Span<byte> bytes)
        {
            fixed (char* pChars = chars)
            fixed (byte* pBytes = bytes)
            {
                return encoding.GetBytes(pChars, chars.Length, pBytes, bytes.Length);
            }
        }

        /// <summary>
        /// Calculates the number of bytes needed to encode a set of characters.
        /// </summary>
        /// <param name="encoding">The encoding to use for the calculation.</param>
        /// <param name="chars">The span containing the set of characters to encode.</param>
        /// <returns>The number of bytes needed to encode the specified characters.</returns>
        public unsafe static int GetByteCount(this Encoding encoding, ReadOnlySpan<char> chars)
        {
            fixed (char* pChars = chars)
            {
                return encoding.GetByteCount(pChars, chars.Length);
            }
        }
#endif
    }
}
