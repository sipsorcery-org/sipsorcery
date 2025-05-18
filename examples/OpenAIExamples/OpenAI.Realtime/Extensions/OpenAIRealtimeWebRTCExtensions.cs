//-----------------------------------------------------------------------------
// Filename: OpenAIRealtimeWebRTCServiceCollectionExtensions.cs
//
// Description: Extension method to register OpenAI Realtime WebRTC client
// and required services in the DI container.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 18 May 2025  Aaron Clauson   Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License and the additional
// BDS BY-NC-SA restriction, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Net.Http.Headers;
using SIPSorcery.OpenAI.RealtimeWebRTC;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension method to register OpenAI Realtime WebRTC client and required services in the DI container.
/// </summary>
public static class OpenAIRealtimeWebRTCServiceCollectionExtensions
{
    /// <summary>
    /// Adds and configures the OpenAI Realtime REST and WebRTC endpoint clients.
    /// </summary>
    /// <param name="services">The service collection to add registrations to.</param>
    /// <param name="openAiKey">Your OpenAI API key for authorization.</param>
    /// <returns>The original <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddOpenAIRealtimeWebRTC(this IServiceCollection services, string openAiKey)
    {
        if (string.IsNullOrWhiteSpace(openAiKey))
        {
            throw new ArgumentException("OpenAI API key must be provided", nameof(openAiKey));
        }

        // Register the HTTP client for the REST client
        services
            .AddHttpClient(OpenAIRealtimeRestClient.OPENAI_HTTP_CLIENT_NAME, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(5);
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", openAiKey);
            });

        // Register the REST and WebRTC clients
        services.AddTransient<IOpenAIRealtimeRestClient, OpenAIRealtimeRestClient>();
        services.AddTransient<IOpenAIRealtimeWebRTCEndPoint, OpenAIRealtimeWebRTCEndPoint>();

        return services;
    }
}
