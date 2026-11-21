// The debug log, Diagnostics' first tab: every line the host and the page log, each with a
// coloured tag for what it's about, and a chip per tag to show or hide its lines (with how many
// there are). The tags:
//  - SIP: messages on the wire (one line each: their whole text with "Full SIP messages" on),
//    registration, and the connection to a WebSocket server.
//  - ICE, DTLS: a WebRTC call's media connection on its SIP leg: candidates, checks and the
//    pair they settle on, then the handshake that sets the SRTP keys.
//  - RTP / RTCP: the media itself: codecs, the first packets each way, RTCP reports, timeouts,
//    SRTP errors, real-time text, simulated loss.
//  - Calls: calls starting and ending, hold, transfers, DTMF, the history.
//  - App: the rest: devices, the page's view, accounts, and the relay between the host and this
//    page (a loopback connection that's known to work).
// A line is tagged by what it says (tagOf), so the host and the page, and the web softphone's
// host, log as they always have. Lines saying something failed, and the library's warnings, are
// marked. The chips you leave off and Full SIP messages are remembered (localStorage).

import { $, post } from "./util.js";

export const TAGS = [
  ["sip", "SIP", "SIP messages, registration"],
  ["ice", "ICE", "WebRTC: candidates, connectivity checks and the pair chosen"],
  ["dtls", "DTLS", "WebRTC: the handshake that sets the media's keys"],
  ["rtp", "RTP / RTCP", "The media: codecs, packets, RTCP reports, timeouts, SRTP"],
  ["call", "Calls", "Calls starting and ending, hold, transfers, DTMF, history"],
  ["app", "App", "Devices, the page, accounts, and the relay to the page"],
];
const NAME = Object.fromEntries(TAGS.map(([k, n]) => [k, n === "RTP / RTCP" ? "RTP" : n]));
const MAX = 5000, TRIM = 1000;    // lines kept; the oldest go a thousand at a time

const entries = [];               // { t, tag, text, warn, full } (full: a whole SIP message)
const counts = Object.fromEntries(TAGS.map(([k]) => [k, 0]));
let off = new Set(), full = false;
try {
  const saved = JSON.parse(localStorage.getItem("logFilter") ?? "null");
  if (saved) { off = new Set(saved.off ?? []); full = !!saved.full; }
} catch { /* storage unavailable */ }
const save = () => { try { localStorage.setItem("logFilter", JSON.stringify({ off: [...off], full })); } catch { /* storage unavailable */ } };

const stamp = (d = new Date()) => `${d.toTimeString().slice(0, 8)}.${String(d.getMilliseconds()).padStart(3, "0")}`;

