using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

/// <summary>
/// Represents a conversation item, such as a message, function call, or function call output.
/// Used to populate or manipulate the conversation context.
/// </summary>
public class RealtimeConversationItem
{
    /// <summary>
    /// Unique ID of the item. Can be client-generated. If not provided, the server will assign one.
    /// </summary>
    [JsonPropertyName("id")]
    public string? ID { get; set; }

    /// <summary>
    /// The type of the conversation item (message, function call, or function call output).
    /// </summary>
    [JsonPropertyName("type")]
    public required RealtimeConversationItemTypeEnum Type { get; set; }

    /// <summary>
    /// Identifies the API object - always "realtime.item".
    /// </summary>
    [JsonPropertyName("object")]
    public string? Object { get; set; }

    /// <summary>
    /// Optional status of the item, used for consistency with other events (e.g., completed, incomplete).
    /// </summary>
    [JsonPropertyName("status")]
    public RealtimeStatusEnum? Status { get; set; }

    /// <summary>
    /// The role of the sender: user, assistant, or system (only applicable to message items).
    /// </summary>
    [JsonPropertyName("role")]
    public RealtimeConversationRoleEnum? Role { get; set; }

    /// <summary>
    /// Content of the item. Required for message items. Content types depend on the sender role.
    /// </summary>
    [JsonPropertyName("content")]
    public List<RealtimeConversationContent>? Content { get; set; }

    /// <summary>
    /// ID of the function call, used for function_call and function_call_output items.
    /// </summary>
    [JsonPropertyName("call_id")]
    public string? CallID { get; set; }

    /// <summary>
    /// Name of the function being called (for function_call items).
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// JSON stringified arguments for the function call (for function_call items).
    /// </summary>
    [JsonPropertyName("arguments")]
    public string? Arguments { get; set; }

    /// <summary>
    /// JSON stringified output of the function call (for function_call_output items).
    /// </summary>
    [JsonPropertyName("output")]
    public string? Output { get; set; }
}