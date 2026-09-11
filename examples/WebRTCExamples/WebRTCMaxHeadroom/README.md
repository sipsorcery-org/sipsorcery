# WebRTC Max Headroom Avatar

A demo that streams a **Max Headroom** talking avatar to a WebRTC browser — either a
photoreal **Wav2Lip** head driven from a single photo, or a stylised SkiaSharp cartoon.
Speech is synthesised in-process by **sherpa-onnx** (Piper voices), replies come from an
in-process **LLamaSharp** LLM, and you can **talk to** the avatar with your microphone
(speech-to-text via **Parakeet**, also in-process) or type into the Say / Ask boxes —
both drive the same reply pipeline. The whole stack runs offline inside one .NET
process, so the avatar containerises and deploys as a single service. Cloud engines
(ElevenLabs, OpenAI-compatible LLM gateways) remain available as alternatives.

```
 mic (browser)  ─► WebRTC audio ─► Parakeet STT (in-process) ─┐
 Ask box  ───────────────────────────────────────────────────┤
                                                              ▼
 LLamaSharp LLM (in-process)      sherpa-onnx TTS (in-process)
   text  ─────────────►  text  ───────────────►  16kHz PCM  ─────►  AudioExtrasSource ─► WebRTC audio
                                         │
                                         └──►  PushAudio(PCM windows)  ─►  IAvatarRenderer
                                                                          │  (Wav2Lip photoreal
                                                                          │   or SkiaSharp cartoon)
                                                                          ▼
                                                            H264/VP8 encode ─► WebRTC video
```

The speaker talks to the renderer only through **`IAvatarRenderer`** (BeginSpeech / PushAudio /
EndSpeech), so the animation engine is swappable via `AVATAR_RENDERER`: the **fully in-process**
`Wav2LipAvatarRenderer` (`wav2lip` — photoreal Wav2Lip via onnxruntime + SkiaSharp) or the in-box
`MaxHeadroomVideoSource` (`cartoon` — SkiaSharp; lip-sync is an amplitude→viseme heuristic).
This mirrors how LiveKit's avatar agents swap `tavus.AvatarSession` for `bithuman.AvatarSession`
behind one contract.

With `wav2lip` + sherpa-onnx TTS/STT + LLamaSharp, **every dependency runs in-process**:
deployment is `dotnet publish` plus model files on disk — no Python, no venvs, no servers.

## Pieces

| File | Role |
|------|------|
| `IAvatarRenderer.cs` | The swappable renderer seam: an `IVideoSource` driven by speech audio (`BeginSpeech` / `PushAudio` / `EndSpeech`). The same decoupling LiveKit's `AvatarSession` and bitHuman's `push_audio()` use. |
| `MaxHeadroomVideoSource.cs` | `IAvatarRenderer` that renders the head/mouth/glitch with SkiaSharp and emits raw BGR frames. `PushAudio` maps each window's loudness onto one of the 0-21 viseme mouth shapes. |
| `Wav2LipAvatarRenderer.cs` | The photoreal talking head, fully **in-process**: Wav2Lip via onnxruntime (DirectML/CPU), the validated `MelSpectrogram` front-end, and SkiaSharp compositing (matte, animated background, VHS grade, sway + blinks). No Python, no sidecar. Selected with `AVATAR_RENDERER=wav2lip`. |
| `MelSpectrogram.cs` | C# port of Wav2Lip's librosa mel front-end, validated against the Python output to ~1e-6 with `--mel-test`. |
| `LipSyncTtsSpeaker.cs` | Base class for the TTS engines: owns the shared pipeline (resample to 16kHz, stream to the audio track, and push real-time audio windows to the `IAvatarRenderer`). Concrete engines only implement `SynthesiseAsync`. |
| `SherpaTtsSpeaker.cs` | Local **in-process** engine — sherpa-onnx runs the same Piper VITS voices natively (NuGet carries the binaries incl. the espeak-ng phonemizer), so there is no external TTS process at all. The preferred local engine; set `SHERPA_MODEL_DIR`. |
| `ElevenLabsTtsSpeaker.cs` | Cloud ElevenLabs engine — POSTs to the API requesting raw `pcm_16000`. Best quality, but paid/online. Used when `ELEVENLABS_API_KEY` is set. |
| `ElevenLabsStreamingTtsSpeaker.cs` | Low-latency ElevenLabs engine — WebSocket fed by the LLM token stream, audio played as it arrives, mouth driven by each chunk's amplitude. Enabled with `ELEVENLABS_STREAMING=true`. |
| `IAvatarSpeaker.cs` | The speaking contract: `IAvatarSpeaker` (speak text) and `IStreamingAvatarSpeaker` (speak a text stream), so the batch and streaming engines plug in uniformly. |
| `SpeechRecognizer.cs` | Base class for the STT engines: owns the streaming front-end (energy VAD that buffers the 8kHz mic PCM into utterances). Concrete engines only implement `TranscribeAsync`. |
| `SherpaSpeechRecognizer.cs` | Local **in-process** STT via sherpa-onnx, sharing the TTS engine's native stack. Auto-detects the model family (NeMo transducer like Parakeet, or Whisper ONNX exports). Preferred when a model folder exists; validate with `--stt-test` (a TTS→STT round trip). |
| `ElevenLabsSpeechRecognizer.cs` | Cloud ElevenLabs "scribe" speech-to-text — POSTs each utterance (as a WAV) to the API. Used when `ELEVENLABS_API_KEY` is set. |
| `ElevenLabsStreamingSpeechRecognizer.cs` | Low-latency realtime STT — streams the 8kHz mic over a WebSocket (Scribe v2) with server-side VAD. Enabled with `ELEVENLABS_STREAMING=true`. |
| `ISpeechRecognizer.cs` | The listening contract (`StartAsync` / `Write` / `OnRecognized`), so the batch and streaming recognisers plug in uniformly. |
| `ILlmClient.cs` | The reply-generation contract (one-shot + sentence stream) both LLM clients implement, plus the shared Max persona prompt. |
| `LlamaSharpLlmClient.cs` | **In-process** LLM via LLamaSharp (llama.cpp) — runs the same GGUF models as Ollama with no external server. Set `LLM_GGUF`. |
| `LocalLlmClient.cs` | OpenAI-compatible HTTP chat client (Ollama / LM Studio / hosted gateway) for generating in-character replies. |
| `Program.cs` | ASP.NET host: `/offer` (send/recv WebRTC), `/say`, `/ask`. Routes both the Ask box and recognised speech through one shared `AskAsync`. |
| `wwwroot/index.html` | Browser client: connect (captures the mic), a mic mute toggle, plus the Say / Ask text boxes. |

