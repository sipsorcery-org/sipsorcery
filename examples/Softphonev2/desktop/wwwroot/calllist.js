// The Active calls panel down the left, the grid of calls, and End all.

import { ROW_MS, audioId, callNotes, dropOutcome, focusCall, goneRows, gridCalls, hasMic, holdState, inGrid, isLive, listed, liveEntries, nameOf, selectedId, setView, tiles, view } from "./app.js";
import { setAvatar } from "./avatar.js";
import { contactMarkEl } from "./contactmark.js";
import { contactFor } from "./contacts.js";
import { closeMenu, openMenu, showMenu } from "./menus.js";
import { openLine } from "./rtt.js";
import { tonesStatus } from "./tonesin.js";
import { transferNotices, transferShort } from "./transfer.js";
import { $, ICON, escHtml, initial, log, post } from "./util.js";
import { sourceAvailable, sourceOf } from "./video.js";

// ---- Active calls panel, down the left ----
// Every call, oldest at the top and new ones at the bottom, so a call never moves. Each row shows
// who, a badge on the avatar for what matters most about the call (callBadge), and its state:
// calling, ringing, connected, on hold with how long (or "Holding…" and "Resuming…" while the
// re-INVITE is out), or how it ended. Clicking a row puts that call on the stage, as do the
// number keys. It starts as a rail of avatars; a click on the panel (the chevron, its head, the
// space under the calls) opens it into the list, and again folds it. Under 700 px it's always the
// rail, with no chevron.

// Narrow windows (under 700 px): the panel is a rail of avatars, to leave the stage its room.
const narrowCalls = matchMedia("(max-width: 699px)");
// Open or folded to the rail: yours to choose, and it stays as you left it while
// calls come and go (a new call never opens or folds it). It starts as the rail when the first
// call comes, and goes back to that once there are none.
let callsOpen = false;
function foldCalls() {
  callsOpen = !callsOpen;
  renderCalls();
}
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
    // (Its note stays while you're back on the call, until × or Resume: "on hold" only if it is.)
    else if (xt?.status === "failed" && xt.role === "out") c.statusText = c.held ? "Transfer failed · on hold" : "Transfer failed";
    else if (pending) c.statusText = e.holdWanted ? "Holding…" : "Resuming…";
    else if (c.held) {
      c.long = Date.now() - e.heldAt > 60000;
      c.statusText = `On hold · ${fmtElapsed(Date.now() - e.heldAt)}`;
    } else if (h.heldByThem) c.statusText = `${nameOf(e)} put you on hold`;
    else if (c.typing) c.statusText = "Typing…";
    else if (c.unread) c.statusText = e.text.lines.findLast((l) => !l.me)?.text || "New message";
    else if (tonesStatus(e)) c.statusText = tonesStatus(e);   // keys they pressed (DTMF) you haven't seen: "Pressed 4 1 2"
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

