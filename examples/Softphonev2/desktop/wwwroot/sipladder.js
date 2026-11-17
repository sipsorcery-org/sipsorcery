// SIP ladder, a tab in Diagnostics (F12): every SIP message of the selected call (or of a call's
// saved diagnostics), as it crossed the wire, drawn as a ladder: a lane per party, an arrow per
// message. The rules are SIP Trace's (sipsorcery-servers, SIPSorcery.SipTrace: CallFlow,
// MermaidWriter and TranscriptWriter), so a ladder here reads like one there:
//  - Every message is numbered once, over the whole call, in the order it went. The number is on
//    the arrow, in the popup, and in both copies, so a message can be named.
//  - A retransmission is its own arrow, "retransmit 2", so the doubling timer can be read off it.
//  - Hide progress messages takes out every 1xx, ACK and PRACK (and a PRACK's response, which on
//    its own would read as an answer to the INVITE). What's left keeps its numbers and its times:
//    1, 2, 5, 6, not renumbered, as a number that moves when a box is ticked is no use.
//  - A call that spans Call-IDs (a transfer's new call) gives each its colour, with a key that
//    hides or shows each, numbers kept the same way.
//  - A lane is named from the From of the requests it sent (not of those sent to it, which name
//    the other party), under the socket it was reached at.
//  - Clicking a number (or an arrow) opens that message whole.
// Copy as Mermaid gives SIP Trace's diagram source (paste it into anything Mermaid runs in),
// Copy as text its transcript, and Save as PNG the ladder as shown, at twice its size, as SIP
// Trace's PNG is (the host asks where to save it). It's drawn here, not by Mermaid: the ladder is a few lanes, and
// this way it's in the app's own colours, light or dark.
// The host keeps the messages (StreamLog.Wire, from the transport's trace events), with the rest
// of the call's record (streams.js). The desktop only, as Streams.

import { selectedId } from "./app.js";
import { snap } from "./snapshots.js";
import { connRec } from "./streams.js";
import { $, escHtml as esc, post } from "./util.js";

const COLOURS = ["var(--tag-sip)", "var(--tag-dtls)", "var(--tag-call)", "var(--tag-ice)", "var(--tag-rtp)"];
let hideProgress = false;
try { hideProgress = localStorage.getItem("ladderHideProgress") === "1"; } catch { }
const hiddenCalls = new Set();   // Call-IDs switched off in the key
let callIdsOf = "";              // whose Call-IDs those are (a record's id)
let bodyHtml = "";
let flowNow = null;              // what's drawn: for the popup and the copies
let recNow = null;               // whose it is: for the PNG's title

// ---- The flow (CallFlow) ----

// 1xx responses, ACK and PRACK, and a PRACK's responses: progress, not decisions.
const isProgress = (m) => (m.response ? m.label.startsWith("1") || m.cSeqMethod === "PRACK" : m.label === "ACK" || m.label === "PRACK");

function flowOf(r) {
  const wire = r.wire ?? [];
  // In the order they went: a new Call-ID's first messages (a transfer's call) are kept by the
  // host until the call claims it, so they can be added after later ones. (A stable sort.)
  const messages = [...wire].sort((a, b) => a.at - b.at).map((w, i) => ({ ...w, n: i + 1 }));
  // Lanes: this phone first, then each far socket in the order it first appears.
  const remotes = [];
  for (const m of messages) if (!remotes.includes(m.remote)) remotes.push(m.remote);
  // Ours: the sockets it used, without the wildcard listener (0.0.0.0, [::]) a send can report
  // when there's a real one.
  const all = [...new Set(messages.map((m) => m.local))];
  const real = all.filter((l) => !/(0\.0\.0\.0|\[::\]):\d+$/.test(l));
  const locals = real.length ? real : all;
  const names = {};
  for (const m of messages) if (!m.out && !m.response && m.from && !names[m.remote]) names[m.remote] = m.from;
  const lanes = [{ key: "", name: "This phone", where: locals.join(", ") }, ...remotes.map((k) => ({ key: k, name: names[k] ?? "", where: k }))];
  const callIds = [...new Set(messages.map((m) => m.callId))];
  return { messages, lanes, callIds, t0: messages[0]?.at ?? 0 };
}

