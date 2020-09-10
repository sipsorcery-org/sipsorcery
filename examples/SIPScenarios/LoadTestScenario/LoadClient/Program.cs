//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: Acts as the client in a SIP call load test.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 17 May 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Linq;
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
using SIPSorcery.Sys;

namespace SIPSorcery
{
    class CallRecord
    {
        public SIPUserAgent UA;
        public int RequestedDtmfCode;
        public string ReceivedDtmfCode;
    }

    class Program
    {
        private static string TARGET_DST = "sip:127.0.0.1";
        private static int TIMEOUT_MILLISECONDS = 5000;

        private static Microsoft.Extensions.Logging.ILogger Log = NullLogger.Instance;

        /// <summary>
        /// Keeps track of the current active calls. It includes both received and placed calls.
        /// </summary>
        private static ConcurrentDictionary<string, CallRecord> _calls = new ConcurrentDictionary<string, CallRecord>();

        static async Task Main()
        {
            Console.WriteLine("SIPSorcery Load Test Client");
            Console.WriteLine("Start the Load Test Server programs.");
            Console.WriteLine("Press c to place call to server.");
            Console.WriteLine("Press 'q' or ctrl-c to exit.");

            AddConsoleLogger();

            CancellationTokenSource exitCts = new CancellationTokenSource();

            int successCount = 0;

            for (int i = 0; i < 100; i++)
            {
                bool success = await PlaceCall(exitCts.Token);

                if (!success)
                {
                    Log.LogWarning("Place call failed.");
                    break;
                }
                else
                {
                    successCount++;
                }
            }

            Console.WriteLine($"Success count {successCount}.");

            //Task.Run(() => OnKeyPress(exitCts));
            //exitCts.Token.WaitHandle.WaitOne();

            Log.LogInformation("Exiting...");
        }

