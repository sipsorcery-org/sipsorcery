//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An example showing how to use SIPSorcery with OpenAI's WebRTC endpoint.
// This demo shows the concept of how you could bridge SIP calls to OpenAI, though
// a complete implementation would require additional SIP handling logic.
//
// Usage:
// set OPENAI_API_KEY=your_openai_key
// dotnet run
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 09 Aug 2025	Aaron Clauson	Created, Dublin, Ireland.
// 26 Feb 2026  Aaron Clauson   Moved to SIPSorcery mono repo.
//
// License: 
// BSD 3-Clause "New" or "Revised" License and the additional
// BDS BY-NC-SA restriction, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorcery.OpenAI.Realtime;
using SIPSorcery.OpenAI.Realtime.Models;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions;
using System;
using System.Collections.Concurrent;
using System.Net;
using System.Threading.Tasks;

namespace demo;

record SIPToOpenAiCall(SIPUserAgent ua, RTPSession voip, WebRTCEndPoint? webrtc);

class Program
{
    private static int SIP_LISTEN_PORT = 5060;

    /// <summary>
    /// Keeps track of the current active calls. It includes both received and placed calls.
    /// </summary>
    private static ConcurrentDictionary<string, SIPToOpenAiCall> _calls = new ConcurrentDictionary<string, SIPToOpenAiCall>();

    static async Task Main()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            //.MinimumLevel.Verbose()
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .CreateLogger();

        var loggerFactory = new SerilogLoggerFactory(Log.Logger);
        SIPSorcery.LogFactory.Set(loggerFactory);

        Log.Logger.Information("SIP-to-WebRTC OpenAI Demo Program");

        var openAiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");

        if (string.IsNullOrWhiteSpace(openAiKey))
        {
            Log.Logger.Error("Please provide your OpenAI key as an environment variable. For example: set OPENAI_API_KEY=<your openai api key>");
            return;
        }

        var logger = loggerFactory.CreateLogger<Program>();

