//-----------------------------------------------------------------------------
// Filename: OpenAIRealtimeWebRTCEndPoint.cs
//
// Description: Helper methods to manage communications with an OpenAI WebRTC
// peer connection.
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
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using LanguageExt;
using LanguageExt.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.OpenAI.RealtimeWebRTC;

public class OpenAIRealtimeWebRTCEndPoint : IOpenAIRealtimeWebRTCEndPoint
{
    public const string OPENAI_DEFAULT_MODEL = "gpt-4o-realtime-preview-2024-12-17";
    public const string OPENAI_DATACHANNEL_NAME = "oai-events";

    private ILogger _logger = NullLogger.Instance;

    private readonly IOpenAIRealtimeRestClient _openAIRealtimeRestClient;

    private RTCPeerConnection? _rtcPeerConnection = null;
    public RTCPeerConnection? PeerConnection => _rtcPeerConnection;

    public event Action<IPEndPoint, SDPMediaTypesEnum, RTPPacket>? OnRtpPacketReceived;

    public event Action<IPEndPoint, uint, uint, uint, int, bool, byte[]>? OnRtpPacketReceivedRaw;

    public event Action? OnPeerConnectionConnected;

    public event Action? OnPeerConnectionFailed;

    public event Action? OnPeerConnectionClosed;

    public event Action<RTCDataChannel, OpenAIServerEventBase>? OnDataChannelMessageReceived;

    /// <summary>
    /// Preferred constructor for dependency injection.
    /// </summary>
    /// <param name="logger"></param>
    /// <param name="openAIRealtimeRestClient"></param>
    public OpenAIRealtimeWebRTCEndPoint(
        ILogger<OpenAIRealtimeWebRTCEndPoint> logger,
        IOpenAIRealtimeRestClient openAIRealtimeRestClient)
    {
        _logger = logger;
        _openAIRealtimeRestClient = openAIRealtimeRestClient;
    }

    /// <summary>
    /// Constructor for use when not using dependency injection.
    /// </summary>
    /// <param name="openAiKey"></param>
    public OpenAIRealtimeWebRTCEndPoint(string openAiKey, ILogger? logger = null)
    {
        var openAIHttpClientFactory = new OpenAIHttpClientFactory(openAiKey);
        _openAIRealtimeRestClient = new OpenAIRealtimeRestClient(openAIHttpClientFactory);

        if(logger != null)
        {
            _logger = logger;
        }
    }

    public void ConnectAudioEndPoint(IAudioEndPoint audioEndPoint)
    {
        audioEndPoint.OnAudioSourceEncodedSample += SendAudio;
        OnRtpPacketReceivedRaw += audioEndPoint.GotAudioRtp;
        OnPeerConnectionConnected += async () => await audioEndPoint.Start();
        OnPeerConnectionFailed += async () => await audioEndPoint.Close();
        OnPeerConnectionClosed += async () => await audioEndPoint.Close();
    }

    public async Task<Either<Error, Unit>> StartConnectAsync(RTCConfiguration? pcConfig = null, string? model = null)
    {
        if(_rtcPeerConnection != null)
        {
            return Unit.Default;
        }

        _rtcPeerConnection = CreatePeerConnection(pcConfig);

        var useModel = string.IsNullOrWhiteSpace(model) ? OPENAI_DEFAULT_MODEL : model;

        var offer = _rtcPeerConnection.createOffer();
        await _rtcPeerConnection.setLocalDescription(offer).ConfigureAwait(false);

        var sdpAnswerResult = await _openAIRealtimeRestClient.GetSdpAnswerAsync(offer.sdp, useModel).ConfigureAwait(false);

        return sdpAnswerResult.Map(sdpAnswer =>
        {
            var answer = new RTCSessionDescriptionInit
            {
                type = RTCSdpType.answer,
                sdp  = sdpAnswer
            };
            _rtcPeerConnection.setRemoteDescription(answer);
            return Unit.Default;
        });
    }

