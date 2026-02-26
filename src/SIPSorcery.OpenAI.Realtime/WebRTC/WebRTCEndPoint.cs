//-----------------------------------------------------------------------------
// Filename: WebRTCEndPoint.cs
//
// Description: WebRTC end point for the OpenAI Realtime API. This end point is
// used to establish a WebRTC connection with the OpenAI Realtime API and
// send/receive audio and data channel messages.
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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SIPSorcery.Net;
using SIPSorcery.OpenAI.Realtime.Models;
using SIPSorceryMedia.Abstractions;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace SIPSorcery.OpenAI.Realtime;

public class WebRTCEndPoint : IWebRTCEndPoint, IDisposable
{
    public const string OPENAI_DATACHANNEL_NAME = "oai-events";

    private ILogger _logger = NullLogger.Instance;

    private readonly IWebRTCRestClient _openAIRealtimeRestClient;

    private bool _disposed = false;

    public Option<RTCPeerConnection> PeerConnection { get; private set; } = Option<RTCPeerConnection>.None;

    public DataChannelMessenger DataChannelMessenger { get; private set; }

    /// <summary>
    /// Event for receiving an encoded media frame from the remote party. Encoded in this
    /// case refers to media encoding, e.g. for audio PCMU, OPUS etc. Currently the OpenAI 
    /// Realtime API only supports audio media frames so the encoded media frames will always
    /// be OPUS encoded audio frames.
    /// </summary>
    public event Action<EncodedAudioFrame>? OnAudioFrameReceived;

    public event Action? OnPeerConnectionConnected;

    public event Action? OnPeerConnectionFailed;

    public event Action? OnPeerConnectionClosed;

    /// <summary>
    /// Raised whenever a parsed OpenAI server event arrives on the data channel.
    /// </summary>
    public event Action<RTCDataChannel, RealtimeEventBase>? OnDataChannelMessage;

    /// <summary>
    /// Preferred constructor for dependency injection.
    /// </summary>
    /// <param name="logger">Logging instance for this class.</param>
    /// <param name="dataChannelMessengerLogger">Dedicated logging instance for data channel messenger class.</param>
    /// <param name="openAIRealtimeRestClient">Client for calls to OpenAI REST endpoint.</param>
    public WebRTCEndPoint(
        ILogger<WebRTCEndPoint> logger,
        ILogger<DataChannelMessenger> dataChannelMessengerLogger,
        IWebRTCRestClient openAIRealtimeRestClient)
    {
        _logger = logger;
        _openAIRealtimeRestClient = openAIRealtimeRestClient;

        DataChannelMessenger = new DataChannelMessenger(this, dataChannelMessengerLogger);
    }

    /// <summary>
    /// Constructor for use when not using dependency injection.
    /// </summary>
    /// <param name="openAiKey">The OpenAI bearer token API key.</param>
    /// <param name="loggerFactory">Logger factory to use for the end point.</param>
    public WebRTCEndPoint(string openAiKey, ILoggerFactory loggerFactory)
    {
        var openAIHttpClientFactory = new HttpClientFactory(openAiKey, loggerFactory);
        _openAIRealtimeRestClient = new WebRTCRestClient(openAIHttpClientFactory);

        _logger = loggerFactory.CreateLogger<DataChannelMessenger>();

        DataChannelMessenger = new DataChannelMessenger(this, _logger);
    }

    public async Task<Either<Error, Unit>> StartConnect(RTCConfiguration? pcConfig = null, RealtimeModelsEnum? model = null)
    {
        if (PeerConnection != null)
        {
            return Unit.Default;
        }

        var pc = CreatePeerConnection(pcConfig);
        PeerConnection =  Option<RTCPeerConnection>.Some(pc);

        var offer = pc.createOffer();
        await pc.setLocalDescription(offer).ConfigureAwait(false);

        var sdpAnswerResult = await _openAIRealtimeRestClient.GetSdpAnswerAsync(offer.sdp, model).ConfigureAwait(false);

        return sdpAnswerResult.Map(sdpAnswer =>
        {
            var answer = new RTCSessionDescriptionInit
            {
                type = RTCSdpType.answer,
                sdp  = sdpAnswer
            };
            pc.setRemoteDescription(answer);
            return Unit.Default;
        });
    }

