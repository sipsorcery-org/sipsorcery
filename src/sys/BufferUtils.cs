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

using System;
using System.Text;

namespace SIPSorcery.Sys;

public class BufferUtils
{
    public static byte[] ParseHexStr(string hex)
    {
        return TypeExtensions.ParseHexStr(hex);
    }

    public static string HexStr(byte[] buffer)
    {
        return buffer.HexStr();
    }
}
