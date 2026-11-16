using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

/// <summary>
/// Adds a new item to the conversation's context, such as messages,
/// function calls, or responses. Can be used to populate history or inject items mid-stream.
/// Note: assistant audio messages cannot currently be added with this event.
/// </summary>
public class RealtimeClientEventConversationItemCreate : RealtimeEventBase
{
    public const string TypeName = "conversation.item.create";

    [JsonPropertyName("type")]
    public override string Type => TypeName;

    /// <summary>
    /// The ID of the preceding item after which the new item will be inserted.
    /// - If null, the item is appended to the end.
    /// - If "root", it's added at the beginning.
    /// - If a valid item ID, it's inserted mid-conversation.
    /// </summary>
    [JsonPropertyName("previous_item_id")]
    public string? PreviousItemID { get; set; }

    /// <summary>
    /// The new item to add to the conversation (message, function call, etc).
    /// </summary>
    [JsonPropertyName("item")]
    public required RealtimeConversationItem Item { get; set; }
}
