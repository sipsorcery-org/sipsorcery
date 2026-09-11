//-----------------------------------------------------------------------------
// Filename: LlmClient.cs
//
// Description: Minimal client for an OpenAI-compatible chat completions endpoint
// (Ollama / LM Studio / llama.cpp locally, or a hosted gateway such as OpenAI /
// OpenRouter with an API key). Turns a prompt into an in-character reply for the
// voice agent to speak. StreamReplyAsync yields the reply sentence-by-sentence as
// tokens arrive so the agent can start speaking the first sentence immediately -
// the latency that matters for a live conversation.
//
// Ported from the WebRTCMaxHeadroom example's LocalLlmClient; the system prompt
// (persona) is a constructor parameter here so the "bridge ... agent --persona"
// option can override it.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.Cli.Commands.Bridge;

public sealed class LlmClient
{
    public const string DEFAULT_PERSONA =
        "You are Max Headroom, the stuttering, wisecracking 1980s computer-generated TV host. " +
        "Reply in one or two short, punchy, slightly sarcastic sentences. Keep it light and witty. " +
        "Plain text only, no stage directions or emojis.";

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private readonly ILogger _logger;
    private readonly string? _endpoint;
    private readonly string _model;
    private readonly string _systemPrompt;

    public string? Endpoint => _endpoint;
    public string Model => _model;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_endpoint);

    public LlmClient(string? endpoint, string? model, string? apiKey, string? persona, ILogger logger)
    {
        _logger = logger;
        _endpoint = endpoint;
        _model = string.IsNullOrWhiteSpace(model) ? "llama3.2" : model!;
        _systemPrompt = string.IsNullOrWhiteSpace(persona) ? DEFAULT_PERSONA : persona!;

        // A key is only needed for hosted gateways (OpenRouter, OpenAI, ...). Local servers like
        // Ollama ignore it, so it is safe to leave unset for those.
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            _http.DefaultRequestHeaders.Add("HTTP-Referer", "https://sipsorcery.com");
            _http.DefaultRequestHeaders.Add("X-Title", "SIPSorcery CLI bridge agent");
        }
    }

    /// <summary>
    /// Streams the reply and yields it one sentence at a time as the tokens arrive. If the endpoint
    /// is not configured or the call fails before producing anything, the prompt itself is yielded so
    /// the agent still says something.
    /// </summary>
    public async IAsyncEnumerable<string> StreamReplyAsync(string prompt)
    {
        if (!IsConfigured)
        {
            yield return prompt;
            yield break;
        }

        var buffer = new StringBuilder();
        bool anyYielded = false;

        await foreach (var delta in ReadDeltasAsync(prompt).ConfigureAwait(false))
        {
            buffer.Append(delta);

            string? sentence;
            while ((sentence = TakeSentence(buffer)) != null)
            {
                if (sentence.Length == 0)
                {
                    continue;
                }
                anyYielded = true;
                yield return sentence;
            }
        }

        var remainder = buffer.ToString().Trim();
        if (remainder.Length > 0)
        {
            anyYielded = true;
            yield return remainder;
        }

        if (!anyYielded)
        {
            yield return prompt;
        }
    }

    private async IAsyncEnumerable<string> ReadDeltasAsync(string prompt)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = JsonContent.Create(new ChatRequest
            {
                Model = _model,
                Stream = true,
                Messages = new[]
                {
                    new ChatMessage { Role = "system", Content = _systemPrompt },
                    new ChatMessage { Role = "user", Content = prompt }
                }
            })
        };

        HttpResponseMessage? resp = null;
        Stream? netStream = null;
        Exception? setupError = null;

        try
        {
            resp = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            netStream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
        }
        catch (Exception excp)
        {
            setupError = excp;
        }

        if (setupError != null)
        {
            _logger.LogWarning(setupError, "LLM stream request failed, speaking the prompt verbatim.");
            resp?.Dispose();
            yield break;
        }

        using (resp)
        using (netStream)
        using (var reader = new StreamReader(netStream!))
        {
            while (true)
            {
                string? line = null;
                bool readFailed = false;

                try
                {
                    line = await reader.ReadLineAsync().ConfigureAwait(false);
                }
                catch (Exception excp)
                {
                    _logger.LogWarning(excp, "LLM stream read error, ending early.");
                    readFailed = true;
                }

                if (readFailed || line == null)
                {
                    break;
                }

                if (TryGetDelta(line, out var done, out var content))
                {
                    if (done)
                    {
                        break;
                    }
                    if (!string.IsNullOrEmpty(content))
                    {
                        yield return content!;
                    }
                }
            }
        }
    }

    private static bool TryGetDelta(string line, out bool done, out string? content)
    {
        done = false;
        content = null;

        if (string.IsNullOrEmpty(line) || !line.StartsWith("data:"))
        {
            return false;
        }

        var payload = line.Substring("data:".Length).Trim();
        if (payload.Length == 0)
        {
            return false;
        }
        if (payload == "[DONE]")
        {
            done = true;
            return true;
        }

        try
        {
            using var doc = JsonDocument.Parse(payload);
            var choices = doc.RootElement.GetProperty("choices");
            if (choices.GetArrayLength() == 0)
            {
                return false;
            }
            if (choices[0].TryGetProperty("delta", out var delta) &&
                delta.TryGetProperty("content", out var c) &&
                c.ValueKind == JsonValueKind.String)
            {
                content = c.GetString();
                return !string.IsNullOrEmpty(content);
            }
        }
        catch
        {
            // Ignore a malformed chunk and keep reading.
        }

        return false;
    }

    private static string? TakeSentence(StringBuilder buffer)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            char c = buffer[i];
            if (c == '.' || c == '!' || c == '?' || c == '\n')
            {
                int len = i + 1;
                var sentence = buffer.ToString(0, len).Trim();
                buffer.Remove(0, len);
                return sentence;
            }
        }

        return null;
    }

    private sealed class ChatRequest
    {
        [JsonPropertyName("model")] public string? Model { get; set; }
        [JsonPropertyName("messages")] public ChatMessage[]? Messages { get; set; }
        [JsonPropertyName("stream")] public bool Stream { get; set; }
    }

    private sealed class ChatMessage
    {
        [JsonPropertyName("role")] public string? Role { get; set; }
        [JsonPropertyName("content")] public string? Content { get; set; }
    }
}
