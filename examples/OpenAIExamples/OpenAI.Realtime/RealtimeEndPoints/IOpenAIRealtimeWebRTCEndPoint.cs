using System;
using System.Net;
using System.Threading.Tasks;
using LanguageExt;
using LanguageExt.Common;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;

namespace demo;

public interface IOpenAIRealtimeWebRTCEndPoint
{
    AudioEncoder AudioEncoder { get; }

    AudioFormat AudioFormat { get; }

    RTCPeerConnection? PeerConnection { get; }

    event Action<IPEndPoint, SDPMediaTypesEnum, RTPPacket>? OnRtpPacketReceived;

    event Action? OnPeerConnectionConnected;

    event Action? OnPeerConnectionClosedOrFailed;

    Task<Either<Error, Unit>> StartConnectAsync(RTCConfiguration? pcConfig = null, string? model = null);

    void SendAudio(uint durationRtpUnits, byte[] sample);

    Either<Error, Unit> SendSessionUpdate(OpenAIVoicesEnum voice, string? instructions = null, string? model = null);

    Either<Error, Unit> SendResponseCreate(OpenAIVoicesEnum voice, string message);

    event Action<RTCDataChannel, OpenAIServerEventBase>? OnDataChannelMessageReceived;
}
