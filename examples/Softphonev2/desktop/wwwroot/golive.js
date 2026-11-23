// Go live: broadcast a call to a WHIP endpoint (RFC 9725), a media server others watch it from.
//
// What's broadcast is the call as its two sides have it: their audio mixed with what you send
// them, and their video beside yours (an audio call shows each side's initial and name). "What you
// send them" is the call's own senders, so muted, on hold, camera off or privacy mode are
// broadcast as they see them: silence, the hold music, your camera-off card. The mix is Web Audio
// (one track) and a <canvas> (one video track, captureStream), because WHIP takes one audio and
// one video track. They go out on a peer connection of their own, send-only, straight from this
// page to the server; the host does the HTTP (live-start: the offer POSTed, live-answer or
// live-failed back; live-stop: DELETE), so the server needn't allow this page's origin.
//
// One call at a time. It's for the call on the stage, in Focus view, once it's connected: Go
// live on the control bar opens a popover with the WHIP address and stream key (the last ones
// used are offered again; the host keeps the key) and a note that the other side will be seen and
// heard, as a call that's recorded would say. While live, the header has a red LIVE with how long,
// the call's row in Active calls says LIVE, and the popover has Stop live. The broadcast stops
// when the call ends, with Stop live, or if its connection drops.
//
// Until you've used another, the address is Broadcast Box's public server, b.siobud.com, where
// any stream key will do and anyone can watch it at b.siobud.com/<key>: so it works straight away,
// with nothing to set up. The key there is made up for you once (brave-otter-427), kept, and shown
// (it's in the link anyway), and the popover gives the link to send to whoever's watching, with
// Copy and Open, as does Live while you are. Another server: no link (we don't know where it
// shows it), and the key's hidden again.

import { entries, hostState, inGrid, nameOf, params, render, sel, selectedId } from "./app.js";
import { fmtElapsed } from "./calllist.js";
import { openMenu } from "./menus.js";
import { prefs, savePref } from "./settings.js";
import { $, ICON, initial, log, post } from "./util.js";

const WIDTH = 1280, HEIGHT = 720, FPS = 25;

// Broadcast Box's public server (github.com/Glimesh/broadcast-box): WHIP at /api/whip with the
// stream key as the Bearer token, watched at /<key>.
const SIOBUD = "https://b.siobud.com/api/whip";
const isSiobud = (url) => /^https:\/\/b\.siobud\.com\//i.test((url ?? "").trim());
const watchLink = (url, key) => (isSiobud(url) && key ? `https://b.siobud.com/${encodeURIComponent(key)}` : null);
// A key that's easy to read out and type, and unlikely to be someone else's on a shared server:
// adjective-noun-number, about a million of them. Made once, and kept (prefs.liveKey).
const ADJ = ["amber", "brave", "calm", "clever", "cosy", "crisp", "dapper", "eager", "fancy", "gentle", "glad", "golden", "happy", "jolly", "keen", "lively",
  "lucky", "mellow", "merry", "nimble", "plucky", "proud", "quick", "quiet", "rosy", "shiny", "snappy", "sunny", "swift", "tidy", "witty", "zesty"];
const NOUN = ["badger", "beacon", "comet", "cricket", "dolphin", "falcon", "ferret", "gecko", "heron", "kestrel", "koala", "lantern", "lemur", "lynx", "magpie", "marmot",
  "meadow", "narwhal", "otter", "panda", "pebble", "pelican", "puffin", "quokka", "raven", "robin", "salmon", "sparrow", "tiger", "walrus", "willow", "wombat"];
