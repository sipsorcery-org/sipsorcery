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
        private const string DEFAULT_SERVER = "sips:sipsorcery.cloud";
        private const string DEFAULT_USERNAME = "user";
        private const string DEFAULT_PASSWORD = "password";
        private const int DEFAULT_EXPIRY = 120;

        private static Microsoft.Extensions.Logging.ILogger Log = NullLogger.Instance;

        static void Main(string[] args)
        {
            Console.WriteLine("SIPSorcery registration user agent example.");
            Console.WriteLine("Press ctrl-c to exit.");

            Log = AddConsoleLogger(LogEventLevel.Verbose);

            string server = DEFAULT_SERVER;
            string username = DEFAULT_USERNAME;
            string password = DEFAULT_PASSWORD;
            int expiry = DEFAULT_EXPIRY;

            int posn = 0;
            while(posn < args?.Length && posn <= 3)
            {
                switch(posn)
                {
                    case 0:
                        server = args[posn++].Trim();
                        break;
                    case 1:
                        username = args[posn++].Trim();
                        break;
                    case 2:
                        password = args[posn++].Trim();
                        break;
                    case 3:
                        int.TryParse(args[posn++], out expiry);
                        break;
                }
            }

            Console.WriteLine("Attempting registration with:");
            Console.WriteLine($" server: {server}");
            Console.WriteLine($" username: {username}");
            Console.WriteLine($" expiry: {expiry}");

            // Set up a default SIP transport.
            var sipTransport = new SIPTransport();
            sipTransport.EnableTraceLogs();

            // Create a client user agent to maintain a periodic registration with a SIP server.
            var regUserAgent = new SIPRegistrationUserAgent(sipTransport, username, password, server, expiry);

            // Event handlers for the different stages of the registration.
            regUserAgent.RegistrationFailed += (uri, err) => Log.LogWarning($"{uri}: {err}");
            regUserAgent.RegistrationTemporaryFailure += (uri, msg) => Log.LogWarning($"{uri}: {msg}");
            regUserAgent.RegistrationRemoved += (uri) => Log.LogWarning($"{uri} registration failed.");
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
