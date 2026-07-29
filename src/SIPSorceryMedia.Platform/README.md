# SIPSorceryMedia.Platform

Platform-agnostic audio end-point factory for the
[SIPSorcery](https://www.nuget.org/packages/SIPSorcery) real-time
communications library. Picks the right audio end-point implementation
at compile time based on the target OS:

| Target framework | Audio end-point used |
|---|---|
| `net10.0-windows*` | `WindowsAudioEndPoint` (NAudio) |
| `net10.0-macos` | `MacAudioEndPoint` (AVFoundation) |
| anything else | throws `PlatformNotSupportedException` |

Use this package when:

- You want a single project that compiles on both Windows and macOS
  without `#if` guards in your own code.
- You're building a cross-platform SIP/WebRTC application and want
  audio to just work on the developer's current OS.

For cross-platform audio + video (including Linux), use
[SIPSorceryMedia.FFmpeg](https://www.nuget.org/packages/SIPSorceryMedia.FFmpeg)
instead.

## Platform support

The project file uses the `$(SIPSorceryDesktopTargetFramework)`
MSBuild property (set in `Directory.Build.props`) to automatically
select `net10.0-windows10.0.17763.0` on Windows and `net10.0-macos`
on macOS.

## Installation

```bash
dotnet add package SIPSorcery
dotnet add package SIPSorceryMedia.Platform
```

## What is in here

| Class | Purpose |
|---|---|
| `DefaultAudioEndPointFactory` | Static `Create()` method that returns an `IAudioEndPoint` backed by the platform-native implementation. |

## Quickstart -- VoIP audio call (Windows or macOS)

```csharp
using SIPSorcery.Media;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.Platform;

const string DESTINATION = "music@iptel.org";

var userAgent = new SIPUserAgent();

IAudioEndPoint audio = DefaultAudioEndPointFactory.Create(new AudioEncoder());
((IAudioSource)audio).RestrictFormats(x => x.Codec == AudioCodecsEnum.PCMU);

var session = new VoIPMediaSession(new MediaEndPoints
{
    AudioSource = audio,
    AudioSink   = audio,
});

bool ok = await userAgent.Call(DESTINATION, null, null, session);
Console.WriteLine($"Call result: {(ok ? "success" : "failure")}");

Console.WriteLine("Press any key to hangup.");
Console.ReadLine();
```

See the full [GetStarted](https://github.com/sipsorcery-org/sipsorcery/tree/master/examples/SIPExamples/GetStarted)
example for a working project file.

## Related packages

- **[SIPSorcery](https://www.nuget.org/packages/SIPSorcery)** -- the
  main real-time communications library.
- **[SIPSorceryMedia.Abstractions](https://www.nuget.org/packages/SIPSorceryMedia.Abstractions)**
  -- the interfaces this package implements.
- **[SIPSorceryMedia.Windows](https://www.nuget.org/packages/SIPSorceryMedia.Windows)**
  -- Windows-specific audio implementation.
- **[SIPSorceryMedia.MacOS](https://www.nuget.org/packages/SIPSorceryMedia.MacOS)**
  -- macOS-specific audio implementation.
- **[SIPSorceryMedia.FFmpeg](https://www.nuget.org/packages/SIPSorceryMedia.FFmpeg)**
  -- cross-platform alternative built on FFmpeg.

## License

BSD 3-Clause License. See [LICENSE](https://github.com/sipsorcery-org/sipsorcery/blob/master/LICENSE) at the
repo root.
