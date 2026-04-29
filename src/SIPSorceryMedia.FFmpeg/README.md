# SIPSorceryMedia.FFmpeg

[![NuGet](https://img.shields.io/nuget/v/SIPSorceryMedia.FFmpeg.svg)](https://www.nuget.org/packages/SIPSorceryMedia.FFmpeg)
[![NuGet downloads](https://img.shields.io/nuget/dt/SIPSorceryMedia.FFmpeg.svg)](https://www.nuget.org/packages/SIPSorceryMedia.FFmpeg)

Cross-platform audio and video media end-points for the
[SIPSorcery](https://www.nuget.org/packages/SIPSorcery) real-time
communications library, built on top of native
[FFmpeg](https://ffmpeg.org/) libraries via
[FFmpeg.AutoGen](https://github.com/Ruslan-B/FFmpeg.AutoGen).

Tested on **Windows, macOS, and Linux**.

> **Licence note.** Unlike the rest of the SIPSorcery family this
> package is licensed under **LGPL-2.1-only** (because it links FFmpeg
> at runtime, and FFmpeg's default build is LGPL). Make sure that's
> acceptable for your application before depending on it.

## Installation

```bash
dotnet add package SIPSorcery
dotnet add package SIPSorceryMedia.FFmpeg
```

You also need the **FFmpeg shared libraries** installed on the target
machine -- this NuGet package wraps FFmpeg, it doesn't ship the
binaries. See [Installing FFmpeg](#installing-ffmpeg) below.

## What is in here

### Codecs

- **Video**: VP8, H.264, H.265, MJPEG.
- **Audio**: PCMU (G.711), PCMA (G.711), G.722, G.729, Opus.

### Sources

| Class | Source kind |
|---|---|
| `FFmpegFileSource` | Local files or remote URIs (HTTP, RTSP, ...) -- audio + video. |
| `FFmpegCameraSource` | Local webcam capture -- video. |
| `FFmpegScreenSource` | Screen / monitor capture -- video. |
| `FFmpegMicrophoneSource` | Microphone capture -- audio. |

You can mix any video source (or none) with any audio source (or
none) when assembling a media session.

### Encoders / decoders

| Class | Purpose |
|---|---|
| `FFmpegVideoEncoder` | `IVideoEncoder` -- VP8 / H.264 / H.265 / MJPEG encode + decode. |
| `FFmpegAudioEncoder` | `IAudioEncoder` -- audio codec encode + decode for the codecs above. |
| `FFmpegVideoEndPoint` | Convenience pairing of source + encoder + sink. |

### Limitations

- **No audio output.** This package does not provide a speaker / playback
  end-point. For audio playback pair it with a platform end-point such
  as [SIPSorceryMedia.Windows](https://www.nuget.org/packages/SIPSorceryMedia.Windows)
  on Windows or [SIPSorcery.SDL2](https://github.com/sipsorcery-org/SIPSorceryMedia.SDL2)
  on other platforms.

## Installing FFmpeg

The shared libraries this package binds to must be discoverable on the
target machine's library load path. Typical setups:

### Windows

The simplest path is `winget`:

```ps1
winget install "FFmpeg (Shared)" --version 7.0
```

That puts the DLLs somewhere SIPSorceryMedia.FFmpeg can find them.

Alternative -- download a "shared" build from
[gyan.dev/ffmpeg/builds/#release-builds](https://www.gyan.dev/ffmpeg/builds/#release-builds)
or use the binaries shipped with
[FFmpeg.AutoGen](https://github.com/Ruslan-B/FFmpeg.AutoGen/tree/master/FFmpeg/bin/x64),
and add them to your `PATH`.

### Linux

```bash
sudo apt install ffmpeg
```

(Equivalent for `dnf`, `pacman`, etc. on other distributions.)

### macOS

```bash
brew install ffmpeg
brew install mono-libgdiplus    # for the test apps that draw bitmaps
```

### FFmpeg version compatibility

This package targets the FFmpeg 7.0 ABI via FFmpeg.AutoGen 7.0.0. Older
or much newer FFmpeg installations may load with PInvoke errors. If
you see `EntryPointNotFoundException` or `DllNotFoundException` at
startup, the most common cause is an FFmpeg version mismatch.

## Quickstart -- WebRTC video to a browser

The
[`WebRTCTestPatternServer`](https://github.com/sipsorcery-org/sipsorcery/tree/master/examples/WebRTCExamples/WebRTCTestPatternServer)
example shows the minimal end-to-end path: a video test-pattern source,
encoded with FFmpeg's H.264 encoder, sent over WebRTC to a browser.

Sketch:

```csharp
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorceryMedia.FFmpeg;

FFmpegInit.Initialise();   // load the shared libs on whatever PATH is configured

var pc = new RTCPeerConnection(null);
var source = new VideoTestPatternSource(new FFmpegVideoEncoder());

var track = new MediaStreamTrack(source.GetVideoSourceFormats(), MediaStreamStatusEnum.SendOnly);
pc.addTrack(track);

source.OnVideoSourceEncodedSample += pc.SendVideo;
pc.OnVideoFormatsNegotiated += formats => source.SetVideoSourceFormat(formats.First());
```

## Testing

The repo includes a console test harness at
[`test/FFmpegFileAndDevicesTest`](https://github.com/sipsorcery-org/sipsorcery/tree/master/test/FFmpegFileAndDevicesTest)
that exercises file, camera, and screen sources end-to-end.

## Related packages

- **[SIPSorcery](https://www.nuget.org/packages/SIPSorcery)** -- the
  main real-time communications library.
- **[SIPSorceryMedia.Abstractions](https://www.nuget.org/packages/SIPSorceryMedia.Abstractions)**
  -- the interfaces this package implements.
- **[SIPSorceryMedia.Windows](https://www.nuget.org/packages/SIPSorceryMedia.Windows)**
  -- Windows-only audio + video device access (no FFmpeg dependency).
- **[SIPSorcery.VP8](https://www.nuget.org/packages/SIPSorcery.VP8)**
  -- pure C# VP8 codec (no native dependency).

## License

LGPL-2.1-only. The rest of the SIPSorcery family is BSD-3-Clause; this
package is the exception because of FFmpeg's licensing.
