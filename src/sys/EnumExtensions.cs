using System.Net.Sockets;

namespace SIPSorcery.Sys;

/// <summary>
/// Extension methods for enumeration types used in the system.
/// </summary>
internal static class EnumExtensions
{
    /// <summary>
    /// Converts a ProtocolType enumeration value to its lowercase string representation.
    /// </summary>
    /// <param name="protocolType">The ProtocolType enumeration value to convert.</param>
    /// <returns>A lowercase string representation of the protocol type. For most protocols,
    /// returns the standard abbreviated form (e.g. "tcp", "udp", "ipv6"). For unrecognized
    /// protocol types, returns the enum value converted to lowercase.</returns>
    /// <remarks>
    /// This method provides standardized string representations for network protocols,
    /// particularly useful for logging, configuration, and protocol-specific formatting needs.
    /// </remarks>
    public static string ToLowerString(this ProtocolType protocolType)
    {
        return protocolType switch
        {
            ProtocolType.IP => "ip",

            ProtocolType.Icmp => "icmp",
            ProtocolType.Igmp => "igmp",
            ProtocolType.Ggp => "ggp",

            ProtocolType.IPv4 => "ipv4",
            ProtocolType.Tcp => "tcp",
            ProtocolType.Pup => "pup",
            ProtocolType.Udp => "udp",
            ProtocolType.Idp => "idp",
            ProtocolType.IPv6 => "ipv6",
            ProtocolType.IPv6RoutingHeader => "routing",
            ProtocolType.IPv6FragmentHeader => "fragment",
            ProtocolType.IPSecEncapsulatingSecurityPayload => "ipsecencapsulatingsecuritypayload",
            ProtocolType.IPSecAuthenticationHeader => "ipsecauthenticationheader",
            ProtocolType.IcmpV6 => "icmpv6",
            ProtocolType.IPv6NoNextHeader => "nonext",
            ProtocolType.IPv6DestinationOptions => "dstopts",
            ProtocolType.ND => "nd",
            ProtocolType.Raw => "raw",

            ProtocolType.Ipx => "ipx",
            ProtocolType.Spx => "spx",
            ProtocolType.SpxII => "spx2",
            ProtocolType.Unknown => "unknown",

            _ => protocolType.ToString().ToLowerInvariant()
        };
    }
}
