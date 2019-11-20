//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: Main program for the sipcmdline application. The aim of this application is to be able
// to perform certain SIP operations from a command line.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 17 Oct 2019	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// Usage examples:
// Send 3 options requests to a SIP server listening 127.0.0.1 & [::1] and default ports 5060 (UDP & TCP) & 5061 (TLS):
//
// IPv4:
// sipcmdline -d 100@127.0.0.1:5060        # scheme: sip  & transport: UDP
// sipcmdline -d sip:100@127.0.0.1:5060    # scheme: sip  & transport: UDP
// sipcmdline -d sips:100@127.0.0.1        # scheme: sips & transport: TLS
// sipcmdline -d udp:127.0.0.1:5060        # scheme: sip  & transport: UDP
// sipcmdline -d tcp:127.0.0.1:5060        # scheme: sip  & transport: TCP
// sipcmdline -d tls:127.0.0.1:5061        # scheme: sip  & transport: TLS
// sipcmdline -d tls:127.0.0.1             # scheme: sip  & transport: TLS
//
// IPv6:
// sipcmdline -d 100@[::1]:5060        # scheme: sip  & transport: UDP
// sipcmdline -d sip:100@[::1]:5060    # scheme: sip  & transport: UDP
// sipcmdline -d sips:100@[::1]        # scheme: sips & transport: TLS
// sipcmdline -d udp:[::1]             # scheme: sip  & transport: UDP
// sipcmdline -d tcp:[::1]:5060        # scheme: sip  & transport: TCP
// sipcmdline -d tls:[::1]:5061        # scheme: sip  & transport: TLS
// sipcmdline -d tls:[::1]             # scheme: sip  & transport: TLS
//
// DNS:
// sipcmdline -d localhost                 # scheme: sip  & transport: UDP
// sipcmdline -d localhost                 # scheme: sip  & transport: UDP
// sipcmdline -d 100@localhost:5060        # scheme: sip  & transport: UDP
// sipcmdline -d sip:100@127.0.0.1:5060    # scheme: sip  & transport: UDP
// sipcmdline -d 127.0.0.1;transport=tcp   # scheme: sip  & transport: TCP
// sipcmdline -d 127.0.0.1;transport=tls   # scheme: sip  & transport: TLS
// sipcmdline -d sips:100@127.0.0.1        # scheme: sips & transport: TLS
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using CommandLine;
using CommandLine.Text;
using Microsoft.Extensions.Logging;
using Serilog;
using SIPSorcery.SIP.App;
using SIPSorcery.Net;
using SIPSorcery.SIP;

namespace SIPSorcery
{
    class Program
    {
        private const int DEFAULT_SIP_CLIENT_PORT = 9060;
        private const int DEFAULT_SIPS_CLIENT_PORT = 9061;

        private static Microsoft.Extensions.Logging.ILogger logger;

        public class Options
        {
            [Option('d', "destination", Required = true,
                HelpText = "The destination SIP end point in the form [(udp|tcp|tls|sip|sips):]<host|ipaddress>[:port] e.g. udp:67.222.131.147:5060.")]
            public string Destination { get; set; }

            [Option('t', "timeout", Required = false, Default = 5, HelpText = "The timeout in seconds for the SIP command to complete.")]
            public int Timeout { get; set; }

            [Option('c', "count", Required = false, Default = 1, HelpText = "The number of requests to send.")]
            public int Count { get; set; }

            [Option('p', "period", Required = false, Default = 1, HelpText = "The period between sending multiple requests.")]
            public int Period { get; set; }

            [Usage(ApplicationAlias = "sipcmdline")]
            public static IEnumerable<Example> Examples
            {
                get
                {
                    return new List<Example>() {
                        new Example("Send an OPTIONS request to a SIP server", new Options { Destination = "udp:67.222.131.147:5060" })
                    };
                }
            }
        }

        static void Main(string[] args)
        {
            var loggerFactory = new Microsoft.Extensions.Logging.LoggerFactory();
            var loggerConfig = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .MinimumLevel.Is(Serilog.Events.LogEventLevel.Debug)
                .WriteTo.Console()
                .CreateLogger();
            loggerFactory.AddSerilog(loggerConfig);
            SIPSorcery.Sys.Log.LoggerFactory = loggerFactory;
            logger = SIPSorcery.Sys.Log.Logger;

            var result = Parser.Default.ParseArguments<Options>(args)
                .WithParsed<Options>(opts => RunCommand(opts));
        }

