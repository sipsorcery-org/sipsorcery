// The web softphone's host: a stand-in, in the browser, for the desktop app's C# host. It loads
// the desktop's page (../desktop/wwwroot) unchanged, gives it the `chrome.webview` messaging it
// expects from WebView2, and does SIP with SIP.js over WebSocket.
//
// The one real difference is the media. On the desktop the page's RTCPeerConnection goes to the
// host's relay; here it goes straight to the far end. So the SDP SIP.js sends and receives is the
// page's own: a session description handler (PageMedia below) passes offers and answers between
// SIP.js and the page with the same media-* messages the desktop host uses.
//
// It registers with a SIP account (account.js): one kept in this browser, a test account created
// on the server, or one typed in to sign in. Or, with server=, it connects straight to the
// desktop app's WebSocket without registering (the desktop isn't a registrar): calls out only.
//
// Query parameters (the page's own, like camera=off or diag=1, work too):
//   server=ws://host:8081   no account: connect to the desktop app at this address, calls out only
//   user=web                with server=, our user name: we're sip:<user>@<server host>
//   name=Web                our display name
//   auto=1                  answer incoming calls after a second
//   dial=desktop            call this once registered
//   hangup=10               hang up calls this many seconds after they connect (for testing)
// and account.js's api=, captcha=, stubws= and wsdefault=.

import * as SIP from "https://cdn.jsdelivr.net/npm/sip.js@0.21.2/+esm";
import { loadAccount, saveAccount, forgetAccount, accountUi, addressOf } from "./account.js";

const q = new URLSearchParams(location.search);
const localServer = q.get("server");
const hostOf = (ws) => new URL(ws.replace(/^ws/, "http")).hostname;
// The account in use: { username, password, domain, wsUri, guest }. noRegister: connect only
// (server=, and the stub's test accounts), so calls out but none in.
let acct = localServer ? { username: q.get("user") || "web", password: null, domain: hostOf(localServer), wsUri: localServer, noRegister: true }
  : loadAccount();
const displayName = () => q.get("name") || acct?.username || "";
const aor = () => (acct ? `sip:${acct.username}@${acct.domain}` : null);
let ui = null;   // the account chip and dialog, once the page's markup is in

// ---- The page's chrome.webview, and the messages to and from it ----

const listeners = [];
const waiting = new Map();   // "type:call id" -> resolve, for the media replies we wait on
let pageReady = false;

function toPage(msg) {
  const data = JSON.parse(JSON.stringify(msg));
  setTimeout(() => listeners.forEach((fn) => fn({ data })), 0);
}

function fromPage(type, id) {
  return new Promise((resolve) => waiting.set(`${type}:${id}`, resolve));
}

window.chrome ??= {};
window.chrome.webview = {
  postMessage: (msg) => setTimeout(() => onPageMessage(msg), 0),
  addEventListener: (type, fn) => type === "message" && listeners.push(fn),
  removeEventListener: (type, fn) => { const i = listeners.indexOf(fn); if (i >= 0) listeners.splice(i, 1); },
};

function log(text) {
  const line = `${new Date().toTimeString().slice(0, 8)}.${String(new Date().getMilliseconds()).padStart(3, "0")} ${text}`;
  console.log(line);
  if (pageReady) toPage({ type: "log", text: line });
  else earlyLog.push(line);
}
const earlyLog = [];

// ---- Call state, as the desktop host reports it ----
//
// Several calls at once, as on the desktop: each a record in `calls` with the SIP.js session
// and the page's RTCPeerConnection for it. Messages about one call carry its id. Which call has
// the microphone and speaker is the page's business. Hold is a re-INVITE with our media
// sendonly, offered by the page on the call's own connection (see PageMedia).

const MAX_CALLS = 8;
const ENDED_KEEP_MS = 4000;   // an ended call stays in the list this long, so the page can say how it ended
const regState = { state: "none", detail: null };
const calls = new Map();      // id -> call record, see newCall
let nextId = 0;

function newCall(fields) {
  const c = {
    id: String(++nextId), session: null, call: "idle", remote: null, reason: null, outgoing: false,
    wantVideo: false, incomingVideo: false, theirPhone: null, held: false, heldByThem: false,
    mediaUp: false,            // the first offer and answer are done: later ones are re-INVITEs
    pc: null,                  // the page's RTCPeerConnection for the call
    text: { state: "off", timer: 0 },
    quality: { pc: null, reportSeen: {}, videoIn: null },
    ...fields,
  };
  calls.set(c.id, c);
  return c;
}
const liveCalls = () => [...calls.values()].filter((c) => ["calling", "ringing", "connected"].includes(c.call));

