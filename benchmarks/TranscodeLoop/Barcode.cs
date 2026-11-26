// The test card's frame number, read back from a decoded frame. The page (card.js) draws it as a
// strip of 32 black or white squares near the bottom of each frame: the frame number in 24 bits,
// most significant first, then a CRC-8 of those 3 bytes, so a frame too damaged to read is
// told apart from a wrong number. The layout here must match card.js's.

namespace SIPSorcery.TranscodeLoop;

internal static class Barcode
{
    public const int Cells = 32;

    /// <summary>The side of one square, in pixels, for a frame this wide.</summary>
    public static int CellSize(int width) => Math.Max(8, width / 40);

    /// <summary>
    /// The frame number in a tightly packed BGR frame, or -1 if the strip can't be read (the
    /// frame is damaged, or isn't the test card).
    /// </summary>
    public static int Read(byte[] bgr, int width, int height)
    {
        int cell = CellSize(width);
        int x0 = (width - Cells * cell) / 2, y0 = height - 2 * cell;
        if (x0 < 0 || y0 < 0 || bgr.Length < width * height * 3) return -1;

        uint bits = 0;
        int half = Math.Max(1, cell / 4);
        for (int c = 0; c < Cells; c++)
        {
            int cx = x0 + c * cell + cell / 2, cy = y0 + cell / 2;
            int sum = 0, n = 0;
            for (int y = cy - half; y < cy + half; y++)
            {
                int row = y * width * 3;
                for (int x = cx - half; x < cx + half; x++)
                {
                    int p = row + x * 3;
                    sum += (29 * bgr[p] + 150 * bgr[p + 1] + 77 * bgr[p + 2]) >> 8;
                    n++;
                }
            }
            bits = (bits << 1) | (sum / n >= 128 ? 1u : 0u);
        }

        int number = (int)(bits >> 8);
        return Crc8((byte)(number >> 16), (byte)(number >> 8), (byte)number) == (byte)bits ? number : -1;
    }

    /// <summary>
    /// CRC-8, polynomial 0x07, initial 0xFF (not 0: an all-black strip, where there's no strip at
    /// all, would otherwise read as frame 0 with a good CRC). card.js has the same.
    /// </summary>
    public static byte Crc8(params byte[] data)
    {
        byte crc = 0xFF;
        foreach (var b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++) crc = (byte)((crc & 0x80) != 0 ? (crc << 1) ^ 0x07 : crc << 1);
        }
        return crc;
    }
}
