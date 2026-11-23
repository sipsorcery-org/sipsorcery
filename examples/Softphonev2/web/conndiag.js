// The Streams and Connection tabs for the web softphone: what the desktop host keeps for them
// (StreamLog.cs and Phone.cs), kept here from the browser's side. The page's streams.js and
// connection.js draw it unchanged, from the same two messages the desktop host sends:
//  - `streams {id, exchanges, streams, sip, events, facts, wire}`: the call's offers and answers,
//    and every SIP message of it whole, for the SIP ladder, from SIP.js's transport (sipMessage);
//    and the media layers' events and facts,
//    from the page's RTCPeerConnection: its ICE and DTLS state changes, and getStats() each second
//    (the candidate pairs and the checks on each, the pair in use, our ICE role, the DTLS role and
//    SRTP profile, the first RTP of each kind, their RTCP reports).
//  - `stream-rates {rates}`: each connected call's packets a second each way, per kind.
// What a browser doesn't say, the tab goes without: why a DTLS handshake failed (beyond a
// fingerprint mismatch or an alert, where the browser reports them), and SRTP packets that
// couldn't be decrypted (the browser drops them without counting).

const SEND_SOON_MS = 150;     // one streams message for a burst of changes
const SIP_CALLS_MAX = 50;     // Call-IDs whose SIP messages are kept (a call's are looked up by it)
const SIP_PER_CALL_MAX = 300; // and messages each: a call that's up for hours re-INVITEs now and then
const WIRE_MAX = 500;         // the SIP ladder's messages a call, as the desktop (StreamLog.WireMax)
const TONES_MAX = 500;        // keypad tones a call, each way together, as the desktop (StreamLog.TonesMax)

const PAIR_STATE = { frozen: "Frozen", waiting: "Waiting", "in-progress": "InProgress", succeeded: "Succeeded", failed: "Failed" };

/**
 * opts: toPage(msg); callOf(callId) -> the host's call record or null; callIdOf(c) -> its Call-ID;
 * calls() -> the host's call records.
 */
