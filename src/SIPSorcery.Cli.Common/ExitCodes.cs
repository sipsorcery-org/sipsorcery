//-----------------------------------------------------------------------------
// Filename: ExitCodes.cs
//
// Description: The process exit codes every verb returns, shared by both the
// SIPSorcery.Cli and SIPSorcery.Diagnostics tools. Stable and meaningful so
// scripts and agents can branch on the outcome without parsing output.
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

namespace SIPSorcery.Cli.Common;

public static class ExitCodes
{
    /// <summary>The operation succeeded.</summary>
    public const int Ok = 0;

    /// <summary>The operation ran but did not achieve its goal (e.g. connected but no media flowed).</summary>
    public const int Failed = 1;

    /// <summary>A supplied argument or option was invalid.</summary>
    public const int InvalidArgument = 2;

    /// <summary>The operation did not complete within the allotted time.</summary>
    public const int Timeout = 3;

    /// <summary>A transport/network level error prevented the operation (DNS, connect, TLS, etc.).</summary>
    public const int TransportError = 4;
}
