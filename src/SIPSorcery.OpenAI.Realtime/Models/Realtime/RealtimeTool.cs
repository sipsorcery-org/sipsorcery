using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

public class RealtimeTool
{
    [JsonPropertyName("type")]
    public RealtimeToolKindEnum Type => RealtimeToolKindEnum.function;

    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("parameters")]
    public RealtimeToolParameters? Parameters { get; set; }
}
