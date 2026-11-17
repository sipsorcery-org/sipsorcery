using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

/// <summary>
/// Emitted by the server in response to a `conversation.item.retrieve` request.
/// Contains the full representation of the retrieved conversation item.
/// </summary>
public class RealtimeServerEventConversationItemRetrieved : RealtimeEventBase
{
    /// <summary>
    /// The fixed event type value: "conversation.item.retrieved".
    /// </summary>
    public const string TypeName = "conversation.item.retrieved";

    /// <summary>
    /// Overrides the base type property with the constant event type name.
    /// </summary>
    [JsonPropertyName("type")]
    public override string? Type => TypeName;

    /// <summary>
    /// The retrieved item from the conversation history.
    /// </summary>
    [JsonPropertyName("item")]
    public required RealtimeConversationItem Item { get; set; }
}
