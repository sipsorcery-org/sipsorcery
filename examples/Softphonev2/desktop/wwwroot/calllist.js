// The Active calls panel down the left, the grid of calls, and End all.

import { audioId, focusCall, gridCalls, hasMic, holdState, inGrid, isLive, listed, liveEntries, nameOf, selectedId, setView, tiles, view } from "./app.js";
import { setAvatar } from "./avatar.js";
import { contactMarkEl } from "./contactmark.js";
import { contactFor } from "./contacts.js";
import { closeMenu, openMenu, showMenu } from "./menus.js";
import { openLine } from "./rtt.js";
import { transferShort } from "./transfer.js";
import { $, ICON, escHtml, initial, log, post } from "./util.js";
import { sourceAvailable, sourceOf } from "./video.js";

// ---- Active calls panel, down the left ----
// Every call, oldest at the top and new ones at the bottom, so a call never moves. Each row shows who, what you're sending (mic and camera), and
// the call's state: calling, ringing, connected, on hold with how long (or "Holding…" and
// "Resuming…" while the re-INVITE is out), or how it ended. Clicking a row puts that call on the
// stage, as do the number keys. The chevron folds it to a rail of avatars and timers, which
// leaves room for the call info beside Mute; the choice is remembered. Under 700 px it's always
// the rail, with no chevron.

// Narrow windows (under 700 px): the panel is a rail of avatars, to leave the stage its room.
const narrowCalls = matchMedia("(max-width: 699px)");
let callsCollapsed = false;   // your choice on wider windows
const CALLS_ICON = {
  collapse: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="m15 18-6-6 6-6"/></svg>',
  expand: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="m9 18 6-6-6-6"/></svg>',
};

export const fmtElapsed = (ms) => {
  const s = Math.max(0, Math.floor(ms / 1000));
  const h = Math.floor(s / 3600), m = Math.floor(s / 60) % 60, ss = String(s % 60).padStart(2, "0");
  return h ? `${h}:${String(m).padStart(2, "0")}:${ss}` : `${String(m).padStart(2, "0")}:${ss}`;
};

// Whether a call gets your microphone: it's the live call, you haven't muted it, and neither
// side has it on hold (held, it gets music or silence instead).
export const getsMic = (e) => hasMic && !!e.m && !e.m.muted && e.id === audioId && !holdState(e) && !e.h.heldByThem;

// One call's status line, for the list (and grid tiles): its state, what it gets from you, and
// text. { status (row class), statusText, since, muted, held, long (held over a minute), audioOff,
// sends: { mic, cam } each { on, muted, label } (cam null on an audio call), typing, unread }.
export function callStatus(e) {
  const h = e.h, m = e.m;
  const c = { status: h.call, statusText: "", since: 0, muted: false, held: false, long: false, audioOff: false, sends: null, typing: false, unread: false };
  if (h.call === "calling") c.statusText = "Calling…";
  else if (h.call === "ringing") c.statusText = h.incomingVideo ? "Incoming video call" : "Incoming call";
  else if (h.call === "ended") c.statusText = h.reason || "Call ended";
  else if (!m) c.statusText = "Connecting media…";
  else {
    c.since = e.connectedAt;
    c.held = !!h.held;
    c.muted = hasMic && m.muted;
    c.audioOff = !c.held && e.id !== audioId;
    c.typing = !!openLine(e.text, false, false);
    c.unread = e.text.unread;
    const pending = e.holdWanted !== undefined && e.holdWanted !== c.held;
    const xt = h.transfer;
    // A transfer under way says so first: it's what's happening to the call.
    if (xt && (xt.status === "asking" || xt.status === "calling" || xt.status === "offered")) c.statusText = transferShort(xt);
    else if (xt?.status === "failed" && xt.role === "out") c.statusText = "Transfer failed · on hold";
    else if (pending) c.statusText = e.holdWanted ? "Holding…" : "Resuming…";
    else if (c.held) {
      c.long = Date.now() - e.heldAt > 60000;
      c.statusText = `On hold · ${fmtElapsed(Date.now() - e.heldAt)}`;
    } else if (h.heldByThem) c.statusText = `${nameOf(e)} put you on hold`;
    else if (c.typing) c.statusText = "Typing…";
    else if (c.unread) c.statusText = e.text.lines.findLast((l) => !l.me)?.text || "New message";
    else c.statusText = c.audioOff ? "Audio off" : c.muted ? "Muted" : m.hasVideo ? "Video call" : "Audio call";
    // What they get from you, at a glance: your mic and camera, or why not.
    const held = holdState(e), music = h.holdMode !== "sip";
    const mic = getsMic(e) ? { on: true, label: `${nameOf(e)} hears you` }
      : { on: false, muted: hasMic && m.muted,
          label: !hasMic ? "No mic"
            : m.muted ? "Muted: they don't hear you"
            : held ? `On hold: they hear ${music ? "music" : "silence"}, not you`
            : h.heldByThem ? "They've put you on hold: they don't hear you"
            : "Your mic is on another call" };
    const camOn = m.hasVideo && m.camOn && !held && sourceAvailable(sourceOf(e));
    const cam = !m.hasVideo ? null : camOn ? { on: true, label: `${nameOf(e)} sees your video` }
      : { on: false, muted: !m.camOn,
          label: held ? (music ? "On hold: they see the On hold card" : "On hold: they see nothing")
            : !m.camOn ? "Video stopped: they see your initial"
            : "No camera: they see your initial" };
    c.sends = { mic, cam };
  }
  return c;
}

