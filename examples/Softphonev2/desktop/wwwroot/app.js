// The call page. The browser engine does all the media: it captures the microphone and camera,
// and plays the far end. The host (C#) handles SIP and relays the media, still encoded, between
// the SIP call and the local RTCPeerConnection created here when a call connects.
//
// What the stage shows during a call, from the UX prototype (prototype/web):
//  - their video, while frames are arriving;
//  - their initial, if video was agreed but nothing is arriving ("No video arriving from X" after
//    2 s; another softphone sends a placeholder while its camera is off, see placeholderTrack, so
//    this means the video has stalled or the far end stopped sending);
//  - an audio-only view, if the call has no video at all: two rows of frequency bars, theirs
//    above the centre line and yours below, with notes when audio isn't flowing one way.
// Call and Video call beside the address place the kind of call; a call ringing in has Answer
// (your camera stays closed, but you see them) and Answer with video. An audio call has no video
// line; "Add video" asks the far end for one (a SIP re-INVITE), and the host then sends a new offer.
// With no call, the stage is Home (home.js), and while you place one, the dial view; Your devices
// (yourdevices.js) is on both and under a call ringing in.
//
// Several calls at once, as in the prototype: each call the host reports has an entry here (see
// entries), with its own peer connection, <video> element, quality, details and text. The stage
// shows the selected call. One live call at a time, like a desk phone: the call you're talking
// to has the microphone and speaker (audioId), and every other call you're on is held. How it's
// held is the account's Hold setting (holdMode): music on hold, the default, sends them music
// (and on a video call an "On hold" card) with nothing signalled, so media flows both ways and
// no NAT binding closes; SIP hold sends a re-INVITE and silence. A call that isn't the live one
// never gets the microphone: it gets music or silence, never nothing (see quietAudio). Picking a held call (its row, its tile, its number key,
// or Resume) resumes it and holds the live one. Call and Answer hold the live call when you press
// them, not before: typing an address, or looking at a ringing call, History or Accounts, holds
// nothing. When a call ends, nothing resumes on its own: a held call stays held, with Resume.
// An incoming call while you're on one shows as a banner, with Answer & hold current or Decline.

import { initAccounts, onAccountsState, onAccountMessage, closeAccounts, useAccount, accountName, accountList, takeAccounts } from "./accounts.js";
import { initHistory, onHistoryMessage, onHistoryState, closeHistory, historyEntries, markSeen, openPeople, renderSide } from "./history.js";
import { prefs, savePref, initSettings } from "./settings.js";
import { initSelfView } from "./selfview.js";
import { normAddress, contactMark, contactMarkEl } from "./contactmark.js";
import { avatarAttr, avatarImage, setAvatar } from "./avatar.js";
import { initContacts, openContacts, onContacts, allContacts, contactFor, saveContact, removeContact, sameUri, mountContacts, contactCount } from "./contacts.js";
import { addressHint, addressTip, dial, fieldText, fieldUri, initAddress, initAddress2, shownAddress } from "./address.js";
import { callStatus, fmtElapsed, initCallList, renderCallTimes, renderCalls, sendsHtml } from "./calllist.js";
import { initDiagnostics, renderInfoLock, setDiagnostics } from "./diagnostics.js";
import { holdAudio, quietAudio, stopHoldMusic } from "./holdmusic.js";
import { initKeypad, keypadOpen, onDtmfIn, renderKeypad, setKeypadOpen } from "./keypad.js";
import { analyserFor, drawViz, initLevels, startTiles } from "./levels.js";
import { closeMenu, initMenus, openMenu, pickDevice, pickMic, renderMore, selfViewOn, showLoss } from "./menus.js";
import { closeDevices, initYourDevices, renderIndicator, renderYourDevices, stopTests } from "./yourdevices.js";
import { dialDone, homeShown, initHome, renderHome } from "./home.js";
import { initQuality, newQuality, onQuality, renderDetails, renderDock, renderQuality } from "./quality.js";
import { closeTextForGrid, hideToast, initRtt, onTextReceived, renderText, renderTextControls, setTextOpen, textOpen, toastCallId } from "./rtt.js";
import { initTransfer, renderTransfer, transferShort, xferBusy } from "./transfer.js";
import { $, ICON, initial, log, post, shortName } from "./util.js";
import { cameraInUse, hasCamera, isTestTrack, openSources, placeholderTrack, retryCamera, sameSources, setHasCamera, showSelfVideo, sourceAvailable, sourceOf, stopPlaceholders, syncCamera, videoToSend, wantedSources } from "./video.js";

const CAMERA_KBPS = 1500;
const ENDED_CARD_MS = 3000;   // how long a call's "Call ended" card stays before Home, with no other call
const NO_FRAMES_MS = 2000;   // how long without video frames before saying their camera is off
export const NO_AUDIO_MS = 2000;    // how long without audio packets before saying none is arriving

export let local = null;          // our tracks: the microphone during a call, the camera while it's wanted
export let hostState = { calls: [] };   // the host's last state message
// The host's state with the selected call's fields on top (call, remote, reason, incomingVideo,
// text, held, heldByThem), as the page had it when there was one call. See deriveState.
export let state = { call: "idle" };
export let pc = null;             // the selected call's peer connection
export let call = null;           // the selected call's media state, see onMediaOffer
// Answer (false) or Answer with video (true), for the call being answered; null: a call you
// placed, which starts as nextVideo says.
export let answerCamera = null;

// ---- The calls ----
// Every call the host reports, by id: { id, h (the host's view of it), m (its media state, once
// connected), pc, el (its <video>), connectedAt, heldAt, quality, details, codecs, text,
// addingVideo, videoRefused, gone (the host has dropped it, but it's still on the stage) }.
export const entries = new Map();
export let selectedId = null;     // the call on the stage
let detailsShown = "";     // the selected call and its state Call details were last drawn for
export let audioId = null;        // the call with the microphone and speaker
export const sel = () => entries.get(selectedId) ?? null;
export const isLive = (e) => ["calling", "ringing", "connected"].includes(e?.h.call);
export const liveEntries = () => [...entries.values()].filter(isLive);
// Newest first, as the Active calls list shows them (and 1–8 pick them).
// Oldest first: a call keeps its place in the list while others come and go, and new ones go at
// the bottom, so where a call is stays where you remember it.
// A call you're placing while on another isn't in it until it's answered (see onNewCall).
export const listed = () => [...entries.values()].filter((e) => !e.gone && !e.pending).sort((a, b) => Number(a.id) - Number(b.id));
export const nameOf = (e) => shortName(e?.h.remote);

function newEntry(id) {
  const el = Object.assign(document.createElement("video"), { className: "remote", autoplay: true, playsInline: true, muted: true });
  el.dataset.call = id;
  if (devices.speaker) el.setSinkId?.(devices.speaker).catch(() => {});
  $("stage").insertBefore(el, $("card"));
  return {
    id, h: { id, call: "idle" }, m: null, pc: null, el, connectedAt: 0, heldAt: 0,
    quality: null, details: null, codecs: null, lastStats: null,
    text: { lines: [], unread: false, bomSent: false, said: false },
    addingVideo: false, videoRefused: false, gone: false,
  };
}

// Ended calls dropped here while the host still lists them (it keeps an ended call a little while):
// not new calls, so they don't come back as one, with their "Call ended" card over Home. Each id
// is forgotten once the host stops listing it.
const dropped = new Set();
function dropEntry(e) {
  if (e.h.call === "ended") dropped.add(e.id);
  closeMedia(e);
  e.el.remove();
  entries.delete(e.id);
  if (audioId === e.id) audioId = null;
}

function deriveState() {
  const e = sel();
  const h = e?.h ?? {};
  state = {
    ...hostState,
    call: e ? h.call : "idle", remote: h.remote ?? "", reason: h.reason ?? null,
    incomingVideo: !!h.incomingVideo, text: h.text ?? "off", held: !!h.held, heldByThem: !!h.heldByThem,
  };
  call = e?.m ?? null;
  pc = e?.pc ?? null;
}

// Shows a call on the stage. A connected call that isn't on hold gets the audio (see syncAudio).
export function selectCall(id) {
  if (id === selectedId || !entries.has(id)) return;
  const old = sel();
  selectedId = id;
  if (old?.gone) dropEntry(old);
  if (textOpen) sel().text.unread = false;
  closeMenu();
  hideToast();
  log(`selected call ${id} (${nameOf(sel())})`);
  renderText();
  render();
}

// The call you're talking to: connected and not on hold, nor being put on hold. (Normally the
// audio call; while a switch is under way, possibly two.)
export const holdState = (e) => e.holdWanted ?? !!e.h.held;
const talking = () => [...entries.values()].filter((e) => e.h.call === "connected" && !holdState(e));
const liveCall = () => talking().find((e) => e.id === audioId) ?? talking()[0] ?? null;

// Holds every call you're talking to but `keep`: before Call or Answer, and when switching calls.
function holdOthers(keep) {
  for (const e of talking()) if (e.id !== keep?.id) holdCall(e, true);
}
// Makes a connected call the one you're talking to: the live one goes on hold, and this one comes
// off hold if it's on it. Anything else (ringing, calling, ended) is only looked at.
function goLive(e) {
  if (e?.h.call !== "connected") return;
  holdOthers(e);
  holdCall(e, false);
}

// The most recent live call other than the selected one, for "Back to …".
const otherLive = () => listed().findLast((e) => e.id !== selectedId && isLive(e)) ?? null;

// ---- Focus and grid views ----
// Focus (the default) shows the selected call, with its controls. Grid shows every active call as
// an equal tile with no buttons (but Resume on a held call's), so it's never in doubt which call a button acts on: picking a
// call there (its tile, its row in Active calls, or its number key) opens it in Focus. The choice
// is remembered (Settings has it too) and only applies with two or more calls. A call ringing in
// isn't one of them: it asks over the call you're on (Answer or Decline), so its arrival never
// switches to the grid. Declined, nothing has changed; answered, it opens in Focus.
export let view = prefs.view === "grid" ? "grid" : "focus";
// The calls in the grid, oldest first like the list: the live ones but those ringing in, and the
// selected one even while it's ending, so it doesn't vanish.
export const gridCalls = () => [...entries.values()].filter((e) => (!e.gone && !e.pending && isLive(e) && e.h.call !== "ringing") || e.id === selectedId)
  .sort((a, b) => Number(a.id) - Number(b.id));
