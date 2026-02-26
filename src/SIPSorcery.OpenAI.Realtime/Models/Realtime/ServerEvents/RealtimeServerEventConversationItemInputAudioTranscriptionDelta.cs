using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

/// <summary>
/// Emitted when the text value of an input audio transcription content part is updated.
/// This represents a partial (delta) update before the final transcript is complete.
/// </summary>
public class RealtimeServerEventConversationItemInputAudioTranscriptionDelta : RealtimeEventBase
{
    /// <summary>
    /// The fixed event type value: "conversation.item.input_audio_transcription.delta".
    /// </summary>
    public const string TypeName = "conversation.item.input_audio_transcription.delta";

    /// <summary>
    /// Overrides the base type property with the constant event type name.
    /// </summary>
    [JsonPropertyName("type")]
    public override string? Type => TypeName;

    /// <summary>
    /// The ID of the conversation item containing the input audio.
    /// </summary>
    [JsonPropertyName("item_id")]
    public required string ItemID { get; set; }

    /// <summary>
    /// The index of the content part in the item's content array.
    /// </summary>
    [JsonPropertyName("content_index")]
    public int? ContentIndex { get; set; }

    /// <summary>
    /// The updated text delta from transcription.
    /// </summary>
    [JsonPropertyName("delta")]
    public string? Delta { get; set; }

    /// <summary>
    /// Optional log probabilities associated with the delta, if available.
    /// </summary>
    [JsonPropertyName("logprobs")]
    public List<RealtimeLogProbProperties>? LogProbs { get; set; }
}
