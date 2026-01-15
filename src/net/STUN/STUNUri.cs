//-----------------------------------------------------------------------------
// Filename: STUNUri.cs
//
// Description: Represents the STUN and TURN URI schemes and constants
// as specified in:
// https://tools.ietf.org/html/rfc7064: URI Scheme for the Session Traversal Utilities for NAT (STUN) Protocol
// https://tools.ietf.org/html/rfc7065: Traversal Using Relays around NAT (TURN) Uniform Resource Identifiers
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 08 Jun 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;
using SIPSorcery.Sys;

namespace SIPSorcery.Net;

public static class STUNConstants
{
    public const int DEFAULT_STUN_PORT = 3478;
    public const int DEFAULT_STUN_TLS_PORT = 5349;
    public const int DEFAULT_TURN_PORT = 3478;
    public const int DEFAULT_TURN_TLS_PORT = 5349;

    public static int GetPortForScheme(STUNSchemesEnum scheme)
        => scheme switch
        {
            STUNSchemesEnum.stun or STUNSchemesEnum.turn => DEFAULT_TURN_PORT,
            STUNSchemesEnum.stuns or STUNSchemesEnum.turns => DEFAULT_TURN_TLS_PORT,
            _ => throw new SipSorceryException("STUN or TURN scheme not recognised in STUNConstants.GetPortForScheme."),
        };

    public static STUNProtocolsEnum GetTransportForScheme(STUNSchemesEnum scheme)
        => scheme switch
        {
            STUNSchemesEnum.stun or STUNSchemesEnum.turn => STUNProtocolsEnum.udp,
            STUNSchemesEnum.stuns or STUNSchemesEnum.turns => STUNProtocolsEnum.tls,
            _ => throw new SipSorceryException("STUN or TURN scheme not recognised in STUNConstants.GetTransportForScheme."),
        };
}

public enum STUNSchemesEnum
{
    stun = 0,
    stuns = 1,
    turn = 2,
    turns = 3
}

/// <summary>
/// A list of the transport layer protocols that are supported by STUNand TURN (the network layers
/// supported are IPv4 mad IPv6).
/// </summary>
public enum STUNProtocolsEnum
{
    /// <summary>
    /// User Datagram Protocol.
    /// </summary>
    udp = 1,
    /// <summary>.
    /// Transmission Control Protocol
    /// </summary>
    tcp = 2,
    /// <summary>
    /// Transport Layer Security.
    /// </summary>
    tls = 3,
    /// <summary>
    /// Transport Layer Security over UDP.
    /// </summary>
    dtls = 4,
}

public sealed class STUNUri : IEquatable<STUNUri>
{
    public const string SCHEME_TRANSPORT_TCP = "transport=tcp";
    public const string SCHEME_TRANSPORT_TLS = "transport=tls";

    public static readonly string SCHEME_TRANSPORT_SEPARATOR = "transport=";
    public const char SCHEME_ADDR_SEPARATOR = ':';
    public const int SCHEME_MAX_LENGTH = 5;

    public const STUNSchemesEnum DefaultSTUNScheme = STUNSchemesEnum.stun;

    public STUNProtocolsEnum Transport { get; } = STUNProtocolsEnum.udp;
    public STUNSchemesEnum Scheme { get; } = DefaultSTUNScheme;

    public string Host { get; }
    public int Port { get; }

    /// <summary>
    /// If the port is specified in a URI it affects the way a DNS lookup occurs.
    /// An explicit port means to lookup the A or AAAA record directly without
    /// checking for SRV records.
    /// </summary>
    public bool ExplicitPort { get; }

    /// <summary>
    /// The network protocol for this URI type.
    /// </summary>
    public ProtocolType Protocol
    {
        get
        {
            if (Transport is STUNProtocolsEnum.tcp or STUNProtocolsEnum.tls)
            {
                return ProtocolType.Tcp;
            }
            else
            {
                return ProtocolType.Udp;
            }
        }
    }

    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public STUNUri(STUNSchemesEnum scheme, string host, int port)
    {
        Scheme = scheme;
        Host = host;
        Port = port;
    }

    public STUNUri(STUNSchemesEnum scheme, string host, int port = STUNConstants.DEFAULT_STUN_PORT, STUNProtocolsEnum transport = STUNProtocolsEnum.udp, bool explicitPort = false)
    {
        Scheme = scheme;
        Host = host;
        Port = port;
        Transport = transport;
        ExplicitPort = explicitPort;
    }

    public static STUNUri ParseSTUNUri(string uriStr)
    {
        if (!TryParse(uriStr, out var uri))
        {
            throw new FormatException($"A STUN URI cannot be parsed from an empty 'uriStr'.");
        }

        return uri;
    }

    public static bool TryParse(string uriStr, [NotNullWhen(true)] out STUNUri? uri)
    {
        if (string.IsNullOrEmpty(uriStr))
        {
            uri = null;
            return false;
        }

        return TryParse(uriStr.AsSpan(), out uri);
    }

