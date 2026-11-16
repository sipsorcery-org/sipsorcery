using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

/// <summary>
/// Ephemeral key used in client environments to authenticate with the Realtime API.
/// </summary>
public class RealtimeClientSecret
{
    /// <summary>
    /// The ephemeral key string.
    /// </summary>
    [JsonPropertyName("value")]
    public string Value { get; set; } = default!;

    /// <summary>
    /// Expiration timestamp of the token (epoch seconds).
    /// </summary>
    [JsonPropertyName("expires_at")]
    public long ExpiresAt { get; set; }
}
