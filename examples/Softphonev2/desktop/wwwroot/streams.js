// Streams, a tab in Diagnostics (F12): a call's media lines as its offers and answers left them,
// against what's actually going each way. Three views of the same call:
//  - Now: a row per media line, with the direction each side put in its SDP (sendrecv, sendonly,
//    recvonly, inactive), then what's going out (the microphone, music, silence, the hold card,
//    nothing) and coming in, in packets a second. The point is the gap between the two, which it
//    flags: "Nothing for 14 s" on a line both sides agreed to send on.
//  - Timeline: the same over the whole call, a lane per line and direction, with what changed it
//    (a hold, a resume, video added, a transfer's new session, an RTP timeout) along the top.
//  - Offers/answers: each one in order, who made it and why, and both SDPs side by side, the lines
//    that changed since that side's last one highlighted.
// Only the SIP side: the relay between the host and this page is a loopback connection that's
// known to work. The host records the offers and answers as they went over the wire (StreamLog.cs,
// a `streams` message whenever they change) and counts the SIP side's RTP packets every second
// (`stream-rates`). Live calls only, like Call details and Call quality: as a call ends its streams
// are saved with its history entry (snapshots.js), and shown from there, from its row in Recent.
// The web softphone's host sends the same two messages (web/conndiag.js), from SIP.js and the
// browser's getStats(); there the page's own connection is the media leg, and nothing's saved.

import { entries, holdState, selectedId } from "./app.js";
import { renderConnection } from "./connection.js";
import { renderLadder } from "./sipladder.js";
import { snap } from "./snapshots.js";
import { $, escHtml as esc, post, shortName } from "./util.js";
import { SOURCE_LABEL, sourceOf } from "./video.js";

const ENDED_KEEP_MS = 5000;   // an ended call's record, kept till it's been saved (snapshots.js takes it sooner)
const GONE_MS = 3000;      // agreed to arrive, but nothing for this long: shown as missing
const KINDS = ["audio", "video", "text"];
// The timeline's lanes: [key, name, arrow]. Text is one lane, as it's mostly idle.
const LANES = [["audio-up", "Audio", "↑"], ["audio-down", "Audio", "↓"], ["video-up", "Video", "↑"], ["video-down", "Video", "↓"], ["text", "Text", ""]];
const STATE_NAME = { flow: "Flowing", hold: "On hold: music, silence, the hold card or nothing", off: "Off by agreement", gone: "Agreed, but nothing arriving", idle: "Idle" };

// Each call's (by id): { id, names (who it's been with, a transfer adds one), startedAt,
//   connectedAt, endedAt, reason, exchanges, streams and notes (the host's), rates (the latest
//   second's, per kind: { tx, rx, idle }), lanes ({ key: [{ s, e, st }] } runs of one state),
//   marks (what no offer/answer says: music on hold), held, heldByThem, holdMode }.
const recs = new Map();
const gone = new Set();    // calls ended and dropped: a late message mustn't bring one back
let view = "now";          // "now", "timeline" or "oa" (Offers/answers)
let pickedId = null;       // a call picked in the tab's own list; null follows the selected call
let followed = null;       // the selected call when last drawn, to follow it when it changes
let pickedN = null;        // the offer/answer open in Offers/answers; null for the latest
let bodyHtml = "", optionsHtml = "";

const sends = (d) => d === "sendrecv" || d === "sendonly";
const takes = (d) => d === "sendrecv" || d === "recvonly";
const lineOf = (r, kind) => r.streams.find((s) => s.kind === kind && s.ourPort && s.theirPort);
const clock = (t) => new Date(t).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit" });
// Time into the call: "1:05", "12:40", "1:02:03".
function since(r, t) {
  const s = Math.max(0, Math.round((t - r.startedAt) / 1000));
  const h = Math.floor(s / 3600), m = Math.floor((s % 3600) / 60), ss = String(s % 60).padStart(2, "0");
  return h ? `${h}:${String(m).padStart(2, "0")}:${ss}` : `${m}:${ss}`;
}
const secs = (ms) => (ms < 60000 ? `${Math.round(ms / 1000)} s` : `${Math.floor(ms / 60000)} min ${Math.round((ms % 60000) / 1000)} s`);

function rec(id) {
  let r = recs.get(id);
  if (!r) {
    r = { id, names: [], startedAt: Date.now(), connectedAt: 0, endedAt: 0, reason: null, exchanges: [], streams: [], notes: [],
      rates: {}, quietSince: {}, lanes: {}, marks: [], held: false, heldByThem: false, holdMode: null, sip: [], events: [], facts: {}, wire: [], tones: [] };
    recs.set(id, r);
  }
  return r;
}

// ---- From the host ----

