using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SIPSorcery.Gemini.Realtime.Models;

/// <summary>
/// Token usage for the session so far. In Gemini's BidiGenerateContentServerMessage this is a
/// sibling of the message-type union rather than a member of it, so it can arrive on its own
/// (surfaced as a <see cref="GeminiServerEventUsageMetadata"/>) or alongside any other server
/// message (surfaced via <see cref="GeminiServerMessage.UsageMetadata"/>).
/// </summary>
public class GeminiUsageMetadata
{
    [JsonPropertyName("promptTokenCount")]
    public int? PromptTokenCount { get; set; }

    [JsonPropertyName("cachedContentTokenCount")]
    public int? CachedContentTokenCount { get; set; }

    [JsonPropertyName("responseTokenCount")]
    public int? ResponseTokenCount { get; set; }

    [JsonPropertyName("toolUsePromptTokenCount")]
    public int? ToolUsePromptTokenCount { get; set; }

    [JsonPropertyName("thoughtsTokenCount")]
    public int? ThoughtsTokenCount { get; set; }

    [JsonPropertyName("totalTokenCount")]
    public int? TotalTokenCount { get; set; }

    [JsonPropertyName("promptTokensDetails")]
    public List<GeminiModalityTokenCount>? PromptTokensDetails { get; set; }

    [JsonPropertyName("cacheTokensDetails")]
    public List<GeminiModalityTokenCount>? CacheTokensDetails { get; set; }

    [JsonPropertyName("responseTokensDetails")]
    public List<GeminiModalityTokenCount>? ResponseTokensDetails { get; set; }
}
