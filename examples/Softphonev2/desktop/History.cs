using System.Text.Json;
using System.Text.RegularExpressions;

namespace SIPSorcery.Softphone;

/// <summary>
/// One past call, as the page's History shows it. Times are Unix milliseconds, so the page can
/// use them as they are.
/// </summary>
public sealed class HistoryEntry
{
    public string Id { get; set; } = "";
    /// <summary>out, in, or missed (an incoming call that stopped ringing before it was answered).</summary>
    public string Dir { get; set; } = "out";
    /// <summary>Their display name, if the call had one.</summary>
    public string? Name { get; set; }
    public string Uri { get; set; } = "";
    /// <summary>The account it was on (its id, and its name then), or null for a direct call.</summary>
    public string? Account { get; set; }
    public string? AccountName { get; set; }
    /// <summary>When it was placed or started ringing.</summary>
    public long At { get; set; }
    /// <summary>How long it was connected for, in milliseconds; 0 if it never was.</summary>
    public long Duration { get; set; }
    /// <summary>A short outcome for a call that didn't connect: Busy, Declined, Cancelled, …</summary>
    public string? Note { get; set; }
    /// <summary>The SIP response that turned it down, e.g. "486 Busy Here".</summary>
    public string? Sip { get; set; }
    /// <summary>How it ended, in the phone's words ("They hung up", "Not answered: rang for 30 s, …").</summary>
    public string? Reason { get; set; }

    private static readonly Regex NameAddr = new(@"^\s*""?(?<name>[^""<]*?)""?\s*<(?<uri>[^>]+)>", RegexOptions.Compiled);

    /// <summary>The history entry for a call that has just ended.</summary>
    public static HistoryEntry From(Phone.Call c)
    {
        var (name, uri) = (null as string, c.Remote);
        if (NameAddr.Match(c.Remote) is { Success: true } m)
        {
            uri = m.Groups["uri"].Value.Trim();
            name = m.Groups["name"].Value.Trim() is { Length: > 0 } n ? n : null;
        }
        var e = new HistoryEntry
        {
            Id = Guid.NewGuid().ToString("N")[..12],
            Name = name,
            Uri = uri,
            Account = c.Account?.Id,
            AccountName = c.Account?.Label,
            At = new DateTimeOffset(c.StartedAt).ToUnixTimeMilliseconds(),
            Reason = c.EndReason,
        };
        if (c.ConnectedAt is { } connected)
        {
            e.Dir = c.Outgoing ? "out" : "in";
            e.Duration = (long)Math.Max(0, ((c.EndedAt ?? DateTime.Now) - connected).TotalMilliseconds);
        }
        else if (!c.Outgoing)
        {
            // Declined here, or answering failed; otherwise they gave up (or the app closed) while it rang.
            if (c.EndReason == "Declined") { e.Dir = "in"; e.Note = "Declined"; }
            else if (c.EndReason == "Couldn't answer") { e.Dir = "in"; e.Note = "Couldn't answer"; }
            else e.Dir = "missed";
        }
        else if (c.EndedByUs) e.Note = "Cancelled";
        else
        {
            e.Sip = c.FailureResponse;
            e.Note = Outcome(c.FailureResponse);
        }
        return e;
    }

    /// <summary>A failure response in a word or two, as the prototype shows them.</summary>
    private static string Outcome(string? response)
    {
        if (response == null || !int.TryParse(response.Split(' ')[0], out var code)) return "Call failed";
        return code switch
        {
            404 or 604 => "Number not found",
            408 or 487 => "No answer",
            480 => "Not available",
            486 or 600 => "Busy",
            603 => "Declined",
            488 or 606 => "Media not accepted",
            401 or 403 or 407 => "Not allowed",
            503 => "Service unavailable",
            _ => "Call failed",
        };
    }
}

/// <summary>
/// The call history, newest first, kept in history-&lt;profile&gt;.json beside the accounts. Capped at
/// <see cref="MaxEntries"/>: the oldest go. Removed entries are kept in memory until the app
/// closes, so the page's Undo can bring them back.
/// </summary>
public sealed class CallHistory
{
    public const int MaxEntries = 500;

    private sealed class FileModel
    {
        public List<HistoryEntry> Entries { get; set; } = new();
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly string _path;
    private readonly List<HistoryEntry> _entries;
    private readonly Dictionary<string, HistoryEntry> _removed = new();
    private readonly object _lock = new();

    /// <summary>A note for the log if the file couldn't be read.</summary>
    public string? LoadNote { get; private set; }

    public CallHistory(string profile)
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SIPSorcery", "Softphonev2");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, $"history-{profile}.json");
        _entries = Read();
    }

    public string FilePath => _path;

    /// <summary>A copy, newest first.</summary>
    public IReadOnlyList<HistoryEntry> All { get { lock (_lock) return _entries.ToList(); } }

    public void Add(HistoryEntry e)
    {
        lock (_lock)
        {
            _entries.Insert(0, e);
            Sort();
            Write();
        }
    }

    /// <summary>Removes these entries; returns how many there were.</summary>
    public int Remove(IEnumerable<string> ids)
    {
        var set = ids.ToHashSet();
        lock (_lock)
        {
            var gone = _entries.Where(e => set.Contains(e.Id)).ToList();
            if (gone.Count == 0) return 0;
            foreach (var e in gone) { _entries.Remove(e); _removed[e.Id] = e; }
            Write();
            return gone.Count;
        }
    }

    /// <summary>Puts back entries removed earlier in this run (Undo); returns how many came back.</summary>
    public int Restore(IEnumerable<string> ids)
    {
        lock (_lock)
        {
            int n = 0;
            foreach (var id in ids)
            {
                if (!_removed.Remove(id, out var e) || _entries.Any(x => x.Id == id)) continue;
                _entries.Add(e);
                n++;
            }
            if (n == 0) return 0;
            Sort();
            Write();
            return n;
        }
    }

    private void Sort()
    {
        _entries.Sort((a, b) => b.At.CompareTo(a.At));
        if (_entries.Count > MaxEntries) _entries.RemoveRange(MaxEntries, _entries.Count - MaxEntries);
    }

    private List<HistoryEntry> Read()
    {
        if (!File.Exists(_path)) return new();
        try
        {
            var model = JsonSerializer.Deserialize<FileModel>(File.ReadAllText(_path), JsonOpts) ?? new FileModel();
            var list = model.Entries.Where(e => !string.IsNullOrEmpty(e.Id) && !string.IsNullOrEmpty(e.Uri)).ToList();
            list.Sort((a, b) => b.At.CompareTo(a.At));
            return list.Take(MaxEntries).ToList();
        }
        catch (Exception ex)
        {
            LoadNote = $"couldn't read {_path} ({ex.Message}); starting with an empty call history";
            return new();
        }
    }

    /// <summary>Saves the list (under the lock). A failure throws, for the caller to log; the list in memory stays as it is.</summary>
    private void Write()
    {
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(new FileModel { Entries = _entries }, JsonOpts));
        File.Move(tmp, _path, overwrite: true);
    }
}
