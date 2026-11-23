using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SIPSorcery.Softphone;

/// <summary>
/// Go live: a call broadcast to a WHIP endpoint (RFC 9725), a media server that others watch it
/// from (over WHEP or HLS, the server's business). The page does the media: it mixes the call (both
/// sides' audio, and their video beside yours) into one stream and publishes it from a peer
/// connection of its own, straight to the server. This is the HTTP half: the offer POSTed, the
/// answer back, and the session's resource (the Location header) DELETEd to stop. C# does it,
/// not the page, so the server doesn't have to allow the page's origin (CORS).
///
/// One broadcast per call. Each ends with its call, with Stop live, or as the window closes.
/// The last endpoint used is kept per profile, with its token encrypted by DPAPI as account
/// passwords are, so Go live offers it again.
/// </summary>
public sealed class LiveStreams : IDisposable
{
    private const string SdpType = "application/sdp";
    private const int BodyMaxChars = 200;

    private sealed record Session(Uri? Resource, string? Token);
    private sealed class FileModel
    {
        public string? Url { get; set; }
        public string? ProtectedToken { get; set; }
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly Dictionary<string, Session> _sessions = [];
    private readonly object _lock = new();
    private readonly string _path;
    private string? _token;

    /// <summary>The endpoint last gone live to, or null.</summary>
    public string? SavedUrl { get; private set; }
    /// <summary>There's a saved token for it (the page never sees it).</summary>
    public bool HasSavedToken => !string.IsNullOrEmpty(_token);
    /// <summary>A note for the log if the file couldn't be read.</summary>
    public string? LoadNote { get; private set; }

    public LiveStreams(string profile)
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SIPSorcery", "Softphonev2");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, $"golive-{profile}.json");
        try
        {
            if (File.Exists(_path) && JsonSerializer.Deserialize<FileModel>(File.ReadAllText(_path), JsonOpts) is { } f)
            {
                SavedUrl = f.Url;
                _token = f.ProtectedToken is { Length: > 0 } p ? AccountStore.Unprotect(p) : null;
            }
        }
        catch (Exception ex)
        {
            LoadNote = $"go live: couldn't read {Path.GetFileName(_path)}: {ex.Message}";
        }
    }

    /// <summary>
    /// POSTs the page's offer to the endpoint and returns the server's answer. A null token uses
    /// the saved one (the page leaves the token field blank to keep it); an empty one sends none.
    /// The endpoint and token are saved once the server has taken the offer. Throws with a
    /// sentence for the page if it didn't.
    /// </summary>
    public async Task<string> StartAsync(string callId, string url, string? token, string offer)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var endpoint) || (endpoint.Scheme != Uri.UriSchemeHttps && endpoint.Scheme != Uri.UriSchemeHttp))
            throw new ApplicationException("That isn't a WHIP address: it starts https://");
        token ??= string.Equals(url.Trim(), SavedUrl, StringComparison.Ordinal) ? _token : null;
        token = string.IsNullOrWhiteSpace(token) ? null : token.Trim();

        await StopAsync(callId);   // one broadcast per call

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = new StringContent(offer, Encoding.UTF8) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(SdpType);
        if (token != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        HttpResponseMessage response;
        try { response = await _http.SendAsync(request); }
        catch (TaskCanceledException) { throw new ApplicationException($"{endpoint.Host} didn't answer within 15 s"); }
        catch (HttpRequestException ex) { throw new ApplicationException($"Couldn't reach {endpoint.Host}: {ex.Message}"); }
        using (response)
        {
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) throw new ApplicationException(Refusal(response.StatusCode, body));
            if (!body.TrimStart().StartsWith("v=", StringComparison.Ordinal)) throw new ApplicationException($"{endpoint.Host} answered, but not with SDP: is it a WHIP endpoint?");
            // The resource is how the broadcast is stopped; a relative one is on the endpoint's host.
            var location = response.Headers.Location;
            var resource = location == null ? null : location.IsAbsoluteUri ? location : new Uri(endpoint, location);
            lock (_lock) _sessions[callId] = new Session(resource, token);
            Save(url.Trim(), token);
            return body;
        }
    }

    // What the server's refusal means, in words, with the start of what it said.
    private static string Refusal(HttpStatusCode status, string body)
    {
        var what = (int)status switch
        {
            401 or 403 => "The server turned the stream key down",
            404 => "There's no WHIP endpoint at that address",
            409 => "That stream is already live from somewhere else",
            400 or 406 or 415 or 422 => "The server didn't accept the call's media",
            >= 500 => "The server had a problem",
            _ => "The server said no",
        };
        var said = body.Trim().ReplaceLineEndings(" ");
        if (said.Length > BodyMaxChars) said = said[..BodyMaxChars] + "…";
        return $"{what} (HTTP {(int)status}{(said.Length > 0 ? $": {said}" : "")})";
    }

    /// <summary>Stops a call's broadcast: DELETE on its resource. Best effort; nothing if it has none.</summary>
    public async Task StopAsync(string callId)
    {
        Session? s;
        lock (_lock) { if (!_sessions.Remove(callId, out s)) return; }
        if (s.Resource == null) return;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, s.Resource);
            if (s.Token != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", s.Token);
            using var _ = await _http.SendAsync(request);
        }
        catch { /* the server times the session out without media anyway */ }
    }

    /// <summary>Calls with a broadcast going, by id.</summary>
    public IReadOnlyList<string> Live { get { lock (_lock) return _sessions.Keys.ToList(); } }

    private void Save(string url, string? token)
    {
        SavedUrl = url;
        _token = token;
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(new FileModel { Url = url, ProtectedToken = token == null ? null : AccountStore.Protect(token) }, JsonOpts));
        }
        catch (Exception ex) { LoadNote = $"go live: couldn't save {Path.GetFileName(_path)}: {ex.Message}"; }
    }

    /// <summary>As the window closes: every broadcast stopped, waiting a moment for the DELETEs.</summary>
    public void Dispose()
    {
        try { Task.WaitAll(Live.Select(StopAsync).ToArray(), TimeSpan.FromSeconds(2)); } catch { }
        _http.Dispose();
    }
}
