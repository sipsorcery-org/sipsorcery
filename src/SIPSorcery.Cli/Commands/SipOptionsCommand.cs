//-----------------------------------------------------------------------------
// Filename: SipOptionsCommand.cs
//
// Description: The "sipsorcery sip options" verb. Sends a SIP OPTIONS request
// to a destination and reports the response. The SIP equivalent of ping:
// proves the server is up, reachable on the chosen transport and parsing
// requests, and measures the round trip time.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 12 Jun 2026	Aaron Clauson	Created, Wexford, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.CommandLine;
using System.Diagnostics;
using System.Net.Sockets;
using SIPSorcery.SIP;

namespace SIPSorcery.Cli.Commands;

public sealed class SipOptionsCommand : CommandBase
{
    private const int DEFAULT_TIMEOUT_SECONDS = 5;

    /// <summary>
    /// The result shape written to stdout with --json. Stable field names; additive changes only.
    /// </summary>
    private sealed record OptionsResult(
        bool Success,
        string Destination,
        int? StatusCode,
        string? ReasonPhrase,
        string? Server,
        string? RemoteEndPoint,
        long? DurationMs,
        string? Error);

    public SipOptionsCommand() : base(DEFAULT_TIMEOUT_SECONDS)
    { }

    public override Command Build()
    {
        var destinationArg = new Argument<string>("destination")
        {
            Description = "The SIP destination in the form [sip:|sips:|udp:|tcp:|tls:|ws:|wss:][user@]host[:port][;transport=x], " +
                          "e.g. music@iptel.org, tcp:sip.example.com:5060, sips:secure.example.com."
        };

        var command = new Command("options", "Send a SIP OPTIONS request and report the response (SIP ping).");
        command.Arguments.Add(destinationArg);
        AddCommonOptions(command);

        command.SetAction((parseResult, cancellationToken) => RunAsync(
            parseResult.GetValue(destinationArg)!,
            parseResult.GetValue(TimeoutOption),
            parseResult.GetValue(JsonOption),
            parseResult.GetValue(VerboseOption),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(string destination, int timeoutSeconds, bool asJson, bool verbose, CancellationToken ct)
    {
        using var loggerFactory = InitLogging(verbose);

        if (!TryParseDestination(destination, out var dstUri, out var parseError))
        {
            return WriteResult(asJson,
                new OptionsResult(false, destination, null, null, null, null, null, parseError),
                ExitCodes.InvalidArgument);
        }

        var sipTransport = new SIPTransport();

        if (verbose)
        {
            sipTransport.EnableTraceLogs();
        }

        try
        {
            var optionsRequest = SIPRequest.GetRequest(SIPMethodsEnum.OPTIONS, dstUri);

            var gotResponse = new TaskCompletionSource<SIPResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            SIPEndPoint? responseRemoteEndPoint = null;

            sipTransport.SIPTransportResponseReceived += (localEndPoint, remoteEndPoint, response) =>
            {
                if (response.Header.CSeqMethod == SIPMethodsEnum.OPTIONS && response.Header.CallId == optionsRequest.Header.CallId)
                {
                    responseRemoteEndPoint = remoteEndPoint;
                    gotResponse.TrySetResult(response);
                }

                return Task.CompletedTask;
            };

            var stopwatch = Stopwatch.StartNew();

            var sendResult = await sipTransport.SendRequestAsync(optionsRequest, true).ConfigureAwait(false);

            if (sendResult != SocketError.Success)
            {
                return WriteResult(asJson,
                    new OptionsResult(false, dstUri.ToString(), null, null, null, null, stopwatch.ElapsedMilliseconds,
                        $"The send failed with socket error {sendResult}."),
                    ExitCodes.TransportError);
            }

            var completed = await Task.WhenAny(gotResponse.Task, Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), ct)).ConfigureAwait(false);

            if (completed != gotResponse.Task)
            {
                return WriteResult(asJson,
                    new OptionsResult(false, dstUri.ToString(), null, null, null, null, stopwatch.ElapsedMilliseconds,
                        ct.IsCancellationRequested ? "Cancelled." : $"No response received within {timeoutSeconds}s."),
                    ExitCodes.Timeout);
            }

            stopwatch.Stop();

            var sipResponse = await gotResponse.Task.ConfigureAwait(false);
            int statusCode = (int)sipResponse.Status;
            bool success = statusCode >= 200 && statusCode < 300;

            var result = new OptionsResult(
                success,
                dstUri.ToString(),
                statusCode,
                sipResponse.ReasonPhrase,
                !string.IsNullOrWhiteSpace(sipResponse.Header.Server) ? sipResponse.Header.Server : sipResponse.Header.UserAgent,
                responseRemoteEndPoint?.ToString(),
                stopwatch.ElapsedMilliseconds,
                null);

            return WriteResult(asJson, result, success ? ExitCodes.Ok : ExitCodes.Failed);
        }
        catch (Exception excp)
        {
            return WriteResult(asJson,
                new OptionsResult(false, dstUri.ToString(), null, null, null, null, null, excp.Message),
                ExitCodes.TransportError);
        }
        finally
        {
            sipTransport.Shutdown();
        }
    }

    /// <summary>
    /// Accepts both SIP URIs (sip:100@host, music@host) and serialised SIP end points
    /// (udp:host:port, tls:host). The same convention as the sipcmdline example.
    /// </summary>
    private static bool TryParseDestination(string destination, out SIPURI uri, out string? error)
    {
        uri = SIPURI.None;
        error = null;

        try
        {
            // SIPURI.TryParse is lenient, e.g. it accepts host names containing spaces, so apply
            // a sanity check to route nonsense to an invalid argument error rather than a DNS failure.
            if (!HasTransportPrefix(destination) && SIPURI.TryParse(destination, out var parsedUri)
                && !string.IsNullOrWhiteSpace(parsedUri.Host) && !parsedUri.Host.Contains(' '))
            {
                uri = parsedUri;
                return true;
            }

            var endPoint = SIPEndPoint.ParseSIPEndPoint(destination);
            uri = new SIPURI(SIPSchemesEnum.sip, endPoint);
            return true;
        }
        catch
        {
            error = $"Could not parse \"{destination}\" as a SIP URI or end point.";
            return false;
        }
    }

    private static bool HasTransportPrefix(string destination) =>
        destination.StartsWith("udp:", StringComparison.OrdinalIgnoreCase) ||
        destination.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase) ||
        destination.StartsWith("tls:", StringComparison.OrdinalIgnoreCase) ||
        destination.StartsWith("ws:", StringComparison.OrdinalIgnoreCase) ||
        destination.StartsWith("wss:", StringComparison.OrdinalIgnoreCase);

    private static int WriteResult(bool asJson, OptionsResult result, int exitCode)
    {
        if (asJson)
        {
            WriteJson(result);
        }
        else if (result.StatusCode != null)
        {
            string server = result.Server != null ? $" ({result.Server})" : string.Empty;
            Console.WriteLine($"{result.StatusCode} {result.ReasonPhrase} from {result.RemoteEndPoint} in {result.DurationMs}ms{server}.");
        }
        else
        {
            Console.Error.WriteLine($"OPTIONS to {result.Destination} failed: {result.Error}");
        }

        return exitCode;
    }
}
