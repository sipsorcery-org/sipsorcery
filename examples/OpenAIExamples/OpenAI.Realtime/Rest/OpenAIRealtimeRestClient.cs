//-----------------------------------------------------------------------------
// Filename: OpenAIRealtimeRestClient.cs
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
// MIT.
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

namespace demo;

public class OpenAIRealtimeRestClient : IOpenAIRealtimeRestClient
{
    public const string OPENAI_HTTP_CLIENT_NAME = "openai";

    private const string OPENAI_REALTIME_BASE_URL = "https://api.openai.com/v1/realtime";
    public const string OPENAI_REALTIME_DEFAULT_MODEL = "gpt-4o-realtime-preview-2024-12-17";

    private readonly IHttpClientFactory _factory;

    public OpenAIRealtimeRestClient(IHttpClientFactory factory)
    {
        _factory = factory;
    }

    //private readonly HttpClient _client;

    //public OpenAIRealtimeRestClient(HttpClient client)
    //{
    //    _client = client;
    //    _client.BaseAddress = new Uri(OPENAI_REALTIME_BASE_URL);
    //    _client.Timeout     = TimeSpan.FromSeconds(5);
    //    // ...and you’ll set the Bearer header once, up‐front:
    //}

    //// keep your old ctor around if you still want the factory overload:
    //public OpenAIRealtimeRestClient(IHttpClientFactory factory)
    //  : this(factory.CreateClient(OPENAI_HTTP_CLIENT_NAME))
    //{
    //}

    public async Task<Either<Error, string>> CreateEphemeralKeyAsync(
        string model = OPENAI_REALTIME_DEFAULT_MODEL,
        OpenAIVoicesEnum voice = OpenAIVoicesEnum.shimmer,
        CancellationToken ct = default)
    {
        var client = GetClient();

        using var req = new HttpRequestMessage(HttpMethod.Post, "/v1/realtime/sessions");
        req.Content = new StringContent(
            JsonSerializer.Serialize(new { model = model, voice = voice }, JsonOptions.Default),
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

    public async Task<Either<Error, string>> GetSdpAnswerAsync(
        string offerSdp,
        string model = OPENAI_REALTIME_DEFAULT_MODEL,
        CancellationToken ct = default)
    {
        var client = GetClient();
        var url = $"?model={Uri.EscapeDataString(model)}";

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

    /// <summary>
    /// Completes the steps required to get an ephemeral key from the OpenAI REST server. The ephemeral key is needed
    /// to send an SDP offer, and get the SDP answer.
    /// </summary>
    //public static async Task<Either<Error, string>> CreateEphemeralKeyAsync(string sessionsUrl, string openAIToken, string model, OpenAIVoicesEnum voice)
    //    => (await SendHttpPostAsync(
    //        sessionsUrl,
    //        openAIToken,
    //        JsonSerializer.Serialize(
    //            new OpenAISession
    //            {
    //                Model = model,
    //                Voice = voice
    //            }, JsonOptions.Default),
    //          "application/json"))
    //    .Bind(responseContent =>
    //        JsonSerializer.Deserialize<JsonElement>(responseContent)
    //            .GetProperty("client_secret")
    //            .GetProperty("value")
    //            .GetString() ??
    //        Prelude.Left<Error, string>(Error.New("Failed to get ephemeral secret."))
    //    );

    /// <summary>
    /// Attempts to get the SDP answer from the OpenAI REST server. This is the way OpenAI does the signalling. The
    /// ICE candidates will be returned in the SDP answer and are publicly accessible IP's.
    /// </summary>
    /// <remarks>
    /// See https://platform.openai.com/docs/guides/realtime-webrtc#creating-an-ephemeral-token.
    /// </remarks>
    //public static Task<Either<Error, string>> GetOpenAIAnswerSdpAsync(string ephemeralKey, string openAIBaseUrl, string model, string offerSdp)
    //    => SendHttpPostAsync(
    //        $"{openAIBaseUrl}?model={model}",
    //        ephemeralKey,
    //        offerSdp,
    //        "application/sdp");

    ///// <summary>
    ///// Helper method to send an HTTP POST request with the required headers.
    ///// </summary>
    //public static async Task<Either<Error, string>> SendHttpPostAsync(
    //    string url,
    //    string token,
    //    string body,
    //    string contentType)
    //{
    //    using var httpClient = new HttpClient();

    //    httpClient.DefaultRequestHeaders.Clear();
    //    httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    //    var content = new StringContent(body, Encoding.UTF8);
    //    content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

    //    var response = await httpClient.PostAsync(url, content);

    //    if (!response.IsSuccessStatusCode)
    //    {
    //        var errorBody = await response.Content.ReadAsStringAsync();
    //        return Error.New($"HTTP POST to {url} failed: {response.StatusCode}. Error body: {errorBody}");
    //    }

    //    return await response.Content.ReadAsStringAsync();
    //}
}
