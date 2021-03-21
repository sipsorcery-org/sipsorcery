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

using System.Collections.Generic;
using System.Linq;
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
        /// The position in a serialised SCTP packet buffer that the checksum 
        /// field starts.
        /// </summary>
        public const int CHECKSUM_BUFFER_POSITION = 8;

        /// <summary>
        /// The common header for the SCTP packet.
        /// </summary>
        public SctpHeader Header;

        /// <summary>
        /// A list of one or more chunks for the SCTP packet.
        /// </summary>
        public List<SctpChunk> Chunks = new List<SctpChunk>();

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
        }

        /// <summary>
        /// Serialises an SCTP packet to a byte array.
        /// </summary>
        /// <returns>The byte array containing the serialised SCTP packet.</returns>
        public byte[] GetBytes()
        {
            int chunksLength = Chunks.Sum(x => x.GetChunkPaddedLength());
            byte[] buffer = new byte[SctpHeader.SCTP_HEADER_LENGTH + chunksLength];

            Header.WriteToBuffer(buffer, 0);

            int writePosn = SctpHeader.SCTP_HEADER_LENGTH;
            foreach (var chunk in Chunks)
            {
                writePosn += chunk.WriteTo(buffer, writePosn);
            }

            NetConvert.ToBuffer(0, buffer, CHECKSUM_BUFFER_POSITION);
            uint checksum = CRC32C.Calculate(buffer, 0, buffer.Length);
            NetConvert.ToBuffer(NetConvert.EndianFlip(checksum), buffer, CHECKSUM_BUFFER_POSITION);

            return buffer;
        }

        /// <summary>
        /// Parses an SCTP packet from a byte buffer.
        /// </summary>
        /// <param name="buffer">The buffer holding the serialised SCTP packet.</param>
        /// <returns>An SCTP packet.</returns>
        public static SctpPacket Parse(byte[] buffer)
        {
            return Parse(buffer, 0, buffer.Length);
        }

        /// <summary>
        /// Parses an SCTP packet from a byte buffer.
        /// </summary>
        /// <param name="buffer">The buffer holding the serialised SCTP packet.</param>
        /// <param name="offset">The position in the buffer to start parsing from.</param>
        /// <param name="length">The length of the available bytes in the buffer.</param>
        /// <returns>An SCTP packet.</returns>
        public static SctpPacket Parse(byte[] buffer, int offset, int length)
        {
            int posn = offset;

            SctpPacket sctpPacket = new SctpPacket();
            sctpPacket.Header = SctpHeader.Parse(buffer, posn);

            posn += SctpHeader.SCTP_HEADER_LENGTH;

            while (posn < length)
            {
                var chunk = SctpChunk.Parse(buffer, posn);
                sctpPacket.Chunks.Add(chunk);
                posn += chunk.GetChunkPaddedLength();
            }

            return sctpPacket;
        }
    }
}
