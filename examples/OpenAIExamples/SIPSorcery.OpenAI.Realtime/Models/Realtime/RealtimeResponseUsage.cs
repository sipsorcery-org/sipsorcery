using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

/// <summary>
/// Contains token usage and billing statistics for a response.
/// </summary>
public class RealtimeResponseUsage
{
    [JsonPropertyName("total_tokens")]
    public int? TotalTokens { get; set; }

    [JsonPropertyName("input_tokens")]
    public int? InputTokens { get; set; }

    [JsonPropertyName("output_tokens")]
    public int? OutputTokens { get; set; }

    [JsonPropertyName("input_token_details")]
    public TokenDetails? InputTokenDetails { get; set; }

    [JsonPropertyName("output_token_details")]
    public TokenDetails? OutputTokenDetails { get; set; }
}