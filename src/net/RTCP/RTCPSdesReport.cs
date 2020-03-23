//-----------------------------------------------------------------------------
// Filename: RTCPSDesReport.cs
//
// Description: RTCP Source Description (SDES) report as defined in RFC3550.
// Only the mandatory CNAME item is supported.
//
//         RTCP SDES Payload
//         0                   1                   2                   3
//         0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
//        +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
// header |V=2|P|    SC   |  PT=SDES=202  |             length            |
//        +=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+
// chunk  |                          SSRC/CSRC_1                          |
//  1     +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//        |                           SDES items                          |
//        |                              ...                              |
//        +=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+
// chunk  |                          SSRC/CSRC_2                          |
//  2     +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//        |                           SDES items                          |
//        |                              ...                              |
//        +=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+
//
//    0                   1                   2                   3
//    0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
//   +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//   |    CNAME=1    |     length    | user and domain name        ...
//   +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
// 
//   the CNAME item SHOULD have the format
//   "user@host", or "host" if a user name is not available as on single-
//   user systems.For both formats, "host" is either the fully qualified
//   domain name of the host from which the real-time data originates,
//   formatted according to the rules specified in RFC 1034 [6], RFC 1035
//   [7] and Section 2.1 of RFC 1123 [8]; or the standard ASCII
//   representation of the host's numeric address on the interface used
//   for the RTP communication.
//
//  The list of items in each chunk
//  MUST be terminated by one or more null octets, the first of which is
//  interpreted as an item type of zero to denote the end of the list.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 28 Dec 2019  Aaron Clauson   Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Text;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    /// <summary>
    /// RTCP Source Description (SDES) report as defined in RFC3550.
    /// Only the mandatory CNAME item is supported.
    /// </summary>
    public class RTCPSDesReport
    {
        public const int PACKET_SIZE_WITHOUT_CNAME = 6; // 4 byte SSRC, 1 byte CNAME ID, 1 byte CNAME length.
        public const int MAX_CNAME_BYTES = 255;
        public const byte CNAME_ID = 0x01;
        public const int MIN_PACKET_SIZE = RTCPHeader.HEADER_BYTES_LENGTH + PACKET_SIZE_WITHOUT_CNAME;

        public RTCPHeader Header;
        public uint SSRC { get; private set; }
        public string CNAME { get; private set; }

        /// <summary>
        /// Creates a new RTCP SDES payload that can be included in an RTCP packet.
        /// </summary>
        /// <param name="ssrc">The synchronisation source of the SDES.</param>
        /// <param name="cname">Canonical End-Point Identifier SDES item. This should be a 
        /// unique string common to all RTP streams in use by the application. Maximum
        /// length is 255 bytes (note bytes not characters).</param>
        public RTCPSDesReport(uint ssrc, string cname)
        {
            if (String.IsNullOrEmpty(cname))
            {
                throw new ArgumentNullException("cname");
            }

            Header = new RTCPHeader(RTCPReportTypesEnum.SDES, 1);
            SSRC = ssrc;
            CNAME = (cname.Length > MAX_CNAME_BYTES) ? cname.Substring(0, MAX_CNAME_BYTES) : cname;

            // Need to take account of multi-byte characters.
            while (Encoding.UTF8.GetBytes(CNAME).Length > MAX_CNAME_BYTES)
            {
                CNAME = CNAME.Substring(0, CNAME.Length - 1);
            }
        }

        /// <summary>
        /// Create a new RTCP SDES item from a serialised byte array.
        /// </summary>
        /// <param name="packet">The byte array holding the SDES report.</param>
        public RTCPSDesReport(byte[] packet)
        {
            if (packet.Length < MIN_PACKET_SIZE)
            {
                throw new ApplicationException("The packet did not contain the minimum number of bytes for an RTCP SDES packet.");
            }
            else if (packet[8] != CNAME_ID)
            {
                throw new ApplicationException("The RTCP report packet did not have the required CNAME type field set correctly.");
            }

            Header = new RTCPHeader(packet);

            if (BitConverter.IsLittleEndian)
            {
                SSRC = NetConvert.DoReverseEndian(BitConverter.ToUInt32(packet, 4));
            }
            else
            {
                SSRC = BitConverter.ToUInt32(packet, 4);
            }

            int cnameLength = packet[9];
            CNAME = Encoding.UTF8.GetString(packet, 10, cnameLength);
        }

        /// <summary>
        /// Gets the raw bytes for the SDES item. This packet is ready to be included 
        /// directly in an RTCP packet.
        /// </summary>
        /// <returns>A byte array containing the serialised SDES item.</returns>
        public byte[] GetBytes()
        {
            byte[] cnameBytes = Encoding.UTF8.GetBytes(CNAME);
            byte[] buffer = new byte[RTCPHeader.HEADER_BYTES_LENGTH + GetPaddedLength(cnameBytes.Length)]; // Array automatically initialised with 0x00 values.
            Header.SetLength((ushort)(buffer.Length / 4 - 1));

            Buffer.BlockCopy(Header.GetBytes(), 0, buffer, 0, RTCPHeader.HEADER_BYTES_LENGTH);
            int payloadIndex = RTCPHeader.HEADER_BYTES_LENGTH;

            if (BitConverter.IsLittleEndian)
            {
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(SSRC)), 0, buffer, payloadIndex, 4);
            }
            else
            {
                Buffer.BlockCopy(BitConverter.GetBytes(SSRC), 0, buffer, payloadIndex, 4);
            }

            buffer[payloadIndex + 4] = CNAME_ID;
            buffer[payloadIndex + 5] = (byte)cnameBytes.Length;
            Buffer.BlockCopy(cnameBytes, 0, buffer, payloadIndex + 6, cnameBytes.Length);

            return buffer;
        }

        /// <summary>
        /// The packet has to finish on a 4 byte boundary. This method calculates the minimum
        /// packet length for the SDES fields to fit within a 4 byte boundary.
        /// </summary>
        /// <param name="cnameLength">The length of the cname string.</param>
        /// <returns>The minimum length for the full packet to be able to fit within a 4 byte
        /// boundary.</returns>
        private int GetPaddedLength(int cnameLength)
        {
            // Plus one is for the 0x00 items termination byte.
            int nonPaddedSize = cnameLength + PACKET_SIZE_WITHOUT_CNAME + 1;

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
