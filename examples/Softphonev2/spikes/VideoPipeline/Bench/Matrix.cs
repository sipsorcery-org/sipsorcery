using SIPSorceryMedia.Abstractions;

namespace VideoSpike.Bench;

public static class Matrix
{
    /// <summary>Real needs the camera free; Fake is the synthetic 720p30 source.</summary>
    public static CameraKind Camera { get; set; } = CameraKind.Fake;

    private static readonly PipelinePath[] Paths = [PipelinePath.Relay, PipelinePath.Buffer, PipelinePath.Wpf];
    private static readonly VideoCodecsEnum[] Codecs = [VideoCodecsEnum.VP8, VideoCodecsEnum.H264];

    public static List<RunConfig> Smoke() =>
        Paths.Select(p => new RunConfig { Path = p, Streams = 2, Codec = VideoCodecsEnum.H264, Camera = Camera }).ToList();

    /// <summary>
    /// Receive-only scaling (1, 4, 8 streams), then the camera added (shared encoder, 4 and 8
    /// calls), one encoder per call at 8, 1080p at 4, and C# software decode of H.264 at 8 to show
    /// what hardware decode is worth.
    /// </summary>
    public static List<RunConfig> Default()
    {
        var list = new List<RunConfig>();
        foreach (var codec in Codecs)
        foreach (var path in Paths)
        {
            foreach (var n in new[] { 1, 4, 8 })
                list.Add(new RunConfig { Path = path, Codec = codec, Streams = n });
            foreach (var n in new[] { 4, 8 })
                list.Add(new RunConfig { Path = path, Codec = codec, Streams = n, Camera = Camera, CameraMode = CameraMode.Shared });
            list.Add(new RunConfig { Path = path, Codec = codec, Streams = 8, Camera = Camera, CameraMode = CameraMode.PerCall });
            list.Add(new RunConfig { Path = path, Codec = codec, Streams = 4, Resolution = "1080p30" });
            if (path != PipelinePath.Relay && codec == VideoCodecsEnum.H264)
                list.Add(new RunConfig { Path = path, Codec = codec, Streams = 8, HwDecode = false });
        }
        return list;
    }

    /// <summary>Only the runs that use the camera, to repeat them with a different camera source.</summary>
    public static List<RunConfig> CameraRuns() => Default().Where(c => c.Camera != CameraKind.Off).ToList();

    /// <summary>The heavy cases again, for a run with the near end pinned to 4 logical CPUs.</summary>
    public static List<RunConfig> Cores4()
    {
        var list = new List<RunConfig>();
        foreach (var codec in Codecs)
        foreach (var path in Paths)
        {
            list.Add(new RunConfig { Path = path, Codec = codec, Streams = 4, Camera = Camera });
            list.Add(new RunConfig { Path = path, Codec = codec, Streams = 8, Camera = Camera });
        }
        return list;
    }
}