function stateMessage() {
  return {
    type: "state",
    calls: [...calls.values()].map((c) => ({
      id: c.id, call: c.call, remote: c.remote, reason: c.reason, outgoing: c.outgoing,
      incomingVideo: c.incomingVideo, held: c.held, heldByThem: c.heldByThem, holdBusy: !!c.holdBusy, holdMode: HOLD_MODE,
      // Real-time text on a T.140 data channel (see below): off, adding (opening), on or refused.
      text: c.call === "connected" ? c.text.state : "off",
    })),
    maxCalls: MAX_CALLS,
    // The page knows registered, registering and failed; connected without registering shows as registered.
    registration: !acct ? "none" : regState.state === "connected" ? "registered" : regState.state,
    registrationDetail: regState.detail,
    account: acct ? `${acct.username}@${acct.domain}` : null, address: acct ? addressOf(acct) : null, me: displayName(),
    videoAllowed: true,
    lossAudio: loss.audio, lossVideo: loss.video,
  };
}

function setCall(c, phase, reason = null) {
  c.call = phase;
  if (phase === "calling" || phase === "ringing" || phase === "ended") resetText(c);
  c.reason = phase === "ended" ? reason : null;
  log(`call ${c.id} ${phase} ${c.remote ?? ""} ${c.reason ?? ""}`.trimEnd());
  if (phase === "connected") { c.mediaUp = true; updateText(c); }
  sendState();
}

function end(c, reason) {
  if (!c || c.call === "idle" || c.call === "ended") return;
  c.held = c.heldByThem = false;
  setCall(c, "ended", reason);
  setTimeout(() => { calls.delete(c.id); sendState(); }, ENDED_KEEP_MS);
}

function sendState() { if (pageReady) toPage(stateMessage()); }

function nameOf(identity) {
  const uri = identity.uri.toString();
  return identity.displayName ? `${identity.displayName} <${uri}>` : uri;
}

// The audio line of an SDP says the far end has put us on hold: sendonly or inactive.
function holdsUs(sdp) {
  const audio = sdp?.split(/\r?\n(?=m=)/).find((s) => /^m=audio [1-9]/.test(s));
  return !!audio && /^a=(sendonly|inactive)\s*$/m.test(audio);
}

// ---- The page's media as SIP.js's session description handler ----
// One per session. Once the call is up (mediaUp), offers and answers are re-INVITEs: the page
// makes or answers them on the call's existing connection (renegotiate) instead of a new one.

class PageMedia {
  constructor(session) {
    this.session = session;
    this.theirOffer = null;    // an offer from the far end, for the page to answer
    this.ourOffer = false;     // the page made an offer and waits for the far end's answer
  }

  get rec() { return this.session.callRec; }

  hasDescription(contentType) { return contentType === "application/sdp"; }

  async setDescription(sdp) {
    const c = this.rec;
    if (this.ourOffer) {
      this.ourOffer = false;
      toPage({ type: "media-remote-answer", id: c.id, sdp });
    } else {
      this.theirOffer = sdp;
      if (c.mediaUp) {
        const held = holdsUs(sdp);
        if (held !== c.heldByThem) { c.heldByThem = held; log(`call ${c.id}: they ${held ? "put the call on hold" : "took the call off hold"}`); sendState(); }
      }
    }
  }

  async getDescription() {
    const c = this.rec;
    let sdp;
    if (this.theirOffer) {
      const offer = this.theirOffer;
      this.theirOffer = null;
      const reply = fromPage("media-answer", c.id);
      toPage({ type: "media-offer", id: c.id, sdp: offer, renegotiate: c.mediaUp });
      sdp = withDcmap(c, await reply);
    } else {
      const reply = fromPage("media-offer-made", c.id);
      toPage({ type: "media-request", id: c.id, video: c.wantVideo, renegotiate: c.mediaUp });
      sdp = withDcmap(c, await reply);
      this.ourOffer = true;
    }
    return { body: sdp, contentType: "application/sdp" };
  }

  async rollbackDescription() { this.ourOffer = false; this.theirOffer = null; }
  sendDtmf() { return false; }
  close() {}
}

// ---- Real-time text: a T.140 data channel (RFC 8865) on the page's connection ----
//
// On the desktop the host does text itself (an m=text stream on plain RTP calls, a data channel
// on WebRTC ones), and the page sends and gets it as `text` messages. Here the page's connection
// goes to the far end, so the channel is on it: the wrapper below adds one to every offer the
// page makes, or to the answer to an offer that has one. It's a negotiated channel (no in-band
// open) on the stream the SDP's dcmap gives (RFC 8864), which browsers don't write: withDcmap
// adds it to what the page hands SIP.js. Messages are the T.140 text as strings: letters,
// backspaces and line separators, as they're typed. Each call has its own, on its own connection.

const T140 = "t140";
const T140_OFFER_STREAM = 1;   // any will do: both ends open it from the SDP

