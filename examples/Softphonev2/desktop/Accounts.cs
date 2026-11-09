using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SIPSorcery.Softphone;

/// <summary>A SIP account the phone can call from, and register with so it can be called.</summary>
public sealed class SipAccount
{
    public string Id { get; set; } = "";
    /// <summary>What the account is called in the app, e.g. "Work". Blank: its user@server.</summary>
    public string Name { get; set; } = "";
    /// <summary>Shown to the people you call.</summary>
    public string? DisplayName { get; set; }
    public string Username { get; set; } = "";
    /// <summary>The domain, and the registrar: a host, host:port, or a SIP URI such as sip:host;transport=tcp.</summary>
    public string Server { get; set; } = "";
    /// <summary>The username for authentication, if not the same as <see cref="Username"/>.</summary>
    public string? AuthUsername { get; set; }
    /// <summary>In memory only; on disk it's <see cref="ProtectedPassword"/>.</summary>
    [JsonIgnore] public string Password { get; set; } = "";
    /// <summary>The password encrypted with DPAPI for the current Windows user, base64.</summary>
    public string? ProtectedPassword { get; set; }
    /// <summary>Register with the server so calls to this account reach us.</summary>
    public bool Register { get; set; } = true;
    public int Expiry { get; set; } = 300;
    /// <summary>Off: the account stays in the list but doesn't register and can't be called from.</summary>
    public bool Enabled { get; set; } = true;

    [JsonIgnore] public string Aor => $"{Username}@{Host}";
    [JsonIgnore] public string Label => string.IsNullOrWhiteSpace(Name) ? Aor : Name;
    [JsonIgnore] public string AuthUser => string.IsNullOrWhiteSpace(AuthUsername) ? Username : AuthUsername!;

    /// <summary>The server's host (and port), without any sip: scheme or parameters.</summary>
    [JsonIgnore]
    public string Host
    {
        get
        {
            var s = Server.Trim();
            if (s.StartsWith("sip:", StringComparison.OrdinalIgnoreCase)) s = s[4..];
            else if (s.StartsWith("sips:", StringComparison.OrdinalIgnoreCase)) s = s[5..];
            int at = s.IndexOf('@');
            if (at >= 0) s = s[(at + 1)..];
            int semi = s.IndexOf(';');
            return semi >= 0 ? s[..semi] : s;
        }
    }

    /// <summary>The host without a port, for matching the To of an incoming call.</summary>
    [JsonIgnore]
    public string Domain
    {
        get
        {
            var h = Host;
            if (h.StartsWith('[')) return h;   // IPv6, leave it be
            int colon = h.LastIndexOf(':');
            return colon >= 0 ? h[..colon] : h;
        }
    }

    public SipAccount Clone() => (SipAccount)MemberwiseClone();

    /// <summary>Everything registration depends on: a change here means registering again.</summary>
    [JsonIgnore]
    public string RegistrationKey => string.Join("\n", Enabled, Register, Username, Server, AuthUser, Password, Expiry);
}

/// <summary>
/// The accounts, kept per profile in
/// <c>%LOCALAPPDATA%\SIPSorcery\Softphonev2\accounts-{profile}.json</c>, with each password
/// encrypted by DPAPI for the current Windows user. The config file's account (softphone.json,
/// <c>--config</c>) is a seed: it's added once, the first time this profile sees it, and from then
/// on it's edited or removed here like any other.
/// </summary>
public sealed class AccountStore
{
    private sealed class FileModel
    {
        public string? Selected { get; set; }
        public List<SipAccount> Accounts { get; set; } = new();
        /// <summary>Config accounts already added (user@server), so removing one keeps it removed.</summary>
        public List<string> Seeded { get; set; } = new();
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SIPSorcery.Softphonev2.accounts");

    private readonly string _path;
    private readonly FileModel _model;
    private readonly object _lock = new();

    public string Path => _path;

    /// <summary>A note for the log if the file couldn't be read, or a password couldn't be decrypted.</summary>
    public string? LoadNote { get; private set; }

    public AccountStore(string profile, AccountSettings? seed)
    {
        var dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SIPSorcery", "Softphonev2");
        Directory.CreateDirectory(dir);
        _path = System.IO.Path.Combine(dir, $"accounts-{profile}.json");
        _model = Read();

        if (seed != null && !string.IsNullOrWhiteSpace(seed.Server) && !string.IsNullOrWhiteSpace(seed.Username))
        {
            var key = $"{seed.Username}@{seed.Server}".ToLowerInvariant();
            if (!_model.Seeded.Contains(key))
            {
                _model.Seeded.Add(key);
                if (!_model.Accounts.Any(a => $"{a.Username}@{a.Server}".Equals(key, StringComparison.OrdinalIgnoreCase)))
                {
                    _model.Accounts.Add(new SipAccount
                    {
                        Id = NewId(), DisplayName = seed.DisplayName,
                        Username = seed.Username, Server = seed.Server, Password = seed.Password ?? "",
                    });
                }
                Write();
            }
        }
        // Selected: null until chosen, "" for direct calls with no account (kept across restarts).
        if ((_model.Selected == null || (_model.Selected != "" && Find(_model.Selected) == null)) && _model.Accounts.FirstOrDefault(a => a.Enabled) is { } first)
        {
            _model.Selected = first.Id;
            Write();
        }
    }

