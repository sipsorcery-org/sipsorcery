// Your devices: the microphone, speaker and camera calls use, on Home and in the dial view
// (home.js) and under a call ringing on the stage (app.js), from the devices mock
// (prototype/devices). Folded, three lines: the device for each (Settings' defaults, unless one's
// been changed here or from the ▾ menus, which says so: that's for this call or the next, until the
// last call ends), and on the camera's a word on what the call will do with it when that isn't
// plain. The camera's list ends with Camera off: a video call then starts with your camera-off
// card, as if you'd pressed Stop video, until you press Start video. Preview then shows that card,
// and the camera stays closed.
//
// While a call you placed is ringing ("calling"), the same panel is on its card, read-only: the
// devices it's using. They're changed once it's answered, from the ▾ on Mute and Camera. A click on a line opens it with that device's test running (the camera's: its preview;
// the microphone's: a level meter); Change or test opens it with nothing on, so changing the
// speaker doesn't light your camera. Open, a line per device: its picker (one click to change it),
// what it will do, and its test (Test mic, Preview, Play a sound). Only you see and hear a test,
// and nothing opens without a click; Done, placing or answering a call, or privacy mode stops them.
//
// Also the indicator at the top right: the microphone and camera, each shown only while it's
// open, so there's nothing to work out when they aren't. A click says which call or test has each
// one, and lists when each opened and closed.

import { audioId, camOnDefault, chooseDevice, defaults, devices, entries, holdState, localTrack, MIC_CONSTRAINTS, nameOf, nextVideo, privacyOn, render, setStartCamera, state } from "./app.js";
import { analyserFor, bands, BANDS } from "./levels.js";
import { deviceOptions } from "./settings.js";
import { $, escHtml, ICON, log } from "./util.js";
import { cameraInUse, hasCamera, isTestTrack, previewTrack, retryCamera, setVideoSource, SOURCE_LABEL, sourceOf, syncCamera } from "./video.js";

// open: the panel's open; preview: the camera preview's on; mic: the mic test ({ track, analyser,
// levels } once open); soundUntil: Play a sound is playing until then.
export const yd = { open: false, preview: false, mic: null, soundUntil: 0 };
export const previewOn = () => yd.preview && !privacyOn();
const canPickSpeaker = "setSinkId" in HTMLMediaElement.prototype;
const KINDS = ["mic", "speaker", "camera"];   // the camera last, so its preview sits under it
const WORD = { mic: "Microphone", speaker: "Speaker", camera: "Camera" };

let list = [];   // enumerateDevices(), for the names and the pickers
let justPicked = null;   // the picker just used, so its panel is redrawn though it has focus
const diagOn = () => document.body.classList.contains("diag");
async function refreshList() {
  try { list = await navigator.mediaDevices.enumerateDevices(); } catch { list = []; }
}

// The panel folds, and its tests stop: placing or answering a call, Done, leaving the dial view.
export function closeDevices() {
  yd.open = false;
  stopTests();
}
// The tests stop (privacy mode, a call taking the devices over); the panel stays as it is.
export function stopTests() {
  stopMicTest();
  if (yd.preview) { yd.preview = false; log("your devices: preview off"); syncCamera(); }
}

function openPanel(dev = null) {
  yd.open = true;
  refreshList().then(render);
  // A device's line opens it with that device's test running; Change or test, with nothing.
  if (!privacyOn()) {
    if (dev === "camera") setPreview(true);
    else if (dev === "mic") startMicTest();
  }
  render();
}

function setPreview(on) {
  if (on && privacyOn()) return;
  yd.preview = on;
  if (on) retryCamera();   // a fresh try, because you asked
  log(`your devices: preview ${on ? "on" : "off"}`);
  syncCamera();
  render();
}

