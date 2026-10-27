//-----------------------------------------------------------------------------
// Filename: WebRtcBrowserTestCommand.cs
//
// Description: The "sipsorcery webrtc browser-test" verb. Tests the library's
// RTP video packetisers and depacketisers against a REAL external
// implementation (libwebrtc, via Chrome) instead of against themselves.
//
// The motivation: "webrtc loopback" is introspective. It packetises and
// depacketises with the same code, so a packetiser and a depacketiser that
// share a misreading of an RFC agree with each other perfectly and the
// loopback stays green. Only an independent implementation on the other end
// can find that class of fault. A browser is the most valuable such
// implementation, because it is the one almost every real deployment is
// talking to.
//
// This verb serves a page (BrowserTestPage) on a local HTTP listener and
// receives from it, one port, three routes:
//
//   GET  /       -> the test page: a canvas test pattern, codec pinning and a
//                   pc.getStats() poller
//   POST /whip   -> the page's SDP offer; the verb answers (201 + Location)
//   POST /stats  -> the page's flattened getStats reports (BrowserStatsReport)
//
// It is a new verb rather than a flag on "whip-server" because the HTTP
// surface is different (whip-server serves no page and takes no stats
// callback) and because the thing being measured is different: whip-server
// reports on a stream, this reports on an interop RESULT with both sides'
// numbers next to each other. It does reuse the pieces that should not be
// duplicated: RtpStreamStats for the sequence analysis and
// WebRtcWhepCommand.TryResolveOfferedFormats for --video-codec.
//
// Two directions, --mode:
//
//   publish (default) - Chrome publishes its canvas, the CLI receives. One
//                       encode in the path, so a fault is ours: libwebrtc's
//                       packetiser against OUR depacketiser.
//   echo              - the CLI publishes a generated test card and the page
//                       hands the track straight back. The return leg is
//                       Chrome's OWN re-encode (a browser cannot bounce RTP
//                       the way "webrtc echo-server" does with SendRtpRaw;
//                       see BrowserTestPage), so this exercises our
//                       packetiser into Chrome's depacketiser on the way out
//                       and Chrome's packetiser into our depacketiser on the
//                       way back. There is no byte-exact comparison to be had
//                       from it: judge it on decode success and frame counts.
//
// Reading the result: the JSON carries OUR counters (videoPackets, videoLost,
// videoFrames) and the BROWSER's (framesEncoded, framesDecoded, freezeCount,
// pliCount, ...). Chrome's pliCount or freezeCount climbing while videoLost
// stays 0 is the signature of a packetisation or reassembly fault - every
// packet arrived and a usable frame still could not be assembled.
//
// H265 is conditional rather than a flat gap. A browser's H265 SEND support
// is build and platform dependent - it rides on a hardware encoder, so the
// same Chrome version can have it on one machine and not on another. Where it
// is present (verified on Chrome/Windows, which advertises
// level-id=93;profile-id=1;tier-flag=0;tx-mode=SRST) --video-codec h265 tests
// end to end like any other codec; where it is absent the page detects that
// from RTCRtpSender.getCapabilities and fails saying so, rather than timing
// out. So an h265 result is only ever evidence about the browser that ran it.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 27 Oct 2026	Aaron Clauson	Created, Wexford, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.CommandLine;
using System.Diagnostics;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using SIPSorcery.Cli.Common;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.FFmpeg;

namespace SIPSorcery.Cli.Commands;

public sealed class WebRtcBrowserTestCommand : CommandBase
{
    private const int DEFAULT_TIMEOUT_SECONDS = 120;
    private const int DEFAULT_DURATION_SECONDS = 10;
    private const int DEFAULT_PORT = 8080;
    private const int DEFAULT_FPS = 30;
    private const string DEFAULT_PRESET = "360p";
    private const uint VIDEO_CLOCK_RATE = 90000;
    private const double BITS_PER_PIXEL_PER_FRAME = 0.1;

