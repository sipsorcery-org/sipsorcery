//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An example WebRTC application that can be used to act as a bridge
// between a browser based WebRTC peer and OpenAI's real-time API
// https://platform.openai.com/docs/guides/realtime-webrtc.
//
// Browser clients can connect directly to OpenAI. The reason to use a bridging
// asp.net app is to control and utilise the interaction on the asp.net app.
// For example the asp.net could provide a local function to look some DB info etc.
// based on user request.
//
// Usage:
// set OPENAIKEY=your_openai_key
// dotnet run
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 27 Apr 2025	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using LanguageExt;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorcery.Net;

namespace demo;

class Program
{
    private static Microsoft.Extensions.Logging.ILogger _logger = NullLogger.Instance;   

    static async Task Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
        .MinimumLevel.Debug()
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .CreateLogger();

        var factory = new SerilogLoggerFactory(Log.Logger);
        SIPSorcery.LogFactory.Set(factory);
        _logger = factory.CreateLogger<Program>();

        Log.Information("WebRTC OpenAI Browser Bridge Demo Program");

        var openAiKey = Environment.GetEnvironmentVariable("OPENAIKEY") ?? string.Empty;

        if (string.IsNullOrWhiteSpace(openAiKey))
        {
            Log.Logger.Error("Please provide your OpenAI key as an environment variable. For example: set OPENAIKEY=<your openai api key>");
            return;
        }

        var builder = WebApplication.CreateBuilder();

        builder.Host.UseSerilog();

        builder.Services.AddLogging(builder =>
        {
            builder.AddSerilog(dispose: true);
        });

        builder.Services
          .AddHttpClient()
          .AddHttpClient(OpenAIRealtimeRestClient.OPENAI_HTTP_CLIENT_NAME, client =>
          {
              client.Timeout = TimeSpan.FromSeconds(5);
              client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", openAiKey);
          });
        builder.Services.AddTransient<IOpenAIRealtimeRestClient, OpenAIRealtimeRestClient>();
        builder.Services.AddTransient<IOpenAIRealtimeWebRTCEndPoint, OpenAIRealtimeWebRTCEndPoint>();

        var app = builder.Build();

        app.UseDefaultFiles();
        app.UseStaticFiles();
        var webSocketOptions = new WebSocketOptions
        {
            KeepAliveInterval = TimeSpan.FromMinutes(2)
        };

        app.UseWebSockets(webSocketOptions);

        app.Map("/ws", async (HttpContext context,
            [FromServices] IOpenAIRealtimeWebRTCEndPoint openAiWebRTCEndPoint) =>
        {
            Log.Debug("Web socket client connection established.");

            if (context.WebSockets.IsWebSocketRequest)
            {
                var webSocket = await context.WebSockets.AcceptWebSocketAsync();

                RTCConfiguration config = new RTCConfiguration
                {
                    X_ICEIncludeAllInterfaceAddresses = true
                };

                var webSocketPeer = new WebRTCWebSocketPeerAspNet(
                    webSocket,
                    CreateBrowserPeerConnection,
                    config,
                    RTCSdpType.offer);

                var browserPeerTask = webSocketPeer.Run();

                SetOpenAIPeerEventHandlers(openAiWebRTCEndPoint);
                var openAiPeerTask = openAiWebRTCEndPoint.StartConnectAsync(config);

                await Task.WhenAll(browserPeerTask, openAiPeerTask);

                ConnectPeers(webSocketPeer.RTCPeerConnection, openAiWebRTCEndPoint.PeerConnection!);

                //await Task.Delay(30000);

                Log.Debug("Web socket closing with WebRTC peer connection in state {state}.", webSocketPeer.RTCPeerConnection?.connectionState);
            }
            else
            {
                // Not a WebSocket request
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
            }
        });

