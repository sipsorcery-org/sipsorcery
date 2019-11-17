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
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;

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

        /// <summary>    
        /// Gets a value that indicates whether or not the collection is empty.    
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

        public static long GetEpoch(this DateTime dateTime)
        {
            var unixTime = dateTime.ToUniversalTime() -
                new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            return Convert.ToInt64(unixTime.TotalSeconds);
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
        /// <returns>A slice fo the input string or null if the slcie is not possible.</returns>
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

                if(endPosn > startPosn)
                {
                    return s.Substring(startPosn + 1, endPosn - startPosn);
                }
                else
                {
                    return null;
                }
            }
        }
    }
}
