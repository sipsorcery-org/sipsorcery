// The test card: what page A sends, drawn on a canvas frame by frame, and its sound.
//
// It's made to show faults to the eye:
//  - colour bars and corner marks of different colours: swapped U and V planes (red and blue
//    trade places), a mirrored or flipped picture, a crop or a wrong stride;
//  - a grey ramp and fine checkerboards: banding, blocking and blur;
//  - a sweeping hand and a sliding block: freezes and stutter;
//  - a big frame number, and a white A/V square that flashes as the beep sounds, once a second;
//  - a barcode strip near the bottom with the frame number, read back by C# (Barcode.cs) and by
//    page B, for latency and for frames lost, repeated or too damaged to read. Its layout must
//    match Barcode.cs.
//
// Each frame is drawn on a timer and captured with requestFrame(), so every frame number is
// sent exactly once and the time it was drawn is known.

export const CELLS = 32;
export const cellSize = (width) => Math.max(8, Math.floor(width / 40));

// CRC-8, polynomial 0x07, initial 0xFF (not 0: an all-black strip, where there's no strip at
// all, would otherwise read as frame 0 with a good CRC). Barcode.cs has the same.
export function crc8(bytes) {
  let crc = 0xff;
  for (const b of bytes) {
    crc ^= b;
    for (let i = 0; i < 8; i++) crc = crc & 0x80 ? ((crc << 1) ^ 0x07) & 0xff : (crc << 1) & 0xff;
  }
  return crc;
}

/**
 * Reads the frame number from RGBA pixels of the barcode strip (getImageData of the strip,
 * CELLS * cell wide and cell high, starting at the strip's top left). -1 if unreadable.
 */
export function readStrip(data, cell) {
  const width = CELLS * cell;
  return readCells((x, y) => { const p = (y * width + x) * 4; return (77 * data[p] + 150 * data[p + 1] + 29 * data[p + 2]) >> 8; }, cell);
}

/**
 * Reads the frame number from a decoded VideoFrame's strip, straight from its luma plane (or
 * from RGB if that's what it holds). -1 if unreadable. Its rectangle is on even pixels, as
 * copyTo needs for subsampled formats.
 */
export async function readFrame(frame) {
  const w = frame.displayWidth, h = frame.displayHeight;
  const r = stripRect(w, h);
  const rect = { x: r.x, y: r.y, width: r.w, height: r.h };
  const buf = new Uint8Array(frame.allocationSize({ rect }));
  const layout = await frame.copyTo(buf, { rect });
  const fmt = frame.format ?? "";
  const stride = layout[0].stride, offset = layout[0].offset;
  if (fmt.startsWith("RGB") || fmt.startsWith("BGR")) {
    const [ri, bi] = fmt.startsWith("RGB") ? [0, 2] : [2, 0];
    return readCells((x, y) => { const p = offset + y * stride + x * 4; return (77 * buf[p + ri] + 150 * buf[p + 1] + 29 * buf[p + bi]) >> 8; }, r.cell);
  }
  return readCells((x, y) => buf[offset + y * stride + x], r.cell);   // I420, NV12...: Y plane first
}

function readCells(luma, cell) {
  const half = Math.max(1, Math.floor(cell / 4));
  let bits = 0;
  for (let c = 0; c < CELLS; c++) {
    const cx = c * cell + Math.floor(cell / 2), cy = Math.floor(cell / 2);
    let sum = 0, n = 0;
    for (let y = cy - half; y < cy + half; y++) {
      for (let x = cx - half; x < cx + half; x++) {
        sum += luma(x, y);
        n++;
      }
    }
    bits = bits * 2 + (sum / n >= 128 ? 1 : 0);
  }
  const number = Math.floor(bits / 256);
  const check = bits % 256;
  return crc8([(number >> 16) & 0xff, (number >> 8) & 0xff, number & 0xff]) === check ? number : -1;
}

/** Where the strip is in a frame of this size. */
export function stripRect(width, height) {
  const cell = cellSize(width);
  return { x: Math.floor((width - CELLS * cell) / 2), y: height - 2 * cell, cell, w: CELLS * cell, h: cell };
}