export function inGrid() {
  const e = sel();
  return view === "grid" && !!e && e.h.call !== "ringing" && gridCalls().length > 1;
}
export function setView(v) {
  if (v === view) return;
  view = v;
  savePref("view", v);
  log(`view: ${v}`);
  settings?.refresh();
  render();
}
// A call you pick (its row, its tile, its number key) becomes the one you're talking to (goLive):
// a held call resumes, and the live one goes on hold. Picked from the grid, it opens in Focus.
export function focusCall(id) {
  closeHistory(false);
  const fromGrid = inGrid();
  if (entries.get(id)?.h.call === "ringing") answerPicked = id;   // picked: its full answer view
  selectCall(id);
  goLive(entries.get(id));
  if (fromGrid && entries.get(id)?.h.call !== "ringing") setView("focus");
}

// Moves the microphone and speaker to the audio call: its sender gets the microphone, its <video>
// plays; every other call sends no audio and is silent. The selected call takes the audio when it's
// connected and not on hold. A held call sends nothing at all.
function syncAudio() {
  const s = sel();
  if (s?.h.call === "connected" && !holdState(s) && s.m) audioId = s.id;
  const a = entries.get(audioId);
  if (!a || a.h.call !== "connected" || holdState(a)) audioId = null;
  const mic = localTrack("audio");
  if (mic) mic.enabled = !(entries.get(audioId)?.m?.muted ?? startMuted());
  for (const e of entries.values()) {
    const on = e.id === audioId;
    e.el.muted = !on;
    // Played by script, not left to autoplay: Chrome pauses a muted autoplaying video while it
    // can't be seen (another call's, or an audio-only call's), and it stays paused when unmuted.
    if (e.el.srcObject && e.el.paused) e.el.play().catch(() => {});
    const sender = e.m?.audioSender;
    // Held by them, even the live call sends silence rather than you, until they resume.
    // No microphone (privacy mode closed it): silence, never nothing.
    const want = on && mic && !e.h.heldByThem ? mic : sender ? quietAudio(e) : null;
    if (sender && sender.track !== want) sender.replaceTrack(want).catch((err) => log(`audio switch failed: ${err.message}`));
  }
  // The music plays only while a call is sending it.
  if (holdAudio && ![...entries.values()].some((e) => e.m?.audioSender?.track === holdAudio.music.stream.getAudioTracks()[0])) stopHoldMusic();
}

// ---- What new calls start with ----
// The kind of call is the button you press: Call or Video call, Answer or Answer with video.
// There's no switch, and nothing carries over from one call to the next.
export const params = new URLSearchParams(location.search);
const enterVideo = params.get("call") === "video";   // --video (testing): Enter places a video call
// What a video call you place starts with. Each call takes its own copy when its media is set up
// (e.m.camOn, e.source, e.cameraId), so a change on one call touches neither another nor the next.
//  - camOn: a video call starts with your camera on (it's a video call: that's what the button
//    says), unless the camera list's "Camera off" was picked: then they see your camera-off card
//    until Start video. Settings' Default camera can be Camera off (prefs.cameraOff), kept between
//    runs; Your devices picks it on demand (setStartCamera), back to the default when the last
//    call ends. --camera-off (testing) starts them off too.
//  - source: "camera", or in diagnostics "test" or "bars" (setVideoSource).
// The camera device is devices.camera, with the microphone and speaker.
export const camOnDefault = () => params.get("camera") !== "off" && !prefs.cameraOff;
export const nextVideo = { camOn: camOnDefault(), source: "camera" };
/** The camera list's "Camera off" (false), or a camera picked (true): how the next call starts. */
export function setStartCamera(on) {
  if (nextVideo.camOn === on) return;
  nextVideo.camOn = on;
  log(on ? "camera: on for the next video call" : "camera: off for the next call (your camera-off card)");
  syncCallBtn();
  render();
}
const testAddVideo = Number(params.get("addvideo")) || 0;        // --add-video <s> (testing)
export const testSay = params.get("say") ?? "";                           // --say <text> (testing)

export const localTrack = (kind) => local?.getTracks().find((t) => t.kind === kind) ?? null;
initAddress();

// The saved-contact mark (contactmark.js) after a call's name or address, if it's a contact.
function markContact(node, remote) {
  const c = remote ? contactFor(remote) : null;
  if (c) node.append(contactMarkEl(c.name));
}
// An avatar's initial, and its Gravatar (avatar.js) from the address. The text is only set when
// it changes, so a picture over it isn't dropped and added back on every render.
function setInitial(el, name, address) {
  const text = initial(name);
  if (el.textContent !== text || el.firstChild?.nodeType !== 3) {
    const pic = el.querySelector(":scope > img.av-img");
    el.textContent = text;
    if (pic) el.append(pic);
  }
  setAvatar(el, address);
}

// The microphone is only open during a call (see openMic), or Your devices' mic test. The camera is
// only opened while it's wanted (see wantedSources): a video call with your camera on, or Your
// devices' preview. Otherwise it's off, and the camera's light with it.
// A camera another app (or another copy of this one) is using can't be opened on Windows. That
// isn't "no camera", so it says "Camera in use", and the call sends the placeholder. It is NOT
// retried by itself: the camera is only ever opened because of something you did (Preview,
// placing or answering a video call, Add video, Start video). Otherwise it would start
// streaming on its own the moment the other app let go.
// The microphone, speaker and camera. Empty means the system default.
//  - defaults: Settings' picks, kept between runs. Every call you place or answer starts with them.
//  - devices: what's in use now, and for the call about to start. Your devices and the ▾ menus on
//    Mute and Camera change it, on demand, and only until the last call ends: then it's the
//    defaults again (resetDevices), so a change for one call never becomes the next call's.
export const defaults = { mic: "", speaker: "", camera: "" };
try { Object.assign(defaults, JSON.parse(localStorage.getItem("devices") ?? "{}")); } catch { /* storage unavailable */ }
export const devices = { ...defaults };
const saveDefaults = () => { try { localStorage.setItem("devices", JSON.stringify(defaults)); } catch { /* storage unavailable */ } };
export const MIC_CONSTRAINTS = { echoCancellation: true, noiseSuppression: true, autoGainControl: true };

// Like the camera, the microphone is only open while it's needed: from placing or answering a
// call until it ends. So the mic light (on a webcam, say) is off between calls. Mute is the
// call's: every call starts unmuted.
export let hasMic = false;        // a microphone exists
// Privacy mode, the button at the top (Shift+M), is a hard off: while it's on, the microphone and
// camera are closed, every call gets silence and (on video) your camera-off card, and so does each
// new one. Mute, the camera button and Your devices' tests are disabled, so nothing turns it off
// behind your back: only its button (or Shift+M) does. It holds what each call had before, for
// turning it off again. It's never remembered: the app always starts with it off.
let privacy = null;        // null, or { calls: Map(id -> { muted, camOn }) }
export const privacyOn = () => !!privacy;
const startMuted = () => !!privacy;     // a new call's microphone
export const camStart = () => nextVideo.camOn && !privacy;   // a new video call's camera
let micOpening = null;    // the open in progress, so placing a call and its media share one

async function openDevices() {
  local = new MediaStream();
  try {
    const list = await navigator.mediaDevices.enumerateDevices();
    hasMic = list.some((d) => d.kind === "audioinput");
    setHasCamera(list.some((d) => d.kind === "videoinput"));
  } catch { hasMic = false; setHasCamera(false); }
}

// Opens the microphone (the remembered one, or the default if it's gone) unless it's open.
// Resolves to the track, or null if there's no microphone to be had.
function openMic() {
  if (privacy) return Promise.resolve(null);   // privacy mode: closed, the call gets silence
  if (localTrack("audio")) return Promise.resolve(localTrack("audio"));
  micOpening ??= (async () => {
    const open = (id) => navigator.mediaDevices.getUserMedia({ audio: { ...MIC_CONSTRAINTS, ...(id ? { deviceId: { exact: id } } : {}) } });
    try {
      // Exactly ("ideal" isn't always honoured), falling back to the default if it's unplugged.
      const stream = await open(devices.mic).catch((err) => {
        if (!devices.mic) throw err;
        log(`remembered microphone unavailable (${err.name}), using the default`);
        return open("");
      });
      const track = stream.getAudioTracks()[0];
      if (privacy) { track.stop(); return null; }   // turned on while it was opening
      track.enabled = !startMuted();
      local.addTrack(track);
      log(`microphone opened: ${track.label}`);
      return track;
    } catch (err) {
      log(`microphone unavailable: ${err.name} ${err.message}`);
      return null;
    } finally {
      micOpening = null;
      render();
    }
  })();
  return micOpening;
}

function closeMic() {
  const track = localTrack("audio");
  if (!track) return;
  local.removeTrack(track);
  track.stop();
  log("microphone closed");
}

// ---- Media from the host ----

