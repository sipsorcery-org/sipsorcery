// openssl x509 -fingerprint -sha256 -in server-cert.pem 

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SIPSorceryMedia;
using SIPSorcery.Net;
using SIPSorcery.Sys;
using log4net;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace WebRTCVideoServer
{
    class WebRtcPeer
    {
        private static ILog logger = AppState.logger;

        public string WebSocketID;
        public string SDP;
        public string SdpSessionID;
        public string LocalIceUser;
        public string LocalIcePassword;
        public string RemoteIceUser;
        public string RemoteIcePassword;
        public bool StunExchangeComplete;
        public bool IsDtlsNegotiationComplete;
        public uint SSRC;
        public ushort SequenceNumber;
        public uint LastTimestamp;
        public DtlsManaged DtlsContext;
        public SRTPManaged SrtpContext;
        public SRTPManaged SrtpReceiveContext;  // Used to decrypt packets received from the remote peer.
        public DateTime LastRtcpSenderReportSentAt = DateTime.MinValue;
        public List<IceCandidate> LocalIceCandidates;
        public List<SDPICECandidate> RemoteIceCandidates;
        public bool IsClosed;

        public void Close()
        {
            try
            {
                IsClosed = true;

                logger.Debug("WebRTC peer closing.");

                if (LocalIceCandidates != null && LocalIceCandidates.Count > 0)
                {
                    foreach (var iceCandidate in LocalIceCandidates)
                    {
                        iceCandidate.IsDisconnected = true;

                        if (iceCandidate.LocalRtpSocket != null)
                        {
                            logger.Debug("Closing local ICE candidate socket for " + iceCandidate.LocalRtpSocket.LocalEndPoint + ".");

                            iceCandidate.LocalRtpSocket.Shutdown(SocketShutdown.Both);
                            iceCandidate.LocalRtpSocket.Close();
                        }
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception WebRtcPeer.Close. " + excp);
            }
        }
    }

    class IceCandidate
    {
        public Socket LocalRtpSocket;
        public Socket LocalControlSocket;
        public IPAddress LocalAddress;
        public Task RtpListenerTask;
        public TurnServer TurnServer;
        public bool IsGatheringComplete;
        public int AllocateAttempts;
        public IPEndPoint StunRflxIPEndPoint;
        public IPEndPoint TurnRelayIPEndPoint;
        public IPEndPoint RemoteRtpEndPoint;
        public bool HasGatheringFailed;
        public string FailureMessage;
        public bool IsDisconnected;
        public string DisconnectionMessage;
        public DateTime LastSTUNSendAt;
        public DateTime LastSTUNReceiveAt;
    }

    class TurnServer
    {
        public IPEndPoint ServerEndPoint;
        public string Username;
        public string Password;
        public string Realm;
        public string Nonce;
        public int AuthorisationAttempts;
    }

    class Program
    {
        private const int WEBRTC_START_PORT = 49000;
        private const int WEBRTC_END_PORT = 53000;
        private const int EXPIRE_CLIENT_SECONDS = 3;
        private const int RTP_MAX_PAYLOAD = 1400; //1452;
        private const int TIMESTAMP_SPACING = 3000;
        private const int PAYLOAD_TYPE_ID = 100;
        private const int SRTP_AUTH_KEY_LENGTH = 10;
        private const int MAXIMUM_TURN_ALLOCATE_ATTEMPTS = 4;
        private const int STUN_CONNECTIVITY_CHECK_SECONDS = 5;
        private const int ICE_GATHERING_TIMEOUT_SECONDS = 15;

        private const float TEXT_SIZE_PERCENTAGE = 0.035f;       // height of text as a percentage of the total image height
        private const float TEXT_OUTLINE_REL_THICKNESS = 0.02f; // Black text outline thickness is set as a percentage of text height in pixels
        private const int TEXT_MARGIN_PIXELS = 5;
        private const int POINTS_PER_INCH = 72;

        private static ILog logger = AppState.logger;

        private static int _webcamIndex = 1;
        private static uint _webcamWidth = 640;
        private static uint _webcamHeight = 480;
        private static VideoSubTypesEnum _webcamVideoSubType = VideoSubTypesEnum.YUY2; // VideoSubTypesEnum.I420;

        private static bool m_exit = false;

        private static WebSocketServer _receiverWSS;
        private static ConcurrentBag<WebRtcPeer> _webRtcPeers = new ConcurrentBag<WebRtcPeer>();
        private static IPEndPoint _turnServerEndPoint = new IPEndPoint(IPAddress.Parse("103.29.66.243"), 3478);

        private static string _sdpOfferTemplate = @"v=0
o=- {0} 2 IN IP4 127.0.0.1
s=-
t=0 0
m=video {1} RTP/SAVPF " + PAYLOAD_TYPE_ID + @"
c=IN IP4 {2}
{3}
a=ice-ufrag:{4}
a=ice-pwd:{5}
a=fingerprint:sha-256 C4:ED:9C:13:06:A2:79:FB:A1:9A:44:B5:FE:BC:EE:30:2A:2E:00:84:48:6B:54:77:F5:EC:E4:B6:75:BD:F9:5B
a=setup:actpass
a=mid:video
a=sendrecv
a=rtcp-mux
a=rtpmap:" + PAYLOAD_TYPE_ID + @" VP8/90000
";

        static void Main(string[] args)
        {
            try
            {
                Console.WriteLine("WebRTC Test Media Server:");

                //InitialiseWebRtcPeer("test");

                //IPEndPoint receiverLocalEndPoint = new IPEndPoint(IPAddress.Parse(_localIPAddress), WEBRTC_LISTEN_PORT);
                //_webRTCReceiverClient = new UdpClient(receiverLocalEndPoint);

                //IPEndPoint rtpLocalEndPoint = new IPEndPoint(IPAddress.Parse(_localIPAddress), 10001);
                //_rtpClient = new UdpClient(rtpLocalEndPoint);

                //logger.Debug("Commencing listen to receiver WebRTC client on local socket " + receiverLocalEndPoint + ".");
                //ThreadPool.QueueUserWorkItem(delegate { ListenToReceiverWebRTCClient(_webRTCReceiverClient); });
                ThreadPool.QueueUserWorkItem(delegate { SendStunConnectivityChecks(); });
                //ThreadPool.QueueUserWorkItem(delegate { AllocateTurn(_webRTCReceiverClient, turnServerEndPoint); });

                //_sourceSDPOffer = String.Format(_sourceSDPOffer, WEBRTC_LISTEN_PORT.ToString(), _localIPAddress, _senderICEUser, _senderICEPassword, _sourceSRTPKey);
                //_sourceSDPOffer = String.Format(_sourceSDPOffer, WEBRTC_LISTEN_PORT.ToString(), _localIPAddress, _senderICEUser, _senderICEPassword, Crypto.GetRandomInt(10).ToString());

                SDPExchangeReceiver.WebSocketOpened += SDPExchangeReceiver_WebSocketOpened;
                SDPExchangeReceiver.SDPAnswerReceived += SDPExchangeReceiver_SDPAnswerReceived;

                //var httpsv = new HttpServer(8001, true);
                //httpsv.SslConfiguration = new WebSocketSharp.Net.ServerSslConfiguration()
                //httpsv.AddWebSocketService<Echo>("/Echo");

                var wssCertificate = new System.Security.Cryptography.X509Certificates.X509Certificate2("aaron-pc.p12");
                Console.WriteLine("WSS Certificate CN: " + wssCertificate.Subject + ", have key " + wssCertificate.HasPrivateKey + ", Expires " + wssCertificate.GetExpirationDateString() + ".");

                //_receiverWSS = new WebSocketServer(8081);
                _receiverWSS = new WebSocketServer(8081, true);
                //_receiverWSS.Log.Level = LogLevel.Debug;
                _receiverWSS.SslConfiguration = new WebSocketSharp.Net.ServerSslConfiguration(wssCertificate, false,
                     System.Security.Authentication.SslProtocols.Tls,
                    false);

                //_receiverWSS.Certificate = new System.Security.Cryptography.X509Certificates.X509Certificate2("test.p12");
                //_receiverWSS.AddWebSocketService<SDPExchangeReceiver>("/stream");
                _receiverWSS.AddWebSocketService<SDPExchangeReceiver>("/stream",
                    () => new SDPExchangeReceiver()
                    {
                        IgnoreExtensions = true,
                    });
                _receiverWSS.Start();

                //DtlsManaged dtls = new DtlsManaged();
                //int res = dtls.Init();
                //dtls.Dispose();
                //dtls = null;

                //Console.WriteLine("DTLS initialisation result=" + res + ".");

                //ThreadPool.QueueUserWorkItem(delegate { RelayRTP(_rtpClient); });

                //ThreadPool.QueueUserWorkItem(delegate { SendRTPFromCamera(); });

                //ThreadPool.QueueUserWorkItem(delegate { SendRTPFromRawRTPFile("rtpPackets.txt"); });

                //ThreadPool.QueueUserWorkItem(delegate { SendRTPFromRawRTPFileNewVP8Header("rtpPackets.txt"); });

                //ThreadPool.QueueUserWorkItem(delegate { SendRTPFromVP8FramesFile("framesAndHeaders.txt"); });

                //ThreadPool.QueueUserWorkItem(delegate { ICMPListen(IPAddress.Parse(_localIPAddress)); });

                //ThreadPool.QueueUserWorkItem(delegate { CaptureVP8SamplesToFile("vp8sample", 1000); });

                ThreadPool.QueueUserWorkItem(delegate { SendTestPattern(); });

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

        private static void SDPExchangeReceiver_WebSocketOpened(WebSocketSharp.Net.WebSockets.WebSocketContext context, string webSocketID)
        {
            logger.Debug("New WebRTC client added for web socket connection " + webSocketID + ".");

            lock (_webRtcPeers)
            {
                if (!_webRtcPeers.Any(x => x.WebSocketID == webSocketID))
                {
                    var webRtcPeer = new WebRtcPeer() { WebSocketID = webSocketID };

                    _webRtcPeers.Add(webRtcPeer);

                    InitialiseWebRtcPeer(webRtcPeer);

                    //_receiverWSS.WebSocketServices.Broadcast(webRtcPeer.SDP);
                    context.WebSocket.Send(webRtcPeer.SDP);
                }
            }
        }

        private static void SDPExchangeReceiver_SDPAnswerReceived(string webSocketID, string sdpAnswer)
        {
            try
            {
                logger.Debug("SDP Answer Received.");

                //Console.WriteLine(sdpAnswer);

                var answerSDP = SDP.ParseSDPDescription(sdpAnswer);

                logger.Debug("ICE User: " + answerSDP.IceUfrag + ".");
                logger.Debug("ICE Password: " + answerSDP.IcePwd + ".");

                var peer = _webRtcPeers.SingleOrDefault(x => x.WebSocketID == webSocketID);

                if (peer == null)
                {
                    logger.Warn("No WebRTC client entry exists for web socket ID " + webSocketID + ", ignoring.");
                }
                else
                {
                    logger.Debug("New WebRTC client SDP answer for web socket ID " + webSocketID + ".");

                    peer.SdpSessionID = answerSDP.SessionId;
                    peer.RemoteIceUser = answerSDP.IceUfrag;
                    peer.RemoteIcePassword = answerSDP.IcePwd;
                    peer.RemoteIceCandidates = answerSDP.IceCandidates;
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

        /// <summary>
        /// a=candidate:2786641038 1 udp 2122260223 192.168.33.125 59754 typ host generation 0
        /// a=candidate:1788214045 1 udp 1686052607 150.101.105.181 59754 typ srflx raddr 192.168.33.125 rport 59754 generation 0
        /// a=candidate:2234295925 1 udp 41885439 103.29.66.243 61480 typ relay raddr 150.101.105.181 rport 59754 generation 0
        /// </summary>
        private static void InitialiseWebRtcPeer(WebRtcPeer peer)
        {
            List<IceCandidate> iceCandidates = GetIceCandidates(peer);

            DateTime startTime = DateTime.Now;

            while (iceCandidates.Any(x => x.IsGatheringComplete == false))
            {
                //Console.WriteLine("Waiting for ICE candidate gathering to complete.");
                Thread.Sleep(1000);

                if (DateTime.Now.Subtract(startTime).TotalSeconds > ICE_GATHERING_TIMEOUT_SECONDS)
                {
                    Console.WriteLine("Timed out waiting for ICE gathering to complete.");
                    peer.Close();
                    return;
                }
            }

            string iceCandidateString = null;

            foreach (var iceCandidate in iceCandidates.Where(x => x.HasGatheringFailed == false))
            {
                iceCandidateString += String.Format("a=candidate:{0} {1} udp {2} {3} {4} typ host generation 0\r\n", Crypto.GetRandomInt(10).ToString(), "1", Crypto.GetRandomInt(10).ToString(), iceCandidate.LocalAddress.ToString(), (iceCandidate.LocalRtpSocket.LocalEndPoint as IPEndPoint).Port);

                if (iceCandidate.StunRflxIPEndPoint != null)
                {
                    iceCandidateString += String.Format("a=candidate:{0} {1} udp {2} {3} {4} typ srflx raddr {5} rport {6} generation 0\r\n", Crypto.GetRandomInt(10).ToString(), "1", Crypto.GetRandomInt(10).ToString(), iceCandidate.StunRflxIPEndPoint.Address, iceCandidate.StunRflxIPEndPoint.Port, iceCandidate.LocalAddress.ToString(), (iceCandidate.LocalRtpSocket.LocalEndPoint as IPEndPoint).Port);
                }

                //if (iceCandidate.TurnRelayIPEndPoint != null)
                //{
                //    iceCandidateString += String.Format("a=candidate:{0} {1} udp {2} {3} {4} typ relay raddr {5} rport {6} generation 0\r\n", Crypto.GetRandomInt(10).ToString(), "1", Crypto.GetRandomInt(10).ToString(), iceCandidate.TurnRelayIPEndPoint.Address, iceCandidate.TurnRelayIPEndPoint.Port, iceCandidate.StunRflxIPEndPoint.Address, iceCandidate.StunRflxIPEndPoint.Port);
                //}
            }

            logger.Debug("ICE Candidates: " + iceCandidateString);

            var localIceUser = Crypto.GetRandomString(20);
            var localIcePassword = Crypto.GetRandomString(20) + Crypto.GetRandomString(20);

            var offer = String.Format(_sdpOfferTemplate, Crypto.GetRandomInt(10).ToString(), (iceCandidates.First().LocalRtpSocket.LocalEndPoint as IPEndPoint).Port, iceCandidates.First().LocalAddress, iceCandidateString.TrimEnd(), localIceUser, localIcePassword);

            //logger.Debug("WebRTC Offer SDP: " + offer);

            peer.SDP = offer;
            ////SdpSessionID = answerSDP.SessionId,
            //SocketAddress = new IPEndPoint(IPAddress.Parse(_clientIPAddress), matchingCandidate.Port),
            //ICEUser = answerSDP.IceUfrag,
            //ICEPassword = answerSDP.IcePwd,
            peer.LocalIceUser = localIceUser;
            peer.LocalIcePassword = localIcePassword;
            peer.SSRC = Convert.ToUInt32(Crypto.GetRandomInt(10));
            //SSRC = (ssrc != 0) ? ssrc : Convert.ToUInt32(Crypto.GetRandomInt(10)),
            //SSRC = 2889419400,
            peer.SequenceNumber = 1;
            //SrtpReceiveContext = (receiveSrtpKey != null) ? new SRTPManaged(Convert.FromBase64String(receiveSrtpKey), false) : null
            peer.LocalIceCandidates = iceCandidates;
        }

        private static List<IceCandidate> GetIceCandidates(WebRtcPeer peer)
        {
            List<IceCandidate> iceCandidates = new List<IceCandidate>();

            var addresses = System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetUnicastAddresses().Where(x => x.Address.AddressFamily == AddressFamily.InterNetwork && IPAddress.IsLoopback(x.Address) == false);

            foreach (var address in addresses)
            {
                logger.Debug("Attempting to create RTP socket with IP address " + address.Address + ".");

                Socket rtpSocket = null;
                Socket controlSocket = null;

                NetServices.CreateRtpSocket(address.Address, WEBRTC_START_PORT, WEBRTC_END_PORT, false, out rtpSocket, out controlSocket);

                if (rtpSocket != null)
                {
                    logger.Debug("RTP socket successfully created on " + rtpSocket.LocalEndPoint + ".");

                    var iceCandidate = new IceCandidate() { LocalAddress = address.Address, LocalRtpSocket = rtpSocket, LocalControlSocket = controlSocket, TurnServer = new TurnServer() { ServerEndPoint = _turnServerEndPoint, Username = "user", Password = "password" } };

                    var listenerTask = Task.Run(() => { StartWebRtcRtpListener(peer, iceCandidate); });

                    iceCandidate.RtpListenerTask = listenerTask;

                    iceCandidates.Add(iceCandidate);

                    var stunBindingTask = Task.Run(() => { SendInitialStunBindingRequest(peer, iceCandidate); });

                    //AllocateTurn(iceCandidate);

                    //iceCandidate.IsGatheringComplete = true;
                }
            }

            return iceCandidates;
        }

        private static void SendInitialStunBindingRequest(WebRtcPeer peer, IceCandidate iceCandidate)
        {
            int attempts = 1;

            while (attempts < 5 && !peer.IsClosed && iceCandidate.IsGatheringComplete == false)
            {
                logger.Debug("Sending STUN binding request " + attempts + " from " + iceCandidate.LocalAddress + " to " + iceCandidate.TurnServer.ServerEndPoint + ".");

                STUNv2Message stunRequest = new STUNv2Message(STUNv2MessageTypesEnum.BindingRequest);
                stunRequest.Header.TransactionId = Guid.NewGuid().ToByteArray().Take(12).ToArray();
                byte[] stunReqBytes = stunRequest.ToByteBuffer(null, false);

                iceCandidate.LocalRtpSocket.SendTo(stunReqBytes, iceCandidate.TurnServer.ServerEndPoint);

                Thread.Sleep(1000);

                attempts++;
            }
        }

        private static void AllocateTurn(IceCandidate iceCandidate)
        {
            try
            {
                if (iceCandidate.AllocateAttempts >= MAXIMUM_TURN_ALLOCATE_ATTEMPTS)
                {
                    logger.Debug("TURN allocation for local socket " + iceCandidate.LocalAddress + " failed after " + iceCandidate.AllocateAttempts + " attempts.");

                    iceCandidate.IsGatheringComplete = true;
                }
                else
                {
                    iceCandidate.AllocateAttempts++;

                    //logger.Debug("Sending STUN connectivity check to client " + client.SocketAddress + ".");

                    STUNv2Message stunRequest = new STUNv2Message(STUNv2MessageTypesEnum.Allocate);
                    stunRequest.Header.TransactionId = Guid.NewGuid().ToByteArray().Take(12).ToArray();
                    stunRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.Lifetime, 3600));
                    stunRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.RequestedTransport, STUNv2AttributeConstants.UdpTransportType));   // UDP
                    byte[] stunReqBytes = stunRequest.ToByteBuffer(null, false);
                    iceCandidate.LocalRtpSocket.SendTo(stunReqBytes, iceCandidate.TurnServer.ServerEndPoint);

                    //client.LastSTUNSendAt = DateTime.Now;

                    //if(client.IsDtlsNegotiationComplete)
                    //{
                    //    // Send RTCP report.
                    //    RTCPPacket rtcp = new RTCPPacket(client.SSRC, 0, 0, 0, 0);
                    //    RTCPReport rtcpResport = new RTCPReport(Guid.NewGuid(), 0, client.SocketAddress);
                    //    var rtcpBuffer = rtcp.GetBytes(rtcpResport.GetBytes());
                    //    var rtcpProtectedBuffer = new byte[rtcpBuffer.Length + SRTP_AUTH_KEY_LENGTH];
                    //    Buffer.BlockCopy(rtcpBuffer, 0, rtcpProtectedBuffer, 0, rtcpBuffer.Length);
                    //    int rtperr = client.SrtpContext.ProtectRTP(rtcpBuffer, rtcpBuffer.Length - SRTP_AUTH_KEY_LENGTH);

                    //    if (rtperr != 0)
                    //    {
                    //        logger.Debug("RTCP packet protect result " + rtperr + ".");
                    //    }
                    //    else
                    //    {
                    //        localSocket.Send(rtcpProtectedBuffer, rtcpProtectedBuffer.Length, client.SocketAddress);
                    //    }
                    //}
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception AllocateTurn. " + excp);
            }
        }

        private static void CreateTurnPermissions(WebRtcPeer peer)
        {
            try
            {
                var localTurnIceCandidate = (from cand in peer.LocalIceCandidates where cand.TurnRelayIPEndPoint != null select cand).First();
                var remoteTurnCandidate = (from cand in peer.RemoteIceCandidates where cand.CandidateType == IceCandidateTypesEnum.relay select cand).First();

                // Send create permission request
                STUNv2Message turnPermissionRequest = new STUNv2Message(STUNv2MessageTypesEnum.CreatePermission);
                turnPermissionRequest.Header.TransactionId = Guid.NewGuid().ToByteArray().Take(12).ToArray();
                //turnBindRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.ChannelNumber, (ushort)3000));
                turnPermissionRequest.Attributes.Add(new STUNv2XORAddressAttribute(STUNv2AttributeTypesEnum.XORPeerAddress, remoteTurnCandidate.Port, IPAddress.Parse(remoteTurnCandidate.NetworkAddress)));
                turnPermissionRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.Username, Encoding.UTF8.GetBytes(localTurnIceCandidate.TurnServer.Username)));
                turnPermissionRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.Nonce, Encoding.UTF8.GetBytes(localTurnIceCandidate.TurnServer.Nonce)));
                turnPermissionRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.Realm, Encoding.UTF8.GetBytes(localTurnIceCandidate.TurnServer.Realm)));

                MD5 md5 = new MD5CryptoServiceProvider();
                byte[] hmacKey = md5.ComputeHash(Encoding.UTF8.GetBytes(localTurnIceCandidate.TurnServer.Username + ":" + localTurnIceCandidate.TurnServer.Realm + ":" + localTurnIceCandidate.TurnServer.Password));

                byte[] turnPermissionReqBytes = turnPermissionRequest.ToByteBuffer(hmacKey, false);
                localTurnIceCandidate.LocalRtpSocket.SendTo(turnPermissionReqBytes, localTurnIceCandidate.TurnServer.ServerEndPoint);
            }
            catch (Exception excp)
            {
                logger.Error("Exception CreateTurnPermissions. " + excp);
            }
        }

        private static void SendStunConnectivityChecks()
        {
            try
            {
                while (!m_exit)
                {
                    try
                    {
                        //foreach (var peer in _webRtcPeers.Where(x => DateTime.Now.Subtract(x.LastSTUNSendAt).TotalSeconds > STUN_CONNECTIVITY_CHECK_SECONDS
                        //        && x.RemoteIceCandidates != null && x.RemoteIceCandidates.Count > 0 && x.RemoteIceUser != null && x.RemoteIcePassword != null))
                        //{
                        //foreach (var iceCandidate in peer.RemoteIceCandidates.Where(x => x.CandidateType == IceCandidateTypesEnum.relay))
                        foreach (var peer in _webRtcPeers.Where(x => !x.IsClosed && x.RemoteIceUser != null && x.RemoteIcePassword != null && x.LocalIceCandidates != null && x.LocalIceCandidates.Count > 0))
                        {
                            // If one of the ICE candidates has the remote RTP socket set then the negotiation is complete and the STUN checks are to keep the connection alive.
                            if (peer.LocalIceCandidates.Any(x => x.RemoteRtpEndPoint != null))
                            {
                                foreach (var iceCandidate in peer.LocalIceCandidates.Where(x => x.RemoteRtpEndPoint != null && x.IsDisconnected == false &&
                                        DateTime.Now.Subtract(x.LastSTUNSendAt).TotalSeconds > STUN_CONNECTIVITY_CHECK_SECONDS))
                                {
                                    //logger.Debug("Sending STUN connectivity check to client " + iceCandidate.NetworkAddress + ":" + iceCandidate.Port + ".");

                                    STUNv2Message stunRequest = new STUNv2Message(STUNv2MessageTypesEnum.BindingRequest);
                                    stunRequest.Header.TransactionId = Guid.NewGuid().ToByteArray().Take(12).ToArray();
                                    stunRequest.AddUsernameAttribute(peer.RemoteIceUser + ":" + peer.LocalIceUser);
                                    stunRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.Priority, new byte[] { 0x6e, 0x7f, 0x1e, 0xff }));
                                    stunRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.UseCandidate, null));   // Must send this to get DTLS started.
                                    byte[] stunReqBytes = stunRequest.ToByteBufferStringKey(peer.RemoteIcePassword, true);

                                    iceCandidate.LocalRtpSocket.SendTo(stunReqBytes, iceCandidate.RemoteRtpEndPoint);

                                    iceCandidate.LastSTUNSendAt = DateTime.Now;
                                }

                                //if(client.IsDtlsNegotiationComplete)
                                //{
                                //    // Send RTCP report.
                                //    RTCPPacket rtcp = new RTCPPacket(client.SSRC, 0, 0, 0, 0);
                                //    RTCPReport rtcpResport = new RTCPReport(Guid.NewGuid(), 0, client.SocketAddress);
                                //    var rtcpBuffer = rtcp.GetBytes(rtcpResport.GetBytes());
                                //    var rtcpProtectedBuffer = new byte[rtcpBuffer.Length + SRTP_AUTH_KEY_LENGTH];
                                //    Buffer.BlockCopy(rtcpBuffer, 0, rtcpProtectedBuffer, 0, rtcpBuffer.Length);
                                //    int rtperr = client.SrtpContext.ProtectRTP(rtcpBuffer, rtcpBuffer.Length - SRTP_AUTH_KEY_LENGTH);

                                //    if (rtperr != 0)
                                //    {
                                //        logger.Debug("RTCP packet protect result " + rtperr + ".");
                                //    }
                                //    else
                                //    {
                                //        localSocket.Send(rtcpProtectedBuffer, rtcpProtectedBuffer.Length, client.SocketAddress);
                                //    }
                                //}
                            }
                            else
                            {
                                // The RTP socket is not yet available which means the connection negotation is still ongoing. Once the ICE credentials are available send the binding request to all remote candidates.
                                foreach (var iceCandidate in peer.LocalIceCandidates.Where(x => DateTime.Now.Subtract(x.LastSTUNSendAt).TotalSeconds > STUN_CONNECTIVITY_CHECK_SECONDS))
                                {
                                    foreach (var remoteIceCandidate in peer.RemoteIceCandidates)
                                    {
                                        if (remoteIceCandidate.NetworkAddress != "192.168.33.118")
                                        {
                                            STUNv2Message stunRequest = new STUNv2Message(STUNv2MessageTypesEnum.BindingRequest);
                                            stunRequest.Header.TransactionId = Guid.NewGuid().ToByteArray().Take(12).ToArray();
                                            stunRequest.AddUsernameAttribute(peer.RemoteIceUser + ":" + peer.LocalIceUser);
                                            stunRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.Priority, new byte[] { 0x6e, 0x7f, 0x1e, 0xff }));
                                            stunRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.UseCandidate, null));   // Must send this to get DTLS started.
                                            byte[] stunReqBytes = stunRequest.ToByteBufferStringKey(peer.RemoteIcePassword, true);

                                            iceCandidate.LocalRtpSocket.SendTo(stunReqBytes, new IPEndPoint(IPAddress.Parse(remoteIceCandidate.NetworkAddress), remoteIceCandidate.Port));

                                            iceCandidate.LastSTUNSendAt = DateTime.Now;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception excp)
                    {
                        logger.Error("Exception SendStunConnectivityCheck ConnectivityCheck. " + excp);
                    }

                    Thread.Sleep(1000);

                    //lock (_webRtcPeers)
                    //{
                    //    var expiredClients = (from cli in _webRtcPeers where cli.StunExchangeComplete && cli.IsDtlsNegotiationComplete && DateTime.Now.Subtract(cli.LastSTUNReceiveAt).TotalSeconds > EXPIRE_CLIENT_SECONDS select cli).ToList();
                    //    for (int index = 0; index < expiredClients.Count(); index++)
                    //    {
                    //        var expiredClient = expiredClients[index];
                    //        logger.Debug("Removed expired client " + expiredClient.SocketAddress + ".");
                    //        _webRtcPeers.TryTake(out expiredClient);
                    //    }
                    //}
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SendStunConnectivityCheck. " + excp);
            }
        }

        private static void StartWebRtcRtpListener(WebRtcPeer peer, IceCandidate iceCandidate)
        {
            try
            {
                logger.Debug("Starting WebRTC RTP listener for " + iceCandidate.LocalRtpSocket.LocalEndPoint + ".");

                IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                UdpClient localSocket = new UdpClient();
                localSocket.Client = iceCandidate.LocalRtpSocket;

                while (!m_exit)
                {
                    try
                    {
                        //logger.Debug("ListenToReceiverWebRTCClient Receive.");
                        byte[] buffer = localSocket.Receive(ref remoteEndPoint);

                        if (remoteEndPoint.Address.ToString() == "192.168.33.118")
                        {
                            //Console.WriteLine("Droppning packet of " + buffer.Length + " bytes from 192.168.33.118.");
                            continue;
                        }

                        //logger.Debug(buffer.Length + " bytes read on Receiver Client media socket from " + remoteEndPoint.ToString() + ".");

                        //if (buffer.Length > 3 && buffer[0] == 0x16 && buffer[1] == 0xfe)
                        if ((buffer[0] >= 20) && (buffer[0] <= 64))
                        {
                            DtlsMessageReceived(peer, iceCandidate, buffer, remoteEndPoint);
                        }
                        //else if ((buffer[0] & 0x80) == 0)
                        else if ((buffer[0] == 0) || (buffer[0] == 1))
                        {
                            STUNv2Message stunMessage = STUNv2Message.ParseSTUNMessage(buffer, buffer.Length);
                            ProcessStunMessage(peer, iceCandidate, stunMessage, remoteEndPoint);
                        }
                        else if ((buffer[0] >= 128) && (buffer[0] <= 191))
                        {
                            //logger.Debug("A non-STUN packet was received Receiver Client.");

                            if (buffer[1] == 0xC8 /* RTCP SR */ || buffer[1] == 0xC9 /* RTCP RR */)
                            {
                                // RTCP packet.
                                //webRtcClient.LastSTUNReceiveAt = DateTime.Now;
                            }
                            else
                            {
                                // RTP packet.
                                int res = peer.SrtpReceiveContext.UnprotectRTP(buffer, buffer.Length);

                                if (res != 0)
                                {
                                    logger.Warn("SRTP unprotect failed, result " + res + ".");
                                }
                            }
                        }
                        else
                        {
                            logger.Debug("An unrecognised packet was received on the WebRTC media socket.");
                        }
                    }
                    catch (Exception sockExcp)
                    {
                        logger.Debug("ListenToReceiverWebRTCClient Receive. " + sockExcp.Message);

                        if (iceCandidate.RemoteRtpEndPoint != null)
                        {
                            iceCandidate.DisconnectionMessage = sockExcp.Message;
                            break;
                        }
                    }
                }

                peer.Close();
            }
            catch (Exception excp)
            {
                logger.Error("Exception ListenForWebRTCClient. " + excp);
            }
        }

        private static void DtlsMessageReceived(WebRtcPeer peer, IceCandidate iceCandidate, byte[] buffer, IPEndPoint remoteEndPoint)
        {
            logger.Debug("DTLS packet received " + buffer.Length + " bytes from " + remoteEndPoint.ToString() + ".");

            if (peer.DtlsContext == null)
            {
                peer.DtlsContext = new DtlsManaged();
                int res = peer.DtlsContext.Init();
                Console.WriteLine("DtlsContext initialisation result=" + res);
            }

            int bytesWritten = peer.DtlsContext.Write(buffer, buffer.Length);

            if (bytesWritten != buffer.Length)
            {
                logger.Warn("The required number of bytes were not successfully written to the DTLS context.");
            }
            else
            {
                byte[] dtlsOutBytes = new byte[2048];

                int bytesRead = peer.DtlsContext.Read(dtlsOutBytes, dtlsOutBytes.Length);

                if (bytesRead == 0)
                {
                    Console.WriteLine("No bytes read from DTLS context :(.");
                }
                else
                {
                    Console.WriteLine(bytesRead + " bytes read from DTLS context sending to " + remoteEndPoint.ToString() + ".");
                    iceCandidate.LocalRtpSocket.SendTo(dtlsOutBytes, 0, bytesRead, SocketFlags.None, remoteEndPoint);

                    //if (client.DtlsContext.IsHandshakeComplete())
                    if (peer.DtlsContext.GetState() == 3)
                    {
                        Console.WriteLine("DTLS negotiation complete for " + remoteEndPoint.ToString() + ".");
                        peer.SrtpContext = new SRTPManaged(peer.DtlsContext, false);
                        peer.SrtpReceiveContext = new SRTPManaged(peer.DtlsContext, true);
                        peer.IsDtlsNegotiationComplete = true;
                        iceCandidate.RemoteRtpEndPoint = remoteEndPoint;
                    }
                }
            }
        }

        private static void ProcessStunMessage(WebRtcPeer peer, IceCandidate iceCandidate, STUNv2Message stunMessage, IPEndPoint remoteEndPoint)
        {
            //logger.Debug("STUN message received from client " + remoteEndPoint + " @ " + stunMessage.Header.MessageType + ".");

            if (stunMessage.Header.MessageType == STUNv2MessageTypesEnum.BindingRequest)
            {
                string stunUserAttribute = Encoding.UTF8.GetString(stunMessage.Attributes.Where(y => y.AttributeType == STUNv2AttributeTypesEnum.Username).Single().Value);

                //if (peer.SocketAddress == null)
                //{
                //    peer.SocketAddress = remoteEndPoint;
                //    logger.Debug("Set socket endpoint of WebRTC client with SDP session ID " + peer.SdpSessionID + " to " + remoteEndPoint + ".");
                //}

                iceCandidate.LastSTUNReceiveAt = DateTime.Now;

                if (stunMessage.Header.MessageType == STUNv2MessageTypesEnum.BindingRequest)
                {
                    //logger.Debug("Sending STUN response to Receiver Client @ " + remoteEndPoint + ".");

                    STUNv2Message stunResponse = new STUNv2Message(STUNv2MessageTypesEnum.BindingSuccessResponse);
                    stunResponse.Header.TransactionId = stunMessage.Header.TransactionId;
                    stunResponse.AddXORMappedAddressAttribute(remoteEndPoint.Address, remoteEndPoint.Port);
                    byte[] stunRespBytes = stunResponse.ToByteBufferStringKey(peer.LocalIcePassword, true);
                    //localSocket.Send(stunRespBytes, stunRespBytes.Length, remoteEndPoint);
                    iceCandidate.LocalRtpSocket.SendTo(stunRespBytes, remoteEndPoint);

                    //logger.Debug("Sending Binding request to Receiver Client @ " + remoteEndPoint + ".");
                    //if (client != null && !client.STUNExchangeComplete)
                    //if (client != null)
                    //{
                    //    //client.SrtpContext = new SRTPManaged(Convert.FromBase64String(_sourceSRTPKey));
                    //    //client.IsDtlsNegotiationComplete = true;

                    //    STUNv2Message stunRequest = new STUNv2Message(STUNv2MessageTypesEnum.BindingRequest);
                    //    stunRequest.Header.TransactionId = Guid.NewGuid().ToByteArray().Take(12).ToArray();
                    //    stunRequest.AddUsernameAttribute(client.ICEUser + ":" + client.LocalICEUser);
                    //    stunRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.Priority, new byte[] { 0x6e, 0x7f, 0x1e, 0xff }));
                    //    stunRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.UseCandidate, null));   // Must send this to get DTLS started.
                    //    byte[] stunReqBytes = stunRequest.ToByteBufferStringKey(client.ICEPassword, true);
                    //    localSocket.Send(stunReqBytes, stunReqBytes.Length, remoteEndPoint);

                    //    client.LastSTUNSendAt = DateTime.Now;
                    //}
                }
            }
            else if (stunMessage.Header.MessageType == STUNv2MessageTypesEnum.BindingSuccessResponse)
            {
                iceCandidate.LastSTUNReceiveAt = DateTime.Now;

                if (iceCandidate.IsGatheringComplete == false)
                {
                    var reflexAddressAttribute = stunMessage.Attributes.FirstOrDefault(y => y.AttributeType == STUNv2AttributeTypesEnum.XORMappedAddress) as STUNv2XORAddressAttribute;

                    if (reflexAddressAttribute != null)
                    {
                        logger.Debug("STUN reflex address " + reflexAddressAttribute.Address + ":" + reflexAddressAttribute.Port);

                        iceCandidate.StunRflxIPEndPoint = new IPEndPoint(reflexAddressAttribute.Address, reflexAddressAttribute.Port);
                        iceCandidate.IsGatheringComplete = true;
                    }
                    else
                    {
                        logger.Debug("A STUN binding response did not have an XORMappedAddress attribute.");
                        iceCandidate.IsGatheringComplete = true;
                        iceCandidate.HasGatheringFailed = true;
                        iceCandidate.FailureMessage = "The STUN binding response from " + remoteEndPoint + " did not have an XORMappedAddress attribute, rlfx address can not be determined.";
                    }
                }
                else if (peer.StunExchangeComplete == false)
                {
                    peer.StunExchangeComplete = true;
                    logger.Debug("WebRTC client STUN exchange complete for " + remoteEndPoint.ToString() + ".");
                }
            }
            else if (stunMessage.Header.MessageType == STUNv2MessageTypesEnum.BindingErrorResponse)
            {
                logger.Debug("A STUN binding error response was received from  " + remoteEndPoint + ".");
            }
            else if (stunMessage.Header.MessageType == STUNv2MessageTypesEnum.AllocateErrorResponse)
            {
                logger.Debug("A STUN allocate error response was received from " + remoteEndPoint + ".");

                var errorCodeAttribute = stunMessage.Attributes.Where(y => y.AttributeType == STUNv2AttributeTypesEnum.ErrorCode).SingleOrDefault() as STUNv2ErrorCodeAttribute;

                if (errorCodeAttribute == null)
                {
                    logger.Debug("There was no error code attribute on the allocate error response.");
                }
                else
                {
                    logger.Debug("Allocate error response code " + errorCodeAttribute.ErrorCode + ".");

                    if (errorCodeAttribute.ErrorCode == 401)
                    {
                        string stunNonceAttribute = Encoding.UTF8.GetString(stunMessage.Attributes.Where(y => y.AttributeType == STUNv2AttributeTypesEnum.Nonce).Single().Value);
                        string stunRealmAttribute = Encoding.UTF8.GetString(stunMessage.Attributes.Where(y => y.AttributeType == STUNv2AttributeTypesEnum.Realm).Single().Value);

                        //logger.Debug("Allocate Error: " + errorCodeAttribute.ToString() + ".");
                        //logger.Debug("Nonce: " + stunNonceAttribute + ", Realm: " + stunRealmAttribute + ".");

                        iceCandidate.TurnServer.Realm = stunRealmAttribute;
                        iceCandidate.TurnServer.Nonce = stunNonceAttribute;

                        if (iceCandidate.TurnServer.AuthorisationAttempts == 0)
                        {
                            iceCandidate.TurnServer.AuthorisationAttempts++;

                            // Authenticate the request.
                            STUNv2Message turnAllocateRequest = new STUNv2Message(STUNv2MessageTypesEnum.Allocate);
                            turnAllocateRequest.Header.TransactionId = Guid.NewGuid().ToByteArray().Take(12).ToArray();
                            turnAllocateRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.Lifetime, 3600));
                            turnAllocateRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.RequestedTransport, STUNv2AttributeConstants.UdpTransportType));
                            turnAllocateRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.Username, Encoding.UTF8.GetBytes(iceCandidate.TurnServer.Username)));
                            turnAllocateRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.Nonce, Encoding.UTF8.GetBytes(iceCandidate.TurnServer.Nonce)));
                            turnAllocateRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.Realm, Encoding.UTF8.GetBytes(iceCandidate.TurnServer.Realm)));

                            MD5 md5 = new MD5CryptoServiceProvider();
                            byte[] hmacKey = md5.ComputeHash(Encoding.UTF8.GetBytes(iceCandidate.TurnServer.Username + ":" + iceCandidate.TurnServer.Realm + ":" + iceCandidate.TurnServer.Password));

                            byte[] turnAllocateReqBytes = turnAllocateRequest.ToByteBuffer(hmacKey, false);
                            iceCandidate.LocalRtpSocket.SendTo(turnAllocateReqBytes, remoteEndPoint);
                        }
                    }
                    else if (errorCodeAttribute.ErrorCode == 437)
                    {
                        // Allocation already in use, try and delete it.
                        STUNv2Message turnRefreshRequest = new STUNv2Message(STUNv2MessageTypesEnum.Refresh);
                        turnRefreshRequest.Header.TransactionId = Guid.NewGuid().ToByteArray().Take(12).ToArray();
                        turnRefreshRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.Lifetime, 0));

                        byte[] turnRefreshReqBytes = turnRefreshRequest.ToByteBuffer(null, false);
                        iceCandidate.LocalRtpSocket.SendTo(turnRefreshReqBytes, remoteEndPoint);
                    }
                }
            }
            else if (stunMessage.Header.MessageType == STUNv2MessageTypesEnum.RefreshErrorResponse)
            {
                logger.Debug("A STUN refresh error response was received from Receiver Client.");

                var errorCodeAttribute = stunMessage.Attributes.Where(y => y.AttributeType == STUNv2AttributeTypesEnum.ErrorCode).Single() as STUNv2ErrorCodeAttribute;

                if (errorCodeAttribute.ErrorCode == 401)
                {
                    //string stunUserAttribute = Encoding.UTF8.GetString(stunMessage.Attributes.Where(y => y.AttributeType == STUNv2AttributeTypesEnum.Username).Single().Value);
                    string stunNonceAttribute = Encoding.UTF8.GetString(stunMessage.Attributes.Where(y => y.AttributeType == STUNv2AttributeTypesEnum.Nonce).Single().Value);
                    string stunRealmAttribute = Encoding.UTF8.GetString(stunMessage.Attributes.Where(y => y.AttributeType == STUNv2AttributeTypesEnum.Realm).Single().Value);

                    logger.Debug("Refresh Error: " + errorCodeAttribute.ToString() + ".");
                    logger.Debug("Nonce: " + stunNonceAttribute + ", Realm: " + stunRealmAttribute + ".");

                    iceCandidate.TurnServer.Realm = stunRealmAttribute;
                    iceCandidate.TurnServer.Nonce = stunNonceAttribute;

                    if (iceCandidate.TurnServer.AuthorisationAttempts == 0)
                    {
                        iceCandidate.TurnServer.AuthorisationAttempts++;

                        // Authenticate the request.
                        STUNv2Message turnRefreshRequest = new STUNv2Message(STUNv2MessageTypesEnum.Refresh);
                        turnRefreshRequest.Header.TransactionId = Guid.NewGuid().ToByteArray().Take(12).ToArray();
                        turnRefreshRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.Lifetime, 0));
                        turnRefreshRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.Username, Encoding.UTF8.GetBytes(iceCandidate.TurnServer.Username)));
                        turnRefreshRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.Nonce, Encoding.UTF8.GetBytes(iceCandidate.TurnServer.Nonce)));
                        turnRefreshRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.Realm, Encoding.UTF8.GetBytes(iceCandidate.TurnServer.Realm)));

                        MD5 md5 = new MD5CryptoServiceProvider();
                        byte[] hmacKey = md5.ComputeHash(Encoding.UTF8.GetBytes("user" + ":" + stunRealmAttribute + ":" + "password"));

                        byte[] turnRefreshReqBytes = turnRefreshRequest.ToByteBuffer(hmacKey, false);
                        iceCandidate.LocalRtpSocket.SendTo(turnRefreshReqBytes, remoteEndPoint);
                    }
                }
            }
            else if (stunMessage.Header.MessageType == STUNv2MessageTypesEnum.AllocateSuccessResponse)
            {
                logger.Debug("An allocation request was successful.");

                var relayedAddressAttribute = stunMessage.Attributes.FirstOrDefault(y => y.AttributeType == STUNv2AttributeTypesEnum.XORRelayedAddress) as STUNv2XORAddressAttribute;

                if (relayedAddressAttribute != null)
                {
                    logger.Debug("TURN relay address " + relayedAddressAttribute.Address + ":" + relayedAddressAttribute.Port);

                    iceCandidate.TurnRelayIPEndPoint = new IPEndPoint(relayedAddressAttribute.Address, relayedAddressAttribute.Port);
                }
                else
                {
                    logger.Debug("Could not determine the TURN relay address.");
                }

                var reflexAddressAttribute = stunMessage.Attributes.FirstOrDefault(y => y.AttributeType == STUNv2AttributeTypesEnum.XORMappedAddress) as STUNv2XORAddressAttribute;

                if (reflexAddressAttribute != null)
                {
                    logger.Debug("STUN reflex address " + reflexAddressAttribute.Address + ":" + reflexAddressAttribute.Port);

                    iceCandidate.StunRflxIPEndPoint = new IPEndPoint(reflexAddressAttribute.Address, reflexAddressAttribute.Port);
                }
                else
                {
                    logger.Debug("Could not determine the STUN reflex address.");
                }

                iceCandidate.TurnServer.AuthorisationAttempts = 0;
                iceCandidate.IsGatheringComplete = true;
            }
            else if (stunMessage.Header.MessageType == STUNv2MessageTypesEnum.RefreshSuccessResponse)
            {
                logger.Debug("A refresh request was successful.");

                Thread.Sleep(1000);

                iceCandidate.TurnServer.AuthorisationAttempts = 0;

                AllocateTurn(iceCandidate);
            }
            else if (stunMessage.Header.MessageType == STUNv2MessageTypesEnum.CreatePermissionSuccessResponse)
            {
                logger.Debug("A create permission request was successful.");

                // Send channel bind request
                //STUNv2Message turnBindRequest = new STUNv2Message(STUNv2MessageTypesEnum.ChannelBind);
                //turnBindRequest.Header.TransactionId = Guid.NewGuid().ToByteArray().Take(12).ToArray();
                //turnBindRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.ChannelNumber, (ushort)0x4000));
                //turnBindRequest.Attributes.Add(new STUNv2XORAddressAttribute(STUNv2AttributeTypesEnum.XORPeerAddress, 50000, IPAddress.Parse("150.101.105.181")));
                //turnBindRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.Username, Encoding.UTF8.GetBytes(iceCandidate.TurnServer.Username)));
                //turnBindRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.Nonce, Encoding.UTF8.GetBytes(iceCandidate.TurnServer.Nonce)));
                //turnBindRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.Realm, Encoding.UTF8.GetBytes(iceCandidate.TurnServer.Realm)));

                //MD5 md5 = new MD5CryptoServiceProvider();
                //byte[] hmacKey = md5.ComputeHash(Encoding.UTF8.GetBytes("user" + ":" + "qikid.com" + ":" + "password"));

                //byte[] turnBindReqBytes = turnBindRequest.ToByteBuffer(hmacKey, false);
                //localSocket.Send(turnBindReqBytes, turnBindReqBytes.Length, remoteEndPoint);
            }
            else if (stunMessage.Header.MessageType == STUNv2MessageTypesEnum.ChannelBindSuccessResponse)
            {
                logger.Debug("A channel bind request was successful.");

                //UdpClient testClient = new UdpClient(50000);
                //testClient.Send(new byte[] { 0x01, 0x02, 0x03, 0x04 }, 4, turnRelayeEP);
            }
            else if (stunMessage.Header.MessageType == STUNv2MessageTypesEnum.DataIndication)
            {
                logger.Debug("A STUN data indication was received.");

                var dataAttribute = stunMessage.Attributes.Where(y => y.AttributeType == STUNv2AttributeTypesEnum.Data).SingleOrDefault();

                if (dataAttribute != null && dataAttribute.Value != null)
                {
                    logger.Debug("Data:" + Convert.ToBase64String(dataAttribute.Value));
                }

                if (dataAttribute.Value[0] >= 20 && dataAttribute.Value[0] <= 64)
                {
                    logger.Debug("Data indication was DTLS.");
                    DtlsMessageReceived(peer, iceCandidate, dataAttribute.Value, remoteEndPoint);
                }
                if (dataAttribute.Value[0] == 0 || dataAttribute.Value[0] == 1)
                {
                    logger.Debug("Data indication was STUN.");
                    STUNv2Message turnStunMessage = STUNv2Message.ParseSTUNMessage(dataAttribute.Value, dataAttribute.Value.Length);
                    ProcessStunMessage(peer, iceCandidate, turnStunMessage, remoteEndPoint);
                }
                else
                {
                    logger.Debug("Data indication was not recognised.");
                }
            }
            else
            {
                logger.Debug("An unrecognised STUN request was received from Receiver Client.");
            }
        }

        private static void RelayRTP(UdpClient rtpClient)
        {
            try
            {
                DateTime lastCleanup = DateTime.Now;
                //_newRTPReceiverSRTP = new SRTPManaged(Convert.FromBase64String(_sourceSRTPKey));
                //_newRTPReceiverSRTP = new SRTPManaged();

                IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                byte[] buffer = rtpClient.Receive(ref remoteEndPoint);

                StreamWriter sw = new StreamWriter("rtpPackets.txt");
                byte[] frame = new byte[1000000];
                int framePosition = 0;
                int sampleCount = 0;
                DateTime lastReceiveTime = DateTime.Now;

                while (buffer != null && buffer.Length > 0 && !m_exit)
                {
                    int packetSpacingMilli = Convert.ToInt32(DateTime.Now.Subtract(lastReceiveTime).TotalMilliseconds);
                    Console.WriteLine("Packet spacing " + packetSpacingMilli + "ms.");
                    lastReceiveTime = DateTime.Now;

                    if (_webRtcPeers.Count != 0)
                    {
                        RTPPacket triggerRTPPacket = new RTPPacket(buffer);
                        RTPVP8Header vp8Header = RTPVP8Header.GetVP8Header(triggerRTPPacket.Payload);

                        if (sampleCount < 1000)
                        {
                            sw.WriteLine(triggerRTPPacket.Header.Timestamp + "," + triggerRTPPacket.Header.MarkerBit + "," + Convert.ToBase64String(triggerRTPPacket.Payload));

                            //if (triggerRTPPacket.Header.MarkerBit == 1 && vp8Header.StartOfVP8Partition == true)
                            //{
                            //    // This is a single packet frame.
                            //    sw.WriteLine(Convert.ToBase64String(vp8Header.GetBytes()) + "," + Convert.ToBase64String(triggerRTPPacket.Payload, vp8Header.Length, triggerRTPPacket.Payload.Length - vp8Header.Length));
                            //}
                            //else if (vp8Header.StartOfVP8Partition == true)
                            //{
                            //    // This is a first packet in a multi-packet frame.
                            //    sw.Write(Convert.ToBase64String(vp8Header.GetBytes()) + ",");
                            //    Buffer.BlockCopy(triggerRTPPacket.Payload, vp8Header.Length, frame, 0, triggerRTPPacket.Payload.Length - vp8Header.Length);
                            //    framePosition = triggerRTPPacket.Payload.Length - vp8Header.Length;
                            //}
                            //else if (triggerRTPPacket.Header.MarkerBit == 1)
                            //{
                            //    // This is the last continuation frame.
                            //    Buffer.BlockCopy(triggerRTPPacket.Payload, vp8Header.Length, frame, framePosition, triggerRTPPacket.Payload.Length - vp8Header.Length);
                            //    framePosition += triggerRTPPacket.Payload.Length - vp8Header.Length;
                            //    sw.WriteLine(Convert.ToBase64String(frame, 0, framePosition));
                            //    framePosition = 0;
                            //}
                            //else
                            //{
                            //    // This is a middle continuation packet
                            //    Buffer.BlockCopy(triggerRTPPacket.Payload, vp8Header.Length, frame, framePosition, triggerRTPPacket.Payload.Length - vp8Header.Length);
                            //    framePosition += triggerRTPPacket.Payload.Length - vp8Header.Length;
                            //}

                            sampleCount++;

                            if (sampleCount == 1000)
                            {
                                Console.WriteLine("Sample collection complete.");
                                sw.Close();
                            }
                        }

                        lock (_webRtcPeers)
                        {
                            foreach (var client in _webRtcPeers.Where(x => x.StunExchangeComplete))
                            {
                                try
                                {
                                    if (client.LastTimestamp == 0)
                                    {
                                        client.LastTimestamp = RTSPSession.DateTimeToNptTimestamp32(DateTime.Now);
                                    }
                                    else if (vp8Header.StartOfVP8Partition)
                                    {
                                        client.LastTimestamp += 11520;
                                    }

                                    RTPPacket rtpPacket = new RTPPacket(triggerRTPPacket.Payload.Length + SRTP_AUTH_KEY_LENGTH);
                                    rtpPacket.Header.SyncSource = client.SSRC;
                                    rtpPacket.Header.SequenceNumber = client.SequenceNumber++;
                                    rtpPacket.Header.Timestamp = client.LastTimestamp; //triggerRTPPacket.Header.Timestamp; // client.LastTimestamp;
                                    rtpPacket.Header.MarkerBit = triggerRTPPacket.Header.MarkerBit;
                                    rtpPacket.Header.PayloadType = 100;

                                    Buffer.BlockCopy(triggerRTPPacket.Payload, 0, rtpPacket.Payload, 0, triggerRTPPacket.Payload.Length);

                                    var rtpBuffer = rtpPacket.GetBytes();

                                    //_webRTCReceiverClient.Send(rtpBuffer, rtpBuffer.Length - SRTP_AUTH_KEY_LENGTH, _wiresharpEP);

                                    if (vp8Header.IsKeyFrame)
                                    {
                                        Console.WriteLine("key frame.");
                                    }

                                    //int rtperr = _newRTPReceiverSRTP.ProtectRTP(rtpBuffer, rtpBuffer.Length - 10);
                                    int rtperr = client.SrtpContext.ProtectRTP(rtpBuffer, rtpBuffer.Length - SRTP_AUTH_KEY_LENGTH);
                                    if (rtperr != 0)
                                    {
                                        logger.Debug("New RTP packet protect result " + rtperr + ".");
                                    }

                                    // logger.Debug("Sending RTP " + rtpBuffer.Length + " bytes to " + client.SocketAddress + ", timestamp " + rtpPacket.Header.Timestamp + ", trigger timestamp " + triggerRTPPacket.Header.Timestamp + ", marker bit " + rtpPacket.Header.MarkerBit + ".");
                                    // _webRTCReceiverClient.Send(rtpBuffer, rtpBuffer.Length, client.SocketAddress);
                                }
                                catch (Exception sendExcp)
                                {
                                    // logger.Error("RelayRTP exception sending to " + client.SocketAddress + ". " + sendExcp.Message);
                                }
                            }
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

        private static void SendRTPFromCamera()
        {
            try
            {
                unsafe
                {
                    SIPSorceryMedia.MFVideoSampler videoSampler = new SIPSorceryMedia.MFVideoSampler();

                    //List<VideoMode> webcamModes = new List<VideoMode>();
                    //int deviceCount = videoSampler.GetVideoDevices(ref webcamModes);
                    //foreach (var videoMode in webcamModes)
                    //{
                    //    Console.WriteLine(videoMode.DeviceFriendlyName + " " + (videoMode.VideoSubTypeFriendlyName ?? videoMode.VideoSubType.ToString()) + " " + videoMode.Width + "x" + videoMode.Height + ".");
                    //}

                    videoSampler.Init(_webcamIndex, _webcamVideoSubType, _webcamWidth, _webcamHeight);

                    SIPSorceryMedia.VPXEncoder vpxEncoder = new VPXEncoder();
                    vpxEncoder.InitEncoder(_webcamWidth, _webcamHeight);

                    SIPSorceryMedia.ImageConvert colorConverter = new ImageConvert();

                    byte pictureID = 0x1;
                    byte[] sampleBuffer = null;
                    byte[] encodedBuffer = new byte[4096];

                    while (true)
                    {
                        if (_webRtcPeers.Any(x => x.StunExchangeComplete == true && x.IsDtlsNegotiationComplete == true))
                        {
                            int result = videoSampler.GetSample(ref sampleBuffer);
                            if (result != 0)
                            {
                                Console.WriteLine("Video sampler returned a null sample.");
                            }
                            else
                            {
                                //Console.WriteLine("Got managed sample " + sample.Buffer.Length + ", is key frame " + sample.IsKeyFrame + ".");

                                fixed (byte* p = sampleBuffer)
                                {
                                    byte[] convertedFrame = null;
                                    //colorConverter.ConvertToI420(p, _webcamVideoSubType, Convert.ToInt32(_webcamWidth), Convert.ToInt32(_webcamHeight), ref convertedFrame);
                                    colorConverter.ConvertRGBtoYUV(p, _webcamVideoSubType, Convert.ToInt32(_webcamWidth), Convert.ToInt32(_webcamHeight), VideoSubTypesEnum.I420, ref convertedFrame);

                                    //int encodeResult = vpxEncoder.Encode(p, sampleBuffer.Length, 1, ref encodedBuffer);
                                    fixed (byte* q = convertedFrame)
                                    {
                                        int encodeResult = vpxEncoder.Encode(q, sampleBuffer.Length, 1, ref encodedBuffer);

                                        if (encodeResult != 0)
                                        {
                                            Console.WriteLine("VPX encode of video sample failed.");
                                            continue;
                                        }
                                    }
                                }

                                lock (_webRtcPeers)
                                {
                                    foreach (var client in _webRtcPeers.Where(x => x.StunExchangeComplete && x.IsDtlsNegotiationComplete == true))
                                    {
                                        try
                                        {
                                            //if (client.LastRtcpSenderReportSentAt == DateTime.MinValue)
                                            //{
                                            //    logger.Debug("Sending RTCP report to " + client.SocketAddress + ".");

                                            //    // Send RTCP report.
                                            //    RTCPPacket rtcp = new RTCPPacket(client.SSRC, 0, 0, 0, 0);
                                            //    byte[] rtcpBuffer = rtcp.GetBytes();
                                            //    _webRTCReceiverClient.BeginSend(rtcpBuffer, rtcpBuffer.Length, client.SocketAddress, null, null);
                                            //    //int rtperr = client.SrtpContext.ProtectRTP(rtcpBuffer, rtcpBuffer.Length - SRTP_AUTH_KEY_LENGTH);
                                            //}

                                            //Console.WriteLine("Sending VP8 frame of " + encodedBuffer.Length + " bytes to " + client.SocketAddress + ".");

                                            client.LastTimestamp = (client.LastTimestamp == 0) ? RTSPSession.DateTimeToNptTimestamp32(DateTime.Now) : client.LastTimestamp + TIMESTAMP_SPACING;

                                            for (int index = 0; index * RTP_MAX_PAYLOAD < encodedBuffer.Length; index++)
                                            {
                                                int offset = (index == 0) ? 0 : (index * RTP_MAX_PAYLOAD);
                                                int payloadLength = (offset + RTP_MAX_PAYLOAD < encodedBuffer.Length) ? RTP_MAX_PAYLOAD : encodedBuffer.Length - offset;

                                                byte[] vp8HeaderBytes = (index == 0) ? new byte[] { 0x10 } : new byte[] { 0x00 };

                                                RTPPacket rtpPacket = new RTPPacket(payloadLength + SRTP_AUTH_KEY_LENGTH + vp8HeaderBytes.Length);
                                                rtpPacket.Header.SyncSource = client.SSRC;
                                                rtpPacket.Header.SequenceNumber = client.SequenceNumber++;
                                                rtpPacket.Header.Timestamp = client.LastTimestamp;
                                                rtpPacket.Header.MarkerBit = ((offset + payloadLength) >= encodedBuffer.Length) ? 1 : 0; // Set marker bit for the last packet in the frame.
                                                rtpPacket.Header.PayloadType = PAYLOAD_TYPE_ID;

                                                Buffer.BlockCopy(vp8HeaderBytes, 0, rtpPacket.Payload, 0, vp8HeaderBytes.Length);
                                                Buffer.BlockCopy(encodedBuffer, offset, rtpPacket.Payload, vp8HeaderBytes.Length, payloadLength);

                                                var rtpBuffer = rtpPacket.GetBytes();

                                                //_webRTCReceiverClient.Send(rtpBuffer, rtpBuffer.Length, _wiresharpEP);

                                                int rtperr = client.SrtpContext.ProtectRTP(rtpBuffer, rtpBuffer.Length - SRTP_AUTH_KEY_LENGTH);
                                                if (rtperr != 0)
                                                {
                                                    logger.Warn("SRTP packet protection failed, result " + rtperr + ".");
                                                }
                                                else
                                                {
                                                    //logger.Debug("Sending RTP, offset " + offset + ", frame bytes " + payloadLength + ", vp8 header bytes " + vp8HeaderBytes.Length + ", timestamp " + rtpPacket.Header.Timestamp + ", seq # " + rtpPacket.Header.SequenceNumber + " to " + client.SocketAddress + ".");

                                                    //_webRTCReceiverClient.Send(rtpBuffer, rtpBuffer.Length, client.SocketAddress);

                                                    //_webRTCReceiverClient.BeginSend(rtpBuffer, rtpBuffer.Length, client.SocketAddress, null, null);
                                                }
                                            }
                                        }
                                        catch (Exception sendExcp)
                                        {
                                            //logger.Error("SendRTP exception sending to " + client.SocketAddress + ". " + sendExcp.Message);
                                        }
                                    }
                                }

                                pictureID++;

                                if (pictureID > 127)
                                {
                                    pictureID = 1;
                                }

                                encodedBuffer = null;
                                sampleBuffer = null;
                            }
                        }
                    }
                }
            }
            catch (Exception excp)
            {
                Console.WriteLine("Exception SendRTP. " + excp);
            }
        }

        private static void SendRTPFromRawRTPFile(string file)
        {
            try
            {
                StreamReader sr = new StreamReader(file);
                List<string> samples = new List<string>();
                while (!sr.EndOfStream)
                {
                    samples.Add(sr.ReadLine());
                }
                sr.Close();
                logger.Debug(samples.Count + " encoded samples loaded.");

                //_newRTPReceiverSRTP = new SRTPManaged(Convert.FromBase64String(_sourceSRTPKey));
                //_newRTPReceiverSRTP = new SRTPManaged();
                int sampleIndex = 0;

                while (true)
                {
                    if (_webRtcPeers.Count != 0)
                    {
                        var sampleItem = samples[sampleIndex];
                        string[] sampleFields = sampleItem.Split(',');

                        uint timestamp = Convert.ToUInt32(sampleFields[0]);
                        int markerBit = Convert.ToInt32(sampleFields[1]);
                        byte[] sample = Convert.FromBase64String(sampleFields[2]);

                        lock (_webRtcPeers)
                        {
                            foreach (var client in _webRtcPeers.Where(x => x.StunExchangeComplete))
                            {
                                try
                                {
                                    if (client.LastTimestamp == 0)
                                    {
                                        client.LastTimestamp = RTSPSession.DateTimeToNptTimestamp32(DateTime.Now);
                                    }

                                    //for (int index = 0; index * RTP_MAX_PAYLOAD < sample.Length; index++)
                                    //{
                                    //    int offset = (index == 0) ? 0 : (index * RTP_MAX_PAYLOAD) - 1;
                                    //    int payloadLength = (offset + RTP_MAX_PAYLOAD < sample.Length - 1) ? RTP_MAX_PAYLOAD : sample.Length - 1 - offset;

                                    RTPPacket rtpPacket = new RTPPacket(sample.Length + SRTP_AUTH_KEY_LENGTH);
                                    rtpPacket.Header.SyncSource = client.SSRC;
                                    rtpPacket.Header.SequenceNumber = client.SequenceNumber++;
                                    rtpPacket.Header.Timestamp = client.LastTimestamp;
                                    rtpPacket.Header.MarkerBit = markerBit;
                                    rtpPacket.Header.PayloadType = 100;

                                    //if (offset + RTP_MAX_PAYLOAD > sample.Length - 1)
                                    //{
                                    //     Last packet in the frame.
                                    //    rtpPacket.Header.MarkerBit = 1;
                                    //}

                                    Buffer.BlockCopy(sample, 0, rtpPacket.Payload, 0, sample.Length);

                                    var rtpBuffer = rtpPacket.GetBytes();
                                    int rtperr = client.SrtpContext.ProtectRTP(rtpBuffer, rtpBuffer.Length - SRTP_AUTH_KEY_LENGTH);
                                    if (rtperr != 0)
                                    {
                                        logger.Debug("New RTP packet protect result " + rtperr + ".");
                                    }

                                    //logger.Debug("Sending RTP " + sample.Length + " bytes to " + client.SocketAddress + ", timestamp " + client.LastTimestamp + ", marker " + rtpPacket.Header.MarkerBit + ".");

                                    //_webRTCReceiverClient.Send(rtpBuffer, rtpBuffer.Length, client.SocketAddress);

                                    if (markerBit == 1)
                                    {
                                        client.LastTimestamp += TIMESTAMP_SPACING;
                                    }
                                    //}
                                }
                                catch (Exception sendExcp)
                                {
                                    // logger.Error("SendRTPFromFile exception sending to " + client.SocketAddress + ". " + sendExcp.Message);
                                }
                            }
                        }

                        sampleIndex++;
                        if (sampleIndex >= samples.Count - 1)
                        {
                            sampleIndex = 0;
                        }

                        //Thread.Sleep(30);
                    }
                }
            }
            catch (Exception excp)
            {
                Console.WriteLine("Exception SendRTPFromFile. " + excp);
            }
        }

        private static void SendRTPFromRawRTPFileNewVP8Header(string file)
        {
            try
            {
                StreamReader sr = new StreamReader(file);
                List<string> samples = new List<string>();
                while (!sr.EndOfStream)
                {
                    samples.Add(sr.ReadLine());
                }
                sr.Close();
                logger.Debug(samples.Count + " encoded samples loaded.");

                //_newRTPReceiverSRTP = new SRTPManaged(Convert.FromBase64String(_sourceSRTPKey));
                //_newRTPReceiverSRTP = new SRTPManaged();
                int sampleIndex = 0;

                while (true)
                {
                    if (_webRtcPeers.Count != 0)
                    {
                        var sampleItem = samples[sampleIndex];
                        string[] sampleFields = sampleItem.Split(',');

                        //uint timestamp = Convert.ToUInt32(sampleFields[0]);
                        int markerBit = Convert.ToInt32(sampleFields[1]);
                        byte[] sample = Convert.FromBase64String(sampleFields[2]);

                        lock (_webRtcPeers)
                        {
                            foreach (var client in _webRtcPeers.Where(x => x.StunExchangeComplete))
                            {
                                try
                                {
                                    if (client.LastTimestamp == 0)
                                    {
                                        client.LastTimestamp = RTSPSession.DateTimeToNptTimestamp32(DateTime.Now);
                                    }

                                    RTPVP8Header origVP8Header = RTPVP8Header.GetVP8Header(sample);

                                    if (origVP8Header.IsKeyFrame)
                                    {
                                        Console.WriteLine("Key frame");
                                    }

                                    RTPPacket rtpPacket = new RTPPacket(sample.Length + SRTP_AUTH_KEY_LENGTH);
                                    rtpPacket.Header.SyncSource = client.SSRC;
                                    rtpPacket.Header.SequenceNumber = client.SequenceNumber++;
                                    rtpPacket.Header.Timestamp = client.LastTimestamp;
                                    rtpPacket.Header.MarkerBit = markerBit;
                                    rtpPacket.Header.PayloadType = 100;

                                    if (origVP8Header.StartOfVP8Partition && markerBit == 1)
                                    {
                                        Console.WriteLine("My VP8 Header    : " + BitConverter.ToString(origVP8Header.GetBytes()) + ".");
                                        Console.WriteLine("Sample VP8 Header: " + BitConverter.ToString(sample, 0, 6) + ".");

                                        Buffer.BlockCopy(origVP8Header.GetBytes(), 0, rtpPacket.Payload, 0, origVP8Header.Length);
                                        Buffer.BlockCopy(sample, origVP8Header.Length, rtpPacket.Payload, origVP8Header.Length, sample.Length - origVP8Header.Length);
                                    }
                                    else
                                    {
                                        Buffer.BlockCopy(sample, 0, rtpPacket.Payload, 0, sample.Length);
                                    }

                                    var rtpBuffer = rtpPacket.GetBytes();

                                    //_webRTCReceiverClient.Send(rtpBuffer, rtpBuffer.Length - SRTP_AUTH_KEY_LENGTH, _wiresharpEP);

                                    int rtperr = client.SrtpContext.ProtectRTP(rtpBuffer, rtpBuffer.Length - SRTP_AUTH_KEY_LENGTH);
                                    if (rtperr != 0)
                                    {
                                        logger.Debug("New RTP packet protect result " + rtperr + ".");
                                    }

                                    //logger.Debug("Sending RTP " + sample.Length + " bytes to " + client.SocketAddress + ", timestamp " + client.LastTimestamp + ", marker " + rtpPacket.Header.MarkerBit + ".");

                                    //_webRTCReceiverClient.Send(rtpBuffer, rtpBuffer.Length, client.SocketAddress);

                                    if (markerBit == 1)
                                    {
                                        client.LastTimestamp += TIMESTAMP_SPACING;
                                    }
                                    //}
                                }
                                catch (Exception sendExcp)
                                {
                                    //logger.Error("SendRTPFromFile exception sending to " + client.SocketAddress + ". " + sendExcp.Message);
                                }
                            }
                        }

                        sampleIndex++;
                        if (sampleIndex >= samples.Count - 1)
                        {
                            sampleIndex = 0;
                        }

                        //Thread.Sleep(30);
                    }
                }
            }
            catch (Exception excp)
            {
                Console.WriteLine("Exception SendRTPFromFile. " + excp);
            }
        }

        private static void SendRTPFromVP8FramesFile(string file)
        {
            try
            {
                StreamReader sr = new StreamReader(file);
                List<string> samples = new List<string>();
                while (!sr.EndOfStream)
                {
                    string sample = sr.ReadLine();
                    samples.Add(sample);

                    //Console.WriteLine(sample);

                    //string[] sampleFields = sample.Split(',');
                    //RTPVP8Header frameVP8Header = RTPVP8Header.GetVP8Header(Convert.FromBase64String(sampleFields[0]));
                    //byte[] rtpPaylaod = Convert.FromBase64String(sampleFields[1]);

                    //Console.WriteLine((frameVP8Header.IsKeyFrame) ? "K" : "." + " " + frameVP8Header.FirstPartitionSize + " " + rtpPaylaod.Length + ".");
                }
                sr.Close();
                logger.Debug(samples.Count + " encoded samples loaded.");

                //_newRTPReceiverSRTP = new SRTPManaged(Convert.FromBase64String(_sourceSRTPKey));
                //_newRTPReceiverSRTP = new SRTPManaged();
                int sampleIndex = 0;

                while (true)
                {
                    if (_webRtcPeers.Count != 0)
                    {
                        var sampleItem = samples[sampleIndex];
                        string[] sampleFields = sampleItem.Split(',');

                        RTPVP8Header frameVP8Header = RTPVP8Header.GetVP8Header(Convert.FromBase64String(sampleFields[0]));
                        byte[] sample = Convert.FromBase64String(sampleFields[1]);

                        if (frameVP8Header.IsKeyFrame)
                        {
                            Console.WriteLine("Key frame.");
                        }

                        lock (_webRtcPeers)
                        {
                            foreach (var client in _webRtcPeers.Where(x => x.StunExchangeComplete))
                            {
                                try
                                {
                                    if (client.LastTimestamp == 0)
                                    {
                                        client.LastTimestamp = RTSPSession.DateTimeToNptTimestamp32(DateTime.Now);
                                    }

                                    for (int index = 0; index * RTP_MAX_PAYLOAD < sample.Length; index++)
                                    {
                                        int offset = (index == 0) ? 0 : (index * RTP_MAX_PAYLOAD);
                                        int payloadLength = (offset + RTP_MAX_PAYLOAD < sample.Length) ? RTP_MAX_PAYLOAD : sample.Length - offset;

                                        RTPVP8Header packetVP8Header = new RTPVP8Header()
                                        {
                                            ExtendedControlBitsPresent = true,
                                            IsPictureIDPresent = true,
                                            ShowFrame = true,
                                        };

                                        if (index == 0)
                                        {
                                            packetVP8Header.StartOfVP8Partition = true;
                                            //packetVP8Header.FirstPartitionSize = frameVP8Header.FirstPartitionSize;
                                            packetVP8Header.IsKeyFrame = frameVP8Header.IsKeyFrame;
                                            packetVP8Header.PictureID = (frameVP8Header.IsKeyFrame) ? (byte)0x00 : frameVP8Header.PictureID;
                                        }

                                        byte[] vp8HeaderBytes = packetVP8Header.GetBytes();

                                        RTPPacket rtpPacket = new RTPPacket(packetVP8Header.Length + payloadLength + SRTP_AUTH_KEY_LENGTH);
                                        rtpPacket.Header.SyncSource = client.SSRC;
                                        rtpPacket.Header.SequenceNumber = client.SequenceNumber++;
                                        rtpPacket.Header.Timestamp = client.LastTimestamp;
                                        rtpPacket.Header.MarkerBit = ((offset + payloadLength) >= sample.Length) ? 1 : 0;
                                        rtpPacket.Header.PayloadType = 100;

                                        Buffer.BlockCopy(vp8HeaderBytes, 0, rtpPacket.Payload, 0, packetVP8Header.Length);
                                        Buffer.BlockCopy(sample, offset, rtpPacket.Payload, packetVP8Header.Length, payloadLength);

                                        var rtpBuffer = rtpPacket.GetBytes();

                                        //_webRTCReceiverClient.Send(rtpBuffer, rtpBuffer.Length - SRTP_AUTH_KEY_LENGTH, _wiresharpEP);

                                        int rtperr = client.SrtpContext.ProtectRTP(rtpBuffer, rtpBuffer.Length - SRTP_AUTH_KEY_LENGTH);
                                        if (rtperr != 0)
                                        {
                                            logger.Debug("New RTP packet protect result " + rtperr + ".");
                                        }

                                        // logger.Debug("Sending RTP " + sample.Length + " bytes to " + client.SocketAddress + ", timestamp " + client.LastTimestamp + ", marker " + rtpPacket.Header.MarkerBit + ".");

                                        //_webRTCReceiverClient.Send(rtpBuffer, rtpBuffer.Length, client.SocketAddress);
                                    }

                                    client.LastTimestamp += TIMESTAMP_SPACING;
                                }
                                catch (Exception sendExcp)
                                {
                                    //logger.Error("SendRTPFromVP8FramesFile exception sending to " + client.SocketAddress + ". " + sendExcp.Message);
                                }
                            }
                        }

                        sampleIndex++;
                        if (sampleIndex >= samples.Count - 1)
                        {
                            sampleIndex = 0;
                        }

                        Thread.Sleep(30);
                    }
                }
            }
            catch (Exception excp)
            {
                Console.WriteLine("Exception SendRTPFromVP8FramesFile. " + excp);
            }
        }

        //private static void CaptureVP8SamplesToFile(string filename, int sampleCount)
        //{
        //    SIPSorceryMedia.VideoSampler videoSampler = new SIPSorceryMedia.VideoSampler();
        //    videoSampler.Init();

        //    int samples = 0;

        //    using (StreamWriter sw = new StreamWriter(filename))
        //    {
        //        while (samples < sampleCount)
        //        {
        //            var sample = videoSampler.GetSample();
        //            if (sample == null)
        //            {
        //                Console.WriteLine("Video sampler returned a null sample.");
        //            }
        //            else
        //            {
        //                sw.WriteLine(Convert.ToBase64String(sample.Buffer));
        //                Console.WriteLine(samples + " samples capture.");
        //            }

        //            samples++;
        //        }
        //    }

        //    Console.WriteLine("Sample capture complete.");
        //}

        private static void SendTestPattern()
        {
            try
            {
                unsafe
                {
                    SIPSorceryMedia.VPXEncoder vpxEncoder = new VPXEncoder();
                    vpxEncoder.InitEncoder(_webcamWidth, _webcamHeight);

                    SIPSorceryMedia.ImageConvert colorConverter = new ImageConvert();

                    Bitmap testPattern = new Bitmap("testpattern.jpeg");

                    byte pictureID = 0x1;
                    byte[] sampleBuffer = null;
                    byte[] encodedBuffer = new byte[4096];

                    while (true)
                    {
                        //if (_webRtcPeers.Any(x => x.STUNExchangeComplete == true && x.IsDtlsNegotiationComplete == true))
                        if (_webRtcPeers.Any(x => x.IsDtlsNegotiationComplete == true))
                        {
                            //Console.WriteLine("Got managed sample " + sample.Buffer.Length + ", is key frame " + sample.IsKeyFrame + ".");

                            var stampedTestPattern = testPattern.Clone() as System.Drawing.Image;

                            AddTimeStampAndLocation(stampedTestPattern, DateTime.UtcNow.ToString("dd MMM yyyy HH:mm:ss:fff"), "Test Pattern");

                            //Bitmap bitmap = new Bitmap(timestampedTestPattern as System.Drawing.Bitmap);

                            sampleBuffer = BitmapToRGB24(stampedTestPattern as System.Drawing.Bitmap);

                            fixed (byte* p = sampleBuffer)
                            {
                                byte[] convertedFrame = null;
                                //colorConverter.ConvertToI420(p, VideoSubTypesEnum.RGB24, testPattern.Width, testPattern.Height, ref convertedFrame);
                                colorConverter.ConvertRGBtoYUV(p, VideoSubTypesEnum.RGB24, testPattern.Width, testPattern.Height, VideoSubTypesEnum.I420, ref convertedFrame);

                                //int encodeResult = vpxEncoder.Encode(p, sampleBuffer.Length, 1, ref encodedBuffer);
                                fixed (byte* q = convertedFrame)
                                {
                                    int encodeResult = vpxEncoder.Encode(q, sampleBuffer.Length, 1, ref encodedBuffer);

                                    if (encodeResult != 0)
                                    {
                                        Console.WriteLine("VPX encode of video sample failed.");
                                        continue;
                                    }
                                }
                            }

                            stampedTestPattern.Dispose();
                            stampedTestPattern = null;

                            lock (_webRtcPeers)
                            {
                                //foreach (var client in _webRtcPeers.Where(x => x.STUNExchangeComplete && x.IsDtlsNegotiationComplete == true))
                                foreach (var client in _webRtcPeers.Where(x => x.IsDtlsNegotiationComplete == true && x.LocalIceCandidates.Any(y => y.RemoteRtpEndPoint != null)))
                                {
                                    try
                                    {
                                        //if (client.LastRtcpSenderReportSentAt == DateTime.MinValue)
                                        //{
                                        //    logger.Debug("Sending RTCP report to " + client.SocketAddress + ".");

                                        //    // Send RTCP report.
                                        //    RTCPPacket rtcp = new RTCPPacket(client.SSRC, 0, 0, 0, 0);
                                        //    byte[] rtcpBuffer = rtcp.GetBytes();
                                        //    _webRTCReceiverClient.BeginSend(rtcpBuffer, rtcpBuffer.Length, client.SocketAddress, null, null);
                                        //    //int rtperr = client.SrtpContext.ProtectRTP(rtcpBuffer, rtcpBuffer.Length - SRTP_AUTH_KEY_LENGTH);
                                        //}

                                        //Console.WriteLine("Sending VP8 frame of " + encodedBuffer.Length + " bytes to " + client.SocketAddress + ".");

                                        client.LastTimestamp = (client.LastTimestamp == 0) ? RTSPSession.DateTimeToNptTimestamp32(DateTime.Now) : client.LastTimestamp + TIMESTAMP_SPACING;

                                        for (int index = 0; index * RTP_MAX_PAYLOAD < encodedBuffer.Length; index++)
                                        {
                                            int offset = (index == 0) ? 0 : (index * RTP_MAX_PAYLOAD);
                                            int payloadLength = (offset + RTP_MAX_PAYLOAD < encodedBuffer.Length) ? RTP_MAX_PAYLOAD : encodedBuffer.Length - offset;

                                            byte[] vp8HeaderBytes = (index == 0) ? new byte[] { 0x10 } : new byte[] { 0x00 };

                                            RTPPacket rtpPacket = new RTPPacket(payloadLength + SRTP_AUTH_KEY_LENGTH + vp8HeaderBytes.Length);
                                            rtpPacket.Header.SyncSource = client.SSRC;
                                            rtpPacket.Header.SequenceNumber = client.SequenceNumber++;
                                            rtpPacket.Header.Timestamp = client.LastTimestamp;
                                            rtpPacket.Header.MarkerBit = ((offset + payloadLength) >= encodedBuffer.Length) ? 1 : 0; // Set marker bit for the last packet in the frame.
                                            rtpPacket.Header.PayloadType = PAYLOAD_TYPE_ID;

                                            Buffer.BlockCopy(vp8HeaderBytes, 0, rtpPacket.Payload, 0, vp8HeaderBytes.Length);
                                            Buffer.BlockCopy(encodedBuffer, offset, rtpPacket.Payload, vp8HeaderBytes.Length, payloadLength);

                                            var rtpBuffer = rtpPacket.GetBytes();

                                            //_webRTCReceiverClient.Send(rtpBuffer, rtpBuffer.Length, _wiresharpEP);

                                            int rtperr = client.SrtpContext.ProtectRTP(rtpBuffer, rtpBuffer.Length - SRTP_AUTH_KEY_LENGTH);
                                            if (rtperr != 0)
                                            {
                                                logger.Warn("SRTP packet protection failed, result " + rtperr + ".");
                                            }
                                            else
                                            {
                                                var connectedIceCandidate = client.LocalIceCandidates.Where(y => y.RemoteRtpEndPoint != null).First();

                                                //logger.Debug("Sending RTP, offset " + offset + ", frame bytes " + payloadLength + ", timestamp " + rtpPacket.Header.Timestamp + ", seq # " + rtpPacket.Header.SequenceNumber + " to " + connectedIceCandidate.RemoteRtpEndPoint + ".");

                                                //_webRTCReceiverClient.Send(rtpBuffer, rtpBuffer.Length, client.SocketAddress);

                                                //UdpClient localSocket = new UdpClient();
                                                //localSocket.Client = client.LocalIceCandidates.First().RtpSocket;
                                                //localSocket.BeginSend(rtpBuffer, rtpBuffer.Length, client.SocketAddress, null, null);


                                                connectedIceCandidate.LocalRtpSocket.SendTo(rtpBuffer, connectedIceCandidate.RemoteRtpEndPoint);
                                            }
                                        }
                                    }
                                    catch (Exception sendExcp)
                                    {
                                        // logger.Error("SendRTP exception sending to " + client.SocketAddress + ". " + sendExcp.Message);
                                    }
                                }
                            }

                            pictureID++;

                            if (pictureID > 127)
                            {
                                pictureID = 1;
                            }

                            encodedBuffer = null;
                            //sampleBuffer = null;
                        }
                    }
                }
            }
            catch (Exception excp)
            {
                Console.WriteLine("Exception SendRTP. " + excp);
            }
        }

        private static byte[] BitmapToRGB24(Bitmap bitmap)
        {
            try
            {
                BitmapData bitmapData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadWrite, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                var length = bitmapData.Stride * bitmapData.Height;

                byte[] bytes = new byte[length];

                // Copy bitmap to byte[]
                Marshal.Copy(bitmapData.Scan0, bytes, 0, length);
                bitmap.UnlockBits(bitmapData);

                return bytes;
            }
            catch (Exception)
            {
                return new byte[0];
            }
        }

        private static void AddTimeStampAndLocation(System.Drawing.Image image, string timeStamp, string locationText)
        {
            int pixelHeight = (int)(image.Height * TEXT_SIZE_PERCENTAGE);

            Graphics g = Graphics.FromImage(image);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            using (StringFormat format = new StringFormat())
            {
                format.LineAlignment = StringAlignment.Center;
                format.Alignment = StringAlignment.Center;

                using (Font f = new Font("Tahoma", pixelHeight, GraphicsUnit.Pixel))
                {
                    //// Draw a 'shadow' so the text will be visible on light and on dark images
                    //g.DrawString(timeStamp, _timestampFont, Brushes.Black, new Rectangle(0, image.Height - (TIMESTAMP_HEIGHT + 2), image.Width - 2, TIMESTAMP_HEIGHT), format);
                    //// Actual text
                    //g.DrawString(timeStamp, _timestampFont, Brushes.White, new Rectangle(2, image.Height - TIMESTAMP_HEIGHT, image.Width, TIMESTAMP_HEIGHT), format );
                    using (var gPath = new GraphicsPath())
                    {
                        float emSize = g.DpiY * f.Size / POINTS_PER_INCH;
                        if (locationText != null)
                        {
                            gPath.AddString(locationText, f.FontFamily, (int)FontStyle.Bold, emSize, new Rectangle(0, TEXT_MARGIN_PIXELS, image.Width, pixelHeight), format);
                        }

                        gPath.AddString(timeStamp /* + " -- " + fps.ToString("0.00") + " fps" */, f.FontFamily, (int)FontStyle.Bold, emSize, new Rectangle(0, image.Height - (pixelHeight + TEXT_MARGIN_PIXELS), image.Width, pixelHeight), format);
                        g.FillPath(Brushes.White, gPath);
                        g.DrawPath(new Pen(Brushes.Black, pixelHeight * TEXT_OUTLINE_REL_THICKNESS), gPath);
                    }
                }
            }
        }

        private static string SASLPrep(string password)
        {
            byte[] encode = Encoding.UTF8.GetBytes(password);
            return Convert.ToBase64String(encode, 0, encode.Length);
        }
    }

    public class SDPExchangeReceiver : WebSocketBehavior
    {
        public static event Action<WebSocketSharp.Net.WebSockets.WebSocketContext, string> WebSocketOpened;
        public static event Action<string, string> SDPAnswerReceived;

        protected override void OnMessage(MessageEventArgs e)
        {
            SDPAnswerReceived(this.ID, e.Data);
        }

        protected override void OnOpen()
        {
            base.OnOpen();
            WebSocketOpened(this.Context, this.ID);
        }
    }
}
