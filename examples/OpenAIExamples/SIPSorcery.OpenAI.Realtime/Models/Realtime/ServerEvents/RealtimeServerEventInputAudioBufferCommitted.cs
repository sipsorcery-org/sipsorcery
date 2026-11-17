using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

/// <summary>
/// Emitted by the server when the input audio buffer is committed—either manually by the client
/// or automatically in server VAD mode. This indicates that a new user message item will be created.
/// A corresponding `conversation.item.created` event will follow.
/// </summary>
public class RealtimeServerEventInputAudioBufferCommitted : RealtimeEventBase
{
    /// <summary>
    /// The fixed event type value: "input_audio_buffer.committed".
    /// </summary>
    public const string TypeName = "input_audio_buffer.committed";

    /// <summary>
    /// Overrides the base type property with the constant event type name.
    /// </summary>
    [JsonPropertyName("type")]
    public override string? Type => TypeName;

    /// <summary>
    /// The ID of the item that this new message will follow in the conversation.
    /// </summary>
    [JsonPropertyName("previous_item_id")]
    public required string PreviousItemID { get; set; }

    /// <summary>
    /// The ID of the user message item that will be created from the committed audio.
    /// </summary>
    [JsonPropertyName("item_id")]
    public required string ItemID { get; set; }
}