// The stream of the T.140 channel on an SDP's active m=application line, or null.
function findT140(sdp) {
  if (!sdp) return null;
  for (const section of sdp.split(/\r?\n(?=m=)/)) {
    if (!/^m=application [1-9]/.test(section)) continue;
    for (const m of section.matchAll(/^a=dcmap:(\d+) (.*?)\r?$/gm)) {
      if (/(^|;)\s*subprotocol="t140"/i.test(m[2])) return Number(m[1]);
    }
  }
  return null;
}

// The page's SDP with the call's T.140 channel's dcmap on its m=application line (if it has one).
function withDcmap(c, sdp) {
  const dc = c.pc?.t140;
  if (!sdp || !dc?.negotiated || findT140(sdp) != null) return sdp;
  const eol = sdp.includes("\r\n") ? "\r\n" : "\n";
  const lines = sdp.split(/\r?\n/);
  const start = lines.findIndex((l) => /^m=application [1-9]/.test(l));
  if (start < 0) return sdp;
  let end = lines.findIndex((l, i) => i > start && l.startsWith("m="));
  if (end < 0) end = lines.length;
  while (end > start + 1 && lines[end - 1] === "") end--;
  lines.splice(end, 0, `a=dcmap:${dc.id} label="${T140}";subprotocol="${T140}"`);
  return lines.join(eol);
}

const callOfPc = (pc) => [...calls.values()].find((c) => c.pc === pc) ?? null;

function useT140(pc, dc) {
  pc.t140 = dc;
  dc.onopen = () => { const c = callOfPc(pc); if (c) updateText(c); };
  dc.onclose = () => { const c = callOfPc(pc); if (c?.text.state === "on") log(`call ${c.id}: the T.140 data channel closed`); };
  dc.onmessage = (e) => {
    const c = callOfPc(pc);
    if (c?.call !== "connected") return;
    const chars = typeof e.data === "string" ? e.data : new TextDecoder().decode(e.data);
    log(`call ${c.id}: real-time text in: ${chars.length} characters`);
    toPage({ type: "text", id: c.id, text: chars });
  };
}

function setText(c, state, why) {
  if (c.text.state === state) return;
  c.text.state = state;
  if (state !== "adding") { clearTimeout(c.text.timer); c.text.timer = 0; }
  if (why) log(`call ${c.id}: ${why}`);
  sendState();
}

// Moves text on as the call does: their SDP agrees or not, the channel opens, or it doesn't.
function updateText(c) {
  if (c.call !== "connected" || c.text.state === "on" || c.text.state === "refused") return;
  const pc = c.pc, dc = pc?.t140;
  if (!dc) return setText(c, "refused", "they didn't offer text (no T.140 data channel in their SDP)");
  if (pc.remoteDescription && !pc.t140Agreed) return setText(c, "refused", "they didn't take text (no T.140 data channel in their SDP)");
  if (pc.t140Agreed && dc.readyState === "open") return setText(c, "on", `real-time text on (RFC 8865, T.140 data channel on stream ${dc.id})`);
  if (c.text.state !== "adding") {
    setText(c, "adding");
    c.text.timer = setTimeout(() => {
      if (c.text.state === "adding") setText(c, "refused", "the T.140 data channel didn't open: no text on this call");
    }, 10000);
  }
}

function resetText(c) {
  clearTimeout(c.text.timer);
  c.text.timer = 0;
  c.text.state = "off";
}

function sendText(c, chars) {
  const dc = c?.pc?.t140;
  if (c?.text.state === "on" && dc?.readyState === "open") dc.send(chars);
}

// ---- Call quality: their RTCP reports, from the browser's stats ----
//
// On the desktop the host reads the far end's RTCP on the SIP leg and posts it as `quality`
// messages (how they receive what we send). Here the page's own RTCPeerConnection is the media
// leg, and the browser already reads their reports: remote-inbound-rtp in getStats(). So the host
// finds the page's connection for each call (the page makes it with `new RTCPeerConnection({ callId })`,
// wrapped below before app.js loads) and posts the same messages from those stats.

