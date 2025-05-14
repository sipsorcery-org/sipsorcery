//-----------------------------------------------------------------------------
// Filename: WebRTCWebSocketPeerAspNet.cs
//
// Description: This class is NOT a required component for using WebRTC. It is a
// convenience class provided to assist when using a web socket server for the
// WebRTC signalling.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 09 Apr 2025	Aaron Clauson	Created, Dublin, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.Net;

/// <summary>
/// This class is NOT a required component for using WebRTC. It is a convenience
/// class provided to assist when using an ASP.NET web socket server for the WebRTC
/// signalling.
/// </summary>
public class WebRTCWebSocketPeerAspNet
{
    private ILogger _logger = SIPSorcery.Sys.Log.Logger;

    private RTCConfiguration _peerConnectionConfig;
    private RTCPeerConnection _pc;
    public RTCPeerConnection RTCPeerConnection => _pc;

    private readonly WebSocket _webSocket;
    private readonly RTCSdpType _peerRole;
    private readonly CancellationTokenSource _cts;

    private bool _keepalive = false;
    public TimeSpan KeepAliveTime = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Optional property to allow the peer connection SDP offer options to be set.
    /// </summary>
    public RTCOfferOptions OfferOptions { get; set; }

    /// <summary>
    /// Optional property to allow the peer connection SDP answer options to be set.
    /// </summary>
    public RTCAnswerOptions AnswerOptions { get; set; }

    /// <summary>
    /// Optional filter that can be applied to remote ICE candidates. The filter is
    /// primarily intended for use in testing. In real application scenarios it's
    /// normally desirable to accept all remote ICE candidates.
    /// </summary>
    public Func<RTCIceCandidateInit, bool> FilterRemoteICECandidates { get; set; }

    public Func<RTCConfiguration, Task<RTCPeerConnection>> CreatePeerConnection;

    public event Action OnRTCPeerConnectionConnected;

    public WebRTCWebSocketPeerAspNet(
        WebSocket webSocket,
        Func<RTCConfiguration, Task<RTCPeerConnection>> createPeerConnection,
        RTCConfiguration peerConnectionConfig,
        RTCSdpType peerRole,
        bool keepalive = false,
        CancellationToken cancellationToken = default
        )
    {
        _webSocket = webSocket;
        CreatePeerConnection = createPeerConnection;
        _peerConnectionConfig = peerConnectionConfig;
        _peerRole = peerRole;
        _cts = new CancellationTokenSource();
        _keepalive = keepalive;

        if (cancellationToken != default)
        {
            cancellationToken.Register(async () => await Close());
        }
    }

    public async Task Run()
    {
        _logger.LogDebug("Web socket client connection established.");

        _pc = await CreatePeerConnection(_peerConnectionConfig);

        _pc.onicecandidate += async (iceCandidate) =>
        {
            _logger.LogDebug("Got local ICE candidate, {Candidate}.", iceCandidate.candidate);

            if (_pc.signalingState == RTCSignalingState.have_remote_offer ||
                _pc.signalingState == RTCSignalingState.stable)
            {
                await SendMessageAsync(iceCandidate.toJSON());
            }
        };

        _pc.onconnectionstatechange += (state) =>
        {
            _logger.LogDebug("{caller} peer connection state changed to {state}.", nameof(WebRTCWebSocketPeerAspNet),  state);

            if(state == RTCPeerConnectionState.connected)
            {
                OnRTCPeerConnectionConnected?.Invoke();

                _cts.Cancel();
            }
        };

        if (_peerRole == RTCSdpType.offer)
        {
            await SendOffer();
        }

        if (_keepalive)
        {
            _ = Task.Run(async () =>
            {
                while (!_cts.Token.IsCancellationRequested && _webSocket.State == WebSocketState.Open)
                {
                    await Task.Delay(KeepAliveTime);
                    _logger.LogTrace("Sending signaling channel keep alive 'ping'.");
                    await SendMessageAsync("ping");
                }
            });
        }

        // Start the web socket receiving loop.
        await StartReceivingAsync(_cts.Token);
    }