// The call's offers and answers, the streams as the last answer left them, and notes (an RTP
// timeout, an RTCP BYE).
export function onStreams(msg) {
  if (gone.has(msg.id)) return;
  const r = rec(msg.id);
  r.exchanges = msg.exchanges ?? [];
  r.streams = msg.streams ?? [];
  r.notes = msg.notes ?? [];
  // For the Connection tab (connection.js): the SIP messages, the media layers' events, their facts.
  r.sip = msg.sip ?? [];
  r.events = msg.events ?? [];
  r.facts = msg.facts ?? {};
  // For the SIP ladder (sipladder.js): every SIP message of the call, whole.
  r.wire = msg.wire ?? [];
  // Keypad tones (DTMF) each way: { at, out, digit, via, held }, on the timeline's audio lanes.
  r.tones = msg.tones ?? [];
  if (r.exchanges[0] && r.exchanges[0].at < r.startedAt) r.startedAt = r.exchanges[0].at;
  scheduleRender();
}

// Once a second, every connected call's RTP packets each way on the SIP side, per kind.
export function onStreamRates(msg) {
  const now = Date.now();
  const byId = new Map();
  for (const x of msg.rates ?? []) {
    if (!byId.has(x.id)) byId.set(x.id, {});
    byId.get(x.id)[x.kind] = x;
  }
  for (const [id, rates] of byId) {
    if (gone.has(id)) continue;
    const r = rec(id);
    if (r.endedAt) continue;
    r.rates = rates;
    // When we last sent on each kind: a second or two with nothing (a call's first, say) isn't missing.
    for (const [kind, x] of Object.entries(rates)) r.quietSince[kind] = x.tx > 0 ? 0 : (r.quietSince[kind] || now - 1000);
    sample(r, now);
  }
  scheduleRender();
}

// The host's calls (each state message): who each is with, whether it's held, and when it ended.
export function noteStreamCalls(hostCalls) {
  if (!$("paneStreams") && !$("paneConn")) return;
  const now = Date.now();
  const listed = new Set();
  for (const h of hostCalls) {
    listed.add(h.id);
    if (gone.has(h.id)) continue;
    const r = recs.get(h.id) ?? (h.call === "connected" ? rec(h.id) : null);
    if (!r) continue;
    const name = shortName(h.remote);
    if (name && r.names.at(-1) !== name) r.names.push(name);
    r.holdMode = h.holdMode;
    r.heldByThem = !!h.heldByThem && h.call === "connected";
    if (h.call === "connected" && !r.connectedAt) r.connectedAt = now;
    const e = entries.get(h.id);
    const held = h.call === "connected" && (e ? holdState(e) : !!h.held);
    // Music on hold isn't signalled, so no offer/answer says when it started: marked here.
    if (held !== r.held && h.holdMode === "music" && r.connectedAt) r.marks.push({ at: now, label: held ? "Hold (music)" : "Resume", level: "" });
    r.held = held;
    if (h.call === "ended" && !r.endedAt) ended(r, h.reason, now);
  }
  for (const r of recs.values()) if (!r.endedAt && !listed.has(r.id)) ended(r, null, now);
  // Ended calls go once they've been saved; this is in case they never are.
  for (const r of [...recs.values()]) if (r.endedAt && now - r.endedAt > ENDED_KEEP_MS) dropStreams(r.id);
  scheduleRender();
}

// A call's record as it ended, for its saved diagnostics (snapshots.js), with the codecs each way
// that the page saw; null if it had no offers or answers.
export function streamsSnapshot(id, reason, codecs) {
  const r = recs.get(id);
  if (!r || (!r.exchanges.length && !r.streams.length)) return null;
  const copy = JSON.parse(JSON.stringify(r));
  if (!copy.endedAt) Object.assign(copy, { endedAt: Date.now(), reason: reason ?? "Call ended" });
  copy.held = copy.heldByThem = false;
  copy.codecs = codecs ?? null;
  return copy;
}

// Forgets an ended call: the tab shows live calls only.
export function dropStreams(id) {
  recs.delete(id);
  gone.add(id);
  if (gone.size > 100) gone.delete(gone.values().next().value);
  if (pickedId === id) pickedId = null;
  scheduleRender();
}

function ended(r, reason, at) {
  r.endedAt = at;
  r.reason = reason ?? "Call ended";
  r.held = r.heldByThem = false;
}

// ---- The timeline's lanes ----

// What each lane shows this second: flow, hold (on hold: music, silence, the hold card, or
// nothing by design), off (by agreement), gone (agreed, but nothing), idle (text), none (no line).
// How long we've sent nothing on the kind, in ms (0 while we're sending).
const quiet = (r, kind, now = Date.now()) => (r.quietSince[kind] ? now - r.quietSince[kind] : 0);

