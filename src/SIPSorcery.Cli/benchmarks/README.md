# Video pipeline benchmarks

[`run-video-bench.ps1`](run-video-bench.ps1) sweeps the SIPSorcery WebRTC video pipeline across
resolution presets and encoders to find the **maximum sustainable frame rate** for the **encode** and
**decode** stages, and writes a markdown table (with the machine's CPU/memory) and the raw JSON to
`results/`.

It is meant to be run **locally** (not in CI) — the numbers are machine specific and the two stages
share CPU on one box, so treat the output as a snapshot of that machine, not an absolute spec.

## How it works

Every measurement is one self-contained `webrtc loopback` run: it publishes a generated test pattern
to an in-process WHIP receiver (using the same `LibraryVideoPublisher` as `webrtc whip`) and receives
it back. No second terminal, no real network.

- **Encode ceiling** — publish flat out (a very high `--fps` the encoder cannot reach) with no
  decode; the reported `publishedFps` is the encoder's max sustainable rate. Measured for the managed
  `vp8.net` encoder and the `ffmpeg` encoder on **H264, VP8, VP9, H265 and AV1**.
- **Decode breakpoint** — publish with `--decode --video null` (decode in-process, discard) and sweep
  `--fps` upward until the receiver drops more than the threshold (default 10%) of frames. The frames
  are **pre-encoded once** (`-PreEncodeFrames`, default 300) and the encoded bitstream is replayed, so
  **no encoding runs during the window** — the breakpoint reflects the decoder alone, not encode and
  decode sharing CPU. The codec columns:
  - **Decode H264 / VP8 / VP9 / H265 / AV1 (ffmpeg)** — the SIPSorcery FFmpeg decoder, driven by the FFmpeg encoder.
  - **Decode VP8 (vp8.net)** — the managed Vpx.Net VP8 decoder, driven by the **vp8.net** encoder. It
    must use its own encoder's bitstream: the managed decoder crashes on FFmpeg-encoded VP8 (a Vpx.Net
    inter-prediction bug). Because vp8.net encode is slow, this column is **capped at ≤1080p** (larger
    presets show `n/a`); the decode measurement is still valid, only the one-time pre-encode would be too slow.
- **Plumbing (no codec)** — publish flat out (`--max-rate`) with **neither encoder nor decoder**:
  pre-encoded frames are replayed and the receiver discards them without decoding. The reported
  `publishedFps` is the pure WebRTC transport ceiling (packetise → SRTP → socket → depacketise) — the
  theoretical maximum the encode and decode stages sit under.

## Run it

```powershell
# FfmpegPath points at the directory holding the FFmpeg shared libraries (avcodec-*.dll, ...).
./run-video-bench.ps1 -FfmpegPath "C:\path\to\ffmpeg\bin"
```

Useful parameters (all optional):

| Parameter | Default | Notes |
| --- | --- | --- |
| `-Presets` | `480p 720p 1080p 1440p 4k` | Resolutions to test. |
| `-FpsLadder` | `15 30 60 90 120` | Decode sweep points. **Extend above the expected max** — if decode never crosses the threshold the result is the ladder's top value (a "≥"). |
| `-EncodeProbeFps` | `500` | The flat-out target for the encode ceiling probe. |
| `-PreEncodeFrames` | `300` | Frames the decode probe pre-encodes once and replays, so no encoding runs during the decode measurement (isolates decode). `0` encodes live (encode + decode share CPU). Pre-encoding happens before connecting, so it does not eat the media window. |
| `-PresetBitrate` | per-preset map | Realistic ffmpeg encoder bitrate (bps) per preset. Without it the encoder's auto-bitrate scales with the probe fps (hundreds of Mbps), making every measurement bitrate-bound instead of encoder-speed-bound. Ignored by `vp8.net`. |
| `-DropThreshold` | `0.10` | Decode loss fraction that defines the breakpoint. |
| `-DurationSeconds` | `6` | Seconds of media per run. |
| `-Runs` | `1` | Runs per point; the median is taken. Increase for less noise. |
| `-FfmpegPath` | (PATH) | FFmpeg shared-library directory. |
| `-OutputDir` | `./results` | Where `results.json` and `RESULTS.md` are written. |

The CLI is published to a temp directory once at the start, then invoked per run, so build/JIT noise
is out of the measurement loop.

## Output

`results/RESULTS.md` (the committable report — machine info + capacity table) and
`results/results.json` (machine + raw results). A full sweep at the defaults (five codecs encode +
decode across all presets) is roughly 25–40 minutes; narrow `-Presets` to shorten it.

## Caveats

- **One box.** Encode and decode are measured separately. With the default `-PreEncodeFrames`, the
  decode probe replays a pre-encoded stream so the encoder is out of the loop and the breakpoint is
  the decoder's own ceiling; set `-PreEncodeFrames 0` to instead encode live, in which case the
  in-process encoder and decoder compete and the breakpoint is "combined" capacity.
- **`vp8.net` is single-threaded managed** and caps low, especially at high resolutions — the
  interesting high-rate numbers come from the `ffmpeg` encoder.
- **Decode is FFmpeg for every codec**, so "Decode VP8" and "Decode H264" are the FFmpeg decoder on
  those bitstreams, regardless of which encoder produced them.
