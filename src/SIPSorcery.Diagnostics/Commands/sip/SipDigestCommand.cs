//-----------------------------------------------------------------------------
// Filename: SipDigestCommand.cs
//
// Description: The "sipsorcery-diags sip digest" verb. Creates a digest store
// file that SIP diagnostic commands can use instead of a clear text password.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.CommandLine;
using SIPSorcery.SIP;

namespace SIPSorcery.Diagnostics.Commands;

public sealed class SipDigestCommand : CommandBase
{
    private sealed record DigestResult(
        bool Success,
        string Path,
        string? Error);

    public SipDigestCommand() : base(defaultTimeoutSeconds: 0)
    { }

    public override Command Build()
    {
        var usernameOption = new Option<string>("--username", "-u")
        {
            Description = "The SIP account username used to compute username:realm:password.",
            Required = true
        };

        var realmOption = new Option<string>("--realm")
        {
            Description = "The SIP authentication realm used to compute username:realm:password.",
            Required = true
        };

        var passwordOption = new Option<string>("--password")
        {
            Description = "The clear text password used to compute the digest store. It is not written to the output file.",
            Required = true
        };

        var outOption = new Option<string>("--out")
        {
            Description = "The digest store file to create. Existing file will be overwritten.",
            Required = true
        };

        var command = new Command("digest", "Create a digest store file for SIP authentication.");
        command.Options.Add(usernameOption);
        command.Options.Add(realmOption);
        command.Options.Add(passwordOption);
        command.Options.Add(outOption);
        AddCommonOptions(command);

        command.SetAction((parseResult, cancellationToken) => Task.FromResult(Run(
            parseResult.GetValue(usernameOption)!,
            parseResult.GetValue(realmOption)!,
            parseResult.GetValue(passwordOption)!,
            parseResult.GetValue(outOption)!,
            parseResult.GetValue(JsonOption))));

        return command;
    }

    private static int Run(string username, string realm, string password, string path, bool asJson)
    {
        try
        {
            HTTPDigestStore.WriteToFile(path, username, realm, password);

            return WriteResult(
                asJson,
                new DigestResult(true, path, null),
                ExitCodes.Ok);
        }
        catch (Exception excp) when (excp is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return WriteResult(
                asJson,
                new DigestResult(false, path, excp.Message),
                ExitCodes.InvalidArgument);
        }
    }

    private static int WriteResult(bool asJson, DigestResult result, int exitCode)
    {
        if (asJson)
        {
            WriteJson(result);
        }
        else if (result.Success)
        {
            Console.WriteLine($"Created digest store file {result.Path}.");
        }
        else
        {
            Console.Error.WriteLine(result.Error);
        }

        return exitCode;
    }
}
