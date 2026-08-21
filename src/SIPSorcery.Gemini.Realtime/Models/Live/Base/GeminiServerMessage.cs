using System.Text.Json;
using System.Text.Json.Serialization;

namespace SIPSorcery.Gemini.Realtime.Models;

/// <summary>
/// Base class for all messages received from Gemini over the BidiGenerateContent WebSocket.
/// </summary>
[JsonConverter(typeof(GeminiServerMessageConverter))]
public abstract class GeminiServerMessage
{
    /// <summary>
    /// Identifies the concrete message kind for switch/pattern-matching by consumers. Set by each
    /// subclass's constructor rather than an overridden property: System.Text.Json does not
    /// inherit <see cref="JsonIgnoreAttribute"/> onto a property override, so an abstract
    /// <c>Kind</c> would leak into the serialised output of every message.
    /// </summary>
    [JsonIgnore]
    public GeminiServerMessageKind Kind { get; }

    /// <summary>
    /// Token usage reported with this message, if any. Gemini's BidiGenerateContentServerMessage
    /// carries <c>usageMetadata</c> as a sibling of the message-type union (unlike every other
    /// field, which is a union member), so it commonly arrives attached to a
    /// <see cref="GeminiServerEventContent"/> rather than on its own. Populated by
    /// <see cref="GeminiServerMessageConverter"/> whichever way it arrives.
    /// </summary>
    [JsonIgnore]
    public GeminiUsageMetadata? UsageMetadata { get; set; }

    protected GeminiServerMessage(GeminiServerMessageKind kind)
    {
        Kind = kind;
    }

    /// <summary>
    /// Serialises this message's payload, i.e. the value that was wrapped under its top-level JSON
    /// key on the wire ("serverContent", "toolCall", ...) rather than the wrapper itself. Intended
    /// for logging/diagnostics only — this class is never sent by the client.
    /// </summary>
    public string ToJson() => JsonSerializer.Serialize(this, GetType(), JsonOptions.Default);
}
