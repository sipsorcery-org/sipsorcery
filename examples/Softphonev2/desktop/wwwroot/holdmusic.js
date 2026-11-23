// Music on hold, and silence: what a call that isn't the live one sends instead of your microphone.

import { holdState } from "./app.js";
import { log } from "./util.js";

// ---- Music on hold, and silence ----
// A call that isn't the live one sends music (held, music on hold) or silence (held the SIP way,
// they've held us, or briefly while a call is switching): never nothing, so the stream keeps
// flowing, the far end's media timers stay happy and our NAT binding stays open. Silence on a call
// they've held the SIP way is more than SIP asks for: our side is recvonly, so the host sends it
// past the RTP session (MediaRelay.SendPastTheirHold).
export const quietAudio = (e) => (holdState(e) && e.h.holdMode !== "sip" ? holdMusicTrack() : silenceTrack());

// The music is a loop rendered once (a few chords, picked out gently), then played round and
// round: no timers, so it doesn't stutter when the window is minimised and timers are throttled.
export let holdAudio = null;   // { ctx, music, silence, buffer, source }
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

export function stopHoldMusic() {
  holdAudio.wanted = false;
  holdAudio.source?.stop();
  holdAudio.source = null;
}

const silenceTrack = () => holdAudioContext().silence.stream.getAudioTracks()[0];
