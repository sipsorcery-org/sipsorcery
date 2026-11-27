//-----------------------------------------------------------------------------
// Filename: TestFrames.cs
//
// Description: Deterministic camera-like I420 frames for the H.264 benchmarks: a
// textured background panning at a sub-pixel rate, a textured block crossing it
// on a path of its own, and a little sensor noise. The motion is not a whole
// number of pixels a frame, so motion estimation, sub-pixel interpolation and
// the residual coding all get work, as with a camera.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 27 Nov 2026  Claude          Created (Phase 5).
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

namespace H264Benchmarks;

internal static class TestFrames
{
    // Background and foreground motion, in luma pixels a frame.
    private const double BG_DX = 1.37;
    private const double BG_DY = 0.61;
    private const double FG_DX = -3.3;
    private const double FG_DY = 1.7;

    /// <summary>The frames to encode, played forwards then backwards so the motion never jumps.</summary>
    public static int PingPong(long index, int count)
    {
        int period = 2 * (count - 1);
        int i = (int)(index % period);
        return i < count ? i : period - i;
    }

    /// <summary>Generates <paramref name="count"/> I420 frames, each the Y, U and V planes packed.</summary>
    public static byte[][] Generate(int width, int height, int count)
    {
        int cw = width / 2;
        int ch = height / 2;
        int margin = (int)Math.Ceiling(Math.Max(BG_DX, BG_DY) * count) + 2;

        (int, float)[] lumaOctaves = [(96, 70f), (24, 35f), (6, 14f), (2, 5f)];
        (int, float)[] chromaOctaves = [(64, 30f), (16, 10f)];
        int fgw = Math.Max(16, width / 4);
        int fgh = Math.Max(16, height / 4);

        // Each texture is one sample wider and taller than is read, for the bilinear filter.
        int bgStride = width + margin + 1;
        int bgcStride = cw + margin + 1;
        float[] bgY = Texture(bgStride, height + margin + 1, 1, lumaOctaves);
        float[] bgU = Texture(bgcStride, ch + margin + 1, 2, chromaOctaves);
        float[] bgV = Texture(bgcStride, ch + margin + 1, 3, chromaOctaves);
        float[] fgY = Texture(fgw + 1, fgh + 1, 4, lumaOctaves);
        float[] fgU = Texture(fgw / 2 + 1, fgh / 2 + 1, 5, chromaOctaves);
        float[] fgV = Texture(fgw / 2 + 1, fgh / 2 + 1, 6, chromaOctaves);

        var frames = new byte[count][];
        for (int f = 0; f < count; f++)
        {
            var frame = new byte[width * height + 2 * cw * ch];
            double bx = BG_DX * f;
            double by = BG_DY * f;
            // The block wraps around inside the frame so it stays in view.
            double fx = Wrap(width * 0.1 + FG_DX * f, width - fgw);
            double fy = Wrap(height * 0.2 + FG_DY * f, height - fgh);

            Render(frame.AsSpan(0, width * height), width, height, bgY, bgStride, bx, by,
                fgY, fgw + 1, fgw, fgh, fx, fy, 128f, 16, 235, (uint)f * 3 + 0, 2f);
            Render(frame.AsSpan(width * height, cw * ch), cw, ch, bgU, bgcStride, bx / 2, by / 2,
                fgU, fgw / 2 + 1, fgw / 2, fgh / 2, fx / 2, fy / 2, 112f, 16, 240, (uint)f * 3 + 1, 1f);
            Render(frame.AsSpan(width * height + cw * ch, cw * ch), cw, ch, bgV, bgcStride, bx / 2, by / 2,
                fgV, fgw / 2 + 1, fgw / 2, fgh / 2, fx / 2, fy / 2, 144f, 16, 240, (uint)f * 3 + 2, 1f);
            frames[f] = frame;
        }
        return frames;
    }

    private static double Wrap(double value, int range)
    {
        double r = value % range;
        return r < 0 ? r + range : r;
    }

    private static void Render(Span<byte> plane, int width, int height, float[] bg, int bgStride, double bx, double by,
        float[] fg, int fgStride, int fgw, int fgh, double fx, double fy, float mid, int min, int max, uint noiseSeed, float noise)
    {
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                double u = x - fx;
                double v = y - fy;
                float value = u >= 0 && v >= 0 && u < fgw && v < fgh
                    ? Bilinear(fg, fgStride, u, v)
                    : Bilinear(bg, bgStride, x + bx, y + by);
                value += mid + noise * Hash(x, y, noiseSeed);
                plane[y * width + x] = (byte)Math.Clamp((int)MathF.Round(value), min, max);
            }
        }
    }

    private static float Bilinear(float[] t, int stride, double x, double y)
    {
        int x0 = (int)x;
        int y0 = (int)y;
        float ax = (float)(x - x0);
        float ay = (float)(y - y0);
        int i = y0 * stride + x0;
        float top = t[i] + (t[i + 1] - t[i]) * ax;
        float bottom = t[i + stride] + (t[i + stride + 1] - t[i + stride]) * ax;
        return top + (bottom - top) * ay;
    }

    /// <summary>Value noise: smoothly interpolated random lattices, one an octave, summed.</summary>
    private static float[] Texture(int width, int height, uint seed, (int Cell, float Amplitude)[] octaves)
    {
        var t = new float[width * height];
        foreach (var (cell, amplitude) in octaves)
        {
            uint octaveSeed = seed * 7919u + (uint)cell;
            for (int y = 0; y < height; y++)
            {
                int cy = y / cell;
                float fy = Smooth((y % cell) / (float)cell);
                for (int x = 0; x < width; x++)
                {
                    int cx = x / cell;
                    float fx = Smooth((x % cell) / (float)cell);
                    float top = Lerp(Hash(cx, cy, octaveSeed), Hash(cx + 1, cy, octaveSeed), fx);
                    float bottom = Lerp(Hash(cx, cy + 1, octaveSeed), Hash(cx + 1, cy + 1, octaveSeed), fx);
                    t[y * width + x] += amplitude * Lerp(top, bottom, fy);
                }
            }
        }
        return t;
    }

    private static float Smooth(float t) => t * t * (3 - 2 * t);

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    /// <summary>A hash of the position, from -1 to 1.</summary>
    private static float Hash(int x, int y, uint seed)
    {
        uint h = (uint)x * 374761393u + (uint)y * 668265263u + seed * 2246822519u;
        h = (h ^ (h >> 13)) * 1274126177u;
        h ^= h >> 16;
        return (h & 0xffff) / 32767.5f - 1f;
    }
}
