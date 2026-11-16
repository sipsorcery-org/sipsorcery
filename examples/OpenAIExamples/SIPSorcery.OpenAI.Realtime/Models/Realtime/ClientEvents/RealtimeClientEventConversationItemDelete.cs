using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

/// <summary>
/// Sent by the client to delete an item from the conversation history.
/// The server will respond with a `conversation.item.deleted` event on success,
/// or an `error` event if the item does not exist.
/// </summary>
public class RealtimeClientEventConversationItemDelete : RealtimeEventBase
{
    /// <summary>
    /// The fixed event type name: "conversation.item.delete".
    /// </summary>
    public const string TypeName = "conversation.item.delete";

    /// <summary>
    /// Overrides base type property with the required constant value.
    /// </summary>
    [JsonPropertyName("type")]
    public override string? Type => TypeName;

    /// <summary>
    /// The ID of the item to delete from the conversation.
    /// </summary>
    [JsonPropertyName("item_id")]
    public required string ItemID { get; set; }
}
