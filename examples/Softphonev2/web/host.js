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
import { loadAccount, saveAccount, forgetAccount, accountUi } from "./account.js";

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
const waiting = new Map();   // message type -> resolve, for the media replies we wait on
let pageReady = false;

function toPage(msg) {
  const data = JSON.parse(JSON.stringify(msg));
  setTimeout(() => listeners.forEach((fn) => fn({ data })), 0);
}

function fromPage(type) {
  return new Promise((resolve) => waiting.set(type, resolve));
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

const st = {
  call: "idle", remote: null, reason: null,
  registration: "none", registrationDetail: null,
  incomingVideo: false,
};
let session = null;        // the SIP.js Inviter or Invitation
let outgoing = false;
let wantVideo = false;     // video on the call we're placing
let theirPhone = null;

function stateMessage() {
  return {
    type: "state",
    call: st.call, remote: st.remote, reason: st.reason,
    // The page knows registered, registering and failed; connected without registering shows as registered.
    registration: !acct ? "none" : st.registration === "connected" ? "registered" : st.registration,
    registrationDetail: st.registrationDetail,
    account: acct ? `${acct.username}@${acct.domain}` : null, address: aor(), me: displayName(),
    videoAllowed: true, incomingVideo: st.incomingVideo,
    // Real-time text needs a data channel on WebRTC calls: not done yet.
    text: st.call === "connected" ? "unavailable" : "off",
    lossAudio: 0, lossVideo: 0,
  };
}

function setCall(call, reason = null) {
  st.call = call;
  st.reason = call === "ended" ? reason : null;
  log(`call ${call} ${st.remote ?? ""} ${st.reason ?? ""}`.trimEnd());
  if (pageReady) toPage(stateMessage());
}

function end(reason) {
  if (st.call === "idle" || st.call === "ended") return;
  session = null;
  setCall("ended", reason);
}

function sendState() { if (pageReady) toPage(stateMessage()); }

function nameOf(identity) {
  const uri = identity.uri.toString();
  return identity.displayName ? `${identity.displayName} <${uri}>` : uri;
}

// ---- The page's media as SIP.js's session description handler ----

class PageMedia {
  constructor() {
    this.theirOffer = null;    // an offer from the far end, for the page to answer
    this.ourOffer = false;     // the page made an offer and waits for the far end's answer
  }

  hasDescription(contentType) { return contentType === "application/sdp"; }

  async setDescription(sdp) {
    if (this.ourOffer) {
      this.ourOffer = false;
      toPage({ type: "media-remote-answer", sdp });
    } else {
      this.theirOffer = sdp;
    }
  }

  async getDescription() {
    let sdp;
    if (this.theirOffer) {
      const offer = this.theirOffer;
      this.theirOffer = null;
      const reply = fromPage("media-answer");
      toPage({ type: "media-offer", sdp: offer });
      sdp = await reply;
    } else {
      const reply = fromPage("media-offer-made");
      toPage({ type: "media-request", video: wantVideo });
      sdp = await reply;
      this.ourOffer = true;
    }
    return { body: sdp, contentType: "application/sdp" };
  }

  async rollbackDescription() { this.ourOffer = false; this.theirOffer = null; }
  sendDtmf() { return false; }
  close() {}
}

// ---- Call quality: their RTCP reports, from the browser's stats ----
//
// On the desktop the host reads the far end's RTCP on the SIP leg and posts it as `quality`
// messages (how they receive what we send). Here the page's own RTCPeerConnection is the media
// leg, and the browser already reads their reports: remote-inbound-rtp in getStats(). So the host
// finds the page's connection (the page makes it with `new RTCPeerConnection`, wrapped below
// before app.js loads) and posts the same messages from those stats.

const NativeRTCPeerConnection = window.RTCPeerConnection;
let pagePc = null;
window.RTCPeerConnection = class extends NativeRTCPeerConnection {
  constructor(...args) {
    super(...args);
    pagePc = this;
  }
};

const round1 = (n) => Math.round(n * 10) / 10;
let qualityPc = null;   // the connection the state below is for
let reportSeen = {};    // remote-inbound-rtp id -> timestamp of the report last posted
let videoIn = null;     // { t, received, lost } at the last "received" video post

setInterval(async () => {
  const pc = pagePc;
  if (st.call !== "connected" || !pc || pc.connectionState === "closed") return;
  if (pc !== qualityPc) { qualityPc = pc; reportSeen = {}; videoIn = null; }
  let stats;
  try { stats = await pc.getStats(); } catch { return; }
  stats.forEach((r) => {
    // Their latest reception report on each stream we send: one message per new report.
    if (r.type === "remote-inbound-rtp" && (r.kind === "audio" || r.kind === "video")) {
      if (reportSeen[r.id] === r.timestamp) return;
      reportSeen[r.id] = r.timestamp;
      toPage({
        type: "quality", dir: "sent", kind: r.kind,
        lossPct: round1((r.fractionLost ?? 0) * 100),
        lost: Math.max(0, r.packetsLost ?? 0),
        jitterMs: round1((r.jitter ?? 0) * 1000),
        rttMs: r.roundTripTime != null ? round1(r.roundTripTime * 1000) : null,
      });
    }
    // Their video as we receive it, every 5 s (the desktop host sends its own RTCP report on it).
    if (r.type === "inbound-rtp" && r.kind === "video") {
      const now = { t: r.timestamp, received: r.packetsReceived ?? 0, lost: Math.max(0, r.packetsLost ?? 0) };
      if (videoIn && now.t - videoIn.t < 5000) return;
      if (videoIn) {
        const got = now.received - videoIn.received, lost = Math.max(0, now.lost - videoIn.lost);
        toPage({
          type: "quality", dir: "received", kind: "video",
          lossPct: got + lost > 0 ? round1((lost * 100) / (got + lost)) : 0,
          lost: now.lost, jitterMs: round1((r.jitter ?? 0) * 1000), rttMs: null,
        });
      }
      videoIn = now;
    }
  });
}, 1000);

// ---- SIP ----

let ua = null;
let registerer = null;

function setReg(state, detail = null) {
  st.registration = state;
  st.registrationDetail = detail;
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
    sessionDescriptionHandlerFactory: () => new PageMedia(),
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
  if (session) hangup();
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
  if (dialledAtStart || !q.get("dial") || !pageReady || !["registered", "connected"].includes(st.registration)) return;
  dialledAtStart = true;
  dial(q.get("dial"), q.get("call") === "video");
}

function watch(s) {
  s.stateChange.addListener((state) => {
    if (state === SIP.SessionState.Established) {
      setCall("connected");
      const after = Number(q.get("hangup"));
      if (after > 0) setTimeout(() => session === s && hangup(), after * 1000);
    } else if (state === SIP.SessionState.Terminated && session === s) {
      // A call we're placing that's turned down: SIP.js ends the session before it hands dial()'s
      // onReject the response, so wait a tick for that to give the reason ("486 Busy Here").
      if (outgoing && st.call === "calling") {
        setTimeout(() => { if (session === s) end("Call failed"); }, 0);
        return;
      }
      // Whoever ended it set the reason first (end() is a no-op the second time); this is them.
      end(st.call === "ringing" ? "Missed call" : "They hung up");
    }
  });
}

function target(address) {
  address = address.trim();
  if (/^sips?:/i.test(address)) return address;
  return address.includes("@") ? `sip:${address}` : `sip:${address}@${acct.domain}`;
}

async function dial(address, video) {
  if (session) return;
  if (!ua) { ui.showSignIn(); return; }
  const uri = SIP.UserAgent.makeURI(target(address));
  if (!uri) { st.remote = address; st.call = "calling"; end("Not a SIP address"); return; }
  outgoing = true;
  wantVideo = video;
  theirPhone = null;
  st.remote = uri.toString();
  st.incomingVideo = false;
  const inviter = new SIP.Inviter(ua, uri);
  session = inviter;
  watch(inviter);
  setCall("calling");
  try {
    await inviter.invite({
      requestDelegate: {
        onAccept: (resp) => { theirPhone = resp.message.getHeader("Server") ?? resp.message.getHeader("User-Agent"); sendDetails(); },
        onReject: (resp) => end(`${resp.message.statusCode} ${resp.message.reasonPhrase}`),
      },
    });
  } catch (err) {
    end(`Call failed: ${err.message}`);
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

function onIncoming(invitation) {
  if (session) {
    invitation.reject({ statusCode: 486 });
    return;
  }
  if (!isWebRtcOffer(invitation.body)) {
    rejectPlainRtp(invitation);
    return;
  }
  invitation.progress().catch(() => {});   // 180 Ringing, now we know we can take it
  outgoing = false;
  session = invitation;
  theirPhone = invitation.request.getHeader("User-Agent");
  st.remote = nameOf(invitation.remoteIdentity);
  st.incomingVideo = /^m=video [1-9]/m.test(invitation.body ?? "");
  watch(invitation);
  setCall("ringing");
  if (q.get("auto") === "1") setTimeout(() => answer(), 1000);
}

async function answer() {
  if (!(session instanceof SIP.Invitation) || st.call !== "ringing") return;
  try {
    await session.accept();
    sendDetails();
  } catch (err) {
    session?.reject().catch(() => {});
    end(`Couldn't answer: ${err.message}`);
  }
}

function decline() {
  if (!(session instanceof SIP.Invitation) || st.call !== "ringing") return;
  const s = session;
  end("Declined");
  s.reject({ statusCode: 603 }).catch(() => {});
}

function hangup() {
  const s = session;
  if (!s) return;
  if (st.call === "ringing") return decline();
  const was = st.call;
  end("You hung up");
  if (was === "calling") s.cancel?.().catch(() => {});
  else s.bye().catch(() => {});
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

function sendDetails() {
  const s = session;
  if (!s) return;
  toPage({
    type: "details",
    direction: outgoing ? "outgoing" : "incoming",
    account: acct ? `${acct.username}@${acct.domain}` : null,
    signalling: signallingOf(acct?.wsUri),
    theirPhone,
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
  const resolve = waiting.get(msg.type);
  if (resolve) { waiting.delete(msg.type); resolve(msg.sdp); return; }
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
    case "answer": await answer(); break;
    case "decline": decline(); break;
    case "hangup": hangup(); break;
    case "add-video":
      log("adding video isn't done in the web softphone yet");
      toPage({ type: "video-refused" });
      break;
    case "copy":
      navigator.clipboard?.writeText(msg.text).catch(() => {});
      break;
    case "loss":
    case "text":
    case "add-text":
      break;
  }
}

// ---- Start: the desktop's markup and styles, then SIP, then the page's script ----

const base = document.baseURI;
const doc = new DOMParser().parseFromString(await (await fetch(new URL("index.html", base))).text(), "text/html");
for (const link of doc.head.querySelectorAll("link[rel=stylesheet]")) document.head.append(link);
for (const node of [...doc.body.childNodes]) if (node.nodeName !== "SCRIPT") document.body.append(document.adoptNode(node));
document.title = "SIPSorcery Softphone (web)";

ui = accountUi({ signIn, signOut });
window.addEventListener("beforeunload", () => { registerer?.unregister().catch(() => {}); });

await import(new URL("app.js", base).href);

// A kept account registers straight away; with none, the sign-in dialog.
if (acct) startSip(acct).catch((err) => log(`registration failed: ${err.message}`));
else ui.showSignIn();
capabilities().catch(() => {});   // made now, ready for the first 488
