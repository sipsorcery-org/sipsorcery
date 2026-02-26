using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

/// <summary>
/// Returned when the model-generated audio is done. 
/// This event is also emitted if the response is interrupted, incomplete, or cancelled.
/// </summary>
public class RealtimeServerEventResponseAudioDone : RealtimeEventBase
{
    /// <summary>
    /// The fixed event type string: "response.audio.done".
    /// </summary>
    public const string TypeName = "response.audio.done";

    /// <summary>
    /// Overrides the event type property.
    /// </summary>
    [JsonPropertyName("type")]
    public override string? Type => TypeName;

    /// <summary>
    /// The ID of the response that generated the audio.
    /// </summary>
    [JsonPropertyName("response_id")]
    public required string ResponseId { get; set; }

    /// <summary>
    /// The ID of the item within the response.
    /// </summary>
    [JsonPropertyName("item_id")]
    public required string ItemId { get; set; }

    /// <summary>
    /// The index of the output item in the response.
    /// </summary>
    [JsonPropertyName("output_index")]
    public required int OutputIndex { get; set; }

    /// <summary>
    /// The index of the content part in the item's content array.
    /// </summary>
    [JsonPropertyName("content_index")]
    public required int ContentIndex { get; set; }
}