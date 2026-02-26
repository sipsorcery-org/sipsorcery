using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

/// <summary>
/// Details of a single rate limit.
/// </summary>
public class RateLimit
{
    /// <summary>
    /// The name of the rate limit. Options are "requests" or "tokens".
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    /// <summary>
    /// The maximum allowed value for the rate limit.
    /// </summary>
    [JsonPropertyName("limit")]
    public required int Limit { get; set; }

    /// <summary>
    /// The remaining value before the limit is reached.
    /// </summary>
    [JsonPropertyName("remaining")]
    public required int Remaining { get; set; }

    /// <summary>
    /// Seconds until the rate limit resets.
    /// </summary>
    [JsonPropertyName("reset_seconds")]
    public required double ResetSeconds { get; set; }
}
