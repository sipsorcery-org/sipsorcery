using System.Text.Json;

namespace SIPSorcery.Gemini.Realtime.Models;

/// <summary>
/// Base class for all messages sent by the client over the BidiGenerateContent WebSocket. Each
/// concrete subclass wraps its payload under a single, fixed top-level JSON property (its Gemini
/// wire-format discriminator, e.g. "setup", "clientContent") via a normal [JsonPropertyName]
/// attribute — no custom converter is needed on the write side because the caller always
/// constructs the exact concrete type it wants to send.
/// </summary>
public abstract class GeminiClientMessage
{
    public string ToJson() => JsonSerializer.Serialize(this, GetType(), JsonOptions.Default);
}
