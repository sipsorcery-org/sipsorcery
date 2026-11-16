using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

/// <summary>
/// Returned when an Item is done streaming. Also emitted when a Response is interrupted, incomplete, or cancelled.
/// </summary>
public class RealtimeServerEventResponseOutputItemDone : RealtimeEventBase
{
    public const string TypeName = "response.output_item.done";

    [JsonPropertyName("type")]
    public override string? Type => TypeName;

    /// <summary>
    /// The ID of the Response to which the item belongs.
    /// </summary>
    [JsonPropertyName("response_id")]
    public required string ResponseId { get; set; }

    /// <summary>
    /// The index of the output item in the Response.
    /// </summary>
    [JsonPropertyName("output_index")]
    public required int OutputIndex { get; set; }

    /// <summary>
    /// The item that was completed in the response output.
    /// </summary>
    [JsonPropertyName("item")]
    public required RealtimeConversationItem Item { get; set; }
}