    /// <summary>Copies, so callers can't change the store behind its back.</summary>
    public IReadOnlyList<SipAccount> All { get { lock (_lock) return _model.Accounts.Select(a => a.Clone()).ToList(); } }

    public SipAccount? Find(string? id) { lock (_lock) return _model.Accounts.FirstOrDefault(a => a.Id == id)?.Clone(); }

    /// <summary>The account calls go out from: the one picked in Call from, if it's enabled.</summary>
    public SipAccount? Selected
    {
        get { lock (_lock) return _model.Accounts.FirstOrDefault(a => a.Id == _model.Selected && a.Enabled)?.Clone(); }
    }

    public string? SelectedId { get { lock (_lock) return _model.Selected; } }

    public void Select(string? id)
    {
        lock (_lock)
        {
            if (id != null && !_model.Accounts.Any(a => a.Id == id)) return;
            _model.Selected = id ?? "";
            Write();
        }
    }

    /// <summary>
    /// Adds the account (no id) or replaces the one with its id. An empty password keeps the
    /// account's current one, as the page never gets passwords back to show.
    /// </summary>
    public SipAccount Save(SipAccount account)
    {
        lock (_lock)
        {
            var existing = _model.Accounts.FindIndex(a => a.Id == account.Id);
            var saved = account.Clone();
            if (existing < 0)
            {
                saved.Id = NewId();
                _model.Accounts.Add(saved);
            }
            else
            {
                if (string.IsNullOrEmpty(saved.Password)) saved.Password = _model.Accounts[existing].Password;
                _model.Accounts[existing] = saved;
            }
            // Calls go out from an enabled account: a new one if there wasn't one, or another one
            // if the chosen account was just turned off.
            if (Find(_model.Selected) is not { Enabled: true })
                _model.Selected = (saved.Enabled ? saved.Id : null) ?? _model.Accounts.FirstOrDefault(a => a.Enabled)?.Id ?? _model.Selected;
            Write();
            return saved.Clone();
        }
    }

    public void Remove(string id)
    {
        lock (_lock)
        {
            _model.Accounts.RemoveAll(a => a.Id == id);
            if (_model.Selected == id) _model.Selected = _model.Accounts.FirstOrDefault(a => a.Enabled)?.Id;
            Write();
        }
    }

    private static string NewId() => Guid.NewGuid().ToString("N")[..8];

    private FileModel Read()
    {
        if (!File.Exists(_path)) return new FileModel();
        try
        {
            var model = JsonSerializer.Deserialize<FileModel>(File.ReadAllText(_path), JsonOpts) ?? new FileModel();
            foreach (var a in model.Accounts)
            {
                try { a.Password = a.ProtectedPassword is { Length: > 0 } p ? Unprotect(p) : ""; }
                catch (Exception ex)
                {
                    // Another Windows user's file, or copied from another PC: the password has to be typed again.
                    a.Password = "";
                    LoadNote = $"couldn't decrypt the password for {a.Label} ({ex.Message}); enter it again in Manage accounts";
                }
            }
            return model;
        }
        catch (Exception ex)
        {
            LoadNote = $"couldn't read {_path} ({ex.Message}); starting with no saved accounts";
            return new FileModel();
        }
    }

    private void Write()
    {
        foreach (var a in _model.Accounts) a.ProtectedPassword = string.IsNullOrEmpty(a.Password) ? null : Protect(a.Password);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_model, JsonOpts));
        File.Move(tmp, _path, overwrite: true);
    }

    // ---- DPAPI (CryptProtectData), current user. P/Invoke rather than a package for two calls. ----

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob { public int Size; public IntPtr Data; }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref DataBlob dataIn, string? description, ref DataBlob entropy,
        IntPtr reserved, IntPtr prompt, int flags, out DataBlob dataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref DataBlob dataIn, IntPtr description, ref DataBlob entropy,
        IntPtr reserved, IntPtr prompt, int flags, out DataBlob dataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr mem);

    private const int CRYPTPROTECT_UI_FORBIDDEN = 0x1;

    internal static string Protect(string plain) =>
        Convert.ToBase64String(Crypt(Encoding.UTF8.GetBytes(plain), protect: true));

    internal static string Unprotect(string protectedBase64) =>
        Encoding.UTF8.GetString(Crypt(Convert.FromBase64String(protectedBase64), protect: false));

    private static byte[] Crypt(byte[] input, bool protect)
    {
        var inHandle = GCHandle.Alloc(input, GCHandleType.Pinned);
        var entHandle = GCHandle.Alloc(Entropy, GCHandleType.Pinned);
        try
        {
            var dataIn = new DataBlob { Size = input.Length, Data = inHandle.AddrOfPinnedObject() };
            var entropy = new DataBlob { Size = Entropy.Length, Data = entHandle.AddrOfPinnedObject() };
            bool ok = protect
                ? CryptProtectData(ref dataIn, "Softphone SIP password", ref entropy, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, out var dataOut)
                : CryptUnprotectData(ref dataIn, IntPtr.Zero, ref entropy, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, out dataOut);
            if (!ok) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                var result = new byte[dataOut.Size];
                Marshal.Copy(dataOut.Data, result, 0, dataOut.Size);
                return result;
            }
            finally { LocalFree(dataOut.Data); }
        }
        finally
        {
            inHandle.Free();
            entHandle.Free();
        }
    }
}
