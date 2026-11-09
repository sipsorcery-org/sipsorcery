// The call page. The browser engine does all the media: it captures the microphone and camera,
// and plays the far end. The host (C#) handles SIP and relays the media, still encoded, between
// the SIP call and the local RTCPeerConnection created here when a call connects.
//
// What the stage shows during a call, from the UX prototype (prototype/web):
//  - their video, while frames are arriving;
//  - their initial, if video was agreed but nothing is arriving ("No video arriving from X" after
//    2 s; another softphone sends a placeholder while its camera is off, see placeholderTrack, so
//    this means the video has stalled or the far end stopped sending);
//  - an audio-only view, if the call has no video at all: two rows of frequency bars, theirs
//    above the centre line and yours below, with notes when audio isn't flowing one way.
// The Audio | Video switch beside Call picks the kind of call. An audio call has no video line;
// "Add video" asks the far end for one (a SIP re-INVITE), and the host then sends a new offer.

import { initAccounts, onAccountsState, onAccountMessage } from "./accounts.js";
import { prefs, savePref, initSettings } from "./settings.js";

const $ = (id) => document.getElementById(id);
const post = (msg) => chrome.webview.postMessage(msg);

const CAMERA_KBPS = 1500;
const NO_FRAMES_MS = 2000;   // how long without video frames before saying their camera is off
const NO_AUDIO_MS = 2000;    // how long without audio packets before saying none is arriving

const ICON = {
  phone: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M22 16.9v3a2 2 0 0 1-2.2 2 19.8 19.8 0 0 1-8.6-3.1 19.5 19.5 0 0 1-6-6A19.8 19.8 0 0 1 2.1 4.2 2 2 0 0 1 4.1 2h3a2 2 0 0 1 2 1.7c.1 1 .4 1.9.7 2.8a2 2 0 0 1-.5 2.1L8 9.9a16 16 0 0 0 6 6l1.3-1.3a2 2 0 0 1 2.1-.4c.9.3 1.8.6 2.8.7a2 2 0 0 1 1.7 2Z"/></svg>',
  video: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="m22 8-6 4 6 4V8Z"/><rect x="2" y="6" width="14" height="12" rx="2"/></svg>',
  copy: '<svg viewBox="0 0 24 24" aria-hidden="true"><rect x="9" y="9" width="12" height="12" rx="2"/><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"/></svg>',
  check: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M20 6 9 17l-5-5"/></svg>',
  videoOff: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="m22 8-6 4 6 4V8Z"/><rect x="2" y="6" width="14" height="12" rx="2"/><path d="M2 2l20 20"/></svg>',
  mic: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 2a3 3 0 0 0-3 3v7a3 3 0 0 0 6 0V5a3 3 0 0 0-3-3Z"/><path d="M19 10v2a7 7 0 0 1-14 0v-2"/><path d="M12 19v3"/></svg>',
  micOff: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 2a3 3 0 0 0-3 3v7a3 3 0 0 0 6 0V5a3 3 0 0 0-3-3Z"/><path d="M19 10v2a7 7 0 0 1-14 0v-2"/><path d="M12 19v3"/><path d="M3 3l18 18"/></svg>',
  chevronDown: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="m6 9 6 6 6-6"/></svg>',
  text: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z"/><path d="M8 8h8M12 8v6"/></svg>',
  close: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M18 6 6 18M6 6l12 12"/></svg>',
  collapse: '<svg viewBox="0 0 24 24" aria-hidden="true"><rect x="3" y="3" width="18" height="18" rx="2"/><path d="M15 3v18M8 9l3 3-3 3"/></svg>',
  send: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M22 2 11 13M22 2l-7 20-4-9-9-4z"/></svg>',
  lock: '<svg viewBox="0 0 24 24" aria-hidden="true"><rect x="4" y="11" width="16" height="10" rx="2"/><path d="M8 11V7a4 4 0 0 1 8 0v4"/></svg>',
  unlock: '<svg viewBox="0 0 24 24" aria-hidden="true"><rect x="4" y="11" width="16" height="10" rx="2"/><path d="M8 11V7a4 4 0 0 1 7.9-1"/></svg>',
  // Out and back: the round trip.
  roundTrip: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M3 8h13a4 4 0 0 1 0 8H7"/><path d="m10 12-4 4 4 4"/></svg>',
  selfView: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M21 9V6a2 2 0 0 0-2-2H4a2 2 0 0 0-2 2v10c0 1.1.9 2 2 2h4"/><rect x="12" y="13" width="10" height="7" rx="2"/></svg>',
  hangup: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M10.68 13.31a16 16 0 0 0 3.41 2.6l1.27-1.27a2 2 0 0 1 2.11-.45c.91.34 1.85.57 2.81.7a2 2 0 0 1 1.72 2v3a2 2 0 0 1-2.18 2 19.79 19.79 0 0 1-8.63-3.07 19.42 19.42 0 0 1-3.33-2.67m-2.67-3.34a19.79 19.79 0 0 1-3.07-8.63A2 2 0 0 1 4.11 2h3a2 2 0 0 1 2 1.72c.13.96.36 1.9.7 2.81a2 2 0 0 1-.45 2.11L8.09 9.91"/><path d="M22 2 2 22"/></svg>',
};

let local = null;          // our tracks: the microphone during a call, the camera while it's wanted
let pc = null;
let state = { call: "idle" };
let connectedAt = 0;
let timerId = 0;
let call = null;           // media state for the connected call, see onMediaOffer
let answerCamera = null;   // the Answer button's menu: camera on (true) or off (false) for the call being answered; null follows camPref
let addingVideo = false;   // we asked the host to add video; cleared by the new offer or a refusal
let videoRefused = false;  // the far end turned video down on this call

// The Audio | Video switch: always Audio at start (most SIP calls go to phones, and the camera
// stays off until you ask for video).
let callType = "audio";
const params = new URLSearchParams(location.search);
if (params.get("call") === "video") callType = "video";          // --video (testing)
// Whether calls start with your camera on: the Camera button before a call, and Settings.
// Remembered (see setCamPref); --camera-off starts it off. The Answer button's ▾ can override it
// for one call.
let camPref = params.get("camera") === "off" ? false : prefs.camOn !== false;
// A choice you made (the Camera button, a camera picked, Settings) is kept for next time.
function setCamPref(on) {
  camPref = on;
  if (on) cameraInUse = false;   // a fresh try, because you asked
  savePref("camOn", on);
  log(`calls start with camera ${on ? "on" : "off"}`);
}
const testAddVideo = Number(params.get("addvideo")) || 0;        // --add-video <s> (testing)
const testSay = params.get("say") ?? "";                           // --say <text> (testing)

const localTrack = (kind) => local?.getTracks().find((t) => t.kind === kind) ?? null;

