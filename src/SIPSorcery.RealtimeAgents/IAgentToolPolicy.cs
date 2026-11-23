//-----------------------------------------------------------------------------
// Filename: IAgentToolPolicy.cs
//
// Description: The gate between the model asking for a tool and the tool
// running. Tools are the host's (Microsoft.Extensions.AI AIFunctions: a
// softphone's transfer, hold, take-a-message); the policy is the host's too.
// The library guarantees every call passes it.
//
// It exists because the caller is talking to the model. "Transfer me to the
// CEO's mobile" is prompt injection with a phone attached: the policy is where
// the host limits transfer targets, refuses outbound calls, or asks the user
// first. A refused call is answered to the model with the reason, so the
// agent can tell the caller rather than retry.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 23 Nov 2026  Aaron Clauson   Created, moved into v1 from "v2 territory".
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SIPSorcery.RealtimeAgents;

/// <summary>A tool call the model asked for, as the policy sees it.</summary>
/// <param name="CallId">The model's id for the call.</param>
/// <param name="Name">The tool's name.</param>
/// <param name="Arguments">The arguments the model supplied.</param>
public sealed record AgentToolCall(string CallId, string Name, IReadOnlyDictionary<string, object?> Arguments);

/// <summary>A policy's answer to a tool call.</summary>
/// <param name="Allowed">Whether the tool runs.</param>
/// <param name="Reason">Why not, said to the model when refused.</param>
public sealed record ToolCallVerdict(bool Allowed, string? Reason = null)
{
    /// <summary>Run the tool.</summary>
    public static ToolCallVerdict Allow { get; } = new(true);

    /// <summary>Don't run it, and tell the model why.</summary>
    public static ToolCallVerdict Deny(string reason) => new(false, reason);
}

/// <summary>
/// Reviews each tool call before it runs. May take its time (e.g. ask the user
/// and wait); the session keeps the caller company meanwhile.
/// </summary>
public interface IAgentToolPolicy
{
    /// <summary>Decides whether <paramref name="call"/> runs.</summary>
    ValueTask<ToolCallVerdict> ReviewAsync(AgentToolCall call, CancellationToken cancellationToken = default);
}

/// <summary>Allows every call. For tests and trusted tools only.</summary>
public sealed class AllowAllToolPolicy : IAgentToolPolicy
{
    /// <inheritdoc/>
    public ValueTask<ToolCallVerdict> ReviewAsync(AgentToolCall call, CancellationToken cancellationToken = default) =>
        new(ToolCallVerdict.Allow);
}
