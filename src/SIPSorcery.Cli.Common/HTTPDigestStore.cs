//-----------------------------------------------------------------------------
// Filename: HTTPDigestStore.cs
//
// Description: Reads and writes the CLI digest-store file format and resolves
// HA1 values for SIP digest authentication challenges.
//
// License:
// BSD 3-Clause "New" or "Revised" License.
//-----------------------------------------------------------------------------

using SIPSorcery.SIP;

namespace SIPSorcery.Cli.Common;


/// <summary>
/// A file-backed set of HA1 digests used by command-line SIP clients.
/// </summary>
public sealed class HTTPDigestStore
{
    public const string HA1MD5Key = "ha1_md5";

    public const string HA1SHA256Key = "ha1_sha256";


    // Static members >>

    public static HTTPDigestStore NewFromPassword(string username, string realm, string password)
    {
        var store = new HTTPDigestStore();
        store.CalcDigests(username, realm, password);
        return store;
    }

    public static HTTPDigestStore ReadFromFile(string path)
    {
        var store = new HTTPDigestStore();
        store.Read(path);
        return store;
    }

    public static void WriteToFile(string path, string username, string realm, string password)
    {
        NewFromPassword(username, realm, password).Write(path);
    }

    /// <summary>
    /// For SIP, RFC 8760 specifies the digest representation as ASCII characters from exactly '0123456789abcdef'.
    /// </summary>
    public static bool IsValidDigestValue(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length % 2 != 0)
        {
            return false;
        }

        foreach(char ch in value)
        {
            bool isHex = ((ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'f'));
            if (!isHex)
            {
                return false;
            }
        }

        return true;
    }

    // Public members >>

    public string? HA1_MD5 { get; set; }

    public string? HA1_SHA256 { get; set; }


    // Implementation >>

    /// <summary>
    /// Resolves an HA1 digest for a challenge. This store represents one credential, so the username
    /// and realm identify the challenge supplied to Core but are not encoded in the file format.
    /// </summary>
    public string? GetHA1Digest(string username, string realm, DigestAlgorithmsEnum digestAlgorithm)
    {
        _ = username;
        _ = realm;

        return digestAlgorithm switch
        {
            DigestAlgorithmsEnum.SHA256 => HA1_SHA256,
            DigestAlgorithmsEnum.MD5 => HA1_MD5,
            _ => null
        };
    }

    public void CalcDigests(string username, string realm, string password)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new ArgumentException("Username is required to create a digest store.", nameof(username));
        }

        if (string.IsNullOrWhiteSpace(realm))
        {
            throw new ArgumentException("Realm is required to create a digest store.", nameof(realm));
        }

        if (password == null)
        {
            throw new ArgumentException("Password is required to create a digest store.", nameof(password));
        }

        HA1_MD5 = HTTPDigest.DigestCalcHA1(username, realm, password, DigestAlgorithmsEnum.MD5);
        HA1_SHA256 = HTTPDigest.DigestCalcHA1(username, realm, password, DigestAlgorithmsEnum.SHA256);
    }

    /// <summary>
    /// Reads recognized values that are non-empty lowercase hexadecimal strings with an even number of characters.
    /// It intentionally does not validate algorithm-specific digest lengths.
    /// </summary>
    /// <returns>Count of valid values parsed.</returns>
    public int Read(string path)
    {
        int count = 0;
        foreach (string rawLine in File.ReadLines(path))
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            int separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            string key = line[..separatorIndex].Trim();
            string value = line[(separatorIndex + 1)..].Trim();

            if (!IsValidDigestValue(value))
            {
                continue;
            }

            if (key.Equals(HA1MD5Key, StringComparison.OrdinalIgnoreCase))
            {
                HA1_MD5 = value;
                count++;
            }
            else if (key.Equals(HA1SHA256Key, StringComparison.OrdinalIgnoreCase))
            {
                HA1_SHA256 = value;
                count++;
            }
        }

        return count;
    }

    public void Write(string path)
    {
        string content =
            $"{HA1MD5Key}={HA1_MD5}{Environment.NewLine}" +
            $"{HA1SHA256Key}={HA1_SHA256}{Environment.NewLine}";

        File.WriteAllText(path, content);
    }
}
