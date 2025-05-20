using System;
using System.Net;
using System.Runtime.CompilerServices;

namespace SIPSorcery.Sys
{
    internal static class IPAddressExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryParse(ReadOnlySpan<char> ipString, out IPAddress address)
        {
#if NETCOREAPP2_1_OR_GREATER || NETCOREAPP3_1_OR_GREATER
            return IPAddress.TryParse(ipString, out address);
#else
            return IPAddress.TryParse(ipString.ToString(), out address);
#endif
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryParse(string ipString, out IPAddress address)
        {
            return IPAddress.TryParse(ipString, out address);
        }
    }
}
