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
using System.Buffers;
using System.Buffers.Binary;
using CommunityToolkit.HighPerformance.Buffers;
using SIPSorcery.Sys;

namespace SIPSorcery.Net;

public partial struct SctpHeader : IByteSerializable
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

    /// <inheritdoc/>
    public int GetByteCount() => 8;

    /// <inheritdoc/>
    public int WriteTo(IBufferWriter<byte> writer)
    {
        var buffer = writer.GetSpan(8);
        BinaryPrimitives.WriteUInt16BigEndian(buffer, SourcePort);
        BinaryPrimitives.WriteUInt16BigEndian(buffer.Slice(2), DestinationPort);
        BinaryPrimitives.WriteUInt32BigEndian(buffer.Slice(4), VerificationTag);
        writer.Advance(8);

        return 8;
    }

    /// <summary>
    /// Parses the an SCTP header from a buffer.
    /// </summary>
    /// <param name="buffer">The buffer to parse the SCTP header from.</param>
    /// <returns>A new SCTPHeaer instance.</returns>
    public static SctpHeader Parse(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < SCTP_HEADER_LENGTH)
        {
            throw new SipSorceryException("The buffer did not contain the minimum number of bytes for an SCTP header.");
        }

        var header = new SctpHeader();

        header.SourcePort = BinaryPrimitives.ReadUInt16BigEndian(buffer);
        header.DestinationPort = BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(2));
        header.VerificationTag = BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(4));
        header.Checksum = BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(8));

        return header;
    }
}
