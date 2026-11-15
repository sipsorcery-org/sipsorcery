// The device menus (the ▾ on Mute and Camera), and the self view's button.

import { MIC_CONSTRAINTS, call, devices, entries, inGrid, local, localTrack, render, sel, setCamera, settings } from "./app.js";
import { disarmEndAll, fillAllMenu } from "./calllist.js";
import { fillDevMenu, fitDevTabs } from "./diagnostics.js";
import { analyserFor } from "./levels.js";
import { initSelfView } from "./selfview.js";
import { $, ICON, log, post } from "./util.js";
import { SOURCE_LABEL, cameraIdOf, cameraOpenedFor, retryCamera, setVideoSource, sourceOf, syncCamera } from "./video.js";
// ---- Device menus (the ▾ on Mute and Camera) ----
// Mute's lists microphones and speakers; Camera's lists cameras, in diagnostics mode the test
// pattern, and on a video call Camera off (your camera-off card, as Stop video). A pick takes effect straight away, mid-call too (a track swap, no renegotiation),
// and is on demand: it lasts until the last call ends, then the next call starts with Settings'
// defaults (app.js, resetDevices).

export let openMenu = null;   // "mic", "cam", "all" (the calls panel's ⋯), "more" (the control bar's ⋯), "dev" (Diagnostics' ⋯) or null
const MENUS = { mic: ["micMenu", "micMenuBtn"], cam: ["camMenu", "camMenuBtn"], all: ["allMenu", "allMenuBtn"], more: ["moreMenu", "moreBtn"], dev: ["devMenu", "devMoreBtn"] };

const currentMicId = () => localTrack("audio")?.getSettings().deviceId ?? (devices.mic || "default");
// The camera ticked: the call's (or with none, the next call's), as opened if it's the open one.
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
  // Diagnostics' ⋯, at the top of the panel: positioned as the calls panel's is (the panel can
  // be shorter than the menu), below the button if it fits.
  if (kind === "dev") {
    fillDevMenu();
    $("devMenu").hidden = false;
    fitInStage($("devMenu"));
    $("devMoreBtn").setAttribute("aria-expanded", "true");
    $("devMenu").querySelector(".device-option")?.focus();
    return;
  }
  if (kind === "more") {
    fillMoreMenu();
    $("moreMenu").hidden = false;
    $("moreBtn").setAttribute("aria-expanded", "true");
    $("moreMenu").querySelector(".device-option:not(:disabled)")?.focus();
    return;
  }
  let list = [];
  try { list = await navigator.mediaDevices.enumerateDevices(); } catch { /* none to show */ }
  if (openMenu !== kind) return;
  const of = (k) => list.filter((d) => d.kind === k && d.deviceId);
  // The Camera ▾ ticks the selected call's source (with none, the one the next call starts with),
  // or on a video call with your camera off, Camera off.
  const source = sourceOf(sel());
  const camOff = !!call?.hasVideo && !call.camOn;
  const groups = kind === "mic"
    ? [["mic", "Microphone", of("audioinput"), currentMicId()],
       ["speaker", "Speaker", of("audiooutput"), devices.speaker || "default"]]
    : [["camera", "Camera", of("videoinput"), camOff || source !== "camera" ? null : currentCameraId(of("videoinput"), cameraIdOf(sel()))]];
  if (kind !== "mic" && document.body.classList.contains("diag")) {
    groups.push(["test", "Diagnostics", Object.entries(SOURCE_LABEL).map(([deviceId, label]) => ({ deviceId, label })),
      !camOff && source !== "camera" ? source : null]);
  }
  if (kind !== "mic" && call?.hasVideo) groups.push(["off", "", [{ deviceId: "off", label: "Camera off: send my camera-off card" }], camOff ? "off" : null]);
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
      opt.onclick = () => pickDevice(key, d.deviceId);
      menu.append(opt);
    });
  });
  menu.hidden = false;
  $(MENUS[kind][1]).setAttribute("aria-expanded", "true");
  (menu.querySelector('[aria-checked="true"]') ?? menu.querySelector(".device-option"))?.focus();
}

// The ⋯ menu is inside the calls panel, which clips (overflow: hidden), so a z-index can't lift
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

export function closeMenu() {
  if (!openMenu) return;
  const kind = openMenu;
  openMenu = null;
  if (kind === "all") disarmEndAll();
  $(MENUS[kind][0]).hidden = true;
  $(MENUS[kind][1]).setAttribute("aria-expanded", "false");
}

// A source picked from the Camera ▾ is for the selected call (and any started while it's on).
// The default device is Settings'; whether it's on is the call's.
export async function pickDevice(key, id) {
  const target = sel();
  closeMenu();
  if (key === "mic") await pickMic(id);
  else if (key === "speaker") {
    devices.speaker = id;
    try {
      for (const e of entries.values()) await e.el.setSinkId(id);
      log(`speaker: ${id}`);
    } catch (err) { log(`speaker unavailable: ${err.name} ${err.message}`); }
  } else if (key === "camera") {
    devices.camera = id;
    if (target?.m) target.cameraId = id;
    log(`camera${target?.m ? ` for call ${target.id}` : ""}: ${id}`);
    retryCamera();   // a fresh try, because you asked
    setVideoSource("camera", target);
    await syncCamera();   // switches to it, if something is sending the camera
    await cameraOnForPick();
  } else if (key === "test") {
    setVideoSource(id, target);   // the test pattern or audio bars
    await cameraOnForPick();
  } else if (key === "off") {
    if (call?.hasVideo && call.camOn) await setCamera(false, target);   // as Stop video
  }
  render();
}

