using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

/// <summary>
/// Emitted by the server when a conversation item has been deleted in response
/// to a client-issued `conversation.item.delete` event. Keeps server and client
/// histories in sync.
/// </summary>
public class RealtimeServerEventConversationItemDeleted : RealtimeEventBase
{
    /// <summary>
    /// The fixed event type value: "conversation.item.deleted".
    /// </summary>
    public const string TypeName = "conversation.item.deleted";

    /// <summary>
    /// Overrides the base type property with the constant event type name.
    /// </summary>
    [JsonPropertyName("type")]
    public override string? Type => TypeName;

    /// <summary>
    /// The ID of the item that was deleted from the conversation.
    /// </summary>
    [JsonPropertyName("item_id")]
    public required string ItemID { get; set; }
}
