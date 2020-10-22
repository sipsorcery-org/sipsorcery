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
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommandLine;
using Makaretu.Dns;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorcery.Sys;
using WebSocketSharp.Net.WebSockets;
using WebSocketSharp.Server;

namespace SIPSorcery.Examples
{
    public class Options
    {
        [Option("ws", Required = false,
            HelpText = "(default) Create a web socket server to act as a signalling channel for exchanging SDP and ICE candidates with a remote WebRTC peer.")]
        public bool UseWebSocket { get; set; }

        [Option("wss", Required = false,
            HelpText = "Create a secure web socket server to act as a signalling channel for exchanging SDP and ICE candidates with a remote WebRTC peer.")]
        public bool UseSecureWebSocket { get; set; }

        [Option("wsserver", Required = false,
           HelpText = "The address of a web socket server to connect to establish a WebRTC connection. Format \"--wsserver=ws://127.0.0.1:8081\".")]
        public string WebSocketServer { get; set; }

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
            HelpText = "Address and ID's for a node-dss simple signalling server to exchange SDP and ice candidates. Format \"--nodedss=http://127.0.0.1:3001;myid;theirid\".")]
        public string NodeDssServer { get; set; }

        [Option("icetypes", Required = false,
            HelpText = "Only generate ICE candidates of these types. Format \"--icetypes=(host|srflx|relay)\".")]
        public string IceTypes { get; set; }

        [Option("accepticetypes", Required = false,
            HelpText = "Only accept ICE candidates of these types from the remote peer and ignore any others. Format \"--accepticetypes=(host|srflx|relay)\".")]
        public string AcceptIceTypes { get; set; }
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

        private static Microsoft.Extensions.Logging.ILogger logger = NullLogger.Instance;

        private static WebSocketServer _webSocketServer;
        private static RTCIceServer _stunServer;
        private static bool _relayOnly;

        /// <summary>
        /// If non-empty means only transmit ICE candidates if they have a type matching this list.
        /// </summary>
        private static List<RTCIceCandidateType> _iceTypes = new List<RTCIceCandidateType>();

        /// <summary>
        /// If non-empty means only accept ICE candidates from the remote peer if they have a type 
        /// matching this list.
        /// </summary>
        private static List<RTCIceCandidateType> _acceptIceTypes = new List<RTCIceCandidateType>();

        /// <summary>
        /// For simplicity this program only supports one active peer connection.
        /// </summary>
        private static RTCPeerConnection _peerConnection;

        private static RTCOfferOptions _offerOptions;
        private static RTCAnswerOptions _answerOptions;

        static void Main(string[] args)
        {
            Console.WriteLine("WebRTC Console Test Program");
            Console.WriteLine("Press ctrl-c to exit.");

            //var cert = DtlsUtils.CreateSelfSignedCert();
            //Console.WriteLine(Convert.ToBase64String(cert.Export(X509ContentType.Pfx)));

            bool noOptions = args?.Count() == 0;

            var result = Parser.Default.ParseArguments<Options>(args)
                .WithParsed<Options>(opts => RunCommand(opts, noOptions).Wait());
        }

        private static async Task RunCommand(Options options, bool noOptions)
        {
            // Plumbing code to facilitate a graceful exit.
            CancellationTokenSource exitCts = new CancellationTokenSource(); // Cancellation token to stop the SIP transport and RTP stream.
            //ManualResetEvent exitMre = new ManualResetEvent(false);

            logger = AddConsoleLogger();

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

            if (!string.IsNullOrEmpty(options.IceTypes))
            {
                options.IceTypes.Split().ToList().ForEach(x =>
                {
                    if (Enum.TryParse<RTCIceCandidateType>(x, out var iceType))
                    {
                        _iceTypes.Add(iceType);
                    }
                });

                if (!_iceTypes.Any(x => x == RTCIceCandidateType.host))
                {
                    _offerOptions = new RTCOfferOptions { X_ExcludeIceCandidates = true };
                    _answerOptions = new RTCAnswerOptions { X_ExcludeIceCandidates = true };
                }
            }

            if (!string.IsNullOrEmpty(options.AcceptIceTypes))
            {
                options.AcceptIceTypes.Split().ToList().ForEach(x =>
                {
                    if (Enum.TryParse<RTCIceCandidateType>(x, out var iceType))
                    {
                        _acceptIceTypes.Add(iceType);
                    }
                });
            }

            if (options.UseWebSocket || options.UseSecureWebSocket || noOptions)
            {
                // Start web socket.
                Console.WriteLine("Starting web socket server...");
                _webSocketServer = new WebSocketServer(IPAddress.Any, WEBSOCKET_PORT, options.UseSecureWebSocket);
                if (options.UseSecureWebSocket)
                {
                    _webSocketServer.SslConfiguration.ServerCertificate = new X509Certificate2(LOCALHOST_CERTIFICATE_PATH);
                    _webSocketServer.SslConfiguration.CheckCertificateRevocation = false;
                }
                _webSocketServer.AddWebSocketService<WebRTCWebSocketPeer>("/", (peer) =>
                {
                    peer.OfferOptions = _offerOptions;
                    if (_acceptIceTypes != null && _acceptIceTypes.Count > 0)
                    {
                        peer.FilterRemoteICECandidates = (init) => _acceptIceTypes.Any(x => x == RTCIceCandidate.Parse(init.candidate).type);
                    }
                    peer.CreatePeerConnection = CreatePeerConnection;
                });
                _webSocketServer.Start();

                Console.WriteLine($"Waiting for browser web socket connection to {_webSocketServer.Address}:{_webSocketServer.Port}...");
            }
            else if (!string.IsNullOrWhiteSpace(options.WebSocketServer))
            {
                // We are the client for a web socket server. The JSON signalling exchange still occurs the same way as when the web socket
                // server option is used except that as the web socket client we receive the SDP offer from the server.
                WebRTCWebSocketClient wsockClient = new WebRTCWebSocketClient(options.WebSocketServer, CreatePeerConnection);
                await wsockClient.Start(exitCts.Token);
                Console.WriteLine("web socket client started.");
            }
            else if (options.CreateJsonOffer)
            {
                var pc = await Createpc(null, _stunServer, _relayOnly);

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
                string[] fields = options.NodeDssServer.Split(';');
                if (fields.Length < 3)
                {
                    throw new ArgumentException("The 'nodedss' option must contain 3 semi-colon separated fields, e.g. --nodedss=http://127.0.0.1:3000;myid;theirid.");
                }

                var nodeDssPeer = new WebRTCNodeDssPeer(fields[0], fields[1], fields[2], CreatePeerConnection);
                await nodeDssPeer.Start(exitCts);
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
        private static void ProcessInput(CancellationTokenSource cts)
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

                            //case var x when x.StartsWith("node"):
                            //    (_, var sdpType, var myUser, string theirUser) = x.Split(" ", 4, StringSplitOptions.None);

                            //    if (sdpType == "so")
                            //    {
                            //        _peerConnection = Createpc(null, _stunServer, _relayOnly);

                            //        var offerSdp = _peerConnection.createOffer(null);
                            //        await _peerConnection.setLocalDescription(offerSdp);

                            //        Console.WriteLine($"Our Offer:\n{offerSdp.sdp}");

                            //        var offerJson = JsonConvert.SerializeObject(offerSdp, new Newtonsoft.Json.Converters.StringEnumConverter());

                            //        var content = new StringContent(offerJson, Encoding.UTF8, "application/json");
                            //        var res = await _nodeDssclient.PostAsync($"{_nodeDssUri}data/{theirUser}", content);

                            //        Console.WriteLine($"node-dss POST result {res.StatusCode}.");
                            //    }
                            //    else if (sdpType == "go")
                            //    {
                            //        var res = await _nodeDssclient.GetAsync($"{_nodeDssUri}data/{myUser}");

                            //        Console.WriteLine($"node-dss GET result {res.StatusCode}.");

                            //        if (res.StatusCode == HttpStatusCode.OK)
                            //        {
                            //            var content = await res.Content.ReadAsStringAsync();
                            //            RTCSessionDescriptionInit offerInit = JsonConvert.DeserializeObject<RTCSessionDescriptionInit>(content);

                            //            Console.WriteLine($"Remote offer:\n{offerInit.sdp}");

                            //            _peerConnection = Createpc(null, _stunServer, _relayOnly);

                            //            var setRes = _peerConnection.setRemoteDescription(offerInit);
                            //            if (setRes != SetDescriptionResultEnum.OK)
                            //            {
                            //                // No point continuing. Something will need to change and then try again.
                            //                _peerConnection.Close("failed to set remote sdp offer");
                            //            }
                            //            else
                            //            {
                            //                var answer = _peerConnection.createAnswer(null);
                            //                await _peerConnection.setLocalDescription(answer);

                            //                Console.WriteLine($"Our answer:\n{answer.sdp}");

                            //                var answerJson = JsonConvert.SerializeObject(answer, new Newtonsoft.Json.Converters.StringEnumConverter());
                            //                var answerContent = new StringContent(answerJson, Encoding.UTF8, "application/json");
                            //                var postRes = await _nodeDssclient.PostAsync($"{_nodeDssUri}data/{theirUser}", answerContent);

                            //                Console.WriteLine($"node-dss POST result {res.StatusCode}.");
                            //            }
                            //        }
                            //    }
                            //    else if (sdpType == "ga")
                            //    {
                            //        var res = await _nodeDssclient.GetAsync($"{_nodeDssUri}data/{myUser}");

                            //        Console.WriteLine($"node-dss GET result {res.StatusCode}.");

                            //        if (res.StatusCode == HttpStatusCode.OK)
                            //        {
                            //            var content = await res.Content.ReadAsStringAsync();
                            //            RTCSessionDescriptionInit answerInit = JsonConvert.DeserializeObject<RTCSessionDescriptionInit>(content);

                            //            Console.WriteLine($"Remote answer:\n{answerInit.sdp}");

                            //            var setRes = _peerConnection.setRemoteDescription(answerInit);
                            //            if (setRes != SetDescriptionResultEnum.OK)
                            //            {
                            //                // No point continuing. Something will need to change and then try again.
                            //                _peerConnection.Close("failed to set remote sdp answer");
                            //            }
                            //        }
                            //    }
                            //    break;

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

        private static Task<RTCPeerConnection> CreatePeerConnection()
        {
            return Createpc(null, _stunServer, false);
        }

        private static Task<RTCPeerConnection> Createpc(WebSocketContext context, RTCIceServer stunServer, bool relayOnly)
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
                //certificates = presetCertificates,
                //X_RemoteSignallingAddress = (context != null) ? context.UserEndPoint.Address : null,
                iceServers = stunServer != null ? new List<RTCIceServer> { stunServer } : null,
                iceTransportPolicy = relayOnly ? RTCIceTransportPolicy.relay : RTCIceTransportPolicy.all,
                //X_BindAddress = IPAddress.Any, // NOTE: Not reqd. Using this to filter out IPv6 addresses so can test with Pion.
            };

            _peerConnection = new RTCPeerConnection(pcConfiguration);

            //_peerConnection.GetRtpChannel().MdnsResolve = (hostname) => Task.FromResult(NetServices.InternetDefaultAddress);
            _peerConnection.GetRtpChannel().MdnsResolve = MdnsResolve;
            //_peerConnection.GetRtpChannel().OnStunMessageReceived += (msg, ep, isrelay) => logger.LogDebug($"STUN message received from {ep}, message type {msg.Header.MessageType}.");

            //var dc = _peerConnection.createDataChannel(DATA_CHANNEL_LABEL, null);
            //dc.onmessage += (msg) => logger.LogDebug($"data channel receive ({dc.label}-{dc.id}): {msg}");

            // Add a send-only audio track (this doesn't require any native libraries for encoding so is good for x-platform testing).
            AudioExtrasSource audioSource = new AudioExtrasSource(new AudioEncoder(), new AudioSourceOptions { AudioSource = AudioSourcesEnum.Music });
            audioSource.OnAudioSourceEncodedSample += _peerConnection.SendAudio;

            MediaStreamTrack audioTrack = new MediaStreamTrack(audioSource.GetAudioSourceFormats(), MediaStreamStatusEnum.SendOnly);
            _peerConnection.addTrack(audioTrack);

            _peerConnection.OnAudioFormatsNegotiated += (formats) =>
                audioSource.SetAudioSourceFormat(formats.First());

            _peerConnection.onicecandidateerror += (candidate, error) => logger.LogWarning($"Error adding remote ICE candidate. {error} {candidate}");
            _peerConnection.onconnectionstatechange += async (state) =>
            {
                logger.LogDebug($"Peer connection state changed to {state}.");

                if (state == RTCPeerConnectionState.disconnected || state == RTCPeerConnectionState.failed)
                {
                    _peerConnection.Close("remote disconnection");
                }

                if (state == RTCPeerConnectionState.connected)
                {
                    await audioSource.StartAudio();
                }
                else if (state == RTCPeerConnectionState.closed)
                {
                    await audioSource.CloseAudio();
                }
            };
            _peerConnection.OnReceiveReport += (re, media, rr) => logger.LogDebug($"RTCP Receive for {media} from {re}\n{rr.GetDebugSummary()}");
            _peerConnection.OnSendReport += (media, sr) => logger.LogDebug($"RTCP Send for {media}\n{sr.GetDebugSummary()}");
            _peerConnection.OnRtcpBye += (reason) => logger.LogDebug($"RTCP BYE receive, reason: {(string.IsNullOrWhiteSpace(reason) ? "<none>" : reason)}.");

            // Peer ICE connection state changes are for ICE events such as the STUN checks completing.
            _peerConnection.oniceconnectionstatechange += (state) => logger.LogDebug($"ICE connection state change to {state}.");

            _peerConnection.ondatachannel += (dc) =>
            {
                logger.LogDebug($"Data channel opened by remote peer, label {dc.label}, stream ID {dc.id}.");
                dc.onmessage += (msg) =>
                {
                    logger.LogDebug($"data channel ({dc.label}:{dc.id}): {msg}.");
                };
            };

            return Task.FromResult(_peerConnection);
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
        private static Microsoft.Extensions.Logging.ILogger AddConsoleLogger()
        {
            var loggerConfig = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .MinimumLevel.Is(Serilog.Events.LogEventLevel.Debug)
                .WriteTo.Console()
                .CreateLogger();
            var factory = new SerilogLoggerFactory(loggerConfig);
            SIPSorcery.LogFactory.Set(factory);
            return factory.CreateLogger<Program>();
        }
    }
}