export function connDiag({ toPage, callOf, callIdOf, calls, server }) {
  // Call-ID -> { msgs: [{ at, out, cSeq, method, status, reason, sdp }], seen: Set,
  //              wire: [the SIP ladder's], sendings: Map }
  const sip = new Map();
  let local = "";              // our side, for the ladder: the Via we send ("ws:abc123.invalid")
  const state = new WeakMap(); // call record -> { pc, facts, events, timer, rx, tx, polling }

  function st(c) {
    let s = state.get(c);
    if (!s) state.set(c, s = { pc: null, facts: {}, events: [], timer: 0, rx: {}, tx: {}, rtcp: null, dtls: null, iceUp: false, tones: [] });
    return s;
  }

  /** A keypad tone (DTMF) sent or received, for the Streams tab's timeline, as StreamLog.AddTone keeps them. */
  function tone(c, out, digit, via) {
    const s = st(c);
    if (s.tones.length < TONES_MAX) s.tones.push({ at: Date.now(), out, digit, via, held: !!(c.held || c.heldByThem) });
    changed(c);
  }
  const event = (c, layer, text, level = "info") => st(c).events.push({ at: Date.now(), layer, text, level });

  // ---- SIP: every message on the transport, by its Call-ID ----

  function parse(raw) {
    const text = typeof raw === "string" ? raw : String(raw ?? "");
    const split = text.indexOf("\r\n\r\n");
    const head = split >= 0 ? text.slice(0, split) : text;
    const body = split >= 0 ? text.slice(split + 4) : "";
    const lines = head.split("\r\n");
    const hdr = {};
    const compact = { i: "call-id", c: "content-type" };
    for (const line of lines.slice(1)) {
      const m = /^([^:\s]+)\s*:\s*(.*)$/.exec(line);
      if (!m) continue;
      const name = compact[m[1].toLowerCase()] ?? m[1].toLowerCase();
      hdr[name] ??= m[2].trim();
    }
    const cseq = /^(\d+)\s+(\S+)/.exec(hdr.cseq ?? "");
    if (!hdr["call-id"] || !cseq) return null;
    const req = /^([A-Z]+) \S+ SIP\/2\.0$/.exec(lines[0]);
    const resp = /^SIP\/2\.0 (\d{3}) ?(.*)$/.exec(lines[0]);
    if (!req && !resp) return null;
    const ctype = hdr["content-type"] ?? "";
    // For the ladder: the top Via's transport, sent-by and branch, and a request's From.
    const via = /^SIP\/2\.0\/(\S+)\s+([^;\s]+)(?:.*;branch=([^;\s]+))?/i.exec(hdr.via ?? hdr.v ?? "");
    const sdp = body.trim() && !/multipart/i.test(ctype) && (/sdp/i.test(ctype) || body.trimStart().startsWith("v=0")) ? body : null;
    return { callId: hdr["call-id"], cSeq: Number(cseq[1]), method: req ? req[1] : cseq[2].toUpperCase(),
      status: resp ? Number(resp[1]) : 0, reason: resp ? resp[2] : null, sdp,
      uri: req ? lines[0].split(" ")[1] : null, from: req ? fromOf(hdr.from ?? hdr.f ?? "") : null,
      via: via ? `${via[1].toLowerCase()}:${via[2]}` : "", branch: via?.[3] ?? "", text };
  }
  // A From as the desktop shows it: `Name <sip:user@host>`, or the bare URI, without parameters.
  function fromOf(v) {
    const m = /^\s*(?:"([^"]*)"|([^<]*?))\s*<([^>]*)>/.exec(v);
    const uri = (m ? m[3] : v.split(";")[0]).split(";")[0].trim();
    const name = (m?.[1] ?? m?.[2] ?? "").trim();
    return uri ? (name ? `${name} <${uri}>` : uri) : null;
  }

  /** A SIP message as it went over the WebSocket: out (sent) or in. */
  function sipMessage(out, raw) {
    const m = parse(raw);
    if (!m) return;
    if (out && m.via) local = m.via;
    // A call's Call-ID is first seen on its INVITE; other Call-IDs (REGISTER, OPTIONS, …) aren't
    // a call's, so they aren't kept.
    if (!sip.has(m.callId) && m.method !== "INVITE") return;
    let rec = sip.get(m.callId);
    if (!rec) {
      sip.set(m.callId, rec = { msgs: [], seen: new Set(), wire: [], sendings: new Map() });
      if (sip.size > SIP_CALLS_MAX) sip.delete(sip.keys().next().value);
    }
    wire(rec, out, m);
    // For Streams, as the desktop keeps them: the requests that make or end a call, and the
    // responses to an INVITE or UPDATE (100 Trying aside), each once.
    const key = `${out}|${m.cSeq}|${m.method}|${m.status}`;
    if ((m.status === 0 ? ["INVITE", "ACK", "UPDATE", "BYE", "CANCEL"].includes(m.method)
      : ["INVITE", "UPDATE"].includes(m.method) && m.status >= 101)
      && !rec.seen.has(key) && rec.msgs.length < SIP_PER_CALL_MAX) {
      rec.seen.add(key);
      rec.msgs.push({ at: Date.now(), out, cSeq: m.cSeq, method: m.method, status: m.status, reason: m.reason, sdp: m.sdp });
    }
    const c = callOf(m.callId);
    if (c) changed(c);
  }

  // Every message for the SIP ladder, as the desktop's StreamLog.AddWire keeps them: a
  // retransmission (the same message the same way again) is its own entry, with which sending it
  // is. Their side is the server the WebSocket goes to; ours, the Via SIP.js puts on what it sends.
  function wire(rec, out, m) {
    if (rec.wire.length >= WIRE_MAX) return;
    const label = m.status ? `${m.status} ${m.reason ?? ""}`.trim() : m.method;
    const key = `${out}|${m.cSeq}|${m.method}|${label}|${m.branch}`;
    const attempt = (rec.sendings.get(key) ?? 0) + 1;
    rec.sendings.set(key, attempt);
    rec.wire.push({ at: Date.now(), out, local: local || "browser", remote: server() ?? "server", callId: m.callId,
      cSeq: m.cSeq, cSeqMethod: m.method, label, uri: m.uri, from: m.from, response: !!m.status, attempt, text: m.text });
  }

  // ---- Offers and answers, from the SIP messages (StreamLog.Build's, in short) ----

  // Each m-line: its kind, port, transport, direction, whether it has SDES keys, and its formats
  // ("opus/48000"), in the m-line's order.
  function lines(sdp) {
    const out = [];
    let sessionDir = null, cur = null;
    for (const raw of (sdp ?? "").split(/\r?\n/)) {
      const line = raw.trim();
      if (line.startsWith("m=")) {
        const [kind, port, transport, ...pts] = line.slice(2).split(" ");
        out.push(cur = { kind: kind === "application" ? "data" : kind, port: Number(port), transport: transport ?? "", dir: null, crypto: false, pts, maps: {} });
      } else if (/^a=(sendrecv|sendonly|recvonly|inactive)$/.test(line)) {
        if (cur) cur.dir = line.slice(2); else sessionDir = line.slice(2);
      } else if (cur && line.startsWith("a=crypto:")) cur.crypto = true;
      else if (cur) {
        const m = /^a=rtpmap:(\d+) ([^/\s]+)\/(\d+)/.exec(line);
        if (m) cur.maps[m[1]] = `${m[2]}/${m[3]}`;
      }
    }
    for (const m of out) {
      m.dir ??= sessionDir ?? "sendrecv";
      m.formats = m.kind === "data" ? [] : m.pts.map((pt) => m.maps[pt] ?? pt);
    }
    return out;
  }

  // Each kind's first live m-line's direction.
  const dirsByKind = (ls) => ls.reduce((d, l) => { if (l.port && !(l.kind in d)) d[l.kind] = l.dir; return d; }, {});
  const holding = (dir) => dir === "sendonly" || dir === "inactive";
  const kindName = (k) => (k === "data" ? "text (data channel)" : k);

  function build(msgs) {
    const sipLane = msgs.map((m) => ({ at: m.at, out: m.out, text: m.status ? `${m.status} ${m.reason ?? ""}`.trim() : m.method, cSeq: m.cSeq }));
    const offerish = msgs.filter((m) => m.method === "INVITE" || m.method === "UPDATE" || (m.method === "ACK" && m.sdp));
    const exchanges = [];
    const answered = new Set();
    let last = null, prevOurs = null, prevTheirs = null;
    for (const r of offerish.filter((m) => m.status === 0 && m.method !== "ACK")) {
      const resps = offerish.filter((m) => m.status > 0 && m.cSeq === r.cSeq && m.method === r.method && m.out !== r.out);
      const final = resps.find((m) => m.status >= 200);
      if (final && (final.status === 401 || final.status === 407)) continue;   // goes again, with credentials
      const inDialog = answered.size > 0;
      const name = r.method === "INVITE" && inDialog ? "re-INVITE" : r.method;
      let offer, answer, byUs, offerMsg, answerMsg, answerAt;
      if (r.sdp) {
        const a = resps.find((m) => m.sdp && m.status < 300);
        [offer, byUs, offerMsg, answer, answerMsg, answerAt] = [r.sdp, r.out, name, a?.sdp ?? null, a ? `${a.status} ${a.reason ?? ""}`.trim() : null, a?.at];
      } else {
        // No offer in the request: it's in their 200 OK, and the answer in the ACK.
        const o = resps.find((m) => m.sdp && m.status >= 200 && m.status < 300);
        const ack = offerish.find((m) => m.method === "ACK" && m.cSeq === r.cSeq && m.out === r.out);
        [offer, byUs, offerMsg] = [o?.sdp ?? null, o ? !r.out : r.out, o ? `${name} without SDP, then ${o.status} ${o.reason ?? ""}`.trim() : name];
        [answer, answerMsg, answerAt] = [ack?.sdp ?? null, ack ? "ACK" : null, ack?.at];
      }
      const ok = !!final && final.status < 300 && !!answer;
      // What it did, as the desktop names it: the call, then what changed (a kind added or
      // dropped, a hold, a resume), or no change.
      let label;
      if (!inDialog) label = exchanges.length ? "Call again" : "Call";
      else {
        const dirs = dirsByKind(lines(offer)), before = (byUs ? prevOurs : prevTheirs) ?? {};
        const added = Object.keys(dirs).filter((k) => !(k in before)), dropped = Object.keys(before).filter((k) => !(k in dirs));
        const key = "audio" in dirs ? "audio" : Object.keys(dirs)[0];
        const holdNow = key != null && holding(dirs[key]), holdBefore = key != null && holding(before[key]);
        label = added.length ? `${byUs ? "Added" : "They added"} ${added.map(kindName).join(" and ")}`
          : dropped.length ? `${byUs ? "Dropped" : "They dropped"} ${dropped.map(kindName).join(" and ")}`
          : holdNow && !holdBefore ? (byUs ? "Hold" : "Held by them")
          : !holdNow && holdBefore ? (byUs ? "Resume" : "They resumed")
          : byUs ? `${name}, no change` : `${name} from them, no change`;
      }
      if (ok) {
        answered.add(r.callId);
        const ours = lines(byUs ? offer : answer), theirs = lines(byUs ? answer : offer);
        last = { ours, theirs, byUs };
        prevOurs = dirsByKind(ours);
        prevTheirs = dirsByKind(theirs);
      }
      const status = final ? `${final.status} ${final.reason ?? ""}`.trim() : resps.at(-1) ? `${resps.at(-1).status} ${resps.at(-1).reason ?? ""}`.trim() : null;
      exchanges.push({ n: exchanges.length + 1, at: r.at, byUs, label, offerMsg, answerMsg, status, ok,
        pending: !final, session: 1, offer, answer, answerMs: answerAt ? answerAt - r.at : null });
    }
    const streams = [];
    if (last) {
      for (let i = 0; i < Math.max(last.ours.length, last.theirs.length); i++) {
        const a = last.ours[i], b = last.theirs[i], any = a ?? b;
        const transport = b?.transport ?? any.transport;
        const security = /TLS/i.test(transport) ? (/SCTP/i.test(transport) ? "DTLS" : "DTLS-SRTP") : a?.crypto && b?.crypto ? "SRTP (SDES)" : "None";
        // The answer's formats are the ones agreed: theirs if we offered, ours if they did.
        const answerLine = last.byUs ? b : a;
        const formats = answerLine?.formats.length ? answerLine.formats : any.formats;
        streams.push({ index: i, kind: any.kind, ourPort: a?.port ?? 0, theirPort: b?.port ?? 0, transport, security,
          ours: a?.dir ?? "inactive", theirs: b?.dir ?? "inactive", formats });
      }
    }
    return { sip: sipLane, exchanges, streams };
  }

  // ---- To the page ----

  function changed(c) {
    const s = st(c);
    clearTimeout(s.timer);
    s.timer = setTimeout(() => send(c), SEND_SOON_MS);
  }
  function send(c) {
    const s = st(c);
    const rec = sip.get(callIdOf(c));
    toPage({ type: "streams", id: c.id, ...build(rec?.msgs ?? []), notes: [], events: s.events, facts: s.facts, wire: rec?.wire ?? [], tones: s.tones });
  }

  // ---- The page's connection: ICE and DTLS as they change ----

  // An address the browser hides (a peer-reflexive one it learned from a check) shows as "?".
  const addr = (x) => {
    if (!x) return "?";
    const a = x.address || x.ip || "?";
    return `${a.includes(":") ? `[${a}]` : a}:${x.port}`;
  };
  const cand = (x) => (x ? `${x.candidateType} ${String(x.protocol ?? "").toUpperCase()} ${addr(x)}` : "?");
  // Our candidates by type, each address once: before bundling the browser gathers a set for
  // each m-line, on the same addresses.
  const kinds = (list) => {
    const seen = new Set(), n = {};
    for (const x of list) {
      const key = `${x.candidateType}|${x.protocol}|${x.address || x.ip}`;
      if (seen.has(key)) continue;
      seen.add(key);
      n[x.candidateType] = (n[x.candidateType] ?? 0) + 1;
    }
    return Object.entries(n).map(([k, v]) => `${v} ${k}`).join(", ");
  };

  /** The page made a connection for the call (a new one replaces the last). */
  function watchPc(c, pc) {
    const s = st(c);
    s.pc = pc;
    s.dtls = null;
    s.iceUp = false;
    pc.addEventListener("icegatheringstatechange", async () => {
      if (s.pc !== pc || pc.iceGatheringState !== "complete") return;
      const local = await localCandidates(pc);
      s.facts.iceLocal = kinds(local);
      event(c, "ice", `gathered ${s.facts.iceLocal || "no candidates"}`);
      changed(c);
    });
    pc.addEventListener("iceconnectionstatechange", () => {
      if (s.pc !== pc) return;
      const ice = pc.iceConnectionState, f = s.facts, now = Date.now();
      if (ice === "new" || ice === "closed") return;
      f.iceState = ice;
      if (ice === "checking" || !f.iceStartedAt) { f.iceStartedAt ??= now; if (ice === "checking") event(c, "ice", "checking"); }
      if (ice === "connected" || ice === "completed") {
        if (!s.iceUp) { f.iceDoneAt = now; s.iceUp = true; event(c, "ice", "connected", "ok"); }
      } else if (ice === "failed" && s.iceUp) { f.iceLostAt = now; event(c, "ice", "lost: no answers to our checks", "bad"); }
      else if (ice === "failed") { f.iceDoneAt = now; event(c, "ice", "failed: no pair answered", "bad"); }
      else if (ice === "disconnected") event(c, "ice", "disconnected", "warn");
      poll(c);
    });
    pc.addEventListener("connectionstatechange", () => {
      if (s.pc === pc && pc.connectionState === "closed" && s.facts.dtlsState === "connected") { event(c, "dtls", "closed"); changed(c); }
    });
    changed(c);
  }

  async function localCandidates(pc) {
    const out = [];
    try { (await pc.getStats()).forEach((r) => { if (r.type === "local-candidate") out.push(r); }); } catch { }
    return out;
  }

  // The DTLS transport, once there is one: its state changes, and an error (a fingerprint that
  // doesn't match, an alert) where the browser reports one.
  function watchDtls(c, pc) {
    const s = st(c);
    const t = [...pc.getSenders(), ...pc.getReceivers()].map((x) => x.transport).find(Boolean) ?? pc.sctp?.transport;
    if (!t || s.dtls === t) return;
    s.dtls = t;
    let seen = "new";
    const onState = () => {
      // Two changes close together can both be read as the later state: each is taken once.
      if (s.dtls !== t || t.state === seen) return;
      seen = t.state;
      const f = s.facts, now = Date.now();
      if (t.state === "connecting") { f.dtlsState = "starting"; f.dtlsStartedAt = now; event(c, "dtls", "handshake"); }
      else if (t.state === "connected") {
        f.dtlsState = "connected"; f.dtlsDoneAt = now;
        f.dtlsStartedAt ??= f.iceDoneAt ?? now;
        event(c, "dtls", "connected · fingerprint matches", "ok");
      } else if (t.state === "failed") {
        f.dtlsState = "failed"; f.dtlsDoneAt = now; f.dtlsError ??= "the handshake failed";
        event(c, "dtls", `failed${f.dtlsError ? `: ${f.dtlsError}` : ""}`, "bad");
      } else if (t.state === "closed" && f.dtlsState === "connected") event(c, "dtls", "closed");
      poll(c);
    };
    t.addEventListener("statechange", onState);
    t.addEventListener("error", (e) => {
      const err = e.error, f = s.facts;
      if (err?.errorDetail === "fingerprint-failure") f.dtlsError = "their certificate doesn't match their SDP's fingerprint";
      const alert = err?.receivedAlert ?? err?.sentAlert;
      if (alert != null) f.dtlsAlert = `alert ${alert}${err.receivedAlert != null ? " from them" : " from us"}`;
      changed(c);
    });
    if (t.state !== "new") onState();
  }

  // ---- getStats(), each second (and on each ICE or DTLS change) ----

  async function poll(c) {
    const s = st(c), pc = s.pc;
    if (!pc || pc.signalingState === "closed" || s.polling) { changed(c); return; }
    s.polling = true;
    let stats;
    try { stats = await pc.getStats(); } catch { s.polling = false; return; }
    s.polling = false;
    watchDtls(c, pc);
    const f = s.facts, now = Date.now();
    const by = new Map();
    let transport = null;
    const inbound = {}, outbound = {}, reports = [];
    stats.forEach((r) => {
      by.set(r.id, r);
      if (r.type === "transport" && (r.selectedCandidatePairId || !transport)) transport = r;
      else if (r.type === "inbound-rtp" && (r.kind === "audio" || r.kind === "video")) inbound[r.kind] = r;
      else if (r.type === "outbound-rtp" && (r.kind === "audio" || r.kind === "video")) outbound[r.kind] = (outbound[r.kind] ?? 0) + (r.packetsSent ?? 0);
      else if (r.type === "remote-inbound-rtp") reports.push(r);
    });

    // ICE: the pairs, the one in use, our role.
    const pairs = [];
    stats.forEach((r) => { if (r.type === "candidate-pair") pairs.push(r); });
    if (pairs.length) {
      const selId = transport?.selectedCandidatePairId;
      f.icePairs = pairs.map((p) => ({
        ours: cand(by.get(p.localCandidateId)), theirs: cand(by.get(p.remoteCandidateId)),
        state: PAIR_STATE[p.state] ?? p.state, selected: p.id === selId, nominated: !!p.nominated,
        sent: p.requestsSent ?? 0, answers: p.responsesReceived ?? 0, received: p.requestsReceived ?? 0,
        rtt: p.currentRoundTripTime != null ? Math.round(p.currentRoundTripTime * 10000) / 10 : null,
      }));
      const peers = new Map();
      for (const p of pairs) {
        const ep = addr(by.get(p.remoteCandidateId));
        const x = peers.get(ep) ?? { ep, sent: 0, answers: 0, theirs: 0 };
        x.sent += p.requestsSent ?? 0; x.answers += p.responsesReceived ?? 0; x.theirs += p.requestsReceived ?? 0;
        peers.set(ep, x);
      }
      f.icePeers = [...peers.values()].sort((a, b) => a.ep.localeCompare(b.ep));
      const sel = pairs.find((p) => p.id === selId);
      if (sel) {
        const l = by.get(sel.localCandidateId), r = by.get(sel.remoteCandidateId);
        f.icePair = `${cand(l)} ⇄ ${cand(r)}`;
        if (sel.currentRoundTripTime != null) f.iceRtt = Math.round(sel.currentRoundTripTime * 10000) / 10;
        s.remote = addr(r);
        s.local = addr(l);
        // As the desktop puts a WebRTC line's: the protocol, and ICE's pair by type.
        s.transport = `${String(l?.protocol ?? "udp").toUpperCase()} · ICE ${l?.candidateType} ⇄ ${r?.candidateType}`;
        const ev = s.events.findLast((e) => e.layer === "ice" && e.text === "connected");
        if (ev) ev.text = `connected · ${l?.candidateType} ⇄ ${r?.candidateType}`;
      }
    }
    const local = [];
    stats.forEach((r) => { if (r.type === "local-candidate") local.push(r); });
    if (local.length) f.iceLocal = kinds(local);
    if (transport?.iceRole === "controlling" || transport?.iceRole === "controlled") f.iceControlling = transport.iceRole === "controlling";

    // DTLS: our role, and the SRTP profile it agreed.
    if (transport?.dtlsRole === "client" || transport?.dtlsRole === "server") f.dtlsRole = transport.dtlsRole;
    if (transport?.srtpCipher && f.dtlsProfile !== transport.srtpCipher) {
      f.dtlsProfile = transport.srtpCipher;
      if (f.dtlsState === "connected") event(c, "srtp", `keys from DTLS · ${f.dtlsProfile}`, "ok");
    }

    // RTP: the first of each kind from them, and the packets a second each way.
    const rates = [];
    for (const kind of ["audio", "video"]) {
      const rx = inbound[kind]?.packetsReceived ?? null, tx = outbound[kind] ?? null;
      if (rx == null && tx == null) continue;
      const key = `rtpFirst${kind[0].toUpperCase()}${kind.slice(1)}`;
      if (rx > 0 && !f[key]) { f[key] = now; event(c, "rtp", `first ${kind} ↓`, "ok"); }
      const was = s.rx[kind] ?? { n: rx ?? 0, at: now, lastRx: now }, sent = s.tx[kind] ?? tx ?? 0;
      if ((rx ?? 0) > was.n) was.lastRx = now;
      rates.push({ id: c.id, kind, tx: Math.max(0, (tx ?? 0) - sent), rx: Math.max(0, (rx ?? 0) - was.n), idle: now - was.lastRx,
        transport: s.transport ?? `ICE ${pc.iceConnectionState}`, local: s.local ?? null, remote: s.remote ?? null, from: s.remote ?? null,
        rtcpLocal: null, rtcpRemote: null });
      s.rx[kind] = { n: rx ?? 0, lastRx: was.lastRx };
      s.tx[kind] = tx ?? 0;
    }

    // RTCP: their reports on what we send (the browser timestamps each as it comes).
    for (const r of reports) {
      const seen = (s.rtcp ??= {});
      if (seen[r.id] === r.timestamp) continue;
      seen[r.id] = r.timestamp;
      if (!f.rtcpFirstAt) { f.rtcpFirstAt = now; event(c, "rtcp", "their first report ↓", "ok"); }
      f.rtcpLastAt = now;
      f.rtcpReports = (f.rtcpReports ?? 0) + 1;
      if (r.roundTripTime != null) f.rtcpRtt = Math.round(r.roundTripTime * 10000) / 10;
    }

    if (c.call === "connected" && rates.length) toPage({ type: "stream-rates", rates });
    changed(c);
  }

  setInterval(() => {
    for (const c of calls()) {
      const s = state.get(c);
      if (s?.pc && c.call !== "ended" && s.pc.connectionState !== "closed") poll(c);
    }
  }, 1000);

  return { sipMessage, watchPc, changed, tone };
}
