//-----------------------------------------------------------------------------
// Filename: TypeExtensions.cs
//
// Description: Helper methods.
//
// Author(s):
// Aaron Clauson
//
// History:
// ??	Aaron Clauson	Created.
// 21 Jan 2020  Aaron Clauson   Added HexStr and ParseHexStr (borrowed from
//                              Bitcoin Core source).
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;

namespace SIPSorcery.Sys
{
    public static class TypeExtensions
    {
        // The Trim method only trims 0x0009, 0x000a, 0x000b, 0x000c, 0x000d, 0x0085, 0x2028, and 0x2029.
        // This array adds in control characters.
        public static readonly char[] WhiteSpaceChars = new char[] { (char)0x00, (char)0x01, (char)0x02, (char)0x03, (char)0x04, (char)0x05,
            (char)0x06, (char)0x07, (char)0x08, (char)0x09, (char)0x0a, (char)0x0b, (char)0x0c, (char)0x0d, (char)0x0e, (char)0x0f,
            (char)0x10, (char)0x11, (char)0x12, (char)0x13, (char)0x14, (char)0x15, (char)0x16, (char)0x17, (char)0x18, (char)0x19, (char)0x20,
            (char)0x1a, (char)0x1b, (char)0x1c, (char)0x1d, (char)0x1e, (char)0x1f, (char)0x7f, (char)0x85, (char)0x2028, (char)0x2029 };

        private static readonly sbyte[] _hexDigits =
            { -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
              -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
              -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
              0,1,2,3,4,5,6,7,8,9,-1,-1,-1,-1,-1,-1,
              -1,0xa,0xb,0xc,0xd,0xe,0xf,-1,-1,-1,-1,-1,-1,-1,-1,-1,
              -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
              -1,0xa,0xb,0xc,0xd,0xe,0xf,-1,-1,-1,-1,-1,-1,-1,-1,-1,
              -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
              -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
              -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
              -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
              -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
              -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
              -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
              -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
              -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1, };

        private static readonly char[] hexmap = { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'a', 'b', 'c', 'd', 'e', 'f' };

        /// <summary>    
        /// Gets a value that indicates whether or not the string is empty.    
        /// </summary>    
        public static bool IsNullOrBlank(this string s)
        {
            if (s == null || s.Trim(WhiteSpaceChars).Length == 0)
            {
                return true;
            }

            return false;
        }

        public static bool NotNullOrBlank(this string s)
        {
            if (s == null || s.Trim(WhiteSpaceChars).Length == 0)
            {
                return false;
            }

            return true;
        }

        [Obsolete("Use ToUnixTime.")]
        public static long GetEpoch(this DateTime dateTime)
        {
            var unixTime = dateTime.ToUniversalTime() -
                new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            return Convert.ToInt64(unixTime.TotalSeconds);
        }

        public static long ToUnixTime(this DateTime dateTime)
        {
            return new DateTimeOffset(dateTime.ToUniversalTime()).ToUnixTimeSeconds();
        }

        /// <summary>
        /// Returns a slice from a string that is delimited by the first instance of a 
        /// start and end character. The delimiting characters are not included.
        /// 
        /// <code>
        /// "sip:127.0.0.1:5060;connid=1234".slice(':', ';') => "127.0.0.1:5060"
        /// </code>
        /// </summary>
        /// <param name="s">The input string to extract the slice from.</param>
        /// <param name="startDelimiter">The character to start the slice from. The first instance of the character found is used.</param>
        /// <param name="endDelimeter">The character to end the slice on. The first instance of the character found is used.</param>
        /// <returns>A slice of the input string or null if the slice is not possible.</returns>
        public static string Slice(this string s, char startDelimiter, char endDelimeter)
        {
            if (String.IsNullOrEmpty(s))
            {
                return null;
            }
            else
            {
                int startPosn = s.IndexOf(startDelimiter);
                int endPosn = s.IndexOf(endDelimeter) - 1;

                if (endPosn > startPosn)
                {
                    return s.Substring(startPosn + 1, endPosn - startPosn);
                }
                else
                {
                    return null;
                }
            }
        }

        public static string HexStr(this byte[] buffer, char? separator = null)
        {
            return buffer.HexStr(buffer.Length, separator);
        }

