# TranscodeLoop

A smoke and benchmark test of SIPSorcery's WebRTC plumbing and its video codecs, made to be
watched. A browser (WebView2) sends a test card to SIPSorcery, SIPSorcery decodes it, encodes it
again and sends it to a second browser page, and the three pictures are shown side by side:

```
 page A ── leg A ──► RTCPeerConnection A ─► decode ─► BGR ─► encode ─► RTCPeerConnection B ── leg B ──► page B
 (browser encodes)   (recvonly)             vp8.net          vp8.net   (sendonly)             (browser decodes)
                                            h264.net         av1.net
                                            ffmpeg           ffmpeg
```

The browser does what browsers do well (capture, encode, decode, show) and SIPSorcery does
everything between: ICE, DTLS/SRTP, RTP, RTCP feedback, depacketising, decoding, encoding and
packetising. The first version is deliberately a UI, not a headless test: dropped keyframes, a
picture that's frozen or smeared, colours that are off, or audio that stutters are things a person
spots that counters can hide.

## Running it

Windows only (WinForms and WebView2). From this folder:

```
dotnet run -c Release
```

and press **Start**. The managed codecs (vp8.net, av1.net, h264.net) need nothing else. The `ffmpeg` codec
implementations need the FFmpeg 8 shared libraries on the `PATH` (for example
`winget install Gyan.FFmpeg.Shared`); the pickers only offer what was found.

## Codecs

Every codec SIPSorcery can carry is in the pickers: VP8, VP9, H264, H265 and AV1. Each leg's offer
has just the one codec, so the browser has to use it. What does the work in SIPSorcery:

