// https://platform.openai.com/docs/api-reference/realtime-client-events/response/create

using System.Text.Json;
using System.Text.Json.Serialization;

namespace demo;

public class OpenAIResponseCreate : OpenAIServerEventBase
{
    [JsonPropertyName("type")]
    public override string Type => "response.create";

    [JsonPropertyName("response")]
    public required OpenAIResponseCreateResponse Response { get; set; }

    public override string ToJson()
    {
        return JsonSerializer.Serialize(this, JsonOptions.Default);
    }
}

public class OpenAIResponseCreateResponse
{
    [JsonPropertyName("modalities")]
    public string[] Modalities { get; set; } = new[] { "audio", "text" };

    [JsonPropertyName("instructions")]
    public string? Instructions { get; set; }

    [JsonPropertyName("voice")]
    public string? Voice { get; set; }

    [JsonPropertyName("output_audio_format")]
    public string OutputAudioFrmat { get; set; } = "pcm16";
}
