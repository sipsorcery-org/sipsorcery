//-----------------------------------------------------------------------------
// Filename: MatroskaWriter.cs
//
// Description: A minimal live Matroska (MKV) muxer: writes an EBML header, a
// Segment and Tracks, then Clusters of SimpleBlocks as frames arrive. The Segment
// and Clusters use the "unknown size" encoding, so nothing is ever seeked back to
// and patched - the output can be a pipe (stdout into mpv/ffplay) as well as a
// file. No cues or seek head are written; players read it front to back.
//
// Only what the route Matroska sink needs: up to one video and one audio track,
// SimpleBlocks without lacing, millisecond timestamps. The caller supplies each
// frame already in the track's Matroska codec format (e.g. length-prefixed H264).
//
// Element IDs and layout: https://www.matroska.org/technical/elements.html
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 25 Oct 2026	Aaron Clauson	Created, Wexford, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace SIPSorcery.Cli.Commands.Route;

/// <summary>A track to declare in the Matroska header.</summary>
/// <param name="Number">The track number blocks refer to (1-127).</param>
/// <param name="IsVideo">True for a video track, false for audio.</param>
/// <param name="CodecId">The Matroska codec ID, e.g. V_MPEG4/ISO/AVC, V_VP8, A_OPUS, A_MS/ACM.</param>
/// <param name="CodecPrivate">Codec initialisation data (avcC, OpusHead, WAVEFORMATEX), if the codec has any.</param>
/// <param name="Width">Video only: the pixel width, if known.</param>
/// <param name="Height">Video only: the pixel height, if known.</param>
/// <param name="SamplingFrequency">Audio only: the sampling rate in Hz.</param>
/// <param name="Channels">Audio only: the channel count.</param>
public sealed record MatroskaTrack(
    int Number,
    bool IsVideo,
    string CodecId,
    byte[]? CodecPrivate = null,
    int? Width = null,
    int? Height = null,
    double? SamplingFrequency = null,
    int? Channels = null);

public sealed class MatroskaWriter
{
    // EBML / Matroska element IDs (written as-is, the marker bits are part of the ID).
    private const uint EBML = 0x1A45DFA3;
    private const uint EBMLVersion = 0x4286;
    private const uint EBMLReadVersion = 0x42F7;
    private const uint EBMLMaxIDLength = 0x42F2;
    private const uint EBMLMaxSizeLength = 0x42F3;
    private const uint DocType = 0x4282;
    private const uint DocTypeVersion = 0x4287;
    private const uint DocTypeReadVersion = 0x4285;
    private const uint Segment = 0x18538067;
    private const uint Info = 0x1549A966;
    private const uint TimestampScale = 0x2AD7B1;
    private const uint MuxingApp = 0x4D80;
    private const uint WritingApp = 0x5741;
    private const uint Tracks = 0x1654AE6B;
    private const uint TrackEntry = 0xAE;
    private const uint TrackNumber = 0xD7;
    private const uint TrackUID = 0x73C5;
    private const uint TrackType = 0x83;
    private const uint FlagLacing = 0x9C;
    private const uint CodecID = 0x86;
    private const uint CodecPrivateId = 0x63A2;
    private const uint Video = 0xE0;
    private const uint PixelWidth = 0xB0;
    private const uint PixelHeight = 0xBA;
    private const uint Audio = 0xE1;
    private const uint SamplingFrequencyId = 0xB5;
    private const uint ChannelsId = 0x9F;
    private const uint Cluster = 0x1F43B675;
    private const uint ClusterTimestamp = 0xE7;
    private const uint SimpleBlock = 0xA3;

    private const int TRACK_TYPE_VIDEO = 1;
    private const int TRACK_TYPE_AUDIO = 2;

    // A Cluster is started on a video key frame once the current one is this old (so a player joining
    // mid-stream finds key frames at cluster starts), and in any case once it is this old.
    private const long KEYFRAME_CLUSTER_MIN_MS = 1000;
    private const long MAX_CLUSTER_MS = 5000;

    // The "unknown size" EBML size: an 8 byte VINT with every value bit set.
    private static readonly byte[] UnknownSize = { 0x01, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };

    private readonly Stream _output;
    private readonly HashSet<int> _videoTracks = new();
    private bool _headerWritten;
    private long _clusterTimestamp = -1;

    /// <summary>Total bytes written to the output.</summary>
    public long BytesWritten { get; private set; }

    public MatroskaWriter(Stream output)
    {
        _output = output;
    }