| | Decodes | Encodes |
|---|---|---|
| **vp8.net** (SIPSorcery.VP8, managed) | VP8 | VP8 |
| **av1.net** (SIPSorcery.AV1, managed port of libaom's realtime encoder) | | AV1 |
| **h264.net** (SIPSorcery.H264, managed port of OpenH264; its encoder isn't ported yet) | H264 (Constrained Baseline, as WebRTC sends) | |
| **ffmpeg** (SIPSorceryMedia.FFmpeg) | VP8, VP9, H264, H265, AV1 (dav1d) | VP8 (libvpx), VP9 (libvpx-vp9), H264 (libx264), H265 (libx265), AV1 (SVT-AV1) |

The pickers name FFmpeg's own codec, as found in the FFmpeg build that's loaded.

The browser is the other limit. WebView2 sends and receives VP8, VP9, H264 and AV1, but not H265
over WebRTC: it's built without libwebrtc's H265 code (Chrome isn't), so it can't send H265, and
although it claims it can receive it, the first H265 packet crashes it (see "Found so far"). So
H265 out is greyed for WebView2; `--try-h265` offers it anyway, for a later WebView2 that may have
it. A codec that can't be used on a leg is shown in its picker, greyed, with the reason.

## The screen

Three tiles of the same size:

1. **Source**: page A's test card, as drawn, before the browser encodes it.
2. **Decoded**: SIPSorcery's decoded frames, shown straight from the decoder through a shared
   memory buffer, before they're encoded again. It can be turned off (it costs a copy per frame),
   and is off by default above 1080p (see below).
3. **Output**: what page B decoded from SIPSorcery's stream.

The test card is made to show faults to the eye: colour bars and differently coloured corners
(swapped U and V, mirroring, cropping), a grey ramp and fine checkerboards (banding, blocking,
blur), a sweeping hand and a sliding block (freezes and stutter), a big frame number, and a white
A/V square that flashes as a beep sounds once a second. A barcode strip near the bottom carries
the frame number with a CRC, read back from the decoded frames by C# and from page B's frames by
the page, so a frame lost, repeated or too damaged to read is counted, and so is latency.

Each tile has a stats overlay and, below it, a strip of the last 30 s of events: keyframes,
keyframe requests, NACKs, freezes, missing or unreadable frames, and simulated loss. Hover over a
mark for details; click a picture to zoom all three.

The status line under the header sums it up: "Looks right", or what isn't (a size mismatch, a low
frame rate, latency over half a second, freezes, missing or unreadable frames in the last 10 s). It
is one line, always the same height, so problems coming and going never move the page; hover over
it for the whole text. The frame rate is judged on the frames page B decoded against the frames
page A actually sent, not the rate picked: when the source or the browser's encoder falls short,
the status line and the source tile say so separately rather than blaming SIPSorcery.

**A run passes** once page B has kept up for 10 s in a row with nothing wrong. Until then the badge
counts up ("Keeping up 6 s", filling green) or says "Check"; then it turns solid green, "✓ Passed",
and stays so for the run. Trouble after that still shows, in amber beside it ("but now: ..."). The
first 4 s ("Starting…") aren't held against a run, as the loop warms up then, but anything that
goes wrong later counts for 10 s, so after a glitch a run takes up to 20 s to pass. Stopping
leaves the result up: passed, or didn't pass and the longest stretch it kept up for. Changing a
picker starts a new run, and a new judgement.

What counts against a run is what SIPSorcery did: frames page B lost or got damaged after
SIPSorcery's decoder had read them (its encoder, leg B, page B), its decoder falling behind, and
codec errors. Frames already missing or damaged when SIPSorcery decoded them (not sent by page A,
lost on leg A, or damaged by page A's encoder) are said beside it, "N frames already damaged or
missing when SIPSorcery decoded them", but don't stop a pass. The host tells the page which frame
numbers its decoder never read, and page B's losses are split by those. In relay mode there's no
decoder to split them by, so everything page B loses counts.

## Sizes

320×240 up to 4K (3840×2160). Picking a size also picks bitrates that suit it (1.5 Mbit/s at
640×480, 4 Mbit/s at 1080p, 15 Mbit/s at 4K), which can be changed after. Above 1080p:

- The decoded tile starts off. Each frame shown there is copied into the shared buffer on
  SIPSorcery's decode thread and uploaded to the GPU on the page's main thread (25 MB a frame at
  4K, about 40 ms), which takes the main thread from the test card and from page B's frame
  reading: at 4K page A sends 23 fps instead of 30, and page B's counts miss frames the page
  never saw. Turn it on to look, not to measure.
- H264 is offered at the level the size needs (3.1 up to 720p, 4.0 for 1080p, 5.1 for 4K), and
  SIPSorcery's frame reassembly buffer is enlarged to hold a 4K keyframe.

At 4K the browsers are at their limit, not just SIPSorcery (on a 20-thread desktop, where the
whole loop uses about 6 cores):

- Page A's H264 encoder takes about 70 ms a frame, so it sends about 15 fps and its stats say it's
  limited by CPU. Its VP8 encoder keeps up (20 ms), so to see SIPSorcery's H264 encoder at 4K,
  send VP8 in and H264 out.
- Page B decodes 30 fps but the browser drops some frames after decoding (5 to 10%), latency
  rises to a few hundred ms, and there are occasional freezes.

There's no 8K: in WebView2 the browser couldn't capture and encode it in real time (4 frames
encoded in 15 s), and SIPSorcery's FFmpeg VP8 takes about 100 ms to decode an 8K frame and 180 ms
to encode one, so even a capable source would get 5 fps at best.

### Where missing frames come from

Counts on a tile include everything lost upstream of it, so read them left to right:

- **Source, "not encoded"**: frames the card drew that page A's encoder never sent (the browser
  drops frames when it's busy or its rate control says so).
- **Decoded, "missing"**: frame numbers that never came out of SIPSorcery's decoder: the source's
  plus any lost on leg A or dropped while the decoder waited for a keyframe.
- **Decoded, "skipped"**: decoded frames the encoder had no time for. Up to 3 wait; beyond that
  the oldest is skipped, so a slow encoder shows as skips rather than a growing delay.
- **Decoded, "dropped by encoder"**: frames the encoder took and gave nothing back for: its rate
  control dropped them to hold the bitrate. Only shown when there are some.
- **Output, "missing"**: frame numbers page B's decoder never produced, read from the decoded
  frames themselves (not from what the display showed, which drops frames to fit its refresh
  rate). Frames the page had no time to read are counted as "not read", not as missing; frames
  the browser drops after decoding (at 4K it does) show as "dropped", and as missing.

A frame that can't be read is counted as "unreadable" only, not as missing as well (the frame
number after it used to count it again).

### Simulated trouble

- **Leg A, drop frames before decode**: whole frames thrown away before SIPSorcery's decoder, as if
  lost. The decoder carries on, so the picture breaks until a keyframe repairs it; with "ask page A
  for a keyframe" on, SIPSorcery sends page A a PLI after each drop.
- **Leg B, packet loss**: leg B runs through a small UDP relay (`LossyLink.cs`) that drops video RTP
  packets on their way to page B. Page B is told the relay is SIPSorcery and SIPSorcery is told the
  relay is page B, so ICE, DTLS and all the media go through it. Packets are dropped after
  SIPSorcery sent them, so they're in its resend buffer and page B's NACKs are answered, as on a
  real network: the overlay shows packets lost and packets SIPSorcery resent.
- **Keyframe buttons**: a PLI to page A, or a forced keyframe from SIPSorcery's encoder.

## Things the app does that you should know about

- **Leg A's start bitrate**: SIPSorcery doesn't send REMB or transport-cc feedback, so the
  browser's bandwidth estimate never climbs past where it starts (around 300 kbit/s). Leg A's offer
  is given `x-google-start-bitrate` and `x-google-max-bitrate` so the browser sends at the chosen
  "browser bitrate" from the start.
- **vp8.net encodes sizes that are multiples of 16 only**, so other sizes (640×360, say) are cropped
  to fit and the overlay says so.
- **Relay mode** (Pipeline: relay) skips both codecs: leg A's frames go to leg B as they came. It's
  the baseline, the plumbing alone.
- **A fixed source size**: page A's video track is marked as "detail" content and its sender set
  to maintain resolution, so the browser sends the full size from the first frame instead of
  starting smaller and stepping up (which, with FFmpegVideoEncoder, would hit the size-change
  bug below).
- **The fake camera** runs at 30 fps (`--use-fake-device-for-media-stream=fps=30`): left alone,
  Chromium's runs at 20.
- **Page B's frames are read in a worker.** Reading a frame's barcode means reading it back from
  the GPU, which on the page's main thread holds up the test card's timer: page A then sends
  fewer frames and page B falls behind, which looks like frames lost in SIPSorcery. The worker
  keeps several reads in flight, and latency is measured by matching the frame the `<video>`
  shows to the one read by RTP timestamp, without reading its pixels.
- **Clocks**: latencies across the page and C# use an offset measured with a ping between them.

## Scripted runs

The same app runs a test and exits, for comparing runs or a future CI job:

```
TranscodeLoop.exe --exit-after 15 --stats run.json --capture run.png --capture-after 13 --loss-b 3
```

| Switch | |
|---|---|
| `--start` | start straight away |
| `--source card\|fake` | the test card, or Chromium's fake camera |
| `--size 640x480`, `--fps 30` | the source's size (up to `3840x2160`) and frame rate |
| `--mode transcode\|relay` | decode and encode, or pass frames through |
| `--codec-in`, `--codec-out` | `VP8`, `VP9`, `H264`, `H265` (out only, with `--try-h265`) or `AV1`. A codec a leg can't use isn't swapped for another: the run doesn't start, and a scripted one exits with `refused` in its stats |
| `--decoder`, `--encoder` | `vp8.net` or `ffmpeg`; `h264.net` decodes H264 and `av1.net` encodes AV1 |
| `--try-h265` | offer H265 out though WebView2 can't receive it (it crashes WebView2 158) |
| `--browser-kbps`, `--encoder-kbps` | bitrates for page A's encoder and SIPSorcery's (default: picked for the size) |
| `--drop-a <percent>` | leg A frames dropped before decode |
| `--no-drop-pli` | don't ask page A for a keyframe after a dropped frame, so only the decoder's own keyframe requests recover the stream |
| `--loss-b <percent>` | leg B video packets lost |
| `--no-decoded-view`, `--decoded-view` | turn the decoded tile off, or on (it's off by default above 1080p) |
| `--capture <png>`, `--capture-after <s>` | a screenshot this long after both legs connect (default 10 s) |
| `--exit-after <s>` | close this long after both legs connect |
| `--stats <json>` | on closing, write SIPSorcery's counters and the page's, with the verdict (`page.verdict`: passed, after how long, the longest stretch kept up) |

Logs go to `%LOCALAPPDATA%\SIPSorcery\TranscodeLoop`.

## Found so far (November 2026)

What the runs turned up in SIPSorcery. Three are fixed on master; the rest are still open.

- **Fixed (#1937): colours from vp8.net's decoder were off.** `PixelConverter.I420toBGR` (and
  `BGRtoI420`) used the analogue YUV equations, with full-range luma and `2.302` where `2.032` was
  meant, instead of BT.601 limited-range YCbCr. The card's white (192) came out at 181 and its blue
  bar too bright, where the FFmpeg decoder's colours matched the browser's. All seven of
  PixelConverter's RGB/YUV converters now use BT.601 limited range.
- **Fixed (#1936): FFmpegVideoEncoder didn't follow a change of size.** When the frame size
  changed it set the encoder context's width and height but didn't reinitialise the encoder, so
  after page A started at 640×360 and moved to 1280×720, page B got the top-left 640×360 of each
  frame (and at 1080p, the top-left 960×540). It now reopens the encoder at the new size. The app
  keeps page A at one size anyway, so it never showed this after the first runs.
- **Fixed (#1938): libvpx-vp9 held 24 frames before its first output.** FFmpegVideoEncoder didn't
  set `lag-in-frames` for libvpx-vp9, so libvpx's default of 25 applied: page B showed "dropped by
  encoder 24" and every frame arrived about 800 ms late (860 ms at 720p and 1080p, 1.1 s at 4K). It
  now sets it to 0, and VP8 to VP9 at 720p has 59 ms latency and passes.
- **No bandwidth estimation feedback.** SIPSorcery sends no REMB or transport-cc, so a browser
  sending to it stays at its start bitrate (worked around above).
- **vp8.net at 720p.** Encoding takes 25 ms a frame (p95 29 ms), so at 30 fps about 1 frame in 8 is
  skipped, and its per-frame allocations cause around 2 gen-2 garbage collections a second. It
  also has no rate control yet: its output is about twice the browser's bitrate for the same
  picture. At 640×480 it keeps up; at 1080p it manages 14 fps (encode 70 ms).
- **FFmpeg at 1080p and 4K.** 1080p keeps up with VP8 and H264 (VP8 decode 4 ms, encode 15 ms;
  H264 decode 3 ms, encode 7 ms). At 4K, H264 decoding takes 14 to 21 ms and encoding about 32 ms
  (p95 35 to 40 ms), right at the 33 ms a frame lasts at 30 fps, so 1 to 4% of frames are skipped.
  VP8 doesn't keep up at 4K: decoding takes 24 ms and encoding about 50 ms, so about 1 frame in 3
  is skipped.
- **The other codecs, timed (November 2026, 30 fps).** Encode time per frame, p50 / p95 in ms
  (what page A sent made little difference, so ranges cover runs with different codecs in):

  | Encoder | 720p | 1080p | 4K |
  |---|---|---|---|
  | av1.net (AV1) | 2.5–2.9 / 3.5–3.9 | 4.7 / 6.5 | 16.7 / 23.5 |
  | ffmpeg libx264 (H264) | 3.4 / 4.2 | 7.0 / 8.3 | 32–33 / 37–39 |
  | ffmpeg libvpx-vp9 (VP9) | 4.4 / 9.6 | 8.8 / 11.5 | 33 / 55 |
  | ffmpeg libsvtav1 (AV1) | 7.8–8.1 / 12–13 | 17 / 24 | 72 / 104 |

  av1.net's output is much smaller than the FFmpeg encoders' (0.6 Mbps at 720p and 1 Mbps at
  1080p, where SVT-AV1 sends 2.5 and 5.2 Mbps), so this isn't like for like. FFmpeg's decoders take
  1.3 to 1.6 ms a frame at 720p and 2.7 to 3.1 ms at 1080p for all four codecs; at 4K, AV1
  (libdav1d) takes 12.5 ms, VP9 19 ms and VP8 19 to 21 ms. Runs that passed: VP8 to H264 at 720p,
  VP8 to AV1 with av1.net at 1080p, VP9 or AV1 to H264 at 1080p, and (since #1938) VP8 to VP9 at
  720p. Nothing passes at 4K: H264
  encoding is right at the 33 ms limit (as above), and with av1.net the VP8 decoder slows to 27 ms
  (p95 39 ms) while av1.net encodes alongside it, so about 1 frame in 5 is skipped.
- **H265 out crashes WebView2: Edge's bug, not SIPSorcery's.** Edge and WebView2 (157 and 158)
  are built without libwebrtc's H265 support, yet with `WebRtcAllowH265Receive` on they agree to
  receive H265 with no depacketiser behind it, so the first H265 RTP packet crashes the renderer
  (access violation, a call through a null pointer in `RtpVideoStreamReceiver2::ReceivePacket`).
  One packet holding a VPS is enough. Chrome 158, on the same Chromium, decodes SIPSorcery's H265
  cleanly, and SIPSorcery's RTP passes an independent RFC 7798 check. The same missing code is why
  WebView2 can't send H265. H265 out is now greyed for WebView2; if a run crashes the renderer
  anyway, the app stops it, reloads the page and shows what happened.
- **Chromium's VP9 and AV1 encoders leave blocks stale.** At 720p about 1 in 3 of page A's VP9
  frames, and 1 in 8 to 1 in 12 of its AV1 frames, have one or more 64×64 blocks over the strip
  still showing an earlier frame (the CRC cells hold the previous frame's values). SIPSorcery's
  decoder sees the same, and so does page B in relay mode (157 VP9 and 55 AV1 frames in 10 s), so
  it's in page A's bitstream rather than anything SIPSorcery does. It didn't show over the strip
  at 1080p. The verdict lists these as already damaged when SIPSorcery decoded them, not as a
  problem, except in relay mode.
- **SVT-AV1's output loses frames on page B.** Page B can't read 9 to 18 frames in 10 s at 720p,
  and about 107 at 1080p, that SIPSorcery's decoder read fine. av1.net's AV1 goes through the same
  packetiser and arrives clean, so it's more likely in what SVT-AV1 encodes (perhaps the same
  stale blocks as above) than in the RTP path. Not yet looked into.
- **h264.net, the OpenH264 port's decoder (h264-port branch).** H264 from page A decodes cleanly:
  no decode errors or unreadable frames in 25 s runs at 640×480, 720p and 1080p. It takes 1.3 ms
  a frame at 640×480, about 4 ms at 720p and 8.1 to 8.6 ms at 1080p (p95 under 10 ms), about 3
  times FFmpeg's decoder (1.3 ms at 720p, 3 ms at 1080p). With 3% of frames dropped before decode
  and `--no-drop-pli`, its `KeyFrameRequired` asks page A for a keyframe after each drop and the
  stream recovers within a few frames (88 unreadable in 25 s). FFmpeg's decoder gives no such
  signal: 704 of its 744 frames stayed unreadable. At 1080p, page B drops about 14 frames once,
  about 2 s into each h264.net run, and asks for a keyframe; runs with FFmpeg's decoder don't. It
  looks like a startup race between page B getting ready and the first keyframe, but hasn't been
  looked into.
- **av1.net skips its first second.** About 26 frames are skipped at the start of each run before
  its first frame comes out, then it keeps up. It sends one keyframe, at the start, where the
  FFmpeg encoders send one a second. At 720p page B can't read 1 or 2 of its frames in some runs.
