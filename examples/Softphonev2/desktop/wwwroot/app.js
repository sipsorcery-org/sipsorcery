// The call page. The browser engine does all the media: it captures the microphone and camera,
// and plays the far end. The host (C#) handles SIP and relays the media, still encoded, between
// the SIP call and the local RTCPeerConnection created here when a call connects.
//
// What the stage shows during a call, from the UX prototype (prototype/web):
//  - their video, while frames are arriving;
//  - their initial, if video was agreed but nothing is arriving ("Their camera is off" after 2 s);
//  - an audio-only view, if the call has no video at all: two rows of frequency bars, theirs
//    above the centre line and yours below, with notes when audio isn't flowing one way.
// The Audio | Video switch beside Call picks the kind of call. An audio call has no video line;
// "Add video" asks the far end for one (a SIP re-INVITE), and the host then sends a new offer.

const $ = (id) => document.getElementById(id);
const post = (msg) => chrome.webview.postMessage(msg);

const CAMERA_KBPS = 1500;
const NO_FRAMES_MS = 2000;   // how long without video frames before saying their camera is off
const NO_AUDIO_MS = 2000;    // how long without audio packets before saying none is arriving

const ICON = {
  phone: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M22 16.9v3a2 2 0 0 1-2.2 2 19.8 19.8 0 0 1-8.6-3.1 19.5 19.5 0 0 1-6-6A19.8 19.8 0 0 1 2.1 4.2 2 2 0 0 1 4.1 2h3a2 2 0 0 1 2 1.7c.1 1 .4 1.9.7 2.8a2 2 0 0 1-.5 2.1L8 9.9a16 16 0 0 0 6 6l1.3-1.3a2 2 0 0 1 2.1-.4c.9.3 1.8.6 2.8.7a2 2 0 0 1 1.7 2Z"/></svg>',
  video: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="m22 8-6 4 6 4V8Z"/><rect x="2" y="6" width="14" height="12" rx="2"/></svg>',
  videoOff: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="m22 8-6 4 6 4V8Z"/><rect x="2" y="6" width="14" height="12" rx="2"/><path d="M2 2l20 20"/></svg>',
};

let local = null;          // MediaStream from getUserMedia
let pc = null;
let state = { call: "idle" };
let connectedAt = 0;
let timerId = 0;
let call = null;           // media state for the connected call, see onMediaOffer
let answerCamera = null;   // "Answer, camera off" sets this to false for the call being answered
let addingVideo = false;   // we asked the host to add video; cleared by the new offer or a refusal
let videoRefused = false;  // the far end turned video down on this call

// The Audio | Video switch: audio by default (most SIP calls go to phones), then the last choice.
let callType = "audio";
try { callType = localStorage.getItem("callType") === "video" ? "video" : "audio"; } catch { /* no storage */ }
const params = new URLSearchParams(location.search);
if (params.get("call") === "video") callType = "video";          // --video (testing)
const testCameraOff = params.get("camera") === "off";            // --camera-off (testing)
const testAddVideo = Number(params.get("addvideo")) || 0;        // --add-video <s> (testing)

const localTrack = (kind) => local?.getTracks().find((t) => t.kind === kind) ?? null;

function log(text, fromHost = false) {
  if (!fromHost) post({ type: "log", text });
  const el = $("log");
  el.textContent += text + "\n";
  if (el.textContent.length > 60000) el.textContent = el.textContent.slice(-40000);
  el.scrollTop = el.scrollHeight;
}

