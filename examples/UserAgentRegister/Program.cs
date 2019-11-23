//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An example program of how to use the SIPSorcery core library 
// to register a SIP account. 
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//  
// History:
// 07 Oct 2019	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Net;
using System.Threading;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using Serilog;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.Register
{
    class Program
    {
        private const int SUCCESS_REGISTRATION_COUNT = 3;   // Number of successful registrations to attempt before exiting process.

        private static Microsoft.Extensions.Logging.ILogger Log = SIPSorcery.Sys.Log.Logger;

        static void Main()
        {
            Console.WriteLine("SIPSorcery registration user agent example.");
            Console.WriteLine("Press ctrl-c to exit.");

            AddConsoleLogger();

            // Set up a default SIP transport.
            var sipTransport = new SIPTransport();
            var sipChannel = new SIPUDPChannel(IPAddress.Any, 0);
            sipTransport.AddSIPChannel(sipChannel);

            //EnableTraceLogs(sipTransport);

            // Create a client user agent to maintain a periodic registration with a SIP server.
            var regUserAgent = new SIPRegistrationUserAgent(
                sipTransport,
                "softphonesample",
                "password",
                "sipsorcery.com",
                120);

            int successCounter = 0;
            ManualResetEvent taskCompleteMre = new ManualResetEvent(false);

            // Event handlers for the different stages of the registration.
            regUserAgent.RegistrationFailed += (uri, err) => SIPSorcery.Sys.Log.Logger.LogError($"{uri.ToString()}: {err}");
            regUserAgent.RegistrationTemporaryFailure += (uri, msg) => SIPSorcery.Sys.Log.Logger.LogWarning($"{uri.ToString()}: {msg}");
            regUserAgent.RegistrationRemoved += (uri) => SIPSorcery.Sys.Log.Logger.LogError($"{uri.ToString()} registration failed.");
            regUserAgent.RegistrationSuccessful += (uri) =>
            {
                SIPSorcery.Sys.Log.Logger.LogInformation($"{uri.ToString()} registration succeeded.");
                Interlocked.Increment(ref successCounter);
                SIPSorcery.Sys.Log.Logger.LogInformation($"Successful registrations {successCounter} of {SUCCESS_REGISTRATION_COUNT}.");

                if (successCounter == SUCCESS_REGISTRATION_COUNT)
                {
                    taskCompleteMre.Set();
                }
            };

            Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                SIPSorcery.Sys.Log.Logger.LogInformation("Exiting...");
                taskCompleteMre.Set();
            };

            // Start the thread to perform the initial registration and then periodically resend it.
            regUserAgent.Start();

            taskCompleteMre.WaitOne();

            regUserAgent.Stop();
            if (sipTransport != null)
            {
                SIPSorcery.Sys.Log.Logger.LogInformation("Shutting down SIP transport...");
                sipTransport.Shutdown();
            }
            SIPSorcery.Net.DNSManager.Stop();
        }

        /// <summary>
        ///  Adds a console logger. Can be ommitted if internal SIPSorcery debug and warning messages are not required.
        /// </summary>
        private static void AddConsoleLogger()
        {
            var loggerFactory = new Microsoft.Extensions.Logging.LoggerFactory();
            var loggerConfig = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .MinimumLevel.Is(Serilog.Events.LogEventLevel.Debug)
                .WriteTo.Console()
                .CreateLogger();
            loggerFactory.AddSerilog(loggerConfig);
            SIPSorcery.Sys.Log.LoggerFactory = loggerFactory;
        }

        /// <summary>
        /// Enable detailed SIP log messages.
        /// </summary>
        private static void EnableTraceLogs(SIPTransport sipTransport)
        {
            sipTransport.SIPRequestInTraceEvent += (localEP, remoteEP, req) =>
            {
                Log.LogDebug($"Request received: {localEP}<-{remoteEP}");
                Log.LogDebug(req.ToString());
            };

            sipTransport.SIPRequestOutTraceEvent += (localEP, remoteEP, req) =>
            {
                Log.LogDebug($"Request sent: {localEP}->{remoteEP}");
                Log.LogDebug(req.ToString());
            };

            sipTransport.SIPResponseInTraceEvent += (localEP, remoteEP, resp) =>
            {
                Log.LogDebug($"Response received: {localEP}<-{remoteEP}");
                Log.LogDebug(resp.ToString());
            };

            sipTransport.SIPResponseOutTraceEvent += (localEP, remoteEP, resp) =>
            {
                Log.LogDebug($"Response sent: {localEP}->{remoteEP}");
                Log.LogDebug(resp.ToString());
            };

            sipTransport.SIPRequestRetransmitTraceEvent += (tx, req, count) =>
            {
                Log.LogDebug($"Request retransmit {count} for request {req.StatusLine}, initial transmit {DateTime.Now.Subtract(tx.InitialTransmit).TotalSeconds.ToString("0.###")}s ago.");
            };

            sipTransport.SIPResponseRetransmitTraceEvent += (tx, resp, count) =>
            {
                Log.LogDebug($"Response retransmit {count} for response {resp.ShortDescription}, initial transmit {DateTime.Now.Subtract(tx.InitialTransmit).TotalSeconds.ToString("0.###")}s ago.");
            };
        }
    }
}