    private RTCPeerConnection CreatePeerConnection(RTCConfiguration? pcConfig)
    {
        _rtcPeerConnection = new RTCPeerConnection(pcConfig);

        MediaStreamTrack audioTrack = new MediaStreamTrack(AudioCommonlyUsedFormats.OpusWebRTC, MediaStreamStatusEnum.SendRecv);
        _rtcPeerConnection.addTrack(audioTrack);

        // This call is synchronous when the WebRTC connection is not yet connected.
        var dataChannel = _rtcPeerConnection.createDataChannel(OPENAI_DATACHANNEL_NAME).Result;

        _rtcPeerConnection.onconnectionstatechange += state => _logger.LogDebug($"Peer connection connected changed to {state}.");
        _rtcPeerConnection.OnTimeout += mediaType => _logger.LogDebug($"Timeout on media {mediaType}.");
        _rtcPeerConnection.oniceconnectionstatechange += state => _logger.LogDebug($"ICE connection state changed to {state}.");

        _rtcPeerConnection.onsignalingstatechange += () =>
        {
            if (_rtcPeerConnection.signalingState == RTCSignalingState.have_local_offer)
            {
                _logger.LogDebug($"Local SDP:\n{_rtcPeerConnection.localDescription.sdp}");
            }
            else if (_rtcPeerConnection.signalingState is RTCSignalingState.have_remote_offer or RTCSignalingState.stable)
            {
                _logger.LogDebug($"Remote SDP:\n{_rtcPeerConnection.remoteDescription?.sdp}");
            }
        };

        _rtcPeerConnection.OnRtpPacketReceived += (ep, mt, rtp) => OnRtpPacketReceived?.Invoke(ep, mt, rtp);

        _rtcPeerConnection.OnRtpPacketReceived += (ep, mt, rtp) =>
        {
            OnRtpPacketReceivedRaw?.Invoke(ep, rtp.Header.SyncSource, rtp.Header.SequenceNumber, rtp.Header.Timestamp, rtp.Header.PayloadType, rtp.Header.MarkerBit == 1, rtp.Payload);
        };

        _rtcPeerConnection.onconnectionstatechange += (state) =>
        {
            if(state is RTCPeerConnectionState.failed)
            {
                OnPeerConnectionFailed?.Invoke();
            }
            else if (state is RTCPeerConnectionState.closed or
                RTCPeerConnectionState.disconnected)
            {
                OnPeerConnectionClosed?.Invoke();
            }
        };

        dataChannel.onopen += () => OnPeerConnectionConnected?.Invoke();

        dataChannel.onmessage += OnDataChannelMessage;

        dataChannel.onclose += () => OnPeerConnectionClosed?.Invoke();

        return _rtcPeerConnection;
    }

    public void SendAudio(uint durationRtpUnits, byte[] sample)
    {
        if (_rtcPeerConnection != null && _rtcPeerConnection.connectionState == RTCPeerConnectionState.connected)
        {
            _rtcPeerConnection.SendAudio(durationRtpUnits, sample);
        }
    }

    public Either<Error, Unit> SendSessionUpdate(OpenAIVoicesEnum voice, string? instructions = null, string? model = null)
    {
        if (_rtcPeerConnection == null || _rtcPeerConnection.connectionState != RTCPeerConnectionState.connected)
        {
            return Error.New("Peer connection not established.");
        }

        var responseCreate = new OpenAISessionUpdate
        {
            EventID = Guid.NewGuid().ToString(),
            Session = new OpenAISession
            {
                Voice = voice,
                Instructions = instructions,
            }
        };

        if(!string.IsNullOrWhiteSpace(model))
        {
            responseCreate.Session.Model = model;
        }

        if (!string.IsNullOrWhiteSpace(instructions))
        {
            responseCreate.Session.Instructions = instructions;
        }

        var dc = _rtcPeerConnection.DataChannels.First();

        _logger.LogDebug($"Sending initial response create to first call data channel {dc.label}.");
        _logger.LogDebug(responseCreate.ToJson());

        dc.send(responseCreate.ToJson());

        return Unit.Default;
    }

    public Either<Error, Unit> SendResponseCreate(OpenAIVoicesEnum voice, string instructions)
    {
        if(_rtcPeerConnection == null || _rtcPeerConnection.connectionState != RTCPeerConnectionState.connected)
        {
            return Error.New("Peer connection not established.");
        }

        var responseCreate = new OpenAIResponseCreate
        {
            EventID = Guid.NewGuid().ToString(),
            Response = new OpenAIResponseCreateResponse
            {
                Instructions = instructions,
                Voice = voice.ToString()
            }
        };

        var dc = _rtcPeerConnection.DataChannels.First();

        _logger.LogDebug($"Sending initial response create to first call data channel {dc.label}.");
        _logger.LogDebug(responseCreate.ToJson());

        dc.send(responseCreate.ToJson());

        return Unit.Default;
    }

