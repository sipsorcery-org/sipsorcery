using System;
using System.Runtime.InteropServices;

namespace SIPSorcery.Sys
{
    internal ref partial struct ValueStringBuilder
    {
        /// <summary>
        /// Character array for uppercase hexadecimal representation (0-9, A-F).
        /// </summary>
        private static readonly char[] upperHexmap = { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'A', 'B', 'C', 'D', 'E', 'F' };

        /// <summary>
        /// Character array for lowercase hexadecimal representation (0-9, a-f).
        /// </summary>
        private static readonly char[] lowerHexmap = { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'a', 'b', 'c', 'd', 'e', 'f' };

        /// <summary>
        /// Appends a byte array to the string builder as hexadecimal characters.
        /// </summary>
        /// <param name="bytes">The byte array to append. Can be null.</param>
        /// <param name="separator">Optional separator character to insert between bytes.</param>
        public void Append(byte[]? bytes, char? separator = null)
        {
            if (bytes is { Length: > 0 })
            {
                Append(bytes.AsSpan(), separator);
            }
        }

        /// <summary>
        /// Appends a span of bytes to the string builder as hexadecimal characters.
        /// </summary>
        /// <param name="bytes">The span of bytes to append.</param>
        /// <param name="separator">Optional separator character to insert between bytes.</param>
        /// <param name="lowercase">If true, uses lowercase hex characters (a-f); if false, uses uppercase (A-F).</param>
        /// <remarks>
        /// Each byte is converted to two hexadecimal characters. If a separator is specified,
        /// it will be inserted between each pair of hex characters representing a byte.
        /// For example, with separator '-': "AA-BB-CC"
        /// </remarks>
        public void Append(ReadOnlySpan<byte> bytes, char? separator = null, bool lowercase = false)
        {
            var hexmap = lowercase ? lowerHexmap : upperHexmap;

            if (bytes.IsEmpty)
            {
                return;
            }

            if (separator is { } s)
            {
                for (int i = 0; i < bytes.Length;)
                {
                    var b = bytes[i];
                    Append(hexmap[(int)b >> 4]);
                    Append(hexmap[(int)b & 0b1111]);
                    if (++i < bytes.Length)
                    {
                        Append(s);
                    }
                }
            }
            else
            {
                for (var i = 0; i < bytes.Length; i++)
                {
                    var b = bytes[i];
                    Append(hexmap[(int)b >> 4]);
                    Append(hexmap[(int)b & 0b1111]);
                }
            }
        }
    }
}
