//-----------------------------------------------------------------------------
// Filename: IAgentSession.cs
//
// Description: What the host sees of an agent on a call: its state, its
// transcript and its tool activity to show, and the controls a host needs -
// say this, stop talking, pause while on hold, and leave the call so a person
// can take over (detach: the call carries on without the agent).
//
// The same surface whichever pipeline runs underneath (the cascade or a
// speech-to-speech engine) and whatever the transport (IAgentMediaPort).
// AgentSession, the implementation, is the next step; this cut fixes the
// surface it must provide.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 23 Nov 2026  Aaron Clauson   Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.RealtimeAgents;

/// <summary>What the agent is doing, for the host's UI.</summary>
public enum AgentState
{
    /// <summary>Created, not started.</summary>
    Idle,

    /// <summary>Waiting for the caller.</summary>
    Listening,

    /// <summary>The caller has finished; a reply is being worked out (or a tool is running).</summary>
    Thinking,

    /// <summary>Speaking to the caller.</summary>
    Speaking,

    /// <summary>Attached but neither listening nor speaking (the call is on hold).</summary>
    Paused,

    /// <summary>Off the call; the call may carry on. Final.</summary>
    Detached
}

/// <summary>Who said a transcript line.</summary>
public enum TranscriptSpeaker
{
    /// <summary>The caller, by voice or text.</summary>
    Caller,

    /// <summary>The agent.</summary>
    Agent
}

/// <summary>A line of the conversation.</summary>
/// <param name="Speaker">Who said it.</param>
/// <param name="Text">What was said (for the agent, what was actually spoken before any interruption).</param>
/// <param name="Interrupted">True when the agent was cut off (barge-in or <see cref="IAgentSession.Interrupt"/>).</param>
public sealed record TranscriptLine(TranscriptSpeaker Speaker, string Text, bool Interrupted = false);

/// <summary>A tool call and what became of it.</summary>
/// <param name="Call">What the model asked for.</param>
/// <param name="Verdict">The policy's answer; null while it is still deciding.</param>
/// <param name="Result">The tool's result, once it has run.</param>
/// <param name="Error">What went wrong, if the tool threw.</param>
public sealed record AgentToolActivity(AgentToolCall Call, ToolCallVerdict? Verdict, object? Result = null, string? Error = null);

/// <summary>
/// Configures a session. Give either the cascade (<see cref="Recognizer"/>,
/// <see cref="ChatClient"/> and <see cref="Synthesizer"/>) or a
/// <see cref="SpeechToSpeech"/> engine, not both.
/// </summary>
/// <remarks>
/// Sharing: <see cref="ChatClient"/> and <see cref="Synthesizer"/> are
/// stateless and can be shared by every session in the app (load models
/// once); <see cref="Recognizer"/>, <see cref="SpeechToSpeech"/> and the
/// avatar hold per-call state, so each session needs its own.
/// </remarks>
public sealed class AgentSessionOptions
{
    /// <summary>The persona and rules: the system prompt.</summary>
    public required string Instructions { get; init; }

    /// <summary>Speech-to-text, for the cascade.</summary>
    public ISpeechRecognizer? Recognizer { get; init; }

    /// <summary>The model, for the cascade. Any Microsoft.Extensions.AI chat client.</summary>
    public IChatClient? ChatClient { get; init; }

    /// <summary>Text-to-speech, for the cascade.</summary>
    public ISpeechSynthesizer? Synthesizer { get; init; }

    /// <summary>A speech-to-speech engine, instead of the cascade.</summary>
    public ISpeechToSpeechEngine? SpeechToSpeech { get; init; }

    /// <summary>The tools the model may call: the host's, e.g. transfer, hold, take a message.</summary>
    public IReadOnlyList<AIFunction> Tools { get; init; } = Array.Empty<AIFunction>();

    /// <summary>Reviews every tool call before it runs. Required when there are tools.</summary>
    public IAgentToolPolicy? ToolPolicy { get; init; }

    /// <summary>
    /// The face, if any: an <see cref="IAvatarRenderer"/> (the session puts its
    /// video on the port) or an engine-hosted <see cref="IAvatarMouth"/>.
    /// Null for a voice-only agent.
    /// </summary>
    public IAvatarMouth? Avatar { get; init; }

    /// <summary>Said as soon as the session starts (a greeting, an AI disclosure), before listening.</summary>
    public string? Greeting { get; init; }

    /// <summary>Optional logging.</summary>
    public ILoggerFactory? LoggerFactory { get; init; }
}

/// <summary>
/// An agent on one call. Events may be raised on any thread; a UI host should
/// marshal them.
/// </summary>
public interface IAgentSession : IAsyncDisposable
{
    /// <summary>What the agent is doing now.</summary>
    AgentState State { get; }

    /// <summary>The conversation so far.</summary>
    IReadOnlyList<TranscriptLine> Transcript { get; }

    /// <summary>Raised whenever <see cref="State"/> changes.</summary>
    event Action<AgentState>? OnStateChanged;

    /// <summary>Raised for each finished transcript line.</summary>
    event Action<TranscriptLine>? OnTranscript;

    /// <summary>Raised as a tool call is asked for, decided and finished.</summary>
    event Action<AgentToolActivity>? OnToolActivity;

    /// <summary>Raised if the session fails and detaches itself (an engine died), so the host can step in.</summary>
    event Action<Exception>? OnFailed;

    /// <summary>Attaches to <paramref name="port"/>, warms anything not yet warm, says the greeting and starts listening.</summary>
    Task StartAsync(IAgentMediaPort port, CancellationToken cancellationToken = default);

    /// <summary>
    /// Says <paramref name="text"/> as it is, interrupting nothing (it is
    /// queued after anything being said), and adds it to the transcript so
    /// the model knows it was said. For the host's own lines ("Aaron's just
    /// finishing a call").
    /// </summary>
    Task SayAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>Stops speaking now and goes back to listening. The cut-off line is marked <see cref="TranscriptLine.Interrupted"/>.</summary>
    void Interrupt();

    /// <summary>Stops listening and speaking while staying attached (the call is on hold).</summary>
    void Pause();

    /// <summary>Picks up from <see cref="Pause"/>.</summary>
    void Resume();

    /// <summary>
    /// Leaves the call: stops speaking, takes the avatar's video off and
    /// unhooks from the port, which the call keeps. For a person taking over,
    /// or the agent's work being done. Final.
    /// </summary>
    Task DetachAsync();
}
