//-----------------------------------------------------------------------------
// Filename: IceCandidate.cs
//
// Description: Represents a candidate used in the Interactive Connectivity Establishment (ICE) 
// negotiation to set up a usable network connection between two peers as 
// per RFC5245 https://tools.ietf.org/html/rfc5245.
//
// History:
// 26 Feb 2016	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2016 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery Pty Ltd, Hobart, Australia (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery Pty Ltd 
// nor the names of its contributors may be used to endorse or promote products derived from this software without specific 
// prior written permission. 
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, 
// BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. 
// IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, 
// OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, 
// OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, 
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE 
// POSSIBILITY OF SUCH DAMAGE.
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

        public Socket LocalRtpSocket;
        public Socket LocalControlSocket;
        public IPAddress LocalAddress;
        public Task RtpListenerTask;
        public TurnServer TurnServer;
        public bool IsGatheringComplete;
        public int TurnAllocateAttempts;
        public IPEndPoint StunRflxIPEndPoint;
        public IPEndPoint TurnRelayIPEndPoint;
        public IPEndPoint RemoteRtpEndPoint;
        public bool IsDisconnected;
        public string DisconnectionMessage;
        public DateTime LastSTUNSendAt;
        public DateTime LastStunRequestReceivedAt;
        public DateTime LastStunResponseReceivedAt;
        public bool IsStunLocalExchangeComplete;      // This is the authenticated STUN request sent by us to the remote WebRTC peer.
        public bool IsStunRemoteExchangeComplete;     // This is the authenticated STUN request sent by the remote WebRTC peer to us.
        public int StunConnectionRequestAttempts = 0;
        public DateTime LastCommunicationAt;

        public string Transport;
        public string NetworkAddress;
        public int Port;
        public IceCandidateTypesEnum CandidateType;
        public string RemoteAddress;
        public int RemotePort;
        public string RawString;

        public bool IsConnected
        {
            get { return IsStunLocalExchangeComplete == true && IsStunRemoteExchangeComplete && !IsDisconnected; }
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
            var candidateStr = String.Format("a=candidate:{0} {1} udp {2} {3} {4} typ host generation 0\r\n", Crypto.GetRandomInt(10).ToString(), "1", Crypto.GetRandomInt(10).ToString(), LocalAddress.ToString(), (LocalRtpSocket.LocalEndPoint as IPEndPoint).Port);

            if (StunRflxIPEndPoint != null)
            {
                candidateStr += String.Format("a=candidate:{0} {1} udp {2} {3} {4} typ srflx raddr {5} rport {6} generation 0\r\n", Crypto.GetRandomInt(10).ToString(), "1", Crypto.GetRandomInt(10).ToString(), StunRflxIPEndPoint.Address, StunRflxIPEndPoint.Port, LocalAddress.ToString(), (LocalRtpSocket.LocalEndPoint as IPEndPoint).Port);
                //logger.Debug(" " + srflxCandidateStr);
                //iceCandidateString += srflxCandidateStr;
            }

            return candidateStr;
        }
    }
}