const NativeRTCPeerConnection = window.RTCPeerConnection;
window.RTCPeerConnection = class extends NativeRTCPeerConnection {
  constructor(config, ...rest) {
    super(config, ...rest);
    // The call this connection is for: the page says, and a renegotiated call gets a new one.
    const c = calls.get(String(config?.callId));
    if (c) c.pc = this;
    this.t140 = null;          // the T.140 data channel, once there is one
    this.t140Agreed = false;   // their SDP has it too (or they opened it in band)
    // A far end that opens its T.140 channel in band (DCEP) rather than in the SDP.
    this.addEventListener("datachannel", (e) => {
      if (this.t140 || e.channel.protocol.toLowerCase() !== T140) return;
      useT140(this, e.channel);
      this.t140Agreed = true;
      const rec = callOfPc(this);
      if (rec) updateText(rec);
    });
  }

  // Our offer: a T.140 channel goes in it, so the browser writes an m=application line.
  async createOffer(...args) {
    if (!this.t140) useT140(this, this.createDataChannel(T140, { negotiated: true, id: T140_OFFER_STREAM, protocol: T140 }));
    return super.createOffer(...args);
  }

  // Each sender gets the simulated loss transform as it's made (see below): calling out, by
  // addTransceiver; answering, by the far end's offer.
  addTransceiver(...args) {
    const t = super.addTransceiver(...args);
    addLossTransform(t);
    return t;
  }

  async setRemoteDescription(...args) {
    // Their offer's T.140 channel, on the stream it says, before the answer is made.
    const desc = args[0];
    const theirs = findT140(desc?.sdp);
    if (desc?.type === "offer" && !this.t140 && theirs != null) {
      useT140(this, this.createDataChannel(T140, { negotiated: true, id: theirs, protocol: T140 }));
    }
    await super.setRemoteDescription(...args);
    this.getTransceivers().forEach(addLossTransform);
    if (this.t140?.negotiated) this.t140Agreed = theirs != null && theirs === this.t140.id;
    const rec = callOfPc(this);
    if (rec) updateText(rec);
  }
};

const round1 = (n) => Math.round(n * 10) / 10;

setInterval(() => {
  for (const c of calls.values()) if (c.call === "connected" && c.pc && c.pc.connectionState !== "closed") reportQuality(c);
}, 1000);

async function reportQuality(c) {
  const pc = c.pc, q = c.quality;
  if (pc !== q.pc) { q.pc = pc; q.reportSeen = {}; q.videoIn = null; }
  let stats;
  try { stats = await pc.getStats(); } catch { return; }
  stats.forEach((r) => {
    // Their latest reception report on each stream we send: one message per new report.
    if (r.type === "remote-inbound-rtp" && (r.kind === "audio" || r.kind === "video")) {
      if (q.reportSeen[r.id] === r.timestamp) return;
      q.reportSeen[r.id] = r.timestamp;
      toPage({
        type: "quality", id: c.id, dir: "sent", kind: r.kind,
        lossPct: round1((r.fractionLost ?? 0) * 100),
        lost: Math.max(0, r.packetsLost ?? 0),
        jitterMs: round1((r.jitter ?? 0) * 1000),
        rttMs: r.roundTripTime != null ? round1(r.roundTripTime * 1000) : null,
      });
    }
    // Their video as we receive it, every 5 s (the desktop host sends its own RTCP report on it).
    if (r.type === "inbound-rtp" && r.kind === "video") {
      const now = { t: r.timestamp, received: r.packetsReceived ?? 0, lost: Math.max(0, r.packetsLost ?? 0) };
      if (q.videoIn && now.t - q.videoIn.t < 5000) return;
      if (q.videoIn) {
        const got = now.received - q.videoIn.received, lost = Math.max(0, now.lost - q.videoIn.lost);
        toPage({
          type: "quality", id: c.id, dir: "received", kind: "video",
          lossPct: got + lost > 0 ? round1((lost * 100) / (got + lost)) : 0,
          lost: now.lost, jitterMs: round1((r.jitter ?? 0) * 1000), rttMs: null,
        });
      }
      q.videoIn = now;
    }
  });
}

// ---- Simulated loss (the Diagnostics menu's Network tab) ----
//
// The desktop host drops RTP packets on the SIP leg. A page can't reach RTP packets, so here an
// encoded transform on each of the page connection's senders drops that share of the encoded
// frames, at random, before the browser packetises them. Audio frames are a packet each, so the
// far end hears the same gaps; a dropped video frame leaves the far end's decoder without a
// reference, so it freezes and asks for a keyframe, as after a lost packet. What differs is that
// no sequence numbers go missing, so their RTCP reports show no loss.
//
// Chrome only passes frames through a transform that's on the sender before the call's media is
// negotiated: one set later gets none, and the media goes round it. So every sender gets one as
// it's made (the RTCPeerConnection wrapper above), passing every frame until loss is turned on.

const loss = { audio: 0, video: 0 };
const lossSupported = typeof window.RTCRtpScriptTransform === "function";
let lossWorker = null;
const lossSenders = new WeakSet();   // senders with the transform on

// The worker does the dropping: a transform per sender, rates set by message, counts posted back.
// (Its rtctransform event comes to addEventListener; Chrome doesn't fire it at an onrtctransform.)
const LOSS_WORKER = `
const rate = { audio: 0, video: 0 };
const count = { audio: { sent: 0, dropped: 0 }, video: { sent: 0, dropped: 0 } };
self.addEventListener("message", (e) => Object.assign(rate, e.data));
self.addEventListener("rtctransform", (e) => {
  const { readable, writable, options } = e.transformer;
  const kind = options.kind;
  readable.pipeThrough(new TransformStream({
    transform(frame, out) {
      if (Math.random() * 100 < rate[kind]) count[kind].dropped++;
      else { count[kind].sent++; out.enqueue(frame); }
    },
  })).pipeTo(writable).catch(() => {});
});
setInterval(() => {
  if (count.audio.dropped + count.video.dropped === 0) return;
  postMessage(count);
  for (const c of Object.values(count)) { c.sent = 0; c.dropped = 0; }
}, 5000);
`;

