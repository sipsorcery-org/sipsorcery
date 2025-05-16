using System;
using System.Linq;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using SIPSorcery.Media;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Threading.Tasks;
using LanguageExt;
using LanguageExt.Common;
using System.Text.Json;
using System.Text;

namespace demo;

public class OpenAIRealtimeWebRTCEndPoint : IOpenAIRealtimeWebRTCEndPoint
{
    private const string OPENAI_MODEL = "gpt-4o-realtime-preview-2024-12-17";
    private const string OPENAI_DATACHANNEL_NAME = "oai-events";

    private ILogger _logger = NullLogger.Instance;

    public AudioEncoder AudioEncoder { get; }
    public AudioFormat AudioFormat { get; }

    private readonly IOpenAIRealtimeRestClient _openAIRealtimeRestClient;

    private RTCPeerConnection? _rtcPeerConnection = null;
    public RTCPeerConnection? PeerConnection => _rtcPeerConnection;

    public event Action<IPEndPoint, SDPMediaTypesEnum, RTPPacket>? OnRtpPacketReceived;

    public event Action? OnPeerConnectionConnected;

    public event Action? OnPeerConnectionClosedOrFailed;

    public event Action<RTCDataChannel, OpenAIServerEventBase>? OnDataChannelMessageReceived;

    public OpenAIRealtimeWebRTCEndPoint(
        ILogger<OpenAIRealtimeWebRTCEndPoint> logger,
        IOpenAIRealtimeRestClient openAIRealtimeRestClient)
    {
        _logger = logger;
        _openAIRealtimeRestClient = openAIRealtimeRestClient;

        AudioEncoder = new AudioEncoder(includeOpus: true);
        AudioFormat = AudioEncoder.SupportedFormats.Single(x => x.FormatName == AudioCodecsEnum.OPUS.ToString());
    }

    public async Task<Either<Error, Unit>> StartConnectAsync(RTCConfiguration? pcConfig = null, string? model = null)
    {
        if(_rtcPeerConnection != null)
        {
            return Unit.Default;
        }

        _rtcPeerConnection = CreatePeerConnection(pcConfig);

        var useModel = string.IsNullOrWhiteSpace(model) ? OPENAI_MODEL : model;

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

        MediaStreamTrack audioTrack = new MediaStreamTrack(AudioFormat, MediaStreamStatusEnum.SendRecv);
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

        _rtcPeerConnection.onconnectionstatechange += (state) =>
        {
            if (state is RTCPeerConnectionState.closed or
                RTCPeerConnectionState.failed or
                RTCPeerConnectionState.disconnected)
            {
                OnPeerConnectionClosedOrFailed?.Invoke();
            }
        };

        dataChannel.onopen += () => OnPeerConnectionConnected?.Invoke();

        dataChannel.onmessage += OnDataChannelMessage;

        dataChannel.onclose += () => OnPeerConnectionClosedOrFailed?.Invoke();

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
                Instructions = "You are a joke bot. Tell a Dad joke every chance you get.",
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

        _logger.LogInformation($"Sending initial response create to first call data channel {dc.label}.");
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

        _logger.LogInformation($"Sending initial response create to first call data channel {dc.label}.");
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
        var serverEvent = JsonSerializer.Deserialize<OpenAIServerEventBase>(message, JsonOptions.Default);

        var serverEventModel = OpenAIDataChannelManager.ParseDataChannelMessage(data);
        serverEventModel.IfSome(e =>
        {
            OnDataChannelMessageReceived?.Invoke(dc, e);
        });
    }
}
