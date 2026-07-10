//-----------------------------------------------------------------------------
// Filename: LocalLlmClient.cs
//
// Description: Minimal client for an OpenAI-compatible chat completions endpoint.
// Works with a local server (Ollama, LM Studio, llama.cpp) or a hosted gateway
// such as OpenRouter / OpenAI - the only difference for a hosted service is an
// API key, supplied via the constructor and sent as a Bearer token. Used to turn
// a short prompt into a snappy, in-character Max Headroom reply that is then
// spoken by the avatar. Entirely optional - if no endpoint is configured the
// prompt text is spoken verbatim instead.
//
// GenerateReplyAsync returns the whole reply in one shot. StreamReplyAsync
// (stream=true) yields the reply sentence-by-sentence as the tokens arrive, so
// the caller can start speaking the first sentence without waiting for the
// entire completion - the latency that matters for a live talking avatar.
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

namespace demo;

public class LocalLlmClient : ILlmClient
{
    private readonly string _systemPrompt;

    private static readonly ILogger logger = SIPSorcery.LogFactory.CreateLogger<LocalLlmClient>();

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private readonly string _endpoint;
    private readonly string _model;

    public string Endpoint { get { return _endpoint; } }

    public string Model { get { return _model; } }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_endpoint);

    public string Description => $"endpoint {_endpoint} and model {_model}";

    public LocalLlmClient(string endpoint, string model, string apiKey = null, string systemPrompt = null)
    {
        _endpoint = endpoint;
        _model = string.IsNullOrWhiteSpace(model) ? "llama3.2" : model;
        _systemPrompt = string.IsNullOrWhiteSpace(systemPrompt) ? LlmShared.SystemPrompt : systemPrompt;

        // A key is only needed for hosted gateways (OpenRouter, OpenAI, ...). Local
        // servers like Ollama ignore it, so it is safe to leave unset for those.
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            // Optional OpenRouter attribution headers; harmless on other endpoints.
            _http.DefaultRequestHeaders.Add("HTTP-Referer", "https://sipsorcery.com");
            _http.DefaultRequestHeaders.Add("X-Title", "SIPSorcery Max Headroom Demo");
        }
    }

    /// <summary>
    /// Sends the prompt to the local LLM and returns an in-character reply. Falls
    /// back to echoing the prompt if the LLM is unavailable.
    /// </summary>
    public async Task<string> GenerateReplyAsync(string prompt)
    {
        if (!IsConfigured)
        {
            return prompt;
        }

        try
        {
            var request = new ChatRequest
            {
                Model = _model,
                Stream = false,
                Messages = new[]
                {
                    new ChatMessage { Role = "system", Content = _systemPrompt },
                    new ChatMessage { Role = "user", Content = prompt }
                }
            };

            using var resp = await _http.PostAsJsonAsync(_endpoint, request).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();

            var completion = await resp.Content.ReadFromJsonAsync<ChatResponse>().ConfigureAwait(false);
            var reply = completion?.Choices is { Length: > 0 } ? completion.Choices[0].Message?.Content : null;

            return string.IsNullOrWhiteSpace(reply) ? prompt : reply.Trim();
        }
        catch (Exception excp)
        {
            logger.LogWarning(excp, $"LLM call to {_endpoint} failed, speaking the prompt verbatim.");
            return prompt;
        }
    }

    /// <summary>
    /// Streams the reply and yields it one sentence at a time as the tokens arrive, so
    /// the caller can begin speaking immediately. If the endpoint is not configured or
    /// the call fails before producing anything, the prompt itself is yielded so the
    /// avatar still says something (mirrors <see cref="GenerateReplyAsync"/>).
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

            string sentence;
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

        // Flush any trailing text that did not end with punctuation.
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

    /// <summary>
    /// POSTs the streaming request and yields the raw content deltas from the
    /// OpenAI-style server-sent events. Network/parse errors are swallowed (logged) so
    /// the iterator simply ends, letting the caller fall back to the prompt.
    /// </summary>
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

        HttpResponseMessage resp = null;
        Stream netStream = null;
        Exception setupError = null;

        try
        {
            // ResponseHeadersRead so we get the body as it streams rather than buffering it all.
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
            logger.LogWarning(setupError, "LLM stream request failed, speaking the prompt verbatim.");
            resp?.Dispose();
            yield break;
        }

        using (resp)
        using (netStream)
        using (var reader = new StreamReader(netStream))
        {
            while (true)
            {
                string line = null;
                bool readFailed = false;

                try
                {
                    line = await reader.ReadLineAsync().ConfigureAwait(false);
                }
                catch (Exception excp)
                {
                    logger.LogWarning(excp, "LLM stream read error, ending early.");
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
                        yield return content;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Parses one SSE line of the form <c>data: {json}</c>. Sets <paramref name="done"/>
    /// for the terminating <c>data: [DONE]</c> sentinel and extracts
    /// <c>choices[0].delta.content</c> when present. Never throws; malformed or
    /// non-data lines simply return false.
    /// </summary>
    private static bool TryGetDelta(string line, out bool done, out string content)
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

    /// <summary>
    /// Removes and returns the first complete sentence (up to and including a '.', '!',
    /// '?' or newline) from <paramref name="buffer"/>. Returns null when there is no
    /// sentence terminator yet, or an empty string when the removed span was blank.
    /// </summary>
    private static string TakeSentence(StringBuilder buffer)
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

    private class ChatRequest
    {
        [JsonPropertyName("model")] public string Model { get; set; }
        [JsonPropertyName("messages")] public ChatMessage[] Messages { get; set; }
        [JsonPropertyName("stream")] public bool Stream { get; set; }
    }

    private class ChatMessage
    {
        [JsonPropertyName("role")] public string Role { get; set; }
        [JsonPropertyName("content")] public string Content { get; set; }
    }

    private class ChatResponse
    {
        [JsonPropertyName("choices")] public Choice[] Choices { get; set; }
    }

    private class Choice
    {
        [JsonPropertyName("message")] public ChatMessage Message { get; set; }
    }
}