// The panel shows whenever there's a call (or the grid's on screen), so it's always where a
// call's lamp is: the rail (each call's avatar and its badge) until you open it, and as you left
// it while calls come and go.
export function renderCalls() {
  // (A call that's ended on you shows as its gone row, not its own, while the host still lists it.)
  const gone = goneRows(), goneIds = new Set(gone.map((o) => String(o.row.id)));
  const calls = listed().filter((e) => !goneIds.has(String(e.id)));
  const panel = $("calls");
  const single = calls.length + gone.length < 2 && !inGrid();
  panel.hidden = calls.length + gone.length < 1 && !inGrid();
  if (panel.hidden) callsOpen = false;
  const rail = narrowCalls.matches || !callsOpen;
  panel.classList.toggle("collapsed", rail);
  panel.classList.toggle("single", single);
  const toggle = $("callsToggle");
  toggle.hidden = narrowCalls.matches;
  const label = rail ? "Show active calls" : "Fold active calls";
  toggle.setAttribute("aria-expanded", String(!rail));
  toggle.setAttribute("aria-label", label);
  toggle.title = label;
  const icon = rail ? CALLS_ICON.expand : CALLS_ICON.collapse;
  if (toggle.innerHTML !== icon) toggle.innerHTML = icon;
  $("callCount").textContent = calls.filter(isLive).length || "";
  // How a transfer on a call in the background went: on its row (noteRow).
  const xn = transferNotices(() => () => {});
  const notes = new Map([...callNotes(), ...xn.outcomes.filter((o) => o.row)].map((o) => [String(o.row), o]));
  // A transfer you're asked to take, on a call in the background: its lamp (askRow).
  const asks = new Map(xn.decisions.filter((d) => d.row).map((d) => [String(d.row), d]));
  for (const k of [...noteSeen.keys()]) if (![...notes.values()].some((o) => o.key === k)) noteSeen.delete(k);
  // A call that's just ended on you keeps its place a few seconds (goneRow), among the rest.
  const rows = [...calls.map((e, i) => ({ id: Number(e.id), el: asks.has(String(e.id)) ? askRow(callRow(e, i, rail), asks.get(String(e.id))) : noteRow(callRow(e, i, rail), notes.get(String(e.id))) })), ...gone.map((o) => ({ id: Number(o.row.id), el: goneRow(o) }))];
  $("callList").replaceChildren(...rows.sort((a, b) => a.id - b.id).map((r) => r.el));
  // Folded to the rail, the chevron to open it pulses too: there's more to read there.
  toggle.classList.toggle("attn", rail && !narrowCalls.matches && (gone.length > 0 || notes.size > 0 || asks.size > 0 || calls.some((e) => e.h.call === "ringing")));
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
  li.className = `call-row ${c.status}${c.muted ? " muted" : ""}${c.held ? " held" : ""}${c.long ? " long" : ""}${c.audioOff ? " audio-off" : ""}${c.unread ? " unread" : ""}`;
  li.dataset.call = e.id;
  if (e.id === selectedId) li.setAttribute("aria-current", "true");   // the call on the stage
  li.title = [e.h.remote || name, c.statusText, i < 8 ? `(${i + 1})` : ""].filter(Boolean).join(" · ");
  // A click picks the call; on the rail you folded it to, it opens the panel too, to
  // read what the call's doing.
  li.onclick = (ev) => {
    ev.stopPropagation();
    const unfold = collapsed && !narrowCalls.matches;
    focusCall(e.id);
    if (unfold) foldCalls();
  };
  const el = (tag, cls, text) => Object.assign(document.createElement(tag), { className: cls, textContent: text ?? "" });
  const avatar = el("span", "call-avatar", initial(name));
  avatar.setAttribute("aria-hidden", "true");
  setAvatar(avatar, e.h.remote);
  const top = el("span", "call-top");
  top.append(el("span", "call-name", name));
  const contact = contactFor(e.h.remote);
  if (contact) top.firstChild.append(contactMarkEl(contact.name));
  // No mic and camera chips, and no timer: the call bar has the time, and only one
  // call ever has your mic. What matters about the call is a badge on its avatar (callBadge).
  const main = el("span", "call-main");
  const status = el("span", "call-status", c.statusText);
  if (c.held && c.statusText.startsWith("On hold")) status.dataset.heldAt = e.heldAt;   // ticks (renderCallTimes)
  main.append(top, status);
  li.append(callFace(avatar, callBadge(e, c)), main);
  return li;
}

// A call's state, as a badge on its avatar's corner: the one that matters most. The
// avatar stays, so you still see who it is (two calls on hold aren't two pause signs). In order:
// ringing; then (askRow, noteRow) a transfer you're asked to take, then something that went wrong
// or just happened; then on hold (amber after a minute); muted; your mic on a call that isn't on
// the stage (it hears you while you look at another); a message you haven't read.
function callBadge(e, c) {
  if (c.status === "ringing") return { kind: "ring", icon: ICON.phone, label: "Ringing" };
  if (!c.sends) return null;
  if (c.held || e.h.heldByThem) return { kind: `held${c.long ? " long" : ""}`, icon: ICON.pause, label: c.held ? "On hold" : "They've put you on hold" };
  if (c.muted) return { kind: "muted", icon: ICON.micOff, label: "Muted" };
  if (c.sends.mic.on && (e.id !== selectedId || inGrid())) return { kind: "live", icon: ICON.mic, label: c.sends.mic.label };
  if (c.unread) return { kind: "unread", icon: ICON.text, label: "New message" };
  return null;
}
function callFace(avatar, b) {
  const f = Object.assign(document.createElement("span"), { className: "call-face" });
  f.append(avatar);
  setBadge(f, b);
  return f;
}
function setBadge(f, b) {
  f.querySelector(".call-badge")?.remove();
  f.classList.toggle("badged", !!b);
  if (!b) return;
  const s = Object.assign(document.createElement("span"), { className: `call-badge ${b.kind}`, title: b.label ?? "" });
  if (b.icon) s.innerHTML = b.icon; else s.textContent = b.text;
  f.append(s);
}

