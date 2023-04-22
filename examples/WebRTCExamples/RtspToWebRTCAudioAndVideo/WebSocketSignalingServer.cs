using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WebSocketSharp.Server;
using WebSocketSharp.Net.WebSockets;
using System.Net;
using SIPSorcery.Net;
using DnsClient.Internal;
using Microsoft.Extensions.Logging;
using System.ComponentModel;

namespace RtspToWebRtcRestreamer
{
    internal class WebSocketSignalingServer : IDisposable
    {
        private ILogger<WebSocketSignalingServer> _logger;
        private int _port;
        private WebSocketServer _ws;
        private FFmpegListener _ffmpegListener;
        private RTCPeerConnection _pc;     

        public event Action OnPeerConnecionClosed;
        public event Action OnWebSocketClosed;

        public WebSocketSignalingServer(FFmpegListener listener ,int port)
        {
            _ffmpegListener = listener;
            _port = port;          
        }

        public async Task<bool> Run()
        {
            try
            {
                //_ws = new WebSocketServer(IPAddress.Any, _port);
                _ws = new WebSocketServer(IPAddress.Loopback, _port);
                _ws.AddWebSocketService<WebRtcClient>("/", (client) =>
                {
                    client.SocketOpened += SendOffer;
                    client.MessageReceived += WebSocketMessageReceived;
                    client.OnWsClose += OnWsClose;
                });

                _ws.Start();
                return true;
            }
            catch (Exception ex)
            {
                return false;
            }
        }

        private void OnWsClose()
        {
            OnWebSocketClosed?.Invoke();
        }

        private async Task<RTCPeerConnection> SendOffer(WebSocketContext context)
        {
            var pc = Createpc(context);

            var offerInit = pc.createOffer(null);
            await pc.setLocalDescription(offerInit);

            context.WebSocket.Send(offerInit.sdp);

            return pc;
        }

        private RTCPeerConnection Createpc(WebSocketContext context)
        {
            var pc = new RTCPeerConnection(null);

            //add videoTrack
            if (_ffmpegListener.videoTrack != null)
            {
                MediaStreamTrack videoTrack = new MediaStreamTrack(
                                            SDPMediaTypesEnum.video,
                                            false,
                                            new List<SDPAudioVideoMediaFormat> { _ffmpegListener.videoFormatRTP },
                                            MediaStreamStatusEnum.SendOnly);
                videoTrack.Ssrc = _ffmpegListener.videoTrack.Ssrc;
                pc.addTrack(videoTrack);
            }

            //add audio Track
            if (_ffmpegListener.audioTrack != null)
            {
                MediaStreamTrack audioTrack = new MediaStreamTrack(
                                            SDPMediaTypesEnum.audio,
                                            false,
                                            new List<SDPAudioVideoMediaFormat> { _ffmpegListener.audioFormatRTP },
                                            MediaStreamStatusEnum.SendOnly);
                audioTrack.Ssrc = _ffmpegListener.audioTrack.Ssrc;
                pc.addTrack(audioTrack);
            }
            
            pc.onicecandidate += (candidate) =>
            {
                if (pc.signalingState == RTCSignalingState.have_local_offer ||
                    pc.signalingState == RTCSignalingState.have_remote_offer)
                {
                    context.WebSocket.Send($"candidate:{candidate}");
                }
            };

            pc.onconnectionstatechange += (state) =>
            {
                if (state == RTCPeerConnectionState.connected)
                {
                    _ffmpegListener.OnAudioRtpPacketReceived += (ep, media, rtpPkt) =>
                    {
                        try
                        {                                                 
                            pc.SendRtpRaw(media, rtpPkt.Payload, rtpPkt.Header.Timestamp, rtpPkt.Header.MarkerBit, rtpPkt.Header.PayloadType);                                                          
                        }
                        catch (Exception ex)
                        {

                        }
                    };
                    _ffmpegListener.OnVideoRtpPacketReceived += (ep, media, rtpPkt) =>
                    {
                        try
                        {
                            pc.SendRtpRaw(media, rtpPkt.Payload, rtpPkt.Header.Timestamp, rtpPkt.Header.MarkerBit, rtpPkt.Header.PayloadType);
                        }
                        catch (Exception ex)
                        {

                        }
                    };

                }
                else if (state == RTCPeerConnectionState.disconnected)
                {
                    OnPeerConnecionClosed.Invoke();
                    pc.close();
                    pc.Dispose();
                    pc = null;
                }
            };

            return pc;
        }

        private async Task WebSocketMessageReceived(WebSocketContext context, RTCPeerConnection pc, string message)
        {
            try
            {
                if (pc.remoteDescription == null)
                {
                    pc.setRemoteDescription(new RTCSessionDescriptionInit { sdp = message, type = RTCSdpType.answer });
                }
                else
                {
                    _logger.LogInformation("ICE Candidate: " + message);

                    if (string.IsNullOrWhiteSpace(message) || message.Trim().ToLower() == SDP.END_ICE_CANDIDATES_ATTRIBUTE)
                    {
                        _logger.LogDebug("End of candidates message received.");
                    }
                    else
                    {
                        _logger.LogInformation("add Ice Candidate");
                        var candInit = Newtonsoft.Json.JsonConvert.DeserializeObject<RTCIceCandidateInit>(message);
                        pc.addIceCandidate(candInit);
                    }
                }
            }
            catch (Exception excp)
            {

            }
        }

        public void Dispose()
        {
            throw new NotImplementedException();
        }
    }
}
