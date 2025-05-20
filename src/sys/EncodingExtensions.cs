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

        public unsafe static int GetBytes(this Encoding encoding, ReadOnlySpan<char> chars, Span<byte> bytes)
        {
            fixed (char* pChars = chars)
            fixed (byte* pBytes = bytes)
            {
                return encoding.GetBytes(pChars, chars.Length, pBytes, bytes.Length);
            }
        }
#endif
    }
}