        private static Task<bool> PlaceCall(CancellationToken cancel)
        {
            // Set up a default SIP transport.
            var sipTransport = new SIPTransport();
            //EnableTraceLogs(sipTransport);

            var targetUri = SIPURI.ParseSIPURI(TARGET_DST);
            int requestedDtmfCode = Crypto.GetRandomInt(4);
            targetUri.User = requestedDtmfCode.ToString();

            SIPUserAgent ua = new SIPUserAgent(sipTransport, null, true);
            CallRecord cr = new CallRecord { UA = ua, RequestedDtmfCode = requestedDtmfCode, ReceivedDtmfCode = "" };

            TaskCompletionSource<bool> dtmfComplete = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            // Place an outgoing call.
            ua.ClientCallTrying += (uac, resp) => Log.LogInformation($"{uac.CallDescriptor.To} Trying: {resp.StatusCode} {resp.ReasonPhrase}.");
            ua.ClientCallRinging += (uac, resp) => Log.LogInformation($"{uac.CallDescriptor.To} Ringing: {resp.StatusCode} {resp.ReasonPhrase}.");
            ua.ClientCallFailed += (uac, err, resp) => Log.LogWarning($"{uac.CallDescriptor.To} Failed: {err}, Status code: {resp?.StatusCode}");
            ua.ClientCallAnswered += (uac, resp) => Log.LogInformation($"{uac.CallDescriptor.To} Answered: {resp.StatusCode} {resp.ReasonPhrase}.");
            ua.OnDtmfTone += (key, duration) =>
            {
                cr.ReceivedDtmfCode = cr.ReceivedDtmfCode.Insert(0, key.ToString());
                Log.LogInformation($"Received DTMF tone {key} {cr.ReceivedDtmfCode}.");

                if (cr.ReceivedDtmfCode == cr.RequestedDtmfCode.ToString())
                {
                    dtmfComplete.SetResult(true);
                }
            };
            //ua.OnRtpEvent += (evt, hdr) => Log.LogDebug($"transferee rtp event {evt.EventID}, ssrc {hdr.SyncSource}, duration {evt.Duration}, end of event {evt.EndOfEvent}, timestamp {hdr.Timestamp}, marker {hdr.MarkerBit}.");
            ua.OnCallHungup += (dialog) => Log.LogDebug("Call hungup by remote party.");

            var rtpSession = CreateRtpSession();
            var callTask = ua.Call(targetUri.ToString(), null, null, rtpSession);
            bool dtmfResult = false;

            bool didComplete = Task.WaitAll(new Task[] { callTask }, TIMEOUT_MILLISECONDS, cancel);

            if (!didComplete || !callTask.Result)
            {
                Log.LogWarning($"Call to {targetUri} failed.");
            }
            else
            {
                Log.LogInformation($"Call to {targetUri} was successful.");
                _calls.TryAdd(ua.Dialogue.CallId, cr);

                if (Task.WaitAll(new Task[] { dtmfComplete.Task }, TIMEOUT_MILLISECONDS, cancel))
                {
                    dtmfResult = dtmfComplete.Task.Result;
                }
                else
                {
                    Log.LogWarning("Timeout waiting for DTMF task to complete.");
                }

                ua.Hangup();
            }

            return Task.FromResult(dtmfResult);
            //return Task.FromResult(true);
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
                        // Set up a default SIP transport.
                        var sipTransport = new SIPTransport();
                        //EnableTraceLogs(sipTransport);

                        var targetUri = SIPURI.ParseSIPURI(TARGET_DST);
                        int requestedDtmfCode = Crypto.GetRandomInt(4);
                        targetUri.User = requestedDtmfCode.ToString();

                        SIPUserAgent ua = new SIPUserAgent(sipTransport, null, true);
                        CallRecord cr = new CallRecord { UA = ua, RequestedDtmfCode = requestedDtmfCode, ReceivedDtmfCode = "" };

                        // Place an outgoing call.
                        ua.ClientCallTrying += (uac, resp) => Log.LogInformation($"{uac.CallDescriptor.To} Trying: {resp.StatusCode} {resp.ReasonPhrase}.");
                        ua.ClientCallRinging += (uac, resp) => Log.LogInformation($"{uac.CallDescriptor.To} Ringing: {resp.StatusCode} {resp.ReasonPhrase}.");
                        ua.ClientCallFailed += (uac, err, resp) => Log.LogWarning($"{uac.CallDescriptor.To} Failed: {err}, Status code: {resp?.StatusCode}");
                        ua.ClientCallAnswered += (uac, resp) => Log.LogInformation($"{uac.CallDescriptor.To} Answered: {resp.StatusCode} {resp.ReasonPhrase}.");
                        ua.OnDtmfTone += (key, duration) =>
                        {
                            cr.ReceivedDtmfCode = cr.ReceivedDtmfCode.Insert(0, key.ToString());
                            Log.LogInformation($"Received DTMF tone {key} {cr.ReceivedDtmfCode}.");
                        };
                        //ua.OnRtpEvent += (evt, hdr) => Log.LogDebug($"transferee rtp event {evt.EventID}, ssrc {hdr.SyncSource}, duration {evt.Duration}, end of event {evt.EndOfEvent}, timestamp {hdr.Timestamp}, marker {hdr.MarkerBit}.");
                        ua.OnCallHungup += (dialog) => Log.LogDebug("Call hungup by remote party.");

                        Task.Run(async () =>
                        {
                            var rtpSession = CreateRtpSession();
                            var callResult = await ua.Call(targetUri.ToString(), null, null, rtpSession);

                            if (!callResult)
                            {
                                Log.LogWarning($"Call to {targetUri} failed.");
                            }
                            else
                            {
                                Log.LogInformation($"Call to {targetUri} was successful.");
                                _calls.TryAdd(ua.Dialogue.CallId, cr);
                            }
                        });
                    }
                    else if (keyProps.KeyChar == 'h')
                    {
                        if (_calls.Count == 0)
                        {
                            Log.LogWarning("There are no active calls.");
                        }
                        else
                        {
                            var oldestCall = _calls.OrderBy(x => x.Value.UA.Dialogue.Inserted).First();
                            Log.LogInformation($"Hanging up call {oldestCall.Key}.");
                            oldestCall.Value.UA.OnCallHungup -= OnHangup;
                            oldestCall.Value.UA.Hangup();
                            _calls.TryRemove(oldestCall.Key, out _);
                        }
                    }
                    else if (keyProps.KeyChar == 'H')
                    {
                        if (_calls.Count == 0)
                        {
                            Log.LogWarning("There are no active calls.");
                        }
                        else
                        {
                            foreach (var call in _calls)
                            {
                                Log.LogInformation($"Hanging up call {call.Key}.");
                                call.Value.UA.OnCallHungup -= OnHangup;
                                call.Value.UA.Hangup();
                            }
                            _calls.Clear();
                        }
                    }
                    else if (keyProps.KeyChar == 'l')
                    {
                        if (_calls.Count == 0)
                        {
                            Log.LogInformation("There are no active calls.");
                        }
                        else
                        {
                            Log.LogInformation("Current call list:");
                            foreach (var call in _calls)
                            {
                                int duration = Convert.ToInt32(DateTimeOffset.Now.Subtract(call.Value.UA.Dialogue.Inserted).TotalSeconds);
                                uint rtpSent = (call.Value.UA.MediaSession as AudioSendOnlyMediaSession).AudioRtcpSession.PacketsSentCount;
                                uint rtpRecv = (call.Value.UA.MediaSession as AudioSendOnlyMediaSession).AudioRtcpSession.PacketsReceivedCount;
                                Log.LogInformation($"{call.Key}: {call.Value.UA.Dialogue.RemoteTarget}, req code {call.Value.RequestedDtmfCode}, recvd code {call.Value.ReceivedDtmfCode}, dur {duration}s, rtp sent/recvd {rtpSent}/{rtpRecv}");
                            }
                        }
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
        /// Remove call from the active calls list.
        /// </summary>
        /// <param name="dialogue">The dialogue that was hungup.</param>
        private static void OnHangup(SIPDialogue dialogue)
        {
            if (dialogue != null)
            {
                string callID = dialogue.CallId;
                if (_calls.ContainsKey(callID))
                {
                    _calls.TryRemove(callID, out _);
                }
            }
        }

        /// <summary>
        /// Example of how to create a basic RTP session object and hook up the event handlers.
        /// </summary>
        /// <param name="ua">The user agent the RTP session is being created for.</param>
        /// <param name="dst">THe destination specified on an incoming call. Can be used to
        /// set the audio source.</param>
        /// <returns>A new RTP session object.</returns>
        private static AudioSendOnlyMediaSession CreateRtpSession()
        {
            var rtpAudioSession = new AudioSendOnlyMediaSession();

            // Wire up the event handler for RTP packets received from the remote party.
            //rtpAudioSession.OnRtpPacketReceived += OnRtpPacketReceived;
            rtpAudioSession.OnRtcpBye += (reason) => Log.LogDebug($"RTCP BYE received.");
            rtpAudioSession.OnRtpClosed += (reason) => Log.LogDebug("RTP session closed.");
            //rtpAudioSession.OnReceiveReport += RtpSession_OnReceiveReport;
            //rtpAudioSession.OnSendReport += RtpSession_OnSendReport;
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
        private static void RtpSession_OnReceiveReport(SDPMediaTypesEnum mediaType, RTCPCompoundPacket recvRtcpReport)
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
        /// Adds a console logger. Can be omitted if internal SIPSorcery debug and warning messages are not required.
        /// </summary>
        private static void AddConsoleLogger()
        {
            var logger = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .MinimumLevel.Is(Serilog.Events.LogEventLevel.Debug)
                .WriteTo.Console()
                .CreateLogger();
            var factory = new SerilogLoggerFactory(logger);
            SIPSorcery.LogFactory.Set(factory);
            Log = factory.CreateLogger<Program>();
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