// Picking a source from the Camera menu on a video call means you want to send it: if the camera
// was off, it comes on (not in privacy mode: setCamera won't). Before a call, or on an audio call,
// it's only the choice: picking a camera never turns it on by itself.
async function cameraOnForPick() {
  if (call?.hasVideo && !call.camOn) await setCamera(true, sel(), true);
}

// A new microphone replaces the old one in place: same mute state, and on a call the audio
// sender just swaps tracks.
export async function pickMic(id) {
  devices.mic = id;
  if (!localTrack("audio")) { log(`microphone for the next call: ${id || "system default"}`); return; }   // opens with it
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

// ---- The control bar's ⋯ More (phone width only: styles.css) ----
// Transfer, Keypad, Text and Self view, which are hidden there. Each item stands for its button
// and does what pressing it does, so all their rules (disabled, hidden, the panels they open)
// stay in one place; the menu only shows their state: disabled, open or on (ticked), new text.
const MORE = [["transferBtn", "transfer", "Transfer…"], ["keypadBtn", "keypad", "Keypad"], ["textBtn", "text", "Text"], ["selfViewBtn", "selfView", "Self view"]];
const DOTS = '<svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="5" cy="12" r="1.8"/><circle cx="12" cy="12" r="1.8"/><circle cx="19" cy="12" r="1.8"/></svg>';
let moreShown = "";   // what the open menu was drawn from, so a render only redraws it on a change
const moreItems = () => MORE.map(([id, icon, label]) => {
  const b = $(id);
  return { b, icon, label, hidden: b.hidden, disabled: b.disabled, on: b.getAttribute("aria-pressed") === "true",
    toggle: id !== "transferBtn", unread: !!b.querySelector(".btn-dot") };
});
function fillMoreMenu() {
  const items = moreItems();
  moreShown = JSON.stringify(items.map(({ b, ...rest }) => rest));
  const menu = $("moreMenu");
  menu.replaceChildren();
  for (const it of items) {
    if (it.hidden) continue;
    const opt = Object.assign(document.createElement("button"), { type: "button", className: "device-option more-option", disabled: it.disabled, title: it.b.title });
    opt.setAttribute("role", it.toggle ? "menuitemcheckbox" : "menuitem");
    if (it.toggle) opt.setAttribute("aria-checked", String(it.on));
    opt.innerHTML = `<span class="more-icon">${ICON[it.icon]}</span><span></span><span class="device-check">${it.toggle && it.on ? ICON.check : ""}</span>`;
    opt.children[1].textContent = it.label;
    if (it.unread) opt.children[1].append(Object.assign(document.createElement("span"), { className: "more-new", textContent: " · new" }));
    opt.onclick = () => { closeMenu(); it.b.click(); };
    menu.append(opt);
  }
}
// From render(), after the four buttons are drawn: the dot, the accent edge, and an open menu.
export function renderMore() {
  const items = moreItems();
  const unread = items.some((it) => it.unread);
  const btn = $("moreBtn");
  const html = `${DOTS}${unread ? '<span class="btn-dot" aria-hidden="true"></span>' : ""}`;
  if (btn.innerHTML !== html) btn.innerHTML = html;
  btn.setAttribute("aria-label", unread ? "More, new text message" : "More");
  btn.classList.toggle("active", items.some((it) => it.b.id !== "selfViewBtn" && it.on && !it.hidden));
  if (openMenu === "more" && JSON.stringify(items.map(({ b, ...rest }) => rest)) !== moreShown) {
    const had = document.activeElement && $("moreMenu").contains(document.activeElement);
    fillMoreMenu();
    if (had) $("moreMenu").querySelector(".device-option:not(:disabled)")?.focus();
  }
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
  fitDevTabs();   // the ⋯ carries the dot if Network's in its menu
}

// Run by app.js at this point in its start-up, in the order it always ran.
export function initMenus() {
  addEventListener("resize", () => { if (openMenu === "all" || openMenu === "dev") closeMenu(); });

  $("micMenuBtn").onclick = () => showMenu("mic");
  $("camMenuBtn").onclick = () => showMenu("cam");
  $("moreBtn").onclick = () => showMenu("more");
  $("devMoreBtn").onclick = () => showMenu("dev");
  // The ⋯ goes when the window's widened past phone width: its four buttons are back.
  addEventListener("resize", () => { if (openMenu === "more" && !$("moreBtn").offsetParent) closeMenu(); });
  document.addEventListener("pointerdown", (e) => {
    if (openMenu && !e.target.closest(".device-menu, .split-caret, .calls-more, .more-btn, .dev-more-btn")) closeMenu();
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
