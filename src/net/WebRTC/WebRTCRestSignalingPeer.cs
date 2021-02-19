//-----------------------------------------------------------------------------
// Filename: WebRTCRestSignalingPeer.cs
//
// Description: This class is NOT a required component for using WebRTC. It is a
// convenience class provided to perform the signaling via a HTTP REST server.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 29 Sep 2020	Aaron Clauson	Created, Dublin, Ireland.
// 27 Jan 2021  Aaron Clauson   Switched from the nodejs Dead Simple signalling
//                              server https://github.com/bengreenier/node-dss to
//                              a custom HTTP REST API. The node-dss option was
//                              a bit too simple.
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
    public enum WebRTCSignalTypesEnum
    {
        any = 0,        // Any message type.
        sdp = 2,        // SDP offer or answer.
        ice = 3         // ICE candidates
    }

    /// <summary>
    /// This class is not a required component for using WebRTC. It is a
    /// convenience class provided to perform the signalling via a HTTP REST server.
    /// </summary>
    public class WebRTCRestSignalingPeer
    {
        private const int REST_SERVER_POLL_PERIOD = 2000;   // Period in milliseconds to poll the HTTP server to check for new messages.
        private const int CONNECTION_RETRY_PERIOD = 5000;   // Period in milliseconds to retry if the initial HTTP connection attempt fails.

        private ILogger logger = SIPSorcery.Sys.Log.Logger;

        private Uri _restServerUri;
        private string _ourID;
        private string _theirID;
        private bool _isReceiving;
        private Task _receiveTask;
        private Func<Task<RTCPeerConnection>> _createPeerConnection;

        private RTCPeerConnection _pc;
        public RTCPeerConnection RTCPeerConnection => _pc;

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

        /// <summary>
        /// Default constructor.
        /// </summary>
        /// <param name="restServerUri">The base URI of the HTTP REST server API.</param>
        /// <param name="ourID">The arbitrary ID this peer is using.</param>
        /// <param name="theirID">The arbitrary ID the remote peer is using.</param>
        /// <param name="createPeerConnection">Function delegate used to create a new WebRTC peer connection.</param>
        public WebRTCRestSignalingPeer(
            string restServerUri,
            string ourID,
            string theirID,
            Func<Task<RTCPeerConnection>> createPeerConnection)
        {
            if (string.IsNullOrWhiteSpace(restServerUri))
            {
                throw new ArgumentNullException($"The {nameof(restServerUri)} parameter must be set.");
            }

            if (string.IsNullOrWhiteSpace(ourID))
            {
                throw new ArgumentNullException(nameof(ourID));
            }

            if (string.IsNullOrWhiteSpace(theirID))
            {
                throw new ArgumentNullException(nameof(theirID));
            }

            _restServerUri = new Uri(restServerUri);
            _ourID = ourID;
            _theirID = theirID;
            _createPeerConnection = createPeerConnection;
        }

        /// <summary>
        /// Creates a new WebRTC peer connection and then starts polling the REST server.
        /// If there is an offer waiting for this peer it will be retrieved and an answer posted.
        /// If no offer is available we will post one and then poll for the answer,
        /// </summary>
        public async Task Start(CancellationTokenSource cancellation)
        {
            var peerConnectedCancellation = new CancellationTokenSource();
            CancellationTokenSource linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token, peerConnectedCancellation.Token);

            var restClient = new HttpClient();

            _pc = await _createPeerConnection().ConfigureAwait(false);
            _pc.onconnectionstatechange += (state) =>
            {
                if (_isReceiving && !(state == RTCPeerConnectionState.@new || state == RTCPeerConnectionState.connecting))
                {
                    logger.LogDebug("cancelling HTTP receive task.");
                    peerConnectedCancellation?.Cancel();
                }
            };
            _pc.onicecandidate += async (cand) =>
            {
                if (cand.type != RTCIceCandidateType.host)
                {
                    // Host candidates are always included in the SDP offer or answer.
                    logger.LogDebug($"webrtc-rest onicecandidate: {cand.ToShortString()}.");
                    await SendToSignalingServer(restClient, cand.toJSON(), WebRTCSignalTypesEnum.ice);
                }
            };

            logger.LogDebug($"webrtc-rest starting receive task for server {_restServerUri}, our ID {_ourID} and their ID {_theirID}.");

            _receiveTask = Task.Run(() => ReceiveFromNSS(restClient, _pc, linkedSource.Token));
        }

        /// <summary>
        /// Creates a new WebRTC peer connection and send an SDP offer to the REST server.
        /// </summary>
        private async Task SendOffer(HttpClient httpClient)
        {
            logger.LogDebug("webrtc-rest sending initial SDP offer to server.");

            var offerSdp = _pc.createOffer(OfferOptions);

            await _pc.setLocalDescription(offerSdp).ConfigureAwait(false);

            await SendToSignalingServer(httpClient, offerSdp.toJSON(), WebRTCSignalTypesEnum.sdp).ConfigureAwait(false);
        }

        private async Task SendToSignalingServer(HttpClient httpClient, string jsonStr, WebRTCSignalTypesEnum sendType)
        {
            var content = new StringContent(jsonStr, Encoding.UTF8, "application/json");
            var res = await httpClient.PutAsync($"{_restServerUri}/{sendType}/{_ourID}/{_theirID}", content).ConfigureAwait(false);

            logger.LogDebug($"webrtc-rest PUT result for {_restServerUri}/{sendType}/{_ourID}/{_theirID} {res.StatusCode}.");
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
                        res = await httpClient.GetAsync($"{_restServerUri}/{_ourID}/{_theirID}", ct).ConfigureAwait(false);
                    }
                    catch (HttpRequestException e)
                        when (e.InnerException is SocketException && (e.InnerException as SocketException).SocketErrorCode == SocketError.ConnectionRefused)
                    {
                        if (isInitialReceive)
                        {
                            logger.LogDebug($"webrtc-rest server initial connection attempt failed, will retry in {CONNECTION_RETRY_PERIOD}ms.");
                            await Task.Delay(CONNECTION_RETRY_PERIOD).ConfigureAwait(false);
                            continue;
                        }
                        else
                        {
                            logger.LogWarning($"webrtc-rest server connection attempt failed.");
                            break;
                        }
                    }

                    if (res.StatusCode == HttpStatusCode.OK)
                    {
                        var signal = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                        var resp = await OnMessage(signal, pc).ConfigureAwait(false);

                        if (resp != null)
                        {
                            await SendToSignalingServer(httpClient, resp, WebRTCSignalTypesEnum.sdp).ConfigureAwait(false);
                        }
                    }
                    else if (res.StatusCode == HttpStatusCode.NoContent)
                    {
                        if (isInitialReceive)
                        {
                            // We are the first peer to connect. Send the offer so it will be waiting
                            // for the remote peer.
                            await SendOffer(httpClient).ConfigureAwait(false);
                        }
                        else
                        {
                            // There are no waiting messages for us.
                            await Task.Delay(REST_SERVER_POLL_PERIOD).ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        throw new ApplicationException($"Get request to REST server failed with response code {res.StatusCode}.");
                    }

                    isInitialReceive = false;
                }
            }
            catch (OperationCanceledException) // Thrown if the task is explicitly cancelled by the consumer using a cancellation token.
            { }
            catch (Exception excp)
            {
                logger.LogError($"Exception receiving webrtc signal. {excp}");
            }
            finally
            {
                logger.LogDebug("webrtc-rest receive task exiting.");
                _isReceiving = false;
            }
        }

        private async Task<string> OnMessage(string signal, RTCPeerConnection pc)
        {
            string sdpAnswer = null;

            if (RTCIceCandidateInit.TryParse(signal, out var iceCandidateInit))
            {
                logger.LogDebug($"Got remote ICE candidate, {iceCandidateInit.candidate}");

                bool useCandidate = true;
                if (FilterRemoteICECandidates != null && !string.IsNullOrWhiteSpace(iceCandidateInit.candidate))
                {
                    useCandidate = FilterRemoteICECandidates(iceCandidateInit);
                }

                if (!useCandidate)
                {
                    logger.LogDebug($"WebRTCRestPeer excluding ICE candidate due to filter: {iceCandidateInit.candidate}");
                }
                else
                {
                    _pc.addIceCandidate(iceCandidateInit);
                }
            }
            else if (RTCSessionDescriptionInit.TryParse(signal, out var descriptionInit))
            {
                logger.LogDebug($"Got remote SDP, type {descriptionInit.type}.");
                //logger.LogDebug(descriptionInit.sdp);

                var result = pc.setRemoteDescription(descriptionInit);
                
                if (result != SetDescriptionResultEnum.OK)
                {
                    logger.LogWarning($"Failed to set remote description, {result}.");
                    pc.Close("failed to set remote description");
                }
                else if (descriptionInit.type == RTCSdpType.offer)
                {
                    var answerSdp = pc.createAnswer(AnswerOptions);
                    await pc.setLocalDescription(answerSdp).ConfigureAwait(false);

                    sdpAnswer = answerSdp.toJSON();
                }
            }
            else
            {
                logger.LogWarning($"webrtc-rest could not parse JSON message. {signal}");
            }

            return sdpAnswer;
        }
    }
}
