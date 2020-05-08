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
using System.Linq;
using System.Net;
using System.Net.Sockets;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    public class RTCIceCandidate : IRTCIceCandidate
    {
        public const string m_CRLF = "\r\n";
        public const string REMOTE_ADDRESS_KEY = "raddr";
        public const string REMOTE_PORT_KEY = "rport";

        /// <summary>
        /// The base address is the local address on this host for the candidate. The 
        /// candidate address could be different depending on the ICE candidate type.
        /// </summary>
        public IPAddress BaseAddress { get; private set; }

        public string StunServerAddress { get; private set; }

        public string TurnServerAddress { get; private set; }

        public string candidate { get; private set; }

        public IPAddress CandidateAddress
        {
            get
            {
                if (!string.IsNullOrEmpty(address)) 
                {
                    return IPAddress.Parse(address);
                }
                else
                {
                    return null;
                }
            }
        }

        public AddressFamily addressFamily
        {
            get { return CandidateAddress.AddressFamily; }
        }

        public string sdpMid { get; private set; }

        public ushort sdpMLineIndex { get; private set; }

        /// <summary>
        /// Composed of 1 to 32 chars. It is an
        /// identifier that is equivalent for two candidates that are of the
        /// same type, share the same base, and come from the same STUN
        /// server.
        /// </summary>
        /// <remarks>
        /// See https://tools.ietf.org/html/rfc8445#section-5.1.1.3.
        /// </remarks>
        public string foundation { get; private set; }

        /// <summary>
        ///  Is a positive integer between 1 and 256 (inclusive)
        /// that identifies the specific component of the data stream for
        /// which this is a candidate.
        /// </summary>
        public RTCIceComponent component { get; private set; } = RTCIceComponent.rtp;

        /// <summary>
        /// A positive integer between 1 and (2**31 - 1) inclusive.
        /// This priority will be used by ICE to determine the order of the
        /// connectivity checks and the relative preference for candidates.
        /// Higher-priority values give more priority over lower values.
        /// </summary>
        /// <remarks>
        /// See specification at https://tools.ietf.org/html/rfc8445#section-5.1.2.
        /// </remarks>
        public ulong priority { get; private set; }

        /// <summary>
        /// The local address for the candidate.
        /// </summary>
        public string address { get; private set; }

        /// <summary>
        /// The transport protocol for the candidate, supported options are UDP and TCP.
        /// </summary>
        public RTCIceProtocol protocol { get; private set; }

        /// <summary>
        /// The local port the candidate is listening on.
        /// </summary>
        public ushort port { get; private set; }

        /// <summary>
        /// The typ of ICE candidate, host, srflx etc.
        /// </summary>
        public RTCIceCandidateType type { get; private set; }

        /// <summary>
        /// For TCP candidates the role they are fulfilling (client, server or both).
        /// </summary>
        public RTCIceTcpCandidateType tcpType { get; private set; }

        public string relatedAddress { get; private set; }

        public ushort relatedPort { get; private set; }

        public string usernameFragment { get; private set; }

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
                relatedAddress = iceCandidate.relatedAddress;
                relatedPort = iceCandidate.relatedPort;
            }
        }

        public void SetAddressProperties(
            RTCIceProtocol cProtocol,
            IPAddress cAddress,
            ushort cPort,
            RTCIceCandidateType cType,
            IPAddress cRelatedAddress,
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
            if (string.IsNullOrEmpty(candidateLine))
            {
                throw new ArgumentNullException("Cant parse ICE candidate from empty string.", candidateLine);
            }
            else
            {
                candidateLine = candidateLine.Replace("candidate:", "");

                RTCIceCandidate candidate = new RTCIceCandidate();

                string[] candidateFields = candidateLine.Trim().Split(' ');

                candidate.foundation = candidateFields[0];

                if (Enum.TryParse<RTCIceComponent>(candidateFields[1], out var candidateComponent))
                {
                    candidate.component = candidateComponent;
                }

                if (Enum.TryParse<RTCIceProtocol>(candidateFields[2], out var candidateProtocol))
                {
                    candidate.protocol = candidateProtocol;
                }

                if (ulong.TryParse(candidateFields[3], out var candidatePriority))
                {
                    candidate.priority = candidatePriority;
                }

                candidate.address = candidateFields[4];
                candidate.port = Convert.ToUInt16(candidateFields[5]);

                if (Enum.TryParse<RTCIceCandidateType>(candidateFields[7], out var candidateType))
                {
                    candidate.type = candidateType;
                }

                if (candidateFields.Length > 8 && candidateFields[8] == REMOTE_ADDRESS_KEY)
                {
                    candidate.relatedAddress = candidateFields[9];
                }

                if (candidateFields.Length > 10 && candidateFields[10] == REMOTE_PORT_KEY)
                {
                    candidate.relatedPort = Convert.ToUInt16(candidateFields[11]);
                }

                return candidate;
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
            var candidateStr = String.Format("{0} {1} udp {2} {3} {4} typ host generation 0",
                foundation,
                component.GetHashCode(),
                priority,
                address,
                port);

            if (relatedAddress != null)
            {
                candidateStr += String.Format("{0} {1} udp {2} {3} {4} typ srflx raddr {5} rport {6} generation 0",
                    foundation,
                    component.GetHashCode(),
                    priority,
                    relatedAddress,
                    relatedPort,
                    address,
                    port);
            }

            return candidateStr;
        }

        private string GetFoundation()
        {
            int addressVal = !String.IsNullOrEmpty(address) ? Crypto.GetSHAHash(address).Sum(x => (byte)x) : 0;
            int svrVal = (type == RTCIceCandidateType.relay || type == RTCIceCandidateType.srflx) ?
                Crypto.GetSHAHash(StunServerAddress, TurnServerAddress).Sum(x => (byte)x) : 0;
            return (type.GetHashCode() + addressVal + svrVal + protocol.GetHashCode()).ToString();
        }

        private ulong GetPriority()
        {
            return (ulong)((2 ^ 24) * (126 - type.GetHashCode()) +
                      (2 ^ 8) * (65535) + // TODO: Add some kind of priority to different local IP addresses if needed.
                      (2 ^ 0) * (256 - component.GetHashCode()));
        }

        public RTCIceCandidateInit toJSON()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Gets the IP end point corresponding to the ICE candidate. This will typically
        /// be called on the remote nominated candidate to identify the remote end point
        /// to use subsequent to a successful ICE negotiation.
        /// </summary>
        /// <returns>An IP end point.</returns>
        public IPEndPoint GetEndPoint()
        {
            int remotePort = (relatedPort != 0) ? relatedPort : port;

            if(relatedAddress != null)
            {
                return new IPEndPoint(IPAddress.Parse(relatedAddress), remotePort);
            }
            else
            {
                return new IPEndPoint(IPAddress.Parse(address), remotePort);
            }
        }

        /// <summary>
        /// Checks the candidate to identify whether it is equivalent to the specified
        /// protocol and IP end point. Primary use case is to check whether a candidate
        /// is a match for a remote end point that a message has been received from.
        /// </summary>
        /// <param name="epProtocol">The protocol to check equivalence for.</param>
        /// <param name="ep">The IP end point to check equivalence for.</param>
        /// <returns>True if the candidate is deemed equivalent or false if not.</returns>
        public bool IsEquivalentEndPoint(RTCIceProtocol epPotocol, IPEndPoint ep)
        {
            if (protocol == epPotocol &&
                (
                 (!string.IsNullOrEmpty(address) && ep.Address.Equals(IPAddress.Parse(address)) && port == ep.Port) ||
                 (!string.IsNullOrEmpty(relatedAddress) && ep.Address.Equals(IPAddress.Parse(relatedAddress)) && relatedPort == ep.Port)
                 ))
            {
                return true;
            }
            else
            {
                return false;
            }
        }
    }
}