// A new offer arrives when a call connects, and again when video is added to it. Video you
// asked for starts with your camera on; video they added starts with it off, so being sent a
// video request never turns your camera on by itself.
// The host's offer for the call's media: answer it (media-answer). With no offer (the web
// softphone placing a call, where this page's connection goes straight to the far end), make
// one instead (media-offer-made) for a call with or without video, and take the answer from
// media-remote-answer.
async function onMediaOffer(id, sdp, video = false, renegotiate = false) {
  const e = entries.get(id);
  if (!e) return;
  // The web softphone: a re-INVITE on a call that's up (hold, say) is answered, or offered, on
  // the same connection. Rebuilding it would start new ICE and DTLS under the far end's feet.
  if (renegotiate && e.pc && e.m) { await renegotiateMedia(e, sdp); return; }
  const midCall = !!e.m;
  closeMedia(e);
  if (!midCall) e.quality = newQuality();
  else if (e.quality) e.quality.prev = e.quality.audioWindow = null;   // a new peer connection: its counters start again
  // The microphone opens once they answer (onPhase), not while a call you placed rings: the web
  // softphone sets its media up before then, and sends silence until the microphone's open.
  if (e.h.call !== "calling") await openMic();   // usually already opening, from Answer
  // Privacy mode keeps it closed, whatever was pressed.
  // Answer with video, or Video call, opens it unless the camera list says Camera off.
  const wantCamera = !privacy && (e.addingVideo ? true : midCall ? false : (answerCamera ?? true) && camStart());
  e.addingVideo = false;
  if (wantCamera) retryCamera();   // you placed, answered or added video: try the camera again
  // A new call starts with the source and camera last picked; a call that's renegotiating keeps its own.
  e.source ??= nextVideo.source;
  e.cameraId ??= devices.camera;
  const m = {
    // The call has video at all (an m=video line from the host). Read from the offer now, not
    // once the transceivers are set up, or a render in between briefly shows the audio bars.
    hasVideo: sdp ? /^m=video [1-9]/m.test(sdp) : video,
    camOn: wantCamera && sourceAvailable(e.source),
    muted: startMuted(),
    audioSender: null,
    videoSender: null,
    framesDecoded: -1, lastFrameAt: 0,   // when the decoder last produced a frame
    rxPackets: -1, rxChangedAt: performance.now(), rxDead: false,
    txLost: false,               // from the host: their RTCP says none of our audio arrives
    remoteAnalyser: null, localAnalyser: null,
  };
  e.m = m;
  answerCamera = null;   // Answer or Answer with video was for this call only

  // { callId } is for the web softphone's host.js, which watches the page's connections; the
  // browser ignores what it doesn't know.
  const pc = new RTCPeerConnection({ callId: id });
  e.pc = pc;
  const remote = new MediaStream();
  e.el.srcObject = remote;
  pc.ontrack = (ev) => {
    remote.addTrack(ev.track);
    if (ev.track.kind === "audio") m.remoteAnalyser = analyserFor(ev.track);
  };
  pc.onconnectionstatechange = () => {
    log(`call ${id} media ${pc.connectionState}`);
    if (selectedId === id) renderKeypad();   // tones need the call's audio up
  };

  if (sdp) await pc.setRemoteDescription({ type: "offer", sdp });
  else {
    pc.addTransceiver("audio");
    if (video) pc.addTransceiver("video");
  }
  await setUpTransceivers(e);
  // Opens the camera (and puts it on the call) if this is a video call with it on; closes it
  // if nothing wants it. Not awaited: the answer doesn't depend on it.
  syncCamera();
  syncAudio();
  if (localTrack("audio")) m.localAnalyser = analyserFor(localTrack("audio"));
  await pc.setLocalDescription(sdp ? await pc.createAnswer() : await pc.createOffer());
  await iceGathered(pc);
  if (e.pc !== pc) return;   // ended meanwhile
  post({ type: sdp ? "media-answer" : "media-offer-made", id, sdp: pc.localDescription.sdp });
  log(`call ${id} media: ${m.hasVideo ? `video${m.camOn ? "" : ", your camera off"}` : "audio only"}${midCall ? " (renegotiated)" : ""}`);

  // The browser only sees the local link to the host, so its own bandwidth estimate would grow
  // without limit. Cap the camera until the relay passes on the far end's feedback.
  if (m.videoSender) {
    const sender = m.videoSender;
    pc.addEventListener("connectionstatechange", async () => {
      if (pc.connectionState !== "connected") return;
      const p = sender.getParameters();
      p.encodings = p.encodings?.length ? p.encodings : [{}];
      p.encodings[0].maxBitrate = CAMERA_KBPS * 1000;
      await sender.setParameters(p).catch((err) => log(`bitrate cap failed: ${err.message}`));
    });
  }
  render();
}

// The senders, once the transceivers exist: audio follows the audio focus (syncAudio), video is
// always sendrecv, camera or not, with the placeholder until the camera goes out, so it can be
// turned on later without renegotiating. Sent now, so video flows from the start.
async function setUpTransceivers(e) {
  const m = e.m;
  for (const tr of e.pc.getTransceivers()) {
    const kind = tr.receiver.track.kind;
    if (kind === "video") {
      if (m.videoSender === tr.sender) continue;
      m.hasVideo = true;
      m.videoSender = tr.sender;
      tr.direction = "sendrecv";
      await tr.sender.replaceTrack(videoToSend(e));
    } else if (kind === "audio" && !m.audioSender) {
      m.audioSender = tr.sender;
      // With a microphone, sendrecv even while privacy mode has it closed (silence goes out),
      // so turning privacy mode off needs no renegotiation.
      tr.direction = localTrack("audio") || hasMic ? "sendrecv" : "recvonly";
    }
  }
}

function iceGathered(pc) {
  return new Promise((resolve) => {
    if (pc.iceGatheringState === "complete") return resolve();
    pc.addEventListener("icegatheringstatechange", () => pc.iceGatheringState === "complete" && resolve());
    setTimeout(resolve, 2000);
  });
}

// The web softphone, mid-call: their re-INVITE's offer (answered here), or a re-offer of ours
// (sdp null: hold or resume, from the Hold button). On hold our side is sendonly, as a SIP phone's is.
async function renegotiateMedia(e, sdp) {
  const pc = e.pc;
  if (sdp) {
    await pc.setRemoteDescription({ type: "offer", sdp });
    await setUpTransceivers(e);
  } else {
    for (const tr of pc.getTransceivers()) {
      if (tr.receiver.track.kind === "audio" || tr.receiver.track.kind === "video") tr.direction = holdState(e) ? "sendonly" : "sendrecv";
    }
  }
  await pc.setLocalDescription(sdp ? await pc.createAnswer() : await pc.createOffer());
  await iceGathered(pc);
  post({ type: sdp ? "media-answer" : "media-offer-made", id: e.id, sdp: pc.localDescription.sdp });
  log(`call ${e.id} media renegotiated${sdp ? " (their re-INVITE)" : holdState(e) ? " (hold)" : " (resume)"}`);
  syncCamera();
  render();
}

// Their answer to the offer this page made. Video they turned down (port 0) means no video.
async function onMediaAnswer(id, sdp) {
  const e = entries.get(id);
  if (!e?.pc) return;
  await e.pc.setRemoteDescription({ type: "answer", sdp });
  if (e.m?.hasVideo && !/^m=video [1-9]/m.test(sdp)) {
    e.m.hasVideo = false;
    e.videoRefused = true;
    syncCamera();
    log("they didn't take video");
  }
  render();
}

function closeMedia(e) {
  e.pc?.close();
  e.pc = null;
  e.lastStats = null;
  for (const a of [e.m?.remoteAnalyser, e.m?.localAnalyser]) a?.source.disconnect();
  e.m = null;
  e.el.srcObject = null;
  stopPlaceholders();
}

// Camera on or off during a video call, for that call only. Off swaps in the placeholder, then
// closes the camera (unless another call is still sending it). Not on in privacy mode.
export async function setCamera(on, e = sel()) {
  if (!e?.m?.videoSender || !(hasCamera || sourceOf(e) !== "camera")) return;
  if (on && privacy) return;
  e.m.camOn = on;
  if (on) retryCamera();   // try again, because you asked
  if (!on && !holdState(e)) await e.m.videoSender.replaceTrack(placeholderTrack(e));
  log(`call ${e.id}: camera ${on ? "on" : "off"}`);
  render();
  await syncCamera();
}
initLevels();
initRtt();
initKeypad();
initTransfer();
initQuality();

// ---- Rendering the state ----

export function stageMode() {
  if (state.call !== "connected" || !call) return "card";
  if (!call.hasVideo) return "audio";
  return performance.now() - call.lastFrameAt < NO_FRAMES_MS ? "video" : "nocam";
}

// New calls go alongside the others, up to the host's limit.
const callsFull = () => liveEntries().length >= (hostState.maxCalls ?? 8);
// Why a new call can't be placed now, or null. Every way of calling goes by it: Call, Enter in
// the address bar, a contact's Call in People, Call back, so none of them can get round it. One
// unanswered call you've placed at a time (calling or ringing out, or the Calling… banner over
// another call); calls ringing in, connected or held don't count, so Call & hold still works.
// A cancelled one stops counting at once, before the host reports it's ended.
export function callBlocked() {
  if (callsFull()) return "That's as many calls as there can be: end one to call again";
  const out = [...entries.values()].find((e) => e.h.outgoing && e.h.call === "calling" && !e.cancelled);
  return out ? `Wait for ${nameOf(out)} to answer, or cancel it, to place another call` : null;
}
// Call's and Video call's enabled state and tooltips: off while callBlocked says so, or there's
// nothing to call. On every change to the address field (typing, ×, a suggestion, paste) as well
// as in render.
export function syncCallBtn() {
  const blocked = callBlocked();
  const empty = !$("address").value.trim();
  const kept = liveCall();
  for (const [btn, video] of [[$("callBtn"), false], [$("videoCallBtn"), true]]) {
    btn.disabled = !!blocked || empty;
    btn.title = blocked ?? (empty ? "Type a number or address to call"
      : kept ? `${video ? "Video call" : "Call"}, and put ${nameOf(kept)} on hold`
      : !video ? "Call: your camera stays closed (Enter)"
      : privacy ? "Video call: privacy mode is on, so they'll see your camera-off card (Ctrl+Enter)"
      : `Video call: your camera ${nextVideo.camOn ? "on" : "off at the start: they'll see your camera-off card"} (Ctrl+Enter)`);
  }
}

