using System.Text.Json;

namespace SIPSorcery.Softphone;

/// <summary>
/// A name you've given a SIP address, for the address field's suggestions. Account: the SIP
/// account (its id) to call them from, "direct" for no account, or null for whichever Call from
/// has. Media: the media to offer them, when it's known what their phone takes (see
/// <see cref="ContactMedia"/>), or null to work it out from their answers. Older files have neither.
/// </summary>
public sealed record Contact(string Name, string Uri, string? Account = null, string? Media = null);

/// <summary>
/// A contact's media: what our first INVITE offers them, with no falling back to another kind if
/// they turn it down (Phone.Dial). Without one a call starts with plain RTP, SDES keys on RTP/AVP,
/// and on a 488 tries RTP/SAVP and then WebRTC, an INVITE each.
/// </summary>
public static class ContactMedia
{
    /// <summary>Plain RTP, with SDES keys on RTP/AVP that a phone with SRTP takes up.</summary>
    public const string Rtp = "rtp";
    /// <summary>SRTP required: RTP/SAVP with SDES keys, for a phone with SRTP mandatory (MicroSIP's).</summary>
    public const string Srtp = "srtp";
    /// <summary>WebRTC: ICE and DTLS-SRTP, for a browser phone.</summary>
    public const string WebRtc = "webrtc";

    /// <summary>One of the above, or null for anything else (none set, or one this build doesn't know).</summary>
    public static string? Parse(string? media) => media?.Trim().ToLowerInvariant() switch
    {
        Rtp => Rtp,
        Srtp => Srtp,
        WebRtc => WebRtc,
        _ => null,
    };
}

/// <summary>
/// The contacts, kept per profile in <c>%LOCALAPPDATA%\SIPSorcery\Softphonev2\contacts-{profile}.json</c>
/// beside the accounts: a plain list of name and SIP address, one per address. The page adds them
/// from the address field's suggestions (Add to contacts) and removes them there too.
/// </summary>
public sealed class ContactStore
{
    private const int MaxContacts = 500;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _path;
    private readonly List<Contact> _contacts;
    private readonly object _lock = new();

    /// <summary>A note for the log if the file couldn't be read.</summary>
    public string? LoadNote { get; private set; }

    public ContactStore(string profile)
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SIPSorcery", "Softphonev2");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, $"contacts-{profile}.json");
        _contacts = Read();
    }

    public IReadOnlyList<Contact> All { get { lock (_lock) return _contacts.ToList(); } }

    /// <summary>
    /// Adds a contact, or renames the one with that address and sets the account to call it
    /// from (null or empty: Call from's) and its media (null: worked out on each call). Sorted by name.
    /// </summary>
    public void Save(string name, string uri, string? account = null, string? media = null)
    {
        name = name.Trim();
        uri = uri.Trim();
        account = string.IsNullOrWhiteSpace(account) ? null : account.Trim();
        if (uri.Length == 0) return;
        if (name.Length == 0) name = uri;
        lock (_lock)
        {
            var contact = new Contact(name, uri, account, ContactMedia.Parse(media));
            var i = _contacts.FindIndex(c => SameUri(c.Uri, uri));
            if (i >= 0) _contacts[i] = contact;
            else if (_contacts.Count < MaxContacts) _contacts.Add(contact);
            _contacts.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
            Write();
        }
    }

    public void Remove(string uri)
    {
        lock (_lock)
        {
            if (_contacts.RemoveAll(c => SameUri(c.Uri, uri)) > 0) Write();
        }
    }

    private static bool SameUri(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    private List<Contact> Read()
    {
        try
        {
            if (!File.Exists(_path)) return new();
            return (JsonSerializer.Deserialize<List<Contact>>(File.ReadAllText(_path), JsonOpts) ?? new())
                .Where(c => !string.IsNullOrWhiteSpace(c?.Uri)).ToList();
        }
        catch (Exception ex)
        {
            LoadNote = $"contacts: couldn't read {_path} ({ex.Message}), starting with none";
            return new();
        }
    }

    private void Write()
    {
        // Write then swap, so a crash mid-write doesn't lose the list.
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_contacts, JsonOpts));
        File.Move(tmp, _path, overwrite: true);
    }
}
