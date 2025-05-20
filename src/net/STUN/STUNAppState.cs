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
using SIPSorcery.Sys;

namespace SIPSorcery.Net;

public static class Utility
{
    public static ushort ReverseEndian(ushort val)
    {
        return Convert.ToUInt16(val << 8 & 0xff00 | (val >> 8));
    }

    public static uint ReverseEndian(uint val)
    {
        return Convert.ToUInt32((val << 24 & 0xff000000) | (val << 8 & 0x00ff0000) | (val >> 8 & 0xff00) | (val >> 24));
    }

    public static string? PrintBuffer(byte[] buffer)
    {
        if (buffer.Length == 0)
        {
            return null;
        }

        using var builder = new ValueStringBuilder(stackalloc char[256]);

        for (var index = 0; index < buffer.Length; index++)
        {
            builder.Append(buffer[index], "X2");

            if ((index + 1) % 4 == 0)
            {
                builder.Append('\n');
            }
            else
            {
                builder.Append(" | ");
            }
        }

        return builder.ToString();
    }
}
