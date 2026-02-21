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
            string bufferStr = null;

            for (int index = 0; index < buffer.Length; index++)
            {
                string byteStr = buffer[index].ToString("X");

                if (byteStr.Length == 1)
                {
                    bufferStr += "0" + byteStr;
                }
                else
                {
                    bufferStr += byteStr;
                }

                if ((index + 1) % 4 == 0)
                {
                    bufferStr += "\n";
                }
                else
                {
                    bufferStr += " | ";
                }
            }

            return bufferStr;
        }
    }
}