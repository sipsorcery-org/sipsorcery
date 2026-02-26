using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

/// <summary>
/// Returned when the model-generated audio is updated. 
/// This includes a base64-encoded delta of audio data and metadata 
/// identifying the corresponding response, item, and content location.
/// </summary>
public class RealtimeServerEventResponseAudioDelta : RealtimeEventBase
{
    /// <summary>
    /// The fixed event type string: "response.audio.delta".
    /// </summary>
    public const string TypeName = "response.audio.delta";

    /// <summary>
    /// Overrides the event type property.
    /// </summary>
    [JsonPropertyName("type")]
    public override string? Type => TypeName;

    /// <summary>
    /// The ID of the response this audio belongs to.
    /// </summary>
    [JsonPropertyName("response_id")]
    public required string ResponseId { get; set; }

    /// <summary>
    /// The ID of the item within the response.
    /// </summary>
    [JsonPropertyName("item_id")]
    public required string ItemId { get; set; }

    /// <summary>
    /// The index of the output item in the response's output array.
    /// </summary>
    [JsonPropertyName("output_index")]
    public required int OutputIndex { get; set; }

    /// <summary>
    /// The index of the content part within the item's content array.
    /// </summary>
    [JsonPropertyName("content_index")]
    public required int ContentIndex { get; set; }

    /// <summary>
    /// Base64-encoded audio data delta.
    /// </summary>
    [JsonPropertyName("delta")]
    public required string Delta { get; set; }
}
