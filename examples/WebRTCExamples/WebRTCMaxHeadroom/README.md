# WebRTC Max Headroom Avatar

A demo that streams a stylised **Max Headroom** style talking avatar to a WebRTC
browser. The face is rendered with SkiaSharp, speech is synthesised by a pluggable
TTS engine — local **Piper** or cloud **ElevenLabs** — and the mouth is lip-synced
to an **amplitude envelope** of that audio.
An optional local LLM generates the replies. You can **talk to** the avatar with
your microphone (speech-to-text via local **Whisper** or cloud **ElevenLabs**), or
type into the Say / Ask boxes — both drive the same reply pipeline. With the local
engines everything runs offline with no cloud dependency, so the avatar can be
containerised and deployed to Kubernetes.

```
 mic (browser)  ─► WebRTC audio ─► Whisper STT (local) ─┐
 Ask box  ────────────────────────────────────────────┤
                                                       ▼
 local LLM (optional)             Piper TTS (local)
   text  ─────────────►  text  ───────────────►  16kHz PCM  ─────►  AudioExtrasSource ─► WebRTC audio
                                         │
                                         └──►  amplitude envelope  ─►  MaxHeadroomVideoSource (SkiaSharp)
                                                                          │  raw BGR frames
                                                                          ▼
                                                          VideoEncoderEndPoint (VP8) ─► WebRTC video
```

## Pieces

| File | Role |
|------|------|
| `MaxHeadroomVideoSource.cs` | `IVideoSource` that renders the head/mouth/glitch with SkiaSharp and emits raw BGR frames. Mouth shape is driven by `CurrentViseme`. |
| `LipSyncTtsSpeaker.cs` | Base class for the TTS engines: owns the shared pipeline (resample to 16kHz, stream to the audio track, drive the mouth from an RMS amplitude envelope). Concrete engines only implement `SynthesiseAsync`. |
| `PiperTtsSpeaker.cs` | Local Piper engine — HTTP server (recommended) or per-utterance child process. Returns 16-bit PCM for the base class to play. |
| `ElevenLabsTtsSpeaker.cs` | Cloud ElevenLabs engine — POSTs to the API requesting raw `pcm_16000`. Best quality, but paid/online. Used when `ELEVENLABS_API_KEY` is set. |
| `ElevenLabsStreamingTtsSpeaker.cs` | Low-latency ElevenLabs engine — WebSocket fed by the LLM token stream, audio played as it arrives, mouth driven by each chunk's amplitude. Enabled with `ELEVENLABS_STREAMING=true`. |
| `IAvatarSpeaker.cs` | The speaking contract: `IAvatarSpeaker` (speak text) and `IStreamingAvatarSpeaker` (speak a text stream), so the batch and streaming engines plug in uniformly. |
| `SpeechRecognizer.cs` | Base class for the STT engines: owns the streaming front-end (energy VAD that buffers the 8kHz mic PCM into utterances). Concrete engines only implement `TranscribeAsync`. |
| `WhisperSpeechRecognizer.cs` | Local Whisper.net speech-to-text — resamples each utterance to 16kHz and transcribes it offline. The default STT. |
| `ElevenLabsSpeechRecognizer.cs` | Cloud ElevenLabs "scribe" speech-to-text — POSTs each utterance (as a WAV) to the API. Used when `ELEVENLABS_API_KEY` is set. |
| `ElevenLabsStreamingSpeechRecognizer.cs` | Low-latency realtime STT — streams the 8kHz mic over a WebSocket (Scribe v2) with server-side VAD. Enabled with `ELEVENLABS_STREAMING=true`. |
| `ISpeechRecognizer.cs` | The listening contract (`StartAsync` / `Write` / `OnRecognized`), so the batch and streaming recognisers plug in uniformly. |
| `LocalLlmClient.cs` | Optional OpenAI-compatible chat client (Ollama / LM Studio / llama.cpp) for generating in-character replies. |
| `Program.cs` | ASP.NET host: `/offer` (send/recv WebRTC), `/say`, `/ask`. Routes both the Ask box and recognised speech through one shared `AskAsync`. |
| `wwwroot/index.html` | Browser client: connect (captures the mic), a mic mute toggle, plus the Say / Ask text boxes. |

## Prerequisites

