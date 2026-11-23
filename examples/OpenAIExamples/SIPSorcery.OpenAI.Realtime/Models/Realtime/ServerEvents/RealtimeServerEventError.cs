using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

/// <summary>
/// Emitted by the server when an error occurs. This can result from either client- or server-side issues.
/// Most errors are recoverable and the session typically remains open.
/// Clients should monitor and log errors for diagnostics and recovery.
/// </summary>
public class RealtimeServerEventError : RealtimeEventBase
{
    /// <summary>
    /// The fixed event type value: "error".
    /// </summary>
    public const string TypeName = "error";

    /// <summary>
    /// Overrides the base type property with the constant error type.
    /// </summary>
    [JsonPropertyName("type")]
    public override string? Type => TypeName;

    /// <summary>
    /// Details about the error, including type, message, and context.
    /// </summary>
    [JsonPropertyName("error")]
    public required RealtimeErrorDetail Error { get; set; }
}
