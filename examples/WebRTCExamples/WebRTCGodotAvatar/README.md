# WebRTC Godot Avatar

A talking avatar — a **3D VRM** model *or* a **2D Live2D (Cubism)** model — rendered entirely
in-process by **Godot 4.7 (.NET / C#)** and streamed to a browser over **WebRTC** with SIPSorcery.
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
  head-and-shoulders. Mouth driven by `ParamMouthOpenY` from the model's `cubism_process` effect
  (after motions but before drawable vertices update; see the code comments).

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

- **Godot 4.7.x .NET** — the `Godot_v4.7-stable_mono_win64` editor (the C# "Godot Engine - .NET" / "mono" build, **not**
  the standard build). `run-demo.ps1` auto-discovers it anywhere under `C:\dev`; set the `GODOT`
  environment variable to the editor `.exe` if yours is elsewhere.
- **.NET SDK 8.0+**, and a full checkout of this repo — the example references the in-repo
  `SIPSorcery` and `SIPSorcery.VP8` projects directly.
- **For the Live2D native build only:** Python 3 with SCons, and the Visual Studio 2026
  **"Desktop development with C++"** workload (MSVC v143). Not needed for the VRM avatar, or if you
  obtain a prebuilt `gd_cubism` binary.

> The Godot addons (`addons/`) and the avatar models (`Models/*.vrm`, `Models/Live2D/`) are
> **deliberately kept out of git** — they are third-party and/or licensed (see `.gitignore`). A
> fresh checkout has none of them, so the setup steps below install them. They enable automatically
> once present: `project.godot` already lists the VRM/MToon editor plugins and the gd_cubism
> extension.

## Setup from a fresh checkout

Do the steps for whichever avatar you want: the **3D VRM** avatar needs 2a + 4a, the **2D Live2D**
avatar needs 2b + 4b. Steps 1, 3 and (optionally) 5 are common.

### 1. Install Godot
Install Godot 4.7.x **.NET** and confirm `./run-demo.ps1` finds it (or set `GODOT`).

### 2a. VRM + MToon addon  *(3D avatar)*
Installs `addons/vrm` + `addons/mtoon` and the `.vrm` import plugin. This project uses the
[**AzPepoze** VRM importer](https://github.com/AzPepoze/godot-vrm) (a maintained fork of the
[V-Sekai importer](https://github.com/V-Sekai/godot-vrm)) — v2.5.7.

- **From GitHub (recommended):** download the addon from its
  [releases](https://github.com/AzPepoze/godot-vrm/releases) (or clone the repo) and copy its
  `addons/vrm` and `addons/mtoon` folders into this folder's `addons/`. Keep the folder names
  exactly `vrm` and `mtoon` — generated VRM meta scripts reference those paths. *Or*
- **From the in-editor store:** Godot 4.7 replaced the old *AssetLib* tab with the new **Asset
  Store**. Existing assets were **not** auto-migrated from the old Asset Library, so the VRM addon
  may not be listed there yet — if a search for "VRM" turns up nothing, use the GitHub route above.

Then enable both plugins in **Project Settings → Plugins** (they are already listed in
`project.godot`, so they activate as soon as the folders are present).

> **Godot 4.7 compatibility patch.** The VRM addon (through v2.5.7) predates Godot 4.7's stricter
> GDScript: overrides of `GLTFDocumentExtension` virtuals such as `_import_post` must now return
> `Error` on every path, and the addon still uses a bare `return`, so its importer plugin fails to
> compile. Until an upstream release fixes this, apply the bundled patch after installing the addon,
> from this folder:
> ```powershell
> git apply patches/vrm-godot47-compat.patch   # or: patch -p1 < patches/vrm-godot47-compat.patch
> ```
> Symptom without it: the headless import logs `Failed to compile depended scripts` for
> `res://addons/vrm/plugin.gd`, no scene is produced, and `run-demo.ps1` reports the import did not
> complete. (Not needed on Godot 4.6 or earlier.)

### 2b. gd_cubism GDExtension  *(Live2D avatar)*
Installs `addons/gd_cubism` **and** the native library under `addons/gd_cubism/bin/` that actually
renders `.moc3` models. The C#/shader wrapper files can be copied in, but the native `.dll` links
Live2D's Cubism Core and **cannot be redistributed**, so you either download a prebuilt release or
build it:

- **Build it with the helper script** (the reliable path for Godot 4.7). First download **Cubism
  SDK for Native** from Live2D (<https://www.live2d.com/en/sdk/download/native/>) — the one part
  that can't be scripted, as you must accept Live2D's license. Then:
  ```powershell
  # one-time: fetch the SDK download above, then extract it to
  #   ThirdParty/gd_cubism/thirdparty/CubismSdkForNative-5-r.5
  ./build-gd-cubism.ps1
  ```
  The script clones `MizunagiKB/gd_cubism` (with its `godot-cpp` submodule) into `ThirdParty/`,
  builds the debug + release native libraries with SCons, and installs the whole addon (wrappers +
  binaries) into `addons/gd_cubism`. It needs Python + SCons and the VS2022 (or newer) C++ toolchain
  (see Prerequisites); it stops with instructions if the Cubism SDK or the C++ tools are missing.
  It pins gd_cubism to a revision that builds against **SDK 5-r.5** — newer gd_cubism revisions call
  `CubismModel::GetDrawableRenderOrders()`, which 5-r.5 lacks (a `C2039` compile error), and need a
  newer SDK. To build the latest instead, get the matching newer SDK and pass `-GdCubismRef main`.

If the native library is missing you'll get a `gd_cubism extension not loaded` error and the Live2D
avatar won't render (the VRM avatar is unaffected).

### 3. Build the example
```powershell
dotnet build     # in this folder; pulls in the in-repo SIPSorcery projects
```

### 4a. VRM model  *(3D avatar)*
Drop any VRoid/VRM humanoid at `Models/UserAvatar.vrm`. A good free option is a **VRoid
AvatarSample** or a model exported from **VRoid Studio**
([usage terms](https://vroid.pixiv.help/hc/en-us/articles/4402394424089)). The first launch converts
it — see [Run](#run).

### 4b. Live2D model  *(2D avatar)*
Drop any Cubism model under `Models/Live2D/<Name>/runtime/` (with its `*.model3.json`, `.moc3`,
textures, motions, etc. as the vendor ships them). A good free option is the Live2D sample **"Ren"**
(<https://www.live2d.com/en/download/sample-data/>, accept the Free Material License) → extract to
`Models/Live2D/Ren/runtime/`. **No import step is needed** — gd_cubism reads the model and textures
directly at runtime; just place the folder and run.

Select which one to stream with `--avatar live2d:<Name>` (see the table below); `--avatar live2d`
with no name auto-picks the first model found under `Models/Live2D/`, and `--avatar ren` defaults to
the Ren model. The `*.model3.json` filename may be anything (the folder is scanned), so keep the
vendor's filenames — only the containing `<Name>` folder matters.

**Per-model tuning.** Most models render correctly with the defaults. Overrides live in the
`Live2DConfigs` registry in `Scripts/AvatarStreamer.cs`, keyed by folder name. Ren supplies explicit
foreground face details and closed/open mouth drawable sets because gd_cubism does not layer those
drawables correctly; the mouth sets are cross-faded using the lip-sync value. `UseDrawOrder = true`
is also available for models that need static draw order instead of dynamic render order. It drives
a `use_draw_order` property added to the gd_cubism addon (part of
`patches/gd_cubism-sdk-5-r.5.patch`).

### 5. (optional) Speech / LLM models
The **local speech/LLM models** the Max demo uses go in the conventional folders — each engine is
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
boxes. The page also has an **avatar dropdown + Switch** button: it swaps the live avatar without
dropping the WebRTC session (the model's nodes are rebuilt inside the capture viewport, so the stream
just keeps showing whatever is now rendered). VRM targets must already be imported (Live2D needs no
import); the switcher is served from `GET /avatars` and driven by `POST /avatar <kind[:name]>`.

For Live2D models with authored `*.motion3.json` animations, the page also shows a **motion tester**
with Play, Loop, Stop and Play all controls. It refreshes when the avatar changes and labels unnamed
motion groups using their filenames. The catalogue is served by `GET /motions`; playback uses
`POST /motion` with `{ "group": "Tap", "index": 0, "loop": false }`, and `POST /motion/stop`
stops it. Authored motions temporarily control the model's pose and expression while TTS continues
to control the lip-sync parameters. Playing anything other than `Idle[0]` smoothly zooms out to fit
the model's full canvas; when it finishes, the view returns to the portrait crop and `Idle[0]` loops.

On the **first** launch `run-demo.ps1` opens the project once in the headless Godot editor to import
the assets — the VRM importer and the Live2D texture import are editor-only and don't run when the
game is launched directly. For a VRM avatar it imports the selected `Models/<name>.vrm`; for a Live2D
avatar it runs a one-time texture-import pass (no VRM required). If a VRM ever fails to load, delete
`.godot/imported` and let it re-import. The in-process LLM then takes ~10–15s to load; the page
answers once it is ready.

## Selecting the avatar and voice

`run-demo.ps1` takes three options, each also available as a launch flag (after `--` on the Godot
command line, read via `OS.GetCmdlineUserArgs`) or an environment variable:

| Script param | Launch flag | Env var | Meaning |
|---|---|---|---|
| `-Avatar <kind[:name]>` | `--avatar` | `AVATAR_MODEL` | `vrm` (default) or `ren` / `live2d`. `:name` selects a model: `vrm:Alice` → `Models/Alice.vrm`, `live2d:Haru` → `Models/Live2D/Haru/runtime/*.model3.json`. |
| `-Gender <male\|female>` | `--gender` | `AVATAR_GENDER` | Picks a matching default voice. Omit for the avatar's default (VRM → female, Live2D → male). |
| `-Voice <name>` | `--voice` | `AVATAR_VOICE` | An exact sherpa-tts voice folder under `C:\tools\sherpa-tts` — the full name or the short suffix (e.g. `amy-medium` → `vits-piper-en_US-amy-medium`). Overrides `-Gender`. |

```powershell
./run-demo.ps1 -Avatar "vrm:Alice" -Gender male
./run-demo.ps1 -Avatar ren -Voice amy-medium
```

Voice resolution (`AvatarStreamer.ResolveVoiceDir()`), highest priority first: an exact `-Voice`;
then `-Gender`; then the **gender inferred from the model's folder** (see below); then the kind
default (VRM → female, Live2D → male). Female tries `hfc_female-medium` / `amy-medium` / `amy-low` /
`kathleen-low` in order (male uses `ryan-high`), and a female choice with no female voice installed
falls back to male. Drop another `vits-piper-*` folder into `C:\tools\sherpa-tts\` to add voices — no
code change needed.

### Voice gender by folder (optional)

So the voice **follows the avatar** (including when switched at runtime), you can file models under a
`female/` or `male/` folder; the folder sets that avatar's default voice gender:

```
Models/
  vrm/Alice.vrm                    # no gender: uses the kind default (VRM → female)
  vrm/female/Bella.vrm             # → female voice
  vrm/male/Bob.vrm                 # → male voice
  Live2D/mao/runtime/…             # no gender: kind default (Live2D → male)
  Live2D/female/chitose/runtime/…  # → female voice
  Live2D/male/natori/runtime/…     # → male voice
```

(A legacy flat `Models/<name>.vrm` still works too — e.g. the default `Models/UserAvatar.vrm`.)

Both layouts work together — the gender folder is optional and just sets the default; `-Gender` and
`-Voice` still override it. Selection is by name regardless of folder (`--avatar live2d:chitose`
finds it wherever it lives). When you switch avatars from the web page, the TTS voice is rebuilt to
match the new avatar's gender.

## The persona

The avatar's character is the LLM system prompt. Adjust it in `AvatarStreamer.cs` (`DefaultPersona`)
or override at launch:

```powershell
$env:AVATAR_PERSONA = "You are a laconic film-noir detective. Reply in one terse sentence."
./run-demo.ps1
```
