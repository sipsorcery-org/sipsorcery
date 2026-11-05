// Shared by both WebView2 pages: query options, messaging with the C# host, clock calibration,
// grid layout and per-tile presentation tracking.

export const q = Object.fromEntries(new URLSearchParams(location.search));
export const streams = +q.streams;
export const cameraOn = q.camera !== "off";
export const tileCount = streams + (cameraOn ? 1 : 0);
export const epoch = (perfTime) => performance.timeOrigin + perfTime;

const handlers = {};
export const on = (type, fn) => (handlers[type] = fn);
export const post = (msg) => chrome.webview.postMessage(msg);
export const log = (text) => post({ type: "log", text: String(text) });

chrome.webview.addEventListener("message", async (e) => {
  const msg = e.data;
  if (msg.type === "ping") {
    post({ type: "pong", t0: msg.t0, tp: epoch(performance.now()) });
    return;
  }
  const fn = handlers[msg.type];
  if (fn) {
    try { await fn(msg); } catch (err) { log(`${msg.type} failed: ${err?.stack || err}`); }
  }
});
window.addEventListener("error", (e) => log(`error: ${e.message}`));
window.addEventListener("unhandledrejection", (e) => log(`rejection: ${e.reason?.stack || e.reason}`));

/** Tile rectangles for an even grid, in CSS pixels, the same layout the WPF window uses. */
export function gridLayout(count, width, height, gap = 4) {
  const cols = Math.ceil(Math.sqrt(count));
  const rows = Math.ceil(count / cols);
  const w = (width - gap * (cols + 1)) / cols;
  const h = (height - gap * (rows + 1)) / rows;
  return Array.from({ length: count }, (_, i) => ({
    x: gap + (i % cols) * (w + gap),
    y: gap + Math.floor(i / cols) * (h + gap),
    w, h,
  }));
}

/** Counts shown frames for one tile, plus gaps of more than 100 ms between them. */
export class Presenter {
  constructor(tile, kind) {
    this.tile = tile;
    this.kind = kind;
    this.first = false;
    this.reset();
  }
  reset() {
    this.presented = 0;
    this.skipped = 0;
    this.stalls = 0;
    this.longestGap = 0;
    this.last = NaN;
    this.samples = [];
  }
  shown(atEpoch, frames = 1) {
    if (!this.first) {
      this.first = true;
      post({ type: "firstFrame", tile: this.tile });
    }
    if (!isNaN(this.last)) {
      const gap = atEpoch - this.last;
      if (gap > 100) this.stalls++;
      if (gap > this.longestGap) this.longestGap = gap;
    }
    this.last = atEpoch;
    this.presented += frames;
  }
  summary() {
    return {
      tile: this.tile, kind: this.kind, presented: this.presented, skipped: this.skipped,
      stalls: this.stalls, longestGap: this.longestGap,
    };
  }
}

/** Tells the host the page is set up and ready for its messages. */
export const hello = () => post({ type: "hello" });
