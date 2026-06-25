//-----------------------------------------------------------------------------
// Filename: BrowserAudioBridge.cs
//
// Description: A browser-as-audio-device for the "openai chat" verb. It hosts a
// local HTTP listener that serves a small page; the page captures the microphone
// with getUserMedia and plays the model's voice, over a single send/recv OPUS
// WebRTC peer connection back to the CLI. The CLI then relays OPUS frames between
// this browser peer connection and the OpenAI endpoint in both directions
// (repacketise, not transcode) - the browser is the microphone AND the speaker.
//
// This replaces the fragile ffmpeg microphone capture (a piped s16le stdin) and
// the ffplay output: the browser is a far more reliable cross platform audio
// device, and crucially it has a built-in acoustic echo canceller, so unlike the
// piped path this is FULL DUPLEX - no half-duplex mic gating is needed, the model
// can be interrupted (barge-in). getUserMedia works over http://localhost because
// localhost is a secure context, so no TLS is required.
//
// The HTTP + answer plumbing mirrors the route "web" sink (an HttpListener that
// serves a page and answers a browser SDP offer), kept as raw HttpListener so the
// packaged tool stays lean.
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

using System.Diagnostics;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.Cli.Commands;

internal sealed class BrowserAudioBridge : IAsyncDisposable
{
    private const string WHEP_PATH = "/whep";

    private readonly int _port;
    private readonly bool _openBrowser;
    private readonly ILogger _logger;
    private readonly HttpListener _listener = new();

    private RTCPeerConnection? _browserPc;
    private readonly object _pcLock = new();
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;
    private string _url = string.Empty;
    private bool _announcedConnected;

    /// <summary>Raised for each OPUS frame received from the browser microphone (relay it to OpenAI).</summary>
    public event Action<EncodedAudioFrame>? OnMicFrameReceived;

    /// <summary>Raised once when a browser peer connection first reaches connected (cue the model greeting).</summary>
    public event Action? OnBrowserConnected;

    public string Url => _url;

    public BrowserAudioBridge(int port, bool openBrowser, ILogger logger)
    {
        _port = port;
        _openBrowser = openBrowser;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken ct)
    {
        // localhost only: a loopback prefix needs no urlacl/admin on Windows, and getUserMedia + WebRTC
        // both work on http://localhost without TLS (localhost is a secure context).
        _url = $"http://localhost:{_port}/";
        _listener.Prefixes.Add(_url);

        try
        {
            _listener.Start();
        }
        catch (HttpListenerException excp)
        {
            throw new InvalidOperationException($"Could not bind the browser audio device on {_url}: {excp.Message} (is the port already in use?).");
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token));

        Console.Error.WriteLine($"Browser audio device on {_url} - open it and click \"Start talking\" to use your mic and speakers.");

        if (_openBrowser)
        {
            TryOpenBrowser(_url);
        }

