//-----------------------------------------------------------------------------
// Filename: WebRTCEchoServer.cs
//
// Description: The simplest possible WebRTC peer connection that can act
// as an echo server.
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

using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;

namespace demo
{
    public class WebRTCEchoServer
    {
        private const int VP8_PAYLOAD_ID = 96;
        private const string CONFIG_KEY_PUBLIC_IPV4 = "PublicIPv4";
        private const string CONFIG_KEY_PUBLIC_IPV6 = "PublicIPv6";

        private readonly ILogger<WebRTCEchoServer> _logger;
        private readonly IPAddress _publicIPv4;
        private readonly IPAddress _publicIPv6;

        public WebRTCEchoServer(ILoggerFactory loggerFactory, IConfiguration config)
        {
            _logger = loggerFactory.CreateLogger<WebRTCEchoServer>();

            if (IPAddress.TryParse(config[CONFIG_KEY_PUBLIC_IPV4], out _publicIPv4))
            {
                _logger.LogInformation($"Public IPv4 address set to {_publicIPv4}.");
            }

            if (IPAddress.TryParse(config[CONFIG_KEY_PUBLIC_IPV6], out _publicIPv6))
            {
                _logger.LogInformation($"Public IPv6 address set to {_publicIPv6}.");
            }
        }

        public async Task<RTCSessionDescriptionInit> GotOffer(RTCSessionDescriptionInit offer)
        {
            _logger.LogDebug($"SDP offer received.");
            _logger.LogTrace($"Offer SDP:\n{offer.sdp}");

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

            pc.OnTimeout += (mediaType) => _logger.LogWarning($"Timeout for {mediaType}.");
            pc.onconnectionstatechange += (state) =>
            {
                _logger.LogDebug($"Peer connection state changed to {state}.");

                if (state == RTCPeerConnectionState.failed)
                {
                    pc.Close("ice failure");
                }
            };

            var setResult = pc.setRemoteDescription(offer);
            if (setResult == SetDescriptionResultEnum.OK)
            {
                var offerSdp = pc.createOffer(null);
                await pc.setLocalDescription(offerSdp);

                var answer = pc.createAnswer(null);
                await pc.setLocalDescription(answer);

                _logger.LogTrace($"Answer SDP:\n{answer.sdp}");

                return answer;
            }
            else
            {
                return null;
            }
        }
    }
}
