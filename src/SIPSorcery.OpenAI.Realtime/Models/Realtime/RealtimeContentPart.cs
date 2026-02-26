using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

/// <summary>
/// Represents the content part that was added to a response.
/// </summary>
public class ContentPart
{
    [JsonPropertyName("type")]
    public required string Type { get; set; } // "text" or "audio"

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("audio")]
    public string? Audio { get; set; }

    [JsonPropertyName("transcript")]
    public string? Transcript { get; set; }
}