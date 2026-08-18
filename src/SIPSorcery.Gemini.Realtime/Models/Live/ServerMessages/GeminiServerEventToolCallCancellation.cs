using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SIPSorcery.Gemini.Realtime.Models;

/// <summary>
/// Signals that previously issued function call(s) are no longer needed, typically because the
/// user interrupted the model before the calls could be answered.
/// </summary>
public class GeminiServerEventToolCallCancellation : GeminiServerMessage
{
    public const string JsonKey = "toolCallCancellation";

    public GeminiServerEventToolCallCancellation() : base(GeminiServerMessageKind.ToolCallCancellation)
    {
    }

    /// <summary>
    /// The <see cref="GeminiFunctionCall.Id"/> values of the cancelled calls.
    /// </summary>
    [JsonPropertyName("ids")]
    public List<string>? Ids { get; set; }
}
