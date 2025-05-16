using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.Http.Headers;

namespace demo;

public class OpenAIHttpClientFactory : IHttpClientFactory
{
    private readonly string _openAiKey;
    private readonly ConcurrentDictionary<string, HttpClient> _clients
        = new ConcurrentDictionary<string, HttpClient>();

    public OpenAIHttpClientFactory(string openAiKey)
    {
        _openAiKey = openAiKey;
    }

    public HttpClient CreateClient(string name)
    {
        return _clients.GetOrAdd(name, _ =>
        {
            var client = new HttpClient();

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _openAiKey);

            return client;
        });
    }
}
