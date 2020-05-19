//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: A console application to test the ICE negotiation process.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 28 Mar 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Serilog;
using SIPSorcery.Net;
using SIPSorceryMedia;
using WebSocketSharp;
using WebSocketSharp.Net.WebSockets;
using WebSocketSharp.Server;

namespace SIPSorcery.Examples
{
    public class WebRtcClient : WebSocketBehavior
    {
        public RTCPeerConnection PeerConnection;

        public event Func<WebSocketContext, Task<RTCPeerConnection>> WebSocketOpened;
        public event Func<WebSocketContext, RTCPeerConnection, string, Task> OnMessageReceived;

        public WebRtcClient()
        { }

        protected override void OnMessage(MessageEventArgs e)
        {
            OnMessageReceived(this.Context, PeerConnection, e.Data);
        }

        protected override async void OnOpen()
        {
            base.OnOpen();
            PeerConnection = await WebSocketOpened(this.Context);
        }
    }

    class Program
    {
        private const string WEBSOCKET_CERTIFICATE_PATH = "certs/localhost.pfx";
        private const string DTLS_CERTIFICATE_PATH = "certs/localhost.pem";
        private const string DTLS_KEY_PATH = "certs/localhost_key.pem";
        private const string DTLS_CERTIFICATE_FINGERPRINT = "sha-256 C6:ED:8C:9D:06:50:77:23:0A:4A:D8:42:68:29:D0:70:2F:BB:C7:72:EC:98:5C:62:07:1B:0C:5D:CB:CE:BE:CD";
        private const int WEBSOCKET_PORT = 8081;
        private const int TEST_DTLS_HANDSHAKE_TIMEOUT = 10000;

        private static Microsoft.Extensions.Logging.ILogger logger = SIPSorcery.Sys.Log.Logger;

        private static WebSocketServer _webSocketServer;

        private delegate void MediaSampleReadyDelegate(SDPMediaTypesEnum mediaType, uint timestamp, byte[] sample);
        private static event MediaSampleReadyDelegate OnMediaSampleReady;

        static void Main()
        {
            Console.WriteLine("ICE Console Test Program");
            Console.WriteLine("Press ctrl-c to exit.");

            if (!File.Exists(DTLS_CERTIFICATE_PATH))
            {
                throw new ApplicationException($"The DTLS certificate file could not be found at {DTLS_CERTIFICATE_PATH}.");
            }
            else if (!File.Exists(DTLS_KEY_PATH))
            {
                throw new ApplicationException($"The DTLS key file could not be found at {DTLS_KEY_PATH}.");
            }

            // Plumbing code to facilitate a graceful exit.
            CancellationTokenSource exitCts = new CancellationTokenSource(); // Cancellation token to stop the SIP transport and RTP stream.
            ManualResetEvent exitMre = new ManualResetEvent(false);

            AddConsoleLogger();

            // Initialise OpenSSL & libsrtp, saves a couple of seconds for the first client connection.
            Console.WriteLine("Initialising OpenSSL and libsrtp...");
            DtlsHandshake.InitialiseOpenSSL();
            Srtp.InitialiseLibSrtp();

            Task.Run(DoDtlsHandshakeLoopbackTest).Wait();

            Console.WriteLine("Test DTLS handshake complete.");

            // Start web socket.
            Console.WriteLine("Starting web socket server...");
            _webSocketServer = new WebSocketServer(IPAddress.Any, WEBSOCKET_PORT, true);
            _webSocketServer.SslConfiguration.ServerCertificate = new System.Security.Cryptography.X509Certificates.X509Certificate2(WEBSOCKET_CERTIFICATE_PATH);
            _webSocketServer.SslConfiguration.CheckCertificateRevocation = false;
            //_webSocketServer.Log.Level = WebSocketSharp.LogLevel.Debug;
            _webSocketServer.AddWebSocketService<WebRtcClient>("/sendoffer", (client) =>
            {
                client.WebSocketOpened += SendOffer;
                client.OnMessageReceived += WebSocketMessageReceived;
            });
            _webSocketServer.AddWebSocketService<WebRtcClient>("/receiveoffer", (client) =>
            {
                client.WebSocketOpened += ReceiveOffer;
                client.OnMessageReceived += WebSocketMessageReceived;
            });
            _webSocketServer.Start();

            Console.WriteLine($"Waiting for browser web socket connection to {_webSocketServer.Address}:{_webSocketServer.Port}...");

            // Ctrl-c will gracefully exit the call at any point.
            Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                exitMre.Set();
            };