// The chips for what a call gets from you (see callStatus).
export function sendsHtml(c) {
  if (!c.sends) return "";
  const chip = (s, on, off) => `<span class="send${s.on ? " on" : s.muted ? " muted" : ""}" role="img" title="${escHtml(s.label)}" aria-label="${escHtml(s.label)}">${s.on ? on : off}</span>`;
  return chip(c.sends.mic, ICON.mic, ICON.micOff) + (c.sends.cam ? chip(c.sends.cam, ICON.video, ICON.videoOff) : "");
}

// The panel shows only with two or more calls (or while the grid is on screen): with one, the
// stage says all there is to say about it.
export function renderCalls() {
  const calls = listed();
  const panel = $("calls");
  panel.hidden = calls.length < 2 && !inGrid();
  const rail = narrowCalls.matches || callsCollapsed;
  panel.classList.toggle("collapsed", rail);
  const toggle = $("callsToggle");
  toggle.hidden = narrowCalls.matches;
  const label = rail ? "Show active calls" : "Fold active calls";
  toggle.setAttribute("aria-expanded", String(!rail));
  toggle.setAttribute("aria-label", label);
  toggle.title = label;
  const icon = rail ? CALLS_ICON.expand : CALLS_ICON.collapse;
  if (toggle.innerHTML !== icon) toggle.innerHTML = icon;
  $("callCount").textContent = calls.filter(isLive).length || "";
  $("callList").replaceChildren(...calls.map((e, i) => callRow(e, i, rail)));
  renderAllButtons();
}

