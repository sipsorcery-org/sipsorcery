using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

/// <summary>
/// Emitted at the beginning of a Response to indicate the updated rate limits.
/// When a Response is created, some tokens will be "reserved" for the output tokens.
/// The rate limits shown here reflect that reservation and are adjusted again
/// when the Response completes.
/// </summary>
public class RealtimeServerEventRateLimitsUpdated : RealtimeEventBase
{
    /// <summary>
    /// The fixed event type string: "rate_limits.updated".
    /// </summary>
    public const string TypeName = "rate_limits.updated";

    /// <summary>
    /// Overrides the event type property.
    /// </summary>
    [JsonPropertyName("type")]
    public override string? Type => TypeName;

    /// <summary>
    /// A list of updated rate limits including limits on requests and tokens.
    /// </summary>
    [JsonPropertyName("rate_limits")]
    public required List<RateLimit> RateLimits { get; set; } = [];
}