using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace SIPSorcery.Gemini.Realtime.Models;

public static class GeminiServerMessageTypes
{
    /// <summary>
    /// Maps each top-level JSON key of Gemini's BidiGenerateContentServerMessage <b>message-type
    /// union</b> to the type it deserialises to. <c>usageMetadata</c> is deliberately absent: it is
    /// a sibling field of that union, not a member, so it can accompany any of the messages below
    /// and is handled separately by <see cref="GeminiServerMessageConverter"/>.
    /// </summary>
    public static readonly ReadOnlyDictionary<string, Type> TypeMap = new(
        new Dictionary<string, Type>
        {
            [GeminiServerEventSetupComplete.JsonKey] = typeof(GeminiServerEventSetupComplete),
            [GeminiServerEventContent.JsonKey] = typeof(GeminiServerEventContent),
            [GeminiServerEventToolCall.JsonKey] = typeof(GeminiServerEventToolCall),
            [GeminiServerEventToolCallCancellation.JsonKey] = typeof(GeminiServerEventToolCallCancellation),
            [GeminiServerEventGoAway.JsonKey] = typeof(GeminiServerEventGoAway),
            [GeminiServerEventSessionResumptionUpdate.JsonKey] = typeof(GeminiServerEventSessionResumptionUpdate),
        });
}
