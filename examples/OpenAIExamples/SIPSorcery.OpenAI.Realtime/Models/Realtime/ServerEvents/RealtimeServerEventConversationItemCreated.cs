using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

/// <summary>
/// Emitted by the server when a conversation item is created. This may occur:
/// - As part of generating a model response (message or function call),
/// - When committing an input audio buffer,
/// - Or when the client explicitly adds an item via `conversation.item.create`.
/// </summary>
public class RealtimeServerEventConversationItemCreated : RealtimeEventBase
{
    /// <summary>
    /// The fixed event type value: "conversation.item.created".
    /// </summary>
    public const string TypeName = "conversation.item.created";

    /// <summary>
    /// Overrides the base type property with the constant value.
    /// </summary>
    [JsonPropertyName("type")]
    public override string? Type => TypeName;

    /// <summary>
    /// The ID of the item that immediately precedes the newly created item in the conversation.
    /// </summary>
    [JsonPropertyName("previous_item_id")]
    public required string PreviousItemID { get; set; }

    /// <summary>
    /// The item that was created and added to the conversation.
    /// </summary>
    [JsonPropertyName("item")]
    public required RealtimeConversationItem Item { get; set; }
}
