// https://platform.openai.com/docs/api-reference/realtime-server-events/conversation/item/created

using System.Text.Json;
using System.Text.Json.Serialization;

namespace demo;

public class OpenAIConversationItemCreated : OpenAIServerEventBase
{
    public const string TypeName = "conversation.item.created";

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

