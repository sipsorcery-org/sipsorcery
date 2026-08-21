using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SIPSorcery.Gemini.Realtime.Models;

/// <summary>
/// One or more function calls requested by the model. Reply with a
/// <see cref="GeminiToolResponseMessage"/> carrying a matching <see cref="GeminiFunctionResponse"/>
/// per call, keyed by <see cref="GeminiFunctionCall.Id"/>.
/// </summary>
public class GeminiServerEventToolCall : GeminiServerMessage
{
    public const string JsonKey = "toolCall";

    public GeminiServerEventToolCall() : base(GeminiServerMessageKind.ToolCall)
    {
    }

    [JsonPropertyName("functionCalls")]
    public List<GeminiFunctionCall>? FunctionCalls { get; set; }
}
