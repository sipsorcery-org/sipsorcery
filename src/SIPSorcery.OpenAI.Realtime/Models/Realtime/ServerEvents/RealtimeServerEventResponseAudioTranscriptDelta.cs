using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

/// <summary>
/// Returned when the model-generated transcription of audio output is updated.
/// </summary>
public class RealtimeServerEventResponseAudioTranscriptDelta : RealtimeEventBase
{
    /// <summary>
    /// The fixed event type string: "response.audio_transcript.delta".
    /// </summary>
    public const string TypeName = "response.audio_transcript.delta";

    /// <summary>
    /// Overrides the event type property.
    /// </summary>
    [JsonPropertyName("type")]
    public override string? Type => TypeName;

    /// <summary>
    /// The ID of the response that produced the audio output.
    /// </summary>
    [JsonPropertyName("response_id")]
    public required string ResponseId { get; set; }

    /// <summary>
    /// The ID of the item in the response whose transcript is being updated.
    /// </summary>
    [JsonPropertyName("item_id")]
    public required string ItemId { get; set; }

    /// <summary>
    /// The index of the output item within the response.
    /// </summary>
    [JsonPropertyName("output_index")]
    public required int OutputIndex { get; set; }

    /// <summary>
    /// The index of the content part in the item's content array.
    /// </summary>
    [JsonPropertyName("content_index")]
    public required int ContentIndex { get; set; }

    /// <summary>
    /// The delta (partial update) of the transcribed text.
    /// </summary>
    [JsonPropertyName("delta")]
    public required string Delta { get; set; }
}
