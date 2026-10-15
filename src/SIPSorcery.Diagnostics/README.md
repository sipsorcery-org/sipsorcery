# SIPSorcery.Diagnostics

SIP and WebRTC **diagnostics and benchmarking** from the command line, built on the
[SIPSorcery](https://github.com/sipsorcery-org/sipsorcery) library. This is probe/test/benchmark
tooling (connectivity checks, test calls, echo-test peers, pipeline benchmarks) — not a general
softphone. It is human and agent friendly: every verb supports `--json` and meaningful exit codes.

## Install

```bash
dotnet tool install -g SIPSorcery.Diagnostics --prerelease
```

The installed command is **`sipsorcery-diags`** (the bare `sipsorcery` name is reserved for a
possible future interactive tool):

```bash
sipsorcery-diags sip options music@iptel.org
```

## Usage

```bash
# SIP ping: send an OPTIONS request and report the response.
sipsorcery-diags sip options music@iptel.org
sipsorcery-diags sip options tcp:sip.example.com:5060
sipsorcery-diags sip options sips:secure.example.com -t 10 -v

# SIP call: place a call, send a test audio source and report on the media received.
# No audio devices are used; received audio renders via ffplay, a WAV file, or raw PCM
# on stdout. ffmpeg/ffplay act as the cross platform audio device layer
# (winget/brew/apt install ffmpeg).
sipsorcery-diags sip call music@iptel.org --audio play           # listen via ffplay
sipsorcery-diags sip call music@iptel.org --scope                # live spectrum + level in the terminal
sipsorcery-diags sip call music@iptel.org --audio play --scope   # listen AND watch: --scope renders on
                                                           # stderr so it composes with any --audio
sipsorcery-diags sip call music@iptel.org --audio rx.wav -d 10   # record 10s to a WAV file
sipsorcery-diags sip call music@iptel.org --audio - > rx.pcm     # raw s16le PCM on stdout
                                                           # (the result moves to stderr)
sipsorcery-diags sip call 100@pbx.example.com -u user --password pass --play tone --send-dtmf 123

# SIP register: register an account with a registrar and report the result.
sipsorcery-diags sip register sipsorcery.com -u myuser --password mypass
sipsorcery-diags sip register tls:sip.example.com -u myuser --password mypass -d 30  # hold 30s then remove
sipsorcery-diags sip register sipsorcery.com -u myuser --password mypass --keep      # leave registered
sipsorcery-diags sip register sipsorcery.com -u myuser --digest-store myuser.digest

# SIP digest: create a digest store file for passwordless registration.
sipsorcery-diags sip digest --username myuser --realm sipsorcery.com --password mypass --out myuser.digest

# SIP load: generate concurrent OPTIONS load and report aggregate timing/success stats.
sipsorcery-diags sip load myserver.org -c 1000 -x 25            # 1000 requests, 25 in flight at once
sipsorcery-diags sip load myserver.org -c 100 -x 10 -p 1        # each worker waits 1s between requests
sipsorcery-diags sip load myserver.org -c 100 -x 10 --break-on-fail --hep 192.168.0.10

# The SIP verbs can mirror their traffic to a HEPv3 capture server (HOMER, heplify-server,
# sipcapture.org) so the exchange shows up as a call ladder diagram:
sipsorcery-diags sip options music@iptel.org --hep "127.0.0.1:9060"
sipsorcery-diags sip call music@iptel.org --hep "192.168.0.10:9060;myHep;42"   # host:port;password;agentId

# STUN lookup: report this machine's public IP address and port.
sipsorcery-diags stun lookup stun.cloudflare.com
sipsorcery-diags stun lookup stun:stun.l.google.com:19302

# ICE gather: gather candidates and verify STUN/TURN connectivity.
sipsorcery-diags ice gather
sipsorcery-diags ice gather --stun stun:stun.cloudflare.com
sipsorcery-diags ice gather --turn "turn:turn.example.com;user;pass" --relay-only
sipsorcery-diags ice gather --key-id <key-id> --token <api-token> --relay-only   # Cloudflare TURN health check

# TURN allocate: check a single TURN server can allocate a relay socket and report the relay address.
# A focused, relay-only credential/health check (use "ice gather" to see all candidate types at once).
sipsorcery-diags turn allocate turn:turn.example.com:3478 -u user -p pass
sipsorcery-diags turn allocate "turn:turn.example.com:3478?transport=tcp" -u user -p pass
sipsorcery-diags turn allocate turns:turn.example.com:5349 -u user -p pass -t 12
# Or check Cloudflare TURN: omit the url and pass (or set CLOUDFLARE_TURN_KEY_ID/CLOUDFLARE_API_TOKEN)
# the key ID and token; short lived credentials are fetched and that relay is allocated.
sipsorcery-diags turn allocate --key-id <key-id> --token <api-token>
sipsorcery-diags turn allocate --transport udp        # turn:3478?transport=udp instead of turns:443

# WebRTC WHEP: Video sink, full connection (ICE, DTLS, SRTP) to a WHEP endpoint, verifies media arrives.
# Publish to the same stream key first to get media flowing. FFmpeg can publish to
# Broadcast Box (https://b.siobud.com/) using:
ffmpeg `
  -re `
  -f lavfi -i testsrc=size=1280x720 `
  -f lavfi -i sine=frequency=440 `
  -pix_fmt yuv420p -vcodec libx264 -profile:v baseline -r 25 -g 50 `
  -acodec libopus -ar 48000 -ac 2 `
  -f whip -authorization "mystreamkey" `
  "https://b.siobud.com/api/whip"
sipsorcery-diags webrtc whep https://b.siobud.com/api/whep --token mystreamkey

# Received video can be rendered or captured (decode is delegated to the consumer, so no
# video codecs are needed in-process). H264 is written as Annex B, VP8 in an IVF container.
sipsorcery-diags webrtc whep https://b.siobud.com/api/whep --token key --video play       # ffplay window
sipsorcery-diags webrtc whep https://b.siobud.com/api/whep --token key --video rx.h264    # capture to file
sipsorcery-diags webrtc whep https://b.siobud.com/api/whep --token key `
 --video - | mpv --vo=tct - # bitstream on stdout: video IN the terminal (the result moves to stderr)
# mpv is a media player with terminal renderers (https://mpv.io/installation/):
#   winget install mpv          (Windows)
#   brew install mpv            (macOS)
#   sudo apt install mpv        (Debian/Ubuntu)
# Windows PowerShell 5.1 corrupts binary data in pipes; run pipelines like the above
# under cmd or PowerShell 7.4+. If mpv does not detect the format from a pipe, add
# --demuxer-lavf-format=h264 (or ivf for VP8).
# --decode (like whip-server) decodes in-process with the SIPSorcery FFmpeg decoder and sends raw
# RGB to the sink instead of the encoded bitstream. The result JSON includes videoFps either way.
sipsorcery-diags webrtc whep https://b.siobud.com/api/whep --token key --video play --decode

# WebRTC WHIP server: accept a publish directly from ffmpeg/OBS and report on the media,
# including sequence anomalies. Useful for isolating where stream problems originate.
sipsorcery-diags webrtc whip-server --listen http://localhost:8080/whip --token test -d 10
ffmpeg `
  -re `
  -f lavfi -i testsrc=size=640x360 `
  -f lavfi -i sine=frequency=440 `
  -pix_fmt yuv420p -c:v libx264 -profile:v baseline -r 25 -g 50 `
  -c:a libopus -ar 48000 -ac 2 `
  -f whip -authorization "test" `
  "http://localhost:8080/whip"

# WebRTC loopback: a self-contained encode -> network -> decode loop in ONE process. It runs the same
# receive engine as whip-server and, in-process, publishes a generated test pattern to it with the
# SIPSorcery library (the same publisher as "webrtc whip"), so no second terminal and no startup race.
# Resolution presets are 360p/480p/720p/1080p/1440p/4k (or --size WxH), with --encoder (vp8.net or
# ffmpeg), --fps, --codec (ffmpeg: h264, h265, vp8, vp9 or av1), --bitrate and --video. The result JSON reports both
# the send side (publishedFps) and the receive side (videoFps), so it doubles as a quick throughput check.
# videoEncode/videoDecode flag whether an encoder/decoder actually ran in-process for the test
# (videoEncode is false when --pre-encode replays a bitstream; videoDecode follows --decode).
sipsorcery-diags webrtc loopback --preset 1080p -d 10                 # 1080p30 loop, measure send + receive fps
sipsorcery-diags webrtc loopback --preset 720p --video play -d 30     # publish + view the received stream
# By default received frames are passed straight to the sink (ffplay decodes them). Add --decode to
# instead decode in-process and send raw RGB to the sink, so the picture goes through the library's
# decode path. --decoder selects ffmpeg (default, any codec; needs the FFmpeg libraries) or vp8.net
# (managed Vpx.Net, VP8 only -- and only reliable on vp8.net-encoded VP8; it can crash on FFmpeg's VP8).
sipsorcery-diags webrtc loopback --video play --decode -d 30          # library-decoded (ffmpeg), rendered raw
sipsorcery-diags webrtc loopback --video frames.rgb --decode -d 30    # capture raw rgb24 to a file
sipsorcery-diags webrtc loopback --encoder vp8.net --codec vp8 --decode --decoder vp8.net --video null -d 10  # managed VP8 round-trip
sipsorcery-diags webrtc loopback --encoder ffmpeg --codec av1 --preset 720p --decode --video null -d 10       # AV1 (or h265/vp9) library round-trip
# To measure the DECODE stage on its own, add --pre-encode N: it encodes N frames once before
# connecting, then replays that bitstream, so no encoding runs during the window (the encoder is out
# of the hot loop and not competing for CPU). Pair it with --decode --video null for headless decode.
sipsorcery-diags webrtc loopback --preset 1080p --fps 120 --pre-encode 300 --decode --video null -d 10
# --max-rate sends flat out (ignoring --fps). With --pre-encode and no --decode it gives the pure
# transport ceiling (packetise -> SRTP -> socket -> depacketise, no codec): the pipeline's theoretical max.
sipsorcery-diags webrtc loopback --preset 1080p --pre-encode 300 --max-rate -d 10
# WebRTC WHIP publish (library sender): publish a generated test pattern to a WHIP endpoint using the
# SIPSorcery stack itself -- the full SEND pipeline (generate -> encode -> RTP/SRTP -> ICE/DTLS). It
# is the counterpart to "video-bench", which measures the encoder but stops before the network.
# --encoder vp8.net (managed Vpx.Net VP8, no native deps) or ffmpeg (--codec h264/h265/vp8/vp9/av1); presets/--size,
# --fps, --bitrate and --max-rate (flat out, local receiver only). Reports frames sent, achieved vs
# target fps and encode ms/frame, so e.g. vp8.net's ceiling vs ffmpeg H264 at 720p is obvious.
sipsorcery-diags webrtc whip http://localhost:8080/whip --preset 720p --fps 30 --encoder ffmpeg
sipsorcery-diags webrtc whip https://b.siobud.com/api/whip --token key --preset 1080p --encoder ffmpeg
# To exercise an ALL-LIBRARY path (no ffmpeg muxer, both ends are SIPSorcery), point it at a
# "webrtc whip-server" running as the ingest in another terminal, or use "webrtc loopback" (above) to
# run the publisher and receiver in a single process.

# WebRTC echo test (https://github.com/sipsorcery/webrtc-echoes): the echo-server answers offers
# and echoes RTP and data channel messages; the echo client verifies the data channel round trips.
# The two pair for a self-contained interop test:
sipsorcery-diags webrtc echo-server --listen http://localhost:8080/    # run the echo server (ctrl-c to stop)
sipsorcery-diags webrtc echo http://localhost:8080/offer               # echo client against any echo server
sipsorcery-diags webrtc echo http://localhost:8080/offer --stun "turn:turn.example.com;user;pass" --relay-only

# WebRTC video bench: measure the video SEND pipeline (no peer connection, DTLS or socket) to
# answer "can this machine sustain a target resolution and frame rate", default 1080p30. The
# pipeline is measured in stages via --encoder so the bottleneck can be isolated: none packetises
# a target-sized frame flat out (the RTP packetisation ceiling), vp8.net adds the managed Vpx.Net
# codec, and ffmpeg/ffmpeg-piped add the native FFmpeg encoder in-process or via an external
# ffmpeg process. --codec selects vp8, vp9, h264, h265 or av1: the in-process ffmpeg stage does all
# five, vp8.net is VP8 only and ffmpeg-piped does vp8/vp9 (IVF). Exits 0 if the target fps is met, 1 if below.
sipsorcery-diags webrtc video-bench                                      # 1080p30, packetise only
sipsorcery-diags webrtc video-bench --encoder vp8.net --fps 30           # managed VP8 codec
sipsorcery-diags webrtc video-bench --encoder ffmpeg --codec h265 --width 1280 --height 720 --fps 60
sipsorcery-diags webrtc video-bench --encoder ffmpeg --codec vp9 --preset 4k --fps 30
sipsorcery-diags webrtc video-bench --all --preset 1080p                 # benchmark vp8/vp9/h265/av1 in one run
sipsorcery-diags webrtc video-bench --encoder ffmpeg-piped --cpu-used 8 --threads 4 -d 10
# libvpx tuning (--deadline, --cpu-used, --threads) applies to the VP8/VP9 ffmpeg stages; for H264/H265
# the encoder uses its own realtime defaults. AV1 uses the SVT-AV1 encoder by default (--av1-encoder
# selects another, e.g. av1_nvenc; --av1-preset tunes its speed). The in-process ffmpeg stage needs the
# FFmpeg shared libraries (winget/brew/apt install ffmpeg, or --ffmpeg-path).

# The cloudflare, livekit, openai and route verbs moved to the sibling "sipsorcery" tool
# (SIPSorcery.Cli), the application/streams CLI. Install it with:
#   dotnet tool install -g SIPSorcery.Cli --prerelease
```

### SIP call ladders in the terminal with sngrep

[`sngrep`](https://github.com/irontec/sngrep) can act as a HEP *receiver* and draw call
ladders right in the terminal, no database or web UI needed. Start it listening, then point
`--hep` at it:

```bash
sngrep -L udp:0.0.0.0:9060
sipsorcery-diags sip call music@iptel.org --hep 127.0.0.1:9060 -d 5
```

The call appears in the sngrep calls list as it happens; arrow keys to select, Enter for the
ladder. This shows only the traffic the CLI itself sends and receives; for ladders of other
processes' SIP run `sngrep` in its normal sniffing mode (`sudo sngrep -d any port 5060`).

sngrep is not available natively on Windows, but it runs well under WSL2. **Note:** WSL2's
default NAT networking only forwards `localhost` for TCP, so HEP (UDP) to `127.0.0.1` is
silently dropped. Either target the WSL IP (`wsl hostname -I`) instead of `127.0.0.1`, or
switch WSL to mirrored networking, which makes `127.0.0.1` work in both directions for UDP.
For mirrored mode add to `%UserProfile%\.wslconfig`:

```ini
[wsl2]
networkingMode=mirrored
```

then `wsl --shutdown` and reopen WSL.

Every verb supports `--json` for a machine readable result on stdout (logs always go to
stderr, so JSON output is pipeable):

```bash
sipsorcery-diags sip options music@iptel.org --json
```

```json
{
  "success": true,
  "destination": "sip:music@iptel.org",
  "statusCode": 200,
  "reasonPhrase": "OK",
  "server": "kamailio",
  "remoteEndPoint": "udp:212.79.111.155:5060",
  "durationMs": 86
}
```

### Exit codes

| Code | Meaning |
| --- | --- |
| 0 | Success. |
| 1 | The operation completed but failed, e.g. an error SIP response. |
| 2 | An argument value was invalid. |
| 3 | No response within the timeout. |
| 4 | A network send failed. |

## Status

Early preview. Covers SIP (OPTIONS, calls, registration, load), WebRTC (WHEP, WHIP publish, WHIP
server with optional self-publish, echo test peers, video send benchmark), STUN/TURN/ICE connectivity
checks, and service integrations for
Cloudflare Realtime (TURN, SFU), LiveKit and the OpenAI Realtime API. SIP DNS resolution and further
verbs are planned.
