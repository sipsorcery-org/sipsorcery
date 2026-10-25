//-----------------------------------------------------------------------------
// Filename: TestCardPattern.cs
//
// Description: A recognisable I420 test card for the generated video the
// LibraryVideoPublisher sends ("webrtc loopback" / "webrtc whip"), for when a
// person is watching the result. Unlike the benchmark texture it is easy to judge
// by eye:
//  - 75% colour bars across the top two thirds, so wrong colours or swapped
//    chroma planes are obvious;
//  - a white block sweeping back and forth along the bottom band, so motion,
//    stutter and frozen video are obvious;
//  - a frame counter in the middle of the bars, so dropped or repeated frames
//    can be read off directly.
//
// The static bars are drawn once; each frame copies them and draws the block and
// counter on top, which is cheap next to an encode.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 24 Oct 2026	Aaron Clauson	Created, Wexford, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

namespace SIPSorcery.Cli.Commands;

public sealed class TestCardPattern
{
    // BT.601 limited range Y, U, V for 75% colour bars, left to right.
    private static readonly (byte Y, byte U, byte V)[] Bars =
    {
        (180, 128, 128), // white
        (162, 44, 142),  // yellow
        (131, 156, 44),  // cyan
        (112, 72, 58),   // green
        (84, 184, 198),  // magenta
        (65, 100, 212),  // red
        (35, 212, 114),  // blue
    };

    private static readonly (byte Y, byte U, byte V) Black = (16, 128, 128);
    private static readonly (byte Y, byte U, byte V) White = (235, 128, 128);
    private static readonly (byte Y, byte U, byte V) Band = (40, 128, 128);

    // 5x7 bitmap digits, one byte per row, the low five bits are the pixels (bit 4 is the left column).
    private static readonly byte[][] Digits =
    {
        new byte[] { 0x0E, 0x11, 0x13, 0x15, 0x19, 0x11, 0x0E }, // 0
        new byte[] { 0x04, 0x0C, 0x04, 0x04, 0x04, 0x04, 0x0E }, // 1
        new byte[] { 0x0E, 0x11, 0x01, 0x02, 0x04, 0x08, 0x1F }, // 2
        new byte[] { 0x1F, 0x02, 0x04, 0x02, 0x01, 0x11, 0x0E }, // 3
        new byte[] { 0x02, 0x06, 0x0A, 0x12, 0x1F, 0x02, 0x02 }, // 4
        new byte[] { 0x1F, 0x10, 0x1E, 0x01, 0x01, 0x11, 0x0E }, // 5
        new byte[] { 0x06, 0x08, 0x10, 0x1E, 0x11, 0x11, 0x0E }, // 6
        new byte[] { 0x1F, 0x01, 0x02, 0x04, 0x08, 0x08, 0x08 }, // 7
        new byte[] { 0x0E, 0x11, 0x11, 0x0E, 0x11, 0x11, 0x0E }, // 8
        new byte[] { 0x0E, 0x11, 0x11, 0x0F, 0x01, 0x02, 0x0C }, // 9
    };

    private const int COUNTER_DIGITS = 6;
    private const int GLYPH_WIDTH = 5;
    private const int GLYPH_HEIGHT = 7;
    private const int SWEEP_SECONDS = 2;   // Time for the block to cross the frame one way.

    private readonly int _width;
    private readonly int _height;
    private readonly int _fps;
    private readonly byte[] _background;

    /// <summary>
    /// Creates a test card for frames of the given size. The width and height must be even (I420).
    /// </summary>
    /// <param name="fps">The frame rate, which sets how far the block moves per frame.</param>
    public TestCardPattern(int width, int height, int fps)
    {
        _width = width;
        _height = height;
        _fps = Math.Max(1, fps);
        _background = new byte[I420Size];

        int barsHeight = height * 2 / 3;
        for (int i = 0; i < Bars.Length; i++)
        {
            int x0 = i * width / Bars.Length;
            int x1 = (i + 1) * width / Bars.Length;
            FillRect(_background, x0, 0, x1 - x0, barsHeight, Bars[i]);
        }
        FillRect(_background, 0, barsHeight, width, height - barsHeight, Band);
    }

