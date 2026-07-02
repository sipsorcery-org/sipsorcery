//-----------------------------------------------------------------------------
// Filename: Wav2LipAvatarRenderer.cs
//
// Description: The fully IN-PROCESS photoreal avatar - a C# port of the Python
// neural sidecar (neural/neural_sidecar.py). Wav2Lip runs via onnxruntime
// (DirectML on any DX12 GPU, CPU fallback), the mel front-end is the validated
// MelSpectrogram port, and all compositing (mouth paste, matte blend, animated
// cube-corner background, VHS grade, head sway + blinks) is SkiaSharp. No Python,
// no WebSocket, no external process.
//
// Select with AVATAR_RENDERER=wav2lip. Configuration (env):
//   WAV2LIP_ONNX     path to wav2lip_gan.onnx        (default C:\tools\wav2lip\wav2lip-onnx\checkpoints\wav2lip_gan.onnx)
//   NEURAL_PERSONA   front-facing face image          (default C:\tools\wav2lip\persona.jpg)
//   NEURAL_MATTE     grayscale figure matte PNG       (default C:\tools\wav2lip\persona_alpha.png)
//   NEURAL_FACE_BOX  y1,y2,x1,x2 face box at 640x480  (default = the bundled Max persona's)
//   NEURAL_EYES      x,y,w,h[;x,y,w,h] blink eyes     (default = the Max persona's lit eye)
// The face box / eye rects are static per persona (the Python side detected them with
// Haar cascades; here they are config - re-detect once when you swap the persona).
//
// Pipeline per 25fps tick (mirrors the sidecar's emitter):
//   mouth <- ONNX(mel window) while speaking, else the precomputed idle mouth
//   frame <- grade( blend( warp(zoom(blink(persona+mouth))), background(t) ) )
// The mouth advances only as fast as its audio's mel is available (never outruns the
// model's ~200ms look-ahead); the background advances every tick so silence never
// freezes the scene. Audio arrives via IAvatarRenderer.PushAudio - burst-pushed by the
// speaker since PacesAudioInternally is true - and the mel is recomputed incrementally
// on a background task, never on the render thread.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SIPSorceryMedia.Abstractions;
using SkiaSharp;

namespace demo;

public sealed class Wav2LipAvatarRenderer : IAvatarRenderer
{
    public const int WIDTH = 640;
    public const int HEIGHT = 480;

    private const int VIDEO_SAMPLING_RATE = 90000;
    private const int FPS = 25;
    private const int IMG_SIZE = 96;               // Wav2Lip face crop.
    private const int MEL_STEP = 16;               // mel columns per inference window.
    private const double MEL_PER_FRAME = 80.0 / FPS;
    private const float ZOOM = 1.25f;              // figure fills the frame (per reference).
    private const int ZOOM_TOP = 70;               // top crop offset in zoomed px.
    private const int BG_EVERY = 3;                // re-render the slow background every Nth tick.

    public static readonly List<VideoFormat> SupportedFormats = new()
    {
        new VideoFormat(VideoCodecsEnum.H264, 100, VIDEO_SAMPLING_RATE, "packetization-mode=1")
    };

    private static readonly ILogger logger = SIPSorcery.LogFactory.CreateLogger<Wav2LipAvatarRenderer>();

    // Renderers are created per peer connection; the ONNX session (model load + DirectML
    // kernel compilation) is heavy and stateless, so share one per model path for the app's life.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, InferenceSession> _sessions = new();

    private static InferenceSession GetSharedSession(string modelPath) =>
        _sessions.GetOrAdd(Path.GetFullPath(modelPath), path =>
        {
            // DirectML (any DX12 GPU) with CPU fallback.
            var so = new SessionOptions { EnableMemoryPattern = false, ExecutionMode = ExecutionMode.ORT_SEQUENTIAL };
            try
            {
                so.AppendExecutionProvider_DML(0);
                logger.LogInformation("Wav2Lip (in-process) using the DirectML execution provider.");
            }
            catch (Exception)
            {
                logger.LogWarning("DirectML unavailable; Wav2Lip runs on CPU (may not hold 25fps).");
            }
            return new InferenceSession(path, so);
        });

    /// <summary>Conventional model location, overridable with WAV2LIP_ONNX.</summary>
    private static string DefaultModelPath() =>
        Environment.GetEnvironmentVariable("WAV2LIP_ONNX")
            ?? @"C:\tools\wav2lip\wav2lip-onnx\checkpoints\wav2lip_gan.onnx";

