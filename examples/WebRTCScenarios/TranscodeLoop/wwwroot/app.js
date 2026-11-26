// TranscodeLoop's page. It holds both browser ends of the loop:
//   page A: the source (test card or Chromium's fake camera), sent to SIPSorcery on leg A;
//   page B: what SIPSorcery sends back on leg B, shown and measured.
// SIPSorcery (the host, C#) makes the offers; this page answers them. In between, the host
// shares the frames it decoded (DecodedView.cs), which the middle tile draws with WebGL.

import { startCard } from "./card.js";

const $ = (id) => document.getElementById(id);
const post = (m) => window.chrome?.webview?.postMessage(m);
const params = new URLSearchParams(location.search);

// ---------------------------------------------------------------------------------------------
// Log

const logLines = [];
function log(line, fromHost = false) {
  const stamped = fromHost ? line : `${new Date().toTimeString().slice(0, 8)} page: ${line}`;
  logLines.push(stamped);
  if (logLines.length > 600) logLines.shift();
  if ($("logs").open) { $("logText").textContent = logLines.join("\n"); $("logText").scrollTop = 1e9; }
  if (!fromHost) post({ type: "log", line });
}
$("logs").addEventListener("toggle", () => { $("logText").textContent = logLines.join("\n"); $("logText").scrollTop = 1e9; });

function showError(message) {
  const bar = $("errorBar");
  bar.textContent = message;
  bar.hidden = false;
  log(`error: ${message}`);
}

// ---------------------------------------------------------------------------------------------
// Clock: host times (ms since the app started) to this page's performance.now().

let hostOffset = null, bestRtt = Infinity;
const toPage = (hostMs) => (hostOffset == null ? null : hostMs - hostOffset);
function syncClock() { for (let i = 0; i < 5; i++) setTimeout(() => post({ type: "clock", t0: performance.now() }), i * 60); }
function onClock(m) {
  const t1 = performance.now(), rtt = t1 - m.t0;
  if (rtt < bestRtt) { bestRtt = rtt; hostOffset = m.host - (m.t0 + t1) / 2; }
}

// ---------------------------------------------------------------------------------------------
// Controls

let caps = { implementations: [] };
// No 8K: the browser can't capture and encode it in real time (a few frames in 15 s), and
// SIPSorcery's FFmpeg VP8 takes 100 ms to decode an 8K frame and 180 ms to encode one.
const SIZES = ["320x240", "640x480", "1280x720", "1920x1080", "3840x2160"];
const SIZE_LABELS = {
  "320x240": "320×240", "640x480": "640×480", "1280x720": "1280×720 (720p)",
  "1920x1080": "1920×1080 (1080p)", "3840x2160": "3840×2160 (4K)",
};
// Bitrates that give a clean picture at each size, picked when the size is: [browser, encoder] kbit/s.
const BITRATES = {
  "320x240": [500, 500], "640x480": [1500, 1000], "1280x720": [2500, 2000],
  "1920x1080": [4000, 4000], "3840x2160": [15000, 15000],
};
// Above 1080p the decoded tile starts off: each frame is copied into the shared buffer on
// SIPSorcery's decode thread and uploaded to the GPU on the page's main thread (25 MB a frame at
// 4K), which would slow the pipeline being measured and starve the test card. It can be turned on.
const BIG = 1920 * 1080;
const isBig = () => { const [w, h] = $("size").value.split("x").map(Number); return w * h > BIG; };
function defaultDecodedTile() {
  $("showDecoded").checked = !isBig();
  updateDecodedNote();
}

function suggestBitrates({ browser = true, encoder = true } = {}) {
  const [b, e] = BITRATES[$("size").value] ?? [];
  if (browser && b) $("kbps").value = String(b);
  if (encoder && e) $("encKbps").value = String(e);
}

function fillSelect(sel, values, wanted, labels = {}) {
  const keep = wanted ?? sel.value;
  sel.innerHTML = values.map((v) => `<option value="${v}">${labels[v] ?? v}</option>`).join("");
  if (values.includes(keep)) sel.value = keep;
}

function refreshPickers(initial = false) {
  const impls = caps.implementations;
  const relay = $("mode").value === "relay";
  const decCodecs = [...new Set(impls.flatMap((i) => i.decode))];
  const encCodecs = [...new Set(impls.flatMap((i) => i.encode))];
  fillSelect($("codecIn"), decCodecs.length ? decCodecs : ["VP8"], initial ? params.get("in") : null);
  fillSelect($("decoder"), impls.filter((i) => i.decode.includes($("codecIn").value)).map((i) => i.name), initial ? params.get("dec") : null);
  fillSelect($("codecOut"), encCodecs.length ? encCodecs : ["VP8"], initial ? params.get("out") : null);
  fillSelect($("encoder"), impls.filter((i) => i.encode.includes($("codecOut").value)).map((i) => i.name), initial ? params.get("enc") : null);
  for (const id of ["decoder", "codecOut", "encoder", "encKbps"]) $(id).disabled = relay;
  if (relay) $("codecOut").value = $("codecIn").value;
  $("encKbps").disabled = relay || $("encoder").value !== "ffmpeg";
  $("encKbps").title = $("encoder").value === "vp8.net" ? "vp8.net has a fixed quantiser, not a bitrate" : "";
}

function readConfig() {
  const [w, h] = $("size").value.split("x").map(Number);
  return {
    source: $("source").value, width: w, height: h, fps: Number($("fps").value), browserKbps: Number($("kbps").value),
    mode: $("mode").value, codecIn: $("codecIn").value, decoder: $("decoder").value,
    codecOut: $("mode").value === "relay" ? $("codecIn").value : $("codecOut").value, encoder: $("encoder").value,
    encoderKbps: Number($("encKbps").value),
    showDecoded: $("showDecoded").checked, pliOnDrop: $("pliOnDrop").checked,
    dropA: Number($("dropA").value), lossB: Number($("lossB").value),
  };
}

