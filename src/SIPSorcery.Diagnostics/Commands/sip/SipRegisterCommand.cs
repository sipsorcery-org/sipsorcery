//-----------------------------------------------------------------------------
// Filename: SipRegisterCommand.cs
//
// Description: The "sipsorcery sip register" verb. Registers a SIP account with
// a registrar and reports whether the registration succeeded. The equivalent of
// the sipcmdline "reg" scenario. By default the registration is removed again on
// exit (a zero expiry re-register); --duration holds it active first.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 13 Jun 2026	Aaron Clauson	Created, Wexford, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.CommandLine;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;

namespace SIPSorcery.Diagnostics.Commands;

public sealed class SipRegisterCommand : CommandBase
{
    private const int DEFAULT_TIMEOUT_SECONDS = 10;
    private const int DEFAULT_EXPIRY_SECONDS = 120;

    /// <summary>
    /// The result shape written to stdout with --json. Stable field names; additive changes only.
    /// </summary>
    private sealed record RegisterResult(
        bool Success,
        string Registrar,
        string Aor,
        int RequestedExpiry,
        long DurationMs,
        bool Unregistered,
        string? Error);

    public SipRegisterCommand() : base(DEFAULT_TIMEOUT_SECONDS)
    { }

    public override Command Build()
    {
        var destinationArg = new Argument<string>("destination")
        {
            Description = "The registrar in the form [sip:|sips:|udp:|tcp:|tls:]host[:port], e.g. sipsorcery.com, tls:sip.example.com."
        };

        var usernameOption = new Option<string?>("--username", "-u")
        {
            Description = "The account username to register. Defaults to the user part of the destination if present."
        };

        var passwordOption = new Option<string?>("--password")
        {
            Description = "The account password used to authenticate the registration."
        };

        var expiryOption = new Option<int>("--expiry")
        {
            Description = "The requested registration expiry in seconds.",
            DefaultValueFactory = _ => DEFAULT_EXPIRY_SECONDS
        };

        var durationOption = new Option<int>("--duration", "-d")
        {
            Description = "Seconds to hold the registration active (refreshing as needed) before removing it. 0 registers then removes immediately.",
            DefaultValueFactory = _ => 0
        };

        var keepOption = new Option<bool>("--keep")
        {
            Description = "Leave the registration in place on exit instead of removing it with a zero expiry re-register."
        };

        var hepOption = HepCapture.CreateOption();

        var command = new Command("register", "Register a SIP account with a registrar and report the result (sipcmdline reg).");
        command.Arguments.Add(destinationArg);
        command.Options.Add(usernameOption);
        command.Options.Add(passwordOption);
        command.Options.Add(expiryOption);
        command.Options.Add(durationOption);
        command.Options.Add(keepOption);
        command.Options.Add(hepOption);
        AddCommonOptions(command);

        command.SetAction((parseResult, cancellationToken) => RunAsync(
            parseResult.GetValue(destinationArg)!,
            parseResult.GetValue(usernameOption),
            parseResult.GetValue(passwordOption),
            parseResult.GetValue(expiryOption),
            parseResult.GetValue(durationOption),
            parseResult.GetValue(keepOption),
            parseResult.GetValue(hepOption),
            parseResult.GetValue(TimeoutOption),
            parseResult.GetValue(JsonOption),
            parseResult.GetValue(VerboseOption),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(string destination, string? username, string? password, int expiry,
        int durationSeconds, bool keep, string? hep, int timeoutSeconds, bool asJson, bool verbose, CancellationToken ct)
    {
        using var loggerFactory = InitLogging(verbose);
        var logger = loggerFactory.CreateLogger(nameof(SipRegisterCommand));

        if (!SipDestination.TryParse(destination, out var dstUri, out var parseError))
        {
            return WriteResult(asJson,
                new RegisterResult(false, destination, string.Empty, expiry, 0, false, parseError),
                ExitCodes.InvalidArgument);
        }

        username ??= dstUri.User;

        if (string.IsNullOrWhiteSpace(username))
        {
            return WriteResult(asJson,
                new RegisterResult(false, dstUri.ToString(), string.Empty, expiry, 0, false,
                    "A username is required (--username or a user part on the destination)."),
                ExitCodes.InvalidArgument);
        }

        // The registrar is the host of the destination; the account AOR is username@host.
        string registrar = dstUri.ToString();
        string aor = $"{username}@{dstUri.Host}";

        using var hepCapture = HepCapture.Create(hep, logger, out string? hepError);

        if (hepError != null)
        {
            return WriteResult(asJson,
                new RegisterResult(false, registrar, aor, expiry, 0, false, hepError),
                ExitCodes.InvalidArgument);
        }

        var sipTransport = new SIPTransport();
        hepCapture?.Attach(sipTransport);

        if (verbose)
        {
            sipTransport.EnableTraceLogs();
        }

        var regUserAgent = new SIPRegistrationUserAgent(sipTransport, username, password ?? string.Empty, registrar, expiry);

        var outcome = new TaskCompletionSource<(bool Success, string? Error)>(TaskCreationOptions.RunContinuationsAsynchronously);

        regUserAgent.RegistrationSuccessful += (uri, resp) =>
        {
            logger.LogDebug("Registration successful for {Aor}.", uri);
            outcome.TrySetResult((true, null));
        };
        regUserAgent.RegistrationFailed += (uri, resp, err) => outcome.TrySetResult((false, err));
        regUserAgent.RegistrationTemporaryFailure += (uri, resp, err) => outcome.TrySetResult((false, err));

        var stopwatch = Stopwatch.StartNew();
        bool unregistered = false;

        try
        {
            regUserAgent.Start();

            var completed = await Task.WhenAny(outcome.Task, Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), ct)).ConfigureAwait(false);

            if (completed != outcome.Task)
            {
                return WriteResult(asJson,
                    new RegisterResult(false, registrar, aor, expiry, stopwatch.ElapsedMilliseconds, false,
                        ct.IsCancellationRequested ? "Cancelled." : $"No registration response within {timeoutSeconds}s."),
                    ExitCodes.Timeout);
            }

            var (success, error) = await outcome.Task.ConfigureAwait(false);

            if (!success)
            {
                return WriteResult(asJson,
                    new RegisterResult(false, registrar, aor, expiry, stopwatch.ElapsedMilliseconds, false, error),
                    ExitCodes.Failed);
            }

            // Hold the registration active for the requested duration. The user agent refreshes it
            // in the background.
            if (durationSeconds > 0)
            {
                logger.LogDebug("Holding registration for {Duration}s.", durationSeconds);
                await Task.Delay(TimeSpan.FromSeconds(durationSeconds), ct).ConfigureAwait(false);
            }

            return WriteResult(asJson,
                new RegisterResult(true, registrar, aor, expiry, stopwatch.ElapsedMilliseconds, !keep, null),
                ExitCodes.Ok);
        }
        catch (OperationCanceledException)
        {
            return WriteResult(asJson,
                new RegisterResult(false, registrar, aor, expiry, stopwatch.ElapsedMilliseconds, false, "Cancelled."),
                ExitCodes.Timeout);
        }
        catch (Exception excp)
        {
            return WriteResult(asJson,
                new RegisterResult(false, registrar, aor, expiry, stopwatch.ElapsedMilliseconds, false, excp.Message),
                ExitCodes.TransportError);
        }
        finally
        {
            // Stop(true) sends a zero expiry re-register to remove the binding; Stop(false) leaves it.
            regUserAgent.Stop(!keep);
            unregistered = !keep;
            // Give a removal register a moment to be sent before the transport is torn down.
            if (unregistered)
            {
                await Task.Delay(500, CancellationToken.None).ConfigureAwait(false);
            }
            sipTransport.Shutdown();
        }
    }

    private static int WriteResult(bool asJson, RegisterResult result, int exitCode)
    {
        if (asJson)
        {
            WriteJson(result);
        }
        else if (result.Success)
        {
            string removal = result.Unregistered ? " (removed on exit)" : " (left in place)";
            Console.WriteLine($"Registered {result.Aor} with {result.Registrar} in {result.DurationMs}ms, expiry {result.RequestedExpiry}s{removal}.");
        }
        else
        {
            Console.Error.WriteLine($"Registration of {result.Aor} with {result.Registrar} failed: {result.Error}");
        }

        return exitCode;
    }
}
