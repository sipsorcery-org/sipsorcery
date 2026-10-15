//-----------------------------------------------------------------------------
// Filename: ExitCodes.cs
//
// Description: Process exit codes for the sipsorcery command line tool. Kept
// stable so scripts and agents can branch on the failure mode.
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

namespace SIPSorcery.Diagnostics;

public static class ExitCodes
{
    /// <summary>The operation completed and got the expected result.</summary>
    public const int Ok = 0;

    /// <summary>The operation completed but the result was a failure, e.g. an error SIP response.</summary>
    public const int Failed = 1;

    /// <summary>An argument could not be parsed. System.CommandLine also uses 1 for usage errors;
    /// this code is for values that parse as strings but are semantically invalid.</summary>
    public const int InvalidArgument = 2;

    /// <summary>No response was received within the timeout.</summary>
    public const int Timeout = 3;

    /// <summary>A network send failed, e.g. socket error or no route.</summary>
    public const int TransportError = 4;
}
