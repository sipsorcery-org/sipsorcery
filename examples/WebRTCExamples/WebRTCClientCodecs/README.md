# WebRTCClientCodecs

Receives a browser's camera, or a test pattern, over WebRTC and shows it in a
window, with the video codec, and the library that decodes it, picked on the
page. It's an ASP.NET app that serves its own page and the web socket used for
signalling. The window makes it Windows only.

[WebRTCGetStartedCodecs](../WebRTCGetStartedCodecs) goes the other way, sending
video to the browser with the same choice of codecs.

## Running

```
dotnet run
```

Then open <http://localhost:8081>, pick a codec and a decoder, and press Start.
The page sends its camera, or a test pattern if there's no camera or the box is
ticked, and shows what it's sending: the codec, the frame size, the frame rate,
and how many key frames and key frame requests there have been. The program's
window shows the decoded video, with the codec, decoder, frame size and frame
rate under it. To try another codec, press Close, pick it and press Start again.

The codec list disables the codecs this browser can't send and those that no
available library decodes. If FFmpeg's shared libraries can't be loaded, the
example still runs with the managed libraries, and the page says why FFmpeg
isn't available.

The window shows one browser at a time. A browser that connects replaces the
one before it.

## Libraries

| Decoder | Codecs | From | Needs |
|---|---|---|---|
| `vp8.net` | VP8 | SIPSorcery.VP8 | Nothing native. |
| `h264.net` | H264 | SIPSorcery.H264 | Nothing native. |
| `ffmpeg` | VP8, VP9, H264, H265, AV1 | SIPSorceryMedia.FFmpeg | The FFmpeg 8 shared libraries, e.g. `winget install Gyan.FFmpeg.Shared` on Windows. |

For each codec the page picks the first available decoder in the table, so the
managed one when there is one. SIPSorcery.AV1 isn't here because it only
encodes.

SIPSorcery.H264 is for educational purposes only. H.264 is covered by patents,
and anyone wishing to use it in a live application should check their patent
exposure first. See [its README](../../../src/SIPSorcery.H264/README.md).

## Browsers

The offer holds only the chosen codec, with the decoder's own format parameters
(H264's `profile-level-id=42e01f`, for example), so the browser has to be able to
send it. H265, and on older browsers AV1, depend on the browser and the
hardware. The page's [codec list](wwwroot/codecs.html), at
<http://localhost:8081/codecs.html>, shows what the browser can send.

## Options

They're read through ASP.NET's configuration, so `--stun=url` and environment
variables work too.

| Option | |
|---|---|
| `--urls <url>` | Where the web server listens. The default is `http://localhost:8081`. |
| `--stun <url>` | A STUN server for the ICE candidates, e.g. `stun:stun.cloudflare.com`. |
| `--turn <url>` | A TURN server, as `url;username;password`. |
| `--bind-port <port>` | The UDP port for the media. The default lets the OS choose. |
| `--ffmpeg-path <dir>` | Where the FFmpeg shared libraries are, if they aren't found on their own. |

## How it works

The page gets the libraries, and the codecs each decodes, from `/libraries`, and
puts its choice in the web socket's query string, `/ws?codec=H264&library=h264.net`.
For each connection the program offers one receive only video track, and
`RTCPeerConnection.OnVideoFrameReceived` passes the frames it assembles to that
library's `IVideoSink`: `Vp8NetVideoEncoderEndPoint`, `H264NetVideoEndPoint` or
`FFmpegVideoEndPoint`. Each raises BGR pictures, which `VideoWindow` draws.

A browser sends a key frame when the video starts and then only when it's asked
for one, so `VideoReceiver` sends an RTCP PLI when:

- the first video packet arrives, in case the start of the first key frame was
  lost while the connection settled;
- RTP packets are lost;
- a frame decodes to no picture;
- h264.net raises `OnKeyFrameRequired`, which it does when it has concealed
  damage.

It asks at most once a second.

A decoder that throws is replaced with a new one, which the next key frame
starts off. vp8.net needs this: given a damaged key frame, it throws on every
frame after it, key frames included.