const shown = (flow) => flow.messages.filter((m) => !(hideProgress && isProgress(m)) && !hiddenCalls.has(m.callId));
const laneOf = (flow, m) => flow.lanes.findIndex((l) => l.key === m.remote);
const colourOf = (flow, m) => (flow.callIds.length > 1 ? COLOURS[flow.callIds.indexOf(m.callId) % COLOURS.length] : "var(--text)");
const what = (m) => `${m.label}${m.attempt > 1 ? ` retransmit ${m.attempt}` : ""}`;
const offset = (flow, m) => `${Math.round(m.at - flow.t0)} ms`;
const shortId = (id) => (id.length > 14 ? `${id.slice(0, 12)}…` : id);

// ---- Drawing ----

function ladderHtml(r, flow) {
  const n = flow.lanes.length;
  const x = (i) => ((i + 0.5) / n) * 100;
  const keys = flow.callIds.length > 1
    ? `<span class="ld-keys" role="group" aria-label="Calls">${flow.callIds.map((id, i) => `<label class="ld-key" title="${esc(id)}"><input type="checkbox" data-call="${esc(id)}"${hiddenCalls.has(id) ? "" : " checked"}><i style="background:${COLOURS[i % COLOURS.length]}"></i>${i === 0 ? "Call" : `Call ${i + 1}`} <span class="mono">${esc(shortId(id))}</span></label>`).join("")}</span>`
    : "";
  const tools = `<div class="ld-tools">
      <label class="ld-check"><input type="checkbox" id="ladderProgress"${hideProgress ? " checked" : ""}> Hide progress messages</label>${keys}
      <span class="sx-grow"></span>
      <button type="button" class="ld-btn" data-copy="mermaid" title="SIP Trace's diagram source: paste it into anything Mermaid runs in">Copy as Mermaid</button>
      <button type="button" class="ld-btn" data-copy="text" title="Every message shown, numbered, with its time and sockets">Copy as text</button>
      <button type="button" class="ld-btn" data-png title="The ladder as shown, as a picture at twice its size: for a ticket or a slide">Save as PNG</button>
    </div>`;
  const heads = flow.lanes.map((l, i) => `<div class="ld-head" style="left:${x(i)}%"><b>${esc(l.name || (i ? "Them" : ""))}</b><span class="mono">${esc(l.where)}</span></div>`).join("");
  const guides = flow.lanes.map((_, i) => `<i style="left:${x(i)}%"></i>`).join("");
  const list = shown(flow);
  const rows = list.map((m) => {
    const them = laneOf(flow, m);
    const [from, to] = m.out ? [0, them] : [them, 0];
    const rtl = to < from;
    const left = x(Math.min(from, to)), width = Math.abs(x(to) - x(from));
    // The number goes before a gap in the numbering too, so a filtered ladder says it's filtered.
    return `<div class="ld-row${m.response ? " resp" : ""}${rtl ? " rtl" : ""}" style="--c:${colourOf(flow, m)}">
        <span class="ld-t">${offset(flow, m)}</span>
        <div class="ld-track"><button type="button" class="ld-arrow" data-n="${m.n}" style="left:${left}%;width:${width}%" title="#${m.n} ${esc(what(m))}: open it">
          <span class="ld-lbl">${esc(m.label)}${m.attempt > 1 ? ` <em>retransmit ${m.attempt}</em>` : ""}</span>
          <span class="ld-line"><span class="ld-n">${m.n}</span></span>
          ${m.uri ? `<span class="ld-uri mono">${esc(m.uri.split(";")[0])}</span>` : ""}
        </button></div>
      </div>`;
  }).join("");
  const hiddenN = flow.messages.length - list.length;
  const foot = `<p class="ld-foot">${flow.messages.length} message${flow.messages.length === 1 ? "" : "s"}${hiddenN ? `, ${hiddenN} hidden` : ""}${flow.callIds.length > 1 ? ` · ${flow.callIds.length} Call-IDs` : ` · Call-ID <span class="mono">${esc(flow.callIds[0] ?? "")}</span>`}${flow.messages.length >= 500 ? " · the first 500 kept" : ""}. Times from the first message.</p>`;
  return `${tools}<div class="ld-scroll"><div class="ld" style="min-width:${70 + n * 190}px">
      <div class="ld-heads">${heads}</div>
      <div class="ld-body"><div class="ld-guides">${guides}</div>${rows || `<p class="q-empty">Nothing shown: every message is hidden.</p>`}</div>
    </div></div>${foot}`;
}

