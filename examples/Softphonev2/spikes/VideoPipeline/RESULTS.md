# Video pipeline spike: results

Run on 2026-11-05. The full tables are in [results/summary.md](results/summary.md) and
[results/cores4/summary.md](results/cores4/summary.md), with per-run JSON in `results/raw`.

**Machine:** i9-10900 (10 cores / 20 threads), NVIDIA Quadro T2000 plus Intel UHD 630, a 59 Hz
display, Windows 11, WebView2 runtime 156. Every run used the NVIDIA GPU. The near-end window was
1600×900 physical pixels. Each run measured for 30 s after 8 s of settling.

**Camera:** the main matrix used a synthetic 720p30 camera, because the MX Brio was in use by another
app at the time. Every camera run was then repeated with the real MX Brio at 720p30 (NV12, at a
steady 30 fps). See [Real camera](#real-camera) and [results/realcam/summary.md](results/realcam/summary.md).

## Recommendation

**Use WebView2 with the local WebRTC relay (path 1).** It was the cheapest path in every
configuration, usually by 3× or more, and it held 30 fps on every tile.

C# never touches a pixel. It forwards encoded frames between the SIP legs and the page. The
browser engine then does hardware H.264 decode with zero copies, renders, and encodes the camera.
The cost of C# forwarding 8 streams each way, SRTP included, was 11–24% of one core.

The native WPF baseline (path 3) was the *worst* on CPU and the only path that dropped frames at
8 streams. So "native" isn't automatically the safe choice for a video grid. Its cost is in getting
decoded pixels onto the screen, and that cost lands on one UI thread.

## Headline: 8 calls at 720p30 plus the camera (one shared encoder)

CPU is shown with 1 core = 100, for the near end's whole process tree. "lat" is from the SIP frame
being complete in C# to on screen, in ms.

| | CPU | fps (min / avg) | dropped | lat p50 / p95 |
|---|---|---|---|---|
| **VP8**, 1 relay | **93** | 30.0 / 30.0 | 0.1% | 48 / 51 |
| VP8, 2 buffer | 172 | 30.0 / 30.0 | 0% | 24 / 26 |
| VP8, 3 WPF | 290 | 22.5 / 26.1 | 13.3% | 33 / 51 |
| **H.264**, 1 relay | **80** | 29.2 / 29.8 | 0.7% | 39 / 91 |
| H.264, 2 buffer | 268 | 30.0 / 30.0 | 0% | 31 / 47 |
| H.264, 3 WPF | 370 | 27.7 / 28.3 | 5.9% | 30 / 60 |

Pinned to 4 logical CPUs (2 cores with hyperthreading, a stand-in for a small laptop), the same runs
cost:

- **Relay:** 86–90, at 30 fps.
- **Buffer:** 185–211, at 30 fps.
- **WPF:** 233–249, dropping 3–7%.

Even on 2 cores, the relay path stayed under one core of load.

## Real camera

The same 8-call runs with the MX Brio instead of the synthetic source:

| | Shared encoder: CPU / fps / dropped / lat p50/p95 | One encoder per call: CPU |
|---|---|---|
| **VP8**, 1 relay | **92** / 30.0 / 0.1% / 65/84 | 374 |
| VP8, 2 buffer | 136 / 30.0 / 0% / 27/28 | 725 |
| VP8, 3 WPF | 210 / 29.0 / 3.3% / 27/46 | 863 |
| **H.264**, 1 relay | **72** / 29.9 / 0.6% / 47/79 | 253 |
| H.264, 2 buffer | 317 / 30.0 / 0% / 26/43 | 275 |
| H.264, 3 WPF | 322 / 29.3 / 2.3% / 37/50 | 377 |

The ranking and the conclusions are unchanged. The real camera costs no more than the synthetic one.
The paths that capture in C# came out a little cheaper on VP8, because the real image compresses
more easily than the synthetic noise. Every camera ran at 30 fps on every call. The relay's VP8
latency was higher in these runs (p50 53–65 ms). That is the same two-value refresh effect described
below, and it's worth looking at with jitter buffer tuning.

## What else the numbers say

1. **Hardware decode only pays when the frame stays on the GPU.**
   - In path 2, H.264 decoded on the GPU (D3D11VA) and then copied back to memory cost
     **286** CPU at 8 streams, against **121** for plain software decode. The readback costs more
     than the decode it replaces (8.1 ms against 0.7 ms per frame).
   - The browser's hardware decode in path 1 never reads back, and costs 50.
   - So any C# decode path should either decode in software or render on the GPU directly
     (D3D11 → `D3DImage` / swap chain), which neither path 2 nor path 3 does.
2. **WPF's bottleneck is the UI thread.** Copying 8 × 3.7 MB of BGRA into `WriteableBitmap`s on
   every render tick falls behind at 8 streams, even with the CPU mostly idle. It drops 6–13% at 8
   streams and 7% for 4 × 1080p.
3. **The camera must be encoded once and fanned out.**
   - With one encoder per call at 8 calls, CPU rose to:
     - VP8: relay 379, buffer 855, WPF 1012.
     - H.264: relay 278 (8 × OpenH264), buffer 273 (8 × NVENC).
   - With one shared encoder, the cost was 80–93 for the relay.
   - Path 1 makes the shared encoder easy: only the first local connection carries the camera,
     and C# copies its frames to every SIP leg.
   - The cost is one bitrate and resolution for every call, so v0.1 needs a policy for that,
     such as the lowest bitrate any call can take, or two encoders (high and low).
4. **WebView2 encodes the camera in software.** It uses OpenH264 for H.264 and libvpx for VP8,
   even with NVENC available. One 720p30 encode is cheap (about 3 ms a frame), and that's the
   reason the shared encoder above matters.
5. **Latency is fine, and the relay adds a little.**
   - Path 1's p50 was 17–54 ms across runs. It moves between two values about two display
     refreshes apart (around 20 or around 50 ms), depending on where frames land against the
     59 Hz refresh.
   - About 8.5 ms of it is the WebRTC jitter buffer, decode is 0.6 ms (H.264) to 3.5 ms (VP8),
     and getting from SIP to the browser takes 0.5 ms.
   - The p95 reached 91 ms in some H.264 runs with the camera on.
   - Paths 2 and 3 measured 21–38 ms p50, but those figures are estimates. See the
     [README](README.md#how-each-number-is-measured).
6. **1080p is not a problem for the relay.** At 4 × 1080p30 it used 31 (VP8) and 37 (H.264),
   against 99/198 (buffer) and 172/257 (WPF, dropping 7%).
7. **One relay run is an outlier.** 8 × H.264 receive-only, with no camera, had 12 stalls and dipped
   to 26 fps. The heavier runs with the camera on, either side of it, were clean. It needs a rerun
   to see if it repeats.

## What this doesn't settle

- **The relay's camera bitrate.** The page's kbit/s calculation came out empty in path 1, so the
  browser's actual camera bitrate isn't recorded. It was capped at 1.5 Mbit/s with `maxBitrate`.
  Paths 2 and 3 measured 1.5 Mbit/s.
- **A mid-range laptop with only an Intel or AMD iGPU.** This machine's NVIDIA GPU did all the work.
  The quickest check is to set WebView2/NearEnd to "Power saving" in Windows Graphics settings, which
  forces the UHD 630, and rerun. Then try a real laptop.
- **Audio, lip sync and echo cancellation.** These argue for sending audio through the same local
  connection, so the browser's playout keeps it in sync with video and Chromium's echo canceller
  applies. That isn't measured here.
- **Congestion control.** The browser only sees the loopback link, so its bitrate estimate is
  meaningless. The camera is capped with `maxBitrate` here. A real build has to translate the far
  end's RTCP feedback (receiver reports, REMB) into that cap.
- **SIP phones whose video the browser can't decode.** That covers H.263, H.264 packetization-mode 0,
  and unusual profiles. These need a transcoding fallback in C#. The decode code in
  `VideoSpike.Common` is a starting point, and it should use software decode (point 1).
- **Jitter buffer tuning.** Setting `jitterBufferTarget` or `playoutDelayHint` to 0 on the receivers
  might trim the p95 latency. Not tried.

## Library findings along the way

- `FFmpegVideoEncoder.InitialiseDecoder` (`src/SIPSorceryMedia.FFmpeg/FFmpegVideoEncoder.cs:541`)
  applies decoder options to `_encoderContext->priv_data` instead of the decoder's context.
  It sets them on the wrong codec, and if no encoder has been opened yet it dereferences null.
- That class's decoder always converts to BGR24, and with hardware decode on it always reads the
  frame back. Point 1 shows that hardware decode is then a net CPU loss for anything that renders
  from system memory.
- Chromium only reports `decoderImplementation` / `powerEfficientDecoder` while the page has an
  active camera or microphone capture. This is a fingerprinting guard, and it's why the camera-off
  relay rows have no decoder name.
