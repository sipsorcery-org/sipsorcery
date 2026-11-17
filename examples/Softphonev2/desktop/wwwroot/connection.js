// Connection, a tab in Diagnostics (F12): the SIP leg's media connection, layer by layer, for the
// selected call (or a call's saved diagnostics). Two views:
//  - Layers: a row per layer in the order they come up (SDP, ICE, DTLS, SRTP, RTP, RTCP), each
//    with a status word and the facts that matter for it. The first that isn't fine is tinted,
//    and when it failed it says what's likely why, from the evidence. A layer the call doesn't
//    use (ICE and DTLS on a plain SIP call) says so, and how the call does it instead.
//  - Setup timeline: a lane per layer, from the INVITE: when each thing happened. Ringing is
//    squeezed, so the second after the answer, where the media comes up, gets the room.
// It reads the record Streams keeps for the call (streams.js): the host's offers and answers, its
// SIP messages, the media layers' events and facts (StreamLog.cs, Phone.cs), and the RTP counts
// each second. The relay to this page is left out: it's a loopback connection that's known to
// work. The desktop only, as Streams.

import { entries, selectedId } from "./app.js";
import { snap } from "./snapshots.js";
import { connRec } from "./streams.js";
import { $, escHtml as esc } from "./util.js";

let view = "layers";       // "layers" or "timeline"
let bodyHtml = "";
let pairsOpen = false;     // the connected ICE row's candidate pairs, unfolded (kept across the redraws)

const LAYERS = ["sdp", "ice", "dtls", "srtp", "rtp", "rtcp"];
const LAYER_NAME = { sdp: "SDP", sip: "SIP", ice: "ICE", dtls: "DTLS", srtp: "SRTP", rtp: "RTP", rtcp: "RTCP" };
const NO_RTCP_MS = 15000, GONE_MS = 3000;

const secs = (ms) => (ms < 10000 ? `${(ms / 1000).toFixed(2)} s` : ms < 60000 ? `${Math.round(ms / 1000)} s` : `${Math.floor(ms / 60000)} min ${Math.round((ms % 60000) / 1000)} s`);
const ago = (t, now) => (t ? `${secs(Math.max(0, now - t))} ago` : null);

// ---- Reading the call ----

// Their SDP in the last answered offer/answer (or the last one, if none was).
function lastExchange(r) {
  const ok = r.exchanges.filter((x) => x.ok);
  return ok.at(-1) ?? r.exchanges.at(-1) ?? null;
}
const theirSdp = (x) => (x ? (x.byUs ? x.answer : x.offer) : null);
const ourSdp = (x) => (x ? (x.byUs ? x.offer : x.answer) : null);

// Their ICE candidates, from their SDP: { type, address, port, protocol }.
function candidates(sdp) {
  const out = [];
  for (const line of (sdp ?? "").split(/\r?\n/)) {
    const m = /^a=candidate:\S+ \d+ (\S+) \d+ (\S+) (\d+) typ (\S+)/i.exec(line.trim());
    if (m) out.push({ protocol: m[1].toUpperCase(), address: m[2], port: Number(m[3]), type: m[4] });
  }
  // Each address once: audio and video carry the same ones when bundled.
  return out.filter((c, i) => out.findIndex((d) => d.address === c.address && d.port === c.port && d.protocol === c.protocol) === i);
}
const isPrivate = (a) => /^(10\.|192\.168\.|172\.(1[6-9]|2\d|3[01])\.|127\.|169\.254\.|f[cd][0-9a-f]{2}:|fe80:|::1$)/i.test(a) || /\.local$/i.test(a);
// The first m-line's address in an SDP (c=, the media's or the session's) and port.
function sdpAddress(sdp) {
  let session = null, media = null, port = null;
  for (const line of (sdp ?? "").split(/\r?\n/)) {
    if (line.startsWith("m=") && port == null) port = Number(line.split(" ")[1]);
    const c = /^c=IN IP[46] (\S+)/.exec(line);
    if (c) { if (port == null) session = c[1]; else if (!media) media = c[1]; }
  }
  const a = media ?? session;
  return a ? `${a.includes(":") ? `[${a}]` : a}:${port}` : null;
}
// The SRTP suite the answer chose (a=crypto:1 AES_CM_128_HMAC_SHA1_80 …).
function cryptoSuite(x) {
  const answer = x?.answer ?? "";
  return /a=crypto:\d+ (\S+)/.exec(answer)?.[1] ?? null;
}

