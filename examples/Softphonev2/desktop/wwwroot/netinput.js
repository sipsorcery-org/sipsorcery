// Network input: video and sound from ffmpeg (-f whip), OBS or any WHIP publisher on this PC,
// used by a call in place of your camera and microphone. The host listens (NetInput.cs), takes
// the publisher's connection itself and relays its media here, offered as a call's is; this page
// answers on a receive-only connection, and what arrives is one more source: "Network input" on
// the ▾ menus of Camera and Mute.
//  - It's one source, not two devices: picked from either menu, a feed with sound becomes your
//    camera and your microphone together, and a notice says so, with Keep my microphone to take
//    the sound back. Picking a camera (or a microphone) afterwards takes only that half back.
//  - What callers get shows where your devices' does: the self view shows the feed, and the mic
//    indicator and the call's row say they hear Network input, not you.
//  - When the publisher stops (or its connection drops), whatever was using it goes back to your
//    camera and microphone, and a notice says so: a dead feed never silently mutes you.
//  - A publisher that starts while you're on a call, and not in use: a notice offers Use it.
//  - Mute, camera off, hold and privacy mode work on it as they do on your devices.
// Desktop only: a browser page can't listen for connections. Settings turns it on and shows the
// address and stream key to give the publisher.

import { entries, hostState, localTrack, nextVideo, privacyOn, pushOutcome, dropOutcome, render, sel, settings, syncAudio } from "./app.js";
import { analyserFor } from "./levels.js";
import { ICON, log, post } from "./util.js";
import { setVideoSource, syncCamera } from "./video.js";

export const NET_LABEL = "Network input";

let feed = null;           // { session, pc, video, audio, connected }
let netAudioOn = false;    // the feed's sound is your microphone
let lastSource = "camera"; // the video source to go back to when the feed goes

export const netState = () => hostState.netInput ?? null;   // null: the web build, which has none
export const netEnabled = () => !!netState()?.on;
const live = (t) => (t?.readyState === "live" ? t : null);
/** The feed's video, while a publisher is connected and sending it. */
export const netVideo = () => (feed?.connected ? live(feed.video) : null);
/** The feed's sound, likewise. */
export const netAudio = () => (feed?.connected ? live(feed.audio) : null);
/** A publisher's connected (with something to use). */
export const netReady = () => !!(netVideo() || netAudio());
/** "video and sound", "video only", "sound only". */
export const netKinds = () => (netVideo() && netAudio() ? "video and sound" : netVideo() ? "video only" : netAudio() ? "sound only" : "");
/** The feed's sound is what calls are sent as your microphone. */
export const netAudioInUse = () => netAudioOn && !!netAudio() && !privacyOn();
/** What calls send as your microphone: the feed's sound, or your microphone. Never anything in privacy mode. */
export const micToSend = () => (privacyOn() ? null : netAudioInUse() ? netAudio() : localTrack("audio"));
/** Something is using the feed: a call, the next call, or its sound. */
const inUse = () => netAudioOn || nextVideo.source === "net" || [...entries.values()].some((e) => e.m && e.source === "net");

// The audio view's bars and level meters on your side follow what's sent.
function followMic() {
  const track = micToSend();
  for (const e of entries.values()) {
    if (!e.m) continue;
    e.m.localAnalyser?.source.disconnect();
    e.m.localAnalyser = track ? analyserFor(track) : null;
  }
}

function setNetAudio(on) {
  if (netAudioOn === on) return;
  netAudioOn = on;
  log(on ? "microphone: network input's sound" : "microphone: yours again");
  syncAudio();
  followMic();
  render();
}

/** Picked from the Camera or Mute ▾ (or Your devices): the feed's video, and its sound if it has any. */
export function useNet(e = sel()) {
  if (!netReady()) return;
  if (netVideo()) {
    if (nextVideo.source !== "net") lastSource = nextVideo.source;
    setVideoSource("net", e);
  }
  const both = !!netVideo() && !!netAudio();
  if (netAudio()) setNetAudio(true);
  if (e?.m) {
    pushOutcome({
      key: "net-use", icon: ICON.video, who: NET_LABEL, tone: "info",
      what: both ? "Callers now see and hear it" : netVideo() ? "Callers now see it" : "Callers now hear it",
      action: netAudio() && both ? { label: "Keep my microphone", title: "Send its video, with your microphone", fn: () => { dropOutcome("net-use"); setNetAudio(false); } } : null,
    });
  }
  render();
}

/** Your microphone picked: the feed's sound is no longer sent. */
export function stopNetAudio() { setNetAudio(false); }

