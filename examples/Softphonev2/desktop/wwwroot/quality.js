// Call quality and Call details, and the dock beside the call that shows them.

import { call, entries, inGrid, pc, sel, selectedId, state, tick } from "./app.js";
import { contactMark } from "./contactmark.js";
import { contactFor } from "./contacts.js";
import { keypadOpen } from "./keypad.js";
import { textOpen } from "./rtt.js";
import { $, ICON, log } from "./util.js";
let detailsHtml = "";      // what Call details last drew

// ---- Call quality (diagnostics) ----
// "Call quality", a tab in Diagnostics (F12): ↑ Sent is how the far end receives
// what we send, from their RTCP receiver reports (every 5 s or so, from any SIP phone that sends
// RTCP); ↓ Received is how we receive them, measured here. Received audio comes from the page's
// WebRTC stats: they're for the hop from the relay, but audio is relayed packet by packet with
// its sequence numbers, so its loss and jitter are the SIP leg's. Received video is packetised
// again for the page, so its loss comes from our own RTCP report instead, and its frame rate
// from the page. A graph below shows the picked column over the last two minutes.
const Q_POINTS = 120;   // graph length: 2 min of 1 s samples
const NO_RTCP_MS = 15000;
// Each call's (entry.quality): { sent: { audio, video } their latest reports, recvVideoRtcp: our
//   latest report on their video, recv: { audio, video } latest 1 s measures, history: per-second
//   points for the graph, prev, startedAt }. The panel shows the selected call's.

// ---- Call details: the chevron on the info bar drops down the facts about the call ----
// The host sends how the call was set up (details); the codecs each way come from the page's
// stats, which are the SIP leg's, since the relay passes the media on without re-encoding.
// Each call's are entry.details and entry.codecs ({ audio: { tx, rx }, video: { tx, rx } }).

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