export function renderLadder() {
  const pane = $("paneLadder");
  if (!pane || pane.hidden) return;
  const r = connRec(selectedId);
  const has = !!r?.wire?.length;
  $("ladderEmpty").hidden = has;
  $("ladderEmpty").textContent = snap
    ? "No SIP messages were saved for this call (it was saved before the ladder kept them)."
    : "The SIP ladder shows here during a call: every SIP message of it, as it went, a lane per party.";
  $("ladderView").hidden = !has;
  if (!has) { bodyHtml = ""; flowNow = recNow = null; return; }
  if (callIdsOf !== `${r.id}${r.endedAt ? "e" : ""}${snap?.entryId ?? ""}`) { hiddenCalls.clear(); callIdsOf = `${r.id}${r.endedAt ? "e" : ""}${snap?.entryId ?? ""}`; }
  const who = r.names.length ? r.names.join(" → ") : `Call ${r.id}`;
  $("ladderWho").textContent = r.endedAt ? `${who} · ended: ${r.reason ?? ""}` : who;
  flowNow = flowOf(r);
  recNow = r;
  const html = ladderHtml(r, flowNow);
  if (html !== bodyHtml) { $("ladderBody").innerHTML = html; bodyHtml = html; }
}

// ---- A message, whole ----

let openN = 0;
function openMessage(n) {
  const m = flowNow?.messages.find((x) => x.n === n);
  if (!m) return;
  openN = n;
  const lane = flowNow.lanes[laneOf(flowNow, m)];
  const them = lane?.name ? `${lane.name} (${m.remote})` : m.remote;
  $("ldpSeq").textContent = `#${m.n}`;
  $("ldpWhat").textContent = what(m);
  $("ldpWhere").textContent = `${m.out ? `${m.local} → ${them}` : `${them} → ${m.local}`} · ${new Date(m.at).toLocaleTimeString([], { hour12: false })}.${String(m.at % 1000).padStart(3, "0")} · ${offset(flowNow, m)}`;
  $("ldpRaw").textContent = m.text.replace(/\r\n/g, "\n").trimEnd();
  const list = shown(flowNow), i = list.findIndex((x) => x.n === n);
  $("ldpPrev").disabled = i <= 0;
  $("ldpNext").disabled = i < 0 || i >= list.length - 1;
  const d = $("ladderPopup");
  if (!d.open) d.showModal();
  for (const a of document.querySelectorAll(".ld-arrow.on")) a.classList.remove("on");
  document.querySelector(`.ld-arrow[data-n="${n}"]`)?.classList.add("on");
}

function step(by) {
  const list = shown(flowNow), i = list.findIndex((x) => x.n === openN);
  if (list[i + by]) openMessage(list[i + by].n);
}

// ---- The copies (MermaidWriter, TranscriptWriter) ----

// Mermaid reads a colon as the start of the text and a semicolon as the end of a statement, and
// angle brackets would be markup: all go from a name or a label.
const mmEsc = (v) => String(v).replace(/[:;]/g, " ").replace(/[<>]/g, "").replace(/\r/g, "").replace(/\n/g, " ").trim();
// A URI keeps its colons; from its first semicolon (its parameters) it goes.
const mmUri = (v) => String(v).split(";")[0].replace(/\r/g, "").replace(/\n/g, " ").replace(/[<>]/g, "").trim();

