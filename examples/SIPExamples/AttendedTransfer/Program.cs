//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An example program of how to use the SIPSorcery core library to 
// place or receive two calls and then bridge them together to accomplish an
// attended transfer.
//
// Testing:
// This example does not play well with all SIP user agents. It seems support
// for attended transfers is patchy. A successful scenario was:
// 
// 1. Start this application,
// 2. Place first call from Bria to this app (gets automatically answered),
// 3. Place second call from MicroSIP to this app (gets automatically answered),
// 4. Press the 't' key and the transfer works.
//
// Note if the order of the Bria and MicroSIP calls are reversed the MicroSIP
// softphone will crash when the 't' key is pressed.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 12 Dec 2019	Aaron Clauson	Created, Dublin, Ireland.
// 20 Feb 2020  Aaron Clauson   Switched to RtpAVSession and simplified.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Windows;

namespace SIPSorcery
{
    class Program
    {
        private static int SIP_LISTEN_PORT = 5060;
        private static int TRANSFER_TIMEOUT_SECONDS = 2;    // Give up on transfer if no response within this period.
        private static readonly string DEFAULT_DESTINATION_SIP_URI = "sip:127.0.0.1:50803"; //"sip:1@172.19.16.1:7060";

        // If set will mirror SIP packets to a Homer (sipcapture.org) logging and analysis server.
        private static string HOMER_SERVER_ADDRESS = null; //"192.168.11.49";
        private static int HOMER_SERVER_PORT = 9060;

        private static Microsoft.Extensions.Logging.ILogger Log = NullLogger.Instance;

        static void Main()
        {
            Console.WriteLine("SIPSorcery Attended Transfer example.");
            Console.WriteLine("Press 'c' to place a call to the default destination.");
            Console.WriteLine("Place two simultaneous SIP calls to this program and then press 't'.");
            Console.WriteLine("Press 'q' or ctrl-c to exit.");

            // Plumbing code to facilitate a graceful exit.
            CancellationTokenSource exitCts = new CancellationTokenSource(); // Cancellation token to stop the SIP transport and RTP stream.

            Log = AddConsoleLogger();

            // Set up a default SIP transport.
            var sipTransport = new SIPTransport();
            sipTransport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(IPAddress.Any, SIP_LISTEN_PORT)));

            //EnableTraceLogs(sipTransport);

            // Create two user agents. Each gets configured to answer an incoming call.
            var userAgent1 = new SIPUserAgent(sipTransport, null);
            var userAgent2 = new SIPUserAgent(sipTransport, null);

            userAgent1.OnCallHungup += (dialog) => Log.LogInformation($"UA1: Call hungup by remote party.");
            userAgent1.ServerCallCancelled += (uas) => Log.LogInformation("UA1: Incoming call cancelled by caller.");

            userAgent2.OnCallHungup += (dialog) => Log.LogInformation($"UA2: Call hungup by remote party.");
            userAgent2.ServerCallCancelled += (uas) => Log.LogInformation("UA2: Incoming call cancelled by caller.");