function initControls() {
  fillSelect($("size"), SIZES, params.get("size") ?? "640x480", SIZE_LABELS);
  suggestBitrates();   // the switches below override these
  $("size").addEventListener("change", () => { suggestBitrates(); defaultDecodedTile(); });   // before the restart below
  for (const [id, key] of [["source", "source"], ["fps", "fps"], ["mode", "mode"], ["kbps", "kbps"], ["encKbps", "enckbps"], ["dropA", "dropA"], ["lossB", "lossB"]]) {
    if (params.get(key)) $(id).value = params.get(key);
  }
  $("dropAOut").textContent = `${$("dropA").value}%`;
  $("lossBOut").textContent = `${$("lossB").value}%`;
  $("showDecoded").checked = params.get("decoded") ? params.get("decoded") !== "0" : !isBig();
  for (const id of ["source", "size", "fps", "kbps", "mode", "codecIn", "decoder", "codecOut", "encoder", "encKbps"]) {
    $(id).addEventListener("change", () => {
      refreshPickers();
      setAspect();
      if (running) restart();
    });
  }
  $("startBtn").addEventListener("click", () => (running ? stop() : start()));
  for (const [id, key] of [["dropA", "dropA"], ["lossB", "lossB"]]) {
    $(id).addEventListener("input", () => {
      $(id + "Out").textContent = `${$(id).value}%`;
      post({ type: "set", key, value: Number($(id).value) });
    });
  }
  $("pliOnDrop").addEventListener("change", () => post({ type: "set", key: "pliOnDrop", value: $("pliOnDrop").checked }));
  $("showDecoded").addEventListener("change", () => {
    post({ type: "set", key: "showDecoded", value: $("showDecoded").checked });
    updateDecodedNote();
  });
  $("showOverlay").addEventListener("change", () => document.body.classList.toggle("no-overlay", !$("showOverlay").checked));
  $("listen").addEventListener("change", () => { $("outVideo").muted = !$("listen").checked; });
  $("keyA").addEventListener("click", () => post({ type: "keyframe", leg: "a" }));
  $("keyB").addEventListener("click", () => post({ type: "keyframe", leg: "b" }));
  for (const s of document.querySelectorAll(".screen")) s.addEventListener("click", (e) => toggleZoom(e, s));
  setAspect();
}

function setAspect() {
  const [w, h] = $("size").value.split("x").map(Number);
  document.documentElement.style.setProperty("--aspect", `${w} / ${h}`);
}

// Clicking a picture zooms all three to 3x around the same spot, pixels unsmoothed, to compare
// them closely; clicking again zooms out.
let zoomed = false;
function toggleZoom(e, screen) {
  zoomed = !zoomed;
  const r = screen.getBoundingClientRect();
  const ox = ((e.clientX - r.left) / r.width) * 100, oy = ((e.clientY - r.top) / r.height) * 100;
  for (const s of document.querySelectorAll(".screen")) {
    s.classList.toggle("zoomed", zoomed);
    for (const m of s.querySelectorAll("video, canvas.picture")) m.style.transformOrigin = `${ox}% ${oy}%`;
  }
}

// ---------------------------------------------------------------------------------------------
// The loop

let running = false;
let cfg = null;
let source = null;      // { stream, drawnAt?, stop }
let pcA = null, pcB = null;
let outStream = null;
let startedAt = 0;
let restartTimer = 0;
// Page B's per-frame callback. A pending one outlives a stop and would fire again on the next
// run's frames, alongside that run's own: each restart would add another read of every frame.
let outFrameCallback = 0;

async function start() {
  clearTimeout(restartTimer);
  source?.stop();   // never two cards drawing at once
  $("errorBar").hidden = true;
  cfg = readConfig();
  running = true;
  startedAt = performance.now();
  resetStats();
  setRunningUi();
  try {
    if (cfg.source === "card") {
      source = startCard(cfg.width, cfg.height, cfg.fps);
    } else {
      const stream = await navigator.mediaDevices.getUserMedia({
        // The fake camera runs at 30 fps (MainForm sets it); max brings it down for 15.
        video: { width: { exact: cfg.width }, height: { exact: cfg.height }, frameRate: { ideal: cfg.fps, max: cfg.fps } },
        audio: true,
      });
      source = { stream, drawnAt: () => undefined, stop: () => stream.getTracks().forEach((t) => t.stop()) };
    }
    // "detail" makes the browser keep the full size from the first frame (its default for
    // camera-like content is to start smaller and step up, and a size change isn't what's being
    // tested). setParameters below says the same once leg A is up.
    for (const t of source.stream.getVideoTracks()) t.contentHint = "detail";
    $("srcVideo").srcObject = source.stream;
    log(`start: ${cfg.source} ${cfg.width}x${cfg.height} at ${cfg.fps} fps, ${cfg.mode === "relay" ? `relay ${cfg.codecIn}` : `${cfg.codecIn} (${cfg.decoder}) -> ${cfg.codecOut} (${cfg.encoder})`}`);
    post({ type: "start", config: cfg });
  } catch (err) {
    showError(`Couldn't start the source: ${err.message}`);
    stop();
  }
}

function stop() {
  clearTimeout(restartTimer);
  running = false;
  post({ type: "stop" });
  pcA?.close();
  pcB?.close();
  pcA = pcB = null;
  source?.stop();
  source = null;
  $("srcVideo").srcObject = null;
  $("outVideo").cancelVideoFrameCallback(outFrameCallback);
  stopFrameReader();
  $("outVideo").srcObject = null;
  setLegState("a", "idle");
  setLegState("b", "idle");
  setRunningUi();
}

// Several changes in quick succession (arrowing through a picker) make one restart, not several.
function restart() {
  stop();
  restartTimer = setTimeout(start, 200);
}

function setRunningUi() {
  $("startBtn").textContent = running ? "Stop" : "Start";
  $("startBtn").classList.toggle("running", running);
  document.body.classList.toggle("running", running);
  updateDecodedNote();
}

function iceGathered(pc) {
  return new Promise((resolve) => {
    if (pc.iceGatheringState === "complete") return resolve();
    pc.addEventListener("icegatheringstatechange", () => pc.iceGatheringState === "complete" && resolve());
    setTimeout(resolve, 2000);
  });
}

