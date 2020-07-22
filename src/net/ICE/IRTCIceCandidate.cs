//-----------------------------------------------------------------------------
// Filename: IRTCIceCandidate.cs
//
// Description: Contains the interface definition for the RTCIceCandidate
// class as defined by the W3C WebRTC specification. Should be kept up to 
// date with:
// https://www.w3.org/TR/webrtc/#rtcicecandidate-interface
//
// History:
// 16 Mar 2020	Aaron Clauson	Created.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

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
        /// <summary>
        /// The connection has been closed. All checks stop.
        /// </summary>
        closed,

        /// <summary>
        /// The connection attempt has failed or connection checks on an established
        /// connection have failed.
        /// </summary>
        failed,

        /// <summary>
        /// Connection attempts on an established connection have failed. Attempts
        /// will continue until the state transitions to failure.
        /// </summary>
        disconnected,

        /// <summary>
        /// The initial state.
        /// </summary>
        @new,

        /// <summary>
        /// Checks are being carried out in an attempt to establish a connection.
        /// </summary>
        checking,

        /// <summary>
        /// What is this state for?
        /// </summary>
        //completed,

        /// <summary>
        /// The checks have been successful and the connection has been established.
        /// </summary>
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
        public string candidate { get; set; }
        public string sdpMid { get; set; }
        public ushort sdpMLineIndex { get; set; }
        public string usernameFragment { get; set; }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <remarks>
    /// As specified in https://www.w3.org/TR/webrtc/#dom-rtcicecomponent.
    /// </remarks>
    public enum RTCIceComponent
    {
        rtp = 1,
        rtcp = 2
    }

    /// <summary>
    /// The transport protocol types for an ICE candidate.
    /// </summary>
    /// <remarks>
    /// As specified in https://www.w3.org/TR/webrtc/#rtciceprotocol-enum.
    /// </remarks>
    public enum RTCIceProtocol
    {
        udp,
        tcp
    }

    /// <summary>
    /// The RTCIceTcpCandidateType represents the type of the ICE TCP candidate.
    /// </summary>
    /// <remarks>
    /// As defined in https://www.w3.org/TR/webrtc/#rtcicetcpcandidatetype-enum.
    /// </remarks>
    public enum RTCIceTcpCandidateType
    {
        /// <summary>
        /// An active TCP candidate is one for which the transport will attempt to 
        /// open an outbound connection but will not receive incoming connection requests.
        /// </summary>
        active,

        /// <summary>
        /// A passive TCP candidate is one for which the transport will receive incoming 
        /// connection attempts but not attempt a connection.
        /// </summary>
        passive,

        /// <summary>
        /// An so candidate is one for which the transport will attempt to open a connection 
        /// simultaneously with its peer.
        /// </summary>
        so
    }

    /// <summary>
    /// The RTCIceCandidateType represents the type of the ICE candidate.
    /// </summary>
    /// <remarks>
    /// As defined in https://www.w3.org/TR/webrtc/#rtcicecandidatetype-enum.
    /// </remarks>
    public enum RTCIceCandidateType
    {
        /// <summary>
        /// A host candidate, locally gathered.
        /// </summary>
        host,

        /// <summary>
        /// A peer reflexive candidate, obtained as a result of a connectivity check 
        /// (e.g. STUN request from a previously unknown address).
        /// </summary>
        prflx,

        /// <summary>
        /// A server reflexive candidate, obtained from STUN and/or TURN (non-relay TURN).
        /// </summary>
        srflx,

        /// <summary>
        /// A relay candidate, TURN (relay).
        /// </summary>
        relay
    }

    public interface IRTCIceCandidate
    {
        //constructor(optional RTCIceCandidateInit candidateInitDict = { });
        string candidate { get; }
        string sdpMid { get; }
        ushort sdpMLineIndex { get; }
        string foundation { get; }
        RTCIceComponent component { get; }
        ulong priority { get; }
        string address { get; }
        RTCIceProtocol protocol { get; }
        ushort port { get; }
        RTCIceCandidateType type { get; }
        RTCIceTcpCandidateType tcpType { get; }
        string relatedAddress { get; }
        ushort relatedPort { get; }
        string usernameFragment { get; }
        RTCIceCandidateInit toJSON();
    }
}
