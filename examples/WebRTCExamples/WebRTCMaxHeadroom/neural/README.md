# Neural avatar renderer (Wav2Lip sidecar)

A photoreal, model-driven alternative to the SkiaSharp cartoon. `NeuralAvatarRenderer`
(C#) streams the TTS audio to this Python **sidecar**, which runs [Wav2Lip](https://github.com/Rudrabha/Wav2Lip)
(via the ONNX build [instant-high/wav2lip-onnx](https://github.com/instant-high/wav2lip-onnx))
on a **static persona image** and streams lip-synced BGR frames back for the WebRTC video
track. Same seam as bitHuman's `push_audio()` / LiveKit's `DataStreamAudioOutput`.

The C# side only talks to `IAvatarRenderer`, so this is a drop-in swap for the cartoon.

## One-time setup (Windows)

The ML stack needs Python 3.11 (not 3.13+, which drops wheels); onnxruntime-directml runs
on any DX12 GPU with no CUDA toolkit. `insightface` is intentionally **not** used - the
sidecar finds the face box once with OpenCV's bundled Haar cascade.

```powershell
py -3.11 -m venv C:\tools\wav2lip\venv
C:\tools\wav2lip\venv\Scripts\pip install onnxruntime-directml numpy opencv-python librosa scipy gdown websockets soundfile

# Wav2Lip ONNX repo (only its audio.py mel front-end is imported) + model weights.
git clone https://github.com/instant-high/wav2lip-onnx C:\tools\wav2lip\wav2lip-onnx
C:\tools\wav2lip\venv\Scripts\python -m gdown 1_l4QC2RJ9nXapSQRD61-Q4KbSApc53HM -O C:\tools\wav2lip\wav2lip-onnx\checkpoints.zip
Expand-Archive C:\tools\wav2lip\wav2lip-onnx\checkpoints.zip C:\tools\wav2lip\wav2lip-onnx\checkpoints
```

Then drop a **front-facing, well-lit face image** at `C:\tools\wav2lip\persona.jpg`
(or pass `--persona <path>`; `.webp/.png/.jpg` all work). Wav2Lip is trained on real faces.

## Check quality offline (no C#)

Renders a WAV to an mp4 so you can eyeball the lip-sync before a full call:

```powershell
C:\tools\wav2lip\venv\Scripts\python neural_sidecar.py `
  --persona C:\tools\wav2lip\persona.jpg `
  --selftest C:\tools\wav2lip\test.wav C:\tools\wav2lip\out.mp4
```

## Run the live avatar

1. Start the sidecar (loads the model once, then serves):

   ```powershell
   C:\tools\wav2lip\venv\Scripts\python neural_sidecar.py --persona C:\tools\wav2lip\persona.jpg
   ```

2. Start the app selecting this renderer, then connect a browser as usual:

   ```powershell
   $env:AVATAR_RENDERER = "neural"          # NEURAL_SIDECAR_URL overrides ws://127.0.0.1:5002
   dotnet run
   ```

## Dynamic background (default)

The renderer cuts Max's figure out of the photo and composites him over an **animated
cube-corner backdrop** (three coloured faces meeting at a slowly moving vertex). Pass
`--static-bg` to keep the original photo background instead.

- **Matte**: the figure is segmented once. A grayscale matte PNG at
  `C:\tools\wav2lip\persona_alpha.png` is used if present (produce a clean one with
  [rembg](https://github.com/danielgatis/rembg) in a *separate* venv so it can't disturb
  the DirectML setup); otherwise the sidecar falls back to OpenCV GrabCut.

  ```powershell
  py -3.11 -m venv C:\tools\rembg-venv
  C:\tools\rembg-venv\Scripts\pip install "rembg[cpu]" pillow
  C:\tools\rembg-venv\Scripts\python -c "from rembg import remove; from PIL import Image; import numpy as np; a=np.array(remove(Image.open(r'C:\tools\wav2lip\persona.jpg').convert('RGB')).split()[-1]); Image.fromarray(a).resize((640,480)).save(r'C:\tools\wav2lip\persona_alpha.png')"
  ```
- **No freeze during silence**: the sidecar emits frames continuously at 25 fps, so the
  background keeps animating even when Max is silent (the mouth just holds idle). This is
  the video analogue of the always-on audio clock.

## Notes / limitations

- **Real-time**: `wav2lip_gan.onnx` runs ~53 fps on a Quadro T2000 (4 GB) via DirectML -
  ahead of the 25 fps clock, so the mouth keeps up.
- **A/V sync**: the model needs ~200 ms of audio look-ahead per frame. The speaker
  therefore pushes the utterance's PCM to the sidecar AS FAST AS IT HAS IT (see
  `IAvatarRenderer.PacesAudioInternally`) rather than paced to playback, so the look-ahead
  never waits on real-time delivery; the audio track then starts `_visemeLeadMs` later to
  cover the slower video path. Verified: mouth starts ~90 ms after speech onset.
- **Throughput gotchas** (already handled, for the record): websockets' default
  permessage-deflate would zlib every ~900KB frame (disabled), Windows' 15.6 ms timer
  quantised the 25 fps emitter (timeBeginPeriod(1)), and librosa builds its mel filterbank
  lazily (warmed at startup).
- **Sharpness**: the 96x96 model softens the lower face. For better quality use
  `wav2lip-onnx-HQ` (GFPGAN enhancement) or the 256px model, and tighten the crop.
- **Idle**: during silence the renderer re-emits the last frame, keeping the RTP clock
  alive so RTCP A/V sync stays stable.