// The feed has gone: everything using it goes back to your devices.
function feedGone(why) {
  const was = feed;
  feed = null;
  if (was) { try { was.pc.close(); } catch { /* closed */ } }
  dropOutcome("net-started");
  const used = inUse();
  netAudioOn = false;
  const back = lastSource === "net" ? "camera" : lastSource;
  for (const e of entries.values()) if (e.m && e.source === "net") e.source = back;
  if (nextVideo.source === "net") nextVideo.source = back;
  log(`network input stopped (${why})${used ? `: back to your ${back === "camera" ? "camera" : back} and microphone` : ""}`);
  syncCamera();
  syncAudio();
  followMic();
  if (used && [...entries.values()].some((e) => e.m && e.h.call === "connected")) {
    pushOutcome({ key: "net-use", icon: ICON.video, who: NET_LABEL, tone: "warn", what: "Stopped: back to your camera and microphone" });
  }
  settings?.refresh();
  render();
}

function iceGathered(pc) {
  return new Promise((resolve) => {
    if (pc.iceGatheringState === "complete") return resolve();
    pc.addEventListener("icegatheringstatechange", () => pc.iceGatheringState === "complete" && resolve());
    setTimeout(resolve, 2000);
  });
}

// A publisher's media, from the host: the host ends the publisher's connection itself (NetInput.cs)
// and relays what arrives, offering it here as it does a call's media. Answered receive-only.
async function onOffer(session, sdp) {
  if (feed) feedGone("a new publisher");
  const pc = new RTCPeerConnection();
  const f = { session, pc, video: null, audio: null, connected: false };
  feed = f;
  pc.ontrack = (ev) => {
    if (ev.track.kind === "video") f.video = ev.track;
    else if (ev.track.kind === "audio") f.audio = ev.track;
  };
  pc.onconnectionstatechange = () => {
    if (feed !== f) return;
    log(`network input media ${pc.connectionState}`);
    if (pc.connectionState === "connected" && !f.connected) {
      f.connected = true;
      log(`network input: ${netKinds() || "nothing yet"}`);
      // On a call, not in use: offered.
      if (!inUse() && [...entries.values()].some((e) => e.m && e.h.call === "connected")) {
        pushOutcome({
          key: "net-started", icon: ICON.video, who: NET_LABEL, tone: "info", what: `Started: ${netKinds()}`,
          action: { label: "Use it", title: "Send it on this call in place of your camera and microphone", fn: () => { dropOutcome("net-started"); useNet(); } },
        });
      }
      syncCamera();
      settings?.refresh();
      render();
    } else if (pc.connectionState === "failed" || pc.connectionState === "closed") {
      post({ type: "net-closed", session });
      feedGone(`its connection ${pc.connectionState}`);
    }
  };
  try {
    await pc.setRemoteDescription({ type: "offer", sdp });
    for (const tr of pc.getTransceivers()) tr.direction = "recvonly";
    await pc.setLocalDescription(await pc.createAnswer());
    await iceGathered(pc);
    if (feed !== f) return;
    post({ type: "net-answer", session, sdp: pc.localDescription.sdp });
  } catch (err) {
    log(`network input: couldn't take the publisher's media: ${err.message}`);
    post({ type: "net-refused", session, text: `The softphone couldn't take this stream: ${err.message}` });
    if (feed === f) feedGone("its offer couldn't be answered");
  }
}

// What the Settings section shows, from the host's state.
export function netStatusText() {
  const s = netState();
  if (!s?.on) return "";
  if (!s.address) return `Not listening: ${s.error ?? "it couldn't start"}`;
  if (feed?.connected) return `Receiving ${netKinds() || "nothing yet"}${s.from ? ` from ${s.from}` : ""}${inUse() ? ", in use" : ""}`;
  if (s.session) return "A publisher is connecting…";
  return "Waiting for a publisher";
}

// An ffmpeg command line that publishes a test pattern and tone, for Settings to copy.
export function ffmpegExample() {
  const s = netState();
  if (!s?.address) return "";
  return `ffmpeg -re -f lavfi -i testsrc2=size=1280x720:rate=30 -f lavfi -i sine=frequency=440 `
    + `-c:v libx264 -profile:v baseline -tune zerolatency -g 30 -pix_fmt yuv420p -c:a libopus -ar 48000 -ac 2 `
    + `-f whip -authorization ${s.token} ${s.address}`;
}

export function initNetInput() {
  if (!window.chrome?.webview) return;   // the web build: no host to listen
  chrome.webview.addEventListener("message", (ev) => {
    const msg = ev.data;
    if (msg?.type === "net-offer") onOffer(msg.session, msg.sdp);
    else if (msg?.type === "net-ended" && feed?.session === msg.session) feedGone("the publisher stopped");
    else if (msg?.type === "state") {
      // Turned off, or the host dropped the publisher (a new key): the connection goes too.
      const s = msg.netInput;
      if (feed && (!s?.on || (s.session && s.session !== feed.session))) feedGone(!s?.on ? "network input turned off" : "a new publisher");
      settings?.refresh();
    }
  });
  // A track that ends on its own (the publisher's encoder stopped) is the feed going too.
  setInterval(() => {
    if (feed?.connected && !netReady()) { post({ type: "net-closed", session: feed.session }); feedGone("its tracks ended"); }
  }, 1000);
}
