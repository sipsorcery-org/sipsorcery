//-----------------------------------------------------------------------------
// Filename: SctpErrorChunk.cs
//
// Description: Represents the SCTP ERROR chunk.
//
// Remarks:
// Defined in section 3.3.10 of RFC4960:
// https://tools.ietf.org/html/rfc4960#section-3.3.10
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 01 Apr 2021	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    /// <summary>
    /// An endpoint sends this chunk to its peer endpoint to notify it of
    /// certain error conditions. It contains one or more error causes. An
    /// Operation Error is not considered fatal in and of itself, but may be
    /// used with an ABORT chunk to report a fatal condition.
    /// </summary>
    public class SctpErrorChunk : SctpChunk
    {
        public List<ISctpErrorCause> ErrorCauses { get; private set; }

        private SctpErrorChunk() : base(SctpChunkType.ERROR)
        {
            ErrorCauses = new List<ISctpErrorCause>();
        }

        /// <summary>
        /// Creates a new ERROR chunk.
        /// </summary>
        /// <param name="errorCauseCode">The initial error cause code to set on this chunk.</param>
        public SctpErrorChunk(SctpErrorCauseCode errorCauseCode) : 
            this(new SctpError(errorCauseCode))
        { }

        /// <summary>
        /// Creates a new ERROR chunk.
        /// </summary>
        /// <param name="errorCause">The initial error cause to set on this chunk.</param>
        public SctpErrorChunk(ISctpErrorCause errorCause) : base(SctpChunkType.ERROR)
        {
            ErrorCauses = new List<ISctpErrorCause>();
            ErrorCauses.Add(errorCause);
        }

        /// <summary>
        /// Adds an additional error cause parameter to the chunk.
        /// </summary>
        /// <param name="errorCause">The additional error cause to add to the chunk.</param>
        public void AddErrorCause(ISctpErrorCause errorCause)
        {
            ErrorCauses.Add(errorCause);
        }

        /// <summary>
        /// Calculates the padded length for the chunk.
        /// </summary>
        /// <param name="padded">If true the length field will be padded to a 4 byte boundary.</param>
        /// <returns>The padded length of the chunk.</returns>
        public override ushort GetChunkLength(bool padded)
        {
            // TODO.
            return SCTP_CHUNK_HEADER_LENGTH;
        }

        /// <summary>
        /// Serialises the SHUTDOWN chunk to a pre-allocated buffer.
        /// </summary>
        /// <param name="buffer">The buffer to write the serialised chunk bytes to. It
        /// must have the required space already allocated.</param>
        /// <param name="posn">The position in the buffer to write to.</param>
        /// <returns>The number of bytes, including padding, written to the buffer.</returns>
        public override ushort WriteTo(byte[] buffer, int posn)
        {
            WriteChunkHeader(buffer, posn);
            // TODO.
            return GetChunkLength(true);
        }

        /// <summary>
        /// Parses the ERROR chunk fields.
        /// </summary>
        /// <param name="buffer">The buffer holding the serialised chunk.</param>
        /// <param name="posn">The position to start parsing at.</param>
        public static SctpErrorChunk ParseChunk(byte[] buffer, int posn)
        {
            var errorChunk = new SctpErrorChunk();
            // TODO.
            return errorChunk;
        }
    }
}
