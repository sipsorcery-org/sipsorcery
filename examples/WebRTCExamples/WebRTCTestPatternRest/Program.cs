//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An example WebRTC server application that serves a test pattern
// video stream to a WebRTC enabled browser. This demo uses a REST signaling 
// mechanism to receive an SDP offer from a client peer and respond with the 
// SDP answer.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 28 Feb 2021	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using EmbedIO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorcery.Media;
using SIPSorceryMedia.FFmpeg;

namespace demo
{
    class Program
    {
        private const string HTTP_LISTEN_URL = "http://*:8080/";
        private const int TEST_PATTERN_FRAMES_PER_SECOND = 30;

        private static Microsoft.Extensions.Logging.ILogger logger = NullLogger.Instance;

        static async Task Main()
        {
            Console.WriteLine("WebRTC Test Pattern Server Demo");

            logger = AddConsoleLogger();

            // Start the web server.
            using (var server = CreateWebServer(HTTP_LISTEN_URL))
            {
                await server.RunAsync();
            }
        }

        private static WebServer CreateWebServer(string url)
        {
            var server = new WebServer(o => o
                    .WithUrlPrefix(url)
                    .WithMode(HttpListenerMode.EmbedIO))
                .WithCors("*", "*", "*")
                .WithAction("/offer", HttpVerbs.Post, Offer);
                //.WithStaticFolder("/", "../../html", false);
            server.StateChanged += (s, e) => logger.LogDebug($"WebServer New State - {e.NewState}");

            return server;
        }

        private async static Task Offer(IHttpContext context)
        {
            var offer = await context.GetRequestDataAsync<RTCSessionDescriptionInit>();

            var jsonOptions = new JsonSerializerOptions();
            jsonOptions.Converters.Add(new JsonStringEnumConverter());

            var pc = await CreatePeerConnection();
            var result = pc.setRemoteDescription(offer);

            if (result == SetDescriptionResultEnum.OK)
            {
                var answer = pc.createAnswer(null);
                await pc.setLocalDescription(answer);

                context.Response.ContentType = "application/json";
                using (var responseStm = context.OpenResponseStream(false, false))
                {
                    await JsonSerializer.SerializeAsync(responseStm, answer, jsonOptions);
                }
            }
            else
            {
                throw HttpException.NotAcceptable($"Error setting SDP offer {result}.");
            }
        }

        private static Task<RTCPeerConnection> CreatePeerConnection()
        {
            //RTCConfiguration config = new RTCConfiguration
            //{
            //    iceServers = new List<RTCIceServer> { new RTCIceServer { urls = STUN_URL } },
            //    certificates = new List<RTCCertificate> { new RTCCertificate { Certificate = cert } }
            //};
            //var pc = new RTCPeerConnection(config);
            var pc = new RTCPeerConnection();

            //var testPatternSource = new VideoTestPatternSource(new SIPSorceryMedia.Encoders.VideoEncoder());
            var testPatternSource = new VideoTestPatternSource(new FFmpegVideoEncoder());
            testPatternSource.SetFrameRate(TEST_PATTERN_FRAMES_PER_SECOND);
            //testPatternSource.SetMaxFrameRate(true);
            //var videoEndPoint = new SIPSorceryMedia.FFmpeg.FFmpegVideoEndPoint();
            //videoEndPoint.RestrictFormats(format => format.Codec == VideoCodecsEnum.H264);
            //testPatternSource.RestrictFormats(format => format.Codec == VideoCodecsEnum.H264);
            //var videoEndPoint = new SIPSorceryMedia.Windows.WindowsEncoderEndPoint();
            //var videoEndPoint = new SIPSorceryMedia.Encoders.VideoEncoderEndPoint();

            MediaStreamTrack track = new MediaStreamTrack(testPatternSource.GetVideoSourceFormats(), MediaStreamStatusEnum.SendOnly);
            pc.addTrack(track);

            //testPatternSource.OnVideoSourceRawSample += videoEndPoint.ExternalVideoSourceRawSample;
            testPatternSource.OnVideoSourceEncodedSample += pc.SendVideo;
            pc.OnVideoFormatsNegotiated += (formats) => testPatternSource.SetVideoSourceFormat(formats.First());

            pc.onconnectionstatechange += async (state) =>
            {
                logger.LogDebug($"Peer connection state change to {state}.");

                if (state == RTCPeerConnectionState.failed)
                {
                    pc.Close("ice disconnection");
                }
                else if (state == RTCPeerConnectionState.closed)
                {
                    await testPatternSource.CloseVideo();
                    testPatternSource.Dispose();
                }
                else if (state == RTCPeerConnectionState.connected)
                {
                    await testPatternSource.StartVideo();
                }
            };

            // Diagnostics.
            //pc.OnReceiveReport += (re, media, rr) => logger.LogDebug($"RTCP Receive for {media} from {re}\n{rr.GetDebugSummary()}");
            //pc.OnSendReport += (media, sr) => logger.LogDebug($"RTCP Send for {media}\n{sr.GetDebugSummary()}");
            //pc.GetRtpChannel().OnStunMessageReceived += (msg, ep, isRelay) => logger.LogDebug($"STUN {msg.Header.MessageType} received from {ep}.");
            pc.oniceconnectionstatechange += (state) => logger.LogDebug($"ICE connection state change to {state}.");
            pc.onsignalingstatechange += () =>
            {
                if (pc.signalingState == RTCSignalingState.have_local_offer)
                {
                    logger.LogDebug($"Local SDP set, type {pc.localDescription.type}.");
                    logger.LogDebug(pc.localDescription.sdp.ToString());
                }
                else if (pc.signalingState == RTCSignalingState.have_remote_offer)
                {
                    logger.LogDebug($"Remote SDP set, type {pc.remoteDescription.type}.");
                    logger.LogDebug(pc.remoteDescription.sdp.ToString());
                }
            };

            return Task.FromResult(pc);
        }

        /// <summary>
        ///  Adds a console logger. Can be omitted if internal SIPSorcery debug and warning messages are not required.
        /// </summary>
        private static Microsoft.Extensions.Logging.ILogger AddConsoleLogger()
        {
            var seriLogger = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .MinimumLevel.Is(Serilog.Events.LogEventLevel.Debug)
                .WriteTo.Console()
                .CreateLogger();
            var factory = new SerilogLoggerFactory(seriLogger);
            SIPSorcery.LogFactory.Set(factory);
            return factory.CreateLogger<Program>();
        }
    }
}