            userAgent2.OnTransferNotify += (sipFrag) =>
            {
                if (!string.IsNullOrEmpty(sipFrag))
                {
                    Log.LogInformation($"UA2: Transfer status update: {sipFrag.Trim()}.");
                    if (sipFrag?.Contains("SIP/2.0 200") == true)
                    {
                        // The transfer attempt got a successful answer. Can hangup the call.
                        userAgent2.Hangup();
                        exitCts.Cancel();
                    }
                }
            };

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
                    if (!userAgent1.IsCallActive || !userAgent2.IsCallActive)
                    {
                        SIPUserAgent activeAgent = (!userAgent1.IsCallActive) ? userAgent1 : userAgent2;
                        string agentDesc = (!userAgent1.IsCallActive) ? "UA1" : "UA2";

                        Log.LogInformation($"{agentDesc}: Incoming call request from {remoteEndPoint}: {sipRequest.StatusLine}.");
                        var incomingCall = activeAgent.AcceptCall(sipRequest);

                        WindowsAudioEndPoint winAudio = new WindowsAudioEndPoint(new AudioEncoder());
                        var voipMediaSession = new VoIPMediaSession(winAudio.ToMediaEndPoints());

                        await activeAgent.Answer(incomingCall, voipMediaSession);
                        Log.LogInformation($"{agentDesc}: Answered incoming call from {sipRequest.Header.From.FriendlyDescription()} at {remoteEndPoint}.");
                    }
                    else
                    {
                        // If both user agents are already on a call return a busy response.
                        Log.LogWarning($"Busy response returned for incoming call request from {remoteEndPoint}: {sipRequest.StatusLine}.");
                        UASInviteTransaction uasTransaction = new UASInviteTransaction(sipTransport, sipRequest, null);
                        SIPResponse busyResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.BusyHere, null);
                        uasTransaction.SendFinalResponse(busyResponse);
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
                            // Place an outgoing call using the first free user agent.
                            SIPUserAgent freeAgent = (!userAgent1.IsCallActive) ? userAgent1 : (!userAgent2.IsCallActive) ? userAgent2 : null;
                            if (freeAgent != null)
                            {
                                WindowsAudioEndPoint winAudio = new WindowsAudioEndPoint(new AudioEncoder());
                                var voipMediaSession = new VoIPMediaSession(winAudio.ToMediaEndPoints());
                                bool callResult = await freeAgent.Call(DEFAULT_DESTINATION_SIP_URI, null, null, voipMediaSession);

                                Log.LogInformation($"Call attempt {((callResult) ? "successfull" : "failed")}.");
                            }
                            else
                            {
                                Log.LogWarning("Both user agents are already on calls.");
                            }
                        }
                        if (keyProps.KeyChar == 't')
                        {
                            // Initiate the attended transfer.
                            if (userAgent1.IsCallActive && userAgent2.IsCallActive)
                            {
                                bool result = await userAgent2.AttendedTransfer(userAgent1.Dialogue, TimeSpan.FromSeconds(TRANSFER_TIMEOUT_SECONDS), exitCts.Token);
                                if (!result)
                                {
                                    Log.LogWarning($"Attended transfer failed.");
                                }
                            }
                            else
                            {
                                Log.LogWarning("There need to be two active calls before the attended transfer can occur.");
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
                   Log.LogError($"Exception Key Press listener. {excp.Message}.");
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

            userAgent1?.Hangup();
            userAgent2?.Hangup();

            // Give any BYE or CANCEL requests time to be transmitted.
            Log.LogInformation("Waiting 1s for calls to be cleaned up...");
            Task.Delay(1000).Wait();

            if (sipTransport != null)
            {
                Log.LogInformation("Shutting down SIP transport...");
                sipTransport.Shutdown();
            }

            #endregion
        }

        /// <summary>
        /// Enable detailed SIP log messages.
        /// </summary>
        private static void EnableTraceLogs(SIPTransport sipTransport)
        {
            UdpClient homerSIPClient = null;

            if (HOMER_SERVER_ADDRESS != null)
            {
                homerSIPClient = new UdpClient(0, AddressFamily.InterNetwork);
            }

            sipTransport.SIPRequestInTraceEvent += (localEP, remoteEP, req) =>
            {
                Log.LogDebug($"Request received: {localEP}<-{remoteEP}");
                Log.LogDebug(req.ToString());

                if (homerSIPClient != null)
                {
                    var hepBuffer = HepPacket.GetBytes(remoteEP, localEP, DateTime.Now, 333, "myHep", req.ToString());
                    homerSIPClient.SendAsync(hepBuffer, hepBuffer.Length, HOMER_SERVER_ADDRESS, HOMER_SERVER_PORT);
                }
            };

            sipTransport.SIPRequestOutTraceEvent += (localEP, remoteEP, req) =>
            {
                Log.LogDebug($"Request sent: {localEP}->{remoteEP}");
                Log.LogDebug(req.ToString());

                if (homerSIPClient != null)
                {
                    var hepBuffer = HepPacket.GetBytes(localEP, remoteEP, DateTime.Now, 333, "myHep", req.ToString());
                    homerSIPClient.SendAsync(hepBuffer, hepBuffer.Length, HOMER_SERVER_ADDRESS, HOMER_SERVER_PORT);
                }
            };

            sipTransport.SIPResponseInTraceEvent += (localEP, remoteEP, resp) =>
            {
                Log.LogDebug($"Response received: {localEP}<-{remoteEP}");
                Log.LogDebug(resp.ToString());

                if (homerSIPClient != null)
                {
                    var hepBuffer = HepPacket.GetBytes(remoteEP, localEP, DateTime.Now, 333, "myHep", resp.ToString());
                    homerSIPClient.SendAsync(hepBuffer, hepBuffer.Length, HOMER_SERVER_ADDRESS, HOMER_SERVER_PORT);
                }
            };

            sipTransport.SIPResponseOutTraceEvent += (localEP, remoteEP, resp) =>
            {
                Log.LogDebug($"Response sent: {localEP}->{remoteEP}");
                Log.LogDebug(resp.ToString());

                if (homerSIPClient != null)
                {
                    var hepBuffer = HepPacket.GetBytes(localEP, remoteEP, DateTime.Now, 333, "myHep", resp.ToString());
                    homerSIPClient.SendAsync(hepBuffer, hepBuffer.Length, HOMER_SERVER_ADDRESS, HOMER_SERVER_PORT);
                }
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