export function render() {
  deriveState();
  syncAudio();
  const grid = inGrid();
  if (grid && textOpen) closeTextForGrid();
  const { call: phase, remote, reason } = state;
  const live = phase === "calling" || phase === "ringing" || phase === "connected";
  const connected = phase === "connected" && !!call;
  const name = shortName(remote);
  const e = sel();
  // New calls go alongside the others, up to the host's limit.
  const full = callsFull();
  $("address").disabled = full;
  $("address").placeholder = $("xferAddr").placeholder = addressHint();
  $("address").title = $("xferAddr").title = addressTip();
  $("info").hidden = phase !== "connected";
  $("stage").classList.toggle("head", phase === "connected");
  const shown = shownAddress(remote, e ? e.h.account ?? null : state.account);
  renderInfoLock();
  // The call header: a saved contact's name on top (with the mark), the address under it with
  // the timer, as a phone's call screen has it; otherwise the address on top.
  const headContact = remote ? contactFor(remote) : null;
  $("infoName").textContent = headContact ? headContact.name : shown;
  $("infoName").title = headContact ? `${headContact.name} (saved contact): ${shown}` : shown;
  $("infoContact").hidden = !headContact;
  if (headContact && $("infoContact").dataset.name !== headContact.name) {
    $("infoContact").replaceChildren(contactMarkEl(headContact.name));
    $("infoContact").dataset.name = headContact.name;
    $("infoContact").title = "Saved contact";
  }
  $("infoAddr").hidden = !headContact;
  $("infoAddr").textContent = headContact ? shown : "";
  $("infoAddr").title = shown;
  for (const x of entries.values()) x.el.classList.toggle("sel", x.id === selectedId);

  // Call and Video call beside the address. No Video call if video is off (--no-video).
  // The selected call's source, or before a call the one it'll start with.
  const camAvailable = hasCamera || sourceOf(e) !== "camera";
  const videoAllowed = state.videoAllowed !== false;
  // On a call, each says what happens to it: "Call & hold current", in the same words as a
  // ringing call's "Answer & hold current" (its tooltip names who).
  const tail = liveCall() ? '<span class="hold-tail"> &amp; hold current</span>' : "";
  const callHtml = `${ICON.phone}<span>Call${tail}</span>`, videoHtml = `${ICON.video}<span>Video call${tail}</span>`;
  if ($("callBtn").innerHTML !== callHtml) $("callBtn").innerHTML = callHtml;
  if ($("videoCallBtn").innerHTML !== videoHtml) $("videoCallBtn").innerHTML = videoHtml;
  $("videoCallBtn").hidden = !videoAllowed;
  syncCallBtn();

  // The stage.
  const mode = stageMode();
  const stage = $("stage");
  if (stage.dataset.mode !== mode) {
    stage.dataset.mode = mode;
    log(`stage: ${mode}`);
    if (mode === "audio") requestAnimationFrame(drawViz);
  }
  // Call details follow the selected call: redrawn when it changes, connects or ends (and as the
  // host's details and the codecs arrive).
  const detailsKey = `${selectedId}:${state.call}`;
  if (detailsKey !== detailsShown) { detailsShown = detailsKey; renderDetails(); }
  setInitial($("nocamInitial"), name, remote);
  $("nocamNote").textContent = connected && Date.now() - e.connectedAt > NO_FRAMES_MS ? `No video arriving from ${name}` : "";
  setInitial($("vizThem"), name, remote);
  $("vizThem").title = name;
  const note = (id, text, bad) => {
    const el = $(id);
    el.hidden = !text;
    el.classList.toggle("bad", !!bad);
    el.firstElementChild.textContent = text || "";
  };
  // On hold, either way round, the held note says it all: no warnings about audio under it.
  const onHold = connected && (holdState(e) || state.heldByThem);
  note("noteThem", connected && !onHold && call.rxDead ? `No audio arriving from ${name}` : "", true);
  note("noteMe", !connected ? "" : call.txLost && !onHold ? `${name} isn't getting your audio` : call.muted ? "You're muted" : "", connected && call.txLost && !onHold);

  // Self view: only on a video call, and always what they see: your camera, or the placeholder
  // while it's off. A note on top says why the camera isn't showing. Before a call there's none:
  // Your devices has the preview.
  const selfViewApplies = connected && call.hasVideo;
  const showSelf = !!local && selfViewApplies && selfViewOn;
  const sv = $("selfViewBtn");
  sv.hidden = !selfViewApplies;
  sv.setAttribute("aria-pressed", String(selfViewOn));
  sv.title = `${selfViewOn ? "Hide" : "Show"} your self view (V). They still see you.`;
  if (!sv.innerHTML) sv.innerHTML = `${ICON.selfView}<span>Self view</span>`;
  const camOn = connected && call.camOn;
  const selfText = !showSelf ? null : privacy ? "Privacy mode" : !camAvailable ? "No camera" : cameraInUse && sourceOf(e) === "camera" ? "Camera in use"
    : !camOn ? "Camera off" : null;
  showSelfVideo(showSelf);
  $("selfOff").hidden = !selfText;
  $("selfOffText").textContent = selfText ?? "";
  if (!sameSources(wantedSources(), openSources())) syncCamera();

  // The control bar, under the stage once a call's been answered (connected, or on hold), and
  // until the last one ends: before that there's nothing for it to do. Home and a call ringing
  // in have Your devices instead. Mute and Camera are split buttons: the ▾ picks the device; the
  // button itself is for the call you're on, and only that call.
  const answered = [...entries.values()].some((x) => x.h.call === "connected");
  if ($("controls").hidden === answered) {
    $("controls").hidden = !answered;
    if (!answered && (openMenu === "mic" || openMenu === "cam" || openMenu === "more")) closeMenu();
  }
  const setBtn = (el, icon, text) => {
    const html = `${icon}<span>${text}</span>`;
    if (el.innerHTML !== html) el.innerHTML = html;
  };
  const muted = !!call && call.muted;
  const mute = $("muteBtn");
  mute.disabled = !hasMic || !call || !!privacy;
  mute.setAttribute("aria-pressed", String(hasMic && muted));
  // What pressing it does, as on the camera button; red while you're muted.
  mute.title = !hasMic ? "No mic found"
    : privacy ? "Privacy mode is on: your microphone stays closed until you turn it off (Shift+M)"
    : !call ? "Mute, for the call you're on (the ▾ picks the microphone and speaker)"
    : muted ? "Unmute: they'll hear you (M)" : "Mute: they won't hear you (M)";
  setBtn(mute, muted || !hasMic ? ICON.micOff : ICON.mic, !hasMic ? "No mic" : muted ? "Unmute" : "Mute");
  // While muted on a call, a red mic by the timer (in the call header) says so.
  const pill = $("mutedPill");
  pill.hidden = !(live && hasMic && muted);
  if (!pill.hidden && !pill.innerHTML) { pill.innerHTML = `${ICON.micOff}<span>You're muted</span>`; pill.title = "You're muted"; }

  // Camera: during a video call it turns your camera on and off, for that call; on an audio call
  // it's Add video, unless video can't be had. Before a call it's disabled: the ▾ picks the camera,
  // and Your devices has the preview. Privacy mode disables it.
  const cam = $("camBtn");
  const canAdd = connected && !call.hasVideo && videoAllowed && !e.videoRefused;
  const lockedTitle = "Privacy mode is on: your camera stays closed until you turn it off (Shift+M)";
  if (connected && !call.hasVideo) {
    cam.disabled = !canAdd || e.addingVideo || !!privacy;
    cam.setAttribute("aria-pressed", "false");
    setBtn(cam, canAdd ? ICON.video : ICON.videoOff, e.addingVideo ? "Adding video…" : canAdd ? "Add video" : "No video");
    cam.title = privacy && canAdd ? lockedTitle : canAdd ? "Add video to this call. They'll be asked; some phones can't."
      : e.videoRefused ? "Their phone doesn't do video" : "Video is turned off";
  } else {
    const on = connected && call.camOn;
    cam.disabled = !connected || !camAvailable || !videoAllowed || !!privacy;
    cam.setAttribute("aria-pressed", String(connected && camAvailable && videoAllowed && !on));
    setBtn(cam, camAvailable && on ? ICON.video : ICON.videoOff,
      // What pressing it does, as on the mic button: the video, whatever its source.
      !videoAllowed ? "No video" : !camAvailable ? "No camera" : on ? "Stop video" : "Start video");
    cam.title = !videoAllowed ? "Video is turned off" : !camAvailable ? "No camera found"
      : privacy ? lockedTitle
      : !connected ? "Your camera, for a video call you're on (the ▾ picks it; Your devices has a preview)"
      : on ? "Stop video: they'll see your camera-off card" : "Start video: turn your camera on";
  }
  $("camMenuBtn").disabled = !videoAllowed;

  renderTextControls(phase);
  renderKeypad();
  renderTransfer();
  renderMore();   // the ⋯ at phone width, from the four above
  renderDock();
  // Hold: a re-INVITE. On hold the stage says so, with Resume.
  const hold = $("holdBtn");
  hold.disabled = !connected;
  hold.setAttribute("aria-pressed", String(connected && state.held));
  setBtn(hold, state.held ? ICON.play : ICON.pause, state.held ? "Resume" : "Hold");
  hold.title = !connected ? "Hold, during a call" : state.held ? "Take the call off hold (H)" : "Put the call on hold (H)";
  const heldNote = $("heldNote");
  heldNote.hidden = !(connected && (state.held || state.heldByThem)) || xferBusy(e);
  if (!heldNote.hidden) {
    $("heldText").textContent = state.held ? `On hold · ${fmtElapsed(Date.now() - e.heldAt)}` : `${name} put you on hold`;
    heldNote.classList.toggle("long", state.held && Date.now() - e.heldAt > 60000);
    $("heldResume").hidden = !state.held;
    // Why it's on hold, when a transfer of yours failed (they declined it, say): the same words as
    // the call header's note, where it's easy to miss, with Try again beside Resume.
    const xt = e?.h.transfer;
    const failed = state.held && xt?.role === "out" && xt.status === "failed" ? xt : null;
    $("heldWhy").hidden = !failed;
    if (failed) { $("heldWhy").textContent = transferShort(failed); $("heldWhy").title = failed.detail || ""; }
    $("heldRetry").hidden = !failed;
    if (failed) $("heldRetry").title = `Transfer ${name} again`;
  }
  // End is for a connected call. A call you're placing is cancelled from its card, beside who
  // you're calling, as a call ringing in is declined from its card.
  const hang = $("hangupBtn");
  hang.disabled = phase !== "connected";
  setBtn(hang, ICON.hangup, "End");
  for (const b of [$("micMenuBtn"), $("camMenuBtn")]) if (!b.innerHTML) b.innerHTML = ICON.chevronDown;

  // The card: everything but a connected call.
  const actions = $("cardActions");
  actions.replaceChildren();
  const title = $("cardTitle"), sub = $("cardSub");
  const button = (cls, text, onclick) => Object.assign(document.createElement("button"), { className: `btn ${cls}`, textContent: text, onclick });
  switch (phase) {
    case "idle":
      title.textContent = "Ready";
      sub.textContent = "Type an address above to start a call.";
      // Desktop: say why the account calls go out from isn't registered (Manage accounts has more).
      if (state.accounts && state.registration === "failed")
        sub.textContent = `${state.account} isn't registered: ${state.registrationDetail || "no reason given"}.`;
      break;
    case "calling":
      title.textContent = shown;
      markContact(title, remote);
      sub.textContent = "Calling…";
      actions.append(button("btn-danger", "Cancel", () => post({ type: "hangup", id: selectedId })));
      break;
    case "ringing": {
      if (ringingUnpicked()) {
        // Several ringing and none picked: they're in the banner above, all alike.
        title.textContent = `${allRinging().length} calls ringing`;
        sub.textContent = "Answer one above, or pick a name to see that call.";
        break;
      }
      title.textContent = shown;
      markContact(title, remote);
      sub.textContent = state.incomingVideo ? "Incoming video call" : "Incoming call";
      actions.append(button("btn-danger", "Decline", () => post({ type: "decline", id: selectedId })));
      // Answer: you see them (on a video call), your camera stays closed. Answer with video, only
      // when they offered it, opens it too. With another call going, either holds it.
      const holdTail = liveCall() ? " & hold current" : "";
      const offered = state.incomingVideo && state.videoAllowed !== false;
      setBtn(answerBtn, ICON.phone, `Answer${holdTail}`);
      answerBtn.title = offered ? "Answer: you'll see them; your camera stays closed (A)" : "Answer (A)";
      actions.append(answerBtn);
      if (offered) {
        setBtn(answerVideoBtn, ICON.video, `Answer with video${holdTail}`);
        answerVideoBtn.title = privacy ? "Answer with video: privacy mode is on, so they'll see your camera-off card" : "Answer with your camera on";
        actions.append(answerVideoBtn);
      }
      if (lastPhase !== "ringing" || lastSelected !== selectedId) answerBtn.focus();
      break;
    }
    case "connected":
      title.textContent = shown;
      markContact(title, remote);
      sub.textContent = "Connecting media…";
      break;
    case "ended": {
      title.textContent = shown || "Call ended";
      sub.textContent = reason || "Call ended";
      // Back to the call you're on, or with none, Close: Home now, rather than in a few seconds.
      const back = otherLive();
      if (back) actions.append(button("", `Back to ${nameOf(back)}`, () => selectCall(back.id)));
      else {
        const close = button("", "Close", () => closeEnded(e));
        close.title = "Back to Home (Esc)";
        actions.append(close);
      }
      break;
    }
  }
  // Your devices under a call ringing on the stage: what the call will use, and a quick change.
  // Under a call you're placing: what it's using, read-only (the ▾ menus change it once answered).
  const ringDevices = phase === "ringing" && !ringingUnpicked();
  const callingDevices = phase === "calling";
  $("cardDevices").hidden = !ringDevices && !callingDevices;
  $("card").classList.toggle("with-devices", ringDevices || callingDevices);
  if (ringDevices) renderYourDevices($("cardDevices"), "ring");
  else if (callingDevices) renderYourDevices($("cardDevices"), "calling", e);
  // No call: Home (or the dial view) in the card's place.
  $("card").hidden = homeShown();
  // Privacy mode on a call: a note says what they get, with Turn off.
  const pn = $("privacyNote");
  pn.hidden = !(connected && privacy) || grid;
  if (!pn.hidden) $("privacyNoteText").textContent = `Privacy mode: ${name} hears silence${call.hasVideo ? " and sees your camera-off card" : ""}`;
  renderIncoming();
  renderOutgoing();
  renderGrid(grid);

  // Answer or Answer with video is for that call only (onMediaOffer uses it, then clears it); one
  // that wasn't answered mustn't carry over to the next.
  if (phase !== "ringing" && phase !== "connected") answerCamera = null;
  lastPhase = phase;
  lastSelected = selectedId;
  renderQuality();

  renderCalls();
  renderPrivacy();
  renderHome();
  renderIndicator();
}

