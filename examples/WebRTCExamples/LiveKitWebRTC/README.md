# LiveKit WebRTC Demo

An ASP.NET example that uses [SIPSorcery](https://github.com/sipsorcery-org/sipsorcery) to
interoperate with [LiveKit](https://livekit.io/) by speaking the LiveKit
[signalling protocol](https://github.com/livekit/protocol) directly over a web socket.

On startup the app joins a LiveKit room and **publishes** a test pattern video (VP8) and a music
audio stream. It then serves a browser page that joins the same room as a **subscriber** (using
the official LiveKit JS client) and plays the tracks back. The LiveKit API key and secret stay
server-side; the browser only ever receives a short-lived, subscribe-only JWT.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- A LiveKit server. Either:
  - [LiveKit Cloud](https://cloud.livekit.io/) - create a project and grab its websocket URL,
    API key and API secret, or
  - a self-hosted server, e.g. `livekit-server --dev` which uses the well-known dev key/secret
    pair (`devkey` / `secret`) and listens on `ws://localhost:7880`.

## Configuration

The connection details are read from environment variables:

| Variable | Description |
| --- | --- |
| `LIVEKIT_API_KEY` | The LiveKit API key. |
| `LIVEKIT_API_SECRET` | The LiveKit API secret. |
| `LIVEKIT_WEBSOCKET_URL` | The server's websocket URL, e.g. `wss://<project>.livekit.cloud`. |
| `LIVEKIT_SIP_TRUNK_ID` | Optional. A SIP inbound trunk ID (`ST_...`). When set, inbound calls on the trunk are routed into the publisher's room. |

Windows (PowerShell):

```powershell
$env:LIVEKIT_API_KEY = "<your api key>"
$env:LIVEKIT_API_SECRET = "<your api secret>"
$env:LIVEKIT_WEBSOCKET_URL = "wss://<your project>.livekit.cloud"
```

Linux / macOS:

```bash
export LIVEKIT_API_KEY="<your api key>"
export LIVEKIT_API_SECRET="<your api secret>"
export LIVEKIT_WEBSOCKET_URL="wss://<your project>.livekit.cloud"
```

## Running

```bash
dotnet run
```

Once the console shows the publisher peer connection reaching the `connected` state, open
<http://localhost:8080> and click **Subscribe**. The test pattern video and audio should start
playing.

Press `Ctrl-C` to stop. On shutdown the app sends a `Leave` request so LiveKit removes the
publisher from the room immediately, then closes the web socket and both peer connections.

> The page is served over `http://localhost`, which browsers treat as a secure context, so WebRTC
> works without a TLS certificate. If you host it off `localhost` you will need HTTPS.

## How it works

```
 Browser ── HTTP (token only) ──►  This app (ASP.NET)
    │                                  │
    └────── WebRTC + signalling ───────┴── protobuf signalling over /rtc websocket ──► LiveKit
```

Unlike an SFU with a plain HTTPS API, LiveKit clients talk a protobuf signalling protocol
(`SignalRequest` / `SignalResponse`) over a web socket. This example implements just enough of
that protocol with hand-rolled `RTCPeerConnection`s to act as a publishing participant - useful
for understanding what the official SDKs do under the hood.

Every LiveKit participant has up to **two** peer connections with fixed negotiation directions:

- **Subscriber PC** - server → client media. The *server* initiates offers. This is the
  "primary" transport: LiveKit offers it immediately on join (it also carries the data
  channels), even for a participant that never subscribes to anything. The app answers it with
  no local tracks and connects with `auto_subscribe=false` since it only publishes.
- **Publisher PC** - client → server media. The *client* initiates offers, and only after the
  tracks have been registered.

### Publish flow

1. Mint a publisher JWT from the API key/secret (`AccessToken` from the
   [LiveKit .NET server SDK](https://github.com/livekit/server-sdk-dotnet)) and connect the
   `/rtc` web socket with it.
2. Receive the `Join` response confirming the participant (`Publisher-xxxxxxxx`) is in the room.
   The room name (`Room-xxxxxxxx`) is randomly generated on each run.
3. Send an `AddTrack` request for each track (video test pattern, music audio) and wait for the
   matching `TrackPublished` acknowledgement. LiveKit must know about the tracks **before** the
   SDP offer arrives so it can map the media sections to published tracks.
4. Create the publisher `RTCPeerConnection` with the two send-only tracks, send its offer, and
   apply LiveKit's `Answer`.
5. `Trickle` messages carry a target (`PUBLISHER` / `SUBSCRIBER`) and are routed to the matching
   peer connection.

> SIPSorcery's SDP does not include `a=msid` attributes, so LiveKit falls back to matching the
> incoming media to pending `AddTrack` registrations by media kind. With one audio and one video
> track this is unambiguous; the `cid` values are used to correlate the `TrackPublished`
> acknowledgements.

### Audio codec: publish OPUS

The publisher pins its audio to OPUS:

```csharp
_audioSource = new AudioExtrasSource(new AudioEncoder(includeOpus: true), ...);
_audioSource.RestrictFormats(format => format.Codec == AudioCodecsEnum.OPUS);
```

Both lines matter. The SIPSorcery `AudioEncoder` does not include OPUS by default, and without
the `RestrictFormats` pin the offer also lists G711/G722, leaving the outcome to LiveKit's
answer ordering.

Why it has to be OPUS: the LiveKit server is an SFU - it forwards published RTP verbatim and
never transcodes, so a track can only reach a subscriber that negotiated the same codec. The
room will happily *accept* a G711 publish, and browser subscribers will even play it (browsers
negotiate G711 natively), but LiveKit's own infrastructure components - the SIP bridge, egress,
agents - are OPUS-first on their room-facing side. The failure mode is nasty: every signalling
indicator stays green (track published, call active) while a G711 room track arrives at the SIP
bridge as pure silence. Publish what the platform's own clients publish: OPUS.

A related subtlety: the `2` in the SDP's `opus/48000/2` is fixed by RFC 7587 and does not mean
stereo is being sent. The actual channel count is signalled in-band per packet; this app sends
mono, which every OPUS decoder accepts.

### Subscribe flow (browser)

The browser is a normal LiveKit participant using the official
[livekit-client](https://github.com/livekit/client-sdk-js) JS SDK, loaded from a CDN. It only
needs a token:

1. Browser `POST /api/join`.
2. App mints a **subscribe-only** token (`CanSubscribe = true`, `CanPublish = false`) with a
   random `viewer-xxxxxxxx` identity and a 1 hour TTL, and returns
   `{ url, token, room, identity }`.
3. Browser calls `room.connect(url, token)` and attaches tracks on `TrackSubscribed` events.

### HTTP endpoints

| Method & path | Purpose |
| --- | --- |
| `POST /api/join` | Mints a subscribe-only LiveKit token and returns the join details. |

(`Program.cs` also contains a commented-out `GET /api/status` endpoint that reports the publisher
and subscriber peer connection states, handy when debugging the connection sequence.)

### Inbound SIP calls (optional)

If a [SIP inbound trunk](https://docs.livekit.io/sip/) is configured on the LiveKit project, set
`LIVEKIT_SIP_TRUNK_ID` to its ID and callers to the project's SIP URI will be dropped into the
same room as the publisher and the browser viewers. The caller hears the music track and the
browser viewers hear the caller.

LiveKit routes inbound calls with **dispatch rules**, which are persistent server-side config: a
rule must already exist when an INVITE arrives or the call is rejected. Because this example
uses a random room name per run, the app manages the rule itself on startup:

1. List the project's dispatch rules and delete any rule named `sipsorcery-demo-direct` left
   behind by a previous run (`CreateSIPDispatchRule` does not upsert, and duplicate rules on the
   same trunk would compete for calls).
2. Create a `dispatchRuleDirect` rule pointing the trunk at this run's room name.
3. On shutdown, delete the rule again so callers are cleanly rejected rather than landing in an
   empty room.

The SIP API calls use the same `SipServiceClient` from the LiveKit .NET server SDK, against the
HTTPS host that corresponds to the websocket URL.

## Files

| File | Description |
| --- | --- |
| `Program.cs` | ASP.NET host, LiveKit signalling client, publisher room service and token endpoint. |
| `wwwroot/index.html` | Browser subscriber page using the LiveKit JS client. |

## Notes

- The protobuf signalling types (`SignalRequest`, `AddTrackRequest`, `LeaveRequest`, etc.) come
  from the `Livekit.Server.Sdk.Dotnet` NuGet package, which embeds the
  [LiveKit protocol](https://github.com/livekit/protocol) definitions - no separate protobuf
  code generation step is needed.
- The web socket connects with `protocol=9` and `auto_subscribe=false`. Auto-subscribe only
  controls whether remote tracks are pushed to the subscriber PC; the subscriber PC itself is
  always established as the primary transport.
- On shutdown the app sends `LeaveRequest { Reason = ClientInitiated, Action = Disconnect }`.
  Without it the publisher would linger in the room until LiveKit's connection timeout expired,
  which can cause a stale-participant clash when the app is restarted with the same identity.
