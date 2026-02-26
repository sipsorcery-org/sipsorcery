using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace SIPSorcery.OpenAI.Realtime.Models;

public static class RealtimeEventTypes
{
    public static readonly ReadOnlyDictionary<string, Type> TypeMap = new(
        new Dictionary<string, Type>
        {
            // Realtime Client Events.
            [RealtimeClientEventConversationItemCreate.TypeName] = typeof(RealtimeClientEventConversationItemCreate),
            [RealtimeClientEventConversationItemDelete.TypeName] = typeof(RealtimeClientEventConversationItemDelete),
            [RealtimeClientEventConversationItemRetrieve.TypeName] = typeof(RealtimeClientEventConversationItemRetrieve),
            [RealtimeClientEventConversationItemTruncate.TypeName] = typeof(RealtimeClientEventConversationItemTruncate),
            [RealtimeClientEventInputAudioBufferAppend.TypeName] = typeof (RealtimeClientEventInputAudioBufferAppend),
            [RealtimeClientEventInputAudioBufferClear.TypeName] = typeof(RealtimeClientEventInputAudioBufferClear),
            [RealtimeClientEventInputAudioBufferCommit.TypeName] = typeof(RealtimeClientEventInputAudioBufferCommit),
            [RealtimeClientEventOutputAudioBufferClear.TypeName] = typeof(RealtimeClientEventOutputAudioBufferClear),
            [RealtimeClientEventResponseCancel.TypeName] = typeof(RealtimeClientEventResponseCancel),
            [RealtimeClientEventResponseCreate.TypeName] = typeof(RealtimeClientEventResponseCreate),
            [RealtimeClientEventSessionUpdate.TypeName] = typeof(RealtimeClientEventSessionUpdate),
            [RealtimeClientEventTranscriptionSessionUpdate.TypeName] = typeof(RealtimeClientEventTranscriptionSessionUpdate),

            // Realtime Server Events.
            [RealtimeServerEventConversationCreated.TypeName] = typeof(RealtimeServerEventConversationCreated),
            [RealtimeServerEventConversationItemCreated.TypeName] = typeof(RealtimeServerEventConversationItemCreated),
            [RealtimeServerEventConversationItemDeleted.TypeName] = typeof(RealtimeServerEventConversationItemDeleted),
            [RealtimeServerEventConversationItemInputAudioTranscriptionCompleted.TypeName] = typeof(RealtimeServerEventConversationItemInputAudioTranscriptionCompleted),
            [RealtimeServerEventConversationItemInputAudioTranscriptionDelta.TypeName] = typeof(RealtimeServerEventConversationItemInputAudioTranscriptionDelta),
            [RealtimeServerEventConversationItemInputAudioTranscriptionFailed.TypeName] = typeof(RealtimeServerEventConversationItemInputAudioTranscriptionFailed),
            [RealtimeServerEventConversationItemRetrieved.TypeName] = typeof(RealtimeServerEventConversationItemRetrieved),
            [RealtimeServerEventConversationItemTruncated.TypeName] = typeof(RealtimeServerEventConversationItemTruncated),
            [RealtimeServerEventError.TypeName] = typeof(RealtimeServerEventError),
            [RealtimeServerEventInputAudioBufferCleared.TypeName] = typeof(RealtimeServerEventInputAudioBufferCleared),
            [RealtimeServerEventInputAudioBufferCommitted.TypeName] = typeof(RealtimeServerEventInputAudioBufferCommitted),
            [RealtimeServerEventInputAudioBufferSpeechStarted.TypeName] = typeof(RealtimeServerEventInputAudioBufferSpeechStarted),
            [RealtimeServerEventInputAudioBufferSpeechStopped.TypeName] = typeof(RealtimeServerEventInputAudioBufferSpeechStopped),
            [RealtimeServerEventOutputAudioBufferCleared.TypeName] = typeof(RealtimeServerEventOutputAudioBufferCleared),
            [RealtimeServerEventOutputAudioBufferStarted.TypeName] = typeof(RealtimeServerEventOutputAudioBufferStarted),
            [RealtimeServerEventOutputAudioBufferStopped.TypeName] = typeof(RealtimeServerEventOutputAudioBufferStopped),
            [RealtimeServerEventRateLimitsUpdated.TypeName] = typeof(RealtimeServerEventRateLimitsUpdated),
            [RealtimeServerEventResponseAudioDelta.TypeName] = typeof(RealtimeServerEventResponseAudioDelta),
            [RealtimeServerEventResponseAudioDone.TypeName] = typeof(RealtimeServerEventResponseAudioDone),
            [RealtimeServerEventResponseAudioTranscriptDelta.TypeName] = typeof(RealtimeServerEventResponseAudioTranscriptDelta),
            [RealtimeServerEventResponseAudioTranscriptDone.TypeName] = typeof(RealtimeServerEventResponseAudioTranscriptDone),
            [RealtimeServerEventResponseContentPartAdded.TypeName] = typeof(RealtimeServerEventResponseContentPartAdded),
            [RealtimeServerEventResponseContentPartDone.TypeName] = typeof(RealtimeServerEventResponseContentPartDone),
            [RealtimeServerEventResponseCreated.TypeName] = typeof(RealtimeServerEventResponseCreated),
            [RealtimeServerEventResponseDone.TypeName] = typeof(RealtimeServerEventResponseDone),
            [RealtimeServerEventResponseFunctionCallArgumentsDelta.TypeName] = typeof(RealtimeServerEventResponseFunctionCallArgumentsDelta),
            [RealtimeServerEventResponseFunctionCallArgumentsDone.TypeName] = typeof(RealtimeServerEventResponseFunctionCallArgumentsDone),
            [RealtimeServerEventResponseOutputItemAdded.TypeName] = typeof(RealtimeServerEventResponseOutputItemAdded),
            [RealtimeServerEventResponseOutputItemDone.TypeName] = typeof(RealtimeServerEventResponseOutputItemDone),
            [RealtimeServerEventResponseTextDelta.TypeName] = typeof(RealtimeServerEventResponseTextDelta),
            [RealtimeServerEventResponseTextDone.TypeName] = typeof(RealtimeServerEventResponseTextDone),
            [RealtimeServerEventSessionCreated.TypeName] = typeof(RealtimeServerEventSessionCreated),
            [RealtimeServerEventSessionUpdated.TypeName] = typeof(RealtimeServerEventSessionUpdated),
            [RealtimeServerEventTranscriptionSessionUpdated.TypeName] = typeof (RealtimeServerEventTranscriptionSessionUpdated)
        });
}
