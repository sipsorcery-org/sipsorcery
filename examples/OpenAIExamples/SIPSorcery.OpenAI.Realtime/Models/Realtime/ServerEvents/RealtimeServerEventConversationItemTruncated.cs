using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

/// <summary>
/// Emitted by the server when an assistant audio message item is truncated by the client.
/// This keeps the server's understanding of what was heard in sync with the user's playback.
/// Truncation removes both the audio and associated transcript beyond the specified point.
/// </summary>
public class RealtimeServerEventConversationItemTruncated : RealtimeEventBase
{
    /// <summary>
    /// The fixed event type value: "conversation.item.truncated".
    /// </summary>
    public const string TypeName = "conversation.item.truncated";

    /// <summary>
    /// Overrides the base type property with the constant event type.
    /// </summary>
    [JsonPropertyName("type")]
    public override string? Type => TypeName;

    /// <summary>
    /// The ID of the assistant message item that was truncated.
    /// </summary>
    [JsonPropertyName("item_id")]
    public required string ItemID { get; set; }

    /// <summary>
    /// The index of the content part within the item that was truncated.
    /// </summary>
    [JsonPropertyName("content_index")]
    public required int ContentIndex { get; set; }

    /// <summary>
    /// The duration in milliseconds up to which the audio was truncated.
    /// </summary>
    [JsonPropertyName("audio_end_ms")]
    public required int AudioEndMs { get; set; }
}