    /// <summary>Conventional persona location (first of persona.jpg/png/webp - the decoder
    /// sniffs content, so the extension is cosmetic), overridable with NEURAL_PERSONA.</summary>
    private static string DefaultPersonaPath()
    {
        var env = Environment.GetEnvironmentVariable("NEURAL_PERSONA");
        if (!string.IsNullOrWhiteSpace(env))
        {
            return env;
        }
        foreach (var name in new[] { "persona.jpg", "persona.png", "persona.webp" })
        {
            var candidate = Path.Combine(@"C:\tools\wav2lip", name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        return @"C:\tools\wav2lip\persona.jpg";
    }

    /// <summary>True when the model and persona files this renderer needs are on disk -
    /// used to make the in-process photoreal head the default renderer.</summary>
    public static bool FilesPresent() =>
        File.Exists(DefaultModelPath()) && File.Exists(DefaultPersonaPath());

    /// <summary>
    /// Loads the shared model session and runs one dummy inference at app start, so the first
    /// connection doesn't pay the model load + DirectML kernel compilation (~1-2s).
    /// </summary>
    public static Task PreloadAsync() => Task.Run(() =>
    {
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var session = GetSharedSession(DefaultModelPath());
            lock (session)   // the DML EP does not support concurrent Run.
            {
                using var _ = session.Run(new[]
                {
                    NamedOnnxValue.CreateFromTensor("mel_spectrogram", new DenseTensor<float>(new[] { 1, 1, MelSpectrogram.NMels, MEL_STEP })),
                    NamedOnnxValue.CreateFromTensor("video_frames", new DenseTensor<float>(new[] { 1, 6, IMG_SIZE, IMG_SIZE })),
                });
            }
            logger.LogInformation("Wav2Lip warmed up in {Ms} ms.", sw.ElapsedMilliseconds);
        }
        catch (Exception excp)
        {
            logger.LogWarning(excp, "Wav2Lip warm-up failed (first connection will be slow).");
        }
    });

    private readonly MediaFormatManager<VideoFormat> _formatManager = new(SupportedFormats);
    private readonly IVideoEncoder _videoEncoder;
    private readonly MelSpectrogram _mel = new();

    // Model.
    private readonly InferenceSession _session;
    private readonly DenseTensor<float> _faceInput;     // [1,6,96,96], constant for a static persona.
    private readonly byte[] _idleMouth;                 // 96x96 BGR from a silence mel.

    // Persona / scene (all fixed per session).
    private readonly SKBitmap _persona;                 // BGRA premultiplied by the repaired matte.
    private readonly float[] _matteAlpha;               // repaired matte, per pixel 0..1.
    private readonly (int y1, int y2, int x1, int x2) _faceBox;
    private readonly (int x, int y, int w, int h)[] _eyes;
    private readonly byte[] _postByte;                  // scanline*vignette per pixel, 0..255.
    private readonly byte[] _biasPost;                  // grade bias * post, per pixel BGR.
    private readonly int[] _gradeM;                     // 3x3 colour matrix, Q8 fixed point.

    // Audio -> mouth state (locked).
    private readonly object _audioLock = new();
    private readonly List<short> _pcm = new();
    private float[,] _melMatrix;
    private int _melSolidCols;                          // columns whose audio can no longer change.
    private bool _melDirty;
    private bool _melRefreshRunning;
    private volatile bool _speaking;
    private int _mouthFrame;
    private byte[] _lastMouth;                          // last live 96x96 BGR mouth.

    // Render loop.
    private Timer _renderTimer;
    private int _renderBusy;                            // non-reentrancy guard for the timer.
    private SKBitmap _bgCache;
    private int _bgCacheIdx = int.MinValue;
    private int _frameIdx;
    private bool _isStarted, _isPaused, _isClosed, _faulted;

    public event EncodedSampleDelegate OnVideoSourceEncodedSample;
    public event SourceErrorDelegate OnVideoSourceError;
#pragma warning disable CS0067
    public event RawVideoSampleDelegate OnVideoSourceRawSample;
    public event RawVideoSampleFasterDelegate OnVideoSourceRawSampleFaster;
#pragma warning restore CS0067

    public Wav2LipAvatarRenderer(IVideoEncoder encoder,
        string modelPath = null, string personaPath = null, string mattePath = null)
    {
        _videoEncoder = encoder;

        modelPath ??= DefaultModelPath();
        personaPath ??= DefaultPersonaPath();
        mattePath ??= Environment.GetEnvironmentVariable("NEURAL_MATTE") ?? @"C:\tools\wav2lip\persona_alpha.png";
        _faceBox = ParseBox(Environment.GetEnvironmentVariable("NEURAL_FACE_BOX")) ?? (140, 406, 223, 479);
        _eyes = ParseEyes(Environment.GetEnvironmentVariable("NEURAL_EYES")) ?? new[] { (275, 213, 62, 62) };

        _session = GetSharedSession(modelPath);

        // Persona at target size; raw pixels are needed for the model's face crop BEFORE
        // the matte alpha is applied.
        using var raw = SKBitmap.Decode(personaPath)
            ?? throw new FileNotFoundException($"Could not read persona image {personaPath}.");
        var personaOpaque = raw.Resize(new SKImageInfo(WIDTH, HEIGHT, SKColorType.Bgra8888, SKAlphaType.Premul),
            SKFilterQuality.Medium);

        _faceInput = BuildFaceInput(personaOpaque, _faceBox);
        _idleMouth = Infer(new float[MelSpectrogram.NMels, MEL_STEP], 0);   // silence mel window.

        // Matte -> repaired alpha, baked into the persona's alpha channel (premultiplied) so
        // one SrcOver draw does the whole figure/background blend, warped and all.
        _matteAlpha = LoadRepairedMatte(mattePath);
        _persona = ApplyMatte(personaOpaque, _matteAlpha);
        personaOpaque.Dispose();

        BuildGradeTables(_matteAlpha, out _postByte, out _biasPost, out _gradeM);

        _renderTimer = new Timer(RenderTick, null, Timeout.Infinite, Timeout.Infinite);
    }

    // --- IAvatarRenderer ------------------------------------------------------------------

    /// <summary>The renderer buffers PCM and paces the mouth itself - burst-push audio.</summary>
    public bool PacesAudioInternally => true;

    public void BeginSpeech()
    {
        lock (_audioLock)
        {
            _pcm.Clear();
            _melMatrix = null;
            _melSolidCols = 0;
            _melDirty = false;
            _mouthFrame = 0;
            _lastMouth = null;
            _speaking = true;
        }
    }

    public void PushAudio(ReadOnlySpan<short> pcm16, int sampleRate)
    {
        if (pcm16.Length == 0 || _isClosed)
        {
            return;
        }
        lock (_audioLock)
        {
            foreach (var s in pcm16) { _pcm.Add(s); }
            _melDirty = true;
        }
    }

    public void EndSpeech() => _speaking = false;

    // --- IVideoSource plumbing --------------------------------------------------------------

    public List<VideoFormat> GetVideoSourceFormats() => _formatManager.GetSourceFormats();
    public void SetVideoSourceFormat(VideoFormat videoFormat) => _formatManager.SetSelectedFormat(videoFormat);
    public void RestrictFormats(Func<VideoFormat, bool> filter) => _formatManager.RestrictFormats(filter);
    public void ForceKeyFrame() => _videoEncoder?.ForceKeyFrame();
    public bool HasEncodedVideoSubscribers() => OnVideoSourceEncodedSample != null;
    public bool IsVideoSourcePaused() => _isPaused;

    public void ExternalVideoSourceRawSample(uint durationMilliseconds, int width, int height, byte[] sample, VideoPixelFormatsEnum pixelFormat) =>
        throw new NotImplementedException("The Wav2Lip renderer generates its own frames from audio.");
    public void ExternalVideoSourceRawSampleFaster(uint durationMilliseconds, RawImage rawImage) =>
        throw new NotImplementedException("The Wav2Lip renderer generates its own frames from audio.");

    public Task StartVideo()
    {
        if (!_isStarted)
        {
            _isStarted = true;
            _renderTimer.Change(0, 1000 / FPS);
        }
        return Task.CompletedTask;
    }

    public Task PauseVideo() { _isPaused = true; return Task.CompletedTask; }
    public Task ResumeVideo() { _isPaused = false; return Task.CompletedTask; }
    public Task CloseVideo()
    {
        _isClosed = true;
        _renderTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        return Task.CompletedTask;
    }

    // --- Render loop --------------------------------------------------------------------

    private void RenderTick(object state)
    {
        if (_isClosed || _isPaused || _faulted || _videoEncoder == null ||
            OnVideoSourceEncodedSample == null || _formatManager.SelectedFormat.IsEmpty())
        {
            return;
        }

        // System.Threading.Timer OVERLAPS callbacks when one runs longer than the period.
        // Everything below (the DirectML session especially, which crashes with an access
        // violation on concurrent Run) assumes single-threaded rendering, so drop this tick
        // if the previous one is still going - a late frame beats a dead process.
        if (Interlocked.CompareExchange(ref _renderBusy, 1, 0) != 0)
        {
            return;
        }

        try
        {
            var mouth = NextMouth();
            var bgr = RenderFrame(mouth, _frameIdx);
            _frameIdx++;

            var encoded = _videoEncoder.EncodeVideo(WIDTH, HEIGHT, bgr, VideoPixelFormatsEnum.Bgr,
                _formatManager.SelectedFormat.Codec);
            if (encoded != null)
            {
                OnVideoSourceEncodedSample?.Invoke(VIDEO_SAMPLING_RATE / FPS, encoded);
            }
        }
        catch (Exception excp)
        {
            _faulted = true;
            _renderTimer.Change(Timeout.Infinite, Timeout.Infinite);
            logger.LogError(excp, "Fatal error in Wav2Lip render loop; stopping video.");
            OnVideoSourceError?.Invoke(excp.Message);
        }
        finally
        {
            Volatile.Write(ref _renderBusy, 0);
        }
    }

    /// <summary>The mouth for this tick: the next live one if its mel window is ready (holding
    /// the previous while audio catches up), else idle. Kicks a background mel refresh when
    /// new PCM is waiting - the O(100ms) recompute never runs on the render thread.</summary>
    private byte[] NextMouth()
    {
        if (!_speaking)
        {
            return _idleMouth;
        }

        float[,] mel;
        bool wantRefresh;
        lock (_audioLock)
        {
            mel = _melMatrix;
            int start = (int)(_mouthFrame * MEL_PER_FRAME);
            wantRefresh = _melDirty && !_melRefreshRunning &&
                          (mel == null || start + MEL_STEP > _melSolidCols);
            if (wantRefresh) { _melRefreshRunning = true; }
        }

        if (wantRefresh)
        {
            _ = Task.Run(RefreshMel);
        }

        if (mel != null)
        {
            int start = (int)(_mouthFrame * MEL_PER_FRAME);
            if (start + MEL_STEP <= _melSolidCols)
            {
                _lastMouth = Infer(mel, start);
                _mouthFrame++;
            }
        }
        return _lastMouth ?? _idleMouth;
    }

    /// <summary>Recomputes the mel of the buffered PCM off the render thread.</summary>
    private void RefreshMel()
    {
        try
        {
            short[] snapshot;
            lock (_audioLock)
            {
                snapshot = _pcm.ToArray();
                _melDirty = false;
            }
            if (snapshot.Length < MelSpectrogram.SampleRate / 10)
            {
                return;   // need some audio before the first mel.
            }
            var mel = _mel.Compute(snapshot);
            lock (_audioLock)
            {
                _melMatrix = mel;
                // Columns near the end were computed against zero padding that more audio
                // would change; only trust the "solid" ones (frame window fully in-signal).
                _melSolidCols = Math.Max(0, (snapshot.Length - 400) / 200);
            }
        }
        catch (Exception excp)
        {
            logger.LogWarning(excp, "Mel refresh failed.");
        }
        finally
        {
            lock (_audioLock) { _melRefreshRunning = false; }
        }
    }

    /// <summary>Runs the model for the 16-column mel window at <paramref name="startCol"/>;
    /// returns the 96x96 BGR mouth patch.</summary>
    private byte[] Infer(float[,] mel, int startCol)
    {
        var melTensor = new DenseTensor<float>(new[] { 1, 1, MelSpectrogram.NMels, MEL_STEP });
        int cols = mel.GetLength(1);
        for (int m = 0; m < MelSpectrogram.NMels; m++)
        {
            for (int c = 0; c < MEL_STEP; c++)
            {
                melTensor[0, 0, m, c] = startCol + c < cols ? mel[m, startCol + c] : 0f;
            }
        }

        // The DirectML EP does not support concurrent Run calls on a session, and the session
        // is shared app-wide - serialize on it (also guards the startup preload racing us).
        IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results;
        lock (_session)
        {
            results = _session.Run(new[]
            {
                NamedOnnxValue.CreateFromTensor("mel_spectrogram", melTensor),
                NamedOnnxValue.CreateFromTensor("video_frames", _faceInput),
            });
        }
        using var _ = results;
        var pred = results.First().AsTensor<float>();     // [1,3,96,96], BGR planes, 0..1.

        var mouth = new byte[IMG_SIZE * IMG_SIZE * 3];
        for (int y = 0; y < IMG_SIZE; y++)
        {
            for (int x = 0; x < IMG_SIZE; x++)
            {
                int o = (y * IMG_SIZE + x) * 3;
                mouth[o + 0] = (byte)Math.Clamp(pred[0, 0, y, x] * 255f, 0f, 255f);
                mouth[o + 1] = (byte)Math.Clamp(pred[0, 1, y, x] * 255f, 0f, 255f);
                mouth[o + 2] = (byte)Math.Clamp(pred[0, 2, y, x] * 255f, 0f, 255f);
            }
        }
        return mouth;
    }

    // --- Frame composition (port of the sidecar's _finish) --------------------------------

    /// <summary>Composites one full frame: mouth+blink on the matted persona, warped by the
    /// liveness pose over the animated background, then the VHS grade. Returns BGR24.</summary>
    private byte[] RenderFrame(byte[] mouth96, int idx)
    {
        double t = idx / (double)FPS;

        if (_bgCache == null || idx - _bgCacheIdx >= BG_EVERY || idx < _bgCacheIdx)
        {
            _bgCache?.Dispose();
            _bgCache = RenderBackground(t);
            _bgCacheIdx = idx;
        }

        using var frame = new SKBitmap(new SKImageInfo(WIDTH, HEIGHT, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(frame))
        using (var paint = new SKPaint { FilterQuality = SKFilterQuality.Low })
        {
            canvas.DrawBitmap(_bgCache, SKRect.Create(WIDTH, HEIGHT), paint);

            // Figure layer: persona copy + live mouth + blink, drawn through zoom*pose so the
            // matte alpha warps with it (one SrcOver draw = the whole blend).
            using var fg = new SKBitmap(_persona.Info);
            _persona.CopyTo(fg);
            using (var fgCanvas = new SKCanvas(fg))
            {
                DrawMouth(fgCanvas, mouth96);
                double blink = BlinkAmount(t);
                if (blink > 0)
                {
                    DrawBlink(fgCanvas, blink);
                }
            }

            canvas.SetMatrix(PoseMatrix(t).PreConcat(ZoomMatrix()));
            canvas.DrawBitmap(fg, 0, 0, paint);
            canvas.ResetMatrix();
        }

        return GradeToBgr(frame);
    }

    private void DrawMouth(SKCanvas fgCanvas, byte[] mouth96)
    {
        var info = new SKImageInfo(IMG_SIZE, IMG_SIZE, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var mouthBmp = new SKBitmap(info);
        unsafe
        {
            var dst = (byte*)mouthBmp.GetPixels().ToPointer();
            for (int i = 0, o = 0; i < mouth96.Length; i += 3, o += 4)
            {
                dst[o] = mouth96[i]; dst[o + 1] = mouth96[i + 1]; dst[o + 2] = mouth96[i + 2]; dst[o + 3] = 255;
            }
        }

        // Scale to the face box, then premultiply by the MATTE alpha for that region before the
        // Src draw - the box edges reach into soft-matte areas (the shadow beside the face), and
        // pasting opaque there would punch a hard rectangle through the figure's alpha.
        var (y1, y2, x1, x2) = _faceBox;
        int bw = x2 - x1, bh = y2 - y1;
        using var scaled = new SKBitmap(new SKImageInfo(bw, bh, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var c = new SKCanvas(scaled))
        {
            c.DrawBitmap(mouthBmp, SKRect.Create(bw, bh), new SKPaint { FilterQuality = SKFilterQuality.Medium });
        }
        unsafe
        {
            var px = (byte*)scaled.GetPixels().ToPointer();
            for (int yy = 0; yy < bh; yy++)
            {
                int rowA = (y1 + yy) * WIDTH + x1;
                int rowP = yy * bw * 4;
                for (int xx = 0; xx < bw; xx++)
                {
                    float a = _matteAlpha[rowA + xx];
                    int o = rowP + xx * 4;
                    px[o] = (byte)(px[o] * a);
                    px[o + 1] = (byte)(px[o + 1] * a);
                    px[o + 2] = (byte)(px[o + 2] * a);
                    px[o + 3] = (byte)(a * 255f);
                }
            }
        }

        using var paint = new SKPaint { BlendMode = SKBlendMode.Src };
        fgCanvas.DrawBitmap(scaled, x1, y1, paint);
    }

    /// <summary>Blink: stretch the lid skin just above each eye down over it (per the sidecar).</summary>
    private void DrawBlink(SKCanvas fgCanvas, double amount)
    {
        using var paint = new SKPaint { FilterQuality = SKFilterQuality.Low, BlendMode = SKBlendMode.Src };
        foreach (var (x, y, w, h) in _eyes)
        {
            int cover = (int)(h * amount);
            if (cover <= 2 || y - h < 0)
            {
                continue;
            }
            int stripH = Math.Max(3, (int)(h * 0.35));
            var src = SKRect.Create(x, y - stripH, w, stripH);
            var dst = SKRect.Create(x, y, w, cover);
            using var strip = new SKBitmap(new SKImageInfo(w, stripH, SKColorType.Bgra8888, SKAlphaType.Premul));
            using (var c = new SKCanvas(strip))
            {
                c.DrawBitmap(_persona, src, SKRect.Create(w, stripH));
            }
            fgCanvas.DrawBitmap(strip, dst, paint);
        }
    }

    private static SKMatrix ZoomMatrix()
    {
        int x0 = (int)(WIDTH * ZOOM - WIDTH) / 2;
        var m = SKMatrix.CreateScale(ZOOM, ZOOM);
        return SKMatrix.CreateTranslation(-x0, -ZOOM_TOP).PreConcat(m);
    }

    /// <summary>Head micro-sway plus the occasional held "snap" (Max's signature jerks).</summary>
    private static SKMatrix PoseMatrix(double t)
    {
        double dx = 2.5 * Math.Sin(t * 0.7) + 1.5 * Math.Sin(t * 1.31);
        double dy = 1.2 * Math.Sin(t * 0.9 + 1.0);
        double rot = 0.7 * Math.Sin(t * 0.53);

        const double period = 3.7;
        int k = (int)(t / period);
        if (t - k * period < 0.24)
        {
            uint rng = (uint)(k * 2654435761) & 0xFFFF;
            // NOTE: cast before subtracting - with uint operands C# picks UNSIGNED arithmetic
            // (the int constants convert to uint), so e.g. 1u - 2 wraps to 4 billion and the
            // "small tilt" becomes a ~90 degree spin.
            dx += ((int)(rng % 9) - 4) * 1.6;
            dy += ((int)((rng >> 4) % 5) - 2) * 1.0;
            rot += ((int)((rng >> 8) % 5) - 2) * 0.6;
        }

        var m = SKMatrix.CreateRotationDegrees((float)rot, WIDTH / 2f, HEIGHT * 0.55f);
        return SKMatrix.CreateTranslation((float)dx, (float)dy).PreConcat(m);
    }

    /// <summary>0..1 lid closure; a ~200ms triangular blink at a pseudo-random point in each ~3.3s window.</summary>
    private static double BlinkAmount(double t)
    {
        const double period = 3.3;
        int k = (int)(t / period);
        double start = ((uint)(k * 40503) & 0xFFFF) % 100 / 100.0 * (period - 0.3);
        double ph = t - k * period - start;
        return ph >= 0 && ph < 0.20 ? 1.0 - Math.Abs(ph / 0.10 - 1.0) : 0.0;
    }

    // --- Background (port of the sidecar's isometric room-corner louvres) -----------------

    private SKBitmap RenderBackground(double t)
    {
        // Half resolution (the upscale softens like the reference's VHS bloom).
        int bw = WIDTH / 2, bh = HEIGHT / 2;
        var bmp = new SKBitmap(new SKImageInfo(bw, bh, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Black);

        float vx = (float)(bw * 0.46 + 20 * Math.Cos(t * 0.13));
        float vy = (float)(bh * 0.76 + 12 * Math.Sin(t * 0.11));
        double aL = (45.0 + 3.0 * Math.Sin(t * 0.08)) * Math.PI / 180.0;
        double aR = (15.0 + 3.0 * Math.Sin(t * 0.06 + 1.0)) * Math.PI / 180.0;
        float diag = (float)Math.Sqrt(bw * bw + bh * bh);
        float R = 2f * diag;
        const float spacing = 13f;
        float phWall = (float)(t * 5.0 % spacing);
        float phFloor = (float)(t * 4.0 % spacing);

        var dL = ((float)-Math.Cos(aL), (float)Math.Sin(aL));
        var dR = ((float)Math.Cos(aR), (float)Math.Sin(aR));
        float degL = (float)(Math.Atan2(dL.Item2, dL.Item1) * 180.0 / Math.PI);
        float degR = (float)(Math.Atan2(dR.Item2, dR.Item1) * 180.0 / Math.PI);

        // (wedge start, wedge end, rule dir or null for floor, hue)
        var regions = new (float a1, float a2, (float, float)? dir, float hue)[]
        {
            (degL, 270f, dL, 96f),      // left wall.
            (-90f, degR, dR, 118f),     // right wall.
            (degR, degL, null, 136f),   // floor: rules parallel to the LEFT wall's.
        };

        for (int f = 0; f < regions.Length; f++)
        {
            var (a1, a2, dir, hue0) = regions[f];
            float hue = (float)(hue0 + 8 * Math.Sin(t * 0.07 + f * 2.1)) * 2f;   // Skia hue is 0..360.

            using var wedge = new SKPath();
            wedge.MoveTo(vx, vy);
            for (int i = 0; i <= 13; i++)
            {
                double deg = (a1 + (a2 - a1) * i / 13.0) * Math.PI / 180.0;
                wedge.LineTo(vx + R * (float)Math.Cos(deg), vy + R * (float)Math.Sin(deg));
            }
            wedge.Close();

            canvas.Save();
            canvas.ClipPath(wedge, antialias: true);
            canvas.DrawColor(SKColor.FromHsv(hue, 82f, 10f));   // near-black tinted panel.

            using var halo = new SKPaint { IsAntialias = true, StrokeWidth = 4, Color = SKColor.FromHsv(hue, 84f, 45f) };
            using var core = new SKPaint { IsAntialias = true, StrokeWidth = 1, Color = SKColor.FromHsv(hue - 12f, 51f, 100f) };

            if (dir.HasValue)
            {
                var (dx, dy) = dir.Value;
                for (int k = 0; k < 80; k++)
                {
                    float fy = vy - (k * spacing + phWall);
                    if (fy < -diag) { break; }
                    canvas.DrawLine(vx, fy, vx + dx * R, fy + dy * R, halo);
                    canvas.DrawLine(vx, fy, vx + dx * R, fy + dy * R, core);
                }
            }
            else
            {
                var (dx, dy) = dL;
                float px2 = (float)Math.Sin(aL), py2 = (float)Math.Cos(aL);   // step dir.
                for (int m = -40; m <= 40; m++)
                {
                    float off = m * spacing + phFloor;
                    float px = vx + px2 * off, py = vy + py2 * off;
                    canvas.DrawLine(px - dx * R, py - dy * R, px + dx * R, py + dy * R, halo);
                    canvas.DrawLine(px - dx * R, py - dy * R, px + dx * R, py + dy * R, core);
                }
            }
            canvas.Restore();
        }

        // Faint fold + seams.
        using var seam = new SKPaint { IsAntialias = true, StrokeWidth = 1, Color = new SKColor(16, 16, 24) };
        canvas.DrawLine(vx, vy, vx, vy - R, seam);
        canvas.DrawLine(vx, vy, vx + dL.Item1 * R, vy + dL.Item2 * R, seam);
        canvas.DrawLine(vx, vy, vx + dR.Item1 * R, vy + dR.Item2 * R, seam);

        return bmp;
    }

    // --- VHS grade + BGR conversion --------------------------------------------------------

    /// <summary>Applies the colour grade (desat/dim matrix, wash+warm bias, scanline/vignette
    /// post map) while converting the composited BGRA frame to the encoder's BGR24.</summary>
    private byte[] GradeToBgr(SKBitmap frame)
    {
        var bgr = new byte[WIDTH * HEIGHT * 3];
        ReadOnlySpan<byte> src;
        unsafe { src = new ReadOnlySpan<byte>((byte*)frame.GetPixels().ToPointer(), WIDTH * HEIGHT * 4); }

        int m00 = _gradeM[0], m01 = _gradeM[1];
        for (int p = 0, s = 0, d = 0; p < WIDTH * HEIGHT; p++, s += 4, d += 3)
        {
            int b = src[s], g = src[s + 1], r = src[s + 2];
            // Desat+dim matrix: diag m00, off-diag m01 (symmetric across channels).
            int b2 = (m00 * b + m01 * (g + r)) >> 8;
            int g2 = (m00 * g + m01 * (b + r)) >> 8;
            int r2 = (m00 * r + m01 * (b + g)) >> 8;
            int post = _postByte[p];
            int bp = p * 3;
            bgr[d + 0] = (byte)Math.Min(255, ((b2 * post) >> 8) + _biasPost[bp + 0]);
            bgr[d + 1] = (byte)Math.Min(255, ((g2 * post) >> 8) + _biasPost[bp + 1]);
            bgr[d + 2] = (byte)Math.Min(255, ((r2 * post) >> 8) + _biasPost[bp + 2]);
        }
        return bgr;
    }

    // --- One-time setup helpers -----------------------------------------------------------

    /// <summary>The model's constant identity input: face crop, lower half masked, 6 BGR planes.</summary>
    private static DenseTensor<float> BuildFaceInput(SKBitmap persona, (int y1, int y2, int x1, int x2) box)
    {
        var (y1, y2, x1, x2) = box;
        using var crop = new SKBitmap(new SKImageInfo(IMG_SIZE, IMG_SIZE, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var c = new SKCanvas(crop))
        {
            c.DrawBitmap(persona, SKRect.Create(x1, y1, x2 - x1, y2 - y1), SKRect.Create(IMG_SIZE, IMG_SIZE),
                new SKPaint { FilterQuality = SKFilterQuality.Medium });
        }

        var tensor = new DenseTensor<float>(new[] { 1, 6, IMG_SIZE, IMG_SIZE });
        unsafe
        {
            var px = (byte*)crop.GetPixels().ToPointer();
            for (int y = 0; y < IMG_SIZE; y++)
            {
                for (int x = 0; x < IMG_SIZE; x++)
                {
                    int o = (y * IMG_SIZE + x) * 4;
                    bool masked = y >= IMG_SIZE / 2;
                    // Planes 0-2: masked BGR; planes 3-5: full BGR (matches np.concatenate order).
                    tensor[0, 0, y, x] = masked ? 0f : px[o] / 255f;
                    tensor[0, 1, y, x] = masked ? 0f : px[o + 1] / 255f;
                    tensor[0, 2, y, x] = masked ? 0f : px[o + 2] / 255f;
                    tensor[0, 3, y, x] = px[o] / 255f;
                    tensor[0, 4, y, x] = px[o + 1] / 255f;
                    tensor[0, 5, y, x] = px[o + 2] / 255f;
                }
            }
        }
        return tensor;
    }

    /// <summary>Loads the figure matte and repairs it for a bust portrait: boost weak (dark
    /// suit) alpha, fill down in the shoulder zone, feather. Returns per-pixel 0..1.</summary>
    private static float[] LoadRepairedMatte(string mattePath)
    {
        var m = new float[WIDTH * HEIGHT];
        if (File.Exists(mattePath))
        {
            using var raw = SKBitmap.Decode(mattePath);
            using var resized = raw.Resize(new SKImageInfo(WIDTH, HEIGHT, SKColorType.Bgra8888, SKAlphaType.Premul),
                SKFilterQuality.Medium);
            unsafe
            {
                var px = (byte*)resized.GetPixels().ToPointer();
                for (int p = 0; p < m.Length; p++)
                {
                    m[p] = Math.Clamp((px[p * 4] / 255f - 0.08f) / 0.24f, 0f, 1f);   // boost weak alpha.
                }
            }

            int y0 = (int)(0.70 * HEIGHT);
            for (int x = 0; x < WIDTH; x++)
            {
                float run = 0f;
                for (int y = y0; y < HEIGHT; y++)
                {
                    int p = y * WIDTH + x;
                    float v = Math.Clamp(m[p] * 1.5f, 0f, 1f);   // extra boost near the corners.
                    run = Math.Max(run, v);                       // bust fill-down.
                    m[p] = run;
                }
            }

            Feather(m);
        }
        else
        {
            logger.LogWarning("No matte at {Path}; the figure will show its original photo background.", mattePath);
            Array.Fill(m, 1f);
        }
        return m;
    }

    /// <summary>Small separable blur to feather the matte edge (stand-in for cv2's 5x5 Gaussian).</summary>
    private static void Feather(float[] m)
    {
        var tmp = new float[m.Length];
        for (int y = 0; y < HEIGHT; y++)
        {
            for (int x = 0; x < WIDTH; x++)
            {
                float acc = 0f;
                for (int k = -2; k <= 2; k++)
                {
                    int xx = Math.Clamp(x + k, 0, WIDTH - 1);
                    acc += m[y * WIDTH + xx] * (k == 0 ? 6 : Math.Abs(k) == 1 ? 4 : 1);
                }
                tmp[y * WIDTH + x] = acc / 16f;
            }
        }
        for (int x = 0; x < WIDTH; x++)
        {
            for (int y = 0; y < HEIGHT; y++)
            {
                float acc = 0f;
                for (int k = -2; k <= 2; k++)
                {
                    int yy = Math.Clamp(y + k, 0, HEIGHT - 1);
                    acc += tmp[yy * WIDTH + x] * (k == 0 ? 6 : Math.Abs(k) == 1 ? 4 : 1);
                }
                m[y * WIDTH + x] = acc / 16f;
            }
        }
    }

    /// <summary>Bakes the matte into the persona as premultiplied alpha (one SrcOver = full blend).</summary>
    private static SKBitmap ApplyMatte(SKBitmap opaque, float[] alpha)
    {
        var matted = new SKBitmap(new SKImageInfo(WIDTH, HEIGHT, SKColorType.Bgra8888, SKAlphaType.Premul));
        unsafe
        {
            var src = (byte*)opaque.GetPixels().ToPointer();
            var dst = (byte*)matted.GetPixels().ToPointer();
            for (int p = 0; p < alpha.Length; p++)
            {
                float a = alpha[p];
                int s = p * 4;
                dst[s] = (byte)(src[s] * a);
                dst[s + 1] = (byte)(src[s + 1] * a);
                dst[s + 2] = (byte)(src[s + 2] * a);
                dst[s + 3] = (byte)(a * 255f);
            }
        }
        return matted;
    }

    /// <summary>Precomputes the grade: Q8 colour matrix, scanline*vignette post map, bias*post.</summary>
    private static void BuildGradeTables(float[] alpha, out byte[] postByte, out byte[] biasPost, out int[] gradeM)
    {
        // out = ((0.88*I + 0.04*ones) * 0.92) * rgb * post + bias * post   (bias BGR = 10,14,20).
        gradeM = new[] { (int)Math.Round((0.88 * 0.92 + 0.12 / 3.0 * 0.92) * 256),   // diag incl. own share.
                         (int)Math.Round(0.12 / 3.0 * 0.92 * 256) };

        postByte = new byte[WIDTH * HEIGHT];
        biasPost = new byte[WIDTH * HEIGHT * 3];
        Span<float> bias = stackalloc float[] { 10f, 14f, 20f };
        for (int y = 0; y < HEIGHT; y++)
        {
            float ry = (y - HEIGHT / 2f) / (HEIGHT / 2f);
            float scan = y % 3 == 0 ? 0.84f : 1.0f;
            for (int x = 0; x < WIDTH; x++)
            {
                float rx = (x - WIDTH / 2f) / (WIDTH / 2f);
                float vig = Math.Clamp(1.06f - 0.30f * (rx * rx + ry * ry), 0.60f, 1.0f);
                float post = vig * scan;
                int p = y * WIDTH + x;
                postByte[p] = (byte)(post * 255f);
                for (int c = 0; c < 3; c++)
                {
                    biasPost[p * 3 + c] = (byte)Math.Clamp(bias[c] * post, 0f, 255f);
                }
            }
        }
    }

    private static (int, int, int, int)? ParseBox(string s)
    {
        var parts = s?.Split(',');
        return parts?.Length == 4
            ? (int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]), int.Parse(parts[3]))
            : null;
    }

    private static (int, int, int, int)[] ParseEyes(string s) =>
        string.IsNullOrWhiteSpace(s)
            ? null
            : s.Split(';').Select(e =>
              {
                  var p = e.Split(',');
                  return (int.Parse(p[0]), int.Parse(p[1]), int.Parse(p[2]), int.Parse(p[3]));
              }).ToArray();

    // --- Offline smoke test ----------------------------------------------------------------

    /// <summary>Renders <paramref name="count"/> frames driven by <paramref name="pcm16"/> to
    /// PNGs in <paramref name="outDir"/> - validates the whole in-process pipeline with no
    /// WebRTC involved (--avatar-test).</summary>
    public void TestRenderFrames(short[] pcm16, string outDir, int count = 100)
    {
        BeginSpeech();
        PushAudio(pcm16, MelSpectrogram.SampleRate);
        RefreshMel();

        for (int i = 0; i < count; i++)
        {
            var bgr = RenderFrame(NextMouth(), i);
            // Save every 10th frame, plus the pose-snap window around t=3.7s (frames 92-98)
            // so liveness glitches are captured.
            if (i % 10 != 0 && !(i >= 92 && i <= 98))
            {
                continue;
            }
            using var bmp = new SKBitmap(new SKImageInfo(WIDTH, HEIGHT, SKColorType.Bgra8888, SKAlphaType.Opaque));
            unsafe
            {
                var dst = (byte*)bmp.GetPixels().ToPointer();
                for (int p = 0, s = 0, d = 0; p < WIDTH * HEIGHT; p++, s += 3, d += 4)
                {
                    dst[d] = bgr[s]; dst[d + 1] = bgr[s + 1]; dst[d + 2] = bgr[s + 2]; dst[d + 3] = 255;
                }
            }
            using var img = SKImage.FromBitmap(bmp);
            using var data = img.Encode(SKEncodedImageFormat.Png, 90);
            using var fs = File.OpenWrite(Path.Combine(outDir, $"wav2lip_frame_{i:D3}.png"));
            data.SaveTo(fs);
        }
        EndSpeech();
    }

    public void Dispose()
    {
        _isClosed = true;
        _renderTimer?.Dispose();
        // _session is shared for the app's lifetime (renderers are per-connection).
        _persona?.Dispose();
        _bgCache?.Dispose();
        _videoEncoder?.Dispose();
    }
}