// The facts the tab needs about a call, live or saved.
function factsOf(r) {
  const f = r.facts ?? {};
  const x = lastExchange(r);
  const live = r.streams.filter((s) => s.ourPort && s.theirPort);
  const webrtc = live.some((s) => /TLS/i.test(s.transport)) || f.iceState != null;
  const sdes = live.some((s) => /SDES/.test(s.security));
  const encrypted = live.some((s) => s.security && s.security !== "None");
  const details = snap ? snap.details?.d : entries.get(r.id)?.details;
  return { f, x, live, webrtc, sdes, encrypted, signalling: details?.signalling ?? null };
}

// The answer to the call's first INVITE: the first 2xx to it, the other way.
function answerOf(r) {
  const sip = r.sip ?? [];
  const first = sip[0];
  return first ? sip.find((s) => s.cSeq === first.cSeq && /^2\d\d /.test(s.text) && s.out !== first.out) ?? null : null;
}

// ---- The ladder ----

// One row: [layer, state ("ok", "warn", "bad", "na", "wait"), status word, facts html, why html].
function ladder(r, now) {
  const { f, x, live, webrtc, sdes, encrypted, signalling } = factsOf(r);
  const rows = [];
  const ended = !!r.endedAt;
  const kinds = live.map((s) => (s.kind === "data" ? "text (data channel)" : s.kind));
  const theirs = candidates(theirSdp(x));

  // SDP
  if (!x) rows.push(["sdp", "wait", "No offer yet", ""]);
  else if (x.pending && !r.exchanges.some((e) => e.ok)) rows.push(["sdp", "wait", "Waiting for their answer", esc(x.status ?? "sent")]);
  else if (!r.exchanges.some((e) => e.ok)) rows.push(["sdp", "bad", "Turned down", `${esc(x.offerMsg)} got <b>${esc(x.status ?? "no answer")}</b>`]);
  else {
    const profile = [...new Set(live.map((s) => s.transport))].join(", ");
    const cands = webrtc ? ` · their SDP has ${theirs.length} candidate${theirs.length === 1 ? "" : "s"}${theirs.length ? `: ${esc(theirs.slice(0, 3).map((c) => `${c.type} ${c.address}:${c.port}`).join(", "))}${theirs.length > 3 ? " …" : ""}` : ""}` : "";
    rows.push(["sdp", live.length ? "ok" : "bad", live.length ? "Agreed" : "Nothing agreed", `${esc(kinds.join(", ") || "no media")} · <span class="mono">${esc(profile)}</span><small>${esc(x.answerMsg ? `the answer in the ${x.answerMsg}` : "")}${cands}</small>`]);
  }
  const sdpOk = rows[0][1] === "ok";

  // ICE
  if (!webrtc) {
    const where = r.rates.audio?.remote ?? sdpAddress(theirSdp(x));
    rows.push(["ice", "na", "Not used", `plain SIP sends straight to the address in their SDP${where ? `: <span class="mono">${esc(where)}</span>` : ""}`]);
  } else if (!sdpOk || !f.iceState || f.iceState === "new") {
    rows.push(["ice", "na", "Not started", sdpOk ? "waiting for the media to start" : ""]);
  } else {
    const took = f.iceStartedAt && f.iceDoneAt ? ` · ${secs(f.iceDoneAt - f.iceStartedAt)}` : "";
    const role = f.iceControlling != null ? ` · we're ${f.iceControlling ? "controlling" : "controlled"}` : "";
    const peers = f.icePeers ?? [];
    const answered = peers.filter((p) => p.answers > 0).length;
    if (f.iceState === "connected" || f.iceState === "completed") {
      const rtt = f.iceRtt != null ? ` · RTT ${f.iceRtt} ms` : "";
      const more = f.icePairs?.length ? `<details class="cx-more"${pairsOpen ? " open" : ""}><summary>${f.icePairs.length} candidate pair${f.icePairs.length === 1 ? "" : "s"}</summary>${checkTable(f, peers, theirs)}</details>` : "";
      rows.push(["ice", "ok", "Connected", `<span class="mono">${esc(f.icePair ?? "")}</span><small>ours: ${esc(f.iceLocal ?? "?")}${role}${took}${rtt} · ${answered} of ${peers.length} of their addresses answered</small>`, more]);
    } else if (f.iceState === "checking") {
      rows.push(["ice", "wait", "Checking", `ours: ${esc(f.iceLocal ?? "?")}${role} · ${peers.reduce((n, p) => n + p.sent, 0)} checks sent, ${peers.reduce((n, p) => n + p.answers, 0)} answers`, checkTable(f, peers, theirs)]);
    } else if (f.iceState === "disconnected") {
      rows.push(["ice", "warn", "Disconnected", `checks to them stopped getting answers · was <span class="mono">${esc(f.icePair ?? "?")}</span>`,
        `<p class="cx-why"><b>Likely why</b> ICE checks the path every few seconds once it's up, and their phone stopped answering: it may have gone (closed, crashed or lost its network), or a NAT between you forgot the mapping. The media stops with it.</p>`]);
    } else if (f.icePair) {
      // It connected, then went: their phone stopped answering the checks that keep the path up.
      rows.push(["ice", "bad", "Lost", `connected, then their checks stopped getting answers${f.iceLostAt && f.iceDoneAt ? ` (gone ${secs(f.iceLostAt - f.iceDoneAt)} after it connected)` : ""} · was <span class="mono">${esc(f.icePair)}</span>`,
        `<p class="cx-why"><b>Likely why</b> ICE checks the path every few seconds once it's up, and their phone stopped answering long enough to give up: it may have gone (closed, crashed or lost its network), or a NAT between you forgot the mapping.</p>`]);
    } else {
      const why = iceWhy(peers, theirs, f);
      rows.push(["ice", "bad", `Failed${f.iceStartedAt && f.iceDoneAt ? ` after ${secs(f.iceDoneAt - f.iceStartedAt)}` : ""}`,
        `${answered} of ${peers.length} of their addresses answered our checks${role}`, `${why ? `<p class="cx-why"><b>Likely why</b> ${why}</p>` : ""}${checkTable(f, peers, theirs)}`]);
    }
  }
  // Lost or disconnected after connecting: the layers above it still came up, so show them.
  const iceLost = webrtc && (rows[1][2] === "Lost" || rows[1][2] === "Disconnected");
  const iceOk = !webrtc || rows[1][1] === "ok" || iceLost;

  // DTLS
  if (!webrtc) rows.push(["dtls", "na", "Not used", sdes ? "the keys came in the SDP (SDES)" : encrypted ? "" : "the media isn't encrypted"]);
  else if (!iceOk || !f.dtlsState) rows.push(["dtls", "na", "Not reached", ""]);
  else if (f.dtlsState === "starting") rows.push(["dtls", ended ? "bad" : "wait", ended ? "Didn't finish" : "Handshaking", `we're the ${esc(f.dtlsRole ?? "?")}${f.dtlsStartedAt ? ` · for ${secs((r.endedAt || now) - f.dtlsStartedAt)}` : ""}`]);
  else if (f.dtlsState === "connected") rows.push(["dtls", "ok", "Connected", `we're the ${esc(f.dtlsRole)} (${f.dtlsRole === "client" ? "a=setup:active" : "a=setup:passive"}) · their certificate matches their SDP's fingerprint${f.dtlsStartedAt && f.dtlsDoneAt ? ` · ${secs(f.dtlsDoneAt - f.dtlsStartedAt)}` : ""}${f.dtlsAlert ? `<small>then an alert: <span class="mono">${esc(f.dtlsAlert)}</span></small>` : ""}`]);
  else {
    const mismatch = /fingerprint/i.test(f.dtlsError ?? "");
    const prints = mismatch && (f.dtlsExpected || f.dtlsRemote)
      ? `<small>their SDP's: <span class="mono">${esc(fp(f.dtlsExpected))}</span> · their certificate's: <span class="mono">${esc(fp(f.dtlsRemote))}</span></small>` : "";
    const alert = f.dtlsAlert ? `<small>alert: <span class="mono">${esc(f.dtlsAlert)}</span></small>` : "";
    rows.push(["dtls", "bad", "Failed", `we're the ${esc(f.dtlsRole ?? "?")}${f.dtlsError ? `<small class="mono">${esc(f.dtlsError)}</small>` : ""}${alert}${prints}`,
      `<p class="cx-why"><b>Likely why</b> ${dtlsWhy(f, mismatch)}</p>`]);
  }
  const dtlsOk = !webrtc || rows[2][1] === "ok";

  // SRTP
  const { errs, level: errLevel } = srtpErrs(f);
  if (webrtc) {
    if (!dtlsOk) rows.push(["srtp", "na", "Not reached", ""]);
    else rows.push(["srtp", errLevel ?? "ok", "Keys from DTLS", `${f.dtlsProfile ? `<span class="mono">${esc(f.dtlsProfile)}</span> · ` : ""}the profile and keys were agreed in the handshake, not the SDP${errs}`, errLevel ? srtpWhy(f) : ""]);
  } else if (sdes) {
    const open = signalling && !/^(TLS|WSS)/i.test(signalling);
    rows.push(["srtp", errLevel === "bad" ? "bad" : open ? "warn" : errLevel ?? "ok", "Keys from SDP", `${esc(cryptoSuite(x) ?? "SDES")}${open ? `<small>the keys went in SIP over ${esc(signalling.split(" ")[0])}: anyone who can see the SIP can read them</small>` : ""}${errs}`, errLevel ? srtpWhy(f) : ""]);
  } else rows.push(["srtp", sdpOk ? "na" : "na", sdpOk ? "Not used" : "Not reached", sdpOk ? "plain RTP: the media isn't encrypted" : ""]);

  // RTP
  const reached = sdpOk && iceOk && dtlsOk;
  if (!reached) rows.push(["rtp", "na", "Not reached", f.rtpFirstAudio || f.rtpFirstVideo ? "" : "0 packets from them"]);
  else rows.push(rtpRow(r, f, live, now, iceLost));

  // RTCP
  if (!reached) rows.push(["rtcp", "na", "Not reached", ""]);
  else {
    const mux = r.rates.audio ? (r.rates.audio.rtcpLocal ? `port ${esc(r.rates.audio.rtcpLocal.split(":").pop())}, not muxed` : "rtcp-mux") : null;
    const facts = [mux, f.rtcpRtt != null ? `RTT ${f.rtcpRtt} ms` : null, f.rtcpReports ? `${f.rtcpReports} report${f.rtcpReports === 1 ? "" : "s"}` : null, f.rtcpBye != null ? `BYE from them${f.rtcpBye ? `: ${esc(f.rtcpBye)}` : ""}` : null].filter(Boolean).join(" · ");
    const since = r.connectedAt || (r.exchanges.find((e) => e.ok)?.at ?? r.startedAt);
    const quiet = (r.endedAt || now) - (f.rtcpLastAt ?? 0);
    if (f.rtcpLastAt && quiet > NO_RTCP_MS) rows.push(["rtcp", "warn", "Stopped", `their last report ${ended ? `came ${secs(quiet)} before the end` : ago(f.rtcpLastAt, now)} (they come every 5 s or so) · ${facts}`]);
    else if (f.rtcpLastAt) rows.push(["rtcp", "ok", ended ? "Reported" : "Reporting", `their last report ${ended ? `came ${secs(quiet)} before the end` : ago(f.rtcpLastAt, now)} · ${facts}`]);
    else if ((r.endedAt || now) - since > NO_RTCP_MS) rows.push(["rtcp", "warn", "No reports", `none from them in ${secs((r.endedAt || now) - since)}: their phone may not send RTCP${facts ? ` · ${facts}` : ""}`]);
    else rows.push(["rtcp", "wait", "Waiting", `for their first report (every 5 s or so)${facts ? ` · ${facts}` : ""}`]);
  }
  return rows;
}

