//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An example of how top send DTMF tones in band (with specific RTP
// packets) as specified in RFC2833.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 12 Nov 2019	Aaron Clauson   Created, Dublin, Ireland.
// 20 Feb 2020  Aaron Clauson   Switched to RtpAVSession and simplified.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// Note on call flow and destinations being used in this sample.
//
// This example calls a destination (represented by DEFAULT_DESTINATION_SIP_URI)
// and expects a SIP agent capable of receiving DTMF with RFC2833 to be listening.
// What the receiving SIP does with the received DTMF is up to it. A good example
// is to playback the presses via speech synthesis. The dialplan below is an
// example of how to do that with Asterisk.
//
// Example Asterisk dialplan snippet to repeat back any DTMF tones received:
//
// exten => *63,1(start),Gotoif($[ "${LEN(${extensao})}" < "3"]?collect:bye)
// exten => *63,n(collect),Read(digito,,1)
// exten => *63,n,SayDigits(${digito})
// exten => *63,n,Set(extensao=${extensao}${digito})
// exten => *63,n,GoTo(start)
// exten => *63,n(bye),Playback("vm-goodbye")
// exten => *63,n,hangup()
//-----------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Serilog;
using SIPSorcery.Media;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;

namespace SIPSorcery
{
    class Program
    {
        private static readonly string DEFAULT_DESTINATION_SIP_URI = "sip:*63@192.168.11.48";   // Custom Asterisk dialplan to speak back DTMF tones.

        private static Microsoft.Extensions.Logging.ILogger Log = SIPSorcery.Sys.Log.Logger;

        static async Task Main()
        {
            Console.WriteLine("SIPSorcery Send DTMF Tones example.");
            Console.WriteLine("Press ctrl-c to exit.");

            // Plumbing code to facilitate a graceful exit.
            CancellationTokenSource rtpCts = new CancellationTokenSource(); // Cancellation token to stop the RTP stream.

            AddConsoleLogger();

            var sipTransport = new SIPTransport();
            var userAgent = new SIPUserAgent(sipTransport, null);
            var rtpSession = new RtpAVSession(new AudioOptions { AudioSource = AudioSourcesEnum.CaptureDevice }, null);

            // Place the call and wait for the result.
            bool callResult = await userAgent.Call(DEFAULT_DESTINATION_SIP_URI, null, null, rtpSession);

            // Ctrl-c will gracefully exit the call at any point.
            Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                rtpCts.Cancel();
            };

            if (callResult)
            {
                Console.WriteLine("Call attempt successful.");

                // Give the call some time to answer.
                await Task.Delay(1000);

                // Send the DTMF tones.
                await userAgent.SendDtmf(0x05);
                await Task.Delay(2000);
                await userAgent.SendDtmf(0x09);
                await Task.Delay(2000);
                await userAgent.SendDtmf(0x02);
                await Task.Delay(2000);

                if (userAgent.IsCallActive)
                {
                    Console.WriteLine("Hanging up.");
                    rtpCts.Cancel();
                    userAgent.Hangup();
                }
            }
            else
            {
                Console.WriteLine("Call attempt failed.");
            }

            Log.LogInformation("Exiting...");

            // Clean up.
            sipTransport.Shutdown();
        }

        /// <summary>
        ///  Adds a console logger. Can be omitted if internal SIPSorcery debug and warning messages are not required.
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