function mermaid(flow) {
  const out = ["sequenceDiagram", "    autonumber 1"];
  flow.lanes.forEach((l, i) => out.push(`    participant P${i} as ${l.name ? `${mmEsc(l.name)}<br/>${mmEsc(l.where)}` : mmEsc(l.where)}`));
  // Mermaid numbers as it draws; a message whose number isn't the next (after a gap a filter
  // left) resets it, so the numbers stay the ladder's.
  let expected = 1;
  for (const m of shown(flow)) {
    if (m.n !== expected) out.push(`    autonumber ${m.n}`);
    expected = m.n + 1;
    const them = `P${laneOf(flow, m)}`;
    const [a, b] = m.out ? ["P0", them] : [them, "P0"];
    const label = `${mmEsc(m.label)}${m.attempt > 1 ? ` retransmit ${m.attempt}` : ""} (${Math.round(m.at - flow.t0)}ms)${m.uri ? `<br/>${mmUri(m.uri)}` : ""}`;
    out.push(`    ${a}${m.response ? "-->>" : "->>"}${b}: ${label}`);
  }
  return out.join("\n") + "\n";
}

const stamp = (t) => new Date(t).toISOString().replace("T", " ").replace("Z", "");
const header = (text, name) => text.match(new RegExp(`^(?:${name}):\\s*(.*)$`, "mi"))?.[1]?.trim();

function transcript(flow) {
  const list = shown(flow);
  const first = flow.messages.find((m) => !m.response) ?? flow.messages[0];
  const out = [
    `# Call-ID: ${flow.callIds.join(", ")}`,
    `# From:    ${header(first.text, "From|f") ?? "unknown"}`,
    `# To:      ${header(first.text, "To|t") ?? "unknown"}`,
    `# Started: ${stamp(flow.t0)}`,
    `# ${list.length} messages${list.length < flow.messages.length ? ` of ${flow.messages.length} (some hidden)` : ""}, as this phone sent and received them`,
    "#",
    "# One entry per message sent. A retransmission is a separate entry. Times are UTC.",
    "# Each entry is numbered with its place in the call, which is the",
    "# number the ladder draws beside the same arrow.",
    "",
  ];
  const where = (sock, lane) => (lane?.name ? `${lane.name} (${sock})` : sock);
  for (const m of list) {
    const lane = flow.lanes[laneOf(flow, m)];
    const [a, b] = m.out ? [m.local, where(m.remote, lane)] : [where(m.remote, lane), m.local];
    out.push(`===== #${m.n}  ${stamp(m.at)}  ${a} -> ${b}${m.attempt > 1 ? `  retransmit ${m.attempt}` : ""}`);
    out.push(m.text.replace(/\r\n/g, "\n").replace(/\n+$/, ""), "");
  }
  return out.join("\n");
}

// ---- The PNG ----