    public static bool TryParse(ReadOnlySpan<char> uriSpan, [NotNullWhen(true)] out STUNUri? uri)
    {
        uri = null;

        uriSpan = uriSpan.Trim();
        ReadOnlySpan<char> querySpan;
        if ((uriSpan.IndexOf('?') is { } queryStart) && queryStart >= 0)
        {
            querySpan = uriSpan.Slice(queryStart + 1);
            uriSpan = uriSpan.Slice(0, queryStart);
        }
        else
        {
            querySpan = ReadOnlySpan<char>.Empty;
        }

        var scheme = DefaultSTUNScheme;

        // Handle scheme parsing

        if (uriSpan.StartsWith("stun:".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            scheme = STUNSchemesEnum.stun;
            uriSpan = uriSpan.Slice(5);
        }
        else if (uriSpan.StartsWith("stuns:".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            scheme = STUNSchemesEnum.stuns;
            uriSpan = uriSpan.Slice(6);
        }
        else if (uriSpan.StartsWith("turn:".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            scheme = STUNSchemesEnum.turn;
            uriSpan = uriSpan.Slice(5);
        }
        else if (uriSpan.StartsWith("turns:".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            scheme = STUNSchemesEnum.turns;
            uriSpan = uriSpan.Slice(6);
        }

        if (uriSpan.IsEmpty)
        {
            return false;
        }

        var explicitPort = false;
        int port;
        string host;

        var lastColonPos = uriSpan.LastIndexOf(':');
        if (lastColonPos != -1)
        {
            explicitPort = true;

            if (IPSocket.TryParseIPEndPoint(uriSpan, out var ipEndPoint))
            {
                if (ipEndPoint.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    host = $"[{ipEndPoint.Address}]";
                }
                else
                {
                    host = ipEndPoint.Address.ToString();
                }

                port = ipEndPoint.Port;
            }
            else
            {
                if (
                     !int.TryParse(uriSpan.Slice(lastColonPos + 1), out port)
                     || port <= 0 || port > 65535)
                {
                    return false;
                }

                var hostSpan = uriSpan.Slice(0, lastColonPos);

                if (hostSpan.IsEmpty || hostSpan.IndexOfAny(SearchValues.InvalidHostNameChars) >= 0)
                {
                    return false;
                }

                host = hostSpan.ToLowerString();
            }
        }
        else
        {
            if (uriSpan.IsEmpty || uriSpan.IndexOfAny(SearchValues.InvalidHostNameChars) >= 0)
            {
                return false;
            }

            host = uriSpan.ToLowerString();

            port = STUNConstants.GetPortForScheme(scheme);
        }

        var transport = STUNConstants.GetTransportForScheme(scheme);

        // Handle transport protocol
        if (!querySpan.IsEmpty)
        {
            if (querySpan.StartsWith(SCHEME_TRANSPORT_SEPARATOR.AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                var protocolSpan = querySpan.Slice(SCHEME_TRANSPORT_SEPARATOR.Length).Trim();
                if (protocolSpan.IsEmpty || !STUNProtocolsEnumExtensions.TryParse(protocolSpan, out transport, true))
                {
                    return false;
                }
            }
            else
            {
                return false;
            }
        }

        uri = new STUNUri(scheme, host, port: port, transport: transport, explicitPort: explicitPort);
        return true;
    }

    public override string ToString()
    {
        using var sb = new ValueStringBuilder(stackalloc char[256]);

        sb.Append(Scheme.ToStringFast());
        sb.Append(SCHEME_ADDR_SEPARATOR);
        sb.Append(Host);

        if ((Scheme == STUNSchemesEnum.stun && Port != STUNConstants.DEFAULT_STUN_PORT) ||
            (Scheme == STUNSchemesEnum.turn && Port != STUNConstants.DEFAULT_TURN_PORT) ||
            (Scheme == STUNSchemesEnum.stuns && Port != STUNConstants.DEFAULT_STUN_TLS_PORT) ||
            (Scheme == STUNSchemesEnum.turns && Port != STUNConstants.DEFAULT_TURN_TLS_PORT))
        {
            sb.Append(SCHEME_ADDR_SEPARATOR);
            sb.Append(Port);
        }

        if (((Scheme is STUNSchemesEnum.stun or STUNSchemesEnum.turn) && Transport != STUNProtocolsEnum.udp) ||
            ((Scheme is STUNSchemesEnum.stuns or STUNSchemesEnum.turns) && Transport != STUNProtocolsEnum.tls))
        {
            sb.Append('?');
            sb.Append(SCHEME_TRANSPORT_SEPARATOR);
            sb.Append(Transport.ToStringFast());
        }

        return sb.ToString();
    }

    public static bool AreEqual(STUNUri uri1, STUNUri uri2)
    {
        return uri1 == uri2;
    }

    public bool Equals(STUNUri? other)
    {
        return (this == other);
    }

    public override bool Equals(object? obj)
    {
        return Equals(this, (STUNUri?)obj);
    }

    public static bool operator ==(STUNUri? uri1, STUNUri? uri2)
    {
        if (object.ReferenceEquals(uri1, uri2))
        {
            return true;
        }
        else if (uri1 is null || uri2 is null)
        {
            return false;
        }
        else if (uri1.Host is null || uri2.Host is null)
        {
            return false;
        }
        else if (uri1.Scheme != uri2.Scheme)
        {
            return false;
        }
        else if (uri1.Transport != uri2.Transport)
        {
            return false;
        }
        else if (uri1.Port != uri2.Port)
        {
            return false;
        }
        else if (uri1.ExplicitPort != uri2.ExplicitPort)
        {
            return false;
        }

        return true;
    }

    public static bool operator !=(STUNUri? x, STUNUri? y)
    {
        return !(x == y);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Scheme, Transport, Host, Port, ExplicitPort);
    }
}
