using SIPSorceryMedia.Abstractions;

namespace VideoSpike;

public enum PipelinePath { Relay, Buffer, Wpf }
public enum CameraKind { Off, Fake, Real }
public enum CameraMode { Shared, PerCall }

/// <summary>
/// One run of the spike: which pipeline, how many incoming streams, the codec and resolution the
/// far end sends, and what the near end does with its camera. Passed on the command line as
/// --key value pairs, so the bench and the near-end apps share one parser.
/// </summary>
public sealed record RunConfig
{
    public PipelinePath Path { get; init; } = PipelinePath.Relay;
    public int Streams { get; init; } = 4;
    public VideoCodecsEnum Codec { get; init; } = VideoCodecsEnum.VP8;
    public string Resolution { get; init; } = "720p30";
    public CameraKind Camera { get; init; } = CameraKind.Off;
    public CameraMode CameraMode { get; init; } = CameraMode.Shared;
    public bool HwDecode { get; init; } = true;
    public string FarEnd { get; init; } = "127.0.0.1:5090";
    public int Width { get; init; } = 1600;
    public int Height { get; init; } = 900;
    public int CameraBitrateKbps { get; init; } = 1500;

    public string CodecName => Codec == VideoCodecsEnum.H264 ? "h264" : "vp8";
    public string ClipName => $"{CodecName}-{Resolution}";
    public string CallUri => $"sip:{ClipName}@{FarEnd}";

    public string Label =>
        $"{Path.ToString().ToLowerInvariant()}-{Streams}x{ClipName}-cam{Camera.ToString().ToLowerInvariant()}" +
        (Camera == CameraKind.Off ? "" : "-" + CameraMode.ToString().ToLowerInvariant()) +
        (Path != PipelinePath.Relay && !HwDecode ? "-swdec" : "");

    public static RunConfig Parse(string[] args)
    {
        var cfg = new RunConfig();
        for (int i = 0; i + 1 < args.Length; i += 2)
        {
            var key = args[i].TrimStart('-').ToLowerInvariant();
            var val = args[i + 1];
            cfg = key switch
            {
                "path" => cfg with { Path = Enum.Parse<PipelinePath>(val, true) },
                "streams" => cfg with { Streams = int.Parse(val) },
                "codec" => cfg with { Codec = val.ToLowerInvariant() == "h264" ? VideoCodecsEnum.H264 : VideoCodecsEnum.VP8 },
                "res" => cfg with { Resolution = val },
                "camera" => cfg with { Camera = Enum.Parse<CameraKind>(val, true) },
                "cameramode" => cfg with { CameraMode = Enum.Parse<CameraMode>(val.Replace("-", ""), true) },
                "hwdecode" => cfg with { HwDecode = val is "on" or "true" or "1" },
                "farend" => cfg with { FarEnd = val },
                "width" => cfg with { Width = int.Parse(val) },
                "height" => cfg with { Height = int.Parse(val) },
                "camerakbps" => cfg with { CameraBitrateKbps = int.Parse(val) },
                _ => cfg,
            };
        }
        return cfg;
    }

    public string[] ToArgs() =>
    [
        "--path", Path.ToString(), "--streams", Streams.ToString(), "--codec", CodecName, "--res", Resolution,
        "--camera", Camera.ToString(), "--cameramode", CameraMode.ToString(), "--hwdecode", HwDecode ? "on" : "off",
        "--farend", FarEnd, "--width", Width.ToString(), "--height", Height.ToString(),
        "--camerakbps", CameraBitrateKbps.ToString(),
    ];
}
