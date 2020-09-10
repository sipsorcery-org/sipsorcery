//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: A getting started program to demonstrate how to use the SIPSorcery
// library to place a call targeting the .Net Framework.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 17 Apr 2020 Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions.V1;
using SIPSorceryMedia.Windows;

namespace demo
{
    class Program
    {
        private static string DESTINATION = "time@sipsorcery.com";

        static async Task Main()
        {
            Console.WriteLine("SIPSorcery Getting Started Demo");

            AddConsoleLogger();

            var sipTransport = new SIPTransport();
            var userAgent = new SIPUserAgent(sipTransport, null);
            var winAudio = new WindowsAudioEndPoint(new AudioEncoder());
            var voipMediaSession = new VoIPMediaSession(new MediaEndPoints { AudioSink = winAudio, AudioSource = winAudio });

            // Place the call and wait for the result.
            bool callResult = await userAgent.Call(DESTINATION, null, null, voipMediaSession);
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
        }

        /// <summary>
        ///  Adds a console logger. Can be omitted if internal SIPSorcery debug and warning messages are not required.
        /// </summary>
        private static void AddConsoleLogger()
        {
            var serilogLogger = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .MinimumLevel.Is(Serilog.Events.LogEventLevel.Debug)
                .WriteTo.Console()
                .CreateLogger();
            var factory = new SerilogLoggerFactory(serilogLogger);
            SIPSorcery.LogFactory.Set(factory);
        }
    }
}