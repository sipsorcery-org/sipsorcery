//-----------------------------------------------------------------------------
// Filename: WebSinkNode.cs
//
// Description: A self-hosting WebRTC egress edge for the "route" verb: instead of
// publishing to an external endpoint (whip:), it binds a local HTTP listener and
// becomes a WHEP (WebRTC-HTTP Egress Protocol) SERVER. A single listener serves
// two routes off one port:
//
//   GET  /        -> a tiny HTML player page that auto-connects on load
//   POST /whep    -> the WHEP signalling: the browser POSTs its SDP offer, the
//                    sink answers (201 + application/sdp + Location) and streams
//
// So "route --from testpattern --to web" turns the CLI into a watch-it-in-a-browser
// server: open the printed URL and the page pulls the live stream. Each browser tab
// that connects gets its own peer connection, so the one graph tee fans out to many
// viewers for free. This is the demand-driven mirror of WhipSinkNode (which dials a
// remote WHIP endpoint and blocks until connected); here StartAsync only binds the
// listener and returns - viewers may join later, or never - and the run is bounded
// by --duration (0 = until Ctrl-C, the default).
//
// Like the rest of the graph this repacketises, it does not transcode: video is the
// graph's H264 and audio is the --audio-codec format, relayed onto each viewer's
// send-only tracks unchanged. Browsers play H264 + Opus (and Chrome/Firefox also
// G.711), so the defaults work; use --audio-codec opus for the broadest support.
//
// The server side HTTP+answer plumbing mirrors the diagnostics tool's
// "webrtc whip-server" verb (an HttpListener that answers an SDP offer), kept as
// raw HttpListener so the packaged tool stays lean (no ASP.NET dependency).
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 25 Jun 2026	Aaron Clauson	Created, Wexford, Ireland.
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

namespace SIPSorcery.Cli.Commands.Route;

public sealed class WebSinkNode : ISinkNode
{
    private const string WHEP_PATH = "/whep";

    private readonly int _port;
    private readonly AudioFormat _audioFormat;
    private readonly string? _token;
    private readonly bool _openBrowser;
    private readonly ILogger _logger;
    private readonly HttpListener _listener = new();

    // The set of currently connected browser viewers. Mutated by the accept loop (new POST /whep) and
    // the per-peer state change handler (a viewer leaving); read by Write on the source thread. Guarded
    // by its own lock; Write snapshots under the lock and sends outside it so a slow send never blocks
    // a new viewer joining.
    private readonly List<Viewer> _viewers = new();
    private readonly object _viewersLock = new();

    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;
    private string _url = string.Empty;

    private int _framesServed;
    private long _bytesServed;
    private int _dropped;

    /// <summary>One connected browser: its peer connection and whether it is up and ready for frames.</summary>
    private sealed class Viewer
    {
        public required RTCPeerConnection Pc { get; init; }
        public volatile bool Connected;
    }

    public WebSinkNode(int port, AudioFormat audioFormat, string? token, bool openBrowser, ILogger logger)
    {
        _port = port;
        _audioFormat = audioFormat;
        _token = token;
        _openBrowser = openBrowser;
        _logger = logger;
    }

    public string Describe() => $"web ({_url})";

    public Task StartAsync(CancellationToken ct)
    {
        // localhost only: a loopback prefix needs no urlacl/admin on Windows, browsers treat
        // http://localhost as a secure context (so WebRTC works without TLS), and it keeps the stream
        // off the LAN by default. Exposing it to other devices would be an explicit future opt-in.
        _url = $"http://localhost:{_port}/";
        _listener.Prefixes.Add(_url);

        try
        {
            _listener.Start();
        }
        catch (HttpListenerException excp)
        {
            throw new EdgeException($"Could not bind the web sink on {_url}: {excp.Message} (is the port already in use?).");
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token));

        // Operator guidance on stderr so stdout/--json stays the clean result channel.
        Console.Error.WriteLine($"Web sink listening on {_url} - open it in a browser to watch the stream (streaming until Ctrl-C unless --duration is set).");

        if (_openBrowser)
        {
            TryOpenBrowser(_url);
        }