// Every 5 s during a call: what's arriving and leaving on the selected call, for the log (and for testing).
async function statsLine(e) {
  if (!e?.pc) return;
  const now = {};
  (await e.pc.getStats()).forEach((r) => {
    if (r.type === "inbound-rtp" || r.type === "outbound-rtp") now[`${r.type}-${r.kind}`] = r;
  });
  const fps = (k, field) => {
    const a = e.lastStats?.[k], b = now[k];
    return a && b ? (((b[field] ?? 0) - (a[field] ?? 0)) / ((b.timestamp - a.timestamp) / 1000)).toFixed(1) : "-";
  };
  const vin = now["inbound-rtp-video"], ain = now["inbound-rtp-audio"];
  log(`stats (call ${e.id}): video in ${fps("inbound-rtp-video", "framesDecoded")} fps ${vin?.frameWidth ?? 0}x${vin?.frameHeight ?? 0} ${vin?.decoderImplementation ?? ""}, ` +
      `video out ${fps("outbound-rtp-video", "framesEncoded")} fps, audio in ${fps("inbound-rtp-audio", "packetsReceived")} pkt/s lost ${ain?.packetsLost ?? "-"} concealed ${ain?.concealedSamples ?? "-"} level ${(ain?.audioLevel ?? 0).toFixed(3)}, ` +
      `audio out ${fps("outbound-rtp-audio", "packetsSent")} pkt/s, stage ${$("stage").dataset.mode}`);
  e.lastStats = now;
}

// Once a second: the selected call's timer, every call's time in the list, and how long held
// calls have been held.
export function tick() {
  const e = sel();
  if (e?.h.call === "connected" && e.connectedAt) {
    const s = Math.floor((Date.now() - e.connectedAt) / 1000);
    if (s > 0 && s % 5 === 0) statsLine(e);
    $("timer").textContent = `${String(Math.floor(s / 60)).padStart(2, "0")}:${String(s % 60).padStart(2, "0")}`;
  }
  if (inGrid() || [...entries.values()].some((x) => x.h.held)) render();
  else renderCallTimes();
  if (!$("incomingMore").hidden) renderIncoming();   // how long each ringing call has waited
}
setInterval(tick, 1000);

// ---- The calls: the host's state, and acting on them ----

// Every state message: new calls get an entry, changed ones move on, and ones the host has
// dropped go (unless one is on the stage, where it stays, ended, until you pick another).
function onState(msg) {
  hostState = msg;
  const seen = new Set();
  const listed = new Set((msg.calls ?? []).map((h) => h.id));
  for (const id of dropped) if (!listed.has(id)) dropped.delete(id);
  for (const h of msg.calls ?? []) {
    if (dropped.has(h.id)) continue;
    seen.add(h.id);
    let e = entries.get(h.id);
    const isNew = !e;
    if (isNew) { e = newEntry(h.id); entries.set(h.id, e); }
    const was = e.h.call, wasHeld = !!e.h.held, wasWith = e.h.remote;
    e.h = h;
    if (h.call === "ringing") e.ringAt ||= Date.now();   // how long it's waited, in the ringing banner's list
    // Transferred, by them or to us: the same call, with someone else now. Its timer starts again.
    if (was === "connected" && h.call === "connected" && wasWith && h.remote !== wasWith) {
      log(`call ${e.id}: now with ${h.remote} (was ${wasWith})`);
      e.connectedAt = Date.now();
      e.heldAt = 0;
    }
    // Hold settled: the host has done what was asked, or has nothing out and has had time to
    // start it (so it was turned down). Then the host's word is what counts.
    if (e.holdWanted !== undefined && !h.holdBusy && (!!h.held === e.holdWanted || Date.now() - e.holdAskedAt > 1000)) delete e.holdWanted;
    if (h.held && !e.heldAt) e.heldAt = Date.now();
    else if (!h.held) e.heldAt = 0;
    // On hold the call sends no video; resumed, the camera (or placeholder) goes back on it.
    if (!!h.held !== wasHeld) syncCamera();
    if (isNew) onNewCall(e);
    if (h.call !== was) onPhase(e);
    if (h.call === "connected" && was !== "connected") clearAddressOnAnswer(e);
  }
  for (const e of [...entries.values()]) {
    if (seen.has(e.id)) continue;
    if (e.h.call !== "ended") { e.h = { ...e.h, call: "ended", reason: e.h.reason ?? "Call ended" }; onPhase(e); }
    if (e.id === selectedId) e.gone = true;
    else dropEntry(e);
  }
  // The call on the stage ended (declined, or they gave up) with no call going but others still
  // ringing: the stage goes to them, as it would have had it been empty when they rang (the
  // banner's list if several, the answer view if one), not the ended call's card.
  const s = sel(), ringingNow = allRinging();
  if (ringingNow.length && (!s || s.gone || s.h.call === "ended") && liveEntries().every((x) => x.h.call === "ringing")) {
    answerPicked = null;
    selectCall(ringingNow[0].id);
  }
  // The microphone closes when the last call ends (it opens with Call or Answer), and the devices
  // go back to Settings' defaults for the next one.
  const live = liveEntries().length > 0;
  if (!live) closeMic();
  if (hadLive && !live) resetDevices();
  hadLive = live;
}
let hadLive = false;   // a call was going at the last state message

// The last call's over: what's in use goes back to Settings' defaults, so the next call you place
// or answer starts with them, whatever was changed on demand for this one.
function resetDevices() {
  const changed = Object.keys(defaults).filter((k) => devices[k] !== defaults[k]);
  if (nextVideo.camOn !== camOnDefault()) { nextVideo.camOn = camOnDefault(); changed.push("camera off"); }
  if (!changed.length) return;
  Object.assign(devices, defaults);
  log(`devices back to the defaults: ${changed.join(", ")}`);
  syncCamera();
  render();
}

// An ended call's card, closed (Close, or Esc) with nothing else going: Home straight away.
function closeEnded(e) {
  if (selectedId !== e?.id || e.h.call !== "ended" || liveEntries().length) return false;
  dropEntry(e);
  selectedId = null;
  render();
  return true;
}

function onNewCall(e) {
  const s = sel();
  if (e.h.outgoing) e.placedVideo = placedVideo;
  // A call you place while on another (now on hold) leaves the stage and the list alone: it shows
  // as a "Calling…" banner, and only once it's answered does it join the list and take the stage.
  // Unanswered, the banner says why, and the call you were on stays on hold: you may want to
  // call someone else, or resume it, and that's your choice.
  if (e.h.outgoing && isLive(s) && !s.gone) {
    e.pending = true;
    log(`calling ${nameOf(e)} (call ${e.id}) over call ${s.id}`);
  // A call you place goes on the stage. So does one ringing while the stage has no live call;
  // otherwise it rings as a banner over the call you're on.
  } else if (e.h.outgoing || !isLive(s)) {
    if (s?.gone) dropEntry(s);
    selectedId = e.id;
  }
}

