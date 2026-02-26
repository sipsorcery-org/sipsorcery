using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

public class RealtimeToolParameters
{
    [JsonPropertyName("type")]
    public string Type => "object";

    [JsonPropertyName("properties")]
    public Dictionary<string, RealtimeToolProperty>? Properties { get; set; }

    [JsonPropertyName("required")]
    public List<string>? Required { get; set; }
}
