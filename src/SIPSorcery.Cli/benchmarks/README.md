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
  decode; the reported `publishedFps` is the encoder's max sustainable rate. Measured for `vp8.net`,
  `ffmpeg` H264 and `ffmpeg` VP8.
- **Decode breakpoint** — publish with `--decode --video null` (decode in-process, discard) and sweep
  `--fps` upward until the receiver drops more than the threshold (default 10%) of frames. Driven by
  the fast `ffmpeg` encoder so the **decoder** is the limit, not the encoder. The decoder is always
  the SIPSorcery (FFmpeg) decoder; measured for H264 and VP8.

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
`results/results.json` (machine + raw results). A full sweep at the defaults is roughly 10–15 minutes.

## Caveats

- **One box, shared CPU.** With `--decode` the in-process encoder and decoder compete; the decode
  breakpoint is "combined" capacity, not isolated decode.
- **`vp8.net` is single-threaded managed** and caps low, especially at high resolutions — the
  interesting high-rate numbers come from the `ffmpeg` encoder.
- **Decode is FFmpeg for every codec**, so "Decode VP8" and "Decode H264" are the FFmpeg decoder on
  those bitstreams, regardless of which encoder produced them.
