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

    public class STUNUri
    {
        public const char SCHEME_ADDR_SEPARATOR = ':';
        public const int SCHEME_MAX_LENGTH = 5;

        public const STUNSchemesEnum DefaultSTUNScheme = STUNSchemesEnum.stun;

        public STUNProtocolsEnum Transport = STUNProtocolsEnum.udp;
        public STUNSchemesEnum Scheme = DefaultSTUNScheme;

        public string Host;
        public int Port;

        /// <summary>
        /// If the port is specified in a URI it affects the way a DNS lookup occurs.
        /// An explicit port means to lookup the A or AAAA record directly without
        /// checking for SRV records.
        /// </summary>
        public bool ExplicitPort;

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

        public STUNUri(STUNSchemesEnum scheme, string host, int port = STUNConstants.DEFAULT_STUN_PORT)
        {
            Scheme = scheme;
            Host = host;
            Port = port;
        }

        public static STUNUri ParseSTUNUri(string uri)
        {
            STUNUri stunUri = new STUNUri();

            if (String.IsNullOrEmpty(uri))
            {
                throw new ApplicationException("A STUN URI cannot be parsed from an empty string.");
            }
            else
            {
                uri = uri.Trim();

                // If the scheme is included it needs to be within the first 5 characters.
                if (uri.Length > SCHEME_MAX_LENGTH + 2)
                {
                    string schemeStr = uri.Substring(0, SCHEME_MAX_LENGTH + 1);
                    int colonPosn = schemeStr.IndexOf(SCHEME_ADDR_SEPARATOR);

                    if (colonPosn == -1)
                    {
                        // No scheme has been specified, use default.
                        stunUri.Scheme = DefaultSTUNScheme;
                    }
                    else
                    {
                        if (!Enum.TryParse<STUNSchemesEnum>(schemeStr.Substring(0, colonPosn), true, out stunUri.Scheme))
                        {
                            stunUri.Scheme = DefaultSTUNScheme;
                        }

                        uri = uri.Substring(colonPosn + 1);
                    }
                }

                if (uri.IndexOf(':') != -1)
                {
                    stunUri.ExplicitPort = true;

                    if (IPSocket.TryParseIPEndPoint(uri, out var ipEndPoint))
                    {
                        if (ipEndPoint.AddressFamily == AddressFamily.InterNetworkV6)
                        {
                            stunUri.Host = $"[{ipEndPoint.Address}]";
                        }
                        else
                        {
                            stunUri.Host = ipEndPoint.Address.ToString();
                        }

                        stunUri.Port = ipEndPoint.Port;
                    }
                    else
                    {
                        stunUri.Host = uri.Substring(0, uri.LastIndexOf(':'));
                        if (!Int32.TryParse(uri.Substring(uri.LastIndexOf(':') + 1), out stunUri.Port))
                        {
                            stunUri.Port = STUNConstants.GetPortForScheme(stunUri.Scheme);
                        }
                    }
                }
                else
                {
                    stunUri.Host = uri?.Trim();
                    stunUri.Port = STUNConstants.GetPortForScheme(stunUri.Scheme);
                }
            }

            return stunUri;
        }

        public static bool TryParse(string uriStr, out STUNUri uri)
        {
            try
            {
                uri = ParseSTUNUri(uriStr);
                return (uri != null);
            }
            catch
            {
                uri = null;
                return false;
            }
        }

        public override string ToString()
        {
            if ((Scheme == STUNSchemesEnum.stun && Port == STUNConstants.DEFAULT_STUN_PORT) ||
                (Scheme == STUNSchemesEnum.turn && Port == STUNConstants.DEFAULT_TURN_PORT) ||
                (Scheme == STUNSchemesEnum.stuns && Port == STUNConstants.DEFAULT_STUN_TLS_PORT) ||
                (Scheme == STUNSchemesEnum.turns && Port == STUNConstants.DEFAULT_TURN_TLS_PORT))
            {
                return $"{Scheme}{SCHEME_ADDR_SEPARATOR}{Host}";
            }
            else
            {
                return $"{Scheme}{SCHEME_ADDR_SEPARATOR}{Host}:{Port}";
            }
        }

        public static bool AreEqual(STUNUri uri1, STUNUri uri2)
        {
            return uri1 == uri2;
        }

        public override bool Equals(object obj)
        {
            return AreEqual(this, (STUNUri)obj);
        }

        public static bool operator ==(STUNUri uri1, STUNUri uri2)
        {
            if (uri1 is null && uri2 is null)
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
            return Scheme.GetHashCode()
                + Transport.GetHashCode()
                + ((Host != null) ? Host.GetHashCode() : 0)
                + Port
                + ((ExplicitPort) ? 1 : 0);
        }
    }
}
