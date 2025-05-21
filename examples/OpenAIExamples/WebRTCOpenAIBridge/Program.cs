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
// BSD 3-Clause "New" or "Revised" License and the additional
// BDS BY-NC-SA restriction, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
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
using SIPSorcery.Net;
using SIPSorcery.OpenAI.RealtimeWebRTC;
using SIPSorceryMedia.Abstractions;

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

        builder.Services.AddOpenAIRealtimeWebRTC(openAiKey);

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

                ConnectPeers(webSocketPeer.RTCPeerConnection, openAiWebRTCEndPoint);

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

    private static void ConnectPeers(RTCPeerConnection browserPc, IOpenAIRealtimeWebRTCEndPoint openAiEndPoint)
    {
        if (browserPc != null && openAiEndPoint?.PeerConnection != null)
        {
            // Send RTP audio payloads recied from the brower WebRTC peer connection to OpenAI.
            browserPc.OnRtpPacketReceived += openAiEndPoint.SendAudioFromRtpPacket;

            // Send RTP audio payloads received from OpenAI to the browser WebRTC peer connection.
            openAiEndPoint.OnRtpPacketReceived += (IPEndPoint rep, SDPMediaTypesEnum media, RTPPacket rtpPkt, uint rtpDuration) =>
                browserPc.SendAudio(rtpDuration, rtpPkt.Payload);

            // If the browser peer connection closes we need to close the OpenAI peer connection too.
            browserPc.OnClosed += () => openAiEndPoint.PeerConnection?.Close("Browser peer closed.");

            // If the OpenAI peer connection closes we need to close the browser peer connection too.
            openAiEndPoint.PeerConnection.OnClosed += () => browserPc.Close("OpenAI peer closed.");
        }
    }

    /// <summary>
    /// Method to create the peer connection with the browser.
    /// </summary>
    private static Task<RTCPeerConnection> CreateBrowserPeerConnection(RTCConfiguration pcConfig)
    {
        var peerConnection = new RTCPeerConnection(pcConfig);

        MediaStreamTrack audioTrack = new MediaStreamTrack(AudioCommonlyUsedFormats.OpusWebRTC, MediaStreamStatusEnum.SendRecv);
        peerConnection.addTrack(audioTrack);

        return Task.FromResult(peerConnection);
    }
}
