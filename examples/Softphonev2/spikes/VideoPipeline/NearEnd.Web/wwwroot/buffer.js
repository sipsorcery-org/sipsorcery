// Path 2: C# writes decoded NV12 frames into one WebView2 shared buffer per tile. On every
// animation frame this page checks each buffer's sequence number and, if a new frame is there,
// uploads its Y and UV planes to WebGL textures, then draws every tile on one canvas.
//
// Buffer layout (little endian): [0] int32 seq, [4] int32 latest slot, [8] int32 width,
// [12] int32 height, [64 + slot*16] float64 C# receive time of the frame in that slot,
// [header + slot*slotBytes] NV12 (Y plane then interleaved UV, stride = width).

import { tileCount, on, post, log, gridLayout, Presenter, hello } from "./common.js";

const REFRESH_MS = 1000 / 60;   // one display refresh, added as the compositor's share of latency
const canvas = document.getElementById("c");
const gl = canvas.getContext("webgl2", { antialias: false, alpha: false, desynchronized: false, powerPreference: "high-performance" });
if (!gl) log("no WebGL2");

const tiles = [];
let measuring = false;

const vs = `#version 300 es
in vec2 p; out vec2 uv;
void main() { uv = vec2(p.x * 0.5 + 0.5, 0.5 - p.y * 0.5); gl_Position = vec4(p, 0.0, 1.0); }`;
const fs = `#version 300 es
precision mediump float;
in vec2 uv; out vec4 color;
uniform sampler2D ty, tuv; uniform vec2 scale; uniform float mirror;
void main() {
  vec2 c = (uv - 0.5) / scale + 0.5;          // object-fit: cover
  if (mirror > 0.5) c.x = 1.0 - c.x;
  float y = texture(ty, c).r;
  vec2 cbcr = texture(tuv, c).rg - 0.5;
  y = 1.1643 * (y - 0.0625);                  // BT.601 limited range, as decoders output by default
  color = vec4(y + 1.5958 * cbcr.y, y - 0.39173 * cbcr.x - 0.8129 * cbcr.y, y + 2.017 * cbcr.x, 1.0);
}`;

function shader(type, src) {
  const s = gl.createShader(type);
  gl.shaderSource(s, src);
  gl.compileShader(s);
  if (!gl.getShaderParameter(s, gl.COMPILE_STATUS)) log(gl.getShaderInfoLog(s));
  return s;
}
const prog = gl.createProgram();
gl.attachShader(prog, shader(gl.VERTEX_SHADER, vs));
gl.attachShader(prog, shader(gl.FRAGMENT_SHADER, fs));
gl.linkProgram(prog);
gl.useProgram(prog);
const buf = gl.createBuffer();
gl.bindBuffer(gl.ARRAY_BUFFER, buf);
gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([-1, -1, 1, -1, -1, 1, 1, 1]), gl.STATIC_DRAW);
const loc = gl.getAttribLocation(prog, "p");
gl.enableVertexAttribArray(loc);
gl.vertexAttribPointer(loc, 2, gl.FLOAT, false, 0, 0);
gl.uniform1i(gl.getUniformLocation(prog, "ty"), 0);
gl.uniform1i(gl.getUniformLocation(prog, "tuv"), 1);
const uScale = gl.getUniformLocation(prog, "scale");
const uMirror = gl.getUniformLocation(prog, "mirror");
gl.pixelStorei(gl.UNPACK_ALIGNMENT, 1);

function texture() {
  const t = gl.createTexture();
  gl.bindTexture(gl.TEXTURE_2D, t);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
  return t;
}

chrome.webview.addEventListener("sharedbufferreceived", (e) => {
  const meta = e.additionalData;
  const buffer = e.getBuffer();
  tiles[meta.tile] = {
    ...meta, buffer, view: new DataView(buffer), seq: 0, w: 0, h: 0,
    ty: texture(), tuv: texture(), presenter: new Presenter(meta.tile, meta.kind), latency: [],
  };
});

function upload(t, slot, w, h) {
  const base = t.header + slot * t.slotBytes;
  if (w !== t.w || h !== t.h) {
    gl.bindTexture(gl.TEXTURE_2D, t.ty);
    gl.texStorage2D(gl.TEXTURE_2D, 1, gl.R8, w, h);
    gl.bindTexture(gl.TEXTURE_2D, t.tuv);
    gl.texStorage2D(gl.TEXTURE_2D, 1, gl.RG8, w / 2, h / 2);
    t.w = w;
    t.h = h;
  }
  gl.bindTexture(gl.TEXTURE_2D, t.ty);
  gl.texSubImage2D(gl.TEXTURE_2D, 0, 0, 0, w, h, gl.RED, gl.UNSIGNED_BYTE, new Uint8Array(t.buffer, base, w * h));
  gl.bindTexture(gl.TEXTURE_2D, t.tuv);
  gl.texSubImage2D(gl.TEXTURE_2D, 0, 0, 0, w / 2, h / 2, gl.RG, gl.UNSIGNED_BYTE, new Uint8Array(t.buffer, base + w * h, (w * h) / 2));
}

function frame(now) {
  const dpr = devicePixelRatio;
  const W = Math.round(innerWidth * dpr), H = Math.round(innerHeight * dpr);
  if (canvas.width !== W || canvas.height !== H) { canvas.width = W; canvas.height = H; }
  gl.viewport(0, 0, W, H);
  gl.clearColor(0.07, 0.07, 0.07, 1);
  gl.clear(gl.COLOR_BUFFER_BIT);

  const rafEpoch = performance.timeOrigin + now;
  const rects = gridLayout(tileCount, W, H, 4 * dpr);
  for (const t of tiles) {
    if (!t) continue;
    const seq = t.view.getInt32(0, true);
    if (seq !== t.seq) {
      const slot = t.view.getInt32(4, true);
      const w = t.view.getInt32(8, true), h = t.view.getInt32(12, true);
      const recv = t.view.getFloat64(64 + slot * 16, true);
      upload(t, slot, w, h);
      if (measuring || !t.presenter.first) {
        if (measuring && t.seq) t.presenter.skipped += Math.max(0, seq - t.seq - 1);
        t.presenter.shown(rafEpoch + REFRESH_MS);
        if (measuring) t.latency.push(rafEpoch + REFRESH_MS - recv);   // C# adds back the clock offset
      }
      t.seq = seq;
    }
    if (!t.w) continue;

    const r = rects[t.tile];
    gl.viewport(Math.round(r.x), Math.round(H - r.y - r.h), Math.round(r.w), Math.round(r.h));
    const tileAspect = r.w / r.h, frameAspect = t.w / t.h;
    gl.uniform2f(uScale, Math.max(1, frameAspect / tileAspect), Math.max(1, tileAspect / frameAspect));
    gl.uniform1f(uMirror, t.kind === "self" ? 1 : 0);
    gl.activeTexture(gl.TEXTURE0);
    gl.bindTexture(gl.TEXTURE_2D, t.ty);
    gl.activeTexture(gl.TEXTURE1);
    gl.bindTexture(gl.TEXTURE_2D, t.tuv);
    gl.drawArrays(gl.TRIANGLE_STRIP, 0, 4);
  }
  requestAnimationFrame(frame);
}
requestAnimationFrame(frame);

on("start", () => {
  tiles.forEach((t) => { if (t) { t.presenter.reset(); t.latency = []; } });
  measuring = true;
});

on("stop", () => {
  measuring = false;
  post({
    type: "stats",
    tiles: tiles.filter(Boolean).map((t) => ({ ...t.presenter.summary(), latency: t.latency, width: t.w, height: t.h })),
  });
});

hello();
