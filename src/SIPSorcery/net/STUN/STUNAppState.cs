// ============================================================================
// FileName: STUNLog.cs
//
// Description:
//  Holds application configuration information.
//
// Author(s):
//	Aaron Clauson
//
// History:
// 27 Dec 2006	Aaron Clauson	Created (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com).
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
// ============================================================================

using System;
using System.Globalization;
using System.Text;

namespace SIPSorcery.Net
{
    public class Utility
    {
        public static UInt16 ReverseEndian(UInt16 val)
        {
            return Convert.ToUInt16(val << 8 & 0xff00 | (val >> 8));
        }

        public static UInt32 ReverseEndian(UInt32 val)
        {
            return Convert.ToUInt32((val << 24 & 0xff000000) | (val << 8 & 0x00ff0000) | (val >> 8 & 0xff00) | (val >> 24));
        }

        public static string PrintBuffer(byte[] buffer)
        {
            if (buffer.Length == 0)
            {
                return null;
            }

            var fullGroups = buffer.Length / 4;
            var remainder = buffer.Length % 4;
            var outputLength = (fullGroups * 18) + (remainder * 5);

            return string.Create(
                outputLength,
                buffer,
                static (output, bytes) =>
                {
                    const string UPPER_HEX_DIGITS = "0123456789ABCDEF";
                    var outputIndex = 0;

                    for (var index = 0; index < bytes.Length; index++)
                    {
                        var value = bytes[index];
                        output[outputIndex++] = UPPER_HEX_DIGITS[value >> 4];
                        output[outputIndex++] = UPPER_HEX_DIGITS[value & 0x0f];

                        if ((index + 1) % 4 == 0)
                        {
                            output[outputIndex++] = '\n';
                        }
                        else
                        {
                            output[outputIndex++] = ' ';
                            output[outputIndex++] = '|';
                            output[outputIndex++] = ' ';
                        }
                    }
                });
        }
    }
}