function onPhase(e) {
  // A call you placed with Accounts open leaves you there until it's answered; then it closes (an
  // unsaved edit is dropped) and the call is selected. (People closes as soon as you call.)
  if (e.h.call === "connected" && !e.connectedAt && e.h.outgoing) {
    closeHistory(false);
    closeAccounts();
    if (!e.pending && selectedId !== e.id) selectCall(e.id);
  }
  if (e.h.call === "connected" && e.pending) {
    // The call placed over another is answered: now it's a call, in the list and on the stage
    // (in Focus), with the one you were on held.
    e.pending = false;
    selectCall(e.id);
    goLive(e);
    setView("focus");
  }
  if (e.h.call === "connected" && !e.connectedAt) {
    e.connectedAt = Date.now();
    // Answered: now the microphone opens (a call you placed rang with it closed) and goes on the call.
    openMic().then((track) => {
      if (track && e.m && !e.m.localAnalyser) e.m.localAnalyser = analyserFor(track);
      syncAudio();
      render();
    });
    if (e.id === selectedId) {
      renderText();
    }
    if (testAddVideo) setTimeout(() => { if (e.m && !e.m.hasVideo && e.id === selectedId) $("camBtn").click(); }, testAddVideo * 1000);
  } else if (e.h.call === "ended") {
    // Not while calling or ringing: in the web softphone the page's connection is the call's
    // own, and it's set up (offer or answer) before the call connects.
    closeMedia(e);
    if (audioId === e.id) audioId = null;
    if (toastCallId === e.id) hideToast();
    // With nothing else going, its "Call ended" card gives way to Home after a few seconds.
    setTimeout(() => closeEnded(e), ENDED_CARD_MS);
    // The call on the stage ended: its keypad and text panels close (docked or not), so the
    // stage is back to its full width for the ended call's card.
    if (e.id === selectedId && (keypadOpen || textOpen)) {
      if (keypadOpen) setKeypadOpen(false);
      if (textOpen) setTextOpen(false, false);
      log(`call ${e.id} ended: keypad and text closed`);
    }
  }
}

// Answers a ringing call, holding the call you're on, and opens it in Focus (as picking a call
// from the grid does). video: Answer with video, your camera on; otherwise it stays closed.
export function answerCall(id, video = false) {
  const e = entries.get(id);
  if (e?.h.call !== "ringing") return;
  closeMenu();
  answerCamera = !!video && !!e.h.incomingVideo;
  closeDevices();   // the call takes the devices over
  holdOthers(e);
  openMic();
  post({ type: "answer", id });
  if (selectedId !== id) selectCall(id);
  setView("focus");
}

function declineCall(id) { post({ type: "decline", id }); }

// Holds or resumes a call. Compared with what was last asked for, not what the host has done
// yet, so a quick switch back and forth still ends where you left it (the host sends the
// re-INVITEs for a call one at a time).
function holdCall(e, hold) {
  if (e?.h.call !== "connected" || holdState(e) === hold) return;
  e.holdWanted = hold;
  e.holdAskedAt = Date.now();
  log(`${hold ? "holding" : "resuming"} call ${e.id} (${nameOf(e)})`);
  post({ type: "hold", id: e.id, hold });
  render();   // the audio moves now (syncAudio), and "Holding…" shows if the host queues it
  syncCamera();   // the "On hold" card, or the camera back
}

// A call you're placing while on another: a banner over the stage with who and Cancel, and if it
// isn't answered, why (until the host lets it go, a few seconds later, or you close it).
function renderOutgoing() {
  const e = [...entries.values()].filter((x) => x.pending && !x.cancelled).sort((a, b) => Number(b.id) - Number(a.id))[0];
  const el = $("outgoing");
  el.hidden = !e;
  if (!e) return;
  const ended = e.h.call === "ended";
  $("outgoingWho").textContent = nameOf(e);
  $("outgoingWho").title = e.h.remote;
  $("outgoingWhat").textContent = ended ? (e.h.reason || "Call ended") : "Calling…";
  el.classList.toggle("ended", ended);
  const b = $("outgoingCancel");
  b.textContent = ended ? "Close" : "Cancel";
  b.className = ended ? "btn" : "btn btn-danger";
  b.onclick = () => {
    // Cancelled, it just goes: you know why.
    if (!ended) post({ type: "hangup", id: e.id });
    if (ended) dropEntry(e); else e.cancelled = true;
    render();
  };
}

// Calls ringing while you're on another: a banner over the stage, the caller who has waited
// longest first, with Answer & hold current and Decline. Clicking the name shows the full answer view.
// More than one: "+N more" drops down a list of the others inside the banner (scrolling past a
// few), each with how long it's waited and its own Decline and Answer, so you pick which.
let incomingOpen = false;
let answerPicked = null;   // a ringing call you picked (its name, row or tile): it gets the full answer view
const allRinging = () => [...entries.values()].filter((e) => e.h.call === "ringing").sort((a, b) => Number(a.id) - Number(b.id));
// One ringing call on the stage has it to itself (the full answer view). Several ringing, with
// one of them on the stage, they're all in the banner, the same list as over a call, and stay
// there as you switch between them: the banner is every call waiting, the stage the one you're
// looking at. Until you pick one (its name, its row) the stage just says how many.
const ringingInBanner = () => sel()?.h.call === "ringing" && allRinging().length > 1;
const ringingUnpicked = () => ringingInBanner() && selectedId !== answerPicked;
// The one in the answer view, picked, is marked in the banner (its name in the accent colour).
const markShown = (who, id) => {
  const shown = id === selectedId && !ringingUnpicked();
  who.classList.toggle("shown", shown);
  if (shown) who.setAttribute("aria-current", "true"); else who.removeAttribute("aria-current");
};
const pickRinging = (id) => { answerPicked = id; closeHistory(false); selectCall(id); render(); };
const incomingRows = new Map();   // call id -> { li, who, what, decline, answer }
const ringingWait = (e) => fmtElapsed(Date.now() - (e.ringAt ||= Date.now()));
const ringingWhat = (e, many) => `${e.h.incomingVideo ? "Video call" : "Calling"}${many ? ` · ${ringingWait(e)}` : ""}`;
// Two ringing with the same name but from different addresses: each says its address too, so
// you can tell which is which (the same both ways, the waiting time is what tells them apart).
const ringingAddr = (e) => (e.h.remote ?? "").replace(/^.*</, "").replace(/>.*$/, "").replace(/^sips?:/i, "");
const ringingWho = (e, ringing) => {
  const n = nameOf(e), same = ringing.filter((r) => nameOf(r) === n);
  return new Set(same.map(ringingAddr)).size > 1 ? `${n} · ${ringingAddr(e)}` : n;
};

function renderIncoming() {
  const all = ringingInBanner();
  const ringing = allRinging().filter((e) => all || e.id !== selectedId);
  const el = $("incoming");
  el.hidden = !ringing.length;
  const e = ringing[0], rest = ringing.slice(1), many = rest.length > 0;
  if (!many) incomingOpen = false;
  el.classList.toggle("open", incomingOpen);
  const answerLabel = liveCall() ? "Answer & hold current" : "Answer";
  if (e) {
    $("incomingWho").textContent = ringingWho(e, ringing);
    $("incomingWho").title = `${e.h.remote}: show the answer view`;
    $("incomingWhat").textContent = ringingWhat(e, many);
    $("incomingWho").onclick = () => pickRinging(e.id);
    markShown($("incomingWho"), e.id);
    $("incomingDecline").onclick = () => declineCall(e.id);
    $("incomingAnswer").textContent = answerLabel;
    $("incomingAnswer").onclick = () => { incomingOpen = false; answerCall(e.id); };
    // A video call: Answer with video too, as an icon (the banner's short of room).
    const iv = $("incomingVideoAnswer");
    iv.hidden = !(e.h.incomingVideo && state.videoAllowed !== false);
    iv.title = `${answerLabel} with video`;
    iv.setAttribute("aria-label", iv.title);
    iv.onclick = () => { incomingOpen = false; answerCall(e.id, true); };
  }

  const more = $("incomingMore");
  more.hidden = !many;
  more.textContent = `+${rest.length} more`;
  more.setAttribute("aria-expanded", String(incomingOpen));
  more.title = incomingOpen ? "Hide the other calls" : `${rest.length} more ${rest.length > 1 ? "calls" : "call"} waiting: pick one`;

  // The list's rows are kept from one render to the next (they're redrawn every second while
  // it's open, for the waiting times), so a row under the pointer or with focus stays put.
  const list = $("incomingList");
  list.hidden = !incomingOpen;
  const ids = new Set(rest.map((r) => r.id));
  for (const [id, row] of incomingRows) if (!ids.has(id)) { row.li.remove(); incomingRows.delete(id); }
  for (const r of rest) {
    let row = incomingRows.get(r.id);
    if (!row) {
      const mk = (tag, cls, text) => Object.assign(document.createElement(tag), { className: cls, textContent: text ?? "" });
      row = { li: mk("li", "incoming-row"), who: mk("button", "incoming-who"), what: mk("span", "incoming-what"),
        decline: mk("button", "btn btn-danger", "Decline"), answer: mk("button", "btn btn-primary") };
      row.who.type = row.decline.type = row.answer.type = "button";
      const acts = mk("span", "incoming-actions");
      acts.append(row.decline, row.answer);
      row.li.append(row.who, row.what, acts);
      row.who.onclick = () => pickRinging(r.id);
      row.decline.onclick = () => declineCall(r.id);
      // Answered, you're talking to them: the list folds away (the rest still wait in the banner).
      // Declined, it stays open, for the next.
      row.answer.onclick = () => { incomingOpen = false; answerCall(r.id); };
      incomingRows.set(r.id, row);
      list.append(row.li);   // ids only go up, so a new one belongs at the bottom
    }
    row.who.textContent = ringingWho(r, ringing);
    row.who.title = `${r.h.remote}: show the answer view`;
    row.what.textContent = ringingWhat(r, true);
    markShown(row.who, r.id);
    row.answer.textContent = answerLabel;
  }
}
$("incomingMore").onclick = () => { incomingOpen = !incomingOpen; renderIncoming(); };
$("incoming").addEventListener("keydown", (ev) => {
  if (ev.key === "Escape" && incomingOpen) { ev.stopPropagation(); incomingOpen = false; renderIncoming(); $("incomingMore").focus(); }
});

