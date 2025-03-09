//-----------------------------------------------------------------------------
// Filename: WebSocketService.cs
//
// Description: A background service to host a web socket server listener. New
// websocket connections are assumed to want to create a WebRTC connection.
// The websocket is acting as the WebRTC signalling transport.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 23 Feb 2025	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using WebSocketSharp.Server;

namespace demo;

public class WebSocketService : BackgroundService
{
    private const int WEBSOCKET_PORT = 8081;

    private readonly ILogger<WebSocketService> _logger;
    private readonly IPaidWebRtcConnectionFactory _webRtcConnectionManagerFactory;
    private WebSocketServer _webSocketServer;

    public WebSocketService(
        ILogger<WebSocketService> logger,
        IPaidWebRtcConnectionFactory webRtcConnectionManagerFactory)
    {
        _logger = logger;
        _webRtcConnectionManagerFactory = webRtcConnectionManagerFactory;
        _webSocketServer = new WebSocketServer(IPAddress.Any, WEBSOCKET_PORT);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting WebRTC WebSocket server...");

        StartWebSocketServer();

        return Task.CompletedTask;
    }

    private void StartWebSocketServer()
    {
        _webSocketServer.AddWebSocketService<WebRTCWebSocketPeer>("/", (peer) => peer.CreatePeerConnection = async () =>
        {
            IPaidWebRtcConnection webRtcConnectionManager = _webRtcConnectionManagerFactory.CreatePaidWebRTCConnection(peer.ID);
            var pc = await webRtcConnectionManager.CreatePeerConnection(peer.ID);
            _logger.LogInformation($"Peer connection {peer.ID} successfully created.");
            return pc;
        });
        _webSocketServer.Start();

        _logger.LogInformation($"Waiting for web socket connections on {_webSocketServer.Address}:{_webSocketServer.Port}...");
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping WebRTC WebSocket server...");
        _webSocketServer?.Stop();
        return Task.CompletedTask;
    }
}