    public int I420Size => _width * _height + 2 * ((_width / 2) * (_height / 2));

    /// <summary>
    /// Fills <paramref name="buf"/> (at least <see cref="I420Size"/> bytes) with the test card for
    /// <paramref name="frameIndex"/>.
    /// </summary>
    public void Fill(byte[] buf, int frameIndex)
    {
        Buffer.BlockCopy(_background, 0, buf, 0, _background.Length);

        // Moving block: bounces across the bottom band, one crossing every SWEEP_SECONDS.
        int barsHeight = _height * 2 / 3;
        int bandHeight = _height - barsHeight;
        int block = Math.Max(2, bandHeight / 2) & ~1;
        int travel = Math.Max(1, _width - block);
        int period = 2 * SWEEP_SECONDS * _fps;
        int phase = frameIndex % period;
        int half = period / 2;
        int x = (phase < half ? phase : period - phase) * travel / half;
        FillRect(buf, x, barsHeight + (bandHeight - block) / 2, block, block, White);

        // Frame counter: white digits on a black box in the middle of the bars.
        int scale = Math.Max(1, _height / 60);
        int glyphAdvance = (GLYPH_WIDTH + 1) * scale;
        int textWidth = COUNTER_DIGITS * glyphAdvance - scale;
        int textHeight = GLYPH_HEIGHT * scale;
        int pad = 2 * scale;
        int boxX = (_width - textWidth) / 2 - pad;
        int boxY = (barsHeight - textHeight) / 2 - pad;
        FillRect(buf, boxX, boxY, textWidth + 2 * pad, textHeight + 2 * pad, Black);

        int value = frameIndex % 1_000_000;
        for (int d = COUNTER_DIGITS - 1; d >= 0; d--)
        {
            DrawDigit(buf, value % 10, boxX + pad + d * glyphAdvance, boxY + pad, scale);
            value /= 10;
        }
    }

    private void DrawDigit(byte[] buf, int digit, int x, int y, int scale)
    {
        byte[] rows = Digits[digit];
        for (int row = 0; row < GLYPH_HEIGHT; row++)
        {
            for (int col = 0; col < GLYPH_WIDTH; col++)
            {
                if ((rows[row] & (0x10 >> col)) != 0)
                {
                    FillRect(buf, x + col * scale, y + row * scale, scale, scale, White);
                }
            }
        }
    }

    /// <summary>
    /// Fills a rectangle in an I420 frame, clipped to the frame. The chroma for every 2x2 block the
    /// rectangle touches is set too, so small shapes keep their colour.
    /// </summary>
    private void FillRect(byte[] buf, int x, int y, int w, int h, (byte Y, byte U, byte V) colour)
    {
        int x0 = Math.Max(0, x), y0 = Math.Max(0, y);
        int x1 = Math.Min(_width, x + w), y1 = Math.Min(_height, y + h);
        if (x0 >= x1 || y0 >= y1)
        {
            return;
        }

        for (int row = y0; row < y1; row++)
        {
            buf.AsSpan(row * _width + x0, x1 - x0).Fill(colour.Y);
        }

        int chromaWidth = _width / 2;
        int uBase = _width * _height;
        int vBase = uBase + chromaWidth * (_height / 2);
        int cx0 = x0 / 2, cx1 = Math.Min(chromaWidth, (x1 + 1) / 2);
        int cy0 = y0 / 2, cy1 = Math.Min(_height / 2, (y1 + 1) / 2);
        for (int row = cy0; row < cy1; row++)
        {
            buf.AsSpan(uBase + row * chromaWidth + cx0, cx1 - cx0).Fill(colour.U);
            buf.AsSpan(vBase + row * chromaWidth + cx0, cx1 - cx0).Fill(colour.V);
        }
    }
}
