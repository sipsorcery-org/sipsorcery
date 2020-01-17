//-----------------------------------------------------------------------------
// Filename: IceCandidate.cs
//
// Description: Represents a candidate used in the Interactive Connectivity Establishment (ICE) 
// negotiation to set up a usable network connection between two peers as 
// per RFC5245 https://tools.ietf.org/html/rfc5245.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 26 Feb 2016	Aaron Clauson	Created, Hobart, Australia.
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
    public enum IceCandidateTypesEnum
    {
        Unknown = 0,
        host = 1,
        srflx = 2,
        relay = 3
    }

    public class IceCandidate
    {
        public const string m_CRLF = "\r\n";
        public const string REMOTE_ADDRESS_KEY = "raddr";
        public const string REMOTE_PORT_KEY = "rport";

        //public RTPChannel RtpChannel;
        //public Socket LocalRtpSocket;
        //public Socket LocalControlSocket;
        public IPAddress LocalAddress;
        //public Task RtpListenerTask;
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

        public string Transport;
        public string NetworkAddress;
        public int Port;
        public IceCandidateTypesEnum CandidateType;
        public string RemoteAddress;
        public int RemotePort;
        public string RawString;

        //public bool IsConnected
        //{
        //    get { return IsStunLocalExchangeComplete == true && IsStunRemoteExchangeComplete && !IsDisconnected; }
        //}

        private IceCandidate()
        { }

        public IceCandidate(IPAddress localAddress, int port, TurnServer turnServer)
        {
            LocalAddress = localAddress;
            Port = port;
            TurnServer = turnServer;
        }

        public IceCandidate(string transport, IPAddress remoteAddress, int port, IceCandidateTypesEnum candidateType)
        {
            Transport = transport;
            NetworkAddress = remoteAddress.ToString();
            Port = port;
            CandidateType = candidateType;
        }

        public static IceCandidate Parse(string candidateLine)
        {
            IceCandidate candidate = new IceCandidate();

            candidate.RawString = candidateLine;

            string[] candidateFields = candidateLine.Trim().Split(' ');
            candidate.Transport = candidateFields[2];
            candidate.NetworkAddress = candidateFields[4];
            candidate.Port = Convert.ToInt32(candidateFields[5]);
            Enum.TryParse<IceCandidateTypesEnum>(candidateFields[7], out candidate.CandidateType);

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

        public override string ToString()
        {
            var candidateStr = String.Format("a=candidate:{0} {1} udp {2} {3} {4} typ host generation 0\r\n", Crypto.GetRandomInt(10).ToString(), "1", Crypto.GetRandomInt(10).ToString(), LocalAddress.ToString(), Port);

            if (StunRflxIPEndPoint != null)
            {
                candidateStr += String.Format("a=candidate:{0} {1} udp {2} {3} {4} typ srflx raddr {5} rport {6} generation 0\r\n", Crypto.GetRandomInt(10).ToString(), "1", Crypto.GetRandomInt(10).ToString(), StunRflxIPEndPoint.Address, StunRflxIPEndPoint.Port, LocalAddress.ToString(), Port);
                //logger.LogDebug(" " + srflxCandidateStr);
                //iceCandidateString += srflxCandidateStr;
            }

            return candidateStr;
        }
    }
}
