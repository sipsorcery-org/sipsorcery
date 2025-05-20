//-----------------------------------------------------------------------------
// Filename: RTCIceCandidate.cs
//
// Description: Represents a candidate used in the Interactive Connectivity 
// Establishment (ICE) negotiation to set up a usable network connection 
// between two peers as per RFC8445 https://tools.ietf.org/html/rfc8445
// (previously implemented for RFC5245 https://tools.ietf.org/html/rfc5245).
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 26 Feb 2016	Aaron Clauson	Created, Hobart, Australia.
// 15 Mar 2020  Aaron Clauson   Updated for RFC8445.
// 17 Mar 2020  Aaron Clauson   Renamed from IceCandidate to RTCIceCandidate.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Sockets;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    public class RTCIceCandidate : IRTCIceCandidate
    {
        public const string m_CRLF = "\r\n";
        public const string TCP_TYPE_KEY = "tcpType";
        public const string REMOTE_ADDRESS_KEY = "raddr";
        public const string REMOTE_PORT_KEY = "rport";
        public const string CANDIDATE_PREFIX = "candidate";

        /// <summary>
        /// The ICE server (STUN or TURN) the candidate was generated from.
        /// Will be null for non-ICE server candidates.
        /// </summary>
        public IceServer IceServer { get; internal set; }

        public string candidate => ToString();

        public string sdpMid { get; set; }

        public ushort sdpMLineIndex { get; set; }

        /// <summary>
        /// Composed of 1 to 32 chars. It is an
        /// identifier that is equivalent for two candidates that are of the
        /// same type, share the same base, and come from the same STUN
        /// server.
        /// </summary>
        /// <remarks>
        /// See https://tools.ietf.org/html/rfc8445#section-5.1.1.3.
        /// </remarks>
        public string foundation { get; set; }

        /// <summary>
        ///  Is a positive integer between 1 and 256 (inclusive)
        /// that identifies the specific component of the data stream for
        /// which this is a candidate.
        /// </summary>
        public RTCIceComponent component { get; set; } = RTCIceComponent.rtp;

        /// <summary>
        /// A positive integer between 1 and (2**31 - 1) inclusive.
        /// This priority will be used by ICE to determine the order of the
        /// connectivity checks and the relative preference for candidates.
        /// Higher-priority values give more priority over lower values.
        /// </summary>
        /// <remarks>
        /// See specification at https://tools.ietf.org/html/rfc8445#section-5.1.2.
        /// </remarks>
        public uint priority { get; set; }

        /// <summary>
        /// The address or hostname for the candidate.
        /// </summary>
        public string address { get; set; }

        /// <summary>
        /// The transport protocol for the candidate, supported options are UDP and TCP.
        /// </summary>
        public RTCIceProtocol protocol { get; set; }

        /// <summary>
        /// The local port the candidate is listening on.
        /// </summary>
        public ushort port { get; set; }

        /// <summary>
        /// The type of ICE candidate, host, srflx etc.
        /// </summary>
        public RTCIceCandidateType type { get; set; }

        /// <summary>
        /// For TCP candidates the role they are fulfilling (client, server or both).
        /// </summary>
        public RTCIceTcpCandidateType tcpType { get; set; }

        public string? relatedAddress { get; set; }

        public ushort relatedPort { get; set; }

        public string usernameFragment { get; set; }

        /// <summary>
        /// This is the end point to use for a remote candidate. The address supplied for an ICE
        /// candidate could be a hostname or IP address. This field will be set before the candidate
        /// is used.
        /// </summary>
        public IPEndPoint DestinationEndPoint { get; private set; }

        private RTCIceCandidate()
        { }

        public RTCIceCandidate(RTCIceCandidateInit init)
        {
            sdpMid = init.sdpMid;
            sdpMLineIndex = init.sdpMLineIndex;
            usernameFragment = init.usernameFragment;

            if (!String.IsNullOrEmpty(init.candidate))
            {
                var iceCandidate = Parse(init.candidate);
                foundation = iceCandidate.foundation;
                priority = iceCandidate.priority;
                component = iceCandidate.component;
                address = iceCandidate.address;
                port = iceCandidate.port;
                type = iceCandidate.type;
                tcpType = iceCandidate.tcpType;
                relatedAddress = iceCandidate.relatedAddress;
                relatedPort = iceCandidate.relatedPort;
            }
        }

        /// <summary>
        /// Convenience constructor for cases when the application wants
        /// to create an ICE candidate,
        /// </summary>
        public RTCIceCandidate(
            RTCIceProtocol cProtocol,
            IPAddress cAddress,
            ushort cPort,
            RTCIceCandidateType cType)
        {
            SetAddressProperties(cProtocol, cAddress, cPort, cType, null, 0);
        }

        public void SetAddressProperties(
            RTCIceProtocol cProtocol,
            IPAddress cAddress,
            ushort cPort,
            RTCIceCandidateType cType,
            IPAddress? cRelatedAddress,
            ushort cRelatedPort)
        {
            protocol = cProtocol;
            address = cAddress.ToString();
            port = cPort;
            type = cType;
            relatedAddress = cRelatedAddress?.ToString();
            relatedPort = cRelatedPort;

            foundation = GetFoundation();
            priority = GetPriority();
        }

        public static RTCIceCandidate Parse(string candidateLine)
        {
            ArgumentExceptionExtensions.ThrowIfNullOrWhiteSpace(candidateLine);

            var span = candidateLine.AsSpan();
            const string prefix = "candidate:";
            if (span.StartsWith(prefix.AsSpan(), StringComparison.Ordinal))
            {
                span = span.Slice(prefix.Length);
            }

            var candidate = new RTCIceCandidate();

            candidate.foundation = NextField(ref span).ToString();

            if (RTCIceComponentExtensions.TryParse(NextField(ref span), out var component))
            {
                candidate.component = component;
            }

            if (RTCIceProtocolExtensions.TryParse(NextField(ref span), out var protocol))
            {
                candidate.protocol = protocol;
            }

            if (UInt32.TryParse(NextField(ref span), out var priority))
            {
                candidate.priority = priority;
            }

            candidate.address = NextField(ref span).ToString();
            candidate.port = UInt16.Parse(NextField(ref span));

            _ = NextField(ref span); // skip "typ"

            if (RTCIceCandidateTypeExtensions.TryParse(NextField(ref span), out var type))
            {
                candidate.type = type;
            }

            while (!span.IsEmpty)
            {
                var key = NextField(ref span);

                if (key.Equals(TCP_TYPE_KEY.AsSpan(), StringComparison.Ordinal))
                {
                    candidate.relatedAddress = NextField(ref span).ToString();
                }
                else if (key.Equals(REMOTE_ADDRESS_KEY.AsSpan(), StringComparison.Ordinal))
                {
                    candidate.relatedAddress = NextField(ref span).ToString();
                }
                else if (key.Equals(REMOTE_PORT_KEY.AsSpan(), StringComparison.Ordinal))
                {
                    candidate.relatedPort = UInt16.Parse(NextField(ref span));
                }
                else
                {
                    _ = NextField(ref span); // skip unknown key-value pair
                }
            }

            return candidate;

            static ReadOnlySpan<char> NextField(ref ReadOnlySpan<char> span)
            {
                var spaceIndex = span.IndexOf(' ');

                ReadOnlySpan<char> field;
                if (spaceIndex == -1)
                {
                    field = span;
                    span = ReadOnlySpan<char>.Empty;
                }
                else
                {
                    field = span.Slice(0, spaceIndex);
                    span = span.Slice(spaceIndex + 1);
                }

                return field.Trim();
            }
        }

        /// <summary>
        /// Serialises an ICE candidate to a string that's suitable for inclusion in an SDP session
        /// description payload.
        /// </summary>
        /// <remarks>
        /// The specification regarding how an ICE candidate should be serialised in SDP is at
        /// https://tools.ietf.org/html/draft-ietf-mmusic-ice-sip-sdp-39#section-5.1.   
        /// </remarks>
        /// <returns>A string representing the ICE candidate suitable for inclusion in an SDP session
        /// description.</returns>
        public override string ToString()
        {
            using var sb = new ValueStringBuilder(stackalloc char[256]);

            sb.Append(foundation);
            sb.Append(' ');
            sb.Append((int)component);
            sb.Append(' ');

            if (protocol == RTCIceProtocol.tcp)
            {
                sb.Append("tcp ");
            }
            else
            {
                sb.Append("udp ");
            }

            sb.Append(priority);
            sb.Append(' ');
            sb.Append(address);
            sb.Append(' ');
            sb.Append(port);
            sb.Append(" typ ");
            sb.Append(type.ToStringFast());

            if (protocol == RTCIceProtocol.tcp)
            {
                sb.Append(" tcptype ");
                sb.Append(tcpType.ToStringFast());
            }

            if (type is not RTCIceCandidateType.host and not RTCIceCandidateType.prflx)
            {
                var relAddr = string.IsNullOrWhiteSpace(relatedAddress) ? IPAddress.Any.ToString() : relatedAddress;
                sb.Append(" raddr ");
                sb.Append(relAddr);
                sb.Append(" rport ");
                sb.Append(relatedPort);
            }

            sb.Append(" generation 0");

            return sb.ToString();
        }

        /// <summary>
        /// Sets the remote end point for a remote candidate.
        /// </summary>
        /// <param name="destinationEP">The resolved end point for the candidate.</param>
        public void SetDestinationEndPoint(IPEndPoint destinationEP)
        {
            DestinationEndPoint = destinationEP;
        }

        private string GetFoundation()
        {
            var serverProtocol = (IceServer?.Protocol ?? ProtocolType.Udp).ToLowerString();
            var builder = new System.Text.StringBuilder();
            builder = builder.Append(type).Append(address).Append(protocol).Append(serverProtocol);
            var bytes = System.Text.Encoding.ASCII.GetBytes(builder.ToString());
            return UpdateCrc32(0, bytes).ToString();

            /*int addressVal = !String.IsNullOrEmpty(address) ? Crypto.GetSHAHash(address).Sum(x => (byte)x) : 0;
            int svrVal = (type == RTCIceCandidateType.relay || type == RTCIceCandidateType.srflx) ?
                Crypto.GetSHAHash(IceServer != null ? IceServer._uri.ToString() : "").Sum(x => (byte)x) : 0;
            return (type.GetHashCode() + addressVal + svrVal + protocol.GetHashCode()).ToString();*/
        }

        private uint GetPriority()
        {
            uint localPreference = 0;
            IPAddress addr;

            //Calculate our LocalPreference Priority
            if (IPAddress.TryParse(address, out addr))
            {
                var addrPref = IPAddressHelper.IPAddressPrecedence(addr);

                // relay_preference in original code was sorted with params:
                // UDP == 2
                // TCP == 1
                // TLS == 0
                var relayPreference = protocol == RTCIceProtocol.udp ? 2u : 1u;

                // TODO: Original implementation consider network adapter preference as strength of wifi
                // We will ignore it as its seems to not be a trivial implementation for use in net-standard 2.0
                uint networkAdapterPreference = 0;

                localPreference = ((networkAdapterPreference << 8) | addrPref) + relayPreference;
            }

            // RTC 5245 Define priority for RTCIceCandidateType
            // https://datatracker.ietf.org/doc/html/rfc5245
            uint typePreference = 0;
            switch (type)
            {
                case RTCIceCandidateType.host:
                    typePreference = 126;
                    break;
                case RTCIceCandidateType.prflx:
                    typePreference = 110;
                    break;
                case RTCIceCandidateType.srflx:
                    typePreference = 100;
                    break;
            }

            //Use formula found in RFC 5245 to define candidate priority
            return (uint)((typePreference << 24) | (localPreference << 8) | (256u - component.GetHashCode()));
        }

        public string toJSON()
        {
            var rtcCandInit = new RTCIceCandidateInit
            {
                sdpMid = sdpMid ?? sdpMLineIndex.ToString(),
                sdpMLineIndex = sdpMLineIndex,
                usernameFragment = usernameFragment,
                candidate = CANDIDATE_PREFIX + ":" + this.ToString()
            };

            return rtcCandInit.toJSON();
        }

        /// <summary>
        /// Checks the candidate to identify whether it is equivalent to the specified
        /// protocol and IP end point. Primary use case is to check whether a candidate
        /// is a match for a remote end point that a message has been received from.
        /// </summary>
        /// <param name="epPotocol">The protocol to check equivalence for.</param>
        /// <param name="ep">The IP end point to check equivalence for.</param>
        /// <returns>True if the candidate is deemed equivalent or false if not.</returns>
        public bool IsEquivalentEndPoint(RTCIceProtocol epPotocol, IPEndPoint ep)
        {
            if (protocol == epPotocol && DestinationEndPoint != null &&
               ep.Address.Equals(DestinationEndPoint.Address) && DestinationEndPoint.Port == ep.Port)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Gets a short description for the candidate that's helpful for log messages.
        /// </summary>
        /// <returns>A short string describing the key properties of the candidate.</returns>
        public string ToShortString()
        {
            string epDescription;
            if (IPAddress.TryParse(address, out var ipAddress))
            {
                var ep = new IPEndPoint(ipAddress, port);
                epDescription = ep.ToString();
            }
            else
            {
                epDescription = $"{address}:{port}";
            }

            return $"{protocol}:{epDescription} ({type.ToStringFast()})";
        }

        //CRC32 implementation from C++ to calculate foundation
        const uint kCrc32Polynomial = 0xEDB88320;
        private static uint[] LoadCrc32Table()
        {
            var kCrc32Table = new uint[256];
            for (uint i = 0; i < kCrc32Table.Length; ++i)
            {
                var c = i;
                for (var j = 0; j < 8; ++j)
                {
                    if ((c & 1) != 0)
                    {
                        c = kCrc32Polynomial ^ (c >> 1);
                    }
                    else
                    {
                        c >>= 1;
                    }
                }
                kCrc32Table[i] = c;
            }
            return kCrc32Table;
        }

        private uint UpdateCrc32(uint start, byte[] buf)
        {
            var kCrc32Table = LoadCrc32Table();

            long c = (int)(start ^ 0xFFFFFFFF);
            var u = buf;
            for (var i = 0; i < buf.Length; ++i)
            {
                c = kCrc32Table[(c ^ u[i]) & 0xFF] ^ (c >> 8);
            }
            return (uint)(c ^ 0xFFFFFFFF);
        }

    }
}
