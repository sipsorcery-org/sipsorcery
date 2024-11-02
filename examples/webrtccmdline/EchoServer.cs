//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: Implements a WebRTC Echo Test Server suitable for interoperability
// testing as per specification at:
// https://github.com/sipsorcery/webrtc-echoes/blob/master/doc/EchoTestSpecification.md
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 19 Feb 2021	Aaron Clauson	Created, Dublin, Ireland.
// 14 Apr 2021  Aaron Clauson   Added data channel support.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EmbedIO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.Examples
{
    public class EchoServerOptions
    {
        public const string DEFAULT_WEBSERVER_LISTEN_URL = "http://*:8080/";
        public const LogEventLevel DEFAULT_VERBOSITY = LogEventLevel.Debug;
        public const int TEST_TIMEOUT_SECONDS = 10;

        //[Option('l', "listen", Required = false, Default = DEFAULT_WEBSERVER_LISTEN_URL,
        //    HelpText = "The URL the web server will listen on.")]
        //public string ServerUrl { get; set; }

        //[Option("timeout", Required = false, Default = TEST_TIMEOUT_SECONDS,
        //    HelpText = "Timeout in seconds to close the peer connection. Set to 0 for no timeout.")]
        //public int TestTimeoutSeconds { get; set; }

        //[Option('v', "verbosity", Required = false, Default = DEFAULT_VERBOSITY,
        //    HelpText = "The log level verbosity (0=Verbose, 1=Debug, 2=Info, 3=Warn...).")]
        //public LogEventLevel Verbosity { get; set; }
    }

    public class EchoServer
    {
        private static Microsoft.Extensions.Logging.ILogger logger = NullLogger.Instance;

        private static List<IPAddress> _icePresets = new List<IPAddress>();

        public void Start()
        {
            // Apply any command line options
            //if (args.Length > 0)
            //{
            //    url = args[0];
            //    for(int i=1; i<args.Length; i++)
            //    {
            //        if(IPAddress.TryParse(args[i], out var addr))
            //        {
            //            _icePresets.Add(addr);
            //            Console.WriteLine($"ICE candidate preset address {addr} added.");
            //        }
            //    }
            //}

            string listenUrl = EchoServerOptions.DEFAULT_WEBSERVER_LISTEN_URL;
            LogEventLevel verbosity = EchoServerOptions.DEFAULT_VERBOSITY;
            int pcTimeout = EchoServerOptions.TEST_TIMEOUT_SECONDS;

            //if (args != null)
            //{
            //    Options opts = null;
            //    var parseResult = Parser.Default.ParseArguments<Options>(args)
            //        .WithParsed(o => opts = o);

            //    listenUrl = opts != null && !string.IsNullOrEmpty(opts.ServerUrl) ? opts.ServerUrl : listenUrl;
            //    verbosity = opts != null ? opts.Verbosity : verbosity;
            //    pcTimeout = opts != null ? opts.TestTimeoutSeconds : pcTimeout;
            //}

            logger = AddConsoleLogger(verbosity);

            // Start the web server.
            using (var server = CreateWebServer(listenUrl, pcTimeout))
            {
                server.RunAsync();

                Console.WriteLine("ctrl-c to exit.");
                var mre = new ManualResetEvent(false);
                Console.CancelKeyPress += (sender, eventArgs) =>
                {
                    // cancel the cancellation to allow the program to shutdown cleanly
                    eventArgs.Cancel = true;
                    mre.Set();
                };

                mre.WaitOne();
            }
        }

        private static WebServer CreateWebServer(string url, int pcTimeout)
        {
            var server = new WebServer(o => o
                    .WithUrlPrefix(url)
                    .WithMode(HttpListenerMode.EmbedIO))
                .WithCors("*", "*", "*")
                .WithAction("/offer", HttpVerbs.Post, (ctx) => Offer(ctx, pcTimeout))
                .WithAction("/icecandidate", HttpVerbs.Post, (ctx) => Offer(ctx, pcTimeout));
            //.WithStaticFolder("/", "../../html", false);
            server.StateChanged += (s, e) => Console.WriteLine($"WebServer New State - {e.NewState}");

            return server;
        }

        private async static Task Offer(IHttpContext context, int pcTimeout)
        {
            var offer = await context.GetRequestDataAsync<RTCSessionDescriptionInit>();

            var jsonOptions = new JsonSerializerOptions();
            jsonOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());

            var echoServer = new WebRTCEchoServer(_icePresets);
            var pc = await echoServer.GotOffer(offer);

            if (pc != null)
            {
                var answer = new RTCSessionDescriptionInit { type = RTCSdpType.answer, sdp = pc.localDescription.sdp.ToString() };
                context.Response.ContentType = "application/json";
                using (var responseStm = context.OpenResponseStream(false, false))
                {
                    await JsonSerializer.SerializeAsync(responseStm, answer, jsonOptions);
                }

                if (pcTimeout != 0)
                {
                    logger.LogDebug($"Setting peer connection close timeout to {pcTimeout} seconds.");

                    var timeout = new Timer((state) =>
                    {
                        if (!pc.IsClosed)
                        {
                            logger.LogWarning("Test timed out.");
                            pc.close();
                        }
                    }, null, pcTimeout * 1000, Timeout.Infinite);
                    pc.OnClosed += timeout.Dispose;
                }
            }
        }

        private static Microsoft.Extensions.Logging.ILogger AddConsoleLogger(
            LogEventLevel logLevel = LogEventLevel.Debug)
        {
            var serilogLogger = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .MinimumLevel.Is(logLevel)
                .WriteTo.Console()
                .CreateLogger();
            var factory = new SerilogLoggerFactory(serilogLogger);
            SIPSorcery.LogFactory.Set(factory);
            return factory.CreateLogger<Program>();
        }
    }

    public class WebRTCEchoServer
    {
        private const int VP8_PAYLOAD_ID = 96;

        private static Microsoft.Extensions.Logging.ILogger logger = NullLogger.Instance;

        private List<IPAddress> _presetIceAddresses;

        public WebRTCEchoServer(List<IPAddress> presetAddresses)
        {
            logger = SIPSorcery.LogFactory.CreateLogger<WebRTCEchoServer>();
            _presetIceAddresses = presetAddresses;
        }

        public async Task<RTCPeerConnection> GotOffer(RTCSessionDescriptionInit offer)
        {
            logger.LogTrace($"SDP offer received.");
            logger.LogTrace(offer.sdp);

            var pc = new RTCPeerConnection();

            if (_presetIceAddresses != null)
            {
                foreach (var addr in _presetIceAddresses)
                {
                    var rtpPort = pc.GetRtpChannel().RTPPort;
                    var publicIPv4Candidate = new RTCIceCandidate(RTCIceProtocol.udp, addr, (ushort)rtpPort, RTCIceCandidateType.host);
                    pc.addLocalIceCandidate(publicIPv4Candidate);
                }
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
            };

            pc.OnTimeout += (mediaType) => logger.LogWarning($"Timeout for {mediaType}.");
            pc.oniceconnectionstatechange += (state) => logger.LogInformation($"ICE connection state changed to {state}.");
            pc.onsignalingstatechange += () => logger.LogInformation($"Signaling state changed to {pc.signalingState}.");
            pc.onconnectionstatechange += (state) =>
            {
                logger.LogInformation($"Peer connection state changed to {state}.");
                if (state == RTCPeerConnectionState.failed)
                {
                    pc.Close("ice failure");
                }
            };

            pc.ondatachannel += (dc) =>
            {
                logger.LogInformation($"Data channel opened for label {dc.label}, stream ID {dc.id}.");
                dc.onmessage += (rdc, proto, data) =>
                {
                    logger.LogInformation($"Data channel got message: {Encoding.UTF8.GetString(data)}");
                    rdc.send(Encoding.UTF8.GetString(data));
                };
            };

            var setResult = pc.setRemoteDescription(offer);
            if (setResult == SetDescriptionResultEnum.OK)
            {
                var answer = pc.createAnswer();
                await pc.setLocalDescription(answer);

                logger.LogTrace($"SDP answer created.");
                logger.LogTrace(answer.sdp);

                return pc;
            }
            else
            {
                logger.LogWarning($"Failed to set remote description {setResult}.");
                return null;
            }
        }
    }
}
