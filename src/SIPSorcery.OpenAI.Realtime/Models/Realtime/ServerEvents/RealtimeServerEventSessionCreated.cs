using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

/// <summary>
/// Emitted automatically by the server when a new session is established.
/// This is the first event sent from the server on a new connection, and it
/// contains the default session configuration.
/// </summary>
public class RealtimeServerEventSessionCreated : RealtimeEventBase
{
    /// <summary>
    /// The fixed event type value: "session.created".
    /// </summary>
    public const string TypeName = "session.created";

    /// <summary>
    /// Overrides the base type property with the constant value.
    /// </summary>
    [JsonPropertyName("type")]
    public override string? Type => TypeName;

    /// <summary>
    /// The full session configuration associated with the newly created session.
    /// </summary>
    [JsonPropertyName("session")]
    public required RealtimeSession Session { get; set; }
}
