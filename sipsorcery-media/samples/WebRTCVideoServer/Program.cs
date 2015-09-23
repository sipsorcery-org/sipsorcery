// openssl x509 -fingerprint -sha256 -in server-cert.pem 

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
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
    class WebRTCClient
    {
        public IPEndPoint SocketAddress;
        public string ICEUser;
        public string ICEPassword;
        public bool STUNExchangeComplete;
        public bool IsDtlsNegotiationComplete;
        public DateTime LastSTUNSendAt;
        public DateTime LastSTUNReceiveAt;
        public uint SSRC;
        public ushort SequenceNumber;
        public uint LastTimestamp;
        public DtlsManaged DtlsContext;
        public SRTPManaged SrtpContext;
        public SRTPManaged SrtpReceiveContext;  // Used to decrypt packets received from the remote peer.
        public DateTime LastRtcpSenderReportSentAt = DateTime.MinValue;
    }

    class Program
    {
        private const int WEBRTC_LISTEN_PORT = 49890;
        private const int EXPIRE_CLIENT_SECONDS = 3;
        private const int RTP_MAX_PAYLOAD = 1400; //1452;
        private const int TIMESTAMP_SPACING = 3000;
        private const int PAYLOAD_TYPE_ID = 100;
        private const int SRTP_AUTH_KEY_LENGTH = 10;

        private static ILog logger = AppState.logger;

        private static int _webcamIndex = 0;
        private static uint _webcamWidth = 640;
        private static uint _webcamHeight = 480;
        private static VideoSubTypesEnum _webcamVideoSubType = VideoSubTypesEnum.YUY2; // VideoSubTypesEnum.I420;

        private static bool m_exit = false;

        private static IPEndPoint _wiresharpEP = new IPEndPoint(IPAddress.Parse("10.1.1.1"), 10001);
        private static string _localIPAddress = "192.168.33.117"; //"10.1.1.2";//"192.168.33.116"; //  ;
        private static string _clientIPAddress = "192.168.33.108"; //"10.1.1.2"; // "192.168.33.108"; // ;
        private static UdpClient _webRTCReceiverClient;
        //private static UdpClient _rtpClient;

        //private static string _sourceSRTPKey = "zIN6kIVR4DY5dpc5T2vBDvOC1X9VjPTegBx/6EnQ";
        private static string _senderICEUser = "AoszpFFXN92GdqKc";
        private static string _senderICEPassword = "0csAdt+PHzR3/OepgHBmnPKi";
        private static WebSocketServer _receiverWSS;
        private static ConcurrentBag<WebRTCClient> _webRTCClients = new ConcurrentBag<WebRTCClient>();

//        private static string _sourceSDPOffer = @"v=0
//o=- 2925822133501083390 2 IN IP4 127.0.0.1
//s=-
//t=0 0
//m=video {0} RTP/SAVPF 100
//c=IN IP4 {1}
//a=candidate:0 1 udp 2122194687 {1} {0} typ host
//a=ice-ufrag:{2}
//a=ice-pwd:{3}
//a=mid:video
//a=sendrecv
//a=rtcp-mux
//a=crypto:0 AES_CM_128_HMAC_SHA1_80 inline:{4}
//a=rtpmap:100 VP8/90000
//";

        private static string _sourceSDPOffer = @"v=0
o=- 2925822133501083390 2 IN IP4 127.0.0.1
s=-
t=0 0
m=video {0} RTP/SAVPF " + PAYLOAD_TYPE_ID + @"
c=IN IP4 {1}
a=candidate:0 1 udp 2122194687 {1} {0} typ host
a=ice-ufrag:{2}
a=ice-pwd:{3}
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

                //_sourceSDPOffer = String.Format(_sourceSDPOffer, WEBRTC_LISTEN_PORT.ToString(), _localIPAddress, _senderICEUser, _senderICEPassword, _sourceSRTPKey);
                _sourceSDPOffer = String.Format(_sourceSDPOffer, WEBRTC_LISTEN_PORT.ToString(), _localIPAddress, _senderICEUser, _senderICEPassword);

                SDPExchangeReceiver.SDPAnswerReceived += SDPExchangeReceiver_SDPAnswerReceived;
                SDPExchangeReceiver.WebSocketOpened += SDPExchangeReceiver_WebSocketOpened;

                _receiverWSS = new WebSocketServer(8081, false);
                _receiverWSS.AddWebSocketService<SDPExchangeReceiver>("/stream");
                _receiverWSS.Start();

                //DtlsManaged dtls = new DtlsManaged();
                //int res = dtls.Init();
                //dtls.Dispose();
                //dtls = null;

                //Console.WriteLine("DTLS initialisation result=" + res + ".");

                IPEndPoint receiverLocalEndPoint = new IPEndPoint(IPAddress.Parse(_localIPAddress), WEBRTC_LISTEN_PORT);
                _webRTCReceiverClient = new UdpClient(receiverLocalEndPoint);

                //IPEndPoint rtpLocalEndPoint = new IPEndPoint(IPAddress.Parse(_localIPAddress), 10001);
                //_rtpClient = new UdpClient(rtpLocalEndPoint);

                logger.Debug("Commencing listen to receiver WebRTC client on local socket " + receiverLocalEndPoint + ".");
                ThreadPool.QueueUserWorkItem(delegate { ListenToReceiverWebRTCClient(_webRTCReceiverClient); });
                ThreadPool.QueueUserWorkItem(delegate { SendStunConnectivityChecks(_webRTCReceiverClient); });

                //ThreadPool.QueueUserWorkItem(delegate { RelayRTP(_rtpClient); });

                ThreadPool.QueueUserWorkItem(delegate { SendRTPFromCamera(); });

                //ThreadPool.QueueUserWorkItem(delegate { SendRTPFromRawRTPFile("rtpPackets.txt"); });

                //ThreadPool.QueueUserWorkItem(delegate { SendRTPFromRawRTPFileNewVP8Header("rtpPackets.txt"); });

                //ThreadPool.QueueUserWorkItem(delegate { SendRTPFromVP8FramesFile("framesAndHeaders.txt"); });

                //ThreadPool.QueueUserWorkItem(delegate { ICMPListen(IPAddress.Parse(_localIPAddress)); });

                //ThreadPool.QueueUserWorkItem(delegate { CaptureVP8SamplesToFile("vp8sample", 1000); });

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

                Console.WriteLine(sdpAnswer);

                var answerSDP = SDP.ParseSDPDescription(sdpAnswer);

                //logger.Debug("ICE User: " + _answerSDP.IceUfrag + ".");
                //logger.Debug("ICE Password: " + _answerSDP.IcePwd + ".");
                var matchingCandidate = (from cand in answerSDP.IceCandidates where cand.NetworkAddress == _clientIPAddress select cand).FirstOrDefault();

                if (matchingCandidate != null)
                {
                    logger.Debug("New WebRTC client SDP answer with socket: " + matchingCandidate.NetworkAddress + ":" + matchingCandidate.Port + ".");

                    // Look for an a=ssrc attribute.
                    uint ssrc = 0;
                    string ssrcAttribute = answerSDP.ExtraAttributes.Where(x => x.StartsWith("a=ssrc")).FirstOrDefault();
                    if(ssrcAttribute != null)
                    {
                        var ssrcMatch = Regex.Match(ssrcAttribute, @"^a=ssrc:(?<ssrc>\d+) ");
                        if(ssrcMatch.Success)
                        {
                            if(UInt32.TryParse(ssrcMatch.Result("${ssrc}"), out ssrc))
                            {
                                logger.Debug("ssrc found for SDP answer " + ssrc + ".");
                            }
                        }
                    }

                    // Look for a=crypto attribute.
                    string receiveSrtpKey = null;
                    string cryptoAttribute = answerSDP.ExtraAttributes.Where(x => x.StartsWith("a=crypto")).FirstOrDefault();
                    if (cryptoAttribute != null)
                    {
                        var cryptoMatch = Regex.Match(cryptoAttribute, @"^a=crypto:.*?inline:(?<key>\S+)$");

                        if(cryptoMatch.Success)
                        {
                            receiveSrtpKey = cryptoMatch.Result("${key}");
                        }
                    }

                    var newWebRTCClient = new WebRTCClient()
                    {
                        SocketAddress = new IPEndPoint(IPAddress.Parse(_clientIPAddress), matchingCandidate.Port),
                        ICEUser = answerSDP.IceUfrag,
                        ICEPassword = answerSDP.IcePwd,
                        SSRC = Convert.ToUInt32(Crypto.GetRandomInt(10)),
                        //SSRC = (ssrc != 0) ? ssrc : Convert.ToUInt32(Crypto.GetRandomInt(10)),
                        //SSRC = 2889419400,
                        SequenceNumber = 1,
                        SrtpReceiveContext = (receiveSrtpKey != null) ? new SRTPManaged(Convert.FromBase64String(receiveSrtpKey), false) : null
                    };

                    lock (_webRTCClients)
                    {
                        _webRTCClients.Add(newWebRTCClient);
                    }
                }
                else
                {
                    logger.Warn("No matching media offer was found.");
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

        private static void SendStunConnectivityChecks(UdpClient localSocket)
        {
            try
            {
                while (!m_exit)
                {
                    try
                    {
                        lock (_webRTCClients)
                        {
                            var expiredClients = (from cli in _webRTCClients where cli.STUNExchangeComplete && DateTime.Now.Subtract(cli.LastSTUNReceiveAt).TotalSeconds > EXPIRE_CLIENT_SECONDS select cli).ToList();
                            for (int index = 0; index < expiredClients.Count(); index++)
                            {
                                var expiredClient = expiredClients[index];
                                logger.Debug("Removed expired client " + expiredClient.SocketAddress + ".");
                                _webRTCClients.TryTake(out expiredClient);
                            }
                        }

                        foreach (var client in _webRTCClients)
                        {
                            //logger.Debug("Sending STUN connectivity check to client " + client.SocketAddress + ".");

                            STUNv2Message stunRequest = new STUNv2Message(STUNv2MessageTypesEnum.BindingRequest);
                            stunRequest.Header.TransactionId = Guid.NewGuid().ToByteArray().Take(12).ToArray();
                            stunRequest.AddUsernameAttribute(client.ICEUser + ":" + _senderICEUser);
                            stunRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.Priority, new byte[] { 0x6e, 0x7f, 0x1e, 0xff }));
                            stunRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.UseCandidate, null));   // Must send this to get DTLS started.
                            byte[] stunReqBytes = stunRequest.ToByteBuffer(client.ICEPassword, true);
                            localSocket.Send(stunReqBytes, stunReqBytes.Length, client.SocketAddress);

                            client.LastSTUNSendAt = DateTime.Now;

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
                        logger.Error("Exception SendStunConnectivityCheck ConnectivityCheck. " + excp);
                    }

                    Thread.Sleep(2000);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SendStunConnectivityCheck. " + excp);
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

                        //if (buffer.Length > 3 && buffer[0] == 0x16 && buffer[1] == 0xfe)
                        if ((buffer[0] >= 20) && (buffer[0] <= 64))
                        {
                            logger.Debug("DTLS packet received " + buffer.Length + " bytes from " + remoteEndPoint.ToString() + ".");

                            var client = _webRTCClients.Where(x => x.SocketAddress.ToString() == remoteEndPoint.ToString()).SingleOrDefault();

                            if (client != null)
                            {
                                if (client.DtlsContext == null)
                                {
                                    client.DtlsContext = new DtlsManaged();
                                    int res = client.DtlsContext.Init();
                                    Console.WriteLine("DtlsContext initialisation result=" + res);
                                }

                                int bytesWritten = client.DtlsContext.Write(buffer, buffer.Length);

                                if (bytesWritten != buffer.Length)
                                {
                                    logger.Warn("The required number of bytes were not successfully written to the DTLS context.");
                                }
                                else
                                {
                                    byte[] dtlsOutBytes = new byte[2048];

                                    int bytesRead = client.DtlsContext.Read(dtlsOutBytes, dtlsOutBytes.Length);

                                    if (bytesRead == 0)
                                    {
                                        Console.WriteLine("No bytes read from DTLS context :(.");
                                    }
                                    else
                                    {
                                        Console.WriteLine(bytesRead + " bytes read from DTLS context sending to " + remoteEndPoint.ToString() + ".");
                                        localSocket.Send(dtlsOutBytes, bytesRead, remoteEndPoint);

                                        if (client.DtlsContext.IsHandshakeComplete())
                                        {
                                            Console.WriteLine("DTLS negotiation complete for " + remoteEndPoint.ToString() + ".");
                                            client.SrtpContext = new SRTPManaged(client.DtlsContext, false);
                                            client.SrtpReceiveContext = new SRTPManaged(client.DtlsContext, true);
                                            client.IsDtlsNegotiationComplete = true;
                                        }
                                    }
                                }

                            }
                        }
                        //else if ((buffer[0] & 0x80) == 0)
                        else if ((buffer[0] == 0) || (buffer[0] == 1))
                        {
                            STUNv2Message stunMessage = STUNv2Message.ParseSTUNMessage(buffer, buffer.Length);

                            //logger.Debug("STUN message received from Receiver Client @ " + stunMessage.Header.MessageType + ".");

                            var client = _webRTCClients.Where(x => x.SocketAddress.ToString() == remoteEndPoint.ToString()).SingleOrDefault();

                            if(client != null)
                            {
                                client.LastSTUNReceiveAt = DateTime.Now;
                            }

                            if (stunMessage.Header.MessageType == STUNv2MessageTypesEnum.BindingRequest)
                            {
                                //logger.Debug("Sending STUN response to Receiver Client @ " + remoteEndPoint + ".");

                                STUNv2Message stunResponse = new STUNv2Message(STUNv2MessageTypesEnum.BindingSuccessResponse);
                                stunResponse.Header.TransactionId = stunMessage.Header.TransactionId;
                                stunResponse.AddXORMappedAddressAttribute(remoteEndPoint.Address, remoteEndPoint.Port);
                                byte[] stunRespBytes = stunResponse.ToByteBuffer(_senderICEPassword, true);
                                localSocket.Send(stunRespBytes, stunRespBytes.Length, remoteEndPoint);

                                //logger.Debug("Sending Binding request to Receiver Client @ " + remoteEndPoint + ".");
                                //if (client != null && !client.STUNExchangeComplete)
                                if (client != null)
                                {
                                    //client.SrtpContext = new SRTPManaged(Convert.FromBase64String(_sourceSRTPKey));
                                    //client.IsDtlsNegotiationComplete = true;

                                    //STUNv2Message stunRequest = new STUNv2Message(STUNv2MessageTypesEnum.BindingRequest);
                                    //stunRequest.Header.TransactionId = Guid.NewGuid().ToByteArray().Take(12).ToArray();
                                    //stunRequest.AddUsernameAttribute(client.ICEUser + ":" + _senderICEUser);
                                    //stunRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.Priority, new byte[] { 0x6e, 0x7f, 0x1e, 0xff }));
                                    //stunRequest.Attributes.Add(new STUNv2Attribute(STUNv2AttributeTypesEnum.UseCandidate, null));   // Must send this to get DTLS started.
                                    //byte[] stunReqBytes = stunRequest.ToByteBuffer(client.ICEPassword, true);
                                    //localSocket.Send(stunReqBytes, stunReqBytes.Length, remoteEndPoint);

                                    //client.LastSTUNMessageAt = DateTime.Now;
                                }
                            }
                            else if (stunMessage.Header.MessageType == STUNv2MessageTypesEnum.BindingSuccessResponse)
                            {
                                if (client != null && client.STUNExchangeComplete == false)
                                {
                                    client.STUNExchangeComplete = true;
                                    logger.Debug("WebRTC client STUN exchange complete for " + remoteEndPoint.ToString() + ".");

                                    //client.IsDtlsNegotiationComplete = true; // Not using DTLS in this case.
                                    //client.SrtpContext = new SRTPManaged(Convert.FromBase64String(_sourceSRTPKey), true);
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
                        else if ((buffer[0] >= 128) && (buffer[0] <= 191))
                        {
                            //logger.Debug("A non-STUN packet was received Receiver Client.");

                            var client = _webRTCClients.Where(x => x.SocketAddress.ToString() == remoteEndPoint.ToString()).SingleOrDefault();

                            if(client != null)
                            {
                                if (buffer[1] == 0xC8 /* RTCP SR */ || buffer[1] == 0xC9 /* RTCP RR */)
                                {
                                    // RTCP packet.
                                }
                                else
                                {
                                    // RTP packet.
                                    int res = client.SrtpReceiveContext.UnprotectRTP(buffer, buffer.Length);

                                    if (res != 0)
                                    {
                                        logger.Warn("SRTP unprotect failed, result " + res + ".");
                                    }
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

                    if (_webRTCClients.Count != 0)
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

                        lock (_webRTCClients)
                        {
                            foreach (var client in _webRTCClients.Where(x => x.STUNExchangeComplete))
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

                                    _webRTCReceiverClient.Send(rtpBuffer, rtpBuffer.Length - SRTP_AUTH_KEY_LENGTH, _wiresharpEP);

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

                                    logger.Debug("Sending RTP " + rtpBuffer.Length + " bytes to " + client.SocketAddress + ", timestamp " + rtpPacket.Header.Timestamp + ", trigger timestamp " + triggerRTPPacket.Header.Timestamp + ", marker bit " + rtpPacket.Header.MarkerBit + ".");
                                    _webRTCReceiverClient.Send(rtpBuffer, rtpBuffer.Length, client.SocketAddress);
                                }
                                catch (Exception sendExcp)
                                {
                                    logger.Error("RelayRTP exception sending to " + client.SocketAddress + ". " + sendExcp.Message);
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
                        if (_webRTCClients.Count(x => x.STUNExchangeComplete == true && x.IsDtlsNegotiationComplete == true) > 0)
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
                                    colorConverter.ConvertToI420(p, _webcamVideoSubType, Convert.ToInt32(_webcamWidth), Convert.ToInt32(_webcamHeight), ref convertedFrame);

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

                                lock (_webRTCClients)
                                {
                                    foreach (var client in _webRTCClients.Where(x => x.STUNExchangeComplete && x.IsDtlsNegotiationComplete == true))
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

                                            Console.WriteLine("Sending VP8 frame of " + encodedBuffer.Length + " bytes to " + client.SocketAddress + ".");

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

                                                    _webRTCReceiverClient.BeginSend(rtpBuffer, rtpBuffer.Length, client.SocketAddress, null, null);
                                                }
                                            }
                                        }
                                        catch (Exception sendExcp)
                                        {
                                            logger.Error("SendRTP exception sending to " + client.SocketAddress + ". " + sendExcp.Message);
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
                    if (_webRTCClients.Count != 0)
                    {
                        var sampleItem = samples[sampleIndex];
                        string[] sampleFields = sampleItem.Split(',');

                        uint timestamp = Convert.ToUInt32(sampleFields[0]);
                        int markerBit = Convert.ToInt32(sampleFields[1]);
                        byte[] sample = Convert.FromBase64String(sampleFields[2]);

                        lock (_webRTCClients)
                        {
                            foreach (var client in _webRTCClients.Where(x => x.STUNExchangeComplete))
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

                                    logger.Debug("Sending RTP " + sample.Length + " bytes to " + client.SocketAddress + ", timestamp " + client.LastTimestamp + ", marker " + rtpPacket.Header.MarkerBit + ".");

                                    _webRTCReceiverClient.Send(rtpBuffer, rtpBuffer.Length, client.SocketAddress);

                                    if (markerBit == 1)
                                    {
                                        client.LastTimestamp += TIMESTAMP_SPACING;
                                    }
                                    //}
                                }
                                catch (Exception sendExcp)
                                {
                                    logger.Error("SendRTPFromFile exception sending to " + client.SocketAddress + ". " + sendExcp.Message);
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
                    if (_webRTCClients.Count != 0)
                    {
                        var sampleItem = samples[sampleIndex];
                        string[] sampleFields = sampleItem.Split(',');

                        //uint timestamp = Convert.ToUInt32(sampleFields[0]);
                        int markerBit = Convert.ToInt32(sampleFields[1]);
                        byte[] sample = Convert.FromBase64String(sampleFields[2]);

                        lock (_webRTCClients)
                        {
                            foreach (var client in _webRTCClients.Where(x => x.STUNExchangeComplete))
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

                                    _webRTCReceiverClient.Send(rtpBuffer, rtpBuffer.Length - SRTP_AUTH_KEY_LENGTH, _wiresharpEP);

                                    int rtperr = client.SrtpContext.ProtectRTP(rtpBuffer, rtpBuffer.Length - SRTP_AUTH_KEY_LENGTH);
                                    if (rtperr != 0)
                                    {
                                        logger.Debug("New RTP packet protect result " + rtperr + ".");
                                    }

                                    logger.Debug("Sending RTP " + sample.Length + " bytes to " + client.SocketAddress + ", timestamp " + client.LastTimestamp + ", marker " + rtpPacket.Header.MarkerBit + ".");

                                    _webRTCReceiverClient.Send(rtpBuffer, rtpBuffer.Length, client.SocketAddress);

                                    if (markerBit == 1)
                                    {
                                        client.LastTimestamp += TIMESTAMP_SPACING;
                                    }
                                    //}
                                }
                                catch (Exception sendExcp)
                                {
                                    logger.Error("SendRTPFromFile exception sending to " + client.SocketAddress + ". " + sendExcp.Message);
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
                    if (_webRTCClients.Count != 0)
                    {
                        var sampleItem = samples[sampleIndex];
                        string[] sampleFields = sampleItem.Split(',');

                        RTPVP8Header frameVP8Header = RTPVP8Header.GetVP8Header(Convert.FromBase64String(sampleFields[0]));
                        byte[] sample = Convert.FromBase64String(sampleFields[1]);

                        if (frameVP8Header.IsKeyFrame)
                        {
                            Console.WriteLine("Key frame.");
                        }

                        lock (_webRTCClients)
                        {
                            foreach (var client in _webRTCClients.Where(x => x.STUNExchangeComplete))
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

                                        _webRTCReceiverClient.Send(rtpBuffer, rtpBuffer.Length - SRTP_AUTH_KEY_LENGTH, _wiresharpEP);

                                        int rtperr = client.SrtpContext.ProtectRTP(rtpBuffer, rtpBuffer.Length - SRTP_AUTH_KEY_LENGTH);
                                        if (rtperr != 0)
                                        {
                                            logger.Debug("New RTP packet protect result " + rtperr + ".");
                                        }

                                        logger.Debug("Sending RTP " + sample.Length + " bytes to " + client.SocketAddress + ", timestamp " + client.LastTimestamp + ", marker " + rtpPacket.Header.MarkerBit + ".");

                                        _webRTCReceiverClient.Send(rtpBuffer, rtpBuffer.Length, client.SocketAddress);
                                    }

                                    client.LastTimestamp += TIMESTAMP_SPACING;
                                }
                                catch (Exception sendExcp)
                                {
                                    logger.Error("SendRTPFromVP8FramesFile exception sending to " + client.SocketAddress + ". " + sendExcp.Message);
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
