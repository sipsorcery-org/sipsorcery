//-----------------------------------------------------------------------------
// Filename: LocalLlmClient.cs
//
// Description: Minimal client for a local OpenAI-compatible chat completions
// endpoint (Ollama, LM Studio, llama.cpp server, etc.). Used to turn a short
// prompt into a snappy, in-character Max Headroom reply that is then spoken by
// the avatar. Entirely optional - if no endpoint is configured the prompt text
// is spoken verbatim instead.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace demo
{
    public class LocalLlmClient
    {
        private const string SYSTEM_PROMPT =
            "You are Max Headroom, the stuttering, wisecracking 1980s computer-generated TV host. " +
            "Reply in one or two short, punchy, slightly sarcastic sentences. Keep it light and witty. " +
            "Plain text only, no stage directions or emojis.";

        private static readonly ILogger logger = SIPSorcery.LogFactory.CreateLogger<LocalLlmClient>();

        private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };
        private readonly string _endpoint;
        private readonly string _model;

        public bool IsConfigured => !string.IsNullOrWhiteSpace(_endpoint);

        public LocalLlmClient(string endpoint, string model)
        {
            _endpoint = endpoint;
            _model = string.IsNullOrWhiteSpace(model) ? "llama3.2" : model;
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
                        new ChatMessage { Role = "system", Content = SYSTEM_PROMPT },
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
                logger.LogWarning(excp, "Local LLM call failed, speaking the prompt verbatim.");
                return prompt;
            }
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
}
