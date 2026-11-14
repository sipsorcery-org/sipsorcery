// The device menus (the ▾ on Mute and Camera), Answer ▾, and the self view's button.

import { MIC_CONSTRAINTS, answerCall, answerCamera, call, camStart, cameras, devices, entries, inGrid, local, localTrack, nextVideo, privacyRelease, render, saveDevices, sel, selectedId, setAnswerCamera, setCamPref, setCamera, settings, state } from "./app.js";
import { disarmEndAll, fillAllMenu } from "./calllist.js";
import { analyserFor } from "./levels.js";
import { initSelfView } from "./selfview.js";
import { $, ICON, log, post } from "./util.js";
import { SOURCE_LABEL, cameraIdOf, cameraInUse, cameraOpenedFor, hasCamera, retryCamera, setVideoSource, sourceOf, syncCamera, videoAvailable } from "./video.js";
// ---- Device menus (the ▾ on Mute and Camera) ----
// Mute's lists microphones and speakers; Camera's lists cameras, and in diagnostics mode the
// test pattern. A pick takes effect straight away, mid-call too (a track swap, no renegotiation),
// and is remembered for next time.

export let openMenu = null;   // "mic", "cam", "answer", "all" (the calls panel's ⋯) or null
const MENUS = { mic: ["micMenu", "micMenuBtn"], cam: ["camMenu", "camMenuBtn"], answer: ["answerMenu", "answerMenuBtn"], all: ["allMenu", "allMenuBtn"] };

// ---- Answering a video call: Answer ▾ ----
// The ▾ picks what your video will be: a camera, the test pattern (diagnostics mode), or camera
// off. Under "Answer" the button says which, so you know before you answer. It starts from the
// Camera button's choice (nextVideo.camOn), so camera off there means camera off here.
export const answerSplit = document.createElement("div");

const answerCamOn = () => (answerCamera ?? camStart()) && videoAvailable();
function answerSourceLabel() {
  if (!answerCamOn()) {
    return nextVideo.source === "camera" && !hasCamera ? "No camera"
      : nextVideo.source === "camera" && cameraInUse ? "Camera in use" : "Camera off";
  }
  if (nextVideo.source !== "camera") return SOURCE_LABEL[nextVideo.source];
  // The camera the answered call will open (devices.camera, as the ▾ ticks), not the first one.
  return cameras.find((d) => d.deviceId === currentCameraId(cameras, devices.camera))?.label || "Camera";
}

