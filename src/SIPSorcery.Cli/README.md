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
sipsorcery route --from testpattern --to whip:https://b.siobud.com/api/whip --token key  # publish to Broadcast Box (H264 + opus, both now the default)
sipsorcery route --from testpattern --to web                   # watch in a browser: self-hosts a WHEP server + player on http://localhost:8080
sipsorcery route --from sip:music@iptel.org --to web:9000 --scope          # bridge a SIP call (opus if the peer supports it, else G.711 auto-transcoded) to a browser page with an audio-scope video
sipsorcery route --from testpattern --to livekit:demo-room                 # publish into a LiveKit room (creds via --url/--api-key/--api-secret or LIVEKIT_* env)
sipsorcery route --from livekit:demo-room --to web                         # ...and in a SEPARATE process, subscribe to that room and watch it in a browser
sipsorcery route --from testpattern --to cloudflare                        # publish into a Cloudflare Realtime SFU session (creds via --app-id/--token or CLOUDFLARE_APPID/CLOUDFLARE_API_TOKEN)
sipsorcery route --from cloudflare:<sessionId> --to web                    # ...and in a SEPARATE process, pull that session (id printed by the sink) and watch it in a browser

# Bridge: connect two duplex endpoints BOTH ways (full duplex). Where route is directional (--from/--to),
# bridge is symmetric and order-agnostic. v0.1 endpoints: web (a browser mic+speaker page), agent (an
# Azure speech-to-text -> LLM -> Azure text-to-speech voice agent), openai (the OpenAI Realtime API) and
# sip:<uri> (a phone call, transcoded G.711 <-> Opus). See "The bridge verb" below.
sipsorcery bridge web agent --open             # open a browser and TALK to an Azure voice agent (e.g. Max Headroom)
sipsorcery bridge web openai --open            # ...or talk to an OpenAI Realtime agent (OPENAI_API_KEY)
sipsorcery bridge web openai --avatar --open   # ...with a lip-synced video face (mouth driven by the speech envelope)
sipsorcery bridge sip:alice@example.com agent  # ...or let a PHONE caller talk to the agent (G.711 <-> Opus transcoded)