function addLossTransform(t) {
  if (!lossSupported || lossSenders.has(t.sender)) return;
  const kind = t.receiver.track?.kind;
  if (kind !== "audio" && kind !== "video") return;
  lossSenders.add(t.sender);
  if (!lossWorker) {
    lossWorker = new Worker(URL.createObjectURL(new Blob([LOSS_WORKER], { type: "text/javascript" })));
    lossWorker.onmessage = ({ data: c }) =>
      log(`simulated loss: dropped ${c.audio.dropped} of ${c.audio.dropped + c.audio.sent} audio frames, ${c.video.dropped} of ${c.video.dropped + c.video.sent} video frames in 5 s`);
    lossWorker.postMessage(loss);
  }
  try {
    t.sender.transform = new RTCRtpScriptTransform(lossWorker, { kind });
  } catch (err) {
    log(`simulated loss: can't drop ${kind} on this call: ${err.message}`);
  }
}

function setLoss(audio, video) {
  loss.audio = Math.max(0, Math.min(100, Number(audio) || 0));
  loss.video = Math.max(0, Math.min(100, Number(video) || 0));
  if (!lossSupported) {
    if (loss.audio || loss.video) log("simulated loss needs encoded transforms (RTCRtpScriptTransform), which this browser doesn't have");
    return;
  }
  lossWorker?.postMessage(loss);
  log(`simulated loss: audio ${loss.audio}%, video ${loss.video}% of the frames we send`);
}

// ---- SIP ----

let ua = null;
let registerer = null;

function setReg(state, detail = null) {
  regState.state = state;
  regState.detail = detail;
  ui?.set(acct, { state, detail });
  sendState();
}

// Registers with an account (or, for noRegister, just connects). Resolves once registered;
// rejects with a message to show if the server can't be reached or turns us down.
async function startSip(a) {
  await stopSip();
  acct = a;
  const myUa = new SIP.UserAgent({
    uri: SIP.UserAgent.makeURI(`sip:${a.username}@${a.domain}`),
    displayName: displayName(),
    authorizationUsername: a.password ? a.username : undefined,
    authorizationPassword: a.password ?? undefined,
    transportOptions: { server: a.wsUri },
    sessionDescriptionHandlerFactory: (session) => new PageMedia(session),
    // No automatic 180: onIncoming checks the offer first, so a call we can't take is turned
    // down (488) before it rings at either end.
    sendInitialProvisionalResponse: false,
    userAgentString: "SIPSorcery web softphone (SIP.js 0.21.2)",
    logLevel: "warn",
    delegate: {
      onInvite: (invitation) => onIncoming(invitation),
      onConnect: () => log(`connected to ${a.wsUri}`),
      onDisconnect: (err) => {
        if (ua !== myUa) return;
        log(`disconnected from ${a.wsUri}${err ? `: ${err.message}` : ""}`);
        setReg("failed", "Lost the connection to the server");
      },
    },
  });
  ua = myUa;
  if (a.noRegister) {
    setReg("registering");
    try {
      await myUa.start();
    } catch {
      const msg = `Couldn't reach ${a.wsUri}.`;
      if (ua === myUa) setReg("failed", msg);
      throw new Error(msg);
    }
    setReg("connected");
    log(`connected to ${a.wsUri} as ${aor()} without registering: calls out only`);
    dialAtStart();
    return;
  }
  const reg = new SIP.Registerer(myUa, { expires: 600 });
  registerer = reg;
  setReg("registering");
  log(`registering ${aor()} via ${a.wsUri}`);
  reg.stateChange.addListener((s) => {
    if (registerer !== reg) return;
    if (s === SIP.RegistererState.Registered) setReg("registered");
    log(`registration ${s.toLowerCase()} as ${aor()}`);
    dialAtStart();
  });

  try {
    await myUa.start();
  } catch (err) {
    const msg = `Couldn't reach ${a.wsUri}.`;
    if (ua === myUa) setReg("failed", msg);
    throw new Error(msg);
  }
  return new Promise((resolve, reject) => {
    const fail = (msg) => { clearTimeout(timer); if (ua === myUa) setReg("failed", msg); reject(new Error(msg)); };
    const timer = setTimeout(() => fail("The server didn't answer."), 10000);
    reg.stateChange.addListener((s) => { if (s === SIP.RegistererState.Registered) { clearTimeout(timer); resolve(); } });
    reg.register({
      requestDelegate: {
        onReject: (resp) => {
          const m = resp.message;
          fail(m.statusCode === 401 || m.statusCode === 403 || m.statusCode === 407 ? "Wrong user name or password."
            : `The server said ${m.statusCode} ${m.reasonPhrase}.`);
        },
      },
    }).catch((err) => fail(err.message));
  });
}

