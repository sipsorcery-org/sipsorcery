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
| **Encoder** (Constrained Baseline, WebRTC realtime) | Done and wired into SIPSorcery, for camera content in one spatial layer, on one thread or on up to four slice threads. The whole encoder API is ported (`InitializeExt`, `EncodeFrame`, `ForceIntraFrame`, every `SetOption` and `GetOption`), with parameter validation, SPS/PPS writing, I and P slice coding (CAVLC), long-term references, rate control (the quality, bit rate, buffer based and timestamp modes, with frame skipping and padding), slice threads with OpenH264's load balancing, and the camera preprocessing (scene change and background detection, adaptive quantisation, the denoiser and downsampling). It reproduces OpenH264's own encoder test hashes for camera content in one layer, one of them on two slice threads, and matches the C encoder byte for byte, frame by frame, over 66 parameter set configurations, 57 fixed QP streams, 94 rate controlled streams, 63 preprocessed streams and 84 scripts of API calls, 23 of them threaded. Chromium decodes its stream live. Screen content, CABAC, several spatial layers and size-limited slices on more than one thread are refused at initialisation. |

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
starts a new stream with a key frame. `EncoderThreads`, from 1 to 4, codes each
frame as that many slices, one a thread, for large frames; it compresses a
little less well, and a change also starts a new stream. The SDP offers `profile-level-id=42e01f`.

SIPSorcery doesn't answer a receiver's key frame request on its own: call
`ForceKeyFrame` when `RTCPeerConnection.OnReceiveReport` brings an RTCP PLI or
FIR. The
[`WebRTCGetStartedCodecs`](https://github.com/sipsorcery-org/sipsorcery/tree/master/examples/WebRTCExamples/WebRTCGetStartedCodecs)
example does all of this when H264 from h264.net is picked on its page. The CLI, built from source, takes it as
`--encoder h264.net`.

The port runs OpenH264's portable C code, without its assembly. In its place
are SIMD kernels written with `System.Runtime.Intrinsics`, for x86 and x64
with SSE2 and for Arm64. So far they cover:
- SAD and SATD, where motion estimation spends most of its time.
- Motion compensation, which was half of decoding.
- The loop filter, which was then half of decoding again.
- The transforms and quantisation, which were then a third of encoding.
- The encoder's preprocessing statistics and the 16x16 intra predictors,
  which were then over a quarter of encoding.

On one desktop thread it encodes about 380 frames a second at 640x480, 140 at
1280x720 and 60 at 1920x1080 (the CLI's `video-bench`). It decodes about 280 a
second at 1280x720 (`H264Benchmarks`). So at 30 frames a second it keeps up in
real time at 1280x720 with room to spare, and at 1920x1080 with some. On four
threads (`EncoderThreads = 4`) it encodes `H264Benchmarks`' harder content 2 to
2.4 times as fast: about 730 frames a second at 640x480, 270 at 1280x720 and
120 at 1920x1080, from 370, 120 and 50 on one.

## Port conventions

- **File for file.** Each `.cs` file ports one OpenH264 source file and keeps its
  name, and the ported code keeps OpenH264's function and type names, so it
  can be read side by side with the C++.
- **Native memory.** Codec state is unmanaged structs in native memory, reached
  through pointers as upstream does, so the GC never moves or scans it.
- **C kernels, and SIMD ones beside them.** The C# ports the `_c` reference
  kernels behind OpenH264's function tables. SIMD versions go in the same
  slots where the machine has the instructions for them. Each is checked to
  give its C kernel's results, so the output is the same either way.

## License

BSD 3-Clause License, see [LICENSE](https://github.com/sipsorcery-org/sipsorcery/blob/master/LICENSE.md).
The ported OpenH264 code is also covered by Cisco's BSD 2-Clause license,
included in the package as `OPENH264_LICENSE`.

H.264 is covered by patents. Cisco pays the royalties only for the OpenH264
binaries that Cisco itself distributes, so this package is **not** covered by
that arrangement.
