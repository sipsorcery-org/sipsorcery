//-----------------------------------------------------------------------------
// Filename: WebRTCHostedService.cs
//
// Description: This class is designed to act as a singleton in an ASP.Net
// server application to handle WebRTC peer connections. Currently it does
// not serve or process any media. The main point is as a proof of concept
// to be able to negotiate the ICE checks, DTLS handshake and then receive
// RTP & RTCP packets.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 13 May 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;

namespace WebRTCAspNetMvc
{
    public class WebRTCHostedService : IHostedService
    {
        private readonly ILogger<WebRTCHostedService> _logger;

        private ConcurrentDictionary<string, RTCPeerConnection> _peerConnections = new ConcurrentDictionary<string, RTCPeerConnection>();

        public WebRTCHostedService(ILogger<WebRTCHostedService> logger)
        {
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogDebug("WebRTCHostedService StartAsync.");
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public async Task<RTCSessionDescriptionInit> GetOffer(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentNullException("id", "A unique ID parameter must be supplied when creating a new peer connection.");
            }
            else if (_peerConnections.ContainsKey(id))
            {
                throw new ArgumentNullException("id", "The specified peer connection ID is already in use.");
            }
            var peerConnection = new RTCPeerConnection(null);

            MediaStreamTrack audioTrack = new MediaStreamTrack(SDPMediaTypesEnum.audio, false, 
                new List<SDPAudioVideoMediaFormat> { new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU) }, MediaStreamStatusEnum.RecvOnly);
            peerConnection.addTrack(audioTrack);

            peerConnection.OnRtpPacketReceived += (IPEndPoint rep, SDPMediaTypesEnum media, RTPPacket rtpPkt) => _logger.LogDebug($"RTP {media} pkt received, SSRC {rtpPkt.Header.SyncSource}, SeqNum {rtpPkt.Header.SequenceNumber}.");
            //peerConnection.OnReceiveReport += RtpSession_OnReceiveReport;
            //peerConnection.OnSendReport += RtpSession_OnSendReport;

            peerConnection.OnTimeout += (mediaType) => _logger.LogWarning($"Timeout for {mediaType}.");
            peerConnection.onconnectionstatechange += (state) =>
            {
                _logger.LogDebug($"Peer connection {id} state changed to {state}.");

                if (state == RTCPeerConnectionState.closed || state == RTCPeerConnectionState.disconnected || state == RTCPeerConnectionState.failed)
                {
                    _peerConnections.TryRemove(id, out _);
                }
                else if (state == RTCPeerConnectionState.connected)
                {
                    _logger.LogDebug("Peer connection connected.");
                }
            };

            var offerSdp = peerConnection.createOffer(null);
            await peerConnection.setLocalDescription(offerSdp);

            _peerConnections.TryAdd(id, peerConnection);

            return offerSdp;
        }

        public void SetRemoteDescription(string id, RTCSessionDescriptionInit description)
        {
            if (!_peerConnections.TryGetValue(id, out var pc))
            {
                throw new ApplicationException("No peer connection is available for the specified id.");
            }
            else
            { 
                _logger.LogDebug("Answer SDP: " + description.sdp);
                pc.setRemoteDescription(description);
            }
        }

        public void AddIceCandidate(string id, RTCIceCandidateInit iceCandidate)
        {
            if (!_peerConnections.TryGetValue(id, out var pc))
            {
                throw new ApplicationException("No peer connection is available for the specified id.");
            }
            else
            {
                _logger.LogDebug("ICE Candidate: " + iceCandidate.candidate);
                pc.addIceCandidate(iceCandidate);
            }
        }
    }
}