## Everything in-process (the default)

The app runs its whole AI stack **inside the one .NET process** — speech-to-text
(sherpa-onnx running Parakeet), text-to-speech (sherpa-onnx running Piper voices), the
LLM (LLamaSharp) and the photoreal Wav2Lip avatar (onnxruntime + SkiaSharp). There is
nothing to install beyond the .NET
SDK: every dependency is a NuGet package, and the models are plain files. Each engine
looks in a conventional folder and activates automatically when its files are present,
so after the one-time downloads below **no environment variables are needed** — just
`dotnet run`.

One-time model downloads (~4.2 GB total):

```powershell
# 1. TTS voice (sherpa-onnx runs Piper voices natively; pick any vits-piper-* voice).
New-Item -ItemType Directory -Force C:\tools\sherpa-tts | Out-Null
Invoke-WebRequest -Uri "https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models/vits-piper-en_US-ryan-high.tar.bz2" -OutFile C:\tools\sherpa-tts\voice.tar.bz2
tar -xjf C:\tools\sherpa-tts\voice.tar.bz2 -C C:\tools\sherpa-tts; Remove-Item C:\tools\sherpa-tts\voice.tar.bz2

# 1b. STT model (needed for the avatar to LISTEN). Parakeet tdt 0.6b is the top open
#     English model on ASR leaderboards; the model family (NeMo transducer vs Whisper
#     export) is auto-detected from the folder contents, so any model from the sherpa-onnx
#     asr-models release works - e.g. sherpa-onnx-whisper-base.en is smaller/faster.
New-Item -ItemType Directory -Force C:\tools\sherpa-stt | Out-Null
Invoke-WebRequest -Uri "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8.tar.bz2" -OutFile C:\tools\sherpa-stt\model.tar.bz2
tar -xjf C:\tools\sherpa-stt\model.tar.bz2 -C C:\tools\sherpa-stt; Remove-Item C:\tools\sherpa-stt\model.tar.bz2

# 2. LLM (any chat-tuned GGUF dropped in C:\tools\llm is picked up automatically).
New-Item -ItemType Directory -Force C:\tools\llm | Out-Null
Invoke-WebRequest -Uri "https://huggingface.co/bartowski/Llama-3.2-3B-Instruct-GGUF/resolve/main/Llama-3.2-3B-Instruct-Q4_K_M.gguf" -OutFile C:\tools\llm\Llama-3.2-3B-Instruct-Q4_K_M.gguf

# 3. Avatar model + persona. The Wav2Lip ONNX checkpoint is distributed via Google Drive
#    (https://github.com/instant-high/wav2lip-onnx links it) - download checkpoints.zip in
#    a browser and extract wav2lip_gan.onnx to:
#        C:\tools\wav2lip\wav2lip-onnx\checkpoints\wav2lip_gan.onnx
#    Then drop a front-facing face photo as C:\tools\wav2lip\persona.jpg (.png/.webp fine)
#    and its figure matte as C:\tools\wav2lip\persona_alpha.png (below).
```

