using SIPSorceryMedia.Abstractions;

namespace VideoSpike;

/// <summary>
/// A pre-encoded clip held in memory as one byte array per frame: VP8 from an IVF file, or H.264
/// from an Annex B file with access unit delimiters (which are stripped, they mean nothing on RTP).
/// </summary>
public sealed class Clip
{
    public required string Name { get; init; }
    public required VideoCodecsEnum Codec { get; init; }
    public required IReadOnlyList<byte[]> Frames { get; init; }

    public static Clip Load(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".ivf" => new Clip { Name = name, Codec = VideoCodecsEnum.VP8, Frames = ReadIvf(path) },
            ".h264" => new Clip { Name = name, Codec = VideoCodecsEnum.H264, Frames = ReadAnnexB(path) },
            _ => throw new NotSupportedException($"Unknown clip type {path}."),
        };
    }

    private static List<byte[]> ReadIvf(string path)
    {
        var data = File.ReadAllBytes(path);
        if (data.Length < 32 || data[0] != 'D' || data[1] != 'K' || data[2] != 'I' || data[3] != 'F')
        {
            throw new InvalidDataException($"{path} is not an IVF file.");
        }

        int pos = BitConverter.ToUInt16(data, 6);
        var frames = new List<byte[]>();
        while (pos + 12 <= data.Length)
        {
            int size = (int)BitConverter.ToUInt32(data, pos);
            pos += 12;
            frames.Add(data.AsSpan(pos, size).ToArray());
            pos += size;
        }
        return frames;
    }

    private static List<byte[]> ReadAnnexB(string path)
    {
        var data = File.ReadAllBytes(path);
        var nals = new List<(int start, int end, int type)>();   // start includes the start code

        int i = 0, nalStart = -1, nalType = 0;
        while (i + 3 <= data.Length)
        {
            int sc = 0;
            if (data[i] == 0 && data[i + 1] == 0 && data[i + 2] == 1) sc = 3;
            else if (i + 4 <= data.Length && data[i] == 0 && data[i + 1] == 0 && data[i + 2] == 0 && data[i + 3] == 1) sc = 4;

            if (sc > 0)
            {
                if (nalStart >= 0) nals.Add((nalStart, i, nalType));
                nalStart = i;
                nalType = i + sc < data.Length ? data[i + sc] & 0x1f : 0;
                i += sc;
            }
            else
            {
                i++;
            }
        }
        if (nalStart >= 0) nals.Add((nalStart, data.Length, nalType));

        const int AUD = 9;
        var frames = new List<byte[]>();
        var current = new MemoryStream();
        foreach (var (start, end, type) in nals)
        {
            if (type == AUD)
            {
                if (current.Length > 0) frames.Add(current.ToArray());
                current.SetLength(0);
                continue;
            }
            current.Write(data, start, end - start);
        }
        if (current.Length > 0) frames.Add(current.ToArray());
        return frames;
    }
}
