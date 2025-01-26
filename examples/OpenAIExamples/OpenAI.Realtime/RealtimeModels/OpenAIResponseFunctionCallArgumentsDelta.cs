// https://platform.openai.com/docs/api-reference/realtime-server-events/response/function_call_arguments/delta

using System.Text.Json;
using System.Text.Json.Serialization;

namespace demo;

public class OpenAIResponseFunctionCallArgumentsDelta : OpenAIServerEventBase
{
    public const string TypeName = "response.function_call_arguments.delta";

    [JsonPropertyName("type")]
    public override string Type => TypeName;

    [JsonPropertyName("response_id")]
    public string? ResponseID { get; set; }

    [JsonPropertyName("item_id")]
    public string? ItemID { get; set; }

    [JsonPropertyName("output_index")]
    public int OutputIndex { get; set; }

    [JsonPropertyName("call_id")]
    public string? CallID { get; set; }

    [JsonPropertyName("delta")]
    public string? Delta{ get; set; }

    public override string ToJson()
    {
        return JsonSerializer.Serialize(this, JsonOptions.Default);
    }
}
