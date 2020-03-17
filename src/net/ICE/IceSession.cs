//-----------------------------------------------------------------------------
// Filename: IceSession.cs
//
// Description: Represents a ICE Session as described in the Interactive
// Connectivity Establishment RFC8445 https://tools.ietf.org/html/rfc8445.
// Additionally support for the following standards or proposed standards 
// is included:
// - "Trickle ICE" as per draft RFC
//    https://tools.ietf.org/html/draft-ietf-ice-trickle-21.
// - "WebRTC IP Address Handling Requirements" as per draft RFC
//   https://tools.ietf.org/html/draft-ietf-rtcweb-ip-handling-12
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 15 Mar 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace SIPSorcery.Net
{
    /// <summary>
    /// The gathering states an ICE session transitions through.
    /// </summary>
    /// <remarks>
    /// As specified in https://www.w3.org/TR/webrtc/#dom-rtcicegatheringstate.
    /// </remarks>
    public enum RTCIceGatheringState
    {
        @new,
        gathering,
        complete
    }


    /// <summary>
    /// The states an ICE session transitions through.
    /// </summary>
    /// <remarks>
    /// As specified in https://www.w3.org/TR/webrtc/#rtciceconnectionstate-enum.
    /// </remarks>
    public enum RTCIceConnectionState
    {
        closed,
        failed,
        disconnected,
        @new,
        checking,
        completed,
        connected
    }

    /// <summary>
    /// Properties to influence the initialisation of an ICE candidate.
    /// </summary>
    /// <remarks>
    /// As specified in https://www.w3.org/TR/webrtc/#dom-rtcicecandidateinit.
    /// </remarks>
    public class RTCIceCandidateInit
    {
        public string candidate;
        public string sdpMid;
        public ushort sdpMLineIndex;
        public string usernameFragment;
    }

    public class IceSession
    {
        public RTCIceGatheringState GatheringState { get; private set; }

        public RTCPeerConnectionState ConnectionState { get; private set; }

        /// <summary>
        /// THe list of ICE candidates that have been gathered for this peer.
        /// </summary>
        public List<IceCandidate> Candidates { get; private set; }

        /// <summary>
        /// The list of ICE candidates from the remote peer.
        /// </summary>
        public List<IceCandidate> PeerCandidates { get; private set; }

        public IceSession()
        { }

        /// <summary>
        /// Acquires an ICE candidate for each IP address that this host has except for:
        /// - Loopback addresses must not be included.
        /// - Deprecated IPv4-compatible IPv6 addresses and IPv6 site-local unicast addresses
        ///   must not be included,
        /// - IPv4-mapped IPv6 address should not be included.
        /// - If a non-location tracking IPv6 address is available use it and do not included 
        ///   location tracking enabled IPv6 addresses (i.e. prefer temporary IPv6 addresses over 
        ///   permanent addresses), see RFC6724.
        /// </summary>
        /// <returns>A list of "host" ICE candidates for the local machine.</returns>
        private List<IceCandidate> GetHostCandidates()
        {
            List<IceCandidate> hostCandidates = new List<IceCandidate>();

            return hostCandidates;
        }

        /// <summary>
        /// Attempts to get a list of server-reflexive candidates using the local "host" candidates
        /// and a STUN or TURN server.
        /// </summary>
        /// <returns></returns>
        private List<IceCandidate> GetServerRelexiveCandidates()
        {
            throw new NotImplementedException();
        }
    }
}
