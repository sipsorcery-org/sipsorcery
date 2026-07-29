# SIPSorceryMedia.MacOS

macOS-specific audio capture and audio playback end-points for the
[SIPSorcery](https://www.nuget.org/packages/SIPSorcery) real-time
communications library. Built on
[AVFoundation](https://developer.apple.com/av-foundation/) (AVAudioEngine /
AVAudioInputNode / AVAudioPlayerNode).

Use this package when:

- You're targeting macOS and want microphone / speaker access without
  pulling in FFmpeg.
- You need a quick way to give a SIPSorcery `RTPSession` something to
  send audio from and play received audio into.

For cross-platform audio + video, use
[SIPSorceryMedia.FFmpeg](https://www.nuget.org/packages/SIPSorceryMedia.FFmpeg)
instead.

## Platform support

- **Target**: `net10.0-macos`
- **Runtime**: macOS only -- the package uses AVFoundation APIs via the
  .NET Apple platform bindings.

### Microphone permission

The containing application must add an `NSMicrophoneUsageDescription`
entry in its `Info.plist` before microphone capture will be granted by
the OS.

## Installation

```bash
dotnet add package SIPSorcery
dotnet add package SIPSorceryMedia.MacOS
```

The `SIPSorceryMedia.Abstractions` interfaces this package implements
come in transitively.

## What is in here

| Class | Implements | Purpose |
|---|---|---|
| `MacAudioEndPoint` | `IAudioEndPoint` (source + sink) | Microphone capture and speaker playback. Wraps AVAudioEngine with AVAudioInputNode (capture) and AVAudioPlayerNode (playback). |

Captured hardware PCM is converted to mono PCM16, resampled in managed
code and emitted in fixed 20 ms codec frames. Decoded remote PCM16 is
scheduled on `AVAudioPlayerNode`.

## Quickstart -- VoIP audio call

The simplest possible example: place an outbound SIP audio call and
hear the audio through macOS speakers.

```bash
dotnet new console --name SIPGetStarted --framework net10.0-macos
cd SIPGetStarted
dotnet add package SIPSorcery
dotnet add package SIPSorceryMedia.MacOS
```

Paste into `Program.cs`:

```csharp
using SIPSorcery.Media;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.MacOS;

const string DESTINATION = "music@iptel.org";

var userAgent = new SIPUserAgent();
var macAudio  = new MacAudioEndPoint(new AudioEncoder());
var session   = new VoIPMediaSession(new MediaEndPoints
{
    AudioSource = macAudio,
    AudioSink   = macAudio,
});

bool ok = await userAgent.Call(DESTINATION, null, null, session);
Console.WriteLine($"Call result: {(ok ? "success" : "failure")}");

Console.WriteLine("Press any key to hangup.");
Console.ReadLine();
```

`dotnet run` -- you should hear the test audio (grant microphone access
when the OS prompts).

## Related packages

- **[SIPSorcery](https://www.nuget.org/packages/SIPSorcery)** -- the
  main real-time communications library.
- **[SIPSorceryMedia.Abstractions](https://www.nuget.org/packages/SIPSorceryMedia.Abstractions)**
  -- the interfaces this package implements.
- **[SIPSorceryMedia.Windows](https://www.nuget.org/packages/SIPSorceryMedia.Windows)**
  -- Windows-specific equivalent.
- **[SIPSorceryMedia.FFmpeg](https://www.nuget.org/packages/SIPSorceryMedia.FFmpeg)**
  -- cross-platform alternative built on FFmpeg.

## License

BSD 3-Clause License. See [LICENSE](https://github.com/sipsorcery-org/sipsorcery/blob/master/LICENSE) at the
repo root.