            // Wait for a signal saying the call failed, was cancelled with ctrl-c or completed.
            exitMre.WaitOne();

            _webSocketServer.Stop();
        }

        private static Task<RTCPeerConnection> ReceiveOffer(WebSocketContext context)
        {
            logger.LogDebug($"Web socket client connection from {context.UserEndPoint}, waiting for offer...");

            var peerConnection = CreatePeerConnection();

            return Task.FromResult(peerConnection);
        }

        private static async Task<RTCPeerConnection> SendOffer(WebSocketContext context)
        {
            logger.LogDebug($"Web socket client connection from {context.UserEndPoint}, sending offer.");

            var peerConnection = CreatePeerConnection();

            // Offer audio and video.
            MediaStreamTrack audioTrack = new MediaStreamTrack("0", SDPMediaTypesEnum.audio, false, new List<SDPMediaFormat> { new SDPMediaFormat(SDPMediaFormatsEnum.PCMU) }, MediaStreamStatusEnum.SendRecv);
            peerConnection.addTrack(audioTrack);
            MediaStreamTrack videoTrack = new MediaStreamTrack("1", SDPMediaTypesEnum.video, false, new List<SDPMediaFormat> { new SDPMediaFormat(SDPMediaFormatsEnum.VP8) }, MediaStreamStatusEnum.SendRecv);
            peerConnection.addTrack(videoTrack);

            var offerInit = await peerConnection.createOffer(null);
            await peerConnection.setLocalDescription(offerInit);

            logger.LogDebug($"Sending SDP offer to client {context.UserEndPoint}.");

            context.WebSocket.Send(offerInit.sdp);

            return peerConnection;
        }

        private static RTCPeerConnection CreatePeerConnection()
        {
            RTCConfiguration pcConfiguration = new RTCConfiguration
            {
                certificates = new List<RTCCertificate>
                {
                    new RTCCertificate
                    {
                        X_CertificatePath = DTLS_CERTIFICATE_PATH,
                        X_KeyPath = DTLS_KEY_PATH,
                        X_Fingerprint = DTLS_CERTIFICATE_FINGERPRINT
                    }
                }
            };

            var peerConnection = new RTCPeerConnection(pcConfiguration);

            peerConnection.OnReceiveReport += RtpSession_OnReceiveReport;
            peerConnection.OnSendReport += RtpSession_OnSendReport;

            peerConnection.onconnectionstatechange += (state) =>
            {
                if (state == RTCPeerConnectionState.closed || state == RTCPeerConnectionState.disconnected || state == RTCPeerConnectionState.failed)
                {
                    OnMediaSampleReady -= peerConnection.SendMedia;
                    peerConnection.OnReceiveReport -= RtpSession_OnReceiveReport;
                    peerConnection.OnSendReport -= RtpSession_OnSendReport;
                }
                else if (state == RTCPeerConnectionState.connected)
                {
                    logger.LogDebug("Peer connection connected.");
                    OnMediaSampleReady += peerConnection.SendMedia;
                }
            };

            peerConnection.oniceconnectionstatechange += (state) =>
            {
                if (state == RTCIceConnectionState.connected)
                {
                    logger.LogDebug("Starting DTLS handshake task.");

                    bool dtlsResult = false;
                    Task.Run(() => dtlsResult = DoDtlsHandshake(peerConnection))
                    .ContinueWith((t) =>
                    {
                        logger.LogDebug($"dtls handshake result {dtlsResult}.");

                        if (dtlsResult)
                        {
                            var remoteEndPoint = peerConnection.IceSession.NominatedCandidate.GetEndPoint();
                            peerConnection.SetDestination(SDPMediaTypesEnum.audio, remoteEndPoint, remoteEndPoint);

                            logger.LogDebug($"RTP remote end point set to {remoteEndPoint}.");
                        }
                        else
                        {
                            peerConnection.Close("dtls handshake failed.");
                        }
                    });
                }
            };

            return peerConnection;
        }

