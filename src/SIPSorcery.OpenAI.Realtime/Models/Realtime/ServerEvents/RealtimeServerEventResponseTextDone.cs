using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

/// <summary>
/// Returned when the text value of a "text" content part is done streaming.
/// Also emitted when a Response is interrupted, incomplete, or cancelled.
/// </summary>
public class RealtimeServerEventResponseTextDone : RealtimeEventBase
{
    public const string TypeName = "response.text.done";

    [JsonPropertyName("type")]
    public override string? Type => TypeName;

    /// <summary>
    /// The ID of the response.
    /// </summary>
    [JsonPropertyName("response_id")]
    public required string ResponseId { get; set; }

    /// <summary>
    /// The ID of the item.
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

    /// <summary>
    /// The final text content.
    /// </summary>
    [JsonPropertyName("text")]
    public required string Text { get; set; }
}