    /// <summary>
    /// Writes the EBML header, opens the Segment and declares the tracks. Must be called once, before
    /// any frame. Timestamps are in milliseconds (TimestampScale 1,000,000ns).
    /// </summary>
    public void WriteHeader(IReadOnlyList<MatroskaTrack> tracks)
    {
        if (_headerWritten)
        {
            throw new InvalidOperationException("The Matroska header has already been written.");
        }

        var buf = new MemoryStream();

        WriteMaster(buf, EBML, body =>
        {
            WriteUInt(body, EBMLVersion, 1);
            WriteUInt(body, EBMLReadVersion, 1);
            WriteUInt(body, EBMLMaxIDLength, 4);
            WriteUInt(body, EBMLMaxSizeLength, 8);
            WriteString(body, DocType, "matroska");
            WriteUInt(body, DocTypeVersion, 4);
            WriteUInt(body, DocTypeReadVersion, 2);
        });

        WriteId(buf, Segment);
        buf.Write(UnknownSize);

        WriteMaster(buf, Info, body =>
        {
            WriteUInt(body, TimestampScale, 1_000_000);
            WriteString(body, MuxingApp, "SIPSorcery.Cli");
            WriteString(body, WritingApp, "sipsorcery route");
        });

        WriteMaster(buf, Tracks, body =>
        {
            foreach (var track in tracks)
            {
                if (track.IsVideo)
                {
                    _videoTracks.Add(track.Number);
                }

                WriteMaster(body, TrackEntry, entry =>
                {
                    WriteUInt(entry, TrackNumber, (ulong)track.Number);
                    WriteUInt(entry, TrackUID, (ulong)track.Number);
                    WriteUInt(entry, TrackType, (ulong)(track.IsVideo ? TRACK_TYPE_VIDEO : TRACK_TYPE_AUDIO));
                    WriteUInt(entry, FlagLacing, 0);
                    WriteString(entry, CodecID, track.CodecId);
                    if (track.CodecPrivate != null)
                    {
                        WriteBinary(entry, CodecPrivateId, track.CodecPrivate);
                    }

                    if (track.IsVideo && track.Width != null && track.Height != null)
                    {
                        WriteMaster(entry, Video, video =>
                        {
                            WriteUInt(video, PixelWidth, (ulong)track.Width.Value);
                            WriteUInt(video, PixelHeight, (ulong)track.Height.Value);
                        });
                    }
                    else if (!track.IsVideo)
                    {
                        WriteMaster(entry, Audio, audio =>
                        {
                            WriteFloat(audio, SamplingFrequencyId, track.SamplingFrequency ?? 48000);
                            WriteUInt(audio, ChannelsId, (ulong)(track.Channels ?? 1));
                        });
                    }
                });
            }
        });

        Emit(buf);
        _headerWritten = true;
    }

    /// <summary>
    /// Writes one frame as a SimpleBlock, opening a new Cluster when needed. Timestamps should not go
    /// backwards within a track; across tracks small reorderings are fine.
    /// </summary>
    public void WriteFrame(int trackNumber, long timestampMs, bool keyFrame, ReadOnlySpan<byte> data)
    {
        if (!_headerWritten)
        {
            throw new InvalidOperationException("WriteHeader must be called before WriteFrame.");
        }

        var buf = new MemoryStream(data.Length + 32);

        long relative = timestampMs - _clusterTimestamp;
        bool newCluster = _clusterTimestamp < 0
            || relative < short.MinValue
            || relative > short.MaxValue
            || relative >= MAX_CLUSTER_MS
            || (keyFrame && _videoTracks.Contains(trackNumber) && relative >= KEYFRAME_CLUSTER_MIN_MS);

        if (newCluster)
        {
            _clusterTimestamp = Math.Max(0, timestampMs);
            relative = timestampMs - _clusterTimestamp;

            WriteId(buf, Cluster);
            buf.Write(UnknownSize);
            WriteUInt(buf, ClusterTimestamp, (ulong)_clusterTimestamp);
        }

        // SimpleBlock: track number (VINT), signed 16 bit timestamp relative to the Cluster, flags.
        WriteId(buf, SimpleBlock);
        WriteSize(buf, 1 + 2 + 1 + data.Length);
        buf.WriteByte((byte)(0x80 | trackNumber));
        Span<byte> rel = stackalloc byte[2];
        BinaryPrimitives.WriteInt16BigEndian(rel, (short)relative);
        buf.Write(rel);
        buf.WriteByte((byte)(keyFrame ? 0x80 : 0x00));
        buf.Write(data);

        Emit(buf);
    }

    private void Emit(MemoryStream buf)
    {
        _output.Write(buf.GetBuffer(), 0, (int)buf.Length);
        _output.Flush();
        BytesWritten += buf.Length;
    }

    private static void WriteMaster(Stream s, uint id, Action<Stream> writeBody)
    {
        var body = new MemoryStream();
        writeBody(body);
        WriteId(s, id);
        WriteSize(s, body.Length);
        body.Position = 0;
        body.CopyTo(s);
    }

    private static void WriteUInt(Stream s, uint id, ulong value)
    {
        int length = 1;
        while (length < 8 && value >> (8 * length) != 0)
        {
            length++;
        }

        WriteId(s, id);
        WriteSize(s, length);
        for (int i = length - 1; i >= 0; i--)
        {
            s.WriteByte((byte)(value >> (8 * i)));
        }
    }

    private static void WriteFloat(Stream s, uint id, double value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteDoubleBigEndian(bytes, value);
        WriteId(s, id);
        WriteSize(s, 8);
        s.Write(bytes);
    }

    private static void WriteString(Stream s, uint id, string value) => WriteBinary(s, id, Encoding.ASCII.GetBytes(value));

    private static void WriteBinary(Stream s, uint id, byte[] value)
    {
        WriteId(s, id);
        WriteSize(s, value.Length);
        s.Write(value);
    }

    /// <summary>Writes an element ID: its bytes as-is, big-endian, without leading zero bytes.</summary>
    private static void WriteId(Stream s, uint id)
    {
        if (id > 0xFFFFFF) { s.WriteByte((byte)(id >> 24)); }
        if (id > 0xFFFF) { s.WriteByte((byte)(id >> 16)); }
        if (id > 0xFF) { s.WriteByte((byte)(id >> 8)); }
        s.WriteByte((byte)id);
    }

    /// <summary>Writes an element data size as the shortest EBML variable length integer that holds it.</summary>
    private static void WriteSize(Stream s, long size)
    {
        int length = 1;
        // A length-n VINT holds up to 2^(7n) - 2 (all-ones is reserved for "unknown").
        while (length < 8 && size >= (1L << (7 * length)) - 1)
        {
            length++;
        }

        ulong value = (ulong)size | (1UL << (7 * length));
        for (int i = length - 1; i >= 0; i--)
        {
            s.WriteByte((byte)(value >> (8 * i)));
        }
    }
}
