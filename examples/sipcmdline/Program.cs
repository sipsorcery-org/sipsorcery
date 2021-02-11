//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: Main program for the sipcmdline application. The aim of this 
// application is to be able to perform certain SIP operations from a command 
// line.
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
//
// Generate client call load:
// Server: sipp -sn uas
// Client: dotnet run -- -d 127.0.0.1 -c 1000 -x 10 -s uac -b true # Test attempts 10 concurrent calls and a total of 1000.
//
// Results:
// 18 May 2020:
// dotnet run -- -d 127.0.0.1 -c 10000 -x 25 -s uac -b true
// [19:17:03 INF] => Command completed task count 10000 success count 10000 duration 189.21
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CommandLine;
using CommandLine.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using Serilog.Extensions.Logging;
using Serilog.Sinks.SystemConsole.Themes;
using SIPSorcery.Media;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery
{
    class Program
    {
        private const int DEFAULT_RESPONSE_TIMEOUT_SECONDS = 5;

        private static Microsoft.Extensions.Logging.ILogger logger = NullLogger.Instance;

        public enum Scenarios
        {
            opt,    // Send OPTIONS requests.
            uac,    // Initiate a SIP call.
            reg,    // Initiate a registration.
            uacw,   // Initiate a SIP call and wait for the remote party to hangup.
        }

        public class Options
        {
            [Option('d', "destination", Required = true,
                HelpText = "The destination SIP end point in the form [(udp|tcp|tls|sip|sips|ws|wss):]<host|ipaddress>[:port] e.g. -d udp:67.222.131.147:5060.")]
            public string Destination { get; set; }

            [Option('t', "timeout", Required = false, Default = DEFAULT_RESPONSE_TIMEOUT_SECONDS, HelpText = "The timeout in seconds for the SIP command to complete.")]
            public int Timeout { get; set; }

            [Option('c', "count", Required = false, Default = 1, HelpText = "The number of requests to send.")]
            public int Count { get; set; }

            [Option('p', "period", Required = false, Default = 1, HelpText = "The period in seconds between sending multiple requests.")]
            public int Period { get; set; }

            [Option('s', "scenario", Required = false, Default = Scenarios.opt, HelpText = "The command scenario to run. Options are [opt|uac|reg|uacw], e.g. -s reg")]
            public Scenarios Scenario { get; set; }

            [Option('x', "concurrent", Required = false, Default = 1, HelpText = "The number of concurrent tasks to run.")]
            public int Concurrent { get; set; }

            [Option('b', "breakonfail", Required = false, Default = false, HelpText = "Cancel the run if a single test fails.")]
            public bool BreakOnFail { get; set; }

            [Option('v', "verbose", Required = false, Default = false, HelpText = "Enable verbose log messages.")]
            public bool Verbose { get; set; }

            [Option("ipv6", Required = false, Default = false, HelpText = "Prefer IPv6 when resolving DNS lookups.")]
            public bool PreferIPv6 { get; set; }

            [Option("port", Required = false, Default = 0, HelpText = "The source SIP port to use.")]
            public int SourcePort { get; set; }

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
            var seriLogger = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .MinimumLevel.Is(Serilog.Events.LogEventLevel.Debug)
                .WriteTo.Console(theme: AnsiConsoleTheme.Code)
                .CreateLogger();
            var factory = new SerilogLoggerFactory(seriLogger);
            SIPSorcery.LogFactory.Set(factory);
            logger = factory.CreateLogger<Program>();

            var result = Parser.Default.ParseArguments<Options>(args)
                .WithParsed<Options>(opts => RunCommand(opts).Wait());
        }

        /// <summary>
        /// Executes the command set by the program's command line arguments.
        /// </summary>
        /// <param name="options">The options that dictate the SIP command to execute.</param>
        static async Task RunCommand(Options options)
        {
            try
            {
                logger.LogDebug($"RunCommand scenario {options.Scenario}, destination {options.Destination}");

                Stopwatch sw = new Stopwatch();
                sw.Start();

                CancellationTokenSource cts = new CancellationTokenSource();
                int taskCount = 0;
                int successCount = 0;

                List<Task> tasks = new List<Task>();

                for (int i = 0; i < options.Concurrent; i++)
                {
                    var task = Task.Run(async () =>
                    {
                        while (taskCount < options.Count && !cts.IsCancellationRequested)
                        {
                            int taskNum = Interlocked.Increment(ref taskCount);
                            bool success = await RunTask(options, taskNum);

                            if (success)
                            {
                                Interlocked.Increment(ref successCount);
                            }
                            else if (options.BreakOnFail)
                            {
                                cts.Cancel();
                                break;
                            }
                            
                            if (success && options.Period > 0)
                            {
                                await Task.Delay(options.Period * 1000);
                            }
                        }
                    }, cts.Token);

                    tasks.Add(task);

                    // Spread the concurrent tasks out a tiny bit.
                    await Task.Delay(Crypto.GetRandomInt(500, 2000));
                }

                // Wait for all the concurrent tasks to complete.
                await Task.WhenAll(tasks.ToArray());

                sw.Stop();

                // Give the transport half a second to shutdown (puts the log messages in a better sequence).
                await Task.Delay(500);

                logger.LogInformation($"=> Command completed task count {taskCount} success count {successCount} duration {sw.Elapsed.TotalSeconds:0.##}s.");
            }
            catch (Exception excp)
            {
                logger.LogError($"Exception RunCommand. {excp.Message}");
            }
        }

        /// <summary>
        /// Runs a single task as part of the overall job.
        /// </summary>
        /// <param name="options">The options that dictate the type of task to run.</param>
        /// <param name="taskNumber">The number assigned to this task.</param>
        /// <returns>A boolean indicating whether this single task succeeded or not.</returns>
        private static async Task<bool> RunTask(Options options, int taskNumber)
        {
            SIPTransport sipTransport = new SIPTransport();
            sipTransport.PreferIPv6NameResolution = options.PreferIPv6;

            if(options.Verbose)
            {
                sipTransport.EnableTraceLogs();
            }

            try
            {
                DateTime startTime = DateTime.Now;

                var dstUri = ParseDestination(options.Destination);

                logger.LogDebug($"Destination SIP URI {dstUri}");

                if(options.SourcePort != 0)
                {
                    var sipChannel = sipTransport.CreateChannel(dstUri.Protocol,
                        options.PreferIPv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork,
                        options.SourcePort);
                    sipTransport.AddSIPChannel(sipChannel);
                }

                Task<bool> task = null;

                switch (options.Scenario)
                {
                    case Scenarios.reg:
                        task = InitiateRegisterTaskAsync(sipTransport, dstUri);
                        break;
                    case Scenarios.uac:
                    case Scenarios.uacw:
                        task = InitiateCallTaskAsync(sipTransport, dstUri, options.Scenario);
                        break;
                    case Scenarios.opt:
                    default:
                        task = SendOptionsTaskAsync(sipTransport, dstUri);
                        break;
                }

                var result = await Task.WhenAny(task, Task.Delay(options.Timeout * 1000));

                TimeSpan duration = DateTime.Now.Subtract(startTime);
                bool failed = false;

                if (!task.IsCompleted)
                {
                    logger.LogWarning($"=> Request to {dstUri} did not get a response on task {taskNumber} after {duration.TotalMilliseconds.ToString("0")}ms.");
                    failed = true;
                }
                else if (!task.Result)
                {
                    logger.LogWarning($"=> Request to {dstUri} did not get the expected response on task {taskNumber} after {duration.TotalMilliseconds.ToString("0")}ms.");
                    failed = true;
                }
                else
                {
                    logger.LogInformation($"=> Got correct response on send {taskNumber} in {duration.TotalMilliseconds.ToString("0")}ms.");
                }

                return !failed;
            }
            finally
            {
                logger.LogDebug("Shutting down the SIP transport...");
                sipTransport.Shutdown();
            }
        }

        /// <summary>
        /// Parses the destination command line option into:
        ///  - A SIPEndPoint, which is an IP end point and transport (udp, tcp or tls),
        ///  - A SIP URI.
        ///  The SIPEndPoint determines the remote network destination to send the request to.
        ///  The SIP URI is the URI that will be set on the request.
        /// </summary>
        /// <param name="dstn">The destination string to parse.</param>
        /// <returns>The SIPEndPoint and SIPURI parsed from the destination string.</returns>
        private static SIPURI ParseDestination(string dst)
        {
            SIPURI dstUri = null;

            // Don't attempt a SIP URI parse for serialised SIPEndPoints.
            if (Regex.IsMatch(dst, "^(udp|tcp|tls|ws|wss)") == false && SIPURI.TryParse(dst, out var argUri))
            {
                dstUri = argUri;
            }
            else
            {
                var dstEp = SIPEndPoint.ParseSIPEndPoint(dst);
                dstUri = new SIPURI(SIPSchemesEnum.sip, dstEp);
            }

            return dstUri;
        }

        /// <summary>
        /// An asynchronous task that attempts to send a single OPTIONS request.
        /// </summary>
        /// <param name="sipTransport">The transport object to use for the send.</param>
        /// <param name="dst">The destination end point to send the request to.</param>
        /// <returns>True if the expected response was received, false otherwise.</returns>
        private static async Task<bool> SendOptionsTaskAsync(SIPTransport sipTransport, SIPURI dst)
        {
            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();

            //UdpClient hepClient = new UdpClient(0, AddressFamily.InterNetwork);

            try
            {
                //sipTransport.SIPRequestOutTraceEvent += (localEP, remoteEP, req) =>
                //{
                //    logger.LogDebug($"Request sent: {localEP}->{remoteEP}");
                //    logger.LogDebug(req.ToString());

                //    //var hepBuffer = HepPacket.GetBytes(localEP, remoteEP, DateTimeOffset.Now, 333, "myHep", req.ToString());
                //    //hepClient.SendAsync(hepBuffer, hepBuffer.Length, "192.168.11.49", 9060);
                //};

                //sipTransport.SIPResponseInTraceEvent += (localEP, remoteEP, resp) =>
                //{
                //    logger.LogDebug($"Response received: {localEP}<-{remoteEP}");
                //    logger.LogDebug(resp.ToString());

                //    //var hepBuffer = HepPacket.GetBytes(remoteEP, localEP, DateTimeOffset.Now, 333, "myHep", resp.ToString());
                //    //hepClient.SendAsync(hepBuffer, hepBuffer.Length, "192.168.11.49", 9060);
                //};

                var optionsRequest = SIPRequest.GetRequest(SIPMethodsEnum.OPTIONS, dst);

                sipTransport.SIPTransportResponseReceived += (SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPResponse sipResponse) =>
                {
                    if (sipResponse.Header.CSeqMethod == SIPMethodsEnum.OPTIONS && sipResponse.Header.CallId == optionsRequest.Header.CallId)
                    {
                        logger.LogDebug($"Expected response received {localSIPEndPoint}<-{remoteEndPoint}: {sipResponse.ShortDescription}");
                        tcs.SetResult(true);
                    }

                    return Task.FromResult(0);
                };

                SocketError sendResult = await sipTransport.SendRequestAsync(optionsRequest, true);
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

        /// <summary>
        /// An asynchronous task that attempts to initiate a new call to a listening UAS.
        /// </summary>
        /// <param name="sipTransport">The transport object to use for the send.</param>
        /// <param name="dst">The destination end point to send the request to.</param>
        /// <returns>True if the expected response was received, false otherwise.</returns>
        private static async Task<bool> InitiateCallTaskAsync(SIPTransport sipTransport, SIPURI dst, Scenarios scenario)
        {
            //UdpClient hepClient = new UdpClient(0, AddressFamily.InterNetwork);

            try
            {
                //sipTransport.SIPRequestOutTraceEvent += (localEP, remoteEP, req) =>
                //{
                //    logger.LogDebug($"Request sent: {localEP}->{remoteEP}");
                //    logger.LogDebug(req.ToString());

                //    //var hepBuffer = HepPacket.GetBytes(localEP, remoteEP, DateTimeOffset.Now, 333, "myHep", req.ToString());
                //    //hepClient.SendAsync(hepBuffer, hepBuffer.Length, "192.168.11.49", 9060);
                //};

                //sipTransport.SIPResponseInTraceEvent += (localEP, remoteEP, resp) =>
                //{
                //    logger.LogDebug($"Response received: {localEP}<-{remoteEP}");
                //    logger.LogDebug(resp.ToString());

                //    //var hepBuffer = HepPacket.GetBytes(remoteEP, localEP, DateTimeOffset.Now, 333, "myHep", resp.ToString());
                //    //hepClient.SendAsync(hepBuffer, hepBuffer.Length, "192.168.11.49", 9060);
                //};

                var ua = new SIPUserAgent(sipTransport, null);
                ua.ClientCallTrying += (uac, resp) => logger.LogInformation($"{uac.CallDescriptor.To} Trying: {resp.StatusCode} {resp.ReasonPhrase}.");
                ua.ClientCallRinging += (uac, resp) => logger.LogInformation($"{uac.CallDescriptor.To} Ringing: {resp.StatusCode} {resp.ReasonPhrase}.");
                ua.ClientCallFailed += (uac, err, resp) => logger.LogWarning($"{uac.CallDescriptor.To} Failed: {err}");
                ua.ClientCallAnswered += (uac, resp) => logger.LogInformation($"{uac.CallDescriptor.To} Answered: {resp.StatusCode} {resp.ReasonPhrase}.");

                var audioOptions = new AudioSourceOptions { AudioSource = AudioSourcesEnum.Silence };
                var audioExtrasSource = new AudioExtrasSource(new AudioEncoder(), audioOptions);
                audioExtrasSource.RestrictFormats(format => format.Codec == AudioCodecsEnum.PCMU);
                var voipMediaSession = new VoIPMediaSession(new MediaEndPoints { AudioSource = audioExtrasSource });

                var result = await ua.Call(dst.ToString(), null, null, voipMediaSession);

                if (scenario == Scenarios.uacw)
                {
                    // Wait for the remote party to hangup the call.
                    logger.LogDebug("Waiting for the remote party to hangup the call...");

                    ManualResetEvent hangupMre = new ManualResetEvent(false);
                    ua.OnCallHungup += (dialog) => hangupMre.Set();

                    hangupMre.WaitOne();

                    logger.LogDebug("Call hungup by remote party.");
                }
                else
                {
                    // We hangup the call after 1s.
                    await Task.Delay(1000);
                    ua.Hangup();
                }

                // Need a bit of time for the BYE to complete.
                await Task.Delay(500);

                return result;
            }
            catch (Exception excp)
            {
                logger.LogError($"Exception InitiateCallTaskAsync. {excp.Message}");
                return false;
            }
        }

        /// <summary>
        /// An asynchronous task that attempts to initiate a registration.
        /// </summary>
        /// <param name="sipTransport">The transport object to use for the send.</param>
        /// <param name="dst">The destination end point to send the request to.</param>
        /// <returns>True if the expected response was received, false otherwise.</returns>
        private static Task<bool> InitiateRegisterTaskAsync(SIPTransport sipTransport, SIPURI dst)
        {
            if (dst.User == null)
            {
                dst.User = "user";
            }

            var ua = new SIPRegistrationUserAgent(
                sipTransport,
                dst.User, 
                "password",
                dst.ToString(),
                120);

            //var ua = new SIPRegistrationUserAgent(
            //    sipTransport,
            //    null,
            //    dst,
            //    dst.User, 
            //    "password",
            //    null,
            //    dst.ToString(),
            //    new SIPURI(dst.Scheme, IPAddress.Any, 0),
            //    120,
            //    null);

            try
            {
                bool result = false;
                ManualResetEvent mre = new ManualResetEvent(false);

                ua.RegistrationFailed += (uri, err) =>
                {
                    result = false;
                    mre.Set();
                };
                ua.RegistrationTemporaryFailure += (uri, err) =>
                {
                    result = false;
                    mre.Set();
                };
                ua.RegistrationSuccessful += (uri) =>
                {
                    result = true;
                    mre.Set();
                };

                ua.Start();

                mre.WaitOne(TimeSpan.FromSeconds(2000));

                ua.Stop(false);

                return Task.FromResult(result);
            }
            catch (Exception excp)
            {
                logger.LogError($"Exception InitiateRegisterTaskAsync. {excp.Message}");
                ua.Stop();
                return Task.FromResult(false);
            }
        }
    }
}