        public static string HexStr(this byte[] buffer, int length, char? separator = null)
        {
            if (separator is { } s)
            {
                int numberOfChars = length * 3 - 1;
#if NETCOREAPP2_1_OR_GREATER || NETSTANDARD2_1_OR_GREATER
                return string.Create(numberOfChars, (buffer, length, s), PopulateNewStringWithSeparator);
#else
                var rv = new char[numberOfChars];
                PopulateNewStringWithSeparator(rv, (buffer, length, s));
                return new string(rv);
#endif
            }
            else
            {
                int numberOfChars = length * 2;
#if NETCOREAPP2_1_OR_GREATER || NETSTANDARD2_1_OR_GREATER
                return string.Create(numberOfChars, (buffer, length), PopulateNewStringWithoutSeparator);
#else
                var rv = new char[numberOfChars];
                PopulateNewStringWithoutSeparator(rv, (buffer, length));
                return new string(rv);
#endif
            }

            static void PopulateNewStringWithSeparator(Span<char> chars, (byte[] buffer, int length, char separator) state)
            {
                var (buffer, length, s) = state;
                for (int i = 0, j = 0; i < length; i++)
                {
                    var val = buffer[i];
                    chars[j++] = char.ToUpperInvariant(hexmap[val >> 4]);
                    chars[j++] = char.ToUpperInvariant(hexmap[val & 15]);
                    if (j < chars.Length)
                    {
                        chars[j++] = s;
                    }
                }
            }

            static void PopulateNewStringWithoutSeparator(Span<char> chars, (byte[] buffer, int length) state)
            {
                var (buffer, length) = state;
                for (int i = 0, j = 0; i < length; i++)
                {
                    var val = buffer[i];
                    chars[j++] = char.ToUpperInvariant(hexmap[val >> 4]);
                    chars[j++] = char.ToUpperInvariant(hexmap[val & 15]);
                }
            }
        }

        public static byte[] ParseHexStr(string hexStr)
        {
            List<byte> buffer = new List<byte>();
            var chars = hexStr.ToCharArray();
            int posn = 0;
            while (posn < hexStr.Length)
            {
                while (char.IsWhiteSpace(chars[posn]))
                {
                    posn++;
                }
                sbyte c = _hexDigits[chars[posn++]];
                if (c == -1)
                {
                    break;
                }
                sbyte n = (sbyte)(c << 4);
                c = _hexDigits[chars[posn++]];
                if (c == -1)
                {
                    break;
                }
                n |= c;
                buffer.Add((byte)n);
            }
            return buffer.ToArray();
        }

        //#if NET472 || NETSTANDARD2_0
        public static void Deconstruct<T1, T2>(this KeyValuePair<T1, T2> tuple, out T1 key, out T2 value)
        {
            key = tuple.Key;
            value = tuple.Value;
        }
        //#endif

        public static bool IsPrivate(this IPAddress address)
        {
            return IPSocket.IsPrivateAddress(address.ToString());
        }


        /// <summary>
        /// Purpose of this extension is to allow deconstruction of a list into a fixed size tuple.
        /// </summary>
        /// <example>
        /// (var field0, var field1) = "a b c".Split();
        /// </example>
        public static void Deconstruct<T>(this IList<T> list, out T first, out T second)
        {
            first = list.Count > 0 ? list[0] : default(T);
            second = list.Count > 1 ? list[1] : default(T);
        }

        /// <summary>
        /// Purpose of this extension is to allow deconstruction of a list into a fixed size tuple.
        /// </summary>
        /// <example>
        /// (var field0, var field1, var field2) = "a b c".Split();
        /// </example>
        public static void Deconstruct<T>(this IList<T> list, out T first, out T second, out T third)
        {
            first = list.Count > 0 ? list[0] : default(T);
            second = list.Count > 1 ? list[1] : default(T);
            third = list.Count > 2 ? list[2] : default(T);
        }

        /// <summary>
        /// Purpose of this extension is to allow deconstruction of a list into a fixed size tuple.
        /// </summary>
        /// <example>
        /// (var field0, var field1, var field2, var field3) = "a b c d".Split();
        /// </example>
        public static void Deconstruct<T>(this IList<T> list, out T first, out T second, out T third, out T fourth)
        {
            first = list.Count > 0 ? list[0] : default(T);
            second = list.Count > 1 ? list[1] : default(T);
            third = list.Count > 2 ? list[2] : default(T);
            fourth = list.Count > 3 ? list[3] : default(T);
        }
    }
}