    /// <summary>
    /// Event handler for WebRTC data channel messages.
    /// </summary>
    private void OnDataChannelMessage(RTCDataChannel dc, DataChannelPayloadProtocols protocol, byte[] data)
    {
        //logger.LogInformation($"Data channel {dc.label}, protocol {protocol} message length {data.Length}.");

        var message = Encoding.UTF8.GetString(data);

        var serverEventModel = ParseDataChannelMessage(data);
        serverEventModel.IfSome(e =>
        {
            OnDataChannelMessageReceived?.Invoke(dc, e);
        });
    }

    private Option<OpenAIServerEventBase> ParseDataChannelMessage(byte[] data)
    {
        var message = Encoding.UTF8.GetString(data);

        //logger.LogDebug($"Data channel message: {message}");

        var serverEvent = JsonSerializer.Deserialize<OpenAIServerEventBase>(message, JsonOptions.Default);

        if (serverEvent != null)
        {
            //logger.LogInformation($"Server event ID {serverEvent.EventID} and type {serverEvent.Type}.");

            return serverEvent.Type switch
            {
                OpenAIConversationItemCreated.TypeName => JsonSerializer.Deserialize<OpenAIConversationItemCreated>(message, JsonOptions.Default),
                OpenAIInputAudioBufferCommitted.TypeName => JsonSerializer.Deserialize<OpenAIInputAudioBufferCommitted>(message, JsonOptions.Default),
                OpenAIInputAudioBufferSpeechStarted.TypeName => JsonSerializer.Deserialize<OpenAIInputAudioBufferSpeechStarted>(message, JsonOptions.Default),
                OpenAIInputAudioBufferSpeechStopped.TypeName => JsonSerializer.Deserialize<OpenAIInputAudioBufferSpeechStopped>(message, JsonOptions.Default),
                OpenAIOuputAudioBufferAudioStarted.TypeName => JsonSerializer.Deserialize<OpenAIOuputAudioBufferAudioStarted>(message, JsonOptions.Default),
                OpenAIOuputAudioBufferAudioStopped.TypeName => JsonSerializer.Deserialize<OpenAIOuputAudioBufferAudioStopped>(message, JsonOptions.Default),
                OpenAIRateLimitsUpdated.TypeName => JsonSerializer.Deserialize<OpenAIRateLimitsUpdated>(message, JsonOptions.Default),
                OpenAIResponseAudioDone.TypeName => JsonSerializer.Deserialize<OpenAIResponseAudioDone>(message, JsonOptions.Default),
                OpenAIResponseAudioTranscriptDelta.TypeName => JsonSerializer.Deserialize<OpenAIResponseAudioTranscriptDelta>(message, JsonOptions.Default),
                OpenAIResponseAudioTranscriptDone.TypeName => JsonSerializer.Deserialize<OpenAIResponseAudioTranscriptDone>(message, JsonOptions.Default),
                OpenAIResponseContentPartAdded.TypeName => JsonSerializer.Deserialize<OpenAIResponseContentPartAdded>(message, JsonOptions.Default),
                OpenAIResponseContentPartDone.TypeName => JsonSerializer.Deserialize<OpenAIResponseContentPartDone>(message, JsonOptions.Default),
                OpenAIResponseCreated.TypeName => JsonSerializer.Deserialize<OpenAIResponseCreated>(message, JsonOptions.Default),
                OpenAIResponseDone.TypeName => JsonSerializer.Deserialize<OpenAIResponseDone>(message, JsonOptions.Default),
                OpenAIResponseFunctionCallArgumentsDelta.TypeName => JsonSerializer.Deserialize<OpenAIResponseFunctionCallArgumentsDelta>(message, JsonOptions.Default),
                OpenAIResponseFunctionCallArgumentsDone.TypeName => JsonSerializer.Deserialize<OpenAIResponseFunctionCallArgumentsDone>(message, JsonOptions.Default),
                OpenAIResponseOutputItemAdded.TypeName => JsonSerializer.Deserialize<OpenAIResponseOutputItemAdded>(message, JsonOptions.Default),
                OpenAIResponseOutputItemDone.TypeName => JsonSerializer.Deserialize<OpenAIResponseOutputItemDone>(message, JsonOptions.Default),
                OpenAISessionCreated.TypeName => JsonSerializer.Deserialize<OpenAISessionCreated>(message, JsonOptions.Default),
                OpenAISessionUpdated.TypeName => JsonSerializer.Deserialize<OpenAISessionUpdated>(message, JsonOptions.Default),
                _ => Option<OpenAIServerEventBase>.None
            };
        }

        return Option<OpenAIServerEventBase>.None;
    }
}
