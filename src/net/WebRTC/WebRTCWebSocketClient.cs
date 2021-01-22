//-----------------------------------------------------------------------------
// Filename: WebRTCWebSocketClient.cs
//
// Description: This class is NOT a required component for using WebRTC. It is a
// convenience class provided to assist when using a corresponding WebRTC peer 
// running a web socket server (which is the case for most of the demo applications
// that go with this library).
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 01 Oct 2020	Aaron Clauson	Created, Dublin, Ireland.
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

namespace SIPSorcery.Net
{
    /// <summary>
    /// This class is NOT a required component for using WebRTC. It is a
    /// convenience class provided to assist when using a corresponding WebRTC peer 
    /// running a web socket server (which is the case for most of the demo applications
    /// that go with this library).
    /// </summary>
    public class WebRTCWebSocketClient
    {
        private const int MAX_RECEIVE_BUFFER = 8192;
        private const int MAX_SEND_BUFFER = 8192;
        private const int WEB_SOCKET_CONNECTION_TIMEOUT_MS = 10000;

        private ILogger logger = SIPSorcery.Sys.Log.Logger;

        private Uri _webSocketServerUri;
        private Func<Task<RTCPeerConnection>> _createPeerConnection;

        private RTCPeerConnection _pc;
        public RTCPeerConnection RTCPeerConnection => _pc;

        /// <summary>
        /// Default constructor.
        /// </summary>
        /// <param name="webSocketServer">The web socket server URL to connect to for the SDP and 
        /// ICE candidate exchange.</param>
        public WebRTCWebSocketClient(
            string webSocketServer,
            Func<Task<RTCPeerConnection>> createPeerConnection)
        {
            if (string.IsNullOrWhiteSpace(webSocketServer))
            {
                throw new ArgumentNullException("The web socket server URI must be supplied.");
            }

            _webSocketServerUri = new Uri(webSocketServer);
            _createPeerConnection = createPeerConnection;
        }

        /// <summary>
        /// Creates a new WebRTC peer connection and then starts polling the web socket server.
        /// An SDP offer is expected from the server. Once it has been received an SDP answer 
        /// will be returned.
        /// </summary>
        public async Task Start(CancellationToken cancellation)
        {
            _pc = await _createPeerConnection().ConfigureAwait(false);

            logger.LogDebug($"websocket-client attempting to connect to {_webSocketServerUri}.");

            var webSocketClient = new ClientWebSocket();
            // As best I can tell the point of the CreateClientBuffer call is to set the size of the internal
            // web socket buffers. The return buffer seems to be for cases where direct access to the raw
            // web socket data is desired.
            _ = WebSocket.CreateClientBuffer(MAX_RECEIVE_BUFFER, MAX_SEND_BUFFER);
            CancellationTokenSource connectCts = new CancellationTokenSource();
            connectCts.CancelAfter(WEB_SOCKET_CONNECTION_TIMEOUT_MS);
            await webSocketClient.ConnectAsync(_webSocketServerUri, connectCts.Token).ConfigureAwait(false);

            if (webSocketClient.State == WebSocketState.Open)
            {
                logger.LogDebug($"websocket-client starting receive task for server {_webSocketServerUri}.");

                _ = Task.Run(() => ReceiveFromWebSocket(_pc, webSocketClient, cancellation)).ConfigureAwait(false);
            }
            else
            {
                _pc.Close("web socket connection failure");
            }
        }

        private async Task ReceiveFromWebSocket(RTCPeerConnection pc, ClientWebSocket ws, CancellationToken ct)
        {
            var buffer = new byte[MAX_RECEIVE_BUFFER];
            int posn = 0;

            while (ws.State == WebSocketState.Open &&
                (pc.connectionState == RTCPeerConnectionState.@new || pc.connectionState == RTCPeerConnectionState.connecting))
            {
                WebSocketReceiveResult receiveResult;
                do
                {
                    receiveResult = await ws.ReceiveAsync(new ArraySegment<byte>(buffer, posn, MAX_RECEIVE_BUFFER - posn), ct).ConfigureAwait(false);
                    posn += receiveResult.Count;
                }
                while (!receiveResult.EndOfMessage);

                if (posn > 0)
                {
                    var jsonMsg = Encoding.UTF8.GetString(buffer, 0, posn);
                    string jsonResp = await OnMessage(jsonMsg, pc).ConfigureAwait(false);

                    if (jsonResp != null)
                    {
                        await ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(jsonResp)), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
                    }
                }

                posn = 0;
            }

            logger.LogDebug($"websocket-client receive loop exiting.");
        }

        private async Task<string> OnMessage(string jsonStr, RTCPeerConnection pc)
        {
            if (RTCIceCandidateInit.TryParse(jsonStr, out var iceCandidateInit))
            {
                logger.LogDebug("Got remote ICE candidate.");
                pc.addIceCandidate(iceCandidateInit);
            }
            else if (RTCSessionDescriptionInit.TryParse(jsonStr, out var descriptionInit))
            {
                logger.LogDebug($"Got remote SDP, type {descriptionInit.type}.");

                var result = pc.setRemoteDescription(descriptionInit);
                if (result != SetDescriptionResultEnum.OK)
                {
                    logger.LogWarning($"Failed to set remote description, {result}.");
                    pc.Close("failed to set remote description");
                }

                if (descriptionInit.type == RTCSdpType.offer)
                {
                    var answerSdp = pc.createAnswer(null);
                    await pc.setLocalDescription(answerSdp).ConfigureAwait(false);

                    return answerSdp.toJSON();
                }
            }
            else
            {
                logger.LogWarning($"websocket-client could not parse JSON message. {jsonStr}");
            }

            return null;
        }
    }
}
