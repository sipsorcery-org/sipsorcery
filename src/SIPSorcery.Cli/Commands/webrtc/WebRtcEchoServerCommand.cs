//-----------------------------------------------------------------------------
// Filename: WebRtcEchoServerCommand.cs
//
// Description: The "sipsorcery webrtc echo-server" verb. Acts as a WebRTC echo
// test server per the webrtc-echoes interoperability specification: it accepts
// SDP offers as JSON on an HTTP /offer endpoint, answers them, echoes any
// received RTP back to the sender and echoes data channel messages. The
// equivalent of the webrtccmdline "--echoserver" option, and the peer for the
// "webrtc echo" client verb.
//
// See: https://github.com/sipsorcery/webrtc-echoes
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 13 Jun 2026	Aaron Clauson	Created, Wexford, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.CommandLine;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.Cli.Commands;

public sealed class WebRtcEchoServerCommand : CommandBase
{
    private const int DEFAULT_TIMEOUT_SECONDS = 0;            // The server runs until cancelled by default.
    private const int VP8_PAYLOAD_ID = 96;
    private const int H264_PAYLOAD_ID = 100;
    private const string DEFAULT_LISTEN_URL = "http://localhost:8080/";

    /// <summary>
    /// The result shape written to stdout with --json. Stable field names; additive changes only.
    /// </summary>
    private sealed record EchoServerResult(
        bool Success,
        string ListenUrl,
        int SessionsServed,
        long DurationMs,
        string? Error);

    public WebRtcEchoServerCommand() : base(DEFAULT_TIMEOUT_SECONDS)
    { }

