//-----------------------------------------------------------------------------
// Filename: WebRTCRestClient.cs
//
// Description: Used to send requests to the OpenAI Realtime REST server to
// do the initial session set up and the SDP offer/answer exchange.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 23 Jan 2025  Aaron Clauson   Created, Dublin, Ireland.
// 10 May 2025  Aaron Clauson   Enhancing use. Adopted HttpClientFactory pattern.
//
// License: 
// BSD 3-Clause "New" or "Revised" License and the additional
// BDS BY-NC-SA restriction, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using LanguageExt;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Text.Json;
using System.Text;
using System.Threading.Tasks;
using LanguageExt.Common;
using System.Threading;
using System;
using SIPSorcery.OpenAI.Realtime.Models;

namespace SIPSorcery.OpenAI.Realtime;

public class WebRTCRestClient : IWebRTCRestClient
{
    public const string OPENAI_HTTP_CLIENT_NAME = "openai";

    public const RealtimeModelsEnum DEFAULT_REALTIME_MODEL = RealtimeModelsEnum.GptRealtime;

    private const string OPENAI_REALTIME_BASE_URL = "https://api.openai.com/v1/realtime";

    private readonly IHttpClientFactory _factory;

    public WebRTCRestClient(IHttpClientFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Completes the steps required to get an ephemeral key from the OpenAI REST server. The ephemeral key is needed
    /// to send an SDP offer, and get the SDP answer.
    /// </summary>
    /// <param name="model">Optional model to use for the request.</param>
    /// <param name="voice">The voice to request for the session.</param>
    /// <param name="ct">Cancellation token to allow the request to be cancelled.</param>
    /// <returns>Either a descriptive error if the request failed or a string representing the newly created ephemeral API key.</returns>
    public async Task<Either<Error, string>> CreateEphemeralKeyAsync(
        RealtimeVoicesEnum voice = RealtimeVoicesEnum.shimmer,
        RealtimeModelsEnum? model = null,
        CancellationToken ct = default)
    {
        var useModel = model ?? DEFAULT_REALTIME_MODEL;

        var client = GetClient();

        using var req = new HttpRequestMessage(HttpMethod.Post, "/v1/realtime/sessions");
        req.Content = new StringContent(
            JsonSerializer.Serialize(new { model = useModel.ToEnumString(), voice }, JsonOptions.Default),
            Encoding.UTF8,
            "application/json");

        using var res = await client.SendAsync(req, ct).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return Error.New($"CreateEphemeralKey failed [{res.StatusCode}]: {body}");
        }

        using var doc = await JsonDocument.ParseAsync(
            await res.Content.ReadAsStreamAsync(ct),
            cancellationToken: ct).ConfigureAwait(false);

        if (doc.RootElement.TryGetProperty("client_secret", out var cs) &&
            cs.TryGetProperty("value", out var val) &&
            val.GetString() is string secret)
        {
            return secret;
        }

        return Error.New("Failed to parse client_secret.value");
    }

    /// <summary>
    /// Attempts to get the SDP answer from the OpenAI REST server. This is the way OpenAI does the signalling. The
    /// ICE candidates will be returned in the SDP answer and are publicly accessible IP's.
    /// </summary>
    /// <param name="offerSdp">The offer Session Description Protocol (SDP) payload to send to the OpenAI REST server.</param>
    /// <param name="model">Optional model to use for the request.</param>
    /// <param name="ct">Cancellation token to allow the request to be cancelled.</param>
    /// <returns>Either a descriptive error if the request failed or a string representing the answer SDP from the OpenAI REST server.</returns>
    /// <remarks>
    /// See https://platform.openai.com/docs/guides/realtime-webrtc#creating-an-ephemeral-token.
    /// </remarks>
    public async Task<Either<Error, string>> GetSdpAnswerAsync(
        string offerSdp,
        RealtimeModelsEnum? model,
        CancellationToken ct = default)
    {
        var useModel = model ?? DEFAULT_REALTIME_MODEL;
        var client = GetClient();
        //var url = $"?model={Uri.EscapeDataString(useModel.ToEnumString())}";
        var url = $"/v1/realtime/calls?model={Uri.EscapeDataString(useModel.ToEnumString())}";

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Content = new StringContent(offerSdp, Encoding.UTF8);
        req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/sdp");

        using var res = await client.SendAsync(req, ct).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return Error.New($"GetOpenAIAnswerSdp failed [{res.StatusCode}]: {body}");
        }

        var sdp = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return sdp;
    }

    private HttpClient GetClient()
    {
        var client = _factory.CreateClient(OPENAI_HTTP_CLIENT_NAME);
        if (client.BaseAddress is null)
        {
            client.BaseAddress = new Uri(OPENAI_REALTIME_BASE_URL);
        }
        return client;
    }
}