function laneStates(r, now) {
  const out = {};
  for (const kind of KINDS) {
    const s = lineOf(r, kind), x = r.rates[kind];
    if (!s || !x) {
      if (kind === "text") out.text = "none";
      else out[`${kind}-up`] = out[`${kind}-down`] = "none";
      continue;
    }
    const upOk = sends(s.ours) && takes(s.theirs), downOk = sends(s.theirs) && takes(s.ours);
    const up = x.tx > 0 ? ((r.held || r.heldByThem) && kind !== "text" ? "hold" : "flow")
      : !upOk ? "off" : kind === "text" ? "idle" : r.held ? "hold" : quiet(r, kind, now) >= GONE_MS ? "gone" : "flow";
    const down = x.rx > 0 ? "flow"
      : !downOk || r.heldByThem ? "off" : kind === "text" ? "idle" : x.idle >= GONE_MS ? "gone" : "flow";
    if (kind === "text") out.text = up === "flow" || down === "flow" ? "flow" : up === "off" && down === "off" ? "off" : "idle";
    else { out[`${kind}-up`] = up; out[`${kind}-down`] = down; }
  }
  return out;
}

function sample(r, t) {
  for (const [key, st] of Object.entries(laneStates(r, t))) {
    const lane = (r.lanes[key] ??= []);
    const last = lane.at(-1);
    const joined = last && t - last.e < 2500;
    if (joined && last.st === st) last.e = t;
    else lane.push({ s: joined ? last.e : t - 1000, e: t, st });
  }
}

// ---- Reading SDP (for what changed) ----

// Each m-line: its kind, port, transport and direction (a=sendrecv and the rest; the session's if
// it has none, sendrecv if neither).
function mLines(sdp) {
  const out = [];
  let sessionDir = null, cur = null;
  for (const raw of (sdp ?? "").split(/\r?\n/)) {
    const line = raw.trim();
    if (line.startsWith("m=")) {
      const [kind, port, transport] = line.slice(2).split(" ");
      out.push(cur = { kind, port: Number(port), transport, dir: null });
    } else if (/^a=(sendrecv|sendonly|recvonly|inactive)$/.test(line)) {
      if (cur) cur.dir = line.slice(2); else sessionDir = line.slice(2);
    }
  }
  for (const m of out) m.dir ??= sessionDir ?? "sendrecv";
  return out;
}

// One side's SDP in an exchange.
const sdpOf = (x, side) => (side === "ours" ? (x.byUs ? x.offer : x.answer) : (x.byUs ? x.answer : x.offer));

// The SDP's lines, each marked against the same side's previous SDP: unchanged, changed (with what
// it was), or new, and the lines that went. Lines are matched by what they set, so a=sendonly
// replaces a=sendrecv, a=rtpmap:96 another a=rtpmap:96, o= the o= line.
function lineKey(line, section) {
  if (/^a=(sendrecv|sendonly|recvonly|inactive)$/.test(line)) return `${section}:dir`;
  const m = /^([a-z])=(.*)$/.exec(line);
  if (!m) return `${section}:${line}`;
  const [, type, value] = m;
  if (type !== "a") return `${section}:${type}`;
  const a = /^([^:]+)(?::(\S+))?/.exec(value);
  if (!a) return `${section}:${line}`;
  const [, name, first] = a;
  if (["candidate", "rtcp-fb", "ssrc", "ssrc-group", "extmap-allow-mixed", "msid"].includes(name)) return `${section}:${line}`;
  return `${section}:${name}${first !== undefined ? `:${first}` : ""}`;
}
function keyed(sdp) {
  const out = [];
  let section = 0;
  for (const raw of (sdp ?? "").split(/\r?\n/)) {
    const line = raw.trimEnd();
    if (!line) continue;
    if (line.startsWith("m=")) section++;
    out.push({ line, key: lineKey(line, section), section });
  }
  return out;
}
function sdpHtml(sdp, prev) {
  if (!sdp) return "";
  const cur = keyed(sdp);
  if (!prev) return cur.map((l) => `<span class="${l.line.startsWith("m=") ? "k" : ""}">${esc(l.line)}</span>`).join("");
  const before = new Map(keyed(prev).map((l) => [l.key, l]));
  const seen = new Set();
  const html = [];
  const removedIn = (section) => [...before.values()].filter((l) => l.section === section && !cur.some((c) => c.key === l.key));
  let section = 0;
  const flushRemoved = () => { for (const l of removedIn(section)) html.push(`<span class="del">${esc(l.line)}<em>removed</em></span>`); };
  for (const l of cur) {
    if (l.section !== section) { flushRemoved(); section = l.section; }
    seen.add(l.key);
    const was = before.get(l.key);
    const cls = l.line.startsWith("m=") ? " k" : "";
    if (was && was.line === l.line) html.push(`<span class="s${cls}">${esc(l.line)}</span>`);
    else if (was) html.push(`<span class="c${cls}">${esc(l.line)}<em>was ${esc(wasText(was.line, l.line))}</em></span>`);
    else html.push(`<span class="c${cls}">${esc(l.line)}<em>new</em></span>`);
  }
  flushRemoved();
  return html.join("");
}
// What a changed line was: just the words that differ when it's short ("2", "sendrecv"), else all of it.
function wasText(was, now) {
  if (/^a=(sendrecv|sendonly|recvonly|inactive)$/.test(was)) return was.slice(2);
  const a = was.split(" "), b = now.split(" ");
  if (a.length === b.length) {
    const diff = a.filter((w, i) => w !== b[i]);
    if (diff.length && diff.length <= 2) return diff.join(" ");
  }
  return was;
}

