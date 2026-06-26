//-----------------------------------------------------------------------------
// Filename: BridgeCommand.cs
//
// Description: The "bridge" verb: connect two duplex endpoints both ways. Where
// "route --from/--to" is directional (half duplex), "bridge <a> <b>" is symmetric
// (full duplex) - the two endpoints are order-agnostic and media flows in both
// directions. v0.1 endpoints: web (a browser mic+speaker page) and agent (an Azure
// speech-to-text -> LLM -> Azure text-to-speech voice agent), so:
//
//   bridge web agent --open      # open a browser and talk to the agent (e.g. Max Headroom)
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 26 Jun 2026	Aaron Clauson	Created, Wexford, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.CommandLine;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using SIPSorcery.Cli.Commands.Bridge;
using SIPSorcery.Cli.Commands.Route;

namespace SIPSorcery.Cli.Commands;

public sealed class BridgeCommand : CommandBase
{
    private const int DEFAULT_TIMEOUT_SECONDS = 30;
    private const int DEFAULT_DURATION_SECONDS = 0;     // 0 = run until a participant ends or ctrl-c.
    private const int DEFAULT_PORT = 8080;

    /// <summary>The result shape written with --json. Stable field names; additive changes only.</summary>
    private sealed record BridgeResult(
        bool Success,
        string A,
        string B,
        string Stopped,
        long AToBFrames,
        long BToAFrames,
        int RunMs,
        string? Error);

    public BridgeCommand() : base(DEFAULT_TIMEOUT_SECONDS)
    { }

