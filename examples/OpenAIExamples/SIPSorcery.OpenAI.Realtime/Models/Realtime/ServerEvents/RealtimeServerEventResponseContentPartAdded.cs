using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

/// <summary>
/// Returned when a new content part is added to an assistant message item during response generation.
/// </summary>
public class RealtimeServerEventResponseContentPartAdded : RealtimeEventBase
{
    public const string TypeName = "response.content_part.added";

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