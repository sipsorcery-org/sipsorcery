//-----------------------------------------------------------------------------
// Filename: IPAddressHelper.cs
//
// Description: This class contains helper functions related to IPAddress for WebRTC Candidate
//
// Author(s):
// Rafael Soares (raf.csoares@kyubinteractive.com)
//
// History:
// 16 Mar 2022	Rafael Soares   Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Net;


namespace SIPSorcery.Net
{
    public static class IPAddressHelper
    {
        // Prefixes used for categorizing IPv6 addresses.
        static byte[] kV4MappedPrefix = new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0xFF, 0xFF, 0 };
        static byte[] k6To4Prefix = new byte[] { 0x20, 0x02, 0 };
        static byte[] kTeredoPrefix = new byte[] { 0x20, 0x01, 0x00, 0x00 };
        static byte[] kV4CompatibilityPrefix = new byte[] { 0 };
        static byte[] k6BonePrefix = new byte[] { 0x3f, 0xfe, 0 };
        static byte[] kPrivateNetworkPrefix = new byte[] { 0xFD };

        public static uint IPAddressPrecedence(IPAddress ip)
        {
            try
            {
                // Precedence values from RFC 3484-bis. Prefers native v4 over 6to4/Teredo.
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    return 30;
                }
                else if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                {
                    if (IPAddress.IsLoopback(ip))
                    {
                        return 60;
                    }
                    // A unique local address (ULA) is an IPv6 address in the block fc00::/7, defined in RFC 4193.
                    // It is the IPv6 counterpart of the IPv4 private address.
                    // Unique local addresses are available for use in private networks, e.g. inside a single site
                    // or organisation, or spanning a limited number of sites or organisations.
                    // They are not routable in the global IPv6 Internet.
                    else if (ip.IsIPv6SiteLocal)
                    {
                        return 50;
                    }
                    else if (ip.IsIPv4MappedToIPv6)
                    {
                        return 30;
                    }
                    else if (IPIs6To4(ip))
                    {
                        return 20;
                    }
                    // In computer networking, Teredo is a transition technology that gives full IPv6
                    // connectivity for IPv6-capable hosts which are on the IPv4 Internet but which have
                    // no direct native connection to an IPv6 network. Compared to other similar protocols
                    // its distinguishing feature is that it is able to perform its function even from behind
                    // network address translation (NAT) devices such as home routers.
                    else if (ip.IsIPv6Teredo)
                    {
                        return 10;
                    }
                    else if (IPIsV4Compatibility(ip) || IPIsSiteLocal(ip) || IPIs6Bone(ip))
                    {
                        return 1;
                    }
                    else
                    {
                        // A 'normal' IPv6 address.
                        return 40;
                    }
                }
            }
            catch { }

            return 0;
        }

        public static bool IPIsLinkLocalV4(IPAddress ip) 
        {
            try
            {
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    long address = BitConverter.ToInt64(ip.GetAddressBytes(), 0);
                    long ip_in_host_order = IPAddress.NetworkToHostOrder(address);
                    return ((ip_in_host_order >> 16) == ((169 << 8) | 254));
                }
            }
            catch { }
            return false;
        }

        public static bool IPIs6Bone(IPAddress ip) {
            return IPIsHelper(ip, k6BonePrefix, 16);
        }

        public static bool IPIsSiteLocal(IPAddress ip) {
            try
            {
                // Can't use the helper because the prefix is 10 bits.
                ip = ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? ip : ip.MapToIPv6();
                byte[] addr = ip.GetAddressBytes();
                return addr[0] == 0xFE && (addr[1] & 0xC0) == 0xC0;
            }
            catch { }
            return false;
        }

        public static bool IPIsV4Compatibility (IPAddress ip) {
            return IPIsHelper(ip, kV4CompatibilityPrefix, 96);
        }

        public static bool IPIs6To4(IPAddress ip) {
            return IPIsHelper(ip, k6To4Prefix, 16);
        }

        static bool IPIsHelper(IPAddress ip, byte[] tomatch, int lengthInBits)
        {
            try
            {
                // Helper method for checking IP prefix matches (but only on whole byte
                // lengths). Length is in bits.
                byte[] addr = ip.GetAddressBytes();
                var bytesToCompare = (lengthInBits >> 3);

                if (addr == null || addr.Length < bytesToCompare || tomatch == null || tomatch.Length < bytesToCompare)
                    return false;

                for (int i = 0; i < bytesToCompare; i++)
                {
                    if (addr[i] != tomatch[i])
                        return false;
                }
                return true;
            }
            catch { }

            return false;
        }
    }
}
