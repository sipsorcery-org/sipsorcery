﻿//-----------------------------------------------------------------------------
// Filename: RTCPBye.cs
//
// Description: RTCP Goodbye packet as defined in RFC3550.
//
//         Goodbye RTCP Packet
//         0                   1                   2                   3
//         0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
//        +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//        |V=2|P|    SC   |   PT=BYE=203  |             length            |
//        +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//        |                           SSRC/CSRC                           |
//        +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//        :                              ...                              :
//        +=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+
//  (opt) |     length    |               reason for leaving            ...
//        +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 29 Dec 2019  Aaron Clauson   Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Buffers.Binary;
using System.Text;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    /// <summary>
    /// RTCP Goodbye packet as defined in RFC3550. The BYE packet indicates 
    /// that one or more sources are no longer active.
    /// </summary>
    public partial class RTCPBye
    {
        public const int MAX_REASON_BYTES = 255;
        public const int SSRC_SIZE = 4;       // 4 bytes for the SSRC.
        public const int MIN_PACKET_SIZE = RTCPHeader.HEADER_BYTES_LENGTH + SSRC_SIZE;

        public RTCPHeader Header;
        public uint SSRC { get; private set; }
        public string? Reason { get; private set; }

        /// <summary>
        /// Creates a new RTCP Bye payload.
        /// </summary>
        /// <param name="ssrc">The synchronisation source of the RTP stream being closed.</param>
        /// <param name="reason">Optional reason for closing. Maximum length is 255 bytes 
        /// (note bytes not characters).</param>
        public RTCPBye(uint ssrc, string? reason)
        {
            Header = new RTCPHeader(RTCPReportTypesEnum.BYE, 1);
            SSRC = ssrc;

            if (reason is { })
            {
                Reason = (reason.Length > MAX_REASON_BYTES) ? reason.Substring(0, MAX_REASON_BYTES) : reason;

                // Need to take account of multi-byte characters.
                while (Encoding.UTF8.GetBytes(Reason).Length > MAX_REASON_BYTES)
                {
                    Reason = Reason.Substring(0, Reason.Length - 1);
                }
            }
        }

        /// <summary>
        /// Create a new RTCP Goodbye packet from a serialised byte array.
        /// </summary>
        /// <param name="packet">The byte array holding the Goodbye packet.</param>
        public RTCPBye(ReadOnlySpan<byte> packet)
        {
            if (packet.Length < MIN_PACKET_SIZE)
            {
                throw new ApplicationException("The packet did not contain the minimum number of bytes for an RTCP Goodbye packet.");
            }

            Header = new RTCPHeader(packet);

            SSRC = BinaryPrimitives.ReadUInt32BigEndian(packet.Slice(4));

            if (packet.Length > MIN_PACKET_SIZE)
            {
                int reasonLength = packet[8];

                if (packet.Length - MIN_PACKET_SIZE - 1 >= reasonLength)
                {
                    Reason = Encoding.UTF8.GetString(packet.Slice(9, reasonLength));
                }
            }
        }

        public int GetPacketSize() => RTCPHeader.HEADER_BYTES_LENGTH + GetPaddedLength(((Reason is { }) ? Encoding.UTF8.GetByteCount(Reason) : 0));

        /// <summary>
        /// Gets the raw bytes for the Goodbye packet.
        /// </summary>
        /// <param name="buffer">The destination buffer.</param>
        public int WriteBytes(Span<byte> buffer)
        {
            var size = GetPacketSize();

            if (buffer.Length < size)
            {
                throw new ArgumentOutOfRangeException($"The buffer should have at least {size} bytes and had only {buffer.Length}.");
            }

            WriteBytesCore(buffer.Slice(0, size));

            return size;
        }

        private void WriteBytesCore(Span<byte> buffer)
        {
            Header.SetLength((ushort)(buffer.Length / 4 - 1));
            _ = Header.WriteBytes(buffer);

            BinaryPrimitives.WriteUInt32BigEndian(buffer.Slice(RTCPHeader.HEADER_BYTES_LENGTH), SSRC);

            if (Reason is { })
            {
                buffer[RTCPHeader.HEADER_BYTES_LENGTH + 4] =
                    (byte)Encoding.UTF8.GetBytes(Reason.AsSpan(), buffer.Slice(RTCPHeader.HEADER_BYTES_LENGTH + 5));
            }
        }

        /// <summary>
        /// The packet has to finish on a 4 byte boundary. This method calculates the minimum
        /// packet length for the Goodbye fields to fit within a 4 byte boundary.
        /// </summary>
        /// <param name="reasonLength">The length of the optional reason string, can be 0.</param>
        /// <returns>The minimum length for the full packet to be able to fit within a 4 byte
        /// boundary.</returns>
        private int GetPaddedLength(int reasonLength)
        {
            // Plus one is for the reason length field.
            if (reasonLength > 0)
            {
                reasonLength += 1;
            }

            int nonPaddedSize = reasonLength + SSRC_SIZE;

            if (nonPaddedSize % 4 == 0)
            {
                return nonPaddedSize;
            }
            else
            {
                return nonPaddedSize + 4 - (nonPaddedSize % 4);
            }
        }
    }
}
