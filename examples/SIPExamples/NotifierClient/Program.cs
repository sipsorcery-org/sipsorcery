//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: A getting started program to demonstrate how to subscribe to
// a SIP server for Message Waiting Inidications (MWI).
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
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;

namespace demo
{
    class Program
    {
        private const string USERNAME = "username";
        private const string PASSWORD = "password";
        private const string SERVER = "sipsorcery.com";

        static void Main()
        {
            Console.WriteLine("SIPSorcery Getting Started Demo");

            AddConsoleLogger();
            CancellationTokenSource exitCts = new CancellationTokenSource();

            var sipTransport = new SIPTransport();

            EnableTraceLogs(sipTransport);

            var mwiURI = SIPURI.ParseSIPURIRelaxed($"{USERNAME}@{SERVER}");
            int expiry = 180;

            SIPNotifierClient<SIPEvent> mwiSubscriber = new SIPNotifierClient<SIPEvent>(sipTransport, null, SIPEventPackage.MessageSummary, mwiURI, USERNAME, null, PASSWORD, expiry, null);
            mwiSubscriber.SubscriptionFailed += (uri, failureStatus, errorMessage) => Console.WriteLine($"MWI failed for {uri}, {errorMessage}");
            mwiSubscriber.SubscriptionSuccessful += (uri) => Console.WriteLine($"MWI subscription successful for {uri}");

            mwiSubscriber.Start();

            Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                exitCts.Cancel();
            };

            Console.WriteLine("press ctrl-c to exit...");
            exitCts.Token.WaitHandle.WaitOne();

            Console.WriteLine("Exiting...");

            // Clean up.
            sipTransport.Shutdown();
        }

        /// <summary>
        /// Enable detailed SIP log messages.
        /// </summary>
        private static void EnableTraceLogs(SIPTransport sipTransport)
        {
            sipTransport.SIPRequestInTraceEvent += (localEP, remoteEP, req) =>
            {
                Console.WriteLine($"Request received: {localEP}<-{remoteEP}");
                Console.WriteLine(req.ToString());
            };

            sipTransport.SIPRequestOutTraceEvent += (localEP, remoteEP, req) =>
            {
                Console.WriteLine($"Request sent: {localEP}->{remoteEP}");
                Console.WriteLine(req.ToString());
            };

            sipTransport.SIPResponseInTraceEvent += (localEP, remoteEP, resp) =>
            {
                Console.WriteLine($"Response received: {localEP}<-{remoteEP}");
                Console.WriteLine(resp.ToString());
            };

            sipTransport.SIPResponseOutTraceEvent += (localEP, remoteEP, resp) =>
            {
                Console.WriteLine($"Response sent: {localEP}->{remoteEP}");
                Console.WriteLine(resp.ToString());
            };

            sipTransport.SIPRequestRetransmitTraceEvent += (tx, req, count) =>
            {
                Console.WriteLine($"Request retransmit {count} for request {req.StatusLine}, initial transmit {DateTime.Now.Subtract(tx.InitialTransmit).TotalSeconds.ToString("0.###")}s ago.");
            };

            sipTransport.SIPResponseRetransmitTraceEvent += (tx, resp, count) =>
            {
                Console.WriteLine($"Response retransmit {count} for response {resp.ShortDescription}, initial transmit {DateTime.Now.Subtract(tx.InitialTransmit).TotalSeconds.ToString("0.###")}s ago.");
            };
        }

        /// <summary>
        ///  Adds a console logger. Can be omitted if internal SIPSorcery debug and warning messages are not required.
        /// </summary>
        private static Microsoft.Extensions.Logging.ILogger AddConsoleLogger()
        {
            var serilogLogger = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .MinimumLevel.Is(Serilog.Events.LogEventLevel.Debug)
                .WriteTo.Console()
                .CreateLogger();
            var factory = new SerilogLoggerFactory(serilogLogger);
            SIPSorcery.LogFactory.Set(factory);
            return factory.CreateLogger<Program>();
        }
    }
}
