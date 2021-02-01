//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: A getting started program to demonstrate how to use the 
// SIPTransport class to respond to OPTIONS requests.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 13 Oct 2019	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.SIP;

namespace demo
{
    class Program
    {
        private static Microsoft.Extensions.Logging.ILogger Log = NullLogger.Instance;

        static void Main()
        {
            Console.WriteLine("SIPSorcery Getting Started Demo");

            var sipTransport = new SIPTransport();
            EnableTraceLogs(sipTransport);

            var sipChannel = new SIPWebSocketChannel(IPAddress.Loopback, 80);

            var wssCertificate = new System.Security.Cryptography.X509Certificates.X509Certificate2("localhost.pfx");
            var sipChannelSecure = new SIPWebSocketChannel(IPAddress.Loopback, 443, wssCertificate);

            sipTransport.AddSIPChannel(sipChannel);
            sipTransport.AddSIPChannel(sipChannelSecure);

            sipTransport.SIPTransportRequestReceived += async (SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest) =>
            {
                Console.WriteLine($"Request received {localSIPEndPoint}<-{remoteEndPoint}: {sipRequest.StatusLine}");

                if (sipRequest.Method == SIPMethodsEnum.OPTIONS | sipRequest.Method == SIPMethodsEnum.MESSAGE)
                {
                    SIPResponse okResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                    await sipTransport.SendResponseAsync(okResponse);
                }
                else if (sipRequest.Method == SIPMethodsEnum.REGISTER)
                {
                    SIPResponse okResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                    okResponse.Header.Contact = sipRequest.Header.Contact;
                    await sipTransport.SendResponseAsync(okResponse);
                }
            };

            Console.Write("press any key to exit...");
            Console.Read();

            sipTransport.Shutdown();
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

        /// <summary>
        /// Adds a console logger. Can be omitted if internal SIPSorcery debug and warning messages are not required.
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
