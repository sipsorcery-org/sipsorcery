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
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using Serilog.Extensions.Logging;
using Serilog.Events;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;

namespace SIPSorcery.Register
{
    class Program
    {
        private const string USERNAME = "user";
        private const string PASSWORD = "password";
        private const string DOMAIN = "sipsorcery.cloud";
        private const int EXPIRY = 120;

        private static Microsoft.Extensions.Logging.ILogger Log = NullLogger.Instance;

        static void Main()
        {
            Console.WriteLine("SIPSorcery registration user agent example.");
            Console.WriteLine("Press ctrl-c to exit.");

            Log = AddConsoleLogger(LogEventLevel.Verbose);

            // Set up a default SIP transport.
            var sipTransport = new SIPTransport();
            sipTransport.EnableTraceLogs();

            // Create a client user agent to maintain a periodic registration with a SIP server.
            var regUserAgent = new SIPRegistrationUserAgent(sipTransport, USERNAME, PASSWORD, DOMAIN, EXPIRY);

            // Event handlers for the different stages of the registration.
            regUserAgent.RegistrationFailed += (uri, err) => Log.LogError($"{uri}: {err}");
            regUserAgent.RegistrationTemporaryFailure += (uri, msg) => Log.LogWarning($"{uri}: {msg}");
            regUserAgent.RegistrationRemoved += (uri) => Log.LogError($"{uri} registration failed.");
            regUserAgent.RegistrationSuccessful += (uri) => Log.LogInformation($"{uri} registration succeeded.");

            // Start the thread to perform the initial registration and then periodically resend it.
            regUserAgent.Start();

            ManualResetEvent exitMRE = new ManualResetEvent(false);
            Console.CancelKeyPress += (object sender, ConsoleCancelEventArgs e) =>
            {
                e.Cancel = true;
                Log.LogInformation("Exiting...");
                exitMRE.Set();
            };

            exitMRE.WaitOne();

            regUserAgent.Stop();
            sipTransport.Shutdown();
        }

        /// <summary>
        /// Adds a console logger. Can be omitted if internal SIPSorcery debug 
        /// and warning messages are not required.
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
