using System.Text.Json.Serialization;

namespace SIPSorcery.Gemini.Realtime.Models;

public class GeminiRealtimeInputConfig
{
    [JsonPropertyName("automaticActivityDetection")]
    public GeminiAutomaticActivityDetection? AutomaticActivityDetection { get; set; }

    [JsonPropertyName("activityHandling")]
    public GeminiActivityHandlingEnum? ActivityHandling { get; set; }

    [JsonPropertyName("turnCoverage")]
    public GeminiTurnCoverageEnum? TurnCoverage { get; set; }
}
