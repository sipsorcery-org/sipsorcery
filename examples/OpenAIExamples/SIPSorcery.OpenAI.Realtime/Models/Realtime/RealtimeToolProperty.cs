using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

public class RealtimeToolProperty
{
    [JsonPropertyName("type")]
    public required string Type { get; set; }
}