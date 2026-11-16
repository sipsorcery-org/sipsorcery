using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

/// <summary>
/// Sent by the client to cancel an in-progress response.
/// 
/// The server will respond with a `response.cancelled` event if successful,
/// or an error if no cancellable response is found.
/// </summary>
public class RealtimeClientEventResponseCancel : RealtimeEventBase
{
    /// <summary>
    /// The fixed event type value: "response.cancel".
    /// </summary>
    public const string TypeName = "response.cancel";

    /// <summary>
    /// Overrides the base type property with the constant event type.
    /// </summary>
    [JsonPropertyName("type")]
    public override string? Type => TypeName;

    /// <summary>
    /// A specific response ID to cancel. If not provided, cancels the in-progress
    /// response in the default conversation.
    /// </summary>
    [JsonPropertyName("response_id")]
    public string? ResponseID { get; set; }
}
