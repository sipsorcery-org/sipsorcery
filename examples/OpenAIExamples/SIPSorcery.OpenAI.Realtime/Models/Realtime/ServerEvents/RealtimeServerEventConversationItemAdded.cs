using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

/// <summary>
/// Emitted by the server (GA Realtime API) when an item is added to the conversation: as part of a
/// model response, when the input audio buffer is committed, or when the client adds one with
/// `conversation.item.create`. The item's content may still be in progress; a
/// <see cref="RealtimeServerEventConversationItemDone"/> follows once it is complete. The GA
/// counterpart of the beta <see cref="RealtimeServerEventConversationItemCreated"/>.
/// </summary>
public class RealtimeServerEventConversationItemAdded : RealtimeEventBase
{
    /// <summary>
    /// The fixed event type value: "conversation.item.added".
    /// </summary>
    public const string TypeName = "conversation.item.added";

    /// <summary>
    /// Overrides the base type property with the constant value.
    /// </summary>
    [JsonPropertyName("type")]
    public override string? Type => TypeName;

    /// <summary>
    /// The ID of the item that precedes this one in the conversation, or null if it is the first item.
    /// </summary>
    [JsonPropertyName("previous_item_id")]
    public string? PreviousItemID { get; set; }

    /// <summary>
    /// The item that was added.
    /// </summary>
    [JsonPropertyName("item")]
    public required RealtimeConversationItem Item { get; set; }
}
