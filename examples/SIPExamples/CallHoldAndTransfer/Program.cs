//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An example program of how to use the SIPSorcery core library to 
// place a SIP call and then place it on and off hold as well as demonstrate how
// a blind transfer can be initiated.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 25 Nov 2019	Aaron Clauson	Created, Dublin, Ireland.
// 20 Feb 2020  Aaron Clauson   Switched to RtpAVSession and simplified.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
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
        private static int SIP_LISTEN_PORT = 5060;
        private static readonly string DEFAULT_DESTINATION_SIP_URI = "sip:7001@192.168.11.24";
        //private static readonly string DEFAULT_DESTINATION_SIP_URI = "sip:7000@192.168.11.48";
        private static readonly string TRANSFER_DESTINATION_SIP_URI = "sip:*60@192.168.11.24";  // The destination to transfer the initial call to.
        private static readonly string SIP_USERNAME = "7000";
        private static readonly string SIP_PASSWORD = "password";
        private static int TRANSFER_TIMEOUT_SECONDS = 10;                    // Give up on transfer if no response within this period.
        private const string AUDIO_FILE_PCMU = "media/Macroform_-_Simplicity.ulaw";

        private static Microsoft.Extensions.Logging.ILogger Log = SIPSorcery.Sys.Log.Logger;

        private static string _currentDir;

        static void Main()
        {
            Console.WriteLine("SIPSorcery Call Hold and Blind Transfer example.");
            Console.WriteLine("Press 'c' to initiate a call to the default destination.");
            Console.WriteLine("Press 'h' to place an established call on and off hold.");
            Console.WriteLine("Press 'H' to hangup an established call.");
            Console.WriteLine("Press 't' to request a blind transfer on an established call.");
            Console.WriteLine("Press 'q' or ctrl-c to exit.");

            // Plumbing code to facilitate a graceful exit.
            CancellationTokenSource exitCts = new CancellationTokenSource(); // Cancellation token to stop the SIP transport and RTP stream.

            AddConsoleLogger();

            // Set up a default SIP transport.
            var sipTransport = new SIPTransport();
            sipTransport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(IPAddress.Any, SIP_LISTEN_PORT)));

            Console.WriteLine($"Listening for incoming calls on: {sipTransport.GetSIPChannels().First().ListeningEndPoint}.");

            EnableTraceLogs(sipTransport);

            _currentDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);

            RtpAVSession rtpAVSession = null;

            // Create a client/server user agent to place a call to a remote SIP server along with event handlers for the different stages of the call.
            var userAgent = new SIPUserAgent(sipTransport, null);
            userAgent.RemotePutOnHold += () => Log.LogInformation("Remote call party has placed us on hold.");
            userAgent.RemoteTookOffHold += () => Log.LogInformation("Remote call party took us off hold.");

            sipTransport.SIPTransportRequestReceived += async (localEndPoint, remoteEndPoint, sipRequest) =>
            {
                if (sipRequest.Header.From != null &&
                    sipRequest.Header.From.FromTag != null &&
                    sipRequest.Header.To != null &&
                    sipRequest.Header.To.ToTag != null)
                {
                    // This is an in-dialog request that will be handled directly by a user agent instance.
                }
                else if (sipRequest.Method == SIPMethodsEnum.INVITE)
                {
                    if (userAgent?.IsCallActive == true)
                    {
                        Log.LogWarning($"Busy response returned for incoming call request from {remoteEndPoint}: {sipRequest.StatusLine}.");
                        // If we are already on a call return a busy response.
                        UASInviteTransaction uasTransaction = new UASInviteTransaction(sipTransport, sipRequest, null);
                        SIPResponse busyResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.BusyHere, null);
                        uasTransaction.SendFinalResponse(busyResponse);
                    }
                    else
                    {
                        Log.LogInformation($"Incoming call request from {remoteEndPoint}: {sipRequest.StatusLine}.");
                        var incomingCall = userAgent.AcceptCall(sipRequest);

                        rtpAVSession = new RtpAVSession(new AudioOptions { AudioSource = AudioSourcesEnum.CaptureDevice }, null);
                        await userAgent.Answer(incomingCall, rtpAVSession);

                        Log.LogInformation($"Answered incoming call from {sipRequest.Header.From.FriendlyDescription()} at {remoteEndPoint}.");
                    }
                }
                else
                {
                    Log.LogDebug($"SIP {sipRequest.Method} request received but no processing has been set up for it, rejecting.");
                    SIPResponse notAllowedResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.MethodNotAllowed, null);
                    await sipTransport.SendResponseAsync(notAllowedResponse);
                }
            };

            // At this point the call has been initiated and everything will be handled in an event handler.
            Task.Run(async () =>
            {
                try
                {
                    while (!exitCts.Token.WaitHandle.WaitOne(0))
                    {
                        var keyProps = Console.ReadKey();

                        if (keyProps.KeyChar == 'c')
                        {
                            if (!userAgent.IsCallActive)
                            {
                                rtpAVSession = new RtpAVSession(new AudioOptions { AudioSource = AudioSourcesEnum.CaptureDevice }, null);
                                bool callResult = await userAgent.Call(DEFAULT_DESTINATION_SIP_URI, SIP_USERNAME, SIP_PASSWORD, rtpAVSession);

                                Log.LogInformation($"Call attempt {((callResult) ? "successfull" : "failed")}.");
                            }
                            else
                            {
                                Log.LogWarning("There is already an active call.");
                            }
                        }
                        else if (keyProps.KeyChar == 'h')
                        {
                            // Place call on/off hold.
                            if (userAgent.IsCallActive)
                            {
                                if (userAgent.IsOnLocalHold)
                                {
                                    Log.LogInformation("Taking the remote call party off hold.");
                                    userAgent.TakeOffHold();
                                    await (userAgent.MediaSession as RtpAVSession).SetSources(new AudioOptions { AudioSource = AudioSourcesEnum.CaptureDevice }, null);
                                }
                                else
                                {
                                    Log.LogInformation("Placing the remote call party on hold.");
                                    userAgent.PutOnHold();
                                    await (userAgent.MediaSession as RtpAVSession).SetSources(new AudioOptions
                                    {
                                        AudioSource = AudioSourcesEnum.Music,
                                        SourceFiles = new Dictionary<SDPMediaFormatsEnum, string>
                                        {
                                            { SDPMediaFormatsEnum.PCMU, _currentDir + "/" + AUDIO_FILE_PCMU }
                                        }
                                    }, null);
                                }
                            }
                            else
                            {
                                Log.LogWarning("There is no active call to put on hold.");
                            }
                        }
                        else if (keyProps.KeyChar == 'H')
                        {
                            if (userAgent.IsCallActive)
                            {
                                Log.LogInformation("Hanging up call.");
                                userAgent.Hangup();
                            }
                        }
                        else if (keyProps.KeyChar == 't')
                        {
                            // Initiate a blind transfer to the remote call party.
                            if (userAgent.IsCallActive)
                            {
                                var transferURI = SIPURI.ParseSIPURI(TRANSFER_DESTINATION_SIP_URI);
                                bool result = await userAgent.BlindTransfer(transferURI, TimeSpan.FromSeconds(TRANSFER_TIMEOUT_SECONDS), exitCts.Token);
                                if (result)
                                {
                                    // If the transfer was accepted the original call will already have been hungup.
                                    // Wait a second for the transfer NOTIFY request to arrive.
                                    await Task.Delay(1000);
                                    exitCts.Cancel();
                                }
                                else
                                {
                                    Log.LogWarning($"Transfer to {TRANSFER_DESTINATION_SIP_URI} failed.");
                                }
                            }
                            else
                            {
                                Log.LogWarning("There is no active call to transfer.");
                            }
                        }
                        else if (keyProps.KeyChar == 'q')
                        {
                            // Quit application.
                            exitCts.Cancel();
                        }
                    }
                }
                catch (Exception excp)
                {
                    SIPSorcery.Sys.Log.Logger.LogError($"Exception Key Press listener. {excp.Message}.");
                }
            });

            // Ctrl-c will gracefully exit the call at any point.
            Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                exitCts.Cancel();
            };

            // Wait for a signal saying the call failed, was cancelled with ctrl-c or completed.
            exitCts.Token.WaitHandle.WaitOne();

            #region Cleanup.

            Log.LogInformation("Exiting...");

            rtpAVSession?.Close("app exit");

            if (userAgent != null)
            {
                if (userAgent.IsCallActive)
                {
                    Log.LogInformation($"Hanging up call to {userAgent?.CallDescriptor?.To}.");
                    userAgent.Hangup();
                }

                // Give the BYE or CANCEL request time to be transmitted.
                Log.LogInformation("Waiting 1s for call to clean up...");
                Task.Delay(1000).Wait();
            }

            if (sipTransport != null)
            {
                Log.LogInformation("Shutting down SIP transport...");
                sipTransport.Shutdown();
            }

            #endregion
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