        await app.RunAsync();
    }

    private static void SetOpenAIPeerEventHandlers(IOpenAIRealtimeWebRTCEndPoint webrtcEndPoint)
    {
        webrtcEndPoint.OnPeerConnectionConnected += () =>
        {
            Log.Logger.Information("WebRTC peer connection established.");

            // Trigger the conversation by sending a response create message.
            var result = webrtcEndPoint.SendResponseCreate(OpenAIVoicesEnum.shimmer, "Say Hi!");
            if (result.IsLeft)
            {
                Log.Logger.Error($"Failed to send response create message: {result.LeftAsEnumerable().First()}");
            }
        };

        webrtcEndPoint.OnDataChannelMessageReceived += (dc, message) =>
        {
            if (message is OpenAIResponseAudioTranscriptDone done)
            {
                Log.Information($"Transcript done: {done.Transcript}");
            }
        };
    }

    private static void ConnectPeers(RTCPeerConnection browserPeerConnection, RTCPeerConnection openAiPeerConnection)
    {
        uint rtpPreviousTimestampForOpenAI = 0;
        browserPeerConnection.OnRtpPacketReceived += (IPEndPoint rep, SDPMediaTypesEnum media, RTPPacket rtpPkt) =>
        {
            if (rtpPreviousTimestampForOpenAI == 0)
            {
                rtpPreviousTimestampForOpenAI = rtpPkt.Header.Timestamp;
            }
            else
            {
                uint rtpDuration = rtpPkt.Header.Timestamp - rtpPreviousTimestampForOpenAI;
                rtpPreviousTimestampForOpenAI = rtpPkt.Header.Timestamp;

                openAiPeerConnection.SendAudio(rtpDuration, rtpPkt.Payload);
            }
        };

        uint rtpPreviousTimestampForBrowser = 0;
        openAiPeerConnection.OnRtpPacketReceived += (IPEndPoint rep, SDPMediaTypesEnum media, RTPPacket rtpPkt) =>
        {
            if (rtpPreviousTimestampForBrowser == 0)
            {
                rtpPreviousTimestampForBrowser = rtpPkt.Header.Timestamp;
            }
            else
            {
                uint rtpDuration = rtpPkt.Header.Timestamp - rtpPreviousTimestampForBrowser;
                rtpPreviousTimestampForBrowser = rtpPkt.Header.Timestamp;

                browserPeerConnection.SendAudio(rtpDuration, rtpPkt.Payload);
            }
        };

        browserPeerConnection.OnClosed += () => openAiPeerConnection.Close("Browser peer closed.");
    }

     /// <summary>
    /// Method to create the peer connection with the browser.
    /// </summary>
    /// <param name="onConnectedSemaphore">A semaphore that will get set when the data channel on the peer connection is opened. Since the data channel
    /// can only be opened once the peer connection is open this indicates both are ready for use.</param>
    private static Task<RTCPeerConnection> CreateBrowserPeerConnection(RTCConfiguration pcConfig)
    {
        var peerConnection = new RTCPeerConnection(pcConfig);

        var audioEncoder = new AudioEncoder(includeOpus: true);
        var opusFormat = audioEncoder.SupportedFormats.Single(x => x.FormatName == "OPUS");
        MediaStreamTrack audioTrack = new MediaStreamTrack(opusFormat, MediaStreamStatusEnum.SendRecv);
        peerConnection.addTrack(audioTrack);

        peerConnection.OnAudioFormatsNegotiated += (audioFormats) =>
        {
            _logger.LogDebug($"Audio format negotiated {audioFormats.First().FormatName}.");
        };
        //peerConnection.OnReceiveReport += RtpSession_OnReceiveReport;
        //peerConnection.OnSendReport += RtpSession_OnSendReport;
        peerConnection.OnTimeout += (mediaType) => _logger.LogDebug($"Timeout on media {mediaType}.");
        peerConnection.oniceconnectionstatechange += (state) => _logger.LogDebug($"ICE connection state changed to {state}.");
        peerConnection.onconnectionstatechange += (state) => _logger.LogDebug($"Peer connection connected changed to {state}.");
        
        peerConnection.onsignalingstatechange += () =>
        {
            if (peerConnection.signalingState == RTCSignalingState.have_local_offer)
            {
                _logger.LogDebug("Local SDP:\n{sdp}", peerConnection.localDescription.sdp);
            }
            else if (peerConnection.signalingState is RTCSignalingState.have_remote_offer or RTCSignalingState.stable)
            {
                _logger.LogDebug("Remote SDP:\n{sdp}", peerConnection.remoteDescription.sdp);
            }
        };

        return Task.FromResult(peerConnection);
    }
}
