using System.Security.Cryptography;
using SIPSorcery.Sys;

namespace SIPSorcery.UnitTests;

// TODO: When .NET Standard and Framework support are deprecated these pragmas can be removed.
#pragma warning disable SYSLIB0021
internal class CryptoHelper
{
    /// <summary>
    /// Gets the HSA256 hash of an arbitrary buffer.
    /// </summary>
    /// <param name="buffer">The buffer to hash.</param>
    /// <returns>A hex string representing the hashed buffer.</returns>
    public static string GetSHA256Hash(byte[] buffer)
    {
        using var sha256 = new SHA256Managed();
        return sha256.ComputeHash(buffer).HexStr();
    }
}
#pragma warning restore SYSLIB0021
