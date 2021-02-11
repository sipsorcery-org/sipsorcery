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

            var pc = new RTCPeerConnection();

            if (_publicIPv4 != null)
            {
                var rtpPort = pc.GetRtpChannel().RTPPort;
                var publicIPv4Candidate = new RTCIceCandidate(RTCIceProtocol.udp, _publicIPv4, (ushort)rtpPort, RTCIceCandidateType.host);
                pc.addLocalIceCandidate(publicIPv4Candidate);
            }

            if (_publicIPv6 != null)
            {
                var rtpPort = pc.GetRtpChannel().RTPPort;
                var publicIPv6Candidate = new RTCIceCandidate(RTCIceProtocol.udp, _publicIPv6, (ushort)rtpPort, RTCIceCandidateType.host);
                pc.addLocalIceCandidate(publicIPv6Candidate);
            }

            MediaStreamTrack audioTrack = new MediaStreamTrack(SDPMediaTypesEnum.audio, false,
                new List<SDPAudioVideoMediaFormat> { new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU) }, MediaStreamStatusEnum.SendRecv);
            pc.addTrack(audioTrack);
            MediaStreamTrack videoTrack = new MediaStreamTrack(new VideoFormat(VideoCodecsEnum.VP8, VP8_PAYLOAD_ID), MediaStreamStatusEnum.SendRecv);
            pc.addTrack(videoTrack);

            pc.OnRtpPacketReceived += (IPEndPoint rep, SDPMediaTypesEnum media, RTPPacket rtpPkt) =>
            {
                pc.SendRtpRaw(media, rtpPkt.Payload, rtpPkt.Header.Timestamp, rtpPkt.Header.MarkerBit, rtpPkt.Header.PayloadType);
                //_logger.LogDebug($"RTP {media} pkt received, SSRC {rtpPkt.Header.SyncSource}, SeqNum {rtpPkt.Header.SequenceNumber}.");
            };
            //peerConnection.OnReceiveReport += RtpSession_OnReceiveReport;
            //peerConnection.OnSendReport += RtpSession_OnSendReport;

            pc.onsignalingstatechange += () =>
            {
                if(pc.signalingState == RTCSignalingState.have_remote_offer)
                {
                    // This peer is acting as an echo server so deactivate any media streams that the
                    // remote peer is not intending to send.
                    if(pc.AudioRemoteTrack.StreamStatus != MediaStreamStatusEnum.SendRecv)
                    {
                        _logger.LogDebug($"Setting audio stream to inactive due to remote peer audio stream status of {pc.AudioRemoteTrack.StreamStatus}.");
                        pc.SetMediaStreamStatus(SDPMediaTypesEnum.audio, MediaStreamStatusEnum.Inactive);
                    }

                    if (pc.VideoRemoteTrack.StreamStatus != MediaStreamStatusEnum.SendRecv)
                    {
                        _logger.LogDebug($"Setting video stream to inactive due to remote peer video stream status of {pc.VideoRemoteTrack.StreamStatus}.");
                        pc.SetMediaStreamStatus(SDPMediaTypesEnum.video, MediaStreamStatusEnum.Inactive);
                    }
                }
            };

            pc.OnTimeout += (mediaType) => _logger.LogWarning($"Timeout for {mediaType}.");
            pc.onconnectionstatechange += (state) =>
            {
                _logger.LogDebug($"Peer connection {id} state changed to {state}.");

                if (state == RTCPeerConnectionState.failed)
                {
                    pc.Close("ice failure");
                }

                if (_peerConnections.ContainsKey(id) && !(state == RTCPeerConnectionState.@new || state == RTCPeerConnectionState.connecting))
                {
                    // If the peer connection has finished with ICE remove it so the signaling ID can be re-used.
                    _peerConnections.TryRemove(id, out _);
                }
            };

            var offerSdp = pc.createOffer(null);
            await pc.setLocalDescription(offerSdp);

            _peerConnections.TryAdd(id, pc);

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
