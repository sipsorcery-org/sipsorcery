// https://platform.openai.com/docs/api-reference/realtime-server-events/rate_limits

using System.Text.Json;
using System.Text.Json.Serialization;

namespace demo;

public class OpenAIRateLimitsUpdated : OpenAIServerEventBase
{
    public const string TypeName = "rate_limits.updated";

    [JsonPropertyName("type")]
    public override string Type => TypeName;

    // TODO: Add rate limit fields.

    public override string ToJson()
    {
        return JsonSerializer.Serialize(this, JsonOptions.Default);
    }
}
