# Gemini Live SIP Gateway Example

Registers as a SIP extension on a PBX (e.g. Asterisk), automatically answers incoming calls, and
bridges the call audio to Google's [Gemini Live API](https://ai.google.dev/gemini-api/docs/live-api)
— so a caller can have a voice conversation with the assistant over a normal phone call.

## How it works

1. Registers with the PBX using `SIPRegistrationUserAgent` (re-registers every 120s).
2. Listens for incoming `INVITE` requests to that registration and auto-answers them, negotiating
   PCMU (G.711 μ-law, 8kHz) — universally supported, and the PBX transcodes for us if the actual
   external caller used a different codec.
3. For each answered call, opens a `GeminiLiveEndPoint` and bridges audio in both directions:
   - Caller → Gemini: PCMU @ 8kHz → decode → PCM16 @ 8kHz → resample → PCM16 @ 16kHz.
   - Gemini → Caller: PCM16 @ 24kHz → resample → PCM16 @ 8kHz → encode → PCMU @ 8kHz.
   Decoding/encoding uses SIPSorcery's own `SIPSorcery.Media.AudioEncoder`; resampling uses
   `SIPSorcery.Media.PcmResampler`.
4. Logs the caller/assistant transcript to the console (`CALLER ✅` / `AI ✅`) when the caller hangs
   up or the call ends.

## Requirements

- .NET 10 SDK
- A SIP PBX (e.g. Asterisk) with an extension you can register as
- A Gemini API key

## Configuration

Settings are read from user secrets first, then environment variables (which take precedence).

User secrets (recommended — nothing lands in your shell history or the repo):

```bash
dotnet user-secrets set GEMINI_API_KEY your_gemini_api_key
dotnet user-secrets set ASTERISK_SIP_SERVER sip:192.168.1.7
dotnet user-secrets set ASTERISK_SIP_USERNAME your_sip_username
dotnet user-secrets set ASTERISK_SIP_PASSWORD your_sip_password
dotnet run
```

Or environment variables:

```bash
set GEMINI_API_KEY=your_gemini_api_key
set ASTERISK_SIP_SERVER=sip:192.168.1.7
set ASTERISK_SIP_USERNAME=your_sip_username
set ASTERISK_SIP_PASSWORD=your_sip_password
dotnet run
```

All four settings are required — the app exits with an error if any is missing.
**Do not hardcode the password (or API key) in source** — always supply them via user secrets or
environment variables, and don't commit them.

## Limitations

- No authentication/call-routing logic — every call that reaches the registered extension is
  accepted.
- No echo cancellation is relevant here (unlike the microphone-based GetStarted example) since
  audio only ever flows over RTP/WebSocket, never through local speakers/mic.
- PCMU is narrowband (8kHz) — voice quality is phone-call quality, not the full-bandwidth audio
  the plain WebSocket [GetStarted](../GetStarted/README.md) example produces.

## License

BSD 3-Clause "New" or "Revised" License and the additional BDS BY-NC-SA restriction. See
[LICENSE.md](https://github.com/sipsorcery-org/sipsorcery/tree/master/LICENSE.md) for details.
