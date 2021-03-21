//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: A console to test SCTP client functions.
//
// Remarks:
// A good reference implementation to test with is
// https://github.com/sctplab/usrsctp.
//
// An example of running a server is:
// c:\Dev\github\usrsctp\build\programs\Debug>echo_server 11111 22222
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// St Patrick's Day 2021	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;

namespace SctpClientTestConsole
{
    class Program
    {
        static SctpUdpTransport _sctpTransport;

        static async Task Main(string[] args)
        {
            Console.WriteLine("SCTP Client Test Console");

            AddConsoleLogger(LogEventLevel.Verbose);

            _sctpTransport = new SctpUdpTransport();
            var association = _sctpTransport.Associate(
                new IPEndPoint(IPAddress.Parse("192.168.0.50"), 11111), 4444, 7);

            association.OnAssociationStateChanged += (state) =>
            {
                if (state == SctpAssociationState.Established)
                {
                    association.Send("hi\n");
                }
            };

            association.OnData += (data) =>
            {
                Console.WriteLine($"Data received: {Encoding.UTF8.GetString(data)}");
            };

            Console.WriteLine("press any key to exit...");
            Console.ReadLine();

            Console.WriteLine("Sending shutdown...");

            association.Shutdown();

            await Task.Delay(1000);

            Console.WriteLine("Exiting.");
        }

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
