# sipsorcery CLI

SIP and WebRTC diagnostics from the command line, built on the
[SIPSorcery](https://github.com/sipsorcery-org/sipsorcery) library.

## Install

```bash
dotnet tool install -g SIPSorcery.Cli --prerelease
```

## Usage

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

# ICE probe: gather candidates and verify STUN/TURN connectivity.
sipsorcery ice probe
sipsorcery ice probe --stun stun:stun.cloudflare.com
sipsorcery ice probe --turn "turn:turn.example.com;user;pass" --relay-only
sipsorcery ice probe --key-id <key-id> --token <api-token> --relay-only   # probe Cloudflare TURN

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

# --publish makes the server also feed its own listener with an ffmpeg test pattern, so the whole
# self-contained loop (encode -> network -> decode -> view/measure) is one command, no second
# terminal and no startup race. Resolution presets are 360p/480p/720p/1080p/1440p/4k (or --size WxH),
# with --fps, --codec (h264 reliable; ffmpeg's WHIP muxer may not support vp8), --bitrate and --audio.
# The generated ffmpeg command is printed so you can copy and adapt it for edge cases.
sipsorcery webrtc whip-server --publish --preset 1080p -d 10                 # 1080p30 loop, measure fps
sipsorcery webrtc whip-server --publish --preset 720p --video play -d 30     # publish + view received
# By default received frames are passed straight to the sink (ffplay decodes them). Add --decode to
# instead decode in-process with the SIPSorcery (FFmpeg) decoder and send raw RGB to the sink, so the
# picture goes through the library's decode path. Needs the FFmpeg shared libraries.
sipsorcery webrtc whip-server --publish --video play --decode -d 30          # library-decoded, rendered raw
sipsorcery webrtc whip-server --publish --video frames.rgb --decode -d 30    # capture raw rgb24 to a file
# The server still accepts any external publisher (ffmpeg, OBS) when --publish is omitted:
sipsorcery webrtc whip-server --listen http://localhost:8080/whip --token test -d 10
ffmpeg `
  -re `
  -f lavfi -i testsrc=size=640x360 `
  -f lavfi -i sine=frequency=440 `
  -pix_fmt yuv420p -c:v libx264 -profile:v baseline -r 25 -g 50 `
  -c:a libopus -ar 48000 -ac 2 `
  -f whip -authorization "test" `
  "http://localhost:8080/whip"

# WebRTC echo test (https://github.com/sipsorcery/webrtc-echoes): the echo-server answers offers
# and echoes RTP and data channel messages; the echo client verifies the data channel round trips.
# The two pair for a self-contained interop test:
sipsorcery webrtc echo-server --listen http://localhost:8080/    # run the echo server (ctrl-c to stop)
sipsorcery webrtc echo http://localhost:8080/offer               # echo client against any echo server
sipsorcery webrtc echo http://localhost:8080/offer --stun "turn:turn.example.com;user;pass" --relay-only

# WebRTC video bench: measure the video SEND pipeline (no peer connection, DTLS or socket) to
# answer "can this machine sustain a target resolution and frame rate", default 1080p30. The
# pipeline is measured in stages via --encoder so the bottleneck can be isolated: none packetises
# a target-sized frame flat out (the RTP packetisation ceiling), vp8 adds the managed Vpx.Net
# codec, and ffmpeg/ffmpeg-piped add the native libvpx encoder in-process or via an external
# ffmpeg process. Exits 0 if the target fps is met, 1 if below.
sipsorcery webrtc video-bench                                      # 1080p30, packetise only
sipsorcery webrtc video-bench --encoder vp8 --fps 30               # managed VP8 codec
sipsorcery webrtc video-bench --encoder ffmpeg --width 1280 --height 720 --fps 60
sipsorcery webrtc video-bench --encoder ffmpeg-piped --cpu-used 8 --threads 4 -d 10
# libvpx tuning (--deadline, --cpu-used, --threads) applies to the ffmpeg stages. The in-process
# ffmpeg stage needs the FFmpeg shared libraries (winget/brew/apt install ffmpeg, or --ffmpeg-path).

# Cloudflare TURN: fetch short lived credentials from the Realtime TURN API and confirm a relay
# candidate can be allocated. Credentials default to the CLOUDFLARE_TURN_KEY_ID and
# CLOUDFLARE_API_TOKEN environment variables. https://developers.cloudflare.com/realtime/turn/
sipsorcery cloudflare turn --key-id <key-id> --token <api-token>
sipsorcery cloudflare turn --transport udp        # probe turn:3478?transport=udp instead of turns:443

# Cloudflare SFU: create a Realtime SFU session and publish a VP8 + OPUS test pattern, verifying
# the publisher connects. Defaults to CLOUDFLARE_APPID and CLOUDFLARE_API_TOKEN.
sipsorcery cloudflare sfu --app-id <app-id> --token <api-token> -d 10

# LiveKit room: mint an access token, join a room and publish a VP8 + OPUS test pattern. Defaults
# to LIVEKIT_WEBSOCKET_URL, LIVEKIT_API_KEY and LIVEKIT_API_SECRET.
sipsorcery livekit room --url wss://my-app.livekit.cloud --api-key <key> --api-secret <secret>
sipsorcery livekit room --room my-room -d 30       # a specific room (default is a random name)

# OpenAI Realtime: end to end connectivity test for the Realtime WebRTC API. Negotiates the
# connection, asks the model to speak and succeeds when its voice is received. No audio device is
# used. Defaults to the OPENAI_API_KEY environment variable.
sipsorcery openai realtime
sipsorcery openai realtime --voice verse --prompt "Count to three."
```

### SIP call ladders in the terminal with sngrep

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

### Exit codes

| Code | Meaning |
| --- | --- |
| 0 | Success. |
| 1 | The operation completed but failed, e.g. an error SIP response. |
| 2 | An argument value was invalid. |
| 3 | No response within the timeout. |
| 4 | A network send failed. |

## Status

Early preview. Covers SIP (OPTIONS, calls, registration, load), WebRTC (WHEP, WHIP server with
optional self-publish, echo test peers, video send benchmark), STUN/TURN/ICE connectivity checks, and service integrations for
Cloudflare Realtime (TURN, SFU), LiveKit and the OpenAI Realtime API. SIP DNS resolution and further
verbs are planned.