function newKey() {
  const [a, b, c] = crypto.getRandomValues(new Uint32Array(3));
  return `${ADJ[a % ADJ.length]}-${NOUN[b % NOUN.length]}-${100 + (c % 900)}`;
}
function siobudKey() {
  if (!prefs.liveKey) savePref("liveKey", newKey());
  return prefs.liveKey;
}
// The key field for the address: Broadcast Box's, filled with the key (and shown); another's, the
// made-up key taken back out (and the field hidden again).
function fitKey() {
  const url = $("liveUrl").value, key = $("liveToken");
  if (isSiobud(url)) { if (!key.value) key.value = siobudKey(); key.type = "text"; }
  else { if (key.value === prefs.liveKey) key.value = ""; key.type = "password"; }
}
// A watch link: shown, with Copy and Open (the host opens it in your browser).
function showWatch(box, link) {
  box.hidden = !link;
  if (!link) return;
  const a = box.querySelector("a");
  a.textContent = link.replace(/^https:\/\//, "");
  a.href = link;
  a.title = `Open ${link} in your browser`;
  box.querySelector("button").onclick = () => { post({ type: "copy", text: link }); log("go live: watch link copied"); };
}
const ICE_WAIT_MS = 3000;

// The broadcast, while there is one: { id (the call), url, phase: starting | live, startedAt,
// pc, mix }. A failed one is gone, its reason in failed (for the popover) until it's tried again.
let live = null;
let failed = null;      // { id, text }
let liveOpen = false;
let liveFor = null;     // the call the popover was opened on: it closes if another is selected

/** The call that's live (or going live), or null. */
export const liveId = () => live?.id ?? null;
export const isLiveCall = (id) => live?.id === id;

// Why Go live can't be used on the selected call now, or null if it can.
function blocked() {
  const e = sel();
  if (live && live.id !== selectedId) return `You're live on another call (${nameOf(entries.get(live.id))}): stop that first`;
  if (live) return null;
  if (!e || e.h.call !== "connected" || !e.pc) return "Go live, during a call";
  if (inGrid()) return "Go live: pick the call to open it in Focus";
  return null;
}

function setOpen(open) {
  if (open && blocked() && !live) return;
  if (open && !liveOpen) {
    const saved = hostState.live ?? {};
    if (!$("liveUrl").value) $("liveUrl").value = saved.url ?? SIOBUD;
    $("liveToken").value = "";
    fitKey();
    if (failed?.id !== selectedId) failed = null;
  }
  liveOpen = open;
  liveFor = open ? selectedId : null;
  render();
  if (open) (live ? $("liveStop") : $("liveUrl").value ? $("liveGo") : $("liveUrl")).focus();
  else if (document.activeElement && $("livePop").contains(document.activeElement)) $("liveBtn").focus();
}

// From render(): the button, the header's LIVE, the popover, and the end of a broadcast whose call
// has gone.
export function renderLive() {
  if (live && !live.ending) {
    const e = entries.get(live.id);
    // Stopped after this render, not in it (stopping renders again).
    if (!e || e.h.call !== "connected" || e.gone) { live.ending = true; setTimeout(() => stopLive("the call ended")); }
  }
  autoGoLive();
  const btn = $("liveBtn");
  const why = blocked();
  const here = !!live && live.id === selectedId;
  btn.disabled = !!why;
  btn.hidden = inGrid() && !here;
  btn.classList.toggle("live-on", here);
  btn.setAttribute("aria-pressed", String(here));
  btn.setAttribute("aria-expanded", String(liveOpen));
  if (!btn.innerHTML) btn.innerHTML = ICON.live;
  btn.title = why ?? (here ? (live.phase === "live" ? "You're live: see where, or stop it" : "Going live…") : liveOpen ? "Close (Esc)" : "Go live: broadcast this call to a streaming server (WHIP)");

  if (liveOpen && ((why && !here) || selectedId !== liveFor)) {
    liveOpen = false;
    if (document.activeElement && $("livePop").contains(document.activeElement)) btn.focus();
  }
  $("livePop").hidden = !liveOpen;
  renderPill();
  if (!liveOpen) return;

  const e = sel();
  const them = nameOf(e);
  $("liveTitle").textContent = here ? (live.phase === "live" ? "Live" : "Going live…") : `Go live with ${them}`;
  $("liveSetup").hidden = here;
  $("liveClose").title = here ? "Close: the stream keeps going (Esc)" : "Close (Esc)";
  $("liveOn").hidden = !here;
  if (here) {
    $("liveStatus").textContent = live.phase === "live" ? `Live for ${fmtElapsed(Date.now() - live.startedAt)}` : "Connecting to the server…";
    $("liveWhere").textContent = hostOf(live.url);
    $("liveWhere").title = live.url;
    showWatch($("liveWatchOn"), live.watch);
    return;
  }
  $("liveWhat").textContent = e?.m?.hasVideo
    ? `Broadcasts this call: ${them}'s video beside yours, and both sides' audio.`
    : `Broadcasts this call's audio, both sides, with your names on the video.`;
  $("liveConsent").textContent = `Everyone watching will see and hear ${them}. Tell them before you go live.`;
  const saved = hostState.live ?? {};
  const sameUrl = saved.url && $("liveUrl").value.trim() === saved.url;
  $("liveToken").placeholder = sameUrl && saved.hasToken ? "Saved (leave empty to use it)" : isSiobud($("liveUrl").value) ? "Any key you like" : "If the server needs one";
  showWatch($("liveWatch"), watchLink($("liveUrl").value, $("liveToken").value.trim()));
  $("liveError").hidden = !failed;
  $("liveError").textContent = failed?.text ?? "";
  $("liveGo").disabled = !/^https?:\/\/\S+$/i.test($("liveUrl").value.trim());
}

// --go-live <url> (testing): 2 s after the first call connects, Go live to it, with --live-token.
// (Read when it's used: this module is set up before app.js's own code has run.)
let testDone = false;
function autoGoLive() {
  const e = sel();
  const testUrl = params.get("golive");
  if (!testUrl || testDone || live || e?.h.call !== "connected" || !e.pc) return;
  testDone = true;
  setTimeout(() => {
    setOpen(true);
    $("liveUrl").value = testUrl;
    $("liveToken").value = params.get("livetoken") ?? "";
    goLive();
  }, 2000);
}

// The header's red LIVE (with how long), on the call that's live; a click opens the popover.
function renderPill() {
  const pill = $("livePill");
  const on = !!live && live.id === selectedId && sel()?.h.call === "connected";
  pill.hidden = !on;
  if (!on) return;
  const text = live.phase === "live" ? `LIVE ${fmtElapsed(Date.now() - live.startedAt)}` : "Going live…";
  const html = `${ICON.live}<span>${text}</span>`;
  if (pill.innerHTML !== html) pill.innerHTML = html;
  pill.classList.toggle("starting", live.phase !== "live");
  pill.title = `${live.phase === "live" ? "Live" : "Going live"} to ${hostOf(live.url)}: click to see it or stop it`;
}

const hostOf = (url) => { try { return new URL(url).host; } catch { return url; } };

// ---- The mix: the call's two sides as one audio track and one video track ----

let audioCtx = null;
function newMix(e) {
  audioCtx ??= new AudioContext();
  audioCtx.resume().catch(() => {});
  const dest = audioCtx.createMediaStreamDestination();
  const canvas = Object.assign(document.createElement("canvas"), { width: WIDTH, height: HEIGHT });
  const g = canvas.getContext("2d");
  const video = () => Object.assign(document.createElement("video"), { muted: true, playsInline: true });
  const mix = {
    dest, canvas, g, stream: new MediaStream([dest.stream.getAudioTracks()[0], canvas.captureStream(FPS).getVideoTracks()[0]]),
    audio: { them: null, me: null },   // { track, node } for each side's audio in the mix
    videos: { them: video(), me: video() },
    worker: null,
  };
  syncSources(e, mix);
  // Drawn on a worker's timer: a page timer slows to once a second while the window is minimised,
  // and so would the broadcast's video.
  const src = `setInterval(() => postMessage(0), ${Math.round(1000 / FPS)});`;
  mix.worker = new Worker(URL.createObjectURL(new Blob([src], { type: "text/javascript" })));
  let tick = 0;
  mix.worker.onmessage = () => {
    const now = entries.get(e.id);
    if (!now) return;
    if (++tick % FPS === 0) syncSources(now, mix);   // once a second: a track the call swapped (camera, hold, mute, a new connection)
    draw(now, mix);
  };
  return mix;
}

// Each side's tracks, as the call has them now: theirs from its connection's receivers, yours
// from its senders (what they get from you).
function syncSources(e, mix) {
  const recv = (kind) => e.pc?.getReceivers().find((r) => r.track?.kind === kind && r.track.readyState === "live")?.track ?? null;
  const want = {
    them: { audio: recv("audio"), video: e.m?.hasVideo ? recv("video") : null },
    me: { audio: e.m?.audioSender?.track ?? null, video: e.m?.hasVideo ? e.m?.videoSender?.track ?? null : null },
  };
  for (const side of ["them", "me"]) {
    const a = want[side].audio, cur = mix.audio[side];
    if (cur?.track !== a) {
      cur?.node.disconnect();
      mix.audio[side] = null;
      if (a) {
        const node = audioCtx.createMediaStreamSource(new MediaStream([a]));
        node.connect(mix.dest);
        mix.audio[side] = { track: a, node };
      }
    }
    const v = mix.videos[side], vt = want[side].video;
    if ((v.srcObject?.getVideoTracks()[0] ?? null) !== vt) {
      v.srcObject = vt ? new MediaStream([vt]) : null;
      if (vt) v.play().catch(() => {});
    } else if (vt && v.paused) v.play().catch(() => {});
  }
}

function draw(e, mix) {
  const { g } = mix;
  g.fillStyle = "#14161a";
  g.fillRect(0, 0, WIDTH, HEIGHT);
  const half = WIDTH / 2;
  side(g, mix.videos.them, 0, half, nameOf(e) || "Them");
  side(g, mix.videos.me, half, half, e.h.me || hostState.me || "You");
  g.fillStyle = "#000";
  g.fillRect(half - 1, 0, 2, HEIGHT);
}

// One side of the picture: its video, cropped to fill, or its initial and name; its name at the foot.
function side(g, v, x, w, name) {
  if (v.srcObject && v.readyState >= 2 && v.videoWidth) {
    const s = Math.max(w / v.videoWidth, HEIGHT / v.videoHeight);
    const sw = w / s, sh = HEIGHT / s;
    g.drawImage(v, (v.videoWidth - sw) / 2, (v.videoHeight - sh) / 2, sw, sh, x, 0, w, HEIGHT);
  } else {
    g.fillStyle = "#3a4250";
    g.beginPath();
    g.arc(x + w / 2, HEIGHT / 2 - 30, 90, 0, Math.PI * 2);
    g.fill();
    g.fillStyle = "#fff";
    g.font = "600 88px system-ui, sans-serif";
    g.textAlign = "center";
    g.textBaseline = "middle";
    g.fillText(initial(name), x + w / 2, HEIGHT / 2 - 26);
  }
  g.font = "500 30px system-ui, sans-serif";
  g.textAlign = "left";
  g.textBaseline = "middle";
  const label = name.length > 28 ? `${name.slice(0, 27)}…` : name;
  const tw = g.measureText(label).width;
  g.fillStyle = "rgba(0, 0, 0, .55)";
  g.beginPath();
  g.roundRect(x + 20, HEIGHT - 76, tw + 32, 52, 10);
  g.fill();
  g.fillStyle = "#fff";
  g.fillText(label, x + 36, HEIGHT - 50);
}

// A side's level in the mix just now (peak, 0–1), for the debug log: "none" if it has no audio.
async function level(side) {
  if (!side) return "none";
  const a = audioCtx.createAnalyser();
  a.fftSize = 8192;
  side.node.connect(a);
  const buf = new Float32Array(a.fftSize);
  let peak = 0;
  for (let i = 0; i < 8; i++) {   // over 1.2 s: speech (or a test beep) comes and goes
    await new Promise((r) => setTimeout(r, 150));
    a.getFloatTimeDomainData(buf);
    peak = buf.reduce((m, v) => Math.max(m, Math.abs(v)), peak);
  }
  try { side.node.disconnect(a); } catch { /* gone meanwhile */ }
  return peak.toFixed(3);
}

function closeMix(mix) {
  if (!mix) return;
  mix.worker?.terminate();
  for (const s of Object.values(mix.audio)) s?.node.disconnect();
  for (const v of Object.values(mix.videos)) v.srcObject = null;
  mix.stream.getTracks().forEach((t) => t.stop());
}

// ---- The broadcast's peer connection ----

// H264 first (a broadcast is for media servers, and H264 is the one they all take), then the
// browser's order. rtx and the rest stay, so the browser keeps them.
function preferH264(tr) {
  const caps = RTCRtpSender.getCapabilities?.("video")?.codecs;
  if (!caps || !tr.setCodecPreferences) return;
  const rank = (c) => /^video\/H264$/i.test(c.mimeType) && /packetization-mode=1/.test(c.sdpFmtpLine ?? "")
    ? (/profile-level-id=42e01f/i.test(c.sdpFmtpLine) ? 0 : 1) : 2;
  try { tr.setCodecPreferences(caps.map((c, i) => [c, i]).sort((a, b) => rank(a[0]) - rank(b[0]) || a[1] - b[1]).map(([c]) => c)); }
  catch (err) { log(`go live: can't put H264 first: ${err.message}`); }
}

function iceGathered(pc) {
  return new Promise((resolve) => {
    if (pc.iceGatheringState === "complete") return resolve();
    pc.addEventListener("icegatheringstatechange", () => pc.iceGatheringState === "complete" && resolve());
    setTimeout(resolve, ICE_WAIT_MS);
  });
}

async function goLive() {
  const e = sel();
  const url = $("liveUrl").value.trim();
  if (live || !e || blocked() || !url) return;
  const typed = $("liveToken").value;
  // An empty field keeps the saved token for the saved address (null); otherwise it's what's typed.
  const saved = hostState.live ?? {};
  const token = typed ? typed : saved.url === url && saved.hasToken ? null : "";
  failed = null;
  // { live: true }: the web softphone's host leaves this connection alone (no text channel, no
  // simulated loss or tones), as it's not a call.
  const pc = new RTCPeerConnection({ live: true });
  const mix = newMix(e);
  // Broadcast Box's key, typed or made up, is kept for next time (and its link).
  if (isSiobud(url) && typed.trim()) savePref("liveKey", typed.trim());
  live = { id: e.id, url, phase: "starting", startedAt: Date.now(), pc, mix, watch: watchLink(url, typed.trim()) };
  render();
  log(`call ${e.id}: going live to ${hostOf(url)}`);
  try {
    const [audio] = mix.stream.getAudioTracks(), [video] = mix.stream.getVideoTracks();
    pc.addTransceiver(audio, { direction: "sendonly", streams: [mix.stream] });
    preferH264(pc.addTransceiver(video, { direction: "sendonly", streams: [mix.stream], sendEncodings: [{ maxBitrate: 2_500_000 }] }));
    pc.onconnectionstatechange = () => {
      if (live?.pc !== pc) return;
      log(`call ${live.id}: live media ${pc.connectionState}`);
      if (pc.connectionState === "connected" && live.phase !== "live") {
        live.phase = "live";
        live.startedAt = Date.now();
        render();
        const id = live.id;
        setTimeout(async () => {
          const [them, me] = await Promise.all([level(mix.audio.them), level(mix.audio.me)]);
          if (live?.pc === pc) log(`call ${id}: live mix level: them ${them}, you ${me}`);
        }, 3000);
      } else if (pc.connectionState === "failed") fail(live.id, "The broadcast's connection to the server dropped");
    };
    await pc.setLocalDescription(await pc.createOffer());
    await iceGathered(pc);
    if (live?.pc !== pc) return;   // stopped meanwhile
    post({ type: "live-start", id: e.id, url, token, sdp: pc.localDescription.sdp });
  } catch (err) {
    fail(e.id, `Couldn't set up the broadcast: ${err.message}`);
  }
}

// The host's answer: the server took the offer.
async function onLiveAnswer(msg) {
  if (live?.id !== msg.id) return;
  try { await live.pc.setRemoteDescription({ type: "answer", sdp: msg.sdp }); }
  catch (err) { fail(msg.id, `The server's answer didn't work here: ${err.message}`); }
}

function fail(id, text) {
  log(`call ${id}: go live failed: ${text}`);
  if (live?.id === id) stopLive(null);
  failed = { id, text };
  if (selectedId === id) { liveOpen = true; liveFor = id; }   // say why, where Go live was pressed
  render();
}

/** Stops the broadcast: its connection, the mix, and the server's session (live-stop). */
export function stopLive(why) {
  if (!live) return;
  const { id, pc, mix, phase, startedAt } = live;
  live = null;
  pc.onconnectionstatechange = null;
  pc.close();
  closeMix(mix);
  post({ type: "live-stop", id });
  if (why) log(`call ${id}: live stopped (${why})${phase === "live" ? ` after ${fmtElapsed(Date.now() - startedAt)}` : ""}`);
  render();
}

export function initGoLive() {
  $("liveBtn").onclick = () => setOpen(!liveOpen);
  $("livePill").onclick = () => setOpen(true);
  $("liveClose").innerHTML = ICON.close;
  $("liveClose").onclick = () => setOpen(false);
  $("liveUrl").oninput = () => { failed = null; fitKey(); render(); };
  $("liveToken").oninput = () => render();   // the watch link follows the key
  for (const id of ["liveWatch", "liveWatchOn"]) {
    $(id).querySelector("a").onclick = (ev) => { ev.preventDefault(); post({ type: "open-url", url: ev.currentTarget.href }); };
  }
  $("livePop").onsubmit = (ev) => { ev.preventDefault(); goLive(); };
  $("liveStop").onclick = () => { stopLive("you stopped it"); setOpen(false); };
  chrome.webview.addEventListener("message", (ev) => {
    const msg = ev.data;
    if (msg?.type === "live-answer") onLiveAnswer(msg);
    else if (msg?.type === "live-failed") fail(msg.id, msg.text);
  });
  // The timer on LIVE, and in the popover.
  setInterval(() => { if (live?.phase === "live") renderLive(); }, 1000);
  // Esc or the × closes it (before the other shortcuts). A click away doesn't: copying an
  // address or key from another window clicks the softphone, and that mustn't lose the form.
  document.addEventListener("keydown", (ev) => {
    if (!liveOpen || ev.key !== "Escape" || openMenu) return;
    ev.preventDefault();
    ev.stopImmediatePropagation();
    setOpen(false);
  }, true);
}
