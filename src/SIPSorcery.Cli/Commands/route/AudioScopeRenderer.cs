//-----------------------------------------------------------------------------
// Filename: AudioScopeRenderer.cs
//
// Description: Portable, dependency-free renderer for the Hilbert audio scope. It
// draws the analytic-signal trace produced by AudioScope directly into a BGR byte
// buffer using a tiny software rasteriser - no GPU, no native libraries. For ~256
// line segments a frame a plain CPU rasteriser is more than fast enough.
//
// This is the examples/Softphone/AudioScope/AudioScopeRenderer.cs rasteriser,
// brought into the CLI for the "route --scope-mode hilbert" video, with the frame
// size made configurable (the example is fixed at 640x480) so it follows
// --scope-size.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 08 Jun 2026	Aaron Clauson	Created, via CLaude Opus 4.8, Wexford, Ireland.
// 24 Oct 2026	Aaron Clauson	Copied into the CLI from the Softphone example, configurable size.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Numerics;

namespace SIPSorcery.Cli.Commands.Route;

/// <summary>
/// Renders the audio scope trace to a packed BGR byte buffer with a minimal software rasteriser.
/// </summary>
/// <remarks>
/// Not thread safe, and <see cref="ProcessAudioSample"/> returns the instance's own pixel buffer
/// rather than a copy, which the next call overwrites.
/// </remarks>
public sealed class AudioScopeRenderer
{
    public const int PIXEL_STRIDE = 3;

    private const int Stride = AudioScope.DISPLAY_ARRAY_STRIDE;
    private const float LineRadius = 1.6f;   // Half line thickness, in pixels (also the round-cap radius).
    private const float DecayKeep = 0.82f;   // Fraction of the previous frame kept each frame (trail length).
    private const float BaseHue = 0.0f;
    private const float Desaturation = 0.1f;

    private readonly AudioScope _audioScope = new();
    private readonly byte[] _pixels;

    public int Width { get; }

    public int Height { get; }

