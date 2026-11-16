using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

/// <summary>
/// Returned when the model-generated function call arguments are updated.
/// </summary>
public class RealtimeServerEventResponseFunctionCallArgumentsDelta : RealtimeEventBase
{
    public const string TypeName = "response.function_call_arguments.delta";

    [JsonPropertyName("type")]
    public override string? Type => TypeName;

    /// <summary>
    /// The ID of the response.
    /// </summary>
    [JsonPropertyName("response_id")]
    public required string ResponseId { get; set; }

    /// <summary>
    /// The ID of the function call item.
    /// </summary>
    [JsonPropertyName("item_id")]
    public required string ItemId { get; set; }

    /// <summary>
    /// The index of the output item in the response.
    /// </summary>
    [JsonPropertyName("output_index")]
    public required int OutputIndex { get; set; }

    /// <summary>
    /// The ID of the function call.
    /// </summary>
    [JsonPropertyName("call_id")]
    public required string CallId { get; set; }

    /// <summary>
    /// The arguments delta as a JSON string.
    /// </summary>
    [JsonPropertyName("delta")]
    public required string Delta { get; set; }
}