// The ladder as shown (filters and all), drawn onto a canvas from the flow rather than copied from
// the page: the page's is HTML, sized to the pane and scrolled. In the theme's colours, on its
// background (a transparent PNG of dark-theme text is unreadable on white), at 2×.
export function ladderPng(scale = 2) {
  const flow = flowNow, r = recNow;
  if (!flow) return null;
  const css = getComputedStyle(document.documentElement);
  const col = (v) => { const m = /^var\((--[\w-]+)\)$/.exec(v); return m ? css.getPropertyValue(m[1]).trim() : v; };
  const C = { bg: col("var(--surface)"), text: col("var(--text)"), muted: col("var(--muted)"), line: col("var(--border-strong)"), warn: col("var(--warn-ink)") };
  const family = getComputedStyle(document.body).fontFamily;
  const mono = "Consolas, monospace";
  const list = shown(flow);
  const n = flow.lanes.length;
  const GUTTER = 76, LANE = 230, TOP = 40, HEAD = 46, ROW = 46, PAD = 16;
  const W = GUTTER + n * LANE + PAD, H = TOP + HEAD + Math.max(1, list.length) * ROW + 34;
  const canvas = document.createElement("canvas");
  canvas.width = W * scale; canvas.height = H * scale;
  const g = canvas.getContext("2d");
  g.scale(scale, scale);
  g.fillStyle = C.bg; g.fillRect(0, 0, W, H);
  const x = (i) => GUTTER + (i + 0.5) * LANE;
  const fit = (t, max) => { if (g.measureText(t).width <= max) return t; while (t.length > 1 && g.measureText(`${t}…`).width > max) t = t.slice(0, -1); return `${t}…`; };
  // plate: the background behind it first, so a lane's line it crosses doesn't run through it.
  const text = (t, px, y, { font, colour = C.text, align = "center", max = 1e4, plate = false }) => {
    g.font = font; g.textAlign = align;
    const s = fit(t, max);
    if (plate) {
      const w = g.measureText(s).width, left = align === "center" ? px - w / 2 : align === "right" ? px - w : px;
      g.fillStyle = C.bg; g.fillRect(left - 3, y - 11, w + 6, 14);
    }
    g.fillStyle = colour; g.fillText(s, px, y);
  };
  // Whose, and when.
  const who = r?.names?.length ? r.names.join(" → ") : "SIP ladder";
  const when = new Date(flow.t0).toLocaleString([], { day: "numeric", month: "short", year: "numeric", hour: "2-digit", minute: "2-digit", second: "2-digit" });
  text(who, PAD, 24, { font: `600 14px ${family}`, align: "left", max: W / 2 });
  text(`${when} · ${flow.callIds.length > 1 ? `${flow.callIds.length} Call-IDs` : `Call-ID ${flow.callIds[0]}`}${list.length < flow.messages.length ? ` · ${flow.messages.length - list.length} of ${flow.messages.length} hidden` : ""}`,
    W - PAD, 24, { font: `12px ${family}`, colour: C.muted, align: "right", max: W / 2 - PAD });
  // The lanes.
  flow.lanes.forEach((l, i) => {
    text(l.name || (i ? "Them" : ""), x(i), TOP + 16, { font: `600 12px ${family}`, max: LANE - 16 });
    text(l.where, x(i), TOP + 32, { font: `11px ${mono}`, colour: C.muted, max: LANE - 16 });
  });
  g.strokeStyle = C.line; g.lineWidth = 1;
  g.beginPath(); g.moveTo(GUTTER, TOP + HEAD - 4.5); g.lineTo(W - PAD, TOP + HEAD - 4.5); g.stroke();
  g.setLineDash([3, 3]);
  for (let i = 0; i < n; i++) { g.beginPath(); g.moveTo(Math.round(x(i)) + 0.5, TOP + HEAD); g.lineTo(Math.round(x(i)) + 0.5, H - 30); g.stroke(); }
  g.setLineDash([]);
  // The messages.
  list.forEach((m, k) => {
    const y = TOP + HEAD + k * ROW;
    const c = col(colourOf(flow, m));
    const them = laneOf(flow, m);
    const [a, b] = m.out ? [x(0), x(them)] : [x(them), x(0)];
    const dir = Math.sign(b - a), ly = y + 24;
    const mid = (a + b) / 2, span = Math.abs(b - a) - 40;
    text(offset(flow, m), GUTTER - 10, ly + 4, { font: `11px ${family}`, colour: C.muted, align: "right" });
    g.font = `600 12px ${family}`;
    const label = fit(m.label, span), extra = m.attempt > 1 ? ` retransmit ${m.attempt}` : "";
    const lw = g.measureText(label).width;
    g.font = `12px ${family}`;
    const ew = extra ? g.measureText(extra).width : 0;
    text(label + (extra ? " ".repeat(Math.ceil(ew / g.measureText(" ").width)) : ""), mid - ew / 2, ly - 7, { font: `600 12px ${family}`, colour: c, plate: true });
    if (extra) text(extra, mid + lw / 2, ly - 7, { font: `12px ${family}`, colour: C.warn });
    g.strokeStyle = c; g.lineWidth = 1.5;
    g.setLineDash(m.response ? [5, 3] : []);
    g.beginPath(); g.moveTo(a, ly); g.lineTo(b - dir * 7, ly); g.stroke();
    g.setLineDash([]);
    g.fillStyle = c;
    g.beginPath(); g.moveTo(b, ly); g.lineTo(b - dir * 9, ly - 4.5); g.lineTo(b - dir * 9, ly + 4.5); g.closePath(); g.fill();
    // The number, at the end it leaves from.
    const nx = a + dir * 13;
    g.beginPath(); g.arc(nx, ly, 8.5, 0, Math.PI * 2); g.fill();
    text(String(m.n), nx, ly + 3.5, { font: `700 10px ${family}`, colour: C.bg });
    if (m.uri) text(m.uri.split(";")[0], mid, ly + 15, { font: `10.5px ${mono}`, colour: C.muted, max: span, plate: true });
  });
  text("Drawn by the SIPSorcery softphone. Times from the first message.", PAD, H - 10, { font: `10.5px ${family}`, colour: C.muted, align: "left", max: W - 2 * PAD });
  return canvas.toDataURL("image/png");
}

