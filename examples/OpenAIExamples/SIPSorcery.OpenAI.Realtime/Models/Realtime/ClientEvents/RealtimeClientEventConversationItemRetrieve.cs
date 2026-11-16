using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

/// <summary>
/// Sent by the client to retrieve the server's internal representation of a specific
/// item in the conversation history. Useful for inspecting post-processed audio
/// (e.g. after VAD and noise cancellation).
///
/// The server will respond with a `conversation.item.retrieved` event on success,
/// or an error if the item ID is invalid or missing.
/// </summary>
public class RealtimeClientEventConversationItemRetrieve : RealtimeEventBase
{
    /// <summary>
    /// The fixed event type value: "conversation.item.retrieve".
    /// </summary>
    public const string TypeName = "conversation.item.retrieve";

    /// <summary>
    /// Overrides the base type property with the constant event type.
    /// </summary>
    [JsonPropertyName("type")]
    public override string? Type => TypeName;

    /// <summary>
    /// The ID of the item to retrieve from the conversation history.
    /// </summary>
    [JsonPropertyName("item_id")]
    public required string ItemID { get; set; }
}

