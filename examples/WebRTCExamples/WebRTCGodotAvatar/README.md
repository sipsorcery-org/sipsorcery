# WebRTC Godot Avatar

A talking avatar — a **3D VRM** model *or* a **2D Live2D (Cubism)** model — rendered entirely
in-process by **Godot 4.6 (.NET / C#)** and streamed to a browser over **WebRTC** with SIPSorcery.
It is the Godot counterpart to [`WebRTCMaxHeadroom`](../WebRTCMaxHeadroom): the same in-process
speech/AI stack ([`AvatarPipeline`](../AvatarPipeline)) — sherpa-onnx TTS + STT and an LLamaSharp
LLM — but the face is a Godot-rendered avatar instead of the SkiaSharp / Wav2Lip renderer.

You can **talk to it** (browser mic → speech-to-text → LLM → spoken reply) or type at it
(`/say`, `/ask`).

## How it works

The whole thing is one Godot process (`Scripts/AvatarStreamer.cs`), which owns the capture →
VP8 → WebRTC path and the speech pipeline, and delegates rendering to a swappable
[`IAvatarModel`](Scripts/IAvatarModel.cs):

- **`VrmAvatarModel`** — a 3D VRM humanoid with a Camera3D + lights, posed into an A-pose, framed
  head-and-shoulders. Mouth driven by the VRoid `Fcl_MTH_*` morph targets.
- **`Live2DAvatarModel`** — a 2D Cubism model via the **gd_cubism** GDExtension, framed
  head-and-shoulders. Mouth driven by `ParamMouthOpenY` from the model's `cubism_epilogue` effect
  (so lip-sync overrides the idle motion; see the code comments).

Common to both:

- The avatar renders into a fixed **640×480 SubViewport**; each 25fps tick the texture is read back,
  converted to BGR24, **VP8-encoded in pure C#** (`SIPSorcery.VP8`, no native FFmpeg) and sent on
  the WebRTC video track.
- `AvatarStreamer` implements **`IAvatarMouth`** from `AvatarPipeline`; the shared TTS speaker feeds
  it the speech PCM, whose RMS drives whichever mouth the active model exposes.
- The browser microphone (WebRTC audio, PCMU) is decoded and fed to the shared
  speech-to-text → LLM → speak path — identical to the Max demo, just a different renderer.
- Signalling is a tiny in-process `HttpListener` on port 8081 (`/offer`, `/say`, `/ask`).

```
 browser mic ─▶ STT (Parakeet) ─▶ LLM (Llama) ─▶ TTS (Piper) ─┬─▶ WebRTC audio track
                                                              └─▶ RMS ─▶ avatar mouth
 Godot render (VRM or Live2D) ─▶ SubViewport ─▶ VP8 (pure C#) ─▶ WebRTC video track
```

## Prerequisites

1. **Godot 4.6.x .NET (mono)** — the `Godot_v4.6.3-stable_mono` editor/runtime.
2. Godot addons in `addons/` (all kept out of git — install from the Godot AssetLib or copy in):
   - **VRM** + **MToon** (for the 3D avatar), and **gd_cubism** (for the Live2D avatar; its native
     library must be built/present under `addons/gd_cubism/bin`).
3. Avatar models (kept out of git):
   - `Models/UserAvatar.vrm` — any VRoid/VRM humanoid.
   - `Models/Live2D/<Name>/runtime/*.model3.json` — a Cubism model (the code points at
     `Models/Live2D/Ren/runtime/ren.model3.json`).
4. The **local speech/LLM models** the Max demo uses, in the conventional folders — each engine is
   optional and activates when its files are present:
   - `C:\tools\sherpa-tts\vits-piper-en_US-ryan-high` — male TTS voice
   - `C:\tools\sherpa-tts\vits-piper-en_US-hfc_female-medium` — female TTS voice (used by the VRM)
   - `C:\tools\sherpa-stt\<parakeet-or-whisper-model>` — speech-to-text
   - `C:\tools\llm\*.gguf` — the LLM (e.g. `Llama-3.2-3B-Instruct-Q4_K_M.gguf`)

With none of the speech models present the avatar still renders and streams; it just can't talk.

## Run

```powershell
./run-demo.ps1              # the 3D VRM avatar (default)
./run-demo.ps1 -Avatar ren  # the 2D Live2D "Ren" avatar
```

Then browse to <http://localhost:8081>, click **Connect**, and either speak or use the Say / Ask
boxes. `run-demo.ps1` does a one-time headless import of the assets first (the VRM importer and the
Live2D texture import are editor-only and don't run when the game is launched directly). The
in-process LLM takes ~10–15s to load; the page answers once it is ready.

Avatar selection is `--avatar <vrm|ren>` after `--` on the Godot command line
(`OS.GetCmdlineUserArgs`) or the `AVATAR_MODEL` environment variable.

## Voices

The voice is chosen per avatar in `AvatarStreamer.ResolveVoiceDir()`: the VRM prefers a female
Piper voice (first of `hfc_female-medium` / `amy-medium` / `amy-low` / `kathleen-low` that is
installed), the Live2D "Ren" character uses the male `ryan-high` voice, and either falls back to
the male voice if its preferred one is missing. Drop another `vits-piper-*` folder into
`C:\tools\sherpa-tts\` to change a voice — no code change needed.

## The persona

The avatar's character is the LLM system prompt. Adjust it in `AvatarStreamer.cs` (`DefaultPersona`)
or override at launch:

```powershell
$env:AVATAR_PERSONA = "You are a laconic film-noir detective. Reply in one terse sentence."
./run-demo.ps1
```