async function stopSip() {
  const [oldUa, oldReg] = [ua, registerer];
  if (!oldUa) return;
  for (const c of liveCalls()) hangup(c);
  ua = registerer = null;
  try { if (oldReg?.state === SIP.RegistererState.Registered) await oldReg.unregister(); } catch { }
  try { await oldUa.stop(); } catch { }
}

async function signIn(a) {
  // A new test account is kept straight away: its password exists nowhere else yet.
  if (a.guest) saveAccount(a);
  try {
    await startSip(a);
    saveAccount(a);
  } catch (err) {
    if (!a.guest) { await stopSip(); acct = null; setReg("none"); }
    throw err;
  }
}

async function signOut() {
  log(`signing out of ${aor()}`);
  await stopSip();
  forgetAccount();
  acct = null;
  setReg("none");
  ui.showSignIn();
}

// dial= places its call once we're registered (or connected) and the page is up, whichever comes last.
let dialledAtStart = false;
function dialAtStart() {
  if (dialledAtStart || !q.get("dial") || !pageReady || !["registered", "connected"].includes(regState.state)) return;
  dialledAtStart = true;
  dial(q.get("dial"), q.get("call") === "video");
}

function watch(c) {
  const s = c.session;
  s.callRec = c;
  s.stateChange.addListener((state) => {
    if (state === SIP.SessionState.Established) {
      setCall(c, "connected");
      const after = Number(q.get("hangup"));
      if (after > 0) setTimeout(() => hangup(c), after * 1000);
    } else if (state === SIP.SessionState.Terminated) {
      // A call we're placing that's turned down: SIP.js ends the session before it hands dial()'s
      // onReject the response, so wait a tick for that to give the reason ("486 Busy Here").
      if (c.outgoing && c.call === "calling") {
        setTimeout(() => end(c, "Call failed"), 0);
        return;
      }
      // Whoever ended it set the reason first (end() is a no-op the second time); this is them.
      end(c, c.call === "ringing" ? "Missed call" : "They hung up");
    }
  });
}

function target(address) {
  address = address.trim();
  if (/^sips?:/i.test(address)) return address;
  return address.includes("@") ? `sip:${address}` : `sip:${address}@${acct.domain}`;
}

// A new call, alongside any others (the page holds the one you were on).
async function dial(address, video) {
  if (!ua) { ui.showSignIn(); return; }
  if (liveCalls().length >= MAX_CALLS) { log(`not calling ${address}: already ${MAX_CALLS} calls`); return; }
  const uri = SIP.UserAgent.makeURI(target(address));
  if (!uri) { const c = newCall({ remote: address, outgoing: true, call: "calling" }); end(c, "Not a SIP address"); return; }
  const inviter = new SIP.Inviter(ua, uri);
  const c = newCall({ session: inviter, outgoing: true, wantVideo: video, remote: uri.toString() });
  watch(c);
  setCall(c, "calling");
  try {
    await inviter.invite({
      requestDelegate: {
        onAccept: (resp) => { c.theirPhone = resp.message.getHeader("Server") ?? resp.message.getHeader("User-Agent"); sendDetails(c); },
        onReject: (resp) => end(c, `${resp.message.statusCode} ${resp.message.reasonPhrase}`),
      },
    });
  } catch (err) {
    end(c, `Call failed: ${err.message}`);
  }
}

// The offer is a WebRTC one: some active m-line is DTLS-SRTP (UDP/TLS/RTP/SAVPF from browsers,
// UDP/TLS/RTP/SAVP from SIPSorcery). No offer (the INVITE had no SDP) is fine: ours goes in the 200.
function isWebRtcOffer(sdp) {
  return !sdp || /^m=\w+ [1-9]\d*(\/\d+)? [A-Z/]*TLS\/RTP\/SAVPF?/m.test(sdp);
}

// What we can take, for a 488: a receive-only offer from a throwaway RTCPeerConnection, which
// lists the browser's real codecs on UDP/TLS/RTP/SAVPF lines without touching the camera or mic.
// Made once (the first takes about half a second) and kept.
let capabilitiesSdp = null;
function capabilities() {
  capabilitiesSdp ??= (async () => {
    const pc = new NativeRTCPeerConnection();   // not the page's, so not watched for quality
    try {
      pc.addTransceiver("audio", { direction: "recvonly" });
      pc.addTransceiver("video", { direction: "recvonly" });
      return (await pc.createOffer()).sdp;
    } finally {
      pc.close();
    }
  })();
  capabilitiesSdp.catch(() => { capabilitiesSdp = null; });
  return capabilitiesSdp;
}

