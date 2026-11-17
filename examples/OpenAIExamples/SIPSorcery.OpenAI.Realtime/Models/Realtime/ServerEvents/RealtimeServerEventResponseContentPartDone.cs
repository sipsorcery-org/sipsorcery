using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

/// <summary>
/// Returned when a content part is done streaming in an assistant message item.
/// Also emitted when a Response is interrupted, incomplete, or cancelled.
/// </summary>
public class RealtimeServerEventResponseContentPartDone : RealtimeEventBase
{
    public const string TypeName = "response.content_part.done";

    [JsonPropertyName("type")]
    public override string? Type => TypeName;

    [JsonPropertyName("response_id")]
    public required string ResponseId { get; set; }

    [JsonPropertyName("item_id")]
    public required string ItemId { get; set; }

    [JsonPropertyName("output_index")]
    public required int OutputIndex { get; set; }

    [JsonPropertyName("content_index")]
    public required int ContentIndex { get; set; }

    [JsonPropertyName("part")]
    public required ContentPart Part { get; set; }
}