function rtpRow(r, f, live, now, iceLost) {
  const kinds = live.filter((s) => s.kind === "audio" || s.kind === "video");
  const first = (k) => f[`rtpFirst${k[0].toUpperCase()}${k.slice(1)}`];
  const start = answerOf(r)?.at ?? r.exchanges.find((e) => e.ok)?.at ?? r.startedAt;
  const firsts = kinds.map((s) => (first(s.kind) ? `first ${s.kind} ↓ ${secs(first(s.kind) - start)} after the answer` : `no ${s.kind} ↓ yet`)).join(" · ");
  // Where their RTP came from, if not their SDP's address: the session switched to it (and says so).
  const mv = f.rtpMovedAudio ?? f.rtpMovedVideo;
  const a = r.rates.audio ?? r.rates[kinds[0]?.kind];
  const moved = mv ? `<small>their ${f.rtpMovedAudio ? "audio" : "video"} came from <span class="mono">${esc(mv.to)}</span>, not their SDP's address (<span class="mono">${esc(mv.from)}</span>), so we send there now: a NAT between you, likely</small>`
    : a?.from && a.remote && a.from !== a.remote ? `<small>it arrives from <span class="mono">${esc(a.from)}</span>, not their SDP's address (<span class="mono">${esc(a.remote)}</span>): a NAT between you, likely</small>` : "";
  if (r.endedAt && iceLost) return ["rtp", "bad", "Stopped", `${firsts}<small>with ICE: see above</small>`];
  if (r.endedAt) {
    const any = kinds.some((s) => first(s.kind));
    const timeouts = kinds.filter((s) => f[`rtpTimeout${s.kind[0].toUpperCase()}${s.kind.slice(1)}`]).map((s) => `${s.kind} timed out`);
    return ["rtp", timeouts.length ? "bad" : any ? "ok" : "bad", timeouts.length ? "Timed out" : any ? (mv ? "Flowed, moved" : "Flowed") : "Nothing arrived", `${[firsts, ...timeouts].filter(Boolean).join(" · ")}${moved}`];
  }
  const counts = kinds.map((s) => { const x = r.rates[s.kind]; return x ? `${s.kind} ↑ ${x.tx} ↓ ${x.rx}` : null; }).filter(Boolean).join(" · ");
  const missing = kinds.filter((s) => { const x = r.rates[s.kind]; return x && x.rx === 0 && x.idle >= GONE_MS && (s.theirs === "sendrecv" || s.theirs === "sendonly") && (s.ours === "sendrecv" || s.ours === "recvonly"); });
  if (missing.length && iceLost) return ["rtp", "bad", "Stopped", `${counts} packets/s · ${firsts}<small>with ICE: see above</small>`];
  if (missing.length) {
    const why = `<p class="cx-why"><b>Likely why</b> Their SDP says they send ${missing.map((s) => s.kind).join(" and ")}, but nothing's arrived for ${secs(Math.min(...missing.map((s) => r.rates[s.kind].idle)))}. A firewall or NAT may be dropping it, or their phone is sending somewhere else (their SDP says <span class="mono">${esc(r.rates[missing[0].kind]?.remote ?? "?")}</span>).</p>`;
    return ["rtp", "bad", "Nothing arriving", `${counts} packets/s · ${firsts}${moved}`, why];
  }
  return ["rtp", a?.rx ? "ok" : "wait", a?.rx ? (moved ? "Flowing, moved" : "Flowing") : "Starting", `${counts} packets/s · ${firsts}${moved}`];
}

