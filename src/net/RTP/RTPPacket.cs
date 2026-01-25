//-----------------------------------------------------------------------------
// Filename: RTPPacket.cs
//
// Description: Encapsulation of an RTP packet.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 24 May 2005	Aaron Clauson 	Created, Dublin, Ireland.
// 11 Aug 2019  Aaron Clauson   Added full license header.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Diagnostics.CodeAnalysis;
using SIPSorcery.Sys;

namespace SIPSorcery.Net;

public class RTPPacket : IByteSerializable
{
    public RTPHeader Header { get; }
    public ReadOnlyMemory<byte> Payload { get; }

    public RTPPacket(ReadOnlyMemory<byte> packet)
    {
        Header = new RTPHeader(packet.Span);
        Payload = packet.Slice(Header.Length, Header.PayloadSize);
    }

    public RTPPacket(RTPHeader rtpHeader, ReadOnlyMemory<byte> payload)
    {
        Header = rtpHeader;
        Payload = payload;
    }

    /// <inheritdoc/>
    public int GetByteCount() => Header.GetByteCount() + Payload.Length;

    /// <inheritdoc/>
    public int WriteBytes(Span<byte> buffer)
    {
        var size = GetByteCount();

        if (buffer.Length < size)
        {
            throw new ArgumentOutOfRangeException($"The buffer should have at least {size} bytes and had only {buffer.Length}.");
        }

        WriteBytesCore(buffer.Slice(0, size));

        return size;
    }

    private void WriteBytesCore(Span<byte> buffer)
    {
        var bytesWritten = Header.WriteBytes(buffer);
        Payload.Span.CopyTo(buffer.Slice(bytesWritten));
    }

    public static bool TryParse(
        ReadOnlyMemory<byte> buffer,
        [NotNullWhen(true)] out RTPPacket? packet,
        out int consumed)
    {
        packet = null;
        consumed = 0;
        if (!RTPHeader.TryParse(buffer.Span, out var header, out var headerConsumed))
        {
            return false;
        }

        packet = new RTPPacket(header, buffer.Slice(headerConsumed, header.PayloadSize));

        consumed = headerConsumed + header.PayloadSize;
        return true;
    }
}
