//-----------------------------------------------------------------------------
// Filename: SipDestination.cs
//
// Description: Parses the SIP destination argument shared by the sip verbs.
// Accepts both SIP URIs (sip:100@host, music@host) and serialised SIP end
// points (udp:host:port, tls:host). The same convention as the sipcmdline
// example.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 12 Jun 2026	Aaron Clauson	Created, Wexford, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using SIPSorcery.SIP;

namespace SIPSorcery.Cli.Commands;

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
            error = $"Could not parse \"{destination}\" as a SIP URI or end point.";
            return false;
        }
    }

    private static bool HasTransportPrefix(string destination) =>
        destination.StartsWith("udp:", StringComparison.OrdinalIgnoreCase) ||
        destination.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase) ||
        destination.StartsWith("tls:", StringComparison.OrdinalIgnoreCase) ||
        destination.StartsWith("ws:", StringComparison.OrdinalIgnoreCase) ||
        destination.StartsWith("wss:", StringComparison.OrdinalIgnoreCase);
}
