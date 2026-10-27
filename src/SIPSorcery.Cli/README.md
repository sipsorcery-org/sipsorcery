# SIPSorcery.Cli

The **`sipsorcery`** command: SIP and WebRTC from the command line, built on the
[SIPSorcery](https://github.com/sipsorcery-org/sipsorcery) library. It does two jobs:

- **Diagnostics and benchmarking.** The protocol nouns (`sip`, `stun`, `ice`, `turn`, `webrtc`) carry
  probe/test/benchmark verbs: connectivity checks, test calls, registration and load, echo-test peers
  and video pipeline benchmarks. Not a general softphone.
- **Stream routing.** The top level `route` verb treats a media **stream** as the
  primitive and attach edges to it, moving live media between SIP, WebRTC, the major realtime fabrics
  (Cloudflare Realtime, LiveKit) and AI agents (OpenAI Realtime).

It is human and agent friendly: every verb supports `--json` and meaningful exit codes.

> The diagnostics verbs used to ship as the separate `SIPSorcery.Diagnostics` tool (the
> `sipsorcery-diags` command). They are now part of this tool with the same nouns and verbs, so
> `sipsorcery-diags sip options ...` becomes `sipsorcery sip options ...`.

## Install

```bash
dotnet tool install -g SIPSorcery.Cli --prerelease
```

```bash
sipsorcery sip options music@iptel.org
sipsorcery route --from testpattern --to out.ivf -d 10
```

## Diagnostics

```bash
# SIP ping: send an OPTIONS request and report the response.
sipsorcery sip options music@iptel.org
sipsorcery sip options tcp:sip.example.com:5060
sipsorcery sip options sips:secure.example.com -t 10 -v

# SIP call: place a call, send a test audio source and report on the media received.
# No audio devices are used; received audio renders via ffplay, a WAV file, or raw PCM
# on stdout. ffmpeg/ffplay act as the cross platform audio device layer
# (winget/brew/apt install ffmpeg).
sipsorcery sip call music@iptel.org --audio play           # listen via ffplay
sipsorcery sip call music@iptel.org --scope                # live spectrum + level in the terminal
sipsorcery sip call music@iptel.org --audio play --scope   # listen AND watch: --scope renders on
                                                           # stderr so it composes with any --audio
sipsorcery sip call music@iptel.org --audio rx.wav -d 10   # record 10s to a WAV file
sipsorcery sip call music@iptel.org --audio - > rx.pcm     # raw s16le PCM on stdout
                                                           # (the result moves to stderr)
sipsorcery sip call 100@pbx.example.com -u user --password pass --play tone --send-dtmf 123

# SIP register: register an account with a registrar and report the result.
sipsorcery sip register sipsorcery.com -u myuser --password mypass
sipsorcery sip register tls:sip.example.com -u myuser --password mypass -d 30  # hold 30s then remove
sipsorcery sip register sipsorcery.com -u myuser --password mypass --keep      # leave registered
sipsorcery sip register sipsorcery.com -u myuser --digest-store myuser.digest

# SIP digest: create a digest store file for passwordless registration.
sipsorcery sip digest --username myuser --realm sipsorcery.com --password mypass --out myuser.digest

# SIP load: generate concurrent OPTIONS load and report aggregate timing/success stats.
sipsorcery sip load myserver.org -c 1000 -x 25            # 1000 requests, 25 in flight at once
sipsorcery sip load myserver.org -c 100 -x 10 -p 1        # each worker waits 1s between requests
sipsorcery sip load myserver.org -c 100 -x 10 --break-on-fail --hep 192.168.0.10

# The SIP verbs can mirror their traffic to a HEPv3 capture server (HOMER, heplify-server,
# sipcapture.org) so the exchange shows up as a call ladder diagram:
sipsorcery sip options music@iptel.org --hep "127.0.0.1:9060"
sipsorcery sip call music@iptel.org --hep "192.168.0.10:9060;myHep;42"   # host:port;password;agentId

# STUN lookup: report this machine's public IP address and port.
sipsorcery stun lookup stun.cloudflare.com
sipsorcery stun lookup stun:stun.l.google.com:19302

# ICE gather: gather candidates and verify STUN/TURN connectivity.
sipsorcery ice gather
sipsorcery ice gather --stun stun:stun.cloudflare.com
sipsorcery ice gather --turn "turn:turn.example.com;user;pass" --relay-only
sipsorcery ice gather --key-id <key-id> --token <api-token> --relay-only   # Cloudflare TURN health check

# TURN allocate: check a single TURN server can allocate a relay socket and report the relay address.
# A focused, relay-only credential/health check (use "ice gather" to see all candidate types at once).
sipsorcery turn allocate turn:turn.example.com:3478 -u user -p pass
sipsorcery turn allocate "turn:turn.example.com:3478?transport=tcp" -u user -p pass
sipsorcery turn allocate turns:turn.example.com:5349 -u user -p pass -t 12
# Or check Cloudflare TURN: omit the url and pass (or set CLOUDFLARE_TURN_KEY_ID/CLOUDFLARE_API_TOKEN)
# the key ID and token; short lived credentials are fetched and that relay is allocated.
sipsorcery turn allocate --key-id <key-id> --token <api-token>
sipsorcery turn allocate --transport udp        # turn:3478?transport=udp instead of turns:443

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
sipsorcery webrtc whep https://b.siobud.com/api/whep --token mystreamkey

# Received video can be rendered or captured (decode is delegated to the consumer, so no
# video codecs are needed in-process). H264 is written as Annex B, VP8 in an IVF container.
sipsorcery webrtc whep https://b.siobud.com/api/whep --token key --video play       # ffplay window
sipsorcery webrtc whep https://b.siobud.com/api/whep --token key --video rx.h264    # capture to file
sipsorcery webrtc whep https://b.siobud.com/api/whep --token key `
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
sipsorcery webrtc whep https://b.siobud.com/api/whep --token key --video play --decode

# WebRTC WHIP server: accept a publish directly from ffmpeg/OBS and report on the media,
# including sequence anomalies. Useful for isolating where stream problems originate.
sipsorcery webrtc whip-server --listen http://localhost:8080/whip --token test -d 10
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
sipsorcery webrtc loopback --preset 1080p -d 10                 # 1080p30 loop, measure send + receive fps
sipsorcery webrtc loopback --preset 720p --video play -d 30     # publish + view the received stream
# --pattern picks the generated video: testcard (colour bars, a moving block and a frame counter, easy
# to check by eye: a frozen block or a skipping counter is a problem) or texture (a busy shifting
# pattern that keeps the encoder working, used by the benchmarks). The default, auto, uses testcard
# when --video shows or saves the video and texture for headless runs; "webrtc whip" defaults to
# testcard except with --max-rate.
# By default received frames are passed straight to the sink (ffplay decodes them). Add --decode to
# instead decode in-process and send raw RGB to the sink, so the picture goes through the library's
# decode path. --decoder selects ffmpeg (default, any codec; needs the FFmpeg libraries) or vp8.net
# (managed Vpx.Net, VP8 only -- and only reliable on vp8.net-encoded VP8; it can crash on FFmpeg's VP8).
sipsorcery webrtc loopback --video play --decode -d 30          # library-decoded (ffmpeg), rendered raw
sipsorcery webrtc loopback --video frames.rgb --decode -d 30    # capture raw rgb24 to a file
sipsorcery webrtc loopback --encoder vp8.net --codec vp8 --decode --decoder vp8.net --video null -d 10  # managed VP8 round-trip
sipsorcery webrtc loopback --encoder ffmpeg --codec av1 --preset 720p --decode --video null -d 10       # AV1 (or h265/vp9) library round-trip
# To measure the DECODE stage on its own, add --pre-encode N: it encodes N frames once before
# connecting, then replays that bitstream, so no encoding runs during the window (the encoder is out
# of the hot loop and not competing for CPU). Pair it with --decode --video null for headless decode.
sipsorcery webrtc loopback --preset 1080p --fps 120 --pre-encode 300 --decode --video null -d 10
# --max-rate sends flat out (ignoring --fps). With --pre-encode and no --decode it gives the pure
# transport ceiling (packetise -> SRTP -> socket -> depacketise, no codec): the pipeline's theoretical max.
sipsorcery webrtc loopback --preset 1080p --pre-encode 300 --max-rate -d 10
# WebRTC WHIP publish (library sender): publish a generated test pattern to a WHIP endpoint using the
# SIPSorcery stack itself -- the full SEND pipeline (generate -> encode -> RTP/SRTP -> ICE/DTLS). It
# is the counterpart to "video-bench", which measures the encoder but stops before the network.
# --encoder vp8.net (managed Vpx.Net VP8, no native deps) or ffmpeg (--codec h264/h265/vp8/vp9/av1); presets/--size,
# --fps, --bitrate and --max-rate (flat out, local receiver only). Reports frames sent, achieved vs
# target fps and encode ms/frame, so e.g. vp8.net's ceiling vs ffmpeg H264 at 720p is obvious.
sipsorcery webrtc whip http://localhost:8080/whip --preset 720p --fps 30 --encoder ffmpeg
sipsorcery webrtc whip https://b.siobud.com/api/whip --token key --preset 1080p --encoder ffmpeg
# To exercise an ALL-LIBRARY path (no ffmpeg muxer, both ends are SIPSorcery), point it at a
# "webrtc whip-server" running as the ingest in another terminal, or use "webrtc loopback" (above) to
# run the publisher and receiver in a single process.

# WebRTC echo test (https://github.com/sipsorcery/webrtc-echoes): the echo-server answers offers
# and echoes RTP and data channel messages; the echo client verifies the data channel round trips.
# The two pair for a self-contained interop test:
sipsorcery webrtc echo-server --listen http://localhost:8080/    # run the echo server (ctrl-c to stop)
sipsorcery webrtc echo http://localhost:8080/offer               # echo client against any echo server
sipsorcery webrtc echo http://localhost:8080/offer --stun "turn:turn.example.com;user;pass" --relay-only

# WebRTC video bench: measure the video SEND pipeline (no peer connection, DTLS or socket) to
# answer "can this machine sustain a target resolution and frame rate", default 1080p30. The
# pipeline is measured in stages via --encoder so the bottleneck can be isolated: none packetises
# a target-sized frame flat out (the RTP packetisation ceiling), vp8.net adds the managed Vpx.Net
# codec, and ffmpeg/ffmpeg-piped add the native FFmpeg encoder in-process or via an external
# ffmpeg process. --codec selects vp8, vp9, h264, h265 or av1: the in-process ffmpeg stage does all
# five, vp8.net is VP8 only and ffmpeg-piped does vp8/vp9 (IVF). Exits 0 if the target fps is met, 1 if below.
sipsorcery webrtc video-bench                                      # 1080p30, packetise only
sipsorcery webrtc video-bench --encoder vp8.net --fps 30           # managed VP8 codec
sipsorcery webrtc video-bench --encoder ffmpeg --codec h265 --width 1280 --height 720 --fps 60
sipsorcery webrtc video-bench --encoder ffmpeg --codec vp9 --preset 4k --fps 30
sipsorcery webrtc video-bench --all --preset 1080p                 # benchmark vp8/vp9/h265/av1 in one run
sipsorcery webrtc video-bench --encoder ffmpeg-piped --cpu-used 8 --threads 4 -d 10
# libvpx tuning (--deadline, --cpu-used, --threads) applies to the VP8/VP9 ffmpeg stages; for H264/H265
# the encoder uses its own realtime defaults. AV1 uses the SVT-AV1 encoder by default (--av1-encoder
# selects another, e.g. av1_nvenc; --av1-preset tunes its speed). The in-process ffmpeg stage needs the
# FFmpeg shared libraries (winget/brew/apt install ffmpeg, or --ffmpeg-path).

```

#### SIP call ladders in the terminal with sngrep

[`sngrep`](https://github.com/irontec/sngrep) can act as a HEP *receiver* and draw call
ladders right in the terminal, no database or web UI needed. Start it listening, then point
`--hep` at it:

```bash
sngrep -L udp:0.0.0.0:9060
sipsorcery sip call music@iptel.org --hep 127.0.0.1:9060 -d 5
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
sipsorcery sip options music@iptel.org --json
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

## Streams

```bash
# Route (v0.1): wire a source edge to one or more sink edges over a stream graph. The stream is the
# noun; --from / --to attach edges to it. The graph repacketises, it does not transcode -- frames
# travel encoded from source to sink. See "The route verb" below.
sipsorcery route --from testpattern --to out.h264 -d 10         # generate H264, record to Annex B
sipsorcery route --from testpattern --to play                  # generate H264, watch in ffplay
sipsorcery route --from testpattern --to play --to out.h264    # tee: watch AND record at once
sipsorcery route --from whep:https://b.siobud.com/api/whep --to out.ivf --token key  # record a live WebRTC stream
sipsorcery route --from sip:music@iptel.org --to whip:http://localhost:8080/whip      # bridge a SIP call to WebRTC
sipsorcery route --from sip:music@iptel.org --to whip:http://localhost:8080/whip --scope  # ...and add an audio-scope video
sipsorcery route --from testpattern --to whip:https://b.siobud.com/api/whip --token key  # publish to Broadcast Box (H264 + opus, both now the default)
sipsorcery route --from testpattern --to web                   # watch in a browser: self-hosts a WHEP server + player on http://localhost:8080
sipsorcery route --from sip:music@iptel.org --to web:9000 --scope          # bridge a SIP call (opus if the peer supports it, else G.711 auto-transcoded) to a browser page with an audio-scope video
sipsorcery route --from testpattern --to livekit:demo-room                 # publish into a LiveKit room (creds via --url/--api-key/--api-secret or LIVEKIT_* env)
sipsorcery route --from livekit:demo-room --to web                         # ...and in a SEPARATE process, subscribe to that room and watch it in a browser
sipsorcery route --from testpattern --to cloudflare                        # publish into a Cloudflare Realtime SFU session (creds via --app-id/--token or CLOUDFLARE_APPID/CLOUDFLARE_API_TOKEN)
sipsorcery route --from cloudflare:<sessionId> --to web                    # ...and in a SEPARATE process, pull that session (id printed by the sink) and watch it in a browser

# Two-way: with --duplex, or automatically when either end is an agent, --from and --to each send to and
# receive from the other (full duplex). Two-way endpoints: web (a browser mic+speaker page), term (the
# terminal: mic via ffmpeg, Matroska on stdout), agent (an Azure speech-to-text -> LLM -> Azure
# text-to-speech voice agent), openai (the OpenAI Realtime API) and sip:<uri> (a phone call, transcoded
# G.711 <-> Opus). See "Two-way routes" below.
sipsorcery route --from web --to agent                    # open a browser and TALK to an Azure voice agent (e.g. Max Headroom)
sipsorcery route --from web --to openai                   # ...or talk to an OpenAI Realtime agent (OPENAI_API_KEY)
sipsorcery route --from web --to openai --avatar          # ...with a lip-synced video face (the photoreal Wav2Lip Max Headroom head)
sipsorcery route --from sip:alice@example.com --to agent  # ...or let a PHONE caller talk to the agent (G.711 <-> Opus transcoded)
sipsorcery route --from term --to openai --avatar | mpv --vo=tct --profile=low-latency -   # ...or talk from the TERMINAL (headphones!)
sipsorcery route --from term --to sip:alice@example.com --duplex | mpv -   # no agent, so ask for two-way: a phone call from the terminal


# The realtime fabrics (Cloudflare, LiveKit, OpenAI) have no standalone verbs: each is reached as a
# route edge, shown above. The probe/test verbs for them (turn allocate, ice gather, webrtc ...)
# are listed under Diagnostics.
```

### The route verb

`route` is the first cut of the stream router: rather than a one-shot diagnostic, it treats a media
**stream** as the primitive and lets you attach **edges** to it. A graph is a source node, one or more
sink nodes, and the directed edges between them; a routing decision is just a mutation of that graph.
v0.1 implements the simplest shape — one source fanned out to N sinks (a free tee).

Two design rules carry through from the bigger picture:

- **Repacketise, not transcode.** Frames travel the edges still encoded (VP8 stays VP8, H264 stays
  H264). The tool stands in the media path only far enough to depacketise and re-emit; it never
  decodes to samples. Per-sample work (a real transcode) is the job of a future transform node backed
  by ffmpeg, not managed code.
- **Edges are addressable.** A `--from` / `--to` spec is either a bare keyword/path (`out.ivf`,
  `play`, `null`, `-`) or `scheme:rest` (`whep:https://host/whep`). Adding a transport edge is one new
  case in the edge factory and nothing else.

Edges:

| Direction | Edge | Notes |
|-----------|------|-------|
| `--from`  | `testpattern` | A generated video pattern **+ music** (video codec via `--video-codec`: h264 default / vp8 / vp9 / h265 / av1; audio via `--audio-codec`: opus default / pcmu / pcma), so it can feed a `whip:` sink with audio and video. `--fps` sets the video rate. |
| `--from`  | `whep:<url>` | A live WebRTC ingress (full ICE/DTLS/SRTP). `--token` sets a bearer/stream key. Offers every supported video codec unless `--video-codec` pins one. |
| `--from`  | `sip:<uri>` | Place a SIP call and forward its received audio. It offers **opus first, then a G.711 fallback**, so a modern endpoint is carried in opus end to end and a G.711-only gateway is **auto-transcoded** up to the graph codec (opus by default) — no flag needed either way. `sip:music@iptel.org`, `sips:…` or a bare `user@host`; `-u`/`--password` authenticate. |
| `--from`  | `livekit:<room>` | Subscribe to a LiveKit **room** and emit the received H264 video + OPUS audio. Needs `--url`/`--api-key`/`--api-secret` (or `LIVEKIT_*` env). Mints its own subscribe-only token, so it only needs the room name + creds. Start the publisher first so the track is in the room when this joins. |
| `--from`  | `cloudflare:<sessionId>` | Pull (subscribe to) a **Cloudflare Realtime SFU** session published by the `cloudflare` sink, emitting its received video + audio. The session id is the one the sink printed; the pulled track names are the sink's (`cli-video`/`cli-audio`). Needs `--app-id`/`--token` (or `CLOUDFLARE_APPID`/`CLOUDFLARE_API_TOKEN`). Start the publisher first. |
| `--to`    | *file path* | VP8 written as IVF, H264/H265 as Annex B. |
| `--to`    | `play` | An ffplay window (decode delegated to ffplay). |
| `--to`    | `null` | Discard — exercises the pipeline headlessly and reports throughput. |
| `--to`    | `-` | The video bitstream on stdout (the result JSON then moves to stderr). |
| `--to`    | `-:mkv` | Video **and** audio muxed into a live Matroska stream on stdout, with real timestamps, so a player gets both: `route --from testpattern --to -:mkv \| mpv -`. Repacketised, not transcoded: H264 or VP8 video, Opus or G.711 audio. |
| `--to`    | *path*`.mkv` | The same Matroska stream written to a file, e.g. to record a SIP call with its scope video. |
| `--to`    | `whip:<url>` | Publish to a WebRTC (WHIP) endpoint as a send-only audio (`--audio-codec`: opus default / pcmu / pcma) + video (`--video-codec`: h264 default) peer connection. `--token` sets the Authorization. Opus is the default, so endpoints that require it (e.g. Broadcast Box) just work. |
| `--to`    | `web[:port]` | Self-host a **WHEP server + player page** on `http://localhost:<port>` (default 8080) and stream to any browser that opens it. Many tabs can watch at once — each gets its own peer connection fanned from the one graph tee. `--video-codec` video (h264 by default) + `--audio-codec` audio (opus is the most broadly supported). `--token` gates the WHEP endpoint; `--no-open` suppresses the browser auto-launch (also never opened with `--json`). |
| `--to`    | `livekit[:room]` | Publish into a LiveKit **room** (default a random name) as a send-only H264 + OPUS participant. Needs `--url`/`--api-key`/`--api-secret` (or `LIVEKIT_WEBSOCKET_URL`/`LIVEKIT_API_KEY`/`LIVEKIT_API_SECRET`); requires opus, which is the default. The CLI mints its own publisher token locally, so a separate subscriber only needs the same room name + creds. |
| `--to`    | `cloudflare` | Publish into a **Cloudflare Realtime SFU** session as a send-only H264 + `--audio-codec` peer (opus recommended). Needs `--app-id`/`--token` (or `CLOUDFLARE_APPID`/`CLOUDFLARE_API_TOKEN`). Cloudflare allocates the session id, so the sink prints it plus the track names; a subscriber pulls the media by (session id, track name). |

#### Forcing one codec (`--audio-codec` / `--video-codec`)

Each flag takes **exactly one** codec name, matched case-insensitively: `--audio-codec` one of opus
(the default), pcmu, pcma; `--video-codec` one of vp8, vp9, h264, h265, av1. There is no list, no
priority order and no fallback — the point is "use this codec and nothing else", so a codec the far
end will not accept **fails the run** instead of quietly negotiating something else, and an
unrecognised name is an argument error (exit 2) listing the valid values rather than a silent
fall-through to the defaults.

Left out, `--video-codec` leaves each edge on its own default: H264 for a sending edge, every
supported codec for a receiving one. It does not apply to a two-way (`--duplex`) route, whose video
is always H264, nor to the H264 `--scope` video.

```bash
# Pin VP9 end to end: a VP9 graph into the browser page, then prove it with the WHEP client.
sipsorcery route --from testpattern --to web:8099 --video-codec vp9 --no-open --duration 30
sipsorcery webrtc whep http://localhost:8099/whep --video-codec vp9 --duration 6 --json

# A codec the other side is not offering fails, which is the whole point.
sipsorcery webrtc whep http://localhost:8099/whep --video-codec h264 --json   # -> HTTP 400, exit 1
```

The receiving `webrtc` verbs take the same two flags to pin what they offer the far side: `webrtc
whep <url> --video-codec vp9` and `webrtc whip-server --video-codec vp9`. The publishing verbs
(`webrtc whip`, `webrtc loopback`, `webrtc video-bench`) already force a single send codec with
`--codec`, which also answers to `--video-codec`.

#### The `--scope` audio-scope video

With a `sip:` source, `--scope` adds a second video track that is a live **visualisation of the call
audio**, sized with `--scope-size WxH` (even sides, default 640x360). `--scope-mode` picks the picture:

| Mode | Picture |
|------|---------|
| `waves` (default) | A moving waveform (ffmpeg's `showwaves` filter). |
| `spectrum` | A scrolling frequency spectrum (ffmpeg's `showspectrum` filter). |
| `hilbert` | The **audio scope** from the Softphone and WebRTCAudioScope examples: the call audio's analytic signal (the audio against its Hilbert transform) traced as a curve with fading trails, coloured by frequency. |

The forwarded audio still travels repacketised; the scope is the one transform that decodes to
samples. The H264 encoding is delegated to an external **ffmpeg** process (`libx264`, the same way the
sinks shell out to `ffplay`), which for `waves` and `spectrum` also does the rendering. `hilbert` has
no ffmpeg filter equivalent, so it is drawn in-process by a small software rasteriser and the raw
frames are piped to ffmpeg. ffmpeg must be on the `PATH` or located with `--ffmpeg-path`.

```bash
sipsorcery route --from sip:music@iptel.org --to whip:http://localhost:8080/whip --scope --scope-mode spectrum
sipsorcery route --from sip:music@iptel.org --to web --scope --scope-mode hilbert   # watch the audio scope in a browser
```

#### The `web` sink (watch in a browser)

`--to web` turns the CLI into its own viewer. It binds a local `HttpListener` that serves a small
auto-connecting HTML player at `http://localhost:8080/` (pick another port with `web:9000`) and answers
the browser's WHEP request, streaming the graph's H264 video + audio over WebRTC. Unlike `whip:`, which
dials *out* to a remote endpoint and blocks until connected, this **listens** for viewers — so it never
blocks waiting for one. Open the URL whenever, in as many tabs as you like, each becoming its own peer
connection fanned from the one graph tee. The browser opens for you (suppress with `--no-open`, or
`--json`); the run continues until ctrl-c. The page and the WHEP signalling share one port (`GET /`
serves the player, `POST /whep` does the offer/answer), and it binds localhost only.

```bash
sipsorcery route --from testpattern --to web                 # open http://localhost:8080 and watch
sipsorcery route --from sip:music@iptel.org --to web --scope
```

The `livekit:` and `cloudflare:` **source and sink** are both wired — publish into a room / SFU session,
and subscribe to (pull) one — so running the two in separate processes is a full round trip
(`route --from testpattern --to cloudflare` in one, `route --from cloudflare:<id> --to web` in another).
`sip:` as a sink is recognised but reports it is not wired into `route` yet; it becomes a `route` edge in
a later version, at which point `route --from sip:… --to livekit:room` bridges a call into a room with no
bespoke command.

### Two-way routes

A route is **one-way** by default — `--from` → `--to`. A conversation is **duplex**, so a route can also
run **both ways** between exactly two endpoints (`--from` → `--to` *and* `--to` → `--from`):

- **`--duplex`** asks for it, e.g. a phone call: `route --from term --to sip:alice@example.com --duplex`.
- It is **automatic when either end is an agent** (`openai` or `agent`), since an agent that cannot hear
  is useless: `route --from web --to agent` needs no flag.

One-way is the default so that a **microphone** (`term`, `web`) is only ever opened for a route that is
clearly a conversation: `route --from sip:music@iptel.org --to term` just plays the call, and never sends
your microphone to it. The two endpoints are order-agnostic — `--from web --to agent` and
`--from agent --to web` are the same. A two-way route always uses Opus, takes a single `--to`, and has no
`--scope`.

Two-way endpoints:

| Endpoint | Role |
|----------|------|
| `web`   | A browser as a **microphone + speaker** over one send/recv Opus peer connection (self-hosts the page on `--port`, default 8080, and **opens it automatically** — suppress with `--no-open`, also never opened with `--json`). When the other side is an agent, the live **conversation transcript is logged to the browser console** (`[you] …` / `[ai] …`) over a data channel. |
| `term`  | The **terminal** as microphone + speaker: the microphone is captured by an **ffmpeg** process (DirectShow on Windows, the first audio device unless `--mic` names one; avfoundation on macOS; PulseAudio on Linux) and what the other side sends back (an agent's voice and, with `--avatar`, its face) is written to **stdout as Matroska**, for a player to show: `route --from term --to openai --avatar \| mpv --vo=tct -`. The agent's bursty speech is paced into a continuous audio track (silence in the gaps) so the player keeps sound and picture in sync. There is **no echo cancellation**, so use headphones or the agent hears itself. With `dotnet run`, add `--no-launch-profile` so nothing but the Matroska stream reaches stdout. |
| `agent` | A locally-assembled **voice agent**: **Azure speech-to-text → LLM → Azure text-to-speech**, with a Max Headroom persona by default. You speak, it listens, thinks and replies. |
| `openai` | A voice agent backed by the **OpenAI Realtime API** — OpenAI does the listening, thinking and speaking in one hop. Opus passes straight through both ways (repacketise, not transcode). Needs an OpenAI key (`--api-key` or `OPENAI_API_KEY`); `--voice` picks a Realtime voice (marin, alloy, verse, …), `--persona` sets the system prompt. |
| `sip:<uri>` | A **SIP call** as a full-duplex peer — place a call to `sip:user@host` (or a bare `user@host`) and bridge the caller to the other side, so a **phone can talk to a voice agent**. The G.711 call is transcoded to/from 48 kHz Opus at the boundary (audio is cheap to transcode in managed code), so it drops in against `web`, `agent` or `openai`. `--username`/`--password` authenticate if challenged. With `--avatar` the call becomes a **video call** (a send-only H264 m-line carrying the agent's face); a phone that can't do video just declines it and the call stays audio-only. |

```bash
# Set the agent's credentials (or pass --azure-key/--azure-region/--llm), then:
export AZURE_SPEECH_KEY=…  AZURE_SPEECH_REGION=westeurope
export LLM_ENDPOINT=http://localhost:11434/v1/chat/completions  LLM_MODEL=llama3.2   # e.g. Ollama
sipsorcery route --from web --to agent
```

Open the page, grant the microphone, and **talk to Max** — full duplex (the browser's echo canceller
keeps the agent out of its own mic, so you can interrupt). `--voice`, `--persona`, `--greeting`, `--llm`
/`--llm-model`/`--api-key` tune it; without an `--llm` the agent just repeats what it heard.

Or talk to an **OpenAI Realtime** agent instead — same browser, no Azure or local LLM, OpenAI does the
whole listen→think→speak in one hop:

```bash
export OPENAI_API_KEY=sk-…
sipsorcery route --from web --to openai                                 # talk to OpenAI Realtime
sipsorcery route --from web --to openai --voice verse --persona "You are a terse pirate."
```

Add **`--avatar`** to put a lip-synced **video face** on the voice — the browser then shows his face
alongside the audio, on either agent. There are two faces:

| `--avatar` | Face |
|------------|------|
| *(no name)* or `wav2lip` | The default: the **photoreal Max Headroom head** from the [maxheadroom](https://github.com/sipsorcery/maxheadroom) repo. The **Wav2Lip** model animates the mouth from the agent's actual voice, run in-process with onnxruntime on the GPU via **DirectML** (~30fps; CPU fallback). Windows only, and it needs the model and persona files, which are not bundled (Wav2Lip is licensed for non-commercial use): `WAV2LIP_ONNX` (else `C:\tools\wav2lip\wav2lip-onnx\checkpoints\wav2lip_gan.onnx` or `wav2lip.onnx`), `NEURAL_PERSONA` (else `C:\tools\wav2lip\persona.jpg/.png/.webp`) and `NEURAL_MATTE` (else `C:\tools\wav2lip\persona_alpha.png`). If it cannot run, a plain `--avatar` falls back to `max` with a warning; `--avatar wav2lip` reports why instead. |
| `max` | The **SkiaSharp cartoon** Max Headroom, drawn in-process with no extra files. The Azure `agent` drives its mouth from the TTS viseme timeline (phoneme-accurate); the `openai` voice has no visemes, so its mouth follows the speech envelope. |

```bash
sipsorcery route --from web --to openai --avatar                       # the photoreal Wav2Lip head
sipsorcery route --from web --to agent  --avatar max                   # the cartoon, lip-synced from Azure TTS visemes
sipsorcery route --from web --to openai --avatar max                   # the cartoon, mouth following the speech envelope
```

Either agent also answers the **phone**: a `sip:<uri>` peer places a SIP call and bridges the caller to
the other side, so someone on a handset can speak to the agent. The endpoints are symmetric, so the
agent can be on either side:

```bash
sipsorcery route --from sip:alice@example.com --to agent          # call alice, bridge her to the Azure agent
sipsorcery route --from sip:alice@example.com --to openai         # ...or to the OpenAI Realtime agent
sipsorcery route --from sip:1001@pbx.local --to agent --username 1000 --password secret   # authenticate the call
```

The SIP leg is **G.711** (8 kHz); every other endpoint speaks **48 kHz Opus**, so this peer transcodes
G.711 ↔ Opus at its boundary (audio is light enough to transcode in managed code — only video is
delegated to ffmpeg). The agent greets the moment the call is answered.

Adding **`--avatar`** turns it into a **video call**: the SIP leg offers a send-only H264 m-line and the
agent's lip-synced face is sent down it, so a **video softphone** (Linphone, Zoiper, …) shows the avatar
while you talk to it — the same `--avatar` that paints the face in the browser, no extra flag:

```bash
sipsorcery route --from sip:alice@example.com --to agent  --avatar    # Azure agent, viseme lip-sync, on the phone
sipsorcery route --from sip:alice@example.com --to openai --avatar    # OpenAI agent, envelope lip-sync, on the phone
```

An audio-only phone simply rejects the video m-line and the call carries on as audio. Pluggable/external
avatars are planned.

## Exit codes

| Code | Meaning |
|------|---------|
| 0 | Success. |
| 1 | Ran but did not achieve its goal (e.g. an error SIP response, or connected but no media flowed). |
| 2 | Invalid argument or option. |
| 3 | Timed out. |
| 4 | Transport/network error (DNS, connect, TLS, socket send). |

## Status

Early preview. The diagnostics verbs cover SIP (OPTIONS, calls, registration, load, transfer), WebRTC
(WHEP, WHIP publish, WHIP server, loopback, echo test peers, video send benchmark) and STUN/TURN/ICE
connectivity checks, including Cloudflare TURN. SIP DNS resolution and further verbs are planned.

Early. The `route` engine carries audio and video over a single source → N sinks (a free tee), with
`whep:`/`sip:`/`livekit:`/`cloudflare:` sources, `whip:`/`web`/`livekit:`/`cloudflare` sinks and an
ffmpeg-backed audio-scope transform wired in. Two-way routes (`--duplex`, automatic with an agent) add the duplex
case, with `web`, `term`, a local Azure voice `agent`, an `openai` Realtime agent and a `sip:` phone peer (transcoded G.711 ↔ Opus)
as the endpoints, the agents each with an optional lip-synced `--avatar`. Streaming verbs default to
`-d 0` (run until ctrl-c); pass a positive `--duration` for a fixed window. Fan-in, richer transforms,
the remaining transport edges, pluggable/external avatars, and the authoring layers above the graph
(declarative routing, scripted policies, an external control API) are planned. Feedback and issues
welcome on the [SIPSorcery repo](https://github.com/sipsorcery-org/sipsorcery).
