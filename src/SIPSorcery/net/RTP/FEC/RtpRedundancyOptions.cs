//-----------------------------------------------------------------------------
// Filename: RtpRedundancyOptions.cs
// Description: Optional RTP redundancy capabilities for a local video track.
// License: BSD 3-Clause, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;

namespace SIPSorcery.Net
{
    /// <summary>
    /// RTP redundancy advertised by a local video track. The current implementation
    /// supports RED and ULPFEC together; neither option is supported independently.
    /// </summary>
    [Flags]
    public enum RtpRedundancyOptions
    {
        None = 0,
        Red = 1,
        Ulpfec = 2
    }
}