// The grid's tiles, kept from one render to the next (each holds its call's <video>, which
// mustn't be pulled out and put back every second), and the Focus | Grid switch.
export const tiles = new Map();   // call id -> { root, feed, avatar, time, name, status, media, held, heldText, resume }
function makeTile(e) {
  const el = (tag, cls) => Object.assign(document.createElement(tag), { className: cls });
  const t = { root: el("div", "tile"), feed: el("div", "tile-feed"), avatar: el("span", "avatar tile-avatar"), time: el("span", "tile-time"),
    name: el("span", "tile-name"), status: el("span", "tile-status"), media: el("span", "call-sends"), viz: el("canvas", "tile-viz"),
    self: Object.assign(el("video", "tile-self"), { autoplay: true, playsInline: true, muted: true, hidden: true }),
    held: el("div", "held-note"), heldText: el("span", ""), resume: el("button", "btn") };
  t.root.dataset.call = e.id;
  t.root.tabIndex = 0;
  t.root.setAttribute("role", "button");
  t.root.onclick = () => focusCall(e.id);
  t.root.onkeydown = (ev) => { if (ev.target === t.root && (ev.key === "Enter" || ev.key === " ")) { ev.preventDefault(); focusCall(e.id); } };
  t.avatar.setAttribute("aria-hidden", "true");
  const text = el("span", "tile-text");
  text.append(t.name, t.status);
  const meta = el("div", "tile-meta");
  meta.append(text, t.media);
  // On hold: the same note as on the stage in Focus, over the middle of the tile. Resume takes
  // it off hold and selects it, so it has the audio, without leaving the grid.
  t.held.hidden = true;
  t.resume.type = "button";
  t.resume.innerHTML = `${ICON.play}<span>Resume</span>`;
  t.resume.onclick = (ev) => { ev.stopPropagation(); selectCall(e.id); goLive(e); };
  t.held.append(t.heldText, t.resume);
  t.feed.append(t.avatar, t.viz, t.self, t.time, t.held);
  t.root.append(t.feed, meta);
  return t;
}

function renderGrid(grid) {
  const stage = $("stage"), box = $("tiles");
  stage.classList.toggle("grid", grid);
  box.hidden = !grid;
  $("controls").classList.toggle("grid", grid);

  const shown = grid ? gridCalls() : [];
  const ids = new Set(shown.map((e) => e.id));
  box.style.setProperty("--cols", stage.clientWidth < 560 ? 1 : shown.length > 4 ? 3 : 2);
  // Calls leaving the grid (or all of them, back in Focus): their <video> goes back on the stage.
  for (const [id, t] of tiles) {
    if (ids.has(id)) continue;
    const e = entries.get(id);
    if (e && e.el.parentElement !== stage) {
      stage.insertBefore(e.el, $("card"));
      if (e.el.srcObject && e.el.paused) e.el.play().catch(() => {});
    }
    t.root.remove();
    tiles.delete(id);
  }
  shown.forEach((e, i) => {
    let t = tiles.get(e.id);
    if (!t) { t = makeTile(e); tiles.set(e.id, t); }
    if (e.el.parentElement !== t.feed) {
      t.feed.prepend(e.el);
      if (e.el.srcObject && e.el.paused) e.el.play().catch(() => {});
    }
    if (box.children[i] !== t.root) box.insertBefore(t.root, box.children[i] ?? null);
    const c = callStatus(e);
    const name = nameOf(e);
    const m = e.m;
    // What Focus would show: their video while it's arriving (on hold too: with music on hold
    // it keeps coming), and on an audio call the bars.
    const video = !!m?.hasVideo && performance.now() - m.lastFrameAt < NO_FRAMES_MS;
    const audio = !!m && !m.hasVideo && c.status === "connected";
    t.root.className = `tile ${c.status}${video ? " video" : ""}${audio ? " audio" : ""}${e.id === selectedId ? " sel" : ""}${c.muted ? " muted" : ""}${c.held ? " held" : ""}${c.long ? " long" : ""}`;
    t.root.title = [e.h.remote || name, c.statusText, i < 8 ? `${i + 1} opens it` : "Open this call"].join(" · ");
    t.root.setAttribute("aria-label", `${name}, ${c.statusText}`);
    setInitial(t.avatar, name, e.h.remote);
    t.name.textContent = name;
    const tc = contactFor(e.h.remote);
    if (tc) t.name.append(contactMarkEl(tc.name));
    t.status.textContent = c.statusText;
    t.time.textContent = c.since ? fmtElapsed(Date.now() - c.since) : "";
    const media = sendsHtml(c);
    if (t.media.innerHTML !== media) t.media.innerHTML = media;
    // The self view, as in Focus: on a video call, what this call sends them (your camera, your
    // initial while it's off, the "On hold" card while it's held), small in the corner. Self
    // view off hides it here too.
    const self = selfViewOn && m?.videoSender && c.status === "connected" ? videoToSend(e) : null;
    if ((t.self.srcObject?.getVideoTracks()[0] ?? null) !== self) t.self.srcObject = self ? new MediaStream([self]) : null;
    t.self.hidden = !self;
    t.self.classList.toggle("mirror", !!self && self === localTrack("video") && !isTestTrack(self));
    t.held.hidden = !(m && c.status === "connected" && (c.held || e.h.heldByThem));
    if (!t.held.hidden) {
      t.heldText.textContent = c.held ? c.statusText : `${name} put you on hold`;
      t.held.classList.toggle("long", c.long);
      t.resume.hidden = !c.held;
    }
  });
  if (grid) startTiles();
}
initCallList();

// ---- Controls ----

// Call (and Enter) places an audio call, Video call (and Ctrl+Enter) a video call.
$("dialForm").addEventListener("submit", (e) => {
  e.preventDefault();
  callFromField(enterVideo);
});
$("videoCallBtn").onclick = () => callFromField(true);
$("address").addEventListener("keydown", (e) => {
  if (e.key !== "Enter" || !(e.ctrlKey || e.metaKey) || e.altKey) return;
  e.preventDefault();
  if (!$("videoCallBtn").disabled && !$("videoCallBtn").hidden) callFromField(true);
});
function callFromField(video) {
  dial.close();
  const typed = $("address").value.trim();
  // A contact's address (typed, pasted, or picked from the list or the Contacts tab) calls from
  // the contact's account; a recent call picked from the list, from the account that call used.
  // A row picked (and not edited since) shows in the field as its user part: it calls its address.
  const pick = dial.pick && (sameUri(dial.pick.uri, typed) || fieldText(dial.pick.uri) === typed) ? dial.pick : null;
  dial.pick = null;
  const address = pick ? pick.uri : typed;
  const c = contactFor(pick ? pick.uri : fieldUri(typed));
  placeCall(address, c ? c.account : pick?.account, c?.name, video);
}
// A call answered (ours, or theirs to one we placed) empties the address bar, so it's ready for
// the next one; not while you're in it typing something else (another address, say).
let lastDialled = "";
function clearAddressOnAnswer(e) {
  const input = $("address");
  const text = input.value.trim();
  if (!text) return;
  if (document.activeElement === input && text !== lastDialled && !sameUri(text, lastDialled) && !sameUri(fieldUri(text), e.h.remote)) return;
  input.value = "";
  dial.pick = null;
  dial.close();
  log(`call ${e.id} answered: address bar cleared`);
  render();
}

// Call or Video call (video), from the dial bar, Home or History. account: the one to call from (a contact's, or the one that
// call was on): Call from switches to it first, so it shows what's used, unless it's been removed
// or disabled, when the call goes from Call from's account and a note says so. A contact's media
// (desktop only: contacts.js) goes with the call, so the host offers just that. People closes, so
// the stage shows how the call's going.
let placedVideo = false;   // the last call you placed: Video call (the host doesn't say), for its card
export function placeCall(address, account = null, who = null, video = false) {
  if (!address) return;
  const blocked = callBlocked();
  if (blocked) { log(`not calling ${address}: ${blocked}`); return; }
  closeHistory(false);
  dialDone();   // the dial view's done, and Your devices' tests stop: the call takes the devices
  markSeen();   // missed calls count as seen once you next place a call
  renderSide();
  lastDialled = address;
  account = account ? useAccount(account, who) : null;
  // The microphone isn't opened yet: only once they answer (onPhase), so nothing's open, and the
  // indicator shows nothing, while it rings.
  // A new call holds the one you're on, now that you've pressed Call (the new one goes on the
  // stage when the host reports it).
  holdOthers(null);
  const media = (contactFor(address) ?? contactFor(fieldUri(address)))?.media;
  placedVideo = state.videoAllowed !== false && video;
  post({ type: "dial", address, video: placedVideo, ...(account ? { account } : {}), ...(media ? { media } : {}) });
  // If the call never starts (a bad address, say), don't leave the microphone open.
  setTimeout(() => { if (!liveEntries().length) closeMic(); }, 5000);
}
initAddress2();

let lastPhase = null;  // the call phase at the last render, to focus Answer once when ringing starts
let lastSelected = null;   // and the call that was on the stage

// The Answer buttons, kept from one render to the next.
const answerBtn = Object.assign(document.createElement("button"), { type: "button", className: "btn btn-primary answer-btn" });
const answerVideoBtn = Object.assign(document.createElement("button"), { type: "button", className: "btn btn-primary answer-btn" });
answerBtn.onclick = () => answerCall(selectedId, false);
answerVideoBtn.onclick = () => answerCall(selectedId, true);
function setBtn(el, icon, text) {
  const html = `${icon}<span>${text}</span>`;
  if (el.innerHTML !== html) el.innerHTML = html;
}

async function refreshCameras() {
  try {
    const list = await navigator.mediaDevices.enumerateDevices();
    hasMic = list.some((d) => d.kind === "audioinput");
  } catch { /* keep the old list */ }
  render();
}
navigator.mediaDevices.addEventListener?.("devicechange", refreshCameras);
initMenus();
let lossLoaded = false;   // the host's starting values (--loss-audio, --loss-video), taken from the first state
initDiagnostics();

$("hangupBtn").onclick = () => post({ type: "hangup", id: selectedId });
$("holdBtn").onclick = () => { const e = sel(); if (e && holdState(e)) goLive(e); else holdCall(e, true); };
$("heldResume").onclick = () => goLive(sel());
$("heldRetry").onclick = () => $("xferRetry").click();   // as the call header's Try again
$("heldResume").innerHTML = `${ICON.play}<span>Resume</span>`;
// Mute: the selected call's microphone, for that call only. Not in privacy mode: only its own
// button turns that off.
function toggleMute() {
  const e = sel();
  if (!e?.m || privacy) return;
  e.m.muted = !e.m.muted;
  log(`call ${e.id}: ${e.m.muted ? "muted" : "unmuted"}`);
  render();
}
$("muteBtn").onclick = toggleMute;