    public AudioScopeRenderer(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "The scope width and height must be greater than zero.");
        }

        Width = width;
        Height = height;
        _pixels = new byte[width * height * PIXEL_STRIDE];
    }

    /// <summary>
    /// Processes a block of audio samples and returns the rendered scope frame as packed BGR bytes.
    /// </summary>
    /// <returns>
    /// This instance's own pixel buffer, which the next call overwrites.
    /// </returns>
    public byte[] ProcessAudioSample(Complex[] samples)
    {
        _audioScope.ProcessSample(samples);
        float[]? data = _audioScope.GetSample();

        Fade();
        DrawTrace(data);

        return _pixels;
    }

    /// <summary>
    /// Dims the whole frame towards black so previous frames fade out, giving the persistence
    /// "trail" effect.
    /// </summary>
    private void Fade()
    {
        for (int i = 0; i < _pixels.Length; i++)
        {
            _pixels[i] = (byte)(_pixels[i] * DecayKeep);
        }
    }

    /// <summary>
    /// Draws the analytic-signal curve as a sequence of coloured segments. Each segment's colour
    /// comes from its vertex data (frequency -> hue, noise -> desaturation).
    /// </summary>
    private void DrawTrace(float[]? data)
    {
        if (data == null)
        {
            return;
        }

        int vertexCount = data.Length / Stride;
        if (vertexCount < 2)
        {
            return;
        }

        // The trace lives in the unit circle, so it is scaled by the shorter side and centred, which
        // keeps it round on a non-square frame.
        float radius = Math.Min(Width, Height) / 2.0f;
        float prevX = 0.0f, prevY = 0.0f;
        for (int i = 0; i < vertexCount; i++)
        {
            float x = Width / 2.0f + data[i * Stride] * radius;
            float y = Height / 2.0f - data[i * Stride + 1] * radius; // image space is top-down.

            if (i > 0)
            {
                (float r, float g, float b) = ColourFor(data, i - 1);
                DrawSegment(prevX, prevY, x, y, r, g, b);
            }

            prevX = x;
            prevY = y;
        }
    }

    /// <summary>
    /// Draws a thick, round-capped, anti-aliased line by stamping soft discs along it.
    /// </summary>
    private void DrawSegment(float x0, float y0, float x1, float y1, float r, float g, float b)
    {
        float dx = x1 - x0;
        float dy = y1 - y0;
        float length = MathF.Sqrt(dx * dx + dy * dy);
        int steps = Math.Max(1, (int)MathF.Ceiling(length));

        for (int s = 0; s <= steps; s++)
        {
            float t = (float)s / steps;
            Stamp(x0 + dx * t, y0 + dy * t, r, g, b);
        }
    }

    /// <summary>
    /// Alpha-blends a soft disc of the given colour into the frame, anti-aliased over ~1px at the
    /// edge.
    /// </summary>
    private void Stamp(float cx, float cy, float r, float g, float b)
    {
        int minX = Math.Max(0, (int)MathF.Floor(cx - LineRadius - 1.0f));
        int maxX = Math.Min(Width - 1, (int)MathF.Ceiling(cx + LineRadius + 1.0f));
        int minY = Math.Max(0, (int)MathF.Floor(cy - LineRadius - 1.0f));
        int maxY = Math.Min(Height - 1, (int)MathF.Ceiling(cy + LineRadius + 1.0f));

        byte rb = ToByte(r);
        byte gb = ToByte(g);
        byte bb = ToByte(b);

        for (int py = minY; py <= maxY; py++)
        {
            for (int px = minX; px <= maxX; px++)
            {
                float ddx = px - cx;
                float ddy = py - cy;
                float d = MathF.Sqrt(ddx * ddx + ddy * ddy);
                float coverage = Math.Clamp(LineRadius - d + 0.5f, 0.0f, 1.0f); // AA falloff over ~1px.

                if (coverage <= 0.0f)
                {
                    continue;
                }

                // Channels are stored blue-green-red (bgr24).
                int idx = (py * Width + px) * PIXEL_STRIDE;
                _pixels[idx] = Blend(_pixels[idx], bb, coverage);
                _pixels[idx + 1] = Blend(_pixels[idx + 1], gb, coverage);
                _pixels[idx + 2] = Blend(_pixels[idx + 2], rb, coverage);
            }
        }
    }

    private static byte Blend(byte dst, byte src, float coverage)
    {
        return (byte)(dst * (1.0f - coverage) + src * coverage + 0.5f);
    }

    /// <summary>
    /// Computes the colour for a vertex from its angular-velocity and noise channels: hue from
    /// log2(angular velocity), saturation reduced by noise.
    /// </summary>
    private static (float, float, float) ColourFor(float[] data, int index)
    {
        float angularVelocity = data[index * Stride + 2];
        float noise = data[index * Stride + 3];

        float phase = MathF.Log2(MathF.Max(angularVelocity, 1e-9f));
        float hue = BaseHue + phase;
        float saturation = MathF.Pow(2.0f, -Desaturation * noise);

        return Hsv2Rgb(hue, saturation, 1.0f);
    }

    // Matches the GLSL hsv2rgb used by the original fragment shader. Hue is in turns and wraps.
    private static (float, float, float) Hsv2Rgb(float h, float s, float v)
    {
        float Channel(float n)
        {
            float k = MathF.Abs(Frac(h + n) * 6.0f - 3.0f);
            return v * Lerp(1.0f, Math.Clamp(k - 1.0f, 0.0f, 1.0f), s);
        }

        return (Channel(1.0f), Channel(2.0f / 3.0f), Channel(1.0f / 3.0f));
    }

    private static float Frac(float x) => x - MathF.Floor(x);

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static byte ToByte(float c) => (byte)(Math.Clamp(c, 0.0f, 1.0f) * 255.0f + 0.5f);
}
