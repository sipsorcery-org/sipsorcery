// https://platform.openai.com/docs/api-reference/realtime-client-events/conversation/item

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace demo;

public class OpenAIConversationItemCreate : OpenAIServerEventBase
{
    public const string TypeName = "conversation.item.create";

    [JsonPropertyName("type")]
    public override string Type => TypeName;

    [JsonPropertyName("previous_item_id")]
    public string? PreviousItemID { get; set; }

    [JsonPropertyName("item")]
    public OpenAIConversationItem? Item { get; set; }

    public override string ToJson()
    {
        return JsonSerializer.Serialize(this, JsonOptions.Default);
    }
}

public class OpenAIConversationItem
{
    [JsonPropertyName("id")]
    public string? ID { get; set; }

    [JsonPropertyName("type")]
    public OpenAIConversationConversationTypeEnum Type { get; set; }

    [JsonPropertyName("object")]
    public string? Object { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("content")]
    public List<OpenAIContentItem>? Content { get; set; }

    [JsonPropertyName("call_id")]
    public string? CallID { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("arguments")]
    public string? Arguments { get; set; }

    [JsonPropertyName("output")]
    public string? Output { get; set; }
}

public class OpenAIContentItem
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = default!;

    [JsonPropertyName("transcript")]
    public string Transcript { get; set; } = default!;

    [JsonPropertyName("audio")]
    public string Audio { get; set; } = default!;
}
