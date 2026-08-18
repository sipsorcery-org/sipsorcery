# .NET Library for Google's Gemini Live API

This library provides a .NET client for Google's
[Gemini Live API](https://ai.google.dev/gemini-api/docs/live-api) — the `BidiGenerateContent`
WebSocket protocol used for low-latency, streaming voice (and text) conversations with Gemini.

It follows the same architectural pattern as the sibling
[SIPSorcery.OpenAI.Realtime](../SIPSorcery.OpenAI.Realtime/README.md) library (an `EndPoint` +
`Messenger` + strongly typed JSON models), but the transport is different because the underlying
API is different: Gemini Live does not offer WebRTC signalling. It is a single, long-lived
**WebSocket** connection carrying JSON messages, with raw **PCM16** audio (16 kHz input, 24 kHz
output) instead of Opus/RTP. As a result this library has no dependency on `SIPSorcery.Net` or
`RTCPeerConnection` — it only needs a plain `System.Net.WebSockets.ClientWebSocket`.

> **Model/enum names may change.** Google revises Gemini Live model identifiers and some enum
> values relatively often. The values shipped in `GeminiLiveModelsEnum` and similar enums reflect
> documentation reviewed in August 2026 — check
> [the current docs](https://ai.google.dev/gemini-api/docs/live-api) before relying on them in
> production. Because `GeminiSetup.Model` is a plain string, a newer model id can always be used
> directly even if it isn't in the enum yet.

## Authentication

The `apiKey` passed to `GeminiLiveEndPoint`/`GeminiLiveWebSocketClient` is sent as the `key`
query parameter on the WebSocket URL, same as the rest of the Generative Language API. This
accepts either:

- A plain Gemini API key from [Google AI Studio](https://aistudio.google.com/) (format
  `AIzaSy...`), or
- A short-lived ephemeral token minted via the Live API's `auth_tokens` endpoint (format
  `AQ....`), recommended for client-side/browser use so a long-lived key is never exposed —
  ephemeral tokens are a drop-in replacement for the API key, not a separate auth mechanism.

If the connection closes immediately after `StartConnect` with no messages received, check the
logged `CloseStatus`/`CloseStatusDescription` from `GeminiLiveWebSocketClient` — this almost
always indicates an authentication problem (expired/invalid key or token). A `PolicyViolation`
close with a message like "doesn't allow unregistered callers" means the key/token wasn't
recognised at all (e.g. sent under the wrong query parameter, or empty).

## Features

- Establishes the `BidiGenerateContent` WebSocket session and sends the initial setup message.
- Streams raw PCM16 audio in both directions. Outbound audio goes through a bounded queue drained
  by a single writer, so chunks reach the socket in the order they were captured no matter how many
  threads call `SendAudio`, and a congested network drops the newest audio (counted in
  `DroppedAudioChunks`) instead of queueing without limit.
- Strongly typed client/server message models with a defensive JSON parser: a payload this library
  can't bind degrades to a `GeminiUnknownServerMessage` carrying the original JSON, unknown values
  in optional enums are ignored, and neither a malformed message nor an exception thrown by one of
  your event handlers can end the session — the receive loop logs it and carries on.
- Function/tool calling (`GeminiServerEventToolCall` / `GeminiToolResponseMessage`).
- Barge-in support via `OnInterrupted`.
- Designed to work with dependency injection (ASP.NET) or standalone console/WinForms apps.

## Token usage

Gemini's server message carries `usageMetadata` as a sibling of the message-type union rather than a
member of it, so it usually arrives in the same JSON object as a `serverContent` message. Read it
from `GeminiServerMessage.UsageMetadata`, which is populated on whichever message it came with:

```csharp
geminiEndPoint.OnServerMessage += message =>
{
    if (message.UsageMetadata is { TotalTokenCount: { } totalTokens })
    {
        Log.Information("Gemini session tokens so far: {TotalTokens}", totalTokens);
    }
};
```

A `GeminiServerEventUsageMetadata` message is surfaced only when usage arrives on its own.

## Installation

```bash
dotnet add package SIPSorcery.Gemini.Realtime
```

## Usage

See [GetStarted](https://github.com/sipsorcery-org/sipsorcery/tree/master/examples/GeminiExamples/GetStarted)
for a full console example that wires this up to a microphone/speakers with NAudio.

```csharp
using SIPSorcery.Gemini.Realtime;
using SIPSorcery.Gemini.Realtime.Models;

var geminiEndPoint = new GeminiLiveEndPoint(geminiApiKey, loggerFactory);

geminiEndPoint.OnConnected += () =>
{
    Log.Logger.Information("Gemini Live session established.");
};

geminiEndPoint.OnAudioReceived += (pcm16, sampleRateHz) =>
{
    // pcm16 is raw little-endian PCM16 mono audio, sampleRateHz is normally 24000.
    playbackDevice.AddSamples(pcm16, 0, pcm16.Length);
};

geminiEndPoint.OnInterrupted += () =>
{
    // The user started talking while the model was still speaking (barge-in).
    // Discard any audio already queued for playback.
    playbackDevice.ClearBuffer();
};

geminiEndPoint.OnServerMessage += message =>
{
    var log = message switch
    {
        GeminiServerEventContent { OutputTranscription.Text: { Length: > 0 } text } => $"AI: {text}",
        GeminiServerEventContent { InputTranscription.Text: { Length: > 0 } text } => $"ME: {text}",
        _ => string.Empty
    };

    if (log != string.Empty)
    {
        Log.Information(log);
    }
};

var setup = new GeminiSetup
{
    GenerationConfig = new GeminiGenerationConfig
    {
        ResponseModalities = [GeminiResponseModalityEnum.AUDIO],
        SpeechConfig = new GeminiSpeechConfig
        {
            VoiceConfig = new GeminiVoiceConfig
            {
                PrebuiltVoiceConfig = new GeminiPrebuiltVoiceConfig { VoiceName = GeminiVoiceEnum.Puck }
            }
        }
    },
    InputAudioTranscription = new GeminiAudioTranscriptionConfig(),
    OutputAudioTranscription = new GeminiAudioTranscriptionConfig()
};

var connectResult = await geminiEndPoint.StartConnect(GeminiLiveModelsEnum.Gemini25FlashNativeAudioLatest, setup);

// From your microphone capture callback (16 kHz/16-bit/mono PCM expected):
geminiEndPoint.SendAudio(pcm16Chunk);

// When the session is finished. DisposeAsync is preferred over Dispose: it lets the WebSocket close
// handshake complete and the queued audio drain, which the synchronous path has to skip.
await geminiEndPoint.DisposeAsync();
```

## Examples

- **GetStarted** — minimal console program that connects your microphone to Gemini.
- **GetStartedSIP** — registers as a SIP extension on a PBX and bridges incoming phone calls to
  Gemini, so a caller can talk to the assistant over a normal phone call.

## License

Distributed under the BSD 3‑Clause license with an additional BDS BY‑NC‑SA restriction. See
[LICENSE.md](https://github.com/sipsorcery-org/sipsorcery/tree/master/LICENSE.md) for details.