**Changing the persona** needs two one-time artefacts alongside the photo:

- a **figure matte** (`persona_alpha.png`, grayscale, figure = white) — generate it with
  [rembg](https://github.com/danielgatis/rembg) in a throwaway Python env (the app itself
  never runs Python; this is an offline asset-prep step, like exporting a texture):

  ```powershell
  py -3.11 -m venv C:\tools\rembg-venv
  C:\tools\rembg-venv\Scripts\pip install "rembg[cpu]" pillow
  C:\tools\rembg-venv\Scripts\python -c "from rembg import remove, new_session; from PIL import Image; import numpy as np; s=new_session('u2net_human_seg'); a=np.array(remove(Image.open(r'C:\tools\wav2lip\persona.jpg').convert('RGB'), session=s).split()[-1]); Image.fromarray(a).resize((640,480)).save(r'C:\tools\wav2lip\persona_alpha.png')"
  ```
  The figure must be **white on black**; if your tool emits the opposite (dark figure on a
  light background) the head shows the background through a transparent hole. The renderer
  auto-detects and flips an inverted matte (logging a warning), but regenerating it the right
  way round is cleaner.

- the **face box and eye rects** for the new face: set `NEURAL_FACE_BOX=y1,y2,x1,x2` and
  `NEURAL_EYES=x,y,w,h` (coordinates at 640x480 — eyeball them in any image editor). The
  baked defaults match the bundled Max persona.

Sanity-check each engine without a browser: `dotnet run -- --tts-test "hi"`,
`-- --stt-test "hi"` (a TTS→STT round trip), `-- --llm-test "hi"`,
`-- --avatar-test <raw-pcm> <out-dir>`.

| Engine | Conventional path (auto-detected) | Override |
|---|---|---|
| TTS | `C:\tools\sherpa-tts\vits-piper-en_US-ryan-high` | `SHERPA_MODEL_DIR` |
| STT | first model folder under `C:\tools\sherpa-stt` | `SHERPA_STT_DIR` (+ `SHERPA_STT_PROVIDER`) |
| LLM | first `*.gguf` in `C:\tools\llm` | `LLM_GGUF` (+ `LLM_GPU_LAYERS`) |
| Avatar model | `C:\tools\wav2lip\wav2lip-onnx\checkpoints\wav2lip_gan.onnx` | `WAV2LIP_ONNX` |
| Persona / matte | `C:\tools\wav2lip\persona.{jpg,png,webp}` / `persona_alpha.png` | `NEURAL_PERSONA` / `NEURAL_MATTE` |
| Face box / eyes | baked defaults for the bundled persona | `NEURAL_FACE_BOX` / `NEURAL_EYES` |
| Renderer choice | `wav2lip` when its files exist, else the cartoon | `AVATAR_RENDERER` (`wav2lip`/`cartoon`) |

When a model folder is missing the app degrades gracefully (cartoon renderer, verbatim
replies, speaking without listening) — and the sections below describe the cloud
alternatives (ElevenLabs, OpenAI-compatible LLM gateways), which take priority per
their own env vars.

## Cloud alternatives

1. **ElevenLabs (speech).** For the most natural voices you can use
   [ElevenLabs](https://elevenlabs.io). It is a **paid cloud service** (so it reintroduces
   an external dependency and a per-character cost — the opposite of the offline goal),
   but the quality is the best of the options here. Setting an API key switches **both**
   the voice (TTS) and the listening (STT, via the ElevenLabs "scribe" model) to
   ElevenLabs, taking priority over the local engines:

   ```powershell
   $env:ELEVENLABS_API_KEY   = "<your key>"
   $env:ELEVENLABS_VOICE_ID  = "21m00Tcm4TlvDq8ikWAM"   # optional, TTS voice, default "Rachel"
   $env:ELEVENLABS_MODEL     = "eleven_turbo_v2_5"        # optional, TTS model, low-latency default
   $env:ELEVENLABS_STT_MODEL = "scribe_v1"                # optional, STT model default
   ```
   ```bash
   export ELEVENLABS_API_KEY=<your key>
   ```

   Find voice ids in your ElevenLabs dashboard or via `GET https://api.elevenlabs.io/v1/voices`.
   The app requests raw `pcm_16000` audio, so there is no MP3 decode or resample — it feeds
   straight into the same lip-sync pipeline as the local engine.

   **Low-latency streaming (`ELEVENLABS_STREAMING=true`).** Switches both directions to
   ElevenLabs WebSockets:
   - **TTS** opens `/stream-input`, is fed the LLM's token stream as it is generated, and
     plays audio chunks as they arrive — so the avatar starts talking sooner. Lip-sync is
     driven by the amplitude of each audio chunk as it plays (an earlier version used the
     API's alignment timestamps, but those drift against chunked playback and froze the mouth
     partway through long sentences).
   - **STT** opens `/v1/speech-to-text/realtime` (Scribe v2) and streams the 8kHz mic straight
     up with **server-side VAD** — no local buffer-until-silence — so listening is lower
     latency. Final (`committed`) transcripts drive the same LLM→speak path.
   ```powershell
   $env:ELEVENLABS_STREAMING = "true"
   ```
   This is a prototype; the realtime STT commit strategy and message field names are best
   confirmed against your account.

