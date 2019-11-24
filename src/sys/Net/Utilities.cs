//-----------------------------------------------------------------------------
// Filename: Utilities.cs
//
// Description: Useful functions for VoIP protocol implementation.
//
// Author(s):
// Aaron Clauson
//
// History:
// 23 May 2005	Aaron Clauson	Created (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com)
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;

namespace SIPSorcery.Sys
{
    public class NetConvert
    {
        public static UInt16 DoReverseEndian(UInt16 x)
        {
            return Convert.ToUInt16((x << 8 & 0xff00) | (x >> 8));
        }

        public static uint DoReverseEndian(uint x)
        {
            return (x << 24 | (x & 0xff00) << 8 | (x & 0xff0000) >> 8 | x >> 24);
        }

        public static ulong DoReverseEndian(ulong x)
        {
            return (x << 56 | (x & 0xff00) << 40 | (x & 0xff0000) << 24 | (x & 0xff000000) << 8 | (x & 0xff00000000) >> 8 | (x & 0xff0000000000) >> 24 | (x & 0xff000000000000) >> 40 | x >> 56);
        }
    }
}
