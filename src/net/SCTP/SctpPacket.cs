//-----------------------------------------------------------------------------
// Filename: SctpPacket.cs
//
// Description: Represents an SCTP packet.
//
// Remarks:
// Defined in section 3 of RFC4960:
// https://tools.ietf.org/html/rfc4960#section-3.
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
using System.Linq;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    public class CRC32C
    {
        private const uint INITIAL_POLYNOMIAL = 0x82f63b78;

        private static readonly uint[] _table = new uint[256];

        static CRC32C()
        {
            uint poly = INITIAL_POLYNOMIAL;
            for (uint i = 0; i < 256; i++)
            {
                uint res = i;
                for (int k = 0; k < 8; k++)
                {
                    res = (res & 1) == 1 ? poly ^ (res >> 1) : (res >> 1);
                }
                _table[i] = res;
            }
        }

        public static uint Calculate(byte[] buffer, int offset, int length)
        {
            uint crc = ~0u;
            while (--length >= 0)
            {
                crc = _table[(crc ^ buffer[offset++]) & 0xff] ^ crc >> 8;
            }
            return crc ^ 0xffffffff;
        }
    }

    /// <summary>
    /// An SCTP packet is composed of a common header and chunks. A chunk
    /// contains either control information or user data.
    /// </summary>
    public class SctpPacket
    {
        /// <summary>
        /// The position in a serialised SCTP packet buffer that the verification
        /// tag field starts.
        /// </summary>
        public const int VERIFICATIONTAG_BUFFER_POSITION = 4;

        /// <summary>
        /// The position in a serialised SCTP packet buffer that the checksum 
        /// field starts.
        /// </summary>
        public const int CHECKSUM_BUFFER_POSITION = 8;

        private static ILogger logger = LogFactory.CreateLogger<SctpPacket>();

        /// <summary>
        /// The common header for the SCTP packet.
        /// </summary>
        public SctpHeader Header;

        /// <summary>
        /// The list of one or recognised chunks after parsing with <see cref="GetChunks"/> 
        /// or chunks that have been manually added for an outgoing SCTP packet.
        /// </summary>
        public List<SctpChunk> Chunks;

        /// <summary>
        /// A list of the blobs for chunks that weren't recognised when parsing
        /// a received packet.
        /// </summary>
        public List<byte[]> UnrecognisedChunks;

        private SctpPacket()
        { }

        /// <summary>
        /// Creates a new SCTP packet instance.
        /// </summary>
        /// <param name="sourcePort">The source port value to place in the packet header.</param>
        /// <param name="destinationPort">The destination port value to place in the packet header.</param>
        /// <param name="verificationTag">The verification tag value to place in the packet header.</param>
        public SctpPacket(
            ushort sourcePort,
            ushort destinationPort,
            uint verificationTag)
        {
            Header = new SctpHeader
            {
                SourcePort = sourcePort,
                DestinationPort = destinationPort,
                VerificationTag = verificationTag
            };

            Chunks = new List<SctpChunk>();
            UnrecognisedChunks = new List<byte[]>();
        }

        /// <summary>
        /// Serialises an SCTP packet to a byte array.
        /// </summary>
        /// <returns>The byte array containing the serialised SCTP packet.</returns>
        public byte[] GetBytes()
        {
            int chunksLength = Chunks.Sum(x => x.GetChunkLength(true));
            byte[] buffer = new byte[SctpHeader.SCTP_HEADER_LENGTH + chunksLength];

            Header.WriteToBuffer(buffer, 0);

            int writePosn = SctpHeader.SCTP_HEADER_LENGTH;
            foreach (var chunk in Chunks)
            {
                writePosn += chunk.WriteTo(buffer, writePosn);
            }

            NetConvert.ToBuffer(0U, buffer, CHECKSUM_BUFFER_POSITION);
            uint checksum = CRC32C.Calculate(buffer, 0, buffer.Length);
            NetConvert.ToBuffer(NetConvert.EndianFlip(checksum), buffer, CHECKSUM_BUFFER_POSITION);

            return buffer;
        }

        /// <summary>
        /// Adds a new chunk to send with an outgoing packet.
        /// </summary>
        /// <param name="chunk">The chunk to add.</param>
        public void AddChunk(SctpChunk chunk)
        {
            Chunks.Add(chunk);
        }

        /// <summary>
        /// Parses an SCTP packet from a serialised buffer.
        /// </summary>
        /// <param name="buffer">The buffer holding the serialised packet.</param>
        /// <param name="offset">The position in the buffer of the packet.</param>
        /// <param name="length">The length of the serialised packet in the buffer.</param>
        public static SctpPacket Parse(byte[] buffer, int offset, int length)
        {
            var pkt = new SctpPacket();
            pkt.Header = SctpHeader.Parse(buffer, offset);
            (pkt.Chunks, pkt.UnrecognisedChunks) = ParseChunks(buffer, offset, length);

            return pkt;
        }

        /// <summary>
        /// Parses the chunks from a serialised SCTP packet.
        /// </summary>
        /// <param name="buffer">The buffer holding the serialised packet.</param>
        /// <param name="offset">The position in the buffer of the packet.</param>
        /// <param name="length">The length of the serialised packet in the buffer.</param>
        /// <returns>The lsit of parsed chunks and a list of unrecognised chunks that were not de-serialised.</returns>
        private static (List<SctpChunk> chunks, List<byte[]> unrecognisedChunks) ParseChunks(byte[] buffer, int offset, int length)
        {
            List<SctpChunk> chunks = new List<SctpChunk>();
            List<byte[]> unrecognisedChunks = new List<byte[]>();

            int posn = offset + SctpHeader.SCTP_HEADER_LENGTH;

            bool stop = false;

            while (posn < length)
            {
                byte chunkType = buffer[posn];

                if (Enum.IsDefined(typeof(SctpChunkType), chunkType))
                {
                    var chunk = SctpChunk.Parse(buffer, posn);
                    chunks.Add(chunk);
                }
                else
                {
                    switch (SctpChunk.GetUnrecognisedChunkAction(chunkType))
                    {
                        case SctpUnrecognisedChunkActions.Stop:
                            stop = true;
                            break;
                        case SctpUnrecognisedChunkActions.StopAndReport:
                            stop = true;
                            unrecognisedChunks.Add(SctpChunk.CopyUnrecognisedChunk(buffer, posn));
                            break;
                        case SctpUnrecognisedChunkActions.Skip:
                            break;
                        case SctpUnrecognisedChunkActions.SkipAndReport:
                            unrecognisedChunks.Add(SctpChunk.CopyUnrecognisedChunk(buffer, posn));
                            break;
                    }
                }

                if (stop)
                {
                    logger.LogWarning($"SCTP unrecognised chunk type {chunkType} indicated no further chunks should be processed.");
                    break;
                }

                posn += (int)SctpChunk.GetChunkLengthFromHeader(buffer, posn, true);
            }

            return (chunks, unrecognisedChunks);
        }

        /// <summary>
        /// Verifies whether the checksum for a serialised SCTP packet is valid.
        /// </summary>
        /// <param name="buffer">The buffer holding the serialised packet.</param>
        /// <param name="posn">The start position in the buffer.</param>
        /// <param name="length">The length of the packet in the buffer.</param>
        /// <returns>True if the checksum was valid, false if not.</returns>
        public static bool VerifyChecksum(byte[] buffer, int posn, int length)
        {
            uint origChecksum = NetConvert.ParseUInt32(buffer, posn + CHECKSUM_BUFFER_POSITION);
            NetConvert.ToBuffer(0U, buffer, posn + CHECKSUM_BUFFER_POSITION);
            uint calcChecksum = CRC32C.Calculate(buffer, posn, length);

            // Put the original checksum back.
            NetConvert.ToBuffer(origChecksum, buffer, posn + CHECKSUM_BUFFER_POSITION);

            return origChecksum == NetConvert.EndianFlip(calcChecksum);
        }

        /// <summary>
        /// Gets the verification tag from a serialised SCTP packet. This allows
        /// a pre-flight check to be carried out before de-serialising the whole buffer.
        /// </summary>
        /// <param name="buffer">The buffer holding the serialised packet.</param>
        /// <param name="posn">The start position in the buffer.</param>
        /// <param name="length">The length of the packet in the buffer.</param>
        /// <returns>The verification tag for the serialised SCTP packet.</returns>
        public static uint GetVerificationTag(byte[] buffer, int posn, int length)
        {
            return NetConvert.ParseUInt32(buffer, posn + VERIFICATIONTAG_BUFFER_POSITION);
        }

        /// <summary>
        /// Performs verification checks on a serialised SCTP packet.
        /// </summary>
        /// <param name="buffer">The buffer holding the serialised packet.</param>
        /// <param name="posn">The start position in the buffer.</param>
        /// <param name="length">The length of the packet in the buffer.</param>
        /// <param name="requiredTag">The required verification tag for the serialised
        /// packet. This should match the verification tag supplied by the remote party.</param>
        /// <returns>True if the packet is valid, false if not.</returns>
        public static bool IsValid(byte[] buffer, int posn, int length, uint requiredTag)
        {
            return GetVerificationTag(buffer, posn, length) == requiredTag &&
                VerifyChecksum(buffer, posn, length);
        }
    }
}
