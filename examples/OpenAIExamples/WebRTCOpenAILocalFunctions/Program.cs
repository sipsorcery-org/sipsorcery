﻿//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An example WebRTC application that can be used to interact with
// OpenAI's real-time API https://platform.openai.com/docs/guides/realtime-webrtc
// and utilise the local function calling feature https://platform.openai.com/docs/guides/function-calling.
//
// Usage:
// set OPENAIKEY=your_openai_key
// dotnet run %OPENAIKEY%
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 19 Jan 2025	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Serilog;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorceryMedia.Windows;
using LanguageExt;
using LanguageExt.Common;

namespace demo;

record PcContext(
   RTCPeerConnection Pc,
   SemaphoreSlim PcConnectedSemaphore,
   string EphemeralKey = "",
   string OfferSdp = "",
   string AnswerSdp = ""
);

class Program
{
    private const string OPENAI_REALTIME_SESSIONS_URL = "https://api.openai.com/v1/realtime/sessions";
    private const string OPENAI_REALTIME_BASE_URL = "https://api.openai.com/v1/realtime";
    private const string OPENAI_MODEL = "gpt-4o-realtime-preview-2024-12-17";
    private const OpenAIVoicesEnum OPENAI_VOICE = OpenAIVoicesEnum.shimmer;
    private const string OPENAI_DATACHANNEL_NAME = "oai-events";

    private static Microsoft.Extensions.Logging.ILogger logger = LoggerFactory.Create(builder =>
        builder.AddSerilog(new LoggerConfiguration()
            .Enrich.FromLogContext()
            .MinimumLevel.Debug()
            .WriteTo.Console()
            .CreateLogger()))
        .CreateLogger<Program>();

    static async Task Main(string[] args)
    {
        Console.WriteLine("WebRTC OpenAI Local Function Demo Program");
        Console.WriteLine("Press ctrl-c to exit.");

        if (args.Length != 1)
        {
            Console.WriteLine("Please provide your OpenAI key as a command line argument. It's used to get the single use ephemeral secret for the WebRTC connection.");
            Console.WriteLine("The recommended approach is to use an environment variable, for example: set OPENAIKEY=<your openai api key>");
            Console.WriteLine("Then execute the application using: dotnet run %OPENAIKEY%");
            return;
        }

        var flow = await Prelude.Right<Error, Unit>(default)
            .BindAsync(_ =>
            {
                logger.LogInformation("STEP 1: Get ephemeral key from OpenAI.");
                return OpenAIRealtimeRestClient.CreateEphemeralKeyAsync(OPENAI_REALTIME_SESSIONS_URL, args[0], OPENAI_MODEL, OPENAI_VOICE);
            })
            .BindAsync(async ephemeralKey =>
            {
                logger.LogDebug("STEP 2: Create WebRTC PeerConnection & get local SDP offer.");

                var onConnectedSemaphore = new SemaphoreSlim(0, 1);
                var pc = await CreatePeerConnection(onConnectedSemaphore);
                var offer = pc.createOffer();
                await pc.setLocalDescription(offer);

                logger.LogDebug("SDP offer:");
                logger.LogDebug(offer.sdp);

                return Prelude.Right<Error, PcContext>(
                    new PcContext(pc, onConnectedSemaphore, ephemeralKey, offer.sdp, string.Empty)
                );
            })
            .BindAsync(async ctx =>
            {
                logger.LogInformation("STEP 3: Send SDP offer to OpenAI REST server & get SDP answer.");

                var answerEither = await OpenAIRealtimeRestClient.GetOpenAIAnswerSdpAsync(ctx.EphemeralKey, OPENAI_REALTIME_BASE_URL, OPENAI_MODEL, ctx.OfferSdp);
                return answerEither.Map(answer => ctx with { AnswerSdp = answer });
            })
            .BindAsync(ctx =>
            {
                logger.LogInformation("STEP 4: Set remote SDP");

                logger.LogDebug("SDP answer:");
                logger.LogDebug(ctx.AnswerSdp);

                var setAnswerResult = ctx.Pc.setRemoteDescription(
                    new RTCSessionDescriptionInit { sdp = ctx.AnswerSdp, type = RTCSdpType.answer }
                );
                logger.LogInformation($"Set answer result {setAnswerResult}.");

                return setAnswerResult == SetDescriptionResultEnum.OK ?
                    Prelude.Right<Error, PcContext>(ctx) :
                    Prelude.Left<Error, PcContext>(Error.New("Failed to set remote SDP."));
            })
            .MapAsync(async ctx =>
            {
                logger.LogInformation("STEP 5: Wait for data channel to connect and then trigger conversation.");

                await ctx.PcConnectedSemaphore.WaitAsync();

                UpdateSessionOptions(ctx.Pc.DataChannels.First());

                // NOTE: If you want to trigger the convesation by using the audio from your microphone comment
                // out this line.
                SendResponseCreate(ctx.Pc.DataChannels.First(), OpenAIVoicesEnum.alloy, "Introduce urself.");

                return ctx;
            })
            .BindAsync(ctx =>
            {
                logger.LogInformation("STEP 6: Wait for ctrl-c to indicate user exit.");

                ManualResetEvent exitMre = new(false);
                Console.CancelKeyPress += (_, e) =>
                {
                    e.Cancel = true;
                    exitMre.Set();
                };
                exitMre.WaitOne();

                ctx.Pc.Close("User exit.");

                return Prelude.Right<Error, PcContext>(ctx);
            });

        flow.Match(
            Left: prob => Console.WriteLine($"There was a problem setting up the connection. {prob.Message}"),
            Right: _ => Console.WriteLine("The call was successful.")
        );
    }

