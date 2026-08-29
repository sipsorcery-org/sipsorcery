namespace SIPSorcery.Gemini.Realtime.Models;

/// <summary>
/// Discriminates the concrete type of a <see cref="GeminiServerMessage"/>. Unlike the OpenAI
/// Realtime API, Gemini's BidiGenerateContent protocol does not carry an explicit "type" field —
/// the wrapping JSON property name on the server message (e.g. "serverContent", "toolCall") is
/// itself the discriminator. This enum exists purely so consumers can pattern-match/switch on
/// message kind without repeatedly using "is" type checks.
/// </summary>
public enum GeminiServerMessageKind
{
    Unknown,
    SetupComplete,
    ServerContent,
    ToolCall,
    ToolCallCancellation,
    UsageMetadata,
    GoAway,
    SessionResumptionUpdate
}
