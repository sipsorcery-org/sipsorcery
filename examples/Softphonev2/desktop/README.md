# Softphone v2 – desktop prototype

A minimal Windows softphone that makes or takes **one SIP video call**. It is the first build of
the desktop app, using the media design the video spike
([../spikes/VideoPipeline](../spikes/VideoPipeline/RESULTS.md)) picked: the **relay**.

- **C# (this project)** does SIP with SIPSorcery: the transport, an optional registration, and
  one call (dial, answer, decline, cancel, hang up).
- **The page** (`wwwroot`, shown in WebView2) does all the media: it captures the camera and
  microphone, does echo cancellation, encodes, decodes (in hardware where it can) and renders.
- **`MediaRelay`** joins the two. Once a call connects, it creates a local `RTCPeerConnection` to
  the page that offers the codecs the SIP call negotiated, and passes the media between the two
  without decoding it:
  - audio packet by packet, keeping the timestamps and sequence numbers;
  - video frame by frame;
  - keyframe requests (PLI/FIR) in both directions, plus one request each way when the relay
    comes up, so video starts at once.

## Video and no video

- **The Audio | Video switch beside Call** picks the kind of call. *Audio* leaves video out of
  the INVITE; *Video* offers it, and the button reads "Video call". It starts on Audio, since
  most SIP calls go to phones and an audio offer never trips anything up, then remembers your
  last choice. With `--no-video` it's locked to Audio.
- **Add video**, on an audio call, is where the Camera button would be. It adds our video codecs
  to the SIP session and sends a re-INVITE with `m=video`. If they accept, the relay is rebuilt
  on the same SIP session and the page gets a new offer with video. If they refuse (`m=video 0`,
  an error, or no answer in 8 s), the button becomes a disabled *No video*. Video you add
  starts with your camera on. When the *other* side adds video, it's accepted with your camera
  off, so a request from them never turns your camera on by itself.
- **Answer, camera off** answers a ringing video call without sending your camera.
- **Camera on/off** during a video call turns it on or off with no renegotiation: the page keeps
  the video sendrecv and swaps the track in and out (`replaceTrack`).

The stage then shows one of three things:

| They send video | What you see |
|---|---|
| Yes | Their video. |
| Agreed, but nothing arriving for 2 s | Their initial and "Their camera is off". |
| No video on the call (`m=video` refused or never offered) | An "Audio only" label and two rows of frequency bars: their audio above the centre line, yours below. With reduced motion on, each row is a single level bar. |

The bars also show **one-way audio**, the classic SIP/NAT fault. If no audio packets arrive for
2 s, their row drops to a thin line with "No audio arriving from X". If the far end's RTCP
reports show our audio isn't reaching them (our source isn't listed, or its sequence number has
stopped), your row turns red with "X isn't getting your audio".

There is no settings screen, call history, contacts, DTMF, hold, transfer or device picker yet.

## Running it

```bash
dotnet run -c Release
```

On its own it listens on port 5060 (UDP and TCP), or on a random free port if 5060 is taken (the log says which), and makes direct calls: type
`sip:user@host[:port]` or `user@host` and press Call. To register with a server, copy
`softphone.sample.json` to `softphone.json` next to the exe and fill it in. The password is stored
in plain text for now.

Switches, mostly for testing:

| Switch | What it does |
|---|---|
| `--config <file>` | Use another settings file. |
| `--port <n>` | SIP port (default 5060). |
| `--profile <name>` | Names the WebView2 profile and log file (default `default`). If another running copy already has that profile, the next free `name-2`, `name-3`… is used. |
| `--fake-media` | Chromium's fake camera and microphone (a test pattern and a beep). |
| `--dial <uri>` | Call this address as soon as the window is up. |
| `--auto-answer` | Answer incoming calls after a second. |
| `--video` | Start with the switch on Video, so `--dial` places a video call. |
| `--no-video` | No video at all: the switch is locked to Audio, and video offers or Add video from the other side are turned down. (Will be the per-account "Video calls" setting.) |
| `--camera-off` | Calls and answers start with the camera off. |
| `--add-video <s>` | Press Add video this many seconds into an audio call. |

The log, with every full SIP message, goes to
`%LOCALAPPDATA%\SIPSorcery\Softphonev2\softphone-<profile>.log`. Every 5 seconds during a call it
also records the page's receive and send rates, and the relay's packet and frame counts.

### Two softphones calling each other

```bash
Softphone.exe --port 5080 --profile b --fake-media --auto-answer
```