        /// <summary>
        /// Executes the command set by the program's command line arguments.
        /// </summary>
        /// <param name="options">The options that dictate the SIP command to execute.</param>
        static async void RunCommand(Options options)
        {
            try
            {
                logger.LogDebug($"RunCommand {options.Destination}");

                (var dstEp, var dstUri) = ParseDestination(options.Destination);

                logger.LogDebug($"Destination IP end point {dstEp} and SIP URI {dstUri}");

                int sendCount = 0;
                bool success = true;

                do
                {
                    IPAddress localAddress = (dstEp.Address.AddressFamily == AddressFamily.InterNetworkV6) ? IPAddress.IPv6Any : IPAddress.Any;
                    SIPChannel sipChannel = null;

                    switch (dstEp.Protocol)
                    {
                        case SIPProtocolsEnum.tcp:
                            sipChannel = new SIPTCPChannel(new IPEndPoint(localAddress, DEFAULT_SIP_CLIENT_PORT));
                            (sipChannel as SIPTCPChannel).DisableLocalTCPSocketsCheck = true; // Allow sends to listeners on this host.
                            break;
                        case SIPProtocolsEnum.tls:
                            var certificate = new X509Certificate2(@"localhost.pfx", "");
                            sipChannel = new SIPTLSChannel(certificate, new IPEndPoint(localAddress, DEFAULT_SIPS_CLIENT_PORT));
                            break;
                        case SIPProtocolsEnum.udp:
                            sipChannel = new SIPUDPChannel(new IPEndPoint(localAddress, DEFAULT_SIP_CLIENT_PORT));
                            break;
                        case SIPProtocolsEnum.ws:
                            sipChannel = new SIPWebSocketChannel(new IPEndPoint(localAddress, DEFAULT_SIP_CLIENT_PORT), null);
                            break;
                        case SIPProtocolsEnum.wss:
                            var wsCertificate = new X509Certificate2(@"localhost.pfx", "");
                            sipChannel = new SIPWebSocketChannel(new IPEndPoint(localAddress, DEFAULT_SIP_CLIENT_PORT), wsCertificate);
                            break;
                        default:
                            throw new ApplicationException($"Don't know how to create SIP channel for transport {dstEp.Protocol}.");
                    }

                    SIPTransport sipTransport = new SIPTransport();
                    sipTransport.AddSIPChannel(sipChannel);

                    if (sendCount > 0 && options.Period > 0) await Task.Delay(options.Period * 1000);

                    sendCount++;

                    DateTime sendTime = DateTime.Now;
                    var sendTask = SendOptionsTaskAsync(sipTransport, dstUri);
                    var result = await Task.WhenAny(sendTask, Task.Delay(options.Timeout * 1000));

                    TimeSpan duration = DateTime.Now.Subtract(sendTime);

                    if (!sendTask.IsCompleted)
                    {
                        logger.LogWarning($"=> Request to {dstEp} did not get a response on send {sendCount} of {options.Count} after {duration.TotalMilliseconds.ToString("0")}ms.");
                        success = false;
                    }
                    else if (!sendTask.Result)
                    {
                        logger.LogWarning($"=> Request to {dstEp} did not get the expected response on request {sendCount} of {options.Count} after {duration.TotalMilliseconds.ToString("0")}ms.");
                        success = false;
                    }
                    else
                    {
                        logger.LogInformation($"=> Got correct response on send {sendCount} of {options.Count} in {duration.TotalMilliseconds.ToString("0")}ms.");
                    }

                    logger.LogDebug("Shutting down the SIP transport...");
                    sipTransport.Shutdown();

                    if (success == false)
                    {
                        break;
                    }
                }
                while (sendCount < options.Count);

                DNSManager.Stop();

                // Give the transport half a second to shutdown (puts the log messages in a better sequence).
                await Task.Delay(500);

                logger.LogInformation($"=> Command completed {((success) ? "successfully" : "with failure")}.");
            }
            catch (Exception excp)
            {
                logger.LogError($"Exception RunCommand. {excp.Message}");
            }
        }

        /// <summary>
        /// Parses the destination command line option into:
        ///  - A SIPEndPoint, which is an IP end point and transport (udp, tcp or tls),
        ///  - A SIP URI.
        ///  The SIPEndPoint determines the remote network destination to send the request to.
        ///  The SIP URI is whe URI that will be set on the request.
        /// </summary>
        /// <param name="dstn">The destination string to parse.</param>
        /// <returns>The SIPEndPoint and SIPURI parsed from the destination string.</returns>
        private static (SIPEndPoint, SIPURI) ParseDestination(string dst)
        {
            var dstEp = SIPEndPoint.ParseSIPEndPoint(dst);

            SIPURI dstUri = null;
            // Don't attempt a SIP URI parse for serialised SIPEndPoints.
            if (Regex.IsMatch(dst, "^(udp|tcp|tls|ws|wss)") == false && SIPURI.TryParse(dst))
            {
                dstUri = SIPURI.ParseSIPURIRelaxed(dst);
            }
            else
            {
                dstUri = new SIPURI(dstEp.Scheme, dstEp);
            }

            if (dstEp == null)
            {
                logger.LogDebug($"Could not extract IP end point from destination host of {dstUri.Host}.");
                var result = SIPDNSManager.ResolveSIPService(dstUri, false);
                if (result != null)
                {
                    logger.LogDebug($"Resolved SIP URI {dstUri} to {result.GetSIPEndPoint()}.");
                    dstEp = result.GetSIPEndPoint();
                }
            }

            return (dstEp, dstUri);
        }

        /// <summary>
        /// An asynchronous task that attempts to send a single OPTIONS request.
        /// </summary>
        /// <param name="sipTransport">The transport object to use for the send.</param>
        /// <param name="dst">The destination end point to send the request to.</param>
        /// <returns>True if the expected response was recevived, false otherwise.</returns>
        private static async Task<bool> SendOptionsTaskAsync(SIPTransport sipTransport, SIPURI dst)
        {
            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();

            try
            {
                sipTransport.SIPTransportResponseReceived += (SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPResponse sipResponse) =>
                {
                    logger.LogDebug($"Response received {localSIPEndPoint.ToString()}<-{remoteEndPoint.ToString()}: {sipResponse.ShortDescription}");
                    logger.LogDebug(sipResponse.ToString());

                    tcs.SetResult(true);
                };

                var optionsRequest = sipTransport.GetRequest(SIPMethodsEnum.OPTIONS, dst);

                logger.LogDebug(optionsRequest.ToString());

                SocketError sendResult = await sipTransport.SendRequestAsync(optionsRequest);
                if (sendResult != SocketError.Success)
                {
                    logger.LogWarning($"Attempt to send request failed with {sendResult}.");
                    tcs.SetResult(false);
                }
            }
            catch (Exception excp)
            {
                logger.LogError($"Exception SendOptionsTask. {excp.Message}");
                tcs.SetResult(false);
            }

            return await tcs.Task;
        }
    }
}
