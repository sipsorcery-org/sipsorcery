//-----------------------------------------------------------------------------
// Filename: HepCapture.cs
//
// Description: Mirrors all SIP traffic on a SIPTransport to a HEPv3 (Homer
// Encapsulation Protocol) capture server such as HOMER or heplify-server
// (sipcapture.org), which renders the requests and responses as SIP call
// ladder diagrams. Each SIP message in or out is duplicated into a HEP
// packet, stamped with the original source and destination end points, and
// fired at the capture server over UDP. Capture is passive: failures are
// logged at debug level and never affect the SIP operation being mirrored.
//
// The --hep option value follows the same packed form as --turn:
//   host[:port][;password[;agentId]]
// e.g. --hep homer.example.com  or  --hep "192.168.0.10:9060;myHep;42".
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 13 Jun 2026	Aaron Clauson	Created, Wexford, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.CommandLine;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorcery.SIP;

namespace SIPSorcery.Diagnostics.Commands;

public sealed class HepCapture : IDisposable
{
    private const int DEFAULT_PORT = 9060;          // Standard HOMER/heplify-server HEP listen port.
    private const uint DEFAULT_AGENT_ID = 4242;     // Arbitrary; identifies this CLI to the capture server.

    private readonly UdpClient _client;
    private readonly IPEndPoint _server;
    private readonly uint _agentId;
    private readonly string? _password;
    private readonly ILogger _logger;

    private int _packetsMirrored;

    public int PacketsMirrored => _packetsMirrored;

    private HepCapture(IPEndPoint server, uint agentId, string? password, ILogger logger)
    {
        _server = server;
        _agentId = agentId;
        _password = password;
        _logger = logger;
        _client = new UdpClient(0, server.AddressFamily);
    }

    public static Option<string?> CreateOption() => new("--hep")
    {
        Description = "Mirror the SIP traffic to a HEPv3 capture server (HOMER/heplify-server) so the call shows " +
                      "up as a ladder diagram, as host[:port][;password[;agentId]]. The port defaults to 9060. " +
                      "HOMER's default capture password is \"myHep\"; omit it if the server does not use one."
    };

    /// <summary>
    /// Parses the --hep option value and creates the capture sender. Returns null with no error
    /// when no value was supplied, and null with an error message when the value is invalid.
    /// </summary>
    public static HepCapture? Create(string? spec, ILogger logger, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(spec))
        {
            return null;
        }

        string[] fields = spec.Split(';');

        string host = fields[0];
        int port = DEFAULT_PORT;
        int hostPortSep = host.LastIndexOf(':');
        if (hostPortSep != -1 && host.IndexOf(':') == hostPortSep) // A single colon; bare IPv6 has many.
        {
            if (!int.TryParse(host[(hostPortSep + 1)..], out port) || port <= 0 || port > ushort.MaxValue)
            {
                error = $"Could not parse a port from the --hep value \"{spec}\".";
                return null;
            }
            host = host[..hostPortSep];
        }

        string? password = fields.Length > 1 && fields[1].Length > 0 ? fields[1] : null;

        uint agentId = DEFAULT_AGENT_ID;
        if (fields.Length > 2 && !uint.TryParse(fields[2], out agentId))
        {
            error = $"Could not parse a numeric agent ID from the --hep value \"{spec}\".";
            return null;
        }

        IPAddress? address;
        try
        {
            address = IPAddress.TryParse(host, out var literal)
                ? literal
                : Dns.GetHostAddresses(host).OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1).FirstOrDefault();
        }
        catch (SocketException)
        {
            address = null;
        }

        if (address == null)
        {
            error = $"Could not resolve the HEP capture server host \"{host}\".";
            return null;
        }

        return new HepCapture(new IPEndPoint(address, port), agentId, password, logger);
    }

    /// <summary>
    /// Hooks the transport's trace events so every SIP message in or out is mirrored. The HEP
    /// source/destination are the original SIP end points, which is what lets the capture
    /// server reconstruct the ladder.
    /// </summary>
    public void Attach(SIPTransport transport)
    {
        transport.SIPRequestOutTraceEvent += (localEP, remoteEP, req) => Send(localEP, remoteEP, req.ToString());
        transport.SIPRequestInTraceEvent += (localEP, remoteEP, req) => Send(remoteEP, localEP, req.ToString());
        transport.SIPResponseOutTraceEvent += (localEP, remoteEP, resp) => Send(localEP, remoteEP, resp.ToString());
        transport.SIPResponseInTraceEvent += (localEP, remoteEP, resp) => Send(remoteEP, localEP, resp.ToString());

        Console.Error.WriteLine($"Mirroring SIP traffic to HEP capture server udp:{_server} (agent ID {_agentId}).");
    }

    private void Send(SIPEndPoint srcEndPoint, SIPEndPoint dstEndPoint, string payload)
    {
        try
        {
            var buffer = HepPacket.GetBytes(srcEndPoint, dstEndPoint, DateTime.Now, _agentId, _password, payload);
            _client.Send(buffer, buffer.Length, _server);
            Interlocked.Increment(ref _packetsMirrored);
        }
        catch (Exception excp)
        {
            _logger.LogDebug("HEP capture send failed: {Error}", excp.Message);
        }
    }

    public void Dispose()
    {
        _logger.LogDebug("HEP capture mirrored {PacketsMirrored} SIP messages to {Server}.", _packetsMirrored, _server);
        _client.Dispose();
    }
}
