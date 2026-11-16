// What each call sends as video: your camera, a diagnostic source (test pattern, audio bars), the
// camera-off placeholder or the "On hold" card. Opens and closes the camera to match.

import { call, devices, entries, holdState, local, localTrack, nextVideo, render, sel, selectedId, state } from "./app.js";
import { avatarImage } from "./avatar.js";
import { BANDS, FALL_MS, analyserFor, bands } from "./levels.js";
import { $, fitText, initial, log } from "./util.js";
import { previewOn } from "./yourdevices.js";

// On a video call held with music on hold, they see this card instead of your camera: a pause
// sign and "On hold", a few frames a second, like the camera-off placeholder.
const holdCards = new Map();   // name -> its canvasSource
function holdCardTrack(e) {
  const name = meOf(e);
  if (!holdCards.has(name)) holdCards.set(name, canvasSource((g, w, h) => drawHoldCard(g, w, h, name), PLACEHOLDER_FPS));
  return holdCards.get(name).track;
}
function drawHoldCard(g, w, h, name) {
  g.fillStyle = "#3a3324";
  g.fillRect(0, 0, w, h);
  g.fillStyle = "#e8d3a2";
  g.fillRect(w / 2 - 44, h / 2 - 110, 28, 110);
  g.fillRect(w / 2 + 16, h / 2 - 110, 28, 110);
  g.font = '300 64px "Segoe UI", system-ui, sans-serif';
  g.textAlign = "center";
  g.textBaseline = "middle";
  g.fillText("On hold", w / 2, h / 2 + 70);
  g.font = '300 32px "Segoe UI", system-ui, sans-serif';
  g.fillStyle = "#bfae86";
  g.fillText(name ? `${name} will be back shortly` : "Back shortly", w / 2, h / 2 + 130);
}
// Stops the hold cards no call is sending.
function stopHoldCards() {
  for (const [name, c] of holdCards) {
    if ([...entries.values()].some((e) => e.m?.videoSender?.track === c.track)) continue;
    c.stop();
    holdCards.delete(name);
  }
}

export let hasCamera = false;     // a camera exists (and hasn't failed to open for good)
export let cameraInUse = false;   // the last try found it in use; cleared by your next camera action
// A fresh try at a camera that was in use last time, because you asked for it.
export function retryCamera() { cameraInUse = false; }
export function setHasCamera(has) { hasCamera = has; }
let cameraBusy = null;     // the open or close in progress, so they never overlap

// Which video sources should be open ("camera", "test", "bars"): each call's own, while it's
// sending video with your camera on, and while Your devices' preview is on (yourdevices.js), the
// source new calls start with. Nothing opens before a call without that click.
export function wantedSources() {
  const want = new Set();
  if (!local) return want;   // not before the microphone's open (local is set)
  const all = [...entries.values()];
  // Any call sending (not on hold).
  for (const e of all) if (e.m?.hasVideo && e.m.camOn && !holdState(e) && e.h.call === "connected") want.add(sourceOf(e));
  if (!call) {
    if (all.some((e) => e.h.call === "connected" && !e.m)) {
      // Connected, but the media isn't set up yet: leave what's open as it is, so a video
      // call's preview carries straight on into the call.
      for (const kind of openSources()) want.add(kind);
    } else {
      if (previewOn() && nextVideo.camOn) want.add(nextVideo.source);   // Camera off: the card, no camera
    }
  }
  if (!sourceAvailable("camera")) want.delete("camera");
  return want;
}

// The sources open now.
export function openSources() {
  const open = new Set(sources.keys());
  if (localTrack("video")) open.add("camera");
  return open;
}

export const sameSources = (a, b) => a.size === b.size && [...a].every((k) => b.has(k));

// While your camera is off (or there's none) on a video call, the call sends a placeholder
// instead of nothing: your initial on a plain background, a few frames a second. Video never
// stops, so every SIP phone just sees a picture rather than a frozen frame or a dead stream,
// NAT bindings and media timers stay alive, and turning the camera on is a track swap with the
// encoder already running. It's drawn on a canvas and sent at the camera's size, so the far
// end doesn't see the picture change shape. The initial is the call's account's (e.h.me): with
// two accounts, Bob's call shows B and Dave's D. With Settings' "Show Gravatar pictures" on, the
// account's Gravatar picture (or identicon) fills the circle instead, with the account's address
// under it, once it has loaded; the
// card is redrawn every frame, so it appears, or goes when the setting is turned off, within one.
// One placeholder per name and account, shared by their calls.
const PLACEHOLDER_FPS = 5;   // a still picture costs a few kbps; much sparser upsets some decoders
const placeholders = new Map();   // placeholderKey -> its canvasSource

