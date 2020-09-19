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

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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

            host.Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddHostedService<WebRTCWorker>();
                }).UseWindowsService();
    }
}
