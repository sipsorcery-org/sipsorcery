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
using System.Threading.Tasks;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    public class RTCIceCandidate : IRTCIceCandidate
    {
        public const string m_CRLF = "\r\n";
        public const string REMOTE_ADDRESS_KEY = "raddr";
        public const string REMOTE_PORT_KEY = "rport";
        public const int RTP_COMPONENT_ID = 1;
        public const int RTCP_COMPONENTID = 2;

        /// <summary>
        /// This implementation does not support separate RTP and RTCP sessions.
        /// It assumes RTP and RTCP will always be multiplexed.
        /// </summary>
        public readonly int ComponentID = RTP_COMPONENT_ID;

        /// <summary>
        /// The base address is the local address on this host for the candidate. The 
        /// candidate address could be different depending on the ICE candidate type.
        /// </summary>
        public IPAddress BaseAddress { get; private set; }

        /// <summary>
        /// Whether the candidate is UDP or TCP.
        /// </summary>
        public ProtocolType TransportProtocol { get; private set; }

        public string StunServerAddress { get; private set; }

        public string TurnServerAddress { get; private set; }



        public string candidate { get; private set; }

        public string sdpMid { get; private set; }

        public ushort sdpMLineIndex { get; private set; }

        /// <summary>
        /// Composed of 1 to 32 chars.  It is an
        /// identifier that is equivalent for two candidates that are of the
        /// same type, share the same base, and come from the same STUN
        /// server.
        /// </summary>
        public string foundation { get; private set; }

        /// <summary>
        ///  Is a positive integer between 1 and 256 (inclusive)
        /// that identifies the specific component of the data stream for
        /// which this is a candidate.
        /// </summary>
        public RTCIceComponent component { get; private set; }

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


        public TurnServer TurnServer;
        public bool IsGatheringComplete;
        public int TurnAllocateAttempts;
        public IPEndPoint StunRflxIPEndPoint;
        public IPEndPoint TurnRelayIPEndPoint;
        //public IPEndPoint RemoteRtpEndPoint;
        //public bool IsDisconnected;
        //public string DisconnectionMessage;
        public DateTime LastSTUNSendAt;
        public DateTime LastStunRequestReceivedAt;
        public DateTime LastStunResponseReceivedAt;
        public bool IsStunLocalExchangeComplete;      // This is the authenticated STUN request sent by us to the remote WebRTC peer.
        public bool IsStunRemoteExchangeComplete;     // This is the authenticated STUN request sent by the remote WebRTC peer to us.
        public int StunConnectionRequestAttempts = 0;
        public DateTime LastCommunicationAt;
        public bool HasConnectionError;

        //public string Transport;
        public string NetworkAddress;
        //public int Port;
        //public RTCIceCandidateType CandidateType;
        public string RemoteAddress;
        public int RemotePort;
        public string RawString;

        public Task InitialStunBindingCheck;

        //public bool IsConnected
        //{
        //    get { return IsStunLocalExchangeComplete == true && IsStunRemoteExchangeComplete && !IsDisconnected; }
        //}

        private RTCIceCandidate()
        {
            component = RTCIceComponent.rtp;
            foundation = Crypto.GetRandomInt(10).ToString();
        }

        public RTCIceCandidate(RTCIceCandidateInit init) : this()
        {
            candidate = init.candidate;
            sdpMid = init.sdpMid;
            sdpMLineIndex = init.sdpMLineIndex;
            usernameFragment = init.usernameFragment;
        }

        public RTCIceCandidate(IPAddress localAddress, ushort localPort) : this()
        {
            NetworkAddress = localAddress.ToString();
            port = localPort;
        }

        public RTCIceCandidate(RTCIceProtocol candidateProtocol, IPAddress remoteAddress, ushort localPort, RTCIceCandidateType candidateType)
             : this()
        {
            //Transport = transport;
            protocol = candidateProtocol;
            NetworkAddress = remoteAddress.ToString();
            port = localPort;
            type = candidateType;
        }

        public static RTCIceCandidate Parse(string candidateLine)
        {
            RTCIceCandidate candidate = new RTCIceCandidate();

            candidate.RawString = candidateLine;

            string[] candidateFields = candidateLine.Trim().Split(' ');

            if (Enum.TryParse<RTCIceProtocol>(candidateFields[2], out var candidateProtocol))
            {
                candidate.protocol = candidateProtocol;
            }

            candidate.NetworkAddress = candidateFields[4];
            candidate.port = Convert.ToUInt16(candidateFields[5]);

            if (Enum.TryParse<RTCIceCandidateType>(candidateFields[7], out var candidateType))
            {
                candidate.type = candidateType;
            }

            if (candidateFields.Length > 8 && candidateFields[8] == REMOTE_ADDRESS_KEY)
            {
                candidate.RemoteAddress = candidateFields[9];
            }

            if (candidateFields.Length > 10 && candidateFields[10] == REMOTE_PORT_KEY)
            {
                candidate.RemotePort = Convert.ToInt32(candidateFields[11]);
            }

            return candidate;
        }
        /// <summary>
        /// 
        /// </summary>
        /// <remarks>
        /// The specification regarding how an ICE candidate should be serialised in SDP is at
        /// https://tools.ietf.org/html/draft-ietf-mmusic-ice-sip-sdp-39#section-5.1.   
        /// </remarks>
        /// <returns></returns>
        public override string ToString()
        {
            var candidateStr = String.Format("{0} {1} udp {2} {3} {4} typ host generation 0",
                foundation,
                component,
                priority,
                NetworkAddress,
                port);

            if (StunRflxIPEndPoint != null)
            {
                candidateStr += String.Format("{0} {1} udp {2} {3} {4} typ srflx raddr {5} rport {6} generation 0",
                    foundation,
                    component,
                    priority,
                    StunRflxIPEndPoint.Address,
                    StunRflxIPEndPoint.Port,
                    NetworkAddress,
                    port);
            }

            return candidateStr;
        }

        /// <summary>
        /// Calculates the foundation string for an ICE candidate. It can be used to determine whether two ICE candidates are 
        /// equivalent.
        /// </summary>
        /// <remarks>
        /// See https://tools.ietf.org/html/rfc8445#section-5.1.1.3.
        /// </remarks>
        /// <returns>A string capturing the attributes that are used in determining the foundation value.</returns>
        public string GetFoundation()
        {
            string stunOrTurnAddress = !String.IsNullOrEmpty(StunServerAddress) ? StunServerAddress : TurnServerAddress;

            return type + BaseAddress.ToString() + stunOrTurnAddress + TransportProtocol.ToString();
        }

        /// <summary>
        /// Determines the unique priority value for an ICE candidate.
        /// </summary>
        /// <remarks>
        /// See https://tools.ietf.org/html/rfc8445#section-5.1.2.
        /// </remarks>
        /// <returns></returns>
        public int GetPriority()
        {
            return 0;
        }

        public RTCIceCandidateInit toJSON()
        {
            throw new NotImplementedException();
        }
    }
}