// The name we go by on a call: its account's (none when the desktop can't tell which account
// it's on: no name then, never another call's), else (before a call, or the web softphone) ours.
const meOf = (e) => (e?.h && e.h.me !== undefined ? e.h.me : state.me) ?? "";
// The address of the account a call is on (none for a direct call), else the selected account's.
const aorOf = (e) => (e?.h ? e.h.account : state.account) ?? "";
const placeholderKey = (e) => `${meOf(e)}\n${aorOf(e)}`;

export function placeholderTrack(e = null) {
  const key = placeholderKey(e);
  if (!placeholders.has(key)) {
    const name = meOf(e), aor = aorOf(e);
    placeholders.set(key, canvasSource((g, w, h) => drawPlaceholder(g, w, h, name, aor), PLACEHOLDER_FPS));
  }
  return placeholders.get(key).track;
}

// A video track drawn on a 1280×720 canvas, fps times a second, for the placeholder, the hold
// card and the diagnostic sources: draw(g, w, h) paints each frame. Frame rate 0 and
// requestFrame: a frame goes out each time one is drawn and asked for, and a canvas only produces
// one after a draw, so even a still picture is redrawn every time. stop() ends it.
function canvasSource(draw, fps) {
  const canvas = Object.assign(document.createElement("canvas"), { width: 1280, height: 720 });
  const g = canvas.getContext("2d");
  const track = canvas.captureStream(0).getVideoTracks()[0];
  const frame = () => { draw(g, canvas.width, canvas.height); track.requestFrame(); };
  const timer = setInterval(frame, 1000 / fps);
  frame();
  return { track, stop: () => { clearInterval(timer); track.stop(); } };
}