        private static async Task WebSocketMessageReceived(WebSocketContext context, RTCPeerConnection peerConnection, string message)
        {
            try
            {
                if(peerConnection.localDescription == null)
                {
                    //logger.LogDebug("Offer SDP: " + message);
                    logger.LogDebug("Offer SDP received.");

                    // Add local media tracks depending on what was offered. Also add local tracks with the same media ID as 
                    // the remote tracks so that the media announcement in the SDP answer are in the same order.
                    SDP remoteSdp = SDP.ParseSDPDescription(message);

                    var remoteAudioAnn = remoteSdp.Media.Where(x => x.Media == SDPMediaTypesEnum.audio).FirstOrDefault();
                    var remoteVideoAnn = remoteSdp.Media.Where(x => x.Media == SDPMediaTypesEnum.video).FirstOrDefault();

                    if (remoteAudioAnn != null)
                    {
                        MediaStreamTrack audioTrack = new MediaStreamTrack(remoteAudioAnn.MediaID, SDPMediaTypesEnum.audio, false, new List<SDPMediaFormat> { new SDPMediaFormat(SDPMediaFormatsEnum.PCMU) }, MediaStreamStatusEnum.RecvOnly);
                        peerConnection.addTrack(audioTrack);
                    }
                    
                    if (remoteVideoAnn != null)
                    {
                        MediaStreamTrack videoTrack = new MediaStreamTrack(remoteVideoAnn.MediaID, SDPMediaTypesEnum.video, false, new List<SDPMediaFormat> { new SDPMediaFormat(SDPMediaFormatsEnum.VP8) }, MediaStreamStatusEnum.RecvOnly);
                        peerConnection.addTrack(videoTrack);
                    }

                    // After local media tracks have been added the remote description can be set.
                    await peerConnection.setRemoteDescription(new RTCSessionDescriptionInit { sdp = message, type = RTCSdpType.offer });

                    var answer = await peerConnection.createAnswer(null);
                    await peerConnection.setLocalDescription(answer);

                    context.WebSocket.Send(answer.sdp);
                }
                else if (peerConnection.remoteDescription == null)
                {
                    logger.LogDebug("Answer SDP: " + message);
                    await peerConnection.setRemoteDescription(new RTCSessionDescriptionInit { sdp = message, type = RTCSdpType.answer });
                }
                else
                {
                    logger.LogDebug("ICE Candidate: " + message);
                    await peerConnection.addIceCandidate(new RTCIceCandidateInit { candidate = message });
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception WebSocketMessageReceived. " + excp.Message);
            }
        }

        /// <summary>
        /// Hands the socket handle to the DTLS context and waits for the handshake to complete.
        /// </summary>
        /// <param name="webRtcSession">The WebRTC session to perform the DTLS handshake on.</param>
        /// <returns>True if the handshake completed successfully or false otherwise.</returns>
        private static bool DoDtlsHandshake(RTCPeerConnection peerConnection)
        {
            logger.LogDebug("DoDtlsHandshake started.");

            var dtls = new DtlsHandshake(DTLS_CERTIFICATE_PATH, DTLS_KEY_PATH);

            int res = dtls.DoHandshakeAsServer((ulong)peerConnection.GetRtpChannel(SDPMediaTypesEnum.audio).RtpSocket.Handle);

            logger.LogDebug("DtlsContext initialisation result=" + res);

            if (dtls.IsHandshakeComplete())
            {
                logger.LogDebug("DTLS negotiation complete.");

                var srtpSendContext = new Srtp(dtls, false);
                var srtpReceiveContext = new Srtp(dtls, true);

                peerConnection.SetSecurityContext(
                    srtpSendContext.ProtectRTP,
                    srtpReceiveContext.UnprotectRTP,
                    srtpSendContext.ProtectRTCP,
                    srtpReceiveContext.UnprotectRTCP);

                dtls.Shutdown();
                return true;
            }
            else
            {
                dtls.Shutdown();
                return false;
            }
        }

        /// <summary>
        /// Diagnostic handler to print out our RTCP sender/receiver reports.
        /// </summary>
        private static void RtpSession_OnSendReport(SDPMediaTypesEnum mediaType, RTCPCompoundPacket sentRtcpReport)
        {
            try
            {
                if (sentRtcpReport.SenderReport != null)
                {
                    var sr = sentRtcpReport.SenderReport;
                    Console.WriteLine($"RTCP sent SR {mediaType}, ssrc {sr.SSRC}, pkts {sr.PacketCount}, bytes {sr.OctetCount}.");
                }
                else if (sentRtcpReport.ReceiverReport != null && sentRtcpReport.ReceiverReport.ReceptionReports?.Count > 0)
                {
                    var rrSample = sentRtcpReport.ReceiverReport.ReceptionReports.First();
                    Console.WriteLine($"RTCP sent RR {mediaType}, ssrc {rrSample.SSRC}, seqnum {rrSample.ExtendedHighestSequenceNumber}.");
                }
                else
                {
                    Console.WriteLine($"RTCP report (empty sender and receiver reports) SDES CNAME {sentRtcpReport.SDesReport.CNAME}.");
                }
            }
            catch (Exception excp)
            {
                logger.LogWarning($"Exception RtpSession_OnSendReport. {excp.Message}");
            }
        }

        /// <summary>
        /// Diagnostic handler to print out our RTCP reports from the remote WebRTC peer.
        /// </summary>
        private static void RtpSession_OnReceiveReport(SDPMediaTypesEnum mediaType, RTCPCompoundPacket recvRtcpReport)
        {
            try
            {
                var rr = recvRtcpReport.ReceiverReport.ReceptionReports.FirstOrDefault();
                if (rr != null)
                {
                    logger.LogDebug($"RTCP {mediaType} Receiver Report: SSRC {rr.SSRC}, pkts lost {rr.PacketsLost}, delay since SR {rr.DelaySinceLastSenderReport}.");
                }
                else
                {
                    logger.LogDebug($"RTCP {mediaType} Receiver Report: empty.");
                }
            }
            catch (Exception excp)
            {
                logger.LogWarning($"Exception RtpSession_OnReceiveReport. {excp.Message}");
            }
        }

        /// <summary>
        /// Runs a DTLS handshake test between two threads on a loopback address. The main motivation for
        /// this test was that the first DTLS handshake between this application and a client browser
        /// was often substantially slower and occasionally failed. By doing a loopback test the idea 
        /// is that the internal OpenSSL state is initialised.
        /// </summary>
        private static void DoDtlsHandshakeLoopbackTest()
        {
            IPAddress testAddr = IPAddress.Loopback;

            Socket svrSock = new Socket(testAddr.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            svrSock.Bind(new IPEndPoint(testAddr, 9000));
            int svrPort = ((IPEndPoint)svrSock.LocalEndPoint).Port;
            DtlsHandshake svrHandshake = new DtlsHandshake(DTLS_CERTIFICATE_PATH, DTLS_KEY_PATH);
            //svrHandshake.Debug = true;
            var svrTask = Task.Run(() => svrHandshake.DoHandshakeAsServer((ulong)svrSock.Handle));

            Socket cliSock = new Socket(testAddr.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            cliSock.Bind(new IPEndPoint(testAddr, 0));
            cliSock.Connect(testAddr, svrPort);
            DtlsHandshake cliHandshake = new DtlsHandshake();
            //cliHandshake.Debug = true;
            var cliTask = Task.Run(() => cliHandshake.DoHandshakeAsClient((ulong)cliSock.Handle, (short)testAddr.AddressFamily, testAddr.GetAddressBytes(), (ushort)svrPort));

            bool result = Task.WaitAll(new Task[] { svrTask, cliTask }, TEST_DTLS_HANDSHAKE_TIMEOUT);

            cliHandshake.Shutdown();
            svrHandshake.Shutdown();
            cliSock.Close();
            svrSock.Close();
        }

        /// <summary>
        ///  Adds a console logger. Can be omitted if internal SIPSorcery debug and warning messages are not required.
        /// </summary>
        private static void AddConsoleLogger()
        {
            var loggerFactory = new Microsoft.Extensions.Logging.LoggerFactory();
            var loggerConfig = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .MinimumLevel.Is(Serilog.Events.LogEventLevel.Debug)
                .WriteTo.Console()
                .CreateLogger();
            loggerFactory.AddSerilog(loggerConfig);
            SIPSorcery.Sys.Log.LoggerFactory = loggerFactory;
        }
    }
}
