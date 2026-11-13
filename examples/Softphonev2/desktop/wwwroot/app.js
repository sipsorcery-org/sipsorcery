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
// The Audio | Video switch beside Call picks the kind of call. An audio call has no video line;
// "Add video" asks the far end for one (a SIP re-INVITE), and the host then sends a new offer.
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

import { initAccounts, onAccountsState, onAccountMessage, closeAccounts, useAccount, accountName } from "./accounts.js";
import { initHistory, onHistoryMessage, onHistoryState, closeHistory, historyEntries, openPeople } from "./history.js";
import { prefs, savePref, initSettings } from "./settings.js";
import { initSelfView } from "./selfview.js";
import { normAddress, contactMark, contactMarkEl } from "./contactmark.js";
import { avatarAttr, setAvatar } from "./avatar.js";
import { initContacts, openContacts, onContacts, allContacts, contactFor, saveContact, removeContact, sameUri, mountContacts, contactCount } from "./contacts.js";

const $ = (id) => document.getElementById(id);
const escHtml = (t) => String(t).replace(/[&<>"]/g, (ch) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[ch]));
const post = (msg) => chrome.webview.postMessage(msg);

const CAMERA_KBPS = 1500;
const NO_FRAMES_MS = 2000;   // how long without video frames before saying their camera is off
const NO_AUDIO_MS = 2000;    // how long without audio packets before saying none is arriving

const ICON = {
  phone: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M22 16.9v3a2 2 0 0 1-2.2 2 19.8 19.8 0 0 1-8.6-3.1 19.5 19.5 0 0 1-6-6A19.8 19.8 0 0 1 2.1 4.2 2 2 0 0 1 4.1 2h3a2 2 0 0 1 2 1.7c.1 1 .4 1.9.7 2.8a2 2 0 0 1-.5 2.1L8 9.9a16 16 0 0 0 6 6l1.3-1.3a2 2 0 0 1 2.1-.4c.9.3 1.8.6 2.8.7a2 2 0 0 1 1.7 2Z"/></svg>',
  video: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="m22 8-6 4 6 4V8Z"/><rect x="2" y="6" width="14" height="12" rx="2"/></svg>',
  copy: '<svg viewBox="0 0 24 24" aria-hidden="true"><rect x="9" y="9" width="12" height="12" rx="2"/><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"/></svg>',
  check: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M20 6 9 17l-5-5"/></svg>',
  videoOff: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="m22 8-6 4 6 4V8Z"/><rect x="2" y="6" width="14" height="12" rx="2"/><path d="M2 2l20 20"/></svg>',
  mic: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 2a3 3 0 0 0-3 3v7a3 3 0 0 0 6 0V5a3 3 0 0 0-3-3Z"/><path d="M19 10v2a7 7 0 0 1-14 0v-2"/><path d="M12 19v3"/></svg>',
  block: '<svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="12" cy="12" r="9"/><path d="m5.6 5.6 12.8 12.8"/></svg>',
  micOff: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 2a3 3 0 0 0-3 3v7a3 3 0 0 0 6 0V5a3 3 0 0 0-3-3Z"/><path d="M19 10v2a7 7 0 0 1-14 0v-2"/><path d="M12 19v3"/><path d="M3 3l18 18"/></svg>',
  chevronDown: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="m6 9 6 6 6-6"/></svg>',
  grid: '<svg viewBox="0 0 24 24" aria-hidden="true"><rect x="3" y="3" width="7.5" height="7.5" rx="1.5"/><rect x="13.5" y="3" width="7.5" height="7.5" rx="1.5"/><rect x="3" y="13.5" width="7.5" height="7.5" rx="1.5"/><rect x="13.5" y="13.5" width="7.5" height="7.5" rx="1.5"/></svg>',
  text: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z"/><path d="M8 8h8M12 8v6"/></svg>',
  close: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M18 6 6 18M6 6l12 12"/></svg>',
  collapse: '<svg viewBox="0 0 24 24" aria-hidden="true"><rect x="3" y="3" width="18" height="18" rx="2"/><path d="M15 3v18M8 9l3 3-3 3"/></svg>',
  send: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M22 2 11 13M22 2l-7 20-4-9-9-4z"/></svg>',
  lock: '<svg viewBox="0 0 24 24" aria-hidden="true"><rect x="4" y="11" width="16" height="10" rx="2"/><path d="M8 11V7a4 4 0 0 1 8 0v4"/></svg>',
  unlock: '<svg viewBox="0 0 24 24" aria-hidden="true"><rect x="4" y="11" width="16" height="10" rx="2"/><path d="M8 11V7a4 4 0 0 1 7.9-1"/></svg>',
  // Out and back: the round trip.
  selfView: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M21 9V6a2 2 0 0 0-2-2H4a2 2 0 0 0-2 2v10c0 1.1.9 2 2 2h4"/><rect x="12" y="13" width="10" height="7" rx="2"/></svg>',
  transfer: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="m18 2 4 4-4 4"/><path d="M14 6h8"/><path d="M22 16.92v3a2 2 0 0 1-2.18 2 19.79 19.79 0 0 1-8.63-3.07 19.5 19.5 0 0 1-6-6 19.79 19.79 0 0 1-3.07-8.67A2 2 0 0 1 4.11 2h3a2 2 0 0 1 2 1.72c.13.96.36 1.9.7 2.81a2 2 0 0 1-.45 2.11L8.09 9.91a16 16 0 0 0 6 6l1.27-1.27a2 2 0 0 1 2.11-.45c.91.34 1.85.57 2.81.7A2 2 0 0 1 22 16.92z"/></svg>',
  keypad: '<svg viewBox="0 0 24 24" aria-hidden="true"><rect x="3" y="3" width="18" height="18" rx="2"/><path d="M3 9h18M3 15h18M9 3v18M15 3v18"/></svg>',
  pause: '<svg viewBox="0 0 24 24" aria-hidden="true"><rect x="6" y="4" width="4" height="16" rx="1"/><rect x="14" y="4" width="4" height="16" rx="1"/></svg>',
  play: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M6 4l14 8-14 8z"/></svg>',
  hangup: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M10.68 13.31a16 16 0 0 0 3.41 2.6l1.27-1.27a2 2 0 0 1 2.11-.45c.91.34 1.85.57 2.81.7a2 2 0 0 1 1.72 2v3a2 2 0 0 1-2.18 2 19.79 19.79 0 0 1-8.63-3.07 19.42 19.42 0 0 1-3.33-2.67m-2.67-3.34a19.79 19.79 0 0 1-3.07-8.63A2 2 0 0 1 4.11 2h3a2 2 0 0 1 2 1.72c.13.96.36 1.9.7 2.81a2 2 0 0 1-.45 2.11L8.09 9.91"/><path d="M22 2 2 22"/></svg>',
};

let local = null;          // our tracks: the microphone during a call, the camera while it's wanted
let hostState = { calls: [] };   // the host's last state message
// The host's state with the selected call's fields on top (call, remote, reason, incomingVideo,
// text, held, heldByThem), as the page had it when there was one call. See deriveState.
let state = { call: "idle" };
let pc = null;             // the selected call's peer connection
let call = null;           // the selected call's media state, see onMediaOffer
let answerCamera = null;   // the Answer button's menu: camera on (true) or off (false) for the call being answered; null follows camPref
let keypadOpen = false;    // the keypad, in the dock: see renderKeypad
let keypadFor = null;      // the call it was opened on: it closes if another is selected
let keypadDigits = "";     // the tones sent since it opened

// ---- The calls ----
// Every call the host reports, by id: { id, h (the host's view of it), m (its media state, once
// connected), pc, el (its <video>), connectedAt, heldAt, quality, details, codecs, text,
// addingVideo, videoRefused, gone (the host has dropped it, but it's still on the stage) }.
const entries = new Map();
let selectedId = null;     // the call on the stage
let detailsShown = "";     // the selected call and its state Call details were last drawn for
let detailsHtml = "";      // what Call details last drew
let audioId = null;        // the call with the microphone and speaker
const sel = () => entries.get(selectedId) ?? null;
const isLive = (e) => ["calling", "ringing", "connected"].includes(e?.h.call);
const liveEntries = () => [...entries.values()].filter(isLive);
// Newest first, as the Active calls list shows them (and 1–8 pick them).
// Oldest first: a call keeps its place in the list while others come and go, and new ones go at
// the bottom, so where a call is stays where you remember it.
// A call you're placing while on another isn't in it until it's answered (see onNewCall).
const listed = () => [...entries.values()].filter((e) => !e.gone && !e.pending).sort((a, b) => Number(a.id) - Number(b.id));
const nameOf = (e) => shortName(e?.h.remote);

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

function dropEntry(e) {
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
function selectCall(id) {
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
const holdState = (e) => e.holdWanted ?? !!e.h.held;
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
let view = prefs.view === "grid" ? "grid" : "focus";
// The calls in the grid, oldest first like the list: the live ones but those ringing in, and the
// selected one even while it's ending, so it doesn't vanish.
const gridCalls = () => [...entries.values()].filter((e) => (!e.gone && !e.pending && isLive(e) && e.h.call !== "ringing") || e.id === selectedId)
  .sort((a, b) => Number(a.id) - Number(b.id));
function inGrid() {
  const e = sel();
  return view === "grid" && !!e && e.h.call !== "ringing" && gridCalls().length > 1;
}
function setView(v) {
  if (v === view) return;
  view = v;
  savePref("view", v);
  log(`view: ${v}`);
  settings?.refresh();
  render();
}
// A call you pick (its row, its tile, its number key) becomes the one you're talking to (goLive):
// a held call resumes, and the live one goes on hold. Picked from the grid, it opens in Focus.
function focusCall(id) {
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
    const want = on && !e.h.heldByThem ? mic : sender ? quietAudio(e) : null;
    if (sender && sender.track !== want) sender.replaceTrack(want).catch((err) => log(`audio switch failed: ${err.message}`));
  }
  // The music plays only while a call is sending it.
  if (holdAudio && ![...entries.values()].some((e) => e.m?.audioSender?.track === holdAudio.music.stream.getAudioTracks()[0])) stopHoldMusic();
}

// ---- Music on hold, and silence ----
// A call that isn't the live one sends music (held, music on hold) or silence (held the SIP way,
// they've held us, or briefly while a call is switching): never nothing, so the stream keeps
// flowing, the far end's media timers stay happy and our NAT binding stays open. Silence on a call
// they've held the SIP way is more than SIP asks for: our side is recvonly, so the host sends it
// past the RTP session (MediaRelay.SendPastTheirHold).
const quietAudio = (e) => (holdState(e) && e.h.holdMode !== "sip" ? holdMusicTrack() : silenceTrack());

// The music is a loop rendered once (a few chords, picked out gently), then played round and
// round: no timers, so it doesn't stutter when the window is minimised and timers are throttled.
let holdAudio = null;   // { ctx, music, silence, buffer, source }
const HOLD_BPM = 84;
const HOLD_CHORDS = [[53, 57, 60, 64], [52, 55, 59, 62], [50, 53, 57, 60], [48, 52, 55, 59]];   // Fmaj7 Em7 Dm7 Cmaj7
const HOLD_ARP = [0, 1, 2, 3, 2, 1, 2, 3];

function holdAudioContext() {
  if (!holdAudio) {
    const ctx = new AudioContext({ sampleRate: 48000 });
    const music = ctx.createMediaStreamDestination();
    const silence = ctx.createMediaStreamDestination();
    // Something always connected, so the silent track has frames to send.
    const zero = ctx.createConstantSource();
    zero.offset.value = 0;
    zero.connect(silence);
    zero.start();
    holdAudio = { ctx, music, silence, buffer: null, source: null };
    renderHoldMusic().then((buffer) => { holdAudio.buffer = buffer; if (holdAudio.wanted) startHoldMusic(); })
      .catch((err) => log(`hold music failed: ${err.message}`));
  }
  holdAudio.ctx.resume().catch(() => {});
  return holdAudio;
}

async function renderHoldMusic() {
  const step = 60 / HOLD_BPM / 2;   // eighth notes
  const steps = HOLD_CHORDS.length * 16;
  const ctx = new OfflineAudioContext(1, Math.ceil(steps * step * 48000), 48000);
  const out = ctx.createGain();
  out.gain.value = 0.22;
  out.connect(ctx.destination);
  const note = (midi, at, len, level) => {
    const o = ctx.createOscillator();
    o.type = "triangle";
    o.frequency.value = 440 * 2 ** ((midi - 69) / 12);
    const g = ctx.createGain();
    g.gain.setValueAtTime(0.0001, at);
    g.gain.linearRampToValueAtTime(level, at + 0.03);
    g.gain.exponentialRampToValueAtTime(0.0001, at + len);
    o.connect(g).connect(out);
    o.start(at);
    o.stop(at + len + 0.05);
  };
  for (let i = 0; i < steps; i++) {
    const chord = HOLD_CHORDS[Math.floor(i / 16)];
    const at = i * step;
    // The last notes of the loop are cut short, so nothing rings over the join.
    const left = (steps - i) * step - 0.05;
    note(chord[HOLD_ARP[i % 8]] + 12, at, Math.min(step * 2.5, left), 0.35);
    if (i % 16 === 0) for (const m of chord.slice(0, 3)) note(m, at, Math.min(step * 15, left), 0.12);
  }
  return ctx.startRendering();
}

function startHoldMusic() {
  const h = holdAudio;
  if (h.source || !h.buffer) return;
  h.source = Object.assign(h.ctx.createBufferSource(), { buffer: h.buffer, loop: true });
  h.source.connect(h.music);
  h.source.start();
}

function holdMusicTrack() {
  const h = holdAudioContext();
  h.wanted = true;
  startHoldMusic();
  return h.music.stream.getAudioTracks()[0];
}

function stopHoldMusic() {
  holdAudio.wanted = false;
  holdAudio.source?.stop();
  holdAudio.source = null;
}

const silenceTrack = () => holdAudioContext().silence.stream.getAudioTracks()[0];

// On a video call held with music on hold, they see this card instead of your camera: a pause
// sign and "On hold", a few frames a second, like the camera-off placeholder.
const holdCards = new Map();   // name -> { name, canvas, track, timer }
function holdCardTrack(e) {
  const name = meOf(e);
  let c = holdCards.get(name);
  if (!c) {
    const canvas = Object.assign(document.createElement("canvas"), { width: 1280, height: 720 });
    const track = canvas.captureStream(0).getVideoTracks()[0];
    c = { name, canvas, track, timer: 0 };
    c.timer = setInterval(() => holdCardFrame(c), 1000 / PLACEHOLDER_FPS);
    holdCards.set(name, c);
    holdCardFrame(c);
  }
  return c.track;
}
function holdCardFrame(c) {
  const { canvas, track } = c;
  const g = canvas.getContext("2d");
  const { width: w, height: h } = canvas;
  g.fillStyle = "#3a3324";
  g.fillRect(0, 0, w, h);
  g.fillStyle = "#e8d3a2";
  g.fillRect(w / 2 - 44, h / 2 - 110, 28, 110);
  g.fillRect(w / 2 + 16, h / 2 - 110, 28, 110);
  g.font = '300 64px "Segoe UI", system-ui, sans-serif';
  g.textAlign = "center";
  g.textBaseline = "middle";
  g.fillText("On hold", w / 2, h / 2 + 70);
  g.font = '300 32px "Segoe UI", system-ui, sans-serif';
  g.fillStyle = "#bfae86";
  g.fillText(c.name ? `${c.name} will be back shortly` : "Back shortly", w / 2, h / 2 + 130);
  track.requestFrame();
}
// Stops the hold cards no call is sending.
function stopHoldCards() {
  for (const [name, c] of holdCards) {
    if ([...entries.values()].some((e) => e.m?.videoSender?.track === c.track)) continue;
    clearInterval(c.timer);
    c.track.stop();
    holdCards.delete(name);
  }
}

// The Audio | Video switch: always Audio at start (most SIP calls go to phones, and the camera
// stays off until you ask for video).
let callType = "audio";
const params = new URLSearchParams(location.search);
if (params.get("call") === "video") callType = "video";          // --video (testing)
// Whether calls start with your camera on: the Camera button before a call, and Settings.
// Remembered (see setCamPref); --camera-off starts it off. The Answer button's ▾ can override it
// for one call.
let camPref = params.get("camera") === "off" ? false : prefs.camOn !== false;
// A choice you made (the Camera button, a camera picked, Settings) is kept for next time.
function setCamPref(on) {
  camPref = on;
  if (on) { cameraInUse = false; privacyRelease(); }   // a fresh try, because you asked
  savePref("camOn", on);
  log(`calls start with camera ${on ? "on" : "off"}`);
}
const testAddVideo = Number(params.get("addvideo")) || 0;        // --add-video <s> (testing)
const testSay = params.get("say") ?? "";                           // --say <text> (testing)

const localTrack = (kind) => local?.getTracks().find((t) => t.kind === kind) ?? null;

function log(text, fromHost = false) {
  if (!fromHost) post({ type: "log", text });
  const el = $("log");
  // Follows the newest line, unless you've scrolled up to read something.
  const atEnd = el.hidden || el.scrollHeight - el.scrollTop - el.clientHeight < 24;
  el.textContent += text + "\n";
  if (el.textContent.length > 60000) el.textContent = el.textContent.slice(-40000);
  if (atEnd) el.scrollTop = el.scrollHeight;
}

// "Alice <sip:alice@example.com>" → "Alice"; "sip:bob@host" → "Bob".
function shortName(remote) {
  const m = /^\s*(.+?)\s*</.exec(remote ?? "");
  if (m) return m[1];
  const u = /^(?:sips?:|tel:)?([^@;>]+)/i.exec(remote ?? "");
  const user = u ? u[1] : remote || "Them";
  return /^[+\d*#]+$/.test(user) ? user : user.charAt(0).toUpperCase() + user.slice(1);
}
// The address without its URI parameters and headers, which say nothing to a person:
// "Bob <sip:bob@host:5060;transport=tcp?X-Y=1>;tag=9" → "Bob <sip:bob@host:5060>",
// "sip:bob@host;transport=tcp" → "sip:bob@host". Call details has the full one.
function plainAddress(remote) {
  if (!remote) return "";
  const strip = (u) => u.replace(/[;?].*$/, "");
  const m = /^(.*<)([^>]*)>/.exec(remote);
  return m ? `${m[1]}${strip(m[2])}>` : strip(remote.trim());
}
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
const initial = (name) => (name ?? "?").replace(/^\+/, "").charAt(0).toUpperCase() || "#";

// The microphone is only open during a call (see openMic). The camera is only opened while it's wanted (see
// wantedSources), so on an audio call, or with the switch on Audio, it's off: no self view, and
// the camera's light stays off.
// A camera another app (or another copy of this one) is using can't be opened on Windows. That
// isn't "no camera", so it says "Camera in use", and the call sends the placeholder. It is NOT
// retried by itself: the camera is only ever opened because of something you did (switching to
// Video, placing or answering a video call, Add video, Camera on). Otherwise it would start
// streaming on its own the moment the other app let go.
// The microphone, speaker and camera you picked from the menus on Mute and Camera, remembered
// between runs. Empty means the system default.
let devices = { mic: "", speaker: "", camera: "" };
try { Object.assign(devices, JSON.parse(localStorage.getItem("devices") ?? "{}")); } catch { /* storage unavailable */ }
const saveDevices = () => { try { localStorage.setItem("devices", JSON.stringify(devices)); } catch { /* storage unavailable */ } };
const MIC_CONSTRAINTS = { echoCancellation: true, noiseSuppression: true, autoGainControl: true };

let hasCamera = false;     // a camera exists (and hasn't failed to open for good)
let cameraInUse = false;   // the last try found it in use; cleared by your next camera action
let cameraBusy = null;     // the open or close in progress, so they never overlap

// Like the camera, the microphone is only open while it's needed: from placing or answering a
// call until it ends. So the mic light (on a webcam, say) is off between calls. Mute works
// before a call too: it's remembered, and the call's microphone opens muted.
let hasMic = false;        // a microphone exists
let micMuted = false;
// The privacy switch at the top (Shift+M): while it's on, every call is muted with its camera
// off, and so is each new one. It holds what each call had before, for turning it off again.
// It's never remembered: the app always starts with it off. Unmuting a call or turning a camera
// on turns it off without putting the other calls back (see privacyRelease).
let privacy = null;        // null, or { calls: Map(id -> { muted, camOn }) }
const startMuted = () => micMuted || !!privacy;     // a new call's microphone
const camStart = () => camPref && !privacy;         // a new video call's camera
let micOpening = null;    // the open in progress, so placing a call and its media share one

async function openDevices() {
  local = new MediaStream();
  try {
    const list = await navigator.mediaDevices.enumerateDevices();
    hasMic = list.some((d) => d.kind === "audioinput");
    hasCamera = list.some((d) => d.kind === "videoinput");
  } catch { hasMic = hasCamera = false; }
}

// Opens the microphone (the remembered one, or the default if it's gone) unless it's open.
// Resolves to the track, or null if there's no microphone to be had.
function openMic() {
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

// Which video sources should be open ("camera", "test", "bars"): each call's own, while it's
// sending video with your camera on, and before a call, while the switch is on Video, the
// source new calls start with (a preview of what they'll see).
function wantedSources() {
  const want = new Set();
  if (!local) return want;   // not before the microphone's open (local is set)
  const all = [...entries.values()];
  // Any call sending (not on hold).
  for (const e of all) if (e.m?.hasVideo && e.m.camOn && !holdState(e) && e.h.call === "connected") want.add(sourceOf(e));
  if (!call) {
    if (all.some((e) => e.h.call === "connected" && !e.m)) {
      // Connected, but the media isn't set up yet: leave what's open as it is, so a video
      // call's preview carries straight on into the call.
      for (const kind of openSources()) want.add(kind);
    } else {
      const before = state.call === "idle" || state.call === "ended" || state.call === "calling";
      // The preview is only for you, so with Self view off nothing opens until the call.
      if (before && state.videoAllowed !== false && callType === "video" && camStart() && selfViewOn) want.add(videoSource);
    }
  }
  if (!sourceAvailable("camera")) want.delete("camera");
  return want;
}

// The sources open now.
function openSources() {
  const open = new Set(sources.keys());
  if (localTrack("video")) open.add("camera");
  return open;
}

const sameSources = (a, b) => a.size === b.size && [...a].every((k) => b.has(k));

// While your camera is off (or there's none) on a video call, the call sends a placeholder
// instead of nothing: your initial on a plain background, a few frames a second. Video never
// stops, so every SIP phone just sees a picture rather than a frozen frame or a dead stream,
// NAT bindings and media timers stay alive, and turning the camera on is a track swap with the
// encoder already running. It's drawn on a canvas and sent at the camera's size, so the far
// end doesn't see the picture change shape. The initial is the call's account's (e.h.me): with
// two accounts, Bob's call shows B and Dave's D. One placeholder per name, shared by its calls.
const PLACEHOLDER_FPS = 5;   // a still picture costs a few kbps; much sparser upsets some decoders
const placeholders = new Map();   // name -> { name, canvas, track, timer }

// The name we go by on a call: its account's (none when the desktop can't tell which account
// it's on: no name then, never another call's), else (before a call, or the web softphone) ours.
const meOf = (e) => (e?.h && e.h.me !== undefined ? e.h.me : state.me) ?? "";

function placeholderTrack(e = null) {
  const name = meOf(e);
  let p = placeholders.get(name);
  if (!p) {
    const canvas = Object.assign(document.createElement("canvas"), { width: 1280, height: 720 });
    // Frame rate 0 and requestFrame: a frame goes out each time one is drawn and asked for. A
    // canvas only produces a frame after a draw, so the picture is redrawn every time.
    const track = canvas.captureStream(0).getVideoTracks()[0];
    p = { name, canvas, track, timer: 0 };
    p.timer = setInterval(() => placeholderFrame(p), 1000 / PLACEHOLDER_FPS);
    placeholders.set(name, p);
    placeholderFrame(p);
  }
  return p.track;
}

function placeholderFrame(p) {
  const { canvas, track } = p;
  const label = p.name ? initial(p.name) : "";
  const g = canvas.getContext("2d");
  const { width: w, height: h } = canvas;
  g.fillStyle = "#1f3a31";
  g.fillRect(0, 0, w, h);
  g.strokeStyle = g.fillStyle = "#a9c2b8";
  g.lineWidth = 4;
  g.beginPath();
  g.arc(w / 2, h / 2, 120, 0, 2 * Math.PI);
  g.stroke();
  if (label) {
    g.font = '300 120px "Segoe UI", system-ui, sans-serif';
    g.textAlign = "center";
    g.textBaseline = "middle";
    g.fillText(label, w / 2, h / 2 + 6);
  } else {
    // No name to show: a head and shoulders.
    g.beginPath();
    g.arc(w / 2, h / 2 - 30, 38, 0, 2 * Math.PI);
    g.fill();
    g.beginPath();
    g.ellipse(w / 2, h / 2 + 70, 70, 50, 0, Math.PI, 2 * Math.PI);
    g.fill();
  }
  track.requestFrame();
}

// Stops the placeholders no call with video could be sending (by name, so one that's just being
// put on a call stays) and that aren't showing (keep: the self view's track).
function stopPlaceholders(keep = null) {
  for (const [name, p] of placeholders) {
    if (p.track === keep || [...entries.values()].some((e) => e.m?.videoSender && meOf(e) === name)) continue;
    clearInterval(p.timer);
    p.track.stop();
    placeholders.delete(name);
  }
}

// ---- Test pattern (diagnostics) ----
// In diagnostics mode, a call's video source can be a test pattern instead of the camera. It
// stands in for the camera exactly: it's opened and closed when the camera would be, and
// Camera off swaps in the placeholder as usual. Each call has its own source, picked from the
// Camera ▾ while it's the selected call, so two calls can send different pictures (the test
// pattern on one, the camera on the other). The camera itself is one device, shared by every
// call sending it; the test pattern and audio bars are one canvas each, likewise shared. The
// source last picked is the one the preview and new calls start with. The picture has
// colour bars and a grey ramp (colour and banding), a dial whose hand steps once per frame
// drawn (a dropped frame shows as a skipped tick), a clock to the millisecond (compare the
// receiver's picture with the sender's screen for the delay) and a frame counter. It's picked
// from the Camera button's menu.
// "Audio bars" is the other diagnostic source: your microphone's frequency bars, drawn the way
// the audio-only view draws them, with the same clock. The far end sees what you're sending as
// audio, so they can tell sound from silence (or a muted or dead microphone) at a glance, and
// judge lip sync: a bar should jump as they hear the sound. The microphone only opens with a
// call, so before one the picture says so.
const TEST_FPS = 30;
// "camera", "test" or "bars": what the preview and new calls start with (a call keeps its own
// in e.source). Everything goes back to the camera when diagnostics goes off.
let videoSource = "camera";
// The open test sources by kind: { kind, canvas, track, timer, frame, and for bars the analyser
// and levels }. The camera, when open, is the video track in `local`.
const sources = new Map();
const SOURCE_LABEL = { test: "Test pattern", bars: "Audio bars" };

// A call's source; before its media is set up, once the call has ended (its card can stay on the
// stage), or with no call, the one new calls start with. (An ended call keeps what it had, and a
// pick then changes only the next call's, so its own would be stale.)
const sourceOf = (e) => (e?.m ? e.source : null) ?? videoSource;
// Likewise the camera (device): each call keeps the one it was given or picked, so picking a
// camera for one call doesn't change another's. One camera is open at a time, the one the call
// sending it wants (only the live call sends it: the others are held), switched when you go to
// a call that wants another (see syncCameraTrack).
const cameraIdOf = (e) => (e?.m ? e.cameraId : null) ?? devices.camera;
let cameraOpenedFor = null;   // the device asked for when the open camera was opened
// Whether a source can be opened: the test sources always; a camera if there is one and it
// isn't in use elsewhere.
const sourceAvailable = (kind) => kind !== "camera" || (hasCamera && !cameraInUse);
const videoAvailable = () => sourceAvailable(videoSource);
const isTestTrack = (track) => !!track && [...sources.values()].some((tp) => tp.track === track);
const sourceTrack = (kind) => (kind === "camera" ? localTrack("video") : sources.get(kind)?.track ?? null);

function openTestPattern(kind) {
  const canvas = Object.assign(document.createElement("canvas"), { width: 1280, height: 720 });
  const track = canvas.captureStream(0).getVideoTracks()[0];
  const tp = {
    kind, canvas, track, frame: 0, timer: 0,
    analyser: null, micTrack: null, levels: new Float32Array(BANDS), target: new Float32Array(BANDS), last: performance.now(),
  };
  const draw = () => (kind === "bars" ? audioBarsFrame(tp) : patternFrame(tp));
  tp.timer = setInterval(draw, 1000 / TEST_FPS);
  sources.set(kind, tp);
  draw();
  return track;
}

function closeTestPattern(kind) {
  const tp = sources.get(kind);
  if (!tp) return;
  clearInterval(tp.timer);
  tp.analyser?.source.disconnect();
  tp.track.stop();
  sources.delete(kind);
}

// The band along the bottom of every diagnostic source: a clock to the millisecond, who's
// sending, size and rate, and a frame counter.
function sourceFooter(g, W, H, frame) {
  const pad = (n, w = 2) => String(n).padStart(w, "0");
  const now = new Date();
  g.textBaseline = "middle";
  g.fillStyle = "#101418";
  g.fillRect(0, 530, W, H - 530);
  g.fillStyle = "#fff";
  g.font = "600 64px Consolas, monospace";
  g.textAlign = "center";
  g.fillText(`${pad(now.getHours())}:${pad(now.getMinutes())}:${pad(now.getSeconds())}.${pad(now.getMilliseconds(), 3)}`, W / 2, 600);
  g.font = "28px Consolas, monospace";
  g.fillStyle = "#9fb3ad";
  g.textAlign = "left";
  g.fillText(state.me || state.address || "", 40, 680);
  g.textAlign = "center";
  g.fillText(`${W}×${H} · ${TEST_FPS} fps`, W / 2, 680);
  g.textAlign = "right";
  g.fillText(`frame ${pad(frame, 6)}`, W - 40, 680);
}

function closeCamera(track) {
  local.removeTrack(track);
  track.stop();
}

function patternFrame(tp) {
  const { canvas, track } = tp;
  const frame = tp.frame++;
  const g = canvas.getContext("2d");
  const W = canvas.width, H = canvas.height, pad = (n, w = 2) => String(n).padStart(w, "0");
  g.textBaseline = "middle";
  g.lineCap = "butt";

  // 75% colour bars, a stepped grey ramp, and a smooth gradient for banding.
  const bars = ["#c0c0c0", "#c0c000", "#00c0c0", "#00c000", "#c000c0", "#c00000", "#0000c0"];
  const bw = W / bars.length, barH = 430;
  bars.forEach((c, i) => { g.fillStyle = c; g.fillRect(Math.round(i * bw), 0, Math.ceil(bw), barH); });
  for (let i = 0; i < 11; i++) {
    const v = Math.round(i * 25.5);
    g.fillStyle = `rgb(${v},${v},${v})`;
    g.fillRect(Math.round(i * W / 11), barH, Math.ceil(W / 11), 70);
  }
  const grad = g.createLinearGradient(0, 0, W, 0);
  grad.addColorStop(0, "#000");
  grad.addColorStop(1, "#fff");
  g.fillStyle = grad;
  g.fillRect(0, barH + 70, W, 30);
  // A marker across the ramp, once every 4 s: smooth motion.
  g.fillStyle = "#ff2d55";
  g.fillRect(((frame % (TEST_FPS * 4)) / (TEST_FPS * 4)) * W - 3, barH, 6, 100);

  sourceFooter(g, W, H, frame);

  // The dial: one tick per frame of a second, the hand on this frame's tick.
  const cx = W / 2, cy = 215, r = 150, tick = frame % TEST_FPS;
  g.fillStyle = "rgba(16,20,24,0.88)";
  g.beginPath();
  g.arc(cx, cy, r + 18, 0, 2 * Math.PI);
  g.fill();
  for (let i = 0; i < TEST_FPS; i++) {
    const a = (i / TEST_FPS) * 2 * Math.PI - Math.PI / 2, lit = i === tick;
    g.strokeStyle = lit ? "#ffd60a" : "#56645f";
    g.lineWidth = lit ? 6 : 3;
    g.beginPath();
    g.moveTo(cx + Math.cos(a) * (r - 22), cy + Math.sin(a) * (r - 22));
    g.lineTo(cx + Math.cos(a) * r, cy + Math.sin(a) * r);
    g.stroke();
  }
  const a = (tick / TEST_FPS) * 2 * Math.PI - Math.PI / 2;
  g.strokeStyle = "#ffd60a";
  g.lineWidth = 8;
  g.lineCap = "round";
  g.beginPath();
  g.moveTo(cx, cy);
  g.lineTo(cx + Math.cos(a) * (r - 30), cy + Math.sin(a) * (r - 30));
  g.stroke();
  g.fillStyle = "#fff";
  g.font = "600 36px Consolas, monospace";
  g.textAlign = "center";
  // The tick number sits opposite the hand, so the hand never covers it.
  g.fillText(pad(tick), cx - Math.cos(a) * 62, cy - Math.sin(a) * 62);

  track.requestFrame();
}

function audioBarsFrame(tp) {
  const { canvas, track } = tp;
  const frame = tp.frame++;
  const g = canvas.getContext("2d");
  const W = canvas.width, H = canvas.height;

  // Follow the microphone: it opens with the call, and can be switched mid-call.
  const mic = localTrack("audio");
  if (mic !== tp.micTrack) {
    tp.analyser?.source.disconnect();
    tp.analyser = mic ? analyserFor(mic) : null;
    tp.micTrack = mic;
  }
  const level = bands(mic?.enabled ? tp.analyser : null, tp.target);
  const t = performance.now(), fall = Math.exp(-Math.min(250, t - tp.last) / FALL_MS);
  tp.last = t;
  for (let b = 0; b < BANDS; b++) {
    const v = tp.levels[b], to = tp.target[b];
    tp.levels[b] = to > v ? v + (to - v) * 0.6 : Math.max(to, v * fall);
  }

  g.fillStyle = "#0f1a17";
  g.fillRect(0, 0, W, H);
  g.textBaseline = "middle";

  // Top: what this is, and the microphone's state.
  g.font = "600 30px 'Segoe UI', system-ui, sans-serif";
  g.textAlign = "left";
  g.fillStyle = "#e4ece8";
  // Below where the far end's name-and-timer overlay sits.
  g.fillText("Audio bars", 48, 116);
  const status = !mic ? "Microphone opens with the call" : !mic.enabled ? "Muted" : mic.label;
  g.font = "24px 'Segoe UI', system-ui, sans-serif";
  g.fillStyle = !mic || !mic.enabled ? "#e6b86a" : "#9fb3ad";
  g.fillText(status, 48, 156);
  // A level meter, top right.
  g.fillStyle = "#24332e";
  g.fillRect(W - 48 - 320, 108, 320, 16);
  g.fillStyle = "#4cc29b";
  g.fillRect(W - 48 - 320, 108, 320 * Math.min(1, level * 2), 16);

  // The bars, mirrored about a centre line, as in the audio-only view.
  const mid = 340, room = 130, left = 64, width = W - 128;
  const slot = width / BANDS, bar = slot * 0.6, gap = (slot - bar) / 2;
  g.fillStyle = "#3ea486";
  for (let b = 0; b < BANDS; b++) {
    const len = Math.max(4, tp.levels[b] * room);
    g.beginPath();
    g.roundRect(left + b * slot + gap, mid - len, bar, len * 2, Math.min(bar, len) / 2);
    g.fill();
  }
  g.fillStyle = "#24332e";
  g.fillRect(left, mid - 1, width, 2);
  g.font = "18px Consolas, monospace";
  g.fillStyle = "#677670";
  g.textAlign = "left";
  g.fillText("100 Hz", left, mid + room + 30);
  g.textAlign = "right";
  g.fillText("6 kHz", left + width, mid + room + 30);

  sourceFooter(g, W, H, frame);
  track.requestFrame();
}

// Switches the video source for a call (e), and for the preview and new calls; with no call,
// only the latter. With reopen, the camera closes first, so a newly picked camera opens.
// syncCamera opens what's now wanted and closes what isn't (so a camera that's off stays off).
function setVideoSource(source, e = null, reopen = false) {
  const changed = source !== videoSource || (e?.m && e.source !== source);
  if (!changed && !reopen) return;
  videoSource = source;
  if (e?.m) e.source = source;
  if (source === "camera") cameraInUse = false;   // a fresh try, because you asked
  if (changed) log(`video source${e?.m ? ` for call ${e.id}` : ""}: ${source}`);
  if (!local) return;   // at start, before the devices are open; the first syncCamera picks it up
  const cam = localTrack("video");
  if (reopen && cam) closeCamera(cam);
  syncCamera();
}

// What a call's video sender should carry: its source when the camera's on and the source is
// open, else the placeholder. On hold, the "On hold" card (music on hold) or nothing (SIP hold).
const videoToSend = (e) => (holdState(e) ? (e.h.holdMode === "sip" ? null : holdCardTrack(e))
  : (e.m.camOn && sourceTrack(sourceOf(e))) || placeholderTrack(e));
const anyVideoSender = () => [...entries.values()].some((e) => e.m?.videoSender);

// Puts what's sent (or, before a call, what would be) in the self view. Only the camera is
// mirrored, so the placeholder's initial reads the right way round. The placeholder is stopped
// once neither the self view nor a call needs it.
function showSelfVideo(show) {
  const el = $("self");
  const track = !show ? null : call?.videoSender ? videoToSend(sel()) : sourceTrack(videoSource) ?? placeholderTrack(sel());
  if ((el.srcObject?.getVideoTracks()[0] ?? null) !== track) el.srcObject = track ? new MediaStream([track]) : null;
  el.hidden = !track;
  el.classList.toggle("mirror", !!track && track === localTrack("video") && !isTestTrack(track));
  stopPlaceholders(track);
}

// Opens or closes the camera and the test sources to match wantedSources(), and puts on each call
// what it should send.
function syncCamera() {
  cameraBusy = (cameraBusy ?? Promise.resolve()).then(async () => {
    const want = wantedSources();
    for (const kind of Object.keys(SOURCE_LABEL)) {
      if (want.has(kind) && !sources.has(kind)) {
        openTestPattern(kind);
        log(`${SOURCE_LABEL[kind].toLowerCase()} opened`);
      } else if (!want.has(kind) && sources.has(kind)) {
        closeTestPattern(kind);
        log(`${SOURCE_LABEL[kind].toLowerCase()} closed`);
      }
    }
    await syncCameraTrack(want.has("camera"));
    // Always match each call's sender to its source, even when no source changed:
    // a new call (or the new connection after Add video) has a new sender, and a camera left
    // open from the preview or the last call must still be put on it.
    for (const e of entries.values()) {
      const sender = e.m?.videoSender;
      if (!sender) continue;
      const send = videoToSend(e);
      if (sender.track !== send) await sender.replaceTrack(send);
    }
    stopHoldCards();
    render();   // the self view follows what's sent
  }).catch((err) => log(`camera sync failed: ${err.message}`));   // so one failure doesn't stall the queue
  return cameraBusy;
}

// The camera that should be open: the one the call sending it wants (the selected call if more
// than one does), or before a call, the preview's. Null: no preference, keep what's open.
function wantedCameraId() {
  const sending = [...entries.values()].filter((e) => e.m?.hasVideo && e.m.camOn && !holdState(e)
    && e.h.call === "connected" && sourceOf(e) === "camera");
  const e = sending.find((x) => x.id === selectedId) ?? sending[0];
  if (e) return cameraIdOf(e);
  return call ? null : devices.camera;
}

// Opens or closes the camera itself, or switches it to the one wanted. If it can't open, every
// call sending it goes to camera off.
async function syncCameraTrack(want) {
  let track = localTrack("video");
  if (!want && track) {
    log("camera closed");
    closeCamera(track);
    return;
  }
  if (!want) return;
  const chosen = wantedCameraId();
  if (track && chosen != null && chosen !== cameraOpenedFor) {
    log("camera closed, to switch cameras");
    closeCamera(track);
    track = null;
  }
  if (track) return;
  const cameraCallsOff = () => { for (const e of entries.values()) if (e.m && sourceOf(e) === "camera") e.m.camOn = false; };
  try {
    // The chosen camera exactly, or if it's gone, the default.
    const open = (id) => navigator.mediaDevices.getUserMedia({
      video: { width: { ideal: 1280 }, height: { ideal: 720 }, frameRate: { ideal: 30 }, ...(id ? { deviceId: { exact: id } } : {}) },
    });
    const id = chosen ?? devices.camera;
    const cam = await open(id).catch((err) => {
      if (!id || err.name !== "OverconstrainedError" && err.name !== "NotFoundError") throw err;
      log("chosen camera not found, using the default");
      return open("");
    });
    track = cam.getVideoTracks()[0];
    // Changed while it was opening (including a switch to the test pattern).
    if (!wantedSources().has("camera")) { track.stop(); return; }
    local.addTrack(track);
    cameraOpenedFor = id;
    log("camera opened");
  } catch (err) {
    if (err.name === "NotReadableError" || err.name === "AbortError") {
      // In use elsewhere. Your camera goes off; trying again is up to you.
      log(`camera in use: ${err.name} ${err.message}`);
      cameraInUse = true;
    } else {
      // Missing, or permission refused: no camera for this run.
      log(`camera unavailable: ${err.name} ${err.message}`);
      hasCamera = false;
      cameraInUse = false;
    }
    cameraCallsOff();
  }
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
  await openMic();   // usually already opening, from Call or Answer
  const wantCamera = e.addingVideo ? true : midCall ? false : (answerCamera ?? camStart());
  e.addingVideo = false;
  if (wantCamera) cameraInUse = false;   // you placed, answered or added video: try the camera again
  // A new call starts with the source and camera last picked; a call that's renegotiating keeps its own.
  e.source ??= videoSource;
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
  answerCamera = null;

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
      tr.direction = localTrack("audio") ? "sendrecv" : "recvonly";
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

// Camera on or off during a video call. Off swaps in the placeholder, then closes the camera
// (unless another call is still sending it).
async function setCamera(on, e = sel()) {
  if (!e?.m?.videoSender || !(hasCamera || sourceOf(e) !== "camera")) return;
  e.m.camOn = on;
  if (on) { cameraInUse = false; privacyRelease(); }   // try again, because you asked
  if (!on && !holdState(e)) await e.m.videoSender.replaceTrack(placeholderTrack(e));
  log(`call ${e.id}: camera ${on ? "on" : "off"}`);
  render();
  await syncCamera();
}

// ---- Audio levels: frequency bars ----

let audioCtx = null;

function analyserFor(track) {
  audioCtx ??= new AudioContext();
  const source = audioCtx.createMediaStreamSource(new MediaStream([track]));
  const node = audioCtx.createAnalyser();
  node.fftSize = 512;
  node.smoothingTimeConstant = 0.5;
  node.minDecibels = -90;
  node.maxDecibels = -25;
  source.connect(node);   // not to the speakers: the remote <video> element plays their audio
  return { source, node, bins: new Uint8Array(node.frequencyBinCount) };
}

const BANDS = 28;
const FALL_MS = 260;
// Bars as drawn, and where they're heading, per canvas: the stage's, and each grid tile's (e.bars).
const newBars = () => ({ them: new Float32Array(BANDS), me: new Float32Array(BANDS),
  target: { them: new Float32Array(BANDS), me: new Float32Array(BANDS) } });
const stageBars = newBars();
const reducedMotion = matchMedia("(prefers-reduced-motion: reduce)");
let lastDraw = 0;

// Bands spaced logarithmically from ~100 Hz to ~6 kHz, where a voice's energy is.
function bands(analyser, out) {
  if (!analyser) { out.fill(0); return 0; }
  analyser.node.getByteFrequencyData(analyser.bins);
  const binHz = audioCtx.sampleRate / analyser.node.fftSize;
  const lo = 100 / binHz, hi = 6000 / binHz;
  let sum = 0;
  for (let b = 0; b < BANDS; b++) {
    const from = Math.floor(lo * (hi / lo) ** (b / BANDS));
    const to = Math.max(from + 1, Math.floor(lo * (hi / lo) ** ((b + 1) / BANDS)));
    let peak = 0;
    for (let i = from; i < to && i < analyser.bins.length; i++) peak = Math.max(peak, analyser.bins[i]);
    out[b] = Math.max(0, (peak / 255 - 0.12) / 0.88);
    sum += out[b];
  }
  return sum / BANDS;
}

function drawViz(t) {
  if ($("stage").dataset.mode !== "audio" || !call) return;
  requestAnimationFrame(drawViz);
  const dt = Math.min(250, t - (lastDraw || t));
  lastDraw = t;
  drawBars($("viz"), stageBars, { them: call.remoteAnalyser, me: call.localAnalyser, rxDead: call.rxDead, txLost: call.txLost }, dt);
}

// The grid's audio tiles, while the grid's up. A tile shows your bars only while that call gets
// your microphone, so a held call's tile shows them talking (or their music) and you silent.
let tilesDrawing = false, lastTileDraw = 0;
function drawTiles(t) {
  if (!inGrid()) { tilesDrawing = false; return; }
  requestAnimationFrame(drawTiles);
  const dt = Math.min(250, t - (lastTileDraw || t));
  lastTileDraw = t;
  for (const [id, tile] of tiles) {
    const e = entries.get(id);
    if (!e?.m || !tile.root.classList.contains("audio")) continue;
    e.bars ??= newBars();
    drawBars(tile.viz, e.bars, { them: e.m.remoteAnalyser, me: getsMic(e) ? e.m.localAnalyser : null, rxDead: e.m.rxDead, txLost: false }, dt);
  }
}

// Two rows of frequency bars on canvas c, theirs growing up from the middle and yours down.
function drawBars(c, b, { them, me, rxDead, txLost }, dt) {
  const level = { them: bands(rxDead ? null : them, b.target.them), me: bands(me, b.target.me) };
  const fall = Math.exp(-dt / FALL_MS);
  for (const k of ["them", "me"]) {
    const bars = b[k], target = b.target[k];
    for (let i = 0; i < BANDS; i++) {
      // Rise most of the way at once; sink slowly, so the picture settles rather than flickers.
      bars[i] = target[i] > bars[i] ? bars[i] + (target[i] - bars[i]) * 0.6 : Math.max(target[i], bars[i] * fall);
    }
  }

  const dpr = devicePixelRatio || 1;
  const w = Math.round(c.clientWidth * dpr), ht = Math.round(c.clientHeight * dpr);
  if (!w || !ht) return;
  if (c.width !== w || c.height !== ht) { c.width = w; c.height = ht; }
  const g = c.getContext("2d");
  g.clearRect(0, 0, w, ht);
  const css = getComputedStyle(c);
  const colour = { them: css.color, me: (txLost ? css.getPropertyValue("--viz-warn") : css.getPropertyValue("--viz-me")).trim() };
  const mid = ht / 2, gap = 3 * dpr, room = mid - gap - 2 * dpr;
  for (const [k, dir] of [["them", -1], ["me", 1]]) {
    const base = mid + dir * gap;
    g.fillStyle = colour[k];
    g.globalAlpha = 0.9;
    if (k === "them" && rxDead) {
      // No packets: no bars at all, just one thin line, unlike the resting pills of a quiet line.
      g.globalAlpha = 0.45;
      g.fillRect(0, base - dpr / 2, w, dpr);
      continue;
    }
    if (reducedMotion.matches) {
      g.fillRect(0, mid + dir * room / 2 - 3 * dpr, Math.max(2 * dpr, Math.min(1, level[k] * 2) * w), 6 * dpr);
      continue;
    }
    const slot = w / BANDS, bar = Math.max(2 * dpr, slot * 0.6), pad = (slot - bar) / 2;
    for (let i = 0; i < BANDS; i++) {
      const len = Math.max(2 * dpr, b[k][i] * room);
      const x = i * slot + pad, y = dir < 0 ? base - len : base;
      g.beginPath();
      g.roundRect(x, y, bar, len, Math.min(bar, len) / 2);
      g.fill();
    }
  }
  g.globalAlpha = 1;
}

// Twice a second during a call: is video arriving, is audio arriving? Video is judged by the
// decoder's frame count, not by frames shown: a hidden <video> element (as on the "camera off"
// screen) shows none, so it could never switch back.
// Every call, as each has its own video (and, in grid view, its own tile).
setInterval(async () => {
  let changed = false;
  for (const e of entries.values()) {
    const { pc, m } = e;
    if (!pc || !m || e.h.call !== "connected") continue;
    const t = performance.now();
    let stats;
    try { stats = await pc.getStats(); } catch { continue; }
    stats.forEach((r) => {
      if (r.type !== "inbound-rtp") return;
      if (r.kind === "audio" && r.packetsReceived !== m.rxPackets) { m.rxPackets = r.packetsReceived; m.rxChangedAt = t; }
      if (r.kind === "video" && r.framesDecoded > 0 && r.framesDecoded !== m.framesDecoded) { m.framesDecoded = r.framesDecoded; m.lastFrameAt = t; }
    });
    // rxChangedAt starts when the media is set up, so a call gets the same 2 s grace at the start.
    // On hold, either way round, their audio isn't played or doesn't come: not worth a warning.
    if (holdState(e) || e.h.heldByThem) m.rxChangedAt = t;
    const rxDead = t - m.rxChangedAt > NO_AUDIO_MS;
    if (rxDead !== m.rxDead) { m.rxDead = rxDead; changed = true; log(`call ${e.id}: ${rxDead ? "no audio arriving" : "audio arriving again"}`); }
  }
  const mode = stageMode();
  if (changed || mode !== $("stage").dataset.mode || mode === "nocam") render();
}, 500);

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
function renderDetails() {
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


// ---- Real-time text (RFC 4103) ----
// The host sends and receives the T.140 text; the page shows the conversation. What you type is
// sent when you press Send or Enter, as the line and a T.140 line separator (U+2028). Theirs
// may arrive letter by letter (a real-time text phone): their unfinished line shows as it comes,
// backspaces and all, dashed with a caret.
const LINE_SEP = "\u2028";
// Each call has its own conversation (entry.text): { lines: [{ me, text, open }], unread (they
// typed while you weren't reading it: a dot on the Text button and in the call list), bomSent
// (T.140 starts with a byte order mark), said (--say, testing, typed once per call) }. The
// text panel shows the selected call's.
let textOpen = false;        // the text panel, in the dock
let toastTimer = 0;
let toastCallId = null;      // the call the toast is about

const openLine = (t, me, create = true) => {
  let l = t.lines.findLast((x) => x.me === me && x.open);
  if (!l && create) { l = { me, text: "", open: true }; t.lines.push(l); }
  return l;
};
function closeLine(t, me) {
  const l = openLine(t, me, false);
  if (!l) return;
  l.open = false;
  if (!l.text) t.lines.splice(t.lines.indexOf(l), 1);
}

function onTextReceived(id, chars) {
  const e = entries.get(id);
  if (!e) return;
  const t = e.text;
  for (const ch of chars.replace(/\uFEFF/g, "").replace(/\r\n/g, "\n")) {
    if (ch === "\b") { const l = openLine(t, false, false); if (l) l.text = [...l.text].slice(0, -1).join(""); }
    else if (ch === LINE_SEP || ch === "\u2029" || ch === "\n" || ch === "\r") closeLine(t, false);
    else if (ch >= " ") openLine(t, false).text += ch;
  }
  if (!textOpen || id !== selectedId) { t.unread = true; showToast(e); }
  if (id === selectedId) renderText();
  render();
}

// With the text panel closed (or for another call), what they type shows in a toast for 6 s after
// the last letter. Reply opens that call's text.
function showToast(e) {
  const l = openLine(e.text, false, false) ?? e.text.lines.findLast((x) => !x.me);
  if (!l?.text) return;
  toastCallId = e.id;
  $("rttToastWho").textContent = nameOf(e) || "They";
  $("rttToastText").textContent = l.text;
  $("rttToast").hidden = false;
  clearTimeout(toastTimer);
  toastTimer = setTimeout(hideToast, 6000);
}
function hideToast() {
  clearTimeout(toastTimer);
  $("rttToast").hidden = true;
}
$("rttToastIcon").innerHTML = ICON.text;
$("rttReply").onclick = () => {
  if (toastCallId && entries.has(toastCallId)) selectCall(toastCallId);
  setView("focus");   // the text panel is in Focus view only
  setTextOpen(true);
};

// Sends what's in the box, with a line separator to finish it. Nothing goes until then.
function sendLine() {
  const e = sel();
  const line = $("rttInput").value;
  if (!e || !line.trim() || state.text !== "on") return;
  post({ type: "text", id: e.id, text: (e.text.bomSent ? "" : "\uFEFF") + line + LINE_SEP });
  e.text.bomSent = true;
  e.text.lines.push({ me: true, text: line, open: false });
  $("rttInput").value = "";
  $("rttSend").disabled = true;
  renderText();
}
$("rttSend").innerHTML = ICON.send;
$("rttSend").onclick = () => { sendLine(); $("rttInput").focus(); };
$("rttInput").addEventListener("input", () => { $("rttSend").disabled = !$("rttInput").value.trim(); });
$("rttInput").addEventListener("keydown", (e) => {
  if (e.key === "Enter") { e.preventDefault(); sendLine(); }
});
$("rttAdd").onclick = () => post({ type: "add-text", id: selectedId });
$("rttClose").innerHTML = ICON.close;
$("rttClose").onclick = () => setTextOpen(false);
$("textBtn").onclick = () => setTextOpen(!textOpen);
// Esc closes the text panel.
document.addEventListener("keydown", (e) => {
  if (e.key !== "Escape" || !textOpen || openMenu) return;
  e.preventDefault();
  setTextOpen(false);
});
document.addEventListener("keydown", (e) => {
  if (e.key.toLowerCase() !== "t" || e.ctrlKey || e.altKey || e.metaKey || openMenu) return;
  if (e.target.closest?.("input, textarea, select, [contenteditable]")) return;
  if ($("textBtn").disabled) return;
  e.preventDefault();
  // From the grid: Focus on the selected call, with the text panel open.
  if (inGrid()) { setView("focus"); setTextOpen(true); return; }
  setTextOpen(!textOpen);
});

function setTextOpen(open, focus = true) {
  textOpen = open;
  if (open) { const e = sel(); if (e) e.text.unread = false; hideToast(); renderText(); }
  render();
  if (open && focus && !$("rttEntry").hidden) $("rttInput").focus();
  else if (!open && focus && document.activeElement && $("rtt").contains(document.activeElement)) $("textBtn").focus();
}

function renderText() {
  const el = $("rttLog");
  const atEnd = el.scrollHeight - el.scrollTop - el.clientHeight < 24;
  const esc = (t) => t.replace(/[&<>]/g, (ch) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;" }[ch]));
  const name = shortName(state.remote) || "Them";
  const lines = sel()?.text.lines ?? [];
  let who = null;
  el.innerHTML = !lines.length ? `<p class="rtt-empty">Nothing typed yet.</p>`
    : lines.map((l) => {
      const head = l.me !== who ? `<div class="rtt-who ${l.me ? "me" : ""}">${l.me ? "You" : esc(name)}</div>` : "";
      who = l.me;
      return `${head}<div class="rtt-msg ${l.me ? "me" : "them"}${l.open ? " live" : ""}">${esc(l.text)}${l.open ? `<span class="rtt-caret" aria-hidden="true"></span>` : ""}</div>`;
    }).join("");
  if (atEnd) el.scrollTop = el.scrollHeight;
}

// The panel and the Text button, from the selected call's state (render calls this).
function renderTextControls(phase) {
  const live = phase === "connected";
  const e = sel();
  const unread = live && !!e?.text.unread;
  const btn = $("textBtn");
  btn.disabled = !live;
  btn.setAttribute("aria-pressed", String(live && textOpen));
  const typing = !!(e && openLine(e.text, false, false));
  const dot = unread ? `<span class="btn-dot" aria-hidden="true"></span>` : "";
  const html = `${ICON.text}${dot}`;
  if (btn.innerHTML !== html) btn.innerHTML = html;
  btn.title = !live ? "Real-time text, during a call"
    : unread ? `Text: ${typing ? "they're typing" : "new message"} (T)` : textOpen ? "Close text (T)" : "Text (T)";
  btn.setAttribute("aria-label", unread ? "Text, new message" : "Text");
  $("rtt").hidden = !(live && textOpen);
  if (!liveEntries().some((x) => x.h.call === "connected")) hideToast();
  // The compose area follows the text state: on, adding, refused, off or disabled.
  const t = state.text ?? "off";
  const name = shortName(state.remote) || "They";
  $("rttEntry").hidden = t !== "on";
  $("rttAdd").hidden = t !== "off";
  $("rttHint").textContent = t === "on" ? "Enter or Send sends the message."
    : t === "adding" ? `Asking ${name}'s phone to add text…`
    : t === "refused" ? `${name}'s phone doesn't support real-time text.`
    : t === "disabled" ? "Real-time text is turned off (--no-text)."
    : t === "unavailable" ? "Text isn't available on calls with browser phones yet."
    : "Text isn't set up on this call.";
  // --say (testing): once text is up on a call, type it into the box, then send it.
  if (testSay && t === "on" && e && !e.text.said) {
    e.text.said = true;
    setTimeout(() => { if (selectedId !== e.id) return; $("rttInput").value = testSay; sendLine(); }, 1500);
  }
}
// ---- The keypad (DTMF), from the prototype ----
// The Keypad button (Focus view; the grid hides the controls) opens a 12-key pad above it for the
// selected call. Each key sends a tone on that call: the host sends it as an RFC 4733
// telephone-event, or by SIP INFO if the far end didn't agree telephone-event (the web build:
// RTCDTMFSender). While it's open the number keys, * and # send tones rather than picking calls,
// and Esc closes it. It closes when the call is held (either way), ends, or another is selected.
const KEYPAD_KEYS = [["1", ""], ["2", "ABC"], ["3", "DEF"], ["4", "GHI"], ["5", "JKL"], ["6", "MNO"],
  ["7", "PQRS"], ["8", "TUV"], ["9", "WXYZ"], ["*", ""], ["0", "+"], ["#", ""]];
// Why the selected call can't take tones now, or null if it can.
function keypadBlocked() {
  const e = sel();
  if (!e || e.h.call !== "connected") return "Keypad, during a call";
  if (inGrid()) return "Keypad: pick the call to open it in Focus";
  if (e.pc?.connectionState !== "connected") return "Keypad: waiting for the call's audio";
  if (holdState(e)) return "Keypad: resume the call to send tones";
  if (e.h.heldByThem) return `Keypad: ${nameOf(e)} has put you on hold`;
  if (e.h.dtmf === false) return "This call can't send keypad tones";
  return null;
}
function renderKeypad() {
  const btn = $("keypadBtn");
  const blocked = keypadBlocked();
  if (keypadOpen && (blocked || selectedId !== keypadFor)) {
    keypadOpen = false;
    if (document.activeElement && $("keypad").contains(document.activeElement)) btn.focus();
  }
  if (!keypadOpen) keypadDigits = "";
  btn.disabled = !!blocked;
  btn.setAttribute("aria-pressed", String(keypadOpen));
  btn.setAttribute("aria-expanded", String(keypadOpen));
  if (!btn.innerHTML) btn.innerHTML = ICON.keypad;
  btn.title = blocked ?? (keypadOpen ? "Close keypad (Esc)" : "Keypad: send tones (DTMF)");
  // --dtmf <keys> (testing): once a call is up, open the keypad and press them, as typed keys
  // would, 2 s in (so audio is flowing to put the tones in), one every 400 ms.
  const testKeys = new URLSearchParams(location.search).get("dtmf");
  const e = sel();
  if (testKeys && !blocked && e && !e.dtmfTested) {
    e.dtmfTested = true;
    setTimeout(async () => {
      if (selectedId !== e.id) return;
      setKeypadOpen(true);
      for (const k of testKeys) {
        document.dispatchEvent(new KeyboardEvent("keydown", { key: k, bubbles: true }));
        await new Promise((r) => setTimeout(r, 400));
      }
    }, 2000);
  }
  $("keypad").hidden = !keypadOpen;
  if (!keypadOpen) { $("keypadIn").textContent = ""; return; }
  $("keypadTitle").textContent = `Keypad · ${nameOf(sel())}`;
  $("keypadDigits").textContent = keypadDigits;
}
function setKeypadOpen(open) {
  if (open && keypadBlocked()) return;
  keypadOpen = open;
  keypadFor = open ? selectedId : null;
  if (open && sel()) sel().dtmfIn = "";
  render();
}
function pressKey(digit) {
  const e = sel();
  if (!keypadOpen || keypadBlocked() || !e) return;
  keypadDigits = (keypadDigits + digit).slice(-20);
  $("keypadDigits").textContent = keypadDigits;
  const key = $("keypadKeys").querySelector(`[data-digit="${CSS.escape(digit)}"]`);
  if (key) { key.classList.add("hit"); setTimeout(() => key.classList.remove("hit"), 150); }
  log(`call ${e.id}: keypad '${digit}' → ${nameOf(e)}`);
  post({ type: "dtmf", id: e.id, digit });
}
// Tones they send: in the log (the host logs them), and under the keys while it's open on that call.
function onDtmfIn(id, digit) {
  const e = entries.get(id);
  if (!e || !keypadOpen || id !== keypadFor) return;
  e.dtmfIn = ((e.dtmfIn ?? "") + digit).slice(-20);
  $("keypadIn").textContent = `${nameOf(e)} sent ${e.dtmfIn}`;
}
$("keypadKeys").innerHTML = KEYPAD_KEYS.map(([d, s]) => `<button type="button" class="key" data-digit="${d}" aria-label="${d}${s ? ` ${s}` : ""}"><span><b>${d}</b><br><small>${s || "&nbsp;"}</small></span></button>`).join("");
$("keypadKeys").onclick = (ev) => { const k = ev.target.closest(".key"); if (k) pressKey(k.dataset.digit); };
$("keypadBtn").onclick = () => setKeypadOpen(!keypadOpen);
$("keypadClose").innerHTML = ICON.close;
$("keypadClose").onclick = () => { setKeypadOpen(false); $("keypadBtn").focus(); };
// Before the shortcuts (capture): while it's open, 0–9, * and # are tones (1–8 would pick calls),
// and Esc closes it. Not while typing in a field (the address bar, say).
document.addEventListener("keydown", (ev) => {
  if (!keypadOpen || ev.ctrlKey || ev.altKey || ev.metaKey || openMenu) return;
  if (ev.target.closest?.("input, textarea, select, [contenteditable]")) return;
  if (ev.key === "Escape") { ev.preventDefault(); ev.stopImmediatePropagation(); setKeypadOpen(false); $("keypadBtn").focus(); return; }
  if (!/^[0-9*#]$/.test(ev.key)) return;
  ev.preventDefault();
  ev.stopImmediatePropagation();
  if (!ev.repeat) pressKey(ev.key);
}, true);

// ---- Transfer ----
// As in the prototype: "Attended to another active call, or blind to an address". The Transfer
// button (Focus view, a connected call, a host that can: not the web softphone's) opens a
// popover above it: your other connected calls first (Join: they're asked to call that person,
// replacing your call with them, and both your calls end), then an address field that is the
// address bar's (addressField, below: the same contacts and recent calls, keys, mark and ×).
// Enter or a click puts a row in the field; Transfer, or Enter again, sends the call there. A
// bare name or number goes to the call's account's server (the host resolves it). The
// call goes on hold as the transfer starts, and a note on the stage says how it's going, from the
// host's transfer state (see MainForm). Failed, the call stays on hold: Resume or Try again,
// your choice. A transfer done to you (they send you elsewhere, or someone takes over from them)
// shows in the same note, for a few seconds.
let xferOpen = false;
let xferFor = null;        // the call it was opened on: it closes if another is selected
const xferBusy = (e) => ["asking", "calling"].includes(e?.h.transfer?.status);
// Why the selected call can't be transferred now, or null if it can.
function transferBlocked() {
  const e = sel();
  if (!e || e.h.call !== "connected") return "Transfer, during a call";
  if (inGrid()) return "Transfer: pick the call to open it in Focus";
  if (xferBusy(e)) return "A transfer is under way on this call";
  return null;
}
function setTransferOpen(open) {
  if (open && transferBlocked()) return;
  if (open && !xferOpen) {
    $("xferAddr").value = "";
    xferField.pick = null;
  }
  xferOpen = open;
  xferFor = open ? selectedId : null;
  render();
  if (open) {
    xferField.refresh();
    ($("xferCalls").querySelector("button") ?? $("xferAddr")).focus();
  } else if (document.activeElement && $("xferPop").contains(document.activeElement)) $("transferBtn").focus();
}
// Your other calls it can be joined with: connected, and not being transferred themselves.
const xferCandidates = () => listed().filter((x) => x.id !== selectedId && x.h.call === "connected" && !xferBusy(x));
function renderTransfer() {
  const btn = $("transferBtn");
  const can = !!hostState.canTransfer;
  btn.hidden = !can || inGrid();
  const blocked = can ? transferBlocked() : "Transfer";
  if (xferOpen && (blocked || selectedId !== xferFor)) {
    xferOpen = false;
    if (document.activeElement && $("xferPop").contains(document.activeElement)) btn.focus();
  }
  btn.disabled = !!blocked;
  btn.setAttribute("aria-pressed", String(xferOpen));
  btn.setAttribute("aria-expanded", String(xferOpen));
  if (!btn.innerHTML) btn.innerHTML = ICON.transfer;
  btn.title = blocked ?? (xferOpen ? "Close transfer (Esc)" : "Transfer this call: to another of your calls, or an address");
  $("xferPop").hidden = !xferOpen;
  renderTransferNote();
  if (!xferOpen) return;
  const e = sel();
  $("xferTitle").textContent = `Transfer ${nameOf(e)}`;
  // Rebuilt only when it changes: render runs every second while a call's held.
  const others = xferCandidates();
  const html = others.length ? `<div class="xfer-label">To another of your calls</div>${others.map((o) => `
    <button type="button" class="xfer-call" data-call="${o.id}" title="${escHtml(`${nameOf(e)} will be put through to ${nameOf(o)}, and you'll leave both calls`)}">
      <span class="suggest-avatar" aria-hidden="true"${avatarAttr(o.h.remote)}>${escHtml(initial(nameOf(o)))}</span>
      <span class="suggest-main"><span class="suggest-name">${escHtml(nameOf(o))}</span><span class="suggest-uri">${escHtml(o.h.remote)}</span></span>
      <span class="xfer-call-go">Join</span></button>`).join("")}` : "";
  if ($("xferCalls").dataset.html !== html) { $("xferCalls").dataset.html = html; $("xferCalls").innerHTML = html; }
  $("xferAddrLabel").textContent = others.length ? "Or to an address" : "To an address";
  renderXferEntry();
}
// The address: the address bar's field (addressField), its list always showing, without the
// person being transferred and without the Contacts… and Add to contacts rows.
const xferField = addressField({
  input: $("xferAddr"), list: $("xferSug"), clear: $("xferClear"), badge: $("xferContact"),
  idPrefix: "xsug", popup: false, foot: false, goIcon: () => ICON.transfer, goLabel: "Transfer here",
  skip: () => sel()?.h.remote ?? null,
  pick: () => renderXferEntry(),
  changed: () => renderXferEntry(),
});
// The Transfer button: enabled with something typed or highlighted.
function renderXferEntry() {
  $("xferGo").disabled = !$("xferAddr").value.trim() && !xferField.items[xferField.active];
}
// Starts a transfer of the selected call: { target } another call's id, or { address, label }.
// The host holds the call (both, attended) as it sends the REFER; here they count as held at
// once, so the audio moves off them now.
function startTransfer(how) {
  const e = sel();
  if (!e || transferBlocked()) return;
  for (const x of [e, how.target ? entries.get(how.target) : null]) {
    if (!x || holdState(x)) continue;
    x.holdWanted = true;
    x.holdAskedAt = Date.now();
  }
  log(how.target ? `call ${e.id}: transfer to call ${how.target} (${nameOf(entries.get(how.target))}), attended`
    : `call ${e.id}: transfer to ${how.address}, blind`);
  post({ type: "transfer", id: e.id, ...how });
  setTransferOpen(false);
  syncCamera();   // the On hold card
}
// it: the row picked; without one, what's typed.
function transferToAddress(it = null) {
  const typed = $("xferAddr").value.trim();
  // A row picked into the field (and not edited since) goes with its name.
  if (!it && xferField.pick && sameUri(xferField.pick.uri, typed)) it = xferField.pick;
  if (it) startTransfer({ address: it.uri, label: it.name });
  else if (typed) startTransfer({ address: typed, label: contactFor(typed)?.name ?? null });
}
// A transfer, in a few words, for the call header's one line and the call's row: what's
// happening now. Who it's with is already in the header (whose call it is), so it names the other
// party. The host's sentence in full (t.detail) goes in the tooltip. Failed: why, from t.reason.
function transferShort(t) {
  const busy = t.status === "asking" || t.status === "calling";
  if (t.role === "out") {
    if (busy) return t.rang ? `${t.target} is ringing…` : t.attended ? `Joining with ${t.target}…` : `Transferring to ${t.target}…`;
    if (t.status === "done") return t.attended ? `Joined with ${t.target}` : `Transferred to ${t.target}`;
    if (t.status === "cancelled") return "Transfer cancelled";
    return t.reason ? `Transfer failed: ${t.reason}` : `Transfer to ${t.target} failed`;
  }
  if (t.role === "in") {
    if (busy) return t.transferorGone ? `${t.by} hung up · calling ${t.target}…`
      : `Transferring you to ${t.target}${t.rang ? " · ringing" : ""}…`;
    if (t.status === "done") return `Transferred by ${t.by}`;
    return t.reason === "you cancelled it" ? `Transfer cancelled · still with ${t.by}`
      : `Transfer to ${t.target} failed${t.reason ? `: ${t.reason}` : ""}`;
  }
  // "replace": someone taking over the call from the person you were talking to.
  return busy ? `${t.by} is handing you over…` : `Handed over by ${t.by}`;
}
// The call header's status (#xferNote): the selected call's transfer while it's going and for a
// few seconds after, with what you can do. Under way: Cancel (ours: take the call back; theirs:
// stay with who's transferring you; the host says when it can), and on our blind one, Hang up
// (leave it to their phone). Ours failed: Try again and ×; the call's on hold, and the held note
// in the middle of the stage has Resume. Done, cancelled, theirs failed: × (it goes by itself).
function renderTransferNote() {
  const e = sel();
  const t = e?.h.call === "connected" ? e.h.transfer : null;
  const note = $("xferNote");
  note.hidden = !t || inGrid();
  if (note.hidden) return;
  const ours = t.role === "out";
  const busy = t.status === "asking" || t.status === "calling";
  $("xferText").textContent = transferShort(t);
  note.title = t.detail || "";
  note.dataset.state = busy ? "busy" : t.status === "failed" ? "bad" : t.status === "done" ? "done" : "info";
  if (!$("xferIcon").innerHTML) $("xferIcon").innerHTML = ICON.transfer;
  const cancel = $("xferCancel");
  cancel.hidden = !t.canCancel;
  cancel.title = ours ? "Take the call back (SIP can't stop their phone once it's accepted: if it still goes ahead, it will leave the call)"
    : `Stay with ${t.by}: cancels the call to ${t.target}`;
  $("xferLeave").hidden = !t.canLeave;
  $("xferLeave").title = `Hang up now and leave the transfer to ${t.target} to their phone`;
  $("xferRetry").hidden = !(ours && t.status === "failed");
  $("xferRetry").title = `Transfer ${nameOf(e)} again`;
  $("xferDismiss").hidden = busy;
}
$("transferBtn").onclick = () => setTransferOpen(!xferOpen);
$("xferClose").innerHTML = ICON.close;
$("xferClose").onclick = () => setTransferOpen(false);
$("xferDismiss").innerHTML = ICON.close;
$("xferDismiss").onclick = () => post({ type: "transfer-dismiss", id: selectedId });
$("xferCancel").onclick = () => { log(`call ${selectedId}: transfer cancelled`); post({ type: "transfer-cancel", id: selectedId }); };
$("xferLeave").onclick = () => { log(`call ${selectedId}: hanging up, leaving the transfer to them`); post({ type: "transfer-leave", id: selectedId }); };
$("xferRetry").onclick = () => { post({ type: "transfer-dismiss", id: selectedId }); setTransferOpen(true); };
$("xferCalls").onclick = (ev) => { const b = ev.target.closest("[data-call]"); if (b) startTransfer({ target: b.dataset.call }); };
$("xferPop").onsubmit = (ev) => {
  ev.preventDefault();
  const it = xferField.items[xferField.active];
  transferToAddress(it?.action ? null : it);
};
// Esc closes it (before the other shortcuts), and so does a click away from it.
document.addEventListener("keydown", (ev) => {
  if (!xferOpen || ev.key !== "Escape" || openMenu) return;
  ev.preventDefault();
  ev.stopImmediatePropagation();
  setTransferOpen(false);
}, true);
document.addEventListener("click", (ev) => {
  if (xferOpen && !$("xferPop").contains(ev.target) && !$("transferBtn").contains(ev.target)) setTransferOpen(false);
}, true);

// ---- The dock: the keypad and the text panel beside the call ----
// Both open in a side panel to the right of the selected call (Focus view), and the stage narrows
// to make room; they stay open when you click elsewhere, and Esc or their × closes them. With both
// open they're stacked, the keypad (a fixed size) at the top and the text taking the rest, so you
// can read and send tones at once. The dock shows while either is open, and goes with them (the
// grid, hold and switching calls close them). In a narrow window (under 960 px) it floats over
// the stage's right edge instead.
function renderDock() {
  const dock = $("dock");
  dock.hidden = inGrid() || !(keypadOpen || textOpen);
  dock.parentElement.classList.toggle("docked", !dock.hidden);
  dock.classList.toggle("both", keypadOpen && textOpen);
}

const lastOf = (a) => a[a.length - 1];
const newQuality = () => ({
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

function onQuality(msg) {
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

// Once a second during a call: how we're receiving, from the page's WebRTC stats, and a point
// for the graph (the sent side holds its latest report until the next).
setInterval(() => {
  for (const e of entries.values()) if (e.pc && e.m && e.h.call === "connected") measureQuality(e).catch(() => {});
}, 1000);

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

function renderQuality() {
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

// ---- Rendering the state ----

function stageMode() {
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
function callBlocked() {
  if (callsFull()) return "That's as many calls as there can be: end one to call again";
  const out = [...entries.values()].find((e) => e.h.outgoing && e.h.call === "calling" && !e.cancelled);
  return out ? `Wait for ${nameOf(out)} to answer, or cancel it, to place another call` : null;
}
// Call's enabled state and tooltip: off while callBlocked says so, or there's nothing to call.
// On every change to the address field (typing, ×, a suggestion, paste) as well as in render.
function syncCallBtn() {
  const btn = $("callBtn");
  const blocked = callBlocked();
  const empty = !$("address").value.trim();
  btn.disabled = !!blocked || empty;
  const kept = liveCall();
  const video = state.videoAllowed !== false && callType === "video";
  btn.title = blocked ?? (empty ? "Type a number or address to call"
    : kept ? `${video ? "Video call" : "Call"}, and put ${nameOf(kept)} on hold` : "");
}

function render() {
  deriveState();
  syncAudio();
  const grid = inGrid();
  if (grid && textOpen) textOpen = false;   // the text panel is in Focus view only
  const { call: phase, remote, reason } = state;
  const live = phase === "calling" || phase === "ringing" || phase === "connected";
  const connected = phase === "connected" && !!call;
  const name = shortName(remote);
  const e = sel();
  // New calls go alongside the others, up to the host's limit.
  const full = callsFull();
  $("address").disabled = full;
  $("info").hidden = phase !== "connected";
  $("stage").classList.toggle("head", phase === "connected");
  const shown = plainAddress(remote);
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

  // The Audio | Video switch beside Call. Locked to Audio if video is off (--no-video).
  // The selected call's source, or before a call the one it'll start with.
  const camAvailable = hasCamera || sourceOf(e) !== "camera";
  const videoAllowed = state.videoAllowed !== false;
  const type = videoAllowed ? callType : "audio";
  $("callType").dataset.side = type === "video" ? "right" : "left";
  $("callType").classList.toggle("disabled", full || !videoAllowed);
  for (const el of document.querySelectorAll("#callType [data-type]")) {
    el.classList.toggle("on", el.dataset.type === type);
    el.disabled = full || (!videoAllowed && el.dataset.type === "video");
  }
  const track = $("callType").querySelector(".slide-track");
  track.setAttribute("aria-checked", String(type === "video"));
  track.disabled = full || !videoAllowed;
  $("callType").title = !videoAllowed ? "Video is turned off"
    : type === "video" ? "Video call: you'll see each other. You can turn your camera off once it connects."
    : "Audio call. You can add video once it connects.";
  // On a call, Call says what happens to it: "Call & hold current", in the same words as a
  // ringing call's "Answer & hold current" (its tooltip names who). Both labels are always there
  // (one hidden), so the Audio/Video switch never resizes it.
  const tail = liveCall() ? '<span class="hold-tail"> &amp; hold current</span>' : "";
  const callHtml = `${type === "video" ? ICON.video : ICON.phone}<span class="label-stack"><span class="${type === "video" ? "off" : ""}">Call${tail}</span><span class="${type === "video" ? "" : "off"}">Video call${tail}</span></span>`;
  if ($("callBtn").innerHTML !== callHtml) $("callBtn").innerHTML = callHtml;
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

  // Self view: only for video, and always what they see (or will see). On a video call that's
  // your camera, or the placeholder while it's off; before a call it's a preview while the
  // switch is on Video. A note on top says why the camera isn't showing. Audio shows none.
  const before = phase === "idle" || phase === "ended" || phase === "calling";
  // Self view hides it, before a call (the preview with the switch on Video) or during a video
  // call; on a call what's sent doesn't change. The button is there whenever there's a self view.
  const selfViewApplies = connected ? call.hasVideo : before && type === "video";
  const showSelf = !!local && selfViewApplies && selfViewOn;
  const sv = $("selfViewBtn");
  sv.hidden = !selfViewApplies;
  sv.setAttribute("aria-pressed", String(selfViewOn));
  sv.title = connected ? `${selfViewOn ? "Hide" : "Show"} your self view (V). They still see you.`
    : `${selfViewOn ? "Hide" : "Show"} your camera preview (V)`;
  if (!sv.innerHTML) sv.innerHTML = `${ICON.selfView}<span>Self view</span>`;
  const camOn = connected ? call.camOn : camStart();
  const selfText = !showSelf ? null : !camAvailable ? "No camera" : cameraInUse && sourceOf(e) === "camera" ? "Camera in use"
    : !camOn ? "Camera off" : null;
  showSelfVideo(showSelf);
  $("selfOff").hidden = !selfText;
  $("selfOffText").textContent = selfText ?? "";
  if (!sameSources(wantedSources(), openSources())) syncCamera();

  // The control bar, under the stage the whole time, so you can set up your microphone and
  // camera before a call. Mute and Camera are split buttons: the ▾ picks the device.
  const setBtn = (el, icon, text) => {
    const html = `${icon}<span>${text}</span>`;
    if (el.innerHTML !== html) el.innerHTML = html;
  };
  const muted = call ? call.muted : startMuted();
  const mute = $("muteBtn");
  mute.disabled = !hasMic;
  mute.setAttribute("aria-pressed", String(hasMic && muted));
  // What pressing it does, as on the camera button; red while you're muted.
  mute.title = !hasMic ? "No mic found"
    : call ? (muted ? "Unmute: they'll hear you (M)" : "Mute: they won't hear you (M)")
    : muted ? "Calls start muted" : "Calls start unmuted";
  setBtn(mute, muted || !hasMic ? ICON.micOff : ICON.mic, !hasMic ? "No mic" : muted ? "Unmute" : "Mute");
  // While muted on a call, a red mic by the timer (in the call header) says so.
  const pill = $("mutedPill");
  pill.hidden = !(live && hasMic && muted);
  if (!pill.hidden && !pill.innerHTML) { pill.innerHTML = `${ICON.micOff}<span>You're muted</span>`; pill.title = "You're muted"; }

  // Camera: during a video call it turns your camera on and off; on an audio call it's Add video,
  // unless video can't be had. Before a call it decides whether a video call starts with your
  // camera on (and the preview shows it).
  const cam = $("camBtn");
  const canAdd = connected && !call.hasVideo && videoAllowed && !e.videoRefused;
  const ringing = phase === "ringing";
  if (connected && !call.hasVideo) {
    cam.disabled = !canAdd || e.addingVideo;
    cam.setAttribute("aria-pressed", "false");
    setBtn(cam, canAdd ? ICON.video : ICON.videoOff, e.addingVideo ? "Adding video…" : canAdd ? "Add video" : "No video");
    cam.title = canAdd ? "Add video to this call. They'll be asked; some phones can't."
      : e.videoRefused ? "Their phone doesn't do video" : "Video is turned off";
  } else {
    // Before a call on Audio it isn't disabled: pressing the camera means you want video, so it
    // switches the call to Video with the camera on (see its onclick).
    const audioBefore = !connected && type === "audio";
    const on = connected ? call.camOn : camStart() && !audioBefore;
    cam.disabled = !camAvailable || !videoAllowed || (phase === "connected" && !connected) || ringing;
    cam.setAttribute("aria-pressed", String(camAvailable && videoAllowed && !on && !audioBefore));
    setBtn(cam, camAvailable && on ? ICON.video : ICON.videoOff,
      // What pressing it does, as on the mic button: the video, whatever its source.
      !videoAllowed ? "No video" : !camAvailable ? "No camera" : on ? "Stop video" : "Start video");
    cam.title = !videoAllowed ? "Video is turned off" : !camAvailable ? "No camera found"
      : ringing ? "Choose the camera with the Answer button"
      : audioBefore ? "Make it a video call, with your camera on"
      : connected ? (on ? "Stop video: they'll see your initial" : "Start video: turn your camera on")
      : on ? "Video calls start with your camera on" : "Video calls start with your camera off";
  }
  $("camMenuBtn").disabled = !videoAllowed || ringing || (!connected && type === "audio");

  renderTextControls(phase);
  renderKeypad();
  renderTransfer();
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
  }
  const hang = $("hangupBtn");
  hang.disabled = phase !== "calling" && phase !== "connected";
  setBtn(hang, ICON.hangup, phase === "calling" ? "Cancel" : "End");
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
      title.textContent = remote;
      markContact(title, remote);
      sub.textContent = "Calling…";
      break;
    case "ringing": {
      if (ringingUnpicked()) {
        // Several ringing and none picked: they're in the banner above, all alike.
        title.textContent = `${allRinging().length} calls ringing`;
        sub.textContent = "Answer one above, or pick a name to see that call.";
        break;
      }
      title.textContent = remote;
      markContact(title, remote);
      sub.textContent = state.incomingVideo ? "Incoming video call" : "Incoming call";
      actions.append(button("btn-danger", "Decline", () => post({ type: "decline", id: selectedId })));
      // With another call going, Answer holds it.
      actions.append(answerSplit);
      renderAnswer(liveCall());
      if (lastPhase !== "ringing" || lastSelected !== selectedId) $("answerBtn").focus();
      break;
    }
    case "connected":
      title.textContent = remote;
      markContact(title, remote);
      sub.textContent = "Connecting media…";
      break;
    case "ended": {
      title.textContent = remote || "Call ended";
      sub.textContent = reason || "Call ended";
      const back = otherLive();
      if (back) actions.append(button("", `Back to ${nameOf(back)}`, () => selectCall(back.id)));
      break;
    }
  }
  renderIncoming();
  renderOutgoing();
  renderGrid(grid);

  if (phase !== "ringing" && openMenu === "answer") closeMenu();
  // A choice from the Answer menu is for that call only (onMediaOffer uses it, then clears it);
  // one that wasn't answered mustn't carry over to the next.
  if (phase !== "ringing" && phase !== "connected") answerCamera = null;
  lastPhase = phase;
  lastSelected = selectedId;
  renderQuality();

  renderCalls();
  renderPrivacy();
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
function tick() {
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
  for (const h of msg.calls ?? []) {
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
  // The microphone closes when the last call ends (it opens with Call or Answer).
  if (!liveEntries().length) closeMic();
}

function onNewCall(e) {
  const s = sel();
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
// from the grid does).
function answerCall(id) {
  const e = entries.get(id);
  if (e?.h.call !== "ringing") return;
  closeMenu();
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
const tiles = new Map();   // call id -> { root, feed, avatar, time, name, status, media, held, heldText, resume }
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
  if (grid && !tilesDrawing) { tilesDrawing = true; requestAnimationFrame(drawTiles); }
}

// ---- Active calls panel, down the left ----
// Every call, oldest at the top and new ones at the bottom, so a call never moves. Each row shows who, what you're sending (mic and camera), and
// the call's state: calling, ringing, connected, on hold with how long (or "Holding…" and
// "Resuming…" while the re-INVITE is out), or how it ended. Clicking a row puts that call on the
// stage, as do the number keys. The chevron folds it to a rail of avatars and timers, which
// leaves room for the call info beside Mute; the choice is remembered. Under 700 px it's always
// the rail, with no chevron.

// Narrow windows (under 700 px): the panel is a rail of avatars, to leave the stage its room.
const narrowCalls = matchMedia("(max-width: 699px)");
let callsCollapsed = false;   // your choice on wider windows
try { callsCollapsed = localStorage.getItem("callsPanel") === "collapsed"; } catch { /* storage unavailable */ }
const CALLS_ICON = {
  collapse: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="m15 18-6-6 6-6"/></svg>',
  expand: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="m9 18 6-6-6-6"/></svg>',
};

const fmtElapsed = (ms) => {
  const s = Math.max(0, Math.floor(ms / 1000));
  const h = Math.floor(s / 3600), m = Math.floor(s / 60) % 60, ss = String(s % 60).padStart(2, "0");
  return h ? `${h}:${String(m).padStart(2, "0")}:${ss}` : `${String(m).padStart(2, "0")}:${ss}`;
};

// Whether a call gets your microphone: it's the live call, you haven't muted it, and neither
// side has it on hold (held, it gets music or silence instead).
const getsMic = (e) => hasMic && !!e.m && !e.m.muted && e.id === audioId && !holdState(e) && !e.h.heldByThem;

// One call's status line, for the list (and grid tiles): its state, what it gets from you, and
// text. { status (row class), statusText, since, muted, held, long (held over a minute), audioOff,
// sends: { mic, cam } each { on, muted, label } (cam null on an audio call), typing, unread }.
function callStatus(e) {
  const h = e.h, m = e.m;
  const c = { status: h.call, statusText: "", since: 0, muted: false, held: false, long: false, audioOff: false, sends: null, typing: false, unread: false };
  if (h.call === "calling") c.statusText = "Calling…";
  else if (h.call === "ringing") c.statusText = h.incomingVideo ? "Incoming video call" : "Incoming call";
  else if (h.call === "ended") c.statusText = h.reason || "Call ended";
  else if (!m) c.statusText = "Connecting media…";
  else {
    c.since = e.connectedAt;
    c.held = !!h.held;
    c.muted = hasMic && m.muted;
    c.audioOff = !c.held && e.id !== audioId;
    c.typing = !!openLine(e.text, false, false);
    c.unread = e.text.unread;
    const pending = e.holdWanted !== undefined && e.holdWanted !== c.held;
    const xt = h.transfer;
    // A transfer under way says so first: it's what's happening to the call.
    if (xt && (xt.status === "asking" || xt.status === "calling")) c.statusText = transferShort(xt);
    else if (xt?.status === "failed" && xt.role === "out") c.statusText = "Transfer failed · on hold";
    else if (pending) c.statusText = e.holdWanted ? "Holding…" : "Resuming…";
    else if (c.held) {
      c.long = Date.now() - e.heldAt > 60000;
      c.statusText = `On hold · ${fmtElapsed(Date.now() - e.heldAt)}`;
    } else if (h.heldByThem) c.statusText = `${nameOf(e)} put you on hold`;
    else if (c.typing) c.statusText = "Typing…";
    else if (c.unread) c.statusText = e.text.lines.findLast((l) => !l.me)?.text || "New message";
    else c.statusText = c.audioOff ? "Audio off" : c.muted ? "Muted" : m.hasVideo ? "Video call" : "Audio call";
    // What they get from you, at a glance: your mic and camera, or why not.
    const held = holdState(e), music = h.holdMode !== "sip";
    const mic = getsMic(e) ? { on: true, label: `${nameOf(e)} hears you` }
      : { on: false, muted: hasMic && m.muted,
          label: !hasMic ? "No mic"
            : m.muted ? "Muted: they don't hear you"
            : held ? `On hold: they hear ${music ? "music" : "silence"}, not you`
            : h.heldByThem ? "They've put you on hold: they don't hear you"
            : "Your mic is on another call" };
    const camOn = m.hasVideo && m.camOn && !held && sourceAvailable(sourceOf(e));
    const cam = !m.hasVideo ? null : camOn ? { on: true, label: `${nameOf(e)} sees your video` }
      : { on: false, muted: !m.camOn,
          label: held ? (music ? "On hold: they see the On hold card" : "On hold: they see nothing")
            : !m.camOn ? "Video stopped: they see your initial"
            : "No camera: they see your initial" };
    c.sends = { mic, cam };
  }
  return c;
}

// The chips for what a call gets from you (see callStatus).
function sendsHtml(c) {
  if (!c.sends) return "";
  const chip = (s, on, off) => `<span class="send${s.on ? " on" : s.muted ? " muted" : ""}" role="img" title="${escHtml(s.label)}" aria-label="${escHtml(s.label)}">${s.on ? on : off}</span>`;
  return chip(c.sends.mic, ICON.mic, ICON.micOff) + (c.sends.cam ? chip(c.sends.cam, ICON.video, ICON.videoOff) : "");
}

// The panel shows only with two or more calls (or while the grid is on screen): with one, the
// stage says all there is to say about it.
function renderCalls() {
  const calls = listed();
  const panel = $("calls");
  panel.hidden = calls.length < 2 && !inGrid();
  const rail = narrowCalls.matches || callsCollapsed;
  panel.classList.toggle("collapsed", rail);
  const toggle = $("callsToggle");
  toggle.hidden = narrowCalls.matches;
  const label = rail ? "Show active calls" : "Fold active calls";
  toggle.setAttribute("aria-expanded", String(!rail));
  toggle.setAttribute("aria-label", label);
  toggle.title = label;
  const icon = rail ? CALLS_ICON.expand : CALLS_ICON.collapse;
  if (toggle.innerHTML !== icon) toggle.innerHTML = icon;
  $("callCount").textContent = calls.filter(isLive).length || "";
  $("callList").replaceChildren(...calls.map((e, i) => callRow(e, i, rail)));
  renderAllButtons();
}

// ---- Every call at once: Grid, and ⋯ for End all ----
// Under the list. Grid is a toggle that looks pressed while the grid is the view (a ringing call's
// answer view leaves it pressed, as it goes back to the grid once answered); picking a call (its
// row, its tile) goes back to Focus too. ⋯ (with two calls you can end) opens End all, which is
// rarely needed and asks again ("End 3 calls?") for a few seconds before it ends them: calls you've placed and calls
// you're on, not one that's only ringing.
// (Not a call you're placing over another: that's the banner's to Cancel.)
const endable = () => liveEntries().filter((e) => !e.pending && (e.h.call === "calling" || e.h.call === "connected"));
let endAllUntil = 0;
function renderAllButtons() {
  const n = endable().length, grid = inGrid();
  $("callsFoot").hidden = n < 2 && !grid;
  const gridBtn = $("gridBtn");
  const tiles = gridCalls().length;
  const gridOn = view === "grid";
  gridBtn.disabled = !gridOn && tiles < 2;
  gridBtn.setAttribute("aria-pressed", String(gridOn));
  // With the number of calls it shows, as a badge (on the rail, over the icon's corner).
  const html = `${ICON.grid}<span>Grid</span>${tiles > 1 ? `<span class="count">${tiles}</span>` : ""}`;
  if (gridBtn.innerHTML !== html) gridBtn.innerHTML = html;
  gridBtn.title = gridOn ? "Showing every call in a grid. Click again, or pick a call, for one call (G)" : `Show all ${tiles} calls in a grid (G)`;
  $("allMenuBtn").hidden = n < 2;
  if (n < 2) { endAllUntil = 0; if (openMenu === "all") closeMenu(); }
  else if (openMenu === "all") fillAllMenu();
}
// The ⋯ menu's items, refilled as things change while it's open (End all's confirm, say).
function fillAllMenu() {
  const menu = $("allMenu"), n = endable().length;
  const at = [...menu.querySelectorAll(".device-option")].indexOf(document.activeElement);
  const item = (icon, text, title, onclick, cls = "") => {
    const b = Object.assign(document.createElement("button"), { type: "button", className: `device-option ${cls}`.trim(), title, onclick });
    b.setAttribute("role", "menuitem");
    b.innerHTML = `<span class="device-check">${icon}</span><span></span>`;
    b.lastChild.textContent = text;
    return b;
  };
  const armed = Date.now() < endAllUntil;
  menu.replaceChildren(item(ICON.hangup, armed ? `End ${n} calls? Click again` : `End all ${n} calls`,
    armed ? `Click again to end all ${n} calls` : `End all ${n} calls`, endAll, "danger"));
  if (at >= 0) menu.querySelectorAll(".device-option")[at]?.focus();
}
function endAll() {
  if (Date.now() >= endAllUntil) {
    endAllUntil = Date.now() + 4000;
    setTimeout(renderAllButtons, 4100);
    renderAllButtons();
    return;
  }
  endAllUntil = 0;
  closeMenu();
  const calls = endable();
  log(`ending all ${calls.length} calls`);
  for (const e of calls) post({ type: "hangup", id: e.id });
  renderAllButtons();
}
$("gridBtn").onclick = () => setView(view === "grid" ? "focus" : "grid");
$("allMenuBtn").onclick = () => showMenu("all");

function callRow(e, i, collapsed = false) {
  const c = callStatus(e);
  const name = nameOf(e);
  const li = document.createElement("li");
  li.className = `call-row ${c.status}${c.muted ? " muted" : ""}${c.held ? " held" : ""}${c.long ? " long" : ""}${c.audioOff ? " audio-off" : ""}`;
  li.dataset.call = e.id;
  if (e.id === selectedId) li.setAttribute("aria-current", "true");   // the call on the stage
  li.title = [e.h.remote || name, c.statusText, i < 8 ? `(${i + 1})` : ""].filter(Boolean).join(" · ");
  // A click picks the call.
  li.onclick = (ev) => {
    ev.stopPropagation();
    focusCall(e.id);
  };
  const el = (tag, cls, text) => Object.assign(document.createElement(tag), { className: cls, textContent: text ?? "" });
  const avatar = el("span", "call-avatar", initial(name));
  avatar.setAttribute("aria-hidden", "true");
  setAvatar(avatar, e.h.remote);
  const top = el("span", "call-top");
  top.append(el("span", "call-name", name));
  const contact = contactFor(e.h.remote);
  if (contact) top.firstChild.append(contactMarkEl(contact.name));
  // What it gets from you: beside the name, or on the rail under the avatar.
  const sends = el("span", "call-sends");
  sends.innerHTML = sendsHtml(c);
  if (c.sends && !collapsed) top.append(sends);
  const main = el("span", "call-main");
  main.append(top, el("span", "call-status", c.statusText));
  const time = el("span", "call-time", c.since ? fmtElapsed(Date.now() - c.since) : "");
  if (c.since) time.dataset.since = c.since;
  li.append(avatar, main);
  if (c.sends && collapsed) li.append(sends);
  li.append(time);
  return li;
}

// Every second while connected: just the timers.
function renderCallTimes() {
  for (const t of document.querySelectorAll("#callList .call-time[data-since]")) t.textContent = fmtElapsed(Date.now() - Number(t.dataset.since));
}

narrowCalls.addEventListener("change", renderCalls);
$("callsToggle").onclick = () => {
  callsCollapsed = !callsCollapsed;
  try { localStorage.setItem("callsPanel", callsCollapsed ? "collapsed" : "open"); } catch { /* private window: this visit only */ }
  renderCalls();
};

// ---- Controls ----

$("dialForm").addEventListener("submit", (e) => {
  e.preventDefault();
  const address = $("address").value.trim();
  // A contact's address (typed, pasted, or picked from the list or the Contacts tab) calls from
  // the contact's account; a recent call picked from the list, from the account that call used.
  const pick = dial.pick && sameUri(dial.pick.uri, address) ? dial.pick : null;
  dial.pick = null;
  const c = contactFor(address);
  placeCall(address, c ? c.account : pick?.account, c?.name);
});
// A call answered (ours, or theirs to one we placed) empties the address bar, so it's ready for
// the next one; not while you're in it typing something else (another address, say).
let lastDialled = "";
function clearAddressOnAnswer(e) {
  const input = $("address");
  const text = input.value.trim();
  if (!text) return;
  if (document.activeElement === input && text !== lastDialled && !sameUri(text, e.h.remote)) return;
  input.value = "";
  dial.pick = null;
  dial.close();
  log(`call ${e.id} answered: address bar cleared`);
  render();
}

// Call, or Call back from History. account: the one to call from (a contact's, or the one that
// call was on): Call from switches to it first, so it shows what's used, unless it's been removed
// or disabled, when the call goes from Call from's account and a note says so. People closes, so
// the stage shows how the call's going.
function placeCall(address, account = null, who = null) {
  if (!address) return;
  const blocked = callBlocked();
  if (blocked) { log(`not calling ${address}: ${blocked}`); return; }
  closeHistory(false);
  lastDialled = address;
  account = account ? useAccount(account, who) : null;
  openMic();   // alongside the INVITE, so it's ready when the call connects
  // A new call holds the one you're on, now that you've pressed Call (the new one goes on the
  // stage when the host reports it).
  holdOthers(null);
  post({ type: "dial", address, video: state.videoAllowed !== false && callType === "video", ...(account ? { account } : {}) });
  // If the call never starts (a bad address, say), don't leave the microphone open.
  setTimeout(() => { if (!liveEntries().length) closeMic(); }, 5000);
}

// ---- Address fields: the address bar's, and Transfer's ----
// As in the prototype: focusing the address (or /) lists contacts and recent calls; typing
// filters them (see suggestions). Contacts come first, in their own group: with nothing typed the
// ones you call most, then your latest calls to anyone else; typed, the contacts that match, then
// other addresses you've called that match. Typing part of a name highlights the first match, so
// Enter calls it; a full address or number doesn't, so Enter calls what you typed. ↑/↓ move
// through the list, Enter or a click puts the highlighted entry in the field (Call, or Enter
// again, calls it), and Esc closes it. Calls that
// didn't connect show their outcome, and missed calls are red. Recents come from the call history
// (see recentCalls); contacts from contacts.js. Each entry's person button adds it to contacts, or
// removes it. The last row opens the People view's Contacts tab: "Contacts…" with nothing typed,
// or "Add … to contacts" for a typed address that isn't one yet.
//
// addressField puts all that on an input: the address bar's (dial, a dropdown) and Transfer's
// (xferField, its list always showing), so the two can't drift apart.

const SUGGEST_ICON = {
  outgoing: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M7 7h10v10"/><path d="M7 17 17 7"/></svg>',
  inbound: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M17 7 7 17"/><path d="M17 17H7V7"/></svg>',
  missed: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="m16 2 6 6"/><path d="m22 2-6 6"/><path d="M22 16.92v3a2 2 0 0 1-2.18 2 19.79 19.79 0 0 1-8.63-3.07 19.5 19.5 0 0 1-6-6 19.79 19.79 0 0 1-3.07-8.67A2 2 0 0 1 4.11 2h3a2 2 0 0 1 2 1.72c.13.96.36 1.9.7 2.81a2 2 0 0 1-.45 2.11L8.09 9.91a16 16 0 0 0 6 6l1.27-1.27a2 2 0 0 1 2.11-.45c.91.34 1.85.57 2.81.7A2 2 0 0 1 22 16.92z"/></svg>',
  addContact: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M19 8v6M22 11h-6"/></svg>',
  removeContact: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M22 11h-6"/></svg>',
};
const MAX_CONTACT_SUGGESTIONS = 5;   // nothing typed: your most-called contacts
const MAX_RECENT_SUGGESTIONS = 6;
const MAX_TYPED_CONTACTS = 5;
const MAX_TYPED_RECENT = 4;
const RECENCY_DAYS = 14;   // a call this long ago counts half as much as one just now

// The call history (history.js), newest first: { name, uri, dir: "out" | "in" | "missed", at
// (ms), note? ("Busy", "Declined": why a call didn't connect), sip? (the failure response) }.
// None from a host that keeps no history.
function recentCalls() {
  const h = historyEntries();
  return Array.isArray(h) ? h : [];
}

function relTime(at) {
  const mins = Math.round((Date.now() - at) / 60_000);
  if (mins < 1) return "Just now";
  if (mins < 60) return `${mins} min ago`;
  const hours = Math.round(mins / 60);
  if (hours < 24) return `${hours} h ago`;
  if (hours < 48) return "Yesterday";
  return `${Math.round(hours / 24)} days ago`;
}

// The user part of an address: "sip:alice@example.com" → "alice", "tel:+353…" → "+353…".
const userPart = (uri) => String(uri ?? "").toLowerCase().replace(/^(sips?|tel):/, "").split(/[@;]/)[0];

// How well what's typed matches: 0 when it starts a word of the name, the address's user part
// or the address (less sip:); 1 when it's anywhere in the name or address; -1 not at all.
function matchTier(x, q) {
  const name = String(x.name ?? "").toLowerCase();
  const uri = String(x.uri ?? "").toLowerCase();
  const bare = q.replace(/^(sips?|tel):/, "");
  if (bare && (name.startsWith(q) || name.split(/[^\p{L}\p{N}+]+/u).some((w) => w && w.startsWith(q))
    || userPart(uri).startsWith(bare) || uri.replace(/^(sips?|tel):/, "").startsWith(bare))) return 0;
  return name.includes(q) || uri.includes(q) ? 1 : -1;
}

// Per address: how often and how lately you've called or been called (each call counts 1, half
// that after RECENCY_DAYS, a third after twice that…), and its latest call.
function callStats() {
  const now = Date.now();
  const stats = new Map();
  for (const r of recentCalls()) {
    if (!r?.uri) continue;
    const k = normAddress(r.uri);
    const st = stats.get(k) ?? { score: 0, count: 0, last: r };
    st.score += 1 / (1 + Math.max(0, now - (r.at ?? now)) / (RECENCY_DAYS * 86_400_000));
    st.count++;
    stats.set(k, st);
  }
  return stats;
}

// The list for what's typed: { group: "Contacts" | "Recent", name, uri, contact, account?, dir?,
// note?, sip?, at? } entries, then the foot row ({ action: "manage" | "add" }) when foot is set.
// Contacts first: nothing typed, the ones you call most (by callStats: how often and how lately),
// then by name; typed, those that match, a prefix match (a word of the name, or the address's
// user part) before one inside, then the most called. Then Recent: one per address, newest
// first, less the contacts already listed (a contact further down shows here, by its name);
// typed, the other addresses that match, ranked the same way. skip: an address to leave out.
function suggestions(typed, { skip = null, foot = true } = {}) {
  typed = typed.trim();
  const q = typed.toLowerCase();
  const stats = callStats();
  const skipKey = skip ? normAddress(skip) : null;
  const score = (x) => stats.get(normAddress(x.uri))?.score ?? 0;
  const byName = (a, b) => a.name.localeCompare(b.name, undefined, { sensitivity: "base" });
  const contacts = [];
  const seen = new Set();
  for (const c of allContacts()) {
    const k = normAddress(c.uri);
    if (k === skipKey || seen.has(k)) continue;
    seen.add(k);
    const last = stats.get(k)?.last;
    contacts.push({ name: c.name, uri: c.uri, account: c.account ?? null, contact: true,
      ...(last ? { dir: last.dir, note: last.note, sip: last.sip, at: last.at } : {}) });
  }
  const recent = [];
  const seenRecent = new Set();
  for (const r of recentCalls()) {
    const k = r?.uri ? normAddress(r.uri) : "";
    if (!k || k === skipKey || seenRecent.has(k)) continue;
    seenRecent.add(k);
    const c = contactFor(r.uri);
    recent.push({ ...r, name: c?.name || r.name || shortName(r.uri), contact: !!c, ...(c ? { account: c.account ?? null } : {}) });
  }
  let top, rest;
  if (!q) {
    top = contacts.sort((a, b) => score(b) - score(a) || byName(a, b)).slice(0, MAX_CONTACT_SUGGESTIONS);
    const shown = new Set(top.map((x) => normAddress(x.uri)));
    rest = recent.filter((x) => !shown.has(normAddress(x.uri))).slice(0, MAX_RECENT_SUGGESTIONS);
  } else {
    const rank = (list, max) => list.map((x) => ({ x, tier: matchTier(x, q) })).filter((m) => m.tier >= 0)
      .sort((a, b) => a.tier - b.tier || score(b.x) - score(a.x) || byName(a.x, b.x)).slice(0, max).map((m) => m.x);
    top = rank(contacts, MAX_TYPED_CONTACTS);
    rest = rank(recent.filter((x) => !x.contact), MAX_TYPED_RECENT);
  }
  const items = [...top.map((x) => ({ ...x, group: "Contacts" })), ...rest.map((x) => ({ ...x, group: "Recent" }))];
  if (foot) {
    // The way to the Contacts tab; not while filtering by name, so a name that matches nothing
    // closes the list, as in the prototype.
    if (!typed) items.push({ group: "", action: "manage" });
    else if (looksLikeAddress(typed) && !contactFor(typed)) items.push({ group: "", action: "add", uri: typed });
  }
  return items;
}

const looksLikeAddress = (q) => /[@:]/.test(q) || /^[+\d*#\s]+$/.test(q.trim());

/**
 * Suggestions on an address input, for the address bar and Transfer. popup: the list drops down
 * while the field has focus (the address bar's); otherwise it's always there (Transfer's). badge:
 * the saved-contact pill inside the field; clear: its × button (both inside a .dest-wrap). foot:
 * the Contacts… and Add to contacts rows. goIcon: the icon on the highlighted row (what Enter
 * does). skip(): an address to leave out. pick(entry): a row was chosen: its address is in the
 * field (f.pick is the entry, for its name and account), and nothing is dialled or sent. changed(): after every refresh. Returns its state: { open, items, active, pick,
 * refresh(keepActive), close(), renderBadge() }.
 */
function addressField({ input, list, clear, badge, idPrefix, popup, foot, goIcon, goLabel, skip = () => null, pick, changed = () => {} }) {
  const wrap = input.closest(".dest-wrap");
  const f = { open: false, items: [], active: -1, pick: null };

  // keepActive: a refresh (contacts or history changed) keeps the highlighted entry where it can.
  f.refresh = (keepActive = false) => {
    if (input.disabled) return;
    const q = input.value;
    const items = suggestions(q, { skip: skip(), foot });
    let active = q.trim() && !looksLikeAddress(q) && items.length && !items[0].action ? 0 : -1;
    if (keepActive && f.active >= 0) {
      const was = f.items[f.active];
      const i = items.findIndex((x) => x.group === was?.group && sameUri(x.uri, was?.uri));
      active = i >= 0 ? i : Math.min(f.active, items.length - 1);
    }
    Object.assign(f, { open: popup ? items.length > 0 : true, items, active });
    if (!f.open) f.active = -1;
    render();
    f.renderBadge();
    changed();
  };

  f.close = () => {
    f.renderBadge();
    if (!popup || !f.open) return;
    Object.assign(f, { open: false, items: [], active: -1 });
    render();
  };

  // The badge: what's in the field is a saved contact's address (typed, pasted or picked). Also
  // the clear (×) button, shown while there's any text.
  f.renderBadge = () => {
    const value = input.value;
    const c = contactFor(value.trim());
    badge.hidden = !c;
    clear.hidden = !value;
    wrap.classList.toggle("has-text", !!value);
    wrap.classList.toggle("has-contact", !!c);
    if (c) {
      badge.innerHTML = `${contactMark(c.name)}<span class="dest-contact-name">${escHtml(c.name)}</span>`;
      badge.title = `Saved contact: ${c.name}`;
    }
  };

  function render() {
    const { open, items, active } = f;
    list.hidden = !open;
    input.setAttribute("aria-expanded", String(open && items.length > 0));
    if (open && active >= 0) input.setAttribute("aria-activedescendant", `${idPrefix}-${active}`); else input.removeAttribute("aria-activedescendant");
    if (!open) { list.innerHTML = ""; return; }
    let group = "";
    list.innerHTML = items.map((it, i) => {
      const cls = `suggest-item${i === active ? " active" : ""}`;
      const attrs = `role="option" id="${idPrefix}-${i}" aria-selected="${i === active}" data-idx="${i}"`;
      if (it.action) {
        const text = it.action === "add" ? `Add <b>${escHtml(it.uri)}</b> to contacts…` : "Contacts…";
        return `<div class="${cls} suggest-foot" ${attrs}>${SUGGEST_ICON.addContact}<span class="suggest-foot-text">${text}</span></div>`;
      }
      const head = it.group !== group
        ? `<div class="suggest-group${it.group === "Contacts" ? " contacts" : ""}" role="presentation">${(group = it.group)}</div>` : "";
      const missed = it.dir === "missed";
      const meta = !it.at ? "<span></span>"
        : `<span class="suggest-meta ${missed ? "bad" : ""}" ${it.sip ? `title="${escHtml(it.sip)}"` : ""}>${SUGGEST_ICON[missed ? "missed" : it.dir === "in" ? "inbound" : "outgoing"]}
            ${missed ? "Missed · " : it.note ? `${escHtml(it.note)} · ` : ""}${relTime(it.at)}</span>`;
      const label = it.contact ? `Remove ${it.name} from contacts` : `Add ${it.name} to contacts`;
      return `${head}<div class="${cls}${it.contact ? " is-contact" : ""}" ${attrs}>
        <span class="suggest-avatar" aria-hidden="true"${avatarAttr(it.uri)}>${escHtml(initial(it.name))}</span>
        <span class="suggest-main"><span class="suggest-name">${escHtml(it.name)}${it.contact ? contactMark(it.name) : ""}</span><span class="suggest-uri">${escHtml(it.uri)}${fromLabel(it)}</span></span>
        ${meta}
        <button type="button" class="suggest-contact" tabindex="-1" data-contact="${it.contact ? "remove" : "add"}" data-idx="${i}"
          title="${escHtml(label)}" aria-label="${escHtml(label)}">${it.contact ? SUGGEST_ICON.removeContact : SUGGEST_ICON.addContact}</button>
        <span class="suggest-call" title="${escHtml(goLabel)}" aria-hidden="true">${goIcon()}</span>
      </div>`;
    }).join("");
    list.querySelector(".suggest-item.active")?.scrollIntoView({ block: "nearest" });
  }

  // A row chosen: the foot opens the Contacts tab; an entry goes into the field, and nothing
  // more: the call (or transfer) waits for its button, or Enter with no row highlighted.
  function choose(i) {
    const it = f.items[i];
    if (!it) return;
    if (it.action) {
      f.close();
      openContacts(it.action === "add" ? { name: "", uri: it.uri } : null);
      return;
    }
    input.value = it.uri;
    f.pick = it;
    if (popup) f.close(); else f.refresh();
    input.focus();
    pick(it);
  }

  if (popup) {
    input.addEventListener("focus", () => f.refresh());
    input.addEventListener("blur", () => setTimeout(f.close, 0));
  }
  input.addEventListener("input", () => f.refresh());
  input.addEventListener("keydown", (e) => {
    if (e.key === "ArrowDown" || e.key === "ArrowUp") {
      e.preventDefault();
      if (!f.open) { f.refresh(); return; }
      const n = f.items.length;
      if (!n) return;
      f.active = e.key === "ArrowDown" ? (f.active + 1) % n : (f.active <= 0 ? n - 1 : f.active - 1);
      render();
      changed();
    } else if (e.key === "Enter" && f.open && f.active >= 0) {
      e.preventDefault();
      choose(f.active);
    } else if (e.key === "Escape" && popup && f.open) {
      e.preventDefault();
      e.stopPropagation();   // Esc closes the list, and nothing else (not a menu, panel or ended call)
      f.close();
    } else if (e.key === "Escape" && popup && input.value) {
      // A second Esc, with the list closed, empties the field (leaving the list closed).
      e.preventDefault();
      e.stopPropagation();
      input.value = "";
      f.renderBadge();
      changed();
    }
  });
  // The clear (×) button: empties the field and lists Contacts and Recent, as deleting the text
  // would. mousedown's preventDefault keeps focus in the field (and the list open) until the click.
  clear.innerHTML = ICON.close;
  clear.addEventListener("mousedown", (e) => e.preventDefault());
  clear.addEventListener("click", () => {
    if (input.disabled) return;
    input.value = "";
    input.focus();
    f.refresh();
  });
  list.addEventListener("mousedown", (e) => e.preventDefault());   // keep focus in the field
  list.addEventListener("click", (e) => {
    const btn = e.target.closest(".suggest-contact");
    if (btn) {
      const it = f.items[Number(btn.dataset.idx)];
      if (!it) return;
      if (btn.dataset.contact === "remove") removeContact(it.uri); else saveContact(it.name, it.uri, it.account);
      f.refresh(true);
      return;
    }
    const item = e.target.closest(".suggest-item");
    if (item) choose(Number(item.dataset.idx));
  });
  return f;
}

// A contact's own Call from account, after its address (not a recent call's: that's in History).
function fromLabel(it) {
  const name = it.contact && it.account ? accountName(it.account) : null;
  return name ? ` · from ${escHtml(name)}` : "";
}

// The address bar's. dial.pick: the entry last picked from the list, so Call knows its account.
const dial = addressField({
  input: $("address"), list: $("dialSuggest"), clear: $("destClear"), badge: $("destContact"),
  idPrefix: "sug", popup: true, foot: true, goIcon: () => ICON.phone, goLabel: "Call",
  pick: syncCallBtn, changed: syncCallBtn,
});
$("dialForm").addEventListener("submit", () => dial.close());
// Either list, when what it reads changes: the history store says when a call's ended, and
// contacts.js when a contact's saved or removed.
function refreshAddressFields() {
  if (dial.open) dial.refresh(true);
  dial.renderBadge();
  if (xferOpen) xferField.refresh(true);
}
document.addEventListener("history-changed", () => { if (dial.open) dial.refresh(true); if (xferOpen) xferField.refresh(true); });
// Contacts (contacts.js): the store the suggestions read, and the People view's Contacts tab
// that edits it.
initContacts({
  post,
  log,
  shortName,
  // Call, from the tab: the way Call does, with the contact's address in the field, which closes
  // the view (placeCall) so the stage shows how the call's going. Not while a call can't be
  // placed (callBlocked): then nothing happens, and the address bar keeps what's in it.
  call(uri) {
    const blocked = callBlocked();
    if (blocked) { log(`not calling ${uri}: ${blocked}`); return; }
    $("address").value = uri;
    syncCallBtn();
    $("dialForm").requestSubmit();
  },
  changed() { refreshAddressFields(); render(); },
  openPeople,
});
// / anywhere but a text field goes to the address field, which opens the list.
document.addEventListener("keydown", (e) => {
  if (e.key !== "/" || e.ctrlKey || e.altKey || e.metaKey || openMenu) return;
  if (e.target.closest?.("input, textarea, select, [contenteditable], dialog")) return;
  if ($("address").disabled) return;
  e.preventDefault();
  $("address").focus();
});

$("callType").querySelector('[data-type="audio"]').innerHTML = `${ICON.phone}<span class="slide-word" data-text="Audio">Audio</span>`;
$("callType").querySelector('[data-type="video"]').innerHTML = `${ICON.video}<span class="slide-word" data-text="Video">Video</span>`;
function setCallType(type) {
  callType = type;
  if (type === "video") cameraInUse = false;   // a fresh try for the preview, because you asked
  render();
}
// The track flips the switch; a word either side picks that side.
$("callType").addEventListener("click", (e) => {
  const btn = e.target.closest("button");
  if (!btn || btn.disabled) return;
  setCallType(btn.dataset.type ?? (callType === "video" ? "audio" : "video"));
});
// The track takes focus: Space flips it, Left and Right push it to that side.
$("callType").addEventListener("keydown", (e) => {
  if ((e.key !== "ArrowLeft" && e.key !== "ArrowRight") || e.target.disabled) return;
  e.preventDefault();
  setCallType(e.key === "ArrowRight" ? "video" : "audio");
});
// ---- Device menus (the ▾ on Mute and Camera) ----
// Mute's lists microphones and speakers; Camera's lists cameras, and in diagnostics mode the
// test pattern. A pick takes effect straight away, mid-call too (a track swap, no renegotiation),
// and is remembered for next time.

let openMenu = null;   // "mic", "cam", "answer", "all" (the calls panel's ⋯) or null
const MENUS = { mic: ["micMenu", "micMenuBtn"], cam: ["camMenu", "camMenuBtn"], answer: ["answerMenu", "answerMenuBtn"], all: ["allMenu", "allMenuBtn"] };
let cameras = [];      // the video inputs, for the Answer button's label; refreshed when devices change
let lastPhase = null;  // the call phase at the last render, to focus Answer once when ringing starts
let lastSelected = null;   // and the call that was on the stage

async function refreshCameras() {
  try {
    const list = await navigator.mediaDevices.enumerateDevices();
    cameras = list.filter((d) => d.kind === "videoinput" && d.deviceId);
    hasMic = list.some((d) => d.kind === "audioinput");
  } catch { /* keep the old list */ }
  render();
}
navigator.mediaDevices.addEventListener?.("devicechange", refreshCameras);

// ---- Answering a video call: Answer ▾ ----
// The ▾ picks what your video will be: a camera, the test pattern (diagnostics mode), or camera
// off. Under "Answer" the button says which, so you know before you answer. It starts from the
// Camera button's choice (camPref), so camera off there means camera off here.
const answerSplit = document.createElement("div");
answerSplit.className = "split answer-split";
answerSplit.innerHTML = `<button type="button" class="btn btn-primary" id="answerBtn"><span class="answer-main"></span><span class="answer-src"></span></button>
  <button type="button" class="btn btn-primary split-caret" id="answerMenuBtn" aria-haspopup="menu" aria-expanded="false"
    aria-label="Choose your camera for this call" title="Choose your camera for this call">${ICON.chevronDown}</button>
  <div class="device-menu down" id="answerMenu" role="menu" aria-label="Your camera" hidden></div>`;
answerSplit.querySelector("#answerBtn").onclick = () => answerCall(selectedId);
answerSplit.querySelector("#answerMenuBtn").onclick = () => showMenu("answer");

const answerCamOn = () => (answerCamera ?? camStart()) && videoAvailable();
function answerSourceLabel() {
  if (!answerCamOn()) {
    return videoSource === "camera" && !hasCamera ? "No camera"
      : videoSource === "camera" && cameraInUse ? "Camera in use" : "Camera off";
  }
  if (videoSource !== "camera") return SOURCE_LABEL[videoSource];
  // The camera the answered call will open (devices.camera, as the ▾ ticks), not the first one.
  return cameras.find((d) => d.deviceId === currentCameraId(cameras, devices.camera))?.label || "Camera";
}

// current: the call you're on, which answering holds.
function renderAnswer(current) {
  const video = !!state.incomingVideo && state.videoAllowed !== false;
  const on = video && answerCamOn();
  const main = `${video ? (on ? ICON.video : ICON.videoOff) : ICON.phone}<span>${current ? "Answer & hold current" : "Answer"}</span>`;
  const el = answerSplit.querySelector(".answer-main");
  if (el.innerHTML !== main) el.innerHTML = main;
  const src = answerSplit.querySelector(".answer-src");
  src.hidden = !video;
  src.textContent = video ? answerSourceLabel() : "";
  answerSplit.querySelector("#answerBtn").title = !video ? "Answer"
    : on ? `Answer with ${src.textContent}` : "Answer with your camera off. You'll still see them.";
  answerSplit.querySelector("#answerMenuBtn").hidden = !video;
  answerSplit.classList.toggle("single", !video);
}

const currentMicId = () => localTrack("audio")?.getSettings().deviceId ?? (devices.mic || "default");
// The camera ticked: the call's (or for Answer, the next call's), as opened if it's the open one.
function currentCameraId(cameras, want) {
  const track = localTrack("video");
  if (track && want === cameraOpenedFor) return track.getSettings().deviceId;
  return cameras.some((d) => d.deviceId === want) ? want : cameras[0]?.deviceId;
}

async function showMenu(kind) {
  if (openMenu === kind) { closeMenu(); return; }
  closeMenu();
  openMenu = kind;
  if (kind === "all") {
    fillAllMenu();
    $("allMenu").hidden = false;
    fitInStage($("allMenu"));
    $("allMenuBtn").setAttribute("aria-expanded", "true");
    $("allMenu").querySelector(".device-option:not(:disabled)")?.focus();
    return;
  }
  let list = [];
  try { list = await navigator.mediaDevices.enumerateDevices(); } catch { /* none to show */ }
  if (openMenu !== kind) return;
  const of = (k) => list.filter((d) => d.kind === k && d.deviceId);
  // The Camera ▾ ticks the selected call's source; Answer's, the one the new call will start with.
  const source = kind === "answer" ? videoSource : sourceOf(sel());
  const groups = kind === "mic"
    ? [["mic", "Microphone", of("audioinput"), currentMicId()],
       ["speaker", "Speaker", of("audiooutput"), devices.speaker || "default"]]
    : [["camera", "Camera", of("videoinput"), source !== "camera" ? null : currentCameraId(of("videoinput"), kind === "answer" ? devices.camera : cameraIdOf(sel()))]];
  if (kind !== "mic" && document.body.classList.contains("diag")) {
    groups.push(["test", "Diagnostics", Object.entries(SOURCE_LABEL).map(([deviceId, label]) => ({ deviceId, label })),
      source !== "camera" ? source : null]);
  }
  if (kind === "answer") {
    // While the camera's off nothing above is ticked; "Camera off" is instead.
    const on = answerCamOn();
    if (!on) for (const g of groups) g[3] = null;
    groups.push(["off", "", [{ deviceId: "off", label: "Camera off (you'll still see them)" }], on ? null : "off"]);
  }
  const menu = $(MENUS[kind][0]);
  menu.replaceChildren();
  groups.forEach(([key, label, items, current], gi) => {
    if (gi) menu.append(Object.assign(document.createElement("div"), { className: "device-menu-sep" }));
    if (label) menu.append(Object.assign(document.createElement("div"), { className: "device-menu-label", textContent: label }));
    if (!items.length) menu.append(Object.assign(document.createElement("div"), { className: "device-none", textContent: "None found" }));
    items.forEach((d, i) => {
      const on = d.deviceId === current;
      const opt = Object.assign(document.createElement("button"), { type: "button", className: "device-option" });
      opt.setAttribute("role", "menuitemradio");
      opt.setAttribute("aria-checked", String(on));
      opt.innerHTML = `<span class="device-check">${on ? ICON.check : ""}</span><span></span>`;
      opt.lastChild.textContent = d.label || `${label} ${i + 1}`;
      opt.onclick = () => pickDevice(key, d.deviceId, kind === "answer");
      menu.append(opt);
    });
  });
  menu.hidden = false;
  if (kind === "answer") fitInStage(menu);
  $(MENUS[kind][1]).setAttribute("aria-expanded", "true");
  (menu.querySelector('[aria-checked="true"]') ?? menu.querySelector(".device-option"))?.focus();
}

// The Answer menu is inside the stage, which clips (overflow: hidden), so a z-index can't lift
// it out (the ⋯ menu likewise, in the calls panel). It's positioned against the window instead (position: fixed isn't clipped by the
// stage), below the button if it fits, else above, else on the roomier side, scrolling.
function fitInStage(menu) {
  const btn = menu.parentElement.getBoundingClientRect();
  const margin = 8, gap = 6;
  Object.assign(menu.style, { position: "fixed", maxHeight: "", top: "", bottom: "", left: `${btn.left}px` });
  const height = menu.scrollHeight + 2;
  const below = innerHeight - btn.bottom - gap - margin;
  const above = btn.top - gap - margin;
  const down = height <= below || (height > above && below >= above);
  menu.style.top = down ? `${btn.bottom + gap}px` : "auto";
  menu.style.bottom = down ? "auto" : `${innerHeight - btn.top + gap}px`;
  const room = down ? below : above;
  if (height > room) menu.style.maxHeight = `${Math.max(room, 80)}px`;
  menu.style.left = `${Math.max(margin, Math.min(btn.left, innerWidth - menu.offsetWidth - margin))}px`;
}
addEventListener("resize", () => { if (openMenu === "answer" || openMenu === "all") closeMenu(); });

function closeMenu() {
  if (!openMenu) return;
  const kind = openMenu;
  openMenu = null;
  if (kind === "all") endAllUntil = 0;
  $(MENUS[kind][0]).hidden = true;
  $(MENUS[kind][1]).setAttribute("aria-expanded", "false");
}

// From the Answer menu (forAnswer), a camera or the test pattern also means camera on for the
// call being answered, and "off" means camera off. A source picked from the Camera ▾ is for the
// selected call (and the next ones); from Answer's, for the call being answered (and the next).
async function pickDevice(key, id, forAnswer = false) {
  const target = forAnswer ? null : sel();
  closeMenu();
  if (forAnswer) { answerCamera = key !== "off"; if (answerCamera) privacyRelease(); }
  if (key === "off") log("answering with camera off");
  else if (key === "mic") await pickMic(id);
  else if (key === "speaker") {
    devices.speaker = id;
    saveDevices();
    try {
      for (const e of entries.values()) await e.el.setSinkId(id);
      log(`speaker: ${id}`);
    } catch (err) { log(`speaker unavailable: ${err.name} ${err.message}`); }
  } else if (key === "camera") {
    devices.camera = id;
    saveDevices();
    if (target?.m) target.cameraId = id;
    log(`camera${target?.m ? ` for call ${target.id}` : ""}: ${id}`);
    cameraInUse = false;   // a fresh try, because you asked
    setVideoSource("camera", target);
    await syncCamera();   // switches to it, if something is sending the camera
    if (!forAnswer) await cameraOnForPick();
  } else if (key === "test") {
    setVideoSource(id, target);   // the test pattern or audio bars
    if (!forAnswer) await cameraOnForPick();
  }
  render();
}

// Picking a source from the Camera menu means you want to send it: if the camera was off, it
// comes on, on a video call or (before one) for the next video call. Not on an audio call,
// where it's only the choice for Add video.
async function cameraOnForPick() {
  if (call?.hasVideo) { if (!call.camOn) await setCamera(true); }
  else if (!call && !camStart()) setCamPref(true);
}

// A new microphone replaces the old one in place: same mute state, and on a call the audio
// sender just swaps tracks.
async function pickMic(id) {
  devices.mic = id;
  saveDevices();
  if (!localTrack("audio")) { log(`microphone for calls: ${id}`); return; }   // opens with the next call
  let fresh;
  try {
    fresh = (await navigator.mediaDevices.getUserMedia({ audio: { ...MIC_CONSTRAINTS, ...(id ? { deviceId: { exact: id } } : {}) } })).getAudioTracks()[0];
  } catch (err) {
    log(`microphone unavailable: ${err.name} ${err.message}`);
    return;
  }
  const old = localTrack("audio");
  const senders = [...entries.values()].map((e) => e.m?.audioSender).filter((s) => s && old && s.track === old);
  if (old) {
    fresh.enabled = old.enabled;
    local.removeTrack(old);
    old.stop();
  }
  local.addTrack(fresh);
  for (const sender of senders) await sender.replaceTrack(fresh);
  for (const e of entries.values()) {
    if (!e.m) continue;
    e.m.localAnalyser?.source.disconnect();
    e.m.localAnalyser = analyserFor(fresh);
  }
  log(`microphone: ${fresh.label}`);
}

$("micMenuBtn").onclick = () => showMenu("mic");
$("camMenuBtn").onclick = () => showMenu("cam");
document.addEventListener("pointerdown", (e) => {
  if (openMenu && !e.target.closest(".device-menu, .split-caret, .calls-more")) closeMenu();
});
document.addEventListener("keydown", (e) => {
  if (!openMenu) return;
  const menu = $(MENUS[openMenu][0]);
  if (e.key === "Escape") {
    const btn = $(MENUS[openMenu][1]);
    closeMenu();
    btn.focus();
  } else if (e.key === "ArrowDown" || e.key === "ArrowUp") {
    e.preventDefault();
    const opts = [...menu.querySelectorAll(".device-option")];
    const i = opts.indexOf(document.activeElement);
    opts[(i + (e.key === "ArrowDown" ? 1 : opts.length - 1)) % opts.length]?.focus();
  }
});

// Self view: shows or hides your own picture during a video call (what's sent doesn't change).
// Remembered between runs. V does the same, unless you're typing.
let selfViewOn = true;
try { selfViewOn = localStorage.getItem("selfView") !== "off"; } catch { /* storage unavailable */ }
function toggleSelfView() {
  selfViewOn = !selfViewOn;
  settings?.refresh();
  try { localStorage.setItem("selfView", selfViewOn ? "on" : "off"); } catch { /* storage unavailable */ }
  log(`self view ${selfViewOn ? "on" : "off"}`);
  render();
}
$("selfViewBtn").onclick = toggleSelfView;
initSelfView($("stage"), $("selfBox"), $("selfGrip"));   // drag, resize and reset it
document.addEventListener("keydown", (e) => {
  if (e.key.toLowerCase() !== "v" || e.ctrlKey || e.altKey || e.metaKey || openMenu) return;
  if (e.target.closest?.("input, textarea, select, [contenteditable]")) return;
  if ($("selfViewBtn").hidden || inGrid()) return;   // no self view to show or hide
  e.preventDefault();
  toggleSelfView();
});

// Simulated loss (Diagnostics menu): the host drops that share of the audio or video packets
// we send on the SIP leg. Labels follow the slider as it moves; the host hears once it's let go.
function showLoss() {
  for (const kind of ["Audio", "Video"]) {
    const v = Number($(`loss${kind}`).value);
    $(`loss${kind}Out`).textContent = `${v}%`;
    $(`loss${kind}Out`).classList.toggle("on", v > 0);
  }
  const on = Number($("lossAudio").value) > 0 || Number($("lossVideo").value) > 0;
  $("tabNet").innerHTML = `Network${on ? `<span class="dev-dot" title="Loss is on"></span>` : ""}`;
}
for (const kind of ["Audio", "Video"]) {
  $(`loss${kind}`).addEventListener("input", showLoss);
  $(`loss${kind}`).addEventListener("change", () => post({ type: "loss", audio: Number($("lossAudio").value), video: Number($("lossVideo").value) }));
}
let lossLoaded = false;   // the host's starting values (--loss-audio, --loss-video), taken from the first state

// ---- Diagnostics (F12) ----
// A panel docked at the bottom, like a browser's developer tools: the SIP log, Call details,
// Call quality and Network (the loss sliders). While it's open the app is in diagnostics mode, which also shows
// your SIP address in the header, and the test video sources. Closed at every start
// (--diagnostics opens it, for testing). Ctrl+Shift+I is left to WebView2's own DevTools.
function setDiagnostics(on) {
  document.body.classList.toggle("diag", on);
  $("devPanel").hidden = !on;
  if (on) { const el = $("log"); el.scrollTop = el.scrollHeight; }
  // The test pattern and audio bars are diagnostics things, so the camera takes over again.
  // But it isn't sent until you turn it on: the camera never starts by itself. (With no camera
  // the placeholder goes, as it would anyway.)
  if (!on) {
    const onTest = [...entries.values()].filter((e) => e.m && sourceOf(e) !== "camera");
    const sending = onTest.filter((e) => e.m.hasVideo && e.m.camOn);
    for (const e of sending) setCamera(false, e);
    for (const e of onTest) e.source = "camera";
    if (videoSource !== "camera" && !sending.length) camPref = false;
    if (onTest.length || videoSource !== "camera") setVideoSource("camera");
  }
  // Simulated loss is a diagnostics thing too: leaving diagnostics turns it off, so it can't
  // quietly carry on spoiling calls with the sliders out of sight.
  if (!on && (Number($("lossAudio").value) > 0 || Number($("lossVideo").value) > 0)) {
    $("lossAudio").value = 0;
    $("lossVideo").value = 0;
    showLoss();
    post({ type: "loss", audio: 0, video: 0 });
  }
  closeMenu();
  renderQuality();
  render();
}
document.body.classList.toggle("diag", params.get("diag") === "1");
$("devPanel").hidden = params.get("diag") !== "1";
// F12, caught before WebView2 would open its DevTools with it.
document.addEventListener("keydown", (e) => {
  if (e.key !== "F12" || e.ctrlKey || e.altKey || e.shiftKey || e.metaKey) return;
  e.preventDefault();
  e.stopPropagation();
  setDiagnostics($("devPanel").hidden);
}, true);
$("devClose").innerHTML = ICON.close;
$("devClose").onclick = () => setDiagnostics(false);

// The padlock in the call info: green with SIP and the media both encrypted, yellow with one of
// them, and grey and open with neither. A click shows the call's security under it.
function renderInfoLock() {
  const d = sel()?.details;
  const b = $("infoLock");
  b.hidden = !d;
  if (!d) return;
  const n = Number(!!d.mediaEncrypted) + Number(!!d.signallingEncrypted);
  const icon = n ? ICON.lock : ICON.unlock;
  if (b.innerHTML !== icon) b.innerHTML = icon;
  b.classList.toggle("partial", n === 1);
  b.classList.toggle("none", n === 0);
  const sip = (d.signalling ?? "UDP").split(" ")[0];
  const how = d.mediaSecurity ?? "SRTP";
  const what = n === 2 ? `Encrypted: ${how} media, SIP over ${sip === "WSS" ? "WSS (TLS)" : "TLS"}`
    : n === 0 ? `Not encrypted: plain RTP, SIP over ${sip}`
    : d.mediaEncrypted ? `Media encrypted (${how}); SIP unencrypted (${sip})` : "SIP encrypted (TLS); media unencrypted (plain RTP)";
  b.title = `${what}. Click for more.`;
  b.setAttribute("aria-label", b.title);
  renderSecurity();
}

// The call's security, for anyone, under the padlock: is the conversation (voice and video)
// encrypted, and the call setup (SIP: who's calling whom)? Each with the technical name small
// (DTLS + SRTP, SIP over TLS), so it can be recognised, then what that means in a sentence.
// (The rest is Call details, in Diagnostics.) Media encrypted with plain SRTP but SIP not is only partly
// protected: its keys travel in the SIP, in the clear. Esc, a click away or the padlock closes it.
let secHtml = "";
function renderSecurity() {
  const pop = $("secPop");
  if (pop.hidden) return;
  const d = sel()?.details;
  if (!d || $("info").hidden) { setSecurityOpen(false); return; }
  const esc = (t) => String(t).replace(/[&<>"]/g, (ch) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[ch]));
  const media = !!d.mediaEncrypted, sip = !!d.signallingEncrypted;
  const how = d.mediaSecurity ?? "SRTP";
  const keysInSip = media && !sip && how !== "DTLS + SRTP";   // SDES: the keys are in the SIP
  const transport = (d.signalling ?? "UDP").split(" ")[0];
  const [title, cls, means] =
    media && sip ? ["This call is encrypted", "ok", "Others on the network can't listen in or see who you're calling."]
    : keysInSip ? ["This call is only partly protected", "part", "Your conversation is encrypted, but the key to it is sent in the unencrypted call setup, so someone watching the network could still listen in."]
    : media ? ["Your conversation is encrypted", "part", "Others on the network can't listen in, but could see who you're calling."]
    : sip ? ["Your conversation isn't encrypted", "part", "Others on the network can't see who you're calling, but could listen in."]
    : ["This call isn't encrypted", "off", "Someone on the network between you could listen in and see who you're calling."];
  const row = (on, what, sub, tech) => `<li class="sec-row"><span class="${on ? "ok" : "off"}">${on ? ICON.lock : ICON.unlock}</span>`
    + `<span class="sec-what">${what}</span><span class="sec-state${on ? "" : " no"}">${on ? "Encrypted" : "Not encrypted"}</span>`
    + `<span class="sec-sub">${sub} · ${esc(tech)}</span></li>`;
  const html = `<div class="sec-title" id="secTitle"><span class="${cls}">${media || sip ? ICON.lock : ICON.unlock}</span>${title}</div>`
    + `<ul class="sec-rows">`
    + row(media, "Your conversation", "Voice and video", media ? how : "plain RTP")
    + row(sip, "Call setup", "Who's calling whom", sip ? (transport === "WSS" ? "SIP over WSS (TLS)" : "SIP over TLS") : `SIP over ${transport}`)
    + `</ul><p class="sec-means">${means}</p>`;
  if (html === secHtml) return;
  pop.innerHTML = secHtml = html;
}
function setSecurityOpen(on) {
  $("secPop").hidden = !on;
  $("infoLock").setAttribute("aria-expanded", String(on));
  secHtml = "";
  if (on) renderSecurity();
}
$("infoLock").setAttribute("aria-haspopup", "dialog");
$("infoLock").onclick = () => setSecurityOpen($("secPop").hidden);
document.addEventListener("keydown", (ev) => {
  if ($("secPop").hidden || ev.key !== "Escape") return;
  ev.preventDefault();
  ev.stopImmediatePropagation();
  setSecurityOpen(false);
  $("infoLock").focus();
}, true);
document.addEventListener("click", (ev) => {
  if (!$("secPop").hidden && !$("secPop").contains(ev.target) && !$("infoLock").contains(ev.target)) setSecurityOpen(false);
}, true);

function setDevTab(tab) {
  for (const b of document.querySelectorAll(".dev-tab")) b.setAttribute("aria-selected", String(b.dataset.tab === tab));
  $("log").hidden = tab !== "log";
  $("paneDetails").hidden = tab !== "details";
  $("paneQuality").hidden = tab !== "quality";
  $("paneNet").hidden = tab !== "net";
  $("logCopy").hidden = $("logClear").hidden = tab !== "log";
  if (tab === "log") $("log").scrollTop = $("log").scrollHeight;
  if (tab === "details") renderDetails();
  if (tab === "quality") renderQuality();
}
for (const b of document.querySelectorAll(".dev-tab")) b.onclick = () => setDevTab(b.dataset.tab);
$("logCopy").onclick = () => {
  post({ type: "copy", text: $("log").textContent });
  $("logCopy").textContent = "Copied";
  setTimeout(() => { $("logCopy").textContent = "Copy log"; }, 1500);
};
$("logClear").onclick = () => { $("log").textContent = ""; };

// Dragging the panel's top edge resizes it, between 120 px and 70% of the window. The height you
// leave it at is remembered (localStorage, per profile on the desktop) and used next time, kept
// within the same limits in case the window is smaller now.
const devHeight = (h) => Math.round(Math.max(120, Math.min(window.innerHeight * 0.7, h)));
try {
  const saved = Number(localStorage.getItem("devPanelHeight"));
  if (saved > 0) $("devPanel").style.setProperty("--dev-h", `${devHeight(saved)}px`);
} catch { /* storage unavailable */ }
$("devResize").addEventListener("pointerdown", (e) => {
  e.preventDefault();
  const handle = e.currentTarget;
  handle.setPointerCapture(e.pointerId);
  document.body.classList.add("dev-resizing");
  const move = (ev) => {
    $("devPanel").style.setProperty("--dev-h", `${devHeight(window.innerHeight - ev.clientY)}px`);
  };
  const up = () => {
    handle.removeEventListener("pointermove", move);
    document.body.classList.remove("dev-resizing");
    const h = parseInt($("devPanel").style.getPropertyValue("--dev-h"), 10);
    if (h > 0) try { localStorage.setItem("devPanelHeight", String(h)); } catch { /* storage unavailable */ }
  };
  handle.addEventListener("pointermove", move);
  handle.addEventListener("pointerup", up, { once: true });
  handle.addEventListener("pointercancel", up, { once: true });
});
if (SOURCE_LABEL[params.get("video")]) setVideoSource(params.get("video"));   // --test-pattern, --audio-bars (testing)

$("hangupBtn").onclick = () => post({ type: "hangup", id: selectedId });
$("holdBtn").onclick = () => { const e = sel(); if (e && holdState(e)) goLive(e); else holdCall(e, true); };
$("heldResume").onclick = () => goLive(sel());
$("heldResume").innerHTML = `${ICON.play}<span>Resume</span>`;
// Mute: the selected call's microphone. Before a call, whether calls start muted. Unmuting turns
// the privacy switch off (the other calls stay muted).
function toggleMute() {
  const e = sel();
  if (e?.m) {
    e.m.muted = !e.m.muted;
    if (!e.m.muted) privacyRelease();
    log(`call ${e.id}: ${e.m.muted ? "muted" : "unmuted"}`);
  } else {
    const unmute = startMuted();
    micMuted = !unmute;
    if (unmute) privacyRelease();
    log(micMuted ? "calls start muted" : "calls start unmuted");
  }
  render();
}
$("muteBtn").onclick = toggleMute;

// The privacy switch (Shift+M), like a desk phone's mute key or a safety switch: on, every call
// is muted and every video call sends your placeholder instead of the camera, which closes, and
// calls you place or answer start that way too. Off again, each call goes back to how it was
// (a call that started while it was on, to how a new call would). Unmuting a call or turning a
// camera on turns it off by itself, changing only that call. Never remembered.
function togglePrivacy() {
  if (privacy) {
    const was = privacy.calls;
    privacy = null;
    for (const e of entries.values()) {
      if (!e.m) continue;
      const w = was.get(e.id) ?? { muted: micMuted, camOn: camPref };
      e.m.muted = w.muted;
      if (w.camOn && !e.m.camOn && e.m.videoSender) setCamera(true, e);
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
    log("privacy on: every call muted, cameras off");
  }
  syncCamera();
  render();
}
// You unmuted a call or turned a camera on: the switch is off, the rest stays as it is.
function privacyRelease() {
  if (!privacy) return;
  privacy = null;
  log("privacy off");
}
// The button is the block sign alone. Like a safety switch's cover, the first click only arms it:
// "Block Audio and Video Devices" slides out and the button turns red-edged, and a second click
// blocks. Armed, a × at the end of the words folds it back at once; left alone (the pointer gone,
// or focus elsewhere) for 3 s, it folds back by itself. While blocked it's the red chip, and one click unblocks. Shift+M needs no arming.
let privacyArmed = false, privacyFold = 0;
const PRIVACY_FOLD_MS = 3000;
function privacyDisarm() {
  clearTimeout(privacyFold);
  if (!privacyArmed) return;
  privacyArmed = false;
  renderPrivacy();
}
function privacyFoldLater() {
  clearTimeout(privacyFold);
  if (privacyArmed) privacyFold = setTimeout(privacyDisarm, PRIVACY_FOLD_MS);
}
{
  const btn = $("privacyBtn");
  btn.innerHTML = `<span class="privacy-icons"></span><span class="privacy-text"><span></span></span>`;
  btn.onclick = () => {
    if (privacy || privacyArmed) { privacyDisarm(); togglePrivacy(); return; }
    privacyArmed = true;
    renderPrivacy();
  };
  const cancel = $("privacyCancel");
  cancel.innerHTML = ICON.close;
  cancel.onclick = () => { privacyDisarm(); btn.focus(); };
  // The button and its × count as one: on either, it stays out; off both, it folds later.
  const wrap = $("privacy");
  wrap.addEventListener("pointerenter", () => clearTimeout(privacyFold));
  wrap.addEventListener("pointerleave", privacyFoldLater);
  wrap.addEventListener("focusin", () => clearTimeout(privacyFold));
  wrap.addEventListener("focusout", (e) => { if (!wrap.contains(e.relatedTarget)) privacyFoldLater(); });
}
function renderPrivacy() {
  const btn = $("privacyBtn"), on = !!privacy;
  if (on) privacyArmed = false;
  btn.classList.toggle("on", on);
  btn.classList.toggle("armed", privacyArmed);
  $("privacyCancel").hidden = !privacyArmed;
  btn.setAttribute("aria-pressed", String(on));
  // Off, the block sign (its words folded away, still its name); on, the red chip with the icons.
  const icons = on ? ICON.micOff + ICON.videoOff : ICON.block;
  const box = btn.querySelector(".privacy-icons");
  if (box.innerHTML !== icons) box.innerHTML = icons;
  btn.querySelector(".privacy-text > span").textContent = on ? "Mic and Cameras Blocked" : "Block Audio and Video Devices";
  btn.title = on ? "No call hears or sees you, new calls too. Click to unblock (Shift+M)"
    : privacyArmed ? "Click again to mute every call and turn every camera off, new calls too"
    : "Block audio and video devices: click, then click again (Shift+M)";
}

// Keys for calls (not while typing, or with a menu or dialog open): 1–8 put that call in the
// list on the stage (1 is the top, the oldest; from the grid, in Focus), A answers the caller who has
// waited longest, M mutes the selected call, Shift+M is the privacy switch (every call muted, cameras off),
// H holds or resumes (M and H not in the grid), G switches Focus and Grid, and Esc leaves an
// ended call for another.
document.addEventListener("keydown", (ev) => {
  if (ev.ctrlKey || ev.altKey || ev.metaKey || openMenu) return;
  if (ev.target.closest?.("input, textarea, select, [contenteditable], dialog")) return;
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
  } else if (ev.key === "Escape" && state.call === "ended" && otherLive() && !textOpen) {
    ev.preventDefault();
    selectCall(otherLive().id);
  }
});
$("camBtn").onclick = () => {
  if (call?.hasVideo) { setCamera(!call.camOn); return; }
  if (!call && callType === "audio") {
    // On Audio, the camera means you want a video call: switch to one, camera on.
    if (!camStart()) setCamPref(true);
    log("camera: switched to a video call, camera on");
    setCallType("video");
    return;
  }
  if (!call) {
    // Before a call: whether a video call starts with your camera on.
    setCamPref(!camStart());
    render();
    return;
  }
  sel().addingVideo = true;
  privacyRelease();
  post({ type: "add-video", id: selectedId });
  log("adding video");
  render();
};

chrome.webview.addEventListener("message", async (e) => {
  const msg = e.data;
  try {
    const e = msg.id != null ? entries.get(msg.id) : null;
    if (msg.type === "state") {
      onState(msg);
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
  call: (uri, account) => placeCall(uri, account),
  // "Back to …": the call on the stage under the view, if it's still going.
  backTo: () => (isLive(sel()) && !sel().gone ? nameOf(sel()) : null),
  closeAccounts,
  contactFor,
  contactCount,
  mountContacts,
  // Add to contacts / Edit contact on a call: the Contacts tab's form, then back to Recent.
  // A new one's Call from is the account that call used.
  editContact: (e) => openContacts(contactFor(e.uri) ?? { name: e.name ?? "", uri: e.uri, account: e.account ?? null }, "recent"),
});
$("manageAccounts").addEventListener("click", () => closeHistory(false));

// ---- Settings (settings.js), from the gear in the top bar ----
const settings = initSettings({
  log,
  devices,
  // The same as picking from the ▾ menus, except a camera picked here doesn't turn the camera
  // on: if it's open it switches, otherwise it's the one the next video call opens.
  async pickDevice(key, id) {
    if (key === "mic") await pickMic(id);
    else if (key === "speaker") await pickDevice("speaker", id);
    else if (key === "camera") {
      devices.camera = id;
      saveDevices();
      // For new calls, and the selected call; other calls keep theirs.
      if (sel()?.m) sel().cameraId = id;
      log(`camera for calls: ${id || "system default"}`);
      syncCamera();   // switches if the open camera is now the wrong one
    }
    render();
  },
  diagnostics: () => !$("devPanel").hidden,
  setDiagnostics,
});

render();            // the UI straight away, before the microphone is open
await openDevices();
render();
syncCamera();
refreshCameras();
post({ type: "ready" });
