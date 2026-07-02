#!/usr/bin/env python3
"""
neural_sidecar.py - a Wav2Lip audio->video sidecar for the Max Headroom avatar.

This is the model half of the NeuralAvatarRenderer prototype. The C# side
(NeuralAvatarRenderer) streams 16 kHz mono PCM to this process and receives back
lip-synced BGR video frames; it encodes them onto the WebRTC video track. That is the
same boundary as bitHuman's push_audio()/flush() and LiveKit's DataStreamAudioOutput.

Design (see the repo README): the persona is a STATIC image, so the Wav2Lip
`video_frames` input is constant and precomputed once - only the mel window changes per
output frame. The sidecar keeps a rolling audio buffer and emits each frame as soon as
enough audio (a ~16-mel-column window, ~200 ms) is available, which streams with low
latency and far above real time on a modest GPU.

It reuses the wav2lip-onnx repo's audio.py (mel spectrogram) and its ONNX model; point
--repo at the clone and --model at the checkpoint. Face box is found once with OpenCV's
bundled Haar cascade, so there is no insightface dependency.

Two modes:
  * --selftest IMG WAV OUT.mp4  : offline render to eyeball quality (no C# involved).
  * (default) serve             : WebSocket server for the C# renderer.

WebSocket protocol (ws://host:port):
  * On connect the server sends one JSON text frame: {"type":"hello","w":W,"h":H,"fps":F}.
  * Client -> server: JSON text {"type":"begin"} / {"type":"end"} bracket an utterance;
    binary messages are raw little-endian int16 mono PCM at 16 kHz (audio windows).
  * Server -> client: binary messages, each exactly W*H*3 bytes of BGR888 (one frame). Frames
    stream CONTINUOUSLY at FPS whether or not Max is speaking - a steady emitter advances the
    animated background every tick and only swaps the mouth when audio is available - so the
    background never freezes during silence (the video analogue of the always-on audio clock).
"""

import argparse
import asyncio
import json
import os
import subprocess
import sys
import tempfile

import cv2
import numpy as np
import onnxruntime as ort

# --- Config / constants -------------------------------------------------------------

TARGET_W, TARGET_H = 640, 480     # frame size published to the WebRTC track (matches C#).
FPS = 25                          # output video frame rate.
SAMPLE_RATE = 16000               # Wav2Lip mel front-end rate.
IMG_SIZE = 96                     # Wav2Lip face crop size.
MEL_STEP = 16                     # mel columns per inference window.
MEL_PER_FRAME = 80.0 / FPS        # mel columns advanced per output frame.
PADS = (0, 10, 0, 0)              # (top, bottom, left, right) box padding; extends the chin.

# Dynamic-background composition (matched against the max_intro.mp4 reference):
ZOOM = 1.25                       # scale the figure up so head+shoulders fill the frame.
ZOOM_TOP = 70                     # top crop offset (scaled px) - keeps the full head in view.


def load_audio_module(repo_dir):
    """Import the wav2lip-onnx repo's audio.py (mel spectrogram) from its clone."""
    repo_dir = os.path.abspath(repo_dir)
    if repo_dir not in sys.path:
        sys.path.insert(0, repo_dir)
    import audio  # noqa: E402  (needs the sys.path insert above)
    return audio


def detect_face_box(image):
    """One-time face box on the static persona via OpenCV's bundled Haar cascade."""
    cascade_path = os.path.join(cv2.data.haarcascades, "haarcascade_frontalface_default.xml")
    cascade = cv2.CascadeClassifier(cascade_path)
    gray = cv2.cvtColor(image, cv2.COLOR_BGR2GRAY)
    faces = cascade.detectMultiScale(gray, scaleFactor=1.1, minNeighbors=5, minSize=(80, 80))
    if len(faces) == 0:
        raise SystemExit("No face detected in the persona image. Use a clearer front-facing photo.")
    # Largest detection wins.
    x, y, w, h = max(faces, key=lambda f: f[2] * f[3])
    pady1, pady2, padx1, padx2 = PADS
    y1 = max(0, y - pady1)
    y2 = min(image.shape[0], y + h + pady2)
    x1 = max(0, x - padx1)
    x2 = min(image.shape[1], x + w + padx2)
    return (y1, y2, x1, x2)