2. **OpenAI-compatible LLM endpoint.** Instead of the in-process LLamaSharp engine, any
   server exposing an OpenAI-compatible chat endpoint works — a local
   [Ollama](https://ollama.com)/LM Studio, or a hosted gateway. Set `LLM_ENDPOINT` to
   its URL and `LLM_MODEL` accordingly (e.g. `http://localhost:11434/v1/chat/completions`
   + `llama3.2` for Ollama, or `anthropic/claude-3.5-sonnet` on OpenRouter with
   `LLM_API_KEY` sent as a Bearer token). `LLM_GGUF` takes priority when both are set.
   To GPU-offload the in-process engine instead, add a LLamaSharp backend package
   (e.g. `LLamaSharp.Backend.Vulkan`) and set `LLM_GPU_LAYERS=-1`.

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

## Docker

The image publishes the application for Linux x64 and includes its native runtime
dependencies: .NET 8, FFmpeg 8 shared libraries (including the OpenH264 encoder),
SkiaSharp dependencies, the CPU ONNX runtime, sherpa-onnx and the LLamaSharp CPU
backend. Model weights and the Wav2Lip persona are data rather than image layers;
mount them read-only at `/models` so they can be updated without rebuilding a
multi-gigabyte image.

Build from the repository root (the project references other projects in this repo):

```powershell
docker build `
  -f examples/WebRTCExamples/WebRTCMaxHeadroom/Dockerfile `
  -t sipsorcery/webrtc-max-headroom:local .
```

The supplied Compose file maps the conventional `C:\tools` model tree into the
container and configures all of the Linux model paths. From this directory:

```powershell
Copy-Item .env.example .env       # optional; edit MAX_MODELS_ROOT for another location
docker compose up --build
```

Open <http://localhost:8080>. As in `WebRTCGetStartedFFmpeg`, SIPSorcery binds an
OS-selected port and gathers all interface ICE candidates. Compose uses host networking,
which makes the complete ephemeral UDP range available without a fixed WebRTC port, SDP
candidate rewriting, or tens of thousands of Docker NAT mappings. Docker Desktop users
must enable host networking in Docker Desktop settings.

To run without Compose (the cartoon renderer works without model files):

```powershell
docker run --rm --network host `
  sipsorcery/webrtc-max-headroom:local
```

For a full local instance, mount the model root explicitly:

```powershell
docker run --rm --network host `
  --mount type=bind,source=C:\tools,target=/models,readonly `
  sipsorcery/webrtc-max-headroom:local
```

The image exposes `/healthz` and has a Docker health check. For access from another
machine, allow the host's ephemeral UDP range and terminate HTTPS in a reverse proxy
(browser microphone access requires a secure context). Internet/NAT deployment will
generally require a STUN/TURN configuration appropriate to that network.

## Notes / next steps

- The CARTOON renderer's lip-sync maps audio loudness onto a handful of the 22 viseme
  ids (0-21) in `MaxHeadroomVideoSource._visemeShapes` — approximate compared to
  phoneme-accurate visemes. For tighter sync, extract the TTS engine's phoneme sequence
  (sherpa's Piper voices phonemise via eSpeak NG) and map phonemes→viseme ids instead of
  the amplitude envelope. The Wav2Lip renderer needs none of this — its model generates
  the mouth directly from the audio.
- Audio is G.711 from `AudioExtrasSource.SendAudioFromStream` (TTS PCM resampled to 16kHz).
  The audio track is send/recv and pinned to PCMU, so the received mic is a deterministic
  8kHz stream the recogniser consumes; speech-to-text is therefore telephone-grade. Pinning
  Opus (wideband) would improve recognition.
- The demo drives a single connected viewer; the most recent peer connection
  receives `/say`, `/ask` and the microphone.
- Everything runs in one process with no cloud dependency. The Linux container uses CPU
  execution for speech, LLM and Wav2Lip; DirectML remains the Windows execution provider.
  Wav2Lip on CPU is functional but may not sustain 25 fps on all hosts.
