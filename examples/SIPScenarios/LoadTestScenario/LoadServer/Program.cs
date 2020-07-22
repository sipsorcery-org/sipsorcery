//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: Acts as the server in a SIP call load test.
//
// Author(s):
// Aaron Clauson  (aaron@sipsorcery.com)
// 
// History:
// 17 May 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;

namespace SIPSorcery
{
    class Program
    {
        private static int SIP_LISTEN_PORT = 5060;

        private static Microsoft.Extensions.Logging.ILogger Log = SIPSorcery.Sys.Log.Logger;

        private static SIPTransport _sipTransport;

        /// <summary>
        /// Keeps track of the current active calls. It includes both received and placed calls.
        /// </summary>
        private static ConcurrentDictionary<string, SIPUserAgent> _calls = new ConcurrentDictionary<string, SIPUserAgent>();

        static async Task Main()
        {
            Console.WriteLine("SIPSorcery Load Test Server:");
            Console.WriteLine("Press 'd' to send a random DTMF tone to the newest call.");
            Console.WriteLine("Press 'h' to hangup the oldest call.");
            Console.WriteLine("Press 'H' to hangup all calls.");
            Console.WriteLine("Press 'l' to list current calls.");
            Console.WriteLine("Press 'q' to quit.");

            AddConsoleLogger();

            // Set up a default SIP transport.
            _sipTransport = new SIPTransport();
            _sipTransport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(IPAddress.Any, SIP_LISTEN_PORT)));
            EnableTraceLogs(_sipTransport, false);

            _sipTransport.SIPTransportRequestReceived += OnRequest;

            CancellationTokenSource exitCts = new CancellationTokenSource();
            await Task.Run(() => OnKeyPress(exitCts.Token));

            Log.LogInformation("Exiting...");

