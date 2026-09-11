//-----------------------------------------------------------------------------
// Filename: MaxHeadroomVideoSource.cs
//
// Description: A SIPSorcery IVideoSource that renders a stylised "Max Headroom"
// style talking head with SkiaSharp. The mouth shape is driven by the
// CurrentViseme property (an Azure Speech viseme id, 0-21) which the
// AzureTtsSpeaker updates in sync with the synthesised audio. A few retro
// glitch / scanline effects are layered on top to get the 80s CGI look.
//
// The source owns an IVideoEncoder (passed to the constructor). Each rendered
// frame is encoded in the render loop and emitted on OnVideoSourceEncodedSample,
// which Program.cs wires straight to RTCPeerConnection.SendVideo - the same
// pattern the in-box VideoTestPatternSource uses. The unencoded BGR frame is
// still published on OnVideoSourceRawSample for any raw subscribers (e.g. a
// local preview). When no encoder is supplied the source renders raw frames
// only, which is all the --snapshot preview path needs.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorceryMedia.Abstractions;
using SkiaSharp;

namespace demo
{
    public class MaxHeadroomVideoSource : IAvatarRenderer
    {
        public const int WIDTH = 640;
        public const int HEIGHT = 480;

        private const int VIDEO_SAMPLING_RATE = 90000;
        private const int DEFAULT_FRAMES_PER_SECOND = 25;
        private const int H264_SUGGESTED_FORMAT_ID = 100;

        // Lip-sync heuristic (the "how do I turn audio into a face" logic that a neural renderer
        // would do with a model). A window's RMS is normalised by a fixed reference to a 0..1
        // loudness, then mapped onto a handful of the 0-21 viseme mouth shapes below. This lives
        // here - not in the speaker - because it is specific to how THIS renderer animates.
        private const int EnvelopeRmsRef = 4000;
        private static readonly int[] LowVisemes = { 19, 6, 14 };   // ~0.2 open
        private static readonly int[] MidVisemes = { 4, 1 };        // ~0.4 open
        private static readonly int[] HighVisemes = { 2, 11, 9 };   // ~0.6-0.8 open
        private int _visemeRotation;

        public static readonly List<VideoFormat> SupportedFormats = new List<VideoFormat>
        {
            new VideoFormat(VideoCodecsEnum.H264, H264_SUGGESTED_FORMAT_ID, VIDEO_SAMPLING_RATE, "packetization-mode=1")
        };

        // Azure viseme id (0-21) -> mouth shape parameters: openness, horizontal
        // spread and lip rounding (each 0..1). Same mapping used in the design
        // preview; good enough to read as lip-sync.
        private static readonly (float open, float spread, float round)[] _visemeShapes =
        {
            (0.02f, 0.85f, 0.20f), // 0  silence
            (0.45f, 0.90f, 0.10f), // 1  ae ah uh
            (0.82f, 0.95f, 0.05f), // 2  aa
            (0.60f, 0.55f, 0.60f), // 3  ao
            (0.40f, 0.85f, 0.20f), // 4  eh uh
            (0.35f, 0.60f, 0.45f), // 5  er
            (0.20f, 1.00f, 0.00f), // 6  i ih y
            (0.26f, 0.38f, 0.90f), // 7  w u
            (0.50f, 0.50f, 0.80f), // 8  o
            (0.60f, 0.60f, 0.50f), // 9  aw
            (0.50f, 0.60f, 0.50f), // 10 oy
            (0.62f, 0.80f, 0.20f), // 11 ay
            (0.32f, 0.80f, 0.20f), // 12 h
            (0.26f, 0.60f, 0.50f), // 13 r
            (0.30f, 0.80f, 0.12f), // 14 l
            (0.12f, 0.92f, 0.05f), // 15 s z
            (0.22f, 0.55f, 0.60f), // 16 sh ch jh
            (0.15f, 0.80f, 0.10f), // 17 th
            (0.10f, 0.85f, 0.10f), // 18 f v
            (0.20f, 0.80f, 0.10f), // 19 d t n
            (0.26f, 0.80f, 0.15f), // 20 k g ng
            (0.00f, 0.86f, 0.15f), // 21 p b m
        };

