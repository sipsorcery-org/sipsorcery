//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An example WebRTC server application that serves a sine wave 
// audio stream to a WebRTC enabled browser.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 28 Jul 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorcery.Media;
using WebSocketSharp.Server;

namespace demo
{
    class Program
    {
        private const int WEBSOCKET_PORT = 8081;

        private static Microsoft.Extensions.Logging.ILogger logger = NullLogger.Instance;

        static void Main()
        {
            Console.WriteLine("WebRTC Audio Server Example Program");
            
            logger = AddConsoleLogger();

            // Start web socket.
            Console.WriteLine("Starting web socket server...");
            var webSocketServer = new WebSocketServer(IPAddress.Any, WEBSOCKET_PORT);
            webSocketServer.AddWebSocketService<WebRTCWebSocketPeer>("/", (peer) => peer.CreatePeerConnection = CreatePeerConnection);
            webSocketServer.Start();

            Console.WriteLine($"Waiting for browser web socket connection to {webSocketServer.Address}:{webSocketServer.Port}...");

            Console.WriteLine("Press ctrl-c to exit.");
            ManualResetEvent exitMre = new ManualResetEvent(false);
            Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                exitMre.Set();
            };

            // Wait for a signal saying the call failed, was cancelled with ctrl-c or completed.
            exitMre.WaitOne();
        }

        private static Task<RTCPeerConnection> CreatePeerConnection()
        {
            RTCConfiguration config = new RTCConfiguration
            {
                //iceServers = new List<RTCIceServer> { new RTCIceServer { urls = STUN_URL } }
            };
            var pc = new RTCPeerConnection(config);
           
            AudioExtrasSource audioSource = new AudioExtrasSource(new AudioEncoder(includeOpus: false), new AudioSourceOptions { AudioSource = AudioSourcesEnum.SineWave });
            //audioSource.RestrictFormats(x => x.FormatName == "OPUS");
            audioSource.OnAudioSourceEncodedSample += pc.SendAudio;

            MediaStreamTrack audioTrack = new MediaStreamTrack(audioSource.GetAudioSourceFormats(), MediaStreamStatusEnum.SendOnly);
            pc.addTrack(audioTrack);

            pc.OnAudioFormatsNegotiated += (audioFormats) => audioSource.SetAudioSourceFormat(audioFormats.First());

            pc.onconnectionstatechange += async (state) =>
            {
                logger.LogDebug($"Peer connection state change to {state}.");

                if (state == RTCPeerConnectionState.connected)
                {
                    await audioSource.StartAudio();
                }
                else if (state == RTCPeerConnectionState.failed)
                {
                    pc.Close("ice disconnection");
                }
                else if (state == RTCPeerConnectionState.closed)
                {
                    await audioSource.CloseAudio();
                }
            };

            // Diagnostics.
            pc.OnReceiveReport += (re, media, rr) => logger.LogDebug($"RTCP Receive for {media} from {re}\n{rr.GetDebugSummary()}");
            pc.OnSendReport += (media, sr) => logger.LogDebug($"RTCP Send for {media}\n{sr.GetDebugSummary()}");
            pc.GetRtpChannel().OnStunMessageReceived += (msg, ep, isRelay) => logger.LogDebug($"STUN {msg.Header.MessageType} received from {ep}.");
            pc.oniceconnectionstatechange += (state) => logger.LogDebug($"ICE connection state change to {state}.");
            pc.onsignalingstatechange += () =>
            {
                logger.LogDebug($"Signalling state change to {pc.signalingState}.");
                if (pc.signalingState == RTCSignalingState.have_local_offer)
                {
                    logger.LogDebug("Offer SDP:");
                    logger.LogDebug(pc.localDescription.sdp.ToString());
                }
                else if (pc.signalingState == RTCSignalingState.have_remote_offer || pc.signalingState == RTCSignalingState.stable)
                {
                    logger.LogDebug("Answer SDP:");
                    logger.LogDebug(pc.remoteDescription.sdp.ToString());
                }
            };

            return Task.FromResult(pc);
        }

        /// <summary>
        /// Adds a console logger. Can be omitted if internal SIPSorcery debug and warning messages are not required.
        /// </summary>
        private static Microsoft.Extensions.Logging.ILogger AddConsoleLogger()
        {
            var serilogLogger = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .MinimumLevel.Is(Serilog.Events.LogEventLevel.Debug)
                .WriteTo.Console()
                .CreateLogger();
            var factory = new SerilogLoggerFactory(serilogLogger);
            SIPSorcery.LogFactory.Set(factory);
            return factory.CreateLogger<Program>();
        }
    }
}
