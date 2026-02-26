//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An example ASP.NET WebRTC application that can connect to OpenAI's
// real-time API and use a local function to tailor the AI's voice responses.
// https://platform.openai.com/docs/guides/realtime-webrtc.
//
// This demo builds on the ASPNetGetStarted example and adds a local function:
// https://platform.openai.com/docs/guides/function-calling
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
// 20 Jul 2025	Aaron Clauson	Created, Dublin, Ireland.
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
using System.Linq;
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

        Log.Information("WebRTC OpenAI ASP.NET Local Function Demo Program");

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

                await webSocketPeer.Run();

                SetOpenAIPeerEventHandlers(openAiWebRTCEndPoint, webSocketPeer.RTCPeerConnection.DataChannels.First());

                var openAiPeerTask = openAiWebRTCEndPoint.StartConnect(config);

                await openAiPeerTask;

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

    private static void SetOpenAIPeerEventHandlers(IWebRTCEndPoint webrtcEndPoint, RTCDataChannel browserDataChannel)
    {
        webrtcEndPoint.OnPeerConnectionConnected += () =>
        {
            Log.Logger.Information("OpenAI WebRTC peer connection established.");

            browserDataChannel.send("WebRTC connection established with OpenAI.");

            // Trigger the conversation by sending a response create message.
            var result = webrtcEndPoint.DataChannelMessenger.SendResponseCreate(RealtimeVoicesEnum.shimmer, "Say Hi!");
            if (result.IsLeft)
            {
                Log.Logger.Error($"Failed to send response create message: {result.LeftAsEnumerable().First()}");
            }
        };

        //webrtcEndPoint.OnDataChannelMessage += (dc, message) =>
        //{
        //    if (message is RealtimeServerEventResponseAudioTranscriptDone done)
        //    {
        //        Log.Information($"Transcript done: {done.Transcript}");
        //    }
        //};

        webrtcEndPoint.OnDataChannelMessage += (dc, evt) => OnDataChannelMessage(dc, evt, browserDataChannel);
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

        // This call is synchronous when the WebRTC connection is not yet connected.
        _ = peerConnection.createDataChannel("browser").Result;

        return Task.FromResult(peerConnection);
    }

    /// <summary>
    /// Event handler for WebRTC data channel messages.
    /// </summary>
    private static void OnDataChannelMessage(RTCDataChannel dc, RealtimeEventBase serverEvent, RTCDataChannel browserDataChannel)
    {
        switch (serverEvent)
        {
            case RealtimeServerEventResponseFunctionCallArgumentsDone argumentsDone:
                Log.Information($"Function Arguments done: {argumentsDone.ToJson()}\n{argumentsDone.Arguments}");
                OnFunctionArgumentsDone(dc, argumentsDone);
                break;

            case RealtimeServerEventSessionCreated sessionCreated:
                Log.Information($"Session created: {sessionCreated.ToJson()}");
                OnSessionCreated(dc);
                break;

            case RealtimeServerEventSessionUpdated sessionUpdated:
                Log.Information($"Session updated: {sessionUpdated.ToJson()}");
                break;

            case RealtimeServerEventResponseAudioTranscriptDone transcriptionDone:
                Log.Information($"Transcript done: {transcriptionDone.Transcript}");
                browserDataChannel.send($"AI: {transcriptionDone.Transcript?.Trim()}");
                break;

            default:
                //logger.LogInformation($"Data Channel {serverEvent.Type} message received.");
                break;
        }
    }

    /// <summary>
    /// Sends a session update message to add the get weather demo function.
    /// </summary>
    private static void OnSessionCreated(RTCDataChannel dc)
    {
        var sessionUpdate = new RealtimeClientEventSessionUpdate
        {
            EventID = Guid.NewGuid().ToString(),
            Session = new RealtimeSession
            {
                Instructions = "You are a weather bot who favours brevity and accuracy.",
                Tools = new List<RealtimeTool>
                {
                     new RealtimeTool
                    {
                        Name = "get_weather",
                        Description = "Get the current weather.",
                        Parameters = new RealtimeToolParameters
                        {
                           Properties = new Dictionary<string, RealtimeToolProperty>
                           {
                                { "location", new RealtimeToolProperty { Type = "string" } }
                           },
                           Required = new List<string> { "location" }
                        }
                    }
                }
            }
        };

        Log.Information($"Sending OpenAI session update to data channel {dc.label}.");
        Log.Debug(sessionUpdate.ToJson());

        dc.send(sessionUpdate.ToJson());
    }

    private static void OnFunctionArgumentsDone(RTCDataChannel dc, RealtimeServerEventResponseFunctionCallArgumentsDone argsDone)
    {
        var result = argsDone.Name switch
        {
            "get_weather" => GetWeather(argsDone),
            _ => "Unknown Function."
        };

        Log.Information($"Call {argsDone.Name} with args {argsDone.Arguments} result {result}.");

        var resultConvItem = new RealtimeClientEventConversationItemCreate
        {
            EventID = Guid.NewGuid().ToString(),
            Item = new RealtimeConversationItem
            {
                Type = RealtimeConversationItemTypeEnum.function_call_output,
                CallID = argsDone.CallId,
                Output = result
            }
        };

        Log.Debug(resultConvItem.ToJson());
        dc.send(resultConvItem.ToJson());

        // Tell the AI to continue the conversation.
        var responseCreate = new RealtimeClientEventResponseCreate
        {
            EventID = Guid.NewGuid().ToString(),
            Response = new RealtimeResponseCreateParams
            {
                Instructions = "Please give me the answer.",
            }
        };

        dc.send(responseCreate.ToJson());
    }

    /// <summary>
    /// The local function to call and return the result to the AI to continue the conversation.
    /// </summary>
    private static string GetWeather(RealtimeServerEventResponseFunctionCallArgumentsDone argsDone)
    {
        var location = argsDone.Arguments.GetNamedArgumentValue("location") ?? string.Empty;

        return location switch
        {
            string s when s.Contains("Canberra", StringComparison.OrdinalIgnoreCase) => "It's cloudy and 15 degrees.",
            string s when s.Contains("Dublin", StringComparison.OrdinalIgnoreCase) => "It's raining and 7 degrees.",
            string s when s.Contains("Hobart", StringComparison.OrdinalIgnoreCase) => "It's sunny and 25 degrees.",
            string s when s.Contains("Melbourne", StringComparison.OrdinalIgnoreCase) => "It's cold and wet and 11 degrees.",
            string s when s.Contains("Sydney", StringComparison.OrdinalIgnoreCase) => "It's humid and stormy and 30 degrees.",
            string s when s.Contains("Perth", StringComparison.OrdinalIgnoreCase) => "It's hot and dry and 40 degrees.",
            _ => "It's sunny and 20 degrees."
        };
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
