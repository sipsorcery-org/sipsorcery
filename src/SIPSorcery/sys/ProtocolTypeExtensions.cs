namespace System.Net.Sockets;

internal static class ProtocolTypeExtensions
{
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
