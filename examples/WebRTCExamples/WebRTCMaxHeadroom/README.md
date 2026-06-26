# WebRTC Max Headroom Avatar

A demo that streams a stylised **Max Headroom** style talking avatar to a WebRTC
browser. The face is rendered with SkiaSharp, speech is synthesised with Azure
Cognitive Services, and the mouth is lip-synced to the Azure **viseme** events.
An optional local LLM generates the replies. You can **talk to** the avatar with
your microphone (Azure speech-to-text), or type into the Say / Ask boxes — both
drive the same reply pipeline.

```
 mic (browser)  ─► WebRTC audio ─► Azure Speech STT ─┐
 Ask box  ───────────────────────────────────────────┤
                                                      ▼
 local LLM (optional)            Azure Speech TTS
   text  ─────────────►  text  ───────────────►  16kHz PCM  ─────►  AudioExtrasSource ─► WebRTC audio
                                         │
                                         └──►  viseme timeline  ─►  MaxHeadroomVideoSource (SkiaSharp)
                                                                          │  raw BGR frames
                                                                          ▼
                                                          VideoEncoderEndPoint (VP8) ─► WebRTC video
```

## Pieces

| File | Role |
|------|------|
| `MaxHeadroomVideoSource.cs` | `IVideoSource` that renders the head/mouth/glitch with SkiaSharp and emits raw BGR frames. Mouth shape is driven by `CurrentViseme`. |
| `AzureTtsSpeaker.cs` | Calls Azure TTS, collects the viseme timeline, streams PCM to the audio track and walks the timeline to drive the mouth in sync. |
| `AzureSpeechRecognizer.cs` | Azure speech-to-text: the received microphone audio (decoded to 8kHz PCM) is pushed in and each recognised utterance is raised for the LLM→speak path. Lets you talk to the avatar. |
| `LocalLlmClient.cs` | Optional OpenAI-compatible chat client (Ollama / LM Studio / llama.cpp) for generating in-character replies. |
| `Program.cs` | ASP.NET host: `/offer` (send/recv WebRTC), `/say`, `/ask`. Routes both the Ask box and recognised speech through one shared `AskAsync`. |
| `wwwroot/index.html` | Browser client: connect (captures the mic), a mic mute toggle, plus the Say / Ask text boxes. |

## Prerequisites

1. An Azure Speech resource. Set:
   ```powershell
   $env:AZURE_SPEECH_KEY    = "<your key>"
   $env:AZURE_SPEECH_REGION = "westeurope"     # your resource region
   $env:AZURE_SPEECH_VOICE  = "en-US-GuyNeural" # optional, must be a neural voice (visemes)
   ```
   Without these the avatar still renders but cannot speak.

2. (Optional) A local LLM exposing an OpenAI-compatible chat endpoint, e.g. Ollama:
   ```powershell
   $env:LLM_ENDPOINT = "http://localhost:11434/v1/chat/completions"
   $env:LLM_MODEL    = "llama3.2"
   ```
   If unset, `/ask` just speaks the prompt verbatim.

## Run

```powershell
dotnet run
```

Then browse to <https://localhost:5443> (accept the dev cert), click **Connect**
and the avatar should appear and greet you. The browser asks for **microphone**
permission — grant it and just **talk to him**; what you say is recognised and
answered. You can also use the **Say** box to speak text verbatim, the **Ask** box
to route a prompt through the LLM, or **Mute mic** to stop listening. Use a headset
(or rely on the browser's echo cancellation) so he doesn't hear himself.

Render a still frame without a call (sanity check):

```powershell
dotnet run -- --snapshot      # writes maxheadroom_visemeN.png files
```

## Notes / next steps

- Lip-sync uses Azure's 22 viseme ids (0-21) mapped to mouth shapes in
  `MaxHeadroomVideoSource._visemeShapes`. Swap these for real sprites for a nicer look.
- Video is VP8 via the libvpx `VideoEncoderEndPoint` (same as `WebRTCGetStartedLibvpx`).
- Audio is G.711 from `AudioExtrasSource.SendAudioFromStream` (Azure PCM @ 16kHz, resampled).
  The audio track is send/recv and pinned to PCMU, so the received mic is a deterministic
  8kHz stream the recogniser consumes; speech-to-text is therefore telephone-grade. Pinning
  Opus (wideband) would improve recognition.
- The demo drives a single connected viewer; the most recent peer connection
  receives `/say`, `/ask` and the microphone.
- On Linux the `SkiaSharp.NativeAssets.Linux` package is referenced; you may need
  `libfontconfig1` installed.
