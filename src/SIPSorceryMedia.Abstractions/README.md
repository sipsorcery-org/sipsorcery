# SIPSorceryMedia.Abstractions

[![NuGet](https://img.shields.io/nuget/v/SIPSorceryMedia.Abstractions.svg)](https://www.nuget.org/packages/SIPSorceryMedia.Abstractions)
[![NuGet downloads](https://img.shields.io/nuget/dt/SIPSorceryMedia.Abstractions.svg)](https://www.nuget.org/packages/SIPSorceryMedia.Abstractions)

Shared interfaces, enums, and helper types that connect the
[SIPSorcery](https://www.nuget.org/packages/SIPSorcery) real-time
communications library to media-device and codec implementations.

This package is a small dependency-free abstraction layer. You normally
don't reference it directly -- it comes in transitively via the main
SIPSorcery package and via media end-point implementations such as
[SIPSorceryMedia.Windows](https://www.nuget.org/packages/SIPSorceryMedia.Windows)
or [SIPSorceryMedia.FFmpeg](https://www.nuget.org/packages/SIPSorceryMedia.FFmpeg).
You only reference it explicitly when you're writing your own media
end-point or codec wrapper.

## Installation

```bash
dotnet add package SIPSorceryMedia.Abstractions
```

## What is in here

The package defines the contracts that every audio / video source, sink
and codec implements so the SIPSorcery library can interoperate with
them generically.

### Codec interfaces

| Interface | Implement when... | Reference implementation |
|---|---|---|
| `IAudioEncoder` | You provide audio encoding / decoding (G711, G722, G729, Opus, ...) | [`AudioEncoder`](https://github.com/sipsorcery-org/sipsorcery/blob/master/src/app/Media/Codecs/AudioEncoder.cs) in the main SIPSorcery package |
| `IVideoEncoder` | You provide video encoding / decoding (VP8, H.264, ...) | [`Vp8NetVideoEncoder`](https://github.com/sipsorcery-org/sipsorcery/blob/master/src/SIPSorcery.VP8/Vp8NetVideoEncoderEndPoint.cs) (pure C#); FFmpeg wrappers in `SIPSorceryMedia.FFmpeg` |

### Audio source / sink interfaces

| Interface | Implement when... | Reference implementation |
|---|---|---|
| `IAudioSource` | Your class is a source of raw audio samples (microphone, file player, signal generator) | [`WindowsAudioEndPoint`](https://github.com/sipsorcery-org/sipsorcery/blob/master/src/SIPSorceryMedia.Windows/WindowsAudioEndPoint.cs) |
| `IAudioSink`   | Your class consumes raw audio samples (speaker, recorder, RTP transmit) | [`WindowsAudioEndPoint`](https://github.com/sipsorcery-org/sipsorcery/blob/master/src/SIPSorceryMedia.Windows/WindowsAudioEndPoint.cs) |
| `IAudioEndPoint` | Your class is both a source and a sink (full-duplex device) | `WindowsAudioEndPoint`, `FFmpegAudioEndPoint` |

### Video source / sink interfaces

| Interface | Implement when... | Reference implementation |
|---|---|---|
| `IVideoSource` | Your class is a source of raw video frames (webcam, file player, test pattern) | [`WindowsVideoEndPoint`](https://github.com/sipsorcery-org/sipsorcery/blob/master/src/SIPSorceryMedia.Windows/WindowsVideoEndPoint.cs); `VideoTestPatternSource` in the main SIPSorcery package |
| `IVideoSink`   | Your class consumes raw video frames (display, recorder) | `WindowsVideoEndPoint`, `FFmpegVideoEndPoint` |
| `IVideoEndPoint` | Both a source and a sink for video | various |

### Common types and helpers

The package also contains shared types referenced from across the
ecosystem:

- `AudioFormat`, `VideoFormat` -- describe a codec / sample-rate /
  channel-layout combination negotiated over SDP.
- `AudioSamplingRatesEnum`, `VideoPixelFormatsEnum`,
  `VideoCodecsEnum`, `AudioCodecsEnum`.
- `RawImage` -- header + pinned byte buffer wrapper used to pass
  decoded video frames between encoders and sinks without copying.
- `PixelConverter` -- conversion helpers between common pixel
  formats (I420, NV12, BGR, RGB, YUV).

## Versioning and compatibility

`SIPSorceryMedia.Abstractions` shares a major version line with the
main SIPSorcery package. A binary-incompatible change to any of the
interfaces would land as a major version bump on both packages
together; minor versions add new interfaces or new types but never
modify existing ones in breaking ways.

## Related packages

- **[SIPSorcery](https://www.nuget.org/packages/SIPSorcery)** -- the
  main real-time communications library that consumes these
  abstractions.
- **[SIPSorceryMedia.Windows](https://www.nuget.org/packages/SIPSorceryMedia.Windows)**
  -- Windows-specific implementations of the audio / video end-point
  interfaces.
- **[SIPSorceryMedia.FFmpeg](https://www.nuget.org/packages/SIPSorceryMedia.FFmpeg)**
  -- cross-platform implementations using FFmpeg native libraries.
- **[SIPSorcery.VP8](https://www.nuget.org/packages/SIPSorcery.VP8)** --
  pure C# `IVideoEncoder` implementation for VP8.

## License

BSD 3-Clause License. See [LICENSE](https://github.com/sipsorcery-org/sipsorcery/blob/master/LICENSE) at the
repo root.