        private static readonly ILogger logger = SIPSorcery.LogFactory.CreateLogger<MaxHeadroomVideoSource>();

        private readonly MediaFormatManager<VideoFormat> _formatManager = new(SupportedFormats);
        private readonly SKBitmap _bitmap = new(WIDTH, HEIGHT, SKColorType.Bgra8888, SKAlphaType.Premul);
        private readonly byte[] _bgrBuffer = new byte[WIDTH * HEIGHT * 3];
        private readonly Random _rng = new();

        private Timer _renderTimer;
        private int _frameSpacingMs = 1000 / DEFAULT_FRAMES_PER_SECOND;
        private int _frameCount;
        private bool _isStarted;
        private bool _isPaused;
        private bool _isClosed;
        private bool _faulted;

        // Animation state, written by the speaker thread, read by the render thread.
        private volatile int _currentViseme;
        private volatile bool _isSpeaking;

        /// <summary>The current Azure viseme id (0-21) to render the mouth for.</summary>
        public int CurrentViseme { get => _currentViseme; set => _currentViseme = value; }

        /// <summary>When true a little extra "live" jitter/glitch is added.</summary>
        public bool IsSpeaking { get => _isSpeaking; set => _isSpeaking = value; }

        // --- IAvatarRenderer: driven by the speaker's audio ---------------------------------

        /// <summary>The amplitude heuristic reacts instantly, so pushes must be paced to playback.</summary>
        public bool PacesAudioInternally => false;

        /// <summary>Enter the talking state (adds glitch liveliness while an utterance plays).</summary>
        public void BeginSpeech() => _isSpeaking = true;

        /// <summary>
        /// Drive the mouth from a window of the audio that is currently sounding: take its RMS,
        /// normalise to a 0..1 loudness and pick a viseme. A neural renderer would instead enqueue
        /// this PCM to its model; here the "model" is the amplitude->viseme mapping.
        /// </summary>
        public void PushAudio(ReadOnlySpan<short> pcm16, int sampleRate)
        {
            if (pcm16.Length == 0)
            {
                return;
            }

            double sumSq = 0;
            for (int i = 0; i < pcm16.Length; i++)
            {
                double s = pcm16[i];
                sumSq += s * s;
            }
            float level = (float)Math.Min(1.0, Math.Sqrt(sumSq / pcm16.Length) / EnvelopeRmsRef);
            _currentViseme = VisemeForLevel(level);
        }

        /// <summary>Leave the talking state and close the mouth.</summary>
        public void EndSpeech()
        {
            _currentViseme = 0;
            _isSpeaking = false;
        }

        /// <summary>Maps a normalised loudness level (0..1) to a viseme id, rotating within each band for liveliness.</summary>
        private int VisemeForLevel(float level)
        {
            if (level < 0.15f)
            {
                return 0; // closed / silence
            }

            int[] band = level < 0.40f ? LowVisemes : level < 0.70f ? MidVisemes : HighVisemes;
            return band[_visemeRotation++ % band.Length];
        }

        public event RawVideoSampleDelegate OnVideoSourceRawSample;
        public event EncodedSampleDelegate OnVideoSourceEncodedSample;
        public event SourceErrorDelegate OnVideoSourceError;

#pragma warning disable CS0067
        public event RawVideoSampleFasterDelegate OnVideoSourceRawSampleFaster;
#pragma warning restore CS0067

        private readonly IVideoEncoder _videoEncoder;

        /// <param name="encoder">
        /// Encoder used to turn each rendered frame into an encoded sample fired on
        /// <see cref="OnVideoSourceEncodedSample"/>. If null the source only produces
        /// raw BGR frames (e.g. for the snapshot preview).
        /// </param>
        public MaxHeadroomVideoSource(IVideoEncoder encoder = null)
        {
            _videoEncoder = encoder;
            _renderTimer = new Timer(RenderFrame, null, Timeout.Infinite, Timeout.Infinite);
        }

