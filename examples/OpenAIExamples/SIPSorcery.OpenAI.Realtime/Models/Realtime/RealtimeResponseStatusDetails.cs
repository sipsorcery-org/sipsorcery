using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

/// <summary>
/// Provides details about why a response was incomplete, cancelled, or failed.
/// </summary>
public class RealtimeResponseStatusDetails
{
    [JsonPropertyName("type")]
    public RealtimeStatusEnum? Type { get; set; }

    [JsonPropertyName("reason")]
    public RealtimeResponseStatusReasonEnum? Reason { get; set; }

    [JsonPropertyName("error")]
    public RealtimeResponseErrorDetails? Error { get; set; }
}
