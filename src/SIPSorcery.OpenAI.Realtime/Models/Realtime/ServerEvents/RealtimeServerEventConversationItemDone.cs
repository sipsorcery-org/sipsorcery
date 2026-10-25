using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

/// <summary>
/// Emitted by the server (GA Realtime API) when a conversation item is finalised: its content is
/// complete, e.g. an assistant message once the model has finished generating it, with the full
/// transcript of any audio. Follows a <see cref="RealtimeServerEventConversationItemAdded"/> for the
/// same item.
/// </summary>
public class RealtimeServerEventConversationItemDone : RealtimeEventBase
{
    /// <summary>
    /// The fixed event type value: "conversation.item.done".
    /// </summary>
    public const string TypeName = "conversation.item.done";

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
    /// The completed item.
    /// </summary>
    [JsonPropertyName("item")]
    public required RealtimeConversationItem Item { get; set; }
}