// The packets that couldn't be decrypted, by why, as the library counts them on each stream. A call
// saved before it did has only srtpErrors (and srtpError, the log line it came from).
function srtpErrs(f) {
  const n = f.srtpErrors ?? 0, rtcp = f.srtcpErrors ?? 0;
  if (!n && !rtcp) return { errs: "", level: null };
  const counted = f.srtpAuth != null;
  const keys = counted ? f.srtpAuth + (f.srtpOther ?? 0) > 0 : n > 0;
  const parts = counted ? [f.srtpAuth ? `${f.srtpAuth} with keys that don't match` : null, f.srtpReplay ? `${f.srtpReplay} replay${f.srtpReplay === 1 ? "" : "s"}` : null, f.srtpOther ? `${f.srtpOther} other` : null].filter(Boolean) : [];
  const what = [n ? `${n} RTP packet${n === 1 ? "" : "s"}` : null, rtcp ? `${rtcp} RTCP` : null].filter(Boolean).join(" and ");
  const errs = ` · <b class="${keys ? "bad-ink" : ""}">${what} couldn't be decrypted</b>${parts.length ? ` (${parts.join(", ")})` : ""}${f.srtpError ? `<small class="mono">${esc(f.srtpError)}</small>` : ""}`;
  return { errs, level: keys ? "bad" : "warn" };
}