async function onOffers(m) {
  if (!running || !source) return;
  // Leg A: page A sends. SIPSorcery's offer is recvonly for both kinds and has one video codec.
  pcA = new RTCPeerConnection();
  pcA.onconnectionstatechange = () => setLegState("a", pcA?.connectionState ?? "closed");
  await pcA.setRemoteDescription({ type: "offer", sdp: withStartBitrate(m.a, cfg.browserKbps) });
  for (const tr of pcA.getTransceivers()) {
    const kind = tr.receiver.track.kind;
    const track = source.stream.getTracks().find((t) => t.kind === kind);
    tr.direction = "sendonly";
    if (track) await tr.sender.replaceTrack(track);
  }
  await pcA.setLocalDescription(await pcA.createAnswer());
  await iceGathered(pcA);
  post({ type: "answer", leg: "a", sdp: pcA.localDescription.sdp });
  const video = pcA.getSenders().find((s) => s.track?.kind === "video");
  pcA.addEventListener("connectionstatechange", async () => {
    if (pcA?.connectionState !== "connected" || !video) return;
    // The browser only sees a local link, so cap it; and keep the size (vp8.net encodes
    // multiples of 16 only, and a size change mid-test would hide what's being looked at).
    // No maxFramerate: its limiter drops any card frame whose timer fired a little early.
    const p = video.getParameters();
    p.encodings = p.encodings?.length ? p.encodings : [{}];
    p.encodings[0].maxBitrate = cfg.browserKbps * 1000;
    p.degradationPreference = "maintain-resolution";
    await video.setParameters(p).catch((err) => log(`setParameters: ${err.message}`));
  });

  // Leg B: page B receives what SIPSorcery sends.
  pcB = new RTCPeerConnection();
  outStream = new MediaStream();
  pcB.ontrack = (ev) => {
    outStream.addTrack(ev.track);
    if (ev.track.kind === "audio") watchLevel(ev.track);
    else readDecodedFrames(ev.track);
  };
  pcB.onconnectionstatechange = () => setLegState("b", pcB?.connectionState ?? "closed");
  await pcB.setRemoteDescription({ type: "offer", sdp: m.b });
  for (const tr of pcB.getTransceivers()) tr.direction = "recvonly";
  await pcB.setLocalDescription(await pcB.createAnswer());
  await iceGathered(pcB);
  post({ type: "answer", leg: "b", sdp: pcB.localDescription.sdp });
  const out = $("outVideo");
  out.srcObject = outStream;
  out.muted = !$("listen").checked;
  out.play().catch(() => {});
  outFrameCallback = out.requestVideoFrameCallback(onOutputFrame);
}

// SIPSorcery sends no bandwidth estimate (no REMB or transport-cc feedback), so the browser
// would stay near its 300 kbit/s starting rate for the whole run and feed SIPSorcery's decoder a
// starved picture. Chrome reads x-google-start-bitrate and x-google-max-bitrate from the codec's
// fmtp in the remote description, so leg A's offer gets them, set to the Browser bitrate picked.
function withStartBitrate(sdp, kbps) {
  const lines = sdp.split("\r\n");
  const video = lines.findIndex((l) => l.startsWith("m=video"));
  if (video < 0) return sdp;
  const pt = lines[video].split(" ")[3];
  const extra = `x-google-start-bitrate=${kbps};x-google-max-bitrate=${kbps}`;
  const fmtp = lines.findIndex((l, i) => i > video && l.startsWith(`a=fmtp:${pt} `));
  if (fmtp >= 0) lines[fmtp] += `;${extra}`;
  else {
    const rtpmap = lines.findIndex((l, i) => i > video && l.startsWith(`a=rtpmap:${pt} `));
    lines.splice(rtpmap + 1, 0, `a=fmtp:${pt} ${extra}`);
  }
  return lines.join("\r\n");
}

function setLegState(leg, state) {
  const chip = $(leg === "a" ? "chipA" : "chipB");
  chip.dataset.state = state;
  chip.querySelector("b").textContent = state;
  $(leg === "a" ? "hopA" : "hopB").dataset.state = state;
}

// ---------------------------------------------------------------------------------------------
// Page B. Every frame the browser decodes is read (MediaStreamTrackProcessor, in a worker:
// framereader.js) for its number: frames missing, repeated or too damaged to read. Separately, each
// frame the <video> shows is timed for latency, matched to its number by RTP timestamp, so the
// main thread reads no pixels. The two differ: a 59 Hz display shows a 30 fps stream with a frame
// left out every couple of seconds, which is the display's cadence, not a fault in the stream.

const out = { decoded: 0, notRead: 0, number: -1, gaps: 0, repeats: 0, unreadable: 0, presented: 0, latency: [], width: 0, height: 0 };
let outShownTimes = [];
let notReadSince = 0;   // frames not read since the last number read
let frameReader = null;
// RTP timestamp -> frame number for decoded frames not yet shown, and -> display time for shown
// frames not yet read (the <video> can show a frame before the worker has read it).
const numberByRtp = new Map();
const shownByRtp = new Map();

function readDecodedFrames(track) {
  stopFrameReader();
  if (!("MediaStreamTrackProcessor" in window)) { log("no MediaStreamTrackProcessor: page B's frame numbers aren't read"); return; }
  const worker = new Worker("framereader.js", { type: "module" });
  worker.onmessage = ({ data }) => { if (worker === frameReader && running) onDecodedFrame(data); };
  worker.onerror = (e) => log(`page B's frame reader: ${e.message}`);
  worker.postMessage({ card: cfg?.source === "card" });
  frameReader = worker;
  // Frames are taken here, as soon as they come, and handed over one by one. (Handing the worker
  // the stream itself lost about 1% of H264 frames, none of VP8's: a transferred stream pulls one
  // frame at a time, waiting on the worker, and the processor drops frames that come meanwhile.)
  const reader = new MediaStreamTrackProcessor({ track }).readable.getReader();
  (async () => {
    for (;;) {
      const { value: frame, done } = await reader.read();
      if (done) break;
      if (worker !== frameReader) { frame.close(); break; }
      worker.postMessage({ frame }, [frame]);
    }
    reader.cancel().catch(() => {});
  })();
}

function stopFrameReader() {
  frameReader?.terminate();
  frameReader = null;
  numberByRtp.clear();
  shownByRtp.clear();
}

function onDecodedFrame({ n, rtp, error, skipped }) {
  out.decoded++;
  if (skipped) {
    out.notRead++;
    notReadSince++;
    return;
  }
  if (error) log(`reading page B's frame: ${error}`);
  if (n == null) return;
  onOutputNumber(n);
  if (n < 0 || rtp == null) return;
  const shown = shownByRtp.get(rtp);
  if (shown != null) {
    shownByRtp.delete(rtp);
    addLatency(n, shown);
  } else {
    remember(numberByRtp, rtp, n);
  }
}