1. **Piper** for text-to-speech. Piper is now the [OHF-Voice/piper1-gpl](https://github.com/OHF-Voice/piper1-gpl)
   rewrite, distributed as a Python package (`pip install piper-tts`) and run as
   `python -m piper`. You need Python 3 plus a downloaded voice (a `.onnx` and its
   `.onnx.json`, fetched with `piper.download_voices`). Voices are browsable at the
   [Piper voices repo](https://huggingface.co/rhasspy/piper-voices) (samples at
   <https://rhasspy.github.io/piper-samples/>).

   **Windows**
   ```powershell
   # Install Piper into a virtual environment.
   py -3 -m venv C:\tools\piper-venv
   C:\tools\piper-venv\Scripts\pip install piper-tts

   # Download a voice (writes en_US-ryan-high.onnx + .onnx.json into the data dir).
   C:\tools\piper-venv\Scripts\python -m piper.download_voices en_US-ryan-high --data-dir C:\tools\piper-voices

   # Point the app at the interpreter + voice. PIPER_MODEL is the voice NAME; PIPER_DATA_DIR locates it.
   $env:PIPER_PATH     = "C:\tools\piper-venv\Scripts\python.exe"
   $env:PIPER_MODEL    = "en_US-ryan-high"
   $env:PIPER_DATA_DIR = "C:\tools\piper-voices"

   # Sanity check: should write a WAV you can play.
   & $env:PIPER_PATH -m piper -m $env:PIPER_MODEL --data-dir $env:PIPER_DATA_DIR -f test.wav -- "Max Headroom here."
   ```

   **Linux**
   ```bash
   # Install Piper into a virtual environment.
   python3 -m venv /opt/piper-venv
   /opt/piper-venv/bin/pip install piper-tts

   # Download a voice (writes en_US-ryan-high.onnx + .onnx.json into the data dir).
   /opt/piper-venv/bin/python -m piper.download_voices en_US-ryan-high --data-dir /opt/piper-voices

   # Point the app at the interpreter + voice. PIPER_MODEL is the voice NAME; PIPER_DATA_DIR locates it.
   export PIPER_PATH=/opt/piper-venv/bin/python
   export PIPER_MODEL=en_US-ryan-high
   export PIPER_DATA_DIR=/opt/piper-voices

   # Sanity check: should write a WAV you can play.
   "$PIPER_PATH" -m piper -m "$PIPER_MODEL" --data-dir "$PIPER_DATA_DIR" -f test.wav -- "Max Headroom here."
   ```
   `PIPER_PATH` may instead point at the `piper` console script if your install puts one
   on PATH; the app detects a `python`/`python3` interpreter and runs `python -m piper`
   automatically. If you have a voice `.onnx` on disk already, you can set `PIPER_MODEL`
   to its full path and omit `PIPER_DATA_DIR`.

   > **The above (child-process) mode reloads the voice model on every utterance, so it
   > is slow — slower than Azure.** Prefer the HTTP server below, which loads the model
   > once.

   ### Faster: the Piper HTTP server (recommended)

   Run Piper once as a server (model stays loaded) and the app POSTs each utterance to it.
   Install the `http` extra, then start the server alongside the app:

      **Windows**
   ```powershell
   C:\tools\piper-venv\Scripts\pip install "piper-tts[http]"
   & $env:PIPER_PATH -m piper.http_server -m $env:PIPER_MODEL --data-dir $env:PIPER_DATA_DIR --host 127.0.0.1 --port 5000
   ```

   ```bash
   # Linux (Windows is the same with the venv Scripts\ paths).
   /opt/piper-venv/bin/pip install "piper-tts[http]"
   /opt/piper-venv/bin/python -m piper.http_server \
     -m en_US-ryan-high --data-dir /opt/piper-voices --host 127.0.0.1 --port 5000
   ```

   Then point the app at it and **unset** the process-mode vars (or just leave them — the
   HTTP URL takes priority):

   ```bash
   export PIPER_HTTP_URL=http://127.0.0.1:5000
   ```
   ```powershell
   $env:PIPER_HTTP_URL = "http://127.0.0.1:5000"
   ```

   The synthesis endpoint is the **server root** (`POST /`) on piper-tts ≤ 1.4.2, so
   `PIPER_HTTP_URL` is just the base URL. (Newer dev builds moved it to `/synthesize`; if
   you run one of those, set `PIPER_HTTP_URL=http://127.0.0.1:5000/synthesize`.) Quick
   check that the server is up (writes a WAV):
   ```bash
   curl -X POST -H 'Content-Type: application/json' \
     -d '{ "text": "Max Headroom here." }' -o test.wav http://127.0.0.1:5000/
   ```

   In Kubernetes, run the Piper server as a **sidecar container** in the same pod and set
   `PIPER_HTTP_URL=http://127.0.0.1:5000` — the app and Piper share the pod's localhost.

   Without any TTS configured the avatar still renders but cannot speak or listen.

   ### Cloud alternative: ElevenLabs

   For the most natural voices you can use [ElevenLabs](https://elevenlabs.io) instead of
   Piper. It is a **paid cloud service** (so it reintroduces an external dependency and a
   per-character cost — the opposite of the offline goal), but the quality is the best of
   the options here. Setting an API key switches **both** the voice (TTS) and the listening
   (STT, via the ElevenLabs "scribe" model) to ElevenLabs, taking priority over Piper and
   Whisper:

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
   straight into the same amplitude-driven lip-sync as Piper.

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

2. (Optional) **Whisper** speech-to-text. A `ggml` model is downloaded to the app
   directory on first run; override the file with:
   ```powershell
   $env:WHISPER_MODEL = "C:\models\ggml-base.en.bin"
   ```

3. (Optional) A local LLM exposing an OpenAI-compatible chat endpoint, e.g.
   [Ollama](https://ollama.com). Without one, `/ask` just speaks the prompt verbatim
   (no in-character reply generation).

   **Windows**
   ```powershell
   # Install Ollama (or grab the installer from https://ollama.com/download).
   winget install Ollama.Ollama

   # Pull a small, fast model. The Ollama service starts automatically and listens on 11434.
   ollama pull llama3.2

   # Point the app at it.
   $env:LLM_ENDPOINT = "http://localhost:11434/v1/chat/completions"
   $env:LLM_MODEL    = "llama3.2"
   ```

   **Linux**
   ```bash
   # Install Ollama.
   curl -fsSL https://ollama.com/install.sh | sh

   # Pull a small, fast model. (If the service isn't running, start it with `ollama serve`.)
   ollama pull llama3.2

   # Point the app at it.
   export LLM_ENDPOINT=http://localhost:11434/v1/chat/completions
   export LLM_MODEL=llama3.2
   ```

   Quick check that Ollama is answering on its OpenAI-compatible endpoint:
   ```bash
   curl http://localhost:11434/v1/chat/completions \
     -H "Content-Type: application/json" \
     -d '{ "model": "llama3.2", "messages": [{ "role": "user", "content": "Say hi" }] }'
   ```

   A hosted OpenAI-compatible gateway works too — set `LLM_ENDPOINT` to its URL,
   `LLM_MODEL` accordingly (e.g. `anthropic/claude-3.5-sonnet` on OpenRouter), and
   `LLM_API_KEY` to your key. The client sends it as a Bearer token.

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

- Lip-sync maps the audio loudness onto a handful of the 22 viseme ids (0-21) in
  `MaxHeadroomVideoSource._visemeShapes`. It is approximate compared to Azure's
  phoneme-accurate visemes; for tighter sync, extract Piper's phoneme sequence
  (it phonemises via eSpeak NG) and map phonemes→viseme ids instead of using the
  amplitude envelope. Tune the bands in `PiperTtsSpeaker.VisemeForLevel`.
- Video is VP8 via the libvpx `VideoEncoderEndPoint` (same as `WebRTCGetStartedLibvpx`).
- Audio is G.711 from `AudioExtrasSource.SendAudioFromStream` (Piper PCM resampled to 16kHz).
  The audio track is send/recv and pinned to PCMU, so the received mic is a deterministic
  8kHz stream the recogniser consumes; speech-to-text is therefore telephone-grade. Pinning
  Opus (wideband) would improve recognition.
- The demo drives a single connected viewer; the most recent peer connection
  receives `/say`, `/ask` and the microphone.
- Whisper STT and Piper TTS both run on CPU with no cloud dependency, so the whole
  demo containerises cleanly. Use a glibc base image (e.g. `mcr.microsoft.com/dotnet/aspnet:8.0`,
  not Alpine) — the Whisper native and Piper's onnxruntime are glibc-built. The image
  needs **Python 3 plus `pip install piper-tts`** (Piper is no longer a standalone
  binary); bake in the Whisper model and the downloaded voice (`piper.download_voices …`)
  so there is no first-run download. On Linux you may also need `libfontconfig1` for SkiaSharp.
