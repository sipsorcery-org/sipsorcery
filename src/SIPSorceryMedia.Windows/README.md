# SIPSorceryMedia.Windows

[![NuGet](https://img.shields.io/nuget/v/SIPSorceryMedia.Windows.svg)](https://www.nuget.org/packages/SIPSorceryMedia.Windows)
[![NuGet downloads](https://img.shields.io/nuget/dt/SIPSorceryMedia.Windows.svg)](https://www.nuget.org/packages/SIPSorceryMedia.Windows)

Windows-specific audio capture, audio playback, and video capture
end-points for the [SIPSorcery](https://www.nuget.org/packages/SIPSorcery)
real-time communications library. Built on
[NAudio](https://github.com/naudio/NAudio) for audio and Windows Media
Foundation for video.

Use this package when:

- You're targeting Windows and want microphone / speaker / webcam
  access without pulling in FFmpeg.
- You need a quick way to give a SIPSorcery `RTPSession` something to
  send audio from and play received audio into.

For cross-platform audio + video, use
[SIPSorceryMedia.FFmpeg](https://www.nuget.org/packages/SIPSorceryMedia.FFmpeg)
instead.

## Platform support

- **Target**: `net10.0-windows10.0.17763.0` (Windows 10 1809+).
- **Runtime**: Windows only -- the package PInvokes Windows-specific
  audio and video APIs.

## Installation

```bash
dotnet add package SIPSorcery
dotnet add package SIPSorceryMedia.Windows
```

The `SIPSorceryMedia.Abstractions` interfaces this package implements
come in transitively.

## What is in here

| Class | Implements | Purpose |
|---|---|---|
| `WindowsAudioEndPoint` | `IAudioEndPoint` (source + sink) | Microphone capture and speaker playback. Wraps NAudio's `WaveInEvent` and `WaveOutEvent`. |
| `WindowsVideoEndPoint` | `IVideoSource` | Webcam capture via Windows Media Foundation. Surfaces enumerated formats and resolutions. |
| `WindowsAudioSession` | `IAudioSession` | Convenience pairing of audio source + sink + encoder. |

The audio end-point automatically resamples between the device's
native sample rate and the codec's sample rate (e.g. 48 kHz device,
8 kHz G.711 codec).

## Quickstart -- VoIP audio call

The simplest possible example: place an outbound SIP audio call and
hear the audio through Windows speakers.

```bash
dotnet new console --name SIPGetStarted --framework net10.0-windows10.0.17763.0
cd SIPGetStarted
dotnet add package SIPSorcery
dotnet add package SIPSorceryMedia.Windows
```

Paste into `Program.cs`:

```csharp
using SIPSorcery.Media;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Windows;

const string DESTINATION = "music@iptel.org";

var userAgent = new SIPUserAgent();
var winAudio  = new WindowsAudioEndPoint(new AudioEncoder());
var session   = new VoIPMediaSession(winAudio.ToMediaEndPoints());

bool ok = await userAgent.Call(DESTINATION, null, null, session);
Console.WriteLine($"Call result: {(ok ? "success" : "failure")}");

Console.WriteLine("Press any key to hangup.");
Console.ReadLine();
```

`dotnet run` -- you should hear the test audio.

## Quickstart -- WebRTC video to a browser

The full sample lives at
[`examples/WebRTCExamples/WebRTCGetStarted`](https://github.com/sipsorcery-org/sipsorcery/tree/master/examples/WebRTCExamples/WebRTCGetStarted).
Pair `WindowsVideoEndPoint` with a video encoder
(SIPSorceryMedia.FFmpeg's `FFmpegVideoEncoder`,
[SIPSorcery.VP8](https://www.nuget.org/packages/SIPSorcery.VP8), or a
custom `IVideoEncoder`) and feed the encoded samples into
`RTCPeerConnection.SendVideo`.

## Audio sample rate notes

- `WindowsAudioEndPoint` defaults to 8 kHz internal processing -- fine
  for narrow-band G.711 / G.722 codecs which are the most common in
  VoIP.
- For wide-band Opus or 48 kHz codecs, construct the end-point with
  the higher sample rate so internal resampling is minimised:
  `new WindowsAudioEndPoint(new AudioEncoder(), audioSampleRate: 48000)`.

## Related packages

- **[SIPSorcery](https://www.nuget.org/packages/SIPSorcery)** -- the
  main real-time communications library.
- **[SIPSorceryMedia.Abstractions](https://www.nuget.org/packages/SIPSorceryMedia.Abstractions)**
  -- the interfaces this package implements.
- **[SIPSorceryMedia.FFmpeg](https://www.nuget.org/packages/SIPSorceryMedia.FFmpeg)**
  -- cross-platform alternative built on FFmpeg.

## License

BSD 3-Clause License. See [LICENSE](https://github.com/sipsorcery-org/sipsorcery/blob/master/LICENSE) at the
repo root.
