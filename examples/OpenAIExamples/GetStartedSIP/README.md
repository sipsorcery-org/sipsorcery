# OpenAI Realtime SIP Get Started Example

This example demonstrates placing a SIP call to OpenAI's Realtime SIP endpoint and then upgrading that call to a realtime WebSocket session after an incoming webhook. Audio is captured from the default Windows input/output devices (via SIPSorcery + Windows audio endpoint) and sent using PCM (Opus is not currently negotiated successfully as of 05 Sep 2025).

> ⚠️ Note (05 Sep 2025): The example successfully places a SIP call, receives the webhook, accepts the call, and establishes a realtime WebSocket. Echo cancellation is NOT implemented. Use a headset or a device with hardware echo cancellation to avoid the assistant talking to itself.

## What This Sample Does

1. Starts a minimal ASP.NET Core web server to receive OpenAI webhook callbacks at `/webhook`.
2. Places an outbound SIP TLS call to `sip.api.openai.com` using your OpenAI Project ID as the user part: `<PROJECT_ID>@sip.api.openai.com`.
3. Waits for OpenAI to POST a webhook containing the `call_id`.
4. Accepts the call via `POST /v1/realtime/calls/{call_id}/accept`.
5. Opens a realtime WebSocket: `wss://api.openai.com/v1/realtime?call_id=...`.
6. Sends an initial `response.create` instruction ("Say Hi.").
7. Logs all incoming WebSocket text messages (JSON events from OpenAI).
8. Streams audio between your local microphone/speakers and OpenAI (PCM).

## Requirements

- Windows OS (for WindowsAudioEndPoint in this demo)
- .NET 8.0 SDK
- OpenAI API key with Realtime + SIP access
- OpenAI Project ID (e.g. `proj_...`)
- A publicly accessible HTTPS endpoint for webhooks (ngrok recommended)

## Environment Variables

Set these before running:

Windows (cmd.exe):
```
set OPENAI_API_KEY=your_openai_key
set OPENAI_PROJECT_ID=your_openai_project_id
```

PowerShell:
```
$env:OPENAI_API_KEY="your_openai_key"
$env:OPENAI_PROJECT_ID="your_openai_project_id"
```

## Exposing the Webhook (ngrok)

1. Reserve / configure a domain in the ngrok dashboard (recommended) or use a temporary forwarding URL.
2. In the OpenAI dashboard: Settings -> Webhooks -> Add webhook
   - URL: `https://<your-ngrok-domain>/webhook`
3. Start ngrok to forward to the Kestrel HTTPS port from `launchSettings.json` (default shown there is `https://localhost:53742`):
```
ngrok http --url=<your-ngrok-domain> https://localhost:53742
```

## Run

```
dotnet run
```

You should see logs indicating:
- Web server started
- SIP call attempt to `<PROJECT_ID>@sip.api.openai.com;transport=tls`
- Incoming webhook with `call_id`
- Accept POST success
- WebSocket connected and subsequent JSON event logs

## File Overview

### Program.cs
Core sample logic:
- Configures Serilog logging.
- Validates `OPENAI_API_KEY` and `OPENAI_PROJECT_ID` env vars.
- Registers an HTTP POST `/webhook` endpoint to receive call events.
- On webhook: extracts `call_id`, sends accept request, starts WebSocket task.
- Initiates SIP call using SIPSorcery (`SIPUserAgent`, `VoIPMediaSession`).
- Opens WebSocket and sends an initial `response.create` instruction.
- Streams and logs incoming WebSocket messages.

### launchSettings.json
Specifies the local HTTPS port (used for your ngrok forwarding target).

## Audio Notes

- Example uses `WindowsAudioEndPoint` with default input/output devices.
- Opus was attempted (commented line) but PCM only negotiates successfully at time of writing.
- No echo cancellation; prefer headset.

## Customizing

- Change initial instruction: edit the anonymous object `responseCreate` in `StartWebSocketConnection`.
- Provide different model/instructions for acceptance by altering `call_accept` record fields.
- Add parsing of WebSocket JSON events to handle partial transcripts, tool calls, etc.

## Troubleshooting

- No webhook received: verify ngrok is running and the correct HTTPS URL is registered in OpenAI settings.
- 401 / auth errors: confirm `OPENAI_API_KEY` environment variable is set in the same shell you run `dotnet run`.
- SIP call fails immediately: check outbound TLS (firewall/proxy) and that your Project ID is correct.
- WebSocket closes: inspect logged close status and ensure accept POST succeeded.

## Security

- Do NOT hardcode your API key. Use environment variables or a secure secrets store.
- Restrict exposure of your webhook endpoint. ngrok URLs are public; rotate as needed.

## License

BSD 3-Clause "New" or "Revised" License and the additional BDS BY-NC-SA restriction. See `LICENSE.md` for full details.