//-----------------------------------------------------------------------------
// Filename: BufferUtils.cs
//
// Description: Provides some useful methods for working with byte[] buffers.
//
// Author(s):
// Aaron Clauson
//
// History:
// 04 May 2006	Aaron Clauson	Created.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Text;

namespace SIPSorcery.Sys
{
    public class BufferUtils
    {
        /// <summary>
        /// Searches a buffer for a string up until a specified end string.
        /// </summary>
        /// <param name="buffer">The byte array to search for an instance of the specified string.</param>
        /// <param name="startPosition">The position in the array that the search should be started from.</param>
        /// <param name="endPosition">An index that if reached indicates the search should be halted.</param>
        /// <param name="find">The string that is being searched for.</param>
        /// <param name="end">If the end string is found the search is halted and a negative result returned.</param>
        /// <returns>The start position in the buffer of the requested string or -1 if not found.</returns>
        public static int GetStringPosition(byte[] buffer, int startPosition, int endPosition, string find, string end)
        {
            if (buffer == null || buffer.Length == 0 || find == null)
            {
                return -1;
            }
            else
            {
                byte[] findArray = Encoding.UTF8.GetBytes(find);
                byte[] endArray = (end != null) ? Encoding.UTF8.GetBytes(end) : null;

                int findPosn = 0;
                int endPosn = 0;

                for (int index = startPosition; index < endPosition && index < buffer.Length; index++)
                {
                    if (buffer[index] == findArray[findPosn])
                    {
                        findPosn++;
                    }
                    else
                    {
                        findPosn = 0;
                    }

                    if (endArray != null && buffer[index] == endArray[endPosn])
                    {
                        endPosn++;
                    }
                    else
                    {
                        endPosn = 0;
                    }

                    if (findPosn == findArray.Length)
                    {
                        return index - findArray.Length + 1;
                    }
                    else if (endArray != null && endPosn == endArray.Length)
                    {
                        return -1;
                    }
                }

                return -1;
            }
        }

        public static bool HasString(byte[] buffer, int startPosition, int endPosition, string find, string end)
        {
            return GetStringPosition(buffer, startPosition, endPosition, find, end) != -1;
        }

        public static byte[] ParseHexStr(string hex)
        {
            return TypeExtensions.ParseHexStr(hex);
        }

        public static string HexStr(byte[] buffer)
        {
            return buffer.HexStr();
        }
    }
}
