//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: A getting started program to demonstrate how to use the SIPSorcery
// library to place a call using PortAudio for audio capture and render.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 17 Apr 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// Usage on Linux:
// The PortAduioSharp package DOES NOT install the required native libraries on
// Linux.
//
// The approach I used to install the PortAudio library on Linux was:
// - Install Microsoft's C++ pakcage manager (yes it works on Linux) from
//   https://github.com/microsoft/vcpkg,
// - Use vcpkg to install PortAudio: ./vcpkg install portaudio,
// - Build this app using "dotnet run" in the same directory as the project file. It's
//   expected to build successfully but fail at runtime with an error that it cannot
//   locate the portaudio library.
// - Copy the portaudio.so from <vcpkg dir>/installed/x64-linux/lib/portaudio.so to
//   the bin directory, for example:
//   aaron@pcdodo:~/src/sipsorcery/examples/GetStartedPortAudio/bin/Debug/netcoreapp3.1$ cp ~/src/vcpkg/installed/x64-linux/lib/libportaudio.so .
// - Retry "dotnet run". Please report success or failure at https://github.com/sipsorcery/sipsorcery/issues/196.

using System;
using System.Threading.Tasks;
using Serilog;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;

namespace demo
{
    class Program
    {
        //private static string DESTINATION = "time@sipsorcery.com";
        private static string DESTINATION = "*61@192.168.11.48";

        static async Task Main()
        {
            Console.WriteLine("SIPSorcery Getting Started Demo");

            AddConsoleLogger();

            var sipTransport = new SIPTransport();
            var userAgent = new SIPUserAgent(sipTransport, null);
            var rtpSession = new PortAudioRtpSession();

            // Place the call and wait for the result.
            bool callResult = await userAgent.Call(DESTINATION, null, null, rtpSession);
            Console.WriteLine($"Call result {((callResult) ? "success" : "failure")}.");

            Console.WriteLine("press any key to exit...");
            Console.Read();

            if (userAgent.IsCallActive)
            {
                Console.WriteLine("Hanging up.");
                userAgent.Hangup();
            }

            // Clean up.
            sipTransport.Shutdown();
            SIPSorcery.Net.DNSManager.Stop();
        }

        /// <summary>
        ///  Adds a console logger. Can be omitted if internal SIPSorcery debug and warning messages are not required.
        /// </summary>
        private static void AddConsoleLogger()
        {
            var loggerFactory = new Microsoft.Extensions.Logging.LoggerFactory();
            var loggerConfig = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .MinimumLevel.Is(Serilog.Events.LogEventLevel.Information)
                .WriteTo.Console()
                .CreateLogger();
            loggerFactory.AddSerilog(loggerConfig);
            SIPSorcery.Sys.Log.LoggerFactory = loggerFactory;
        }
    }
}