// ---- Drawing ----

let renderQueued = false;
function scheduleRender() {
  if (renderQueued) return;
  renderQueued = true;
  requestAnimationFrame(() => { renderQueued = false; renderStreams(); renderConnection(); renderLadder(); });
}

// The calls the tab offers: those going (an ended one clears at once, as the other tabs do).
const live = () => [...recs.values()].filter((r) => !r.endedAt);

// The call the tab shows: a call's saved diagnostics, else the one picked in its list, else the
// selected call, else the latest.
function shown() {
  if (snap) return snap.streams ?? null;
  if (selectedId !== followed) { followed = selectedId; pickedId = null; pickedN = null; }
  const on = (id) => id != null && recs.has(id) && !recs.get(id).endedAt;
  if (on(pickedId)) return recs.get(pickedId);
  if (on(selectedId)) return recs.get(selectedId);
  return live().sort((a, b) => Number(b.id) - Number(a.id))[0] ?? null;
}
// The Connection tab's call: a call's saved diagnostics, else the selected call, else the latest
// that's going. (It has no picker: it follows the selected call, as Call details does.)
export function connRec(selected) {
  if (snap) return snap.streams ?? null;
  const r = recs.get(selected);
  if (r && !r.endedAt) return r;
  return live().sort((a, b) => Number(b.id) - Number(a.id))[0] ?? null;
}
let snapShown = null;      // the snapshot last drawn, to start it on its latest offer/answer

const nameOf = (r) => (r.names.length ? r.names.join(" → ") : `Call ${r.id}`);

export function renderStreams() {
  const pane = $("paneStreams");
  if (!pane || pane.hidden) return;
  if (snap !== snapShown) { snapShown = snap; pickedN = null; }
  const r = shown();
  $("streamsEmpty").hidden = !!r;
  $("streamsEmpty").textContent = snap ? "No streams were saved for this call: it had no offers or answers."
    : "Streams show here once there's a call: the media its offers and answers agreed, and what's going each way.";
  $("streamsView").hidden = !r;
  if (!r) { bodyHtml = ""; return; }
  // The list: the calls going, or the saved call alone.
  const all = snap ? [r] : live().sort((a, b) => Number(a.id) - Number(b.id));
  const opts = all.map((x) => `<option value="${esc(x.id)}">${esc(nameOf(x))}${x.endedAt ? ` · ended ${clock(x.endedAt)}` : ""}</option>`).join("");
  const pick = $("streamsCall");
  if (opts !== optionsHtml) { pick.innerHTML = opts; optionsHtml = opts; }
  pick.disabled = all.length < 2;
  if (pick.value !== r.id) pick.value = r.id;
  $("streamsMeta").textContent = metaText(r);
  for (const b of document.querySelectorAll("#streamsViews button")) b.setAttribute("aria-pressed", String(b.dataset.view === view));
  const html = view === "timeline" ? timelineHtml(r) : view === "oa" ? oaHtml(r) : nowHtml(r);
  if (html !== bodyHtml) { $("streamsBody").innerHTML = html; bodyHtml = html; }
}

function metaText(r) {
  if (r.endedAt) return `Ended ${clock(r.endedAt)}: ${r.reason}`;
  if (r.held) return r.holdMode === "music" ? "On hold: music on hold, nothing signalled (the SDP still says sendrecv)" : "On hold: we held it with a re-INVITE";
  if (r.heldByThem) return "They've put the call on hold";
  const n = r.exchanges.filter((x) => x.ok).length;
  return r.connectedAt ? `Connected · ${n} offer${n === 1 ? "/answer" : "s/answers"}` : "Setting up";
}

const KIND_NAME = { audio: "Audio", video: "Video", text: "Text", data: "Text (data channel)" };
const cell = (cls, what, more = "") => `<div class="sx-flow"><span class="st ${cls}">${esc(what)}</span>${more ? `<span class="m">${esc(more)}</span>` : ""}</div>`;

// "opus/48000" → "Opus 48 kHz"; "G722/8000" → "G722 16 kHz" (its RTP clock is 8 kHz, its audio 16).
function prettyFormat(f, kind) {
  if (!f) return null;
  const [name, rate] = f.split("/");
  const pretty = /^opus$/i.test(name) ? "Opus" : name.toUpperCase();
  if (kind !== "audio" || !rate) return pretty;
  const hz = pretty === "G722" ? 16000 : Number(rate);
  return `${pretty} ${hz % 1000 === 0 ? `${hz / 1000} kHz` : `${hz} Hz`}`;
}

