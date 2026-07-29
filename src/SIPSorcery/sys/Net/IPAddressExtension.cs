using System;

namespace System.Net;

internal static class IPAddressExtension
{
    extension(IPAddress)
    {
        public static IPAddress Create(ReadOnlySpan<byte> address)
            =>
#if NETSTANDARD2_1 || NETCOREAPP2_1_OR_GREATER
                new IPAddress(address)
#else
                new IPAddress(address.ToArray())
#endif
                ;
    }
}
