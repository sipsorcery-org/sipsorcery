//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: This example program is part of a Blind Transfer demo. 
// This program is acting as the Transfer Target.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 15 May 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Serilog;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;

namespace SIPSorcery
{
    class Program
    {
        private static int SIP_LISTEN_PORT = 6072;
        private static byte[] DTMF_SEQUENCEFOR_TRANSFEROR = { 6, 0, 7, 2 };

        private static SIPTransport _sipTransport;

        private static Microsoft.Extensions.Logging.ILogger Log = SIPSorcery.Sys.Log.Logger;

        static void Main()
        {
            Console.WriteLine("SIPSorcery Blind Transfer Demo: Transfer Target");
            Console.WriteLine("Waiting for incoming call from Transferor.");
            Console.WriteLine("Press 'q' or ctrl-c to exit.");

            // Plumbing code to facilitate a graceful exit.
            CancellationTokenSource exitCts = new CancellationTokenSource(); // Cancellation token to stop the SIP transport and RTP stream.

            AddConsoleLogger();

            // Set up a default SIP transport.
            _sipTransport = new SIPTransport();
            _sipTransport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(IPAddress.Any, SIP_LISTEN_PORT)));

            EnableTraceLogs(_sipTransport);

            var userAgent = new SIPUserAgent(_sipTransport, null);
            userAgent.ServerCallCancelled += (uas) => Log.LogDebug("Incoming call cancelled by remote party.");
            userAgent.OnCallHungup += (dialog) => Log.LogDebug("Call hungup by remote party.");
            userAgent.OnIncomingCall += async (ua, req) =>
            {
                List<SDPMediaFormatsEnum> codecs = new List<SDPMediaFormatsEnum> { SDPMediaFormatsEnum.PCMU, SDPMediaFormatsEnum.PCMA, SDPMediaFormatsEnum.G722 };
                var audioOptions = new AudioSourceOptions { AudioSource = AudioSourcesEnum.Silence };
                var rtpAudioSession = new RtpAudioSession(audioOptions, codecs);

                var uas = ua.AcceptCall(req);
                bool answerResult = await ua.Answer(uas, rtpAudioSession);
                Log.LogDebug($"Answer incoming call result {answerResult}.");

                _ = Task.Run(async () =>
                {
                    await Task.Delay(1000);

                    Log.LogDebug($"Sending DTMF sequence {string.Join("", DTMF_SEQUENCEFOR_TRANSFEROR.Select(x => x))}.");
                    foreach (byte dtmf in DTMF_SEQUENCEFOR_TRANSFEROR)
                    {
                        Log.LogDebug($"Sending DTMF tone to transferor {dtmf}.");
                        await ua.SendDtmf(dtmf);
                    }
                });
            };
            userAgent.OnDtmfTone += (key, duration) => Log.LogInformation($"Received DTMF tone {key}.");

            Task.Run(() => OnKeyPress(userAgent, exitCts));

            exitCts.Token.WaitHandle.WaitOne();

            #region Cleanup.

            Log.LogInformation("Exiting...");

            //userAgent?.Hangup();

            // Give any BYE or CANCEL requests time to be transmitted.
            Log.LogInformation("Waiting 1s for calls to be cleaned up...");
            Task.Delay(1000).Wait();

            if (_sipTransport != null)
            {
                Log.LogInformation("Shutting down SIP transport...");
                _sipTransport.Shutdown();
            }

            #endregion
        }

        /// <summary>
        /// Process user key presses.
        /// </summary>
        /// <param name="exit">The cancellation token to set if the user requests to quit the application.</param>
        private static void OnKeyPress(SIPUserAgent userAgent, CancellationTokenSource exitCts)
        {
            try
            {
                while (!exitCts.IsCancellationRequested)
                {
                    var keyProps = Console.ReadKey();

                    if (keyProps.KeyChar == 'h')
                    {
                        if (userAgent != null && userAgent.IsCallActive)
                        {
                            Log.LogDebug("Hanging up active call.");
                            userAgent.Hangup();
                        }
                        else
                        {
                            Log.LogDebug("No active call.");
                        }
                    }
                    else if (keyProps.KeyChar == 'a')
                    {
                        Log.LogDebug($"Yes I am alive, is call active ? {userAgent?.IsCallActive}.");
                    }
                    else if (keyProps.KeyChar == 'q')
                    {
                        // Quit application.
                        Log.LogInformation("Quitting");
                        exitCts.Cancel();
                        break;
                    }
                }
            }
            catch (Exception excp)
            {
                Log.LogError($"Exception OnKeyPress. {excp.Message}.");
            }
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