function remember(map, key, value) {
  map.set(key, value);
  if (map.size > 120) map.delete(map.keys().next().value);
}

function addLatency(n, displayTime) {
  const drawn = source?.drawnAt?.(n);
  if (drawn == null) return;
  out.latency.push(displayTime - drawn);
  if (out.latency.length > 120) out.latency.shift();
}

function onOutputNumber(n) {
  if (n < 0) {
    out.unreadable++;
    addEvent("out", "unread");
    return;
  }
  // Frames the reader had no room for were decoded, so they're not missing.
  const missing = out.number >= 0 ? n - out.number - 1 - notReadSince : 0;
  if (missing > 0) {
    out.gaps += missing;
    addEvent("out", "gap", missing);
  } else if (out.number >= 0 && n <= out.number) {
    out.repeats++;
    addEvent("out", "repeat");
  }
  out.number = n;
  notReadSince = 0;
}

function onOutputFrame(now, meta) {
  const video = $("outVideo");
  if (!running || !video.srcObject) return;
  // Frames presented, not callbacks: two frames in one display refresh get one callback.
  for (let i = out.presented ? meta.presentedFrames - out.presented : 1; i > 0; i--) outShownTimes.push(now);
  out.presented = meta.presentedFrames;
  out.width = meta.width;
  out.height = meta.height;
  const rtp = meta.rtpTimestamp;
  if (cfg?.source === "card" && rtp != null) {
    const n = numberByRtp.get(rtp);
    if (n != null) {
      numberByRtp.delete(rtp);
      addLatency(n, meta.expectedDisplayTime);
    } else {
      remember(shownByRtp, rtp, meta.expectedDisplayTime);
    }
  }
  outFrameCallback = video.requestVideoFrameCallback(onOutputFrame);
}

// ---------------------------------------------------------------------------------------------
// The middle tile: frames SIPSorcery decoded, from the shared buffer, drawn with WebGL.

const dec = { buffer: null, view: null, header: 0, slotBytes: 0, seq: 0, shown: 0, shownTimes: [], number: -1 };
const gl = $("decCanvas").getContext("webgl2", { antialias: false, preserveDrawingBuffer: true });
let texture = null, texW = 0, texH = 0;

function initGl() {
  const vs = `#version 300 es
    in vec2 p; out vec2 uv;
    void main() { uv = vec2((p.x + 1.0) / 2.0, (1.0 - p.y) / 2.0); gl_Position = vec4(p, 0.0, 1.0); }`;
  const fs = `#version 300 es
    precision mediump float; in vec2 uv; uniform sampler2D t; out vec4 c;
    void main() { vec3 bgr = texture(t, uv).rgb; c = vec4(bgr.b, bgr.g, bgr.r, 1.0); }`;
  const prog = gl.createProgram();
  for (const [type, src] of [[gl.VERTEX_SHADER, vs], [gl.FRAGMENT_SHADER, fs]]) {
    const s = gl.createShader(type);
    gl.shaderSource(s, src);
    gl.compileShader(s);
    gl.attachShader(prog, s);
  }
  gl.linkProgram(prog);
  gl.useProgram(prog);
  const buf = gl.createBuffer();
  gl.bindBuffer(gl.ARRAY_BUFFER, buf);
  gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([-1, -1, 1, -1, -1, 1, 1, 1]), gl.STATIC_DRAW);
  const loc = gl.getAttribLocation(prog, "p");
  gl.enableVertexAttribArray(loc);
  gl.vertexAttribPointer(loc, 2, gl.FLOAT, false, 0, 0);
  texture = gl.createTexture();
  gl.bindTexture(gl.TEXTURE_2D, texture);
  for (const [k, v] of [[gl.TEXTURE_MIN_FILTER, gl.NEAREST], [gl.TEXTURE_MAG_FILTER, gl.NEAREST], [gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE], [gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE]]) gl.texParameteri(gl.TEXTURE_2D, k, v);
  gl.pixelStorei(gl.UNPACK_ALIGNMENT, 1);
}

// A new buffer comes with each stream size that needs one; the old one is let go.
window.chrome?.webview?.addEventListener("sharedbufferreceived", (e) => {
  const meta = e.additionalData;
  if (dec.buffer) { try { chrome.webview.releaseBuffer(dec.buffer); } catch { } }
  dec.buffer = e.getBuffer();
  dec.view = new DataView(dec.buffer);
  dec.header = meta.header;
  dec.slotBytes = meta.slotBytes;
  dec.seq = 0;
});

function drawDecoded(now) {
  if (!dec.view || !running || !$("showDecoded").checked || cfg?.mode === "relay") return;
  try {
    drawDecodedFrame();
  } catch (err) {
    // The host closed this buffer for a new one that's on its way.
    dec.view = null;
  }
}

function drawDecodedFrame() {
  const seq = dec.view.getInt32(0, true);
  if (seq === dec.seq) return;
  const slot = dec.view.getInt32(4, true);
  const w = dec.view.getInt32(16 + slot * 12, true), h = dec.view.getInt32(20 + slot * 12, true);
  dec.number = dec.view.getInt32(24 + slot * 12, true);
  if (w <= 0 || h <= 0) return;
  const canvas = $("decCanvas");
  if (canvas.width !== w || canvas.height !== h) { canvas.width = w; canvas.height = h; }
  gl.viewport(0, 0, w, h);
  gl.bindTexture(gl.TEXTURE_2D, texture);
  const pixels = new Uint8Array(dec.buffer, dec.header + slot * dec.slotBytes, w * h * 3);
  if (w !== texW || h !== texH) {
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGB8, w, h, 0, gl.RGB, gl.UNSIGNED_BYTE, pixels);
    texW = w;
    texH = h;
  } else {
    gl.texSubImage2D(gl.TEXTURE_2D, 0, 0, 0, w, h, gl.RGB, gl.UNSIGNED_BYTE, pixels);
  }
  gl.drawArrays(gl.TRIANGLE_STRIP, 0, 4);
  dec.shown++;
  dec.seq = seq;
}

