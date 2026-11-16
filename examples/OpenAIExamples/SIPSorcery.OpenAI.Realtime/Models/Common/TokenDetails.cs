using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

/// <summary>
/// Describes text/audio token distribution for input/output.
/// </summary>
public class TokenDetails
{
    [JsonPropertyName("cached_tokens")]
    public int? CachedTokens { get; set; }

    [JsonPropertyName("text_tokens")]
    public int? TextTokens { get; set; }

    [JsonPropertyName("audio_tokens")]
    public int? AudioTokens { get; set; }
}