    // The echo send loop waits this long after "connected" before the first frame so the browser has
    // finished installing its receive context and does not discard the opening keyframe (the same
    // settle LibraryVideoPublisher applies for the in-process receiver).
    private const int CONNECT_SETTLE_MS = 200;

    /// <summary>
    /// The result shape written to stdout with --json. Stable field names; additive changes only.
    /// The Video* fields are OUR receive side; Browser is Chrome's own view of the same session.
    /// </summary>
    private sealed record BrowserTestResult(
        bool Success,
        string Url,
        string Mode,
        string ConnectionState,
        string? RequestedVideoCodec,
        string? NegotiatedVideoCodec,
        long? ConnectTimeMs,
        int? MediaDurationMs,
        int VideoPackets,
        long VideoLost,
        int VideoOutOfOrder,
        int VideoDuplicates,
        int VideoFrames,
        double? VideoFps,
        int? SentFrames,
        long? SentBytes,
        double? SentFps,
        BrowserStatsReport? Browser,
        string? Error);

    public WebRtcBrowserTestCommand() : base(DEFAULT_TIMEOUT_SECONDS)
    { }

    public override Command Build()
    {
        var portOption = new Option<int>("--port")
        {
            Description = $"The localhost port to serve the test page and accept the browser's offer on. Defaults to {DEFAULT_PORT}.",
            DefaultValueFactory = _ => DEFAULT_PORT
        };

        var modeOption = new Option<string>("--mode")
        {
            Description = "Which way the video flows: publish (the browser publishes its canvas test pattern and the " +
                          "CLI receives, testing OUR depacketiser against the browser's packetiser) or echo (the CLI " +
                          "publishes and the browser hands the track back, which the browser RE-ENCODES, so both " +
                          "directions are exercised but no byte-exact comparison is possible).",
            DefaultValueFactory = _ => BrowserTestPage.MODE_PUBLISH
        };

        var durationOption = new Option<int>("--duration", "-d")
        {
            Description = "The number of seconds to exchange media for once the browser has connected.",
            DefaultValueFactory = _ => DEFAULT_DURATION_SECONDS
        };

        var presetOption = new Option<string>("--preset")
        {
            Description = $"The test pattern resolution: {VideoPresets.Names}. Overridden by --size.",
            DefaultValueFactory = _ => DEFAULT_PRESET
        };

        var sizeOption = new Option<string?>("--size")
        {
            Description = "An explicit test pattern resolution as WxH, e.g. 1280x720. Overrides --preset."
        };

        var fpsOption = new Option<int>("--fps")
        {
            Description = $"The test pattern frame rate. Defaults to {DEFAULT_FPS}.",
            DefaultValueFactory = _ => DEFAULT_FPS
        };

        var videoCodecOption = new Option<string?>(CodecSelection.VIDEO_OPTION)
        {
            Description = "Force the video codec on BOTH ends: exactly one of " +
                          $"{CodecSelection.VideoNamesList}. The CLI offers only that codec and the page pins it with " +
                          "setCodecPreferences, so a successful run is evidence about that codec specifically. " +
                          "h265 depends on the browser having a hardware H265 encoder, so it works on some machines " +
                          "and reports the missing capability on others."
        };

        var openOption = new Option<bool>("--open")
        {
            Description = "Open the test page in the default browser instead of printing the URL to open by hand."
        };

        var ffmpegPathOption = new Option<string?>("--ffmpeg-path")
        {
            Description = "Directory containing the FFmpeg shared libraries, used by the --mode echo encoder. Defaults to the system path."
        };

        var command = new Command("browser-test",
            "Test the library's video packetiser and depacketiser against a real browser: serve a canvas test pattern page, receive from it (or echo to it) and report both sides' stats.");
        command.Options.Add(portOption);
        command.Options.Add(modeOption);
        command.Options.Add(durationOption);
        command.Options.Add(presetOption);
        command.Options.Add(sizeOption);
        command.Options.Add(fpsOption);
        command.Options.Add(videoCodecOption);
        command.Options.Add(openOption);
        command.Options.Add(ffmpegPathOption);
        AddCommonOptions(command);

        command.SetAction((parseResult, cancellationToken) => RunAsync(
            parseResult.GetValue(portOption),
            parseResult.GetValue(modeOption)!,
            parseResult.GetValue(durationOption),
            parseResult.GetValue(presetOption)!,
            parseResult.GetValue(sizeOption),
            parseResult.GetValue(fpsOption),
            parseResult.GetValue(videoCodecOption),
            parseResult.GetValue(openOption),
            parseResult.GetValue(ffmpegPathOption),
            parseResult.GetValue(TimeoutOption),
            parseResult.GetValue(JsonOption),
            parseResult.GetValue(VerboseOption),
            cancellationToken));

        return command;
    }