        SIPSorcery.LogFactory.Set(loggerFactory);
        var sipTransport = new SIPTransport();
        sipTransport.EnableTraceLogs();
        sipTransport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(IPAddress.Any, SIP_LISTEN_PORT)));
        sipTransport.SIPTransportRequestReceived += (lep, rep, req) => OnRequest(lep, rep, req, sipTransport, openAiKey);

        Console.WriteLine("Wait for ctrl-c to indicate user exit.");

        var exitTcs = new TaskCompletionSource<object?>();
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            exitTcs.TrySetResult(null);
        };

        await exitTcs.Task;
    }

    /// <summary>
    /// Because this is a server user agent the SIP transport must start listening for client user agents.
    /// </summary>
    private static async Task OnRequest(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest, SIPTransport sipTransport, string openAiKey)
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
                Log.Information($"Incoming call request: {localSIPEndPoint}<-{remoteEndPoint} {sipRequest.URI}.");

                SIPUserAgent ua = new SIPUserAgent(sipTransport, null);
                ua.OnCallHungup += OnHangup;
                ua.ServerCallCancelled += (uas, cancelReq) => Log.Debug("Incoming call cancelled by remote party.");
                ua.OnDtmfTone += (key, duration) => OnDtmfTone(ua, key, duration);
                ua.OnRtpEvent += (evt, hdr) => Log.Debug($"rtp event {evt.EventID}, duration {evt.Duration}, end of event {evt.EndOfEvent}, timestamp {hdr.Timestamp}, marker {hdr.MarkerBit}.");
                //ua.OnTransactionTraceMessage += (tx, msg) => Log.LogDebug($"uas tx {tx.TransactionId}: {msg}");
                ua.ServerCallRingTimeout += (uas) =>
                {
                    Log.Warning($"Incoming call timed out in {uas.ClientTransaction.TransactionState} state waiting for client ACK, terminating.");
                    ua.Hangup();
                };

                //bool wasMangled = false;
                //sipRequest.Body = SIPPacketMangler.MangleSDP(sipRequest.Body, remoteEndPoint.Address.ToString(), out wasMangled);
                //Log.LogDebug("INVITE was mangled=" + wasMangled + " remote=" + remoteEndPoint.Address.ToString() + ".");
                //sipRequest.Header.ContentLength = sipRequest.Body.Length;

                var uas = ua.AcceptCall(sipRequest);
                var rtpSession = CreateRtpSession(ua);

                // Insert a brief delay to allow testing of the "Ringing" progress response.
                // Without the delay the call gets answered before it can be sent.
                //await Task.Delay(500);

                //if (!string.IsNullOrWhiteSpace(_publicIPAddress))
                //{
                //    await ua.Answer(uas, rtpSession, IPAddress.Parse(_publicIPAddress));
                //}
                //else
                //{
                await ua.Answer(uas, rtpSession);
                //}

                if (ua.IsCallActive)
                {
                    await rtpSession.Start();
                    _calls.TryAdd(ua.Dialogue.CallId, new SIPToOpenAiCall(ua, rtpSession, null));

                    Log.Information($"Call answered, call ID {ua.Dialogue.CallId}.");

                    // Create a WebRTC session to OpenAI.
                    await CreateOpenAIWebRTCSession(new SerilogLoggerFactory(Log.Logger), openAiKey, ua.Dialogue.CallId, rtpSession);
                }
            }
            else if (sipRequest.Method == SIPMethodsEnum.BYE)
            {
                SIPResponse byeResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.CallLegTransactionDoesNotExist, null);
                await sipTransport.SendResponseAsync(byeResponse);
            }
            else if (sipRequest.Method == SIPMethodsEnum.SUBSCRIBE)
            {
                SIPResponse notAllowededResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.MethodNotAllowed, null);
                await sipTransport.SendResponseAsync(notAllowededResponse);
            }
            else if (sipRequest.Method == SIPMethodsEnum.OPTIONS || sipRequest.Method == SIPMethodsEnum.REGISTER)
            {
                SIPResponse optionsResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                await sipTransport.SendResponseAsync(optionsResponse);
            }
        }
        catch (Exception reqExcp)
        {
            Log.Warning($"Exception handling {sipRequest.Method}. {reqExcp.Message}");
        }
    }

    /// <summary>
    /// Example of how to create a basic RTP session object and hook up the event handlers.
    /// </summary>
    /// <param name="ua">The user agent the RTP session is being created for.</param>
    /// <returns>A new RTP session object.</returns>
    private static RTPSession CreateRtpSession(SIPUserAgent ua)
    {
        var rtpSession = new RTPSession(false, false, false);
        rtpSession.addTrack(new MediaStreamTrack(AudioCommonlyUsedFormats.OpusWebRTC));
        rtpSession.AcceptRtpFromAny = true;

        // Wire up the event handler for RTP packets received from the remote party.
        //rtpSession.OnRtpPacketReceived += (ep, type, rtp) => OnRtpPacketReceived(ua, ep, type, rtp);
        rtpSession.OnTimeout += (mediaType) =>
        {
            if (ua?.Dialogue != null)
            {
                Log.Warning($"RTP timeout on call with {ua.Dialogue.RemoteTarget}, hanging up.");
            }
            else
            {
                Log.Warning($"RTP timeout on incomplete call, closing RTP session.");
            }

            ua?.Hangup();
        };

        return rtpSession;
    }

    private static async Task CreateOpenAIWebRTCSession(ILoggerFactory loggerFactory, string openAiKey, string sipCallID, RTPSession rtpSession)
    {
        var logger = loggerFactory.CreateLogger<WebRTCEndPoint>();
        var webrtcEndPoint = new WebRTCEndPoint(openAiKey, loggerFactory);

        if (_calls.TryGetValue(sipCallID, out var existing))
        {
            var updated = existing with { webrtc = webrtcEndPoint };
            _calls.TryUpdate(sipCallID, updated, existing);
        }

        var negotiateConnectResult = await webrtcEndPoint.StartConnect();

        if (negotiateConnectResult.IsLeft)
        {
            Log.Logger.Error($"Failed to negotiation connection to OpenAI Realtime WebRTC endpoint: {negotiateConnectResult.LeftAsEnumerable().First()}");
            return;
        }

        webrtcEndPoint.OnPeerConnectionConnected += () =>
        {
            Log.Logger.Information("WebRTC peer connection established.");

            webrtcEndPoint.ConnectRTPSession(rtpSession, AudioCommonlyUsedFormats.OpusWebRTC);

            var voice = RealtimeVoicesEnum.shimmer;

            // Optionally send a session update message to adjust the session parameters.
            var sessionUpdateResult = webrtcEndPoint.DataChannelMessenger.SendSessionUpdate(
                voice,
                "Keep it short.",
                transcriptionModel: TranscriptionModelEnum.Whisper1);

            if (sessionUpdateResult.IsLeft)
            {
                Log.Logger.Error($"Failed to send session update message: {sessionUpdateResult.LeftAsEnumerable().First()}");
            }

            // Trigger the conversation by sending a response create message.
            var result = webrtcEndPoint.DataChannelMessenger.SendResponseCreate(voice, "Say Hi!");
            if (result.IsLeft)
            {
                Log.Logger.Error($"Failed to send response create message: {result.LeftAsEnumerable().First()}");
            }
        };

        webrtcEndPoint.OnDataChannelMessage += (dc, message) =>
        {
            var log = message switch
            {
                RealtimeServerEventSessionUpdated sessionUpdated => $"Session updated: {sessionUpdated.ToJson()}",
                //RealtimeServerEventConversationItemInputAudioTranscriptionDelta inputDelta => $"ME ⌛: {inputDelta.Delta?.Trim()}",
                RealtimeServerEventConversationItemInputAudioTranscriptionCompleted inputTranscript => $"ME ✅: {inputTranscript.Transcript?.Trim()}",
                //RealtimeServerEventResponseAudioTranscriptDelta responseDelta => $"AI ⌛: {responseDelta.Delta?.Trim()}",
                RealtimeServerEventResponseAudioTranscriptDone responseTranscript => $"AI ✅: {responseTranscript.Transcript?.Trim()}",
                //_ => $"Received {message.Type} -> {message.GetType().Name}"
                _ => string.Empty
            };

            if (log != string.Empty)
            {
                Log.Information(log);
            }
        };
    }

    /// <summary>
    /// Event handler for receiving RTP packets.
    /// </summary>
    /// <param name="ua">The SIP user agent associated with the RTP session.</param>
    /// <param name="type">The media type of the RTP packet (audio or video).</param>
    /// <param name="rtpPacket">The RTP packet received from the remote party.</param>
    private static void OnRtpPacketReceived(SIPUserAgent ua, IPEndPoint remoteEp, SDPMediaTypesEnum type, RTPPacket rtpPacket)
    {
        // The raw audio data is available in rtpPacket.Payload.
        Log.Verbose($"OnRtpPacketReceived from {remoteEp}.");
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
        Log.Information($"Call {callID} received DTMF tone {key}, duration {duration}ms.");
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
                if (_calls.TryRemove(callID, out var call))
                {
                    Log.Information($"Call {callID} removed.");

                    // This app only uses each SIP user agent once so here the agent is 
                    // explicitly closed to prevent is responding to any new SIP requests.
                    call.ua.Close();
                    call.webrtc?.Close();
                }
            }
        }
    }
}
