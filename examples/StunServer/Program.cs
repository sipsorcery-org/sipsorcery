//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An example STUN server console application.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 08 Nov 2019	Aaron Clauson   Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Net;
using Microsoft.Extensions.Logging;
using Serilog;
using SIPSorcery.Net;

namespace StunServerExample
{
    class Program
    {
        private static Microsoft.Extensions.Logging.ILogger Log = SIPSorcery.Sys.Log.Logger;

        static void Main()
        {
            Console.WriteLine("Example STUN Server");

            ConfigureConsoleLog();

            // STUN servers need two separater end points to listen on.
            IPEndPoint primaryEndPoint = new IPEndPoint(IPAddress.Any, 3478);
            IPEndPoint secondaryEndPoint = new IPEndPoint(IPAddress.Any, 3479);

            // Create the two STUN listeners and wire up the STUN server.
            STUNListener primarySTUNListener = new STUNListener(primaryEndPoint);
            STUNListener secondarySTUNListener = new STUNListener(secondaryEndPoint);
            STUNServer stunServer = new STUNServer(primaryEndPoint, primarySTUNListener.Send, secondaryEndPoint, secondarySTUNListener.Send);
            primarySTUNListener.MessageReceived += stunServer.STUNPrimaryReceived;
            secondarySTUNListener.MessageReceived += stunServer.STUNSecondaryReceived;

            // Optional. Provides verbose logs of STUN server activity.
            EnableVerboseLogs(stunServer);

            Console.WriteLine("STUN server successfully initialised.");
                       
            Console.Write("press any key to exit...");
            Console.Read();

            primarySTUNListener.Close();
            secondarySTUNListener.Close();
            stunServer.Stop();
        }

        /// <summary>
        /// Configures a console logger.
        /// </summary>
        private static void ConfigureConsoleLog()
        {
            // Logging configuration. Can be ommitted if internal SIPSorcery debug and warning messages are not required.
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
        /// Logs receives and sends by the STUN server.
        /// </summary>
        /// <param name="stunServer">The STUN server to enable verbose logs for.</param>
        private static void EnableVerboseLogs(STUNServer stunServer)
        {
            stunServer.STUNPrimaryRequestInTraceEvent += (localEndPoint, fromEndPoint, stunMessage) =>
            {
                Log.LogDebug($"pri recv {localEndPoint}<-{fromEndPoint}: {stunMessage.ToString()}");
            };

            stunServer.STUNSecondaryRequestInTraceEvent += (localEndPoint, fromEndPoint, stunMessage) =>
            {
                Log.LogDebug($"sec recv {localEndPoint}<-{fromEndPoint}: {stunMessage.ToString()}");
            };

            stunServer.STUNPrimaryResponseOutTraceEvent += (localEndPoint, fromEndPoint, stunMessage) =>
            {
                Log.LogDebug($"pri send {localEndPoint}->{fromEndPoint}: {stunMessage.ToString()}");
            };

            stunServer.STUNSecondaryResponseOutTraceEvent += (localEndPoint, fromEndPoint, stunMessage) =>
            {
                Log.LogDebug($"sec send {localEndPoint}->{fromEndPoint}: {stunMessage.ToString()}");
            };
        }
    }
}