    /// <summary>
    /// Validates the options that do not need anything started, resolving the frame size. Separated
    /// so the validation is unit testable without a listener or a browser.
    /// </summary>
    public static bool TryValidate(string mode, int port, int durationSeconds, string preset, string? size, int fps,
        string? videoCodec, out int width, out int height, out string? browserMimeType, out string? error)
    {
        width = 0;
        height = 0;
        browserMimeType = null;
        error = null;

        if (!BrowserTestPage.IsValidMode(mode))
        {
            error = $"Unknown --mode \"{mode}\". Expected {BrowserTestPage.MODE_PUBLISH} (the browser publishes, the CLI receives) " +
                    $"or {BrowserTestPage.MODE_ECHO} (the CLI publishes, the browser hands the track back).";
            return false;
        }

        if (port is < 1 or > 65535)
        {
            error = $"--port {port} is not a valid TCP port (1-65535).";
            return false;
        }

        if (durationSeconds <= 0)
        {
            error = $"--duration {durationSeconds} must be at least 1 second.";
            return false;
        }

        if (fps is < 1 or > 240)
        {
            error = $"--fps {fps} is outside the supported range (1-240).";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(size))
        {
            var parts = size.Split('x', 'X');
            if (parts.Length != 2 || !int.TryParse(parts[0], out width) || !int.TryParse(parts[1], out height) ||
                width < 16 || height < 16 || width > 7680 || height > 4320)
            {
                error = $"Could not parse --size \"{size}\" as WxH in the range 16x16 to 7680x4320, e.g. 1280x720.";
                return false;
            }
        }
        else if (!VideoPresets.TryResolve(preset, out width, out height, out error))
        {
            return false;
        }

        // A pinned codec has to be a name the library knows AND one the page can name to the browser:
        // pinning our end only would let the browser pick something else and make a pass meaningless.
        if (!string.IsNullOrWhiteSpace(videoCodec))
        {
            if (!CodecSelection.TryResolveVideo(videoCodec, CodecSelection.VIDEO_OPTION, out _, out error))
            {
                return false;
            }

            browserMimeType = BrowserTestPage.TryGetMimeType(videoCodec);
            if (browserMimeType == null)
            {
                error = $"No browser RTP mime type is known for {CodecSelection.VIDEO_OPTION} \"{videoCodec?.Trim()}\", " +
                        "so the page could not pin it on the browser side. Use one of: vp8, vp9, h264, h265, av1.";
                return false;
            }
        }

        return true;
    }