const BARS = ["#c0c0c0", "#c0c000", "#00c0c0", "#00c000", "#c000c0", "#c00000", "#0000c0"];

export function startCard(width, height, fps) {
  const canvas = document.createElement("canvas");
  canvas.width = width;
  canvas.height = height;
  const g = canvas.getContext("2d", { alpha: false });
  const stream = canvas.captureStream(0);
  const track = stream.getVideoTracks()[0];
  const drawn = new Map();   // frame number -> performance.now() when it was captured

  // Sound: a quiet steady tone (so it's plain when audio stops) and a beep on each A/V flash.
  const audio = new AudioContext({ sampleRate: 48000 });
  const dest = audio.createMediaStreamDestination();
  const hum = audio.createOscillator();
  const humGain = audio.createGain();
  hum.frequency.value = 220;
  humGain.gain.value = 0.04;
  hum.connect(humGain).connect(dest);
  const beep = audio.createOscillator();
  const beepGain = audio.createGain();
  beep.frequency.value = 880;
  beepGain.gain.value = 0;
  beep.connect(beepGain).connect(dest);
  hum.start();
  beep.start();
  stream.addTrack(dest.stream.getAudioTracks()[0]);

  const start = performance.now();
  let n = 0, timer = 0, stopped = false;

  function tick() {
    if (stopped) return;
    draw(g, width, height, fps, n, start);
    track.requestFrame();
    drawn.set(n, performance.now());
    if (drawn.size > 900) drawn.delete(n - 900);
    if (n % fps === 0) {
      const t = audio.currentTime;
      beepGain.gain.setValueAtTime(0.35, t);
      beepGain.gain.setValueAtTime(0, t + 0.1);
    }
    n++;
    const next = start + (n * 1000) / fps;
    timer = setTimeout(tick, Math.max(0, next - performance.now()));
  }
  tick();

  return {
    stream,
    drawnAt: (number) => drawn.get(number),
    get frames() { return n; },
    stop() {
      stopped = true;
      clearTimeout(timer);
      stream.getTracks().forEach((t) => t.stop());
      audio.close();
    },
  };
}

