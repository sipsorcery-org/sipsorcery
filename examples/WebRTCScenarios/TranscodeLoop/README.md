# TranscodeLoop

A smoke and benchmark test of SIPSorcery's WebRTC plumbing and its video codecs, made to be
watched. A browser (WebView2) sends a test card to SIPSorcery, SIPSorcery decodes it, encodes it
again and sends it to a second browser page, and the three pictures are shown side by side:

```
 page A ── leg A ──► RTCPeerConnection A ─► decode ─► BGR ─► encode ─► RTCPeerConnection B ── leg B ──► page B
 (browser encodes)    (recvonly)          vp8.net | ffmpeg   vp8.net | ffmpeg   (sendonly)            (browser decodes)
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

and press **Start**. The vp8.net codec needs nothing else. The `ffmpeg` codec implementations need
the FFmpeg 8 shared libraries on the `PATH` (for example `winget install Gyan.FFmpeg.Shared`); the
pickers only offer what was found.

## The screen

Three tiles of the same size:

1. **Source**: page A's test card, as drawn, before the browser encodes it.
2. **Decoded**: SIPSorcery's decoded frames, shown straight from the decoder through a shared
   memory buffer, before they're encoded again. It can be turned off (it costs a copy per frame).
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

The header's check sums it up ("Looks right", or what isn't: a size mismatch, a low frame rate,
latency over half a second, freezes, missing or unreadable frames in the last 10 s).

### Where missing frames come from

Counts on a tile include everything lost upstream of it, so read them left to right:

- **Source, "not encoded"**: frames the card drew that page A's encoder never sent (the browser
  drops frames when it's busy or its rate control says so).
- **Decoded, "missing"**: frame numbers that never came out of SIPSorcery's decoder: the source's
  plus any lost on leg A or dropped while the decoder waited for a keyframe.
- **Decoded, "skipped"**: decoded frames the encoder had no time for. Up to 3 wait; beyond that
  the oldest is skipped, so a slow encoder shows as skips rather than a growing delay.
- **Output, "missing"**: frame numbers page B's decoder never produced, read from the decoded
  frames themselves (not from what the display showed, which drops frames to fit its refresh
  rate).

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
| `--size 640x480`, `--fps 30` | the source's size and frame rate |
| `--mode transcode\|relay` | decode and encode, or pass frames through |
| `--codec-in`, `--codec-out` | `VP8` or `H264` |
| `--decoder`, `--encoder` | `vp8.net` or `ffmpeg` |
| `--browser-kbps`, `--encoder-kbps` | bitrates for page A's encoder and SIPSorcery's |
| `--drop-a <percent>` | leg A frames dropped before decode |
| `--loss-b <percent>` | leg B video packets lost |
| `--no-decoded-view` | don't show the decoded tile |
| `--capture <png>`, `--capture-after <s>` | a screenshot this long after both legs connect (default 10 s) |
| `--exit-after <s>` | close this long after both legs connect |
| `--stats <json>` | on closing, write SIPSorcery's counters and the page's |

Logs go to `%LOCALAPPDATA%\SIPSorcery\TranscodeLoop`.

## Found so far (November 2026)

What the first runs turned up in SIPSorcery, to be looked at separately:

- **Colours from vp8.net's decoder are off.** `PixelConverter.I420toBGR` (and `BGRtoI420`) use the
  analogue YUV equations, with full-range luma and `2.302` where `2.032` was meant, instead of
  BT.601 limited-range YCbCr. The card's white (192) comes out at 181 and its blue bar too bright;
  the FFmpeg decoder's colours match the browser's.
- **FFmpegVideoEncoder doesn't follow a change of size.** When the frame size changes it sets the
  encoder context's width and height but doesn't reinitialise the encoder, so after page A starts
  at 640×360 and moves to 1280×720, page B gets the top-left 640×360 of each frame.
- **No bandwidth estimation feedback.** SIPSorcery sends no REMB or transport-cc, so a browser
  sending to it stays at its start bitrate (worked around above).
- **vp8.net at 720p.** Encoding takes 25 ms a frame (p95 29 ms), so at 30 fps about 1 frame in 8 is
  skipped, and its per-frame allocations cause around 2 gen-2 garbage collections a second. It
  also has no rate control yet: its output is about twice the browser's bitrate for the same
  picture. At 640×480 it keeps up.