function srtpWhy(f) {
  if (f.srtpAuth == null) return `<p class="cx-why"><b>Likely why</b> Packets arrive but can't be decrypted: the keys don't match. A re-INVITE that changed the keys on one side only, a server that rewrote the SDP, or packets from an earlier session. (The count is from the library's log, while this was the only call.)</p>`;
  if (f.srtpAuth) return `<p class="cx-why"><b>Likely why</b> Packets arrive but their authentication fails: they were encrypted with other keys than ours for them. A re-INVITE that changed the keys on one side only, a server between you that rewrote the SDP, or packets from an earlier session still arriving on this port.</p>`;
  if (f.srtpOther) return `<p class="cx-why"><b>Likely why</b> Packets arrive that the SRTP context can't take: a cipher it doesn't support, a key identifier (MKI) it doesn't know, or a key past its lifetime.</p>`;
  return `<p class="cx-why"><b>Likely why</b> Packets already seen, or too old for the replay window: duplicated on the network, or held up long enough for later ones to pass them. A few are harmless; the rest decrypt.</p>`;
}

// A DTLS failure's likely why, from the library's reason and the alert.
function dtlsWhy(f, mismatch) {
  if (mismatch) return "Their DTLS certificate isn't the one their SDP's fingerprint names. A server between you may have rewritten the SDP without terminating the media. ICE worked, so the network path is fine.";
  const alert = f.dtlsAlert ?? "";
  if (/handshake_failure|insufficient_security|illegal_parameter/i.test(alert)) return `The handshake was refused (${esc(alert)}): their phone and this one have no SRTP profile or cipher suite in common, or one side wouldn't accept the other's certificate. ICE worked, so the network path is fine.`;
  if (alert) return `The handshake ended with an alert (${esc(alert)}). ICE worked, so packets reach them.`;
  if (/time/i.test(f.dtlsError ?? "")) return "No answer to the handshake: ICE worked, so packets reach them, but nothing came back. Their phone may not be doing DTLS-SRTP the way its SDP says, or a firewall lets STUN through but not DTLS.";
  return "ICE worked, so packets reach them, but the handshake didn't complete: their phone may not support DTLS-SRTP the way its SDP says, or a firewall lets STUN through but not DTLS.";
}