    /// <summary>
    /// Sends a response create message to the OpenAI data channel to trigger the conversation.
    /// </summary>
    private static void UpdateSessionOptions(RTCDataChannel dc)
    {
        var sessionUpdate = new OpenAISessionUpdate
        {
            EventID = Guid.NewGuid().ToString(),
            Session = new OpenAISession
            {
                Model = OPENAI_MODEL,
                Tools = new System.Collections.Generic.List<OpenAITool>
                {
                    new OpenAITool
                    {
                        Name = "get_weather",
                        Description = "Get the current weather.",
                        Parameters = new OpenAIToolParameters
                        {
                          Properties = new OpenAIToolProperties
                          {
                              Location = new OpenAIToolLocation
                              {
                                  Type = "string"
                              }
                          },
                          Required = new System.Collections.Generic.List<string> { "location" }
                        }
                    }
                }
            }
        };

        logger.LogInformation($"Sending OpenAI session update to data channel {dc.label}.");
        logger.LogDebug(sessionUpdate.ToJson());

        dc.send(sessionUpdate.ToJson());
    }

    /// <summary>
    /// Event handler for WebRTC data channel messages.
    /// </summary>
    private static void OnDataChannelMessage(RTCDataChannel dc, DataChannelPayloadProtocols protocol, byte[] data)
    {
        logger.LogInformation($"Data channel {dc.label}, protocol {protocol} message length {data.Length}.");

        var message = Encoding.UTF8.GetString(data);

        //logger.LogDebug($"Data channel message: {message}");

        var serverEvent = JsonSerializer.Deserialize<OpenAIServerEventBase>(message, JsonOptions.Default);

        if (serverEvent != null)
        {
            //logger.LogInformation($"Server event ID {serverEvent.EventID} and type {serverEvent.Type}.");

            Option<OpenAIServerEventBase> serverEventModel = serverEvent.Type switch
            {
                OpenAIConversationItemCreated.TypeName => JsonSerializer.Deserialize<OpenAIConversationItemCreated>(message, JsonOptions.Default),
                OpenAIInputAudioBufferCommitted.TypeName => JsonSerializer.Deserialize<OpenAIInputAudioBufferCommitted>(message, JsonOptions.Default),
                OpenAIInputAudioBufferSpeechStarted.TypeName => JsonSerializer.Deserialize<OpenAIInputAudioBufferSpeechStarted>(message, JsonOptions.Default),
                OpenAIInputAudioBufferSpeechStopped.TypeName => JsonSerializer.Deserialize<OpenAIInputAudioBufferSpeechStopped>(message, JsonOptions.Default),
                OpenAIOuputAudioBufferAudioStarted.TypeName => JsonSerializer.Deserialize<OpenAIOuputAudioBufferAudioStarted>(message, JsonOptions.Default),
                OpenAIOuputAudioBufferAudioStopped.TypeName => JsonSerializer.Deserialize<OpenAIOuputAudioBufferAudioStopped>(message, JsonOptions.Default),
                OpenAIRateLimitsUpdated.TypeName => JsonSerializer.Deserialize<OpenAIRateLimitsUpdated>(message, JsonOptions.Default),
                OpenAIResponseAudioDone.TypeName => JsonSerializer.Deserialize<OpenAIResponseAudioDone>(message, JsonOptions.Default),
                OpenAIResponseAudioTranscriptDelta.TypeName => JsonSerializer.Deserialize<OpenAIResponseAudioTranscriptDelta>(message, JsonOptions.Default),
                OpenAIResponseAudioTranscriptDone.TypeName => JsonSerializer.Deserialize<OpenAIResponseAudioTranscriptDone>(message, JsonOptions.Default),
                OpenAIResponseContentPartAdded.TypeName => JsonSerializer.Deserialize<OpenAIResponseContentPartAdded>(message, JsonOptions.Default),
                OpenAIResponseContentPartDone.TypeName => JsonSerializer.Deserialize<OpenAIResponseContentPartDone>(message, JsonOptions.Default),
                OpenAIResponseCreated.TypeName => JsonSerializer.Deserialize<OpenAIResponseCreated>(message, JsonOptions.Default),
                OpenAIResponseDone.TypeName => JsonSerializer.Deserialize<OpenAIResponseDone>(message, JsonOptions.Default),
                OpenAIResponseFunctionCallArgumentsDelta.TypeName => JsonSerializer.Deserialize<OpenAIResponseFunctionCallArgumentsDelta>(message, JsonOptions.Default),
                OpenAIResponseFunctionCallArgumentsDone.TypeName => JsonSerializer.Deserialize<OpenAIResponseFunctionCallArgumentsDone>(message, JsonOptions.Default),
                OpenAIResponseOutputItemAdded.TypeName => JsonSerializer.Deserialize<OpenAIResponseOutputItemAdded>(message, JsonOptions.Default),
                OpenAIResponseOutputItemDone.TypeName => JsonSerializer.Deserialize<OpenAIResponseOutputItemDone>(message, JsonOptions.Default),
                OpenAISessionCreated.TypeName => JsonSerializer.Deserialize<OpenAISessionCreated>(message, JsonOptions.Default),
                OpenAISessionUpdated.TypeName => JsonSerializer.Deserialize<OpenAISessionUpdated>(message, JsonOptions.Default),
                _ => Option<OpenAIServerEventBase>.None
            };

            serverEventModel.IfSome(e =>
            {
                var logMessage = e switch
                {
                    OpenAIResponseAudioTranscriptDone transcriptionDone => $"Transcript done: {transcriptionDone.Transcript}",
                    OpenAIResponseFunctionCallArgumentsDone argumentsDone => $"Function Arguments done: {message}\n{argumentsDone.ToJson()}\n{argumentsDone.ArgumentsToString()}",
                    _ => $"Data Channel {e.Type} message received."

                };

                if (logMessage != string.Empty)
                {
                    logger.LogInformation(logMessage);
                }

                if (e is OpenAIResponseFunctionCallArgumentsDone argsDone)
                {
                    var result = argsDone.Name switch
                    {
                        "get_weather" => $"The weather in {argsDone.Arguments.GetNamedArgumentValue("location")} is sunny.",
                        _ => "Unknown Function."
                    };
                    logger.LogInformation($"Call {argsDone.Name} with args {argsDone.ArgumentsToString()} result {result}.");

                    var getWeatherResult = GetWeather(argsDone);
                    logger.LogDebug(getWeatherResult.ToJson());
                    dc.send(getWeatherResult.ToJson());

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
            });

            if (serverEventModel.IsNone)
            {
                logger.LogWarning($"Failed to parse server event for: {message}");
            }
        }
        else
        {
            logger.LogWarning($"Failed to parse server event for: {message}");
        }
    }

    /// <summary>
    /// The local function to call and return the result to the AI to continue the conversation.
    /// </summary>
    private static OpenAIConversationItemCreate GetWeather(OpenAIResponseFunctionCallArgumentsDone argsDone)
    {
        var location = argsDone.Arguments.GetNamedArgumentValue("location") ?? string.Empty;

        var weather = location switch
        {
            string s when s.Contains("Canberra", StringComparison.OrdinalIgnoreCase) => "It's cloudy and 15 degrees.",
            string s when s.Contains("Dublin", StringComparison.OrdinalIgnoreCase) => "It's raining and 7 degrees.",
            string s when s.Contains("Hobart", StringComparison.OrdinalIgnoreCase) => "It's sunny and 25 degrees.",
            string s when s.Contains("Melbourne", StringComparison.OrdinalIgnoreCase) => "It's cold and wet and 11 degrees.",
            string s when s.Contains("Sydney", StringComparison.OrdinalIgnoreCase) => "It's humid and stormy and 30 degrees.",
            string s when s.Contains("Perth", StringComparison.OrdinalIgnoreCase) => "It's hot and dry and 40 degrees.",
            _ => "It's sunny and 20 degrees."
        };

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
                Output = weather
            }
        };
    }

    /// <summary>
    /// Sends a response create message to the OpenAI data channel to trigger the conversation.
    /// </summary>
    private static void SendResponseCreate(RTCDataChannel dc, OpenAIVoicesEnum voice, string message)
    {
        var responseCreate = new OpenAIResponseCreate
        {
            EventID = Guid.NewGuid().ToString(),
            Response = new OpenAIResponseCreateResponse
            {
                Instructions = message,
                Voice = voice.ToString()
            }
        };

        logger.LogInformation($"Sending initial response create to first call data channel {dc.label}.");
        logger.LogDebug(responseCreate.ToJson());

        dc.send(responseCreate.ToJson());
    }

    /// <summary>
    /// Method to create the local peer connection instance and data channel.
    /// </summary>
    /// <param name="onConnectedSemaphore">A semaphore that will get set when the data channel on the peer connection is opened. Since the data channel
    /// can only be opened once the peer connection is open this indicates both are ready for use.</param>
    private static async Task<RTCPeerConnection> CreatePeerConnection(SemaphoreSlim onConnectedSemaphore)
    {
        var pcConfig = new RTCConfiguration
        {
            X_UseRtpFeedbackProfile = true,
        };

        var peerConnection = new RTCPeerConnection(pcConfig);
        var dataChannel = await peerConnection.createDataChannel(OPENAI_DATACHANNEL_NAME);

        // Sink (speaker) only audio end point.
        WindowsAudioEndPoint windowsAudioEP = new WindowsAudioEndPoint(new AudioEncoder(includeOpus: true), -1, -1, false, false);
        windowsAudioEP.RestrictFormats(x => x.FormatName == "OPUS");
        windowsAudioEP.OnAudioSinkError += err => logger.LogWarning($"Audio sink error. {err}.");
        windowsAudioEP.OnAudioSourceEncodedSample +=  peerConnection.SendAudio;

        MediaStreamTrack audioTrack = new MediaStreamTrack(windowsAudioEP.GetAudioSourceFormats(), MediaStreamStatusEnum.SendRecv);
        peerConnection.addTrack(audioTrack);

        peerConnection.OnAudioFormatsNegotiated += (audioFormats) =>
        {
            logger.LogDebug($"Audio format negotiated {audioFormats.First().FormatName}.");
            windowsAudioEP.SetAudioSinkFormat(audioFormats.First());
            windowsAudioEP.SetAudioSourceFormat(audioFormats.First());
        };
        //peerConnection.OnReceiveReport += RtpSession_OnReceiveReport;
        //peerConnection.OnSendReport += RtpSession_OnSendReport;
        peerConnection.OnTimeout += (mediaType) => logger.LogDebug($"Timeout on media {mediaType}.");
        peerConnection.oniceconnectionstatechange += (state) => logger.LogDebug($"ICE connection state changed to {state}.");
        peerConnection.onconnectionstatechange += async (state) =>
        {
            logger.LogDebug($"Peer connection connected changed to {state}.");

            if (state == RTCPeerConnectionState.connected)
            {
                await windowsAudioEP.StartAudio();
                await windowsAudioEP.StartAudioSink();
            }
            else if (state == RTCPeerConnectionState.closed || state == RTCPeerConnectionState.failed)
            {
                await windowsAudioEP.CloseAudio();
            }
        };

        peerConnection.OnRtpPacketReceived += (IPEndPoint rep, SDPMediaTypesEnum media, RTPPacket rtpPkt) =>
        {
            //logger.LogDebug($"RTP {media} pkt received, SSRC {rtpPkt.Header.SyncSource}.");

            if (media == SDPMediaTypesEnum.audio)
            {
                windowsAudioEP.GotAudioRtp(rep, rtpPkt.Header.SyncSource, rtpPkt.Header.SequenceNumber, rtpPkt.Header.Timestamp, rtpPkt.Header.PayloadType, rtpPkt.Header.MarkerBit == 1, rtpPkt.Payload);
            }
        };

        dataChannel.onopen += () =>
        {
            logger.LogDebug("OpenAI data channel opened.");
            onConnectedSemaphore.Release();
        };

        dataChannel.onclose += () => logger.LogDebug($"OpenAI data channel {dataChannel.label} closed.");

        dataChannel.onmessage += OnDataChannelMessage;

        return peerConnection;
    }
}
