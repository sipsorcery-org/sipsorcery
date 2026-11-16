using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SIPSorcery.OpenAI.Realtime.Models;

/// <summary>
/// Represents a conversation item that includes references to other items (e.g. for summarization or context reuse).
/// </summary>
public class RealtimeConversationItemWithReference
{
    /// <summary>
    /// The ID of this item. 
    /// - For `message`, `function_call`, and `function_call_output` types: optional (server will generate one if omitted).
    /// - For `item_reference` content: required and must refer to a valid, existing item.
    /// </summary>
    [JsonPropertyName("id")]
    public string? ID { get; set; }

    /// <summary>
    /// The type of the item (e.g., message, function_call, function_call_output).
    /// </summary>
    [JsonPropertyName("type")]
    public required RealtimeConversationItemTypeEnum ItemType { get; set; }

    /// <summary>
    /// Always "realtime.item". Identifies the type of API object being represented.
    /// </summary>
    [JsonPropertyName("object")]
    public string Object { get; set; } = "realtime.item";

    /// <summary>
    /// Optional status of the item (completed or incomplete). Used for consistency but does not affect behavior.
    /// </summary>
    [JsonPropertyName("status")]
    public RealtimeStatusEnum? Status { get; set; }

    /// <summary>
    /// Role of the message sender (user, assistant, or system). Only applies to `message` items.
    /// </summary>
    [JsonPropertyName("role")]
    public RealtimeConversationRoleEnum? Role { get; set; }

    /// <summary>
    /// The content of the item. Required for message items. Content types vary depending on the role.
    /// </summary>
    [JsonPropertyName("content")]
    public List<RealtimeConversationContent>? Content { get; set; }

    /// <summary>
    /// ID of the function call (used for both function_call and function_call_output items).
    /// </summary>
    [JsonPropertyName("call_id")]
    public string? CallID { get; set; }

    /// <summary>
    /// Name of the function being called (applicable to function_call items).
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Arguments to the function call in JSON string format (function_call only).
    /// </summary>
    [JsonPropertyName("arguments")]
    public string? Arguments { get; set; }

    /// <summary>
    /// The output of the function call in JSON string format (function_call_output only).
    /// </summary>
    [JsonPropertyName("output")]
    public string? Output { get; set; }
}