// A fingerprint, shortened: its hash and first bytes.
const fp = (s) => (!s ? "?" : s.length > 32 ? `${s.slice(0, 32)}…` : s);

function iceWhy(peers, theirs, f) {
  if (!theirs.length) return "Their SDP has no ICE candidates, so there was nowhere to check.";
  const theirChecks = peers.reduce((n, p) => n + p.theirs, 0);
  const allPrivate = theirs.every((c) => c.type === "host" && isPrivate(c.address));
  if (allPrivate) return `They offered only private addresses (${esc(theirs.map((c) => c.address).slice(0, 2).join(", "))}) and no server-reflexive or relay candidate, so nothing we send can reach them unless we're on their network. Their phone needs a STUN or TURN server, or their server needs to relay the media.`;
  if (!theirChecks) return "None of our checks got an answer, and none of theirs reached us: a firewall is likely dropping UDP between you, or one side's addresses are unreachable from the other.";
  return "Their checks reach us, but none of ours got an answer back: their side's firewall or NAT may let our packets in only after they've sent to us from the same address.";
}

// The checks: per candidate pair from the library's checklist, or (a call saved before it could be
// read) per their address, from the STUN messages.
const checkTable = (f, peers, theirs) => (f.icePairs ? pairTable(f.icePairs, theirs) : peerTable(peers, theirs));

const PAIR_STATE = { Frozen: "frozen", Waiting: "waiting", InProgress: "checking", Succeeded: "succeeded", Failed: "failed" };

function pairTable(pairs, theirs) {
  const addr = (c) => `${c.address.includes(":") ? `[${c.address}]` : c.address}:${c.port}`;
  const rows = pairs.map((p) => `<tr class="${p.selected ? "sel" : ""}"><td class="mono">${esc(p.ours)}</td><td class="mono">${esc(p.theirs)}</td>`
    + `<td class="${p.state === "Failed" ? "bad-ink" : ""}">${esc(PAIR_STATE[p.state] ?? p.state)}${p.selected ? " · in use" : p.nominated ? " · nominated" : ""}</td>`
    + `<td>${p.sent}</td><td class="${p.sent && !p.answers ? "bad-ink" : ""}">${p.answers}</td><td>${p.received}</td><td>${p.rtt != null ? `${p.rtt} ms` : ""}</td></tr>`).join("");
  const unchecked = theirs.filter((c) => !pairs.some((p) => p.theirs.endsWith(` ${addr(c)}`)))
    .map((c) => `<tr><td></td><td class="mono">${esc(c.type)} ${esc(c.protocol)} ${esc(addr(c))}</td><td colspan="5" class="m">not paired</td></tr>`).join("");
  return `<table class="cx-peers"><thead><tr><th>Ours</th><th>Theirs</th><th>State</th><th>Our checks</th><th>Answers</th><th>Their checks</th><th>RTT</th></tr></thead><tbody>${rows}${unchecked}</tbody></table>`;
}

function peerTable(peers, theirs) {
  if (!peers.length && !theirs.length) return "";
  const typeOf = (ep) => theirs.find((c) => ep.includes(c.address) && ep.endsWith(`:${c.port}`))?.type ?? "learned";
  const rows = peers.map((p) => `<tr><td class="mono">${esc(typeOf(p.ep))} ${esc(p.ep)}</td><td>${p.sent}</td><td class="${p.answers ? "" : "bad-ink"}">${p.answers}</td><td>${p.theirs}</td></tr>`).join("");
  const unchecked = theirs.filter((c) => !peers.some((p) => p.ep.includes(c.address) && p.ep.endsWith(`:${c.port}`)))
    .map((c) => `<tr><td class="mono">${esc(c.type)} ${esc(c.address.includes(":") ? `[${c.address}]` : c.address)}:${c.port}</td><td colspan="3" class="m">not checked</td></tr>`).join("");
  return `<table class="cx-peers"><thead><tr><th>Their address</th><th>Our checks</th><th>Answers</th><th>Their checks</th></tr></thead><tbody>${rows}${unchecked}</tbody></table>`;
}

