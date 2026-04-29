# SIPSorcery.VP8

[![NuGet](https://img.shields.io/nuget/v/SIPSorcery.VP8.svg)](https://www.nuget.org/packages/SIPSorcery.VP8)
[![NuGet downloads](https://img.shields.io/nuget/dt/SIPSorcery.VP8.svg)](https://www.nuget.org/packages/SIPSorcery.VP8)

A **pure C# VP8 video codec** for the
[SIPSorcery](https://www.nuget.org/packages/SIPSorcery) real-time
communications library. No native binaries, no PInvoke -- runs anywhere
.NET runs.

This is a port of Google's reference VP8 implementation
([libvpx](https://chromium.googlesource.com/webm/libvpx)) into managed
C#. Use this package when you want VP8 video support without the
FFmpeg native dependency required by
[SIPSorceryMedia.FFmpeg](https://www.nuget.org/packages/SIPSorceryMedia.FFmpeg).

## Status

| Path | State |
|---|---|
| **Decoder** | Working. Functional but slow -- the C-to-C# port hasn't been performance-tuned. Acceptable for low-resolution / low-frame-rate WebRTC peers. |
| **Encoder -- keyframe (I-frame)** | Working. DC_PRED for Y / UV, single token partition, default quantizer Q=32. |
| **Encoder -- inter (P-frame)** | Working. ZEROMV referencing LAST_FRAME for every macroblock. No real motion estimation yet (NEWMV is on the roadmap). |
| **WebRTC interop** | Verified end-to-end against Chrome via the example apps below. |

For the technical write-up, the foundation-PR sequence, and the
ongoing roadmap (real motion estimation, additional intra modes,
GOLDEN / ALTREF references, loop filter), see
[`diary/2026-04-26-Claude-Opus-4.7/result.md`](https://github.com/sipsorcery-org/sipsorcery/blob/master/diary/2026-04-26-Claude-Opus-4.7/result.md).

## Installation

```bash
dotnet add package SIPSorcery
dotnet add package SIPSorcery.VP8
```

## Quickstart -- send a VP8 test pattern over WebRTC

```csharp
using SIPSorcery.Media;
using SIPSorcery.Net;
using Vpx.Net;

var pc = new RTCPeerConnection(null);

// VP8Codec implements both IVideoEncoder (encode) and IVideoSink (decode).
var vp8Codec = new VP8Codec();

// Wrap it as an encoder end-point.
var encoderEndPoint = new Vp8NetVideoEncoderEndPoint(vp8Codec);

// Drive it from a test pattern source -- pass the codec directly, no
// I420->BGR->I420 round-trip in the wiring.
var testPatternSource = new VideoTestPatternSource(vp8Codec);

var track = new MediaStreamTrack(testPatternSource.GetVideoSourceFormats(),
                                 MediaStreamStatusEnum.SendOnly);
pc.addTrack(track);

testPatternSource.OnVideoSourceEncodedSample += pc.SendVideo;
pc.OnVideoFormatsNegotiated += formats =>
    testPatternSource.SetVideoSourceFormat(formats.First());

await testPatternSource.StartVideo();
```

## Configuration

`VP8Codec` exposes a few tuning knobs:

| Property | Default | Purpose |
|---|---|---|
| `BaseQIndex` | 32 | VP8 base quantizer index, range 0-127. Lower = better quality, higher bitrate. |
| `KeyframeIntervalFrames` | 30 | One keyframe per N frames (1 keyframe/sec at 30 fps default). Set to 1 to force every frame to be a keyframe. |

## Examples

The repository includes two end-to-end demos that use this package:

- **[WebRTCGetStartedVP8Net](https://github.com/sipsorcery-org/sipsorcery/tree/master/examples/WebRTCExamples/WebRTCGetStartedVP8Net)**
  -- pure C# WebRTC server streaming an animated test pattern + audio
  to a browser.
- **[WebRTCClientVP8Net](https://github.com/sipsorcery-org/sipsorcery/tree/master/examples/WebRTCExamples/WebRTCClientVP8Net)**
  -- pure C# WebRTC client receiving a VP8 stream from a browser and
  decoding it locally.

## Performance characteristics

- **Encoder**: optimised for allocation hygiene -- effectively zero
  allocations per frame after warmup. Single-threaded; one core
  comfortably handles 30 fps at typical webcam resolutions on a
  modern CPU.
- **Decoder**: not yet performance-tuned. For 1080p / 30 fps you'll
  want the FFmpeg-based decoder.

## Related packages

- **[SIPSorcery](https://www.nuget.org/packages/SIPSorcery)** -- the
  main real-time communications library.
- **[SIPSorceryMedia.Abstractions](https://www.nuget.org/packages/SIPSorceryMedia.Abstractions)**
  -- the `IVideoEncoder` interface this package implements.
- **[SIPSorceryMedia.FFmpeg](https://www.nuget.org/packages/SIPSorceryMedia.FFmpeg)**
  -- alternative VP8 implementation via native FFmpeg (faster, but
  pulls in native dependencies and an LGPL licence).

## License

BSD 3-Clause License. See [LICENSE](https://github.com/sipsorcery-org/sipsorcery/blob/master/LICENSE) at the
repo root.