        public List<VideoFormat> GetVideoSourceFormats() => _formatManager.GetSourceFormats();
        public void SetVideoSourceFormat(VideoFormat videoFormat) => _formatManager.SetSelectedFormat(videoFormat);
        public void RestrictFormats(Func<VideoFormat, bool> filter) => _formatManager.RestrictFormats(filter);
        public void ForceKeyFrame() => _videoEncoder?.ForceKeyFrame();
        public bool HasEncodedVideoSubscribers() => OnVideoSourceEncodedSample != null;
        public bool IsVideoSourcePaused() => _isPaused;

        public void ExternalVideoSourceRawSample(uint durationMilliseconds, int width, int height, byte[] sample, VideoPixelFormatsEnum pixelFormat) =>
            throw new NotImplementedException("The Max Headroom source generates its own frames.");
        public void ExternalVideoSourceRawSampleFaster(uint durationMilliseconds, RawImage rawImage) =>
            throw new NotImplementedException("The Max Headroom source generates its own frames.");

        public Task StartVideo()
        {
            if (!_isStarted)
            {
                _isStarted = true;
                _renderTimer.Change(0, _frameSpacingMs);
            }
            return Task.CompletedTask;
        }

        public Task PauseVideo()
        {
            _isPaused = true;
            _renderTimer.Change(Timeout.Infinite, Timeout.Infinite);
            return Task.CompletedTask;
        }

        public Task ResumeVideo()
        {
            _isPaused = false;
            _renderTimer.Change(0, _frameSpacingMs);
            return Task.CompletedTask;
        }

        public Task CloseVideo()
        {
            if (!_isClosed)
            {
                _isClosed = true;
                _renderTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            }
            return Task.CompletedTask;
        }

        private void RenderFrame(object state)
        {
            bool hasRawSubscribers = OnVideoSourceRawSample != null;
            bool hasEncodedSubscribers = _videoEncoder != null && OnVideoSourceEncodedSample != null && !_formatManager.SelectedFormat.IsEmpty();

            if (_isClosed || _isPaused || _faulted || (!hasRawSubscribers && !hasEncodedSubscribers))
            {
                return;
            }

            try
            {
                _frameCount++;

                using (var canvas = new SKCanvas(_bitmap))
                {
                    DrawScene(canvas, _frameCount);
                    //ApplyGlitch(canvas, _frameCount);
                }

                BgraToBgr(_bitmap.GetPixelSpan(), _bgrBuffer);

                OnVideoSourceRawSample?.Invoke((uint)_frameSpacingMs, WIDTH, HEIGHT, _bgrBuffer, VideoPixelFormatsEnum.Bgr);

                if (hasEncodedSubscribers)
                {
                    var encodedBuffer = _videoEncoder.EncodeVideo(WIDTH, HEIGHT, _bgrBuffer, VideoPixelFormatsEnum.Bgr, _formatManager.SelectedFormat.Codec);

                    if (encodedBuffer != null)
                    {
                        uint fps = _frameSpacingMs > 0 ? 1000u / (uint)_frameSpacingMs : DEFAULT_FRAMES_PER_SECOND;
                        uint durationRtpTS = VIDEO_SAMPLING_RATE / fps;
                        OnVideoSourceEncodedSample?.Invoke(durationRtpTS, encodedBuffer);
                    }
                }
            }
            catch (Exception excp)
            {
                // A render/encode failure here (e.g. the negotiated codec is missing from the
                // FFmpeg build) is not transient - it recurs on every frame. Stop the render loop
                // and surface the error once via OnVideoSourceError rather than logging it ~25x a
                // second.
                _faulted = true;
                _renderTimer.Change(Timeout.Infinite, Timeout.Infinite);
                logger.LogError(excp, "Fatal error in MaxHeadroomVideoSource render loop; stopping video.");
                OnVideoSourceError?.Invoke(excp.Message);
            }
        }

