using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

/// <summary>
/// Sent by the client to request the server to create a Response by triggering model inference.
///
/// In Server VAD mode, the server may do this automatically. A Response may contain one or more
/// Items, including function calls, and will be appended to the conversation history.
///
/// This event allows clients to override session-level inference settings such as instructions,
/// temperature, and modality for this response only.
///
/// The server responds with a sequence of events:
/// - `response.created`
/// - Events for each created item and content
/// - `response.done` when the Response is complete.
/// </summary>
public class RealtimeClientEventResponseCreate : RealtimeEventBase
{
    /// <summary>
    /// The fixed event type value: "response.create".
    /// </summary>
    public const string TypeName = "response.create";

    /// <summary>
    /// Overrides the base type property with the constant event type name.
    /// </summary>
    [JsonPropertyName("type")]
    public override string? Type => TypeName;

    /// <summary>
    /// Inference configuration and instructions to use for this specific response.
    /// Overrides session-level settings.
    /// </summary>
    [JsonPropertyName("response")]
    public RealtimeResponseCreateParams? Response { get; set; }
}