// A Call-ID is chosen by whoever placed the call, so none of it goes into a file name unexamined.
// (SIP Trace's rule.)
const fileName = (callId, ext) => (callId.replace(/[^A-Za-z0-9_-]/g, "-").slice(0, 60).replace(/^-+|-+$/g, "") || "call") + ext;

// Run by app.js at start-up.
export function initLadder() {
  const pane = $("paneLadder");
  if (!pane) return;
  pane.addEventListener("click", (ev) => {
    const a = ev.target.closest(".ld-arrow");
    if (a) { openMessage(Number(a.dataset.n)); return; }
    if (ev.target.closest("[data-png]") && flowNow) {
      const data = ladderPng();
      if (data) post({ type: "save-png", name: fileName(flowNow.callIds[0] ?? "call", ".png"), title: "Save the SIP ladder", data: data.slice(data.indexOf(",") + 1) });
      return;
    }
    const c = ev.target.closest("[data-copy]");
    if (c && flowNow) {
      post({ type: "copy", text: c.dataset.copy === "mermaid" ? mermaid(flowNow) : transcript(flowNow) });
      const was = c.textContent;
      c.textContent = "Copied";
      setTimeout(() => { c.textContent = was; }, 1200);
    }
  });
  pane.addEventListener("change", (ev) => {
    if (ev.target.id === "ladderProgress") {
      hideProgress = ev.target.checked;
      try { localStorage.setItem("ladderHideProgress", hideProgress ? "1" : "0"); } catch { }
    } else if (ev.target.dataset.call) {
      if (ev.target.checked) hiddenCalls.delete(ev.target.dataset.call);
      else hiddenCalls.add(ev.target.dataset.call);
    } else return;
    renderLadder();
  });
  const d = $("ladderPopup");
  $("ldpClose").addEventListener("click", () => d.close());
  $("ldpPrev").addEventListener("click", () => step(-1));
  $("ldpNext").addEventListener("click", () => step(1));
  $("ldpCopy").addEventListener("click", () => post({ type: "copy", text: $("ldpRaw").textContent }));
  d.addEventListener("keydown", (ev) => {
    if (ev.key === "ArrowLeft" || ev.key === "ArrowUp") { ev.preventDefault(); step(-1); }
    if (ev.key === "ArrowRight" || ev.key === "ArrowDown") { ev.preventDefault(); step(1); }
  });
  d.addEventListener("close", () => { for (const a of document.querySelectorAll(".ld-arrow.on")) a.classList.remove("on"); });
  // A click on the backdrop closes it.
  d.addEventListener("click", (ev) => { if (ev.target === d) d.close(); });
}
