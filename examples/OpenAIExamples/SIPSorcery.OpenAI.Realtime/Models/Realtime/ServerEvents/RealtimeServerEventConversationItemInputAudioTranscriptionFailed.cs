using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

/// <summary>
/// Emitted when a transcription request for a user message fails.
/// This event is specific to transcription errors and includes the item context,
/// allowing the client to correlate the error to the audio item.
/// </summary>
public class RealtimeServerEventConversationItemInputAudioTranscriptionFailed : RealtimeEventBase
{
    /// <summary>
    /// The fixed event type value: "conversation.item.input_audio_transcription.failed".
    /// </summary>
    public const string TypeName = "conversation.item.input_audio_transcription.failed";

    /// <summary>
    /// Overrides the base type property with the constant event type.
    /// </summary>
    [JsonPropertyName("type")]
    public override string? Type => TypeName;

    /// <summary>
    /// The ID of the user message item for which transcription failed.
    /// </summary>
    [JsonPropertyName("item_id")]
    public required string ItemID { get; set; }

    /// <summary>
    /// The index of the content part that failed to transcribe.
    /// </summary>
    [JsonPropertyName("content_index")]
    public required int ContentIndex { get; set; }

    /// <summary>
    /// The error information detailing the cause of the transcription failure.
    /// </summary>
    [JsonPropertyName("error")]
    public required RealtimeTranscriptionError Error { get; set; }
}