//-----------------------------------------------------------------------------
// Filename: BridgeFactory.cs
//
// Description: Maps a two-way route endpoint spec ("web", "term", "agent") to a
// participant, the single place duplex endpoints are wired (the two-way counterpart
// to EdgeFactory, which builds the one-way sources and sinks).
// Adding a new endpoint (a sip: peer, openai, livekit:) is a new case here.
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
using Microsoft.Extensions.Logging;
using SIPSorcery.Cli.Commands.Route;
using SIPSorcery.Cli.Common;
using SIPSorcery.OpenAI.Realtime.Models;
using SIPSorcery.SIP;

namespace SIPSorcery.Cli.Commands.Bridge;

/// <summary>The knobs the bridge endpoints need from the verb.</summary>
public sealed record BridgeOptions(
    int Port = 8080,
    bool OpenBrowser = false,
    string? AzureKey = null,
    string? AzureRegion = null,
    string? Voice = null,
    string? Persona = null,
    string? Llm = null,
    string? LlmModel = null,
    string? LlmApiKey = null,
    string? Greeting = null,
    string? Avatar = null,
    string? SipUsername = null,
    string? SipPassword = null,
    int RingTimeoutSeconds = 30,
    string? MicDevice = null);

public static class BridgeFactory
{
    public static IBridgeParticipant CreateParticipant(string spec, BridgeOptions options, ILoggerFactory loggerFactory, ILogger logger)
    {
        string trimmed = spec.Trim();

        // A sip:/sips: scheme, or a bare user@host, is a SIP call (SipDestination handles both forms).
        if (trimmed.StartsWith("sip:", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("sips:", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains('@'))
        {
            return CreateSip(trimmed, options, logger);
        }

        string lowered = trimmed.ToLowerInvariant();

        // "web:9000" picks the page's port, as for the one-way web sink.
        if (lowered.StartsWith("web:", StringComparison.Ordinal))
        {
            if (!int.TryParse(lowered[4..], out int port) || port < 1 || port > 65535)
            {
                throw new EdgeException($"The web endpoint port '{trimmed[4..]}' is not a valid port number (1-65535), e.g. web:8080.");
            }
            return new WebParticipant(port, options.OpenBrowser, options.Avatar != null, logger);
        }

        switch (lowered)
        {
            case "web":
                // The page offers a video track only when an avatar is in play (the agent's face).
                return new WebParticipant(options.Port, options.OpenBrowser, options.Avatar != null, logger);

            case "agent":
                return CreateAgent(options, logger);

            case "term":
                // The local terminal: microphone in via ffmpeg, the other side out as Matroska on stdout. It
                // carries video only when the other side has an avatar.
                return new TerminalParticipant(options.MicDevice, options.Avatar != null, logger);

            case "openai":
                return CreateOpenAi(options, loggerFactory, logger);

            case "livekit":
                throw new EdgeException(
                    $"'{spec}' is not wired into two-way routes yet (two-way endpoints: web, term, agent, openai, sip:<uri>). " +
                    "Use it one-way (without --duplex) for now.");

            default:
                throw new EdgeException(
                    $"'{spec}' cannot be used in a two-way route: it does not both send and receive. " +
                    "Two-way endpoints: web, term, agent, openai, sip:<uri>. Leave out --duplex to route it one way.");
        }
    }

    private static IBridgeParticipant CreateAgent(BridgeOptions options, ILogger logger)
    {
        string? azureKey = options.AzureKey ?? Environment.GetEnvironmentVariable("AZURE_SPEECH_KEY");
        string? azureRegion = options.AzureRegion ?? Environment.GetEnvironmentVariable("AZURE_SPEECH_REGION");
        string voice = options.Voice ?? Environment.GetEnvironmentVariable("AZURE_SPEECH_VOICE") ?? "en-US-GuyNeural";

        if (string.IsNullOrWhiteSpace(azureKey) || string.IsNullOrWhiteSpace(azureRegion))
        {
            throw new EdgeException(
                "The agent needs an Azure Speech key and region (--azure-key/--azure-region or " +
                "AZURE_SPEECH_KEY/AZURE_SPEECH_REGION) for its speech-to-text and text-to-speech.");
        }

        string? llm = options.Llm ?? Environment.GetEnvironmentVariable("LLM_ENDPOINT");
        string? llmModel = options.LlmModel ?? Environment.GetEnvironmentVariable("LLM_MODEL");
        string? llmApiKey = options.LlmApiKey ?? Environment.GetEnvironmentVariable("LLM_API_KEY");

        // The avatar name (wav2lip, max, or "" for the default) is checked by AgentAvatar.
        return new AzureAgentParticipant(azureKey!, azureRegion!, voice, options.Persona,
            llm, llmModel, llmApiKey, options.Greeting, options.Avatar, logger);
    }

    private static IBridgeParticipant CreateOpenAi(BridgeOptions options, ILoggerFactory loggerFactory, ILogger logger)
    {
        // The OpenAI Realtime key. --api-key is shared with the agent's LLM gateway key; for openai it is
        // the OpenAI key, else the OPENAI_API_KEY environment variable.
        string? apiKey = options.LlmApiKey ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new EdgeException(
                "The openai endpoint needs an OpenAI API key (--api-key or the OPENAI_API_KEY environment variable).");
        }

        // OpenAI uses its own named voices (marin, alloy, ...), not Azure neural voice names.
        RealtimeVoicesEnum voice = RealtimeVoicesEnum.marin;
        if (!string.IsNullOrWhiteSpace(options.Voice) &&
            !Enum.TryParse(options.Voice, ignoreCase: true, out voice))
        {
            throw new EdgeException(
                $"Unknown OpenAI voice '{options.Voice}'. Choose one of: {string.Join(", ", Enum.GetNames<RealtimeVoicesEnum>())}.");
        }

        // The avatar name (wav2lip, max, or "" for the default) is checked by AgentAvatar.
        return new OpenAiAgentParticipant(apiKey!, voice, options.Persona, options.Greeting, options.Avatar, loggerFactory, logger);
    }

    private static IBridgeParticipant CreateSip(string spec, BridgeOptions options, ILogger logger)
    {
        // Reject a bare scheme with no destination before the lenient parser accepts it as an empty host.
        string rest = spec.Contains(':') ? spec[(spec.IndexOf(':') + 1)..] : spec;
        if (string.IsNullOrWhiteSpace(rest))
        {
            throw new EdgeException("The sip endpoint needs a destination, e.g. route --from sip:alice@example.com --to agent.");
        }

        // SipDestination accepts both "sip:"/"sips:" and a bare user@host, so pass the original spec.
        if (!SipDestination.TryParse(spec, out SIPURI uri, out string? error))
        {
            throw new EdgeException(error!);
        }

        // An avatar makes the call a video call: offer a send-only video m-line for the agent's face, the
        // same way --avatar makes the web peer offer a video track. A phone with no video just declines it.
        bool video = options.Avatar != null;

        return new SipBridgeParticipant(uri, options.SipUsername, options.SipPassword, options.RingTimeoutSeconds, video, logger);
    }
}
