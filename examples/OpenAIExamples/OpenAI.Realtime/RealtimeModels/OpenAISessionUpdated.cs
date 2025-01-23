// https://platform.openai.com/docs/api-reference/realtime-server-events/session/updated

using System.Text.Json;
using System.Text.Json.Serialization;

namespace demo;

public class OpenAISessionUpdated : OpenAIServerEventBase
{
    public const string TypeName = "session.updated";

    [JsonPropertyName("type")]
    public override string Type => TypeName;

    [JsonPropertyName("session")]
    public required OpenAISession Session { get; set; }

    public override string ToJson()
    {
        return JsonSerializer.Serialize(this, JsonOptions.Default);
    }
}
