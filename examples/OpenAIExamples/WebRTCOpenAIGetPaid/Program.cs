//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An example WebRTC application that uses OpenAI's real-time API
// together with local function calling to generate payment requests.
//
// Usage:
// set OPENAIKEY=your_openai_key
// dotnet run
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 11 May 2025	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Serilog;
using SIPSorcery.Net;
using SIPSorceryMedia.Windows;
using Serilog.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Generic;

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

        var openAiKey = Environment.GetEnvironmentVariable("OPENAIKEY") ?? string.Empty;

        if (string.IsNullOrWhiteSpace(openAiKey))
        {
            Log.Logger.Error("Please provide your OpenAI key as an environment variable. For example: set OPENAIKEY=<your openai api key>");
            return;
        }

        // Create the OpenAI Realtime WebRTC peer connection.
        var openAIHttpClientFactory = new OpenAIHttpClientFactory(openAiKey);
        var openaiClient = new OpenAIRealtimeRestClient(openAIHttpClientFactory);
        var webrtcEndPoint = new OpenAIRealtimeWebRTCEndPoint(loggerFactory.CreateLogger<OpenAIRealtimeWebRTCEndPoint>(), openaiClient);

        // We'll send/receive audio directly from our Windows audio devices.
        InitialiseWindowsAudioEndPoint(webrtcEndPoint, Log.Logger);

        var negotiateConnectResult = await webrtcEndPoint.StartConnectAsync();

        if (negotiateConnectResult.IsLeft)
        {
            Log.Logger.Error($"Failed to negotiation connection to OpenAI Realtime WebRTC endpoint: {negotiateConnectResult.LeftAsEnumerable().First()}");
            return;
        }

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

        webrtcEndPoint.OnDataChannelMessageReceived += OnDataChannelMessage;

        Console.WriteLine("Wait for ctrl-c to indicate user exit.");

        var exitTcs = new TaskCompletionSource<object?>();
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            exitTcs.TrySetResult(null);
        };

        await exitTcs.Task;
    }

    private static void InitialiseWindowsAudioEndPoint(IOpenAIRealtimeWebRTCEndPoint webrtcEndPoint, Serilog.ILogger logger)
    {
        WindowsAudioEndPoint windowsAudioEP = new WindowsAudioEndPoint(webrtcEndPoint.AudioEncoder, -1, -1, false, false);
        windowsAudioEP.SetAudioSinkFormat(webrtcEndPoint.AudioFormat);
        windowsAudioEP.SetAudioSourceFormat(webrtcEndPoint.AudioFormat);
        windowsAudioEP.OnAudioSourceEncodedSample += webrtcEndPoint.SendAudio;

        webrtcEndPoint.OnRtpPacketReceived += (IPEndPoint rep, SDPMediaTypesEnum media, RTPPacket rtpPkt) =>
        {
            windowsAudioEP.GotAudioRtp(rep, rtpPkt.Header.SyncSource, rtpPkt.Header.SequenceNumber, rtpPkt.Header.Timestamp, rtpPkt.Header.PayloadType, rtpPkt.Header.MarkerBit == 1, rtpPkt.Payload);
        };
        webrtcEndPoint.OnPeerConnectionConnected += async () =>
        {
            logger.Information("WebRTC peer connection established.");

            await windowsAudioEP.StartAudio();
            await windowsAudioEP.StartAudioSink();
        };
        webrtcEndPoint.OnPeerConnectionClosedOrFailed += async () =>
        {
            logger.Information("WebRTC peer connection closed.");
            await windowsAudioEP.CloseAudio();
        };
    }

    /// <summary>
    /// Event handler for WebRTC data channel messages.
    /// </summary>
    private static void OnDataChannelMessage(RTCDataChannel dc, OpenAIServerEventBase serverEvent)
    {
        switch (serverEvent)
        {
            case OpenAIResponseFunctionCallArgumentsDone argumentsDone:
                logger.LogInformation($"Function Arguments done: {argumentsDone.ToJson()}\n{argumentsDone.ArgumentsToString()}");
                OnFunctionArgumentsDone(dc, argumentsDone);
                break;

            case OpenAISessionCreated sessionCreated:
                logger.LogInformation($"Session created: {sessionCreated.ToJson()}");
                OnSessionCreated(dc);
                break;

            case OpenAISessionUpdated sessionUpdated:
                logger.LogInformation($"Session updated: {sessionUpdated.ToJson()}");
                break;

            case OpenAIResponseAudioTranscriptDone transcriptionDone:
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
        var sessionUpdate = new OpenAISessionUpdate
        {
            EventID = Guid.NewGuid().ToString(),
            Session = new OpenAISession
            {
                Instructions = "You are an assistant for sales agents that assists in generating payment requests and favours brevity and accuracy.",
                Tools = new List<OpenAITool>
                {
                    new OpenAITool
                    {
                        Name = "create_payment_request",
                        Description = "Creates a payment request.",
                        Parameters = new OpenAIToolParameters
                        {
                           Properties = new Dictionary<string, OpenAIToolProperty>
                           {
                                { "amount", new OpenAIToolProperty { Type = "number" } },
                                { "currency", new OpenAIToolProperty { Type = "string" } }
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

    private static void OnFunctionArgumentsDone(RTCDataChannel dc, OpenAIResponseFunctionCallArgumentsDone argsDone)
    {
        var result = argsDone.Name switch
        {
            "create_payment_request" => $"Processing create payment request.",
            _ => "Unknown Function."
        };
        logger.LogInformation($"Call {argsDone.Name} with args {argsDone.ArgumentsToString()} result {result}.");

        var createPyamentRequestResult = CreatePaymentRequest(argsDone);
        logger.LogDebug(createPyamentRequestResult.ToJson());
        dc.send(createPyamentRequestResult.ToJson());

        // Tell the AI to continue the conversation.
        var responseCreate = new OpenAIResponseCreate
        {
            EventID = Guid.NewGuid().ToString(),
            Response = new OpenAIResponseCreateResponse
            {
                Instructions = "Please give me the answer.",
            }
        };

        dc.send(responseCreate.ToJson());
    }

    /// <summary>
    /// The local function to call and return the result to the AI to continue the conversation.
    /// </summary>
    private static OpenAIConversationItemCreate CreatePaymentRequest(OpenAIResponseFunctionCallArgumentsDone argsDone)
    {
        string orderID = "X1234";

        return new OpenAIConversationItemCreate
        {
            EventID = Guid.NewGuid().ToString(),
            //PreviousItemID = argsDone.ItemID,
            Item = new OpenAIConversationItem
            {
                //ID = Guid.NewGuid().ToString().Replace("-", string.Empty),
                Type = OpenAIConversationConversationTypeEnum.function_call_output,
                //Status = "completed",
                CallID = argsDone.CallID,
                //Name = argsDone.Name,
                //Arguments = argsDone.ArgumentsToString(),
                //Role = "tool",
                Output = $"New payment request order ID is {orderID}"
            }
        };
    }
}
