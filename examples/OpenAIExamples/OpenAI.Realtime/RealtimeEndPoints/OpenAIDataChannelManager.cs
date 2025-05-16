//-----------------------------------------------------------------------------
// Filename: OpenAIDataChannelManager.cs
//
// Description: Helper methods to manage communications with an OpenAI WebRTC
// peer connection
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 23 Jan 2025  Aaron Clauson   Created, Dublin, Ireland.
//
// License: 
// MIT.
//-----------------------------------------------------------------------------

using LanguageExt;
using System.Text.Json;
using System.Text;

namespace demo;

public class OpenAIDataChannelManager
{
    public static Option<OpenAIServerEventBase> ParseDataChannelMessage(byte[] data)
    {
        var message = Encoding.UTF8.GetString(data);

        //logger.LogDebug($"Data channel message: {message}");

        var serverEvent = JsonSerializer.Deserialize<OpenAIServerEventBase>(message, JsonOptions.Default);

        if (serverEvent != null)
        {
            //logger.LogInformation($"Server event ID {serverEvent.EventID} and type {serverEvent.Type}.");

            return serverEvent.Type switch
            {
                OpenAIConversationItemCreated.TypeName => JsonSerializer.Deserialize<OpenAIConversationItemCreated>(message, JsonOptions.Default),
                OpenAIInputAudioBufferCommitted.TypeName => JsonSerializer.Deserialize<OpenAIInputAudioBufferCommitted>(message, JsonOptions.Default),
                OpenAIInputAudioBufferSpeechStarted.TypeName => JsonSerializer.Deserialize<OpenAIInputAudioBufferSpeechStarted>(message, JsonOptions.Default),
                OpenAIInputAudioBufferSpeechStopped.TypeName => JsonSerializer.Deserialize<OpenAIInputAudioBufferSpeechStopped>(message, JsonOptions.Default),
                OpenAIOuputAudioBufferAudioStarted.TypeName => JsonSerializer.Deserialize<OpenAIOuputAudioBufferAudioStarted>(message, JsonOptions.Default),
                OpenAIOuputAudioBufferAudioStopped.TypeName => JsonSerializer.Deserialize<OpenAIOuputAudioBufferAudioStopped>(message, JsonOptions.Default),
                OpenAIRateLimitsUpdated.TypeName => JsonSerializer.Deserialize<OpenAIRateLimitsUpdated>(message, JsonOptions.Default),
                OpenAIResponseAudioDone.TypeName => JsonSerializer.Deserialize<OpenAIResponseAudioDone>(message, JsonOptions.Default),
                OpenAIResponseAudioTranscriptDelta.TypeName => JsonSerializer.Deserialize<OpenAIResponseAudioTranscriptDelta>(message, JsonOptions.Default),
                OpenAIResponseAudioTranscriptDone.TypeName => JsonSerializer.Deserialize<OpenAIResponseAudioTranscriptDone>(message, JsonOptions.Default),
                OpenAIResponseContentPartAdded.TypeName => JsonSerializer.Deserialize<OpenAIResponseContentPartAdded>(message, JsonOptions.Default),
                OpenAIResponseContentPartDone.TypeName => JsonSerializer.Deserialize<OpenAIResponseContentPartDone>(message, JsonOptions.Default),
                OpenAIResponseCreated.TypeName => JsonSerializer.Deserialize<OpenAIResponseCreated>(message, JsonOptions.Default),
                OpenAIResponseDone.TypeName => JsonSerializer.Deserialize<OpenAIResponseDone>(message, JsonOptions.Default),
                OpenAIResponseFunctionCallArgumentsDelta.TypeName => JsonSerializer.Deserialize<OpenAIResponseFunctionCallArgumentsDelta>(message, JsonOptions.Default),
                OpenAIResponseFunctionCallArgumentsDone.TypeName => JsonSerializer.Deserialize<OpenAIResponseFunctionCallArgumentsDone>(message, JsonOptions.Default),
                OpenAIResponseOutputItemAdded.TypeName => JsonSerializer.Deserialize<OpenAIResponseOutputItemAdded>(message, JsonOptions.Default),
                OpenAIResponseOutputItemDone.TypeName => JsonSerializer.Deserialize<OpenAIResponseOutputItemDone>(message, JsonOptions.Default),
                OpenAISessionCreated.TypeName => JsonSerializer.Deserialize<OpenAISessionCreated>(message, JsonOptions.Default),
                OpenAISessionUpdated.TypeName => JsonSerializer.Deserialize<OpenAISessionUpdated>(message, JsonOptions.Default),
                _ => Option<OpenAIServerEventBase>.None
            };
        }

        return Option<OpenAIServerEventBase>.None;
    }
}
