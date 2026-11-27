# WebRTCGetStartedCodecs

Sends a test pattern and music to a browser over WebRTC, with the video codec,
and the library that encodes it, chosen on the command line. It's an ASP.NET
app that serves its own page and the web socket used for signalling.

[WebRTCGetStarted](../WebRTCGetStarted) is the simpler version, with just
vp8.net.

## Running

```
dotnet run
```

Then open <http://localhost:8080> and press Start. The page shows the codec it's
decoding, the frame size and the frame rate. Pick a library or codec with:

```
dotnet run -- --library h264.net
dotnet run -- --codec av1
dotnet run -- --library ffmpeg --codec h265
dotnet run -- --help
```

Visual Studio has a launch profile for each library.

## Libraries

| `--library` | Codecs | From | Needs |
|---|---|---|---|
| `vp8.net` (the default) | VP8 | SIPSorcery.VP8 | Nothing native. |
| `h264.net` | H264 | SIPSorcery.H264 | Nothing native. |
| `av1.net` | AV1 | SIPSorcery.AV1 | Nothing native. |
| `ffmpeg` | H264 (its default), H265, VP8, VP9, AV1 | SIPSorceryMedia.FFmpeg | The FFmpeg 8 shared libraries, e.g. `winget install Gyan.FFmpeg.Shared` on Windows. |

With `--codec` and no `--library`, the first library in the table that encodes
the codec sends it, so `--codec h264` uses h264.net and `--codec vp9` uses
FFmpeg. `--library ffmpeg --codec vp8` encodes VP8 with libvpx.

## Browsers

The offer holds only the chosen codec, with the encoder's own format parameters
(H264's `profile-level-id=42e01f`, for example), so the browser has to be able to
receive it. H265, and on older browsers AV1, depend on the browser and the
hardware. The page's [codec list](wwwroot/codecs.html), at
<http://localhost:8080/codecs.html>, shows what the browser can receive.

## Options

They're read through ASP.NET's configuration, so `--library=h264.net` and
environment variables work too.

| Option | |
|---|---|
| `--library <name>` | The library that encodes the video, from the table above. |
| `--codec <name>` | The video codec. The default is the library's first one. |
| `--urls <url>` | Where the web server listens. The default is `http://localhost:8080`. |
| `--stun <url>` | A STUN server for the ICE candidates, e.g. `stun:stun.cloudflare.com`. |
| `--turn <url>` | A TURN server, as `url;username;password`. |
| `--bind-port <port>` | The UDP port for the media. The default lets the OS choose. |
| `--ffmpeg-path <dir>` | Where the FFmpeg shared libraries are, if they aren't found on their own. |

## How it works

`VideoTestPatternSource` passes each frame to the library's `IVideoEncoder` and
raises the encoded frames, which go straight to `RTCPeerConnection.SendVideo`.
When the browser asks for a key frame with an RTCP PLI or FIR, the example calls
`ForceKeyFrame`. h264.net and av1.net also send one every two seconds.
