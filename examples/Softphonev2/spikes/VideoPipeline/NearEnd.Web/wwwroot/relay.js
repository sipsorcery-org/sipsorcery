// Path 1: one local RTCPeerConnection per call. The browser engine decodes and renders each
// remote stream in a <video> element and encodes the camera; requestVideoFrameCallback reports
// each frame that reaches the screen with its RTP timestamp, so C# can measure the whole trip.

import { q, streams, cameraOn, tileCount, epoch, on, post, log, gridLayout, Presenter, hello } from "./common.js";

const tiles = [];
const pcs = [];
let camStream = null;
let measuring = false;
let statsAtStart = null;

function layout() {
  const rects = gridLayout(tileCount, innerWidth, innerHeight);
  tiles.forEach((t, i) => Object.assign(t.el.style, {
    left: `${rects[i].x}px`, top: `${rects[i].y}px`, width: `${rects[i].w}px`, height: `${rects[i].h}px`,
  }));
}

function makeTile(i, kind) {
  const el = document.createElement("div");
  el.className = `tile ${kind}`;
  const video = document.createElement("video");
  video.autoplay = true;
  video.muted = true;
  video.playsInline = true;
  const label = document.createElement("div");
  label.className = "label";
  label.textContent = kind === "self" ? "You" : `Call ${i + 1}`;
  el.append(video, label);
  document.body.append(el);
  const tile = { el, video, presenter: new Presenter(i, kind), lastPresented: 0 };
  tiles[i] = tile;
  watch(tile);
  return tile;
}

function watch(tile) {
  const cb = (now, md) => {
    const shownAt = epoch(md.expectedDisplayTime);
    const frames = tile.lastPresented ? md.presentedFrames - tile.lastPresented : 1;
    tile.lastPresented = md.presentedFrames;
    if (measuring) {
      tile.presenter.shown(shownAt, frames);
      if (tile.presenter.kind === "remote" && md.rtpTimestamp !== undefined && md.receiveTime !== undefined) {
        tile.presenter.samples.push([md.rtpTimestamp, epoch(md.receiveTime), shownAt]);
      }
    } else if (!tile.presenter.first) {
      tile.presenter.shown(shownAt);
    }
    tile.video.requestVideoFrameCallback(cb);
  };
  tile.video.requestVideoFrameCallback(cb);
}

for (let i = 0; i < streams; i++) makeTile(i, "remote");

if (cameraOn) {
  const self = makeTile(streams, "self");
  camStream = await navigator.mediaDevices.getUserMedia({
    video: { width: { ideal: 1280 }, height: { ideal: 720 }, frameRate: { ideal: 30 } }, audio: false,
  });
  self.video.srcObject = camStream;
}
layout();
addEventListener("resize", layout);

const sendsCamera = (i) => cameraOn && (q.mode === "percall" || i === 0);

on("offer", async ({ tile, sdp }) => {
  const pc = new RTCPeerConnection();
  pcs[tile] = pc;
  pc.ontrack = (e) => {
    const v = tiles[tile].video;
    v.srcObject = new MediaStream([e.track]);
    // Same playout as a browser call: leave the jitter buffer at its default.
  };
  await pc.setRemoteDescription({ type: "offer", sdp });
  const tr = pc.getTransceivers()[0];
  if (sendsCamera(tile)) {
    await tr.sender.replaceTrack(camStream.getVideoTracks()[0]);
    tr.direction = "sendrecv";
  } else {
    tr.direction = "recvonly";
  }
  await pc.setLocalDescription(await pc.createAnswer());
  await new Promise((resolve) => {
    if (pc.iceGatheringState === "complete") return resolve();
    pc.addEventListener("icegatheringstatechange", () => pc.iceGatheringState === "complete" && resolve());
    setTimeout(resolve, 2000);
  });
  post({ type: `answer-${tile}`, sdp: pc.localDescription.sdp });

  if (sendsCamera(tile)) {
    pc.addEventListener("connectionstatechange", async () => {
      if (pc.connectionState !== "connected") return;
      const params = tr.sender.getParameters();
      params.encodings = params.encodings?.length ? params.encodings : [{}];
      params.encodings[0].maxBitrate = +q.kbps * 1000;
      await tr.sender.setParameters(params);
    });
  }
});

async function snapshot() {
  const out = [];
  for (let i = 0; i < pcs.length; i++) {
    const report = await pcs[i].getStats();
    const s = { inbound: null, outbound: null, source: null };
    report.forEach((r) => {
      if (r.kind !== "video" && r.type !== "media-source") return;
      if (r.type === "inbound-rtp") s.inbound = r;
      if (r.type === "outbound-rtp") s.outbound = r;
      if (r.type === "media-source" && r.kind === "video") s.source = r;
    });
    out.push(s);
  }
  return out;
}

on("start", async () => {
  tiles.forEach((t) => t.presenter.reset());
  statsAtStart = await snapshot();
  measuring = true;
});

on("stop", async () => {
  measuring = false;
  const end = await snapshot();
  const d = (a, b, k) => (b?.[k] ?? 0) - (a?.[k] ?? 0);

  const result = tiles.map((t, i) => {
    const sum = t.presenter.summary();
    sum.samples = t.presenter.samples;
    if (t.presenter.kind === "remote") {
      const a = statsAtStart[i].inbound, b = end[i].inbound;
      const decoded = d(a, b, "framesDecoded");
      const emitted = d(a, b, "jitterBufferEmittedCount");
      Object.assign(sum, {
        framesDecoded: decoded,
        decodeMs: decoded ? (d(a, b, "totalDecodeTime") * 1000) / decoded : null,
        jitterBufferMs: emitted ? (d(a, b, "jitterBufferDelay") * 1000) / emitted : null,
        decoder: b?.decoderImplementation ?? null,
        powerEfficient: b?.powerEfficientDecoder ?? null,
        width: b?.frameWidth ?? 0,
        height: b?.frameHeight ?? 0,
      });
    } else {
      const s = camStream.getVideoTracks()[0].getSettings();
      Object.assign(sum, { width: s.width, height: s.height });
    }
    return sum;
  });

  let camera = null;
  if (cameraOn) {
    const senders = pcs.map((_, i) => i).filter(sendsCamera);
    let encoded = 0, encodeTime = 0, bytes = 0, impl = null, power = null, limit = null, w = 0, h = 0, captured = 0;
    for (const i of senders) {
      const a = statsAtStart[i].outbound, b = end[i].outbound;
      encoded += d(a, b, "framesEncoded");
      encodeTime += d(a, b, "totalEncodeTime");
      bytes += d(a, b, "bytesSent");
      impl = b?.encoderImplementation ?? impl;
      power = b?.powerEfficientEncoder ?? power;
      limit = b?.qualityLimitationReason ?? limit;
      w = b?.frameWidth ?? w;
      h = b?.frameHeight ?? h;
    }
    const src = end[senders[0]]?.source, src0 = statsAtStart[senders[0]]?.source;
    captured = d(src0, src, "frames");
    const secs = (performance.now() - (statsAtStart[senders[0]]?.outbound?.timestamp ?? performance.now())) / 1000;
    camera = {
      label: camStream.getVideoTracks()[0].label,
      encoders: senders.length,
      framesCaptured: captured,
      framesEncoded: encoded,
      encodeMs: encoded ? (encodeTime * 1000) / encoded : null,
      kbpsPerEncoder: secs > 0 && senders.length ? (bytes * 8) / 1000 / secs / senders.length : null,
      encoder: impl, powerEfficient: power, qualityLimitation: limit, width: w, height: h,
    };
  }
  post({ type: "stats", tiles: result, camera });
});

hello();