function drawPlaceholder(g, w, h, name, aor) {
  const label = name ? initial(name) : "";
  g.fillStyle = "#1f3a31";
  g.fillRect(0, 0, w, h);
  g.strokeStyle = g.fillStyle = "#a9c2b8";
  g.lineWidth = 4;
  g.beginPath();
  g.arc(w / 2, h / 2, 120, 0, 2 * Math.PI);
  g.stroke();
  const pic = aor ? avatarImage(aor) : null;
  if (pic) {
    g.save();
    g.beginPath();
    g.arc(w / 2, h / 2, 118, 0, 2 * Math.PI);
    g.clip();
    g.drawImage(pic, w / 2 - 118, h / 2 - 118, 236, 236);
    g.restore();
    // The account's address under the picture, as the picture alone doesn't say who it is.
    g.font = '400 34px "Segoe UI", system-ui, sans-serif';
    g.textAlign = "center";
    g.textBaseline = "top";
    g.fillText(fitText(g, aor, w - 160), w / 2, h / 2 + 150);
  } else if (label) {
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
}

// Stops the placeholders no call with video could be sending (by name and account, so one being
// put on a call stays) and that aren't showing (keep: the self view's track).
export function stopPlaceholders(keep = null) {
  for (const [key, p] of placeholders) {
    if (p.track === keep || [...entries.values()].some((e) => e.m?.videoSender && placeholderKey(e) === key)) continue;
    if (previewOn() && !nextVideo.camOn && key === placeholderKey(null)) continue;   // Your devices' preview of it
    p.stop();
    placeholders.delete(key);
  }
}

// ---- Test pattern (diagnostics) ----
// In diagnostics mode, a call's video source can be a test pattern instead of the camera. It
// stands in for the camera exactly: it's opened and closed when the camera would be, and
// Camera off swaps in the placeholder as usual. Each call has its own source, picked from the
// Camera ▾ while it's the selected call, so two calls can send different pictures (the test
// pattern on one, the camera on the other). The camera itself is one device, shared by every
// call sending it; the test pattern and audio bars are one canvas each, likewise shared. The
// source last picked is the one the preview and new calls start with. The picture has
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
// The source new calls start with is nextVideo.source (a call keeps its own in e.source).
// Everything goes back to the camera when diagnostics goes off.
// The open test sources by kind: { kind, frame, and for bars the analyser and levels, with the
// canvasSource's track and stop }. The camera, when open, is the video track in `local`.
const sources = new Map();
export const SOURCE_LABEL = { test: "Test pattern", bars: "Audio bars" };

// A call's source; before its media is set up, once the call has ended (its card can stay on the
// stage), or with no call, the one new calls start with. (An ended call keeps what it had, and a
// pick then changes only the next call's, so its own would be stale.)
export const sourceOf = (e) => (e?.m ? e.source : null) ?? nextVideo.source;
// Likewise the camera (device): each call keeps the one it was given or picked, so picking a
// camera for one call doesn't change another's. One camera is open at a time, the one the call
// sending it wants (only the live call sends it: the others are held), switched when you go to
// a call that wants another (see syncCameraTrack).
export const cameraIdOf = (e) => (e?.m ? e.cameraId : null) ?? devices.camera;
export let cameraOpenedFor = null;   // the device asked for when the open camera was opened
// Whether a source can be opened: the test sources always; a camera if there is one and it
// isn't in use elsewhere.
export const sourceAvailable = (kind) => kind !== "camera" || (hasCamera && !cameraInUse);
export const videoAvailable = () => sourceAvailable(nextVideo.source);
export const isTestTrack = (track) => !!track && [...sources.values()].some((tp) => tp.track === track);
const sourceTrack = (kind) => (kind === "camera" ? localTrack("video") : sources.get(kind)?.track ?? null);
// What Your devices' preview shows: the source new calls start with, once it's open, or with the
// camera list's Camera off, the camera-off card they'll see (the selected account's).
export const previewTrack = () => (nextVideo.camOn ? sourceTrack(nextVideo.source) : placeholderTrack(null));

function openTestPattern(kind) {
  const tp = { kind, frame: 0, analyser: null, micTrack: null, levels: new Float32Array(BANDS), target: new Float32Array(BANDS), last: performance.now() };
  Object.assign(tp, canvasSource((g, w, h) => (kind === "bars" ? drawAudioBars(g, w, h, tp) : drawTestPattern(g, w, h, tp.frame++)), TEST_FPS));
  sources.set(kind, tp);
  return tp.track;
}

function closeTestPattern(kind) {
  const tp = sources.get(kind);
  if (!tp) return;
  tp.stop();
  tp.analyser?.source.disconnect();
  sources.delete(kind);
}

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

function closeCamera(track) {
  local.removeTrack(track);
  track.stop();
}

function drawTestPattern(g, W, H, frame) {
  const pad = (n, w = 2) => String(n).padStart(w, "0");
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
}

function drawAudioBars(g, W, H, tp) {
  const frame = tp.frame++;

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
}

// Switches the video source for a call (e), and for the preview and new calls; with no call,
// only the latter. With reopen, the camera closes first, so a newly picked camera opens.
// syncCamera opens what's now wanted and closes what isn't (so a camera that's off stays off).
export function setVideoSource(source, e = null, reopen = false) {
  const changed = source !== nextVideo.source || (e?.m && e.source !== source);
  if (!changed && !reopen) return;
  nextVideo.source = source;
  if (e?.m) e.source = source;
  if (source === "camera") cameraInUse = false;   // a fresh try, because you asked
  if (changed) log(`video source${e?.m ? ` for call ${e.id}` : ""}: ${source}`);
  if (!local) return;   // at start, before the devices are open; the first syncCamera picks it up
  const cam = localTrack("video");
  if (reopen && cam) closeCamera(cam);
  syncCamera();
}

// What a call's video sender should carry: its source when the camera's on and the source is
// open, else the placeholder. On hold, the "On hold" card (music on hold) or nothing (SIP hold).
export const videoToSend = (e) => (holdState(e) ? (e.h.holdMode === "sip" ? null : holdCardTrack(e))
  : (e.m.camOn && sourceTrack(sourceOf(e))) || placeholderTrack(e));
const anyVideoSender = () => [...entries.values()].some((e) => e.m?.videoSender);

// Puts what's sent (or, before a call, what would be) in the self view. Only the camera is
// mirrored, so the placeholder's initial reads the right way round. The placeholder is stopped
// once neither the self view nor a call needs it.
export function showSelfVideo(show) {
  const el = $("self");
  const track = !show ? null : call?.videoSender ? videoToSend(sel()) : sourceTrack(nextVideo.source) ?? placeholderTrack(sel());
  if ((el.srcObject?.getVideoTracks()[0] ?? null) !== track) el.srcObject = track ? new MediaStream([track]) : null;
  el.hidden = !track;
  el.classList.toggle("mirror", !!track && track === localTrack("video") && !isTestTrack(track));
  stopPlaceholders(track);
}

// Opens or closes the camera and the test sources to match wantedSources(), and puts on each call
// what it should send.
export function syncCamera() {
  cameraBusy = (cameraBusy ?? Promise.resolve()).then(async () => {
    const want = wantedSources();
    for (const kind of Object.keys(SOURCE_LABEL)) {
      if (want.has(kind) && !sources.has(kind)) {
        openTestPattern(kind);
        log(`${SOURCE_LABEL[kind].toLowerCase()} opened`);
      } else if (!want.has(kind) && sources.has(kind)) {
        closeTestPattern(kind);
        log(`${SOURCE_LABEL[kind].toLowerCase()} closed`);
      }
    }
    await syncCameraTrack(want.has("camera"));
    // Always match each call's sender to its source, even when no source changed:
    // a new call (or the new connection after Add video) has a new sender, and a camera left
    // open from the preview or the last call must still be put on it.
    for (const e of entries.values()) {
      const sender = e.m?.videoSender;
      if (!sender) continue;
      const send = videoToSend(e);
      if (sender.track !== send) await sender.replaceTrack(send);
    }
    stopHoldCards();
    render();   // the self view follows what's sent
  }).catch((err) => log(`camera sync failed: ${err.message}`));   // so one failure doesn't stall the queue
  return cameraBusy;
}

// The camera that should be open: the one the call sending it wants (the selected call if more
// than one does), or before a call, the preview's. Null: no preference, keep what's open.
function wantedCameraId() {
  const sending = [...entries.values()].filter((e) => e.m?.hasVideo && e.m.camOn && !holdState(e)
    && e.h.call === "connected" && sourceOf(e) === "camera");
  const e = sending.find((x) => x.id === selectedId) ?? sending[0];
  if (e) return cameraIdOf(e);
  return call ? null : devices.camera;
}

// Opens or closes the camera itself, or switches it to the one wanted. If it can't open, every
// call sending it goes to camera off.
async function syncCameraTrack(want) {
  let track = localTrack("video");
  if (!want && track) {
    log("camera closed");
    closeCamera(track);
    return;
  }
  if (!want) return;
  const chosen = wantedCameraId();
  if (track && chosen != null && chosen !== cameraOpenedFor) {
    log("camera closed, to switch cameras");
    closeCamera(track);
    track = null;
  }
  if (track) return;
  const cameraCallsOff = () => { for (const e of entries.values()) if (e.m && sourceOf(e) === "camera") e.m.camOn = false; };
  try {
    // The chosen camera exactly, or if it's gone, the default.
    const open = (id) => navigator.mediaDevices.getUserMedia({
      video: { width: { ideal: 1280 }, height: { ideal: 720 }, frameRate: { ideal: 30 }, ...(id ? { deviceId: { exact: id } } : {}) },
    });
    const id = chosen ?? devices.camera;
    const cam = await open(id).catch((err) => {
      if (!id || err.name !== "OverconstrainedError" && err.name !== "NotFoundError") throw err;
      log("chosen camera not found, using the default");
      return open("");
    });
    track = cam.getVideoTracks()[0];
    // Changed while it was opening (including a switch to the test pattern).
    if (!wantedSources().has("camera")) { track.stop(); return; }
    local.addTrack(track);
    cameraOpenedFor = id;
    log("camera opened");
  } catch (err) {
    if (err.name === "NotReadableError" || err.name === "AbortError") {
      // In use elsewhere. Your camera goes off; trying again is up to you.
      log(`camera in use: ${err.name} ${err.message}`);
      cameraInUse = true;
    } else {
      // Missing, or permission refused: no camera for this run.
      log(`camera unavailable: ${err.name} ${err.message}`);
      hasCamera = false;
      cameraInUse = false;
    }
    cameraCallsOff();
  }
}
