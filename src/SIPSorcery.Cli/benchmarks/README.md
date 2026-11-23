# Video pipeline benchmarks

[`run-video-bench.ps1`](run-video-bench.ps1) measures the SIPSorcery video pipeline across resolution
presets: the **maximum frame rate of each encoder and decoder alone**, and of a **real WebRTC
session** sending, receiving, or both. It writes markdown tables (with the machine's CPU/memory) and
the raw JSON to `results/`.

It is meant to be run **locally** (not in CI): the numbers are machine specific, so treat the output
as a snapshot of that machine, not an absolute spec.

## How it works

Two CLI commands do the measuring, each for what it is built for:

- **`webrtc video-bench`** times one component alone, in-process, with no network, as fast as it
  will go. It gives the codec columns.
- **`webrtc loopback`** runs a real WebRTC session in one process: it publishes a generated test
  pattern to an in-process WHIP receiver (the same `LibraryVideoPublisher` as `webrtc whip`) and
  receives it back. It gives the WebRTC pipeline table.

The encode and decode tables (`video-bench`):

- **Encode** (`video-bench --encoder <e> --codec <c> --bitrate <preset bitrate>`): the encoder runs
  flat out on a moving test pattern and the reported `achievedFps` is its maximum rate (RTP
  packetisation included). Measured for the managed `vp8.net` and `av1.net` encoders and the `ffmpeg`
  encoder on **H264, VP8, VP9, H265 and AV1**.
- **Decode** (`video-bench --decoder <d> --encoder <clip encoder> --codec <c>`): a two-second clip is
  encoded once, untimed, then the decoder alone decodes it in a loop flat out. The reported
  `achievedFps` is its maximum rate.
  - **Decode H264 / VP8 / VP9 / H265 / AV1 (ffmpeg)**: the SIPSorcery FFmpeg decoder, on clips from
    the FFmpeg encoder. AV1 clips use a resolution based tile layout (1 tile up to 720p, 2×2 at
    1080p, 4×4 at 4K) so the decoder can use more than one core, as it would with a browser's stream.
  - **Decode VP8 (vp8.net)**: the managed Vpx.Net VP8 decoder, on a **vp8.net** clip. It must use its
    own encoder's bitstream: the managed decoder crashes on FFmpeg-encoded VP8 (a Vpx.Net
    inter-prediction bug).

The WebRTC pipeline table (`loopback`, ffmpeg encoder and decoder, H264 by default):

- **Transport (no codec)** (`--pre-encode 300 --max-rate`, no decode): pre-encoded frames replayed
  flat out and discarded on receipt. The pure transport ceiling (packetise → SRTP → socket →
  depacketise), the maximum everything else sits under.
- **Send (encode)** (`--max-rate`, no decode): live encode sent flat out, frames discarded on
  receipt. The most an app that only sends (a camera, a WHIP publisher, a bot) can publish.
- **Receive (decode)** (`--pre-encode 300 --decode --video null`): a pre-encoded stream decoded on
  receipt. The most an app that only receives (a viewer, a recorder) can decode.
- **Full call (encode + decode)** (`--decode --video null`): live encode, send, receive and decode in
  one process.

Receive and full call are found by searching `--fps`, not by sending flat out: WebRTC has no
backpressure, so a sender that outruns the decoder makes the receiver drop frames, which breaks the
reference chain and stops later frames decoding. A rate passes when the sender delivers it and no
more than 2% of the frames sent are lost, dropped or fail to decode; the search stops within 5% of
the maximum.

## Run it

```powershell
# FfmpegPath points at the directory holding the FFmpeg shared libraries (avcodec-*.dll, ...).
./run-video-bench.ps1 -FfmpegPath "C:\path\to\ffmpeg\bin"
```

Useful parameters (all optional):

| Parameter | Default | Notes |
| --- | --- | --- |
| `-Presets` | `480p 720p 1080p 1440p 4k` | Resolutions to test. |
| `-TargetFps` | `30` | The nominal rate the encoders run their rate control at. The measured rates are maximums either way. |
| `-PresetBitrate` | per-preset map | Realistic bitrate (bps) per preset for the `ffmpeg` and `av1.net` encoders and the decode clips. Ignored by `vp8.net`. |
| `-PipelineCodec` | `h264` | The codec the pipeline table sends with (ffmpeg encoder and decoder). `''` skips the table. |
| `-PipelinePreEncodeFrames` | `300` | Frames the transport and receive probes pre-encode and replay. |
| `-PipelineLossThreshold` | `0.02` | Largest fraction of sent frames the receive and full-call searches may lose and still pass. |
| `-PipelinePrecision` | `0.05` | The receive and full-call searches stop once the bounds are within this fraction. |
| `-DurationSeconds` | `6` | Seconds per run. |
| `-Runs` | `1` | Runs per point; the median is taken. Increase for less noise. |
| `-FfmpegPath` | (PATH) | FFmpeg shared-library directory. |
| `-OutputDir` | `./results` | Where `results.json` and `RESULTS.md` are written. |

The CLI is published to a temp directory once at the start, then invoked per run, so build/JIT noise
is out of the measurement loop.

To measure one cell by hand, run the same command the script does, for example:

```powershell
dotnet run -c Release --project src/SIPSorcery.Cli -- webrtc video-bench --preset 1080p --decoder ffmpeg --codec av1 --json
```

## Output

`results/RESULTS.md` (the committable report: machine info and the capacity table) and
`results/results.json` (machine and raw results).

## Caveats

- **Codec columns are components alone.** A real call encodes, packetises, sends, receives and
  decodes on the same machine, so the rates these tables show are not achievable together. The
  pipeline table's full call column is what a whole stream manages.
- **Both ends of the loopback run on one machine.** Send also pays for depacketising and receive for
  replaying and encrypting. The transport column shows how small that cost is next to the codecs;
  a true one-way figure needs two machines.
- **Decode speed depends on the stream as well as the decoder.** The same decoder can run at very
  different rates on clips from different encoders, AV1 tiling especially.
- **`vp8.net` is single-threaded managed** and caps low, especially at high resolutions; the high
  rates come from the `ffmpeg` encoder.