    private static async Task<int> RunAsync(int port, string mode, int durationSeconds, string preset, string? size,
        int fps, string? videoCodec, bool open, string? ffmpegPath, int timeoutSeconds, bool asJson, bool verbose,
        CancellationToken ct)
    {
        using var loggerFactory = InitLogging(verbose);
        var logger = loggerFactory.CreateLogger(nameof(WebRtcBrowserTestCommand));

        mode = (mode ?? string.Empty).Trim().ToLowerInvariant();
        string url = $"http://localhost:{port}/";

        if (!TryValidate(mode, port, durationSeconds, preset, size, fps, videoCodec,
                out int width, out int height, out string? browserMimeType, out string? validationError))
        {
            return WriteResult(asJson, Fail(url, mode, videoCodec, validationError), ExitCodes.InvalidArgument);
        }

        bool echo = mode == BrowserTestPage.MODE_ECHO;

        // Every codec the library supports, or just the one --video-codec pinned. Audio is left out
        // entirely: this verb exists to test the VIDEO depacketisers, and an audio track the page
        // never fills would only add an unanswered m-line to every result.
        if (!WebRtcWhepCommand.TryResolveOfferedFormats(null, videoCodec, out _, out var offeredVideoFormats,
                out string? codecError))
        {
            return WriteResult(asJson, Fail(url, mode, videoCodec, codecError), ExitCodes.InvalidArgument);
        }

        // Echo mode encodes in-process, which needs the FFmpeg shared libraries. Fail here with the
        // install hint rather than after a browser has already connected.
        IVideoEncoder? encoder = null;
        if (echo)
        {
            try
            {
                FFmpegInit.Initialise(FfmpegLogLevelEnum.AV_LOG_FATAL, ffmpegPath, logger);
            }
            catch (Exception excp)
            {
                return WriteResult(asJson, Fail(url, mode, videoCodec,
                    $"Could not initialise FFmpeg for --mode echo: {excp.Message}. Install the FFmpeg shared libraries (e.g. winget install ffmpeg) or pass --ffmpeg-path."),
                    ExitCodes.TransportError);
            }

            var ffmpegEncoder = new FFmpegVideoEncoder();
            ffmpegEncoder.SetBitrate((int)(width * (long)height * fps * BITS_PER_PIXEL_PER_FRAME), null, null, null);
            encoder = ffmpegEncoder;
        }

        using var listener = new HttpListener();
        listener.Prefixes.Add(url);

        RTCPeerConnection? pc = null;
        // Replaced wholesale when a reloaded tab takes over the run, so the counters only ever
        // describe the session that actually carried the media.
        var videoStats = new RtpStreamStats();

        // Set by the HTTP handlers (listener threads) and read by the run loop, so everything shared
        // is either interlocked or a volatile reference to an immutable record.
        int videoFramesReceived = 0;
        long firstFrameTicks = 0;
        long lastFrameTicks = 0;
        string? negotiatedCodec = null;
        BrowserStatsReport? latestBrowserStats = null;

        var connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var browserFinished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var offerSeen = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            listener.Start();
        }
        catch (HttpListenerException excp)
        {
            return WriteResult(asJson, Fail(url, mode, videoCodec,
                $"Could not listen on {url}: {excp.Message} (is the port already in use?)."), ExitCodes.TransportError);
        }

