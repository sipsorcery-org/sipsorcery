using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

/// <summary>
/// Returned when the model-generated transcription of audio output is done streaming.
/// Also emitted when a Response is interrupted, incomplete, or cancelled.
/// </summary>
public class RealtimeServerEventResponseAudioTranscriptDone : RealtimeEventBase
{
    /// <summary>
    /// The fixed event type string: "response.audio_transcript.done".
    /// </summary>
    public const string TypeName = "response.audio_transcript.done";

    /// <summary>
    /// Overrides the event type property.
    /// </summary>
    [JsonPropertyName("type")]
    public override string? Type => TypeName;

    /// <summary>
    /// The ID of the response that generated the transcription.
    /// </summary>
    [JsonPropertyName("response_id")]
    public required string ResponseId { get; set; }

    /// <summary>
    /// The ID of the item associated with the audio output.
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
    /// The final, complete transcript of the audio.
    /// </summary>
    [JsonPropertyName("transcript")]
    public required string Transcript { get; set; }
}