        /// <summary>
        /// Renders a single frame for the given viseme and writes it to a PNG. Handy for
        /// eyeballing the avatar without standing up a full WebRTC call.
        /// </summary>
        public void SaveSnapshot(string path, int visemeId)
        {
            _currentViseme = visemeId;
            using (var canvas = new SKCanvas(_bitmap))
            {
                DrawScene(canvas, 12);
                //ApplyGlitch(canvas, 12);
            }
            using var image = SKImage.FromBitmap(_bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 90);
            using var fs = System.IO.File.OpenWrite(path);
            data.SaveTo(fs);
        }

        private static void BgraToBgr(ReadOnlySpan<byte> bgra, byte[] bgr)
        {
            int j = 0;
            for (int i = 0; i < bgra.Length; i += 4)
            {
                bgr[j++] = bgra[i];     // B
                bgr[j++] = bgra[i + 1]; // G
                bgr[j++] = bgra[i + 2]; // R
            }
        }

        private void DrawScene(SKCanvas canvas, int frame)
        {
            canvas.Clear(new SKColor(0x05, 0x03, 0x10));

            DrawRetroGrid(canvas, frame);
            DrawHead(canvas, frame);
        }

        // Two louver panels of evenly-spaced neon bars that meet behind the head
        // and counter-rotate, so the field reads as a chevron that slowly opens
        // and closes while the hue cycles - the classic Max Headroom rotating
        // "venetian blind" background.
        private static void DrawRetroGrid(SKCanvas canvas, int frame)
        {
            float t = frame / (float)DEFAULT_FRAMES_PER_SECOND;
            float cx = WIDTH / 2f;
            float cy = HEIGHT * 0.45f;

            float baseAngle = 12f * (float)Math.Sin(t * 0.45);        // shared sway
            float spread = 18f * (float)Math.Sin(t * 0.30) + 14f;     // chevron half-angle

            DrawLouverPanel(canvas, new SKRect(0, 0, cx, HEIGHT), cx, cy, baseAngle - spread, t, hueBase: 190f);
            DrawLouverPanel(canvas, new SKRect(cx, 0, WIDTH, HEIGHT), cx, cy, baseAngle + spread, t, hueBase: 280f);
        }

        private static void DrawLouverPanel(SKCanvas canvas, SKRect clip, float pivotX, float pivotY, float angleDeg, float t, float hueBase)
        {
            const float spacing = 16f;
            float scroll = (t * 22f) % spacing;   // bars stream outward
            float hueDrift = t * 35f;             // whole field cycles hue

            canvas.Save();
            canvas.ClipRect(clip);
            canvas.RotateDegrees(angleDeg, pivotX, pivotY);

            using var glow = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 12, Color = new SKColor(0, 0, 0, 40) };
            using var bar = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 6 };

