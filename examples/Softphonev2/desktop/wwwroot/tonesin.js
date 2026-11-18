// Keypad tones they send (DTMF), and the header's alerts for what's arrived on the call (keys and
// real-time text). An IVR's prompts don't send tones, but a person pressing keys at the other end,
// a test rig or a gateway does. The host reports each as it starts (`dtmf-in`: the digit and how
// it came, RFC 4733 in their audio or SIP INFO); there's no end or length (the library reports a
// tone once, at its start).
// New keys or text that you haven't looked at are flagged in two places, each a way in:
//  - The call header (Focus), at its right: an alert, "New keys · Open keypad" or "New text ·
//    Open text", the feature's icon with a red dot. The link opens its panel in the dock.
//  - The control bar: the Keypad or Text button gets the same red dot (and the ⋯ More menu, at
//    phone width).
// Opening the panel on that call clears both. The keys themselves are in the keypad, under its
// buttons: "Bob pressed 4 1 2 #", newest last and lit as it arrives; a pause of 2 s or more puts a
// gap between two, so "1 2 3 4" and "12 34" read differently; tones that came while the call was
// on hold (either way) are dimmed. A call not on the stage says "Pressed 4 1 2" in its status line
// in the calls panel (and on its grid tile) until you open its keypad. The Streams tab's timeline
// has every tone, each way, with how it came (the host's StreamLog, saved with the call).

import { entries, holdState, inGrid, nameOf, render, sel } from "./app.js";
import { keypadBlocked, keypadOpen, setKeypadOpen } from "./keypad.js";
import { openLine, setTextOpen, textOpen } from "./rtt.js";
import { $, ICON, escHtml as esc } from "./util.js";

const KEEP = 500;           // a call's tones kept for the keypad (the host's StreamLog keeps 500 too)
const SHOW = 12;            // in a call's status line
const GAP_MS = 2000;        // a pause this long between two tones shows as a gap

// A tone from them: kept on the call ({ d, at, held, via }), then everything redrawn. With the
// keypad open on that call it's read as it arrives.
export function onDtmfIn(id, digit, via) {
  const e = entries.get(id);
  if (!e || typeof digit !== "string" || digit.length !== 1) return;
  const tones = (e.tonesIn ??= []);
  tones.push({ d: digit, at: Date.now(), held: holdState(e) || !!e.h.heldByThem, via: via ?? "" });
  if (tones.length > KEEP) { tones.splice(0, tones.length - KEEP); e.tonesRead = Math.max(0, (e.tonesRead ?? 0) - 1); }
  render();
}

// The tones on a call you haven't seen in its keypad: none while it's open there.
const keypadHere = (e) => keypadOpen && e === sel() && !inGrid();
export function unreadTones(e) {
  const tones = e?.tonesIn ?? [];
  return keypadHere(e) ? [] : tones.slice(e.tonesRead ?? 0);
}

// Digits as text, with a space between and a wider one at a pause: "4 1 2 · #".
const spaced = (list) => list.map((t, i) => (i && t.at - list[i - 1].at >= GAP_MS ? " · " : i ? " " : "") + t.d).join("");

// For a call's status line (calllist.js): "Pressed 4 1 2" while it has keys you haven't seen, or "".
export function tonesStatus(e) {
  const list = unreadTones(e);
  return list.length ? `Pressed ${spaced(list.slice(-SHOW))}` : "";
}