    private async Task SendOffer()
    {
        _logger.LogDebug("Generating SDP offer to send to web socket client.");

        var offerSdp = _pc.createOffer(OfferOptions);
        await _pc.setLocalDescription(offerSdp);

        _logger.LogDebug("Sending SDP offer to web socket client.");

        try
        {
            await SendMessageAsync(offerSdp.toJSON());
        }
        catch (Exception ex)
        {
            _logger.LogError("An error has occurred sending web socket message to client.\n{Exception}.", ex);
        }
    }

    private async Task StartReceivingAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("{name} commenced start receiving.", nameof(WebRTCWebSocketPeerAspNet));

        var buffer = new byte[1024 * 4];

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var receiveResult = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                if (receiveResult.MessageType == WebSocketMessageType.Close)
                {
                    _logger.LogDebug("{name} close message received from remote web socket client.", nameof(WebRTCWebSocketPeerAspNet));

                    await Close();
                    break;
                }
                else
                {
                    await OnMessage(receiveResult, buffer);
                }
            }
        }
        catch (System.OperationCanceledException)
        {
            _logger.LogDebug("{caller} stopped due to application cancellation request.", nameof(StartReceivingAsync));
        }
        catch (Exception ex)
        {
            _logger.LogError("Error while receiving WebSocket message: {Exception}", ex.ToString());
        }
    }

    private async Task OnMessage(WebSocketReceiveResult receiveResult, byte[] buffer)
    {
        string message = Encoding.UTF8.GetString(buffer, 0, receiveResult.Count);
        _logger.LogDebug($"Received message: {message}");

        if (RTCIceCandidateInit.TryParse(message, out var iceCandidateInit))
        {
            _logger.LogDebug("Got remote ICE candidate.");

            bool useCandidate = true;
            if (FilterRemoteICECandidates != null && !string.IsNullOrWhiteSpace(iceCandidateInit.candidate))
            {
                useCandidate = FilterRemoteICECandidates(iceCandidateInit);
            }

            if (!useCandidate)
            {
                _logger.LogDebug("Excluding ICE candidate due to filter: {Candidate}", iceCandidateInit.candidate);
            }
            else
            {
                _pc.addIceCandidate(iceCandidateInit);
            }
        }
        else if (RTCSessionDescriptionInit.TryParse(message, out var descriptionInit))
        {
            _logger.LogDebug("Got remote SDP, type {DescriptionType}.", descriptionInit.type);

            var result = _pc.setRemoteDescription(descriptionInit);
            if (result != SetDescriptionResultEnum.OK)
            {
                _logger.LogWarning("Failed to set remote description, {Result}\n{remoteSDP}.", result, descriptionInit.sdp);
                _pc.Close("failed to set remote description");
                await this.Close();
            }
            else
            {
                if (_pc.signalingState == RTCSignalingState.have_remote_offer)
                {
                    var answerSdp = _pc.createAnswer(AnswerOptions);
                    await _pc.setLocalDescription(answerSdp).ConfigureAwait(false);

                    _logger.LogDebug("Sending SDP answer to client.");
                    await SendMessageAsync(answerSdp.toJSON());
                }
            }
        }
        else
        {
            _logger.LogWarning("WebSocket server could not parse JSON message. {MessageData}", message);
        }
    }

    public async Task Close()
    {
        _logger.LogDebug("WebSocket connection closed.");

        // Cancel the receive loop.
        if (!_cts.IsCancellationRequested)
        {
            _cts.Cancel();
        }

        if (_webSocket.State == WebSocketState.Open)
        {
            await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed by the server", CancellationToken.None);
        }
    }

    /// <summary>
    /// Helper method to send a string message to the WebSocket connection.
    /// </summary>
    private async Task SendMessageAsync(string message)
    {
        _logger.LogDebug("{name} sending message to remote web socket client.", nameof(WebRTCWebSocketPeerAspNet));

        var messageBytes = Encoding.UTF8.GetBytes(message);
        var messageSegment = new ArraySegment<byte>(messageBytes);

        await _webSocket.SendAsync(messageSegment, WebSocketMessageType.Text, true, CancellationToken.None);
    }
}
