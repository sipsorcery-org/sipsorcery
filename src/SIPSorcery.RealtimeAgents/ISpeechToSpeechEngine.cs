//-----------------------------------------------------------------------------
// Filename: ISpeechToSpeechEngine.cs
//
// Description: The other pipeline shape. A speech-to-speech model (a
// "realtime" model) takes caller audio and returns agent audio directly,
// doing the work of ISpeechRecognizer, the chat client and
// ISpeechSynthesizer in one engine, with lower latency than the cascade.
//
// A session runs either the cascade or one of these, behind the same
// IAgentSession surface. The avatar is unaffected: the engine's audio goes to
// IAvatarMouth like synthesised speech does. Tools are the same AIFunctions,
// and calls still pass the session's IAgentToolPolicy.
//
// No implementation lives in this package (the OpenAI package became a demo
// in #1912); providers ship as engine packages.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 23 Nov 2026  Aaron Clauson   Created, moved into v1 from "not in this cut".
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace SIPSorcery.RealtimeAgents;

/// <summary>
/// A speech-to-speech conversation engine. Stateful: one instance per
/// session. Events may be raised on any thread.
/// </summary>
public interface ISpeechToSpeechEngine : IAsyncDisposable
{
    /// <summary>The sample rate the engine wants caller audio at; the session resamples to it.</summary>
    int InputSampleRateHz { get; }

    /// <summary>Raised when the caller starts speaking (the engine's own voice detection): barge-in.</summary>
    event Action? OnSpeechStarted;

    /// <summary>Raised with the final text of what the caller said, for the transcript.</summary>
    event Action<string>? OnUserTranscript;

    /// <summary>Raised as the agent starts a spoken response.</summary>
    event Action? OnResponseStarted;

    /// <summary>Raised with each chunk of the agent's speech audio, as fast as the engine produces it (the session paces playout).</summary>
    event Action<AudioSegment>? OnAudio;

    /// <summary>Raised with the text of what the agent said, once the response is done (or cut short).</summary>
    event Action<string>? OnAgentTranscript;

    /// <summary>Raised when a response is complete, including after <see cref="CancelResponse"/>.</summary>
    event Action? OnResponseDone;

    /// <summary>
    /// Raised when the model calls a tool. The session reviews it with its
    /// policy, runs it (or not) and answers with <see cref="SendToolResultAsync"/>.
    /// </summary>
    event Action<FunctionCallContent>? OnToolCall;

    /// <summary>Opens the conversation with the persona and the tools the model may call.</summary>
    Task StartAsync(string instructions, IReadOnlyList<AITool> tools, CancellationToken cancellationToken = default);

    /// <summary>Pushes caller audio at <see cref="InputSampleRateHz"/>. A synchronous, non-blocking hand-off.</summary>
    void WriteAudio(ReadOnlySpan<short> pcm16);

    /// <summary>
    /// Adds text to the conversation: a line the caller typed (as the user),
    /// or something the host wants said (as an instruction). When
    /// <paramref name="respond"/> is true the model answers it.
    /// </summary>
    Task SendTextAsync(string text, ChatRole role, bool respond, CancellationToken cancellationToken = default);

    /// <summary>Answers a tool call, so the model can carry on.</summary>
    Task SendToolResultAsync(FunctionResultContent result, CancellationToken cancellationToken = default);

    /// <summary>Stops the current response at once (barge-in, Interrupt). Safe to call with none in progress.</summary>
    void CancelResponse();
}