// ---- Every call at once: Grid, and ⋯ for End all ----
// Under the list. Grid is a toggle that looks pressed while the grid is the view (a ringing call's
// answer view leaves it pressed, as it goes back to the grid once answered); picking a call (its
// row, its tile) goes back to Focus too. ⋯ (with two calls you can end) opens End all, which is
// rarely needed and asks again ("End 3 calls?") for a few seconds before it ends them: calls you've placed and calls
// you're on, not one that's only ringing.
// (Not a call you're placing over another: that's the banner's to Cancel.)
const endable = () => liveEntries().filter((e) => !e.pending && (e.h.call === "calling" || e.h.call === "connected"));
let endAllUntil = 0;
export const disarmEndAll = () => { endAllUntil = 0; };
function renderAllButtons() {
  const n = endable().length, grid = inGrid();
  $("callsFoot").hidden = n < 2 && !grid;
  const gridBtn = $("gridBtn");
  const tiles = gridCalls().length;
  const gridOn = view === "grid";
  gridBtn.disabled = !gridOn && tiles < 2;
  gridBtn.setAttribute("aria-pressed", String(gridOn));
  // With the number of calls it shows, as a badge (on the rail, over the icon's corner).
  const html = `${ICON.grid}<span>Grid</span>${tiles > 1 ? `<span class="count">${tiles}</span>` : ""}`;
  if (gridBtn.innerHTML !== html) gridBtn.innerHTML = html;
  gridBtn.title = gridOn ? "Showing every call in a grid. Click again, or pick a call, for one call (G)" : `Show all ${tiles} calls in a grid (G)`;
  $("allMenuBtn").hidden = n < 2;
  if (n < 2) { endAllUntil = 0; if (openMenu === "all") closeMenu(); }
  else if (openMenu === "all") fillAllMenu();
}
// The ⋯ menu's items, refilled as things change while it's open (End all's confirm, say).
export function fillAllMenu() {
  const menu = $("allMenu"), n = endable().length;
  const at = [...menu.querySelectorAll(".device-option")].indexOf(document.activeElement);
  const item = (icon, text, title, onclick, cls = "") => {
    const b = Object.assign(document.createElement("button"), { type: "button", className: `device-option ${cls}`.trim(), title, onclick });
    b.setAttribute("role", "menuitem");
    b.innerHTML = `<span class="device-check">${icon}</span><span></span>`;
    b.lastChild.textContent = text;
    return b;
  };
  const armed = Date.now() < endAllUntil;
  menu.replaceChildren(item(ICON.hangup, armed ? `End ${n} calls? Click again` : `End all ${n} calls`,
    armed ? `Click again to end all ${n} calls` : `End all ${n} calls`, endAll, "danger"));
  if (at >= 0) menu.querySelectorAll(".device-option")[at]?.focus();
}
function endAll() {
  if (Date.now() >= endAllUntil) {
    endAllUntil = Date.now() + 4000;
    setTimeout(renderAllButtons, 4100);
    renderAllButtons();
    return;
  }
  endAllUntil = 0;
  closeMenu();
  const calls = endable();
  log(`ending all ${calls.length} calls`);
  for (const e of calls) post({ type: "hangup", id: e.id });
  renderAllButtons();
}

function callRow(e, i, collapsed = false) {
  const c = callStatus(e);
  const name = nameOf(e);
  const li = document.createElement("li");
  li.className = `call-row ${c.status}${c.muted ? " muted" : ""}${c.held ? " held" : ""}${c.long ? " long" : ""}${c.audioOff ? " audio-off" : ""}`;
  li.dataset.call = e.id;
  if (e.id === selectedId) li.setAttribute("aria-current", "true");   // the call on the stage
  li.title = [e.h.remote || name, c.statusText, i < 8 ? `(${i + 1})` : ""].filter(Boolean).join(" · ");
  // A click picks the call.
  li.onclick = (ev) => {
    ev.stopPropagation();
    focusCall(e.id);
  };
  const el = (tag, cls, text) => Object.assign(document.createElement(tag), { className: cls, textContent: text ?? "" });
  const avatar = el("span", "call-avatar", initial(name));
  avatar.setAttribute("aria-hidden", "true");
  setAvatar(avatar, e.h.remote);
  const top = el("span", "call-top");
  top.append(el("span", "call-name", name));
  const contact = contactFor(e.h.remote);
  if (contact) top.firstChild.append(contactMarkEl(contact.name));
  // What it gets from you: beside the name, or on the rail under the avatar.
  const sends = el("span", "call-sends");
  sends.innerHTML = sendsHtml(c);
  if (c.sends && !collapsed) top.append(sends);
  const main = el("span", "call-main");
  main.append(top, el("span", "call-status", c.statusText));
  const time = el("span", "call-time", c.since ? fmtElapsed(Date.now() - c.since) : "");
  if (c.since) time.dataset.since = c.since;
  li.append(avatar, main);
  if (c.sends && collapsed) li.append(sends);
  li.append(time);
  return li;
}

// Every second while connected: just the timers.
export function renderCallTimes() {
  for (const t of document.querySelectorAll("#callList .call-time[data-since]")) t.textContent = fmtElapsed(Date.now() - Number(t.dataset.since));
}

// Run by app.js at this point in its start-up, in the order it always ran.
export function initCallList() {
  try { callsCollapsed = localStorage.getItem("callsPanel") === "collapsed"; } catch { /* storage unavailable */ }
  $("gridBtn").onclick = () => setView(view === "grid" ? "focus" : "grid");
  $("allMenuBtn").onclick = () => showMenu("all");

  narrowCalls.addEventListener("change", renderCalls);
  $("callsToggle").onclick = () => {
    callsCollapsed = !callsCollapsed;
    try { localStorage.setItem("callsPanel", callsCollapsed ? "collapsed" : "open"); } catch { /* private window: this visit only */ }
    renderCalls();
  };
}
