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

namespace demo;

public class OpenAIRealtimeRestClient
{
    /// <summary>
    /// Completes the steps required to get an ephemeral key from the OpenAI REST server. The ephemeral key is needed
    /// to send an SDP offer, and get the SDP answer.
    /// </summary>
    public static async Task<Either<Error, string>> CreateEphemeralKeyAsync(string sessionsUrl, string openAIToken, string model, OpenAIVoicesEnum voice)
        => (await SendHttpPostAsync(
            sessionsUrl,
            openAIToken,
            JsonSerializer.Serialize(
                new OpenAISession
                {
                    Model = model,
                    Voice = voice
                }, JsonOptions.Default),
              "application/json"))
        .Bind(responseContent =>
            JsonSerializer.Deserialize<JsonElement>(responseContent)
                .GetProperty("client_secret")
                .GetProperty("value")
                .GetString() ??
            Prelude.Left<Error, string>(Error.New("Failed to get ephemeral secret."))
        );

    /// <summary>
    /// Attempts to get the SDP answer from the OpenAI REST server. This is the way OpenAI does the signalling. The
    /// ICE candidates will be returned in the SDP answer and are publicly accessible IP's.
    /// </summary>
    /// <remarks>
    /// See https://platform.openai.com/docs/guides/realtime-webrtc#creating-an-ephemeral-token.
    /// </remarks>
    public static Task<Either<Error, string>> GetOpenAIAnswerSdpAsync(string ephemeralKey, string openAIBaseUrl, string model, string offerSdp)
        => SendHttpPostAsync(
            $"{openAIBaseUrl}?model={model}",
            ephemeralKey,
            offerSdp,
            "application/sdp");

    /// <summary>
    /// Helper method to send an HTTP POST request with the required headers.
    /// </summary>
    public static async Task<Either<Error, string>> SendHttpPostAsync(
        string url,
        string token,
        string body,
        string contentType)
    {
        using var httpClient = new HttpClient();

        httpClient.DefaultRequestHeaders.Clear();
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var content = new StringContent(body, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        var response = await httpClient.PostAsync(url, content);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            return Error.New($"HTTP POST to {url} failed: {response.StatusCode}. Error body: {errorBody}");
        }

        return await response.Content.ReadAsStringAsync();
    }
}
