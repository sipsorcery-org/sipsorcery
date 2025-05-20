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

using System.Text.Json;
using System.Text.Json.Serialization;
using SIPSorcery.Sys;

namespace SIPSorcery.Net;

/// <summary>
/// The ICE set up roles that a peer can be in. The role determines how the DTLS
/// handshake is performed, i.e. which peer is the client and which is the server.
/// </summary>
public enum IceImplementationEnum
{
    full,
    lite
}

/// <summary>
/// The ICE set up roles that a peer can be in. The role determines how the DTLS
/// handshake is performed, i.e. which peer is the client and which is the server.
/// </summary>
public enum IceRolesEnum
{
    actpass = 0,
    passive = 1,
    active = 2
}

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
/// Represents an ICE candidate and associated properties that link it to the SDP.
/// </summary>
/// <remarks>
/// As specified in https://www.w3.org/TR/webrtc/#dom-rtcicecandidateinit.
/// </remarks>
public class RTCIceCandidateInit
{
    [JsonPropertyName("candidate")]
    public string? candidate { get; set; }
    [JsonPropertyName("sdpMid")]
    public string? sdpMid { get; set; }
    [JsonPropertyName("sdpMLineIndex")]
    public ushort sdpMLineIndex { get; set; }
    [JsonPropertyName("usernameFragment")]
    public string? usernameFragment { get; set; }

    public string toJSON()
    {
        //return "{" +
        //     $"  \"sdpMid\": \"{sdpMid ?? sdpMLineIndex.ToString()}\"," +
        //     $"  \"sdpMLineIndex\": {sdpMLineIndex}," +
        //     $"  \"usernameFragment\": \"{usernameFragment}\"," +
        //     $"  \"candidate\": \"{candidate}\"" +
        //     "}";

        return JsonSerializer.Serialize(this, SipSorceryJsonSerializerContext.Default.RTCIceCandidateInit);
    }

    public static bool TryParse(string json, out RTCIceCandidateInit? init)
    {
        init = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }
        else
        {
            init = JsonSerializer.Deserialize<RTCIceCandidateInit>(json, SipSorceryJsonSerializerContext.Default.RTCIceCandidateInit);

            // To qualify as parsed all required fields must be set.
            return init is { } &&
            init.candidate is { } &&
            init.sdpMid is { };
        }
    }
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

/// <remarks>
/// As defined in: https://www.w3.org/TR/webrtc/#rtcicecandidate-interface
/// 
/// Rhe 'priority` field was adjusted from ulong to uint due to an issue that 
/// occurred with the STUN PRIORITY attribute being rejected for not being 4 bytes.
/// The ICE and WebRTC specifications are contradictory so went with the same as
/// libwebrtc which is 4 bytes.
/// See https://github.com/sipsorcery/sipsorcery/issues/350.
/// </remarks>
public interface IRTCIceCandidate
{
    //constructor(optional RTCIceCandidateInit candidateInitDict = { });
    string candidate { get; }
    string? sdpMid { get; }
    ushort sdpMLineIndex { get; }
    string? foundation { get; }
    RTCIceComponent component { get; }
    uint priority { get; }
    string? address { get; }
    RTCIceProtocol protocol { get; }
    ushort port { get; }
    RTCIceCandidateType type { get; }
    RTCIceTcpCandidateType tcpType { get; }
    string? relatedAddress { get; }
    ushort relatedPort { get; }
    string? usernameFragment { get; }
    //RTCIceCandidateInit toJSON();
    string toJSON();
}
