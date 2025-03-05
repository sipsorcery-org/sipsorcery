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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using SIPSorceryMedia.FFmpeg;
using System;

namespace demo;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("WebRTC Lightning Demo");

        var builder = WebApplication.CreateBuilder(args);
        builder.Host.UseSerilog((context, services, config) =>
        {
            config.WriteTo.Console()
                .Enrich.FromLogContext()
                .MinimumLevel.Is(Serilog.Events.LogEventLevel.Debug);
        });

        builder.Services.AddHostedService<WebSocketService>();
        builder.Services.AddHostedService<LndInvoiceListener>();
        builder.Services.AddSingleton<LightningInvoiceEventService>();
        builder.Services.AddTransient<IPaidWebRtcConnectionFactory, PaidWebRtcConnectionFactory>();
        builder.Services.AddTransient<ILightningPaymentService, LightningPaymentService>();
        builder.Services.AddTransient<ILightningClientFactory, LightningClientFactory>();
        builder.Services.AddTransient<ILightningService, LightningService>();
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

        app.Run();
    }
}
