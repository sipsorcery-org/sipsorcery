//-----------------------------------------------------------------------------
// Filename: CommandBase.cs
//
// Description: Base class for sipsorcery CLI verbs. Centralises the cross
// cutting conventions every verb must follow:
//
//  - Results go to stdout: human readable by default, a single JSON object
//    with --json. Logs and diagnostics always go to stderr so stdout stays
//    pipeable.
//  - The common options (--timeout/-t, --json, --verbose/-v) have the same
//    names, aliases and descriptions on every verb.
//  - Meaningful exit codes, see ExitCodes.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 12 Jun 2026	Aaron Clauson	Created, Wexford, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.Cli.Common;

public abstract class CommandBase
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    protected Option<int> TimeoutOption { get; }

    protected Option<bool> JsonOption { get; } = new("--json")
    {
        Description = "Write the result to stdout as a single JSON object instead of human readable text."
    };

    protected Option<bool> VerboseOption { get; } = new("--verbose", "-v")
    {
        Description = "Write diagnostic logs, including protocol traces, to stderr."
    };

    protected CommandBase(int defaultTimeoutSeconds)
    {
        TimeoutOption = new Option<int>("--timeout", "-t")
        {
            Description = "The number of seconds to wait for the operation to complete.",
            DefaultValueFactory = _ => defaultTimeoutSeconds
        };
    }

    /// <summary>
    /// Builds the System.CommandLine command for this verb.
    /// </summary>
    public abstract Command Build();

    /// <summary>
    /// Adds the options every verb supports. Call after adding the verb specific arguments and
    /// options so the common ones are listed last in help output.
    /// </summary>
    protected void AddCommonOptions(Command command)
    {
        command.Options.Add(TimeoutOption);
        command.Options.Add(JsonOption);
        command.Options.Add(VerboseOption);
    }

    /// <summary>
    /// Creates a console logger factory for the duration of a verb and wires it into the
    /// SIPSorcery library. All log levels are routed to stderr so stdout carries only the result.
    /// Verbose enables Trace, not Debug: the library logs one line summaries at Debug but the raw
    /// SIP/STUN messages at Trace (see SIPTransport.EnableTraceLogs).
    /// </summary>
    protected static ILoggerFactory InitLogging(bool verbose)
    {
        var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddConsole(opts => opts.LogToStandardErrorThreshold = LogLevel.Trace)
                   .SetMinimumLevel(verbose ? LogLevel.Trace : LogLevel.Warning));

        SIPSorcery.LogFactory.Set(loggerFactory);

        return loggerFactory;
    }

    /// <summary>
    /// Writes a verb's result object to stdout as JSON. Result records should use stable field
    /// names with additive changes only, since scripts and agents parse them.
    /// </summary>
    protected static void WriteJson<T>(T result) =>
        Console.WriteLine(SerializeResult(result));

    /// <summary>
    /// Serialises a result object with the standard JSON settings. For verbs whose stdout may be
    /// claimed by a media payload (--audio -), allowing the result to be written to stderr instead.
    /// </summary>
    protected static string SerializeResult<T>(T result) =>
        JsonSerializer.Serialize(result, _jsonOptions);
}
