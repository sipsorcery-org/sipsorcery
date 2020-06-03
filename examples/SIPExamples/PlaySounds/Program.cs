//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An example program to play pre-recorded sound files on a SIP
// call.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 03 Jun 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using SIPSorcery.Media;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;

namespace demo
{
    class Program
    {
        private static string DESTINATION = "1@127.0.0.1"; //"time@sipsorcery.com";

        private static string WELCOME_SOUND_FILE_8K = "Sounds/hellowelcome8k.raw";
        private static string WELCOME_SOUND_FILE_16K = "Sounds/hellowelcome16k.raw";

        static async Task Main()
        {
            Console.WriteLine("SIPSorcery Play Sounds Demo");

            AddConsoleLogger();

            var sipTransport = new SIPTransport();

            EnableTraceLogs(sipTransport);

            var userAgent = new SIPUserAgent(sipTransport, null);
            var rtpSession = new WindowsAudioRtpSession();

            // Place the call and wait for the result.
            bool callResult = await userAgent.Call(DESTINATION, null, null, rtpSession);
            Console.WriteLine($"Call result {((callResult) ? "success" : "failure")}.");

            CancellationTokenSource cts = new CancellationTokenSource();

            if (callResult)
            {
                userAgent.OnCallHungup += (dialog) => cts.Cancel();
                await Task.Delay(1000, cts.Token);
                //rtpSession.SendAudioFromStream(new FileStream(WELCOME_SOUND_FILE_8K, FileMode.Open), AudioSamplingRatesEnum.SampleRate8KHz);
                await rtpSession.SendAudioFromStream(new FileStream(WELCOME_SOUND_FILE_16K, FileMode.Open), AudioSamplingRatesEnum.SampleRate16KHz);
                Console.WriteLine("Stream task complete.");
            }

            Console.WriteLine("press any key to exit...");
            Console.Read();

            if (userAgent.IsCallActive)
            {
                Console.WriteLine("Hanging up.");
                userAgent.Hangup();
            }

            // Give the hangup a chance to complete.
            await Task.Delay(1000);

            // Clean up.
            sipTransport.Shutdown();
            SIPSorcery.Net.DNSManager.Stop();
        }

        /// <summary>
        /// Adds a console logger. Can be omitted if internal SIPSorcery debug and warning messages are not required.
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
    }
}
