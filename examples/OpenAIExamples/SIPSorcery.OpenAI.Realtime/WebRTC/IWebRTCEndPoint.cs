//-----------------------------------------------------------------------------
// Filename: IWebRTCEndPoint.cs
//
// Description: Interface for the OpenAI WebRTC peer connection.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 18 May 2025  Aaron Clauson   Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License and the additional
// BDS BY-NC-SA restriction, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using LanguageExt;
using LanguageExt.Common;
using SIPSorcery.Net;
using SIPSorcery.OpenAI.Realtime.Models;
using SIPSorceryMedia.Abstractions;
using System;
using System.Threading.Tasks;

namespace SIPSorcery.OpenAI.Realtime;

/// <summary>
/// Contract for a WebRTC endpoint that communicates with the OpenAI real‑time
/// service. Implementations handle connection negotiation, media forwarding and
/// data channel messaging.
/// </summary>
public interface IWebRTCEndPoint
{
    /// <summary>
    /// Gets the underlying peer connection if one has been created.
    /// </summary>
    Option<RTCPeerConnection> PeerConnection { get; }

    /// <summary>
    /// Helper class used to send OpenAI control messages over the data channel.
    /// </summary>
    DataChannelMessenger DataChannelMessenger { get; }

    /// <summary>
    /// Fired once the data channel has opened and the peer connection is fully
    /// established.
    /// </summary>
    event Action? OnPeerConnectionConnected;

    /// <summary>
    /// Fired if the peer connection transitions into the <c>failed</c> state.
    /// </summary>
    event Action? OnPeerConnectionFailed;

    /// <summary>
    /// Fired when the peer connection is closed or disconnected.
    /// </summary>
    event Action? OnPeerConnectionClosed;

    /// <summary>
    /// Raised for each encoded audio frame received from OpenAI.
    /// </summary>
    event Action<EncodedAudioFrame>? OnAudioFrameReceived;

    /// <summary>
    /// Raised whenever a parsed OpenAI server event arrives on the data channel.
    /// </summary>
    event Action<RTCDataChannel, RealtimeEventBase>? OnDataChannelMessage;

    /// <summary>
    /// Initiates connection negotiation with the OpenAI service.
    /// </summary>
    /// <param name="pcConfig">Optional WebRTC configuration to use.</param>
    /// <param name="model">Optional model name to request.</param>
    Task<Either<Error, Unit>> StartConnect(RTCConfiguration? pcConfig = null, RealtimeModelsEnum? model = null);

    /// <summary>
    /// Sends an Opus encoded audio frame to the remote peer.
    /// </summary>
    /// <param name="durationMilliseconds">Duration of the frame in milliseconds.</param>
    /// <param name="encodedAudio">The Opus encoded audio payload.</param>
    void SendAudio(uint durationMilliseconds, byte[] encodedAudio);

    /// <summary>
    /// Sends a control message across the data channel.
    /// </summary>
    void SendDataChannelMessage(RealtimeEventBase message);

    /// <summary>
    /// Closes the peer connection and releases resources.
    /// </summary>
    void Close();
}
