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
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommandLine;
using Makaretu.Dns;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Serilog;
using SIPSorcery.Net;
using SIPSorcery.Sys;
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
            HelpText = "STUN or TURN server to use in the peer connection configuration. Format \"--stun=(stun|turn):host[:port][;username;password]\".")]
        public string StunServer { get; set; }

        [Option("relayonly", Required = false,
            HelpText = "Only TURN servers will be included in the ICE candidates supplied to the remote peer. Format \"--relayonly\".")]
        public bool RelayOnly { get; set; }

        [Option("nodedss", Required = false,
            HelpText = "Address of node-dss simple signalling server to exchange SDP and ice candidates. Format \"--nodedss=http://192.168.11.50:3001\".")]
        public string NodeDssServer { get; set; }
    }

    public class WebRtcClient : WebSocketBehavior
    {
        public RTCPeerConnection pc;

        public event Func<WebSocketContext, Task<RTCPeerConnection>> WebSocketOpened;
        public event Func<WebSocketContext, RTCPeerConnection, string, Task> OnMessageReceived;

        public WebRtcClient()
        { }

        protected override void OnMessage(WebSocketSharp.MessageEventArgs e)
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
        private const string COMMAND_PROMPT = "Command => ";
        private const string DATA_CHANNEL_LABEL = "dcx";
        private const int MDNS_TIMEOUT = 2000;

        private static Microsoft.Extensions.Logging.ILogger logger = SIPSorcery.Sys.Log.Logger;

        private static WebSocketServer _webSocketServer;
        private static RTCIceServer _stunServer;
        private static Uri _nodeDssUri;
        private static HttpClient _nodeDssclient;
        private static bool _relayOnly;

        /// <summary>
        /// For simplicity this program only supports one active peer connection.
        /// </summary>
        private static RTCPeerConnection _peerConnection;

        static void Main(string[] args)
        {
            Console.WriteLine("WebRTC Console Test Program");
            Console.WriteLine("Press ctrl-c to exit.");

            bool noOptions = args?.Count() == 0;

            var result = Parser.Default.ParseArguments<Options>(args)
                .WithParsed<Options>(opts => RunCommand(opts, noOptions).Wait());
        }

        private static async Task RunCommand(Options options, bool noOptions)
        {
            // Plumbing code to facilitate a graceful exit.
            CancellationTokenSource exitCts = new CancellationTokenSource(); // Cancellation token to stop the SIP transport and RTP stream.
            //ManualResetEvent exitMre = new ManualResetEvent(false);

            AddConsoleLogger();

            // Start MDNS server.
            var mdnsServer = new ServiceDiscovery();

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

            _relayOnly = options.RelayOnly;

            if (options.UseWebSocket || options.UseSecureWebSocket || noOptions)
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
            else if (options.CreateJsonOffer)
            {
                var pc = Createpc(null, _stunServer, _relayOnly);

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

                string remoteAnswer = Encoding.UTF8.GetString(Convert.FromBase64String(remoteAnswerB64));

                Console.WriteLine(remoteAnswer);

                RTCSessionDescriptionInit answerInit = JsonConvert.DeserializeObject<RTCSessionDescriptionInit>(remoteAnswer);

                Console.WriteLine($"Remote answer: {answerInit.sdp}");

                var res = pc.setRemoteDescription(answerInit);
                if (res != SetDescriptionResultEnum.OK)
                {
                    // No point continuing. Something will need to change and then try again.
                    pc.Close("failed to set remote sdp");
                }
            }
            else if (options.NodeDssServer != null)
            {
                _nodeDssUri = new Uri(options.NodeDssServer);
                _nodeDssclient = new HttpClient();

                Console.WriteLine($"node-dss server successfully set to {_nodeDssUri}.");
            }

            _ = Task.Run(() => ProcessInput(exitCts));

            // Ctrl-c will gracefully exit the call at any point.
            Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                exitCts.Cancel();
            };

            // Wait for a signal saying the call failed, was cancelled with ctrl-c or completed.
            exitCts.Token.WaitHandle.WaitOne();

            Console.WriteLine();
            Console.WriteLine("Exiting...");

            _peerConnection?.Close("application exit");

            _webSocketServer?.Stop();

            Task.Delay(1000).Wait();
        }

        /// <summary>
        /// This application spits out a lot of log messages. In an attempt to make command entry slightly more usable
        /// this method attempts to always write the current command input as the bottom line on the console output.
        /// </summary>
        private static async Task ProcessInput(CancellationTokenSource cts)
        {
            // Local function to write the current command in the process of being entered.
            Action<int, string> writeCommandPrompt = (lastPromptRow, cmd) =>
            {
                // The cursor is already at the current row.
                if (Console.CursorTop == lastPromptRow)
                {
                    // The command was corrected. Need to re-write the whole line.
                    Console.SetCursorPosition(0, Console.CursorTop);
                    Console.Write(new string(' ', Console.WindowWidth));
                    Console.SetCursorPosition(0, Console.CursorTop);
                    Console.Write($"{COMMAND_PROMPT}{cmd}");
                }
                else
                {
                    // The cursor row has changed since the last input. Rewrite the prompt and command
                    // on the current line.
                    Console.Write($"{COMMAND_PROMPT}{cmd}");
                }
            };

            string command = null;
            int lastInputRow = Console.CursorTop;

            while (!cts.IsCancellationRequested)
            {
                var inKey = Console.ReadKey(true);

                if (inKey.Key == ConsoleKey.Enter)
                {
                    if (command == null)
                    {
                        Console.WriteLine();
                        Console.Write(COMMAND_PROMPT);
                    }
                    else
                    {
                        // Attempt to execute the current command.
                        switch (command.ToLower())
                        {
                            case "c":
                                // Close active peer connection.
                                if (_peerConnection != null)
                                {
                                    Console.WriteLine();
                                    Console.WriteLine("Closing peer connection");
                                    _peerConnection.Close("user initiated");
                                }
                                break;

                            case var x when x.StartsWith("cdc"):
                                // Attempt to create a new data channel.
                                if (_peerConnection != null)
                                {
                                    (_, var label) = x.Split(" ", 2, StringSplitOptions.None);
                                    if (!string.IsNullOrWhiteSpace(label))
                                    {
                                        Console.WriteLine();
                                        Console.WriteLine($"Creating data channel for label {label}.");
                                        var dc = _peerConnection.createDataChannel(label, null);
                                        dc.onmessage += (msg) => logger.LogDebug($" data channel message received on {label}: {msg}");
                                    }
                                    else
                                    {
                                        Console.WriteLine();
                                        Console.WriteLine($"Send message command was in the wrong format. Needs to be: cdc <label>");
                                    }
                                }
                                break;

                            case var x when x.StartsWith("ldc"):
                                // List data channels.
                                if (_peerConnection != null)
                                {
                                    if (_peerConnection.DataChannels.Count > 0)
                                    {
                                        Console.WriteLine();
                                        foreach (var dc in _peerConnection.DataChannels)
                                        {
                                            Console.WriteLine($" data channel: label {dc.label}, stream ID {dc.id}, is open {dc.IsOpened}.");
                                        }
                                    }
                                    else
                                    {
                                        Console.WriteLine();
                                        Console.WriteLine(" no data channels available.");
                                    }
                                }
                                break;

                            case var x when x.StartsWith("sdc"):
                                // Send data channel message.
                                if (_peerConnection != null)
                                {
                                    (_, var label, var msg) = x.Split(" ", 3, StringSplitOptions.None);
                                    if (!string.IsNullOrWhiteSpace(label) && !string.IsNullOrWhiteSpace(msg))
                                    {
                                        Console.WriteLine();
                                        Console.WriteLine($"Sending message on channel {label}: {msg}");

                                        var dc = _peerConnection.DataChannels.FirstOrDefault(x => x.label == label && x.IsOpened);
                                        if (dc != null)
                                        {
                                            dc.send(msg);
                                        }
                                        else
                                        {
                                            Console.WriteLine($"No data channel was found for label {label}.");
                                        }
                                    }
                                    else
                                    {
                                        Console.WriteLine();
                                        Console.WriteLine($"Send data channel message command was in the wrong format. Needs to be: sdc <label> <message>");
                                    }
                                }
                                break;

                            case "q":
                                // Quit.
                                Console.WriteLine();
                                Console.WriteLine("Quitting...");
                                cts.Cancel();
                                break;

                            case "isalive":
                                // Check responsiveness.
                                Console.WriteLine();
                                Console.WriteLine("yep");
                                Console.Write(COMMAND_PROMPT);
                                break;

                            case var x when x.StartsWith("node"):
                                (_, var sdpType, var myUser, string theirUser) = x.Split(" ", 4, StringSplitOptions.None);

                                if (sdpType == "so")
                                {
                                    _peerConnection = Createpc(null, _stunServer, _relayOnly);

                                    var offerSdp = _peerConnection.createOffer(null);
                                    await _peerConnection.setLocalDescription(offerSdp);

                                    Console.WriteLine($"Our Offer:\n{offerSdp.sdp}");

                                    var offerJson = JsonConvert.SerializeObject(offerSdp, new Newtonsoft.Json.Converters.StringEnumConverter());

                                    var content = new StringContent(offerJson, Encoding.UTF8, "application/json");
                                    var res = await _nodeDssclient.PostAsync($"{_nodeDssUri}data/{theirUser}", content);

                                    Console.WriteLine($"node-dss POST result {res.StatusCode}.");
                                }
                                else if (sdpType == "go")
                                {
                                    var res = await _nodeDssclient.GetAsync($"{_nodeDssUri}data/{myUser}");

                                    Console.WriteLine($"node-dss GET result {res.StatusCode}.");

                                    if (res.StatusCode == HttpStatusCode.OK)
                                    {
                                        var content = await res.Content.ReadAsStringAsync();
                                        RTCSessionDescriptionInit offerInit = JsonConvert.DeserializeObject<RTCSessionDescriptionInit>(content);

                                        Console.WriteLine($"Remote offer:\n{offerInit.sdp}");

                                        _peerConnection = Createpc(null, _stunServer, _relayOnly);

                                        var setRes = _peerConnection.setRemoteDescription(offerInit);
                                        if (setRes != SetDescriptionResultEnum.OK)
                                        {
                                            // No point continuing. Something will need to change and then try again.
                                            _peerConnection.Close("failed to set remote sdp offer");
                                        }
                                        else
                                        {
                                            var answer = _peerConnection.createAnswer(null);
                                            await _peerConnection.setLocalDescription(answer);

                                            Console.WriteLine($"Our answer:\n{answer.sdp}");

                                            var answerJson = JsonConvert.SerializeObject(answer, new Newtonsoft.Json.Converters.StringEnumConverter());
                                            var answerContent = new StringContent(answerJson, Encoding.UTF8, "application/json");
                                            var postRes = await _nodeDssclient.PostAsync($"{_nodeDssUri}data/{theirUser}", answerContent);

                                            Console.WriteLine($"node-dss POST result {res.StatusCode}.");
                                        }
                                    }
                                }
                                else if (sdpType == "ga")
                                {
                                    var res = await _nodeDssclient.GetAsync($"{_nodeDssUri}data/{myUser}");

                                    Console.WriteLine($"node-dss GET result {res.StatusCode}.");

                                    if (res.StatusCode == HttpStatusCode.OK)
                                    {
                                        var content = await res.Content.ReadAsStringAsync();
                                        RTCSessionDescriptionInit answerInit = JsonConvert.DeserializeObject<RTCSessionDescriptionInit>(content);

                                        Console.WriteLine($"Remote answer:\n{answerInit.sdp}");

                                        var setRes = _peerConnection.setRemoteDescription(answerInit);
                                        if (setRes != SetDescriptionResultEnum.OK)
                                        {
                                            // No point continuing. Something will need to change and then try again.
                                            _peerConnection.Close("failed to set remote sdp answer");
                                        }
                                    }
                                }
                                break;

                            default:
                                // Command not recognised.
                                Console.WriteLine();
                                Console.WriteLine($"Unknown command: {command}");
                                Console.Write(COMMAND_PROMPT);
                                break;
                        }

                        command = null;
                    }
                }
                else if (inKey.Key == ConsoleKey.UpArrow)
                {
                    // Convenience mechanism to get the current input prompt without
                    // needing to change the command being entered.
                    writeCommandPrompt(lastInputRow, command);
                }
                else if (inKey.Key == ConsoleKey.Escape)
                {
                    // Escape key clears the current command.
                    command = null;
                    writeCommandPrompt(lastInputRow, command);
                }
                else if (inKey.Key == ConsoleKey.Backspace)
                {
                    // Backspace removes the last character.
                    command = (command?.Length > 0) ? command.Substring(0, command.Length - 1) : null;
                    writeCommandPrompt(lastInputRow, command);
                }
                else if (!Char.IsControl(inKey.KeyChar))
                {
                    // Non-control character, append to current command.
                    command += inKey.KeyChar;
                    if (Console.CursorTop == lastInputRow)
                    {
                        Console.Write(inKey.KeyChar);
                    }
                    else
                    {
                        writeCommandPrompt(lastInputRow, command);
                    }
                }

                lastInputRow = Console.CursorTop;
            }
        }

        private static Task<RTCPeerConnection> ReceiveOffer(WebSocketContext context)
        {
            logger.LogDebug($"Web socket client connection from {context.UserEndPoint}, waiting for offer...");
            var pc = Createpc(context, _stunServer, _relayOnly);
            return Task.FromResult(pc);
        }

        private static async Task<RTCPeerConnection> SendOffer(WebSocketContext context)
        {
            logger.LogDebug($"Web socket client connection from {context.UserEndPoint}, sending offer.");

            var pc = Createpc(context, _stunServer, _relayOnly);

            var offerInit = pc.createOffer(null);
            await pc.setLocalDescription(offerInit);

            logger.LogDebug($"Sending SDP offer to client {context.UserEndPoint}.");

            context.WebSocket.Send(offerInit.sdp);

            return pc;
        }

        private static RTCPeerConnection Createpc(WebSocketContext context, RTCIceServer stunServer, bool relayOnly)
        {
            if (_peerConnection != null)
            {
                _peerConnection.Close("normal");
            }

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
                //iceTransportPolicy = RTCIceTransportPolicy.all,
                iceTransportPolicy = relayOnly ? RTCIceTransportPolicy.relay : RTCIceTransportPolicy.all,
                //X_BindAddress = IPAddress.Any, // NOTE: Not reqd. Using this to filter out IPv6 addresses so can test with Pion.
            };

            _peerConnection = new RTCPeerConnection(pcConfiguration);

            //_peerConnection.GetRtpChannel().MdnsResolve = (hostname) => Task.FromResult(NetServices.InternetDefaultAddress);
            _peerConnection.GetRtpChannel().MdnsResolve = MdnsResolve;
            _peerConnection.GetRtpChannel().OnStunMessageReceived += (msg, ep, isrelay) => logger.LogDebug($"STUN message received from {ep}, message type {msg.Header.MessageType}.");

            var dc = _peerConnection.createDataChannel(DATA_CHANNEL_LABEL, null);
            dc.onmessage += (msg) => logger.LogDebug($"data channel receive ({dc.label}-{dc.id}): {msg}");

            // Add inactive audio and video tracks.
            //MediaStreamTrack audioTrack = new MediaStreamTrack(SDPMediaTypesEnum.audio, false, new List<SDPMediaFormat> { new SDPMediaFormat(SDPMediaFormatsEnum.PCMU) }, MediaStreamStatusEnum.RecvOnly);
            //pc.addTrack(audioTrack);
            //MediaStreamTrack videoTrack = new MediaStreamTrack(SDPMediaTypesEnum.video, false, new List<SDPMediaFormat> { new SDPMediaFormat(SDPMediaFormatsEnum.VP8) }, MediaStreamStatusEnum.Inactive);
            //pc.addTrack(videoTrack);

            _peerConnection.onicecandidateerror += (candidate, error) => logger.LogWarning($"Error adding remote ICE candidate. {error} {candidate}");
            _peerConnection.onconnectionstatechange += (state) =>
            {
                logger.LogDebug($"Peer connection state changed to {state}.");

                if (state == RTCPeerConnectionState.disconnected || state == RTCPeerConnectionState.failed)
                {
                    _peerConnection.Close("remote disconnection");
                }
            };
            _peerConnection.OnReceiveReport += (ep, type, rtcp) => logger.LogDebug($"RTCP {type} report received.");
            _peerConnection.OnRtcpBye += (reason) => logger.LogDebug($"RTCP BYE receive, reason: {(string.IsNullOrWhiteSpace(reason) ? "<none>" : reason)}.");

            _peerConnection.onicecandidate += (candidate) =>
            {
                if (_peerConnection.signalingState == RTCSignalingState.have_local_offer ||
                    _peerConnection.signalingState == RTCSignalingState.have_remote_offer)
                {
                    if (context != null)
                    {
                        context.WebSocket.Send($"candidate:{candidate}");
                    }
                }
            };

            // Peer ICE connection state changes are for ICE events such as the STUN checks completing.
            _peerConnection.oniceconnectionstatechange += (state) =>
            {
                logger.LogDebug($"ICE connection state change to {state}.");
            };

            _peerConnection.ondatachannel += (dc) =>
            {
                logger.LogDebug($"Data channel opened by remote peer, label {dc.label}, stream ID {dc.id}.");
                dc.onmessage += (msg) =>
                {
                    logger.LogDebug($"data channel ({dc.label}:{dc.id}): {msg}.");
                };
            };

            return _peerConnection;
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
                    var res = pc.setRemoteDescription(new RTCSessionDescriptionInit { sdp = message, type = RTCSdpType.offer });
                    if (res != SetDescriptionResultEnum.OK)
                    {
                        // No point continuing. Something will need to change and then try again.
                        pc.Close("failed to set remote sdp");
                    }
                    else
                    {
                        var answer = pc.createAnswer(null);
                        await pc.setLocalDescription(answer);

                        context.WebSocket.Send(answer.sdp);
                    }
                }
                else if (pc.remoteDescription == null)
                {
                    logger.LogDebug("Answer SDP: " + message);
                    var res = pc.setRemoteDescription(new RTCSessionDescriptionInit { sdp = message, type = RTCSdpType.answer });
                    if (res != SetDescriptionResultEnum.OK)
                    {
                        // No point continuing. Something will need to change and then try again.
                        pc.Close("failed to set remote sdp");
                    }
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

        private static async Task<IPAddress> MdnsResolve(string service)
        {
            logger.LogDebug($"MDNS resolve requested for {service}.");

            var query = new Message();
            query.Questions.Add(new Question { Name = service, Type = DnsType.ANY });
            var cancellation = new CancellationTokenSource(MDNS_TIMEOUT);

            using (var mdns = new MulticastService())
            {
                mdns.Start();
                var response = await mdns.ResolveAsync(query, cancellation.Token);

                var ans = response.Answers.Where(x => x.Type == DnsType.A || x.Type == DnsType.AAAA).FirstOrDefault();

                logger.LogDebug($"MDNS result {ans}.");

                switch (ans)
                {
                    case ARecord a:
                        return a.Address;
                    case AAAARecord aaaa:
                        return aaaa.Address;
                    default:
                        return null;
                };
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
