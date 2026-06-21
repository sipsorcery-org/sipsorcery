//-----------------------------------------------------------------------------
// Filename: MaxHeadroomVideoSource.cs
//
// Description: A SIPSorcery IVideoSource that renders a stylised "Max Headroom"
// style talking head with SkiaSharp. The mouth shape is driven by the
// CurrentViseme property (an Azure Speech viseme id, 0-21) which the
// AzureTtsSpeaker updates in sync with the synthesised audio. A few retro
// glitch / scanline effects are layered on top to get the 80s CGI look.
//
// The rendered frame is emitted as a raw BGR sample on OnVideoSourceRawSample.
// Program.cs pipes that into a VideoEncoderEndPoint (libvpx VP8) which encodes
// and forwards to RTCPeerConnection.SendVideo, mirroring the
// WebRTCGetStartedLibvpx example.
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
    public class MaxHeadroomVideoSource : IVideoSource, IDisposable
    {
        public const int WIDTH = 640;
        public const int HEIGHT = 480;

        private const int VIDEO_SAMPLING_RATE = 90000;
        private const int DEFAULT_FRAMES_PER_SECOND = 25;
        private const int VP8_FORMAT_ID = 96;

        public static readonly List<VideoFormat> SupportedFormats = new()
        {
            new VideoFormat(VideoCodecsEnum.VP8, VP8_FORMAT_ID, VIDEO_SAMPLING_RATE)
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

        // Animation state, written by the speaker thread, read by the render thread.
        private volatile int _currentViseme;
        private volatile bool _isSpeaking;

        /// <summary>The current Azure viseme id (0-21) to render the mouth for.</summary>
        public int CurrentViseme { get => _currentViseme; set => _currentViseme = value; }

        /// <summary>When true a little extra "live" jitter/glitch is added.</summary>
        public bool IsSpeaking { get => _isSpeaking; set => _isSpeaking = value; }

        public event RawVideoSampleDelegate OnVideoSourceRawSample;

#pragma warning disable CS0067
        public event EncodedSampleDelegate OnVideoSourceEncodedSample;
        public event RawVideoSampleFasterDelegate OnVideoSourceRawSampleFaster;
        public event SourceErrorDelegate OnVideoSourceError;
#pragma warning restore CS0067

        public MaxHeadroomVideoSource()
        {
            _renderTimer = new Timer(RenderFrame, null, Timeout.Infinite, Timeout.Infinite);
        }

        public List<VideoFormat> GetVideoSourceFormats() => _formatManager.GetSourceFormats();
        public void SetVideoSourceFormat(VideoFormat videoFormat) => _formatManager.SetSelectedFormat(videoFormat);
        public void RestrictFormats(Func<VideoFormat, bool> filter) => _formatManager.RestrictFormats(filter);
        public void ForceKeyFrame() { }
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
            if (_isClosed || _isPaused || OnVideoSourceRawSample == null)
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
            }
            catch (Exception excp)
            {
                logger.LogError(excp, "Exception MaxHeadroomVideoSource.RenderFrame.");
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
            canvas.Clear(new SKColor(0x0A, 0x0A, 0x2A));

            DrawRetroGrid(canvas, frame);
            DrawHead(canvas, frame);
        }

        private static void DrawRetroGrid(SKCanvas canvas, int frame)
        {
            using var grid = new SKPaint { Color = new SKColor(0x20, 0xC0, 0xE0, 0x55), StrokeWidth = 2, IsAntialias = true };

            // Receding horizontal lines that scroll downward for a sense of motion.
            int offset = frame % 40;
            for (int i = 0; i < 14; i++)
            {
                float y = (i * 40 + offset) % HEIGHT;
                canvas.DrawLine(0, y, WIDTH, y, grid);
            }

            // Static vertical lines.
            for (int x = 0; x <= WIDTH; x += 40)
            {
                canvas.DrawLine(x, 0, x, HEIGHT, grid);
            }
        }

        private void DrawHead(SKCanvas canvas, int frame)
        {
            float cx = WIDTH / 2f;
            // Subtle bob so the head never looks frozen.
            float bob = (float)Math.Sin(frame / 6.0) * 3f;
            float cy = HEIGHT / 2f + bob;

            // Hair / head block (slicked back, blocky).
            using (var hair = new SKPaint { Color = new SKColor(0x8A, 0x52, 0x16), IsAntialias = true })
            {
                canvas.DrawRoundRect(cx - 150, cy - 220, 300, 200, 40, 40, hair);
            }

            // Face.
            using (var face = new SKPaint { Color = new SKColor(0xE0, 0x96, 0x4A), IsAntialias = true })
            {
                canvas.DrawRoundRect(cx - 130, cy - 170, 260, 320, 60, 60, face);
            }

            // Cheek shading for a bit of plastic CGI relief.
            using (var shade = new SKPaint { Color = new SKColor(0x00, 0x00, 0x00, 0x22), IsAntialias = true })
            {
                canvas.DrawRoundRect(cx + 60, cy - 150, 70, 280, 40, 40, shade);
            }

            // Sunglasses.
            using (var glass = new SKPaint { Color = new SKColor(0x10, 0x10, 0x18), IsAntialias = true })
            using (var rim = new SKPaint { Color = new SKColor(0x20, 0xC0, 0xE0), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 4 })
            {
                var lensL = new SKRect(cx - 110, cy - 90, cx - 10, cy - 30);
                var lensR = new SKRect(cx + 10, cy - 90, cx + 110, cy - 30);
                canvas.DrawRoundRect(lensL, 14, 14, glass);
                canvas.DrawRoundRect(lensR, 14, 14, glass);
                canvas.DrawRoundRect(lensL, 14, 14, rim);
                canvas.DrawRoundRect(lensR, 14, 14, rim);
                canvas.DrawLine(cx - 10, cy - 75, cx + 10, cy - 75, rim);

                // Moving reflection streak across the lenses.
                using var refl = new SKPaint { Color = new SKColor(0xFF, 0xFF, 0xFF, 0x40), StrokeWidth = 6 };
                float rx = cx - 110 + (frame * 6 % 220);
                canvas.DrawLine(rx, cy - 90, rx - 20, cy - 30, refl);
            }

            DrawMouth(canvas, cx, cy + 70, _currentViseme);
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
        }
    }
}
