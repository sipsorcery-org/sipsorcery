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
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.Net;

public static class CRC32C
{
    private const uint INITIAL_POLYNOMIAL = 0x82f63b78;

    private static readonly uint[] _table = new uint[256];

    static CRC32C()
    {
        var poly = INITIAL_POLYNOMIAL;
        for (uint i = 0; i < 256; i++)
        {
            var res = i;
            for (var k = 0; k < 8; k++)
            {
                res = (res & 1) == 1 ? poly ^ (res >> 1) : (res >> 1);
            }
            _table[i] = res;
        }
    }

    public static uint Calculate(byte[] buffer, int offset, int length)
    {
        return Calculate(buffer.AsSpan(offset, length));
    }

    public static uint Calculate(ReadOnlySpan<byte> buffer)
    {
        var crc = ~0u;
        var length = buffer.Length;
        var offset = 0;
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
public partial class SctpPacket : IByteSerializable
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

    private SctpHeader header;

    /// <summary>
    /// The common header for the SCTP packet.
    /// </summary>
    public SctpHeader Header => header;

    /// <summary>
    /// The list of one or recognised chunks after parsing with <see cref="ParseChunks"/>
    /// or chunks that have been manually added for an outgoing SCTP packet.
    /// </summary>
    public List<SctpChunk> Chunks { get; }

    /// <summary>
    /// A list of the blobs for chunks that weren't recognised when parsing
    /// a received packet.
    /// </summary>
    public List<byte[]> UnrecognisedChunks { get; }

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
        : this(
            new SctpHeader
            {
                SourcePort = sourcePort,
                DestinationPort = destinationPort,
                VerificationTag = verificationTag
            },
            new List<SctpChunk>(),
            new List<byte[]>())
    {
    }

    private SctpPacket(SctpHeader header, List<SctpChunk> chunks, List<byte[]> unrecognisedChunks)
    {
        this.header = header;
        Chunks = chunks;
        UnrecognisedChunks = unrecognisedChunks;
    }

    /// <inheritdoc/>
    public int GetByteCount()
    {
        var total = SctpHeader.SCTP_HEADER_LENGTH;
        foreach (var chunk in Chunks)
        {
            total += chunk.GetByteCount(true);
        }
        return total;
    }

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

        var contentBuffer = buffer.Slice(SctpHeader.SCTP_HEADER_LENGTH);
        foreach (var chunk in Chunks)
        {
            bytesWritten = chunk.WriteBytes(contentBuffer);
            contentBuffer = contentBuffer.Slice(bytesWritten);
        }

        var checksumBuffer = buffer.Slice(CHECKSUM_BUFFER_POSITION, sizeof(uint));
        checksumBuffer.Clear();
        var checksum = CRC32C.Calculate(buffer);
        BinaryPrimitives.WriteUInt32LittleEndian(checksumBuffer, checksum);
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
    public static SctpPacket Parse(ReadOnlySpan<byte> buffer)
    {
        var (chunks, unrecognisedChunks) = ParseChunks(buffer);
        var pkt = new SctpPacket(SctpHeader.Parse(buffer), chunks, unrecognisedChunks);

        return pkt;
    }

    /// <summary>
    /// Parses the chunks from a serialised SCTP packet.
    /// </summary>
    /// <param name="buffer">The buffer holding the serialised packet.</param>
    /// <returns>The lsit of parsed chunks and a list of unrecognised chunks that were not de-serialised.</returns>
    private static (List<SctpChunk> chunks, List<byte[]> unrecognisedChunks) ParseChunks(ReadOnlySpan<byte> buffer)
    {
        var chunks = new List<SctpChunk>();
        var unrecognisedChunks = new List<byte[]>();

        var posn = SctpHeader.SCTP_HEADER_LENGTH;

        var stop = false;

        while (posn < buffer.Length)
        {
            var chunkSpan = buffer.Slice(posn);
            var chunkType = chunkSpan[0];

            if (SctpChunkTypeExtensions.IsDefined((SctpChunkType)chunkType))
            {
                var chunk = SctpChunk.Parse(chunkSpan);
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
                        unrecognisedChunks.Add(SctpChunk.CopyUnrecognisedChunk(chunkSpan));
                        break;
                    case SctpUnrecognisedChunkActions.Skip:
                        break;
                    case SctpUnrecognisedChunkActions.SkipAndReport:
                        unrecognisedChunks.Add(SctpChunk.CopyUnrecognisedChunk(chunkSpan));
                        break;
                }
            }

            if (stop)
            {
                logger.LogSctpUnrecognisedChunkType(chunkType);
                break;
            }

            posn += (int)SctpChunk.GetChunkLengthFromHeader(chunkSpan, true);
        }

        return (chunks, unrecognisedChunks);
    }

    /// <summary>
    /// Verifies whether the checksum for a serialised SCTP packet is valid.
    /// </summary>
    /// <param name="buffer">The buffer holding the serialised packet.</param>
    /// <returns>True if the checksum was valid, false if not.</returns>
    public static bool VerifyChecksum(ReadOnlySpan<byte> buffer)
    {
        var tempBuffer = ArrayPool<byte>.Shared.Rent(buffer.Length);
        try
        {
            var tempSpan = tempBuffer.AsSpan(0, buffer.Length);
            buffer.CopyTo(tempBuffer);
            var checksumSpan = tempSpan.Slice(CHECKSUM_BUFFER_POSITION, sizeof(uint));
            var origChecksum = BinaryPrimitives.ReadUInt32LittleEndian(checksumSpan);
            checksumSpan.Clear();
            var calcChecksum = CRC32C.Calculate(tempSpan);

            return origChecksum == calcChecksum;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(tempBuffer);
        }
    }

    /// <summary>
    /// Gets the verification tag from a serialised SCTP packet. This allows
    /// a pre-flight check to be carried out before de-serialising the whole buffer.
    /// </summary>
    /// <param name="buffer">The buffer holding the serialised packet.</param>
    /// <returns>The verification tag for the serialised SCTP packet.</returns>
    public static uint GetVerificationTag(ReadOnlySpan<byte> buffer)
    {
        return BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(VERIFICATIONTAG_BUFFER_POSITION));
    }

    /// <summary>
    /// Performs verification checks on a serialised SCTP packet.
    /// </summary>
    /// <param name="buffer">The buffer holding the serialised packet.</param>
    /// <param name="requiredTag">The required verification tag for the serialised
    /// packet. This should match the verification tag supplied by the remote party.</param>
    /// <returns>True if the packet is valid, false if not.</returns>
    public static bool IsValid(ReadOnlySpan<byte> buffer, uint requiredTag)
    {
        return GetVerificationTag(buffer) == requiredTag &&
            VerifyChecksum(buffer);
    }

    public void SetHeaderVerificationTag(uint verificationTag)
    {
        header.VerificationTag = verificationTag;
    }
}
