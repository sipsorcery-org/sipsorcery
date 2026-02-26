//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An example WebRTC application that uses OpenAI's real-time API
// together with local function calling to generate payment requests.
//
// Usage:
// set OPENAI_API_KEY=your_openai_key
// dotnet run
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 11 May 2025	Aaron Clauson	Created, Dublin, Ireland.
// 27 May 2025  Aaron Clauson   Moved from SIPSorcery main repo to SIPSorcery.OpenAI.WebRTC repo.
// 26 Feb 2026  Aaron Clauson   Moved back to SIPSorcery mono repo.
//
// License: 
// BSD 3-Clause "New" or "Revised" License and the additional
// BDS BY-NC-SA restriction, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Serilog;
using SIPSorcery.Net;
using SIPSorceryMedia.Windows;
using Serilog.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Generic;
using SIPSorcery.OpenAI.Realtime;
using SIPSorcery.OpenAI.Realtime.Models;
using SIPSorceryMedia.Abstractions;
using SIPSorcery.Media;

namespace demo;

class Program
{
    private static Microsoft.Extensions.Logging.ILogger logger = NullLogger.Instance;

    static async Task Main()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .CreateLogger();

        var loggerFactory = new SerilogLoggerFactory(Log.Logger);
        SIPSorcery.LogFactory.Set(loggerFactory);

        logger = loggerFactory.CreateLogger<Program>();

        Log.Logger.Information("WebRTC OpenAI Demo Program");

        var openAiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? string.Empty;

        if (string.IsNullOrWhiteSpace(openAiKey))
        {
            Log.Logger.Error("Please provide your OpenAI key as an environment variable. For example: set OPENAI_API_KEY=<your openai api key>");
            return;
        }

        var webrtcEndPoint = new WebRTCEndPoint(openAiKey, loggerFactory);

        // We'll send/receive audio directly from our Windows audio devices.
        var windowsAudioEP = InitialiseWindowsAudioEndPoint();
        webrtcEndPoint.ConnectAudioEndPoint(windowsAudioEP);

        var negotiateConnectResult = await webrtcEndPoint.StartConnect();

        if (negotiateConnectResult.IsLeft)
        {
            Log.Logger.Error($"Failed to negotiation connection to OpenAI Realtime WebRTC endpoint: {negotiateConnectResult.LeftAsEnumerable().First()}");
            return;
        }

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

        webrtcEndPoint.OnDataChannelMessage += OnDataChannelMessage;

        Console.WriteLine("Wait for ctrl-c to indicate user exit.");

        var exitTcs = new TaskCompletionSource<object?>();
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            exitTcs.TrySetResult(null);
        };

        await exitTcs.Task;
    }

    private static WindowsAudioEndPoint InitialiseWindowsAudioEndPoint()
    {
        var audioEncoder = new AudioEncoder(AudioCommonlyUsedFormats.OpusWebRTC);
        return new WindowsAudioEndPoint(audioEncoder);
    }

    /// <summary>
    /// Event handler for WebRTC data channel messages.
    /// </summary>
    private static void OnDataChannelMessage(RTCDataChannel dc, RealtimeEventBase serverEvent)
    {
        switch (serverEvent)
        {
            case RealtimeServerEventResponseFunctionCallArgumentsDone argumentsDone:
                logger.LogInformation($"Function Arguments done: {argumentsDone.ToJson()}\n{argumentsDone.Arguments}");
                OnFunctionArgumentsDone(dc, argumentsDone);
                break;

            case RealtimeServerEventSessionCreated sessionCreated:
                logger.LogInformation($"Session created: {sessionCreated.ToJson()}");
                OnSessionCreated(dc);
                break;

            case RealtimeServerEventSessionUpdated sessionUpdated:
                logger.LogInformation($"Session updated: {sessionUpdated.ToJson()}");
                break;

            case RealtimeServerEventResponseAudioTranscriptDone transcriptionDone:
                logger.LogInformation($"Transcript done: {transcriptionDone.Transcript}");
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
                Instructions = "You are an assistant for sales agents that assists in generating payment requests and favours brevity and accuracy.",
                Tools = new List<RealtimeTool>
                {
                    new RealtimeTool
                    {
                        Name = "create_payment_request",
                        Description = "Creates a payment request.",
                        Parameters = new RealtimeToolParameters
                        {
                           Properties = new Dictionary<string, RealtimeToolProperty>
                           {
                                { "amount", new RealtimeToolProperty { Type = "number" } },
                                { "currency", new RealtimeToolProperty { Type = "string" } }
                           },
                           Required = new List<string> { "amount", "currency" }
                        }
                    }
                }
            }
        };

        logger.LogInformation($"Sending OpenAI session update to data channel {dc.label}.");
        logger.LogDebug(sessionUpdate.ToJson());

        dc.send(sessionUpdate.ToJson());
    }

    private static void OnFunctionArgumentsDone(RTCDataChannel dc, RealtimeServerEventResponseFunctionCallArgumentsDone argsDone)
    {
        var result = argsDone.Name switch
        {
            "create_payment_request" => $"Processing create payment request.",
            _ => "Unknown Function."
        };
        logger.LogInformation($"Call {argsDone.CallId} with args {argsDone.Arguments} result {result}.");

        var createPyamentRequestResult = CreatePaymentRequest(argsDone);
        logger.LogDebug(createPyamentRequestResult.ToJson());
        dc.send(createPyamentRequestResult.ToJson());

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
    private static RealtimeClientEventConversationItemCreate CreatePaymentRequest(RealtimeServerEventResponseFunctionCallArgumentsDone argsDone)
    {
        string orderID = "X1234";

        return new RealtimeClientEventConversationItemCreate
        {
            EventID = Guid.NewGuid().ToString(),
            Item = new RealtimeConversationItem
            {
                Type = RealtimeConversationItemTypeEnum.function_call_output,
                CallID = argsDone.CallId,
                Output = $"New payment request order ID is {orderID}"
            }
        };
    }
}
