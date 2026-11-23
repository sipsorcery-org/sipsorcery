using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

/// <summary>
/// Contains structured details of an error event returned by the server.
/// </summary>
public class RealtimeErrorDetail
{
    /// <summary>
    /// The type of error (e.g., "invalid_request_error", "server_error").
    /// </summary>
    [JsonPropertyName("type")]
    public required string Type { get; set; }

    /// <summary>
    /// Error code, if provided (e.g., "invalid_event").
    /// </summary>
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    /// <summary>
    /// A human-readable message describing the error.
    /// </summary>
    [JsonPropertyName("message")]
    public required string Message { get; set; }

    /// <summary>
    /// Optional parameter associated with the error.
    /// </summary>
    [JsonPropertyName("param")]
    public string? Param { get; set; }

    /// <summary>
    /// The ID of the client event that triggered this error, if applicable.
    /// </summary>
    [JsonPropertyName("event_id")]
    public string? EventID { get; set; }
}
