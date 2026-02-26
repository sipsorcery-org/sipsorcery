//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An example ASP.NET WebRTC application that can be used to act as a bridge
// between a browser based WebRTC peer and OpenAI's real-time API
// https://platform.openai.com/docs/guides/realtime-webrtc.
//
// Browser clients can connect directly to OpenAI. The reason to use a bridging
// asp.net app is to control and utilise the interaction on the asp.net app.
// For example the asp.net could provide a local function to look some DB info etc.
// based on user request.
//
// Usage:
// set OPENAI_API_KEY=your_openai_key
// dotnet run
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 27 Apr 2025	Aaron Clauson	Created, Dublin, Ireland.
// 27 May 2025  Aaron Clauson   Moved from SIPSorcery main repo to SIPSorcery.OpenAI.WebRTC repo.
// 26 Feb 2026  Aaron Clauson   Moved back to SIPSorcery mono repo.
//
// License: 
// BSD 3-Clause "New" or "Revised" License and the additional
// BDS BY-NC-SA restriction, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using LanguageExt;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorcery.OpenAI.Realtime;
using SIPSorcery.OpenAI.Realtime.Models;
using SIPSorceryMedia.Abstractions;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace demo;

class Program
{
    private static string? _stunUrl = string.Empty;
    private static string? _turnUrl = string.Empty;

    static async Task Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
        .MinimumLevel.Debug()
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .CreateLogger();

        var factory = new SerilogLoggerFactory(Log.Logger);
        SIPSorcery.LogFactory.Set(factory);

        Log.Information("WebRTC OpenAI ASP.NET Demo Program");

        var openAiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? string.Empty;
        _stunUrl = Environment.GetEnvironmentVariable("STUN_URL");
        _turnUrl = Environment.GetEnvironmentVariable("TURN_URL");
        bool.TryParse(Environment.GetEnvironmentVariable("WAIT_FOR_ICE_GATHERING_TO_SEND_OFFER"), out var waitForIceGatheringToSendOffer);

        if (string.IsNullOrWhiteSpace(openAiKey))
        {
            Log.Logger.Error("Please provide your OpenAI key as an environment variable. For example: set OPENAI_API_KEY=<your openai api key>");
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
            [FromServices] IWebRTCEndPoint openAiWebRTCEndPoint) =>
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

                webSocketPeer.OfferOptions = new RTCOfferOptions
                {
                    X_WaitForIceGatheringToComplete = waitForIceGatheringToSendOffer
                };

                var browserPeerTask = webSocketPeer.Run();

                SetOpenAIPeerEventHandlers(openAiWebRTCEndPoint);
                var openAiPeerTask = openAiWebRTCEndPoint.StartConnect(config);

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

    private static void SetOpenAIPeerEventHandlers(IWebRTCEndPoint webrtcEndPoint)
    {
        webrtcEndPoint.OnPeerConnectionConnected += () =>
        {
            Log.Logger.Information("WebRTC peer connection established.");

            // Trigger the conversation by sending a response create message.
            var result = webrtcEndPoint.DataChannelMessenger.SendResponseCreate(RealtimeVoicesEnum.shimmer, "Say Hi!");
            if (result.IsLeft)
            {
                Log.Logger.Error($"Failed to send response create message: {result.LeftAsEnumerable().First()}");
            }
        };

        webrtcEndPoint.OnDataChannelMessage += (dc, message) =>
        {
            if (message is RealtimeServerEventResponseAudioTranscriptDone done)
            {
                Log.Information($"Transcript done: {done.Transcript}");
            }
        };
    }

    private static void ConnectPeers(RTCPeerConnection browserPc, IWebRTCEndPoint openAiEndPoint)
    {
        if (browserPc == null)
        {
            Log.Error("Browser peer connection is null.");
            return;
        }

        openAiEndPoint.PeerConnection.Match(
            pc =>
            {
                // Send RTP audio payloads receied from the brower WebRTC peer connection to OpenAI.
                browserPc.PipeAudioTo(pc);

                // Send RTP audio payloads received from OpenAI to the browser WebRTC peer connection.
                pc.PipeAudioTo(browserPc);

                // If the browser peer connection closes we need to close the OpenAI peer connection too.
                browserPc.OnClosed += () => pc.Close("Browser peer closed.");

                // If the OpenAI peer connection closes we need to close the browser peer connection too.
                pc.OnClosed += () => browserPc.Close("OpenAI peer closed.");
            },
            () => Log.Error("OpenAI peer connection is null.")
        );
    }

    /// <summary>
    /// Method to create the peer connection with the browser.
    /// </summary>
    private static Task<RTCPeerConnection> CreateBrowserPeerConnection(RTCConfiguration pcConfig)
    {
        pcConfig.iceServers = new List<RTCIceServer>();

        if (!string.IsNullOrWhiteSpace(_stunUrl))
        {
            pcConfig.iceServers.Add(_stunUrl.ParseStunServer());
        }

        if (!string.IsNullOrWhiteSpace(_turnUrl))
        {
            pcConfig.iceServers.Add(_turnUrl.ParseStunServer());
        }

        var peerConnection = new RTCPeerConnection(pcConfig);

        MediaStreamTrack audioTrack = new MediaStreamTrack(AudioCommonlyUsedFormats.OpusWebRTC, MediaStreamStatusEnum.SendRecv);
        peerConnection.addTrack(audioTrack);

        return Task.FromResult(peerConnection);
    }
}

public static class StunServerExtensions
{
    public static RTCIceServer ParseStunServer(this string stunServer)
    {
        var fields = stunServer.Split(';');

        return new RTCIceServer
        {
            urls = fields[0],
            username = fields.Length > 1 ? fields[1] : null,
            credential = fields.Length > 2 ? fields[2] : null,
            credentialType = RTCIceCredentialType.password
        };
    }
}
