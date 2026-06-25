# SIPSorcery.Cli

The **`sipsorcery`** command: route and bridge live media **streams** between SIP, WebRTC and the
major realtime fabrics (Cloudflare Realtime, LiveKit) and AI agents (OpenAI Realtime), built on the
[SIPSorcery](https://github.com/sipsorcery-org/sipsorcery) library.

Where the sibling [`sipsorcery-diags`](../SIPSorcery.Diagnostics) tool is a probe/test/benchmark
harness, this tool treats a **stream as the primitive** and lets you attach edges to it. It is human
and agent friendly: every verb supports `--json` and meaningful exit codes.

## Install

```bash
dotnet tool install -g SIPSorcery.Cli --prerelease
```

The installed command is **`sipsorcery`**:

```bash
sipsorcery route --from testpattern --to out.ivf -d 10
```

## Usage

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
sipsorcery route --from testpattern --to whip:https://b.siobud.com/api/whip --audio-codec opus --token key  # publish to Broadcast Box (needs H264+opus)
sipsorcery route --from testpattern --to web                   # watch in a browser: self-hosts a WHEP server + player on http://localhost:8080
sipsorcery route --from sip:music@iptel.org --to web:9000 --scope --audio-codec opus  # bridge a SIP call to a browser page with an audio-scope video

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
sipsorcery openai realtime --audio play              # hear the model's reply (ffplay)
sipsorcery openai realtime --audio reply.wav         # record the model's reply to a WAV file
sipsorcery openai realtime --audio - > reply.pcm     # raw s16le PCM on stdout (the result moves to stderr)

# OpenAI chat: an interactive voice session (runs until ctrl-c). The model's voice plays via ffplay;
# --play - reads microphone PCM from stdin, so pipe an ffmpeg mic capture in. The mic is gated while
# the model speaks (half-duplex, no echo canceller) so it does not hear itself -- use a headset for
# full-duplex barge-in. Capture device syntax is per-OS (dshow/avfoundation/pulse). ffmpeg/ffplay act
# as the cross platform audio device layer (winget/brew/apt install ffmpeg).
ffmpeg -f dshow -i audio="Microphone (Realtek Audio)" -ac 1 -ar 48000 -f s16le - \
  | sipsorcery openai chat --play -
sipsorcery openai chat               # listen-only (hear the model, no mic)
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
| `--from`  | `testpattern` | A generated H264 video pattern **+ music** (codec via `--audio-codec`: pcmu default / pcma / opus), so it can feed a `whip:` sink with audio and video. `--fps` sets the video rate. |
| `--from`  | `whep:<url>` | A live WebRTC ingress (full ICE/DTLS/SRTP). `--token` sets a bearer/stream key. |
| `--from`  | `sip:<uri>` | Place a SIP call (G.711) and forward its received audio, transcoding up to `--audio-codec opus` if the sink needs it. `sip:music@iptel.org`, `sips:…` or a bare `user@host`; `-u`/`--password` authenticate. |
| `--to`    | *file path* | VP8 written as IVF, H264/H265 as Annex B. |
| `--to`    | `play` | An ffplay window (decode delegated to ffplay). |
| `--to`    | `null` | Discard — exercises the pipeline headlessly and reports throughput. |
| `--to`    | `-` | The bitstream on stdout (the result JSON then moves to stderr). |
| `--to`    | `whip:<url>` | Publish to a WebRTC (WHIP) endpoint as a send-only audio (`--audio-codec`: pcmu default / pcma / opus) + H264 video peer connection. `--token` sets the Authorization. Some endpoints (e.g. Broadcast Box) require `--audio-codec opus`. |
| `--to`    | `web[:port]` | Self-host a **WHEP server + player page** on `http://localhost:<port>` (default 8080) and stream to any browser that opens it. Many tabs can watch at once — each gets its own peer connection fanned from the one graph tee. H264 video + `--audio-codec` audio (opus is the most broadly supported). `--token` gates the WHEP endpoint; `--no-open` suppresses the browser auto-launch (also never opened with `--json`). |

#### The `--scope` audio-scope video

With a `sip:` source, `--scope` adds a second video track that is a live **visualisation of the call
audio** — a moving waveform (`--scope-mode waves`, the default) or a scrolling spectrum
(`--scope-mode spectrum`), sized with `--scope-size WxH`. The forwarded audio still travels
repacketised; the scope is the one transform that decodes to samples, and that rendering plus its H264
encoding is delegated to an external **ffmpeg** process (the `showwaves`/`showspectrum` filters +
`libx264`, the same way the sinks shell out to `ffplay`) — never a managed node. ffmpeg must be on the
`PATH` or located with `--ffmpeg-path`.

```bash
sipsorcery route --from sip:music@iptel.org --to whip:http://localhost:8080/whip --scope --scope-mode spectrum
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
sipsorcery route --from sip:music@iptel.org --to web --scope --audio-codec opus
```

Sinks/sources named `livekit:` / `cloudflare:` (and `sip:` as a sink) are recognised and report that
they are not wired into `route` yet — use the dedicated verbs today (`cloudflare sfu`, `livekit room`).
They become `route` edges in a later version, at which point `route --from sip:… --to livekit:room`
bridges a call into a room with no bespoke command.

## Exit codes

| Code | Meaning |
|------|---------|
| 0 | Success. |
| 1 | Ran but did not achieve its goal (e.g. connected but no media flowed). |
| 2 | Invalid argument or option. |
| 3 | Timed out. |
| 4 | Transport/network error (DNS, connect, TLS). |

## Status

Early. The `route` engine carries audio and video over a single source → N sinks (a free tee), with a
`sip:` source, `whip:` and `web` sinks and an ffmpeg-backed audio-scope transform wired in. Streaming
verbs default to `-d 0` (run until ctrl-c); pass a positive `--duration` for a fixed window. Fan-in, richer
transforms, the remaining transport edges and the authoring layers above the graph (declarative
routing, scripted policies, an external control API) are planned. Feedback and issues welcome on the
[SIPSorcery repo](https://github.com/sipsorcery-org/sipsorcery).