        return Task.CompletedTask;
    }

    /// <summary>Relays a model voice frame from OpenAI to the browser speaker.</summary>
    public void SendToBrowser(EncodedAudioFrame frame)
    {
        var pc = _browserPc;
        if (pc == null || frame.EncodedAudio.Length == 0)
        {
            return;
        }

        try
        {
            pc.SendAudio(ToRtpUnits(frame), frame.EncodedAudio);
        }
        catch (Exception excp)
        {
            _logger.LogDebug("Browser audio send failed: {Error}", excp.Message);
        }
    }

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
                return; // Listener stopped/disposed during shutdown.
            }

            try
            {
                await HandleRequestAsync(context, ct).ConfigureAwait(false);
            }
            catch (Exception excp)
            {
                _logger.LogDebug("Browser audio request handling failed: {Error}", excp.Message);
                Respond(context, HttpStatusCode.InternalServerError);
            }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken ct)
    {
        var request = context.Request;
        string path = request.Url?.AbsolutePath ?? "/";

        if (request.HttpMethod == "GET" && (path == "/" || path == "/index.html"))
        {
            byte[] page = Encoding.UTF8.GetBytes(BuildPage());
            context.Response.StatusCode = (int)HttpStatusCode.OK;
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = page.Length;
            await context.Response.OutputStream.WriteAsync(page, ct).ConfigureAwait(false);
            context.Response.Close();
            return;
        }

        if (request.HttpMethod == "POST" && path.TrimEnd('/').EndsWith(WHEP_PATH, StringComparison.OrdinalIgnoreCase))
        {
            await HandleOfferAsync(context, ct).ConfigureAwait(false);
            return;
        }

        if (request.HttpMethod == "DELETE")
        {
            Respond(context, HttpStatusCode.OK);
            return;
        }

        Respond(context, HttpStatusCode.MethodNotAllowed);
    }

    private async Task HandleOfferAsync(HttpListenerContext context, CancellationToken ct)
    {
        string offerSdp;
        using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
        {
            offerSdp = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
        }

        _logger.LogDebug("Browser audio received offer SDP:\n{Sdp}", offerSdp);

        var pc = new RTCPeerConnection();

        // A single send/recv OPUS audio track: receive the browser microphone, send the model's voice.
        pc.addTrack(new MediaStreamTrack(new List<AudioFormat> { AudioCommonlyUsedFormats.OpusWebRTC }, MediaStreamStatusEnum.SendRecv));

        pc.OnAudioFrameReceived += frame => OnMicFrameReceived?.Invoke(frame);

        pc.onconnectionstatechange += (state) =>
        {
            _logger.LogDebug("Browser audio peer connection state changed to {State}.", state);
            if (state == RTCPeerConnectionState.connected)
            {
                if (!_announcedConnected)
                {
                    _announcedConnected = true;
                    OnBrowserConnected?.Invoke();
                }
            }
            else if (state == RTCPeerConnectionState.failed || state == RTCPeerConnectionState.closed ||
                     state == RTCPeerConnectionState.disconnected)
            {
                lock (_pcLock)
                {
                    if (ReferenceEquals(_browserPc, pc))
                    {
                        _browserPc = null;
                    }
                }
                try { pc.Close("browser audio gone"); } catch { /* best effort */ }
            }
        };

        var setOfferResult = pc.setRemoteDescription(new RTCSessionDescriptionInit { type = RTCSdpType.offer, sdp = offerSdp });
        if (setOfferResult != SetDescriptionResultEnum.OK)
        {
            _logger.LogWarning("Browser audio could not apply the offer: {Result}.", setOfferResult);
            try { pc.Close("bad offer"); } catch { /* best effort */ }
            Respond(context, HttpStatusCode.BadRequest);
            return;
        }

        var answer = pc.createAnswer();
        await pc.setLocalDescription(answer).ConfigureAwait(false);

        // Only one browser drives the chat: replace any previous peer connection with this one.
        RTCPeerConnection? previous;
        lock (_pcLock)
        {
            previous = _browserPc;
            _browserPc = pc;
        }
        if (previous != null)
        {
            try { previous.Close("replaced by a new browser"); } catch { /* best effort */ }
        }

        byte[] answerBytes = Encoding.UTF8.GetBytes(answer.sdp);
        context.Response.StatusCode = (int)HttpStatusCode.Created;
        context.Response.ContentType = "application/sdp";
        context.Response.AddHeader("Location", $"{WHEP_PATH}/{Guid.NewGuid().ToString("N")[..8]}");
        context.Response.ContentLength64 = answerBytes.Length;
        await context.Response.OutputStream.WriteAsync(answerBytes, ct).ConfigureAwait(false);
        context.Response.Close();

        _logger.LogDebug("Browser audio answered a new session.");
    }

    /// <summary>Converts a frame's millisecond duration to its RTP clock units for the outgoing track.</summary>
    private static uint ToRtpUnits(EncodedAudioFrame frame) =>
        (uint)((long)frame.DurationMilliSeconds * frame.AudioFormat.RtpClockRate / 1000);

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
    /// The browser audio page: a Start button (needed for the mic permission and audio autoplay gesture)
    /// that captures the microphone with echo cancellation, plays the model's track and runs the WHEP
    /// offer/answer against /whep. Single quoted JS braces are literal in the $$ raw string.
    /// </summary>
    private static string BuildPage()
    {
        return """
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>SIPSorcery · OpenAI voice</title>
              <style>
                html, body { margin: 0; height: 100%; background: #111; color: #ddd; font-family: system-ui, sans-serif; display: grid; place-items: center; }
                .card { text-align: center; }
                button { font-size: 18px; padding: 12px 24px; border: 0; border-radius: 8px; background: #2d7; color: #042; cursor: pointer; }
                button:disabled { background: #555; color: #999; cursor: default; }
                #status { margin-top: 16px; font-size: 14px; color: #aaa; min-height: 1.2em; }
              </style>
            </head>
            <body>
              <div class="card">
                <button id="start">Start talking</button>
                <div id="status">Click to allow your microphone and connect.</div>
                <audio id="a" autoplay></audio>
              </div>
              <script>
                const statusEl = document.getElementById('status');
                const startBtn = document.getElementById('start');
                startBtn.onclick = async () => {
                  startBtn.disabled = true;
                  statusEl.textContent = 'requesting microphone…';
                  try {
                    const stream = await navigator.mediaDevices.getUserMedia({ audio: { echoCancellation: true, noiseSuppression: true, autoGainControl: true } });
                    const pc = new RTCPeerConnection();
                    stream.getTracks().forEach((t) => pc.addTrack(t, stream));
                    pc.ontrack = (e) => { document.getElementById('a').srcObject = e.streams[0]; };
                    pc.onconnectionstatechange = () => {
                      statusEl.textContent = pc.connectionState === 'connected' ? 'connected — talk to the assistant' : pc.connectionState;
                    };
                    const offer = await pc.createOffer();
                    await pc.setLocalDescription(offer);
                    await new Promise((resolve) => {
                      if (pc.iceGatheringState === 'complete') { resolve(); return; }
                      pc.addEventListener('icegatheringstatechange', () => { if (pc.iceGatheringState === 'complete') resolve(); });
                    });
                    const resp = await fetch('/whep', { method: 'POST', headers: { 'Content-Type': 'application/sdp' }, body: pc.localDescription.sdp });
                    if (!resp.ok) { statusEl.textContent = 'connection failed: HTTP ' + resp.status; startBtn.disabled = false; return; }
                    await pc.setRemoteDescription({ type: 'answer', sdp: await resp.text() });
                  } catch (err) {
                    statusEl.textContent = 'error: ' + err;
                    startBtn.disabled = false;
                  }
                };
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

        RTCPeerConnection? pc;
        lock (_pcLock)
        {
            pc = _browserPc;
            _browserPc = null;
        }
        try { pc?.Close("browser audio bridge disposed"); } catch { /* best effort */ }

        try { _listener.Close(); } catch { /* best effort */ }
        _cts?.Dispose();
    }
}
