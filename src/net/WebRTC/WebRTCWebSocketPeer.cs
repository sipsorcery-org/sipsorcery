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

        public Func<Task<RTCPeerConnection>> CreatePeerConnection;

        public WebRTCWebSocketPeer()
        { }

        protected override void OnMessage(MessageEventArgs e)
        {
            logger.LogDebug($"OnMessage: {e.Data}");

            if (RTCIceCandidateInit.TryParse(e.Data, out var iceCandidateInit))
            {
                logger.LogDebug("Got remote ICE candidate.");
                _pc.addIceCandidate(iceCandidateInit);
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

            _pc = await CreatePeerConnection();

            var offerSdp = _pc.createOffer(null);
            await _pc.setLocalDescription(offerSdp);
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
