//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: Entry point for the sipsorcery command line tool. Wires up the
// noun/verb command tree. Each verb lives in its own class under Commands.
//
// Design rules for every verb:
//  - Human readable output to stdout by default, a single JSON object to
//    stdout with --json. Logs and diagnostics always go to stderr so JSON
//    output remains pipeable.
//  - Meaningful exit codes, see ExitCodes.
//  - No hidden interactivity and a timeout on anything that waits.
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
using System.CommandLine.Help;
using SIPSorcery.Diagnostics.Commands;

// A plain Command is used as the root rather than RootCommand so the name shown in help is
// the installed tool command, "sipsorcery-diags", instead of the entry assembly name. RootCommand
// derives its name from the assembly, which cannot be renamed (see the note in the csproj),
// and Command.Name is read only. The help and version options RootCommand would normally
// contribute are added explicitly.
var rootCommand = new Command("sipsorcery-diags",
    "SIP and WebRTC diagnostics from the command line, built on the SIPSorcery library.");
rootCommand.Options.Add(new HelpOption());
rootCommand.Options.Add(new VersionOption());

var sipCommand = new Command("sip", "SIP operations: send requests, make test calls, registrations.");
sipCommand.Subcommands.Add(new SipOptionsCommand().Build());
sipCommand.Subcommands.Add(new SipCallCommand().Build());
sipCommand.Subcommands.Add(new SipRegisterCommand().Build());
sipCommand.Subcommands.Add(new SipLoadCommand().Build());
rootCommand.Subcommands.Add(sipCommand);

var stunCommand = new Command("stun", "STUN operations: public address lookups, NAT diagnostics.");
stunCommand.Subcommands.Add(new StunLookupCommand().Build());
rootCommand.Subcommands.Add(stunCommand);

var iceCommand = new Command("ice", "ICE operations: candidate gathering, STUN/TURN connectivity probes.");
iceCommand.Subcommands.Add(new IceGatherCommand().Build());
rootCommand.Subcommands.Add(iceCommand);

var turnCommand = new Command("turn", "TURN operations: relay allocation checks.");
turnCommand.Subcommands.Add(new TurnAllocateCommand().Build());
rootCommand.Subcommands.Add(turnCommand);

var webrtcCommand = new Command("webrtc", "WebRTC operations: full connection probes with ICE, DTLS and media.");
webrtcCommand.Subcommands.Add(new WebRtcWhepCommand().Build());
webrtcCommand.Subcommands.Add(new WebRtcWhipCommand().Build());
webrtcCommand.Subcommands.Add(new WebRtcWhipServerCommand().Build());
webrtcCommand.Subcommands.Add(new WebRtcLoopbackCommand().Build());
webrtcCommand.Subcommands.Add(new WebRtcEchoCommand().Build());
webrtcCommand.Subcommands.Add(new WebRtcEchoServerCommand().Build());
webrtcCommand.Subcommands.Add(new WebRtcVideoBenchCommand().Build());
rootCommand.Subcommands.Add(webrtcCommand);

// The cloudflare, livekit, openai and route verbs live in the sibling "sipsorcery" tool
// (SIPSorcery.Cli), the application/streams CLI. This tool stays focused on probe/test/benchmark.

return await rootCommand.Parse(args).InvokeAsync();
