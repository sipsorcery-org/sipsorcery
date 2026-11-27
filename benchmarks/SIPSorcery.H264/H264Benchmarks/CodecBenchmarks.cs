//-----------------------------------------------------------------------------
// Filename: CodecBenchmarks.cs
//
// Description: Whole-frame encode and decode benchmarks for SIPSorcery.H264, at
// the frame sizes WebRTC calls use.
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

using BenchmarkDotNet.Attributes;
using SIPSorcery.H264;
using SIPSorceryMedia.Abstractions;

namespace H264Benchmarks;

/// <summary>
/// Encodes one frame an operation, in a stream with a key frame a second, as H264Codec sets the
/// encoder up.
/// </summary>
[MemoryDiagnoser]
public class EncodeBenchmarks
{
    public const int FRAMES = 32;

    [Params("640x480", "1280x720", "1920x1080")]
    public string Resolution { get; set; } = "640x480";

    private byte[][] _frames = null!;
    private BenchEncoder _encoder = null!;
    private long _index;

    [GlobalSetup]
    public void Setup()
    {
        var (width, height) = Resolutions.Parse(Resolution);
        _frames = TestFrames.Generate(width, height, FRAMES);
        _encoder = new BenchEncoder(width, height);
    }

    [GlobalCleanup]
    public void Cleanup() => _encoder.Dispose();

    [Benchmark]
    public int EncodeFrame() => _encoder.Encode(_frames[TestFrames.PingPong(_index++, FRAMES)]);
}

/// <summary>
/// Decodes one access unit an operation, round and round a clip of an IDR and P frames.
/// </summary>
[MemoryDiagnoser]
public class DecodeBenchmarks
{
    [Params("640x480", "1280x720", "1920x1080")]
    public string Resolution { get; set; } = "640x480";

    private List<byte[]> _clip = null!;
    private BenchDecoder _decoder = null!;
    private H264Codec _codec = null!;
    private int _index;

    [GlobalSetup]
    public void Setup()
    {
        var (width, height) = Resolutions.Parse(Resolution);
        _clip = Clips.Encode(width, height, TestFrames.Generate(width, height, EncodeBenchmarks.FRAMES));
        _decoder = new BenchDecoder();
        _codec = new H264Codec();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _decoder.Dispose();
        _codec.Dispose();
    }

    /// <summary>The decoder alone, to its I420 picture.</summary>
    [Benchmark]
    public unsafe int DecodeFrame()
    {
        byte[] au = _clip[_index];
        _index = (_index + 1) % _clip.Count;
        return _decoder.Decode(au, out int width, out _, out _) != null ? width : 0;
    }

    /// <summary>The receive path: H264Codec decoding to BGR, as H264NetVideoEndPoint does.</summary>
    [Benchmark]
    public int DecodeFrameToBgr()
    {
        byte[] au = _clip[_index];
        _index = (_index + 1) % _clip.Count;
        int width = 0;
        foreach (var image in _codec.DecodeVideoFaster(au, VideoPixelFormatsEnum.Bgr, VideoCodecsEnum.H264))
        {
            width = image.Width;
        }
        return width;
    }
}
