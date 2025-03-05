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
// 13 Jul 2021  Aaron Clauson   Added data channel and DTMF echo capability.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
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
        private uint _rtpEventSsrc = 0;

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

            SDP offerSDP = SDP.ParseSDPDescription(offer.sdp);

            if (offerSDP.Media.Any(x => x.Media == SDPMediaTypesEnum.audio))
            {
                MediaStreamTrack audioTrack = new MediaStreamTrack(SDPWellKnownMediaFormatsEnum.PCMU);
                pc.addTrack(audioTrack);
            }

            if (offerSDP.Media.Any(x => x.Media == SDPMediaTypesEnum.video))
            {
                MediaStreamTrack videoTrack = new MediaStreamTrack(new VideoFormat(VideoCodecsEnum.VP8, VP8_PAYLOAD_ID));
                pc.addTrack(videoTrack);
            }

            pc.OnRtpPacketReceived += (IPEndPoint rep, SDPMediaTypesEnum media, RTPPacket rtpPkt) =>
            {
                pc.SendRtpRaw(media, rtpPkt.Payload, rtpPkt.Header.Timestamp, rtpPkt.Header.MarkerBit, rtpPkt.Header.PayloadType);
                //_logger.LogDebug($"RTP {media} pkt received, SSRC {rtpPkt.Header.SyncSource}, SeqNum {rtpPkt.Header.SequenceNumber}.");
            };
            pc.OnRtpEvent += async (ep, ev, hdr) =>
            {
                if (ev.EndOfEvent && hdr.MarkerBit == 1)
                {
                    _logger.LogDebug($"RTP event received: {ev.EventID}.");
                    // Echo the DTMF event back.
                    var echoEvent = new RTPEvent(ev.EventID, true, RTPEvent.DEFAULT_VOLUME, RTPSession.DTMF_EVENT_DURATION, RTPSession.DEFAULT_DTMF_EVENT_PAYLOAD_ID);
                    await pc.SendDtmfEvent(echoEvent, CancellationToken.None).ConfigureAwait(false);
                }

                if (_rtpEventSsrc == 0)
                {
                    if (ev.EndOfEvent && hdr.MarkerBit == 1)
                    {
                        _logger.LogDebug($"RTP event received: {ev.EventID}.");
                        // Echo the DTMF event back.
                        var echoEvent = new RTPEvent(ev.EventID, true, RTPEvent.DEFAULT_VOLUME, RTPSession.DTMF_EVENT_DURATION, RTPSession.DEFAULT_DTMF_EVENT_PAYLOAD_ID);
                        await pc.SendDtmfEvent(echoEvent, CancellationToken.None).ConfigureAwait(false);
                    }
                    else if (!ev.EndOfEvent)
                    {
                        _rtpEventSsrc = hdr.SyncSource;
                        _logger.LogDebug($"RTP event received: {ev.EventID}.");
                        // Echo the DTMF event back.
                        var echoEvent = new RTPEvent(ev.EventID, true, RTPEvent.DEFAULT_VOLUME, RTPSession.DTMF_EVENT_DURATION, RTPSession.DEFAULT_DTMF_EVENT_PAYLOAD_ID);
                        await pc.SendDtmfEvent(echoEvent, CancellationToken.None).ConfigureAwait(false);
                    }
                }

                if (_rtpEventSsrc != 0 && ev.EndOfEvent)
                {
                    _rtpEventSsrc = 0;
                }
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

            pc.ondatachannel += (dc) =>
            {
                _logger.LogInformation($"Data channel opened for label {dc.label}, stream ID {dc.id}.");
                dc.onmessage += (rdc, proto, data) =>
                {
                    _logger.LogInformation($"Data channel got message: {Encoding.UTF8.GetString(data)}");
                    rdc.send(Encoding.UTF8.GetString(data));
                };
            };

            var setResult = pc.setRemoteDescription(offer);
            if (setResult == SetDescriptionResultEnum.OK)
            {
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
