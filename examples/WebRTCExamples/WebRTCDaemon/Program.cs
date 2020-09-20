//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: Main entry point to start the application.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 18 Sep 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// To install as a Windows Service (NOTE: make sure to put the full path to the executable) :
// [project dir]> dotnet publish -c Release
// [publish dir]> sc create "SIPSorcery WebRtc Daemon" binpath="<publish dir>\WebRTCDaemon.exe" start=auto
//
// To uninstall Windows Service:
// [publish dir]>sc delete "SIPSorcery WebRTC Daemon" 
//
// For a self contained single file executable:
// [project dir]> dotnet publish -r win-x64 -p:PublishSIngleFile=true -c Release --self-contained true
//-----------------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace WebRTCDaemon
{
    public class Program
    {
        public static void Main(string[] args)
        {
            //Windows service has system32 as default working folder, we change the working dir to install dir for file access
            System.IO.Directory.SetCurrentDirectory(System.AppDomain.CurrentDomain.BaseDirectory);

             var host = CreateHostBuilder(args).Build();

            SIPSorcery.LogFactory.Set(host.Services.GetService<ILoggerFactory>());

            var logger = host.Services.GetService<ILoggerFactory>().CreateLogger<Program>();

            logger.LogInformation("WebRTC Daemon Starting...");

            host.Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
            .UseSerilog((hostingContext, services, loggerConfiguration) => loggerConfiguration
                .ReadFrom.Configuration(hostingContext.Configuration)
                .Enrich.FromLogContext())
            .ConfigureServices((hostContext, services) =>
                {
                    services.AddHostedService<WebRTCWorker>();
                })
            .UseWindowsService();
    }
}
