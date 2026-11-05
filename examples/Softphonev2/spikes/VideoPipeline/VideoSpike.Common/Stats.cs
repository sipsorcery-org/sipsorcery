using System.Text.Json;
using System.Text.Json.Serialization;

namespace VideoSpike;

/// <summary>What a near end measured about one tile during the measurement window.</summary>
public sealed class TileStats
{
    public int Tile { get; set; }
    public string Kind { get; set; } = "remote";        // "remote" or "self"
    public long FramesReceived { get; set; }            // complete encoded frames off the SIP leg
    public long FramesDecoded { get; set; }
    public long FramesPresented { get; set; }           // frames that reached the screen
    public long FramesDropped { get; set; }             // decoded or received but never shown
    public double PresentedFps { get; set; }
    public int Stalls { get; set; }                     // gaps of more than 100 ms between shown frames
    public double LongestGapMs { get; set; }
    public LatencySummary? Latency { get; set; }        // SIP-side receive -> on screen
    public LatencySummary? ToBrowserReceive { get; set; } // relay only: SIP receive -> browser receive
    public double? AvgDecodeMs { get; set; }
    public double? AvgJitterBufferMs { get; set; }
    public string? Decoder { get; set; }
    public bool? HardwareDecode { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

public sealed class CameraStats
{
    public string Source { get; set; } = "";
    public long FramesCaptured { get; set; }
    public double CaptureFps { get; set; }
    public int Encoders { get; set; }
    public long FramesEncoded { get; set; }             // summed over encoders
    public double EncodeFpsPerEncoder { get; set; }
    public long FramesSentToSip { get; set; }           // summed over SIP legs
    public double SentFpsPerLeg { get; set; }
    public double? AvgEncodeMs { get; set; }
    public double? SentKbpsPerEncoder { get; set; }
    public string? Encoder { get; set; }
    public bool? HardwareEncode { get; set; }
    public string? QualityLimitation { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

public sealed class NearEndStats
{
    public double WindowSeconds { get; set; }
    public double? ClockOffsetMs { get; set; }          // browser clock minus C# clock, from the ping calibration
    public double? ClockRttMs { get; set; }
    public string LatencyMethod { get; set; } = "";
    public List<TileStats> Tiles { get; set; } = new();
    public CameraStats? Camera { get; set; }
    public List<string> Notes { get; set; } = new();
}

public sealed class LatencySummary
{
    public int Samples { get; set; }
    public double P50 { get; set; }
    public double P95 { get; set; }
    public double Max { get; set; }

    public static LatencySummary? From(IEnumerable<double> values)
    {
        var v = values.Where(double.IsFinite).OrderBy(x => x).ToArray();
        if (v.Length == 0) return null;
        double P(double q) => v[Math.Min(v.Length - 1, (int)Math.Round(q * (v.Length - 1)))];
        return new LatencySummary { Samples = v.Length, P50 = Math.Round(P(0.5), 1), P95 = Math.Round(P(0.95), 1), Max = Math.Round(v[^1], 1) };
    }
}

/// <summary>
/// Tracks presented frames for one tile: count, gaps (stalls) and latency samples.
/// Not thread-safe; call from the thread that presents.
/// </summary>
public sealed class PresentTracker
{
    private double _lastPresentMs = double.NaN;
    private readonly List<double> _latency = new();
    public long Presented { get; private set; }
    public long Dropped { get; set; }
    public int Stalls { get; private set; }
    public double LongestGapMs { get; private set; }

    public void Reset()
    {
        _lastPresentMs = double.NaN; _latency.Clear();
        Presented = 0; Dropped = 0; Stalls = 0; LongestGapMs = 0;
    }

    public void OnPresented(double presentMs, double? latencyMs)
    {
        if (!double.IsNaN(_lastPresentMs))
        {
            var gap = presentMs - _lastPresentMs;
            if (gap > 100) Stalls++;
            if (gap > LongestGapMs) LongestGapMs = gap;
        }
        _lastPresentMs = presentMs;
        Presented++;
        if (latencyMs.HasValue) _latency.Add(latencyMs.Value);
    }

    public LatencySummary? Latency => LatencySummary.From(_latency);
}

public static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static readonly JsonSerializerOptions Indented = new(Options) { WriteIndented = true };
}
