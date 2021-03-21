//-----------------------------------------------------------------------------
// Filename: SctpCookieEchoChunk.cs
//
// Description: Represents the SCTP COOKIE ECHO chunk.
//
// Remarks:
// Defined in section 3.3.11 of RFC4960:
// https://tools.ietf.org/html/rfc4960#section-3.3.11
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 18 Mar 2021	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;

namespace SIPSorcery.Net
{
    /// <summary>
    /// The COOKIE ECHO chunk is used only during the initialisation of an association.
    /// It is sent by the initiator of an association to its peer to complete
    /// the initialisation process.
    /// </summary>
    public class SctpCookieEchoChunk : SctpChunk
    {
        /// <summary>
        /// This field must contain the exact cookie received in the State
        /// Cookie parameter from the previous INIT ACK.
        /// </summary>
        public byte[] Cookie;

        public SctpCookieEchoChunk() : base(SctpChunkType.COOKIE_ECHO)
        { }

        /// <summary>
        /// Calculates the padded length for the chunk.
        /// </summary>
        /// <returns>The padded length of the chunk.</returns>
        public override ushort GetChunkLength()
        {
            return (ushort)(SCTP_CHUNK_HEADER_LENGTH 
                + ((Cookie != null) ? Cookie.Length : 0));
        }

        /// <summary>
        /// Serialises a COOKIE ECHO chunk to a pre-allocated buffer.
        /// </summary>
        /// <param name="buffer">The buffer to write the serialised chunk bytes to. It
        /// must have the required space already allocated.</param>
        /// <param name="posn">The position in the buffer to write to.</param>
        /// <returns>The number of bytes, including padding, written to the buffer.</returns>
        public override ushort WriteTo(byte[] buffer, int posn)
        {
            WriteChunkHeader(buffer, posn);

            // Write fixed parameters.
            int startPosn = posn + SCTP_CHUNK_HEADER_LENGTH;

            // Write the cookie
            if (Cookie != null)
            {
                Buffer.BlockCopy(Cookie, 0, buffer, startPosn, Cookie.Length);
            }

            return GetChunkPaddedLength();
        }

        /// <summary>
        /// Parses the COOKIE ECHO chunk fields.
        /// </summary>
        /// <param name="buffer">The buffer holding the serialised chunk.</param>
        /// <param name="posn">The position to start parsing at.</param>
        public static SctpCookieEchoChunk ParseChunk(byte[] buffer, int posn)
        {
            var cookieEchoChunk = new SctpCookieEchoChunk();
            var chunkLen = cookieEchoChunk.ParseFirstWord(buffer, posn);

            int startPosn = posn + SCTP_CHUNK_HEADER_LENGTH;

            cookieEchoChunk.Cookie = new byte[chunkLen - SCTP_CHUNK_HEADER_LENGTH];
            Buffer.BlockCopy(buffer, startPosn, cookieEchoChunk.Cookie, 0, cookieEchoChunk.Cookie.Length);

            return cookieEchoChunk;
        }
    }
}