function updateDecodedNote() {
  const note = $("decNote");
  const relay = (running ? cfg?.mode : $("mode").value) === "relay";
  note.hidden = !(relay || !$("showDecoded").checked);
  note.textContent = relay ? "Relay: SIPSorcery forwards frames without decoding them, so there's nothing to show here."
    : isBig() ? "Off above 1080p: copying and drawing each frame here would slow SIPSorcery's decode thread and the test card. View ▸ Decoded tile turns it on."
    : "Turned off (View ▸ Decoded tile), so it costs nothing while benchmarking.";
  $("decCanvas").style.visibility = note.hidden ? "visible" : "hidden";
}

// ---------------------------------------------------------------------------------------------
// Audio level of what comes back.

let analyser = null, levelData = null;
function watchLevel(track) {
  const ctx = new AudioContext();
  analyser = ctx.createAnalyser();
  analyser.fftSize = 1024;
  levelData = new Float32Array(analyser.fftSize);
  ctx.createMediaStreamSource(new MediaStream([track])).connect(analyser);
}
function drawLevel() {
  let peak = 0;
  if (analyser && running) {
    analyser.getFloatTimeDomainData(levelData);
    for (const v of levelData) peak = Math.max(peak, Math.abs(v));
  }
  $("levelBar").style.width = `${Math.min(100, peak * 200)}%`;
}

// ---------------------------------------------------------------------------------------------
// Events, for the strips under each tile.

const WINDOW_MS = 30000;
const events = { src: [], dec: [], out: [] };
// lane 0: keyframes, 1: keyframe requests and NACKs, 2: trouble.
const KINDS = {
  key: { lane: 0, colour: "--ok", label: "keyframe" },
  "key-out": { lane: 0, colour: "--key-out", label: "keyframe out" },
  pli: { lane: 1, colour: "--warn", label: "keyframe request" },
  nack: { lane: 1, colour: "--nack", label: "NACK" },
  loss: { lane: 2, colour: "--muted", label: "packets lost" },
  drop: { lane: 2, colour: "--muted", label: "frame dropped" },
  freeze: { lane: 2, colour: "--bad", label: "freeze" },
  error: { lane: 2, colour: "--bad", label: "error" },
  overrun: { lane: 2, colour: "--bad", label: "decoder fell behind" },
  gap: { lane: 2, colour: "--gap", label: "frames missing" },
  repeat: { lane: 2, colour: "--gap", label: "frame repeated" },
  unread: { lane: 2, colour: "--unread", label: "frame unreadable" },
  limit: { lane: 2, colour: "--muted", label: "browser limited" },
  state: { lane: 2, colour: "--muted", label: "connection" },
};

function addEvent(tile, kind, n = 0, note = "", t = performance.now(), span = 0) {
  events[tile].push({ t, kind, n, note, span });
}

// The host's events, onto the tiles they belong to.
const HOST_EVENTS = {
  "a-key": ["dec", "key", "keyframe in from page A"],
  "a-pli": ["dec", "pli", "asked page A for a keyframe"],
  "a-drop": ["dec", "drop", "frame dropped on leg A (simulated)"],
  "dec-err": ["dec", "error", "decode failed"],
  "enc-err": ["dec", "error", "encode failed"],
  overrun: ["dec", "overrun", "decoder fell behind; frames dropped"],
  gap: ["dec", "gap", "frame numbers missing before decode"],
  repeat: ["dec", "repeat", "frame number repeated"],
  unread: ["dec", "unread", "decoded frame unreadable"],
  "b-key": ["dec", "key-out", "keyframe encoded for page B"],
  "b-pli": ["dec", "pli", "page B asked for a keyframe"],
  "b-force": ["dec", "pli", "keyframe forced by hand"],
  "b-nack": ["out", "nack", "NACKs from page B"],
  "b-loss": ["out", "loss", "packets dropped on leg B (simulated)"],
  "a-state": ["src", "state", "leg A"],
  "b-state": ["out", "state", "leg B"],
};

function onHostEvents(list) {
  for (const e of list) {
    const map = HOST_EVENTS[e.k];
    if (!map) continue;
    const t = toPage(e.t) ?? performance.now();
    addEvent(map[0], map[1], e.n, e.note ? `${map[2]} (${e.note})` : map[2], t);
  }
}

function drawStrips(now) {
  const css = getComputedStyle(document.documentElement);
  for (const [tile, id] of [["src", "stripSource"], ["dec", "stripDecoded"], ["out", "stripOutput"]]) {
    const canvas = $(id);
    const dpr = devicePixelRatio;
    const W = Math.round(canvas.clientWidth * dpr), H = Math.round(canvas.clientHeight * dpr);
    if (canvas.width !== W || canvas.height !== H) { canvas.width = W; canvas.height = H; }
    const g = canvas.getContext("2d");
    g.clearRect(0, 0, W, H);
    const laneH = H / 3;
    g.fillStyle = css.getPropertyValue("--strip-lane");
    for (let l = 0; l < 3; l++) g.fillRect(0, l * laneH + 1, W, laneH - 2);
    // A tick every 5 s.
    g.fillStyle = css.getPropertyValue("--strip-tick");
    for (let s = 0; s <= WINDOW_MS; s += 5000) {
      const x = W - ((now % 5000) + s) / WINDOW_MS * W;
      if (x >= 0) g.fillRect(Math.round(x), 0, 1, H);
    }
    const list = events[tile];
    while (list.length && list[0].t < now - WINDOW_MS - 1000) list.shift();
    for (const e of list) {
      const k = KINDS[e.kind];
      const x = W - ((now - e.t) / WINDOW_MS) * W;
      const w = Math.max(2 * dpr, (e.span / WINDOW_MS) * W);
      g.fillStyle = css.getPropertyValue(k.colour);
      const y = k.lane * laneH + 2, h = laneH - 4;
      if (e.kind === "key-out") g.fillRect(x - w, y + h / 2, w, h / 2);
      else if (e.kind === "key" && tile === "dec") g.fillRect(x - w, y, w, h / 2);
      else g.fillRect(x - w, y, w, h);
    }
  }
}

