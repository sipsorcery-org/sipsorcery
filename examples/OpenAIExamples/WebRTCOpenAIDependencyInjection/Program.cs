//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An example WebRTC application that can be used to interact with
// OpenAI's real-time API https://platform.openai.com/docs/guides/realtime-webrtc.
//
// NOTE: As of 10 May 2025 this example does work to establish an audio stream and is
// able to receive data channel messages. There is no echo cancellation feature in this
// demo so if not provided by the by your WIndows audio device then ChatGPT will end
// up talking to itself (as a workaround use a headset).
//
// Usage:
// set OPENAIKEY=your_openai_key
// dotnet run
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 19 Dec 2024	Aaron Clauson	Created, Dublin, Ireland.
// 28 Dec 2024  Aaron Clauson   Switched to functional approach for The Craic.
// 17 Jan 2025  Aaron Clauson   Added create resposne data channel message to trigger conversation start.
// 10 MAy 2025  Aaron Clauson   Big refactor of the OpenAI.Realtime library to use HttpClientFactory.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Net;
using System.Threading.Tasks;
using Serilog;
using SIPSorcery.Net;
using SIPSorceryMedia.Windows;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Extensions.Logging;

namespace demo;

class Program
{
    static async Task Main()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .CreateLogger();

        var factory = new SerilogLoggerFactory(Log.Logger);
        SIPSorcery.LogFactory.Set(factory);

        Log.Logger.Information("WebRTC OpenAI Demo Program");

        var openAiKey = Environment.GetEnvironmentVariable("OPENAIKEY") ?? string.Empty;

        if (string.IsNullOrWhiteSpace(openAiKey))
        {
            Log.Logger.Error("Please provide your OpenAI key as an environment variable. For example: set OPENAIKEY=<your openai api key>");
            return;
        }

        // Set up DI.
        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.AddSerilog(dispose: true);
        });

        services
          .AddHttpClient()
          .AddHttpClient(OpenAIRealtimeRestClient.OPENAI_HTTP_CLIENT_NAME, client =>
          {
              client.Timeout = TimeSpan.FromSeconds(5);
              client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", openAiKey);
          });
        services.AddTransient<IOpenAIRealtimeRestClient, OpenAIRealtimeRestClient>();
        services.AddTransient<IOpenAIRealtimeWebRTCEndPoint, OpenAIRealtimeWebRTCEndPoint>();

        using var provider = services.BuildServiceProvider();

        // Create the OpenAI Realtime WebRTC peer connection.
        var openaiClient = provider.GetRequiredService<IOpenAIRealtimeRestClient>();
        var webrtcEndPoint = provider.GetRequiredService<IOpenAIRealtimeWebRTCEndPoint>();

        // We'll send/receive audio directly from our Windows audio devices.
        InitialiseWindowsAudioEndPoint(webrtcEndPoint, Log.Logger);

        var pcConfig = new RTCConfiguration
        {
            X_UseRtpFeedbackProfile = true,
        };
        var negotiateConnectResult = await webrtcEndPoint.StartConnectAsync(pcConfig);

        if(negotiateConnectResult.IsLeft)
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

        webrtcEndPoint.OnDataChannelMessageReceived += (dc, message) =>
        {
            if (message is OpenAIResponseAudioTranscriptDone done)
            {
                Log.Information($"Transcript done: {done.Transcript}");
            }
        };

        Console.WriteLine("Wait for ctrl-c to indicate user exit.");

        var exitTcs = new TaskCompletionSource<object?>();
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            exitTcs.TrySetResult(null);
        };

        await exitTcs.Task;
    }

    private static void InitialiseWindowsAudioEndPoint(IOpenAIRealtimeWebRTCEndPoint webrtcEndPoint, ILogger logger)
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
}
