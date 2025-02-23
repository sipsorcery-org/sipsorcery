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

using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;
using Serilog;
using SIPSorceryMedia.FFmpeg;
using Microsoft.Extensions.DependencyInjection;

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
        builder.Services.AddSingleton<WebRtcConnectionManager>();
        builder.Services.AddSingleton<PeerConnectionPayState>();

        builder.Services.AddControllers();

        var app = builder.Build();

        // Activate SIPSorcery library logging
        var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
        SIPSorcery.LogFactory.Set(loggerFactory);

        // Initialise FFmpeg.
        var logger = loggerFactory.CreateLogger<Program>();
        FFmpegInit.Initialise(FfmpegLogLevelEnum.AV_LOG_VERBOSE, null, logger);

        app.UseRouting();
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.MapControllers();

        app.Run();
    }
}
