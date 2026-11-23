using System.Text.Json;
using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

/// <summary>
/// Base class for all real-time events. Provides common event metadata.
/// </summary>
[JsonConverter(typeof(RealtimeEventBaseConverter))]
public class RealtimeEventBase
{
    /// <summary>
    /// Mandatory type string. Should be overridden by derived classes with a constant.
    /// </summary>
    [JsonPropertyName("type")]
    public virtual string? Type { get; set; }

    /// <summary>
    /// Optional client-generated ID used to identify this event.
    /// </summary>
    [JsonPropertyName("event_id")]
    public string? EventID { get; set; }

    /// <summary>
    /// Serializes the event to JSON using standard options.
    /// </summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions.Default);
}