//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: A console application to test the WebRTC ICE negotiation and
// DTLS handshake.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 28 Mar 2020	Aaron Clauson	Created, Dublin, Ireland.
// 13 Jul 2020  Aaron CLauson   Added command line options.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommandLine;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Serilog;
using SIPSorcery.Net;
using SIPSorcery.Sys;
using WebSocketSharp;
using WebSocketSharp.Net.WebSockets;
using WebSocketSharp.Server;

namespace SIPSorcery.Examples
{
    public class Options
    {
        [Option("ws", Required = false,
            HelpText = "Create a web socket server to act as a signalling channel for exchanging SDP and ICE candidates with a remote WebRTC peer.")]
        public bool UseWebSocket { get; set; }

        [Option("wss", Required = false,
            HelpText = "Create a secure web socket server to act as a signalling channel for exchanging SDP and ICE candidates with a remote WebRTC peer.")]
        public bool UseSecureWebSocket { get; set; }

        [Option("offer", Required = false,
            HelpText = "Create an initial SDP offer for a remote WebRTC peer. The offer will be serialised as base 64 encoded JSON. An SDP answer in the same format can then be entered.")]
        public bool CreateJsonOffer { get; set; }

        [Option("stun", Required = false,
            HelpText = "STUN or TURN server to use in the peer connection configuration. Format \"(stun|turn):host[:port][;username;password]\".")]
        public string StunServer { get; set; }
    }

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
        //private const string SIPSORCERY_STUN_SERVER = "turn:sipsorcery.com";
        //private const string SIPSORCERY_STUN_SERVER_USERNAME = "aaron"; //"stun.sipsorcery.com";
        //private const string SIPSORCERY_STUN_SERVER_PASSWORD = "password"; //"stun.sipsorcery.com";

        private static Microsoft.Extensions.Logging.ILogger logger = SIPSorcery.Sys.Log.Logger;

        private static WebSocketServer _webSocketServer;
        private static RTCIceServer _stunServer;

        static void Main(string[] args)
        {
            Console.WriteLine("WebRTC Console Test Program");
            Console.WriteLine("Press ctrl-c to exit.");

            var result = Parser.Default.ParseArguments<Options>(args)
                .WithParsed<Options>(opts => RunCommand(opts).Wait());
        }

        static async Task RunCommand(Options options)
        {
            // Plumbing code to facilitate a graceful exit.
            CancellationTokenSource exitCts = new CancellationTokenSource(); // Cancellation token to stop the SIP transport and RTP stream.
            ManualResetEvent exitMre = new ManualResetEvent(false);

            AddConsoleLogger();

            if (options.UseWebSocket || options.UseSecureWebSocket)
            {
                // Start web socket.
                Console.WriteLine("Starting web socket server...");
                _webSocketServer = new WebSocketServer(IPAddress.Any, WEBSOCKET_PORT, options.UseSecureWebSocket);

                if (options.UseSecureWebSocket)
                {
                    _webSocketServer.SslConfiguration.ServerCertificate = new System.Security.Cryptography.X509Certificates.X509Certificate2(LOCALHOST_CERTIFICATE_PATH);
                    _webSocketServer.SslConfiguration.CheckCertificateRevocation = false;
                }

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
            }

            if (options.StunServer != null)
            {
                string[] fields = options.StunServer.Split(';');

                _stunServer = new RTCIceServer
                {
                    urls = fields[0],
                    username = fields.Length > 1 ? fields[1] : null,
                    credential = fields.Length > 2 ? fields[2] : null,
                    credentialType = RTCIceCredentialType.password
                };
            }

            if (options.CreateJsonOffer)
            {
                var pc = Createpc(null, _stunServer);
                pc.createDataChannel("mychannel");

                var offerSdp = pc.createOffer(null);
                await pc.setLocalDescription(offerSdp);

                Console.WriteLine(offerSdp.sdp);

                var offerJson = JsonConvert.SerializeObject(offerSdp, new Newtonsoft.Json.Converters.StringEnumConverter());
                var offerBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(offerJson));

                Console.WriteLine(offerBase64);

                string remoteAnswerB64 = null;
                while (string.IsNullOrWhiteSpace(remoteAnswerB64))
                {
                    Console.Write("Remote Answer => ");
                    remoteAnswerB64 = Console.ReadLine();
                }

                if (remoteAnswerB64 == "q")
                {
                    Console.WriteLine("Quitting.");
                }
                else
                {
                    string remoteAnswer = Encoding.UTF8.GetString(Convert.FromBase64String(remoteAnswerB64));

                    Console.WriteLine(remoteAnswer);

                    RTCSessionDescriptionInit answerInit = JsonConvert.DeserializeObject<RTCSessionDescriptionInit>(remoteAnswer);

                    Console.WriteLine($"Remote answer: {answerInit.sdp}");

                    pc.setRemoteDescription(answerInit);

                    // Wait for a signal saying the call failed, was cancelled with ctrl-c or completed.
                    exitMre.WaitOne();

                    Console.WriteLine("Closing.");
                    pc.Close("normal");

                    Task.Delay(1000).Wait();
                }
            }

