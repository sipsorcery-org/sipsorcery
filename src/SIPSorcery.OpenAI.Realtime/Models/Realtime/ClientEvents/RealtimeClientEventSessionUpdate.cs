using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

/// <summary>
/// Send this event to update the session’s default configuration.
/// 
/// You can send this at any time to update any field except for <c>voice</c>.
/// Note: once a session is initialized with a specific <c>model</c>,
/// it cannot be changed using this event.
/// 
/// The server responds with a <c>session.updated</c> event containing the full effective configuration.
/// 
/// Only the fields included in this request will be updated. To clear a field like <c>instructions</c>,
/// use an empty string.
/// </summary>
public class RealtimeClientEventSessionUpdate : RealtimeEventBase
{
    /// <summary>
    /// The fixed event type value: "session.update".
    /// </summary>
    public const string TypeName = "session.update";

    /// <summary>
    /// Overrides the base type property with the constant value.
    /// </summary>
    [JsonPropertyName("type")]
    public override string? Type => TypeName;

    /// <summary>
    /// The new session configuration to apply. Only included fields will be updated.
    /// </summary>
    [JsonPropertyName("session")]
    public required RealtimeSession Session { get; set; }
}
