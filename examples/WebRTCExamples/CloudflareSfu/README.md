# Cloudflare Realtime SFU Demo

An ASP.NET example that uses [SIPSorcery](https://github.com/sipsorcery-org/sipsorcery) with the
[Cloudflare Realtime SFU](https://developers.cloudflare.com/realtime/sfu/) HTTPS API.

On startup the app **publishes** a test pattern video (VP8) and a music audio stream to the SFU.
It then serves a browser page that **subscribes** to those tracks and plays them back. All
Cloudflare API calls are proxied server-side, so the API token is never sent to the browser and
the publisher session ID never has to be copied and pasted.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- A Cloudflare Realtime SFU application. Follow the
  [get started guide](https://developers.cloudflare.com/realtime/sfu/get-started/) to create one
  and obtain its **App ID** and **API token**.

## Configuration

The App ID and API token are read from environment variables:

| Variable | Description |
| --- | --- |
| `CLOUDFLARE_APPID` | Your Cloudflare Realtime SFU application ID. |
| `CLOUDFLARE_API_TOKEN` | The bearer token for the application. |

Windows (PowerShell):

```powershell
$env:CLOUDFLARE_APPID = "<your app id>"
$env:CLOUDFLARE_API_TOKEN = "<your api token>"
```

Linux / macOS:

```bash
export CLOUDFLARE_APPID="<your app id>"
export CLOUDFLARE_API_TOKEN="<your api token>"
```

## Running

```bash
dotnet run
```

Once you see `Publisher tracks pushed...` in the console, open <http://localhost:8080> and click
**Subscribe**. The test pattern video and audio should start playing.

Press `Ctrl-C` to stop. On shutdown the app force-closes the publisher session's tracks so
Cloudflare can reclaim the session.

> The page is served over `http://localhost`, which browsers treat as a secure context, so WebRTC
> works without a TLS certificate. If you host it off `localhost` you will need HTTPS.

## How it works

```
 Browser  ── HTTP/JSON ──►  This app (ASP.NET)  ── HTTPS + token ──►  Cloudflare SFU
   (subscriber PC)              (publisher PC)
```

The app has two roles:

1. **Publisher** – a `CloudflareSfuService` hosted service creates a session, builds an
   `RTCPeerConnection`, and pushes the local audio/video tracks to Cloudflare (`location: local`).
   It holds the resulting session ID and track names in memory.
2. **Proxy + page host** – the browser only ever talks to this app, which forwards the SFU calls
   using the server-held token.

### Subscribe flow

Pulling remote tracks is **not** the same as publishing: the browser does not send its own offer.
Cloudflare adds the m-lines server-side and returns an offer that the browser answers.

1. Browser `POST /api/subscribe`.
2. App creates a subscriber session and pulls the publisher's tracks (`location: remote`).
   Cloudflare responds with `requiresImmediateRenegotiation` and an **offer** SDP.
3. App returns `{ subscriberSessionId, sdp }` to the browser.
4. Browser `setRemoteDescription(offer)` → `createAnswer()` → `setLocalDescription(answer)`.
5. Browser `POST /api/renegotiate` with the answer SDP.
6. App `PUT`s the answer to Cloudflare's renegotiate endpoint, completing the connection.

### HTTP endpoints

| Method & path | Purpose |
| --- | --- |
| `GET /api/publisher` | Returns the publisher session ID and track names (no secrets). |
| `POST /api/subscribe` | Creates a subscriber session, pulls the publisher tracks, returns Cloudflare's offer. |
| `POST /api/renegotiate` | Forwards the browser's answer SDP to Cloudflare. |

## Files

| File | Description |
| --- | --- |
| `Program.cs` | ASP.NET host, publisher service, and proxy endpoints. |
| `wwwroot/index.html` | Browser subscriber page. |
| `RealtimeSfu/` | [Kiota](https://github.com/microsoft/kiota)-generated Cloudflare Realtime API client. |

The Kiota client was generated with:

```bash
kiota generate -l CSharp -d https://developers.cloudflare.com/realtime/static/calls-api-2024-05-21.yaml -c RealtimeSfuClient -n Cloudflare.Realtime.Sfu -o ./RealtimeSfu --exclude-backward-compatible --clean-output
```

## Notes

- The Cloudflare SFU API has no "list sessions" or "delete session" endpoint. Sessions are
  reclaimed automatically once their tracks are closed and the WebRTC transport drops, which is
  why the app force-closes its tracks on shutdown.