// ---- The mic test: the chosen microphone, opened on its own, with a level meter ----
async function startMicTest() {
  stopMicTest();
  if (privacyOn()) return;
  const test = { track: null, analyser: null, levels: new Float32Array(BANDS), error: null };
  yd.mic = test;
  render();
  const open = (id) => navigator.mediaDevices.getUserMedia({ audio: { ...MIC_CONSTRAINTS, ...(id ? { deviceId: { exact: id } } : {}) } });
  try {
    const stream = await open(devices.mic).catch((err) => { if (!devices.mic) throw err; return open(""); });
    const track = stream.getAudioTracks()[0];
    if (yd.mic !== test) { track.stop(); return; }   // stopped while it was opening
    test.track = track;
    test.analyser = analyserFor(track);
    log(`your devices: mic test on (${track.label})`);
    refreshList().then(render);   // names, now that a microphone has been opened
    requestAnimationFrame(drawMeter);
  } catch (err) {
    if (yd.mic === test) test.error = err.name === "NotAllowedError" ? "Not allowed: the browser blocked the microphone" : "Couldn't open it";
    log(`your devices: mic test failed: ${err.name} ${err.message}`);
  }
  render();
}
function stopMicTest() {
  const test = yd.mic;
  if (!test) return;
  yd.mic = null;
  test.analyser?.source.disconnect();
  if (test.track) { test.track.stop(); log("your devices: mic test off"); }
}
function drawMeter() {
  const test = yd.mic;
  if (!test?.analyser) return;
  const level = bands(test.analyser, test.levels);
  const lit = Math.round(Math.min(1, level * 2.6) * 14);
  for (const meter of document.querySelectorAll(".yd-meter")) {
    [...meter.children].forEach((bar, i) => bar.classList.toggle("on", i < lit));
  }
  requestAnimationFrame(drawMeter);
}

// ---- Play a sound: a two-note chime on the chosen speaker ----
async function playSound() {
  if (yd.soundUntil > Date.now()) return;
  const ctx = new AudioContext();
  const out = ctx.createMediaStreamDestination();
  const note = (freq, at, len) => {
    const osc = ctx.createOscillator(), gain = ctx.createGain();
    osc.frequency.value = freq;
    gain.gain.setValueAtTime(0.0001, ctx.currentTime + at);
    gain.gain.exponentialRampToValueAtTime(0.3, ctx.currentTime + at + 0.02);
    gain.gain.exponentialRampToValueAtTime(0.0001, ctx.currentTime + at + len);
    osc.connect(gain).connect(out);
    osc.start(ctx.currentTime + at);
    osc.stop(ctx.currentTime + at + len);
  };
  note(660, 0, 0.4);
  note(880, 0.42, 0.6);
  const el = new Audio();
  el.srcObject = out.stream;
  if (devices.speaker && canPickSpeaker) await el.setSinkId(devices.speaker).catch(() => {});
  el.play().catch((err) => log(`your devices: sound failed: ${err.message}`));
  yd.soundUntil = Date.now() + 1200;
  render();
  setTimeout(() => {
    el.pause();
    el.srcObject = null;
    ctx.close();
    yd.soundUntil = 0;
    render();
  }, 1200);
}

// Use my defaults: back to Settings' devices now, rather than when the calls end.
async function useDefaults() {
  for (const k of KINDS) if (devices[k] !== defaults[k]) await chooseDevice(k, defaults[k]);
  setStartCamera(camOnDefault());
  if (yd.mic) startMicTest();
  await syncCamera();
  render();
}

