# WebRTCGetStartedCodecs

Sends a test pattern and music to a browser over WebRTC, with the video codec,
and the library that encodes it, picked on the page. It's an ASP.NET app that
serves its own page and the web socket used for signalling.

[WebRTCGetStarted](../WebRTCGetStarted) is the simpler version, with just
vp8.net.

## Running

```
dotnet run
```

Then open <http://localhost:8080>, pick a codec and an encoder, and press Start.
The page shows the codec it's decoding, the frame size and the frame rate. To
try another codec, press Close, pick it and press Start again.

The codec list disables the codecs this browser can't receive and those that no
available library encodes. If FFmpeg's shared libraries can't be loaded, the
example still runs with the managed libraries, and the page says why FFmpeg
isn't available.

## Libraries

| Encoder | Codecs | From | Needs |
|---|---|---|---|
| `vp8.net` | VP8 | SIPSorcery.VP8 | Nothing native. |
| `h264.net` | H264 | SIPSorcery.H264 | Nothing native. |
| `av1.net` | AV1 | SIPSorcery.AV1 | Nothing native. |
| `ffmpeg` | VP8, VP9, H264, H265, AV1 | SIPSorceryMedia.FFmpeg | The FFmpeg 8 shared libraries, e.g. `winget install Gyan.FFmpeg.Shared` on Windows. |

For each codec the page picks the first available encoder in the table, so the
managed one when there is one.

## Browsers

The offer holds only the chosen codec, with the encoder's own format parameters
(H264's `profile-level-id=42e01f`, for example), so the browser has to be able to
receive it. H265, and on older browsers AV1, depend on the browser and the
hardware. The page's [codec list](wwwroot/codecs.html), at
<http://localhost:8080/codecs.html>, shows what the browser can receive.

## Options

They're read through ASP.NET's configuration, so `--stun=url` and environment
variables work too.

| Option | |
|---|---|
| `--urls <url>` | Where the web server listens. The default is `http://localhost:8080`. |
| `--stun <url>` | A STUN server for the ICE candidates, e.g. `stun:stun.cloudflare.com`. |
| `--turn <url>` | A TURN server, as `url;username;password`. |
| `--bind-port <port>` | The UDP port for the media. The default lets the OS choose. |
| `--ffmpeg-path <dir>` | Where the FFmpeg shared libraries are, if they aren't found on their own. |

## How it works

The page gets the libraries, and the codecs each encodes, from `/libraries`, and
puts its choice in the web socket's query string, `/ws?codec=H264&library=h264.net`.
For each connection, `VideoTestPatternSource` passes the frames to that library's
`IVideoEncoder` and raises the encoded frames, which go straight to
`RTCPeerConnection.SendVideo`. When the browser asks for a key frame with an
RTCP PLI or FIR, the example calls `ForceKeyFrame`. h264.net and av1.net also
send one every two seconds.
