//-----------------------------------------------------------------------------
// Filename: LlamaSharpLlmClient.cs
//
// Description: IN-PROCESS reply generation using LLamaSharp (llama.cpp bindings,
// https://github.com/SciSharp/LLamaSharp). Runs the same GGUF models Ollama serves,
// but inside this process - no external LLM server to install, start or monitor.
// Point LLM_GGUF at a .gguf chat model (e.g. Llama-3.2-3B-Instruct-Q4_K_M.gguf).
//
// The model's own chat template (from the GGUF metadata) is applied via LLamaTemplate,
// so the system persona and user prompt are formatted exactly as the model expects.
// Inference runs on the CPU by default; set LLM_GPU_LAYERS (and add a GPU backend
// package such as LLamaSharp.Backend.Vulkan) to offload layers to the GPU.
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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LLama;
using LLama.Common;
using LLama.Native;
using LLama.Sampling;
using Microsoft.Extensions.Logging;

namespace demo;

public sealed class LlamaSharpLlmClient : ILlmClient, IDisposable
{
    private static readonly ILogger logger = SIPSorcery.LogFactory.CreateLogger<LlamaSharpLlmClient>();

    private readonly LLamaWeights _weights;
    private readonly StatelessExecutor _executor;
    private readonly string _modelPath;
    private readonly string _systemPrompt;

    // A StatelessExecutor recycles one llama_context across calls, so CONCURRENT InferAsync
    // calls corrupt each other's batch state and die in a native GGML_ASSERT (seen when the
    // startup warm-up overlapped the first user question). Serialise every inference.
    private readonly SemaphoreSlim _inferLock = new(1, 1);

    static LlamaSharpLlmClient()
    {
        // llama.cpp writes its own very chatty log (KV cache layout, graph reserves, model
        // metadata dumps, ...) straight to stdout, bypassing the app's logging. Intercept it:
        // keep errors/warnings, drop the informational spam. Must be set before the first
        // native call, hence the static constructor.
        NativeLogConfig.llama_log_set((level, message) =>
        {
            var text = message?.TrimEnd('\n');
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }
            if (level == LLamaLogLevel.Error)
            {
                logger.LogWarning("llama.cpp: {Message}", text);
            }
            else if (level == LLamaLogLevel.Warning)
            {
                logger.LogDebug("llama.cpp: {Message}", text);
            }
            // Info/Debug/Continue: dropped.
        });
    }

    public bool IsConfigured => true;   // constructed only when a model file exists.

    public string Description => $"in-process LLamaSharp with model {Path.GetFileName(_modelPath)}";

    /// <param name="systemPrompt">Persona/system prompt; defaults to <see cref="LlmShared.SystemPrompt"/>.</param>
    public LlamaSharpLlmClient(string ggufPath, int gpuLayers = 0, string systemPrompt = null)
    {
        _modelPath = ggufPath;
        _systemPrompt = string.IsNullOrWhiteSpace(systemPrompt) ? LlmShared.SystemPrompt : systemPrompt;

        var parameters = new ModelParams(ggufPath)
        {
            ContextSize = 4096,          // plenty for a one-liner persona; keeps memory modest.
            GpuLayerCount = gpuLayers,   // 0 = CPU; needs a GPU backend package to matter.
        };

        _weights = LLamaWeights.LoadFromFile(parameters);
        _executor = new StatelessExecutor(_weights, parameters);

        logger.LogInformation("LLamaSharp loaded {Model} ({GpuLayers} GPU layers).",
            Path.GetFileName(ggufPath), gpuLayers);
    }

    /// <summary>
    /// Runs a single-token inference to page the memory-mapped weights in from disk and set
    /// up llama.cpp's allocations - otherwise those costs (~10s for a ~2GB model on a cold
    /// file cache) land on the user's first question.
    /// </summary>
    public async Task WarmUpAsync()
    {
        await _inferLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var warmParams = new InferenceParams { MaxTokens = 1 };
            await foreach (var _ in _executor.InferAsync("Hi", warmParams).ConfigureAwait(false)) { }
            logger.LogInformation("LLamaSharp warmed up in {Ms} ms.", sw.ElapsedMilliseconds);
        }
        catch (Exception excp)
        {
            logger.LogWarning(excp, "LLamaSharp warm-up failed (first reply will be slow).");
        }
        finally
        {
            _inferLock.Release();
        }
    }

    public async Task<string> GenerateReplyAsync(string prompt)
    {
        await _inferLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var sb = new StringBuilder();
            await foreach (var token in InferAsync(prompt).ConfigureAwait(false))
            {
                sb.Append(token);
            }
            var reply = sb.ToString().Trim();
            return reply.Length > 0 ? reply : prompt;
        }
        catch (Exception excp)
        {
            logger.LogWarning(excp, "LLamaSharp inference failed, speaking the prompt verbatim.");
            return prompt;
        }
        finally
        {
            _inferLock.Release();
        }
    }

    public async IAsyncEnumerable<string> StreamReplyAsync(string prompt)
    {
        var buffer = new StringBuilder();
        bool anyYielded = false;

        // Held for the whole enumeration: the underlying context must not be shared.
        await _inferLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await foreach (var token in InferAsync(prompt).ConfigureAwait(false))
            {
                buffer.Append(token);

                string sentence;
                while ((sentence = LlmShared.TakeSentence(buffer)) != null)
                {
                    if (sentence.Length == 0)
                    {
                        continue;
                    }
                    anyYielded = true;
                    yield return sentence;
                }
            }
        }
        finally
        {
            _inferLock.Release();
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

    /// <summary>Formats the persona + prompt with the model's own chat template and streams tokens.</summary>
    private IAsyncEnumerable<string> InferAsync(string prompt)
    {
        var template = new LLamaTemplate(_weights);
        template.Add("system", _systemPrompt);
        template.Add("user", prompt);
        template.AddAssistant = true;
        var templated = Encoding.UTF8.GetString(template.Apply());

        var inferenceParams = new InferenceParams
        {
            MaxTokens = 120,   // one or two punchy sentences.
            SamplingPipeline = new DefaultSamplingPipeline { Temperature = 0.8f },
        };

        return _executor.InferAsync(templated, inferenceParams);
    }

    public void Dispose()
    {
        _weights?.Dispose();
        _inferLock?.Dispose();
    }
}
