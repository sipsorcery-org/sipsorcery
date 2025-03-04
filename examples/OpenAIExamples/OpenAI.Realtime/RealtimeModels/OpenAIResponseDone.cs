// https://platform.openai.com/docs/api-reference/realtime-server-events/response/done

using System.Text.Json;
using System.Text.Json.Serialization;

namespace demo;

public class OpenAIResponseDone : OpenAIServerEventBase
{
    public const string TypeName = "response.done";

    [JsonPropertyName("type")]
    public override string Type => TypeName;

    [JsonPropertyName("response")]
    public required OpenAIResponse Response { get; set; }

    public override string ToJson()
    {
        return JsonSerializer.Serialize(this, JsonOptions.Default);
    }
}
