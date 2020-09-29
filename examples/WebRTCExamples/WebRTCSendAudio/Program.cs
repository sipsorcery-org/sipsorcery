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
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
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
        private const string STUN_URL = "stun:stun.sipsorcery.com";
        private const string NODE_DSS_SERVER = "http://192.168.11.50:3000";
        private const string NODE_DSS_MY_USER = "svr";
        private const string NODE_DSS_THEIR_USER = "cli";

        private static Microsoft.Extensions.Logging.ILogger logger = NullLogger.Instance;

        static async Task Main()
        {
            Console.WriteLine("WebRTC Audio Server Example Program");

            logger = AddConsoleLogger();

            //// Start web socket.
            //Console.WriteLine("Starting web socket server...");
            //var webSocketServer = new WebSocketServer(IPAddress.Any, WEBSOCKET_PORT);
            //webSocketServer.AddWebSocketService<WebRTCWebSocketPeer>("/", (peer) => peer.CreatePeerConnection = CreatePeerConnection);
            //webSocketServer.Start();

            //Console.WriteLine($"Waiting for browser web socket connection to {webSocketServer.Address}:{webSocketServer.Port}...");

            var nodeDssclient = new HttpClient();

            Console.WriteLine($"node-dss server successfully set to {NODE_DSS_SERVER}.");
            Console.WriteLine("Press enter to generate offer and send to node server.");
            Console.ReadLine();

            var pc = CreatePeerConnection();

            var offerSdp = pc.createOffer(null);
            await pc.setLocalDescription(offerSdp);

            Console.WriteLine($"Our Offer:\n{offerSdp.sdp}");

            var offerJson = JsonConvert.SerializeObject(offerSdp, new Newtonsoft.Json.Converters.StringEnumConverter());
            await SendToNSS(nodeDssclient, offerJson);

            CancellationTokenSource connectedCts = new CancellationTokenSource();
            pc.onconnectionstatechange += (state) =>
            {
                if (!(state == RTCPeerConnectionState.@new || state == RTCPeerConnectionState.connecting))
                {
                    logger.LogDebug("cancelling node DSS receive task.");
                    connectedCts.Cancel();
                }
            };
            await Task.Run(() => ReceiveFromNSS(nodeDssclient, pc), connectedCts.Token);

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

        private static async Task SendToNSS(HttpClient httpClient, string jsonStr)
        {
            var content = new StringContent(jsonStr, Encoding.UTF8, "application/json");
            var res = await httpClient.PostAsync($"{NODE_DSS_SERVER}/data/{NODE_DSS_THEIR_USER}", content);

            logger.LogDebug($"node-dss POST result {res.StatusCode}.");
        }

        private static async Task ReceiveFromNSS(HttpClient httpClient, RTCPeerConnection pc)
        {
            while (true)
            {
                var res = await httpClient.GetAsync($"{NODE_DSS_SERVER}/data/{NODE_DSS_MY_USER}");

                if (res.StatusCode == HttpStatusCode.OK)
                {
                    var content = await res.Content.ReadAsStringAsync();
                    OnMessage(content, pc);
                }
                else if (res.StatusCode == HttpStatusCode.NotFound)
                {
                    // Expected response when there are no waiting messages for us.
                    await Task.Delay(500);
                }
                else
                {
                    throw new ApplicationException($"Get request to node DSS server failed with response code {res.StatusCode}.");
                }
            }
        }

        private static void OnMessage(string jsonStr, RTCPeerConnection pc)
        {
            if (Regex.Match(jsonStr, @"^[^,]*candidate").Success)
            {
                logger.LogDebug("Got remote ICE candidate.");
                var iceCandidateInit = JsonConvert.DeserializeObject<RTCIceCandidateInit>(jsonStr);
                pc.addIceCandidate(iceCandidateInit);
            }
            else
            {
                RTCSessionDescriptionInit descriptionInit = JsonConvert.DeserializeObject<RTCSessionDescriptionInit>(jsonStr);
                var result = pc.setRemoteDescription(descriptionInit);
                if (result != SetDescriptionResultEnum.OK)
                {
                    logger.LogWarning($"Failed to set remote description, {result}.");
                    pc.Close("failed to set remote description");
                }
            }
        }

        private static RTCPeerConnection CreatePeerConnection()
        {
            RTCConfiguration config = new RTCConfiguration
            {
                iceServers = new List<RTCIceServer> { new RTCIceServer { urls = STUN_URL } }
            };
            //var pc = new RTCPeerConnection(config);
            var pc = new RTCPeerConnection(null);

            AudioExtrasSource audioSource = new AudioExtrasSource(new AudioEncoder(), new AudioSourceOptions { AudioSource = AudioSourcesEnum.SineWave });
            audioSource.OnAudioSourceEncodedSample += pc.SendAudio;

            MediaStreamTrack audioTrack = new MediaStreamTrack(audioSource.GetAudioSourceFormats(), MediaStreamStatusEnum.SendOnly);
            pc.addTrack(audioTrack);

            pc.OnAudioFormatsNegotiated += (sdpFormat) =>
                audioSource.SetAudioSourceFormat(SDPMediaFormatInfo.GetAudioCodecForSdpFormat(sdpFormat.First().FormatCodec));

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

            return pc;
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
