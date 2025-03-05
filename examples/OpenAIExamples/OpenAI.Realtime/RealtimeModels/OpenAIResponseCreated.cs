// https://platform.openai.com/docs/api-reference/realtime-server-events/response/created

using System.Text.Json;
using System.Text.Json.Serialization;

namespace demo;

public class OpenAIResponseCreated : OpenAIServerEventBase
{
    public const string TypeName = "response.created";

    [JsonPropertyName("type")]
    public override string Type => TypeName;

    // TODO - Add the properties for the response object

    public override string ToJson()
    {
        return JsonSerializer.Serialize(this, JsonOptions.Default);
    }
}