// A plain RTP offer (an ordinary SIP phone's) can't be answered from a browser. Turned down with
// 488 before ringing, with Warning 302 "Incompatible transport protocol" (RFC 3261 20.43: the
// m-line's profile, not the codecs) and an SDP of what we can take (21.4.26), so the caller can
// place the call again with a WebRTC offer.
async function rejectPlainRtp(invitation) {
  let body;
  try { body = { body: await capabilities(), contentType: "application/sdp" }; } catch { }
  const warning = `Warning: 302 ${location.hostname || "web-softphone"} "Incompatible transport protocol: WebRTC media only"`;
  log(`turned down a call from ${nameOf(invitation.remoteIdentity)}: its offer isn't WebRTC (488, Warning 302)`);
  await invitation.reject({ statusCode: 488, extraHeaders: [warning], body }).catch(() => {});
}

// An incoming call rings alongside any others; only one past MAX_CALLS is busy.
function onIncoming(invitation) {
  if (liveCalls().length >= MAX_CALLS) {
    invitation.reject({ statusCode: 486 });
    return;
  }
  if (!isWebRtcOffer(invitation.body)) {
    rejectPlainRtp(invitation);
    return;
  }
  invitation.progress().catch(() => {});   // 180 Ringing, now we know we can take it
  const c = newCall({
    session: invitation, outgoing: false,
    theirPhone: invitation.request.getHeader("User-Agent"),
    remote: nameOf(invitation.remoteIdentity),
    incomingVideo: /^m=video [1-9]/m.test(invitation.body ?? ""),
  });
  watch(c);
  setCall(c, "ringing");
  if (q.get("auto") === "1") setTimeout(() => answer(c), 1000);
}

async function answer(c) {
  if (!(c?.session instanceof SIP.Invitation) || c.call !== "ringing") return;
  try {
    await c.session.accept();
    sendDetails(c);
  } catch (err) {
    c.session?.reject().catch(() => {});
    end(c, `Couldn't answer: ${err.message}`);
  }
}

function decline(c) {
  if (!(c?.session instanceof SIP.Invitation) || c.call !== "ringing") return;
  end(c, "Declined");
  c.session.reject({ statusCode: 603 }).catch(() => {});
}

function hangup(c) {
  const s = c?.session;
  if (!s) return;
  if (c.call === "ringing") return decline(c);
  const was = c.call;
  end(c, "You hung up");
  if (was === "calling") s.cancel?.().catch(() => {});
  else if (was === "connected") s.bye().catch(() => {});
}

// Hold or resume: a re-INVITE whose offer the page makes on the call's connection (sendonly to
// hold, sendrecv to resume; see PageMedia and the page's renegotiateMedia). One at a time per
// call, as the desktop host does: switching calls quickly (hold, resume, hold) would otherwise
// start a second while the first is out, which SIP.js refuses and the far end would answer 491.
// A change asked for meanwhile waits, and only the last one counts.
// How calls are held: music on hold (the page sends music, nothing is signalled, media keeps
// flowing both ways), as the desktop's accounts do by default; ?hold=sip for the re-INVITE.
// The web softphone's guest accounts are on a plain server, not a PBX that needs to see it.
const HOLD_MODE = q.get("hold") === "sip" ? "sip" : "music";
const HOLD_ANSWER_MS = 5000;   // give up waiting for an answer after this
const HOLD_SETTLE_MS = 250;    // after one, so it's ACKed before the next goes
async function hold(c, on) {
  if (c?.call !== "connected") return;
  if (HOLD_MODE !== "sip") {
    if (c.held === on) return;
    c.held = c.holdWanted = on;
    log(`call ${c.id}: ${on ? "holding" : "resuming"}: music on hold, nothing signalled`);
    sendState();
    return;
  }
  c.holdWanted = on;
  if (c.holdBusy) { log(`call ${c.id}: ${on ? "hold" : "resume"}: after the re-INVITE that's out`); return; }
  while (c.call === "connected" && c.held !== c.holdWanted && c.session?.state === SIP.SessionState.Established) {
    const want = c.holdWanted;
    c.holdBusy = true;
    sendState();
    log(`call ${c.id}: ${want ? "holding" : "resuming"}: re-INVITE`);
    // Turned down or failed: what the far end has is what counts, so stop there.
    const giveUp = (why) => { log(`call ${c.id}: ${want ? "hold" : "resume"} ${why}`); c.holdWanted = c.held; };
    await new Promise((resolve) => {
      const timer = setTimeout(() => { giveUp("got no answer"); resolve(); }, HOLD_ANSWER_MS);
      const done = (fn) => (arg) => { clearTimeout(timer); fn(arg); resolve(); };
      c.session.invite({
        requestDelegate: {
          onAccept: done(() => { c.held = want; }),
          onReject: done((resp) => giveUp(`turned down: ${resp.message.statusCode} ${resp.message.reasonPhrase}`)),
        },
      }).catch(done((err) => giveUp(`failed: ${err.message}`)));
    });
    await new Promise((r) => setTimeout(r, HOLD_SETTLE_MS));
    c.holdBusy = false;
  }
  c.holdBusy = false;
  sendState();
}

