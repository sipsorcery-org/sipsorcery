// https://platform.openai.com/docs/api-reference/realtime-client-events/session/update

using System.Text.Json;
using System.Text.Json.Serialization;

namespace demo;

public class OpenAISessionUpdate : OpenAIServerEventBase
{
    public const string TypeName = "session.update";

    [JsonPropertyName("type")]
    public override string Type => TypeName;

    [JsonPropertyName("session")]
    public required OpenAISession Session { get; set; }

    public override string ToJson()
    {
        return JsonSerializer.Serialize(this, JsonOptions.Default);
    }
}