// A call that's just ended on you (they hung up, or it dropped), in the background or in Grid:
// its row stays where it was, amber (red if it failed), saying so and how long it lasted, for
// ROW_MS; then it fades and folds away. Its one animation runs the whole time, so a rebuilt row
// carries on from where it was (a negative delay: the time it's been there by the outcome's
// clock, which stops while the pointer's on it, as does the animation). A click closes it.
function goneRow(o) {
  const li = document.createElement("li");
  li.className = "call-row gone";
  li.dataset.tone = o.tone ?? "";
  li.title = [o.whoTitle, o.what, "click to close"].filter(Boolean).join(" · ");
  li.onclick = (ev) => { ev.stopPropagation(); dropOutcome(o.key); };
  const el = (tag, cls, text) => Object.assign(document.createElement(tag), { className: cls, textContent: text ?? "" });
  const avatar = el("span", "call-avatar", initial(o.who));
  avatar.setAttribute("aria-hidden", "true");
  setAvatar(avatar, o.row.remote);
  const main = el("span", "call-main");
  main.append(el("span", "call-top"), el("span", "call-status", o.what));
  main.firstChild.append(el("span", "call-name", o.who));
  // A hang-up mark on its avatar (its badge).
  li.append(callFace(avatar, { kind: `gone${o.tone === "bad" ? " bad" : ""}`, icon: ICON.hangup, label: o.what }), main);
  const been = Math.max(0, ROW_MS - ((o.until ?? 0) - Date.now()));
  li.style.animationDelay = `-${been}ms, -${been}ms`;
  return li;
}

// A transfer on a call in the background that's over (done, failed, cancelled, declined): its row
// says so, amber (red if it failed) with two soft pulses when it comes (carried on across
// rebuilds, by when it was first seen), and × to close it (the host clears all but a failed one
// by itself after a few seconds). The row still picks the call.
const noteSeen = new Map();   // outcome key -> when its row first said so
function noteRow(li, o) {
  if (!o) return li;
  if (!noteSeen.has(o.key)) noteSeen.set(o.key, Date.now());
  li.classList.add("noted");
  li.dataset.tone = o.tone ?? "";
  // Something that went wrong: the row just says what ("Transfer to Dave failed"), and the "!" on
  // its avatar says there's more on the call itself. Hover the row for why, in full, without
  // switching to it.
  const bad = o.tone === "bad";
  const full = o.title || o.what;   // the host's sentence, when it has one, else the short one
  li.title = [full, "click to open this call"].filter(Boolean).join(" · ");
  const st = li.querySelector(".call-status");
  st.textContent = bad ? o.what.replace(/:.*$/s, "") : o.what;
  delete st.dataset.heldAt;
  setBadge(li.querySelector(".call-face"), bad ? { kind: "bad", text: "!", label: full } : { kind: "note", icon: o.icon ?? ICON.transfer, label: o.what });
  const x = Object.assign(document.createElement("button"), { type: "button", className: "call-note-x", title: "Close", innerHTML: ICON.close });
  x.setAttribute("aria-label", "Close");
  x.onclick = (ev) => { ev.stopPropagation(); o.dismiss(); };
  li.append(x);
  li.style.animationDelay = `-${Date.now() - noteSeen.get(o.key)}ms`;
  return li;
}

// A call in the background whose caller wants to transfer you: its row blinks, like a
// phone's line lamp, until it's answered (or the host lets it go after 20 s). You switch to it to
// answer (a click, or its number key): its call bar asks.
function askRow(li, d) {
  li.classList.add("asking");
  const st = li.querySelector(".call-status");
  st.textContent = `Transfer request · to ${d.what.replace(/^wants to transfer you to /, "")}`;
  delete st.dataset.heldAt;
  setBadge(li.querySelector(".call-face"), { kind: "ask", icon: ICON.transfer, label: "Transfer request" });
  li.title = [`${d.who} ${d.what}`, "click to switch to this call and answer"].join(" · ");
  return li;
}

// Every second while connected: just the timers (a row's time on hold; the rows have no call timer).
export function renderCallTimes() {
  for (const s of document.querySelectorAll("#callList .call-status[data-held-at]")) s.textContent = `On hold · ${fmtElapsed(Date.now() - Number(s.dataset.heldAt))}`;
}

// Run by app.js at this point in its start-up, in the order it always ran.
export function initCallList() {
  $("gridBtn").onclick = () => setView(view === "grid" ? "focus" : "grid");
  $("allMenuBtn").onclick = () => showMenu("all");

  narrowCalls.addEventListener("change", renderCalls);
  // A click anywhere on the panel folds or unfolds it: the chevron, its head, the
  // space under the calls. Not a call (its row picks it, and on the rail opens the panel too: see
  // callRow), nor what closes a notice on a row, nor Grid and ⋯ under the list. One handler for
  // the chevron too, so a click on it is one toggle, not two. Not under 700 px: always the rail.
  $("calls").addEventListener("click", (ev) => {
    if (narrowCalls.matches) return;
    const t = ev.target;
    if (t.closest(".calls-foot, .call-row") || (t.closest("button, a, input") && !t.closest("#callsToggle"))) return;
    foldCalls();
  });
}
