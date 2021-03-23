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
        ECNE = 12,
        CWR = 13,
        SHUTDOWN_COMPLETE = 14,
        AUTH = 15,
        PKTDROP = 129,
        RE_CONFIG = 130,
        FORWARDTSN = 192,
        ASCONF = 193,
        ASCONF_ACK = 128,
    }

    public class SctpChunk
    {
        public const int SCTP_CHUNK_HEADER_LENGTH = 4;

        private static ILogger logger = SIPSorcery.LogFactory.CreateLogger<SctpChunk>();

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

        public List<SctpChunkParameter> VariableParameters = new List<SctpChunkParameter>();

        public SctpChunk(SctpChunkType chunkType)
        {
            ChunkType = (byte)chunkType;
        }

        /// <summary>
        /// This constructor is only intended to be used when parsing the specialised 
        /// chunk types. Because they are being parsed from a buffer nothing is known
        /// about them and this constructor allows starting from a blank slate.
        /// </summary>
        protected SctpChunk()
        { }

        /// <summary>
        /// Adds a variable parameter to a chunk. Variable parameters are those that do not
        /// occupy a fixed position in the chunk parameter list. Instead they are appended 
        /// as "variable length" parameters.
        /// </summary>
        /// <param name="chunkParameter">The chunk parameter to add to this chunk.</param>
        public void AddChunkParamter(SctpChunkParameter chunkParameter)
        {
            VariableParameters.Add(chunkParameter);
        }

        /// <summary>
        /// Calculates the length for the chunk. Chunks are required
        /// to be padded out to 4 byte boundaries. This method gets overridden 
        /// by specialised SCTP chunks that have their own fields that determine the length.
        /// </summary>
        /// <returns>The length of the chunk.</returns>
        public virtual ushort GetChunkLength()
        {
            return (ushort)(SCTP_CHUNK_HEADER_LENGTH 
                + (ChunkValue == null ? 0 : ChunkValue.Length));
        }

        /// <summary>
        /// Calculates the padded length for the chunk. Chunks are required
        /// to be padded out to 4 byte boundaries. This method gets overridden 
        /// by specialised SCTP chunks that have their own fields that determine the length.
        /// </summary>
        /// <returns>The length of the chunk.</returns>
        public ushort GetChunkPaddedLength()
        {
            return SctpPadding.PadTo4ByteBoundary(GetChunkLength());
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
            NetConvert.ToBuffer(GetChunkLength(), buffer, posn + 2);
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

            return GetChunkPaddedLength();
        }

        /// <summary>
        /// Parses a simple chunk and does not attempt to process any chunk value.
        /// This method is suitable when:
        ///  - the chunk type is not recognised.
        ///  - the chunk type consists only of the 4 byte header and has 
        ///    no fixed or variable parameters set.
        /// </summary>
        /// <param name="buffer">The buffer holding the serialised chunk.</param>
        /// <param name="posn">The position to start parsing at.</param>
        /// <returns>An SCTP chunk instance.</returns>
        public static SctpChunk ParseSimpleChunk(byte[] buffer, int posn)
        {
            var simpleChunk = new SctpChunk();
            ushort chunkLength = simpleChunk.ParseFirstWord(buffer, posn);
            if (chunkLength > SCTP_CHUNK_HEADER_LENGTH)
            {
                simpleChunk.ChunkValue = new byte[chunkLength - SCTP_CHUNK_HEADER_LENGTH];
                Buffer.BlockCopy(buffer, posn + SCTP_CHUNK_HEADER_LENGTH, simpleChunk.ChunkValue, 0, simpleChunk.ChunkValue.Length);
            }

            return simpleChunk;
        }

        /// <summary>
        /// Chunks can optionally contain variable length parameters. This method
        /// parses any variable length parameters from a chunk's value.
        /// </summary>
        /// <param name="buffer">The buffer holding the serialised chunk.</param>
        /// <param name="posn">The position in the buffer to start parsing variable length
        /// parameters from.</param>
        /// <param name="length">The length of the variable chunk parameters in the buffer.</param>
        /// <returns>A list of chunk parameters. Can be empty.</returns>
        public static List<SctpChunkParameter> ParseVariableParameters(byte[] buffer, int posn, int length)
        {
            List<SctpChunkParameter> chunkParams = new List<SctpChunkParameter>();

            int paramPosn = posn;

            while (paramPosn < posn + length)
            {
                var chunkParam = SctpChunkParameter.Parse(buffer, paramPosn);
                chunkParams.Add(chunkParam);
                paramPosn += chunkParam.GetParameterPaddedLength();
            }

            return chunkParams;
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
                    case SctpChunkType.DATA:
                        return SctpDataChunk.ParseChunk(buffer, posn);
                    case SctpChunkType.SACK:
                        return SctpSackChunk.ParseChunk(buffer, posn);
                    case SctpChunkType.COOKIE_ACK:
                    case SctpChunkType.COOKIE_ECHO:
                    case SctpChunkType.HEARTBEAT:
                    case SctpChunkType.HEARTBEAT_ACK:
                        return ParseSimpleChunk(buffer, posn);
                    case SctpChunkType.INIT:
                    case SctpChunkType.INIT_ACK:
                        return SctpInitChunk.ParseChunk(buffer, posn);
                    case SctpChunkType.SHUTDOWN:
                        return SctpShutdownChunk.ParseChunk(buffer, posn);
                    default:
                        logger.LogDebug($"TODO: Implement parsing logic for well known chunk type {(SctpChunkType)chunkType}.");
                        return ParseSimpleChunk(buffer, posn);
                }
            }

            // Didn't recognised the buffer as a well known chunk.
            // Return a simple chunk and leave it up to the application.
            logger.LogWarning($"SCTP chunk type of {chunkType} was not recognised.");

            return ParseSimpleChunk(buffer, posn);
        }
    }
}