    public override Command Build()
    {
        var listenOption = new Option<string>("--listen")
        {
            Description = $"The HTTP URL to accept echo offers on (the /offer endpoint). Defaults to {DEFAULT_LISTEN_URL}.",
            DefaultValueFactory = _ => DEFAULT_LISTEN_URL
        };

        var durationOption = new Option<int>("--duration", "-d")
        {
            Description = "Seconds to run the server before exiting. 0 runs until cancelled with ctrl-c.",
            DefaultValueFactory = _ => 0
        };

        var command = new Command("echo-server", "Act as a WebRTC echo test server: answer offers, echo RTP and data channel messages (webrtccmdline --echoserver).");
        command.Options.Add(listenOption);
        command.Options.Add(durationOption);
        AddCommonOptions(command);

        command.SetAction((parseResult, cancellationToken) => RunAsync(
            parseResult.GetValue(listenOption)!,
            parseResult.GetValue(durationOption),
            parseResult.GetValue(JsonOption),
            parseResult.GetValue(VerboseOption),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(string listenUrl, int durationSeconds, bool asJson, bool verbose, CancellationToken ct)
    {
        using var loggerFactory = InitLogging(verbose);
        var logger = loggerFactory.CreateLogger(nameof(WebRtcEchoServerCommand));

        if (!Uri.TryCreate(listenUrl, UriKind.Absolute, out var listenUri) || listenUri.Scheme != Uri.UriSchemeHttp)
        {
            return WriteResult(asJson,
                new EchoServerResult(false, listenUrl, 0, 0, $"Could not parse \"{listenUrl}\" as an HTTP URL (HTTPS is not supported for the local listener)."),
                ExitCodes.InvalidArgument);
        }

        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://{listenUri.Authority}/");

        // Keep peer connections referenced so they are not garbage collected mid-session.
        var sessions = new ConcurrentDictionary<string, RTCPeerConnection>();
        int sessionsServed = 0;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            listener.Start();
        }
        catch (HttpListenerException excp)
        {
            return WriteResult(asJson,
                new EchoServerResult(false, listenUrl, 0, 0, $"Could not listen on {listenUrl}: {excp.Message}"),
                ExitCodes.TransportError);
        }

        Console.Error.WriteLine($"WebRTC echo server listening on {listenUri.Authority} (offer endpoint {listenUri.GetLeftPart(UriPartial.Authority)}/offer).");
        Console.Error.WriteLine(durationSeconds > 0 ? $"Running for {durationSeconds}s." : "Running until cancelled (ctrl-c).");

        using var overallCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (durationSeconds > 0)
        {
            overallCts.CancelAfter(TimeSpan.FromSeconds(durationSeconds));
        }

        try
        {
            while (!overallCts.IsCancellationRequested)
            {
                var getContext = listener.GetContextAsync();
                var completed = await Task.WhenAny(getContext, Task.Delay(Timeout.Infinite, overallCts.Token)).ConfigureAwait(false);

                if (completed != getContext)
                {
                    break;
                }

                var context = await getContext.ConfigureAwait(false);
                _ = HandleRequestAsync(context, sessions, logger, () => Interlocked.Increment(ref sessionsServed));
            }
        }
        catch (OperationCanceledException)
        {
            // Duration elapsed or cancelled; fall through to report.
        }
        catch (Exception excp)
        {
            return WriteResult(asJson,
                new EchoServerResult(false, listenUrl, sessionsServed, stopwatch.ElapsedMilliseconds, excp.Message),
                ExitCodes.TransportError);
        }
        finally
        {
            foreach (var pc in sessions.Values)
            {
                pc.Close("echo server shutting down");
            }
            if (listener.IsListening)
            {
                listener.Stop();
            }
        }

        stopwatch.Stop();
        return WriteResult(asJson,
            new EchoServerResult(sessionsServed > 0, listenUrl, sessionsServed, stopwatch.ElapsedMilliseconds,
                sessionsServed > 0 ? null : "The server ran but served no echo sessions."),
            sessionsServed > 0 ? ExitCodes.Ok : ExitCodes.Failed);
    }

    private static async Task HandleRequestAsync(HttpListenerContext context, ConcurrentDictionary<string, RTCPeerConnection> sessions,
        ILogger logger, Action onSessionServed)
    {
        try
        {
            var request = context.Request;

            // The echo spec posts the offer to /offer; some clients also POST trickle candidates
            // to /icecandidate, which this all-candidates-in-offer server simply acknowledges.
            bool isOffer = request.HttpMethod == "POST" && (request.Url?.AbsolutePath?.TrimEnd('/').EndsWith("/offer", StringComparison.OrdinalIgnoreCase) ?? false);

            if (!isOffer)
            {
                if (request.HttpMethod == "POST")
                {
                    Respond(context, HttpStatusCode.OK);
                }
                else
                {
                    Respond(context, HttpStatusCode.MethodNotAllowed);
                }
                return;
            }

            string offerJson;
            using (var reader = new StreamReader(request.InputStream, Encoding.UTF8))
            {
                offerJson = await reader.ReadToEndAsync().ConfigureAwait(false);
            }

            if (!RTCSessionDescriptionInit.TryParse(offerJson, out var offerInit))
            {
                Respond(context, HttpStatusCode.BadRequest);
                return;
            }

            var pc = CreateEchoPeerConnection(offerInit, logger, onSessionServed);

            var setResult = pc.setRemoteDescription(offerInit);
            if (setResult != SetDescriptionResultEnum.OK)
            {
                logger.LogWarning("Failed to apply the echo offer: {Result}.", setResult);
                pc.Close("invalid offer");
                Respond(context, HttpStatusCode.BadRequest);
                return;
            }

            var answer = pc.createAnswer();
            await pc.setLocalDescription(answer).ConfigureAwait(false);

            string sessionId = Guid.NewGuid().ToString("N");
            sessions[sessionId] = pc;
            pc.onconnectionstatechange += (state) =>
            {
                if (state is RTCPeerConnectionState.closed or RTCPeerConnectionState.failed)
                {
                    sessions.TryRemove(sessionId, out _);
                }
            };

            var answerInit = new RTCSessionDescriptionInit { type = RTCSdpType.answer, sdp = pc.localDescription.sdp.ToString() };
            var answerBytes = Encoding.UTF8.GetBytes(answerInit.toJSON());

            context.Response.StatusCode = (int)HttpStatusCode.OK;
            context.Response.ContentType = "application/json";
            context.Response.AddHeader("Access-Control-Allow-Origin", "*");
            context.Response.ContentLength64 = answerBytes.Length;
            await context.Response.OutputStream.WriteAsync(answerBytes).ConfigureAwait(false);
            context.Response.Close();

            logger.LogDebug("Answered an echo offer from {Remote}.", request.RemoteEndPoint);
        }
        catch (Exception excp)
        {
            logger.LogWarning("Error handling echo request: {Error}", excp.Message);
            Respond(context, HttpStatusCode.InternalServerError);
        }
    }

    private static RTCPeerConnection CreateEchoPeerConnection(RTCSessionDescriptionInit offer, ILogger logger, Action onSessionServed)
    {
        var pc = new RTCPeerConnection();
        var offerSdp = SDP.ParseSDPDescription(offer.sdp);

        if (offerSdp.Media.Any(x => x.Media == SDPMediaTypesEnum.audio))
        {
            pc.addTrack(new MediaStreamTrack(SDPWellKnownMediaFormatsEnum.PCMU));
        }
        if (offerSdp.Media.Any(x => x.Media == SDPMediaTypesEnum.video))
        {
            pc.addTrack(new MediaStreamTrack(new VideoFormat(VideoCodecsEnum.VP8, VP8_PAYLOAD_ID)));
        }

        // Echo received RTP straight back to the sender.
        pc.OnRtpPacketReceived += (remoteEndPoint, media, rtpPacket) =>
            pc.SendRtpRaw(media, rtpPacket.Payload, rtpPacket.Header.Timestamp, rtpPacket.Header.MarkerBit, rtpPacket.Header.PayloadType);

        pc.ondatachannel += (dc) =>
        {
            logger.LogDebug("Echo data channel opened for label {Label}.", dc.label);
            dc.onmessage += (rdc, proto, data) =>
            {
                logger.LogDebug("Echoing data channel message ({Length} bytes).", data.Length);
                rdc.send(Encoding.UTF8.GetString(data));
            };
        };

        pc.onconnectionstatechange += (state) =>
        {
            logger.LogDebug("Echo peer connection state changed to {State}.", state);
            if (state == RTCPeerConnectionState.connected)
            {
                onSessionServed();
            }
            else if (state == RTCPeerConnectionState.failed)
            {
                pc.Close("ice failure");
            }
        };

        return pc;
    }

    private static void Respond(HttpListenerContext context, HttpStatusCode statusCode)
    {
        try
        {
            context.Response.StatusCode = (int)statusCode;
            context.Response.Close();
        }
        catch (Exception)
        {
            // The connection may already be gone; nothing to do.
        }
    }

    private static int WriteResult(bool asJson, EchoServerResult result, int exitCode)
    {
        if (asJson)
        {
            WriteJson(result);
        }
        else if (result.Success)
        {
            Console.WriteLine($"WebRTC echo server on {result.ListenUrl} served {result.SessionsServed} session(s) over {result.DurationMs}ms.");
        }
        else
        {
            Console.Error.WriteLine($"WebRTC echo server on {result.ListenUrl} failed: {result.Error}");
        }

        return exitCode;
    }
}
