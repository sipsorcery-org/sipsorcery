//-----------------------------------------------------------------------------
// Filename: HexStr.cs
//
// Description: Helper method to load test frames to and from hex strings.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 04 Nov 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Collections.Generic;

namespace Vpx.Net.UnitTest
{
    public static class HexStr
    {
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

        public static string ToHexStr(this byte[] buffer, char? separator = null)
        {
            return buffer.ToHexStr(buffer.Length, separator);
        }

        public static string ToHexStr(this byte[] buffer, int length, char? separator = null)
        {
            string rv = string.Empty;

            for (int i = 0; i < length; i++)
            {
                var val = buffer[i];
                rv += hexmap[val >> 4];
                rv += hexmap[val & 15];

                if (separator != null && i != length - 1)
                {
                    rv += separator;
                }
            }

            return rv.ToLower();
        }

        public static byte[] ParseHexStr(string hexStr)
        {
            List<byte> buffer = new List<byte>();
            var chars = hexStr.ToLower().ToCharArray();
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
    }
}