function log(text, fromHost = false) {
  if (!fromHost) post({ type: "log", text });
  const el = $("log");
  // Follows the newest line, unless you've scrolled up to read something.
  const atEnd = el.hidden || el.scrollHeight - el.scrollTop - el.clientHeight < 24;
  el.textContent += text + "\n";
  if (el.textContent.length > 60000) el.textContent = el.textContent.slice(-40000);
  if (atEnd) el.scrollTop = el.scrollHeight;
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

// The microphone is only open during a call (see openMic). The camera is only opened while it's wanted (see
// cameraWanted), so on an audio call, or with the switch on Audio, it's off: no self view, and
// the camera's light stays off.
// A camera another app (or another copy of this one) is using can't be opened on Windows. That
// isn't "no camera", so it says "Camera in use", and the call sends the placeholder. It is NOT
// retried by itself: the camera is only ever opened because of something you did (switching to
// Video, placing or answering a video call, Add video, Camera on). Otherwise it would start
// streaming on its own the moment the other app let go.
// The microphone, speaker and camera you picked from the menus on Mute and Camera, remembered
// between runs. Empty means the system default.
let devices = { mic: "", speaker: "", camera: "" };
try { Object.assign(devices, JSON.parse(localStorage.getItem("devices") ?? "{}")); } catch { /* storage unavailable */ }
const saveDevices = () => { try { localStorage.setItem("devices", JSON.stringify(devices)); } catch { /* storage unavailable */ } };
const MIC_CONSTRAINTS = { echoCancellation: true, noiseSuppression: true, autoGainControl: true };

let hasCamera = false;     // a camera exists (and hasn't failed to open for good)
let cameraInUse = false;   // the last try found it in use; cleared by your next camera action
let cameraBusy = null;     // the open or close in progress, so they never overlap

// Like the camera, the microphone is only open while it's needed: from placing or answering a
// call until it ends. So the mic light (on a webcam, say) is off between calls. Mute works
// before a call too: it's remembered, and the call's microphone opens muted.
let hasMic = false;        // a microphone exists
let micMuted = false;
let micOpening = null;     // the open in progress, so placing a call and its media share one

async function openDevices() {
  local = new MediaStream();
  try {
    const list = await navigator.mediaDevices.enumerateDevices();
    hasMic = list.some((d) => d.kind === "audioinput");
    hasCamera = list.some((d) => d.kind === "videoinput");
  } catch { hasMic = hasCamera = false; }
  if (devices.speaker) $("remote").setSinkId?.(devices.speaker).catch(() => { devices.speaker = ""; });
}

// Opens the microphone (the remembered one, or the default if it's gone) unless it's open.
// Resolves to the track, or null if there's no microphone to be had.
function openMic() {
  if (localTrack("audio")) return Promise.resolve(localTrack("audio"));
  micOpening ??= (async () => {
    const open = (id) => navigator.mediaDevices.getUserMedia({ audio: { ...MIC_CONSTRAINTS, ...(id ? { deviceId: { exact: id } } : {}) } });
    try {
      // Exactly ("ideal" isn't always honoured), falling back to the default if it's unplugged.
      const stream = await open(devices.mic).catch((err) => {
        if (!devices.mic) throw err;
        log(`remembered microphone unavailable (${err.name}), using the default`);
        return open("");
      });
      const track = stream.getAudioTracks()[0];
      track.enabled = !micMuted;
      local.addTrack(track);
      log(`microphone opened: ${track.label}`);
      return track;
    } catch (err) {
      log(`microphone unavailable: ${err.name} ${err.message}`);
      return null;
    } finally {
      micOpening = null;
      render();
    }
  })();
  return micOpening;
}

function closeMic() {
  const track = localTrack("audio");
  if (!track) return;
  local.removeTrack(track);
  track.stop();
  log("microphone closed");
}

// Whether the camera should be open: during a video call with your camera on, or, before a
// call, while the switch is on Video (a preview of what they'll see).
function cameraWanted() {
  if (!local || !videoAvailable()) return false;   // not before the microphone's open (local is set)
  if (call) return call.hasVideo && call.camOn;
  // Connected, but the media isn't set up yet: leave the camera as it is, so a video call's
  // preview carries straight on into the call.
  if (state.call === "connected") return !!localTrack("video");
  const before = state.call === "idle" || state.call === "ended" || state.call === "calling";
  // The preview is only for you, so with Self view off the camera stays closed until the call.
  return before && state.videoAllowed !== false && callType === "video" && camPref && selfViewOn;
}

// While your camera is off (or there's none) on a video call, the call sends a placeholder
// instead of nothing: your initial on a plain background, a few frames a second. Video never
// stops, so every SIP phone just sees a picture rather than a frozen frame or a dead stream,
// NAT bindings and media timers stay alive, and turning the camera on is a track swap with the
// encoder already running. It's drawn on a canvas and sent at the camera's size, so the far
// end doesn't see the picture change shape.
const PLACEHOLDER_FPS = 5;   // a still picture costs a few kbps; much sparser upsets some decoders
let placeholder = null;      // { canvas, track, timer, drawn }

function placeholderTrack() {
  if (!placeholder) {
    const canvas = Object.assign(document.createElement("canvas"), { width: 1280, height: 720 });
    // Frame rate 0 and requestFrame: a frame goes out each time one is drawn and asked for. A
    // canvas only produces a frame after a draw, so the picture is redrawn every time.
    const track = canvas.captureStream(0).getVideoTracks()[0];
    placeholder = { canvas, track, timer: setInterval(placeholderFrame, 1000 / PLACEHOLDER_FPS) };
    placeholderFrame();
  }
  return placeholder.track;
}

function placeholderFrame() {
  const { canvas, track } = placeholder;
  const label = state.me ? initial(state.me) : "";
  const g = canvas.getContext("2d");
  const { width: w, height: h } = canvas;
  g.fillStyle = "#1f3a31";
  g.fillRect(0, 0, w, h);
  g.strokeStyle = g.fillStyle = "#a9c2b8";
  g.lineWidth = 4;
  g.beginPath();
  g.arc(w / 2, h / 2, 120, 0, 2 * Math.PI);
  g.stroke();
  if (label) {
    g.font = '300 120px "Segoe UI", system-ui, sans-serif';
    g.textAlign = "center";
    g.textBaseline = "middle";
    g.fillText(label, w / 2, h / 2 + 6);
  } else {
    // No name to show: a head and shoulders.
    g.beginPath();
    g.arc(w / 2, h / 2 - 30, 38, 0, 2 * Math.PI);
    g.fill();
    g.beginPath();
    g.ellipse(w / 2, h / 2 + 70, 70, 50, 0, Math.PI, 2 * Math.PI);
    g.fill();
  }
  track.requestFrame();
}

function stopPlaceholder() {
  if (!placeholder) return;
  clearInterval(placeholder.timer);
  placeholder.track.stop();
  placeholder = null;
}

// ---- Test pattern (diagnostics) ----
// In diagnostics mode, the video source can be a test pattern instead of the camera. It stands
// in for the camera exactly: it's opened and closed when the camera would be, becomes the
// video track in `local`, and Camera off swaps in the placeholder as usual. The picture has
// colour bars and a grey ramp (colour and banding), a dial whose hand steps once per frame
// drawn (a dropped frame shows as a skipped tick), a clock to the millisecond (compare the
// receiver's picture with the sender's screen for the delay) and a frame counter. It's picked
// from the Camera button's menu.
// "Audio bars" is the other diagnostic source: your microphone's frequency bars, drawn the way
// the audio-only view draws them, with the same clock. The far end sees what you're sending as
// audio, so they can tell sound from silence (or a muted or dead microphone) at a glance, and
// judge lip sync: a bar should jump as they hear the sound. The microphone only opens with a
// call, so before one the picture says so.
const TEST_FPS = 30;
let videoSource = "camera";   // "camera", "test" or "bars"; back to camera when diagnostics goes off
let testPattern = null;       // { kind, canvas, track, timer, frame, and for bars the analyser and levels }
const SOURCE_LABEL = { test: "Test pattern", bars: "Audio bars" };

// Whether there's a video source to open: the test pattern always; a camera if there is one
// and it isn't in use elsewhere.
const videoAvailable = () => videoSource !== "camera" || (hasCamera && !cameraInUse);
const isTestTrack = (track) => !!track && track === testPattern?.track;

function openTestPattern() {
  const canvas = Object.assign(document.createElement("canvas"), { width: 1280, height: 720 });
  const track = canvas.captureStream(0).getVideoTracks()[0];
  testPattern = {
    kind: videoSource, canvas, track, frame: 0, timer: setInterval(testPatternFrame, 1000 / TEST_FPS),
    analyser: null, micTrack: null, levels: new Float32Array(BANDS), target: new Float32Array(BANDS), last: performance.now(),
  };
  testPatternFrame();
  return track;
}

const testPatternFrame = () => (testPattern.kind === "bars" ? audioBarsFrame() : patternFrame());

// The band along the bottom of every diagnostic source: a clock to the millisecond, who's
// sending, size and rate, and a frame counter.
function sourceFooter(g, W, H, frame) {
  const pad = (n, w = 2) => String(n).padStart(w, "0");
  const now = new Date();
  g.textBaseline = "middle";
  g.fillStyle = "#101418";
  g.fillRect(0, 530, W, H - 530);
  g.fillStyle = "#fff";
  g.font = "600 64px Consolas, monospace";
  g.textAlign = "center";
  g.fillText(`${pad(now.getHours())}:${pad(now.getMinutes())}:${pad(now.getSeconds())}.${pad(now.getMilliseconds(), 3)}`, W / 2, 600);
  g.font = "28px Consolas, monospace";
  g.fillStyle = "#9fb3ad";
  g.textAlign = "left";
  g.fillText(state.me || state.address || "", 40, 680);
  g.textAlign = "center";
  g.fillText(`${W}×${H} · ${TEST_FPS} fps`, W / 2, 680);
  g.textAlign = "right";
  g.fillText(`frame ${pad(frame, 6)}`, W - 40, 680);
}

function closeVideoTrack(track) {
  local.removeTrack(track);
  track.stop();
  if (isTestTrack(track)) {
    clearInterval(testPattern.timer);
    testPattern.analyser?.source.disconnect();
    testPattern = null;
  }
}

function patternFrame() {
  const { canvas, track } = testPattern;
  const frame = testPattern.frame++;
  const g = canvas.getContext("2d");
  const W = canvas.width, H = canvas.height, pad = (n, w = 2) => String(n).padStart(w, "0");
  g.textBaseline = "middle";
  g.lineCap = "butt";

  // 75% colour bars, a stepped grey ramp, and a smooth gradient for banding.
  const bars = ["#c0c0c0", "#c0c000", "#00c0c0", "#00c000", "#c000c0", "#c00000", "#0000c0"];
  const bw = W / bars.length, barH = 430;
  bars.forEach((c, i) => { g.fillStyle = c; g.fillRect(Math.round(i * bw), 0, Math.ceil(bw), barH); });
  for (let i = 0; i < 11; i++) {
    const v = Math.round(i * 25.5);
    g.fillStyle = `rgb(${v},${v},${v})`;
    g.fillRect(Math.round(i * W / 11), barH, Math.ceil(W / 11), 70);
  }
  const grad = g.createLinearGradient(0, 0, W, 0);
  grad.addColorStop(0, "#000");
  grad.addColorStop(1, "#fff");
  g.fillStyle = grad;
  g.fillRect(0, barH + 70, W, 30);
  // A marker across the ramp, once every 4 s: smooth motion.
  g.fillStyle = "#ff2d55";
  g.fillRect(((frame % (TEST_FPS * 4)) / (TEST_FPS * 4)) * W - 3, barH, 6, 100);

  sourceFooter(g, W, H, frame);

  // The dial: one tick per frame of a second, the hand on this frame's tick.
  const cx = W / 2, cy = 215, r = 150, tick = frame % TEST_FPS;
  g.fillStyle = "rgba(16,20,24,0.88)";
  g.beginPath();
  g.arc(cx, cy, r + 18, 0, 2 * Math.PI);
  g.fill();
  for (let i = 0; i < TEST_FPS; i++) {
    const a = (i / TEST_FPS) * 2 * Math.PI - Math.PI / 2, lit = i === tick;
    g.strokeStyle = lit ? "#ffd60a" : "#56645f";
    g.lineWidth = lit ? 6 : 3;
    g.beginPath();
    g.moveTo(cx + Math.cos(a) * (r - 22), cy + Math.sin(a) * (r - 22));
    g.lineTo(cx + Math.cos(a) * r, cy + Math.sin(a) * r);
    g.stroke();
  }
  const a = (tick / TEST_FPS) * 2 * Math.PI - Math.PI / 2;
  g.strokeStyle = "#ffd60a";
  g.lineWidth = 8;
  g.lineCap = "round";
  g.beginPath();
  g.moveTo(cx, cy);
  g.lineTo(cx + Math.cos(a) * (r - 30), cy + Math.sin(a) * (r - 30));
  g.stroke();
  g.fillStyle = "#fff";
  g.font = "600 36px Consolas, monospace";
  g.textAlign = "center";
  // The tick number sits opposite the hand, so the hand never covers it.
  g.fillText(pad(tick), cx - Math.cos(a) * 62, cy - Math.sin(a) * 62);

  track.requestFrame();
}

function audioBarsFrame() {
  const tp = testPattern;
  const { canvas, track } = tp;
  const frame = tp.frame++;
  const g = canvas.getContext("2d");
  const W = canvas.width, H = canvas.height;

  // Follow the microphone: it opens with the call, and can be switched mid-call.
  const mic = localTrack("audio");
  if (mic !== tp.micTrack) {
    tp.analyser?.source.disconnect();
    tp.analyser = mic ? analyserFor(mic) : null;
    tp.micTrack = mic;
  }
  const level = bands(mic?.enabled ? tp.analyser : null, tp.target);
  const t = performance.now(), fall = Math.exp(-Math.min(250, t - tp.last) / FALL_MS);
  tp.last = t;
  for (let b = 0; b < BANDS; b++) {
    const v = tp.levels[b], to = tp.target[b];
    tp.levels[b] = to > v ? v + (to - v) * 0.6 : Math.max(to, v * fall);
  }

  g.fillStyle = "#0f1a17";
  g.fillRect(0, 0, W, H);
  g.textBaseline = "middle";

  // Top: what this is, and the microphone's state.
  g.font = "600 30px 'Segoe UI', system-ui, sans-serif";
  g.textAlign = "left";
  g.fillStyle = "#e4ece8";
  // Below where the far end's name-and-timer overlay sits.
  g.fillText("Audio bars", 48, 116);
  const status = !mic ? "Microphone opens with the call" : !mic.enabled ? "Muted" : mic.label;
  g.font = "24px 'Segoe UI', system-ui, sans-serif";
  g.fillStyle = !mic || !mic.enabled ? "#e6b86a" : "#9fb3ad";
  g.fillText(status, 48, 156);
  // A level meter, top right.
  g.fillStyle = "#24332e";
  g.fillRect(W - 48 - 320, 108, 320, 16);
  g.fillStyle = "#4cc29b";
  g.fillRect(W - 48 - 320, 108, 320 * Math.min(1, level * 2), 16);

  // The bars, mirrored about a centre line, as in the audio-only view.
  const mid = 340, room = 130, left = 64, width = W - 128;
  const slot = width / BANDS, bar = slot * 0.6, gap = (slot - bar) / 2;
  g.fillStyle = "#3ea486";
  for (let b = 0; b < BANDS; b++) {
    const len = Math.max(4, tp.levels[b] * room);
    g.beginPath();
    g.roundRect(left + b * slot + gap, mid - len, bar, len * 2, Math.min(bar, len) / 2);
    g.fill();
  }
  g.fillStyle = "#24332e";
  g.fillRect(left, mid - 1, width, 2);
  g.font = "18px Consolas, monospace";
  g.fillStyle = "#677670";
  g.textAlign = "left";
  g.fillText("100 Hz", left, mid + room + 30);
  g.textAlign = "right";
  g.fillText("6 kHz", left + width, mid + room + 30);

  sourceFooter(g, W, H, frame);
  track.requestFrame();
}

// Switches the video source (or, with reopen, the camera). Whatever's open closes, and
// syncCamera opens the new one if video is wanted (so a camera that's off stays off).
function setVideoSource(source, reopen = false) {
  if (source === videoSource && !reopen) return;
  videoSource = source;
  if (source === "camera") cameraInUse = false;   // a fresh try, because you asked
  log(`video source: ${source}`);
  if (!local) return;   // at start, before the devices are open; the first syncCamera picks it up
  const track = localTrack("video");
  if (track) closeVideoTrack(track);
  syncCamera();
}

// What the call's video sender should carry: the camera when it's on and open, else the placeholder.
const videoToSend = () => (call.camOn && localTrack("video")) || placeholderTrack();

// Puts what's sent (or, before a call, what would be) in the self view. Only the camera is
// mirrored, so the placeholder's initial reads the right way round. The placeholder is stopped
// once neither the self view nor a call needs it.
function showSelfVideo(show) {
  const el = $("self");
  const track = !show ? null : call?.videoSender ? videoToSend() : localTrack("video") ?? placeholderTrack();
  if ((el.srcObject?.getVideoTracks()[0] ?? null) !== track) el.srcObject = track ? new MediaStream([track]) : null;
  el.hidden = !track;
  el.classList.toggle("mirror", !!track && track === localTrack("video") && !isTestTrack(track));
  if (placeholder && track !== placeholder.track && !call?.videoSender) stopPlaceholder();
}

// Opens or closes the camera to match cameraWanted(), and puts it on the call when it should be sent.
function syncCamera() {
  cameraBusy = (cameraBusy ?? Promise.resolve()).then(async () => {
    const want = cameraWanted();
    let track = localTrack("video");
    if (want && !track && videoSource !== "camera") {
      local.addTrack(openTestPattern());
      log(`${SOURCE_LABEL[videoSource].toLowerCase()} opened`);
    } else if (want && !track) {
      try {
        // The chosen camera exactly, or if it's gone, the default.
        const open = (id) => navigator.mediaDevices.getUserMedia({
          video: { width: { ideal: 1280 }, height: { ideal: 720 }, frameRate: { ideal: 30 }, ...(id ? { deviceId: { exact: id } } : {}) },
        });
        const cam = await open(devices.camera).catch((err) => {
          if (!devices.camera || err.name !== "OverconstrainedError" && err.name !== "NotFoundError") throw err;
          log("chosen camera not found, using the default");
          return open("");
        });
        track = cam.getVideoTracks()[0];
        // Changed while it was opening (including a switch to the test pattern).
        if (!cameraWanted() || videoSource !== "camera") { track.stop(); return; }
        local.addTrack(track);
        log("camera opened");
      } catch (err) {
        if (err.name === "NotReadableError" || err.name === "AbortError") {
          // In use elsewhere. Your camera goes off; trying again is up to you.
          log(`camera in use: ${err.name} ${err.message}`);
          cameraInUse = true;
          if (call) call.camOn = false;
          render();
          return;
        }
        // Missing, or permission refused: no camera for this run.
        log(`camera unavailable: ${err.name} ${err.message}`);
        hasCamera = false;
        cameraInUse = false;
        if (call) call.camOn = false;
      }
    } else if (!want && track) {
      log(isTestTrack(track) ? `${SOURCE_LABEL[testPattern.kind].toLowerCase()} closed` : "camera closed");
      closeVideoTrack(track);
    }
    // Always match the call's sender to the camera, even when the camera itself didn't change:
    // a new call (or the new connection after Add video) has a new sender, and a camera left
    // open from the preview or the last call must still be put on it.
    if (call?.videoSender) {
      const send = videoToSend();
      if (call.videoSender.track !== send) await call.videoSender.replaceTrack(send);
    }
    render();   // the self view follows what's sent
  }).catch((err) => log(`camera sync failed: ${err.message}`));   // so one failure doesn't stall the queue
  return cameraBusy;
}

// ---- Media from the host ----

// A new offer arrives when a call connects, and again when video is added to it. Video you
// asked for starts with your camera on; video they added starts with it off, so being sent a
// video request never turns your camera on by itself.
// The host's offer for the call's media: answer it (media-answer). With no offer (the web
// softphone placing a call, where this page's connection goes straight to the far end), make
// one instead (media-offer-made) for a call with or without video, and take the answer from
// media-remote-answer.
async function onMediaOffer(sdp, video = false) {
  const midCall = !!call;
  closeMedia();
  if (!midCall) quality = newQuality();
  else if (quality) quality.prev = quality.audioWindow = null;   // a new peer connection: its counters start again
  await openMic();   // usually already opening, from Call or Answer
  const wantCamera = addingVideo ? true : midCall ? false : (answerCamera ?? camPref);
  addingVideo = false;
  if (wantCamera) cameraInUse = false;   // you placed, answered or added video: try the camera again
  call = {
    // The call has video at all (an m=video line from the host). Read from the offer now, not
    // once the transceivers are set up, or a render in between briefly shows the audio bars.
    hasVideo: sdp ? /^m=video [1-9]/m.test(sdp) : video,
    camOn: wantCamera && videoAvailable(),
    muted: micMuted,
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

  if (sdp) await pc.setRemoteDescription({ type: "offer", sdp });
  else {
    pc.addTransceiver("audio");
    if (video) pc.addTransceiver("video");
  }
  for (const tr of pc.getTransceivers()) {
    const kind = tr.receiver.track.kind;
    if (kind === "video") {
      call.hasVideo = true;
      call.videoSender = tr.sender;
      // Always sendrecv, camera or not: the placeholder goes out until the camera does, so it
      // can be turned on later without renegotiating. Sent now, so video flows from the start.
      tr.direction = "sendrecv";
      await tr.sender.replaceTrack(placeholderTrack());
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
  await pc.setLocalDescription(sdp ? await pc.createAnswer() : await pc.createOffer());
  await new Promise((resolve) => {
    if (pc.iceGatheringState === "complete") return resolve();
    pc.addEventListener("icegatheringstatechange", () => pc.iceGatheringState === "complete" && resolve());
    setTimeout(resolve, 2000);
  });
  post({ type: sdp ? "media-answer" : "media-offer-made", sdp: pc.localDescription.sdp });
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

// Their answer to the offer this page made. Video they turned down (port 0) means no video.
async function onMediaAnswer(sdp) {
  if (!pc) return;
  await pc.setRemoteDescription({ type: "answer", sdp });
  if (call?.hasVideo && !/^m=video [1-9]/m.test(sdp)) {
    call.hasVideo = false;
    videoRefused = true;
    syncCamera();
    log("they didn't take video");
  }
  render();
}

function closeMedia() {
  pc?.close();
  pc = null;
  lastStats = null;
  for (const a of [call?.remoteAnalyser, call?.localAnalyser]) a?.source.disconnect();
  call = null;
  stopPlaceholder();
  $("remote").srcObject = null;
}

// Camera on or off during a video call. Off swaps in the placeholder, then closes the camera.
async function setCamera(on) {
  if (!call?.videoSender || !(hasCamera || videoSource !== "camera")) return;
  call.camOn = on;
  if (on) cameraInUse = false;   // try again, because you asked
  if (!on) await call.videoSender.replaceTrack(placeholderTrack());
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

// ---- Call quality (diagnostics) ----
// "Call quality", a panel over the stage in diagnostics mode: ↑ Sent is how the far end receives
// what we send, from their RTCP receiver reports (every 5 s or so, from any SIP phone that sends
// RTCP); ↓ Received is how we receive them, measured here. Received audio comes from the page's
// WebRTC stats: they're for the hop from the relay, but audio is relayed packet by packet with
// its sequence numbers, so its loss and jitter are the SIP leg's. Received video is packetised
// again for the page, so its loss comes from our own RTCP report instead, and its frame rate
// from the page. A graph below shows the picked column over the last two minutes.
const Q_POINTS = 120;   // graph length: 2 min of 1 s samples
const NO_RTCP_MS = 15000;
// { sent: { audio, video } their latest reports, recvVideoRtcp: our latest report on their video,
//   recv: { audio, video } latest 1 s measures, history: per-second points for the graph, prev, startedAt }
let quality = null;

// ---- Call details: the chevron on the info bar drops down the facts about the call ----
// The host sends how the call was set up (details); the codecs each way come from the page's
// stats, which are the SIP leg's, since the relay passes the media on without re-encoding.
let callDetails = null;
let callCodecs = null;   // { audio: { tx, rx }, video: { tx, rx } }

// "audio/opus" at 48000 → "Opus 48 kHz"; "video/VP8" → "VP8".
function codecLabel(c) {
  if (!c?.mimeType) return null;
  const [kind, name] = c.mimeType.split("/");
  const pretty = /^opus$/i.test(name) ? "Opus" : name.toUpperCase();
  if (kind !== "audio" || !c.clockRate) return pretty;
  // G.722's RTP clock is 8 kHz (RFC 3551 kept it for history), but it's 16 kHz audio.
  const rate = pretty === "G722" ? 16000 : c.clockRate;
  return `${pretty} ${rate % 1000 === 0 ? `${rate / 1000} kHz` : `${rate} Hz`}`;
}

function renderDetails() {
  const el = $("details");
  const d = callDetails;
  if (!d || !call) { el.innerHTML = ""; return; }
  const esc = (t) => String(t).replace(/[&<>"]/g, (ch) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[ch]));
  // Each way, as the page sees it. Audio shows one value when both ways match; video always shows
  // both, as its frame sizes change on their own (each side ramps up) and would otherwise flip
  // between one value and two.
  const codecs = (kind, agreed) => {
    const c = callCodecs?.[kind];
    if (!c?.tx && !c?.rx) return agreed ? esc(agreed) : "–";
    if (kind !== "video" && c.tx === c.rx) return esc(c.tx);
    return `↑ ${esc(c.tx ?? "–")} &nbsp;↓ ${esc(c.rx ?? "–")}`;
  };
  const enc = d.mediaEncrypted && d.signallingEncrypted
    ? `<span class="enc">${ICON.lock}Encrypted: SRTP media, SIP over TLS</span>`
    : d.mediaEncrypted ? `<span class="enc">${ICON.lock}Media encrypted (SRTP); SIP not (${esc(d.signalling?.split(" ")[0] ?? "UDP")})</span>`
    : `<span class="enc off">${ICON.unlock}Not encrypted: plain RTP${d.signallingEncrypted ? "" : `, SIP over ${esc(d.signalling?.split(" ")[0] ?? "UDP")}`}</span>`;
  const rows = [
    ["Call", `${d.direction === "outgoing" ? "Outgoing" : "Incoming"} · ${d.account ? esc(d.account) : "no account"}`],
    ["Their phone", d.theirPhone ? esc(d.theirPhone) : "Didn't say"],
    ["Security", enc],
    ["Audio", codecs("audio", d.audio)],
    ["Video", call.hasVideo ? codecs("video", d.video) : "None"],
    ["SIP", d.signalling ? `<span class="mono">${esc(d.signalling)}</span>` : "–"],
    ["Media", d.theirMedia ? `<span class="mono">${esc(d.theirMedia)}</span>` : "–"],
    ["Call-ID", d.callId ? `<span class="mono">${esc(d.callId)}</span><button type="button" class="copy-id" id="copyCallId" title="Copy the Call-ID">Copy</button>` : "–"],
  ];
  el.innerHTML = rows.map(([k, v]) => `<dt>${k}</dt><dd>${v}</dd>`).join("");
  const copy = $("copyCallId");
  if (copy) copy.onclick = (e) => {
    e.stopPropagation();
    navigator.clipboard?.writeText(d.callId).then(() => { copy.textContent = "Copied"; setTimeout(() => { copy.textContent = "Copy"; }, 1500); }, () => {});
  };
}

// Closed at the start of every call.
function setDetailsOpen(open) {
  $("details").hidden = !open;
  if (open) renderDetails();
  $("infoHead").setAttribute("aria-expanded", String(open));
  $("infoHead").title = open ? "Hide call details" : "Show call details";
}
$("infoChevron").innerHTML = ICON.chevronDown;
// Call details are technical, so only in diagnostics mode: otherwise the info bar is just
// who and how long, with no chevron, and closing diagnostics closes them.
function allowDetails(on) {
  $("infoHead").disabled = !on;
  if (!on && !$("details").hidden) setDetailsOpen(false);
  $("infoHead").title = on ? ($("details").hidden ? "Show call details" : "Hide call details") : "";
}

// ---- Real-time text (RFC 4103) ----
// The host sends and receives the T.140 text; the page shows the conversation. What you type is
// sent when you press Send or Enter, as the line and a T.140 line separator (U+2028). Theirs
// may arrive letter by letter (a real-time text phone): their unfinished line shows as it comes,
// backspaces and all, dashed with a caret.
const LINE_SEP = "\u2028";
let textLines = [];          // { me, text, open }
let textOpen = false;        // the popover
let textUnread = false;      // they typed while it was closed: a dot on the Text button
let toastTimer = 0;
let textBomSent = false;     // T.140 starts with a byte order mark
let textSaid = false;        // --say (testing) typed once per call

const openLine = (me, create = true) => {
  let l = textLines.findLast((x) => x.me === me && x.open);
  if (!l && create) { l = { me, text: "", open: true }; textLines.push(l); }
  return l;
};
function closeLine(me) {
  const l = openLine(me, false);
  if (!l) return;
  l.open = false;
  if (!l.text) textLines.splice(textLines.indexOf(l), 1);
}

function onTextReceived(chars) {
  for (const ch of chars.replace(/\uFEFF/g, "").replace(/\r\n/g, "\n")) {
    if (ch === "\b") { const l = openLine(false, false); if (l) l.text = [...l.text].slice(0, -1).join(""); }
    else if (ch === LINE_SEP || ch === "\u2029" || ch === "\n" || ch === "\r") closeLine(false);
    else if (ch >= " ") openLine(false).text += ch;
  }
  if (!textOpen) { textUnread = true; showToast(); }
  renderText();
  render();
}

// With the popover closed, what they type shows in a toast for 6 s after the last letter.
function showToast() {
  const l = openLine(false, false) ?? textLines.findLast((x) => !x.me);
  if (!l?.text) return;
  $("rttToastWho").textContent = shortName(state.remote) || "They";
  $("rttToastText").textContent = l.text;
  $("rttToast").hidden = false;
  clearTimeout(toastTimer);
  toastTimer = setTimeout(hideToast, 6000);
}
function hideToast() {
  clearTimeout(toastTimer);
  $("rttToast").hidden = true;
}
$("rttToastIcon").innerHTML = ICON.text;
$("rttReply").onclick = () => setTextOpen(true);

// Sends what's in the box, with a line separator to finish it. Nothing goes until then.
function sendLine() {
  const line = $("rttInput").value;
  if (!line.trim() || state.text !== "on") return;
  post({ type: "text", text: (textBomSent ? "" : "\uFEFF") + line + LINE_SEP });
  textBomSent = true;
  textLines.push({ me: true, text: line, open: false });
  $("rttInput").value = "";
  $("rttSend").disabled = true;
  renderText();
}
$("rttSend").innerHTML = ICON.send;
$("rttSend").onclick = () => { sendLine(); $("rttInput").focus(); };
$("rttInput").addEventListener("input", () => { $("rttSend").disabled = !$("rttInput").value.trim(); });
$("rttInput").addEventListener("keydown", (e) => {
  if (e.key === "Enter") { e.preventDefault(); sendLine(); }
});
$("rttAdd").onclick = () => post({ type: "add-text" });
$("rttClose").innerHTML = ICON.close;
$("rttClose").onclick = () => setTextOpen(false);
$("textBtn").onclick = () => setTextOpen(!textOpen);
// Esc, or a click anywhere outside it (other than the Text button), closes the popover.
document.addEventListener("keydown", (e) => {
  if (e.key !== "Escape" || !textOpen || openMenu) return;
  e.preventDefault();
  setTextOpen(false);
});
// On click rather than pointerdown: closing re-renders the controls, and doing that between
// pointerdown and pointerup lost the click itself (End took two clicks with the popover open).
document.addEventListener("click", (e) => {
  if (textOpen && !$("rtt").contains(e.target) && !$("textBtn").contains(e.target)) setTextOpen(false, false);
}, true);
document.addEventListener("keydown", (e) => {
  if (e.key.toLowerCase() !== "t" || e.ctrlKey || e.altKey || e.metaKey || openMenu) return;
  if (e.target.closest?.("input, textarea, select, [contenteditable]")) return;
  if ($("textBtn").disabled) return;
  e.preventDefault();
  setTextOpen(!textOpen);
});

function setTextOpen(open, focus = true) {
  textOpen = open;
  if (open) { textUnread = false; hideToast(); renderText(); }
  render();
  if (open && focus && !$("rttEntry").hidden) $("rttInput").focus();
  else if (!open && focus && document.activeElement && $("rtt").contains(document.activeElement)) $("textBtn").focus();
}

function renderText() {
  const el = $("rttLog");
  const atEnd = el.scrollHeight - el.scrollTop - el.clientHeight < 24;
  const esc = (t) => t.replace(/[&<>]/g, (ch) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;" }[ch]));
  const name = shortName(state.remote) || "Them";
  let who = null;
  el.innerHTML = !textLines.length ? `<p class="rtt-empty">Nothing typed yet.</p>`
    : textLines.map((l) => {
      const head = l.me !== who ? `<div class="rtt-who ${l.me ? "me" : ""}">${l.me ? "You" : esc(name)}</div>` : "";
      who = l.me;
      return `${head}<div class="rtt-msg ${l.me ? "me" : "them"}${l.open ? " live" : ""}">${esc(l.text)}${l.open ? `<span class="rtt-caret" aria-hidden="true"></span>` : ""}</div>`;
    }).join("");
  if (atEnd) el.scrollTop = el.scrollHeight;
}

// The panel and the Text button, from the call state (render calls this).
function renderTextControls(phase) {
  const live = phase === "connected";
  const btn = $("textBtn");
  btn.disabled = !live;
  btn.setAttribute("aria-pressed", String(live && textOpen));
  const typing = !!openLine(false, false);
  const dot = live && textUnread ? `<span class="btn-dot" aria-hidden="true"></span>` : "";
  const html = `${ICON.text}${dot}`;
  if (btn.innerHTML !== html) btn.innerHTML = html;
  btn.title = !live ? "Real-time text, during a call"
    : textUnread ? `Text: ${typing ? "they're typing" : "new message"} (T)` : textOpen ? "Close text (T)" : "Text (T)";
  btn.setAttribute("aria-label", textUnread ? "Text, new message" : "Text");
  $("rtt").hidden = !(live && textOpen);
  if (!live) hideToast();
  // The compose area follows the text state: on, adding, refused, off or disabled.
  const t = state.text ?? "off";
  const name = shortName(state.remote) || "They";
  $("rttEntry").hidden = t !== "on";
  $("rttAdd").hidden = t !== "off";
  $("rttHint").textContent = t === "on" ? "Enter or Send sends the message."
    : t === "adding" ? `Asking ${name}'s phone to add text…`
    : t === "refused" ? `${name}'s phone doesn't support real-time text.`
    : t === "disabled" ? "Real-time text is turned off (--no-text)."
    : t === "unavailable" ? "Text isn't available on calls with browser phones yet."
    : "Text isn't set up on this call.";
  // --say (testing): once text is up, type it into the box, then send it.
  if (testSay && t === "on" && !textSaid) {
    textSaid = true;
    setTimeout(() => { $("rttInput").value = testSay; sendLine(); }, 1500);
  }
}
$("infoHead").onclick = () => setDetailsOpen($("details").hidden);
let qColumn = "sent";   // the column the graph shows
const lastOf = (a) => a[a.length - 1];
const newQuality = () => ({
  sent: { audio: null, video: null }, recvVideoRtcp: null, recv: { audio: null, video: null }, history: [], prev: null,
  startedAt: performance.now(),
});

// An estimated MOS from the ITU-T G.107 E-model, simplified: the delay is half the round trip
// (40 ms if there isn't one) plus a jitter buffer of twice the jitter plus 20 ms for the codec,
// and loss is taken as random, with packet loss concealment (Bpl 25). Wideband codecs sound
// better than this says; it's for spotting trouble, not for grading codecs.
function mos({ lossPct = 0, jitterMs = 0, rttMs = null }) {
  const delay = (rttMs ?? 40) / 2 + jitterMs * 2 + 20;
  const id = delay < 160 ? delay / 40 : (delay - 120) / 10;
  const ie = (95 * lossPct) / (lossPct + 25);
  const r = Math.max(0, Math.min(100, 93.2 - id - ie));
  return 1 + 0.035 * r + 7e-6 * r * (r - 60) * (100 - r);
}
const mosColour = (m) => (m == null ? "#677670" : m >= 4 ? "#4cc29b" : m >= 3.6 ? "#e6b86a" : "#e0718a");

// A strip chart series, the newest at the right: a line, or bars from the bottom. Gaps (null) break the line.
function drawSeries(g, x, y, w, h, { values, min = 0, max, colour, bars = false }) {
  const slot = w / Q_POINTS, start = x + w - values.length * slot;
  const at = (v) => y + h - (Math.max(min, Math.min(max, v)) - min) / (max - min) * h;
  g.fillStyle = g.strokeStyle = colour;
  g.lineWidth = Math.max(2, h / 40);
  g.lineJoin = "round";
  g.beginPath();
  let drawing = false;
  values.forEach((v, i) => {
    const cx = start + i * slot + slot / 2;
    if (v == null) { drawing = false; return; }
    if (bars) { if (v > 0) g.fillRect(cx - slot * 0.35, at(v), slot * 0.7, y + h - at(v)); return; }
    if (drawing) g.lineTo(cx, at(v)); else g.moveTo(cx, at(v));
    drawing = true;
  });
  if (!bars) g.stroke();
}

function onQuality(msg) {
  if (!call || state.call !== "connected" || (msg.kind !== "audio" && msg.kind !== "video")) return;
  quality ??= newQuality();
  if (msg.dir === "sent") {
    quality.sent[msg.kind] = { ...msg, mos: msg.kind === "audio" ? mos(msg) : null };
    log(`their RTCP on our ${msg.kind}: loss ${msg.lossPct}% (${msg.lost} total), jitter ${msg.jitterMs} ms, rtt ${msg.rttMs ?? "-"} ms`);
  } else if (msg.kind === "video") {
    quality.recvVideoRtcp = msg;
    log(`our RTCP on their video: loss ${msg.lossPct}% (${msg.lost} total), jitter ${msg.jitterMs} ms`);
  }
  renderQuality();
}

// Once a second during a call: how we're receiving, from the page's WebRTC stats, and a point
// for the graph (the sent side holds its latest report until the next).
setInterval(async () => {
  if (!pc || !call || state.call !== "connected") return;
  quality ??= newQuality();
  const q = quality;
  let a = null, v = null, out = null, outA = null;
  const codecs = new Map();
  (await pc.getStats()).forEach((r) => {
    if (r.type === "codec") codecs.set(r.id, r);
    if (r.type === "outbound-rtp") { if (r.kind === "video") out = r; else if (r.kind === "audio") outA = r; }
    if (r.type !== "inbound-rtp") return;
    if (r.kind === "audio") a = r; else if (r.kind === "video") v = r;
  });
  // The codecs each way, for Call details.
  const codecOf = (r) => codecLabel(r && codecs.get(r.codecId));
  // Video with its frame size, e.g. "VP8 1280×720": what we're sending and what's arriving (on
  // the desktop the relay passes frames on as they are, so these are the SIP leg's too).
  const videoOf = (r) => codecOf(r) && (r.frameWidth && r.frameHeight ? `${codecOf(r)} ${r.frameWidth}×${r.frameHeight}` : codecOf(r));
  callCodecs = { audio: { tx: codecOf(outA), rx: codecOf(a) }, video: { tx: videoOf(out), rx: videoOf(v) } };
  if (!$("details").hidden) renderDetails();
  const p = q.prev;
  q.prev = { a, v, out };
  if (!p) return;
  if (a) {
    // Loss over the last 5 s or so: the browser only updates packetsLost when it builds its own
    // RTCP report, every few seconds, so a 1 s difference is mostly 0 with the odd spike.
    const win = (q.audioWindow ??= []);
    win.push({ t: a.timestamp, received: a.packetsReceived, lost: a.packetsLost });
    while (win.length > 2 && a.timestamp - win[1].t >= 5000) win.shift();
    const got = a.packetsReceived - win[0].received, lost = Math.max(0, a.packetsLost - win[0].lost);
    const audio = { lossPct: got + lost > 0 ? (lost * 100) / (got + lost) : 0, jitterMs: (a.jitter ?? 0) * 1000, lost: Math.max(0, a.packetsLost) };
    // Our round trip is theirs: the same path both ways.
    audio.mos = mos({ ...audio, rttMs: q.sent.audio?.rttMs ?? null });
    q.recv.audio = audio;
  }
  q.recv.video = !call.hasVideo ? null : {
    lossPct: q.recvVideoRtcp?.lossPct ?? null,
    lost: q.recvVideoRtcp?.lost ?? null,
    // No inbound video stats at all means nothing has ever arrived: 0 fps.
    fps: v && p.v ? (v.framesDecoded - p.v.framesDecoded) / ((v.timestamp - p.v.timestamp) / 1000) : v ? null : 0,
  };
  // Sent video's frame rate is what we send: RTCP doesn't say what they see, and the relay
  // passes every frame the page encodes on to the call.
  q.sentFps = call.hasVideo && out && p.out ? (out.framesEncoded - p.out.framesEncoded) / ((out.timestamp - p.out.timestamp) / 1000) : null;
  // PLIs, for the graph's ticks, from the page's stats (the relay passes them straight through):
  // received are the far end's, which reached the page's encoder; sent are the page's own. Both
  // show on either column.
  const pli = (cur, prev) => !!cur && !!prev && (cur.pliCount ?? 0) > (prev.pliCount ?? 0);
  const point = (side) => ({ mos: side.audio?.mos ?? null, aLoss: side.audio?.lossPct ?? null, vLoss: side.video?.lossPct ?? null });
  q.history.push({
    sent: point(q.sent), recv: point(q.recv),
    pliRx: call.hasVideo && pli(out, p.out), pliTx: call.hasVideo && pli(v, p.v),
  });
  if (q.history.length > Q_POINTS) q.history.shift();
  // Freezes in what we receive: the browser's own measure of the picture stopping (a gap between
  // frames of over 3 times the usual, or 150 ms more). It only adds a freeze once it has ended,
  // so the seconds it covered are marked then, going back from now.
  if (v && p.v && (v.totalFreezesDuration ?? 0) > (p.v.totalFreezesDuration ?? 0)) {
    const secs = Math.max(1, Math.round(v.totalFreezesDuration - p.v.totalFreezesDuration));
    q.history.slice(-secs).forEach((h) => { h.recv.frozen = true; });
  }
  renderQuality();
}, 1000);

const Q_COLOUR = { mos: "#4cc29b", aLoss: "#e0718a", vLoss: "#7fb2f0", frozen: "#ffffff", pliRx: "#e6b86a", pliTx: "#c39af5" };

const PLI_POINTS = 30;   // the PLI strip shows the last half minute: it's an indicator, not a history

// Marks, one per second, the newest at the right: PLI ticks, or (solid) frozen bars.
function drawMarks(g, x, y, w, h, values, colour, solid = false, points = Q_POINTS) {
  values = values.slice(-points);
  const slot = w / points, start = x + w - values.length * slot, tick = solid ? slot : Math.max(2, slot * 0.6);
  g.fillStyle = colour;
  values.forEach((on, i) => { if (on) g.fillRect(start + i * slot + (slot - tick) / 2, y, tick, h); });
}
const Q_LABEL = { sent: "↑ Sent", recv: "↓ Received" };
const Q_BY = { sent: "Reported by them", recv: "Measured by us" };   // the graph's title, by direction

function renderQuality() {
  const el = $("quality");
  el.hidden = !(state.call === "connected" && call);
  if (el.hidden || !document.body.classList.contains("diag")) return;
  const q = quality ?? newQuality();
  const video = call.hasVideo;
  const fmt = (v, unit = "", digits = 1) => (v == null ? "–" : `${v.toFixed(digits)}${unit}`);
  const esc = (t) => t.replace(/"/g, "&quot;");

  // One value cell: in its column, marked when that column drives the graph.
  const cell = (col, text, { cls = "", title = "", colour = "" } = {}) =>
    `<span class="q-v ${cls}${col === qColumn ? " sel" : ""}" data-col="${col}"${title ? ` title="${esc(title)}"` : ""}${colour ? ` style="color:${colour}"` : ""}>${text}</span>`;
  const from = { sent: "their RTCP reports", recv: "measured here" };
  const mosCell = (col, r) => cell(col, fmt(r?.mos), { cls: "mos", colour: r ? mosColour(r.mos) : "", title: `Estimated MOS (1–5), ${from[col]}` });
  const lossCell = (col, r, what) => cell(col, fmt(r?.lossPct, "%"), {
    cls: r?.lossPct >= 1 ? "bad" : "",
    title: `${what} packets lost${r?.lost != null ? `: ${r.lost} in total` : ""}, ${col === "recv" && what === "Video" ? "from our RTCP" : from[col]}`,
  });
  const jitterCell = (col, r) => cell(col, fmt(r?.jitterMs, " ms", 0), { title: `Audio jitter, ${from[col]}` });
  const row = (stream, metric, sent, recv, next = false) =>
    `<span class="q-stream${next ? " next" : ""}">${stream}</span><span class="q-metric">${metric}</span>${sent}${recv}`;

  const S = q.sent, R = q.recv;
  let html = `<span></span><span></span>` +
    Object.entries(Q_LABEL).map(([col, label]) =>
      `<span class="q-col${col === qColumn ? " sel" : ""}" title="${label.slice(2)}: ${from[col]}">${label}</span>`).join("") +
    row("Audio", "MOS", mosCell("sent", S.audio), mosCell("recv", R.audio)) +
    row("", "loss", lossCell("sent", S.audio, "Audio"), lossCell("recv", R.audio, "Audio")) +
    row("", "jitter", jitterCell("sent", S.audio), jitterCell("recv", R.audio));
  if (video) {
    html += row("Video", "loss", lossCell("sent", S.video, "Video"), lossCell("recv", R.video, "Video"), true) +
      row("", "fps", cell("sent", fmt(q.sentFps, "", 0), { title: "Frames per second we send (RTCP doesn't report what they see)" }),
        cell("recv", fmt(R.video?.fps, "", 0), { title: "Frames per second decoded here" }));
  }
  $("qTable").innerHTML = html;

  // Folded: just the two MOS.
  const sum = (col, r) => `<span style="color:${mosColour(r?.mos)}" title="${Q_LABEL[col]}: estimated MOS">${Q_LABEL[col][0]} ${fmt(r?.mos)}</span>`;
  $("qSum").innerHTML = sum("sent", S.audio) + sum("recv", R.audio);

  // Round trip: just the value in the folded line, labelled at the foot when open, where it's
  // otherwise why ↑ Sent is empty. (What each value means belongs in a help or info section
  // later; there's no room for it here.)
  const rtt = S.audio?.rttMs ?? S.video?.rttMs;
  const rttText = rtt == null ? "" : fmt(rtt, " ms", rtt < 10 ? 1 : 0);
  $("qRtt").innerHTML = rttText && `${ICON.roundTrip}${rttText.replace(" ", "")}`;
  $("qRtt").title = "Round trip, from their RTCP reports";
  const waited = performance.now() - q.startedAt;
  $("qFoot").textContent = S.audio || S.video ? (rttText && `RTT ${rttText}`)
    : waited > NO_RTCP_MS ? "No RTCP reports from them: their phone may not send them"
    : "Waiting for their RTCP reports";

  // The graph: the picked column over the last two minutes. Either way it's the receiver's
  // view: theirs of what we send, or ours of what they send.
  $("qGraphCol").textContent = Q_BY[qColumn];
  for (const b of $("qSwitch").children) {
    b.classList.toggle("on", b.dataset.col === qColumn);
    b.setAttribute("aria-checked", String(b.dataset.col === qColumn));
  }
  const series = [["mos", "Audio MOS"], ["aLoss", "Audio loss"],
    ...(video ? [["vLoss", "Video loss"], ...(qColumn === "recv" ? [["frozen", "Frozen"]] : [])] : [])];
  $("qLegend").innerHTML = series.map(([k, label]) => `<span style="--c:${Q_COLOUR[k]}">${label}</span>`).join("");
  const c = $("qChart"), g = c.getContext("2d");
  c.setAttribute("aria-label", `${Q_LABEL[qColumn]}, ${Q_BY[qColumn].toLowerCase()}, last 2 minutes: ${series.map(([, l]) => l).join(", ")}`);
  g.clearRect(0, 0, c.width, c.height);
  g.fillStyle = "rgba(255,255,255,.12)";
  for (const level of [4, 3]) g.fillRect(0, c.height - ((level - 1) / 4) * c.height, c.width, 1);
  const pts = q.history.map((h) => h[qColumn]);
  // Loss on 0–20%, MOS on 1–5.
  drawSeries(g, 0, 0, c.width, c.height, { values: pts.map((h) => h.aLoss), max: 20, colour: Q_COLOUR.aLoss, bars: true });
  if (video) drawSeries(g, 0, 0, c.width, c.height, { values: pts.map((h) => h.vLoss), max: 20, colour: Q_COLOUR.vLoss });
  drawSeries(g, 0, 0, c.width, c.height, { values: pts.map((h) => h.mos), min: 1, max: 5, colour: Q_COLOUR.mos });
  // On Received, along the bottom: how long our picture was frozen. (RTCP doesn't say whether
  // their picture froze, so Sent has none.)
  if (video && qColumn === "recv") {
    const tick = c.height / 14;
    drawMarks(g, 0, c.height - tick, c.width, tick, pts.map((h) => h.frozen), Q_COLOUR.frozen, true);
  }

  // PLIs, both ways, whichever column is picked: a tick for each second with one, received on
  // the top row and sent below, over the last half minute.
  $("qPli").hidden = !video;
  if (video) {
    $("qPliRxKey").style.setProperty("--c", Q_COLOUR.pliRx);
    $("qPliTxKey").style.setProperty("--c", Q_COLOUR.pliTx);
    const pc2 = $("qPliChart"), pg = pc2.getContext("2d"), rowH = 12, pad = (pc2.height - rowH * 2) / 3;
    pg.clearRect(0, 0, pc2.width, pc2.height);
    drawMarks(pg, 0, pad, pc2.width, rowH, q.history.map((h) => h.pliRx), Q_COLOUR.pliRx, false, PLI_POINTS);
    drawMarks(pg, 0, pad * 2 + rowH, pc2.width, rowH, q.history.map((h) => h.pliTx), Q_COLOUR.pliTx, false, PLI_POINTS);
  }
}

// The switch picks the direction the graph shows.
$("qSwitch").addEventListener("click", (e) => {
  const col = e.target.closest("[data-col]")?.dataset.col;
  if (col && col !== qColumn) { qColumn = col; renderQuality(); }
});

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
  $("info").hidden = phase !== "connected";
  $("infoName").textContent = remote || "";

  // The Audio | Video switch beside Call. Locked to Audio if video is off (--no-video).
  const camAvailable = hasCamera || videoSource !== "camera";
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
    log(`stage: ${mode}`);
    if (mode === "audio") requestAnimationFrame(drawViz);
  }
  $("nocamInitial").textContent = initial(name);
  $("nocamNote").textContent = connected && Date.now() - connectedAt > NO_FRAMES_MS ? `No video arriving from ${name}` : "";
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

  // Self view: only for video, and always what they see (or will see). On a video call that's
  // your camera, or the placeholder while it's off; before a call it's a preview while the
  // switch is on Video. A note on top says why the camera isn't showing. Audio shows none.
  const before = phase === "idle" || phase === "ended" || phase === "calling";
  // Self view hides it, before a call (the preview with the switch on Video) or during a video
  // call; on a call what's sent doesn't change. The button is there whenever there's a self view.
  const selfViewApplies = connected ? call.hasVideo : before && type === "video";
  const showSelf = !!local && selfViewApplies && selfViewOn;
  const sv = $("selfViewBtn");
  sv.hidden = !selfViewApplies;
  sv.setAttribute("aria-pressed", String(selfViewOn));
  sv.title = connected ? `${selfViewOn ? "Hide" : "Show"} your self view (V). They still see you.`
    : `${selfViewOn ? "Hide" : "Show"} your camera preview (V)`;
  if (!sv.innerHTML) sv.innerHTML = `${ICON.selfView}<span>Self view</span>`;
  const camOn = connected ? call.camOn : camPref;
  const selfText = !showSelf ? null : !camAvailable ? "No camera" : cameraInUse && videoSource === "camera" ? "Camera in use"
    : !camOn ? "Camera off" : null;
  showSelfVideo(showSelf);
  $("selfOff").hidden = !selfText;
  $("selfOffText").textContent = selfText ?? "";
  if (cameraWanted() !== !!localTrack("video")) syncCamera();

  // The control bar, under the stage the whole time, so you can set up your microphone and
  // camera before a call. Mute and Camera are split buttons: the ▾ picks the device.
  const setBtn = (el, icon, text) => {
    const html = `${icon}<span>${text}</span>`;
    if (el.innerHTML !== html) el.innerHTML = html;
  };
  const muted = micMuted;
  const mute = $("muteBtn");
  mute.disabled = !hasMic;
  mute.setAttribute("aria-pressed", String(hasMic && muted));
  // What pressing it does, as on the camera button; red while you're muted.
  mute.title = !hasMic ? "No mic found"
    : live ? (muted ? "Unmute: they'll hear you" : "Mute: they won't hear you")
    : muted ? "Calls start muted" : "Calls start unmuted";
  setBtn(mute, muted || !hasMic ? ICON.micOff : ICON.mic, !hasMic ? "No mic" : muted ? "Unmute" : "Mute");
  // While muted on a call, a pill by the timer says so.
  const pill = $("mutedPill");
  pill.hidden = !(live && hasMic && muted);
  if (!pill.hidden && !pill.innerHTML) pill.innerHTML = `${ICON.micOff}<span>You're muted</span>`;

  // Camera: during a video call it turns your camera on and off; on an audio call it's Add video,
  // unless video can't be had. Before a call it decides whether a video call starts with your
  // camera on (and the preview shows it).
  const cam = $("camBtn");
  const canAdd = connected && !call.hasVideo && videoAllowed && !videoRefused;
  const ringing = phase === "ringing";
  if (connected && !call.hasVideo) {
    cam.disabled = !canAdd || addingVideo;
    cam.setAttribute("aria-pressed", "false");
    setBtn(cam, canAdd ? ICON.video : ICON.videoOff, addingVideo ? "Adding video…" : canAdd ? "Add video" : "No video");
    cam.title = canAdd ? "Add video to this call. They'll be asked; some phones can't."
      : videoRefused ? "Their phone doesn't do video" : "Video is turned off";
  } else {
    // Before a call on Audio it isn't disabled: pressing the camera means you want video, so it
    // switches the call to Video with the camera on (see its onclick).
    const audioBefore = !connected && type === "audio";
    const on = connected ? call.camOn : camPref && !audioBefore;
    cam.disabled = !camAvailable || !videoAllowed || (phase === "connected" && !connected) || ringing;
    cam.setAttribute("aria-pressed", String(camAvailable && videoAllowed && !on && !audioBefore));
    setBtn(cam, camAvailable && on ? ICON.video : ICON.videoOff,
      // What pressing it does, as on the mic button: the video, whatever its source.
      !videoAllowed ? "No video" : !camAvailable ? "No camera" : on ? "Stop video" : "Start video");
    cam.title = !videoAllowed ? "Video is turned off" : !camAvailable ? "No camera found"
      : ringing ? "Choose the camera with the Answer button"
      : audioBefore ? "Make it a video call, with your camera on"
      : connected ? (on ? "Stop video: they'll see your initial" : "Start video: turn your camera on")
      : on ? "Video calls start with your camera on" : "Video calls start with your camera off";
  }
  $("camMenuBtn").disabled = !videoAllowed || ringing || (!connected && type === "audio");

  renderTextControls(phase);
  const hang = $("hangupBtn");
  hang.disabled = phase !== "calling" && phase !== "connected";
  setBtn(hang, ICON.hangup, phase === "calling" ? "Cancel" : "End");
  for (const b of [$("micMenuBtn"), $("camMenuBtn")]) if (!b.innerHTML) b.innerHTML = ICON.chevronDown;

  // The card: everything but a connected call.
  const actions = $("cardActions");
  actions.replaceChildren();
  const title = $("cardTitle"), sub = $("cardSub");
  const button = (cls, text, onclick) => Object.assign(document.createElement("button"), { className: `btn ${cls}`, textContent: text, onclick });
  switch (phase) {
    case "idle":
      title.textContent = "Ready";
      sub.textContent = "Type an address above to start a call.";
      // Desktop: say why the account calls go out from isn't registered (Manage accounts has more).
      if (state.accounts && state.registration === "failed")
        sub.textContent = `${state.account} isn't registered: ${state.registrationDetail || "no reason given"}.`;
      break;
    case "calling":
      title.textContent = remote;
      sub.textContent = "Calling…";
      break;
    case "ringing": {
      title.textContent = remote;
      sub.textContent = state.incomingVideo ? "Incoming video call" : "Incoming call";
      actions.append(button("btn-danger", "Decline", () => post({ type: "decline" })), answerSplit);
      renderAnswer();
      if (lastPhase !== "ringing") $("answerBtn").focus();
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

  if (phase !== "ringing" && openMenu === "answer") closeMenu();
  // A choice from the Answer menu is for that call only (onMediaOffer uses it, then clears it);
  // one that wasn't answered mustn't carry over to the next.
  if (phase !== "ringing" && phase !== "connected") answerCamera = null;
  lastPhase = phase;
  renderQuality();

  if (phase === "connected" && !connectedAt) {
    connectedAt = Date.now();
    // A new call starts a new conversation.
    textLines = [];
    textUnread = textBomSent = textSaid = textOpen = false;
    hideToast();
    $("rttInput").value = "";
    $("rttSend").disabled = true;
    renderText();
    if (testAddVideo) setTimeout(() => call && !call.hasVideo && $("camBtn").click(), testAddVideo * 1000);
    timerId = setInterval(tick, 1000);
    tick();
  } else if (phase === "idle" || phase === "ended") {
    // Not while calling or ringing: in the web softphone the page's connection is the call's
    // own, and it's set up (offer or answer) before the call connects.
    clearInterval(timerId);
    connectedAt = 0;
    addingVideo = videoRefused = false;
    quality = null;
    callDetails = callCodecs = null;
    // The next call starts with Call details and Call quality closed.
    if (!$("details").hidden) setDetailsOpen(false);
    if (!$("quality").classList.contains("collapsed")) setQualityOpen(false);
    if (pc) closeMedia();
  }

  // The header: who you are, and a click copies your SIP address for the other side to dial.
  const reg = $("reg");
  const copied = performance.now() < copiedUntil;
  reg.className = `reg diag-only ${state.registration}${copied ? " copied" : ""}`;
  $("regText").textContent = copied ? "Copied"
    : !state.account ? (state.address ? `No account · ${state.address}` : "No account · direct calls only")
    : state.registration === "registered" ? state.account
    : state.registration === "failed" ? `${state.account} · ${state.registrationDetail || "not registered"}`
    : `${state.account} · registering…`;
  const icon = copied ? ICON.check : ICON.copy;
  if ($("regCopy").dataset.icon !== icon) { $("regCopy").innerHTML = icon; $("regCopy").dataset.icon = icon; }
  reg.disabled = !state.address;
  reg.title = !state.address ? "" : `Copy ${state.address}`
    + (state.account ? "" : " (no account: direct calls only)")
    + (state.registrationDetail ? `. ${state.registrationDetail}` : "");
  renderCalls();
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
      `video out ${fps("outbound-rtp-video", "framesEncoded")} fps, audio in ${fps("inbound-rtp-audio", "packetsReceived")} pkt/s lost ${ain?.packetsLost ?? "-"} concealed ${ain?.concealedSamples ?? "-"} level ${(ain?.audioLevel ?? 0).toFixed(3)}, ` +
      `audio out ${fps("outbound-rtp-audio", "packetsSent")} pkt/s, stage ${$("stage").dataset.mode}`);
  lastStats = now;
}

function tick() {
  const s = Math.floor((Date.now() - connectedAt) / 1000);
  if (s > 0 && s % 5 === 0) statsLine();
  $("timer").textContent = `${String(Math.floor(s / 60)).padStart(2, "0")}:${String(s % 60).padStart(2, "0")}`;
  renderCallTimes();
}

// ---- Active calls panel, down the left ----
// The hosts carry one call at a time, so today the list is that call while it's live, built
// from the host's state. Each entry has its own id, so more calls can be listed later. Collapsed,
// it's a rail of avatars and timers; the choice is remembered. Under 700 px it's always the
// rail, and expanding it lays the panel over the stage until it's collapsed or you click away.

const CALLS_ICON = {
  collapse: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="m15 18-6-6 6-6"/></svg>',
  expand: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="m9 18 6-6-6-6"/></svg>',
};
const narrowCalls = matchMedia("(max-width: 699px)");
let callsCollapsed = false;   // your choice on wider windows
let callsPeek = false;        // narrow windows: the panel open over the stage, for now
try { callsCollapsed = localStorage.getItem("callsPanel") === "collapsed"; } catch { /* storage unavailable */ }

const fmtElapsed = (ms) => {
  const s = Math.max(0, Math.floor(ms / 1000));
  const h = Math.floor(s / 3600), m = Math.floor(s / 60) % 60, ss = String(s % 60).padStart(2, "0");
  return h ? `${h}:${String(m).padStart(2, "0")}:${ss}` : `${String(m).padStart(2, "0")}:${ss}`;
};

// The calls to list: { id, name, remote, status, statusText, since, muted, media: [[icon, class, label]] }.
function panelCalls() {
  const { call: phase, remote } = state;
  if (phase !== "calling" && phase !== "ringing" && phase !== "connected") return [];
  const entry = { id: "current", name: shortName(remote), remote: remote || "", status: phase, since: 0, muted: false, media: [] };
  if (phase === "calling") entry.statusText = "Calling…";
  else if (phase === "ringing") entry.statusText = state.incomingVideo ? "Incoming video call" : "Incoming call";
  else if (!call) entry.statusText = "Connecting media…";
  else {
    entry.since = connectedAt;
    entry.muted = hasMic && micMuted;
    entry.statusText = entry.muted ? "Muted" : call.hasVideo ? "Video call" : "Audio call";
    entry.media.push(!hasMic ? ["micOff", "idle", "No mic"] : micMuted ? ["micOff", "off", "Muted"] : ["mic", "live", "Your mic is live"]);
    entry.media.push(!call.hasVideo ? ["videoOff", "idle", "Audio only"]
      : call.camOn ? ["video", "live", "Sending your video"] : ["videoOff", "off", "Video stopped"]);
  }
  return [entry];
}

function renderCalls() {
  const calls = panelCalls();
  const narrow = narrowCalls.matches;
  if (!narrow) callsPeek = false;
  const collapsed = narrow ? !callsPeek : callsCollapsed;
  const panel = $("calls");
  panel.classList.toggle("collapsed", collapsed);
  panel.classList.toggle("peek", narrow && callsPeek);
  panel.title = collapsed ? "Show active calls" : "";
  const toggle = $("callsToggle");
  toggle.setAttribute("aria-expanded", String(!collapsed));
  toggle.setAttribute("aria-label", collapsed ? "Show active calls" : "Collapse active calls");
  toggle.title = collapsed ? "Show active calls" : "Collapse active calls";
  const icon = collapsed ? CALLS_ICON.expand : CALLS_ICON.collapse;
  if (toggle.innerHTML !== icon) toggle.innerHTML = icon;
  $("callCount").textContent = calls.length || "";

  const list = $("callList");
  list.replaceChildren(...(calls.length ? calls.map(callRow) : [Object.assign(document.createElement("li"), { className: "call-empty", textContent: "No active calls" })]));
}

function callRow(c) {
  const li = document.createElement("li");
  li.className = `call-row ${c.status}${c.muted ? " muted" : ""}`;
  li.dataset.call = c.id;
  li.setAttribute("aria-current", "true");   // the call on the stage; with one call, always
  li.title = [c.remote || c.name, c.statusText].join(" · ");
  const el = (tag, cls, text) => Object.assign(document.createElement(tag), { className: cls, textContent: text ?? "" });
  const avatar = el("span", "call-avatar", initial(c.name));
  avatar.setAttribute("aria-hidden", "true");
  const top = el("span", "call-top");
  top.append(el("span", "call-name", c.name));
  if (c.media.length) {
    const media = el("span", "call-media");
    for (const [ico, cls, label] of c.media) {
      const m = el("span", `media-ico ${cls}`);
      m.title = label;
      m.setAttribute("role", "img");
      m.setAttribute("aria-label", label);
      m.innerHTML = ICON[ico];
      media.append(m);
    }
    top.append(media);
  }
  const main = el("span", "call-main");
  main.append(top, el("span", "call-status", c.statusText));
  const time = el("span", "call-time", c.since ? fmtElapsed(Date.now() - c.since) : "");
  if (c.since) time.dataset.since = c.since;
  li.append(avatar, main, time);
  return li;
}

// Every second while connected: just the timers.
function renderCallTimes() {
  for (const t of document.querySelectorAll("#callList .call-time[data-since]")) t.textContent = fmtElapsed(Date.now() - Number(t.dataset.since));
}

function setCallsCollapsed(collapsed) {
  if (narrowCalls.matches) callsPeek = !collapsed;
  else {
    callsCollapsed = collapsed;
    try { localStorage.setItem("callsPanel", collapsed ? "collapsed" : "open"); } catch { /* storage unavailable */ }
  }
  renderCalls();
}
$("callsToggle").onclick = (e) => { e.stopPropagation(); setCallsCollapsed(!$("calls").classList.contains("collapsed")); };
// The whole rail expands it.
$("calls").addEventListener("click", () => { if ($("calls").classList.contains("collapsed")) setCallsCollapsed(false); });
// Narrow windows: clicking away or Esc puts the panel back to the rail.
document.addEventListener("pointerdown", (e) => { if (callsPeek && !e.target.closest("#calls")) { callsPeek = false; renderCalls(); } });
document.addEventListener("keydown", (e) => { if (e.key === "Escape" && callsPeek) { callsPeek = false; renderCalls(); } });
narrowCalls.addEventListener("change", renderCalls);

// ---- Controls ----

$("dialForm").addEventListener("submit", (e) => {
  e.preventDefault();
  const address = $("address").value.trim();
  if (address) {
    openMic();   // alongside the INVITE, so it's ready when the call connects
    post({ type: "dial", address, video: state.videoAllowed !== false && callType === "video" });
    // If the call never starts (a bad address, say), don't leave the microphone open.
    setTimeout(() => { if (state.call === "idle" || state.call === "ended") closeMic(); }, 5000);
  }
});
$("callType").querySelector('[data-type="audio"]').innerHTML = `${ICON.phone}Audio`;
$("callType").querySelector('[data-type="video"]').innerHTML = `${ICON.video}Video`;
function setCallType(type) {
  callType = type;
  if (type === "video") cameraInUse = false;   // a fresh try for the preview, because you asked
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
// ---- Device menus (the ▾ on Mute and Camera) ----
// Mute's lists microphones and speakers; Camera's lists cameras, and in diagnostics mode the
// test pattern. A pick takes effect straight away, mid-call too (a track swap, no renegotiation),
// and is remembered for next time.

let openMenu = null;   // "mic", "cam", "answer" or null
const MENUS = { mic: ["micMenu", "micMenuBtn"], cam: ["camMenu", "camMenuBtn"], answer: ["answerMenu", "answerMenuBtn"] };
let cameras = [];      // the video inputs, for the Answer button's label; refreshed when devices change
let lastPhase = null;  // the call phase at the last render, to focus Answer once when ringing starts

async function refreshCameras() {
  try {
    const list = await navigator.mediaDevices.enumerateDevices();
    cameras = list.filter((d) => d.kind === "videoinput" && d.deviceId);
    hasMic = list.some((d) => d.kind === "audioinput");
  } catch { /* keep the old list */ }
  render();
}
navigator.mediaDevices.addEventListener?.("devicechange", refreshCameras);

// ---- Answering a video call: Answer ▾ ----
// The ▾ picks what your video will be: a camera, the test pattern (diagnostics mode), or camera
// off. Under "Answer" the button says which, so you know before you answer. It starts from the
// Camera button's choice (camPref), so camera off there means camera off here.
const answerSplit = document.createElement("div");
answerSplit.className = "split answer-split";
answerSplit.innerHTML = `<button type="button" class="btn btn-primary" id="answerBtn"><span class="answer-main"></span><span class="answer-src"></span></button>
  <button type="button" class="btn btn-primary split-caret" id="answerMenuBtn" aria-haspopup="menu" aria-expanded="false"
    aria-label="Choose your camera for this call" title="Choose your camera for this call">${ICON.chevronDown}</button>
  <div class="device-menu down" id="answerMenu" role="menu" aria-label="Your camera" hidden></div>`;
answerSplit.querySelector("#answerBtn").onclick = () => { closeMenu(); openMic(); post({ type: "answer" }); };
answerSplit.querySelector("#answerMenuBtn").onclick = () => showMenu("answer");

const answerCamOn = () => (answerCamera ?? camPref) && videoAvailable();
function answerSourceLabel() {
  if (!answerCamOn()) {
    return videoSource === "camera" && !hasCamera ? "No camera"
      : videoSource === "camera" && cameraInUse ? "Camera in use" : "Camera off";
  }
  if (videoSource !== "camera") return SOURCE_LABEL[videoSource];
  return cameras.find((d) => d.deviceId === currentCameraId(cameras))?.label || "Camera";
}

function renderAnswer() {
  const video = !!state.incomingVideo && state.videoAllowed !== false;
  const on = video && answerCamOn();
  const main = `${video ? (on ? ICON.video : ICON.videoOff) : ICON.phone}<span>Answer</span>`;
  const el = answerSplit.querySelector(".answer-main");
  if (el.innerHTML !== main) el.innerHTML = main;
  const src = answerSplit.querySelector(".answer-src");
  src.hidden = !video;
  src.textContent = video ? answerSourceLabel() : "";
  answerSplit.querySelector("#answerBtn").title = !video ? "Answer"
    : on ? `Answer with ${src.textContent}` : "Answer with your camera off. You'll still see them.";
  answerSplit.querySelector("#answerMenuBtn").hidden = !video;
  answerSplit.classList.toggle("single", !video);
}

const currentMicId = () => localTrack("audio")?.getSettings().deviceId ?? (devices.mic || "default");
function currentCameraId(cameras) {
  const track = localTrack("video");
  if (track && !isTestTrack(track)) return track.getSettings().deviceId;
  return cameras.some((d) => d.deviceId === devices.camera) ? devices.camera : cameras[0]?.deviceId;
}

async function showMenu(kind) {
  if (openMenu === kind) { closeMenu(); return; }
  closeMenu();
  openMenu = kind;
  let list = [];
  try { list = await navigator.mediaDevices.enumerateDevices(); } catch { /* none to show */ }
  if (openMenu !== kind) return;
  const of = (k) => list.filter((d) => d.kind === k && d.deviceId);
  const groups = kind === "mic"
    ? [["mic", "Microphone", of("audioinput"), currentMicId()],
       ["speaker", "Speaker", of("audiooutput"), devices.speaker || "default"]]
    : [["camera", "Camera", of("videoinput"), videoSource !== "camera" ? null : currentCameraId(of("videoinput"))]];
  if (kind !== "mic" && document.body.classList.contains("diag")) {
    groups.push(["test", "Diagnostics", Object.entries(SOURCE_LABEL).map(([deviceId, label]) => ({ deviceId, label })),
      videoSource !== "camera" ? videoSource : null]);
  }
  if (kind === "answer") {
    // While the camera's off nothing above is ticked; "Camera off" is instead.
    const on = answerCamOn();
    if (!on) for (const g of groups) g[3] = null;
    groups.push(["off", "", [{ deviceId: "off", label: "Camera off (you'll still see them)" }], on ? null : "off"]);
  }
  const menu = $(MENUS[kind][0]);
  menu.replaceChildren();
  groups.forEach(([key, label, items, current], gi) => {
    if (gi) menu.append(Object.assign(document.createElement("div"), { className: "device-menu-sep" }));
    if (label) menu.append(Object.assign(document.createElement("div"), { className: "device-menu-label", textContent: label }));
    if (!items.length) menu.append(Object.assign(document.createElement("div"), { className: "device-none", textContent: "None found" }));
    items.forEach((d, i) => {
      const on = d.deviceId === current;
      const opt = Object.assign(document.createElement("button"), { type: "button", className: "device-option" });
      opt.setAttribute("role", "menuitemradio");
      opt.setAttribute("aria-checked", String(on));
      opt.innerHTML = `<span class="device-check">${on ? ICON.check : ""}</span><span></span>`;
      opt.lastChild.textContent = d.label || `${label} ${i + 1}`;
      opt.onclick = () => pickDevice(key, d.deviceId, kind === "answer");
      menu.append(opt);
    });
  });
  menu.hidden = false;
  if (kind === "answer") fitInStage(menu);
  $(MENUS[kind][1]).setAttribute("aria-expanded", "true");
  (menu.querySelector('[aria-checked="true"]') ?? menu.querySelector(".device-option"))?.focus();
}

// The Answer menu is inside the stage, which clips (overflow: hidden), so a z-index can't lift
// it out. It's positioned against the window instead (position: fixed isn't clipped by the
// stage), below the button if it fits, else above, else on the roomier side, scrolling.
function fitInStage(menu) {
  const btn = menu.parentElement.getBoundingClientRect();
  const margin = 8, gap = 6;
  Object.assign(menu.style, { position: "fixed", maxHeight: "", top: "", bottom: "", left: `${btn.left}px` });
  const height = menu.scrollHeight + 2;
  const below = innerHeight - btn.bottom - gap - margin;
  const above = btn.top - gap - margin;
  const down = height <= below || (height > above && below >= above);
  menu.style.top = down ? `${btn.bottom + gap}px` : "auto";
  menu.style.bottom = down ? "auto" : `${innerHeight - btn.top + gap}px`;
  const room = down ? below : above;
  if (height > room) menu.style.maxHeight = `${Math.max(room, 80)}px`;
  menu.style.left = `${Math.max(margin, Math.min(btn.left, innerWidth - menu.offsetWidth - margin))}px`;
}
addEventListener("resize", () => { if (openMenu === "answer") closeMenu(); });

function closeMenu() {
  if (!openMenu) return;
  const kind = openMenu;
  openMenu = null;
  $(MENUS[kind][0]).hidden = true;
  $(MENUS[kind][1]).setAttribute("aria-expanded", "false");
}

// From the Answer menu (forAnswer), a camera or the test pattern also means camera on for the
// call being answered, and "off" means camera off.
async function pickDevice(key, id, forAnswer = false) {
  closeMenu();
  if (forAnswer) answerCamera = key !== "off";
  if (key === "off") log("answering with camera off");
  else if (key === "mic") await pickMic(id);
  else if (key === "speaker") {
    devices.speaker = id;
    saveDevices();
    try { await $("remote").setSinkId(id); log(`speaker: ${id}`); } catch (err) { log(`speaker unavailable: ${err.name} ${err.message}`); }
  } else if (key === "camera") {
    devices.camera = id;
    saveDevices();
    log(`camera: ${id}`);
    setVideoSource("camera", true);
    if (!forAnswer) await cameraOnForPick();
  } else if (key === "test") {
    setVideoSource(id);   // the test pattern or audio bars
    if (!forAnswer) await cameraOnForPick();
  }
  render();
}

// Picking a source from the Camera menu means you want to send it: if the camera was off, it
// comes on, on a video call or (before one) for the next video call. Not on an audio call,
// where it's only the choice for Add video.
async function cameraOnForPick() {
  if (call?.hasVideo) { if (!call.camOn) await setCamera(true); }
  else if (!call && !camPref) setCamPref(true);
}

// A new microphone replaces the old one in place: same mute state, and on a call the audio
// sender just swaps tracks.
async function pickMic(id) {
  devices.mic = id;
  saveDevices();
  if (!localTrack("audio")) { log(`microphone for calls: ${id}`); return; }   // opens with the next call
  let fresh;
  try {
    fresh = (await navigator.mediaDevices.getUserMedia({ audio: { ...MIC_CONSTRAINTS, ...(id ? { deviceId: { exact: id } } : {}) } })).getAudioTracks()[0];
  } catch (err) {
    log(`microphone unavailable: ${err.name} ${err.message}`);
    return;
  }
  const old = localTrack("audio");
  const sender = old && pc?.getSenders().find((s) => s.track === old);
  if (old) {
    fresh.enabled = old.enabled;
    local.removeTrack(old);
    old.stop();
  }
  local.addTrack(fresh);
  if (sender) await sender.replaceTrack(fresh);
  if (call) {
    call.localAnalyser?.source.disconnect();
    call.localAnalyser = analyserFor(fresh);
  }
  log(`microphone: ${fresh.label}`);
}

$("micMenuBtn").onclick = () => showMenu("mic");
$("camMenuBtn").onclick = () => showMenu("cam");
document.addEventListener("pointerdown", (e) => {
  if (openMenu && !e.target.closest(".device-menu, .split-caret")) closeMenu();
});
document.addEventListener("keydown", (e) => {
  if (!openMenu) return;
  const menu = $(MENUS[openMenu][0]);
  if (e.key === "Escape") {
    const btn = $(MENUS[openMenu][1]);
    closeMenu();
    btn.focus();
  } else if (e.key === "ArrowDown" || e.key === "ArrowUp") {
    e.preventDefault();
    const opts = [...menu.querySelectorAll(".device-option")];
    const i = opts.indexOf(document.activeElement);
    opts[(i + (e.key === "ArrowDown" ? 1 : opts.length - 1)) % opts.length]?.focus();
  }
});

// Self view: shows or hides your own picture during a video call (what's sent doesn't change).
// Remembered between runs. V does the same, unless you're typing.
let selfViewOn = true;
try { selfViewOn = localStorage.getItem("selfView") !== "off"; } catch { /* storage unavailable */ }
function toggleSelfView() {
  selfViewOn = !selfViewOn;
  settings?.refresh();
  try { localStorage.setItem("selfView", selfViewOn ? "on" : "off"); } catch { /* storage unavailable */ }
  log(`self view ${selfViewOn ? "on" : "off"}`);
  render();
}
$("selfViewBtn").onclick = toggleSelfView;
document.addEventListener("keydown", (e) => {
  if (e.key.toLowerCase() !== "v" || e.ctrlKey || e.altKey || e.metaKey || openMenu) return;
  if (e.target.closest?.("input, textarea, select, [contenteditable]")) return;
  if ($("selfViewBtn").hidden) return;   // no self view to show or hide
  e.preventDefault();
  toggleSelfView();
});

// The Call quality panel starts as one line (both audio MOS); the chevron opens it. Closed at the start of every call.
function setQualityOpen(open) {
  $("quality").classList.toggle("collapsed", !open);
  $("qHead").setAttribute("aria-expanded", String(open));
  $("qHead").title = open ? "Hide the details" : "Show the details";
}
$("qChevron").innerHTML = ICON.chevronDown;
$("qHead").onclick = () => setQualityOpen($("quality").classList.contains("collapsed"));

// Simulated loss (Diagnostics menu): the host drops that share of the audio or video packets
// we send on the SIP leg. Labels follow the slider as it moves; the host hears once it's let go.
function showLoss() {
  for (const kind of ["Audio", "Video"]) {
    const v = Number($(`loss${kind}`).value);
    $(`loss${kind}Out`).textContent = `${v}%`;
    $(`loss${kind}Out`).classList.toggle("on", v > 0);
  }
  const on = Number($("lossAudio").value) > 0 || Number($("lossVideo").value) > 0;
  $("tabNet").innerHTML = `Network${on ? `<span class="dev-dot" title="Loss is on"></span>` : ""}`;
}
for (const kind of ["Audio", "Video"]) {
  $(`loss${kind}`).addEventListener("input", showLoss);
  $(`loss${kind}`).addEventListener("change", () => post({ type: "loss", audio: Number($("lossAudio").value), video: Number($("lossVideo").value) }));
}
let lossLoaded = false;   // the host's starting values (--loss-audio, --loss-video), taken from the first state

let copiedUntil = 0;   // the header says "Copied" until then
// ---- Diagnostics (F12) ----
// A panel docked at the bottom, like a browser's developer tools: the SIP log, and Network (the
// loss sliders). While it's open the app is in diagnostics mode, which also shows Call quality,
// your SIP address in the header, and the test video sources. Closed at every start
// (--diagnostics opens it, for testing). Ctrl+Shift+I is left to WebView2's own DevTools.
function setDiagnostics(on) {
  document.body.classList.toggle("diag", on);
  $("devPanel").hidden = !on;
  allowDetails(on);
  if (on) { const el = $("log"); el.scrollTop = el.scrollHeight; }
  // The test pattern and audio bars are diagnostics things, so the camera takes over again.
  // But it isn't sent until you turn it on: the camera never starts by itself. (With no camera
  // the placeholder goes, as it would anyway.)
  if (!on && videoSource !== "camera") {
    if (call?.hasVideo) { if (call.camOn) setCamera(false); }
    else camPref = false;
    setVideoSource("camera");
  }
  // Simulated loss is a diagnostics thing too: leaving diagnostics turns it off, so it can't
  // quietly carry on spoiling calls with the sliders out of sight.
  if (!on && (Number($("lossAudio").value) > 0 || Number($("lossVideo").value) > 0)) {
    $("lossAudio").value = 0;
    $("lossVideo").value = 0;
    showLoss();
    post({ type: "loss", audio: 0, video: 0 });
  }
  closeMenu();
  renderQuality();
  render();
}
document.body.classList.toggle("diag", params.get("diag") === "1");
allowDetails(params.get("diag") === "1");
$("devPanel").hidden = params.get("diag") !== "1";
// F12, caught before WebView2 would open its DevTools with it.
document.addEventListener("keydown", (e) => {
  if (e.key !== "F12" || e.ctrlKey || e.altKey || e.shiftKey || e.metaKey) return;
  e.preventDefault();
  e.stopPropagation();
  setDiagnostics($("devPanel").hidden);
}, true);
$("devClose").innerHTML = ICON.close;
$("devClose").onclick = () => setDiagnostics(false);

function setDevTab(tab) {
  for (const b of document.querySelectorAll(".dev-tab")) b.setAttribute("aria-selected", String(b.dataset.tab === tab));
  $("log").hidden = tab !== "log";
  $("paneNet").hidden = tab !== "net";
  $("logCopy").hidden = $("logClear").hidden = tab !== "log";
  if (tab === "log") $("log").scrollTop = $("log").scrollHeight;
}
for (const b of document.querySelectorAll(".dev-tab")) b.onclick = () => setDevTab(b.dataset.tab);
$("logCopy").onclick = () => {
  post({ type: "copy", text: $("log").textContent });
  $("logCopy").textContent = "Copied";
  setTimeout(() => { $("logCopy").textContent = "Copy log"; }, 1500);
};
$("logClear").onclick = () => { $("log").textContent = ""; };

// Dragging the panel's top edge resizes it, between 120 px and 70% of the window.
$("devResize").addEventListener("pointerdown", (e) => {
  e.preventDefault();
  const handle = e.currentTarget;
  handle.setPointerCapture(e.pointerId);
  document.body.classList.add("dev-resizing");
  const move = (ev) => {
    const h = Math.round(Math.max(120, Math.min(window.innerHeight * 0.7, window.innerHeight - ev.clientY)));
    $("devPanel").style.setProperty("--dev-h", `${h}px`);
  };
  const up = () => {
    handle.removeEventListener("pointermove", move);
    document.body.classList.remove("dev-resizing");
  };
  handle.addEventListener("pointermove", move);
  handle.addEventListener("pointerup", up, { once: true });
  handle.addEventListener("pointercancel", up, { once: true });
});
if (SOURCE_LABEL[params.get("video")]) setVideoSource(params.get("video"));   // --test-pattern, --audio-bars (testing)

$("reg").onclick = () => {
  if (!state.address) return;
  post({ type: "copy", text: state.address });
  copiedUntil = performance.now() + 1500;
  render();
  setTimeout(render, 1600);
};
$("hangupBtn").onclick = () => post({ type: "hangup" });
$("muteBtn").onclick = () => {
  micMuted = !micMuted;   // before a call too: the call then starts muted
  const track = localTrack("audio");
  if (track) track.enabled = !micMuted;
  if (call) call.muted = micMuted;
  log(micMuted ? "muted" : "unmuted");
  render();
};
$("camBtn").onclick = () => {
  if (call?.hasVideo) { setCamera(!call.camOn); return; }
  if (!call && callType === "audio") {
    // On Audio, the camera means you want a video call: switch to one, camera on.
    if (!camPref) setCamPref(true);
    log("camera: switched to a video call, camera on");
    setCallType("video");
    return;
  }
  if (!call) {
    // Before a call: whether a video call starts with your camera on.
    setCamPref(!camPref);
    render();
    return;
  }
  addingVideo = true;
  post({ type: "add-video" });
  log("adding video");
  render();
};

chrome.webview.addEventListener("message", async (e) => {
  const msg = e.data;
  try {
    if (msg.type === "state") {
      // The microphone closes when a call ends. (Not on every render while idle: Call opens it
      // a moment before the host says the call has started.)
      const ended = (msg.call === "ended" || msg.call === "idle") && msg.call !== state.call;
      const ringing = msg.call === "ringing" && state.call !== "ringing";
      state = msg;
      if (ringing && prefs.autoAnswer) autoAnswer();
      if (!lossLoaded) {
        lossLoaded = true;
        $("lossAudio").value = msg.lossAudio ?? 0;
        $("lossVideo").value = msg.lossVideo ?? 0;
        showLoss();
      }
      if (ended) closeMic();
      render();
      onAccountsState(msg);
    }
    else if (msg.type === "account-saved" || msg.type === "account-error") onAccountMessage(msg);
    else if (msg.type === "media-offer") await onMediaOffer(msg.sdp);
    else if (msg.type === "media-request") await onMediaOffer(null, !!msg.video);
    else if (msg.type === "media-remote-answer") await onMediaAnswer(msg.sdp);
    else if (msg.type === "media-health") { if (call) { call.txLost = !!msg.txLost; render(); } }
    else if (msg.type === "quality") onQuality(msg);
    else if (msg.type === "details") { callDetails = msg; renderDetails(); }
    else if (msg.type === "text") onTextReceived(msg.text);
    else if (msg.type === "video-refused") { addingVideo = false; videoRefused = true; log("they didn't take video"); render(); }
    else if (msg.type === "log") log(msg.text, true);
  } catch (err) {
    log(`${msg.type} failed: ${err.message}`);
  }
});

initAccounts({ post, log });
// Settings' "Answer calls automatically": after a second, with the camera off (it's never opened
// without you asking), as if you'd picked Camera off from Answer ▾ and pressed Answer.
function autoAnswer() {
  setTimeout(() => {
    if (state.call !== "ringing" || !prefs.autoAnswer) return;
    log("answering automatically, camera off");
    closeMenu();
    answerCamera = false;
    openMic();
    post({ type: "answer" });
  }, 1000);
}

// ---- Settings (settings.js), from the gear in the top bar ----
const settings = initSettings({
  log,
  devices,
  // The same as picking from the ▾ menus, except a camera picked here doesn't turn the camera
  // on: if it's open it switches, otherwise it's the one the next video call opens.
  async pickDevice(key, id) {
    if (key === "mic") await pickMic(id);
    else if (key === "speaker") await pickDevice("speaker", id);
    else if (key === "camera") {
      devices.camera = id;
      saveDevices();
      log(`camera for calls: ${id || "system default"}`);
      if (videoSource === "camera" && localTrack("video")) setVideoSource("camera", true);
    }
    render();
  },
  camPref: () => camPref,
  setCamPref(on) { setCamPref(on); syncCamera(); render(); },
  selfView: () => selfViewOn,
  setSelfView(on) { if (on !== selfViewOn) toggleSelfView(); },
  diagnostics: () => !$("devPanel").hidden,
  setDiagnostics,
  // Who you are: the account, or with none, the address others can call you on.
  account: () => state.account || (state.address ? `No account · ${state.address}` : null),
});

render();            // the UI straight away, before the microphone is open
await openDevices();
render();
syncCamera();
refreshCameras();
post({ type: "ready" });
