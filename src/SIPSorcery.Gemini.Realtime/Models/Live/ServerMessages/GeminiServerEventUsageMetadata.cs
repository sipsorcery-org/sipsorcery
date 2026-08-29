namespace SIPSorcery.Gemini.Realtime.Models;

/// <summary>
/// Surfaced when a server message carries <c>usageMetadata</c> and nothing else. Because
/// <c>usageMetadata</c> is a sibling of Gemini's message-type union rather than a member of it, it
/// far more often arrives attached to another message — read
/// <see cref="GeminiServerMessage.UsageMetadata"/> (available on every message kind) instead of
/// pattern-matching on this type if all you want is token accounting.
/// </summary>
public class GeminiServerEventUsageMetadata : GeminiServerMessage
{
    public const string JsonKey = "usageMetadata";

    public GeminiServerEventUsageMetadata() : base(GeminiServerMessageKind.UsageMetadata)
    {
    }
}