// Hovering a strip names the events under the pointer.
for (const [tile, id] of [["src", "stripSource"], ["dec", "stripDecoded"], ["out", "stripOutput"]]) {
  const canvas = $(id);
  canvas.addEventListener("mousemove", (e) => {
    const r = canvas.getBoundingClientRect();
    const t = performance.now() - ((r.right - e.clientX) / r.width) * WINDOW_MS;
    const near = events[tile].filter((ev) => Math.abs(ev.t - t) < 350 || (ev.span && t <= ev.t && t >= ev.t - ev.span));
    canvas.title = near.length
      ? near.slice(-6).map((ev) => `${((performance.now() - ev.t) / 1000).toFixed(1)} s ago: ${ev.note || KINDS[ev.kind].label}${ev.n > 1 ? ` ×${ev.n}` : ""}`).join("\n")
      : "";
  });
}

// ---------------------------------------------------------------------------------------------
// Stats: the browser's (getStats on both legs), the host's (posted 4 times a second), and what
// was read off the frames. Shown on the overlays, the hops between tiles and the verdict.

let host = null, prevHost = null;
let srcStats = {}, outStats = {};
let prevSrc = null, prevOut = null;
let freezeTimes = [];

function resetStats() {
  host = prevHost = null;
  hostHistory = [];
  decFps = decKbpsIn = encKbpsOut = srcBaseline = null;
  srcStats = {};
  outStats = {};
  prevSrc = prevOut = null;
  Object.assign(out, { decoded: 0, notRead: 0, number: -1, gaps: 0, repeats: 0, unreadable: 0, presented: 0, latency: [], width: 0, height: 0 });
  notReadSince = 0;
  outShownTimes = [];
  Object.assign(dec, { seq: dec.view ? dec.view.getInt32(0, true) : 0, shown: 0, shownTimes: [], number: -1 });
  freezeTimes = [];
  for (const k of Object.keys(events)) events[k] = [];
}

function find(report, type, kind) {
  for (const s of report.values()) if (s.type === type && s.kind === kind) return s;
  return null;
}

async function pollBrowserStats() {
  if (!running) return;
  const now = performance.now();
  if (pcA) {
    const r = await pcA.getStats();
    const o = find(r, "outbound-rtp", "video");
    if (o) {
      const codec = r.get(o.codecId);
      const media = o.mediaSourceId ? r.get(o.mediaSourceId) : null;
      const p = prevSrc;
      srcStats = {
        framesIn: media?.frames ?? null, sourceFps: media?.framesPerSecond ?? null, cardDrawMs: source?.drawMs ?? null,
        width: o.frameWidth, height: o.frameHeight, fps: o.framesPerSecond,
        kbps: p ? ((o.bytesSent - p.bytesSent) * 8) / (now - p.at) : null,
        codec: codec?.mimeType?.split("/")[1], impl: o.encoderImplementation,
        keyframes: o.keyFramesEncoded, pli: o.pliCount, fir: o.firCount, nack: o.nackCount,
        limit: o.qualityLimitationReason, encodeMs: o.framesEncoded ? (o.totalEncodeTime * 1000) / o.framesEncoded : null,
        framesEncoded: o.framesEncoded,
      };
      if (p) {
        if (o.keyFramesEncoded > p.keyFramesEncoded) addEvent("src", "key", o.keyFramesEncoded - p.keyFramesEncoded, "keyframe encoded");
        if (o.pliCount + o.firCount > p.pliCount + p.firCount) addEvent("src", "pli", o.pliCount + o.firCount - p.pliCount - p.firCount, "keyframe request received");
        if (o.qualityLimitationReason !== p.qualityLimitationReason && o.qualityLimitationReason !== "none") addEvent("src", "limit", 0, `limited by ${o.qualityLimitationReason}`);
      }
      prevSrc = { ...o, at: now };
    }
  }
  if (pcB) {
    const r = await pcB.getStats();
    const i = find(r, "inbound-rtp", "video");
    if (i) {
      const codec = r.get(i.codecId);
      const p = prevOut;
      outStats = {
        width: i.frameWidth, height: i.frameHeight, fps: i.framesPerSecond,
        kbps: p ? ((i.bytesReceived - p.bytesReceived) * 8) / (now - p.at) : null,
        codec: codec?.mimeType?.split("/")[1], impl: i.decoderImplementation,
        keyframes: i.keyFramesDecoded, pli: i.pliCount, fir: i.firCount, nack: i.nackCount,
        freezes: i.freezeCount, freezeSecs: i.totalFreezesDuration, dropped: i.framesDropped,
        lost: i.packetsLost, framesDecoded: i.framesDecoded,
        jitterMs: i.jitterBufferEmittedCount ? (i.jitterBufferDelay / i.jitterBufferEmittedCount) * 1000 : null,
      };
      if (p) {
        if (i.keyFramesDecoded > p.keyFramesDecoded) addEvent("out", "key", i.keyFramesDecoded - p.keyFramesDecoded, "keyframe decoded");
        if (i.pliCount + i.firCount > p.pliCount + p.firCount) addEvent("out", "pli", i.pliCount + i.firCount - p.pliCount - p.firCount, "asked SIPSorcery for a keyframe");
        if (i.nackCount > p.nackCount) addEvent("out", "nack", i.nackCount - p.nackCount, "NACKs sent");
        if (i.freezeCount > p.freezeCount) {
          const span = (i.totalFreezesDuration - p.totalFreezesDuration) * 1000;
          addEvent("out", "freeze", i.freezeCount - p.freezeCount, `froze for ${span.toFixed(0)} ms`, now, span);
          freezeTimes.push(now);
        }
        if (i.framesDropped > p.framesDropped) addEvent("out", "drop", i.framesDropped - p.framesDropped, "frames dropped by the browser");
      }
      prevOut = { ...i, at: now };
    }
  }
  render();
}

// The host's stats from about 2 s ago, for rates that don't jump about with each 250 ms tick.
let hostHistory = [];

function onHostStats(s) {
  host = { ...s, at: performance.now() };
  hostHistory.push(host);
  while (hostHistory.length > 1 && hostHistory[0].now < s.now - 2000) hostHistory.shift();
  prevHost = hostHistory[0] === host ? null : hostHistory[0];
  onHostEvents(s.events);
  render();
}