// What a line's about, from what it says (after its time).
export function tagOf(text) {
  if (/^(->|<-) /.test(text)) return "sip";
  const lib = /^sipsorcery \w+: (\w+): ([\s\S]*)$/.exec(text);
  if (lib) {
    const [, cat, msg] = lib;
    if (/dtls/i.test(msg) || /dtls/i.test(cat)) return "dtls";
    if (/ice|stun|turn/i.test(cat) || /\b(ICE|STUN|TURN)\b/.test(msg)) return "ice";
    if (/rtp|rtcp|srtp|media|stream|audio|video/i.test(cat) || /\b(S?RTP|RTCP)\b/.test(msg)) return "rtp";
    if (/^sip|useragent|transport/i.test(cat)) return "sip";
    return "app";
  }
  const after = text.replace(/^call \d+:? /, "");
  if (/^ICE\b/.test(after)) return "ice";
  if (/^DTLS\b/.test(after)) return "dtls";
  if (/^(S?RTP|RTCP)\b/.test(after)) return "rtp";
  if (/^SIP (start|listening)|^browser phones:|: (registration|registering)\b|^(registration|registering|signing out)\b|^(connected to|disconnected from) wss?:/i.test(text)) return "sip";
  // The relay to the page, and the page's side of it: App, not the call's media.
  if (/^(page media|relay:|stats \(call)|^call \d+ media( |:)/.test(text)) return "app";
  if (/^call connected:|real-time text|T\.140|^simulated loss|RTP timeout|\bRTCP\b|^media connection|WebRTC media|^(adding|added) (video|text)|^video added|didn't take (video|text)|turned down text|is adding video|^they (can't take|didn't take)/.test(after)) return "rtp";
  if (/^call \d+\b|^history:|^(holding|resuming|hold|resume)\b|put the call on hold|took the call off hold|^calling with|^not calling|^turned down a call|INVITE with Replaces/.test(text)) return "call";
  return "app";
}

// A line from the host (with its time) or the page (without). A whole SIP message comes as
// "-> udp:host:port\n<the message>", after its one-line summary.
export function addLog(text, isFull = false) {
  const m = /^(\d\d:\d\d:\d\d\.\d{3}) ([\s\S]*)$/.exec(text);
  const t = m ? m[1] : stamp();
  let body = m ? m[2] : text;
  let e;
  if (isFull) {
    body = body.slice(body.indexOf("\n") + 1).replace(/\r/g, "").trimEnd();
    e = { t, tag: "sip", text: body, full: true };
  } else {
    const tag = tagOf(body);
    e = { t, tag, text: body, warn: /^sipsorcery (warning|error|critical)|\bfail(ed|s|ure)?\b|\berror\b|timeout/i.test(body) };
    counts[tag]++;
    renderChip(tag);
  }
  entries.push(e);
  const el = $("log");
  if (!el) return;
  if (entries.length > MAX) {
    const gone = entries.splice(0, TRIM);
    for (const x of gone) if (!x.full) counts[x.tag]--;
    renderAll();
    return;
  }
  if (!shows(e)) return;
  const atEnd = el.scrollHeight - el.scrollTop - el.clientHeight < 24;
  el.append(lineEl(e));
  if (atEnd || $("paneLog").hidden) el.scrollTop = el.scrollHeight;
}

const shows = (e) => !off.has(e.tag) && (!e.full || full);

function lineEl(e) {
  const line = document.createElement("span");
  if (e.full) {
    line.className = "lg-full";
    line.textContent = e.text + "\n";
    return line;
  }
  line.className = `lg-l${e.warn ? " warn" : ""}`;
  const t = Object.assign(document.createElement("span"), { className: "lg-t", textContent: e.t });
  const g = Object.assign(document.createElement("span"), { className: `lg-g g-${e.tag}`, textContent: NAME[e.tag] });
  line.append(t, " ", g, e.text + "\n");
  return line;
}

function renderAll() {
  const el = $("log");
  el.replaceChildren(...entries.filter(shows).map(lineEl));
  if (!entries.some(shows)) el.append(Object.assign(document.createElement("span"), { className: "lg-none", textContent: entries.length ? "No lines with these tags." : "" }));
  el.scrollTop = el.scrollHeight;
  for (const [k] of TAGS) renderChip(k);
  renderFull();
}

function renderChip(tag) {
  const b = $("logChips")?.querySelector(`[data-tag="${tag}"]`);
  if (!b) return;
  b.setAttribute("aria-pressed", String(!off.has(tag)));
  b.querySelector("em").textContent = counts[tag];
}
function renderFull() {
  const b = $("logFull");
  b.setAttribute("aria-pressed", String(full));
  b.disabled = off.has("sip");
  $("logAll").hidden = !off.size;
}

// What Copy log copies: the lines shown, as shown.
export function logText() {
  return entries.filter(shows).map((e) => (e.full ? e.text.replace(/^/gm, "    ") : `${e.t} ${NAME[e.tag].padEnd(5)} ${e.text}`)).join("\n");
}
export function clearLog() {
  entries.length = 0;
  for (const k in counts) counts[k] = 0;
  renderAll();
}

// Run by diagnostics.js at start-up: the chips, and any lines logged before.
export function initLogView() {
  const box = $("logChips");
  box.innerHTML = TAGS.map(([k, name, about]) =>
    `<button type="button" class="lg-chip g-${k}" data-tag="${k}" aria-pressed="true" title="${about}. Click to show or hide them."><i></i>${name} <em>0</em></button>`).join("")
    + `<button type="button" class="lg-chip lg-full-btn" id="logFull" aria-pressed="false" title="Each SIP message's whole text, under its line">Full SIP messages</button>`
    + `<button type="button" class="lg-all" id="logAll" hidden>Show all</button>`;
  box.addEventListener("click", (ev) => {
    const b = ev.target.closest("button");
    if (!b) return;
    if (b.id === "logFull") full = !full;
    else if (b.id === "logAll") off.clear();
    else if (off.has(b.dataset.tag)) off.delete(b.dataset.tag);
    else off.add(b.dataset.tag);
    save();
    renderAll();
  });
  renderAll();
}

// Copy log and Clear.
export function copyLog() { post({ type: "copy", text: logText() }); }