// Privacy mode (Shift+M), like a browser's private window, and a hard off: on, the microphone and
// camera close, every call gets silence and on video your camera-off card, and calls you place or
// answer start that way too. Mute, the camera button and Your devices' tests are disabled until
// it's off, so nothing else can turn it off. Off again, each call goes back to how it was (one
// that started while it was on, unmuted with its camera off: it never turns a camera on unasked)
// and the microphone opens again for them. Never remembered.
export function togglePrivacy() {
  if (privacy) {
    const was = privacy.calls;
    privacy = null;
    for (const e of entries.values()) {
      if (!e.m) continue;
      const w = was.get(e.id) ?? { muted: false, camOn: false };
      e.m.muted = w.muted;
      if (w.camOn && !e.m.camOn && e.m.videoSender) setCamera(true, e);
    }
    if (liveEntries().length) {
      openMic().then((track) => {
        // The audio view's bars follow the new microphone (syncAudio puts it on the live call).
        if (track) for (const e of entries.values()) if (e.m) { e.m.localAnalyser?.source.disconnect(); e.m.localAnalyser = analyserFor(track); }
        render();
      });
    }
    log("privacy off: calls back as they were");
  } else {
    privacy = { calls: new Map() };
    for (const e of entries.values()) {
      if (!e.m) continue;
      privacy.calls.set(e.id, { muted: e.m.muted, camOn: e.m.camOn });
      e.m.muted = true;
      if (e.m.camOn && e.m.videoSender) setCamera(false, e);
    }
    stopTests();
    closeMic();
    log("privacy on: microphone and camera closed; calls get silence and your camera-off card");
  }
  syncCamera();
  render();
}
// The button: the incognito icon alone while it's off; on, a dark chip with the icon and
// "Privacy mode", as a browser marks a private window. One click turns it on or off: nothing is
// lost either way, since off puts every call back as it was.
{
  const btn = $("privacyBtn");
  btn.innerHTML = `${ICON.incognito}<span class="privacy-text">Privacy mode</span>`;
  btn.onclick = togglePrivacy;
}
function renderPrivacy() {
  const btn = $("privacyBtn"), on = !!privacy;
  btn.classList.toggle("on", on);
  btn.setAttribute("aria-pressed", String(on));
  btn.title = on
    ? "Privacy mode is on: your microphone and camera are closed on every call, new calls too (they get silence and your camera-off card). Click to turn it off (Shift+M)"
    : "Privacy mode: close your microphone and camera on every call, new calls too, until you turn it off (Shift+M)";
}

// Keys for calls (not while typing, or with a menu or dialog open): 1–8 put that call in the
// list on the stage (1 is the top, the oldest; from the grid, in Focus), A answers the caller who has
// waited longest, M mutes the selected call, Shift+M is privacy mode (microphone and camera closed),
// H holds or resumes (M and H not in the grid), G switches Focus and Grid, and Esc leaves an
// ended call's card: for the call you're on, or with none, Home.
document.addEventListener("keydown", (ev) => {
  if (ev.ctrlKey || ev.altKey || ev.metaKey || openMenu) return;
  // Typing doesn't count, except Esc in the empty address (where a call that ends leaves you).
  const emptyAddress = ev.target.id === "address" && !ev.target.value && ev.key === "Escape";
  if (ev.target.closest?.("input, textarea, select, [contenteditable], dialog") && !emptyAddress) return;
  if (!$("accounts").hidden || !$("historyView").hidden) return;
  const key = ev.key.toLowerCase();
  if (/^[1-8]$/.test(ev.key) && !ev.shiftKey) {
    const e = listed()[Number(ev.key) - 1];
    if (!e) return;
    ev.preventDefault();
    focusCall(e.id);
  } else if (key === "a" && !ev.shiftKey) {
    const e = [...entries.values()].filter((x) => x.h.call === "ringing").sort((x, y) => Number(x.id) - Number(y.id))[0];
    if (!e) return;
    ev.preventDefault();
    answerCall(e.id);
  } else if (key === "m") {
    // Like the buttons, M and H act in Focus view only, where it's clear which call they're for.
    if (ev.shiftKey) { ev.preventDefault(); togglePrivacy(); }
    else if (!$("muteBtn").disabled && !inGrid()) { ev.preventDefault(); toggleMute(); }
  } else if (key === "h" && !ev.shiftKey) {
    if ($("holdBtn").disabled || inGrid()) return;
    ev.preventDefault();
    $("holdBtn").click();
  } else if (key === "g" && !ev.shiftKey) {
    ev.preventDefault();
    setView(view === "grid" ? "focus" : "grid");
  } else if (ev.key === "Escape" && state.call === "ended" && !textOpen) {
    ev.preventDefault();
    if (otherLive()) selectCall(otherLive().id);
    else closeEnded(sel());   // Close: Home
  }
});
// The camera button: on a video call, your camera on or off for that call; on an audio call, Add
// video. Nothing before a call, nor in privacy mode.
$("camBtn").onclick = () => {
  if (!call || privacy) return;
  if (call.hasVideo) { setCamera(!call.camOn, sel()); return; }
  sel().addingVideo = true;
  post({ type: "add-video", id: selectedId });
  log("adding video");
  render();
};
$("privacyNoteOff").onclick = togglePrivacy;
$("privacyNote").insertAdjacentHTML("afterbegin", ICON.incognito);
$("incomingVideoAnswer").innerHTML = ICON.video;

chrome.webview.addEventListener("message", async (e) => {
  const msg = e.data;
  try {
    const e = msg.id != null ? entries.get(msg.id) : null;
    if (msg.type === "state") {
      onState(msg);
      takeAccounts(msg);        // before render(): Home's accounts and registrations are this message's
      if (!lossLoaded) {
        lossLoaded = true;
        $("lossAudio").value = msg.lossAudio ?? 0;
        $("lossVideo").value = msg.lossVideo ?? 0;
        showLoss();
      }
      render();
      onAccountsState(state);   // with the selected call's phase as state.call
      onHistoryState(state);
    }
    else if (msg.type === "account-saved" || msg.type === "account-error") onAccountMessage(msg);
    else if (msg.type === "history") onHistoryMessage(msg);
    else if (msg.type === "media-offer") await onMediaOffer(msg.id, msg.sdp, false, !!msg.renegotiate);
    else if (msg.type === "media-request") await onMediaOffer(msg.id, null, !!msg.video, !!msg.renegotiate);
    else if (msg.type === "media-remote-answer") await onMediaAnswer(msg.id, msg.sdp);
    else if (msg.type === "media-health") { if (e?.m) { e.m.txLost = !!msg.txLost; render(); } }
    else if (msg.type === "quality") onQuality(msg);
    else if (msg.type === "details") { if (e) { e.details = msg; if (e.id === selectedId) { renderDetails(); renderInfoLock(); } } }
    else if (msg.type === "text") onTextReceived(msg.id, msg.text);
    else if (msg.type === "dtmf-in") onDtmfIn(msg.id, msg.digit);
    else if (msg.type === "video-refused") { if (e) { e.addingVideo = false; e.videoRefused = true; } log("they didn't take video"); render(); }
    else if (msg.type === "log") log(msg.text, true);
    else if (msg.type === "contacts") onContacts(msg.contacts);
  } catch (err) {
    log(`${msg.type} failed: ${err.message}`);
  }
});

initAccounts({ post, log });
// Call history (history.js). It and Accounts take turns over the stage; picking a call closes it.
initHistory({
  post, log, shortName,
  call: (uri, account, video = false) => placeCall(uri, account, null, video),
  videoAllowed: () => state.videoAllowed !== false,
  // "Back to …": the call on the stage under the view, if it's still going.
  backTo: () => (isLive(sel()) && !sel().gone ? nameOf(sel()) : null),
  closeAccounts,
  contactFor,
  contactCount,
  mountContacts,
  // Add to contacts / Edit contact on a call: the Contacts tab's form, then back to Recent.
  // A new one's name is the one the row shows (the call's, else one from the address), and its
  // Call from the account that call used.
  editContact: (e) => openContacts(contactFor(e.uri) ?? { name: e.name || shortName(e.uri), uri: e.uri, account: e.account ?? null }, "recent"),
});
$("manageAccounts").addEventListener("click", () => closeHistory(false));

// ---- Settings (settings.js), from the gear in the top bar ----
// A device picked in Your devices: the same as picking from the ▾ menus, except a camera picked
// here never turns the camera on: if it's open (a call, or the preview) it switches, otherwise
// it's the one the call about to start opens. On demand: until the last call ends.
export async function chooseDevice(key, id) {
  if (key === "mic") await pickMic(id);
  else if (key === "speaker") await pickDevice("speaker", id);
  else if (key === "camera") {
    devices.camera = id;
    // The selected call, and any call started while it's on; other calls keep theirs.
    if (sel()?.m) sel().cameraId = id;
    log(`camera: ${id || "system default"}`);
    syncCamera();   // switches if the open camera is now the wrong one
  }
  render();
}
// A default picked in Settings: kept, for every call from now on. With no call going it's what's
// in use too; a call that's on keeps what it has.
async function setDefaultDevice(key, id) {
  // The Default camera list's Camera off: video calls start with your camera-off card.
  const camOff = key === "camera" && id === "cam:off";
  if (key === "camera" && !!prefs.cameraOff !== camOff) {
    savePref("cameraOff", camOff);
    log(`default camera: ${camOff ? "off (your camera-off card)" : "on"}`);
    if (!liveEntries().length) setStartCamera(!camOff && params.get("camera") !== "off");
  }
  if (camOff) { render(); return; }
  defaults[key] = id;
  saveDefaults();
  log(`default ${key}: ${id || "system default"}`);
  if (!liveEntries().length) await chooseDevice(key, id);
  else render();
}
export const settings = initSettings({
  log,
  defaults,
  setDefaultDevice,
  inCall: () => liveEntries().length > 0,
  videoAllowed: () => state.videoAllowed !== false,
  diagnostics: () => !$("devPanel").hidden,
  setDiagnostics,
});
initYourDevices();
initHome();

render();            // the UI straight away, before the microphone is open
await openDevices();
render();
syncCamera();
refreshCameras();
post({ type: "ready" });