            if (_sipTransport != null)
            {
                Log.LogInformation("Shutting down SIP transport...");
                _sipTransport.Shutdown();
            }
        }

        /// <summary>
        /// Process user key presses.
        /// </summary>
        /// <param name="exit">The cancellation token to set if the user requests to quit the application.</param>
        private static async Task OnKeyPress(CancellationToken exit)
        {
            try
            {
                while (!exit.WaitHandle.WaitOne(0))
                {
                    var keyProps = Console.ReadKey();

                    if (keyProps.KeyChar == 'd')
                    {
                        if (_calls.Count == 0)
                        {
                            Log.LogWarning("There are no active calls.");
                        }
                        else
                        {
                            var newestCall = _calls.OrderByDescending(x => x.Value.Dialogue.Inserted).First();
                            byte randomDtmf = (byte)Crypto.GetRandomInt(0, 15);
                            Log.LogInformation($"Sending DTMF {randomDtmf} to {newestCall.Key}.");
                            await newestCall.Value.SendDtmf(randomDtmf);
                        }
                    }
                    else if (keyProps.KeyChar == 'h')
                    {
                        if (_calls.Count == 0)
                        {
                            Log.LogWarning("There are no active calls.");
                        }
                        else
                        {
                            var oldestCall = _calls.OrderBy(x => x.Value.Dialogue.Inserted).First();
                            Log.LogInformation($"Hanging up call {oldestCall.Key}.");
                            oldestCall.Value.OnCallHungup -= OnHangup;
                            oldestCall.Value.Hangup();
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
                                call.Value.OnCallHungup -= OnHangup;
                                call.Value.Hangup();
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
                                int duration = Convert.ToInt32(DateTimeOffset.Now.Subtract(call.Value.Dialogue.Inserted).TotalSeconds);
                                uint rtpSent = (call.Value.MediaSession as RtpAudioSession).RtpPacketsSent;
                                uint rtpRecv = (call.Value.MediaSession as RtpAudioSession).RtpPacketsReceived;
                                Log.LogInformation($"{call.Key}: {call.Value.Dialogue.RemoteTarget} {duration}s {rtpSent}/{rtpRecv}");
                            }
                        }
                    }
                    else if (keyProps.KeyChar == 'q')
                    {
                        // Quit application.
                        Log.LogInformation("Quitting");
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
        private static RtpAudioSession CreateRtpSession(SIPUserAgent ua)
        {
            List<SDPMediaFormatsEnum> codecs = new List<SDPMediaFormatsEnum> { SDPMediaFormatsEnum.PCMU, SDPMediaFormatsEnum.PCMA, SDPMediaFormatsEnum.G722 };
            var audioOptions = new AudioSourceOptions { AudioSource = AudioSourcesEnum.Silence };
            var rtpAudioSession = new RtpAudioSession(audioOptions, codecs);

            // Wire up the event handler for RTP packets received from the remote party.
            rtpAudioSession.OnRtpPacketReceived += (endPoint, type, rtp) => OnRtpPacketReceived(ua, type, rtp);
            rtpAudioSession.OnTimeout += (mediaType) =>
            {
                if (ua?.Dialogue != null)
                {
                    Log.LogWarning($"RTP timeout on call with {ua.Dialogue.RemoteTarget}, hanging up.");
                }
                else
                {
                    Log.LogWarning($"RTP timeout on incomplete call, closing RTP session.");
                }

                ua.Hangup();
            };

            return rtpAudioSession;
        }

        /// <summary>
        /// Event handler for receiving RTP packets.
        /// </summary>
        /// <param name="ua">The SIP user agent associated with the RTP session.</param>
        /// <param name="type">The media type of the RTP packet (audio or video).</param>
        /// <param name="rtpPacket">The RTP packet received from the remote party.</param>
        private static void OnRtpPacketReceived(SIPUserAgent ua, SDPMediaTypesEnum type, RTPPacket rtpPacket)
        {
            // The raw audio data is available in rtpPacket.Payload.
        }

        /// <summary>
        /// Event handler for receiving a DTMF tone.
        /// </summary>
        /// <param name="ua">The user agent that received the DTMF tone.</param>
        /// <param name="key">The DTMF tone.</param>
        /// <param name="duration">The duration in milliseconds of the tone.</param>
        private static void OnDtmfTone(SIPUserAgent ua, byte key, int duration)
        {
            string callID = ua.Dialogue.CallId;
            Log.LogInformation($"Call {callID} received DTMF tone {key}, duration {duration}ms.");
        }

        /// <summary>
        /// Because this is a server user agent the SIP transport must start listening for client user agents.
        /// </summary>
        private static async Task OnRequest(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest)
        {
            try
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
                    Log.LogInformation($"Incoming call request: {localSIPEndPoint}<-{remoteEndPoint} {sipRequest.URI}.");

                    SIPUserAgent ua = new SIPUserAgent(_sipTransport, null);
                    ua.OnCallHungup += OnHangup;
                    ua.ServerCallCancelled += (uas) => Log.LogDebug("Incoming call cancelled by remote party.");
                    ua.OnDtmfTone += (key, duration) => OnDtmfTone(ua, key, duration);
                    ua.OnRtpEvent += (evt, hdr) => Log.LogDebug($"rtp event {evt.EventID}, duration {evt.Duration}, end of event {evt.EndOfEvent}, timestamp {hdr.Timestamp}, marker {hdr.MarkerBit}.");
                    ua.OnTransactionTraceMessage += (tx, msg) => Log.LogDebug($"uas tx {tx.TransactionId}: {msg}");
                    ua.ServerCallRingTimeout += (uas) =>
                    {
                        Log.LogWarning($"Incoming call timed out in {uas.ClientTransaction.TransactionState} state waiting for client ACK, terminating.");
                        ua.Hangup();
                    };

                    var uas = ua.AcceptCall(sipRequest);
                    var rtpSession = CreateRtpSession(ua);

                    // Insert a brief delay to allow testing of the "Ringing" progress response.
                    // Without the delay the call gets answered before it can be sent.
                    await Task.Delay(500);

                    await ua.Answer(uas, rtpSession);

                    if (ua.IsCallActive)
                    {
                        _calls.TryAdd(ua.Dialogue.CallId, ua);

                        if (sipRequest.URI.User != null)
                        {
                            if (Int32.TryParse(sipRequest.URI.User, out int dtmfCode))
                            {
                                Log.LogDebug($"URI dtmf code {dtmfCode}.");

                                while (dtmfCode > 0)
                                {
                                    byte dtmfByte = (byte)(dtmfCode % 10);

                                    Log.LogDebug($"Sending DTMF {dtmfByte} to caller.");

                                    if (!ua.IsCallActive)
                                    {
                                        Log.LogWarning($"Client call no longer active.");
                                        break;
                                    }
                                    else
                                    {
                                        await ua.SendDtmf(dtmfByte);
                                    }

                                    dtmfCode /= 10;
                                }
                            }
                        }
                    }
                }
                else if (sipRequest.Method == SIPMethodsEnum.BYE)
                {
                    SIPResponse byeResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.CallLegTransactionDoesNotExist, null);
                    await _sipTransport.SendResponseAsync(byeResponse);
                }
                else if (sipRequest.Method == SIPMethodsEnum.SUBSCRIBE)
                {
                    SIPResponse notAllowededResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.MethodNotAllowed, null);
                    await _sipTransport.SendResponseAsync(notAllowededResponse);
                }
                else if (sipRequest.Method == SIPMethodsEnum.OPTIONS || sipRequest.Method == SIPMethodsEnum.REGISTER)
                {
                    SIPResponse optionsResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                    await _sipTransport.SendResponseAsync(optionsResponse);
                }
            }
            catch (Exception reqExcp)
            {
                Log.LogWarning($"Exception handling {sipRequest.Method}. {reqExcp.Message}");
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

                Log.LogDebug($"OnHangup for call id {callID}.");

                if (_calls.ContainsKey(callID))
                {
                    _calls.TryRemove(callID, out var ua);

                    if (ua != null)
                    {
                        ua.Dispose();
                    }
                }
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
                .WriteTo.Console(theme: AnsiConsoleTheme.Code)
                .CreateLogger();
            loggerFactory.AddSerilog(loggerConfig);
            SIPSorcery.Sys.Log.LoggerFactory = loggerFactory;
        }

        /// <summary>
        /// Enable detailed SIP log messages.
        /// </summary>
        private static void EnableTraceLogs(SIPTransport sipTransport, bool fullSIP)
        {
            sipTransport.SIPRequestInTraceEvent += (localEP, remoteEP, req) =>
            {
                Log.LogDebug($"Request received: {localEP}<-{remoteEP}");

                if (!fullSIP)
                {
                    Log.LogDebug(req.StatusLine);
                }
                else
                {
                    Log.LogDebug(req.ToString());
                }
            };

            sipTransport.SIPRequestOutTraceEvent += (localEP, remoteEP, req) =>
            {
                Log.LogDebug($"Request sent: {localEP}->{remoteEP}");

                if (!fullSIP)
                {
                    Log.LogDebug(req.StatusLine);
                }
                else
                {
                    Log.LogDebug(req.ToString());
                }
            };

            sipTransport.SIPResponseInTraceEvent += (localEP, remoteEP, resp) =>
            {
                Log.LogDebug($"Response received: {localEP}<-{remoteEP}");

                if (!fullSIP)
                {
                    Log.LogDebug(resp.ShortDescription);
                }
                else
                {
                    Log.LogDebug(resp.ToString());
                }
            };

            sipTransport.SIPResponseOutTraceEvent += (localEP, remoteEP, resp) =>
            {
                Log.LogDebug($"Response sent: {localEP}->{remoteEP}");

                if (!fullSIP)
                {
                    Log.LogDebug(resp.ShortDescription);
                }
                else
                {
                    Log.LogDebug(resp.ToString());
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
    }
}