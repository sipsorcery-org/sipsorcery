//-----------------------------------------------------------------------------
// Filename: WebRTCNodeDssPeer.cs
//
// Description: This class is not a required component for using WebRTC. It is a
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
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

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
        private ILogger logger = SIPSorcery.Sys.Log.Logger;

        private Uri _nodeDssServerUri;
        private string _ourID;
        private string _theirID;
        private bool _isReceiving;
        private Func<RTCPeerConnection> _createPeerConnection;

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
            Func<RTCPeerConnection> createPeerConnection)
        {
            if(string.IsNullOrWhiteSpace(nodeDssServer))
            {
                throw new ArgumentNullException("The node DSS server URI must be supplied.");
            }

            if(string.IsNullOrWhiteSpace(ourID))
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
        /// Creates a new WebRTC peer connection and send an SDP offer to the node DSS server.
        /// </summary>
        public async Task StartSendOffer()
        {
            var nodeDssClient = new HttpClient();
            var pc = _createPeerConnection();

            var offerSdp = pc.createOffer(null);
            await pc.setLocalDescription(offerSdp);

            var offerJson = JsonConvert.SerializeObject(offerSdp, new Newtonsoft.Json.Converters.StringEnumConverter());
            await SendToNSS(nodeDssClient, offerJson);

            StartNodeDssPolling(pc, nodeDssClient);
        }

        /// <summary>
        /// Creates a new WebRTC peer connection and then starts polling the node DSS server for an SDP offer
        /// from the remote peer.
        /// </summary>
        public void StartWaitForOffer()
        {
            var nodeDssClient = new HttpClient();
            var pc = _createPeerConnection();

            StartNodeDssPolling(pc, nodeDssClient);
        }

        private void StartNodeDssPolling(RTCPeerConnection pc, HttpClient nodeDssClient)
        {
            CancellationTokenSource connectedCts = new CancellationTokenSource();
            pc.onconnectionstatechange += (state) =>
            {
                if (_isReceiving && !(state == RTCPeerConnectionState.@new || state == RTCPeerConnectionState.connecting))
                {
                    logger.LogDebug("cancelling node DSS receive task.");
                    connectedCts.Cancel();
                }
            };
            _ = Task.Run(() => ReceiveFromNSS(nodeDssClient, pc, connectedCts.Token));
        }

        private async Task SendToNSS(HttpClient httpClient, string jsonStr)
        {
            var content = new StringContent(jsonStr, Encoding.UTF8, "application/json");
            var res = await httpClient.PostAsync($"{_nodeDssServerUri}data/{_theirID}", content);

            logger.LogDebug($"node-dss POST result for {_nodeDssServerUri}data/{_theirID} {res.StatusCode}.");
        }

        private async Task ReceiveFromNSS(HttpClient httpClient, RTCPeerConnection pc, CancellationToken ct)
        {
            _isReceiving = true;

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var res = await httpClient.GetAsync($"{_nodeDssServerUri}data/{_ourID}");

                    if (res.StatusCode == HttpStatusCode.OK)
                    {
                        var content = await res.Content.ReadAsStringAsync();
                        var resp = await OnMessage(content, pc);

                        if(resp != null)
                        {
                            await SendToNSS(httpClient, resp);
                        }
                    }
                    else if (res.StatusCode == HttpStatusCode.NotFound)
                    {
                        // Expected response when there are no waiting messages for us.
                        await Task.Delay(500);
                    }
                    else
                    {
                        throw new ApplicationException($"Get request to node DSS server failed with response code {res.StatusCode}.");
                    }
                }
            }
            finally
            {
                _isReceiving = false;
            }
        }

        private async Task<string> OnMessage(string jsonStr, RTCPeerConnection pc)
        {
            if (Regex.Match(jsonStr, @"^[^,]*candidate").Success)
            {
                logger.LogDebug("Got remote ICE candidate.");
                var iceCandidateInit = JsonConvert.DeserializeObject<RTCIceCandidateInit>(jsonStr);
                pc.addIceCandidate(iceCandidateInit);
            }
            else
            {
                RTCSessionDescriptionInit descriptionInit = JsonConvert.DeserializeObject<RTCSessionDescriptionInit>(jsonStr);
                var result = pc.setRemoteDescription(descriptionInit);
                if (result != SetDescriptionResultEnum.OK)
                {
                    logger.LogWarning($"Failed to set remote description, {result}.");
                    pc.Close("failed to set remote description");
                }

                if(descriptionInit.type == RTCSdpType.offer)
                {
                    var answerSdp = pc.createAnswer(null);
                    await pc.setLocalDescription(answerSdp);

                    return JsonConvert.SerializeObject(answerSdp, new Newtonsoft.Json.Converters.StringEnumConverter());
                }
            }

            return null;
        }
    }
}
