// Audio levels: the frequency bars of the audio-only view and the grid's tiles.

import { NO_AUDIO_MS, call, entries, holdState, inGrid, pc, render, stageMode, tiles } from "./app.js";
import { getsMic } from "./calllist.js";
import { $, log } from "./util.js";

// ---- Audio levels: frequency bars ----

let audioCtx = null;

export function analyserFor(track) {
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

export const BANDS = 28;
export const FALL_MS = 260;
// Bars as drawn, and where they're heading, per canvas: the stage's, and each grid tile's (e.bars).
const newBars = () => ({ them: new Float32Array(BANDS), me: new Float32Array(BANDS),
  target: { them: new Float32Array(BANDS), me: new Float32Array(BANDS) } });
let stageBars;
const reducedMotion = matchMedia("(prefers-reduced-motion: reduce)");
let lastDraw = 0;

// Bands spaced logarithmically from ~100 Hz to ~6 kHz, where a voice's energy is.
export function bands(analyser, out) {
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

export function drawViz(t) {
  if ($("stage").dataset.mode !== "audio" || !call) return;
  requestAnimationFrame(drawViz);
  const dt = Math.min(250, t - (lastDraw || t));
  lastDraw = t;
  drawBars($("viz"), stageBars, { them: call.remoteAnalyser, me: call.localAnalyser, rxDead: call.rxDead, txLost: call.txLost }, dt);
}

// The grid's audio tiles, while the grid's up. A tile shows your bars only while that call gets
// your microphone, so a held call's tile shows them talking (or their music) and you silent.
let tilesDrawing = false, lastTileDraw = 0;
export function startTiles() {
  if (tilesDrawing) return;
  tilesDrawing = true;
  requestAnimationFrame(drawTiles);
}
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

// Run by app.js at this point in its start-up, in the order it always ran.
export function initLevels() {
  stageBars = newBars();

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
}
