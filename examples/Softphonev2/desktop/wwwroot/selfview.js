// The self view's place and size on the stage: drag it to move it, drag its corner handle to
// resize it, double-click it to put it back. With it focused, the arrow keys move it, Shift+arrows
// resize it and Home puts it back. It stays inside the stage, also when the stage resizes.
//
// Kept in localStorage ("selfViewBox") as where it sits between the stage's edges (x, y from 0 at
// the left/top to 1 at the right/bottom) and its size as a multiple of the default (1 to 3), so it
// keeps its corner or its place across window sizes. The default is the bottom-right corner.
//
// The box (#selfBox) holds #self and #selfOff; app.js shows and hides those as before.

const KEY = "selfViewBox";
const MARGIN = 12;          // from the stage's edges, as the default position
const MAX_SCALE = 3;        // the largest, as a multiple of the default size
const RATIO = 16 / 9;
const DEFAULT = { x: 1, y: 1, s: 1 };

export function initSelfView(stage, box, grip) {
  let pos = { ...DEFAULT };
  try {
    const saved = JSON.parse(localStorage.getItem(KEY) ?? "null");
    if (saved) pos = { x: clamp(+saved.x, 0, 1), y: clamp(+saved.y, 0, 1), s: clamp(+saved.s, 1, MAX_SCALE) };
    if (![pos.x, pos.y, pos.s].every(Number.isFinite)) pos = { ...DEFAULT };
  } catch { /* storage unavailable */ }
  const save = () => {
    try {
      if (pos.x === 1 && pos.y === 1 && pos.s === 1) localStorage.removeItem(KEY);
      else localStorage.setItem(KEY, JSON.stringify(pos));
    } catch { /* private window: this visit only */ }
  };

  // The default width is the stage's --side-w (shared with Call quality), at most 45% of the stage.
  function metrics() {
    const W = stage.clientWidth, H = stage.clientHeight;
    const side = parseFloat(getComputedStyle(stage).getPropertyValue("--side-w")) || 260;
    const d = Math.min(side, W * 0.45);
    const fit = Math.max(0, Math.min(W - 2 * MARGIN, (H - 2 * MARGIN) * RATIO));
    return { W, H, d, fit };
  }
  // Where it is now, in stage pixels.
  function rect(m = metrics()) {
    const w = Math.min(m.d * pos.s, m.fit), h = w / RATIO;
    return { w, h, left: MARGIN + pos.x * (m.W - 2 * MARGIN - w), top: MARGIN + pos.y * (m.H - 2 * MARGIN - h) };
  }
  // Takes a place and size in pixels, kept inside the stage, as the new position.
  function place(left, top, w, m = metrics()) {
    const h = w / RATIO;
    const spanX = m.W - 2 * MARGIN - w, spanY = m.H - 2 * MARGIN - h;
    pos = {
      x: spanX > 0 ? clamp((left - MARGIN) / spanX, 0, 1) : pos.x,
      y: spanY > 0 ? clamp((top - MARGIN) / spanY, 0, 1) : pos.y,
      s: w >= m.fit && m.fit < m.d * pos.s ? pos.s : clamp(w / m.d, 1, MAX_SCALE),
    };
    layout(m);
  }

  // The handle sits on the corner facing the middle of the stage, so it's never in a stage corner.
  let gripCorner = "tl";
  function layout(m = metrics()) {
    if (!m.W || !m.H) return;   // not shown (accounts open, or not laid out yet)
    const r = rect(m);
    Object.assign(box.style, { left: `${r.left}px`, top: `${r.top}px`, right: "auto", bottom: "auto", width: `${r.w}px`, maxWidth: "none" });
    if (drag?.mode !== "resize") {   // the handle stays put while it's held
      gripCorner = (r.top + r.h / 2 < m.H / 2 ? "b" : "t") + (r.left + r.w / 2 < m.W / 2 ? "r" : "l");
      box.dataset.grip = gripCorner;
    }
  }

  // Resizes to width w, keeping the corner opposite the handle where it is.
  function resizeTo(w, start, m) {
    const anchorX = gripCorner.includes("l") ? start.left + start.w : start.left;
    const anchorY = gripCorner.includes("t") ? start.top + start.h : start.top;
    const roomX = gripCorner.includes("l") ? anchorX - MARGIN : m.W - MARGIN - anchorX;
    const roomY = (gripCorner.includes("t") ? anchorY - MARGIN : m.H - MARGIN - anchorY) * RATIO;
    const lo = Math.min(m.d, m.fit);
    w = clamp(w, lo, Math.max(lo, Math.min(m.d * MAX_SCALE, m.fit, roomX, roomY)));
    const h = w / RATIO;
    place(gripCorner.includes("l") ? anchorX - w : anchorX, gripCorner.includes("t") ? anchorY - h : anchorY, w, m);
  }

  // Dragging: the box moves it, the handle resizes it. Overlays and controls sit above the box,
  // so a drag can't start from them.
  let drag = null;
  box.addEventListener("pointerdown", (e) => {
    if (!e.isPrimary || e.button !== 0) return;
    const m = metrics();
    drag = { id: e.pointerId, mode: e.target === grip ? "resize" : "move", x0: e.clientX, y0: e.clientY, start: rect(m), m, moved: false };
    box.setPointerCapture(e.pointerId);
    box.classList.add(drag.mode === "move" ? "dragging" : "resizing");
    e.preventDefault();
  });
  box.addEventListener("pointermove", (e) => {
    if (!drag || e.pointerId !== drag.id) return;
    const dx = e.clientX - drag.x0, dy = e.clientY - drag.y0, { start, m } = drag;
    if (!drag.moved && Math.hypot(dx, dy) < 3) return;
    drag.moved = true;
    if (drag.mode === "move") {
      place(clamp(start.left + dx, MARGIN, m.W - MARGIN - start.w), clamp(start.top + dy, MARGIN, m.H - MARGIN - start.h), start.w, m);
    } else {
      const sx = gripCorner.includes("l") ? -1 : 1, sy = gripCorner.includes("t") ? -1 : 1;
      resizeTo(start.w + Math.max(sx * dx, sy * dy * RATIO), start, m);
    }
  });
  const end = (e) => {
    if (!drag || e.pointerId !== drag.id) return;
    const moved = drag.moved;
    box.classList.remove("dragging", "resizing");
    drag = null;
    layout();
    if (moved) save();
  };
  box.addEventListener("pointerup", end);
  box.addEventListener("pointercancel", end);

  function reset() {
    pos = { ...DEFAULT };
    layout();
    save();
  }
  box.addEventListener("dblclick", reset);

  box.addEventListener("keydown", (e) => {
    if (e.ctrlKey || e.altKey || e.metaKey) return;
    const dir = { ArrowLeft: [-1, 0], ArrowRight: [1, 0], ArrowUp: [0, -1], ArrowDown: [0, 1] }[e.key];
    if (e.key === "Home") { e.preventDefault(); reset(); return; }
    if (!dir) return;
    e.preventDefault();
    const m = metrics(), r = rect(m);
    if (e.shiftKey) {
      // Up or Right makes it larger, Down or Left smaller.
      const grow = dir[0] > 0 || dir[1] < 0 ? 1 : -1;
      resizeTo(r.w + grow * m.d * 0.1, r, m);
    } else {
      const step = 16;
      place(clamp(r.left + dir[0] * step, MARGIN, m.W - MARGIN - r.w), clamp(r.top + dir[1] * step, MARGIN, m.H - MARGIN - r.h), r.w, m);
    }
    save();
  });

  new ResizeObserver(() => { if (!drag) layout(); }).observe(stage);
  layout();
  return { reset };
}

const clamp = (v, lo, hi) => Math.min(hi, Math.max(lo, v));
