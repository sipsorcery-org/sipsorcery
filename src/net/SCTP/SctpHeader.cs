//-----------------------------------------------------------------------------
// Filename: SctpHeader.cs
//
// Description: Represents the common SCTP packet header.
//
// Remarks:
// Defined in section 3 of RFC4960:
// https://tools.ietf.org/html/rfc4960#section-3.1.
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
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    public struct SctpHeader
    {
        public const int SCTP_HEADER_LENGTH = 12;

        /// <summary>
        /// The SCTP sender's port number.
        /// </summary>
        public ushort SourcePort;

        /// <summary>
        /// The SCTP port number to which this packet is destined.
        /// </summary>
        public ushort DestinationPort;

        /// <summary>
        /// The receiver of this packet uses the Verification Tag to validate
        /// the sender of this SCTP packet.
        /// </summary>
        public uint VerificationTag;

        /// <summary>
        /// The CRC32c checksum of this SCTP packet.
        /// </summary>
        public uint Checksum { get; private set; }

        /// <summary>
        /// Serialises the header to a pre-allocated buffer.
        /// </summary>
        /// <param name="buffer">The buffer to write the SCTP header bytes to. It
        /// must have the required space already allocated.</param>
        /// <param name="posn">The position in the buffer to write the header
        /// bytes to.</param>
        public void WriteToBuffer(byte[] buffer, int posn)
        {
            NetConvert.ToBuffer(SourcePort, buffer, posn);
            NetConvert.ToBuffer(DestinationPort, buffer, posn + 2);
            NetConvert.ToBuffer(VerificationTag, buffer, posn + 4);
        }

        /// <summary>
        /// Parses the an SCTP header from a buffer.
        /// </summary>
        /// <param name="buffer">The buffer to parse the SCTP header from.</param>
        /// <param name="posn">The position in the buffer to start parsing the header from.</param>
        /// <returns>A new SCTPHeaer instance.</returns>
        public static SctpHeader Parse(byte[] buffer, int posn)
        {
            if (buffer.Length < SCTP_HEADER_LENGTH)
            {
                throw new ApplicationException("The buffer did not contain the minimum number of bytes for an SCTP header.");
            }

            SctpHeader header = new SctpHeader();

            header.SourcePort = NetConvert.ParseUInt16(buffer, posn);
            header.DestinationPort = NetConvert.ParseUInt16(buffer, posn + 2);
            header.VerificationTag = NetConvert.ParseUInt32(buffer, posn + 4);
            header.Checksum = NetConvert.ParseUInt32(buffer, posn + 8);

            return header;
        }
    }
}