# The realtime fabrics (Cloudflare, LiveKit, OpenAI) have no standalone verbs: each is reached as a
# route/bridge edge, shown above. Probe/test commands for them live in the sibling sipsorcery-diags tool.
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
| `--from`  | `testpattern` | A generated H264 video pattern **+ music** (codec via `--audio-codec`: opus default / pcmu / pcma), so it can feed a `whip:` sink with audio and video. `--fps` sets the video rate. |
| `--from`  | `whep:<url>` | A live WebRTC ingress (full ICE/DTLS/SRTP). `--token` sets a bearer/stream key. |
| `--from`  | `sip:<uri>` | Place a SIP call and forward its received audio. It offers **opus first, then a G.711 fallback**, so a modern endpoint is carried in opus end to end and a G.711-only gateway is **auto-transcoded** up to the graph codec (opus by default) — no flag needed either way. `sip:music@iptel.org`, `sips:…` or a bare `user@host`; `-u`/`--password` authenticate. |
| `--from`  | `livekit:<room>` | Subscribe to a LiveKit **room** and emit the received H264 video + OPUS audio. Needs `--url`/`--api-key`/`--api-secret` (or `LIVEKIT_*` env). Mints its own subscribe-only token, so it only needs the room name + creds. Start the publisher first so the track is in the room when this joins. |
| `--from`  | `cloudflare:<sessionId>` | Pull (subscribe to) a **Cloudflare Realtime SFU** session published by the `cloudflare` sink, emitting its received video + audio. The session id is the one the sink printed; the pulled track names are the sink's (`cli-video`/`cli-audio`). Needs `--app-id`/`--token` (or `CLOUDFLARE_APPID`/`CLOUDFLARE_API_TOKEN`). Start the publisher first. |
| `--to`    | *file path* | VP8 written as IVF, H264/H265 as Annex B. |
| `--to`    | `play` | An ffplay window (decode delegated to ffplay). |
| `--to`    | `null` | Discard — exercises the pipeline headlessly and reports throughput. |
| `--to`    | `-` | The bitstream on stdout (the result JSON then moves to stderr). |
| `--to`    | `whip:<url>` | Publish to a WebRTC (WHIP) endpoint as a send-only audio (`--audio-codec`: opus default / pcmu / pcma) + H264 video peer connection. `--token` sets the Authorization. Opus is the default, so endpoints that require it (e.g. Broadcast Box) just work. |
| `--to`    | `web[:port]` | Self-host a **WHEP server + player page** on `http://localhost:<port>` (default 8080) and stream to any browser that opens it. Many tabs can watch at once — each gets its own peer connection fanned from the one graph tee. H264 video + `--audio-codec` audio (opus is the most broadly supported). `--token` gates the WHEP endpoint; `--no-open` suppresses the browser auto-launch (also never opened with `--json`). |
| `--to`    | `livekit[:room]` | Publish into a LiveKit **room** (default a random name) as a send-only H264 + OPUS participant. Needs `--url`/`--api-key`/`--api-secret` (or `LIVEKIT_WEBSOCKET_URL`/`LIVEKIT_API_KEY`/`LIVEKIT_API_SECRET`); requires opus, which is the default. The CLI mints its own publisher token locally, so a separate subscriber only needs the same room name + creds. |
| `--to`    | `cloudflare` | Publish into a **Cloudflare Realtime SFU** session as a send-only H264 + `--audio-codec` peer (opus recommended). Needs `--app-id`/`--token` (or `CLOUDFLARE_APPID`/`CLOUDFLARE_API_TOKEN`). Cloudflare allocates the session id, so the sink prints it plus the track names; a subscriber pulls the media by (session id, track name). |

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
sipsorcery route --from sip:music@iptel.org --to web --scope
```

The `livekit:` and `cloudflare:` **source and sink** are both wired — publish into a room / SFU session,
and subscribe to (pull) one — so running the two in separate processes is a full round trip
(`route --from testpattern --to cloudflare` in one, `route --from cloudflare:<id> --to web` in another).
`sip:` as a sink is recognised but reports it is not wired into `route` yet; it becomes a `route` edge in
a later version, at which point `route --from sip:… --to livekit:room` bridges a call into a room with no
bespoke command.

### The bridge verb

`route` is directional — `--from` → `--to`. A conversation is **duplex**, and `--from/--to` reads wrong
for that, so the duplex case has its own verb: **`bridge <a> <b>`** connects two endpoints **both ways**
(`a` → `b` *and* `b` → `a`). The endpoints are **symmetric / order-agnostic** — `bridge web agent` and
`bridge agent web` are the same.

v0.1 endpoints:

| Endpoint | Role |
|----------|------|
| `web`   | A browser as a **microphone + speaker** over one send/recv Opus peer connection (self-hosts the page; `--open` launches it, `--port` sets the port). When the other side is an agent, the live **conversation transcript is logged to the browser console** (`[you] …` / `[ai] …`) over a data channel. |
| `agent` | A locally-assembled **voice agent**: **Azure speech-to-text → LLM → Azure text-to-speech**, with a Max Headroom persona by default. You speak, it listens, thinks and replies. |
| `openai` | A voice agent backed by the **OpenAI Realtime API** — OpenAI does the listening, thinking and speaking in one hop. Opus passes straight through both ways (repacketise, not transcode). Needs an OpenAI key (`--api-key` or `OPENAI_API_KEY`); `--voice` picks a Realtime voice (marin, alloy, verse, …), `--persona` sets the system prompt. |
| `sip:<uri>` | A **SIP call** as a full-duplex peer — place a call to `sip:user@host` (or a bare `user@host`) and bridge the caller to the other side, so a **phone can talk to a voice agent**. The G.711 call is transcoded to/from 48 kHz Opus at the boundary (audio is cheap to transcode in managed code), so it drops in against `web`, `agent` or `openai`. `--user`/`--password` authenticate if challenged. With `--avatar` the call becomes a **video call** (a send-only H264 m-line carrying the agent's face); a phone that can't do video just declines it and the call stays audio-only. |

```bash
# Set the agent's credentials (or pass --azure-key/--azure-region/--llm), then:
export AZURE_SPEECH_KEY=…  AZURE_SPEECH_REGION=westeurope
export LLM_ENDPOINT=http://localhost:11434/v1/chat/completions  LLM_MODEL=llama3.2   # e.g. Ollama
sipsorcery bridge web agent --open
```

Open the page, grant the microphone, and **talk to Max** — full duplex (the browser's echo canceller
keeps the agent out of its own mic, so you can interrupt). `--voice`, `--persona`, `--greeting`, `--llm`
/`--llm-model`/`--api-key` tune it; without an `--llm` the agent just repeats what it heard.

Or talk to an **OpenAI Realtime** agent instead — same browser, no Azure or local LLM, OpenAI does the
whole listen→think→speak in one hop:

```bash
export OPENAI_API_KEY=sk-…
sipsorcery bridge web openai --open                          # talk to OpenAI Realtime
sipsorcery bridge web openai --voice verse --persona "You are a terse pirate." --open
```

Add **`--avatar`** to put a lip-synced **video face** (the SkiaSharp Max Headroom avatar) on the voice —
the browser then shows his face alongside the audio, on either agent:

```bash
sipsorcery bridge web agent  --avatar --open                # mouth driven by Azure TTS visemes (phoneme-accurate)
sipsorcery bridge web openai --avatar --open                # mouth driven by the speech envelope (no visemes)
```

The two avatars differ in how the mouth is driven: the Azure `agent` has a viseme timeline, so its
lip-sync is phoneme-accurate; the `openai` voice has none, so its mouth is **audio-driven** from the
speech envelope (reads as generic talking that tracks the speech).

Either agent also answers the **phone**: a `sip:<uri>` peer places a SIP call and bridges the caller to
the other side, so someone on a handset can speak to the agent. The endpoints are symmetric, so the
agent can be on either side:

```bash
sipsorcery bridge sip:alice@example.com agent          # call alice, bridge her to the Azure agent
sipsorcery bridge sip:alice@example.com openai         # ...or to the OpenAI Realtime agent
sipsorcery bridge sip:1001@pbx.local agent --user 1000 --password secret   # authenticate the call
```

The SIP leg is **G.711** (8 kHz); every other endpoint speaks **48 kHz Opus**, so this peer transcodes
G.711 ↔ Opus at its boundary (audio is light enough to transcode in managed code — only video is
delegated to ffmpeg). The agent greets the moment the call is answered.

Adding **`--avatar`** turns it into a **video call**: the SIP leg offers a send-only H264 m-line and the
agent's lip-synced face is sent down it, so a **video softphone** (Linphone, Zoiper, …) shows the avatar
while you talk to it — the same `--avatar` that paints the face in the browser, no extra flag:

```bash
sipsorcery bridge sip:alice@example.com agent  --avatar    # Azure agent, viseme lip-sync, on the phone
sipsorcery bridge sip:alice@example.com openai --avatar    # OpenAI agent, envelope lip-sync, on the phone
```

An audio-only phone simply rejects the video m-line and the call carries on as audio. Pluggable/external
avatars are planned.

## Exit codes

| Code | Meaning |
|------|---------|
| 0 | Success. |
| 1 | Ran but did not achieve its goal (e.g. connected but no media flowed). |
| 2 | Invalid argument or option. |
| 3 | Timed out. |
| 4 | Transport/network error (DNS, connect, TLS). |

## Status

Early. The `route` engine carries audio and video over a single source → N sinks (a free tee), with
`whep:`/`sip:`/`livekit:`/`cloudflare:` sources, `whip:`/`web`/`livekit:`/`cloudflare` sinks and an
ffmpeg-backed audio-scope transform wired in. The `bridge` verb adds the duplex case (two endpoints both ways), with `web`, a
local Azure voice `agent`, an `openai` Realtime agent and a `sip:` phone peer (transcoded G.711 ↔ Opus)
as the endpoints, the agents each with an optional lip-synced `--avatar`. Streaming verbs default to
`-d 0` (run until ctrl-c); pass a positive `--duration` for a fixed window. Fan-in, richer transforms,
the remaining transport edges, pluggable/external avatars, and the authoring layers above the graph
(declarative routing, scripted policies, an external control API) are planned. Feedback and issues
welcome on the [SIPSorcery repo](https://github.com/sipsorcery-org/sipsorcery).