// "Alice <sip:alice@example.com>" → "Alice"; "sip:bob@host" → "Bob".
function shortName(remote) {
  const m = /^\s*(.+?)\s*</.exec(remote ?? "");
  if (m) return m[1];
  const u = /^(?:sips?:|tel:)?([^@;>]+)/i.exec(remote ?? "");
  const user = u ? u[1] : remote || "Them";
  return /^[+\d*#]+$/.test(user) ? user : user.charAt(0).toUpperCase() + user.slice(1);
}
const initial = (name) => (name ?? "?").replace(/^\+/, "").charAt(0).toUpperCase() || "#";

// The microphone is opened once, at start. The camera is only opened while it's wanted (see
// cameraWanted), so on an audio call, or with the switch on Audio, it's off: no self view, and
// the camera's light stays off.
let hasCamera = false;     // a camera exists (and hasn't failed to open)
let cameraBusy = null;     // the open or close in progress, so they never overlap

async function openDevices() {
  try {
    local = await navigator.mediaDevices.getUserMedia({
      audio: { echoCancellation: true, noiseSuppression: true, autoGainControl: true },
    });
  } catch (err) {
    log(`microphone unavailable: ${err.name} ${err.message}`);
    local = new MediaStream();
  }
  try {
    hasCamera = (await navigator.mediaDevices.enumerateDevices()).some((d) => d.kind === "videoinput");
  } catch { hasCamera = false; }
}

// Whether the camera should be open: during a video call with your camera on, or, before a
// call, while the switch is on Video (a preview of what they'll see).
function cameraWanted() {
  if (!hasCamera) return false;
  if (call) return call.hasVideo && call.camOn;
  // Connected, but the media isn't set up yet: leave the camera as it is, so a video call's
  // preview carries straight on into the call.
  if (state.call === "connected") return !!localTrack("video");
  const before = state.call === "idle" || state.call === "ended" || state.call === "calling";
  return before && state.videoAllowed !== false && callType === "video" && !testCameraOff;
}

// Opens or closes the camera to match cameraWanted(), and puts it on the call when it should be sent.
function syncCamera() {
  cameraBusy = (cameraBusy ?? Promise.resolve()).then(async () => {
    const want = cameraWanted();
    let track = localTrack("video");
    if (want && !track) {
      try {
        const cam = await navigator.mediaDevices.getUserMedia({
          video: { width: { ideal: 1280 }, height: { ideal: 720 }, frameRate: { ideal: 30 } },
        });
        track = cam.getVideoTracks()[0];
        if (!cameraWanted()) { track.stop(); return; }   // changed while it was opening
        local.addTrack(track);
        log("camera opened");
      } catch (err) {
        log(`camera unavailable: ${err.name} ${err.message}`);
        hasCamera = false;
        if (call) call.camOn = false;
      }
    } else if (!want && track) {
      local.removeTrack(track);
      track.stop();
      log("camera closed");
    } else {
      return;
    }
    if (call?.videoSender) await call.videoSender.replaceTrack(call.camOn ? localTrack("video") : null);
    $("self").srcObject = localTrack("video") ? new MediaStream([localTrack("video")]) : null;
    render();
  });
  return cameraBusy;
}

// ---- Media from the host ----

// A new offer arrives when a call connects, and again when video is added to it. Video you
// asked for starts with your camera on; video they added starts with it off, so being sent a
// video request never turns your camera on by itself.
async function onMediaOffer(sdp) {
  const midCall = !!call;
  closeMedia();
  const wantCamera = addingVideo ? true : midCall ? false : (answerCamera ?? !testCameraOff);
  addingVideo = false;
  call = {
    hasVideo: false,             // the call has video at all (an m=video line from the host)
    camOn: wantCamera && hasCamera,
    muted: !localTrack("audio")?.enabled,
    videoSender: null,
    framesDecoded: -1, lastFrameAt: 0,   // when the decoder last produced a frame
    rxPackets: -1, rxChangedAt: performance.now(), rxDead: false,
    txLost: false,               // from the host: their RTCP says none of our audio arrives
    remoteAnalyser: null, localAnalyser: null,
  };
  answerCamera = null;

  pc = new RTCPeerConnection();
  const remote = new MediaStream();
  $("remote").srcObject = remote;
  pc.ontrack = (e) => {
    remote.addTrack(e.track);
    if (e.track.kind === "audio") call.remoteAnalyser = analyserFor(e.track);
  };
  pc.onconnectionstatechange = () => log(`media ${pc?.connectionState}`);

  await pc.setRemoteDescription({ type: "offer", sdp });
  for (const tr of pc.getTransceivers()) {
    const kind = tr.receiver.track.kind;
    if (kind === "video") {
      call.hasVideo = true;
      call.videoSender = tr.sender;
      // Always sendrecv when there's a camera, so it can be turned on later without
      // renegotiating; a camera that's off just sends nothing.
      tr.direction = hasCamera ? "sendrecv" : "recvonly";
    } else {
      const track = localTrack("audio");
      tr.direction = track ? "sendrecv" : "recvonly";
      if (track) await tr.sender.replaceTrack(track);
    }
  }
  // Opens the camera (and puts it on the call) if this is a video call with it on; closes it
  // if this is an audio call. Not awaited: the answer doesn't depend on it.
  syncCamera();
  if (localTrack("audio")) call.localAnalyser = analyserFor(localTrack("audio"));
  await pc.setLocalDescription(await pc.createAnswer());
  await new Promise((resolve) => {
    if (pc.iceGatheringState === "complete") return resolve();
    pc.addEventListener("icegatheringstatechange", () => pc.iceGatheringState === "complete" && resolve());
    setTimeout(resolve, 2000);
  });
  post({ type: "media-answer", sdp: pc.localDescription.sdp });
  log(`call media: ${call.hasVideo ? `video${call.camOn ? "" : ", your camera off"}` : "audio only"}${midCall ? " (renegotiated)" : ""}`);

  // The browser only sees the local link to the host, so its own bandwidth estimate would grow
  // without limit. Cap the camera until the relay passes on the far end's feedback.
  if (call.videoSender) {
    const sender = call.videoSender;
    pc.addEventListener("connectionstatechange", async () => {
      if (pc?.connectionState !== "connected") return;
      const p = sender.getParameters();
      p.encodings = p.encodings?.length ? p.encodings : [{}];
      p.encodings[0].maxBitrate = CAMERA_KBPS * 1000;
      await sender.setParameters(p).catch((e) => log(`bitrate cap failed: ${e.message}`));
    });
  }
  render();
}

function closeMedia() {
  pc?.close();
  pc = null;
  lastStats = null;
  for (const a of [call?.remoteAnalyser, call?.localAnalyser]) a?.source.disconnect();
  call = null;
  $("remote").srcObject = null;
}

// Camera on or off during a video call. Off closes the camera, not just the stream to them.
async function setCamera(on) {
  if (!call?.videoSender || !hasCamera) return;
  call.camOn = on;
  if (!on) await call.videoSender.replaceTrack(null);
  log(`camera ${on ? "on" : "off"}`);
  render();
  await syncCamera();
}

// ---- Audio levels: frequency bars ----

let audioCtx = null;

function analyserFor(track) {
  audioCtx ??= new AudioContext();
  const source = audioCtx.createMediaStreamSource(new MediaStream([track]));
  const node = audioCtx.createAnalyser();
  node.fftSize = 512;
  node.smoothingTimeConstant = 0.5;
  node.minDecibels = -90;
  node.maxDecibels = -25;
  source.connect(node);   // not to the speakers: the remote <video> element plays their audio
  return { source, node, bins: new Uint8Array(node.frequencyBinCount) };
}

const BANDS = 28;
const FALL_MS = 260;
const bars = { them: new Float32Array(BANDS), me: new Float32Array(BANDS) };
const reducedMotion = matchMedia("(prefers-reduced-motion: reduce)");
let lastDraw = 0;

// Bands spaced logarithmically from ~100 Hz to ~6 kHz, where a voice's energy is.
function bands(analyser, out) {
  if (!analyser) { out.fill(0); return 0; }
  analyser.node.getByteFrequencyData(analyser.bins);
  const binHz = audioCtx.sampleRate / analyser.node.fftSize;
  const lo = 100 / binHz, hi = 6000 / binHz;
  let sum = 0;
  for (let b = 0; b < BANDS; b++) {
    const from = Math.floor(lo * (hi / lo) ** (b / BANDS));
    const to = Math.max(from + 1, Math.floor(lo * (hi / lo) ** ((b + 1) / BANDS)));
    let peak = 0;
    for (let i = from; i < to && i < analyser.bins.length; i++) peak = Math.max(peak, analyser.bins[i]);
    out[b] = Math.max(0, (peak / 255 - 0.12) / 0.88);
    sum += out[b];
  }
  return sum / BANDS;
}

const target = { them: new Float32Array(BANDS), me: new Float32Array(BANDS) };

function drawViz(t) {
  if ($("stage").dataset.mode !== "audio" || !call) return;
  requestAnimationFrame(drawViz);
  const dt = Math.min(250, t - (lastDraw || t));
  lastDraw = t;
  const level = { them: bands(call.rxDead ? null : call.remoteAnalyser, target.them), me: bands(call.localAnalyser, target.me) };
  const fall = Math.exp(-dt / FALL_MS);
  for (const k of ["them", "me"]) {
    for (let b = 0; b < BANDS; b++) {
      // Rise most of the way at once; sink slowly, so the picture settles rather than flickers.
      bars[k][b] = target[k][b] > bars[k][b] ? bars[k][b] + (target[k][b] - bars[k][b]) * 0.6 : Math.max(target[k][b], bars[k][b] * fall);
    }
  }

  const c = $("viz");
  const dpr = devicePixelRatio || 1;
  const w = Math.round(c.clientWidth * dpr), ht = Math.round(c.clientHeight * dpr);
  if (!w || !ht) return;
  if (c.width !== w || c.height !== ht) { c.width = w; c.height = ht; }
  const g = c.getContext("2d");
  g.clearRect(0, 0, w, ht);
  const css = getComputedStyle(c);
  const colour = { them: css.color, me: (call.txLost ? css.getPropertyValue("--viz-warn") : css.getPropertyValue("--viz-me")).trim() };
  const mid = ht / 2, gap = 3 * dpr, room = mid - gap - 2 * dpr;
  for (const [k, dir] of [["them", -1], ["me", 1]]) {
    const base = mid + dir * gap;
    g.fillStyle = colour[k];
    g.globalAlpha = 0.9;
    if (k === "them" && call.rxDead) {
      // No packets: no bars at all, just one thin line, unlike the resting pills of a quiet line.
      g.globalAlpha = 0.45;
      g.fillRect(0, base - dpr / 2, w, dpr);
      continue;
    }
    if (reducedMotion.matches) {
      g.fillRect(0, mid + dir * room / 2 - 3 * dpr, Math.max(2 * dpr, Math.min(1, level[k] * 2) * w), 6 * dpr);
      continue;
    }
    const slot = w / BANDS, bar = Math.max(2 * dpr, slot * 0.6), pad = (slot - bar) / 2;
    for (let b = 0; b < BANDS; b++) {
      const len = Math.max(2 * dpr, bars[k][b] * room);
      const x = b * slot + pad, y = dir < 0 ? base - len : base;
      g.beginPath();
      g.roundRect(x, y, bar, len, Math.min(bar, len) / 2);
      g.fill();
    }
  }
  g.globalAlpha = 1;
}

// Twice a second during a call: is video arriving, is audio arriving? Video is judged by the
// decoder's frame count, not by frames shown: a hidden <video> element (as on the "camera off"
// screen) shows none, so it could never switch back.
setInterval(async () => {
  if (!pc || !call || state.call !== "connected") return;
  const t = performance.now();
  let changed = false;
  (await pc.getStats()).forEach((r) => {
    if (r.type !== "inbound-rtp") return;
    if (r.kind === "audio" && r.packetsReceived !== call.rxPackets) { call.rxPackets = r.packetsReceived; call.rxChangedAt = t; }
    if (r.kind === "video" && r.framesDecoded > 0 && r.framesDecoded !== call.framesDecoded) { call.framesDecoded = r.framesDecoded; call.lastFrameAt = t; }
  });
  // rxChangedAt starts when the media is set up, so a call gets the same 2 s grace at the start.
  const rxDead = t - call.rxChangedAt > NO_AUDIO_MS;
  if (rxDead !== call.rxDead) { call.rxDead = rxDead; changed = true; log(rxDead ? "no audio arriving" : "audio arriving again"); }
  const mode = stageMode();
  if (changed || mode !== $("stage").dataset.mode || mode === "nocam") render();
}, 500);

// ---- Rendering the state ----

function stageMode() {
  if (state.call !== "connected" || !call) return "card";
  if (!call.hasVideo) return "audio";
  return performance.now() - call.lastFrameAt < NO_FRAMES_MS ? "video" : "nocam";
}

function render() {
  const { call: phase, remote, reason } = state;
  const live = phase === "calling" || phase === "ringing" || phase === "connected";
  const connected = phase === "connected" && !!call;
  const name = shortName(remote);
  $("callBtn").disabled = live;
  $("address").disabled = live;
  $("controls").hidden = phase !== "connected" && phase !== "calling";
  $("muteBtn").hidden = $("camBtn").hidden = phase !== "connected";
  $("hangupBtn").textContent = phase === "calling" ? "Cancel" : "Hang up";
  $("info").hidden = phase !== "connected";
  $("infoName").textContent = remote || "";

  // The Audio | Video switch beside Call. Locked to Audio if video is off (--no-video).
  const camAvailable = hasCamera;
  const videoAllowed = state.videoAllowed !== false;
  const type = videoAllowed ? callType : "audio";
  for (const el of document.querySelectorAll("#callType [data-type]")) {
    const on = el.dataset.type === type;
    el.classList.toggle("on", on);
    el.setAttribute("aria-checked", String(on));
    el.tabIndex = on ? 0 : -1;
    el.disabled = live || (!videoAllowed && el.dataset.type === "video");
  }
  $("callType").title = !videoAllowed ? "Video is turned off"
    : type === "video" ? "Video call: you'll see each other. You can turn your camera off once it connects."
    : "Audio call. You can add video once it connects.";
  $("callBtn").innerHTML = `${type === "video" ? ICON.video : ICON.phone}<span>${type === "video" ? "Video call" : "Call"}</span>`;

  // The stage.
  const mode = stageMode();
  const stage = $("stage");
  if (stage.dataset.mode !== mode) {
    stage.dataset.mode = mode;
    if (mode === "audio") requestAnimationFrame(drawViz);
  }
  $("nocamInitial").textContent = initial(name);
  $("nocamNote").textContent = connected && Date.now() - connectedAt > NO_FRAMES_MS ? "Their camera is off" : "";
  $("vizThem").textContent = initial(name);
  $("vizThem").title = name;
  const note = (id, text, bad) => {
    const el = $(id);
    el.hidden = !text;
    el.classList.toggle("bad", !!bad);
    el.firstElementChild.textContent = text || "";
  };
  note("noteThem", connected && call.rxDead ? `No audio arriving from ${name}` : "", true);
  note("noteMe", !connected ? "" : call.txLost ? `${name} isn't getting your audio` : call.muted ? "You're muted" : "", connected && call.txLost);

  // Self view: your camera, or a placeholder saying why there isn't one.
  // Self view: only for video. On a video call it's your camera, or a placeholder saying why
  // not; before a call it's a preview while the switch is on Video. Audio shows none.
  const before = phase === "idle" || phase === "ended" || phase === "calling";
  const showSelf = connected ? call.hasVideo : before && type === "video";
  const camOn = connected ? call.camOn : !testCameraOff;
  const selfText = !showSelf ? null : !camAvailable ? "No camera" : !camOn ? "Camera off" : null;
  $("self").hidden = !showSelf || !!selfText || !localTrack("video");
  $("selfOff").hidden = !selfText;
  $("selfOffText").textContent = selfText ?? "";
  if (cameraWanted() !== !!localTrack("video")) syncCamera();

  // In-call camera button. On an audio call it's Add video, unless video can't be had.
  const cam = $("camBtn");
  const canAdd = connected && !call.hasVideo && videoAllowed && !videoRefused;
  cam.disabled = !connected || (call.hasVideo ? !camAvailable : !canAdd || addingVideo);
  cam.setAttribute("aria-pressed", String(connected && call.hasVideo && camAvailable && !call.camOn));
  cam.textContent = !connected ? "Camera off"
    : !call.hasVideo ? (addingVideo ? "Adding video…" : canAdd ? "Add video" : "No video")
    : !camAvailable ? "No camera" : call.camOn ? "Camera off" : "Camera on";
  cam.title = !connected || call.hasVideo ? ""
    : canAdd ? "Add video to this call. They'll be asked; some phones can't."
    : videoRefused ? "Their phone doesn't do video" : "Video is turned off";

  // The card: everything but a connected call.
  const actions = $("cardActions");
  actions.replaceChildren();
  const title = $("cardTitle"), sub = $("cardSub");
  const button = (cls, text, onclick) => Object.assign(document.createElement("button"), { className: `btn ${cls}`, textContent: text, onclick });
  switch (phase) {
    case "idle":
      title.textContent = "Ready";
      sub.textContent = "Type an address above to start a call.";
      break;
    case "calling":
      title.textContent = remote;
      sub.textContent = "Calling…";
      break;
    case "ringing": {
      title.textContent = remote;
      sub.textContent = "Incoming call";
      const answer = button("btn-primary", "Answer", () => post({ type: "answer" }));
      actions.append(
        button("btn-danger", "Decline", () => post({ type: "decline" })),
        // Answers with your camera off; you still see them, and can turn it on any time.
        button("", "Answer, camera off", () => { answerCamera = false; post({ type: "answer" }); }),
        answer);
      answer.focus();
      break;
    }
    case "connected":
      title.textContent = remote;
      sub.textContent = "Connecting media…";
      break;
    case "ended":
      title.textContent = remote || "Call ended";
      sub.textContent = reason || "Call ended";
      break;
  }

  if (phase === "connected" && !connectedAt) {
    connectedAt = Date.now();
    if (testAddVideo) setTimeout(() => call && !call.hasVideo && $("camBtn").click(), testAddVideo * 1000);
    timerId = setInterval(tick, 1000);
    tick();
  } else if (phase !== "connected") {
    clearInterval(timerId);
    connectedAt = 0;
    addingVideo = videoRefused = false;
    if (pc) closeMedia();
  }

  const reg = $("reg");
  reg.className = `reg ${state.registration}`;
  reg.textContent = !state.account ? "No account · direct calls only"
    : state.registration === "registered" ? state.account
    : state.registration === "failed" ? `${state.account} · ${state.registrationDetail || "not registered"}`
    : `${state.account} · registering…`;
  reg.title = state.registrationDetail || "";
}

// Every 5 s during a call: what's arriving and leaving, for the log (and for testing).
let lastStats = null;
async function statsLine() {
  if (!pc) return;
  const now = {};
  (await pc.getStats()).forEach((r) => {
    if (r.type === "inbound-rtp" || r.type === "outbound-rtp") now[`${r.type}-${r.kind}`] = r;
  });
  const fps = (k, field) => {
    const a = lastStats?.[k], b = now[k];
    return a && b ? (((b[field] ?? 0) - (a[field] ?? 0)) / ((b.timestamp - a.timestamp) / 1000)).toFixed(1) : "-";
  };
  const vin = now["inbound-rtp-video"], ain = now["inbound-rtp-audio"];
  log(`stats: video in ${fps("inbound-rtp-video", "framesDecoded")} fps ${vin?.frameWidth ?? 0}x${vin?.frameHeight ?? 0} ${vin?.decoderImplementation ?? ""}, ` +
      `video out ${fps("outbound-rtp-video", "framesEncoded")} fps, audio in ${fps("inbound-rtp-audio", "packetsReceived")} pkt/s level ${(ain?.audioLevel ?? 0).toFixed(3)}, ` +
      `audio out ${fps("outbound-rtp-audio", "packetsSent")} pkt/s, stage ${$("stage").dataset.mode}`);
  lastStats = now;
}

function tick() {
  const s = Math.floor((Date.now() - connectedAt) / 1000);
  if (s > 0 && s % 5 === 0) statsLine();
  $("timer").textContent = `${String(Math.floor(s / 60)).padStart(2, "0")}:${String(s % 60).padStart(2, "0")}`;
}

// ---- Controls ----

$("dialForm").addEventListener("submit", (e) => {
  e.preventDefault();
  const address = $("address").value.trim();
  if (address) post({ type: "dial", address, video: state.videoAllowed !== false && callType === "video" });
});
$("callType").querySelector('[data-type="audio"]').innerHTML = `${ICON.phone}Audio`;
$("callType").querySelector('[data-type="video"]').innerHTML = `${ICON.video}Video`;
function setCallType(type) {
  callType = type;
  try { localStorage.setItem("callType", type); } catch { /* no storage */ }
  render();
}
$("callType").addEventListener("click", (e) => {
  const btn = e.target.closest("[data-type]");
  if (btn && !btn.disabled) setCallType(btn.dataset.type);
});
// A radio group: the arrow keys move between Audio and Video.
$("callType").addEventListener("keydown", (e) => {
  if (!["ArrowLeft", "ArrowRight", "ArrowUp", "ArrowDown"].includes(e.key) || state.videoAllowed === false) return;
  e.preventDefault();
  setCallType(callType === "video" ? "audio" : "video");
  document.querySelector(`#callType [data-type="${callType}"]`)?.focus();
});
$("hangupBtn").onclick = () => post({ type: "hangup" });
$("muteBtn").onclick = () => {
  const track = localTrack("audio");
  if (!track) return;
  track.enabled = !track.enabled;
  if (call) call.muted = !track.enabled;
  $("muteBtn").setAttribute("aria-pressed", String(!track.enabled));
  $("muteBtn").textContent = track.enabled ? "Mute" : "Unmute";
  render();
};
$("camBtn").onclick = () => {
  if (!call) return;
  if (call.hasVideo) { setCamera(!call.camOn); return; }
  addingVideo = true;
  post({ type: "add-video" });
  log("adding video");
  render();
};

chrome.webview.addEventListener("message", async (e) => {
  const msg = e.data;
  try {
    if (msg.type === "state") { state = msg; render(); }
    else if (msg.type === "media-offer") await onMediaOffer(msg.sdp);
    else if (msg.type === "media-health") { if (call) { call.txLost = !!msg.txLost; render(); } }
    else if (msg.type === "video-refused") { addingVideo = false; videoRefused = true; log("they didn't take video"); render(); }
    else if (msg.type === "log") log(msg.text, true);
  } catch (err) {
    log(`${msg.type} failed: ${err.message}`);
  }
});

await openDevices();
render();
syncCamera();
post({ type: "ready" });