        using var overallCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        try
        {
            // Operator guidance on stderr so stdout stays the result channel.
            Console.Error.WriteLine($"Browser interop test ({mode}) ready. Open {url} in Chrome" +
                $" - {width}x{height}@{fps}, {(videoCodec == null ? "codec negotiated freely" : $"{videoCodec} pinned on both ends")}, {durationSeconds}s.");
            Console.Error.WriteLine("Keep the tab visible: a background tab throttles the page's timers and skews the frame rate.");

            if (open)
            {
                TryOpenBrowser(url, logger);
            }

            // ---- Serve the page, the offer and the stats posts until the run ends. ----
            var serveLoop = Task.Run(async () =>
            {
                while (!overallCts.IsCancellationRequested && listener.IsListening)
                {
                    HttpListenerContext context;
                    try
                    {
                        context = await listener.GetContextAsync().ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        return;     // Listener stopped; the run is over.
                    }

                    try
                    {
                        var request = context.Request;
                        string path = request.Url?.AbsolutePath ?? "/";

                        if (request.HttpMethod == "GET" && (path == "/" || path == "/index.html"))
                        {
                            string page = BrowserTestPage.Build(mode, browserMimeType, width, height, fps, durationSeconds);
                            await RespondAsync(context, HttpStatusCode.OK, "text/html; charset=utf-8", page).ConfigureAwait(false);
                        }
                        else if (request.HttpMethod == "POST" && path.TrimEnd('/').EndsWith("/whip", StringComparison.OrdinalIgnoreCase))
                        {
                            // One session at a time, because two tabs publishing at once would blend
                            // their counters into a meaningless result. A reload before any media has
                            // arrived is the common case though - the page is opened by hand, and an
                            // early refresh is normal - so a fresh offer REPLACES a session that has
                            // not depacketised a frame yet, taking fresh counters with it. Once frames
                            // are flowing the measurement is in progress and a second offer is refused.
                            if (!offerSeen.TrySetResult(true))
                            {
                                if (Volatile.Read(ref videoFramesReceived) > 0)
                                {
                                    logger.LogWarning("Rejected a second browser offer; a session is already carrying media.");
                                    Respond(context, HttpStatusCode.Conflict);
                                    continue;
                                }

                                logger.LogDebug("A new browser offer arrived before any media; replacing the previous session.");
                                pc?.Close("replaced by a reloaded browser tab");
                                videoStats = new RtpStreamStats();
                                Interlocked.Exchange(ref firstFrameTicks, 0);
                                Interlocked.Exchange(ref lastFrameTicks, 0);
                                negotiatedCodec = null;
                            }

                            string offerSdp;
                            using (var reader = new StreamReader(request.InputStream, Encoding.UTF8))
                            {
                                offerSdp = await reader.ReadToEndAsync().ConfigureAwait(false);
                            }

                            logger.LogDebug("Browser offer SDP:\n{Sdp}", offerSdp);

                            pc = new RTCPeerConnection();

                            // publish: we only receive. echo: we send the generated pattern AND receive
                            // what the browser hands back, on the one bidirectional track.
                            pc.addTrack(new MediaStreamTrack(offeredVideoFormats,
                                echo ? MediaStreamStatusEnum.SendRecv : MediaStreamStatusEnum.RecvOnly));

                            pc.OnRtpPacketReceived += RtpStreamStats.CreateRtpHandler(new RtpStreamStats(), videoStats, logger);

                            pc.OnVideoFormatsNegotiated += (formats) =>
                            {
                                if (formats.Count > 0)
                                {
                                    negotiatedCodec = formats[0].Codec.ToString();
                                    logger.LogDebug("Negotiated video format {Codec} (payload {Id}).", formats[0].Codec, formats[0].FormatID);
                                }
                            };

                            pc.OnVideoFrameReceived += (remoteEndPoint, timestamp, frame, format) =>
                            {
                                long nowTicks = Stopwatch.GetTimestamp();
                                if (Interlocked.Increment(ref videoFramesReceived) == 1)
                                {
                                    Interlocked.Exchange(ref firstFrameTicks, nowTicks);
                                    negotiatedCodec ??= format.Codec.ToString();
                                }
                                Interlocked.Exchange(ref lastFrameTicks, nowTicks);
                            };

                            pc.onconnectionstatechange += (state) =>
                            {
                                logger.LogDebug("Peer connection state changed to {State}.", state);
                                if (state == RTCPeerConnectionState.connected)
                                {
                                    connected.TrySetResult(true);
                                }
                                else if (state == RTCPeerConnectionState.failed || state == RTCPeerConnectionState.closed)
                                {
                                    connected.TrySetResult(false);
                                }
                            };

                            var setOfferResult = pc.setRemoteDescription(new RTCSessionDescriptionInit
                            {
                                type = RTCSdpType.offer,
                                sdp = offerSdp
                            });

                            if (setOfferResult != SetDescriptionResultEnum.OK)
                            {
                                await RespondAsync(context, HttpStatusCode.BadRequest, "text/plain",
                                    $"The browser's SDP offer could not be applied: {setOfferResult}.").ConfigureAwait(false);
                                connected.TrySetResult(false);
                                continue;
                            }

                            var answer = pc.createAnswer();
                            await pc.setLocalDescription(answer).ConfigureAwait(false);
                            logger.LogDebug("Answer SDP:\n{Sdp}", answer.sdp);

                            // A rejected video m-line (port 0, or no formats) means the browser's offer
                            // and the pinned codec had nothing in common. Say so now: letting it connect
                            // would just time out with no media and no reason given.
                            if (!HasNegotiatedVideo(answer.sdp, out string? negotiateError))
                            {
                                await RespondAsync(context, HttpStatusCode.BadRequest, "text/plain",
                                    $"{negotiateError} Requested codec: {videoCodec ?? "(any)"}.").ConfigureAwait(false);
                                connected.TrySetResult(false);
                                continue;
                            }

                            string resourcePath = $"/whip/{Guid.NewGuid().ToString("N")[..8]}";
                            context.Response.AddHeader("Location", resourcePath);
                            await RespondAsync(context, HttpStatusCode.Created, "application/sdp", answer.sdp).ConfigureAwait(false);
                        }
                        else if (request.HttpMethod == "POST" && path.TrimEnd('/').EndsWith("/stats", StringComparison.OrdinalIgnoreCase))
                        {
                            string body;
                            using (var reader = new StreamReader(request.InputStream, Encoding.UTF8))
                            {
                                char[] buffer = new char[BrowserStatsReport.MAX_BODY_BYTES];
                                int read = await reader.ReadBlockAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                                body = new string(buffer, 0, read);
                            }

                            if (BrowserStatsReport.TryParse(body, out var report, out string? parseError))
                            {
                                latestBrowserStats = report;
                                if (report!.Final)
                                {
                                    browserFinished.TrySetResult(true);
                                }
                            }
                            else
                            {
                                logger.LogWarning("Could not parse a browser stats post: {Error}", parseError);
                            }

                            Respond(context, HttpStatusCode.NoContent);
                        }
                        else if (request.HttpMethod == "DELETE")
                        {
                            Respond(context, HttpStatusCode.OK);
                            browserFinished.TrySetResult(true);
                        }
                        else
                        {
                            Respond(context, HttpStatusCode.NotFound);
                        }
                    }
                    catch (Exception excp)
                    {
                        logger.LogDebug("Request handling failed: {Error}", excp.Message);
                        Respond(context, HttpStatusCode.InternalServerError);
                    }
                }
            }, overallCts.Token);

            // ---- Wait for the browser to connect. ----
            var connectStopwatch = Stopwatch.StartNew();
            var connectCompleted = await Task.WhenAny(connected.Task,
                Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), overallCts.Token)).ConfigureAwait(false);

            if (connectCompleted != connected.Task || !await connected.Task.ConfigureAwait(false))
            {
                // The page posts a final report carrying its own reason (e.g. "no video/H265
                // capability for sending") before giving up, so prefer that over the generic wording.
                string reason = latestBrowserStats?.Error
                    ?? (ct.IsCancellationRequested ? "Cancelled."
                        : connectCompleted == connected.Task
                            ? $"The peer connection failed (state {pc?.connectionState})."
                            : offerSeen.Task.IsCompleted
                                ? $"The browser offered but the peer connection did not reach connected within {timeoutSeconds}s."
                                : $"No browser connected to {url} within {timeoutSeconds}s.");

                return WriteResult(asJson, new BrowserTestResult(false, url, mode, pc?.connectionState.ToString() ?? "new",
                    videoCodec, negotiatedCodec, null, null, videoStats.Packets, videoStats.Lost, videoStats.OutOfOrder,
                    videoStats.Duplicates, videoFramesReceived, null, null, null, null, latestBrowserStats, reason),
                    ExitCodes.Timeout);
            }

            long connectTimeMs = connectStopwatch.ElapsedMilliseconds;
            logger.LogDebug("Browser connected in {ConnectTimeMs}ms.", connectTimeMs);
            Console.Error.WriteLine($"Browser connected in {connectTimeMs}ms; exchanging media for {durationSeconds}s ...");

            // ---- In echo mode, publish the generated test card for the window. ----
            Task<SendResult>? sendTask = null;
            var sendCts = CancellationTokenSource.CreateLinkedTokenSource(overallCts.Token);
            if (echo)
            {
                sendTask = SendTestCardAsync(pc!, encoder!, width, height, fps, offeredVideoFormats[0].Codec, logger, sendCts.Token);
            }

            // ---- The media window. It ends when the page posts its final report (the normal path,
            // which also means the browser's counters cover the whole window) or the duration plus a
            // grace elapses, whichever comes first. ----
            var mediaWindow = Stopwatch.StartNew();
            await Task.WhenAny(
                browserFinished.Task,
                Task.Delay(TimeSpan.FromSeconds(durationSeconds) + TimeSpan.FromMilliseconds(2 * BrowserTestPage.STATS_INTERVAL_MS), overallCts.Token))
                .ConfigureAwait(false);
            mediaWindow.Stop();

            SendResult? sent = null;
            if (sendTask != null)
            {
                sendCts.Cancel();
                sent = await sendTask.ConfigureAwait(false);
            }

            double? videoFps = null;
            long first = Interlocked.Read(ref firstFrameTicks);
            long last = Interlocked.Read(ref lastFrameTicks);
            if (videoFramesReceived > 1 && last > first)
            {
                videoFps = Math.Round((videoFramesReceived - 1) / ((last - first) / (double)Stopwatch.Frequency), 1);
            }

            bool gotMedia = videoStats.Packets > 0 && videoFramesReceived > 0;

            return WriteResult(asJson, new BrowserTestResult(gotMedia, url, mode, pc!.connectionState.ToString(),
                videoCodec, negotiatedCodec, connectTimeMs, (int)mediaWindow.ElapsedMilliseconds,
                videoStats.Packets, videoStats.Lost, videoStats.OutOfOrder, videoStats.Duplicates,
                videoFramesReceived, videoFps, sent?.Frames, sent?.Bytes, sent?.Fps, latestBrowserStats,
                gotMedia ? null : "The browser connected but no video frames were depacketised."),
                gotMedia ? ExitCodes.Ok : ExitCodes.Failed);
        }
        catch (OperationCanceledException)
        {
            return WriteResult(asJson, Fail(url, mode, videoCodec, "Cancelled."), ExitCodes.Timeout);
        }
        catch (Exception excp)
        {
            return WriteResult(asJson, Fail(url, mode, videoCodec, excp.Message), ExitCodes.TransportError);
        }
        finally
        {
            overallCts.Cancel();
            pc?.Close("browser test complete");
            (encoder as IDisposable)?.Dispose();
            if (listener.IsListening)
            {
                listener.Stop();
            }
        }
    }

    private readonly record struct SendResult(int Frames, long Bytes, double Fps);

    /// <summary>
    /// The --mode echo send loop: draw the shared test card, encode it and send it paced to the frame
    /// rate until cancelled. Deliberately a small local loop rather than LibraryVideoPublisher, which
    /// owns its own peer connection and dials out; here the peer connection is the one the browser
    /// already offered into.
    /// </summary>
    private static Task<SendResult> SendTestCardAsync(RTCPeerConnection pc, IVideoEncoder encoder, int width, int height,
        int fps, VideoCodecsEnum codec, ILogger logger, CancellationToken ct)
    {
        return Task.Run(async () =>
        {
            try
            {
                await Task.Delay(CONNECT_SETTLE_MS, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return new SendResult(0, 0, 0);
            }

            var card = new TestCardPattern(width, height, fps);
            byte[] raw = new byte[card.I420Size];
            uint rtpDuration = VIDEO_CLOCK_RATE / (uint)fps;
            int frames = 0;
            int attempted = 0;
            long bytes = 0;
            var stopwatch = Stopwatch.StartNew();

            while (!ct.IsCancellationRequested && pc.connectionState == RTCPeerConnectionState.connected)
            {
                card.Fill(raw, attempted);
                byte[]? encoded = encoder.EncodeVideo(width, height, raw, VideoPixelFormatsEnum.I420, codec);
                attempted++;

                if (encoded != null && encoded.Length > 0)
                {
                    try
                    {
                        pc.SendVideo(rtpDuration, encoded);
                        frames++;
                        bytes += encoded.Length;
                    }
                    catch (Exception excp)
                    {
                        logger.LogDebug("Echo send failed: {Error}", excp.Message);
                    }
                }

                long sleepMs = (long)attempted * 1000 / fps - stopwatch.ElapsedMilliseconds;
                if (sleepMs > 1)
                {
                    try { await Task.Delay((int)sleepMs, ct).ConfigureAwait(false); }
                    catch (OperationCanceledException) { break; }
                }
            }

            stopwatch.Stop();
            double seconds = stopwatch.Elapsed.TotalSeconds;
            return new SendResult(frames, bytes, seconds > 0 ? Math.Round(frames / seconds, 1) : 0);
        }, ct);
    }

    /// <summary>
    /// Checks an answer SDP actually accepted video. A port of 0, a missing video m-line or an empty
    /// format list all mean the offer and the pinned codec had nothing in common, which has to be a
    /// clear error rather than a connection that goes quiet.
    /// </summary>
    public static bool HasNegotiatedVideo(string answerSdp, out string? error)
    {
        error = null;

        SDP parsed;
        try
        {
            parsed = SDP.ParseSDPDescription(answerSdp);
        }
        catch (Exception excp)
        {
            error = $"The answer SDP could not be parsed back ({excp.Message}).";
            return false;
        }

        var video = parsed.Media.FirstOrDefault(m => m.Media == SDPMediaTypesEnum.video);

        if (video == null)
        {
            error = "The answer has no video media announcement, so nothing was negotiated.";
            return false;
        }

        if (video.Port == 0 || video.MediaFormats.Count == 0)
        {
            error = "The browser's offer and the codec the CLI offered had no video format in common, " +
                    "so the video m-line was rejected.";
            return false;
        }

        return true;
    }

    private static BrowserTestResult Fail(string url, string mode, string? videoCodec, string? error) =>
        new(false, url, mode, "new", videoCodec, null, null, null, 0, 0, 0, 0, 0, null, null, null, null, null, error);

    private static void TryOpenBrowser(string url, ILogger logger)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception excp)
        {
            logger.LogDebug("Could not open a browser automatically ({Error}); open {Url} manually.", excp.Message, url);
        }
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

    private static async Task RespondAsync(HttpListenerContext context, HttpStatusCode statusCode, string contentType, string body)
    {
        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(body);
            context.Response.StatusCode = (int)statusCode;
            context.Response.ContentType = contentType;
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
            context.Response.Close();
        }
        catch (Exception)
        {
            // The browser may have navigated away; nothing to do.
        }
    }

    private static int WriteResult(bool asJson, BrowserTestResult result, int exitCode)
    {
        if (asJson)
        {
            WriteJson(result);
        }
        else if (result.Success)
        {
            string ours = $"Ours: {result.VideoPackets} packets ({FormatAnomalies(result.VideoLost, result.VideoOutOfOrder, result.VideoDuplicates)}), " +
                          $"{result.VideoFrames} frames depacketised" + (result.VideoFps != null ? $" at {result.VideoFps} fps" : string.Empty);
            string sent = result.SentFrames != null ? $" Sent {result.SentFrames} frames ({result.SentBytes} bytes) at {result.SentFps} fps." : string.Empty;
            string browser = result.Browser != null ? $" Browser: {result.Browser.Describe()}." : " The browser reported no stats.";

            Console.WriteLine($"Browser connected in {result.ConnectTimeMs}ms and ran {result.MediaDurationMs}ms as " +
                $"{result.NegotiatedVideoCodec ?? "an unidentified codec"}. {ours}.{sent}{browser}");
        }
        else
        {
            Console.Error.WriteLine($"Browser test on {result.Url} failed (state {result.ConnectionState}): {result.Error}");
        }

        return exitCode;
    }

    private static string FormatAnomalies(long lost, int outOfOrder, int duplicates) =>
        lost == 0 && outOfOrder == 0 && duplicates == 0
            ? "clean"
            : $"{lost} lost, {outOfOrder} reordered, {duplicates} duplicate";
}