// What the info bar's Call details shows (diagnostics mode).
// As the desktop host gives it: "WSS sipsorcery.com:443", "WS localhost:8091".
function signallingOf(wsUri) {
  try {
    const u = new URL(wsUri);
    const proto = u.protocol.replace(":", "");
    return `${proto.toUpperCase()} ${u.hostname}:${u.port || (proto === "wss" ? 443 : 80)}`;
  } catch { return null; }
}

function sendDetails(c) {
  const s = c?.session;
  if (!s) return;
  toPage({
    type: "details",
    id: c.id,
    direction: c.outgoing ? "outgoing" : "incoming",
    account: acct ? `${acct.username}@${acct.domain}` : null,
    signalling: signallingOf(acct?.wsUri),
    theirPhone: c.theirPhone,
    callId: s.dialog?.callId ?? s.request?.callId ?? null,
    theirMedia: null,
    mediaEncrypted: true,
    signallingEncrypted: !!acct?.wsUri.startsWith("wss:"),
    audio: null,
    video: null,
  });
}

// ---- Messages from the page ----

async function onPageMessage(msg) {
  const key = `${msg.type}:${msg.id}`;
  const resolve = waiting.get(key);
  if (resolve) { waiting.delete(key); resolve(msg.sdp); return; }
  const c = msg.id != null ? calls.get(String(msg.id)) : null;
  switch (msg.type) {
    case "ready":
      pageReady = true;
      earlyLog.splice(0).forEach((text) => toPage({ type: "log", text }));
      log(acct ? `web softphone: ${aor()} via ${acct.wsUri}` : "web softphone: not signed in");
      toPage(stateMessage());
      dialAtStart();
      break;
    case "log":
      console.log(`page: ${msg.text}`);
      break;
    case "dial": await dial(msg.address, !!msg.video); break;
    case "answer": await answer(c); break;
    case "decline": decline(c); break;
    case "hangup": hangup(c); break;
    case "hold": await hold(c, msg.hold !== false); break;
    case "add-video":
      log("adding video isn't done in the web softphone yet");
      toPage({ type: "video-refused", id: msg.id });
      break;
    case "copy":
      navigator.clipboard?.writeText(msg.text).catch(() => {});
      break;
    case "loss":
      setLoss(msg.audio, msg.video);
      break;
    case "text":
      sendText(c, msg.text);
      break;
    case "add-text":
      // Text goes in every call's SDP; there's no adding it to one that doesn't have it.
      break;
  }
}

// ---- Start: the desktop's markup and styles, then SIP, then the page's script ----

const base = document.baseURI;
const doc = new DOMParser().parseFromString(await (await fetch(new URL("index.html", base))).text(), "text/html");
for (const link of doc.head.querySelectorAll("link[rel=stylesheet]")) document.head.append(link);
for (const node of [...doc.body.childNodes]) if (node.nodeName !== "SCRIPT") document.body.append(document.adoptNode(node));
document.title = "SIPSorcery Softphone (web)";
// For the page's Settings: what's the desktop's (its account management) and what's ours.
document.documentElement.dataset.host = "web";

// The Network tab's note is the desktop's, about packets on the SIP leg: say what loss does here.
const lossNote = document.querySelector(".loss-note");
if (lossNote) {
  if (lossSupported) {
    const bold = document.createElement("strong");
    bold.textContent = "But no packets go missing, so audio and video loss in Call quality stay at 0% on both ends.";
    lossNote.replaceChildren("Drops that share of the audio or video frames you send, at random, before the browser packetises them. The far end hears the gaps, and its video freezes and asks for keyframes (PLIs). ", bold);
  } else {
    lossNote.textContent = "Simulated loss needs encoded transforms (RTCRtpScriptTransform), which this browser doesn't have.";
  }
}
if (!lossSupported) {
  for (const id of ["lossAudio", "lossVideo"]) { const el = document.getElementById(id); if (el) el.disabled = true; }
}

ui = accountUi({ signIn, signOut });
// Leaving or reloading the page ends a call properly (BYE, CANCEL or decline) rather than just
// dropping its media, then unregisters.
window.addEventListener("beforeunload", () => {
  for (const c of liveCalls()) hangup(c);
  registerer?.unregister().catch(() => {});
});

await import(new URL("app.js", base).href);

// A kept account registers straight away; with none, the sign-in dialog.
if (acct) startSip(acct).catch((err) => log(`registration failed: ${err.message}`));
else ui.showSignIn();
capabilities().catch(() => {});   // made now, ready for the first 488
