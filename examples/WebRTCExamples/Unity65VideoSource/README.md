# Unity Video Source (Jul 2026)

The modern replacement for the original 2019 `UnityVideoSource` example: the game's main
camera view (the Unity 6 "Getting Started" third-person robot game) is streamed to a
browser over WebRTC using SIPSorcery. Video is VP8-encoded with `SIPSorceryMedia.FFmpeg`
(FFmpeg.AutoGen over native FFmpeg 8 libraries), the same pipeline as the
`UnityAvatarSource` example.

How it works ([WebRTCVideoSource.cs](Assets/Scripts/WebRTCVideoSource.cs)):

- A **mirror camera** copies the Cinemachine-driven main camera's pose and renders to a
  fixed 960x540 `RenderTexture`, so the player's Game view is untouched. It is rendered
  on demand with `RenderPipeline.SubmitRenderRequest` (not an enabled camera or
  `WaitForEndOfFrame`), so the stream keeps flowing even when the editor is unfocused or
  the Game view hidden.
- The texture is read back at 25fps, converted to top-down BGR24, VP8-encoded on a
  background thread and sent via `RTCPeerConnection.SendVideo` with a constant RTP
  duration (90000/25 = 3600). A silence audio track exercises full A/V SDP negotiation.
- Signaling is a tiny in-process `HttpListener` that serves the browser page and answers
  `POST /offer` (same as the `WebRTCMaxHeadroom` and `UnityAvatarSource` demos).
- The component bootstraps itself via `RuntimeInitializeOnLoadMethod` — no scene wiring
  is needed.

## Prerequisites

**FFmpeg 8 (shared build)** on the system `PATH`. On Windows:

```powershell
winget install Gyan.FFmpeg.Shared
```

The `SIPSorceryMedia.FFmpeg` package pins FFmpeg.AutoGen 8.1, so the FFmpeg **major
version must be 8** (the shared build ships `avcodec-62.dll` etc. beside `ffmpeg.exe`).
The app finds the DLLs from the folder of `ffmpeg.exe` on `PATH`; override with the
`FFMPEG_LIBS_PATH` environment variable.

## Run

1. Collect the managed plugins (build the SIPSorcery libraries for netstandard2.1):

   ```powershell
   ./copy-plugins.ps1
   ```

2. Open the project folder in Unity Hub (created with editor 6000.5.2f1).
3. Open the `GetStarted_Scene` scene and press Play — the `WebRTCVideoSource` GameObject
   is created automatically at runtime.
4. Browse to <http://localhost:8081> and click **Connect**. The game camera view appears
   in the browser `<video>` element and follows the robot as you play.

## Notes

- The original 2019 example set the main camera's `targetTexture` and read pixels on
  `OnRenderObject`, which no longer plays well with URP/Cinemachine; the mirror-camera +
  render-request approach here works with the scriptable render pipelines.
- Unity 6000.5 ships its own `Microsoft.Extensions.*` assemblies (v8.0, in
  `Editor\Data\BCLExtensions`) and they always win over `Assets/Plugins` copies at
  compile time, so `copy-plugins.ps1` pins the SIPSorcery build to the 8.0 package
  family (see the `unity-compat.targets` it generates). Without the pin every script
  fails to compile with CS1705.
- `Common.Logging*.dll` (a Makaretu.Dns dependency) has **Auto Reference off** in its
  import settings: its root `Common` namespace otherwise collides with
  `Common.umul128` in Unity's Burst/Collections package code.
- `Assets/Editor/AutoPlaySmokeTest.cs` lets automation start play mode
  (`Unity.exe -executeMethod AutoPlaySmokeTest.Run`); it is safe to delete.
- Capture uses synchronous `ReadPixels` for simplicity; switch to `AsyncGPUReadback` if
  the main-thread stall matters for a heavier scene.
- Screen-space overlay UI (the collectible counter) renders to the screen, not to the
  mirror camera, so it does not appear in the stream.
- `Application.runInBackground = true` (also set in Player settings) is essential: without
  it the player loop suspends the moment the editor/app loses focus — precisely when the
  user switches to the browser to click Connect — freezing video while the timer-driven
  audio keeps flowing.
