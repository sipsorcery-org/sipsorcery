using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.WebRTC.Models.Realtime;

/// <summary>
/// Represents the minimal conversation resource returned by the event.
/// </summary>
public class RealtimeConversation
{
    /// <summary>
    /// Unique ID of the conversation.
    /// </summary>
    [JsonPropertyName("id")]
    public required string ID { get; set; }

    /// <summary>
    /// The object type, always "realtime.conversation".
    /// </summary>
    [JsonPropertyName("object")]
    public string Object => "realtime.conversation";
}