function draw(g, w, h, fps, n, start) {
  const strip = stripRect(w, h);
  const top = strip.y - strip.cell;          // nothing but the strip below here
  g.fillStyle = "#1c1c1c";
  g.fillRect(0, 0, w, h);

  // Colour bars, then a grey ramp.
  const barsH = Math.round(h * 0.45);
  const barW = w / BARS.length;
  BARS.forEach((c, i) => {
    g.fillStyle = c;
    g.fillRect(Math.round(i * barW), 0, Math.round((i + 1) * barW) - Math.round(i * barW), barsH);
  });
  const rampY = barsH, rampH = Math.round(h * 0.06);
  const ramp = g.createLinearGradient(0, 0, w, 0);
  ramp.addColorStop(0, "#000");
  ramp.addColorStop(1, "#fff");
  g.fillStyle = ramp;
  g.fillRect(0, rampY, w, rampH);

  // Below the ramp: checkerboards on the left, the numbers in the middle, the sweep on the right.
  const areaY = rampY + rampH + Math.round(h * 0.02);
  const areaH = top - areaY - Math.round(h * 0.07);
  const unit = Math.max(1, Math.round(w / 640));
  const checkW = Math.round(w * 0.2);
  [1, 2, 4].forEach((size, row) => {
    const s = size * unit;
    const y0 = areaY + Math.round((row * areaH) / 3);
    const rowH = Math.round(areaH / 3) - 2 * unit;
    for (let y = 0; y < rowH; y += s) {
      for (let x = 0; x < checkW; x += s) {
        g.fillStyle = ((x / s + y / s) & 1) ? "#fff" : "#000";
        g.fillRect(Math.round(w * 0.03) + x, y0 + y, s, Math.min(s, rowH - y));
      }
    }
  });

  const secs = (n / fps).toFixed(2);
  g.fillStyle = "#fff";
  g.textAlign = "center";
  g.textBaseline = "middle";
  g.font = `bold ${Math.round(areaH * 0.36)}px Consolas, monospace`;
  g.fillText(`#${String(n).padStart(6, "0")}`, w * 0.5, areaY + areaH * 0.32);
  g.font = `${Math.round(areaH * 0.16)}px Consolas, monospace`;
  g.fillStyle = "#bbb";
  g.fillText(`${secs} s · ${w}×${h} · ${fps} fps`, w * 0.5, areaY + areaH * 0.68);

  // The A/V square, white for the first two frames of each second, as the beep sounds.
  const av = Math.round(areaH * 0.28);
  const flash = n % fps < 2;
  g.fillStyle = flash ? "#fff" : "#333";
  g.fillRect(Math.round(w * 0.5 - av / 2), areaY + areaH - av - Math.round(h * 0.01), av, av);
  g.fillStyle = flash ? "#000" : "#888";
  g.font = `bold ${Math.round(av * 0.4)}px Consolas, monospace`;
  g.fillText("A/V", w * 0.5, areaY + areaH - av / 2 - Math.round(h * 0.01));

  // The sweep: one turn every 2 s.
  const r = Math.min(areaH / 2, w * 0.11);
  const cx = w * 0.85, cy = areaY + areaH / 2;
  g.strokeStyle = "#666";
  g.lineWidth = Math.max(1, unit * 2);
  g.beginPath();
  g.arc(cx, cy, r, 0, Math.PI * 2);
  g.stroke();
  const angle = ((n % (2 * fps)) / (2 * fps)) * Math.PI * 2 - Math.PI / 2;
  g.strokeStyle = "#ffd33d";
  g.lineWidth = Math.max(2, unit * 4);
  g.beginPath();
  g.moveTo(cx, cy);
  g.lineTo(cx + r * Math.cos(angle), cy + r * Math.sin(angle));
  g.stroke();
  for (let i = 0; i < 12; i++) {
    const a = (i / 12) * Math.PI * 2;
    g.fillStyle = "#999";
    g.fillRect(cx + (r - 6 * unit) * Math.cos(a) - unit, cy + (r - 6 * unit) * Math.sin(a) - unit, 2 * unit, 2 * unit);
  }

  // The sliding block: across the frame every 2 s, one step per frame.
  const trackY = top - Math.round(h * 0.055);
  const blockW = Math.round(w / 16), blockH = Math.round(h * 0.035);
  g.fillStyle = "#333";
  g.fillRect(0, trackY, w, blockH);
  const bx = Math.round(((n % (2 * fps)) / (2 * fps)) * (w - blockW));
  g.fillStyle = "#58a6ff";
  g.fillRect(bx, trackY, blockW, blockH);

  // Corner marks, each its own colour, and a 1 px border.
  const m = Math.round(w * 0.04);
  const corner = (x, y, dx, dy, colour) => {
    g.fillStyle = colour;
    g.beginPath();
    g.moveTo(x, y);
    g.lineTo(x + dx * m, y);
    g.lineTo(x, y + dy * m);
    g.closePath();
    g.fill();
  };
  corner(0, 0, 1, 1, "#ff0000");
  corner(w, 0, -1, 1, "#00ff00");
  corner(0, h, 1, -1, "#0000ff");
  corner(w, h, -1, -1, "#ffff00");
  g.strokeStyle = "#fff";
  g.lineWidth = 1;
  g.strokeRect(0.5, 0.5, w - 1, h - 1);

  // The barcode: 24 bits of frame number, then its CRC-8, on black.
  g.fillStyle = "#000";
  g.fillRect(strip.x - strip.cell / 2, strip.y - strip.cell / 2, strip.w + strip.cell, strip.h + strip.cell);
  const bytes = [(n >> 16) & 0xff, (n >> 8) & 0xff, n & 0xff];
  const bits = (n & 0xffffff) * 256 + crc8(bytes);
  g.fillStyle = "#fff";
  for (let c = 0; c < CELLS; c++) {
    if (Math.floor(bits / 2 ** (CELLS - 1 - c)) % 2) g.fillRect(strip.x + c * strip.cell, strip.y, strip.cell, strip.cell);
  }
}
