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
using System.Net.Sockets;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    public class STUNConstants
    {
        public const int DEFAULT_STUN_PORT = 3478;
        public const int DEFAULT_STUN_TLS_PORT = 5349;
        public const int DEFAULT_TURN_PORT = 3478;
        public const int DEFAULT_TURN_TLS_PORT = 5349;

        public static int GetPortForScheme(STUNSchemesEnum scheme)
        {
            switch (scheme)
            {
                case STUNSchemesEnum.stun:
                    return DEFAULT_STUN_PORT;
                case STUNSchemesEnum.stuns:
                    return DEFAULT_STUN_TLS_PORT;
                case STUNSchemesEnum.turn:
                    return DEFAULT_TURN_PORT;
                case STUNSchemesEnum.turns:
                    return DEFAULT_TURN_TLS_PORT;
                default:
                    throw new ApplicationException("STUN or TURN scheme not recognised in STUNConstants.GetPortForScheme.");
            }
        }
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

        public static readonly string SCHEME_TRANSPORT_SEPARATOR = "?transport=";
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
                if (Transport == STUNProtocolsEnum.tcp || Transport == STUNProtocolsEnum.tls)
                {
                    return ProtocolType.Tcp;
                }
                else
                {
                    return ProtocolType.Udp;
                }
            }
        }

        private STUNUri()
        { }

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
                throw new ApplicationException("A STUN URI cannot be parsed from an empty string.");
            }

            return uri;
        }

        public static bool TryParse(string uriStr, out STUNUri uri)
        {
            uri = null;

            if (string.IsNullOrEmpty(uriStr))
            {
                return false;
            }

            ReadOnlySpan<char> uriSpan = uriStr.AsSpan();
            STUNProtocolsEnum transport = STUNProtocolsEnum.udp;

            // Handle transport protocol
            int transportIndex = uriSpan.IndexOf('?');
            if (transportIndex >= 0 && uriSpan.Slice(transportIndex, SCHEME_TRANSPORT_SEPARATOR.Length).SequenceEqual(SCHEME_TRANSPORT_SEPARATOR.AsSpan()))
            {
                var protocolSpan = uriSpan.Slice(transportIndex + SCHEME_TRANSPORT_SEPARATOR.Length).Trim();
#if NET6_0_OR_GREATER
                if (!protocolSpan.IsEmpty && !Enum.TryParse(protocolSpan, true, out transport))
#else
                if (!protocolSpan.IsEmpty && !Enum.TryParse(protocolSpan.ToString(), true, out transport))
#endif
                {
                    transport = STUNProtocolsEnum.udp;
                }
                uriSpan = uriSpan.Slice(0, transportIndex);
            }

            uriSpan = uriSpan.Trim();
            var scheme = DefaultSTUNScheme;

            // Handle scheme parsing
            if (uriSpan.Length > SCHEME_MAX_LENGTH + 2)
            {
                ReadOnlySpan<char> schemeSpan = uriSpan.Slice(0, SCHEME_MAX_LENGTH + 1);
                int colonPosn = schemeSpan.IndexOf(SCHEME_ADDR_SEPARATOR);

                if (colonPosn >= 0)
                {
#if NET6_0_OR_GREATER
                    if (!Enum.TryParse(schemeSpan.Slice(0, colonPosn), true, out scheme))
#else
                    if (!Enum.TryParse(schemeSpan.Slice(0, colonPosn).ToString(), true, out scheme))
#endif
                    {
                        scheme = DefaultSTUNScheme;
                    }
                    uriSpan = uriSpan.Slice(colonPosn + 1);
                }
            }

            var explicitPort = false;
            int port;
            string host;

            int lastColonPos = uriSpan.LastIndexOf(':');
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
                    host = uriSpan.Slice(0, lastColonPos).ToString();
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
                    if (!int.TryParse(uriSpan.Slice(lastColonPos + 1), out port))
#else
                    if (!int.TryParse(uriSpan.Slice(lastColonPos + 1).ToString(), out port))
#endif
                    {
                        port = STUNConstants.GetPortForScheme(scheme);
                    }
                }
            }
            else
            {
                host = uriSpan.ToString();
                port = STUNConstants.GetPortForScheme(scheme);
            }

            uri = new STUNUri(scheme, host, port: port, transport: transport, explicitPort: explicitPort);
            return true;
        }

        public override string ToString()
        {
            if ((Scheme == STUNSchemesEnum.stun && Port == STUNConstants.DEFAULT_STUN_PORT) ||
                (Scheme == STUNSchemesEnum.turn && Port == STUNConstants.DEFAULT_TURN_PORT) ||
                (Scheme == STUNSchemesEnum.stuns && Port == STUNConstants.DEFAULT_STUN_TLS_PORT) ||
                (Scheme == STUNSchemesEnum.turns && Port == STUNConstants.DEFAULT_TURN_TLS_PORT))
            {
                if (Protocol != ProtocolType.Udp)
                {
                    return $"{Scheme}{SCHEME_ADDR_SEPARATOR}{Host}?transport={Protocol.ToString().ToLower()}";
                }
                else
                {
                    return $"{Scheme}{SCHEME_ADDR_SEPARATOR}{Host}";
                }
            }
            else
            {
                if (Protocol != ProtocolType.Udp)
                {
                    return $"{Scheme}{SCHEME_ADDR_SEPARATOR}{Host}:{Port}?transport={Protocol.ToString().ToLower()}";
                }
                else
                {
                    return $"{Scheme}{SCHEME_ADDR_SEPARATOR}{Host}:{Port}";
                }
            }
        }

        public static bool AreEqual(STUNUri uri1, STUNUri uri2)
        {
            return uri1 == uri2;
        }

        public bool Equals(STUNUri other)
        {
            return (this == other);
        }

        public override bool Equals(object obj)
        {
            return Equals(this, (STUNUri)obj);
        }

        public static bool operator ==(STUNUri uri1, STUNUri uri2)
        {
            if (object.ReferenceEquals(uri1, uri2))
            {
                return true;
            }
            else if (uri1 is null || uri2 is null)
            {
                return false;
            }
            else if (uri1.Host == null || uri2.Host == null)
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

        public static bool operator !=(STUNUri x, STUNUri y)
        {
            return !(x == y);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Scheme, Transport, Host, Port, ExplicitPort);
        }
    }
}