function layersHtml(r) {
  const now = Date.now();
  const rows = ladder(r, now);
  const firstBad = rows.findIndex(([, st]) => st === "bad" || st === "warn");
  return `<div class="cx-ladder">${rows.map(([layer, st, word, facts, why], i) =>
    `<div class="cx-row ${st}${i === firstBad ? " first" : ""}"><b>${LAYER_NAME[layer]}</b><span class="cx-st"><i></i>${esc(word)}</span><span class="cx-facts">${facts}${why ?? ""}</span></div>`).join("")}</div>`;
}

// ---- The setup timeline ----

function timelineHtml(r) {
  const sip = r.sip ?? [];
  if (!sip.length) return `<p class="q-empty">The timeline starts with the call's first SIP message.</p>`;
  const first = sip[0];
  const answer = answerOf(r);
  const events = [];
  for (const s of sip) events.push({ at: s.at, layer: "sip", text: `${s.out ? "↑" : "↓"} ${s.text}`, level: /^[3-6]\d\d /.test(s.text) ? "bad" : "" });
  for (const e of r.events ?? []) events.push({ at: e.at, layer: e.layer, text: e.text, level: e.level });
  if (r.endedAt) events.push({ at: r.endedAt, layer: "sip", text: `ended: ${r.reason ?? ""}`, level: "" });
  // From the INVITE, or from just before it: we gather our candidates as we make the offer.
  const t0 = Math.min(first.at, ...events.map((e) => e.at));
  const tEnd = Math.max(...events.map((e) => e.at), r.endedAt || Date.now());
  // Where things go: INVITE to the answer in the first 16%; then the stretch after the answer
  // where the media comes up (ICE, DTLS, SRTP and the first RTP, within 10 s), to 86%; anything
  // later (their first RTCP report, usually some seconds on; a BYE) squeezed into the rest.
  const setupTimes = answer ? events.filter((e) => e.at >= answer.at && e.at - answer.at < 10000 && ["ice", "dtls", "srtp", "rtp"].includes(e.layer)).map((e) => e.at) : [];
  const setupEnd = answer ? answer.at + Math.max(1000, (Math.max(answer.at, ...setupTimes) - answer.at) * 1.25) : tEnd;
  const PRE = answer ? 16 : 0, MAIN = answer ? 86 : 100;
  const x = (t) => {
    if (!answer) return ((t - t0) * 100) / Math.max(1, tEnd - t0);
    if (t <= answer.at) return ((t - t0) * PRE) / Math.max(1, answer.at - t0);
    if (t <= setupEnd) return PRE + ((t - answer.at) * (MAIN - PRE)) / Math.max(1, setupEnd - answer.at);
    return MAIN + 2 + ((t - setupEnd) * (100 - MAIN - 2)) / Math.max(1, tEnd - setupEnd);
  };
  const lanes = ["sip", "ice", "dtls", "srtp", "rtp", "rtcp"];
  const webrtc = factsOf(r).webrtc;
  const laneHtml = lanes.map((layer) => {
    const evs = events.filter((e) => e.layer === layer).sort((a, b) => a.at - b.at);
    let body = "";
    if (layer === "sip") {
      const ring = sip.find((s) => s.cSeq === first.cSeq && /^18\d /.test(s.text));
      if (ring && answer) body += `<div class="cx-span" style="left:${x(ring.at)}%;width:${Math.max(0.5, x(answer.at) - x(ring.at))}%" title="ringing ${secs(answer.at - ring.at)}"><span>ringing ${secs(answer.at - ring.at)}</span></div>`;
    }
    // Marks, with labels; marks closer than 4% share a label.
    const groups = [];
    for (const e of evs) {
      const g = groups.at(-1);
      if (g && x(e.at) - x(g[0].at) < 4) g.push(e); else groups.push([e]);
    }
    let lastEnd = -Infinity;
    body += groups.map((g) => {
      const p = x(g[0].at);
      const level = g.some((e) => e.level === "bad") ? "bad" : g.some((e) => e.level === "warn") ? "warn" : g.some((e) => e.level === "ok") ? "ok" : "";
      const label = layer === "sip" && g.length > 2 ? `${g[0].text} … ${g.at(-1).text}` : g.map((e) => e.text).join(" · ");
      const at = (t) => (answer && t >= answer.at ? `answer +${secs(t - answer.at)}` : `INVITE ${t < first.at ? "−" : "+"}${secs(Math.abs(t - first.at))}`);
      const when = answer && g[0].at >= answer.at ? `+${secs(g[0].at - answer.at)}` : "";
      const title = g.map((e) => `${at(e.at)}: ${e.text}`).join("\n");
      // Before the answer the stretch is squeezed: marks there are labelled only if nothing was answered.
      const showLabel = p > lastEnd && (!answer || g[0].at >= answer.at || layer !== "sip");
      if (showLabel) lastEnd = p + Math.min(40, label.length * 0.75);
      const right = p > 78;
      return `<span class="cx-mk ${level}" style="left:${p}%" title="${esc(title)}"></span>`
        + (showLabel ? `<span class="cx-lbl ${level}${right ? " r" : ""}" style="${right ? `right:calc(${100 - p}% + 7px)` : `left:calc(${p}% + 7px)`}" title="${esc(title)}">${esc(label)}${when ? ` <em>${when}</em>` : ""}</span>` : "");
    }).join("");
    const unused = !evs.length && layer !== "sip" && ((!webrtc && (layer === "ice" || layer === "dtls")) ? "not used" : (layer === "srtp" && !webrtc) ? (factsOf(r).sdes ? "keys from the SDP" : "not used") : "nothing yet");
    return `<div class="cx-lane"><b>${LAYER_NAME[layer]}</b><div class="cx-track">${body}${unused ? `<span class="cx-lbl m" style="left:calc(${answer ? x(answer.at) : 0}% + 7px)">${unused}</span>` : ""}</div></div>`;
  }).join("");
  // The axis: INVITE, the answer, steps after it, and "later".
  const ticks = [`<span style="left:${x(first.at)}%">${first.out ? "INVITE ↑" : "INVITE ↓"}</span>`];
  if (answer) {
    ticks.push(`<span style="left:${PRE}%">answer</span>`);
    const span = setupEnd - answer.at;
    const step = [100, 250, 500, 1000, 2000, 5000].find((s) => span / s <= 5) ?? 5000;
    for (let t = step; t < span * 0.95; t += step) ticks.push(`<span style="left:${x(answer.at + t)}%">+${secs(t)}</span>`);
    if (tEnd > setupEnd + 200) ticks.push(`<span class="later" style="left:${MAIN + 2}%">later ›</span>`);
  }
  const mediaUp = answer && (r.facts?.rtpFirstAudio ?? r.facts?.rtpFirstVideo);
  const head = answer ? `answered ${secs(answer.at - t0)} after the INVITE${mediaUp ? ` · media up ${secs(mediaUp - answer.at)} later` : ""}`
    : `not answered${r.endedAt ? ` · ended ${secs(r.endedAt - t0)} after the INVITE` : ""}`;
  return `<p class="cx-tl-head">${esc(head)}</p><div class="cx-tl">${laneHtml}<div class="cx-axis">${ticks.join("")}</div></div>`;
}

