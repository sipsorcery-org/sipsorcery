//-----------------------------------------------------------------------------
// Filename: SctpAbortChunk.cs
//
// Description: Represents the SCTP ABORT chunk.
//
// Remarks:
// Defined in section 3.3.7 of RFC4960:
// https://tools.ietf.org/html/rfc4960#section-3.3.7
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 05 Apr 2021	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

namespace SIPSorcery.Net;

/// <summary>
/// The ABORT chunk is sent to the peer of an association to close the
/// association.The ABORT chunk may contain Cause Parameters to inform
/// the receiver about the reason of the abort.DATA chunks MUST NOT be
/// bundled with ABORT.Control chunks (except for INIT, INIT ACK, and
/// SHUTDOWN COMPLETE) MAY be bundled with an ABORT, but they MUST be
/// placed before the ABORT in the SCTP packet or they will be ignored by
/// the receiver.
/// </summary>
/// <remarks>
/// https://tools.ietf.org/html/rfc4960#section-3.3.7
/// </remarks>
public class SctpAbortChunk : SctpErrorChunk
{
    /// <summary>
    /// Creates a new ABORT chunk.
    /// </summary>
    /// <param name="verificationTagBit">If set to true sets a bit in the chunk header to indicate
    /// the sender filled in the Verification Tag expected by the peer.</param>
    public SctpAbortChunk(bool verificationTagBit) :
        base(SctpChunkType.ABORT, verificationTagBit)
    { }

    /// <summary>
    /// Gets the user supplied abort reason if available.
    /// </summary>
    /// <returns>The abort reason or null if not present.</returns>
    public string? GetAbortReason()
    {
        foreach (var errorCause in ErrorCauses)
        {
            if (errorCause.CauseCode == SctpErrorCauseCode.UserInitiatedAbort)
            {
                var userAbort = (SctpErrorUserInitiatedAbort)errorCause;
                return userAbort.AbortReason;
            }

            if (errorCause.CauseCode == SctpErrorCauseCode.ProtocolViolation)
            {
                var protoViolation = (SctpErrorProtocolViolation)errorCause;
                return protoViolation.AdditionalInformation;
            }
        }

        return null;
    }
}
