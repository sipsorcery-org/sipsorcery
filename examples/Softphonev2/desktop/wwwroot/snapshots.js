// Saved diagnostics: as a call ends, its Call details, SIP ladder, Connection, Streams and Call quality are kept with its
// history entry, and the live tabs clear, as they always have. A Diagnostics button on the call's
// row in Home's Recent and People's Recent opens them again in the Diagnostics panel, with a bar
// across it saying whose they are, until Back to live, closing the panel, or a call starting.
// The host keeps each call's as a file beside the history and deletes it when the call drops off
// the list (MainForm, CallHistory.SaveDiagnostics). The desktop only: the web softphone has no
// host to keep them.
// As a call ends, in diagnostics mode, its ended card on the stage links to what was just saved
// (app.js, savedFor): the live tabs have cleared, and that's where the call went.

import { setDiagnostics, setDevTab, currentDevTab } from "./diagnostics.js";
import { renderDetails, renderQuality } from "./quality.js";
import { renderConnection } from "./connection.js";
import { renderLadder } from "./sipladder.js";
import { dropStreams, renderStreams, streamsSnapshot } from "./streams.js";
import { $, log, post, shortName } from "./util.js";

// The snapshot the panel shows ({ v, remote, endedAt, reason, details, quality, streams, entryId,
// name }), or null for the live call.
export let snap = null;
let wanted = null;   // the row asked for: { id, name }
const endedNames = new Map();   // call id -> name, till the host says it's saved them
const saved = new Map();        // call id -> { entryId, name }: the calls just ended whose are saved
let onSaved = null;             // app.js: to put the link on the call's ended card

// The last of a call's offers and answers can reach the page a moment after it ends (the host
// gathers a burst of SIP into one message), so Streams is taken after this.
const STREAMS_WAIT_MS = 600;
const SNAP_TABS = ["details", "ladder", "conn", "streams", "quality"];

// Run as a call ends, before its media is closed: what the three tabs had for it.
export function captureDiagnostics(e) {
  if (document.documentElement.dataset.host === "web") return;
  const endedAt = Date.now();
  const hasVideo = !!e.m?.hasVideo;
  const details = e.details ? { d: e.details, codecs: e.codecs ?? null, remote: e.h.remote, hasVideo } : null;
  const q = e.connectedAt ? e.quality : null;
  const quality = q ? {
    q: { sent: q.sent, recv: q.recv, recvVideoRtcp: q.recvVideoRtcp ?? null, sentFps: q.sentFps ?? null, history: q.history },
    waitedMs: performance.now() - q.startedAt, hasVideo,
  } : null;
  setTimeout(() => {
    const streams = streamsSnapshot(e.id, e.h.reason, e.codecs);
    dropStreams(e.id);
    if (!details && !quality && !streams) return;
    endedNames.set(e.id, shortName(e.h.remote));
    if (endedNames.size > 8) endedNames.delete(endedNames.keys().next().value);
    post({ type: "diag-save", id: e.id, snapshot: { v: 1, remote: e.h.remote, endedAt, reason: e.h.reason ?? null, details, quality, streams } });
  }, STREAMS_WAIT_MS);
}

// A row's Diagnostics button: the host sends the call's back (diag-snapshot).
export function openSnapshot(id, name) {
  wanted = { id, name };
  post({ type: "diag-get", id });
}

export function onDiagSnapshot(msg) {
  if (!wanted || msg.id !== wanted.id) return;
  const { name } = wanted;
  wanted = null;
  if (!msg.snapshot) { log(`no diagnostics saved for the call with ${name}`); return; }
  snap = { ...msg.snapshot, entryId: msg.id, name };
  if ($("devPanel").hidden) setDiagnostics(true);
  if (!SNAP_TABS.includes(currentDevTab())) setDevTab("details");
  showAll();
}

export function closeSnapshot() {
  if (!snap) return;
  snap = null;
  showAll();
}

function showAll() {
  renderSnap();
  renderDetails();
  renderLadder();
  renderConnection();
  renderStreams();
  renderQuality();
}

// The host has saved a call's diagnostics (diag-saved): its ended card links to them.
export function onDiagSaved(msg) {
  const name = endedNames.get(msg.id);
  if (name == null) return;
  endedNames.delete(msg.id);
  saved.set(msg.id, { entryId: msg.entry, name });
  if (saved.size > 8) saved.delete(saved.keys().next().value);
  onSaved?.(msg.id);
}

/** A call just ended whose diagnostics are saved: { entryId, name }, else null. */
export const savedFor = (id) => saved.get(id) ?? null;

/** Run with a call's id once its diagnostics are saved. */
export function onDiagnosticsSaved(fn) { onSaved = fn; }

// The bar across the panel while a snapshot shows, on its tabs.
export function renderSnap() {
  const bar = $("devSnap");
  if (!bar) return;
  bar.hidden = !snap || !SNAP_TABS.includes(currentDevTab());
  if (bar.hidden) return;
  const when = new Date(snap.endedAt).toLocaleString([], { day: "numeric", month: "short", hour: "2-digit", minute: "2-digit" });
  $("devSnapText").textContent = `Saved with the call: ${snap.name}, ended ${when}${snap.reason ? ` · ${snap.reason}` : ""}`;
}