function codecCell(r, s) {
  if (s.kind === "text") return s.formats.some((f) => /^red\//i.test(f)) ? "T.140 + RED" : "T.140";
  if (s.kind === "data") return "T.140 data channel";
  // Each way, from the page's stats while the call's up (the relay doesn't re-encode, so they're the
  // SIP side's), or as they were when it ended.
  const seen = (r.endedAt ? r.codecs : entries.get(r.id)?.codecs)?.[s.kind];
  if (seen?.tx || seen?.rx) return seen.tx === seen.rx || !seen.tx || !seen.rx ? esc(seen.tx ?? seen.rx) : `↑ ${esc(seen.tx)}<br>↓ ${esc(seen.rx)}`;
  const f = s.formats.find((x) => !/^(telephone-event|rtx|ulpfec|flexfec|red)\//i.test(x));
  return esc(prettyFormat(f, s.kind) ?? "–");
}

// What we're sending on the line: what the page puts on it, from the call's state.
function sending(r, kind) {
  const e = entries.get(r.id);
  if (kind === "audio") return r.held ? (r.holdMode === "music" ? "Music" : "Silence") : r.heldByThem ? "Silence" : e?.m?.muted ? "Muted" : "Microphone";
  if (kind === "video") return r.held ? (r.holdMode === "music" ? "Hold card" : "Nothing") : e?.m?.camOn ? (SOURCE_LABEL[sourceOf(e)] ?? "Camera") : "Camera-off card";
  return "Text";
}

// The line's network: the transport (UDP, or ICE's pair on WebRTC), then our socket ⇄ where we send,
// and RTCP's (or that it shares RTP's port). Where their RTP comes from, if that isn't where we send.
// An ended call keeps the last it had.
function netCell(r, s) {
  // A data channel (text on WebRTC) has no RTP of its own: it's on the bundled socket the media uses.
  const x = s.kind === "data" ? (r.rates.audio ?? r.rates.video) : r.rates[s.kind];
  if (!x?.transport) return `<td class="m">–</td>`;
  const pair = (a, b) => `${esc(a ?? "?")} ⇄ ${esc(b ?? "?")}`;
  const lines = [`<span>${pair(x.local, x.remote)}</span>`];
  if (x.from && x.remote && x.from !== x.remote) lines.push(`<span class="sx-from">↓ arrives from ${esc(x.from)}</span>`);
  lines.push(x.rtcpLocal ? `<span>RTCP ${pair(x.rtcpLocal, x.rtcpRemote)}</span>` : `<span>RTCP on the same port</span>`);
  return `<td class="sx-net">${esc(x.transport)}<small>${lines.join("")}</small></td>`;
}

function upCell(r, s) {
  const x = r.rates[s.kind];
  if (!x) return cell("na", "–");
  const upOk = sends(s.ours) && takes(s.theirs);
  if (x.tx > 0) {
    const past = !upOk && s.kind === "audio" ? " · past their hold, to keep the path open" : "";
    return cell(r.held || r.heldByThem ? "held" : "ok", sending(r, s.kind), `${x.tx} packets/s${past}`);
  }
  if (!upOk) return cell("na", "Nothing", "as agreed");
  if (s.kind === "text") return cell("na", "Idle");
  if (r.held) return cell("held", "Nothing", "on hold");
  const q = quiet(r, s.kind);
  if (q < GONE_MS) return cell("ok", sending(r, s.kind));
  return cell("bad", `Nothing for ${secs(q)}`, "we agreed to send");
}

function downCell(r, s) {
  const x = r.rates[s.kind];
  if (!x) return cell("na", "–");
  const downOk = sends(s.theirs) && takes(s.ours);
  if (x.rx > 0) return cell("ok", "Arriving", `${x.rx} packets/s${r.held && s.kind !== "text" ? ` · not ${s.kind === "video" ? "shown" : "played"} while held` : ""}`);
  if (!downOk) return cell("na", "Nothing", "as agreed");
  if (r.heldByThem) return cell("na", "Nothing", "they're holding the call");
  if (s.kind === "text") return cell("na", "Idle");
  if (x.idle < GONE_MS) return cell("ok", "Arriving");
  return cell("bad", `Nothing for ${secs(x.idle)}`, "they agreed to send");
}

// The last answered exchange's directions against the one before: which changed.
function changedDirs(r) {
  const ok = r.exchanges.filter((x) => x.ok);
  const [prev, last] = [ok.at(-2), ok.at(-1)];
  if (!prev || !last || prev.session !== last.session) return { ours: new Set(), theirs: new Set() };
  const diff = (side) => {
    const a = mLines(sdpOf(prev, side)), b = mLines(sdpOf(last, side));
    return new Set(b.map((m, i) => (a[i] && a[i].dir !== m.dir ? i : -1)).filter((i) => i >= 0));
  };
  return { ours: diff("ours"), theirs: diff("theirs") };
}

function nowHtml(r) {
  if (!r.streams.length) {
    const last = r.exchanges.at(-1);
    return `<p class="q-empty">${last && !last.ok && !last.pending ? `No media agreed: ${esc(last.label)} got ${esc(last.status ?? "no answer")}.` : "No media agreed yet."}</p>`;
  }
  const chg = changedDirs(r);
  const last = r.exchanges.filter((x) => x.ok).at(-1);
  const rows = r.streams.map((s) => {
    const name = `<td>${KIND_NAME[s.kind] ?? esc(s.kind)}<small>m-line ${s.index}</small></td>`;
    if (!s.ourPort || !s.theirPort) return `<tr class="sx-down">${name}<td colspan="7" class="m">Turned down: ${s.ourPort ? "they" : "we"} set its port to 0</td></tr>`;
    const security = s.security === "None" ? "not encrypted" : s.security;
    const dir = (d, changed) => `<span class="sx-dir${changed ? " chg" : ""}">${d}</span>`;
    const flow = r.endedAt ? `<td class="m">–</td><td class="m">–</td>` : `<td>${upCell(r, s)}</td><td>${downCell(r, s)}</td>`;
    return `<tr>${name}${netCell(r, s)}<td>${esc(s.transport)}<small>${esc(security)}</small></td><td>${codecCell(r, s)}</td>`
      + `<td>${dir(s.ours, chg.ours.has(s.index))}</td><td>${dir(s.theirs, chg.theirs.has(s.index))}</td>${flow}</tr>`;
  }).join("");
  const anyChg = chg.ours.size || chg.theirs.size;
  // Keypad tones (DTMF), each way: the digits, and how they went.
  const keys = (out) => {
    const list = (r.tones ?? []).filter((t) => !!t.out === out);
    if (!list.length) return "";
    const vias = [...new Set(list.map((t) => t.via).filter(Boolean))].join(", ");
    const digits = list.slice(-24).map((t) => t.digit).join("");
    return `${out ? "↑ sent" : "↓ received"} <span class="sx-keys">${list.length > 24 ? "…" : ""}${esc(digits)}</span>${vias ? ` (${esc(vias)})` : ""}`;
  };
  const tonesLine = [keys(false), keys(true)].filter(Boolean).join(" · ");
  const foot = [
    anyChg ? "Shaded directions changed in the last offer/answer." : "",
    last ? `From offer/answer ${last.n} of ${r.exchanges.length} (${esc(last.label)}).` : "",
    r.endedAt ? "Ended, so nothing's flowing: the Timeline has how it went."
      : document.documentElement.dataset.host === "web" ? "↑ and ↓ are this second's RTP packets on the call's connection." : "↑ and ↓ are this second's RTP packets on the SIP side.",
  ].filter(Boolean).join(" ");
  return `<div class="sx-wrap"><table class="sx-table"><thead><tr><th>Stream</th><th>Network</th><th>Profile</th><th>Codec</th><th>We said</th><th>They said</th><th>↑ Sending</th><th>↓ Receiving</th></tr></thead><tbody>${rows}</tbody></table></div>`
    + (tonesLine ? `<p class="sx-foot">Keypad tones: ${tonesLine}. <button type="button" class="sx-link" data-view="timeline">Timeline ›</button></p>` : "")
    + `<p class="sx-foot">${foot} <button type="button" class="sx-link" data-view="oa">Offers/answers ›</button></p>`;
}

// Keypad tones on an audio lane (↑ ours, ↓ theirs): a marker per run of tones close enough
// together that they'd overlap at this zoom, labelled with its digits; hover for each one's time
// and how it came. Only where they started: no length is reported.
function tonesHtml(r, out, x, track) {
  const list = (r.tones ?? []).filter((t) => !!t.out === out).sort((a, b) => a.at - b.at);
  const runs = [];
  for (const t of list) {
    const run = runs.at(-1);
    // About 8 px a digit: a tone joins the run if it'd land on the run's label.
    if (run && x(t.at) - run.p < ((run.tones.length + 2) * 8 * 100) / track) run.tones.push(t);
    else runs.push({ p: x(t.at), tones: [t] });
  }
  return runs.map((run) => {
    const digits = run.tones.map((t) => t.digit).join("");
    const label = digits.length > 10 ? `${digits.slice(0, 9)}…` : digits;
    const title = run.tones.map((t) => `${since(r, t.at)} ${out ? "↑" : "↓"} ${t.digit}${t.via ? ` by ${t.via}` : ""}${t.held ? " (on hold)" : ""}`).join("\n");
    const held = run.tones.every((t) => t.held) ? " held" : "";
    // Near the right edge it ends at its time instead, so it isn't cut off.
    const end = run.p + ((label.length + 2) * 8 * 100) / track > 100 ? " end" : "";
    return `<span class="sx-tone${held}${end}" style="left:${run.p}%" title="${esc(`Keypad tones ${out ? "sent" : "received"}:\n${title}`)}">${esc(label)}</span>`;
  }).join("");
}

function timelineHtml(r) {
  const t0 = r.startedAt, t1 = r.endedAt || Date.now();
  const span = Math.max(2000, t1 - t0);
  const x = (t) => Math.max(0, Math.min(100, ((t - t0) * 100) / span));
  const lanes = LANES.filter(([k]) => r.lanes[k]?.some((g) => g.st !== "none"));
  // What changed things, along the top: offers and answers after the first, notes, music on
  // hold, and the end. Labels alternate between two rows and give way when they'd overlap.
  const events = [];
  r.exchanges.forEach((e, i) => {
    if (i === 0) return;
    const failed = !e.ok && !e.pending;
    events.push({ at: e.at, label: failed ? `${e.label}: ${e.status ?? "no answer"}` : e.label, level: failed ? "bad" : /held by them/i.test(e.label) ? "warn" : "", n: e.n });
  });
  for (const n of r.notes) events.push({ at: n.at, label: n.text, level: n.level === "warn" ? "warn" : "" });
  for (const m of r.marks) events.push(m);
  if (r.endedAt) events.push({ at: r.endedAt, label: `Ended: ${r.reason}`, level: "" });
  events.sort((a, b) => a.at - b.at);
  for (let i = events.length - 1; i > 0; i--) {
    const [a, b] = [events[i - 1], events[i]];
    if (b.at - a.at > 1500) continue;
    const parts = [...(a.parts ?? [a.label]), ...(b.parts ?? [b.label])];
    events.splice(i - 1, 2, { at: a.at, label: parts.join(" · "), parts, level: b.level || a.level, n: b.n ?? a.n });
  }
  // Where each label would go, in % of the track (about 6 px a character), so they don't overlap:
  // centred on its time, or ending at it near the right, starting at it near the left.
  const track = Math.max(200, ($("streamsBody").clientWidth || 600) - 84);
  const rowEnd = [-Infinity, -Infinity, -Infinity];
  const evHtml = events.map((ev) => {
    const p = x(ev.at);
    const parts = ev.parts ?? [ev.label], max = parts.length > 1 ? 30 : 44;
    const text = parts.map((t) => (t.length > max ? `${t.slice(0, max - 1)}…` : t)).join(" · ");
    const time = since(r, ev.at);
    const w = ((text.length + time.length + 1) * 6 * 100) / track;
    const anchor = p < w / 2 ? "left" : p + w / 2 > 100 ? "right" : "";
    const from = anchor === "left" ? p : anchor === "right" ? p - w : p - w / 2;
    const row = rowEnd.findIndex((end) => from >= end + 1);
    const title = `${time} ${ev.label}`;
    const tag = ev.n ? `button type="button" data-n="${ev.n}"` : "span";
    const close = ev.n ? "button" : "span";
    if (row < 0) return `<${tag} class="sx-ev dot ${ev.level}" style="left:${p}%" title="${esc(title)}" aria-label="${esc(title)}"></${close}>`;
    rowEnd[row] = from + w;
    return `<${tag} class="sx-ev r${row + 1} ${ev.level} ${anchor}" style="left:${p}%" title="${esc(title)}"><b>${esc(text)}</b><span>${time}</span></${close}>`;
  }).join("");
  // A transfer's new session: a line through the lanes.
  const sessions = [];
  let session = 1;
  for (const e of r.exchanges) if (e.ok && e.session > session) { session = e.session; sessions.push({ at: e.at, n: session }); }
  const guides = events.map((ev) => `<div class="sx-guide" style="left:${x(ev.at)}%"></div>`).join("")
    + sessions.map((s) => `<div class="sx-guide session" style="left:${x(s.at)}%"><span>Session ${s.n}</span></div>`).join("");
  const laneHtml = lanes.map(([k, name, arrow]) => {
    const bars = r.lanes[k].filter((g) => g.st !== "none").map((g) => {
      const w = Math.max(0.4, x(g.e) - x(g.s));
      return `<div class="sx-bar ${g.st}" style="left:${x(g.s)}%;width:${w}%" title="${esc(`${name} ${arrow} ${since(r, g.s)}–${since(r, g.e)}: ${STATE_NAME[g.st]}`)}"></div>`;
    }).join("");
    const tones = k === "audio-up" ? tonesHtml(r, true, x, track) : k === "audio-down" ? tonesHtml(r, false, x, track) : "";
    return `<div class="sx-row"><b>${name}${arrow ? ` <small>${arrow}</small>` : ""}</b><div class="sx-track">${bars}${tones}</div></div>`;
  }).join("");
  // Ticks at a round step, about six of them.
  const steps = [1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600, 7200];
  const step = (steps.find((s) => (span / 1000) / s <= 6) ?? 7200) * 1000;
  const ticks = [];
  for (let t = 0; t <= span; t += step) ticks.push(`<span style="left:${(t * 100) / span}%">${since(r, t0 + t)}</span>`);
  const legend = ["flow", "hold", "off", "gone", "idle"].map((st) => `<span><i class="sx-bar ${st}"></i>${STATE_NAME[st]}</span>`).join("")
    + (r.tones?.length ? `<span><i class="sx-tone">5</i>Keypad tones (DTMF), where each started</span>` : "");
  return `<div class="sx-tl">
    <div class="sx-events">${evHtml}</div>
    <div class="sx-lanes"><div class="sx-guides">${guides}</div>${laneHtml || `<p class="q-empty sx-nolanes">The lanes start once the call's connected and media's counted.</p>`}</div>
    <div class="sx-axis">${ticks.join("")}</div>
    <div class="sx-legend">${legend}</div>
  </div>`;
}

function oaHtml(r) {
  if (!r.exchanges.length) return `<p class="q-empty">No offers or answers yet.</p>`;
  const shownX = r.exchanges.find((x) => x.n === pickedN) ?? r.exchanges.at(-1);
  let session = 1;
  const list = r.exchanges.map((x) => {
    const failed = !x.ok && !x.pending;
    const newSession = x.ok && x.session > session;
    if (newSession) session = x.session;
    const sub = `${x.byUs ? "↑" : "↓"} ${esc(x.offerMsg)}${x.answerMsg ? ` · ${x.byUs ? "↓" : "↑"} ${esc(x.answerMsg)}` : ""}`;
    const status = x.pending ? `<span class="m">${esc(x.status ?? "waiting")}…</span>` : failed ? `<span class="bad">${esc(x.status ?? "no answer")}</span>` : "";
    return `<button type="button" class="sx-oa-item${x === shownX ? " on" : ""}${newSession ? " sess" : ""}" data-n="${x.n}">`
      + `<span class="n">${x.n}</span><span class="tm">${since(r, x.at)}</span><b>${esc(x.label)}</b><span class="sub">${sub} ${status}</span></button>`;
  }).join("");
  const idx = r.exchanges.indexOf(shownX);
  // The same side's SDP before, in the same session: after a transfer it's someone else's.
  const sameSession = (x) => shownX.session == null || x.session == null || x.session === shownX.session;
  const prevSdp = (side) => {
    for (let i = idx - 1; i >= 0; i--) { const x = r.exchanges[i]; const s = sameSession(x) && sdpOf(x, side); if (s) return s; }
    return null;
  };
  const offerSide = shownX.byUs ? "ours" : "theirs", answerSide = shownX.byUs ? "theirs" : "ours";
  const who = (side) => (side === "ours" ? "ours" : "theirs");
  const pane = (title, side, sdp, msgName, emptyText, copyId) => `<div class="sx-sdp"><div class="sx-sdp-h"><b>${title}</b> · ${who(side)}${msgName ? `, in the ${esc(msgName)}` : ""}`
    + `${sdp ? `<button type="button" class="sx-copy" data-copy="${copyId}">Copy</button>` : ""}</div>`
    + (sdp ? `<pre>${sdpHtml(sdp, prevSdp(side))}</pre>` : `<p class="m sx-none">${esc(emptyText)}</p>`) + `</div>`;
  const failed = !shownX.ok && !shownX.pending;
  const answerEmpty = shownX.pending ? "No answer yet." : failed ? `Turned down: ${shownX.status ?? "no answer"}.` : "No SDP in the answer.";
  const head = `${shownX.byUs ? "Our" : "Their"} ${esc(shownX.offerMsg)} at ${since(r, shownX.at)} (${clock(shownX.at)})`
    + (shownX.answerMs != null ? `, answered in ${shownX.answerMs} ms` : "") + (shownX.status && !failed ? ` · ${esc(shownX.status)}` : "");
  const hasPrev = !!(prevSdp("ours") || prevSdp("theirs"));
  return `<div class="sx-oa">
    <div class="sx-oa-list">${list}</div>
    <div class="sx-oa-view">
      <div class="sx-oa-head"><b>${shownX.n} · ${esc(shownX.label)}</b><span class="m">${head}</span></div>
      ${hasPrev ? `<p class="sx-oa-note m">Highlighted: what changed since that side's last SDP.</p>` : ""}
      <div class="sx-sdp2">
        ${pane("Offer", offerSide, shownX.offer, shownX.offerMsg, "No SDP in the offer.", "offer")}
        ${pane("Answer", answerSide, shownX.answer, shownX.answerMsg, answerEmpty, "answer")}
      </div>
    </div>
  </div>`;
}

// Run by app.js at start-up.
export function initStreams() {
  const pane = $("paneStreams");
  if (!pane) return;
  pane.addEventListener("click", (ev) => {
    const v = ev.target.closest("[data-view]");
    if (v) { view = v.dataset.view; renderStreams(); return; }
    const n = ev.target.closest("[data-n]");
    if (n) { pickedN = Number(n.dataset.n); view = "oa"; renderStreams(); return; }
    const copy = ev.target.closest("[data-copy]");
    if (copy) {
      const r = shown();
      const x = r?.exchanges.find((e) => e.n === pickedN) ?? r?.exchanges.at(-1);
      const text = copy.dataset.copy === "offer" ? x?.offer : x?.answer;
      if (text) {
        post({ type: "copy", text });
        copy.textContent = "Copied";
        setTimeout(() => { copy.textContent = "Copy"; }, 900);
      }
    }
  });
  $("streamsCall").addEventListener("change", (ev) => { pickedId = ev.target.value; pickedN = null; renderStreams(); });
}
