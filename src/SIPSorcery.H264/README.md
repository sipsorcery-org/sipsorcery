# SIPSorcery.H264

A **pure C# H.264 video codec** for the
[SIPSorcery](https://www.nuget.org/packages/SIPSorcery) real-time
communications library, ported from Cisco's
[OpenH264](https://github.com/cisco/openh264). No native binaries, no PInvoke.

## Status

**The decoder and the encoder work. Not published yet.** The port was built
as a series of small PRs, each one checked bit-exact against a C-only build of
OpenH264 (see
[`thirdparty/openh264-dotnet-port`](https://github.com/sipsorcery-org/sipsorcery/tree/master/thirdparty/openh264-dotnet-port)).

| Path | State |
|---|---|
| **Decoder** (Constrained Baseline) | Done and wired into SIPSorcery. All 35 Constrained Baseline conformance vectors decode to OpenH264's own SHA1s, fed whole or one access unit at a time, and also after an RTP round trip through SIPSorcery's `H264Depacketiser`. Damaged copies of them decode as the C decoder does, error concealment and decoder resets included (all but one copy, whose damage turns a slice into a B slice, which is not ported). It decodes a browser's live H.264 stream, and asks for a key frame when packets are lost. CABAC and B slices (Main and High profile) are not ported. |
| **Encoder** (Constrained Baseline, WebRTC realtime) | Done and wired into SIPSorcery, for camera content in one spatial layer on one thread. The whole encoder API is ported (`InitializeExt`, `EncodeFrame`, `ForceIntraFrame`, every `SetOption` and `GetOption`), with parameter validation, SPS/PPS writing, I and P slice coding (CAVLC), long-term references, rate control (the quality, bit rate, buffer based and timestamp modes, with frame skipping and padding) and the camera preprocessing (scene change and background detection, adaptive quantisation, the denoiser and downsampling). It reproduces OpenH264's own encoder test hashes for camera content with one slice and one layer, and matches the C encoder byte for byte, frame by frame, over 66 parameter set configurations, 57 fixed QP streams, 94 rate controlled streams, 63 preprocessed streams and 61 scripts of API calls. Chromium decodes its stream live. Screen content, CABAC, several spatial layers and threads are refused at initialisation. |

The target throughout is the profile WebRTC uses, Constrained Baseline
(`profile-level-id=42e01f`). Other profiles come later, and only if needed.

## Using the decoder

`H264NetVideoEndPoint` is a SIPSorcery video sink. Give it the access units
an `RTCPeerConnection` assembles and it raises BGR pictures:

```csharp
var videoSink = new H264NetVideoEndPoint();
videoSink.OnVideoSinkDecodedSample += (bgr, width, height, stride, format) => { /* display */ };

var pc = new RTCPeerConnection();
pc.addTrack(new MediaStreamTrack(videoSink.GetVideoSinkFormats(), MediaStreamStatusEnum.RecvOnly));
pc.OnVideoFrameReceived += videoSink.GotVideoFrame;
pc.OnVideoFormatsNegotiated += formats => videoSink.SetVideoSinkFormat(formats.First());
```

Handle `OnKeyFrameRequired` by sending the sender an RTCP PLI. The decoder
raises it when it starts mid-stream or data is lost, and browsers send a key
frame only when asked. The
[`WebRTCClientH264Net`](https://github.com/sipsorcery-org/sipsorcery/tree/master/examples/WebRTCExamples/WebRTCClientH264Net)
example does all of this.

`H264Codec` is the `IVideoEncoder` underneath, for decoding without an
endpoint. `DecodeVideo` takes one Annex B access unit and returns the picture
as I420, BGR or RGB. `DecodeVideoFaster` returns BGR or RGB in a buffer the
codec re-uses. BGR and RGB follow the colour matrix and range the stream
signals (BT.601 or BT.709, limited or full); a stream that signals nothing is
BT.601 limited range.

## Using the encoder

`H264Codec` is also the encoder, so it can go into any SIPSorcery video source
that takes an `IVideoEncoder`:

```csharp
var h264Codec = new H264Codec { TargetBitrateKbps = 1000 };
var testPattern = new VideoTestPatternSource(h264Codec);

var pc = new RTCPeerConnection();
pc.addTrack(new MediaStreamTrack(testPattern.GetVideoSourceFormats(), MediaStreamStatusEnum.SendOnly));
testPattern.OnVideoSourceEncodedSample += pc.SendVideo;
pc.OnVideoFormatsNegotiated += formats => testPattern.SetVideoSourceFormat(formats.First());
```

It encodes I420, NV12, BGRA, RGBA, BGR and RGB frames (the RGB formats are
converted as BT.601 limited range) to one Annex B access unit a frame. Every
key frame starts with the SPS and PPS, so a receiver can join at any of them.
It is set up as libwebrtc sets up OpenH264: bitrate mode rate control that
may drop a frame to hold the bitrate, one slice, one thread. Set
`TargetBitrateKbps`, `FrameRate` and `KeyframeIntervalFrames` at any time; with
no bitrate set it picks one from the frame size and rate. A new frame size
starts a new stream with a key frame. The SDP offers `profile-level-id=42e01f`.

SIPSorcery doesn't answer a receiver's key frame request on its own: call
`ForceKeyFrame` when `RTCPeerConnection.OnReceiveReport` brings an RTCP PLI or
FIR. The
[`WebRTCGetStartedCodecs`](https://github.com/sipsorcery-org/sipsorcery/tree/master/examples/WebRTCExamples/WebRTCGetStartedCodecs)
example does all of this when run with `--library h264.net`. The CLI, built from source, takes it as
`--encoder h264.net`.

The port runs OpenH264's portable C code, without its assembly. On one desktop
thread it encodes about 65 frames a second at 640x480, 23 at 1280x720 and 12
at 1920x1080, so it keeps up in real time up to about 640x480 at 30 frames a
second.

## Port conventions

- **File for file.** Each `.cs` file ports one OpenH264 source file and keeps its
  name, and the ported code keeps OpenH264's function and type names, so it
  can be read side by side with the C++.
- **Native memory.** Codec state is unmanaged structs in native memory, reached
  through pointers as upstream does, so the GC never moves or scans it.
- **Plain C kernels only.** The C# ports the `_c` reference kernels behind
  OpenH264's function tables. The tables are kept so SIMD versions can be added
  later.

## License

BSD 3-Clause License, see [LICENSE](https://github.com/sipsorcery-org/sipsorcery/blob/master/LICENSE.md).
The ported OpenH264 code is also covered by Cisco's BSD 2-Clause license,
included in the package as `OPENH264_LICENSE`.

H.264 is covered by patents. Cisco pays the royalties only for the OpenH264
binaries that Cisco itself distributes, so this package is **not** covered by
that arrangement.
