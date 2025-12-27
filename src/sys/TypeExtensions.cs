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
using System.Diagnostics.CodeAnalysis;
using System.Net;

namespace SIPSorcery.Sys;

public static class TypeExtensions
{
    // The Trim method only trims 0x0009, 0x000a, 0x000b, 0x000c, 0x000d, 0x0085, 0x2028, and 0x2029.
    // This array adds in control characters.
    public static readonly char[] WhiteSpaceChars = [
        (char)0x00, (char)0x01, (char)0x02, (char)0x03, (char)0x04, (char)0x05,
        (char)0x06, (char)0x07, (char)0x08, (char)0x09, (char)0x0a, (char)0x0b, (char)0x0c, (char)0x0d, (char)0x0e, (char)0x0f,
        (char)0x10, (char)0x11, (char)0x12, (char)0x13, (char)0x14, (char)0x15, (char)0x16, (char)0x17, (char)0x18, (char)0x19, (char)0x20,
        (char)0x1a, (char)0x1b, (char)0x1c, (char)0x1d, (char)0x1e, (char)0x1f, (char)0x7f, (char)0x85, (char)0x2028, (char)0x2029
    ];

    private static readonly sbyte[] _hexDigits = [
        -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
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
        -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
        -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
        -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
        -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
        -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
        -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
        -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
        -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
        -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
        -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
        -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
        -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
        -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
        -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
        -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
        -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
        -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
        -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
        -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
        -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
        -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
        -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
        -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
        -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
        -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
        -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
        -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
        -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
        -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
        -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
    ];

    private static readonly char[] hexmap = { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'a', 'b', 'c', 'd', 'e', 'f' };

    /// <summary>    
    /// Gets a value that indicates whether or not the string is empty.    
    /// </summary>    
    public static bool IsNullOrBlank(this string s)
    {
        if (s is null || s.AsSpan().Trim(WhiteSpaceChars).Length == 0)
        {
            return true;
        }

        return false;
    }

    public static bool NotNullOrBlank([NotNullWhen(true)] this string? s)
    {
        if (s is null || s.AsSpan().Trim(WhiteSpaceChars).Length == 0)
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

    public static string HexStr(this byte[] buffer, char? separator = null)
    {
        return HexStr(buffer.AsSpan(), separator: separator, lowercase: false);
    }

    public static string HexStr(this byte[] buffer, int length, char? separator = null)
    {
        return HexStr(buffer.AsSpan(0, buffer.Length), separator: separator, lowercase: false);
    }

    public static string HexStr(this byte[] buffer, int length, char? separator = null, bool lowercase = false)
    {
        return HexStr(buffer.AsSpan(0, length), separator: separator, lowercase: lowercase);
    }

    public static string HexStr(this Span<byte> buffer, char? separator = null, bool lowercase = false)
    {
        return HexStr((ReadOnlySpan<byte>)buffer, separator: separator, lowercase: lowercase);
    }

    public static string HexStr(this ReadOnlySpan<byte> buffer, char? separator = null, bool lowercase = false)
    {
        using var sb = new ValueStringBuilder(stackalloc char[256]);
        sb.Append(buffer, separator, lowercase);
        return sb.ToString();
    }

    public static byte[] ParseHexStr(string hexStr)
    {
#if NET8_0_OR_GREATER
        if (hexStr.AsSpan().ContainsAny(SearchValues.DigitChars))
        {
            return Convert.FromHexString(hexStr);
        }
#else
#if NET5_0_OR_GREATER
        // Check if string contains whitespace
        var hasWhitespace = false;
        for (int i = 0; i < hexStr.Length; i++)
        {
            if (char.IsWhiteSpace(hexStr[i]))
            {
                hasWhitespace = true;
                break;
            }
        }

        if (!hasWhitespace)
        {
            return Convert.FromHexString(hexStr);
        }
#endif
#endif

        // Fallback implementation
        var buffer = new byte[hexStr.Length / 2 + 1];
        var chars = hexStr.AsSpan();
        var posn = 0;
        var bufferIndex = 0;
        while (posn < hexStr.Length)
        {
            while (posn < hexStr.Length && char.IsWhiteSpace(chars[posn]))
            {
                posn++;
            }
            if (posn >= hexStr.Length)
            {
                break;
            }
            var c = _hexDigits[chars[posn++]];
            if (c == -1)
            {
                break;
            }
            var n = (sbyte)(c << 4);
            if (posn >= hexStr.Length)
            {
                break;
            }
            c = _hexDigits[chars[posn++]];
            if (c == -1)
            {
                break;
            }
            n |= c;
            buffer[bufferIndex++] = (byte)n;
        }

        if (bufferIndex < buffer.Length)
        {
            Array.Resize(ref buffer, bufferIndex);
        }

        return buffer;
    }

    public static bool IsPrivate(this IPAddress address)
    {
        return IPSocket.IsPrivateAddress(address.ToString());
    }
}
