using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.Gemini.Realtime;

/// <summary>
/// Extension methods to work with the Gemini Live end point.
/// </summary>
public static class GeminiLiveServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Gemini Live WebSocket client and end point for the given API key.
    /// </summary>
    /// <param name="services">The service collection to add registrations to.</param>
    /// <param name="apiKey">Your Google AI Studio / Gemini API key.</param>
    /// <returns>The original <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddGeminiLiveRealtime(this IServiceCollection services, string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("Gemini API key must be provided", nameof(apiKey));
        }

        services.TryAddTransient<IGeminiLiveWebSocketClient>(sp =>
            new GeminiLiveWebSocketClient(apiKey, sp.GetService<ILogger<GeminiLiveWebSocketClient>>()));

        services.TryAddTransient<IGeminiLiveEndPoint, GeminiLiveEndPoint>();

        return services;
    }
}