            int n = (int)(HEIGHT * 1.6f / spacing);
            float x0 = pivotX - WIDTH, x1 = pivotX + WIDTH;
            for (int i = -n; i <= n; i++)
            {
                float y = pivotY + i * spacing + scroll;
                // Keep each panel a coherent colour at any instant (subtle banding
                // across the bars) that cycles through the neon spectrum over time.
                float hue = (hueBase + hueDrift + i * 1.4f) % 360f;
                byte alpha = (byte)(120 + 110 * (0.5 + 0.5 * Math.Sin(i * 0.5 + t)));
                bar.Color = SKColor.FromHsv(hue, 92, 100).WithAlpha(alpha);
                canvas.DrawLine(x0, y, x1, y, glow);
                canvas.DrawLine(x0, y, x1, y, bar);
            }
            canvas.Restore();
        }

        private void DrawHead(SKCanvas canvas, int frame)
        {
            float cx = WIDTH / 2f;
            // Subtle bob so the head never looks frozen.
            float bob = (float)Math.Sin(frame / 6.0) * 2f;
            float cy = HEIGHT / 2f + bob;

            DrawSuit(canvas, cx, cy);
            DrawNeck(canvas, cx, cy);
            DrawFace(canvas, cx, cy);
            DrawHair(canvas, cx, cy);
            DrawBrowsAndEyes(canvas, cx, cy, frame);
            DrawNose(canvas, cx, cy);
            DrawMouth(canvas, cx, cy + 92, _currentViseme);
            DrawSpecular(canvas, cx, cy);
        }

        // Glossy dark suit shoulders + shiny lapels, light shirt and tie.
        private static void DrawSuit(SKCanvas canvas, float cx, float cy)
        {
            float top = cy + 150;
            using (var jacket = new SKPaint { IsAntialias = true })
            {
                jacket.Shader = SKShader.CreateLinearGradient(
                    new SKPoint(cx - 220, top), new SKPoint(cx + 220, HEIGHT),
                    new[] { new SKColor(0x10, 0x12, 0x1C), new SKColor(0x28, 0x2C, 0x3A), new SKColor(0x10, 0x12, 0x1C) },
                    new[] { 0f, 0.5f, 1f }, SKShaderTileMode.Clamp);
                using var path = new SKPath();
                path.MoveTo(cx - 70, top);
                path.CubicTo(cx - 150, top + 10, cx - 230, top + 70, cx - 250, HEIGHT);
                path.LineTo(cx + 250, HEIGHT);
                path.CubicTo(cx + 230, top + 70, cx + 150, top + 10, cx + 70, top);
                path.Close();
                canvas.DrawPath(path, jacket);
            }

            // Shirt wedge.
            using (var shirt = new SKPaint { Color = new SKColor(0xC8, 0xD6, 0xE8), IsAntialias = true })
            using (var path = new SKPath())
            {
                path.MoveTo(cx - 46, top - 6);
                path.LineTo(cx + 46, top - 6);
                path.LineTo(cx + 30, HEIGHT);
                path.LineTo(cx - 30, HEIGHT);
                path.Close();
                canvas.DrawPath(path, shirt);
            }

            // Shiny lapels.
            using (var lapel = new SKPaint { Color = new SKColor(0x3A, 0x40, 0x52), IsAntialias = true })
            {
                using var l = new SKPath();
                l.MoveTo(cx - 60, top); l.LineTo(cx - 6, top + 18); l.LineTo(cx - 64, top + 120); l.Close();
                canvas.DrawPath(l, lapel);
                using var r = new SKPath();
                r.MoveTo(cx + 60, top); r.LineTo(cx + 6, top + 18); r.LineTo(cx + 64, top + 120); r.Close();
                canvas.DrawPath(r, lapel);
            }

            // Collar knot + tie blade.
            using (var tie = new SKPaint { Color = new SKColor(0x1C, 0x3A, 0x86), IsAntialias = true })
            {
                using var knot = new SKPath();
                knot.MoveTo(cx - 14, top + 4); knot.LineTo(cx + 14, top + 4);
                knot.LineTo(cx + 18, top + 30); knot.LineTo(cx - 18, top + 30); knot.Close();
                canvas.DrawPath(knot, tie);
                using var blade = new SKPath();
                blade.MoveTo(cx - 16, top + 30); blade.LineTo(cx + 16, top + 30);
                blade.LineTo(cx + 22, HEIGHT); blade.LineTo(cx - 22, HEIGHT); blade.Close();
                canvas.DrawPath(blade, tie);
            }
        }

        private static void DrawNeck(SKCanvas canvas, float cx, float cy)
        {
            using (var neck = new SKPaint { Color = new SKColor(0xC8, 0x82, 0x3E), IsAntialias = true })
            {
                canvas.DrawRoundRect(cx - 46, cy + 90, 92, 90, 24, 24, neck);
            }
            using (var shade = new SKPaint { Color = new SKColor(0x00, 0x00, 0x00, 0x40), IsAntialias = true })
            {
                canvas.DrawRoundRect(cx - 46, cy + 120, 92, 60, 24, 24, shade);
            }
        }

        // Tapered jaw face built from a path, with a side-light gradient and a
        // soft unlit-side shadow for the hard plastic-CGI look.
        private static void DrawFace(SKCanvas canvas, float cx, float cy)
        {
            using var path = new SKPath();
            float t = cy - 175;   // top of forehead
            path.MoveTo(cx - 118, t + 30);
            path.CubicTo(cx - 126, t - 6, cx + 126, t - 6, cx + 118, t + 30);    // forehead
            path.CubicTo(cx + 150, cy - 40, cx + 140, cy + 10, cx + 110, cy + 70); // right temple/cheek
            path.CubicTo(cx + 92, cy + 130, cx + 40, cy + 168, cx, cy + 170);      // right jaw -> chin
            path.CubicTo(cx - 40, cy + 168, cx - 92, cy + 130, cx - 110, cy + 70); // left jaw
            path.CubicTo(cx - 140, cy + 10, cx - 150, cy - 40, cx - 118, t + 30);  // left temple
            path.Close();

            using (var skin = new SKPaint { IsAntialias = true })
            {
                skin.Shader = SKShader.CreateLinearGradient(
                    new SKPoint(cx - 120, cy), new SKPoint(cx + 130, cy),
                    new[] { new SKColor(0xF2, 0xB0, 0x6A), new SKColor(0xDC, 0x90, 0x44), new SKColor(0x9C, 0x5E, 0x28) },
                    new[] { 0f, 0.55f, 1f }, SKShaderTileMode.Clamp);
                canvas.DrawPath(path, skin);
            }

            canvas.Save();
            canvas.ClipPath(path, SKClipOperation.Intersect, true);
            using (var sh = new SKPaint { Color = new SKColor(0x40, 0x22, 0x08, 0x40), IsAntialias = true })
            {
                sh.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 14);
                canvas.DrawRoundRect(cx + 64, cy - 120, 86, 290, 40, 40, sh);
            }
            canvas.Restore();
        }

        // Slicked-back blond hair with strand highlights and a soft widow's peak.
        private static void DrawHair(SKCanvas canvas, float cx, float cy)
        {
            float t = cy - 175;
            using var path = new SKPath();
            path.MoveTo(cx - 122, t + 40);
            path.CubicTo(cx - 130, t - 60, cx + 130, t - 60, cx + 122, t + 40);  // dome
            path.CubicTo(cx + 96, t + 6, cx + 60, t + 14, cx + 30, t + 22);      // hairline
            path.CubicTo(cx + 12, t + 34, cx - 12, t + 34, cx - 30, t + 22);
            path.CubicTo(cx - 60, t + 14, cx - 96, t + 6, cx - 122, t + 40);
            path.Close();

            using (var hair = new SKPaint { IsAntialias = true })
            {
                hair.Shader = SKShader.CreateLinearGradient(
                    new SKPoint(cx, t - 50), new SKPoint(cx, t + 60),
                    new[] { new SKColor(0xC9, 0x9A, 0x3C), new SKColor(0xE8, 0xC4, 0x6A), new SKColor(0xA8, 0x7C, 0x2C) },
                    new[] { 0f, 0.5f, 1f }, SKShaderTileMode.Clamp);
                canvas.DrawPath(path, hair);
            }

            canvas.Save();
            canvas.ClipPath(path, SKClipOperation.Intersect, true);
            using (var strand = new SKPaint { Color = new SKColor(0xFA, 0xE4, 0x9A, 0xC0), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3 })
            {
                for (int i = -5; i <= 5; i++)
                {
                    float x = cx + i * 20;
                    using var s = new SKPath();
                    s.MoveTo(x, t - 40);
                    s.CubicTo(x + 6, t - 10, x + 6, t + 20, x + 2, t + 50);
                    canvas.DrawPath(s, strand);
                }
            }
            using (var dark = new SKPaint { Color = new SKColor(0x6A, 0x4C, 0x16, 0x70), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2 })
            {
                for (int i = -4; i <= 4; i++)
                {
                    float x = cx + i * 20 + 10;
                    using var s = new SKPath();
                    s.MoveTo(x, t - 40);
                    s.CubicTo(x + 6, t - 10, x + 6, t + 20, x + 2, t + 50);
                    canvas.DrawPath(s, dark);
                }
            }
            canvas.Restore();
        }

        private static void DrawBrowsAndEyes(SKCanvas canvas, float cx, float cy, int frame)
        {
            float ey = cy - 70;
            float dx = 56;
            float look = (float)Math.Sin(frame / 13.0) * 5f;   // eyes dart about

            using (var socket = new SKPaint { Color = new SKColor(0x7A, 0x46, 0x1C, 0x55), IsAntialias = true })
            {
                canvas.DrawOval(cx - dx, ey, 40, 24, socket);
                canvas.DrawOval(cx + dx, ey, 40, 24, socket);
            }
            using (var white = new SKPaint { Color = new SKColor(0xF4, 0xF1, 0xE8), IsAntialias = true })
            {
                canvas.DrawOval(cx - dx, ey, 30, 16, white);
                canvas.DrawOval(cx + dx, ey, 30, 16, white);
            }
            using (var iris = new SKPaint { Color = new SKColor(0x3C, 0x6E, 0x8C), IsAntialias = true })
            using (var pupil = new SKPaint { Color = new SKColor(0x08, 0x0A, 0x10), IsAntialias = true })
            using (var spark = new SKPaint { Color = new SKColor(0xFF, 0xFF, 0xFF, 0xC0), IsAntialias = true })
            {
                foreach (float ex in new[] { cx - dx, cx + dx })
                {
                    canvas.DrawCircle(ex + look, ey, 12, iris);
                    canvas.DrawCircle(ex + look, ey, 6, pupil);
                    canvas.DrawCircle(ex + look - 3, ey - 3, 2.5f, spark);
                }
            }
            using (var brow = new SKPaint { Color = new SKColor(0xB8, 0x8A, 0x32), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 8, StrokeCap = SKStrokeCap.Round })
            {
                canvas.DrawLine(cx - dx - 26, ey - 26, cx - dx + 24, ey - 30, brow);
                canvas.DrawLine(cx + dx - 24, ey - 30, cx + dx + 26, ey - 26, brow);
            }
            using (var lid = new SKPaint { Color = new SKColor(0x9C, 0x5E, 0x28, 0x80), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3 })
            {
                canvas.DrawArc(new SKRect(cx - dx - 30, ey - 16, cx - dx + 30, ey + 16), 180, 180, false, lid);
                canvas.DrawArc(new SKRect(cx + dx - 30, ey - 16, cx + dx + 30, ey + 16), 180, 180, false, lid);
            }
        }

        private static void DrawNose(SKCanvas canvas, float cx, float cy)
        {
            using (var sh = new SKPaint { Color = new SKColor(0x7A, 0x44, 0x18, 0x55), IsAntialias = true })
            using (var p = new SKPath())
            {
                p.MoveTo(cx - 6, cy - 60);
                p.LineTo(cx + 14, cy + 18);
                p.CubicTo(cx + 16, cy + 34, cx - 16, cy + 34, cx - 16, cy + 20);
                p.Close();
                canvas.DrawPath(p, sh);
            }
            using (var hl = new SKPaint { Color = new SKColor(0xFF, 0xDC, 0xA0, 0x90), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 4, StrokeCap = SKStrokeCap.Round })
            {
                canvas.DrawLine(cx - 2, cy - 58, cx - 4, cy + 14, hl);
            }
            using (var nostril = new SKPaint { Color = new SKColor(0x40, 0x20, 0x08), IsAntialias = true })
            {
                canvas.DrawOval(cx - 9, cy + 26, 5, 3, nostril);
                canvas.DrawOval(cx + 9, cy + 26, 5, 3, nostril);
            }
        }

        // Bright plastic specular sheen painted last (forehead, cheek).
        private static void DrawSpecular(SKCanvas canvas, float cx, float cy)
        {
            using var hi = new SKPaint { Color = new SKColor(0xFF, 0xF2, 0xD8, 0x40), IsAntialias = true };
            hi.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 10);
            canvas.DrawOval(cx - 18, cy - 134, 48, 12, hi);   // forehead sheen
            canvas.DrawOval(cx - 84, cy + 20, 14, 34, hi);     // left cheek sheen
        }

        private void DrawMouth(SKCanvas canvas, float cx, float cy, int visemeId)
        {
            if (visemeId < 0 || visemeId >= _visemeShapes.Length)
            {
                visemeId = 0;
            }

            var (open, spread, round) = _visemeShapes[visemeId];

            float hw = 70f * spread * (1f - 0.45f * round);
            float hh = Math.Max(2f, 46f * open);

            // Inner mouth (dark).
            using (var inner = new SKPaint { Color = new SKColor(0x2A, 0x10, 0x10), IsAntialias = true })
            {
                canvas.DrawOval(cx, cy, hw, hh, inner);
            }

            // Upper teeth when the mouth is open enough.
            if (open > 0.18f)
            {
                canvas.Save();
                using (var clip = new SKPath())
                {
                    clip.AddOval(new SKRect(cx - hw, cy - hh, cx + hw, cy + hh));
                    canvas.ClipPath(clip, SKClipOperation.Intersect, true);
                }
                using (var teeth = new SKPaint { Color = new SKColor(0xF1, 0xEF, 0xE8), IsAntialias = true })
                {
                    canvas.DrawRect(cx - hw, cy - hh, hw * 2, hh * 0.55f, teeth);
                }
                canvas.Restore();
            }

            // Lips.
            using (var lips = new SKPaint { Color = new SKColor(0xC0, 0x4A, 0x30), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 8 })
            {
                canvas.DrawOval(cx, cy, hw + 4, hh + 4, lips);
            }
        }

        private void ApplyGlitch(SKCanvas canvas, int frame)
        {
            // Always-on scanlines.
            using (var scan = new SKPaint { Color = new SKColor(0, 0, 0, 0x30), StrokeWidth = 1 })
            {
                for (int y = 0; y < HEIGHT; y += 3)
                {
                    canvas.DrawLine(0, y, WIDTH, y, scan);
                }
            }

            // Occasional horizontal datamosh slices (more often while speaking).
            int glitchChance = _isSpeaking ? 3 : 8;
            if (_rng.Next(glitchChance) != 0)
            {
                return;
            }

            using var snapshot = SKImage.FromBitmap(_bitmap);
            int slices = _rng.Next(1, 4);
            for (int i = 0; i < slices; i++)
            {
                int y = _rng.Next(HEIGHT);
                int h = _rng.Next(6, 26);
                int dx = _rng.Next(-30, 30);

                var src = new SKRect(0, y, WIDTH, y + h);
                var dst = new SKRect(dx, y, WIDTH + dx, y + h);

                using var tint = new SKPaint
                {
                    ColorFilter = SKColorFilter.CreateBlendMode(
                        _rng.Next(2) == 0 ? new SKColor(0xFF, 0x00, 0x60, 0x60) : new SKColor(0x00, 0xE0, 0xFF, 0x60),
                        SKBlendMode.Screen)
                };
                canvas.DrawImage(snapshot, src, dst, tint);
            }
        }

        public void Dispose()
        {
            _isClosed = true;
            _renderTimer?.Dispose();
            _bitmap?.Dispose();
            _videoEncoder?.Dispose();
        }
    }
}