    public override Command Build()
    {
        var aArg = new Argument<string>("a") { Description = "The first endpoint: web, agent, openai or sip:<uri>." };
        var bArg = new Argument<string>("b") { Description = "The second endpoint: web, agent, openai or sip:<uri>." };

        var portOption = new Option<int>("--port")
        {
            Description = "Port for the web endpoint's local page.",
            DefaultValueFactory = _ => DEFAULT_PORT
        };
        var openOption = new Option<bool>("--open")
        {
            Description = "Open the web endpoint's page in a browser automatically."
        };
        var voiceOption = new Option<string?>("--voice")
        {
            Description = "Voice for the agent. agent: an Azure neural TTS voice (default en-US-GuyNeural; must be " +
                          "neural for visemes). openai: an OpenAI Realtime voice e.g. marin, alloy, verse (default marin)."
        };
        var personaOption = new Option<string?>("--persona", "--system")
        {
            Description = "System prompt giving the agent its character (default: the Max Headroom persona)."
        };
        var greetingOption = new Option<string?>("--greeting")
        {
            Description = "What the agent says when the far side connects (default: a Max Headroom greeting)."
        };
        var llmOption = new Option<string?>("--llm")
        {
            Description = "OpenAI-compatible chat endpoint for the agent (Ollama/LM Studio/OpenAI/OpenRouter). " +
                          "Without it the agent just repeats what it heard."
        };
        var llmModelOption = new Option<string?>("--llm-model")
        {
            Description = "Model for --llm (default llama3.2)."
        };
        var apiKeyOption = new Option<string?>("--api-key")
        {
            Description = "API key: the OpenAI key for the openai endpoint (else OPENAI_API_KEY), or the bearer key " +
                          "for a hosted agent --llm gateway (OpenRouter/OpenAI). Local servers ignore it."
        };
        var azureKeyOption = new Option<string?>("--azure-key")
        {
            Description = "Azure Speech resource key for the agent (else AZURE_SPEECH_KEY)."
        };
        var azureRegionOption = new Option<string?>("--azure-region")
        {
            Description = "Azure Speech resource region for the agent, e.g. westeurope (else AZURE_SPEECH_REGION)."
        };
        var avatarOption = new Option<bool>("--avatar")
        {
            Description = "Show a lip-synced video face (the Max Headroom avatar) on the agent in the browser."
        };
        var userOption = new Option<string?>("--user", "-u")
        {
            Description = "Username to authenticate a sip: endpoint's call (if the destination challenges)."
        };
        var passwordOption = new Option<string?>("--password")
        {
            Description = "Password for the sip: endpoint's --user."
        };
        var durationOption = new Option<int>("--duration", "-d")
        {
            Description = "Seconds to run for. 0 (default) runs until a participant leaves or ctrl-c.",
            DefaultValueFactory = _ => DEFAULT_DURATION_SECONDS
        };

        var command = new Command("bridge",
            "Connect two duplex endpoints both ways (full duplex), e.g. talk to a voice agent: bridge web agent.");
        command.Arguments.Add(aArg);
        command.Arguments.Add(bArg);
        command.Options.Add(portOption);
        command.Options.Add(openOption);
        command.Options.Add(voiceOption);
        command.Options.Add(personaOption);
        command.Options.Add(greetingOption);
        command.Options.Add(llmOption);
        command.Options.Add(llmModelOption);
        command.Options.Add(apiKeyOption);
        command.Options.Add(azureKeyOption);
        command.Options.Add(azureRegionOption);
        command.Options.Add(avatarOption);
        command.Options.Add(userOption);
        command.Options.Add(passwordOption);
        command.Options.Add(durationOption);
        AddCommonOptions(command);

        command.SetAction((parseResult, cancellationToken) => RunAsync(
            parseResult.GetValue(aArg)!,
            parseResult.GetValue(bArg)!,
            new BridgeOptions(
                parseResult.GetValue(portOption),
                parseResult.GetValue(openOption),
                parseResult.GetValue(azureKeyOption),
                parseResult.GetValue(azureRegionOption),
                parseResult.GetValue(voiceOption),
                parseResult.GetValue(personaOption),
                parseResult.GetValue(llmOption),
                parseResult.GetValue(llmModelOption),
                parseResult.GetValue(apiKeyOption),
                parseResult.GetValue(greetingOption),
                parseResult.GetValue(avatarOption) ? "max" : null,
                parseResult.GetValue(userOption),
                parseResult.GetValue(passwordOption),
                parseResult.GetValue(TimeoutOption)),
            parseResult.GetValue(durationOption),
            parseResult.GetValue(JsonOption),
            parseResult.GetValue(VerboseOption),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(string a, string b, BridgeOptions options, int durationSeconds,
        bool asJson, bool verbose, CancellationToken ct)
    {
        using var loggerFactory = InitLogging(verbose);
        var logger = loggerFactory.CreateLogger(nameof(BridgeCommand));

        // Build both endpoints. A bad spec / missing creds is an argument error before anything starts.
        IBridgeParticipant participantA;
        IBridgeParticipant participantB;
        try
        {
            participantA = BridgeFactory.CreateParticipant(a, options, loggerFactory, logger);
        }
        catch (EdgeException ex)
        {
            return WriteResult(asJson, new BridgeResult(false, a, b, "invalid endpoint", 0, 0, 0, ex.Message), ExitCodes.InvalidArgument);
        }

        try
        {
            participantB = BridgeFactory.CreateParticipant(b, options, loggerFactory, logger);
        }
        catch (EdgeException ex)
        {
            await participantA.DisposeAsync().ConfigureAwait(false);
            return WriteResult(asJson, new BridgeResult(false, a, b, "invalid endpoint", 0, 0, 0, ex.Message), ExitCodes.InvalidArgument);
        }

        // Cue the agent's greeting when the web peer connects (the only control wire the bridge needs).
        WireGreeting(participantA, participantB);

        var graph = new BridgeGraph(participantA, participantB);

        string stopped;
        string? error = null;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            stopped = await graph.RunAsync(durationSeconds, ct).ConfigureAwait(false);
        }
        catch (EdgeException ex)
        {
            error = ex.Message;
            stopped = "failed to start";
        }
        catch (OperationCanceledException)
        {
            stopped = "cancelled";
        }
        catch (Exception ex)
        {
            // A participant failed to start or run (e.g. Azure unreachable, the web port already in use).
            error = ex.Message;
            stopped = "failed";
        }
        stopwatch.Stop();

        await participantA.DisposeAsync().ConfigureAwait(false);
        await participantB.DisposeAsync().ConfigureAwait(false);

        bool success = error == null;
        int exitCode = success ? ExitCodes.Ok : ExitCodes.Failed;

        return WriteResult(asJson,
            new BridgeResult(success, participantA.Describe(), participantB.Describe(), stopped,
                graph.AToBFrames, graph.BToAFrames, (int)stopwatch.ElapsedMilliseconds, error),
            exitCode);
    }

    private static void WireGreeting(IBridgeParticipant a, IBridgeParticipant b)
    {
        // The agent greets when the far side first connects (a browser opening the page, a SIP call
        // answered). The connectable is whichever side announces a connection; the greeter is the agent.
        var greeter = a as IGreetable ?? b as IGreetable;
        var connectable = (a is IGreetable ? null : a as IConnectable) ?? (b is IGreetable ? null : b as IConnectable);
        if (greeter != null && connectable != null)
        {
            connectable.Connected += greeter.Greet;
        }
    }

    private static int WriteResult(bool asJson, BridgeResult result, int exitCode)
    {
        if (asJson)
        {
            WriteJson(result);
        }
        else if (result.Success)
        {
            Console.WriteLine($"Bridged {result.A} <-> {result.B} ({result.Stopped} after {result.RunMs}ms). " +
                $"{result.AToBFrames} frames a->b, {result.BToAFrames} frames b->a.");
        }
        else
        {
            Console.Error.WriteLine($"Bridge {result.A} <-> {result.B} failed ({result.Stopped}): {result.Error}");
        }

        return exitCode;
    }
}
