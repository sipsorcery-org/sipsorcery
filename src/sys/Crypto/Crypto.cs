//-----------------------------------------------------------------------------
// Filename: Crypto.cs
//
// Description: Encrypts and decrypts data.
//
// Author(s):
// Aaron Clauson
//
// History:
// 16 Jul 2005	Aaron Clauson	Created.
// 10 Sep 2009  Aaron Clauson   Updated to use RNGCryptoServiceProvider in place of Random.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Threading;

namespace SIPSorcery.Sys;

public static class Crypto
{
    // TODO: When .NET Standard and Framework support are deprecated these pragmas can be removed.
#pragma warning disable SYSLIB0001
#pragma warning disable SYSLIB0021
#pragma warning disable SYSLIB0023

    public const int DEFAULT_RANDOM_LENGTH = 10;    // Number of digits to return for default random numbers.
    public const int AES_KEY_SIZE = 32;
    public const int AES_IV_SIZE = 16;
    private const string CHARS = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    private static int seed = Environment.TickCount;

    private static readonly ThreadLocal<Random> random =
        new ThreadLocal<Random>(() => new Random(Interlocked.Increment(ref seed)));

    public static int Rand(int maxValue)
    {
        var random = Crypto.random.Value;
        Debug.Assert(random is { });
        return random.Next(maxValue);
    }

    private static RNGCryptoServiceProvider m_randomProvider = new RNGCryptoServiceProvider();

    public static string GetRandomString(int length)
    {
        var buffer = new char[length];

        for (var i = 0; i < length; i++)
        {
            buffer[i] = CHARS[Rand(CHARS.Length)];
        }
        return new string(buffer);
    }

    /// <summary>
    /// Returns a random number of a specified length.
    /// </summary>
    public static int GetRandomInt(int length)
    {
        var randomStart = 1000000000;
        var randomEnd = int.MaxValue;

        if (length is > 0 and < DEFAULT_RANDOM_LENGTH)
        {
            randomStart = Convert.ToInt32(Math.Pow(10, length - 1));
            randomEnd = Convert.ToInt32(Math.Pow(10, length) - 1);
        }

        return GetRandomInt(randomStart, randomEnd);
    }

    public static int GetRandomInt(int minValue, int maxValue)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minValue, maxValue);

        if (minValue == maxValue)
        {
            return minValue;
        }

        long diff = maxValue - minValue + 1;
        var attempts = 0;
        while (attempts < 10)
        {
            var uint32Buffer = new byte[4];
            m_randomProvider.GetBytes(uint32Buffer);
            var rand = BitConverter.ToUInt32(uint32Buffer, 0);

            var max = (1 + (long)uint.MaxValue);
            var remainder = max % diff;
            if (rand <= max - remainder)
            {
                return (int)(minValue + (rand % diff));
            }
            attempts++;
        }

        throw new SipSorceryException("GetRandomInt did not return an appropriate random number within 10 attempts.");
    }

    public static ushort GetRandomUInt16()
    {
        var uint16Buffer = new byte[2];
        m_randomProvider.GetBytes(uint16Buffer);
        return BitConverter.ToUInt16(uint16Buffer, 0);
    }

    public static uint GetRandomUInt(bool noZero = false)
    {
        var uint32Buffer = new byte[4];
        m_randomProvider.GetBytes(uint32Buffer);
        var randomUint = BitConverter.ToUInt32(uint32Buffer, 0);

        if (noZero && randomUint == 0)
        {
            m_randomProvider.GetBytes(uint32Buffer);
            randomUint = BitConverter.ToUInt32(uint32Buffer, 0);
        }

        return randomUint;
    }

    public static ulong GetRandomULong()
    {
        var uint64Buffer = new byte[8];
        m_randomProvider.GetBytes(uint64Buffer);
        return BitConverter.ToUInt64(uint64Buffer, 0);
    }

    /// <summary>
    /// Fills a buffer with random bytes.
    /// </summary>
    /// <param name="buffer">The buffer to fill.</param>
    public static void GetRandomBytes(byte[] buffer)
    {
        m_randomProvider.GetBytes(buffer);
    }
}

#pragma warning restore SYSLIB0001
#pragma warning restore SYSLIB0021
#pragma warning restore SYSLIB0023

