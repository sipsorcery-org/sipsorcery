//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An example WebRTC server application that use Bitcoin Lightning
// payments to control a WebRTC video stream.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 23 Feb 2025	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorceryMedia.FFmpeg;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace demo;

class Program
{
    private const string LINUX_FFMPEG_LIB_PATH = "/usr/local/lib/";

    private static string _stunUrl = string.Empty;
    private static bool _waitForIceGatheringToSendOffer = false;

    static async Task Main(string[] args)
    {
        Console.WriteLine("WebRTC Lightning Demo");

        _stunUrl = Environment.GetEnvironmentVariable("STUN_URL") ?? string.Empty;
        bool.TryParse(Environment.GetEnvironmentVariable("WAIT_FOR_ICE_GATHERING_TO_SEND_OFFER"), out _waitForIceGatheringToSendOffer);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .CreateLogger();

        var factory = new SerilogLoggerFactory(Log.Logger);
        SIPSorcery.LogFactory.Set(factory);
        var programLogger = factory.CreateLogger<Program>();

        if (Environment.OSVersion.Platform == PlatformID.Unix)
        {
            FFmpegInit.Initialise(FfmpegLogLevelEnum.AV_LOG_VERBOSE, LINUX_FFMPEG_LIB_PATH, programLogger);
        }
        else
        {
            FFmpegInit.Initialise(FfmpegLogLevelEnum.AV_LOG_VERBOSE, null, programLogger);
        }

        programLogger.LogDebug(_stunUrl != null ? $"STUN URL: {_stunUrl}" : "No STUN URL provided.");
        programLogger.LogDebug($"Wait for ICE gathering to send offer: {_waitForIceGatheringToSendOffer}");

        var builder = WebApplication.CreateBuilder();

        builder.Host.UseSerilog();

        builder.Services.AddHostedService<LndInvoiceListener>();
        builder.Services.AddSingleton<LightningInvoiceEventService>();
        builder.Services.AddTransient<IPaidWebRtcConnection, PaidWebRtcConnection>();
        builder.Services.AddTransient<ILightningPaymentService, LightningPaymentService>();
        builder.Services.AddTransient<ILightningClientFactory, LightningClientFactory>();
        builder.Services.AddTransient<ILightningService, LightningService>();
        builder.Services.AddTransient<IAnnotatedBitmapGenerator, AnnotatedBitmapService>();
        builder.Services.AddTransient<IAnnotatedBitmapGenerator, AnnotatedBitmapService>();
        builder.Services.AddTransient<IFrameConfigStateMachine, PaymentStateMachine>();

        var app = builder.Build();

        // Activate SIPSorcery library logging
        var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
        SIPSorcery.LogFactory.Set(loggerFactory);

        // Initialise FFmpeg.
        var logger = loggerFactory.CreateLogger<Program>();
        FFmpegInit.Initialise(FfmpegLogLevelEnum.AV_LOG_VERBOSE, null, logger);

        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.UseWebSockets();

        app.Map("/ws", async
            (HttpContext context,
             [FromServices] IPaidWebRtcConnection paidWebRtcConnection,
             [FromServices] ILogger<Program> wsLogger) =>
        {
            wsLogger.LogDebug("Web socket client connection established.");

            if (context.WebSockets.IsWebSocketRequest)
            {
                var webSocket = await context.WebSockets.AcceptWebSocketAsync();

                RTCConfiguration config = new RTCConfiguration
                {
                    X_ICEIncludeAllInterfaceAddresses = true
                };

                if (!string.IsNullOrWhiteSpace(_stunUrl))
                {
                    config.iceServers = new List<RTCIceServer> { new RTCIceServer { urls = _stunUrl } };
                }

                var webSocketPeer = new WebRTCWebSocketPeerAspNet(
                    webSocket,
                    (wsLogger) => paidWebRtcConnection.CreatePeerConnection(config),
                    RTCSdpType.offer,
                    wsLogger);

                webSocketPeer.OfferOptions = new RTCOfferOptions
                {
                    X_WaitForIceGatheringToComplete = _waitForIceGatheringToSendOffer
                };

                await webSocketPeer.Run();

                await webSocketPeer.Close();
            }
            else
            {
                // Not a WebSocket request
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
            }
        });

        await app.RunAsync();
    }
}