class Wav2LipRenderer:
    """Static-persona Wav2Lip renderer: mel window -> full BGR frame with a synced mouth."""

    def __init__(self, model_path, persona_path, audio_mod, dynamic_bg=True, matte_path=None):
        self.audio = audio_mod

        so = ort.SessionOptions()
        so.graph_optimization_level = ort.GraphOptimizationLevel.ORT_ENABLE_ALL
        self.sess = ort.InferenceSession(
            model_path, sess_options=so,
            providers=["DmlExecutionProvider", "CPUExecutionProvider"])
        print("Wav2Lip running on:", self.sess.get_providers()[0], flush=True)

        persona = cv2.imread(persona_path)
        if persona is None:
            raise SystemExit(f"Could not read persona image: {persona_path}")
        persona = cv2.resize(persona, (TARGET_W, TARGET_H))
        self.persona = persona
        self.box = detect_face_box(persona)
        y1, y2, x1, x2 = self.box
        print(f"Persona face box (y1,y2,x1,x2) = {self.box}", flush=True)

        # The face crop (and therefore the 6-channel model input's identity half) is
        # constant for a static persona, so build it once. Lower half masked = the region
        # Wav2Lip regenerates from audio.
        face = cv2.resize(persona[y1:y2, x1:x2], (IMG_SIZE, IMG_SIZE))
        masked = face.copy()
        masked[IMG_SIZE // 2:] = 0
        img = np.concatenate((masked, face), axis=2) / 255.0        # [96,96,6]
        self._face_input = img.transpose(2, 0, 1)[None].astype(np.float32)  # [1,6,96,96]

        # Segment Max's figure once (his pose is static; only the mouth - which is inside the
        # silhouette - changes). The matte lets us drop the frozen starburst and drop the
        # lip-synced head onto an animated background instead.
        self.dynamic_bg = dynamic_bg
        if dynamic_bg:
            # Matte, blend weights, colour-grade matrix and post map are fixed per session,
            # so build them once. The compositing hot path then runs almost entirely in
            # OpenCV SIMD ops - the pure-numpy version couldn't hold 25fps live.
            self.alpha = self._zoom(self._compute_matte(persona, matte_path))
            self._post = self._post_map()
            self._w_fg = np.ascontiguousarray(self.alpha[..., 0])          # HxW float32.
            self._w_one = np.ones_like(self._w_fg)                         # for 1 - warped w.
            # Grade folded to an affine colour transform: desaturate 12%, dim to 92% (the
            # lift/wash and warm cast live in the bias, pre-multiplied by the post map).
            # Everything is staged for saturating uint8 cv2 ops - the hot path has no
            # full-frame float math at all.
            m = (np.eye(3) * 0.88 + np.full((3, 3), 0.12 / 3.0)) * 0.92
            self._grade_m = m.astype(np.float32)
            bias = np.array([14.0 - 4.0, 14.0, 14.0 + 6.0], np.float32)   # B,G,R.
            post3 = np.repeat(self._post, 3, axis=2)
            self._post3_u8 = np.clip(post3 * 255.0, 0, 255).astype(np.uint8)
            self._grade_bias_u8 = np.clip(bias[None, None, :] * post3, 0, 255).astype(np.uint8)
            self._bg_cache = None
            self._bg_cache_idx = -1
        else:
            self.alpha = None

        # Liveness: eye rects (for blinks) found once on the static persona.
        self._eyes = self._detect_eyes(persona, self.box) if dynamic_bg else []

        # Precomputed idle mouth (closed, from a silence mel) reused for every silent frame, so
        # the continuous emitter never has to run inference when Max isn't speaking.
        silence = np.zeros((1, 1, 80, MEL_STEP), np.float32)
        self.idle_mouth96 = self._infer(silence)
        self.idle_frame = self._finish(self.idle_mouth96, 0)

        # Warm the mel front-end: librosa builds/caches its filterbank on first use, which
        # otherwise lands as ~1s of extra mouth latency on the first utterance.
        self.mel_from_pcm16(np.zeros(SAMPLE_RATE // 2, np.int16))

    def _infer(self, mel_batch):
        """Run the model for one mel window -> 96x96x3 uint8 BGR mouth region."""
        pred = self.sess.run(None, {"mel_spectrogram": mel_batch,
                                    "video_frames": self._face_input})[0][0]
        pred = pred.transpose(1, 2, 0) * 255.0
        return pred.astype(np.uint8)

    def _compose(self, mouth96):
        """Paste the generated mouth region back onto a fresh copy of the persona."""
        y1, y2, x1, x2 = self.box
        frame = self.persona.copy()
        frame[y1:y2, x1:x2] = cv2.resize(mouth96, (x2 - x1, y2 - y1))
        return frame

    BG_EVERY = 3   # re-render the (slowly drifting) background every Nth frame.

    def _finish(self, mouth96, idx):
        """Full output frame: paste mouth, apply liveness (blink + head sway), zoom the
        figure, composite over the animated bg, then the VHS grade + scanline/vignette
        post-pass. Hot path - cv2 SIMD throughout."""
        fg = self._compose(mouth96)
        if not self.dynamic_bg:
            return np.ascontiguousarray(fg)

        if self._bg_cache is None or not (0 <= idx - self._bg_cache_idx < self.BG_EVERY):
            self._bg_cache = self._background(idx)
            self._bg_cache_idx = idx

        blink = self._blink_amount(idx)
        if blink > 0.0:
            self._apply_blink(fg, blink)               # persona coords, before the zoom.

        zfg = self._zoom(fg)
        # Head sway/jerk: the same small affine moves the figure AND its matte against the
        # animated background, which reads as body motion.
        M = self._pose_matrix(idx)
        zfg = cv2.warpAffine(zfg, M, (TARGET_W, TARGET_H), flags=cv2.INTER_LINEAR,
                             borderMode=cv2.BORDER_REPLICATE)
        w_fg = cv2.warpAffine(self._w_fg, M, (TARGET_W, TARGET_H), flags=cv2.INTER_LINEAR,
                              borderMode=cv2.BORDER_CONSTANT, borderValue=0.0)
        w_bg = cv2.subtract(self._w_one, w_fg)

        comp = cv2.blendLinear(zfg, self._bg_cache, w_fg, w_bg)
        out = cv2.transform(comp, self._grade_m)                          # desat + dim.
        out = cv2.multiply(out, self._post3_u8, scale=1.0 / 255.0)        # scanline/vignette.
        out = cv2.add(out, self._grade_bias_u8)                           # wash + warm cast.
        return out

    # --- Liveness: idle motion so the avatar never reads as a frozen photo ---------------

    def _pose_matrix(self, idx):
        """Small head transform per frame: a smooth micro-sway plus the occasional held
        offset "snap" - the stuttery pose jumps that were Max Headroom's signature look."""
        t = idx / FPS
        dx = 2.5 * np.sin(t * 0.7) + 1.5 * np.sin(t * 1.31)
        dy = 1.2 * np.sin(t * 0.9 + 1.0)
        rot = 0.7 * np.sin(t * 0.53)

        period = 3.7                                   # a jerk every few seconds...
        k = int(t / period)
        if (t - k * period) < 0.24:                    # ...held for a few frames.
            rng = (k * 2654435761) & 0xFFFF            # deterministic pseudo-random pose.
            dx += ((rng % 9) - 4) * 1.6
            dy += (((rng >> 4) % 5) - 2) * 1.0
            rot += (((rng >> 8) % 5) - 2) * 0.6

        M = cv2.getRotationMatrix2D((TARGET_W / 2.0, TARGET_H * 0.55), rot, 1.0)
        M[0, 2] += dx
        M[1, 2] += dy
        return M

    def _blink_amount(self, idx):
        """0..1 eyelid closure. A ~200ms triangular blink lands at a pseudo-random point in
        each ~3.3s window, so blinks feel irregular but need no state."""
        t = idx / FPS
        period = 3.3
        k = int(t / period)
        start = ((k * 40503) & 0xFFFF) % 100 / 100.0 * (period - 0.3)
        ph = (t - k * period) - start
        if 0.0 <= ph < 0.20:
            return 1.0 - abs(ph / 0.10 - 1.0)
        return 0.0

    def _apply_blink(self, fg, amount):
        """Close the eyes by STRETCHING the lid skin just above each eye down over it (never
        the brow - sliding the full strip dragged the eyebrow down as a dark smear). The
        seam is feathered; at VHS blur levels it reads as a closing lid."""
        for (x, y, w, h) in self._eyes:
            cover = int(h * amount)
            if cover <= 2:
                continue
            strip_h = max(3, int(h * 0.35))            # lid skin below the brow.
            strip = fg[y - strip_h:y, x:x + w]
            patch = cv2.resize(strip, (w, cover), interpolation=cv2.INTER_LINEAR)
            fg[y:y + cover, x:x + w] = patch
            # Feather the bottom seam of the lid into the remaining eye.
            y0 = max(0, y + cover - 3)
            y1 = min(fg.shape[0], y + cover + 3)
            fg[y0:y1, x:x + w] = cv2.GaussianBlur(fg[y0:y1, x:x + w], (1, 5), 0)

    @staticmethod
    def _detect_eyes(persona, face_box):
        """One-time eye boxes on the persona via OpenCV's bundled Haar eye cascade. Returns
        up to two rects (x, y, w, h) clamped so a same-height strip above each is in-frame;
        empty when detection fails (blinking is then disabled). If only ONE eye is found
        (e.g. the other is in deep shadow, as on the Max photo), it is mirrored across the
        face's centreline so the blink isn't a wink."""
        cascade = cv2.CascadeClassifier(
            os.path.join(cv2.data.haarcascades, "haarcascade_eye.xml"))
        gray = cv2.cvtColor(persona, cv2.COLOR_BGR2GRAY)
        found = cascade.detectMultiScale(gray, scaleFactor=1.1, minNeighbors=6,
                                         minSize=(24, 24))
        # Keep the two largest in the upper half of the frame (eyes, not nostrils/mouth).
        found = [f for f in found if f[1] + f[3] / 2 < persona.shape[0] * 0.55]
        found = sorted(found, key=lambda f: f[2] * f[3], reverse=True)[:2]

        if len(found) == 1:
            x, y, w, h = found[0]
            face_cx = (face_box[2] + face_box[3]) / 2.0
            mx = int(2 * face_cx - (x + w / 2.0) - w / 2.0)   # mirrored left edge.
            if 0 <= mx <= persona.shape[1] - w:
                found = [found[0], (mx, y, w, h)]

        eyes = []
        for (x, y, w, h) in sorted(found, key=lambda f: f[0]):
            if y - h < 0:
                continue
            # An eye lost in deep shadow is invisible to the viewer, so patching it only
            # creates a bright block artifact - skip it; the lit eye carries the blink.
            if gray[y:y + h, x:x + w].mean() < 55:
                continue
            eyes.append((int(x), int(y), int(w), int(h)))
        print(f"Blink eyes: {eyes if eyes else 'none found - blinking disabled'}", flush=True)
        return eyes

    @staticmethod
    def _zoom(img):
        """Scale the persona-sized image by ZOOM and crop back to TARGET size, keeping the
        head in view (reference framing: Max large, head+shoulders filling the frame)."""
        zw, zh = int(TARGET_W * ZOOM), int(TARGET_H * ZOOM)
        big = cv2.resize(img, (zw, zh), interpolation=cv2.INTER_LINEAR)
        x0 = (zw - TARGET_W) // 2
        y0 = min(ZOOM_TOP, zh - TARGET_H)
        return big[y0:y0 + TARGET_H, x0:x0 + TARGET_W]

    @staticmethod
    def _post_map():
        """Per-pixel brightness map: soft vignette + every-3rd-row scanline (VHS feel)."""
        yy, xx = np.mgrid[0:TARGET_H, 0:TARGET_W].astype(np.float32)
        rx = (xx - TARGET_W / 2) / (TARGET_W / 2)
        ry = (yy - TARGET_H / 2) / (TARGET_H / 2)
        vignette = np.clip(1.06 - 0.30 * (rx * rx + ry * ry), 0.60, 1.0)
        scanline = np.where((yy.astype(np.int32) % 3) == 0, 0.84, 1.0).astype(np.float32)
        return (vignette * scanline)[..., None].astype(np.float32)

    def _compute_matte(self, persona, matte_path=None):
        """Foreground matte (Max's figure), HxWx3 float 0..1 (1 = keep the figure).

        Prefers a precomputed grayscale PNG (e.g. from rembg - much cleaner hair edges);
        falls back to GrabCut when no matte file is present."""
        if matte_path and os.path.isfile(matte_path):
            m = cv2.imread(matte_path, cv2.IMREAD_GRAYSCALE)
            m = cv2.resize(m, (TARGET_W, TARGET_H)).astype(np.float32) / 255.0
            m = self._repair_bust_matte(m)
            print(f"Loaded matte {matte_path}", flush=True)
            return np.dstack([m, m, m])

        print("No matte file; falling back to GrabCut.", flush=True)
        mask = np.zeros(persona.shape[:2], np.uint8)
        bgd = np.zeros((1, 65), np.float64)
        fgd = np.zeros((1, 65), np.float64)
        rect = (150, 40, TARGET_W - 280, TARGET_H - 40)   # head + torso; edges are background.
        cv2.grabCut(persona, mask, rect, bgd, fgd, 5, cv2.GC_INIT_WITH_RECT)
        fg = np.where((mask == cv2.GC_FGD) | (mask == cv2.GC_PR_FGD), 1.0, 0.0).astype(np.float32)
        fg = cv2.GaussianBlur(fg, (5, 5), 0)              # feather the cut edge a touch.
        return np.dstack([fg, fg, fg])

    @staticmethod
    def _repair_bust_matte(m):
        """Fix the classic dark-suit-on-dark-background matte failure for a bust portrait.

        Segmenters return weak alpha where the suit blends into the black backdrop, so the
        animated background bleeds through the shoulders. Repair: boost the weak alpha, drop
        thin stray wisps, then FILL DOWN each column - in a head-and-shoulders portrait
        nothing hangs below the figure, so once a column enters the figure it stays
        foreground to the bottom edge."""
        m = np.clip((m - 0.08) / 0.24, 0.0, 1.0)     # weak suit alpha -> solid; true bg stays 0.
        # Fill down ONLY in the shoulder zone. Above it the raw matte is correct (keeps the
        # gaps beside the temples/neck as background, and Max's thin hair spike intact);
        # below it the suit runs to the bottom edge, so once a column enters the suit it
        # stays foreground.
        y0 = int(0.70 * TARGET_H)
        m[y0:] = np.clip(m[y0:] * 1.5, 0.0, 1.0)     # extra boost for the near-black corners.
        m[y0:] = np.maximum.accumulate(m[y0:], axis=0)
        return cv2.GaussianBlur(m, (5, 5), 0)        # feather the edge.

    @staticmethod
    def _hsv(h, s, v):
        c = cv2.cvtColor(np.uint8([[[int(h) % 180, int(s), int(v)]]]), cv2.COLOR_HSV2BGR)[0][0]
        return (int(c[0]), int(c[1]), int(c[2]))

    def _background(self, idx):
        """Animated backdrop matched to the max_intro reference: the INSIDE of a cube (room
        corner), ruled with glowing blue/cyan/violet louvres.

        Geometry (per the reference footage): two walls meet at a VERTICAL fold rising from
        the corner vertex, floor below. The LEFT wall and the FLOOR are ruled in the SAME
        "/" direction (~45 deg to the window bottom) - their seam is only a colour change
        between two parallel line fields. The RIGHT wall is ruled shallow (~15 deg, i.e. the
        left's direction rotated ~150 deg); its louvres share fold points with the left
        wall's, so the lines meet at the fold in wide chevrons. Each seam runs parallel to
        its plane's lines. The vertex hides behind Max and everything drifts slowly."""
        t = idx / FPS
        # Rendered at HALF resolution then scaled up: the AA halo strokes are the cost hog,
        # and the bloom blur + upscale hide the difference entirely.
        BW, BH = TARGET_W // 2, TARGET_H // 2
        bg = np.zeros((BH, BW, 3), np.uint8)
        vx = BW * 0.46 + 20 * np.cos(t * 0.13)
        vy = BH * 0.76 + 12 * np.sin(t * 0.11)
        # Rule angles measured from the bottom of the window (per the reference):
        #  * left wall AND floor: ~45 deg "/" - the SAME direction; the wall/floor seam is
        #    only a colour change between two parallel line fields.
        #  * right wall: shallow ~15 deg "\" - the left's direction rotated ~150 deg.
        aL = np.deg2rad(45.0 + 3.0 * np.sin(t * 0.08))
        aR = np.deg2rad(15.0 + 3.0 * np.sin(t * 0.06 + 1.0))
        diag = np.hypot(BW, BH)
        R = 2.0 * diag
        spacing = 13.0                              # 26px at full res.
        ph_wall = (t * 5.0) % spacing               # louvres crawl slowly up the fold...
        ph_floor = (t * 4.0) % spacing              # ...and floor rules drift sideways.

        d_l = (-np.cos(aL), np.sin(aL))             # left louvres: down-left from the fold.
        d_r = (np.cos(aR), np.sin(aR))              # right louvres: down-right, shallow.

        def wedge_mask(a1, a2):
            """Filled wedge at the vertex between screen angles a1->a2 (degrees, y-down)."""
            pts = [(vx, vy)]
            for deg in np.linspace(a1, a2, 14):
                r = np.deg2rad(deg)
                pts.append((vx + R * np.cos(r), vy + R * np.sin(r)))
            m = np.zeros((BH, BW), np.uint8)
            cv2.fillPoly(m, [np.array(pts, np.int32)], 255)
            return m

        # Region boundaries follow the rule directions: each seam is parallel to its plane's
        # lines. Angles y-down: up = 270, left seam = 180-aL (~135), right seam = aR (~15).
        deg_l = np.degrees(np.arctan2(d_l[1], d_l[0]))    # ~135.
        deg_r = np.degrees(np.arctan2(d_r[1], d_r[0]))    # ~15.
        regions = [
            ("wallL", wedge_mask(deg_l, 270.0), 96),      # left of the fold, above its seam.
            ("wallR", wedge_mask(-90.0, deg_r), 118),     # right of the fold, above its seam.
            ("floor", wedge_mask(deg_r, deg_l), 136),     # everything below the seams.
        ]
        if os.environ.get("BG_DEBUG_ONLY"):
            regions = [r for r in regions if r[0] == os.environ["BG_DEBUG_ONLY"]]
        for f, (name, mask, hue0) in enumerate(regions):
            hue = hue0 + 8 * np.sin(t * 0.07 + f * 2.1)

            # Near-black tinted panel; each louvre is a wide dim halo under a bright core.
            panel = np.empty_like(bg)
            panel[:] = self._hsv(hue, 210, 26)
            halo = self._hsv(hue, 215, 115)
            core = self._hsv(hue - 6, 130, 255)

            cx, cy = BW / 2.0, BH / 2.0               # for line visibility culling.

            if name != "floor":
                # Wall louvres start on the fold (same heights both sides, so the lines
                # meet there in wide chevrons) and descend away from it.
                d = d_l if name == "wallL" else d_r
                for k in range(0, 80):
                    fy = vy - (k * spacing + ph_wall)
                    # Louvres from fold points far above the frame still cross it on the way
                    # down (a 45 deg line entering top-left starts ~W px up the fold), so only
                    # stop once the line can no longer intersect the frame.
                    if fy < -diag:
                        break
                    # Cull louvres whose line misses the frame entirely.
                    if abs(d[0] * (cy - fy) - d[1] * (cx - vx)) > 0.75 * diag:
                        continue
                    p1 = (int(vx), int(fy))
                    p2 = (int(vx + d[0] * R), int(fy + d[1] * R))
                    cv2.line(panel, p1, p2, halo, 4, cv2.LINE_AA)
                    cv2.line(panel, p1, p2, core, 1, cv2.LINE_AA)
            else:
                # Floor rules: PARALLEL to the left wall's louvres (the seam between them is
                # a pure colour change), stepped perpendicular to the shared direction.
                perp = (np.sin(aL), np.cos(aL))
                for m in range(-40, 41):
                    off = m * spacing + ph_floor
                    px, py = vx + perp[0] * off, vy + perp[1] * off
                    # Cull rules whose line misses the frame entirely.
                    if abs(d_l[0] * (cy - py) - d_l[1] * (cx - px)) > 0.75 * diag:
                        continue
                    p1 = (int(px - d_l[0] * R), int(py - d_l[1] * R))
                    p2 = (int(px + d_l[0] * R), int(py + d_l[1] * R))
                    cv2.line(panel, p1, p2, halo, 4, cv2.LINE_AA)
                    cv2.line(panel, p1, p2, core, 1, cv2.LINE_AA)

            cv2.copyTo(panel, mask, bg)   # masked copy in C++ (numpy fancy-indexing is slow).

        # Faint fold + seams (the louvre kinks / colour change do most of the work).
        for d in ((0.0, -1.0), d_l, d_r):
            cv2.line(bg, (int(vx), int(vy)),
                     (int(vx + d[0] * R), int(vy + d[1] * R)), (16, 16, 24), 1, cv2.LINE_AA)

        # Mild blur = phosphor bloom, then scale up to full res (adds its own softness).
        bg = cv2.GaussianBlur(bg, (0, 0), 0.7)
        return cv2.resize(bg, (TARGET_W, TARGET_H), interpolation=cv2.INTER_LINEAR)

    def mel_from_pcm16(self, pcm16):
        """int16 PCM -> mel spectrogram (same front-end as the repo's audio.py)."""
        wav = pcm16.astype(np.float32) / 32768.0
        return self.audio.melspectrogram(wav)

    def mouth_for(self, mel, rel):
        """96x96 mouth region for utterance-relative frame `rel`, or None if audio isn't there yet."""
        if mel is None or rel < 0:
            return None
        start = int(rel * MEL_PER_FRAME)
        if start + MEL_STEP > mel.shape[1]:
            return None
        window = mel[:, start:start + MEL_STEP]
        return self._infer(window.reshape(1, 1, 80, MEL_STEP).astype(np.float32))

    def frame_for_mel_index(self, mel, i):
        """Frame i's full output (mouth + bg); None if not enough audio yet. Used by --selftest."""
        mouth = self.mouth_for(mel, i)
        if mouth is None:
            return None
        return self._finish(mouth, i)


# --- Offline self test --------------------------------------------------------------

def run_selftest(renderer, wav_path, out_path):
    """Render a whole wav to an mp4 so quality can be eyeballed without the C# side."""
    wav = renderer.audio.load_wav(wav_path, SAMPLE_RATE)
    mel = renderer.audio.melspectrogram(wav)
    n_frames = int(mel.shape[1] / MEL_PER_FRAME)
    print(f"Rendering {n_frames} frames ({n_frames / FPS:.1f}s)...", flush=True)

    tmp_avi = os.path.join(tempfile.gettempdir(), "neural_selftest.avi")
    writer = cv2.VideoWriter(tmp_avi, cv2.VideoWriter_fourcc(*"DIVX"), FPS, (TARGET_W, TARGET_H))
    for i in range(n_frames):
        frame = renderer.frame_for_mel_index(mel, i)
        if frame is None:
            break
        writer.write(frame)
    writer.release()

    subprocess.run(["ffmpeg", "-y", "-i", tmp_avi, "-i", wav_path,
                    "-c:v", "libx264", "-pix_fmt", "yuv420p", "-shortest", out_path], check=True)
    os.remove(tmp_avi)
    print(f"Wrote {out_path}", flush=True)


# --- WebSocket serving --------------------------------------------------------------

async def serve(renderer, host, port):
    import websockets

    async def handler(ws):
        await ws.send(json.dumps({"type": "hello", "w": TARGET_W, "h": TARGET_H, "fps": FPS}))

        loop = asyncio.get_event_loop()
        # Shared, single-threaded state: the receive loop updates it, the emitter reads it.
        # 'dirty' marks PCM that hasn't been folded into the mel yet - the mel is recomputed
        # LAZILY by the emitter (not per message), so the C# side can burst-push a whole
        # utterance without triggering a full mel recompute per 30ms window.
        st = {"mel": None, "dirty": False, "speaking": False, "mouth_frame": 0,
              "mouth": renderer.idle_mouth96, "pcm": bytearray(), "idx": 0}
        stop = asyncio.Event()

        def refresh_mel():
            samples = np.frombuffer(bytes(st["pcm"]), dtype=np.int16)
            if samples.size >= SAMPLE_RATE // 10:   # need some audio before the first mel.
                st["mel"] = renderer.mel_from_pcm16(samples)
            st["dirty"] = False

        async def emitter():
            """Steady 25 fps producer. The background advances every tick (st['idx'], a wall
            clock) so it never freezes. The MOUTH advances only as fast as its audio arrives -
            st['mouth_frame'] increments per rendered mouth, not per wall-clock frame - so it
            can't outrun the ~200 ms look-ahead and stall on idle (that was the sync bug)."""
            period = 1.0 / FPS
            next_t = loop.time()
            while not stop.is_set():
                if st["speaking"]:
                    live = renderer.mouth_for(st["mel"], st["mouth_frame"])
                    if live is None and st["dirty"]:
                        refresh_mel()               # fold newly-arrived PCM in and retry.
                        live = renderer.mouth_for(st["mel"], st["mouth_frame"])
                    if live is not None:
                        st["mouth"] = live
                        st["mouth_frame"] += 1
                    # else: hold the previous mouth until its audio catches up.
                else:
                    st["mouth"] = renderer.idle_mouth96
                frame = renderer._finish(st["mouth"], st["idx"])
                try:
                    await ws.send(frame.tobytes())
                except Exception:
                    break
                st["idx"] += 1
                next_t += period
                await asyncio.sleep(max(0.0, next_t - loop.time()))

        emit_task = asyncio.create_task(emitter())
        try:
            async for msg in ws:
                if isinstance(msg, (bytes, bytearray)):
                    st["pcm"].extend(msg)
                    st["dirty"] = True                # mel is refreshed lazily by the emitter.
                else:
                    ctrl = json.loads(msg)
                    if ctrl.get("type") == "begin":
                        st["pcm"] = bytearray()
                        st["mel"] = None
                        st["dirty"] = False
                        st["mouth_frame"] = 0         # restart the mouth timeline for this utterance.
                        st["speaking"] = True
                    elif ctrl.get("type") == "end":
                        st["speaking"] = False
        finally:
            stop.set()
            await emit_task

    print(f"Neural sidecar listening on ws://{host}:{port}", flush=True)
    # compression=None: permessage-deflate is on by default and zlib-compressing every
    # ~900KB frame costs more than a frame period. Raw frames over localhost are fine.
    async with websockets.serve(handler, host, port, max_size=None, compression=None):
        await asyncio.Future()  # run forever.


# --- Entry point --------------------------------------------------------------------

def main():
    # Windows timers default to ~15.6ms resolution, which makes asyncio.sleep overshoot and
    # caps the 25fps emitter at ~18fps. Ask for 1ms resolution like every media app does.
    if sys.platform == "win32":
        import ctypes
        ctypes.windll.winmm.timeBeginPeriod(1)

    ap = argparse.ArgumentParser(description="Wav2Lip audio->video sidecar for the Max Headroom avatar.")
    ap.add_argument("--repo", default=r"C:\tools\wav2lip\wav2lip-onnx", help="wav2lip-onnx clone (for audio.py).")
    ap.add_argument("--model", default=r"C:\tools\wav2lip\wav2lip-onnx\checkpoints\wav2lip_gan.onnx")
    ap.add_argument("--persona", default=r"C:\tools\wav2lip\persona.jpg", help="Front-facing face image.")
    ap.add_argument("--matte", default=r"C:\tools\wav2lip\persona_alpha.png",
                    help="Grayscale foreground matte PNG (e.g. from rembg); GrabCut is used if absent.")
    ap.add_argument("--host", default="127.0.0.1")
    ap.add_argument("--port", type=int, default=5002)
    ap.add_argument("--selftest", nargs=2, metavar=("WAV", "OUT_MP4"),
                    help="Render WAV to OUT_MP4 offline and exit.")
    ap.add_argument("--static-bg", action="store_true",
                    help="Keep the persona's original background instead of compositing the animated one.")
    args = ap.parse_args()

    audio_mod = load_audio_module(args.repo)
    renderer = Wav2LipRenderer(args.model, args.persona, audio_mod,
                               dynamic_bg=not args.static_bg, matte_path=args.matte)

    if args.selftest:
        run_selftest(renderer, args.selftest[0], args.selftest[1])
    else:
        asyncio.run(serve(renderer, args.host, args.port))


if __name__ == "__main__":
    main()
