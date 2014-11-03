using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SIPSorceryRTP;
using SIPSorcery.Net;
using SIPSorcery.Sys;
using log4net;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace WebRTCVideoServer
{
    class WebRTCClient
    {
        public IPEndPoint SocketAddress;
        public string ICEUser;
        public string ICEPassword;
        public bool STUNExchangeComplete;
        public DateTime LastSTUNMessageAt;
    }

    class Program
    {
        private const int WEBRTC_LISTEN_PORT = 49890;
        private const int EXPIRE_CLIENT_SECONDS = 3;

        private static ILog logger = AppState.logger;

        private static bool m_exit = false;

        private static UdpClient _webRTCReceiverClient;
        private static UdpClient _rtpClient;

        private static string _sourceSRTPKey = "zIN6kIVR4DY5dpc5T2vBDvOC1X9VjPTegBx/6EnQ";
        private static string _senderICEUser = "AoszpFFXN92GdqKc";
        private static string _senderICEPassword = "0csAdt+PHzR3/OepgHBmnPKi";
        private static SRTPManaged _newRTPReceiverSRTP;
        private static WebSocketServer _receiverWSS;
        private static List<WebRTCClient> _webRTCClients = new List<WebRTCClient>();

        private static string _sourceSDPOffer = @"v=0
o=- 2925822133501083390 2 IN IP4 127.0.0.1
s=-
t=0 0
a=group:BUNDLE video
a=msid-semantic: WMS XHuZbE0oAGhvjMq7UHDMMEzLC0Jga3PZXtvW
m=video {0} RTP/SAVPF 100 116 117 96
c=IN IP4 {1}
a=rtcp:{0} IN IP4 {1}
a=candidate:2675262800 1 udp 2122194687 {1} {0} typ host generation 0
a=candidate:2675262800 2 udp 2122194687 {1} {0} typ host generation 0
a=ice-ufrag:{2}
a=ice-pwd:{3}
a=ice-options:google-ice
a=mid:video
a=extmap:2 urn:ietf:params:rtp-hdrext:toffset
a=extmap:3 http://www.webrtc.org/experiments/rtp-hdrext/abs-send-time
a=sendrecv
a=rtcp-mux
a=crypto:0 AES_CM_128_HMAC_SHA1_80 inline:{4}
a=rtpmap:100 VP8/90000
a=rtcp-fb:100 ccm fir
a=rtcp-fb:100 nack
a=rtcp-fb:100 nack pli
a=rtcp-fb:100 goog-remb
a=rtpmap:116 red/90000
a=rtpmap:117 ulpfec/90000
a=rtpmap:96 rtx/90000
a=fmtp:96 apt=100
a=ssrc-group:FID 1429654490 1191714373
a=ssrc:1429654490 cname:IA+Ohn8PVyDVYiYx
a=ssrc:1429654490 msid:XHuZbE0oAGhvjMq7UHDMMEzLC0Jga3PZXtvW 48a41820-a050-4ed9-9051-21fb2b97a287
a=ssrc:1429654490 mslabel:XHuZbE0oAGhvjMq7UHDMMEzLC0Jga3PZXtvW
a=ssrc:1429654490 label:48a41820-a050-4ed9-9051-21fb2b97a287
a=ssrc:1191714373 cname:IA+Ohn8PVyDVYiYx
a=ssrc:1191714373 msid:XHuZbE0oAGhvjMq7UHDMMEzLC0Jga3PZXtvW 48a41820-a050-4ed9-9051-21fb2b97a287
a=ssrc:1191714373 mslabel:XHuZbE0oAGhvjMq7UHDMMEzLC0Jga3PZXtvW
a=ssrc:1191714373 label:48a41820-a050-4ed9-9051-21fb2b97a287
";

        static void Main(string[] args)
        {
            try
            {
                Console.WriteLine("WebRTC Test Media Server:");

                _sourceSDPOffer = String.Format(_sourceSDPOffer, WEBRTC_LISTEN_PORT.ToString(), "10.1.1.2", _senderICEUser, _senderICEPassword, _sourceSRTPKey);
                //_offerSDP = SDP.ParseSDPDescription(_sourceSDPOffer);

                SDPExchangeReceiver.SDPAnswerReceived += SDPExchangeReceiver_SDPAnswerReceived;
                SDPExchangeReceiver.WebSocketOpened += SDPExchangeReceiver_WebSocketOpened;

                _receiverWSS = new WebSocketServer(8081, false);
                _receiverWSS.AddWebSocketService<SDPExchangeReceiver>("/receiver");
                _receiverWSS.Start();

                IPEndPoint receiverLocalEndPoint = new IPEndPoint(IPAddress.Parse("10.1.1.2"), WEBRTC_LISTEN_PORT);
                _webRTCReceiverClient = new UdpClient(receiverLocalEndPoint);

                IPEndPoint rtpLocalEndPoint = new IPEndPoint(IPAddress.Parse("10.1.1.2"), 10001);
                _rtpClient = new UdpClient(rtpLocalEndPoint);

                logger.Debug("Commencing listen to receiver WebRTC client on local socket " + receiverLocalEndPoint + ".");
                ThreadPool.QueueUserWorkItem(delegate { ListenToReceiverWebRTCClient(_webRTCReceiverClient); });

                ThreadPool.QueueUserWorkItem(delegate { RelayRTP(_rtpClient); });

                ThreadPool.QueueUserWorkItem(delegate { ICMPListen(IPAddress.Parse("10.1.1.2")); });

                ManualResetEvent dontStopEvent = new ManualResetEvent(false);
                dontStopEvent.WaitOne();
            }
            catch (Exception excp)
            {
                Console.WriteLine("Exception Main. " + excp);
            }
            finally
            {
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
            }
        }

        private static void SDPExchangeReceiver_WebSocketOpened()
        {
            _receiverWSS.WebSocketServices.Broadcast(_sourceSDPOffer);
        }

        private static void SDPExchangeReceiver_SDPAnswerReceived(string sdpAnswer)
        {
            try
            {
                logger.Debug("SDP Answer Received.");

                var answerSDP = SDP.ParseSDPDescription(sdpAnswer);

                //logger.Debug("ICE User: " + _answerSDP.IceUfrag + ".");
                //logger.Debug("ICE Password: " + _answerSDP.IcePwd + ".");
                logger.Debug("New WebRTC client SDP answer with port: " + answerSDP.Media.First().Port + ".");

                var newWebRTCClient = new WebRTCClient()
                {
                    SocketAddress = new IPEndPoint(IPAddress.Parse("10.1.1.2"), answerSDP.Media.First().Port),
                    ICEUser = answerSDP.IceUfrag,
                    ICEPassword = answerSDP.IcePwd
                };

                lock (_webRTCClients)
                {
                    _webRTCClients.Add(newWebRTCClient);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SDPExchangeReceiver_SDPAnswerReceived. " + excp.Message);
            }
        }

        private static void ICMPListen(IPAddress listenAddress)
        {
            try
            {
                Socket icmpListener = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.Icmp);
                icmpListener.Bind(new IPEndPoint(listenAddress, 0));
                icmpListener.IOControl(IOControlCode.ReceiveAll, new byte[] { 1, 0, 0, 0 }, new byte[] { 1, 0, 0, 0 });

                while (!m_exit)
                {
                    try
                    {
                        byte[] buffer = new byte[4096];
                        EndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                        int bytesRead = icmpListener.ReceiveFrom(buffer, ref remoteEndPoint);

                        logger.Debug(bytesRead + " ICMP bytes read from " + remoteEndPoint + ".");
                    }
                    catch (Exception listenExcp)
                    {
                        logger.Warn("ICMPListen. " + listenExcp.Message);
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception ICMPListen. " + excp);
            }
        }

        private static void ListenToReceiverWebRTCClient(UdpClient localSocket)
        {
            try
            {
                while (!m_exit)
                {
                    try
                    {
                        //logger.Debug("ListenToReceiverWebRTCClient Receive.");

                        IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                        byte[] buffer = localSocket.Receive(ref remoteEndPoint);

                        //logger.Debug(buffer.Length + " bytes read on Receiver Client media socket from " + remoteEndPoint.ToString() + ".");

                        if ((buffer[0] & 0x80) == 0)
                        {
                            STUNv2Message stunMessage = STUNv2Message.ParseSTUNMessage(buffer, buffer.Length);

                            //logger.Debug("STUN message received from Receiver Client @ " + stunMessage.Header.MessageType + ".");

                            if (stunMessage.Header.MessageType == STUNv2MessageTypesEnum.BindingRequest)
                            {
                                //logger.Debug("Sending STUN response to Receiver Client @ " + remoteEndPoint + ".");

                                STUNv2Message stunResponse = new STUNv2Message(STUNv2MessageTypesEnum.BindingSuccessResponse);
                                stunResponse.Header.TransactionId = stunMessage.Header.TransactionId;
                                stunResponse.AddXORMappedAddressAttribute(remoteEndPoint.Address, remoteEndPoint.Port);
                                byte[] stunRespBytes = stunResponse.ToByteBuffer(_senderICEPassword, true);
                                localSocket.Send(stunRespBytes, stunRespBytes.Length, remoteEndPoint);

                                //logger.Debug("Sending Binding request to Receiver Client @ " + remoteEndPoint + ".");

                                var client = _webRTCClients.Where(x => x.SocketAddress.ToString() == remoteEndPoint.ToString()).SingleOrDefault();
                                if (client != null)
                                {
                                    STUNv2Message stunRequest = new STUNv2Message(STUNv2MessageTypesEnum.BindingRequest);
                                    stunRequest.Header.TransactionId = Guid.NewGuid().ToByteArray().Take(12).ToArray();
                                    stunRequest.AddUsernameAttribute(client.ICEUser + ":" + _senderICEUser);
                                    stunRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.Priority, new byte[] { 0x6e, 0x7f, 0x1e, 0xff }));
                                    byte[] stunReqBytes = stunRequest.ToByteBuffer(client.ICEPassword, true);
                                    localSocket.Send(stunReqBytes, stunReqBytes.Length, remoteEndPoint);

                                    client.LastSTUNMessageAt = DateTime.Now;
                                }
                            }
                            else if (stunMessage.Header.MessageType == STUNv2MessageTypesEnum.BindingSuccessResponse)
                            {
                                var client = _webRTCClients.Where(x => x.SocketAddress.ToString() == remoteEndPoint.ToString()).SingleOrDefault();
                                if (client != null && client.STUNExchangeComplete == false)
                                {
                                    client.STUNExchangeComplete = true;
                                    logger.Debug("WebRTC client STUN exchange complete for " + remoteEndPoint.ToString() + ".");
                                }
                            }
                            else if (stunMessage.Header.MessageType == STUNv2MessageTypesEnum.BindingErrorResponse)
                            {
                                logger.Debug("A STUN binding error response was received from Receiver Client.");
                            }
                            else
                            {
                                logger.Debug("An unrecognised STUN request was received from Receiver Client.");
                            }
                        }
                        else
                        {
                            logger.Debug("A non-STUN packet was received Receiver Client.");
                        }
                    }
                    catch (Exception sockExcp)
                    {
                        logger.Debug("ListenToReceiverWebRTCClient Receive. " + sockExcp.Message);
                        continue;
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception ListenForWebRTCClient. " + excp);
            }
        }

        private static void RelayRTP(UdpClient rtpClient)
        {
            try
            {
                DateTime lastCleanup = DateTime.Now;
                _newRTPReceiverSRTP = new SRTPManaged(Convert.FromBase64String(_sourceSRTPKey));

                IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                byte[] buffer = rtpClient.Receive(ref remoteEndPoint);

                while (buffer != null && buffer.Length > 0 && !m_exit)
                {
                    if (_webRTCClients.Count != 0)
                    {
                        byte[] bufferWithAuth = new byte[buffer.Length + 10];
                        Buffer.BlockCopy(buffer, 0, bufferWithAuth, 0, buffer.Length);
                        RTPPacket rtpPacket = new RTPPacket(bufferWithAuth);
                        rtpPacket.Header.PayloadType = 100;

                        var rtpBuffer = rtpPacket.GetBytes();
                        int rtperr = _newRTPReceiverSRTP.ProtectRTP(rtpBuffer, rtpBuffer.Length - 10);
                        if (rtperr != 0)
                        {
                            logger.Debug("New RTP packet protect result " + rtperr + ".");
                        }

                        lock (_webRTCClients)
                        {
                            foreach (var client in _webRTCClients.Where(x => x.STUNExchangeComplete))
                            {
                                try
                                {
                                    logger.Debug("Sending RTP " + rtpBuffer.Length + " bytes to " + client.SocketAddress + ".");
                                    _webRTCReceiverClient.Send(rtpBuffer, rtpBuffer.Length, client.SocketAddress);
                                }
                                catch (Exception sendExcp)
                                {
                                    logger.Error("RelayRTP exception sending to " + client.SocketAddress + ". " + sendExcp.Message);
                                }
                            }
                        }

                        if (DateTime.Now.Subtract(lastCleanup).TotalSeconds > EXPIRE_CLIENT_SECONDS)
                        {
                            lock (_webRTCClients)
                            {
                                var expiredClients = (from cli in _webRTCClients where cli.STUNExchangeComplete && DateTime.Now.Subtract(cli.LastSTUNMessageAt).TotalSeconds > EXPIRE_CLIENT_SECONDS select cli).ToList();
                                foreach (var expiredClient in expiredClients)
                                {
                                    logger.Debug("Removed expired client " + expiredClient.SocketAddress + ".");
                                    _webRTCClients.Remove(expiredClient);
                                }
                            }

                            lastCleanup = DateTime.Now;
                        }
                    }

                    buffer = rtpClient.Receive(ref remoteEndPoint);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception  RelayRTP. " + excp);
            }
        }
    }

    public class SDPExchangeReceiver : WebSocketBehavior
    {
        public static event Action WebSocketOpened;
        public static event Action<string> SDPAnswerReceived;

        protected override void OnMessage(MessageEventArgs e)
        {
            SDPAnswerReceived(e.Data);
        }

        protected override void OnOpen()
        {
            base.OnOpen();
            WebSocketOpened();
        }
    }
}