// Under the keypad's buttons (keypad.js renderKeypad, while it's open): the keys they've pressed
// on this call, in a line, newest last. The ▾ by "Name pressed" drops down the full list (each
// tone's time, and how it came), and Clear empties it (the Streams tab keeps them all). Opening
// the keypad reads them. The list stays open or shut across calls until it's toggled again.
let listOpen = false;
const clock = (at) => new Date(at).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit" });
export function renderKeypadTones() {
  const box = $("keypadIn");
  const e = sel();
  const tones = e?.tonesIn ?? [];
  box.closest(".keypad-pop")?.classList.toggle("tones-open", listOpen && tones.length > 0);
  if (!e || !tones.length) { box.replaceChildren(); box.dataset.n = ""; return; }
  e.tonesRead = tones.length;
  const last = tones.at(-1);
  // Redrawn only when there's a new tone (or the list opens or shuts), so the newest lights up
  // once, not on every render.
  const key = `${e.id}:${tones.length}:${last.at}:${listOpen}`;
  if (box.dataset.n === key) return;
  const was = box.dataset.n ?? "";   // the same call, the list as it was: a new tone
  const grew = was.startsWith(`${e.id}:`) && !was.endsWith(`:${!listOpen}`);
  box.dataset.n = key;
  const fresh = grew && Date.now() - last.at < 1000;
  const gapAt = (i) => i > 0 && tones[i].at - tones[i - 1].at >= GAP_MS;
  const digits = tones.map((t, i) => {
    const cls = [gapAt(i) && "gap", t.held && "held", fresh && i === tones.length - 1 && "new"].filter(Boolean).join(" ");
    return `<b${cls ? ` class="${cls}"` : ""}${t.held ? ' title="While the call was on hold"' : ""}>${esc(t.d)}</b>`;
  }).join("");
  const rows = !listOpen ? "" : tones.map((t, i) =>
    `<li class="${[gapAt(i) && "gap", t.held && "held", fresh && i === tones.length - 1 && "new"].filter(Boolean).join(" ")}">`
    + `<span class="tl-at">${clock(t.at)}</span><b>${esc(t.d)}</b><span class="tl-via">${esc(t.via)}${t.held ? " · on hold" : ""}</span></li>`).join("");
  const held = tones.some((t) => t.held) ? " Dimmed: while the call was on hold." : "";
  box.innerHTML = `<div class="tones-row">`
    + `<button type="button" class="tones-toggle" aria-expanded="${listOpen}" aria-controls="keypadToneList" title="${listOpen ? "Hide" : "Show"} the full list">`
    + `${ICON.chevronDown}<span class="tones-label">${esc(nameOf(e))} pressed</span></button>`
    + `<span class="tones-digits" role="img" aria-label="${esc(tones.map((t) => t.d).join(" "))}"`
    + ` title="Newest last: ${esc(spaced(tones))} · the last at ${clock(last.at)}${last.via ? `, by ${esc(last.via)}` : ""}.${held}">${digits}</span>`
    + `<button type="button" class="tones-clear" title="Clear the keys they've pressed (the Streams tab keeps them)">Clear</button></div>`
    + `<ol class="tones-list" id="keypadToneList" aria-label="Keys ${esc(nameOf(e))} pressed, oldest first"${listOpen ? "" : " hidden"}>${rows}</ol>`;
  box.querySelector(".tones-toggle").onclick = () => { listOpen = !listOpen; render(); };
  box.querySelector(".tones-clear").onclick = () => { e.tonesIn = []; e.tonesRead = 0; render(); $("keypadBtn").focus(); };
  const line = box.querySelector(".tones-digits");
  line.scrollLeft = line.scrollWidth;
  // The newest at the end, once the dock has made room for the list (renderDock runs after this).
  const list = box.querySelector(".tones-list");
  setTimeout(() => { list.scrollTop = list.scrollHeight; line.scrollLeft = line.scrollWidth; });
}

// The header's alerts, for the call on the stage (render calls this). Each is drawn only when
// what it says changes, so a click on its link isn't lost to a redraw.
export function renderHeadAlerts() {
  const box = $("headAlerts");
  if (!box) return;
  const e = sel();
  const live = e?.h.call === "connected" && !inGrid();
  const items = [];
  if (live && unreadTones(e).length) {
    const blocked = keypadBlocked();
    items.push({ kind: "keys", icon: ICON.keypad, text: "New keys", link: "Open keypad", blocked,
      title: `${nameOf(e)} pressed ${spaced(unreadTones(e))} (DTMF): open the keypad to see them` });
  }
  if (live && e.text.unread && !textOpen) {
    const typing = !!openLine(e.text, false, false);
    items.push({ kind: "text", icon: ICON.text, text: typing ? "Typing…" : "New text", link: "Open text", blocked: null,
      title: `${nameOf(e)} ${typing ? "is typing" : "sent a message"} (real-time text): open the text to read it` });
  }
  const key = JSON.stringify(items.map(({ icon, ...rest }) => rest));
  box.hidden = !items.length;
  if (box.dataset.n === key) return;
  box.dataset.n = key;
  box.replaceChildren(...items.map((it) => {
    const el = Object.assign(document.createElement("div"), { className: "head-alert", title: it.title });
    el.dataset.kind = it.kind;
    el.innerHTML = `<span class="head-alert-icon" aria-hidden="true">${it.icon}<span class="btn-dot"></span></span>`
      + `<span class="head-alert-text">${esc(it.text)}</span>`;
    const link = Object.assign(document.createElement("button"), { type: "button", className: "head-link", textContent: it.link });
    if (it.blocked) { link.disabled = true; link.title = it.blocked; }
    link.onclick = () => openFor(it.kind);
    el.append(link);
    return el;
  }));
}

// The alert's link: the call's keypad or text, in the dock.
function openFor(kind) {
  const e = sel();
  if (!e) return;
  if (kind === "keys") setKeypadOpen(true);
  else setTextOpen(true);
}
