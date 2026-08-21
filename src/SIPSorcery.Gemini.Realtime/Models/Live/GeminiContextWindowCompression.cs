using System.Text.Json.Serialization;

namespace SIPSorcery.Gemini.Realtime.Models;

public class GeminiSlidingWindow
{
    [JsonPropertyName("targetTokens")]
    public long? TargetTokens { get; set; }
}

/// <summary>
/// Configures compression of the session's context window so long-running sessions don't
/// exceed the model's token limit.
/// </summary>
public class GeminiContextWindowCompression
{
    [JsonPropertyName("slidingWindow")]
    public GeminiSlidingWindow? SlidingWindow { get; set; }

    [JsonPropertyName("triggerTokens")]
    public long? TriggerTokens { get; set; }
}
