//-----------------------------------------------------------------------------
// Filename: BrowserTestPage.cs
//
// Description: Builds the self-contained HTML page the "webrtc browser-test"
// verb serves, which turns a Chrome tab into the EXTERNAL end of a WebRTC
// interop test.
//
// Why a generated canvas and not getUserMedia: the page draws its own test
// pattern into a <canvas> and publishes canvas.captureStream(fps). That needs
// no camera and no permission prompt (so it runs unattended), and it is
// deterministic - a frame counter, a moving block and a per-frame colour shift
// mean a dropped, stale or scrambled frame is visible by eye as well as
// countable. getUserMedia would make the test depend on whatever hardware
// happens to be plugged in.
//
// Codec pinning is done with transceiver.setCodecPreferences() over the
// filtered RTCRtpSender/RTCRtpReceiver capability list - the supported API -
// rather than by munging the SDP. If the browser has no capability for the
// requested codec the page says so and POSTs the reason, so pinning a codec
// Chrome cannot send fails with an explanation instead of an unexplained
// timeout.
//
// Two modes:
//   publish - one sendonly transceiver carrying the canvas: Chrome's
//             packetiser against OUR depacketiser, one encode in the path.
//   echo    - one sendrecv transceiver: the CLI sends its test pattern, the
//             page hands the received track straight back with
//             sender.replaceTrack() inside ontrack (no renegotiation).
//
// A browser CANNOT bounce RTP the way "webrtc echo-server" does with
// SendRtpRaw. Feeding an ontrack track into a sender makes Chrome DECODE and
// RE-ENCODE it, so the return leg carries Chrome's own encode and
// packetisation, not our bytes coming back. That is the point - it exercises
// our depacketiser with libwebrtc-shaped RTP - but it means there is no
// byte-exact comparison available, and an echo run is judged on decode
// success and frame counts, never on a bitstream diff.
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

namespace SIPSorcery.Cli.Commands;

public static class BrowserTestPage
{
    public const string MODE_PUBLISH = "publish";
    public const string MODE_ECHO = "echo";

    /// <summary>How often the page polls pc.getStats() and POSTs a progress report.</summary>
    public const int STATS_INTERVAL_MS = 1000;