// Call details, a tab in Diagnostics (F12): drawn while it shows, for the selected call.
export function renderDetails() {
  if ($("paneDetails").hidden) return;
  const el = $("details");
  const d = sel()?.details;
  const on = !!(d && call && sel()?.h.call === "connected");
  el.hidden = !on;
  $("detailsEmpty").hidden = on;
  if (!on) { el.innerHTML = ""; detailsHtml = ""; return; }
  const esc = (t) => String(t).replace(/[&<>"]/g, (ch) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[ch]));
  // Each way, as the page sees it. Audio shows one value when both ways match; video always shows
  // both, as its frame sizes change on their own (each side ramps up) and would otherwise flip
  // between one value and two.
  const codecs = (kind, agreed) => {
    const c = sel()?.codecs?.[kind];
    if (!c?.tx && !c?.rx) return agreed ? esc(agreed) : "–";
    if (kind !== "video" && c.tx === c.rx) return esc(c.tx);
    return `↑ ${esc(c.tx ?? "–")} &nbsp;↓ ${esc(c.rx ?? "–")}`;
  };
  // How the media's encrypted (the host says): "DTLS + SRTP" (keys from a DTLS handshake,
  // WebRTC) or "SRTP" (keys in the SDP).
  const how = esc(d.mediaSecurity ?? "SRTP");
  const enc = d.mediaEncrypted && d.signallingEncrypted
    ? `<span class="enc">${ICON.lock}Encrypted: ${how} media, SIP over ${d.signalling?.startsWith("WSS") ? "WSS (TLS)" : "TLS"}</span>`
    : d.mediaEncrypted ? `<span class="enc">${ICON.lock}Media encrypted (${how}); SIP unencrypted (${esc(d.signalling?.split(" ")[0] ?? "UDP")})</span>`
    : `<span class="enc off">${ICON.unlock}Not encrypted: plain RTP${d.signallingEncrypted ? "" : `, SIP over ${esc(d.signalling?.split(" ")[0] ?? "UDP")}`}</span>`;
  const contact = contactFor(sel()?.h.remote);
  const rows = [
    ["Call", `${d.direction === "outgoing" ? "Outgoing" : "Incoming"} · ${d.account ? esc(d.account) : "no account"}`],
    ["Address", sel()?.h.remote ? `<span class="mono">${esc(sel().h.remote)}</span>` : "–"],
    ...(contact ? [["Contact", `<span class="info-contact">${contactMark(contact.name)}Saved contact: ${esc(contact.name)}</span>`]] : []),
    ["Their phone", d.theirPhone ? esc(d.theirPhone) : "Didn't say"],
    ["Security", enc],
    ["Audio", codecs("audio", d.audio)],
    ["Video", call.hasVideo ? codecs("video", d.video) : "None"],
    ["SIP", d.signalling ? `<span class="mono">${esc(d.signalling)}</span>` : "–"],
    ["Media", d.theirMedia ? `<span class="mono">${esc(d.theirMedia)}</span>` : "–"],
    ["Call-ID", d.callId ? `<span class="mono">${esc(d.callId)}</span><button type="button" class="copy-id" id="copyCallId" title="Copy the Call-ID">Copy</button>` : "–"],
  ];
  // Only when something changed (the codecs come in every second), so selecting a value holds.
  const html = rows.map(([k, v]) => `<dt>${k}</dt><dd>${v}</dd>`).join("");
  if (html === detailsHtml) return;
  el.innerHTML = detailsHtml = html;
  const copy = $("copyCallId");
  if (copy) copy.onclick = (e) => {
    e.stopPropagation();
    navigator.clipboard?.writeText(d.callId).then(() => { copy.textContent = "Copied"; setTimeout(() => { copy.textContent = "Copy"; }, 1500); }, () => {});
  };
}

// ---- The dock: the keypad and the text panel beside the call ----
// Both open in a side panel to the right of the selected call (Focus view), and the stage narrows
// to make room; they stay open when you click elsewhere, and Esc or their × closes them. With both
// open they're stacked, the keypad (a fixed size) at the top and the text taking the rest, so you
// can read and send tones at once. The dock shows while either is open, and goes with them (the
// grid, hold and switching calls close them). In a narrow window (under 960 px) it floats over
// the stage's right edge instead.
export function renderDock() {
  const dock = $("dock");
  dock.hidden = inGrid() || !(keypadOpen || textOpen);
  dock.parentElement.classList.toggle("docked", !dock.hidden);
  dock.classList.toggle("both", keypadOpen && textOpen);
}

const lastOf = (a) => a[a.length - 1];
export const newQuality = () => ({
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
// The table's MOS colour, from the theme so it reads on light or dark.
const mosColour = (m) => (m == null ? "var(--muted)" : m >= 4 ? "var(--ok)" : m >= 3.6 ? "var(--warn-ink)" : "var(--danger)");

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

export function onQuality(msg) {
  const e = entries.get(msg.id);
  if (!e?.m || e.h.call !== "connected" || (msg.kind !== "audio" && msg.kind !== "video")) return;
  const quality = (e.quality ??= newQuality());
  if (msg.dir === "sent") {
    quality.sent[msg.kind] = { ...msg, mos: msg.kind === "audio" ? mos(msg) : null };
    log(`call ${e.id}: their RTCP on our ${msg.kind}: loss ${msg.lossPct}% (${msg.lost} total), jitter ${msg.jitterMs} ms, rtt ${msg.rttMs ?? "-"} ms`);
  } else if (msg.kind === "video") {
    quality.recvVideoRtcp = msg;
    log(`call ${e.id}: our RTCP on their video: loss ${msg.lossPct}% (${msg.lost} total), jitter ${msg.jitterMs} ms`);
  }
  if (e.id === selectedId) renderQuality();
}

// Run by app.js at this point in its start-up, in the order it always ran.
export function initQuality() {
  // Once a second during a call: how we're receiving, from the page's WebRTC stats, and a point
  // for the graph (the sent side holds its latest report until the next).
  setInterval(() => {
    for (const e of entries.values()) if (e.pc && e.m && e.h.call === "connected") measureQuality(e).catch(() => {});
  }, 1000);
}

async function measureQuality(e) {
  const { pc, m: call } = e;
  const q = (e.quality ??= newQuality());
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
  e.codecs = { audio: { tx: codecOf(outA), rx: codecOf(a) }, video: { tx: videoOf(out), rx: videoOf(v) } };
  if (e.id === selectedId) renderDetails();
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
  if (e.id === selectedId) renderQuality();
}

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

export function renderQuality() {
  const el = $("quality");
  el.hidden = !(state.call === "connected" && call);
  $("qEmpty").hidden = !el.hidden;
  if (el.hidden || $("paneQuality").hidden) return;
  const q = sel()?.quality ?? newQuality();
  const video = call.hasVideo;
  const fmt = (v, unit = "", digits = 1) => (v == null ? "–" : `${v.toFixed(digits)}${unit}`);
  const esc = (t) => t.replace(/"/g, "&quot;");

  // One value cell: in its column, marked when that column drives the graph.
  const cell = (col, text, { cls = "", title = "", colour = "" } = {}) =>
    `<span class="q-v ${cls}" data-col="${col}"${title ? ` title="${esc(title)}"` : ""}${colour ? ` style="color:${colour}"` : ""}>${text}</span>`;
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
      `<span class="q-col" title="${label.slice(2)}: ${from[col]}">${label}</span>`).join("") +
    row("Audio", "MOS", mosCell("sent", S.audio), mosCell("recv", R.audio)) +
    row("", "loss", lossCell("sent", S.audio, "Audio"), lossCell("recv", R.audio, "Audio")) +
    row("", "jitter", jitterCell("sent", S.audio), jitterCell("recv", R.audio));
  if (video) {
    html += row("Video", "loss", lossCell("sent", S.video, "Video"), lossCell("recv", R.video, "Video"), true) +
      row("", "fps", cell("sent", fmt(q.sentFps, "", 0), { title: "Frames per second we send (RTCP doesn't report what they see)" }),
        cell("recv", fmt(R.video?.fps, "", 0), { title: "Frames per second decoded here" }));
  }
  $("qTable").innerHTML = html;

  // Round trip, at the foot, where it's otherwise why ↑ Sent is empty. (What each value means
  // belongs in a help or info section later.)
  const rtt = S.audio?.rttMs ?? S.video?.rttMs;
  const rttText = rtt == null ? "" : fmt(rtt, " ms", rtt < 10 ? 1 : 0);
  const waited = performance.now() - q.startedAt;
  $("qFoot").textContent = S.audio || S.video ? (rttText && `RTT ${rttText}`)
    : waited > NO_RTCP_MS ? "No RTCP reports from them: their phone may not send them"
    : "Waiting for their RTCP reports";

  // The graphs, both directions over the last two minutes. Either way it's the receiver's view:
  // theirs of what we send, or ours of what they send.
  const series = [["mos", "Audio MOS"], ["aLoss", "Audio loss"], ...(video ? [["vLoss", "Video loss"], ["frozen", "Frozen (↓ only)"]] : [])];
  $("qLegend").innerHTML = series.map(([k, label]) => `<span style="--c:${Q_COLOUR[k]}">${label}</span>`).join("");
  for (const col of ["sent", "recv"]) {
    const c = $(col === "sent" ? "qChartSent" : "qChartRecv"), g = c.getContext("2d");
    c.setAttribute("aria-label", `${Q_LABEL[col]}, last 2 minutes: ${series.filter(([k]) => k !== "frozen" || col === "recv").map(([, l]) => l).join(", ")}`);
    g.clearRect(0, 0, c.width, c.height);
    g.fillStyle = "rgba(255,255,255,.12)";
    for (const level of [4, 3]) g.fillRect(0, c.height - ((level - 1) / 4) * c.height, c.width, 1);
    const pts = q.history.map((h) => h[col]);
    // Loss on 0–20%, MOS on 1–5.
    drawSeries(g, 0, 0, c.width, c.height, { values: pts.map((h) => h.aLoss), max: 20, colour: Q_COLOUR.aLoss, bars: true });
    if (video) drawSeries(g, 0, 0, c.width, c.height, { values: pts.map((h) => h.vLoss), max: 20, colour: Q_COLOUR.vLoss });
    drawSeries(g, 0, 0, c.width, c.height, { values: pts.map((h) => h.mos), min: 1, max: 5, colour: Q_COLOUR.mos });
    // On Received, along the bottom: how long our picture was frozen. (RTCP doesn't say whether
    // their picture froze, so Sent has none.)
    if (video && col === "recv") {
      const tick = c.height / 14;
      drawMarks(g, 0, c.height - tick, c.width, tick, pts.map((h) => h.frozen), Q_COLOUR.frozen, true);
    }
  }

  // PLIs, both ways: a tick for each second with one, received on
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