// ---- The panel ----
// What each device will do, for where you are: ctx "home", "dial" (placing a call) or "ring" (a
// call ringing in).
function notes(ctx) {
  const off = privacyOn();
  const video = !!state.incomingVideo && state.videoAllowed !== false;
  const camOff = state.videoAllowed === false ? "Video is turned off" : !hasCamera ? "No camera found" : cameraInUse ? "In use by another app: Preview tries again" : null;
  return {
    mic: off ? "Off: privacy mode (they'll hear silence)"
      : yd.mic ? (yd.mic.error ?? (yd.mic.track ? "Open for the test: only you see the meter" : "Opening…"))
      : ctx === "dial" ? "Opens when you call"
      : ctx === "ring" ? "Opens when you answer"
      : "Not in use",
    camera: off ? (ctx === "ring" && !video ? "Off: privacy mode" : "Off: privacy mode (on video, they'll see your camera-off card)")
      : yd.preview && !nextVideo.camOn ? "Your camera-off card, as they'll see it: the camera stays closed"
      : yd.preview ? (previewTrack() ? "Open for your preview: only you see it" : "Opening…")
      : camOff ? camOff
      : !nextVideo.camOn ? (ctx === "ring" && !video ? "Stays closed: it's an audio call" : "Stays closed: on video they'll see your camera-off card, until Start video")
      : ctx === "dial" ? "Video call: opens · Call: stays closed"
      : ctx === "ring" ? (video ? "Answer with video: opens · Answer: stays closed" : "Stays closed: it's an audio call")
      : "Not in use",
    speaker: yd.soundUntil ? "Playing a sound…" : !canPickSpeaker ? "This browser plays calls on the system's speaker"
      : ctx === "home" ? "Calls play here" : "You'll hear them here",
  };
}
// Folded, the camera's line says what the call will do with it only when that isn't plain, and a
// device changed from the default says it's only for now.
const camOffPicked = () => nextVideo.camOn !== camOnDefault();   // on or off, not the default
const notDefault = (k) => (k === "camera" && !nextVideo.camOn ? false : devices[k] !== defaults[k]) || (k === "camera" && camOffPicked());
function hints(ctx) {
  const off = privacyOn();
  const once = "for now";   // the open panel's foot says until when
  const h = {
    mic: off ? "off (privacy mode)" : "",
    camera: off ? "off (privacy mode)"
      : !nextVideo.camOn ? "your camera-off card"
      : ctx === "ring" && state.incomingVideo && state.videoAllowed !== false ? "on only if you answer with video" : "",
    speaker: "",
  };
  for (const k of KINDS) if (notDefault(k) && !(k === "camera" && nextVideo.source !== "camera" && nextVideo.camOn)) h[k] = h[k] ? `${h[k]}, ${once}` : once;
  return h;
}

// A call you placed, ringing: what it's using, read-only.
function callingHtml(e) {
  const off = privacyOn();
  const icon = { mic: off ? ICON.micOff : ICON.mic, camera: off ? ICON.videoOff : ICON.video, speaker: ICON.speaker };
  const o = (k) => deviceOptions(list, k, devices[k]);
  const mic = localTrack("audio"), cam = localTrack("video");
  const video = (e?.m ? e.m.hasVideo : !!e?.placedVideo) && state.videoAllowed !== false;
  const camOn = !off && (e?.m ? e.m.camOn : nextVideo.camOn);
  const source = e?.m ? sourceOf(e) : nextVideo.source;
  const show = {
    mic: off ? ["Off", "privacy mode: they'll hear silence"] : [mic?.label || o("mic").label, ""],
    speaker: [!canPickSpeaker ? "System default" : o("speaker").label, ""],
    camera: !video ? ["Not used", "it's an audio call"]
      : off ? ["Off", "privacy mode: your camera-off card"]
      : !camOn ? ["Off", "your camera-off card, until Start video"]
      : source !== "camera" ? [SOURCE_LABEL[source], ""]
      : [cam?.label || o("camera").label, cam ? "" : "opens when they answer"],
  };
  const line = (k) => `<div class="yd-line static${(off && k !== "speaker") || (k === "camera" && video && !camOn) ? " off" : ""}">
      <span class="yd-ico">${icon[k]}</span><span class="yd-label">${WORD[k]}</span>
      <span class="yd-name">${escHtml(show[k][0])}${show[k][1] ? `<span class="yd-hint"> · ${escHtml(show[k][1])}</span>` : ""}</span></div>`;
  return `<div class="yd-head"><h3 class="yd-title">Your devices</h3><span class="yd-sub">Used for this call: change them once it's answered</span></div>
    <div class="yd-lines">${KINDS.map(line).join("")}</div>`;
}