        return Task.CompletedTask;
    }

    public void Write(MediaFrame frame)
    {
        if (frame.Payload.Length == 0)
        {
            return;
        }

        // Snapshot the connected viewers under the lock, then send outside it.
        Viewer[] connected;
        lock (_viewersLock)
        {
            connected = _viewers.Where(v => v.Connected).ToArray();
        }

        if (connected.Length == 0)
        {
            // Nothing watching yet (or everyone left): the frame has nowhere to go.
            Interlocked.Increment(ref _dropped);
            return;
        }

        foreach (var viewer in connected)
        {
            try
            {
                if (frame.Kind == MediaKind.Audio)
                {
                    viewer.Pc.SendAudio(frame.DurationRtpUnits, frame.Payload);
                }
                else
                {
                    viewer.Pc.SendVideo(frame.DurationRtpUnits, frame.Payload);
                }
            }
            catch (Exception excp)
            {
                _logger.LogDebug("web sink send to a viewer failed: {Error}", excp.Message);
            }
        }

        // Count the frame once (served to >= 1 viewer), independent of the viewer count, so the stat
        // reads as "frames served" rather than "frames x viewers".
        Interlocked.Increment(ref _framesServed);
        Interlocked.Add(ref _bytesServed, frame.Payload.Length);
    }

    public SinkStats GetStats() => new(_framesServed, Interlocked.Read(ref _bytesServed), _dropped);

    private async Task AcceptLoopAsync(CancellationToken ct)
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
                // Listener stopped/disposed during shutdown.
                return;
            }

            try
            {
                await HandleRequestAsync(context, ct).ConfigureAwait(false);
            }
            catch (Exception excp)
            {
                _logger.LogDebug("web sink request handling failed: {Error}", excp.Message);
                Respond(context, HttpStatusCode.InternalServerError);
            }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken ct)
    {
        var request = context.Request;
        string path = request.Url?.AbsolutePath ?? "/";

        // GET / (or /index.html) -> the player page.
        if (request.HttpMethod == "GET" && (path == "/" || path == "/index.html"))
        {
            byte[] page = Encoding.UTF8.GetBytes(BuildPlayerPage());
            context.Response.StatusCode = (int)HttpStatusCode.OK;
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = page.Length;
            await context.Response.OutputStream.WriteAsync(page, ct).ConfigureAwait(false);
            context.Response.Close();
            return;
        }

        // POST /whep -> the WHEP offer/answer exchange that brings up a new viewer.
        if (request.HttpMethod == "POST" && path.TrimEnd('/').EndsWith(WHEP_PATH, StringComparison.OrdinalIgnoreCase))
        {
            if (_token != null && request.Headers["Authorization"] != $"Bearer {_token}")
            {
                _logger.LogWarning("web sink rejected a WHEP offer with a missing or incorrect bearer token.");
                Respond(context, HttpStatusCode.Unauthorized);
                return;
            }

            await HandleWhepOfferAsync(context, ct).ConfigureAwait(false);
            return;
        }

        // DELETE on a session resource -> a viewer leaving (best effort; the connection state change
        // also prunes it). Acknowledge so the browser's WHEP client is satisfied.
        if (request.HttpMethod == "DELETE")
        {
            Respond(context, HttpStatusCode.OK);
            return;
        }

        Respond(context, HttpStatusCode.MethodNotAllowed);
    }

    private async Task HandleWhepOfferAsync(HttpListenerContext context, CancellationToken ct)
    {
        string offerSdp;
        using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
        {
            offerSdp = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
        }

        // Logged at debug (shown with --verbose) to diagnose codec/direction negotiation, e.g. a video
        // m-line the browser offers that does not match the H264 the sink answers with.
        _logger.LogDebug("web sink received viewer offer SDP:\n{Sdp}", offerSdp);

        var pc = new RTCPeerConnection();

        // Send-only tracks the browser (a recvonly WHEP player) will receive: the graph's H264 video and
        // the --audio-codec audio, offered as the exact formats the source produces so payloads relay
        // unchanged (repacketise, not transcode).
        pc.addTrack(new MediaStreamTrack(new List<AudioFormat> { _audioFormat }, MediaStreamStatusEnum.SendOnly));
        pc.addTrack(new MediaStreamTrack(new List<VideoFormat> { RouteVideoFormats.H264 }, MediaStreamStatusEnum.SendOnly));

        var viewer = new Viewer { Pc = pc };

        pc.onconnectionstatechange += (state) =>
        {
            _logger.LogDebug("web sink viewer peer connection state changed to {State}.", state);
            if (state == RTCPeerConnectionState.connected)
            {
                viewer.Connected = true;
            }
            else if (state == RTCPeerConnectionState.failed || state == RTCPeerConnectionState.closed ||
                     state == RTCPeerConnectionState.disconnected)
            {
                viewer.Connected = false;
                RemoveViewer(viewer);
                try { pc.Close("web sink viewer gone"); } catch { /* best effort */ }
            }
        };

        var setOfferResult = pc.setRemoteDescription(new RTCSessionDescriptionInit
        {
            type = RTCSdpType.offer,
            sdp = offerSdp
        });

        if (setOfferResult != SetDescriptionResultEnum.OK)
        {
            _logger.LogWarning("web sink could not apply a viewer's SDP offer: {Result}.", setOfferResult);
            try { pc.Close("bad offer"); } catch { /* best effort */ }
            Respond(context, HttpStatusCode.BadRequest);
            return;
        }

        var answer = pc.createAnswer();
        await pc.setLocalDescription(answer).ConfigureAwait(false);

        _logger.LogDebug("web sink answer SDP to viewer:\n{Sdp}", answer.sdp);

        // Register the viewer before replying so it is ready for frames the instant it connects.
        lock (_viewersLock)
        {
            _viewers.Add(viewer);
        }

        // 201 + answer SDP + a Location header naming the session resource (WHEP, RFC 9725 style).
        string resourcePath = $"{WHEP_PATH}/{Guid.NewGuid().ToString("N")[..8]}";
        byte[] answerBytes = Encoding.UTF8.GetBytes(answer.sdp);
        context.Response.StatusCode = (int)HttpStatusCode.Created;
        context.Response.ContentType = "application/sdp";
        context.Response.AddHeader("Location", resourcePath);
        context.Response.ContentLength64 = answerBytes.Length;
        await context.Response.OutputStream.WriteAsync(answerBytes, ct).ConfigureAwait(false);
        context.Response.Close();

        _logger.LogDebug("web sink accepted a new viewer; {Count} now connected.", CountViewers());
    }

    private void RemoveViewer(Viewer viewer)
    {
        lock (_viewersLock)
        {
            _viewers.Remove(viewer);
        }
    }

    private int CountViewers()
    {
        lock (_viewersLock)
        {
            return _viewers.Count;
        }
    }

    private void TryOpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception excp)
        {
            _logger.LogDebug("Could not open a browser automatically ({Error}); open {Url} manually.", excp.Message, url);
        }
    }

    /// <summary>
    /// The auto-connecting WHEP player page. It offers two recvonly transceivers, waits for ICE
    /// gathering to complete, POSTs the offer to /whep and applies the answer. Single quoted JS braces
    /// are literal in the $$ raw string; {{...}} are the interpolation holes.
    /// </summary>
    private string BuildPlayerPage()
    {
        string authHeader = string.IsNullOrWhiteSpace(_token)
            ? string.Empty
            : $", 'Authorization': 'Bearer {_token}'";

        return $$"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>SIPSorcery stream</title>
              <style>
                html, body { margin: 0; height: 100%; background: #111; color: #ddd; font-family: system-ui, sans-serif; }
                video { width: 100vw; height: 100vh; object-fit: contain; background: #000; }
                #status { position: fixed; top: 8px; left: 8px; padding: 4px 8px; background: rgba(0,0,0,.6); border-radius: 4px; font-size: 13px; }
              </style>
            </head>
            <body>
              <div id="status">connecting…</div>
              <video id="v" autoplay playsinline controls muted></video>
              <script>
                const statusEl = document.getElementById('status');
                const pc = new RTCPeerConnection();
                pc.addTransceiver('video', { direction: 'recvonly' });
                pc.addTransceiver('audio', { direction: 'recvonly' });
                pc.ontrack = (e) => { document.getElementById('v').srcObject = e.streams[0]; };
                pc.onconnectionstatechange = () => {
                  statusEl.textContent = pc.connectionState;
                  if (pc.connectionState === 'connected') { setTimeout(() => { statusEl.style.display = 'none'; }, 1500); }
                };
                (async () => {
                  try {
                    const offer = await pc.createOffer();
                    await pc.setLocalDescription(offer);
                    await new Promise((resolve) => {
                      if (pc.iceGatheringState === 'complete') { resolve(); return; }
                      pc.addEventListener('icegatheringstatechange', () => { if (pc.iceGatheringState === 'complete') resolve(); });
                    });
                    const resp = await fetch('/whep', { method: 'POST', headers: { 'Content-Type': 'application/sdp'{{authHeader}} }, body: pc.localDescription.sdp });
                    if (!resp.ok) { statusEl.textContent = 'WHEP request failed: HTTP ' + resp.status; return; }
                    const answer = await resp.text();
                    await pc.setRemoteDescription({ type: 'answer', sdp: answer });
                  } catch (err) {
                    statusEl.textContent = 'error: ' + err;
                  }
                })();
              </script>
            </body>
            </html>
            """;
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

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();

        if (_listener.IsListening)
        {
            try { _listener.Stop(); } catch { /* best effort */ }
        }

        if (_acceptLoop != null)
        {
            try { await _acceptLoop.ConfigureAwait(false); } catch { /* best effort */ }
        }

        Viewer[] viewers;
        lock (_viewersLock)
        {
            viewers = _viewers.ToArray();
            _viewers.Clear();
        }

        foreach (var viewer in viewers)
        {
            try { viewer.Pc.Close("route web sink disposed"); } catch { /* best effort */ }
        }

        try { _listener.Close(); } catch { /* best effort */ }
        _cts?.Dispose();
    }
}
