//-----------------------------------------------------------------------------
// Filename: IOpenAIRealtimeWebRTCEndPoint.cs
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

using System;
using System.Net;
using System.Threading.Tasks;
using LanguageExt;
using LanguageExt.Common;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.OpenAI.RealtimeWebRTC;

public interface IOpenAIRealtimeWebRTCEndPoint
{
    RTCPeerConnection? PeerConnection { get; }

    /// <summary>
    /// (IPEndPoint remoteEndPoint, SDPMediaTypesEnum mediaType (audio|video|text), RTPPacket rtpPacket, uint durationRtpUnits)
    /// </summary>
    event Action<IPEndPoint, SDPMediaTypesEnum, RTPPacket, uint>? OnRtpPacketReceived;

    /// <summary>
    /// (IPEndPoint remoteEndPoint, uint ssrc, uint seqnum, uint timestamp, int payloadID, bool marker, byte[] payload)
    /// </summary>
    event Action<IPEndPoint, uint, uint, uint, int, bool, byte[]>? OnRtpPacketReceivedRaw;

    event Action? OnPeerConnectionConnected;

    event Action? OnPeerConnectionFailed;

    event Action? OnPeerConnectionClosed;

    void ConnectAudioEndPoint(IAudioEndPoint audioEndPoint);

    Task<Either<Error, Unit>> StartConnectAsync(RTCConfiguration? pcConfig = null, string? model = null);

    void SendAudio(uint durationRtpUnits, byte[] sample);

    void SendAudioFromRtpPacket(IPEndPoint remoteEndPoint, SDPMediaTypesEnum mediaType, RTPPacket rtpPacket);

    Either<Error, Unit> SendSessionUpdate(OpenAIVoicesEnum voice, string? instructions = null, string? model = null);

    Either<Error, Unit> SendResponseCreate(OpenAIVoicesEnum voice, string message);

    event Action<RTCDataChannel, OpenAIServerEventBase>? OnDataChannelMessageReceived;
}
