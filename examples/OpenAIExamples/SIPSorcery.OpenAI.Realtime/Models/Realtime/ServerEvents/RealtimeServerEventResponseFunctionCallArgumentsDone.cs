using System.Text.Json;
using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

/// <summary>
/// Returned when the model-generated function call arguments are done streaming.
/// Also emitted when a Response is interrupted, incomplete, or cancelled.
/// </summary>
public class RealtimeServerEventResponseFunctionCallArgumentsDone : RealtimeEventBase
{
    public const string TypeName = "response.function_call_arguments.done";

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

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// The final arguments as a JSON string.
    /// </summary>
    [JsonPropertyName("arguments")]
    [JsonConverter(typeof(JsonStringToElementConverter))]
    public JsonElement Arguments { get; set; }
}