// ---- Drawing ----

function summary(r) {
  const { live, webrtc, sdes, signalling } = factsOf(r);
  const profile = [...new Set(live.map((s) => s.transport))].join(", ");
  const rtcpMux = r.rates.audio && !r.rates.audio.rtcpLocal ? "rtcp-mux" : null;
  return [webrtc ? "WebRTC" : "Plain SIP", profile ? `${profile}${sdes ? " with SDES keys" : ""}` : null, rtcpMux, signalling ? `SIP over ${signalling.split(" ")[0]}` : null].filter(Boolean).join(" · ");
}

export function renderConnection() {
  const pane = $("paneConn");
  if (!pane || pane.hidden) return;
  const r = connRec(selectedId);
  $("connEmpty").hidden = !!r;
  $("connEmpty").textContent = snap ? "No connection was saved for this call: it had no offers or answers." : "The connection shows here during a call: each layer of its media, and how it came up.";
  $("connView").hidden = !r;
  if (!r) { bodyHtml = ""; return; }
  const who = r.names.length ? r.names.join(" → ") : `Call ${r.id}`;
  $("connWho").textContent = r.endedAt ? `${who} · ended: ${r.reason ?? ""}` : who;
  $("connMeta").textContent = summary(r);
  for (const b of document.querySelectorAll("#connViews button")) b.setAttribute("aria-pressed", String(b.dataset.view === view));
  const html = view === "timeline" ? timelineHtml(r) : layersHtml(r);
  if (html !== bodyHtml) { $("connBody").innerHTML = html; bodyHtml = html; }
}

// Run by app.js at start-up.
export function initConnection() {
  const pane = $("paneConn");
  if (!pane) return;
  pane.addEventListener("toggle", (ev) => { if (ev.target.classList?.contains("cx-more")) pairsOpen = ev.target.open; }, true);
  pane.addEventListener("click", (ev) => {
    const v = ev.target.closest("[data-view]");
    if (v) { view = v.dataset.view; renderConnection(); }
  });
}
