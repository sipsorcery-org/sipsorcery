//-----------------------------------------------------------------------------
// Filename: SctpChunk.cs
//
// Description: Represents the common fields of an SCTP chunk.
//
// Remarks:
// Defined in section 3 of RFC4960:
// https://tools.ietf.org/html/rfc4960#section-3.2.
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
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    public static class SctpPadding
    {
        public static ushort PadTo4ByteBoundary(int val)
        {
            return (ushort)(val % 4 == 0 ? val : val + 4 - val % 4);
        }
    }

    /// <summary>
    /// The values of the Chunk Types.
    /// </summary>
    /// <remarks>
    /// https://tools.ietf.org/html/rfc4960#section-3.2
    /// </remarks>
    public enum SctpChunkType : byte
    {
        DATA = 0,
        INIT = 1,
        INIT_ACK = 2,
        SACK = 3,
        HEARTBEAT = 4,
        HEARTBEAT_ACK = 5,
        ABORT = 6,
        SHUTDOWN = 7,
        SHUTDOWN_ACK = 8,
        ERROR = 9,
        COOKIE_ECHO = 10,
        COOKIE_ACK = 11,
        ECNE = 12,          // Not used (specified in the RFC for future use).
        CWR = 13,           // Not used (specified in the RFC for future use).
        SHUTDOWN_COMPLETE = 14,

        // Not defined in RFC4960.
        //AUTH = 15,
        //PKTDROP = 129,
        //RE_CONFIG = 130,
        //FORWARDTSN = 192,
        //ASCONF = 193,
        //ASCONF_ACK = 128,
    }

    /// <summary>
    /// The actions required for unrecognised chunks. The byte value corresponds to the highest 
    /// order two bits of the chunk type value.
    /// </summary>
    /// <remarks>
    /// https://tools.ietf.org/html/rfc4960#section-3.2
    /// </remarks>
    public enum SctpUnrecognisedChunkActions : byte
    {
        /// <summary>
        /// Stop processing this SCTP packet and discard it, do not process any further chunks within it.
        /// </summary>
        Stop = 0x00,

        /// <summary>
        /// Stop processing this SCTP packet and discard it, do not process any further chunks within it, and report the
        /// unrecognized chunk in an 'Unrecognized Chunk Type'.
        /// </summary>
        StopAndReport = 0x01,

        /// <summary>
        /// Skip this chunk and continue processing.
        /// </summary>
        Skip = 0x02,

        /// <summary>
        /// Skip this chunk and continue processing, but report in an ERROR chunk using the 'Unrecognized Chunk Type' cause of
        /// error.
        /// </summary>
        SkipAndReport = 0x03
    }

    public class SctpChunk
    {
        public const int SCTP_CHUNK_HEADER_LENGTH = 4;

        protected static ILogger logger = SIPSorcery.LogFactory.CreateLogger<SctpChunk>();

        /// <summary>
        /// This field identifies the type of information contained in the
        /// Chunk Value field.
        /// </summary>
        public byte ChunkType;

        /// <summary>
        /// The usage of these bits depends on the Chunk type as given by the
        /// Chunk Type field.Unless otherwise specified, they are set to 0
        /// on transmit and are ignored on receipt.
        /// </summary>
        public byte ChunkFlags;

        /// <summary>
        /// The Chunk Value field contains the actual information to be
        /// transferred in the chunk.The usage and format of this field is
        /// dependent on the Chunk Type.
        /// </summary>
        public byte[] ChunkValue;

        /// <summary>
        /// If recognised returns the known chunk type. If not recognised returns null.
        /// </summary>
        public SctpChunkType? KnownType
        {
            get
            {
                if (Enum.IsDefined(typeof(SctpChunkType), ChunkType))
                {
                    return (SctpChunkType)ChunkType;
                }
                else
                {
                    return null;
                }
            }
        }

        /// <summary>
        /// Records any unrecognised parameters received from the remote peer and are classified
        /// as needing to be reported. These can be sent back to the remote peer if needed.
        /// </summary>
        public List<SctpTlvChunkParameter> UnrecognizedPeerParameters = new List<SctpTlvChunkParameter>();

        public SctpChunk(SctpChunkType chunkType, byte chunkFlags = 0x00)
        {
            ChunkType = (byte)chunkType;
            ChunkFlags = chunkFlags;
        }

        /// <summary>
        /// This constructor is only intended to be used when parsing the specialised 
        /// chunk types. Because they are being parsed from a buffer nothing is known
        /// about them and this constructor allows starting from a blank slate.
        /// </summary>
        protected SctpChunk()
        { }

        /// <summary>
        /// Calculates the length for the chunk. Chunks are required
        /// to be padded out to 4 byte boundaries. This method gets overridden 
        /// by specialised SCTP chunks that have their own fields that determine the length.
        /// </summary>
        /// <param name="padded">If true the length field will be padded to a 4 byte boundary.</param>
        /// <returns>The length of the chunk.</returns>
        public virtual ushort GetChunkLength(bool padded)
        {
            var len = (ushort)(SCTP_CHUNK_HEADER_LENGTH
                + (ChunkValue == null ? 0 : ChunkValue.Length));

            return (padded) ? SctpPadding.PadTo4ByteBoundary(len) : len;
        }

        /// <summary>
        /// The first 32 bits of all chunks represent the same 3 fields. This method
        /// parses those fields and sets them on the current instance.
        /// </summary>
        /// <param name="buffer">The buffer holding the serialised chunk.</param>
        /// <param name="posn">The position in the buffer that indicates the start of the chunk.</param>
        /// <returns>The chunk length value.</returns>
        public ushort ParseFirstWord(byte[] buffer, int posn)
        {
            ChunkType = buffer[posn];
            ChunkFlags = buffer[posn + 1];
            ushort chunkLength = NetConvert.ParseUInt16(buffer, posn + 2);

            if (chunkLength > 0 && buffer.Length < posn + chunkLength)
            {
                // The buffer was not big enough to supply the specified chunk length.
                int bytesRequired = chunkLength;
                int bytesAvailable = buffer.Length - posn;
                throw new ApplicationException($"The SCTP chunk buffer was too short. Required {bytesRequired} bytes but only {bytesAvailable} available.");
            }

            return chunkLength;
        }

        /// <summary>
        /// Writes the chunk header to the buffer. All chunks use the same three
        /// header fields.
        /// </summary>
        /// <param name="buffer">The buffer to write the chunk header to.</param>
        /// <param name="posn">The position in the buffer to write at.</param>
        /// <returns>The padded length of this chunk.</returns>
        protected void WriteChunkHeader(byte[] buffer, int posn)
        {
            buffer[posn] = ChunkType;
            buffer[posn + 1] = ChunkFlags;
            NetConvert.ToBuffer(GetChunkLength(false), buffer, posn + 2);
        }

        /// <summary>
        /// Serialises the chunk to a pre-allocated buffer. This method gets overridden 
        /// by specialised SCTP chunks that have their own parameters and need to be serialised
        /// differently.
        /// </summary>
        /// <param name="buffer">The buffer to write the serialised chunk bytes to. It
        /// must have the required space already allocated.</param>
        /// <param name="posn">The position in the buffer to write to.</param>
        /// <returns>The number of bytes, including padding, written to the buffer.</returns>
        public virtual ushort WriteTo(byte[] buffer, int posn)
        {
            WriteChunkHeader(buffer, posn);

            if (ChunkValue?.Length > 0)
            {
                Buffer.BlockCopy(ChunkValue, 0, buffer, posn + SCTP_CHUNK_HEADER_LENGTH, ChunkValue.Length);
            }

            return GetChunkLength(true);
        }

        /// <summary>
        /// Handler for processing an unrecognised chunk parameter.
        /// </summary>
        /// <param name="chunkParameter">The Type-Length-Value (TLV) formatted chunk that was
        /// not recognised.</param>
        /// <returns>True if further parameter parsing for this chunk should be stopped. 
        /// False to continue.</returns>
        public bool GotUnrecognisedParameter(SctpTlvChunkParameter chunkParameter)
        {
            bool stop = false;

            switch (chunkParameter.UnrecognisedAction)
            {
                case SctpUnrecognisedParameterActions.Stop:
                    stop = true;
                    break;
                case SctpUnrecognisedParameterActions.StopAndReport:
                    stop = true;
                    UnrecognizedPeerParameters.Add(chunkParameter);
                    break;
                case SctpUnrecognisedParameterActions.Skip:
                    break;
                case SctpUnrecognisedParameterActions.SkipAndReport:
                    UnrecognizedPeerParameters.Add(chunkParameter);
                    break;
            }

            return stop;
        }

        /// <summary>
        /// Parses a simple chunk and does not attempt to process any chunk value.
        /// This method is suitable when:
        ///  - the chunk type consists only of the 4 byte header and has 
        ///    no fixed or variable parameters set.
        /// </summary>
        /// <param name="buffer">The buffer holding the serialised chunk.</param>
        /// <param name="posn">The position to start parsing at.</param>
        /// <returns>An SCTP chunk instance.</returns>
        public static SctpChunk ParseBaseChunk(byte[] buffer, int posn)
        {
            var chunk = new SctpChunk();
            ushort chunkLength = chunk.ParseFirstWord(buffer, posn);
            if (chunkLength > SCTP_CHUNK_HEADER_LENGTH)
            {
                chunk.ChunkValue = new byte[chunkLength - SCTP_CHUNK_HEADER_LENGTH];
                Buffer.BlockCopy(buffer, posn + SCTP_CHUNK_HEADER_LENGTH, chunk.ChunkValue, 0, chunk.ChunkValue.Length);
            }

            return chunk;
        }

        /// <summary>
        /// Chunks can optionally contain Type-Length-Value (TLV) parameters. This method
        /// parses any variable length parameters from a chunk's value.
        /// </summary>
        /// <param name="buffer">The buffer holding the serialised chunk.</param>
        /// <param name="posn">The position in the buffer to start parsing variable length
        /// parameters from.</param>
        /// <param name="length">The length of the TLV chunk parameters in the buffer.</param>
        /// <returns>A list of chunk parameters. Can be empty.</returns>
        public static IEnumerable<SctpTlvChunkParameter> GetParameters(byte[] buffer, int posn, int length)
        {
            int paramPosn = posn;

            while (paramPosn < posn + length)
            {
                var chunkParam = SctpTlvChunkParameter.ParseTlvParameter(buffer, paramPosn);

                yield return chunkParam;

                paramPosn += chunkParam.GetParameterLength(true);
            }
        }

        /// <summary>
        /// Parses an SCTP chunk from a buffer.
        /// </summary>
        /// <param name="buffer">The buffer holding the serialised chunk.</param>
        /// <param name="posn">The position to start parsing at.</param>
        /// <returns>An SCTP chunk instance.</returns>
        public static SctpChunk Parse(byte[] buffer, int posn)
        {
            if (buffer.Length < posn + SCTP_CHUNK_HEADER_LENGTH)
            {
                throw new ApplicationException("Buffer did not contain the minimum of bytes for an SCTP chunk.");
            }

            byte chunkType = buffer[posn];

            if (Enum.IsDefined(typeof(SctpChunkType), chunkType))
            {
                switch ((SctpChunkType)chunkType)
                {
                    case SctpChunkType.ABORT:
                        return SctpAbortChunk.ParseChunk(buffer, posn, true);
                    case SctpChunkType.DATA:
                        return SctpDataChunk.ParseChunk(buffer, posn);
                    case SctpChunkType.ERROR:
                        return SctpErrorChunk.ParseChunk(buffer, posn, false);
                    case SctpChunkType.SACK:
                        return SctpSackChunk.ParseChunk(buffer, posn);
                    case SctpChunkType.COOKIE_ACK:
                    case SctpChunkType.COOKIE_ECHO:
                    case SctpChunkType.HEARTBEAT:
                    case SctpChunkType.HEARTBEAT_ACK:
                    case SctpChunkType.SHUTDOWN_ACK:
                    case SctpChunkType.SHUTDOWN_COMPLETE:
                        return ParseBaseChunk(buffer, posn);
                    case SctpChunkType.INIT:
                    case SctpChunkType.INIT_ACK:
                        return SctpInitChunk.ParseChunk(buffer, posn);
                    case SctpChunkType.SHUTDOWN:
                        return SctpShutdownChunk.ParseChunk(buffer, posn);
                    default:
                        logger.LogDebug("TODO: Implement parsing logic for well known chunk type {ChunkType}.", (SctpChunkType)chunkType);
                        return ParseBaseChunk(buffer, posn);
                }
            }

            // Shouldn't reach this point. The SCTP packet parsing logic checks if the chunk is
            // recognised before attempting to parse it.
            throw new ApplicationException($"SCTP chunk type of {chunkType} was not recognised.");
        }

        /// <summary>
        /// Extracts the padded length field from a serialised chunk buffer.
        /// </summary>
        /// <param name="buffer">The buffer holding the serialised chunk.</param>
        /// <param name="posn">The start position of the serialised chunk.</param>
        /// <param name="padded">If true the length field will be padded to a 4 byte boundary.</param>
        /// <returns>The padded length of the serialised chunk.</returns>
        public static uint GetChunkLengthFromHeader(byte[] buffer, int posn, bool padded)
        {
            ushort len = NetConvert.ParseUInt16(buffer, posn + 2);
            return (padded) ? SctpPadding.PadTo4ByteBoundary(len) : len;
        }

        /// <summary>
        /// If this chunk is unrecognised then this field dictates how the remainder of the 
        /// SCTP packet should be handled.
        /// </summary>
        public static SctpUnrecognisedChunkActions GetUnrecognisedChunkAction(ushort chunkType) =>
            (SctpUnrecognisedChunkActions)(chunkType >> 14 & 0x03);

        /// <summary>
        /// Copies an unrecognised chunk to a byte buffer and returns it. This method is
        /// used to assist in reporting unrecognised chunk types.
        /// </summary>
        /// <param name="buffer">The buffer containing the chunk.</param>
        /// <param name="posn">The position in the buffer that the unrecognised chunk starts.</param>
        /// <returns>A new buffer containing a copy of the chunk.</returns>
        public static byte[] CopyUnrecognisedChunk(byte[] buffer, int posn)
        {
            byte[] unrecognised = new byte[SctpChunk.GetChunkLengthFromHeader(buffer, posn, true)];
            Buffer.BlockCopy(buffer, posn, unrecognised, 0, unrecognised.Length);
            return unrecognised;
        }
    }
}
