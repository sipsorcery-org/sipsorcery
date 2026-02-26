using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

/// <summary>
/// Returned when a session is updated with a `session.update` event,
/// unless there is an error.
/// </summary>
public class RealtimeServerEventSessionUpdated : RealtimeEventBase
{
    public const string TypeName = "session.updated";

    /// <summary>
    /// The event type, must be "session.updated".
    /// </summary>
    [JsonPropertyName("type")]
    public override string? Type => TypeName;

    /// <summary>
    /// The updated session object.
    /// </summary>
    [JsonPropertyName("session")]
    public required RealtimeSession Session { get; set; }
}