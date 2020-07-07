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
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Serilog;
using SIPSorcery.Net;
using SIPSorcery.Sys;
using WebSocketSharp;
using WebSocketSharp.Net.WebSockets;
using WebSocketSharp.Server;

namespace SIPSorcery.Examples
{
    public class WebRtcClient : WebSocketBehavior
    {
        public RTCPeerConnection pc;

        public event Func<WebSocketContext, Task<RTCPeerConnection>> WebSocketOpened;
        public event Func<WebSocketContext, RTCPeerConnection, string, Task> OnMessageReceived;

        public WebRtcClient()
        { }

        protected override void OnMessage(MessageEventArgs e)
        {
            OnMessageReceived(this.Context, pc, e.Data);
        }

        protected override async void OnOpen()
        {
            base.OnOpen();
            pc = await WebSocketOpened(this.Context);
        }
    }

    class Program
    {
        private const string LOCALHOST_CERTIFICATE_PATH = "certs/localhost.pfx";
        private const int WEBSOCKET_PORT = 8081;
        private const string SIPSORCERY_STUN_SERVER = "turn:sipsorcery.com";
        private const string SIPSORCERY_STUN_SERVER_USERNAME = "aaron"; //"stun.sipsorcery.com";
        private const string SIPSORCERY_STUN_SERVER_PASSWORD = "password"; //"stun.sipsorcery.com";

        private static Microsoft.Extensions.Logging.ILogger logger = SIPSorcery.Sys.Log.Logger;

        private static WebSocketServer _webSocketServer;

        static void Main()
        {
            Console.WriteLine("ICE Console Test Program");
            Console.WriteLine("Press ctrl-c to exit.");

            // Plumbing code to facilitate a graceful exit.
            CancellationTokenSource exitCts = new CancellationTokenSource(); // Cancellation token to stop the SIP transport and RTP stream.
            ManualResetEvent exitMre = new ManualResetEvent(false);

            AddConsoleLogger();

            // Start web socket.
            Console.WriteLine("Starting web socket server...");
            _webSocketServer = new WebSocketServer(IPAddress.Any, WEBSOCKET_PORT, true);
            _webSocketServer.SslConfiguration.ServerCertificate = new System.Security.Cryptography.X509Certificates.X509Certificate2(LOCALHOST_CERTIFICATE_PATH);
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
            var pc = Createpc(context);
            return Task.FromResult(pc);
        }

        private static async Task<RTCPeerConnection> SendOffer(WebSocketContext context)
        {
            logger.LogDebug($"Web socket client connection from {context.UserEndPoint}, sending offer.");

            var pc = Createpc(context);

            var offerInit = pc.createOffer(null);
            await pc.setLocalDescription(offerInit);

            logger.LogDebug($"Sending SDP offer to client {context.UserEndPoint}.");

            context.WebSocket.Send(offerInit.sdp);

            return pc;
        }

        private static RTCPeerConnection Createpc(WebSocketContext context)
        {
            List<RTCCertificate> presetCertificates = null;
            if (File.Exists(LOCALHOST_CERTIFICATE_PATH))
            {
                var localhostCert = new X509Certificate2(LOCALHOST_CERTIFICATE_PATH, (string)null, X509KeyStorageFlags.Exportable);
                presetCertificates = new List<RTCCertificate> { new RTCCertificate { Certificate = localhostCert } };
            }

            RTCConfiguration pcConfiguration = new RTCConfiguration
            {
                certificates = presetCertificates,
                X_RemoteSignallingAddress = context.UserEndPoint.Address,
                iceServers = new List<RTCIceServer> {
                    new RTCIceServer
                    {
                        urls = SIPSORCERY_STUN_SERVER,
                        username = SIPSORCERY_STUN_SERVER_USERNAME,
                        credential = SIPSORCERY_STUN_SERVER_PASSWORD,
                        credentialType = RTCIceCredentialType.password
                    }
                },
                iceTransportPolicy = RTCIceTransportPolicy.all
            };

            var pc = new RTCPeerConnection(pcConfiguration);

            // Add inactive audio and video tracks.
            MediaStreamTrack audioTrack = new MediaStreamTrack(SDPMediaTypesEnum.audio, false, new List<SDPMediaFormat> { new SDPMediaFormat(SDPMediaFormatsEnum.PCMU) }, MediaStreamStatusEnum.RecvOnly);
            pc.addTrack(audioTrack);
            MediaStreamTrack videoTrack = new MediaStreamTrack(SDPMediaTypesEnum.video, false, new List<SDPMediaFormat> { new SDPMediaFormat(SDPMediaFormatsEnum.VP8) }, MediaStreamStatusEnum.Inactive);
            pc.addTrack(videoTrack);

            pc.onicecandidateerror += (candidate, error) => logger.LogWarning($"Error adding remote ICE candidate. {error} {candidate}");
            pc.onconnectionstatechange += (state) => logger.LogDebug($"Peer connection state changed to {state}.");
            pc.OnReceiveReport += (type, rtcp) => logger.LogDebug($"RTCP {type} report received.");
            pc.OnRtcpBye += (reason) => logger.LogDebug($"RTCP BYE receive, reason: {(string.IsNullOrWhiteSpace(reason) ? "<none>" : reason)}.");

            pc.onicecandidate += (candidate) =>
            {
                if (pc.signalingState == RTCSignalingState.have_local_offer ||
                    pc.signalingState == RTCSignalingState.have_remote_offer)
                {
                    context.WebSocket.Send($"candidate:{candidate}");
                }
            };

            // Peer ICE connection state changes are for ICE events such as the STUN checks completing.
            pc.oniceconnectionstatechange += (state) =>
            {
                logger.LogDebug($"ICE connection state change to {state}.");
            };

            return pc;
        }

        private static async Task WebSocketMessageReceived(WebSocketContext context, RTCPeerConnection pc, string message)
        {
            try
            {
                if (pc.localDescription == null)
                {
                    //logger.LogDebug("Offer SDP: " + message);
                    logger.LogDebug("Offer SDP received.");

                    // Add local media tracks depending on what was offered. Also add local tracks with the same media ID as 
                    // the remote tracks so that the media announcement in the SDP answer are in the same order.
                    SDP remoteSdp = SDP.ParseSDPDescription(message);
                    pc.setRemoteDescription(new RTCSessionDescriptionInit { sdp = message, type = RTCSdpType.offer });

                    var answer = pc.createAnswer(null);
                    await pc.setLocalDescription(answer);

                    context.WebSocket.Send(answer.sdp);
                }
                else if (pc.remoteDescription == null)
                {
                    logger.LogDebug("Answer SDP: " + message);
                    pc.setRemoteDescription(new RTCSessionDescriptionInit { sdp = message, type = RTCSdpType.answer });
                }
                else
                {
                    logger.LogDebug("ICE Candidate: " + message);

                    if (string.IsNullOrWhiteSpace(message) || message.Trim().ToLower() == SDP.END_ICE_CANDIDATES_ATTRIBUTE)
                    {
                        logger.LogDebug("End of candidates message received.");
                    }
                    else
                    {
                        var candInit = Newtonsoft.Json.JsonConvert.DeserializeObject<RTCIceCandidateInit>(message);
                        pc.addIceCandidate(candInit);
                    }
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception WebSocketMessageReceived. " + excp.Message);
            }
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
