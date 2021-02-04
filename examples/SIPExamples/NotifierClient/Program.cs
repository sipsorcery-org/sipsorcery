//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: A getting started program to demonstrate how to subscribe to
// a SIP server for Message Waiting Indications (MWI).
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 20 Jan 2021	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Threading;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;
using Serilog.Events;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;

namespace demo
{
    class Program
    {
        private const string USERNAME = "user";
        private const string PASSWORD = "password";
        private const string SERVER = "sipsorcery.cloud";

        static void Main()
        {
            Console.WriteLine("SIPSorcery Notification Client Demo");

            // Use verbose to get display the full SIP messages.
            AddConsoleLogger(LogEventLevel.Verbose);

            var sipTransport = new SIPTransport();
            sipTransport.EnableTraceLogs();

            var mwiURI = SIPURI.ParseSIPURIRelaxed($"{USERNAME}@{SERVER}");
            int expiry = 180;

            SIPNotifierClient mwiSubscriber = new SIPNotifierClient(sipTransport, null, SIPEventPackagesEnum.MessageSummary, mwiURI, USERNAME, null, PASSWORD, expiry, null);
            mwiSubscriber.SubscriptionFailed += (uri, failureStatus, errorMessage) => Console.WriteLine($"MWI failed for {uri}, {errorMessage}");
            mwiSubscriber.SubscriptionSuccessful += (uri) => Console.WriteLine($"MWI subscription successful for {uri}");
            mwiSubscriber.NotificationReceived += (evt, msg) => Console.WriteLine($"MWI notification, type {evt}, message {msg}.");

            mwiSubscriber.Start();

            Console.WriteLine("pressany key to exit...");
            Console.ReadLine();

            Console.WriteLine("Exiting...");

            // Clean up.
            mwiSubscriber.Stop();
            Thread.Sleep(1000);
            sipTransport.Shutdown();
        }

        /// <summary>
        /// Adds a console logger. Can be omitted if internal SIPSorcery debug and 
        /// warning messages are not required.
        /// </summary>
        private static Microsoft.Extensions.Logging.ILogger AddConsoleLogger(
            LogEventLevel logLevel = LogEventLevel.Debug)
        {
            var serilogLogger = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .MinimumLevel.Is(logLevel)
                .WriteTo.Console()
                .CreateLogger();
            var factory = new SerilogLoggerFactory(serilogLogger);
            SIPSorcery.LogFactory.Set(factory);
            return factory.CreateLogger<Program>();
        }
    }
}