            // Ctrl-c will gracefully exit the call at any point.
            Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                exitMre.Set();
            };

            // Wait for a signal saying the call failed, was cancelled with ctrl-c or completed.
            exitMre.WaitOne();

            _webSocketServer?.Stop();
        }

        private static Task<RTCPeerConnection> ReceiveOffer(WebSocketContext context)
        {
            logger.LogDebug($"Web socket client connection from {context.UserEndPoint}, waiting for offer...");
            var pc = Createpc(context, _stunServer);
            return Task.FromResult(pc);
        }

        private static async Task<RTCPeerConnection> SendOffer(WebSocketContext context)
        {
            logger.LogDebug($"Web socket client connection from {context.UserEndPoint}, sending offer.");

            var pc = Createpc(context, _stunServer);
            pc.createDataChannel("mychannel2");

            var offerInit = pc.createOffer(null);
            await pc.setLocalDescription(offerInit);

            logger.LogDebug($"Sending SDP offer to client {context.UserEndPoint}.");

            context.WebSocket.Send(offerInit.sdp);

            return pc;
        }

        private static RTCPeerConnection Createpc(WebSocketContext context, RTCIceServer stunServer)
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
                X_RemoteSignallingAddress = (context != null) ? context.UserEndPoint.Address : null,
                iceServers = stunServer != null ? new List<RTCIceServer> { stunServer } : null,
                iceTransportPolicy = RTCIceTransportPolicy.all
            };

            var pc = new RTCPeerConnection(pcConfiguration);

            // Add inactive audio and video tracks.
            //MediaStreamTrack audioTrack = new MediaStreamTrack(SDPMediaTypesEnum.audio, false, new List<SDPMediaFormat> { new SDPMediaFormat(SDPMediaFormatsEnum.PCMU) }, MediaStreamStatusEnum.RecvOnly);
            //pc.addTrack(audioTrack);
            //MediaStreamTrack videoTrack = new MediaStreamTrack(SDPMediaTypesEnum.video, false, new List<SDPMediaFormat> { new SDPMediaFormat(SDPMediaFormatsEnum.VP8) }, MediaStreamStatusEnum.Inactive);
            //pc.addTrack(videoTrack);

            pc.onicecandidateerror += (candidate, error) => logger.LogWarning($"Error adding remote ICE candidate. {error} {candidate}");
            pc.onconnectionstatechange += (state) => logger.LogDebug($"Peer connection state changed to {state}.");
            pc.OnReceiveReport += (ep, type, rtcp) => logger.LogDebug($"RTCP {type} report received.");
            pc.OnRtcpBye += (reason) => logger.LogDebug($"RTCP BYE receive, reason: {(string.IsNullOrWhiteSpace(reason) ? "<none>" : reason)}.");
            pc.GetRtpChannel().OnStunMessageReceived += (msg, ep, isrelay) => logger.LogDebug($"STUN message received from {ep}, message class {msg.Header.MessageClass}.");

            pc.onicecandidate += (candidate) =>
            {
                if (pc.signalingState == RTCSignalingState.have_local_offer ||
                    pc.signalingState == RTCSignalingState.have_remote_offer)
                {
                    if (context != null)
                    {
                        context.WebSocket.Send($"candidate:{candidate}");
                    }
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
