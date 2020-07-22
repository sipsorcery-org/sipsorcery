//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: This example program is part of a Blind Transfer demo. 
// This program is acting as the Transferor.
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
        private static int SIP_LISTEN_PORT = 6070;
        private static string TRANSFEREE_DST = "sip:transferee@127.0.0.1:6071";
        private static string TARGET_DST = "sip:target@127.0.0.1:6072";

        private static SIPTransport _sipTransport;

        private static Microsoft.Extensions.Logging.ILogger Log = SIPSorcery.Sys.Log.Logger;

        private static SIPUserAgent _transfereeCall;

        static void Main()
        {
            Console.WriteLine("SIPSorcery Blind Transfer Demo: Transferor");
            Console.WriteLine("Start the Transferee and Target programs.");
            Console.WriteLine("Press c to place call to Transferee.");
            Console.WriteLine("Press t to initiate the transfer.");
            Console.WriteLine("Press 'q' or ctrl-c to exit.");

            AddConsoleLogger();

            // Set up a default SIP transport.
            _sipTransport = new SIPTransport();
            _sipTransport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(IPAddress.Any, SIP_LISTEN_PORT)));

            EnableTraceLogs(_sipTransport);

            CancellationTokenSource exitCts = new CancellationTokenSource();
            Task.Run(() => OnKeyPress(exitCts));

            exitCts.Token.WaitHandle.WaitOne();

            #region Cleanup.

            Log.LogInformation("Exiting...");

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
        private static void OnKeyPress(CancellationTokenSource exitCts)
        {
            try
            {
                while (!exitCts.IsCancellationRequested)
                {
                    var keyProps = Console.ReadKey();

                    if (keyProps.KeyChar == 'c')
                    {
                        if (_transfereeCall == null || !_transfereeCall.IsCallActive)
                        {
                            _transfereeCall = new SIPUserAgent(_sipTransport, null);

                            // Place an outgoing call.
                            _transfereeCall.ClientCallTrying += (uac, resp) => Log.LogInformation($"{uac.CallDescriptor.To} Trying: {resp.StatusCode} {resp.ReasonPhrase}.");
                            _transfereeCall.ClientCallRinging += (uac, resp) => Log.LogInformation($"{uac.CallDescriptor.To} Ringing: {resp.StatusCode} {resp.ReasonPhrase}.");
                            _transfereeCall.ClientCallFailed += (uac, err, resp) => Log.LogWarning($"{uac.CallDescriptor.To} Failed: {err}, Status code: {resp?.StatusCode}");
                            _transfereeCall.ClientCallAnswered += (uac, resp) => Log.LogInformation($"{uac.CallDescriptor.To} Answered: {resp.StatusCode} {resp.ReasonPhrase}.");
                            _transfereeCall.OnDtmfTone += (key, duration) => Log.LogInformation($"Received DTMF tone {key}.");
                            //ua.OnRtpEvent += (evt, hdr) => Log.LogDebug($"transferee rtp event {evt.EventID}, ssrc {hdr.SyncSource}, duration {evt.Duration}, end of event {evt.EndOfEvent}, timestamp {hdr.Timestamp}, marker {hdr.MarkerBit}.");
                            _transfereeCall.OnCallHungup += (dialog) => Log.LogDebug("Call hungup by remote party.");

                            Task.Run(async () =>
                            {
                                var rtpSession = CreateRtpSession();
                                var callResult = await _transfereeCall.Call(TRANSFEREE_DST, null, null, rtpSession);

                                if (!callResult)
                                {
                                    Log.LogWarning($"Call to {TRANSFEREE_DST} failed.");
                                }
                                else
                                {
                                    Log.LogInformation($"Call to {TRANSFEREE_DST} was successful.");
                                }
                            });
                        }
                        else
                        {
                            Log.LogWarning("Cannot place a new call, the transferee user agent is busy.");
                        }
                                
                    }
                    else if (keyProps.KeyChar == 'h')
                    {
                        if (_transfereeCall != null && _transfereeCall.IsCallActive)
                        {
                            Log.LogDebug("Hanging up transferee call.");
                            _transfereeCall.Hangup();
                        }
                        else
                        {
                            Log.LogDebug("No active transferee call.");
                        }

                        _transfereeCall = null;
                    }
                    else if (keyProps.KeyChar == 't')
                    {
                        if (_transfereeCall == null)
                        {
                            Log.LogWarning("The call to the transferee is not established.");
                        }
                        else
                        {
                            Task.Run(async () =>
                            {
                                Log.LogInformation("Initiating transfer to the transferee...");
                                bool transferResult = await _transfereeCall.BlindTransfer(SIPURI.ParseSIPURI(TARGET_DST), TimeSpan.FromSeconds(2), exitCts.Token);

                                Log.LogDebug($"Transfer result {transferResult}.");

                                await Task.Delay(2000);

                                Log.LogDebug($"Transferee is call active (should be false if transfer succeeded) {_transfereeCall?.IsCallActive}.");
                            });
                        }
                    }
                    else if (keyProps.KeyChar == 'a')
                    {
                        Log.LogDebug($"Yes I am alive, transferee is call active ? {_transfereeCall?.IsCallActive}!");
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
        /// Example of how to create a basic RTP session object and hook up the event handlers.
        /// </summary>
        /// <param name="ua">The user agent the RTP session is being created for.</param>
        /// <param name="dst">THe destination specified on an incoming call. Can be used to
        /// set the audio source.</param>
        /// <returns>A new RTP session object.</returns>
        private static RtpAudioSession CreateRtpSession()
        {
            List<SDPMediaFormatsEnum> codecs = new List<SDPMediaFormatsEnum> { SDPMediaFormatsEnum.PCMU, SDPMediaFormatsEnum.PCMA, SDPMediaFormatsEnum.G722 };
            var audioOptions = new AudioSourceOptions { AudioSource = AudioSourcesEnum.Silence };
            var rtpAudioSession = new RtpAudioSession(audioOptions, codecs);

            // Wire up the event handler for RTP packets received from the remote party.
            //rtpAudioSession.OnRtpPacketReceived += (type, rtp) => OnRtpPacketReceived(type, rtp);
            rtpAudioSession.OnRtcpBye += (reason) => Log.LogDebug($"RTCP BYE received.");
            rtpAudioSession.OnRtpClosed += (reason) => Log.LogDebug("RTP session closed.");
            rtpAudioSession.OnReceiveReport += RtpSession_OnReceiveReport;
            rtpAudioSession.OnSendReport += RtpSession_OnSendReport;
            //rtpAudioSession.OnTimeout += (mediaType) =>
            //{
            //    if (ua?.Dialogue != null)
            //    {
            //        Log.LogWarning($"RTP timeout on call with {ua.Dialogue.RemoteTarget}, hanging up.");
            //    }
            //    else
            //    {
            //        Log.LogWarning($"RTP timeout on incomplete call, closing RTP session.");
            //    }

            //    ua.Hangup();
            //};

            return rtpAudioSession;
        }

        /// <summary>
        /// Event handler for receiving RTP packets.
        /// </summary>
        /// <param name="type">The media type of the RTP packet (audio or video).</param>
        /// <param name="rtpPacket">The RTP packet received from the remote party.</param>
        private static void OnRtpPacketReceived(SDPMediaTypesEnum type, RTPPacket rtpPacket)
        {
            // The raw audio data is available in rtpPacket.Payload.
            Log.LogDebug($"rtp pkt received ssrc {rtpPacket.Header.SyncSource} seqnum {rtpPacket.Header.SequenceNumber}.");
        }

        /// <summary>
        /// Diagnostic handler to print out our RTCP sender/receiver reports.
        /// </summary>
        private static void RtpSession_OnSendReport(SDPMediaTypesEnum mediaType, RTCPCompoundPacket sentRtcpReport)
        {
            if (sentRtcpReport.SenderReport != null)
            {
                var sr = sentRtcpReport.SenderReport;
                Log.LogDebug($"RTCP sent SR {mediaType}, ssrc {sr.SSRC}, pkts {sr.PacketCount}, bytes {sr.OctetCount}.");
            }
            else
            {
                var rrSample = sentRtcpReport.ReceiverReport.ReceptionReports.First();
                Log.LogDebug($"RTCP sent RR {mediaType}, ssrc {rrSample.SSRC}, seqnum {rrSample.ExtendedHighestSequenceNumber}.");
            }
        }

        /// <summary>
        /// Diagnostic handler to print out our RTCP reports from the remote WebRTC peer.
        /// </summary>
        private static void RtpSession_OnReceiveReport(IPEndPoint endPoint, SDPMediaTypesEnum mediaType, RTCPCompoundPacket recvRtcpReport)
        {
            Log.LogDebug($"RTCP {mediaType} CNAME {recvRtcpReport.SDesReport.CNAME} SSRC {recvRtcpReport.SDesReport.SSRC}.");

            var rr = (recvRtcpReport.SenderReport != null) ? recvRtcpReport.SenderReport.ReceptionReports.FirstOrDefault() : recvRtcpReport.ReceiverReport.ReceptionReports.FirstOrDefault();

            if (rr != null)
            {
                Log.LogDebug($"RTCP {mediaType} Receiver Report: SSRC {rr.SSRC}, pkts lost {rr.PacketsLost}, delay since SR {rr.DelaySinceLastSenderReport}.");
            }
            else
            {
                Log.LogDebug($"RTCP {mediaType} Receiver Report: empty.");
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
