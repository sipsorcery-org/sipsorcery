//-----------------------------------------------------------------------------
// Filename: WebRTCEchoService.cs
//
// Description: This class is designed to act as a singleton in an ASP.Net
// server application to handle WebRTC peer connections. It will echo back
// any audio or video streams it receives.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 10 Feb 2021	Aaron Clauson	Created, Dublin, Ireland.
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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;

namespace demo
{
    public class WebRTCEchoService : IHostedService
    {
        private const int VP8_PAYLOAD_ID = 96;
        private const string CONFIG_KEY_PUBLIC_IPV4 = "PublicIPv4";
        private const string CONFIG_KEY_PUBLIC_IPV6 = "PublicIPv6";

        private readonly ILogger<WebRTCEchoService> _logger;
        private readonly IPAddress _publicIPv4;
        private readonly IPAddress _publicIPv6;

        private ConcurrentDictionary<string, RTCPeerConnection> _peerConnections = new ConcurrentDictionary<string, RTCPeerConnection>();

        public WebRTCEchoService(ILoggerFactory loggerFactory, IConfiguration config)
        {
            _logger = loggerFactory.CreateLogger<WebRTCEchoService>();

            if (IPAddress.TryParse(config[CONFIG_KEY_PUBLIC_IPV4], out _publicIPv4))
            {
                _logger.LogInformation($"Public IPv4 address set to {_publicIPv4}.");
            }

            if (IPAddress.TryParse(config[CONFIG_KEY_PUBLIC_IPV6], out _publicIPv6))
            {
                _logger.LogInformation($"Public IPv6 address set to {_publicIPv6}.");
            }
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogDebug("WebRTCEchoService StartAsync.");
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public bool IsInUse(string id)
        {
            return _peerConnections.ContainsKey(id);
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

            _logger.LogDebug($"Generating new offer for ID {id}.");

            var peerConnection = new RTCPeerConnection();
            
            if(_publicIPv4 != null)
            {
                var rtpPort = peerConnection.GetRtpChannel().RTPPort;
                var publicIPv4Candidate = new RTCIceCandidate(RTCIceProtocol.udp, _publicIPv4, (ushort)rtpPort, RTCIceCandidateType.host);
                peerConnection.addLocalIceCandidate(publicIPv4Candidate);
            }

            if (_publicIPv6 != null)
            {
                var rtpPort = peerConnection.GetRtpChannel().RTPPort;
                var publicIPv6Candidate = new RTCIceCandidate(RTCIceProtocol.udp, _publicIPv6, (ushort)rtpPort, RTCIceCandidateType.host);
                peerConnection.addLocalIceCandidate(publicIPv6Candidate);
            }

            MediaStreamTrack audioTrack = new MediaStreamTrack(SDPMediaTypesEnum.audio, false,
                new List<SDPAudioVideoMediaFormat> { new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU) }, MediaStreamStatusEnum.SendRecv);
            peerConnection.addTrack(audioTrack);
            MediaStreamTrack videoTrack = new MediaStreamTrack(new VideoFormat(VideoCodecsEnum.VP8, VP8_PAYLOAD_ID), MediaStreamStatusEnum.SendRecv);
            peerConnection.addTrack(videoTrack);

            peerConnection.OnRtpPacketReceived += (IPEndPoint rep, SDPMediaTypesEnum media, RTPPacket rtpPkt) =>
            {
                peerConnection.SendRtpRaw(media, rtpPkt.Payload, rtpPkt.Header.Timestamp, rtpPkt.Header.MarkerBit, rtpPkt.Header.PayloadType);
                //_logger.LogDebug($"RTP {media} pkt received, SSRC {rtpPkt.Header.SyncSource}, SeqNum {rtpPkt.Header.SequenceNumber}.");
            };
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

            //_logger.LogTrace($"Offer SDP for {id}:\n{offerSdp.sdp}");
            Console.WriteLine($"Offer SDP for {id}:\n{offerSdp.sdp}");

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
                //_logger.LogTrace("Answer SDP: {SDP.ParseSDPDescription(description.sdp)}");
                Console.WriteLine($"Answer SDP:\n{description.sdp}");
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
