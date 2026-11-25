# SIPSorcery.H264

A **pure C# H.264 video codec** for the
[SIPSorcery](https://www.nuget.org/packages/SIPSorcery) real-time
communications library, ported from Cisco's
[OpenH264](https://github.com/cisco/openh264). No native binaries, no PInvoke.

## Status

**The decoder works; the encoder is not started. Not published yet.** The port
is being built as a series of small PRs, each one checked bit-exact against a
C-only build of OpenH264 (see
[`thirdparty/openh264-dotnet-port`](https://github.com/sipsorcery-org/sipsorcery/tree/master/thirdparty/openh264-dotnet-port)).

| Path | State |
|---|---|
| **Decoder** (Constrained Baseline) | Done and wired into SIPSorcery. All 35 Constrained Baseline conformance vectors decode to OpenH264's own SHA1s, fed whole or one access unit at a time, and also after an RTP round trip through SIPSorcery's `H264Depacketiser`. Damaged copies of them decode as the C decoder does, error concealment and decoder resets included (all but one copy, whose damage turns a slice into a B slice, which is not ported). It decodes a browser's live H.264 stream, and asks for a key frame when packets are lost. CABAC and B slices (Main and High profile) are not ported. |
| **Encoder** (Constrained Baseline, WebRTC realtime) | Not started; follows the decoder. |

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
BT.601 limited range. Its encode methods throw `NotSupportedException` until
the encoder is ported.

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
