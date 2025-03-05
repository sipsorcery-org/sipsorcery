// https://platform.openai.com/docs/api-reference/realtime-client-events/session/update

using System.Text.Json.Serialization;

namespace demo;

public class OpenAITurnDetection
{
    [JsonPropertyName("type")]
    public string Type => "server_vad";

    [JsonPropertyName("threshold")]
    public decimal Threshold { get; set; }

    [JsonPropertyName("prefix_padding_ms")]
    public int PrefixPaddingMilliseconds { get; set; }

    [JsonPropertyName("silence_duration_ms")]
    public int SilenceDurationMilliseconds { get; set; }

    [JsonPropertyName("create_response")]
    public bool CreateResponse { get; set; }
}