    /// <summary>
    /// The RTP mime types the browser knows the CLI's codec names by. These are the exact strings
    /// RTCRtpSender.getCapabilities('video') reports, so the page can filter the capability list on
    /// them without any normalising of its own.
    /// </summary>
    private static readonly Dictionary<string, string> _mimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["vp8"] = "video/VP8",
        ["vp9"] = "video/VP9",
        ["h264"] = "video/H264",
        ["h265"] = "video/H265",
        ["av1"] = "video/AV1"
    };

    /// <summary>
    /// Maps a CLI --video-codec name to the browser RTP mime type for it. Returns null for a name
    /// no browser mime type is known for, which the caller reports rather than guessing at.
    /// </summary>
    public static string? TryGetMimeType(string? codecName) =>
        codecName != null && _mimeTypes.TryGetValue(codecName.Trim(), out string? mime) ? mime : null;

    /// <summary>True for a mode string the page can be built for.</summary>
    public static bool IsValidMode(string? mode) =>
        string.Equals(mode, MODE_PUBLISH, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(mode, MODE_ECHO, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Builds the page. <paramref name="mimeType"/> null leaves the page unpinned (whatever the
    /// browser and the CLI settle on); otherwise the page refuses to start unless the browser has a
    /// capability for it. Single braces in the $$ raw string are literal JS; {{...}} are the holes.
    /// </summary>
    public static string Build(string mode, string? mimeType, int width, int height, int fps, int durationSeconds)
    {
        bool echo = string.Equals(mode, MODE_ECHO, StringComparison.OrdinalIgnoreCase);
        string mimeLiteral = mimeType == null ? "null" : $"'{mimeType}'";
        string rxHidden = echo ? string.Empty : "hide";

        return $$"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>SIPSorcery browser interop test</title>
              <style>
                html, body { margin: 0; background: #111; color: #ddd; font-family: ui-monospace, Consolas, monospace; font-size: 13px; }
                #wrap { display: flex; flex-wrap: wrap; gap: 12px; padding: 12px; }
                canvas, video { background: #000; border: 1px solid #333; width: 480px; height: auto; }
                #log { white-space: pre-wrap; padding: 0 12px 12px; line-height: 1.5; }
                .hide { display: none; }
                #state { padding: 8px 12px; font-size: 15px; }
                .ok { color: #7ec699; } .bad { color: #e96a6a; } .warn { color: #e0b050; }
              </style>
            </head>
            <body>
              <div id="state">starting…</div>
              <div id="wrap">
                <div id="txbox"><div>canvas source (sent)</div><canvas id="cv" width="{{width}}" height="{{height}}"></canvas></div>
                <div id="rxbox" class="{{rxHidden}}"><div>returned from the CLI (decoded by Chrome)</div><video id="rx" autoplay playsinline muted></video></div>
              </div>
              <div id="log"></div>
              <script>
                const MODE = '{{mode}}';
                const MIME = {{mimeLiteral}};
                const W = {{width}}, H = {{height}}, FPS = {{fps}};
                const DURATION_MS = {{durationSeconds}} * 1000;
                const STATS_MS = {{STATS_INTERVAL_MS}};
                const ECHO = MODE === 'echo';

                const stateEl = document.getElementById('state');
                const logEl = document.getElementById('log');
                const canvas = document.getElementById('cv');
                const rxVideo = document.getElementById('rx');
                const ctx = canvas.getContext('2d', { alpha: false });

                const log = (msg, cls) => {
                  const line = document.createElement('div');
                  if (cls) { line.className = cls; }
                  line.textContent = new Date().toISOString().substr(11, 12) + '  ' + msg;
                  logEl.appendChild(line);
                };
                const setState = (msg, cls) => { stateEl.textContent = msg; stateEl.className = cls || ''; };

                // ---- The test pattern. Every frame differs (counter, moving block, hue shift) so the
                // encoder cannot coast on an unchanged canvas and a stale or reordered frame shows up.
                let frameIndex = 0;
                const BARS = ['#ffffff', '#ffff00', '#00ffff', '#00ff00', '#ff00ff', '#ff0000', '#0000ff', '#000000'];
                const drawFrame = () => {
                  const barW = W / BARS.length;
                  for (let i = 0; i < BARS.length; i++) {
                    ctx.fillStyle = BARS[i];
                    ctx.fillRect(i * barW, 0, Math.ceil(barW), H * 0.55);
                  }
                  // A band whose hue advances every frame: a repeated frame is obvious at a glance.
                  ctx.fillStyle = 'hsl(' + ((frameIndex * 7) % 360) + ', 90%, 50%)';
                  ctx.fillRect(0, H * 0.55, W, H * 0.1);
                  ctx.fillStyle = '#101010';
                  ctx.fillRect(0, H * 0.65, W, H * 0.35);
                  // The moving block sweeps left to right once every 2 seconds.
                  const blockW = Math.max(16, W / 16);
                  const sweep = (frameIndex % (FPS * 2)) / (FPS * 2);
                  ctx.fillStyle = '#20d060';
                  ctx.fillRect(sweep * (W - blockW), H * 0.66, blockW, H * 0.08);
                  // The frame counter, big enough to read in a decoded capture.
                  ctx.fillStyle = '#ffffff';
                  ctx.font = 'bold ' + Math.round(H * 0.18) + 'px ui-monospace, monospace';
                  ctx.textBaseline = 'top';
                  ctx.fillText(String(frameIndex).padStart(6, '0'), 8, H * 0.76);
                  frameIndex++;
                };

                // setInterval, not requestAnimationFrame: rAF is capped at the display refresh rate and
                // would silently cap FPS. Both are throttled in a BACKGROUND tab, so keep this tab visible.
                drawFrame();
                const drawTimer = setInterval(drawFrame, Math.max(1, Math.round(1000 / FPS)));

                // ---- Flatten pc.getStats() into the shape the CLI parses. getStats only links a
                // report to its codec by codecId, so resolve that to a mimeType here.
                const flatten = async (pc) => {
                  const report = await pc.getStats();
                  const codecs = new Map();
                  let outbound = null, inbound = null;
                  report.forEach((s) => {
                    if (s.type === 'codec') { codecs.set(s.id, s.mimeType); }
                  });
                  report.forEach((s) => {
                    if (s.kind !== 'video') { return; }
                    if (s.type === 'outbound-rtp') { outbound = s; }
                    else if (s.type === 'inbound-rtp') { inbound = s; }
                  });
                  const map = (s) => s == null ? null : {
                    codec: codecs.get(s.codecId) || null,
                    width: s.frameWidth ?? null,
                    height: s.frameHeight ?? null,
                    framesPerSecond: s.framesPerSecond ?? null,
                    framesEncoded: s.framesEncoded ?? null,
                    framesSent: s.framesSent ?? null,
                    framesReceived: s.framesReceived ?? null,
                    framesDecoded: s.framesDecoded ?? null,
                    framesDropped: s.framesDropped ?? null,
                    keyFramesEncoded: s.keyFramesEncoded ?? null,
                    keyFramesDecoded: s.keyFramesDecoded ?? null,
                    freezeCount: s.freezeCount ?? null,
                    pliCount: s.pliCount ?? null,
                    nackCount: s.nackCount ?? null,
                    firCount: s.firCount ?? null,
                    packetsSent: s.packetsSent ?? null,
                    packetsReceived: s.packetsReceived ?? null,
                    packetsLost: s.packetsLost ?? null,
                    bytesSent: s.bytesSent ?? null,
                    bytesReceived: s.bytesReceived ?? null,
                    qualityLimitationReason: s.qualityLimitationReason ?? null
                  };
                  return { outbound: map(outbound), inbound: map(inbound) };
                };

                const post = async (body) => {
                  try {
                    await fetch('/stats', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
                  } catch (err) { /* the CLI may already have finished and closed the listener */ }
                };

                const fail = async (msg) => {
                  setState(msg, 'bad');
                  log(msg, 'bad');
                  clearInterval(drawTimer);
                  await post({ mode: MODE, final: true, error: msg });
                };

                (async () => {
                  const pc = new RTCPeerConnection();
                  let tx;
                  try {
                    if (ECHO) {
                      // One sendrecv transceiver with no initial track. ontrack later calls
                      // replaceTrack() on its sender, which needs no renegotiation.
                      tx = pc.addTransceiver('video', { direction: 'sendrecv' });
                      document.getElementById('txbox').classList.add('hide');
                      clearInterval(drawTimer);
                    } else {
                      const stream = canvas.captureStream(FPS);
                      tx = pc.addTransceiver(stream.getVideoTracks()[0], { direction: 'sendonly' });
                    }

                    // ---- Pin the codec the supported way. Filtering the capability list keeps the
                    // browser's own fmtp variants (e.g. every H264 profile it offers) so the CLI can
                    // still choose the one it supports.
                    if (MIME) {
                      const sendCaps = RTCRtpSender.getCapabilities('video').codecs;
                      const recvCaps = RTCRtpReceiver.getCapabilities('video').codecs;
                      const pool = ECHO ? sendCaps.filter((c) => recvCaps.some((r) => r.mimeType.toLowerCase() === c.mimeType.toLowerCase())) : sendCaps;
                      const wanted = pool.filter((c) => c.mimeType.toLowerCase() === MIME.toLowerCase());
                      if (wanted.length === 0) {
                        await fail('This browser reports no ' + MIME + ' capability for ' + (ECHO ? 'send and receive' : 'sending') +
                                   ', so the codec cannot be pinned. H265 send support in particular rides on a ' +
                                   'hardware encoder, so it varies by machine as well as by browser build.');
                        return;
                      }
                      // De-duplicate identical capability entries, preserving the browser's order.
                      const seen = new Set();
                      const prefs = wanted.filter((c) => { const k = c.mimeType + '|' + (c.sdpFmtpLine || ''); if (seen.has(k)) { return false; } seen.add(k); return true; });
                      tx.setCodecPreferences(prefs);
                      log('pinned ' + MIME + ': ' + prefs.map((c) => c.sdpFmtpLine || '(no fmtp)').join(' / '));
                    } else {
                      log('no codec pinned; the browser and the CLI negotiate freely', 'warn');
                    }

                    pc.ontrack = (e) => {
                      log('ontrack: ' + e.track.kind + ' from the CLI');
                      rxVideo.srcObject = new MediaStream([e.track]);
                      rxVideo.play().catch(() => { /* muted autoplay should not be refused */ });
                      if (ECHO) {
                        // Chrome DECODES and RE-ENCODES this track on the way back out: the return leg
                        // is Chrome's bitstream, not ours bounced. See the file header.
                        tx.sender.replaceTrack(e.track).then(() => log('returning the decoded track to the CLI (re-encoded by Chrome)'));
                      }
                    };

                    pc.onconnectionstatechange = () => {
                      log('connection state: ' + pc.connectionState);
                      if (pc.connectionState === 'failed') { fail('the peer connection failed'); }
                    };

                    const offer = await pc.createOffer();
                    await pc.setLocalDescription(offer);
                    await new Promise((resolve) => {
                      if (pc.iceGatheringState === 'complete') { resolve(); return; }
                      pc.addEventListener('icegatheringstatechange', () => { if (pc.iceGatheringState === 'complete') { resolve(); } });
                    });

                    const resp = await fetch('/whip', { method: 'POST', headers: { 'Content-Type': 'application/sdp' }, body: pc.localDescription.sdp });
                    if (!resp.ok) {
                      await fail('the CLI rejected the offer: HTTP ' + resp.status + ' ' + (await resp.text()).slice(0, 300));
                      return;
                    }
                    await pc.setRemoteDescription({ type: 'answer', sdp: await resp.text() });
                    setState('connecting…');

                    // ---- Poll and report until the duration elapses, then send the final report. The
                    // CLI reports the final one; the progress reports keep a watching human informed.
                    const started = Date.now();
                    const tick = async (final) => {
                      const s = await flatten(pc);
                      const elapsedMs = Date.now() - started;
                      await post({ mode: MODE, elapsedMs: elapsedMs, final: final, outbound: s.outbound, inbound: s.inbound });
                      const o = s.outbound, i = s.inbound;
                      setState((o ? 'sent ' + (o.framesEncoded ?? '?') + ' frames (' + (o.codec || '?') + ')' : '') +
                               (o && i ? ' | ' : '') +
                               (i ? 'decoded ' + (i.framesDecoded ?? '?') + ' frames (' + (i.codec || '?') + '), ' + (i.freezeCount ?? '?') + ' freezes' : ''),
                               final ? 'ok' : '');
                    };

                    const statsTimer = setInterval(() => tick(false), STATS_MS);
                    setTimeout(async () => {
                      clearInterval(statsTimer);
                      clearInterval(drawTimer);
                      await tick(true);
                      log('done; closing the peer connection', 'ok');
                      pc.close();
                    }, DURATION_MS);
                  } catch (err) {
                    await fail('error: ' + err);
                    try { pc.close(); } catch (ignored) { /* already gone */ }
                  }
                })();
              </script>
            </body>
            </html>
            """;
    }
}