// Frames a second over the last 2 s.
const rate = (times, now) => {
  while (times.length && times[0] < now - 2000) times.shift();
  const span = Math.min(2000, now - startedAt);
  return span > 250 ? (times.length * 1000) / span : null;
};
const pct = (arr, p) => {
  if (!arr.length) return null;
  const s = [...arr].sort((a, b) => a - b);
  return s[Math.min(s.length - 1, Math.floor(p * s.length))];
};
const f0 = (v) => (v == null || Number.isNaN(v) ? "–" : Math.round(v).toString());
const f1 = (v) => (v == null || Number.isNaN(v) ? "–" : v.toFixed(1));
const mbps = (kbps) => (kbps == null ? "–" : kbps >= 1000 ? `${(kbps / 1000).toFixed(2)} Mbit/s` : `${Math.round(kbps)} kbit/s`);
const size = (w, h) => (w ? `${w}×${h}` : "–");
const ms = (t) => (t ? `${f1(t.p50)} ms <i>p95 ${f1(t.p95)}</i>` : "–");

function hostRate(key, sub) {
  if (!host || !prevHost || !host[key] || !prevHost[key]) return null;
  return ((host[key][sub] - prevHost[key][sub]) * 1000) / (host.now - prevHost.now);
}

let decFps = null, decKbpsIn = null, encKbpsOut = null, srcBaseline = null;

function render() {
  const now = performance.now();
  const s = srcStats, o = outStats;
  const relay = cfg?.mode === "relay";

  // Source.
  // Frames the browser took from the source but didn't encode (its rate control drops some),
  // counted from the first stats once leg A was up (frames before that were never going out).
  if (s.framesIn != null && s.framesEncoded > 0 && srcBaseline == null) srcBaseline = s.framesIn - s.framesEncoded;
  const notEncoded = srcBaseline != null ? Math.max(0, s.framesIn - s.framesEncoded - srcBaseline) : null;
  $("ovSource").innerHTML = running ? [
    `<b>${size(s.width, s.height)}</b> · ${f1(s.fps)} fps sent` + (short(s.sourceFps) ? ` · <em>source gives ${f1(s.sourceFps)} of ${cfg.fps} fps</em>` : ""),
    [s.codec ?? "–", s.impl, mbps(s.kbps)].filter(Boolean).join(" · "),
    `encode ${f1(s.encodeMs)} ms · keyframes ${s.keyframes ?? "–"}${notEncoded != null ? ` · ${bad(notEncoded, "not encoded")}` : ""}`,
    `keyframe requests in ${s.pli != null ? s.pli + (s.fir ?? 0) : "–"}${s.limit && s.limit !== "none" ? ` · <em>limited by ${s.limit}</em>` : ""}`,
  ].join("<br>") : "";

  // SIPSorcery.
  if (host?.a && prevHost?.a) {
    decKbpsIn = ((host.a.bytesIn - prevHost.a.bytesIn) * 8) / (host.now - prevHost.now);
    if (host.dec && prevHost.dec) decFps = hostRate("dec", "decoded");
    if (host.enc && prevHost.enc) encKbpsOut = ((host.enc.bytesOut - prevHost.enc.bytesOut) * 8) / (host.now - prevHost.now);
  }
  const h = host;
  let decLines = [];
  if (running && h) {
    const drawnAt = h.dec?.number >= 0 && source?.drawnAt ? source.drawnAt(h.dec.number) : undefined;
    const decodedAt = h.dec?.numberAt ? toPage(h.dec.numberAt) : null;
    const sinceDrawn = drawnAt != null && decodedAt != null ? decodedAt - drawnAt : null;
    if (relay) {
      decLines = [
        `<b>relay</b> · frames in ${h.a.framesIn} · out ${h.b.framesOut}`,
        `keyframes in ${h.a.keyIn} · requests to page A ${h.a.pli}`,
        `requests from page B ${h.b.pli + h.b.fir}`,
      ];
    } else {
      decLines = [
        `<b>${size(h.dec.width, h.dec.height)}</b> · ${f1(decFps)} fps decoded${h.dec.number >= 0 ? ` · #${h.dec.number}` : ""}`,
        `decode ${ms(h.dec.ms)} · queue ${h.dec.queue}`,
        `encode ${ms(h.enc.ms)} · queue ${h.enc.queue} · ${bad(h.enc.skipped, "skipped")}${h.enc.cropped ? ` · <em>cropped to ${h.enc.cropped}</em>` : ""}`,
        `drawn → decoded ${f0(sinceDrawn)} ms · in SIPSorcery ${h.inChain ? f1(h.inChain.p50) : "–"} ms`,
        `keyframes in ${h.a.keyIn} / out ${h.enc.keyOut} · requests to A ${h.a.pli} · from B ${h.b.pli + h.b.fir}`,
        `${bad(h.dec.errors + h.enc.errors, "errors")} · ${bad(h.dec.gaps, "missing")} · ${bad(h.dec.unreadable, "unreadable")}${h.dec.overruns ? ` · ${bad(h.dec.overruns, "overruns")}` : ""}`,
      ];
      if (h.a.dropped) decLines.push(`dropped on leg A ${h.a.dropped} (simulated)`);
    }
    if (h.a.rtpLost || h.a.reordered) decLines.push(`leg A packets ${bad(h.a.rtpLost, "lost")} · reordered ${h.a.reordered}`);
    if (prevHost?.gc && h.gc) {
      const paused = ((h.gc.pauseMs - prevHost.gc.pauseMs) * 1000) / (h.now - prevHost.now);
      decLines.push(`.NET GC paused ${f1(paused)} ms/s · gen2 collections ${h.gc.gen2}`);
    }
  }
  $("ovDecoded").innerHTML = decLines.join("<br>");

  // Output.
  const lat = out.latency;
  const outFps = rate(outShownTimes, now);
  $("ovOutput").innerHTML = running ? [
    `<b>${size(o.width, o.height)}</b> · ${f1(o.fps)} fps decoded · ${f1(outFps)} shown${out.number >= 0 ? ` · #${out.number}` : ""}`,
    [o.codec ?? "–", o.impl, mbps(o.kbps)].filter(Boolean).join(" · "),
    `drawn → shown ${f0(pct(lat, 0.5))} ms <i>p95 ${f0(pct(lat, 0.95))}</i> · jitter buffer ${f0(o.jitterMs)} ms`,
    `keyframes ${o.keyframes ?? "–"} · requests ${o.pli != null ? o.pli + (o.fir ?? 0) : "–"} · NACKs ${o.nack ?? "–"}`,
    `${bad(o.freezes, "freezes")} · ${bad(o.dropped, "dropped")} · ${bad(out.gaps, "missing")} · ${bad(out.unreadable, "unreadable")}` +
      (out.notRead ? ` · <span title="Frames page B decoded whose numbers this page had no time to read. Not counted as missing.">not read ${out.notRead}</span>` : ""),
    h?.b?.lossDropped || h?.b?.resent
      ? `lost on leg B ${h.b.lossDropped} packets (simulated) · SIPSorcery resent ${h.b.resent}` +
        (h.b.notKept ? ` · ${bad(h.b.notKept, "not kept")}` : "") +
        (h.b.overBudget ? ` · ${bad(h.b.overBudget, "over budget")}` : "")
      : "",
    h?.b && cfg && running && h.b.lossSimulated === false && Number($("lossB")?.value) > 0
      ? `<em>loss relay not in place: no loss simulated</em>` : "",
  ].filter(Boolean).join("<br>") : "";

  // The hops.
  $("hopACodec").textContent = cfg ? cfg.codecIn : "–";
  $("hopARate").textContent = running ? mbps(decKbpsIn ?? s.kbps) : "";
  $("hopBCodec").textContent = cfg ? cfg.codecOut : "–";
  $("hopBRate").textContent = running ? mbps(relay ? o.kbps : encKbpsOut ?? o.kbps) : "";
  $("hopAImpl").textContent = cfg && !relay ? `decode: ${cfg.decoder}` : relay ? "no decode" : "";
  $("hopBImpl").textContent = cfg && !relay ? `encode: ${cfg.encoder}` : relay ? "no encode" : "";

  // Judged on frames page B decoded, not shown: the display drops frames to fit its refresh, and
  // that isn't SIPSorcery's doing (missing frames are counted from decoded frames too).
  renderVerdict(o.fps ?? outFps);
}

