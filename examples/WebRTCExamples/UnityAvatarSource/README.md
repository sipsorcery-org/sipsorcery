# Unity Avatar Source

The **send-side twin of `UnityVideoSink`**: a Unity-rendered avatar streamed to a browser
over WebRTC using SIPSorcery. Video is VP8-encoded with `SIPSorceryMedia.FFmpeg`
(FFmpeg.AutoGen over native FFmpeg 8 libraries), which is why FFmpeg must be installed
(below).

Milestone 1 (this project) is the deliberately-dumb spine: a placeholder quad puppet whose
mouth is driven by a sine wave, rendered by a dedicated camera to a `RenderTexture`,
captured at 25fps, encoded on a background thread and sent to the browser, with a silence
audio track so the full A/V SDP negotiation is exercised. Milestone 2 swaps the puppet for
a Live2D Cubism model (its Unity SDK is the official C# Live2D runtime) by driving
`ParamMouthOpenY` from the same `SetMouthLevel` hook, and milestone 3 moves the Max demo's
speech stack (sherpa TTS/STT, LLamaSharp) in alongside it.

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

2. Open the project folder in Unity Hub (created with editor 6000.4.7f1).
3. In an empty scene, create an empty GameObject and add the `AvatarWebRTCSource`
   component (everything else — puppet, camera, capture, signaling — is created at
   runtime).
4. Press Play, browse to <http://localhost:8081> and click **Connect**. The puppet
   appears in the browser `<video>` element with its mouth flapping.

## Notes

- Signaling mirrors the `WebRTCMaxHeadroom` demo: the component hosts an `HttpListener`
  serving the page and answering `POST /offer`. STUN/srflx handles NAT the same way as
  the other examples when deployed off-box.
- Each encoded frame is sent with a constant RTP duration (90000/25 = 3600). An earlier
  version passed variable real-elapsed-ms through `Vp8NetVideoEncoderEndPoint`, whose
  ms->RTP conversion uses two integer divisions and only round-trips exact divisors like
  40ms cleanly - variable inputs made the RTP timestamp wander and Chrome's jitter buffer
  froze the picture. A steady 25fps capture needs a constant duration, not real ones.
- Capture uses synchronous `ReadPixels` for simplicity; switch to `AsyncGPUReadback`
  if the main-thread stall matters for a heavier scene.
- Cloud rendering: a Live2D-class 2D scene renders fine under xvfb + llvmpipe (software
  GL) in a Linux container — no GPU node required. Check Unity's licensing terms for
  running the player on server hardware before productising.
