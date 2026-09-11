//-----------------------------------------------------------------------------
// Filename: SipDestination.cs
//
// Description: Parses the SIP destination argument shared by the sip verbs.
// Accepts both SIP URIs (sip:100@host, music@host) and serialised SIP end
// points (udp:host:port, tls:host). The same convention as the sipcmdline
// example.
//
// Lives in SIPSorcery.Cli.Common (a global using in both the diagnostics and
// the flagship CLI) because both tools now parse SIP destinations: the
// diagnostics "sip" verbs and the flagship "route" verb's sip: source edge.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 12 Jun 2026	Aaron Clauson	Created, Wexford, Ireland.
// 23 Jun 2026	Aaron Clauson	Moved from SIPSorcery.Diagnostics to the shared
//                              Cli.Common so the route sip: edge can use it.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using SIPSorcery.SIP;

namespace SIPSorcery.Cli.Common;

public static class SipDestination
{
    public static bool TryParse(string destination, out SIPURI uri, out string? error)
    {
        uri = SIPURI.None;
        error = null;

        try
        {
            // SIPURI.TryParse is lenient, e.g. it accepts host names containing spaces, so apply
            // a sanity check to route nonsense to an invalid argument error rather than a DNS failure.
            if (!HasTransportPrefix(destination) && SIPURI.TryParse(destination, out var parsedUri)
                && !string.IsNullOrWhiteSpace(parsedUri.Host) && !parsedUri.Host.Contains(' '))
            {
                uri = parsedUri;
                return true;
            }

            var endPoint = SIPEndPoint.ParseSIPEndPoint(destination);
            uri = new SIPURI(SIPSchemesEnum.sip, endPoint);
            return true;
        }
        catch
        {
            // A transport prefix on a host NAME rather than an address, which is the form anyone
            // reaches for against a real server: "tls:sip.example.com", "wss:sip.example.com:443".
            // SIPEndPoint.ParseSIPEndPoint above only takes an address, so it threw. Both are worth
            // supporting, and for tls and wss the name is the more useful one - it is what has to
            // appear in the server's certificate for the connection to validate.
            if (TryParseTransportAndHost(destination, out uri))
            {
                return true;
            }

            error = $"Could not parse \"{destination}\" as a SIP URI or end point.";
            return false;
        }
    }

    /// <summary>
    /// Reads "transport:host" and "transport:host:port" where the host may be a name.
    /// </summary>
    /// <remarks>
    /// The result keeps the sip scheme and carries the transport as a URI parameter rather than
    /// switching to sips. The scheme travels into the address of record the caller builds from
    /// this, and an account registered as "sip:alice@domain" is not the same as one registered as
    /// "sips:alice@domain"; the transport parameter says everything the transport layer needs
    /// without touching identity.
    /// </remarks>
    private static bool TryParseTransportAndHost(string destination, out SIPURI uri)
    {
        uri = SIPURI.None;

        int separator = destination.IndexOf(':');

        if (separator <= 0 || separator == destination.Length - 1)
        {
            return false;
        }

        if (!Enum.TryParse<SIPProtocolsEnum>(destination[..separator], true, out var protocol))
        {
            return false;
        }

        string host = destination[(separator + 1)..];

        if (string.IsNullOrWhiteSpace(host) || host.Contains(' '))
        {
            return false;
        }

        uri = new SIPURI(null, host, null, SIPSchemesEnum.sip, protocol);
        return true;
    }

    private static bool HasTransportPrefix(string destination) =>
        destination.StartsWith("udp:", StringComparison.OrdinalIgnoreCase) ||
        destination.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase) ||
        destination.StartsWith("tls:", StringComparison.OrdinalIgnoreCase) ||
        destination.StartsWith("ws:", StringComparison.OrdinalIgnoreCase) ||
        destination.StartsWith("wss:", StringComparison.OrdinalIgnoreCase);
}
