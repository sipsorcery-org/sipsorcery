namespace SIPSorcery.Gemini.Realtime.Models;

/// <summary>
/// Synthetic fallback used when a server message's top-level JSON key does not match any
/// known Gemini BidiGenerateContent message type, or when the nested payload fails to
/// deserialise (e.g. an enum value Google has introduced that this library doesn't know
/// about yet). Carries the original JSON so callers can still inspect/handle it.
/// </summary>
public class GeminiUnknownServerMessage : GeminiServerMessage
{
    public GeminiUnknownServerMessage() : base(GeminiServerMessageKind.Unknown)
    {
    }

    /// <summary>
    /// The unrecognised top-level JSON key, if one could be found (e.g. "someNewMessageType").
    /// Null if the root JSON object had no properties at all.
    /// </summary>
    public string? OriginalKey { get; set; }

    /// <summary>
    /// The raw JSON of the full message as received on the wire.
    /// </summary>
    public string? OriginalJson { get; set; }
}
