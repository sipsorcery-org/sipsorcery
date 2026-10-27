//-----------------------------------------------------------------------------
// Filename: BrowserSourceNode.cs
//
// Description: A route ingress edge whose publisher is a BROWSER TAB. It binds
// a localhost listener (BrowserWhipServer, shared with the "webrtc
// browser-test" verb), serves the canvas test-pattern page, answers the page's
// WHIP offer and emits each depacketised video frame into the graph as a
// MediaFrame.
//
// It is the demand-driven mirror of WhepSourceNode: that one DIALS a remote
// WHEP endpoint, this one waits for a browser to come to it. The point is that
// the frames entering the graph were packetised by libwebrtc, so anything
// downstream - a file, an mpv pipe, a whip: sink - is carrying real external
// RTP that our depacketisers reassembled, not our own packetiser's output fed
// back to us:
//
//   route --from browser --to -:mkv | mpv -        watch Chrome's feed live
//   route --from browser --to out.mkv              record it
//   route --from browser --to whip:<url>           forward it on, no transcode
//
// Video only. The page publishes a canvas capture and no audio track, so there
// is no audio to carry and none is negotiated. A Matroska sink handles a
// video-only stream, which is what makes the mpv pipe work.
//
// StartAsync blocks until a browser actually connects (bounded by --timeout),
// unlike a generator source that is producing the moment it starts. That is
// deliberate: the sinks are brought up first and a Matroska sink would
// otherwise write a container header for a stream that may never arrive. Pass
// --open to launch the tab rather than opening the printed URL by hand.
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

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.Cli.Commands.Route;

public sealed class BrowserSourceNode : ISourceNode
{
    private readonly BrowserWhipServer _server;
    private readonly int _port;
    private readonly int _fps;
    private readonly bool _openBrowser;
    private readonly int _timeoutSeconds;
    private readonly ILogger _logger;
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private long? _connectTimeMs;

    public event Action<MediaFrame>? OnFrame;

    public Task Completion => _completion.Task;

    public long? ConnectTimeMs => _connectTimeMs;

    public BrowserSourceNode(int port, int width, int height, int fps, string? browserMimeType,
        List<VideoFormat> videoFormats, bool openBrowser, int timeoutSeconds, ILogger logger)
    {
        _port = port;
        _fps = fps;
        _openBrowser = openBrowser;
        _timeoutSeconds = timeoutSeconds;
        _logger = logger;

        // DurationSeconds 0: the page runs until its tab is closed. A route's length is the verb's
        // --duration (or Ctrl-C), so the page must not decide to stop on its own.
        _server = new BrowserWhipServer(
            new BrowserWhipServer.Settings(port, BrowserTestPage.MODE_PUBLISH, browserMimeType,
                width, height, fps, DurationSeconds: 0, Echo: false),
            videoFormats, logger);

        _server.OnVideoFrame += (payload, timestamp, format) =>
            OnFrame?.Invoke(MediaFrame.ForVideo(payload, timestamp, format));
    }

    public string Describe() => $"browser ({_server.Url})";

    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            _server.Start(ct);
        }
        catch (HttpListenerException excp)
        {
            throw new EdgeException($"Could not bind the browser source on {_server.Url}: {excp.Message} (is the port already in use?).");
        }

        // Operator guidance on stderr so stdout stays clean - it may be carrying the media itself
        // when the sink is "-" or "-:mkv".
        Console.Error.WriteLine($"Browser source waiting on {_server.Url} - open it in Chrome to start publishing " +
            $"(canvas test pattern @ {_fps}fps). Keep the tab visible: a background tab throttles its timers.");

        if (_openBrowser)
        {
            _server.TryOpenBrowser();
        }

        var stopwatch = Stopwatch.StartNew();
        var connectCompleted = await Task.WhenAny(_server.Connected,
            Task.Delay(TimeSpan.FromSeconds(_timeoutSeconds), ct)).ConfigureAwait(false);

        if (connectCompleted != _server.Connected)
        {
            throw new EdgeException(ct.IsCancellationRequested
                ? "Cancelled waiting for a browser to connect to the browser source."
                : $"No browser connected to {_server.Url} within {_timeoutSeconds}s (open the URL, or pass --open).");
        }

        if (!await _server.Connected.ConfigureAwait(false))
        {
            // The page posts its own reason before giving up (e.g. a codec it cannot send), which is
            // far more use than "the connection failed".
            throw new EdgeException(_server.LatestBrowserStats?.Error
                ?? $"The browser connected to {_server.Url} but the peer connection failed.");
        }

        _connectTimeMs = stopwatch.ElapsedMilliseconds;
        _logger.LogDebug("Browser source connected on port {Port} in {ConnectTimeMs}ms.", _port, _connectTimeMs);

        // The tab going away ends the route, the same way a remote hangup ends a whep: source.
        _ = _server.BrowserFinished.ContinueWith(_ => _completion.TrySetResult(), TaskScheduler.Default);
    }

    public ValueTask DisposeAsync() => _server.DisposeAsync();
}