// current: the call you're on, which answering holds.
export function renderAnswer(current) {
  const video = !!state.incomingVideo && state.videoAllowed !== false;
  const on = video && answerCamOn();
  const main = `${video ? (on ? ICON.video : ICON.videoOff) : ICON.phone}<span>${current ? "Answer & hold current" : "Answer"}</span>`;
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
// The camera ticked: the call's (or for Answer, the next call's), as opened if it's the open one.
function currentCameraId(cameras, want) {
  const track = localTrack("video");
  if (track && want === cameraOpenedFor) return track.getSettings().deviceId;
  return cameras.some((d) => d.deviceId === want) ? want : cameras[0]?.deviceId;
}

export async function showMenu(kind) {
  if (openMenu === kind) { closeMenu(); return; }
  closeMenu();
  openMenu = kind;
  if (kind === "all") {
    fillAllMenu();
    $("allMenu").hidden = false;
    fitInStage($("allMenu"));
    $("allMenuBtn").setAttribute("aria-expanded", "true");
    $("allMenu").querySelector(".device-option:not(:disabled)")?.focus();
    return;
  }
  let list = [];
  try { list = await navigator.mediaDevices.enumerateDevices(); } catch { /* none to show */ }
  if (openMenu !== kind) return;
  const of = (k) => list.filter((d) => d.kind === k && d.deviceId);
  // The Camera ▾ ticks the selected call's source; Answer's, the one the new call will start with.
  const source = kind === "answer" ? nextVideo.source : sourceOf(sel());
  const groups = kind === "mic"
    ? [["mic", "Microphone", of("audioinput"), currentMicId()],
       ["speaker", "Speaker", of("audiooutput"), devices.speaker || "default"]]
    : [["camera", "Camera", of("videoinput"), source !== "camera" ? null : currentCameraId(of("videoinput"), kind === "answer" ? devices.camera : cameraIdOf(sel()))]];
  if (kind !== "mic" && document.body.classList.contains("diag")) {
    groups.push(["test", "Diagnostics", Object.entries(SOURCE_LABEL).map(([deviceId, label]) => ({ deviceId, label })),
      source !== "camera" ? source : null]);
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
// it out (the ⋯ menu likewise, in the calls panel). It's positioned against the window instead (position: fixed isn't clipped by the
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

export function closeMenu() {
  if (!openMenu) return;
  const kind = openMenu;
  openMenu = null;
  if (kind === "all") disarmEndAll();
  $(MENUS[kind][0]).hidden = true;
  $(MENUS[kind][1]).setAttribute("aria-expanded", "false");
}

// From the Answer menu (forAnswer), a camera or the test pattern also means camera on for the
// call being answered, and "off" means camera off. A source picked from the Camera ▾ is for the
// selected call (and the next ones); from Answer's, for the call being answered (and the next).
export async function pickDevice(key, id, forAnswer = false) {
  const target = forAnswer ? null : sel();
  closeMenu();
  if (forAnswer) { setAnswerCamera(key !== "off"); if (answerCamera) privacyRelease(); }
  if (key === "off") log("answering with camera off");
  else if (key === "mic") await pickMic(id);
  else if (key === "speaker") {
    devices.speaker = id;
    saveDevices();
    try {
      for (const e of entries.values()) await e.el.setSinkId(id);
      log(`speaker: ${id}`);
    } catch (err) { log(`speaker unavailable: ${err.name} ${err.message}`); }
  } else if (key === "camera") {
    devices.camera = id;
    saveDevices();
    if (target?.m) target.cameraId = id;
    log(`camera${target?.m ? ` for call ${target.id}` : ""}: ${id}`);
    retryCamera();   // a fresh try, because you asked
    setVideoSource("camera", target);
    await syncCamera();   // switches to it, if something is sending the camera
    if (!forAnswer) await cameraOnForPick();
  } else if (key === "test") {
    setVideoSource(id, target);   // the test pattern or audio bars
    if (!forAnswer) await cameraOnForPick();
  }
  render();
}

// Picking a source from the Camera menu means you want to send it: if the camera was off, it
// comes on, on a video call or (before one) for the next video call. Not on an audio call,
// where it's only the choice for Add video.
async function cameraOnForPick() {
  if (call?.hasVideo) { if (!call.camOn) await setCamera(true, sel(), true); }
  else if (!call && !camStart()) setCamPref(true);
}

// A new microphone replaces the old one in place: same mute state, and on a call the audio
// sender just swaps tracks.
export async function pickMic(id) {
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
  const senders = [...entries.values()].map((e) => e.m?.audioSender).filter((s) => s && old && s.track === old);
  if (old) {
    fresh.enabled = old.enabled;
    local.removeTrack(old);
    old.stop();
  }
  local.addTrack(fresh);
  for (const sender of senders) await sender.replaceTrack(fresh);
  for (const e of entries.values()) {
    if (!e.m) continue;
    e.m.localAnalyser?.source.disconnect();
    e.m.localAnalyser = analyserFor(fresh);
  }
  log(`microphone: ${fresh.label}`);
}

// Self view: shows or hides your own picture during a video call (what's sent doesn't change).
// Remembered between runs. V does the same, unless you're typing.
export let selfViewOn = true;
function toggleSelfView() {
  selfViewOn = !selfViewOn;
  settings?.refresh();
  try { localStorage.setItem("selfView", selfViewOn ? "on" : "off"); } catch { /* storage unavailable */ }
  log(`self view ${selfViewOn ? "on" : "off"}`);
  render();
}

// Simulated loss (Diagnostics menu): the host drops that share of the audio or video packets
// we send on the SIP leg. Labels follow the slider as it moves; the host hears once it's let go.
export function showLoss() {
  for (const kind of ["Audio", "Video"]) {
    const v = Number($(`loss${kind}`).value);
    $(`loss${kind}Out`).textContent = `${v}%`;
    $(`loss${kind}Out`).classList.toggle("on", v > 0);
  }
  const on = Number($("lossAudio").value) > 0 || Number($("lossVideo").value) > 0;
  $("tabNet").innerHTML = `Network${on ? `<span class="dev-dot" title="Loss is on"></span>` : ""}`;
}

// Run by app.js at this point in its start-up, in the order it always ran.
export function initMenus() {
  answerSplit.className = "split answer-split";
  answerSplit.innerHTML = `<button type="button" class="btn btn-primary" id="answerBtn"><span class="answer-main"></span><span class="answer-src"></span></button>
  <button type="button" class="btn btn-primary split-caret" id="answerMenuBtn" aria-haspopup="menu" aria-expanded="false"
    aria-label="Choose your camera for this call" title="Choose your camera for this call">${ICON.chevronDown}</button>
  <div class="device-menu down" id="answerMenu" role="menu" aria-label="Your camera" hidden></div>`;
  answerSplit.querySelector("#answerBtn").onclick = () => answerCall(selectedId);
  answerSplit.querySelector("#answerMenuBtn").onclick = () => showMenu("answer");
  addEventListener("resize", () => { if (openMenu === "answer" || openMenu === "all") closeMenu(); });

  $("micMenuBtn").onclick = () => showMenu("mic");
  $("camMenuBtn").onclick = () => showMenu("cam");
  document.addEventListener("pointerdown", (e) => {
    if (openMenu && !e.target.closest(".device-menu, .split-caret, .calls-more")) closeMenu();
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
  try { selfViewOn = localStorage.getItem("selfView") !== "off"; } catch { /* storage unavailable */ }
  $("selfViewBtn").onclick = toggleSelfView;
  initSelfView($("stage"), $("selfBox"), $("selfGrip"));   // drag, resize and reset it
  document.addEventListener("keydown", (e) => {
    if (e.key.toLowerCase() !== "v" || e.ctrlKey || e.altKey || e.metaKey || openMenu) return;
    if (e.target.closest?.("input, textarea, select, [contenteditable]")) return;
    if ($("selfViewBtn").hidden || inGrid()) return;   // no self view to show or hide
    e.preventDefault();
    toggleSelfView();
  });
  for (const kind of ["Audio", "Video"]) {
    $(`loss${kind}`).addEventListener("input", showLoss);
    $(`loss${kind}`).addEventListener("change", () => post({ type: "loss", audio: Number($("lossAudio").value), video: Number($("lossVideo").value) }));
  }
}
