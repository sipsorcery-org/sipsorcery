using SIPSorcery.OpenAI.WebRTC.Models.Realtime;
using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

/// <summary>
/// Emitted by the server after a new conversation is created.
/// This typically follows immediately after a session is established.
/// </summary>
public class RealtimeServerEventConversationCreated : RealtimeEventBase
{
    /// <summary>
    /// The fixed event type value: "conversation.created".
    /// </summary>
    public const string TypeName = "conversation.created";

    /// <summary>
    /// Overrides the base type property with the constant value.
    /// </summary>
    [JsonPropertyName("type")]
    public override string? Type => TypeName;

    /// <summary>
    /// The conversation object containing the conversation ID and type.
    /// </summary>
    [JsonPropertyName("conversation")]
    public required RealtimeConversation Conversation { get; set; }
}