const bad = (n, label) => (n ? `<em>${label} ${n}</em>` : `${label} 0`);
/** A measured frame rate well under the one picked. */
const short = (fps) => fps != null && cfg != null && fps < cfg.fps * 0.9;

function renderVerdict(outFps) {
  const chip = $("chipVerdict");
  if (!running || !cfg) { chip.textContent = "Not running"; chip.dataset.level = ""; return; }
  const now = performance.now();
  if (now - startedAt < 4000 || !outShownTimes.length) { chip.textContent = "Starting…"; chip.dataset.level = ""; return; }
  freezeTimes = freezeTimes.filter((t) => t > now - 10000);
  const problems = [];
  if (outStats.width && (outStats.width !== cfg.width || outStats.height !== cfg.height)) problems.push(`output is ${outStats.width}×${outStats.height}, not ${cfg.width}×${cfg.height}`);
  // Against what page A sends, not the rate picked: a source or browser encoder that falls short
  // isn't SIPSorcery's doing, and is said separately.
  const sent = srcStats.fps ?? cfg.fps;
  if (outFps < Math.min(sent, cfg.fps) * 0.9) problems.push(`page B decodes ${f1(outFps)} of ${f1(sent)} fps sent`);
  const lat = pct(out.latency, 0.5);
  if (lat > 500) problems.push(`latency ${f0(lat)} ms`);
  if (freezeTimes.length) problems.push(`${freezeTimes.length} freeze${freezeTimes.length > 1 ? "s" : ""} in 10 s`);
  const recentTrouble = events.out.filter((e) => e.t > now - 10000 && (e.kind === "unread" || e.kind === "gap")).reduce((sum, e) => sum + Math.max(1, e.n), 0);
  if (recentTrouble) problems.push(`${recentTrouble} damaged or missing frames in 10 s`);
  chip.dataset.level = problems.length ? "warn" : "ok";
  const note = short(srcStats.fps) ? ` (page A sends ${f1(srcStats.fps)} of ${cfg.fps} fps)` : "";
  chip.textContent = (problems.length ? `Check: ${problems.join(" · ")}` : `Looks right: ${f1(outFps)} fps decoded by page B, no freezes`) + note;
}

function onCpu(m) {
  $("chipCpu").innerHTML = `CPU <b>SIPSorcery ${m.host}</b> · WebView2 ${m.web} <i>(1 core = 100, ${m.cores} logical)</i>`;
  latestCpu = m;
}
let latestCpu = null;

// For --stats: a summary of the page's side, every 2 s.
setInterval(() => {
  if (!running) return;
  post({
    type: "pagestats",
    stats: {
      config: cfg, cpu: latestCpu,
      source: srcStats,
      output: { ...outStats, shownFps: rate(outShownTimes, performance.now()), latencyP50: pct(out.latency, 0.5), latencyP95: pct(out.latency, 0.95), framesRead: out.decoded - out.notRead, notRead: out.notRead, gaps: out.gaps, repeats: out.repeats, unreadable: out.unreadable, lastNumber: out.number },
      decodedShown: dec.shown,
    },
  });
}, 2000);

// ---------------------------------------------------------------------------------------------
// Messages from the host

window.chrome?.webview?.addEventListener("message", async (e) => {
  const m = e.data;
  switch (m.type) {
    case "hello":
      caps = m.caps;
      refreshPickers(true);
      if (m.caps.ffmpegNote) log(`ffmpeg not available: ${m.caps.ffmpegNote}`);
      $("version").textContent = m.sipsorcery ? `SIPSorcery ${m.sipsorcery}` : "";
      syncClock();
      if (params.get("start") === "1" && !running) setTimeout(start, 300);
      break;
    case "clock": onClock(m); break;
    case "offers": onOffers(m).catch((err) => showError(`Answering SIPSorcery's offers failed: ${err.message}`)); break;
    case "state": break;   // the page's own connection states are shown; the host's go in the log
    case "stats": onHostStats(m.stats); break;
    case "cpu": onCpu(m); break;
    case "log": log(m.line, true); break;
    case "error": showError(m.message); if (running) stop(); break;
  }
});

function frame(now) {
  drawDecoded(now);
  drawStrips(now);
  drawLevel();
  requestAnimationFrame(frame);
}

initControls();
initGl();
setInterval(pollBrowserStats, 500);
requestAnimationFrame(frame);
post({ type: "ready" });
