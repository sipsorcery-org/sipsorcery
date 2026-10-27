//-----------------------------------------------------------------------------
// Filename: BrowserWhipServer.cs
//
// Description: The listener behind every "browser as the far end" feature: one
// localhost HTTP port that serves the BrowserTestPage, answers the page's WHIP
// offer and surfaces the received video frames and the page's getStats reports.
//
//   GET  /       -> the test page
//   POST /whip   -> the page's SDP offer; answered 201 + application/sdp + Location
//   POST /stats  -> the page's flattened getStats reports (BrowserStatsReport)
//   DELETE       -> the page going away
//
// It exists because two very different callers need exactly this and nothing
// of each other: the "webrtc browser-test" verb, which reports an interop
// result, and the route graph's "browser" source edge, which turns the same
// received frames into MediaFrames for a sink. Keeping the listener here means
// the WHIP answer path, the codec pinning and the reloaded-tab rule are
// written once and cannot drift between them.
//
// One session at a time, because two tabs publishing at once would blend into
// one stream. A reload BEFORE any media has arrived is the common case though
// - the page is opened by hand and an early refresh is normal - so a fresh
// offer replaces a session that has not yet produced a frame, taking fresh
// counters with it. Once frames are flowing the session is live and a second
// offer is refused with 409.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 27 Oct 2026	Aaron Clauson	Created, Wexford, Ireland, extracted from WebRtcBrowserTestCommand
//                              so the route "browser" source edge can share it.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.Cli.Commands;

public sealed class BrowserWhipServer : IAsyncDisposable
{
    /// <summary>
    /// What the page is built with and how the session is negotiated. DurationSeconds 0 leaves the
    /// page running until its tab closes, which is what an open-ended route wants; a positive value
    /// makes the page stop itself and post a final stats report, which is what a measured run wants.
    /// Echo adds the send direction to our track (the page then hands the decoded track back).
    /// </summary>
    public sealed record Settings(
        int Port,
        string Mode,
        string? BrowserMimeType,
        int Width,
        int Height,
        int Fps,
        int DurationSeconds,
        bool Echo);

    private readonly Settings _settings;
    private readonly List<VideoFormat> _offeredVideoFormats;
    private readonly ILogger _logger;
    private readonly HttpListener _listener = new();

    private readonly TaskCompletionSource<bool> _connected = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _browserFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _offerSeen = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private CancellationTokenSource? _cts;
    private Task? _serveLoop;

    private RTCPeerConnection? _pc;
    private RtpStreamStats _videoStats = new();
    private int _videoFramesReceived;
    private long _firstFrameTicks;
    private long _lastFrameTicks;
    private string? _negotiatedCodec;
    private BrowserStatsReport? _latestBrowserStats;
    // The page load whose offer we answered. Stats posts from any other are ignored; see HandleAsync.
    private string? _acceptedSessionId;

    /// <summary>Raised for each depacketised video frame, on the RTP receive thread.</summary>
    public event Action<byte[], uint, VideoFormat>? OnVideoFrame;

    public BrowserWhipServer(Settings settings, List<VideoFormat> offeredVideoFormats, ILogger logger)
    {
        _settings = settings;
        _offeredVideoFormats = offeredVideoFormats;
        _logger = logger;
        Url = $"http://localhost:{settings.Port}/";
    }

    public string Url { get; }

    /// <summary>The peer connection of the live session, or null before the first offer.</summary>
    public RTCPeerConnection? PeerConnection => _pc;

    /// <summary>Sequence analysis for the live session. Replaced when a reloaded tab takes over.</summary>
    public RtpStreamStats VideoStats => _videoStats;

    public int VideoFramesReceived => Volatile.Read(ref _videoFramesReceived);

    /// <summary>The codec actually negotiated, from the formats event or the first frame. Null until then.</summary>
    public string? NegotiatedCodec => _negotiatedCodec;

    /// <summary>The most recent stats report the page posted, or null if it has posted none.</summary>
    public BrowserStatsReport? LatestBrowserStats => _latestBrowserStats;

    /// <summary>Completes true once a browser session reaches connected, false if one fails.</summary>
    public Task<bool> Connected => _connected.Task;

    /// <summary>Completes when the page posts its final stats report or sends DELETE.</summary>
    public Task BrowserFinished => _browserFinished.Task;

