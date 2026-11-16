//-----------------------------------------------------------------------------
// Filename: SipCallSupport.cs
//
// Description: Plumbing shared by the CLI's long-running SIP calls (the route
// sip: sink and the two-way sip: bridge peer): tracing every SIP message for
// --verbose with the credentials masked, and answering the keep-alives some
// softphones send mid-call so they don't hang up on us.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 16 Nov 2026	Aaron Clauson	Created, Wexford, Ireland, from the bridge's sip: peer.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;

namespace SIPSorcery.Cli.Common;

public static class SipCallSupport
{
    // Mask the credential-bearing authorization headers so --verbose SIP traces never leak the digest
    // response/username. The challenge headers (WWW-Authenticate/Proxy-Authenticate) are left visible as
    // they carry no secret and help diagnose auth.
    private static readonly Regex AuthHeaderRegex =
        new(@"^(Authorization|Proxy-Authorization)\s*:.*$", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Logs every SIP message the transport sends or receives at debug level (--verbose): the whole
    /// INVITE/answer/re-INVITE, headers and SDP body, with the authorization headers redacted. The
    /// message string is built lazily so nothing is serialised when debug logging is off.
    /// </summary>
    public static void TraceMessages(SIPTransport transport, ILogger logger)
    {
        transport.SIPRequestOutTraceEvent += (local, remote, req) => LogSipMessage(logger, "sent to", remote, () => req.ToString());
        transport.SIPRequestInTraceEvent += (local, remote, req) => LogSipMessage(logger, "received from", remote, () => req.ToString());
        transport.SIPResponseInTraceEvent += (local, remote, resp) => LogSipMessage(logger, "received from", remote, () => resp.ToString());
        transport.SIPResponseOutTraceEvent += (local, remote, resp) => LogSipMessage(logger, "sent to", remote, () => resp.ToString());
    }

    /// <summary>
    /// Keeps a call up: some softphones (e.g. MicroSIP) send in-dialog INFO/OPTIONS keep-alives and
    /// hang up if they go unanswered. The user agent already answers in-dialog OPTIONS but never INFO,
    /// so this 200 OKs the rest. It runs alongside the user agent's own request handler (a shared,
    /// multicast transport event), so to avoid a double response it only steps in for INFO (never
    /// handled) and OPTIONS the agent will not handle (e.g. an out-of-dialog keep-alive).
    /// </summary>
    /// <param name="userAgent">The call's user agent, read per request since it is created after this is attached.</param>
    /// <param name="name">The edge's name for the log, e.g. "sip sink".</param>
    public static void AnswerKeepalives(SIPTransport transport, Func<SIPUserAgent?> userAgent, ILogger logger, string name)
    {
        transport.SIPTransportRequestReceived += async (local, remote, req) =>
        {
            if (req.Method != SIPMethodsEnum.INFO && req.Method != SIPMethodsEnum.OPTIONS)
            {
                return;
            }

            var dialogue = userAgent()?.Dialogue;
            bool agentAnswersIt = req.Method == SIPMethodsEnum.OPTIONS
                && dialogue != null
                && req.Header.CallId == dialogue.CallId
                && req.Header.From?.FromTag != null
                && req.Header.To?.ToTag != null;
            if (agentAnswersIt)
            {
                return;
            }

            try
            {
                var ok = SIPResponse.GetResponse(req, SIPResponseStatusCodesEnum.Ok, null);
                await transport.SendResponseAsync(ok).ConfigureAwait(false);
                logger.LogDebug("{Name} answered {Method} with 200 OK.", name, req.Method);
            }
            catch (Exception excp)
            {
                logger.LogDebug("{Name} failed to answer {Method}: {Error}", name, req.Method, excp.Message);
            }
        };
    }

    private static void LogSipMessage(ILogger logger, string direction, SIPEndPoint remote, Func<string> message)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            string redacted = AuthHeaderRegex.Replace(message(), "$1: <redacted>");
            logger.LogDebug("SIP message {Direction} {Remote}:\n{Message}", direction, remote, redacted);
        }
    }
}
