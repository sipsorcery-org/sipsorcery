using SIPSorcery.Diagnostics.Commands;
using SIPSorcery.Cli.Common;
using SIPSorcery.SIP;
using Xunit;
using DiagnosticsExitCodes = SIPSorcery.Diagnostics.ExitCodes;

namespace SIPSorcery.Cli.UnitTest;

[Trait("Category", "unit")]
public class HTTPDigestStoreTests
{
    [Fact]
    public void SipDigestCommandUsesDigestVerb()
    {
        Assert.Equal("digest", new SipDigestCommand().Build().Name);
    }

    [Fact]
    public void WriteToFileAndReadRoundTripsDigestStore()
    {
        string tempDirectory = CreateTempDirectory();

        try
        {
            string path = Path.Combine(tempDirectory, "alice.digest");

            HTTPDigestStore.WriteToFile(path, "alice", "example.com", "secret");

            var credential = HTTPDigestStore.ReadFromFile(path);

            Assert.Equal(HTTPDigest.DigestCalcHA1("alice", "example.com", "secret"), credential.HA1_MD5);
            Assert.Equal(
                HTTPDigest.DigestCalcHA1("alice", "example.com", "secret", DigestAlgorithmsEnum.SHA256),
                credential.HA1_SHA256);

            string content = File.ReadAllText(path);
            Assert.Contains("ha1_md5=", content);
            Assert.Contains("ha1_sha256=", content);
            Assert.DoesNotContain("secret", content);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void ReadAllowsMissingDigestValues()
    {
        string tempDirectory = CreateTempDirectory();

        try
        {
            string path = Path.Combine(tempDirectory, "empty.digest");
            File.WriteAllText(path, string.Empty);

            var credential = HTTPDigestStore.ReadFromFile(path);

            Assert.Null(credential.HA1_MD5);
            Assert.Null(credential.HA1_SHA256);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void WriteToFileOverwritesExistingFile()
    {
        string tempDirectory = CreateTempDirectory();

        try
        {
            string path = Path.Combine(tempDirectory, "alice.digest");
            File.WriteAllText(path, "existing");

            HTTPDigestStore.WriteToFile(
                path,
                "alice",
                "example.com",
                "secret");

            string content = File.ReadAllText(path);
            Assert.DoesNotContain("existing", content);
            Assert.Contains("ha1_md5=", content);
            Assert.Contains("ha1_sha256=", content);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task SipRegisterRejectsPasswordCombinedWithDigestStore()
    {
        int exitCode = await new SipRegisterCommand()
            .Build()
            .Parse(new[] { "sip:alice@example.com", "--password", "secret", "--digest-store", "missing.txt", "--json" })
            .InvokeAsync();

        Assert.Equal(DiagnosticsExitCodes.InvalidArgument, exitCode);
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"sipsorcery-digest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
