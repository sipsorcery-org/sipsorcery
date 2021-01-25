//-----------------------------------------------------------------------------
// Filename: WebRTCNodeDssPeer.cs
//
// Description: This class is NOT a required component for using WebRTC. It is a
// convenience class provided to assist when using a nodejs Dead Simple signalling
// (DSS) server for the WebRTC signalling.
// See https://github.com/bengreenier/node-dss.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 29 Sep 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.Net
{
    /// <summary>
    /// This class is not a required component for using WebRTC. It is a convenience
    /// class provided to assist when using a nodejs Dead Simple signalling (DSS)
    /// server for the WebRTC signalling.
    /// </summary>
    /// <remarks>
    /// See https://github.com/bengreenier/node-dss.
    /// </remarks>
    public class WebRTCNodeDssPeer
    {
        private const int NODE_SERVER_POLL_PERIOD = 500;    // Period in milliseconds to poll the node server to check for new messages.
        private const int CONNECTION_RETRY_PERIOD = 5000;   // Period in milliseconds to retry if the initial node-dss connection attempt fails.

        private ILogger logger = SIPSorcery.Sys.Log.Logger;

        private Uri _nodeDssServerUri;
        private string _ourID;
        private string _theirID;
        private bool _isReceiving;
        private Func<Task<RTCPeerConnection>> _createPeerConnection;

        private RTCPeerConnection _pc;
        public RTCPeerConnection RTCPeerConnection => _pc;

        /// <summary>
        /// Default constructor.
        /// </summary>
        /// <param name="nodeDssServer">The base URI of the node Dead Simple Signalling (DSS) server.</param>
        /// <param name="ourID">The arbitrary ID this peer is using.</param>
        /// <param name="theirID">The arbitrary ID the remote peer is using.</param>
        /// <param name="createPeerConnection">Function delegate used to create a new WebRTC peer connection.</param>
        public WebRTCNodeDssPeer(
            string nodeDssServer,
            string ourID,
            string theirID,
            Func<Task<RTCPeerConnection>> createPeerConnection)
        {
            if (string.IsNullOrWhiteSpace(nodeDssServer))
            {
                throw new ArgumentNullException("The node DSS server URI must be supplied.");
            }

            if (string.IsNullOrWhiteSpace(ourID))
            {
                throw new ArgumentNullException("ourID");
            }

            if (string.IsNullOrWhiteSpace(theirID))
            {
                throw new ArgumentNullException("theirID");
            }

            _nodeDssServerUri = new Uri(nodeDssServer);
            _ourID = ourID;
            _theirID = theirID;
            _createPeerConnection = createPeerConnection;
        }

        /// <summary>
        /// Creates a new WebRTC peer connection and then starts polling the node DSS server.
        /// If there is an offer waiting for this peer it will be retrieved and an answer posted.
        /// If no offer is available we will post one and then poll for the answer,
        /// </summary>
        public async Task Start(CancellationTokenSource cancellation)
        {
            var peerConnectedCancellation = new CancellationTokenSource();
            CancellationTokenSource linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token, peerConnectedCancellation.Token);

            var nodeDssClient = new HttpClient();

            _pc = await _createPeerConnection().ConfigureAwait(false);
            _pc.onconnectionstatechange += (state) =>
            {
                if (_isReceiving && !(state == RTCPeerConnectionState.@new || state == RTCPeerConnectionState.connecting))
                {
                    logger.LogDebug("cancelling node DSS receive task.");
                    peerConnectedCancellation?.Cancel();
                }
            };

            logger.LogDebug($"node-dss starting receive task for server {_nodeDssServerUri}, our ID {_ourID} and their ID {_theirID}.");

            _ = Task.Run(() => ReceiveFromNSS(nodeDssClient, _pc, linkedSource.Token));
        }

        /// <summary>
        /// Creates a new WebRTC peer connection and send an SDP offer to the node DSS server.
        /// </summary>
        private async Task SendOffer(HttpClient httpClient)
        {
            logger.LogDebug("node-dss sending initial SDP offer to server.");

            var offerSdp = _pc.createOffer(null);

            await _pc.setLocalDescription(offerSdp).ConfigureAwait(false);

            await SendToNSS(httpClient, offerSdp.toJSON()).ConfigureAwait(false);
        }

        private async Task SendToNSS(HttpClient httpClient, string jsonStr)
        {
            var content = new StringContent(jsonStr, Encoding.UTF8, "application/json");
            var res = await httpClient.PostAsync($"{_nodeDssServerUri}data/{_theirID}", content).ConfigureAwait(false);

            logger.LogDebug($"node-dss POST result for {_nodeDssServerUri}data/{_theirID} {res.StatusCode}.");
        }

        private async Task ReceiveFromNSS(HttpClient httpClient, RTCPeerConnection pc, CancellationToken ct)
        {
            _isReceiving = true;

            try
            {
                bool isInitialReceive = true;

                while (!ct.IsCancellationRequested)
                {
                    HttpResponseMessage res = null;

                    try
                    {
                        res = await httpClient.GetAsync($"{_nodeDssServerUri}data/{_ourID}", ct).ConfigureAwait(false);
                    }
                    catch (HttpRequestException e)
                        when (e.InnerException is SocketException && (e.InnerException as SocketException).SocketErrorCode == SocketError.ConnectionRefused)
                    {
                        if (isInitialReceive)
                        {
                            logger.LogDebug($"node-dss server initial connection attempt failed, will retry in {CONNECTION_RETRY_PERIOD}ms.");
                            await Task.Delay(CONNECTION_RETRY_PERIOD).ConfigureAwait(false);
                            continue;
                        }
                        else
                        {
                            logger.LogDebug($"node-dss server connection attempt failed.");
                            break;
                        }
                    }

                    if (res.StatusCode == HttpStatusCode.OK)
                    {
                        var content = await res.Content.ReadAsStringAsync().ConfigureAwait(false);

                        if (!string.IsNullOrEmpty(content))
                        {
                            var resp = await OnMessage(content, pc).ConfigureAwait(false);

                            if (resp != null)
                            {
                                await SendToNSS(httpClient, resp).ConfigureAwait(false);
                            }
                        }
                        else if (isInitialReceive)
                        {
                            // We are the first peer to connect. Send the offer so it will be waiting
                            // for the remote peer.
                            await SendOffer(httpClient).ConfigureAwait(false);
                        }
                        else
                        {
                            // There are no waiting messages for us.
                            await Task.Delay(NODE_SERVER_POLL_PERIOD).ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        throw new ApplicationException($"Get request to node DSS server failed with response code {res.StatusCode}.");
                    }

                    isInitialReceive = false;
                }
            }
            finally
            {
                logger.LogDebug("node-dss receive task exiting.");
                _isReceiving = false;
            }
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
                logger.LogWarning($"node-dss could not parse JSON message. {jsonStr}");
            }

            return null;
        }
    }
}
