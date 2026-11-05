# Softphone v2: video pipeline spike

This spike answers one question before any UI host is chosen: **can the desktop softphone show a
grid of up to 8 incoming SIP video calls plus your camera, at 720p30, and what does that cost with
each way of putting video on screen?**

Results and the recommendation that came out of them are in [RESULTS.md](RESULTS.md).

## The three paths

| | Path | Who decodes | Who draws | Camera encode |
|---|---|---|---|---|
| 1 | **relay**: WebView2 with a local WebRTC connection | the browser engine (hardware where it can) | `<video>` elements | the browser engine |
| 2 | **buffer**: WebView2 with shared buffers | C#, FFmpeg (D3D11VA for H.264) | WebGL2, uploading NV12 from WebView2 shared buffers | C#, FFmpeg (NVENC/QSV/MF for H.264, libvpx for VP8) |
| 3 | **wpf**: native WPF baseline | C#, FFmpeg, as path 2 | `WriteableBitmap` per tile, BGRA converted on the decode thread | C#, as path 2 |

In path 1 C# never touches pixels. Each SIP leg's RTP is depacketised into frames and re-sent,
still encoded and with its original RTP timestamp, on a local `RTCPeerConnection` to the page. The
browser's camera comes back the same way and C# forwards it, still encoded, to the SIP legs.
*Shared* camera mode means one browser encoder whose output C# copies to every call. *Per call*
means each connection carries the camera, so the browser runs one encoder per call.

Path 3 isn't the fastest possible native renderer. A zero-copy D3D11 → `D3DImage` path would be
cheaper. It's the straightforward native one, there as a reference point.

## Pieces

- `FarEnd`: a SIP server on `127.0.0.1:5090`. It answers any number of calls and streams a
  pre-encoded clip on each at 30 fps (`sip:vp8-720p30@…` plays `media/vp8-720p30.ivf`). It also
  counts the video sent back.
- `NearEnd.Web`: the WebView2 host for paths 1 and 2, with `wwwroot/relay.*` and `wwwroot/buffer.*`.
- `NearEnd.Wpf`: path 3.
- `Bench`: runs the matrix. It starts the far end and launches a near end per configuration.
  When every tile is showing video, it waits 8 s to settle, then measures for 30 s. It writes
  `results/raw/<run>.json` and `results/summary.md` / `.csv`.
- `VideoSpike.Common`: shared code (SIP legs, clip reader, FFmpeg decode/encode, camera, stats).

## Run it

Needs .NET 10, the WebView2 runtime, and FFmpeg 8 shared builds with libvpx and libx264 on `PATH`
(for example `winget install Gyan.FFmpeg.Shared`).

```bash
pwsh ./make-clips.ps1
```

```bash
dotnet build Bench -c Release && dotnet build FarEnd -c Release && dotnet build NearEnd.Web -c Release && dotnet build NearEnd.Wpf -c Release
```

```bash
./artifacts/bin/Bench/Release/Bench.exe --matrix smoke
```

```bash
./artifacts/bin/Bench/Release/Bench.exe --matrix default
```

The near-end window stays on top while it runs. Chromium throttles rendering for windows it thinks
are covered, so covering or minimising it skews the run.

Other options:
- A single run: `--path relay --streams 8 --codec h264 --camera real --cameramode percall`.
- `--camera real` uses the first DirectShow camera (the code's default name is `MX Brio`), which
  must not be in use by another app. `fake` is a synthetic 720p30 source.
- `--affinity 0xF` pins the near end and all its WebView2 processes to those logical CPUs, and
  moves the far end and the bench off them.

## How each number is measured

- **CPU**: the bench samples the near end's whole process tree from outside, once a second. That's
  the host plus every WebView2 browser, GPU, renderer and utility process. "1 core = 100", so 250
  means two and a half cores busy. A per-process split is in the raw JSON.
- **GPU**: the Windows "GPU Engine" counters (what Task Manager shows) for those processes only,
  per adapter. Each engine type is shown as its busiest engine. *Decode* and *encode* are the
  fixed-function video engines, so a non-zero value shows the hardware really did the work.
- **Frames**: frames received off each SIP leg, frames shown, and the difference as dropped.
  A *stall* is a gap of more than 100 ms between frames shown on a tile.
- **Latency**: from the moment C# has a complete frame from the SIP leg to when it's on screen.
  - Path 1 uses the browser's own `requestVideoFrameCallback` `expectedDisplayTime` for that
    frame's RTP timestamp. The browser and C# clocks are aligned by a ping calibration
    (round trip about 0.2 ms).
  - Paths 2 and 3 take the animation or render tick that drew the frame, plus one 60 Hz refresh
    for the compositor. So they're estimates, close to but not as exact as path 1's.
  - None of these is glass-to-glass. The far end's encode and the network aren't included, and
    neither are the display's own scan-out and response time.
- **Hardware decode/encode**: path 1 reports what the browser says it used (`decoderImplementation`,
  `powerEfficientDecoder` and so on). Paths 2 and 3 report whether FFmpeg actually produced GPU
  frames.

## Limits

- Everything runs on one machine over loopback. There's no loss or jitter, and the far end's clips
  are synthetic (`testsrc2` plus noise, at about 2 Mbit/s for 720p and 4 Mbit/s for 1080p).
- Audio isn't in this spike, so lip sync and echo cancellation aren't measured.
- In path 1 the browser's congestion control only sees the loopback link. A real build has to cap
  the camera bitrate itself, or translate the far end's RTCP feedback.
