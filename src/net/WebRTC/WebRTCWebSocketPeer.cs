//-----------------------------------------------------------------------------
// Filename: WebRTCWebSocketPeer.cs
//
// Description: This class is NOT a required component for using WebRTC. It is a
// convenience class provided to assist when using a web socket server for the 
// WebRTC signalling.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 12 Sep 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace SIPSorcery.Net
{
    /// <summary>
    /// This class is NOT a required component for using WebRTC. It is a convenience
    /// class provided to assist when using a web socket server for the  WebRTC 
    /// signalling.
    /// </summary>
    public class WebRTCWebSocketPeer : WebSocketBehavior
    {
        private ILogger logger = SIPSorcery.Sys.Log.Logger;

        private RTCPeerConnection _pc;
        public RTCPeerConnection RTCPeerConnection => _pc;

        /// <summary>
        /// Optional property to allow the peer connection SDP offer options to be set.
        /// </summary>
        public RTCOfferOptions OfferOptions { get; set; }

        /// <summary>
        /// Optional filter that can be applied to remote ICE candidates. The filter is 
        /// primarily intended for use in testing. In real application scenarios it's 
        /// normally desirable to accept all remote ICE candidates.
        /// </summary>
        public Func<RTCIceCandidateInit, bool> FilterRemoteICECandidates { get; set; }

        public Func<Task<RTCPeerConnection>> CreatePeerConnection;

        public WebRTCWebSocketPeer()
        { }

        protected override void OnMessage(MessageEventArgs e)
        {
            logger.LogDebug($"OnMessage: {e.Data}");

            if (RTCIceCandidateInit.TryParse(e.Data, out var iceCandidateInit))
            {
                logger.LogDebug("Got remote ICE candidate.");

                bool useCandidate = true;
                if (FilterRemoteICECandidates != null && !string.IsNullOrWhiteSpace(iceCandidateInit.candidate))
                {
                    useCandidate = FilterRemoteICECandidates(iceCandidateInit);
                }

                if (!useCandidate)
                {
                    logger.LogDebug($"WebRTCWebSocketPeer excluding ICE candidate due to filter: {iceCandidateInit.candidate}");
                }
                else
                {
                    _pc.addIceCandidate(iceCandidateInit);
                }
            }
            else if (RTCSessionDescriptionInit.TryParse(e.Data, out var descriptionInit))
            {
                logger.LogDebug($"Got remote SDP, type {descriptionInit.type}.");

                var result = _pc.setRemoteDescription(descriptionInit);
                if (result != SetDescriptionResultEnum.OK)
                {
                    logger.LogWarning($"Failed to set remote description, {result}.");
                    _pc.Close("failed to set remote description");
                    this.Close();
                }
            }
            else
            {
                logger.LogWarning($"websocket-server could not parse JSON message. {e.Data}");
            }
        }

        protected override async void OnOpen()
        {
            base.OnOpen();

            logger.LogDebug($"Web socket client connection from {Context.UserEndPoint}.");

            _pc = await CreatePeerConnection().ConfigureAwait(false);

            var offerSdp = _pc.createOffer(OfferOptions);
            await _pc.setLocalDescription(offerSdp).ConfigureAwait(false);
            _pc.onicecandidate += (iceCandidate) =>
            {
                if (_pc.signalingState == RTCSignalingState.have_remote_offer)
                {
                    Context.WebSocket.Send(iceCandidate.toJSON());
                }
            };

            logger.LogDebug($"Sending SDP offer to client {Context.UserEndPoint}.");
            logger.LogDebug(offerSdp.sdp);

            Context.WebSocket.Send(offerSdp.toJSON());
        }
    }
}