    /// <summary>True once any browser has POSTed an offer, used to tell "nobody opened the page" from "it failed".</summary>
    public bool OfferSeen => _offerSeen.Task.IsCompleted;

    /// <summary>
    /// Frames per second measured from the first received frame to the last (frames - 1 intervals),
    /// so connection ramp-up and an idle tail do not skew it. Null with fewer than two frames.
    /// </summary>
    public double? MeasuredFps
    {
        get
        {
            long first = Interlocked.Read(ref _firstFrameTicks);
            long last = Interlocked.Read(ref _lastFrameTicks);
            int frames = VideoFramesReceived;
            return frames > 1 && last > first
                ? Math.Round((frames - 1) / ((last - first) / (double)Stopwatch.Frequency), 1)
                : null;
        }
    }

    /// <summary>
    /// Binds the listener and starts serving. Throws <see cref="HttpListenerException"/> if the port
    /// cannot be bound, which callers turn into their own flavour of error.
    /// </summary>
    public void Start(CancellationToken ct)
    {
        _listener.Prefixes.Add(Url);
        _listener.Start();

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _serveLoop = Task.Run(() => ServeLoopAsync(_cts.Token), CancellationToken.None);
    }

    /// <summary>Opens the page in the operator's default browser; best effort, logged if it fails.</summary>
    public void TryOpenBrowser()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Url) { UseShellExecute = true });
        }
        catch (Exception excp)
        {
            _logger.LogDebug("Could not open a browser automatically ({Error}); open {Url} manually.", excp.Message, Url);
        }
    }

    private async Task ServeLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                return;     // Listener stopped; the run is over.
            }

            try
            {
                await HandleAsync(context, ct).ConfigureAwait(false);
            }
            catch (Exception excp)
            {
                _logger.LogDebug("Browser request handling failed: {Error}", excp.Message);
                Respond(context, HttpStatusCode.InternalServerError);
            }
        }
    }

    private async Task HandleAsync(HttpListenerContext context, CancellationToken ct)
    {
        var request = context.Request;
        string path = request.Url?.AbsolutePath ?? "/";

        if (request.HttpMethod == "GET" && (path == "/" || path == "/index.html"))
        {
            string page = BrowserTestPage.Build(_settings.Mode, _settings.BrowserMimeType,
                _settings.Width, _settings.Height, _settings.Fps, _settings.DurationSeconds);
            await RespondAsync(context, HttpStatusCode.OK, "text/html; charset=utf-8", page).ConfigureAwait(false);
            return;
        }

        if (request.HttpMethod == "POST" && path.TrimEnd('/').EndsWith("/whip", StringComparison.OrdinalIgnoreCase))
        {
            await HandleOfferAsync(context).ConfigureAwait(false);
            return;
        }

        if (request.HttpMethod == "POST" && path.TrimEnd('/').EndsWith("/stats", StringComparison.OrdinalIgnoreCase))
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
                // Only the tab whose offer was accepted may report. A refused tab (409) still runs
                // its failure path and posts a final error report; honouring that would end the
                // session the accepted tab is in the middle of carrying.
                string? accepted = _acceptedSessionId;
                if (accepted != null && report!.SessionId != null && report.SessionId != accepted)
                {
                    _logger.LogDebug("Ignoring a stats post from browser session {SessionId}, which is not the live one.", report.SessionId);
                }
                else
                {
                    _latestBrowserStats = report;
                    if (report!.Final)
                    {
                        _browserFinished.TrySetResult(true);
                    }
                }
            }
            else
            {
                _logger.LogWarning("Could not parse a browser stats post: {Error}", parseError);
            }

            Respond(context, HttpStatusCode.NoContent);
            return;
        }

        if (request.HttpMethod == "DELETE")
        {
            Respond(context, HttpStatusCode.OK);
            _browserFinished.TrySetResult(true);
            return;
        }

        Respond(context, HttpStatusCode.NotFound);
    }

    private async Task HandleOfferAsync(HttpListenerContext context)
    {
        // See the file header for the one-session rule. What makes a session worth protecting is that
        // it is STILL UP and carrying media: refusing on "has ever carried media" would lock the port
        // to a tab that has since been closed or navigated away, so a reload could never recover.
        if (!_offerSeen.TrySetResult(true))
        {
            bool liveSessionCarryingMedia =
                _pc?.connectionState == RTCPeerConnectionState.connected && VideoFramesReceived > 0;

            if (liveSessionCarryingMedia)
            {
                _logger.LogWarning("Rejected a second browser offer; a live session is already carrying media.");
                Respond(context, HttpStatusCode.Conflict);
                return;
            }

            _logger.LogDebug("A new browser offer arrived with no live session carrying media; taking over.");
            _pc?.Close("replaced by a reloaded browser tab");
            _videoStats = new RtpStreamStats();
            Interlocked.Exchange(ref _firstFrameTicks, 0);
            Interlocked.Exchange(ref _lastFrameTicks, 0);
            _negotiatedCodec = null;
        }

        _acceptedSessionId = context.Request.Headers["X-Session-Id"];

        string offerSdp;
        using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
        {
            offerSdp = await reader.ReadToEndAsync().ConfigureAwait(false);
        }

        _logger.LogDebug("Browser offer SDP:\n{Sdp}", offerSdp);

        var pc = new RTCPeerConnection();
        _pc = pc;

        // publish: we only receive. echo: we send our generated pattern AND receive what the browser
        // hands back, on the one bidirectional track.
        pc.addTrack(new MediaStreamTrack(_offeredVideoFormats,
            _settings.Echo ? MediaStreamStatusEnum.SendRecv : MediaStreamStatusEnum.RecvOnly));

        // The audio stats are discarded: this surface is about the video depacketisers and the page
        // publishes no audio track, so there is nothing for an audio counter to describe.
        pc.OnRtpPacketReceived += RtpStreamStats.CreateRtpHandler(new RtpStreamStats(), _videoStats, _logger);

        pc.OnVideoFormatsNegotiated += (formats) =>
        {
            if (formats.Count > 0)
            {
                _negotiatedCodec = formats[0].Codec.ToString();
                _logger.LogDebug("Negotiated video format {Codec} (payload {Id}).", formats[0].Codec, formats[0].FormatID);
            }
        };

        pc.OnVideoFrameReceived += (remoteEndPoint, timestamp, frame, format) =>
        {
            long nowTicks = Stopwatch.GetTimestamp();
            if (Interlocked.Increment(ref _videoFramesReceived) == 1)
            {
                Interlocked.Exchange(ref _firstFrameTicks, nowTicks);
                _negotiatedCodec ??= format.Codec.ToString();
            }
            Interlocked.Exchange(ref _lastFrameTicks, nowTicks);

            OnVideoFrame?.Invoke(frame, timestamp, format);
        };

        pc.onconnectionstatechange += (state) =>
        {
            _logger.LogDebug("Browser peer connection state changed to {State}.", state);
            if (state == RTCPeerConnectionState.connected)
            {
                _connected.TrySetResult(true);
            }
            else if (state == RTCPeerConnectionState.failed || state == RTCPeerConnectionState.closed)
            {
                _connected.TrySetResult(false);
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
            _connected.TrySetResult(false);
            return;
        }

        var answer = pc.createAnswer();
        await pc.setLocalDescription(answer).ConfigureAwait(false);
        _logger.LogDebug("Answer SDP:\n{Sdp}", answer.sdp);

        // A rejected video m-line (port 0, or no formats) means the browser's offer and the codec we
        // offered had nothing in common. Say so now: letting it connect would time out with no media
        // and no reason given.
        if (!HasNegotiatedVideo(answer.sdp, out string? negotiateError))
        {
            await RespondAsync(context, HttpStatusCode.BadRequest, "text/plain", negotiateError!).ConfigureAwait(false);
            _connected.TrySetResult(false);
            return;
        }

        string resourcePath = $"/whip/{Guid.NewGuid().ToString("N")[..8]}";
        context.Response.AddHeader("Location", resourcePath);
        await RespondAsync(context, HttpStatusCode.Created, "application/sdp", answer.sdp).ConfigureAwait(false);
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

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();

        try { _pc?.Close("browser session complete"); } catch { /* best effort */ }

        if (_listener.IsListening)
        {
            try { _listener.Stop(); } catch { /* best effort */ }
        }

        if (_serveLoop != null)
        {
            try { await _serveLoop.ConfigureAwait(false); } catch { /* best effort */ }
        }

        try { _listener.Close(); } catch { /* best effort */ }
        _cts?.Dispose();
    }
}