    private RTCPeerConnection CreatePeerConnection(RTCConfiguration? pcConfig)
    {
        var pc = new RTCPeerConnection(pcConfig);

        MediaStreamTrack audioTrack = new MediaStreamTrack(AudioCommonlyUsedFormats.OpusWebRTC, MediaStreamStatusEnum.SendRecv);
        // Note: 1 Jun 2025 AC - I have not been able to get PCMU or PCMA to work reliably with OpenAI.
        //MediaStreamTrack audioTrack = new MediaStreamTrack(new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMU), MediaStreamStatusEnum.SendRecv);
        //MediaStreamTrack audioTrack = new MediaStreamTrack(new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMA), MediaStreamStatusEnum.SendRecv);
        pc.addTrack(audioTrack);

        // This call is synchronous when the WebRTC connection is not yet connected.
        var dataChannel = pc.createDataChannel(OPENAI_DATACHANNEL_NAME).Result;

        pc.onconnectionstatechange += state => _logger.LogDebug($"Peer connection connected changed to {state}.");
        pc.OnTimeout += mediaType => _logger.LogDebug($"Timeout on media {mediaType}.");
        pc.oniceconnectionstatechange += state => _logger.LogDebug($"ICE connection state changed to {state}.");

        pc.onsignalingstatechange += () =>
        {
            if (pc.signalingState == RTCSignalingState.have_local_offer)
            {
                _logger.LogTrace($"Local SDP:\n{pc.localDescription.sdp}");
            }
            else if (pc.signalingState is RTCSignalingState.have_remote_offer or RTCSignalingState.stable)
            {
                _logger.LogTrace($"Remote SDP:\n{pc.remoteDescription?.sdp}");
            }
        };

        pc.OnAudioFrameReceived += (encodedAudioFrame) => OnAudioFrameReceived?.Invoke(encodedAudioFrame);

        pc.onconnectionstatechange += (state) =>
        {
            if (state is RTCPeerConnectionState.failed)
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
        dataChannel.onmessage += DataChannelMessenger.HandleIncomingData;

        return pc;
    }

    public void SendAudio(uint durationRtpUnits, byte[] sample)
    {
        PeerConnection.Match(
            pc =>
            {
                if (pc.connectionState == RTCPeerConnectionState.connected)
                {
                    pc.SendAudio(durationRtpUnits, sample);
                }
            },
            () => _logger.LogError("No peer connection available to send audio.")
        );
    }

    public void SendDataChannelMessage(RealtimeEventBase message)
    {
        PeerConnection.Match(
            pc =>
            {
                var dc = pc.DataChannels.FirstOrDefault();
                if (dc == null)
                {
                    _logger.LogError("No data channel available to send message.");
                    return;
                }

                _logger.LogDebug($"Sending initial response create to first call data channel {dc.label}.");
                _logger.LogTrace(message.ToJson());

                dc.send(message.ToJson());
            },
            () => _logger.LogError("No peer connection available to send data channel message.")
        );
    }

    internal void InvokeOnDataChannelMessage(RTCDataChannel dc, RealtimeEventBase message)
        => OnDataChannelMessage?.Invoke(dc, message);

    /// <summary>
    /// Closes the PeerConnection and data channel (if open) and raises OnPeerConnectionClosed.
    /// </summary>
    public void Close()
    {
        if (_disposed)
        {
            return;
        }

        PeerConnection.IfSome(pc =>
        {
            _logger.LogDebug("Closing PeerConnection.");

            pc.Close("normal");

            OnPeerConnectionClosed?.Invoke();

            pc.OnAudioFrameReceived -= OnAudioFrameReceived;
        });

        PeerConnection = Option<RTCPeerConnection>.None;
    }

    /// <summary>
    /// Disposes the endpoint, closing any active PeerConnection.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        Close();

        GC.SuppressFinalize(this);
    }
}
