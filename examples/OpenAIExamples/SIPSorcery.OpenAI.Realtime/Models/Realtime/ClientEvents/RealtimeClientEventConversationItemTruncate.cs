using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

/// <summary>
/// Sent by the client to truncate audio from a previous assistant message that
/// has already been sent but not played back yet.
/// 
/// This keeps the server in sync with what the user has actually heard and removes
/// the associated transcript so unplayed audio isn't treated as contextual input.
///
/// The server will respond with a `conversation.item.truncated` event on success,
/// or an error if truncation fails (e.g., duration too long).
/// </summary>
public class RealtimeClientEventConversationItemTruncate : RealtimeEventBase
{
    /// <summary>
    /// The fixed event type name: "conversation.item.truncate".
    /// </summary>
    public const string TypeName = "conversation.item.truncate";

    /// <summary>
    /// Overrides the base type property with the constant value.
    /// </summary>
    [JsonPropertyName("type")]
    public override string? Type => TypeName;

    /// <summary>
    /// The ID of the assistant message item to truncate. Only assistant messages can be truncated.
    /// </summary>
    [JsonPropertyName("item_id")]
    public required string ItemID { get; set; }

    /// <summary>
    /// The index of the content part to truncate. Always set to 0.
    /// </summary>
    [JsonPropertyName("content_index")]
    public required int ContentIndex { get; set; }

    /// <summary>
    /// Inclusive duration in milliseconds up to which audio is truncated.
    /// If the value exceeds the actual audio duration, an error will be returned.
    /// </summary>
    [JsonPropertyName("audio_end_ms")]
    public required int AudioEndMs { get; set; }
}

