﻿//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: A getting started program to demonstrate how to use the SIPSorcery
// library to place a call.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 13 Oct 2019	Aaron Clauson	Created, Dublin, Ireland.
// 31 Dec 2019  Aaron Clauson   Changed from an OPTIONS example to a call example.
// 20 Feb 2020  Aaron Clauson   Switched to RtpAVSession and simplified.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Net.Sockets;
using System.Threading.Tasks;
using Serilog;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Media;

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
            var rtpSession = new RtpAVSession(AddressFamily.InterNetwork, new AudioSourceOptions { AudioSource = AudioSourcesEnum.Microphone }, null);

            // Place the call and wait for the result.
            bool callResult = await userAgent.Call(DESTINATION, null, null, rtpSession);

            if(callResult)
            {
                Console.WriteLine("Call attempt successful.");
            }
            else
            {
                Console.WriteLine("Call attempt failed.");
            }
          
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