```bash
Softphone.exe --port 5070 --profile a --fake-media --dial sip:b@127.0.0.1:5080
```

### Against the spike's far end

The far end streams a pre-encoded clip and has no audio. Start it with `--any`, because the
softphone offers its LAN address:

```bash
..\spikes\VideoPipeline\artifacts\bin\FarEnd\Release\FarEnd.exe --media ..\spikes\VideoPipeline\media --any
```

```bash
Softphone.exe --fake-media --port 5070 --dial sip:h264-720p30@127.0.0.1:5090
```

## What's been checked

| Test | Result |
|---|---|
| Softphone to softphone, audio call | Both stages show the audio-only bars and Add video. |
| Softphone to softphone, video call (`--video`) | Audio at 50 pkt/s each way; video at 30 fps each way, VP8. The first stats sample, at 5 s, already shows video. |
| Video call with `--camera-off` on the caller | The caller sees the callee's video at 30 fps and its self view says "Camera off"; the callee sees "Their camera is off". |
| Audio call, caller presses Add video (`--add-video 4`) | re-INVITE accepted; both relays rebuilt with VP8. The callee shows the caller's video at 30 fps; the caller sees "Their camera is off", since the callee accepted with its camera off. |
| Same, but the callee has `--no-video` | The callee answers `m=video 0`; the caller logs "they didn't take video", shows *No video*, and both stay on the audio bars. |
| Softphone to spike far end, H264 720p30 | 30 fps at 1280x720 in, decoded by `D3D11VideoDecoder` (hardware); camera out at 30 fps. |

Not tested yet: the one-way audio warnings (there's no easy way to break one direction yet),
turning the camera on part way through a call, the far end adding video to our audio call
(only SIPSorcery to SIPSorcery so far, where we're both), a real SIP server with registration, a hardware desk phone or another softphone
brand, calls across NAT, or the real camera and microphone in this app (the spike covered the
camera).

## Things learned building it

- **Each end of a SIP call may send a different codec.** In the softphone test, A sent Opus and
  B sent G722, both from the list the two sides had agreed. So the page's connection offers every
  audio codec both SIP ends support, not just the one we send. Otherwise the page drops the
  other side's packets.
- **Keyframe requests need both ends to agree on them, and to get through.** Two library
  fixes came out of this prototype:
  - SIPSorcery's SDP offered no keyframe feedback, only `transport-cc`. With no `nack pli`, the
    browser never asked for a keyframe, and incoming video took about 20 seconds to appear.
    Fixed in #1883, which offers `nack pli` and `ccm fir` on both the SIP side and the page
    offer. The relay used to add these lines to the page offer itself.
  - SIPSorcery dropped any RTCP packet of 12 bytes or less, and a PLI on its own is exactly 12.
    So between two SIPSorcery ends no keyframe request ever arrived, and a page that missed the
    first keyframe never showed video. Browsers send PLIs inside a compound packet, so they
    weren't affected. Fixed in #1884. The relay used to pad each PLI with an SDES packet.
  The PLI's media SSRC is the one learned from received RTP, since SIPSorcery routes feedback
  by it.
- **SIPSorcery hung up calls 30 s in because of the idle video stream.** It ends a call when a
  stream has had no RTP or RTCP for 30 s. Our sessions carry a video stream so video can be
  added, and on an audio call (or after the far end refuses video) nothing ever arrives on it.
  So the callee hung up about 30 s in, and both ends logged "They hung up". The pair tests were
  all shorter than that, so they never caught it. Fixed in #1885: RTCP doesn't start on a stream
  that wasn't negotiated, and a video timeout no longer hangs up a call while audio flows.
- **SIPSorcery makes a video stream for any `m=video` it's offered**, with an inactive local
  track, even when we have no video codecs. So "the call has video" has to mean our local video
  track is active and their video port isn't 0, not just `HasVideo`.
- **Adding video rebuilds the relay.** The page connection is torn down and offered again with
  a video line. The old relay's handlers come off the SIP session first, or both would forward
  media.
- **`requestVideoFrameCallback` never fires on a hidden `<video>`**, so it can't tell whether
  frames are arriving while the stage shows something else. The page uses `framesDecoded` from
  `getStats` instead.
- **The page caps its camera at 1.5 Mbit/s.** Its connection to the host is local, so its own
  bandwidth estimate would grow without limit. The real fix is to pass the far end's RTCP
  feedback through to the page.