function panelHtml(ctx, e) {
  if (ctx === "calling") return callingHtml(e);
  const off = privacyOn();
  const icon = { mic: off ? ICON.micOff : ICON.mic, camera: off ? ICON.videoOff : ICON.video, speaker: ICON.speaker };
  const toggle = `<button type="button" class="link-btn yd-toggle" data-yd="toggle" aria-expanded="${yd.open}">${yd.open ? "Done" : "Change or test"}</button>`;
  const head = (sub) => `<div class="yd-head"><h3 class="yd-title">Your devices</h3>${sub ? `<span class="yd-sub">${sub}</span>` : ""}${toggle}</div>`;
  const opt = (k) => deviceOptions(list, k, devices[k]);
  const isOff = (k) => off && k !== "speaker";
  if (!yd.open) {
    const h = hints(ctx);
    const tip = { mic: off ? "Change the microphone" : "Change or test the microphone", camera: off ? "Change the camera" : "Change the camera, with a preview",
      speaker: "Change the speaker, or play a sound" };
    const line = (k) => {
      const o = opt(k);
      const name = k === "camera" && !nextVideo.camOn ? "Off"
        : k === "camera" && nextVideo.source !== "camera" ? SOURCE_LABEL[nextVideo.source]
        : o.none ? "None found" : k === "speaker" && !canPickSpeaker ? "System default" : o.label;
      return `<button type="button" class="yd-line${isOff(k) ? " off" : ""}" data-yd="line" data-dev="${k}" title="${tip[k]}">
        <span class="yd-ico">${icon[k]}</span><span class="yd-label">${WORD[k]}</span>
        <span class="yd-name">${escHtml(name)}${h[k] ? `<span class="yd-hint"> · ${escHtml(h[k])}</span>` : ""}</span></button>`;
    };
    return `${head("")}<div class="yd-lines">${KINDS.map(line).join("")}</div>`;
  }
  const n = notes(ctx);
  const live = { mic: !off && !!yd.mic?.track, camera: !off && yd.preview, speaker: !!yd.soundUntil };
  const act = {
    mic: `<button type="button" class="btn btn-small yd-act" data-yd="mic-test" ${off ? "disabled" : ""}>${yd.mic ? "Stop test" : "Test mic"}</button>`,
    camera: `<button type="button" class="btn btn-small yd-act" data-yd="preview" ${off || state.videoAllowed === false ? "disabled" : ""}>${yd.preview ? "Stop preview" : "Preview"}</button>`,
    speaker: `<button type="button" class="btn btn-small yd-act" data-yd="sound">Play a sound</button>`,
  };
  // The camera's list has the test sources too in diagnostics mode, as the Camera ▾ does
  // ("src:test", "src:bars"); one of them chosen, it's the one shown. Last, "cam:off": Camera off.
  const pick = (k) => {
    const o = opt(k);
    const diag = k === "camera" && diagOn();
    const dis = (k === "speaker" && !canPickSpeaker) || (o.none && !diag);
    const current = k === "camera" && !nextVideo.camOn ? "cam:off" : k === "camera" && nextVideo.source !== "camera" ? `src:${nextVideo.source}` : o.current;
    const option = ([id, label]) => `<option value="${escHtml(id)}"${id === current ? " selected" : ""}>${escHtml(label)}</option>`;
    const devs = o.none ? "" : o.opts.map(option).join("");
    const tests = diag ? `<optgroup label="Diagnostics">${Object.entries(SOURCE_LABEL).map(([kind, label]) => option([`src:${kind}`, label])).join("")}</optgroup>` : "";
    const camOffOpt = k === "camera" && state.videoAllowed !== false ? `<optgroup label="Or">${option(["cam:off", "Camera off: send my camera-off card"])}</optgroup>` : "";
    return `<select class="yd-pick" data-yd-pick="${k}" aria-label="${WORD[k]}" ${dis ? "disabled" : ""}>${(diag || camOffOpt) && devs ? `<optgroup label="Cameras">${devs}</optgroup>` : devs}${tests}${camOffOpt}</select>`;
  };
  const row = (k) => `<div class="yd-row${live[k] ? " live" : ""}${isOff(k) ? " off" : ""}">
      <span class="yd-ico">${icon[k]}</span><span class="yd-label">${WORD[k]}</span>${pick(k)}
      <span class="yd-note">${k === "mic" && yd.mic?.track ? `<span class="yd-meter" aria-hidden="true">${"<i></i>".repeat(14)}</span>` : ""}<span>${escHtml(n[k])}</span></span>
      ${act[k]}
    </div>`;
  const unnamed = KINDS.some((k) => !opt(k).named);
  const changed = KINDS.some(notDefault);
  return `${head(ctx === "ring" ? "For this call: Settings has your defaults" : "For your next call: Settings has your defaults")}
    <div class="yd-rows">${KINDS.map(row).join("")}</div>
    ${previewOn() ? `<div class="yd-preview"><video class="yd-video" muted autoplay playsinline></video></div>` : ""}
    ${changed ? `<p class="yd-foot">A change here lasts until your calls end; then it's your defaults again. <button type="button" class="link-btn" data-yd="defaults">Use my defaults</button></p>` : ""}
    ${unnamed ? `<p class="yd-foot">Device names show once the page has used your microphone or camera: Test mic or Preview does it.</p>` : ""}`;
}

/** Fills a Your devices container (el, a .yd-host) for ctx: "home", "dial", "ring", or
 * "calling" (read-only, for the call e you placed). */
export function renderYourDevices(el, ctx, e = null) {
  if (!el) return;
  const html = panelHtml(ctx, e);
  // A picker with focus may have its list open, so the panel waits to be redrawn (redrawing would
  // shut it), except right after a pick from it, or when its own choices change (diagnostics on or
  // off, a device plugged in): then it's redrawn and the picker keeps focus. Either way the
  // preview follows what's open: a camera picked, or a test source.
  const focused = el.contains(document.activeElement) && document.activeElement.tagName === "SELECT" ? document.activeElement.dataset.ydPick : null;
  const choices = (s) => [...(s?.options ?? [])].map((o) => `${o.value}\t${o.text}`).join("\n");
  const wait = focused && el.dataset.ctx === ctx && justPicked !== focused
    && choices(Object.assign(document.createElement("template"), { innerHTML: html }).content.querySelector(`[data-yd-pick="${focused}"]`)) === choices(document.activeElement);
  if (el.dataset.html !== html && !wait) {
    el.innerHTML = html;
    el.dataset.html = html;
    el.dataset.ctx = ctx;
    if (focused) el.querySelector(`[data-yd-pick="${focused}"]`)?.focus();
  }
  const v = el.querySelector(".yd-video");
  if (v) {
    const track = previewTrack();
    if ((v.srcObject?.getVideoTracks()[0] ?? null) !== track) v.srcObject = track ? new MediaStream([track]) : null;
    v.classList.toggle("mirror", !!track && track === localTrack("video"));   // the camera, as a mirror; not a card or test source
  }
}

// ---- The indicator: the microphone and camera, only while they're open ----
// usage(): { mic, camera }, each null (closed) or { state: "live" | "muted", why, name }.
function usage() {
  const mic = localTrack("audio");
  let m = null;
  if (mic) {
    const talking = entries.get(audioId);
    if (talking?.m && !holdState(talking)) {
      m = mic.enabled && !talking.m.muted ? { state: "live", why: `${nameOf(talking)} hears you` }
        : { state: "muted", why: `Open, muted on your call with ${nameOf(talking)}` };
    } else {
      // A call being placed or answered (its media not set up yet), or every call on hold.
      const starting = [...entries.values()].find((e) => !e.m && ["calling", "ringing", "connected"].includes(e.h.call));
      const held = [...entries.values()].some((e) => e.m && holdState(e));
      m = { state: "muted", why: starting ? `Open for your call with ${nameOf(starting)}`
        : held ? "Open: your calls are on hold, so no one hears you" : "Open for your call" };
    }
    m.name = mic.label;
  } else if (yd.mic?.track) {
    m = { state: "live", why: "Your mic test: only you see the meter", name: yd.mic.track.label };
  }
  const cam = localTrack("video");
  let c = null;
  if (cam) {
    const sending = [...entries.values()].filter((e) => e.m?.hasVideo && e.m.camOn && !holdState(e) && e.h.call === "connected" && sourceOf(e) === "camera");
    const names = sending.map(nameOf);
    c = names.length ? { state: "live", why: `${names.join(" and ")} ${names.length > 1 ? "see" : "sees"} you` }
      : previewOn() ? { state: "live", why: "Your preview: only you see it" }
      : { state: "live", why: "Open for your call" };
    c.name = cam.label;
  }
  return { mic: m, camera: c };
}

const devLog = [];   // { at, text }: when each opened and closed, newest last
let was = { mic: null, camera: null };
let popOpen = false;

export function renderIndicator() {
  const u = usage();
  for (const k of ["mic", "camera"]) {
    const word = k === "mic" ? "Microphone" : "Camera";
    // "Microphone opened: muted on your call with Bob", not "opened: open, muted…".
    const why = u[k]?.why.replace(/^Open[,:]?\s*/, "");
    if (!!u[k] !== !!was[k]) devLog.push({ at: Date.now(), text: u[k] ? `${word} opened: ${why.charAt(0).toLowerCase()}${why.slice(1)}` : `${word} closed` });
  }
  if (devLog.length > 8) devLog.splice(0, devLog.length - 8);
  was = u;
  const btn = $("devInd");
  const html = `${u.mic ? `<span class="ind ${u.mic.state}">${u.mic.state === "muted" ? ICON.micOff : ICON.mic}</span>` : ""}${u.camera ? `<span class="ind live">${ICON.video}</span>` : ""}`;
  if (btn.innerHTML !== html) btn.innerHTML = html;
  btn.hidden = !u.mic && !u.camera;
  const what = [u.mic && `Microphone: ${u.mic.why}`, u.camera && `Camera: ${u.camera.why}`].filter(Boolean);
  btn.title = what.length ? `${what.join("\n")}\nClick for more` : "";
  btn.setAttribute("aria-label", `In use: ${[u.mic && "microphone", u.camera && "camera"].filter(Boolean).join(" and ")}`);
  if (btn.hidden) popOpen = false;
  btn.setAttribute("aria-expanded", String(popOpen));
  const pop = $("devPop");
  pop.hidden = !popOpen;
  if (!popOpen) return;
  const clock = (at) => new Date(at).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit" });
  const row = (k, word) => (u[k] ? `<div class="dp-row ${u[k].state}"><span class="dp-ico">${k === "mic" ? (u[k].state === "muted" ? ICON.micOff : ICON.mic) : ICON.video}</span>
      <span class="dp-main"><b>${word}</b>${u[k].name ? ` <span class="dp-dev">${escHtml(u[k].name)}</span>` : ""}<span class="dp-why">${escHtml(u[k].why)}</span></span></div>` : "");
  const html2 = `<div class="dp-head">In use now</div>${row("mic", "Microphone")}${row("camera", "Camera")}
    <div class="dp-head dp-log-head">Opened and closed</div>
    <ol class="dp-log">${devLog.slice().reverse().map((x) => `<li><span class="dp-at">${clock(x.at)}</span>${escHtml(x.text)}</li>`).join("")}</ol>`;
  if (pop.innerHTML !== html2) pop.innerHTML = html2;
}

// Run by app.js once the page is up.
export function initYourDevices() {
  refreshList().then(render);
  navigator.mediaDevices?.addEventListener?.("devicechange", () => refreshList().then(render));
  document.addEventListener("click", (e) => {
    const t = e.target.closest("[data-yd]");
    if (!t || t.disabled) return;
    switch (t.dataset.yd) {
      case "toggle": if (yd.open) closeDevices(); else openPanel(); render(); break;
      case "line": openPanel(t.dataset.dev); break;
      case "mic-test": if (yd.mic) { stopMicTest(); render(); } else startMicTest(); break;
      case "preview": setPreview(!yd.preview); break;
      case "sound": playSound(); break;
      case "defaults": useDefaults(); break;
    }
  });
  document.addEventListener("change", async (e) => {
    const k = e.target.dataset?.ydPick;
    if (!k) return;
    const value = e.target.value;
    if (k === "camera" && value === "cam:off") {
      // Camera off: the next video call sends your camera-off card, which the preview shows.
      setStartCamera(false);
    } else if (k === "camera" && value.startsWith("src:")) {
      // A test source (diagnostics): for the preview and the next video call, as the Camera ▾'s.
      setStartCamera(true);
      setVideoSource(value.slice(4));
    } else {
      if (k === "camera") setStartCamera(true);
      if (k === "camera" && nextVideo.source !== "camera") setVideoSource("camera");
      await chooseDevice(k, value);
    }
    if (k === "mic" && yd.mic) startMicTest();   // the meter follows the microphone picked
    await syncCamera();   // the preview's source open before it's redrawn
    justPicked = k;
    render();
    justPicked = null;
  });
  // The indicator's popover: a click says why; a click away or Esc closes it.
  $("devInd").onclick = () => { popOpen = !popOpen; renderIndicator(); };
  document.addEventListener("pointerdown", (e) => {
    if (popOpen && !e.target.closest("#devPop, #devInd")) { popOpen = false; renderIndicator(); }
  });
  document.addEventListener("keydown", (e) => {
    if (e.key === "Escape" && popOpen) { popOpen = false; renderIndicator(); $("devInd").focus(); }
  });
}
