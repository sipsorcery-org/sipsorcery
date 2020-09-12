//-----------------------------------------------------------------------------
// Filename: WebRTCWebSocketPeer.cs
//
// Description: This class is not a required component for using WebRTC. It is a
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
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace SIPSorcery.Net
{
    /// <summary>
    /// This class is not a required component for using WebRTC. It is a convenience
    /// class provided to assist when using a web socket server for the  WebRTC 
    /// signalling.
    /// </summary>
    public class WebRTCWebSocketPeer : WebSocketBehavior
    {
        private ILogger logger = SIPSorcery.Sys.Log.Logger;

        private RTCPeerConnection _pc;
        public Func<RTCPeerConnection> CreatePeerConnection;

        public WebRTCWebSocketPeer()
        { }

        protected override void OnMessage(MessageEventArgs e)
        {
            logger.LogDebug($"OnMessage: {e.Data}");

            if (Regex.Match(e.Data, @"^.*?candidate.*?,").Success)
            {
                logger.LogDebug("Got remote ICE candidate.");
                var iceCandidateInit = JsonConvert.DeserializeObject<RTCIceCandidateInit>(e.Data);
                _pc.addIceCandidate(iceCandidateInit);
            }
            else
            {
                RTCSessionDescriptionInit descriptionInit = JsonConvert.DeserializeObject<RTCSessionDescriptionInit>(e.Data);
                var result = _pc.setRemoteDescription(descriptionInit);
                if (result != SetDescriptionResultEnum.OK)
                {
                    logger.LogWarning($"Failed to set remote description, {result}.");
                    _pc.Close("failed to set remote description");
                    this.Close();
                }
            }
        }

        protected override async void OnOpen()
        {
            base.OnOpen();

            logger.LogDebug($"Web socket client connection from {Context.UserEndPoint}.");

            _pc = CreatePeerConnection();

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

            Context.WebSocket.Send(JsonConvert.SerializeObject(offerSdp,
                 new Newtonsoft.Json.Converters.StringEnumConverter()));
        }
    }
}
