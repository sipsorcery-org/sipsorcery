//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: Entry point for the "sipsorcery" CLI: the application/streams tool
// (as opposed to the "sipsorcery-diags" probe/test harness). It wires the
// noun/verb tree for the realtime fabrics and the stream router.
//
// Design rules for every verb (shared with SIPSorcery.Diagnostics):
//  - Human readable output to stdout by default, a single JSON object to stdout
//    with --json. Logs and diagnostics always go to stderr so JSON stays pipeable.
//  - Meaningful exit codes, see ExitCodes.
//  - No hidden interactivity and a timeout on anything that waits.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 18 Jun 2026	Aaron Clauson	Created, Wexford, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.CommandLine;
using System.CommandLine.Help;
using SIPSorcery.Cli.Commands;

// A plain Command is used as the root rather than RootCommand so the name shown in help is the
// installed tool command, "sipsorcery", instead of the entry assembly name (SIPSorcery.Cli, which
// cannot be "sipsorcery" because of the SIPSorcery.dll collision, see the csproj). The help and
// version options RootCommand would normally contribute are added explicitly.
var rootCommand = new Command("sipsorcery",
    "SIPSorcery CLI: route and bridge live media streams between SIP, WebRTC and the realtime fabrics.");
rootCommand.Options.Add(new HelpOption());
rootCommand.Options.Add(new VersionOption());

var cloudflareCommand = new Command("cloudflare", "Cloudflare Realtime operations: SFU publish check.");
cloudflareCommand.Subcommands.Add(new CloudflareSfuCommand().Build());
rootCommand.Subcommands.Add(cloudflareCommand);

// route is a top-level verb (not a noun/verb pair): it wires a source edge to one or more sink edges
// over a stream graph. It is the seed of the stream-routing flagship. LiveKit and OpenAI are reached
// through route/bridge edges (livekit:<room>, bridge web openai), so they have no standalone verbs.
rootCommand.Subcommands.Add(new RouteCommand().Build());

// bridge is the duplex counterpart to route: it connects two endpoints both ways (e.g. talk to a
// voice agent in a browser with "bridge web agent").
rootCommand.Subcommands.Add(new BridgeCommand().Build());

return await rootCommand.Parse(args).InvokeAsync();
