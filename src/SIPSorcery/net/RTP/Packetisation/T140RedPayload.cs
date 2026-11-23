//-----------------------------------------------------------------------------
// Filename: T140RedPayload.cs
//
// Description: Encodes and decodes the RTP redundancy (RED) payload used to
// carry T.140 real-time text.
//
// RFC 4103 "RTP Payload for Text Conversation", section 4:
// https://www.rfc-editor.org/rfc/rfc4103#section-4
// RFC 2198 "RTP Payload for Redundant Audio Data", section 3 (header format):
// https://www.rfc-editor.org/rfc/rfc2198#section-3
//
// Author(s):
// Claude Code
//
// History:
// 20 Oct 2026  Claude Code    Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace SIPSorcery.Net;

/// <summary>
/// A single block within a RED payload.
/// </summary>
public readonly struct T140RedBlock
{
    /// <summary>
    /// The payload type of the block's data, normally the negotiated t140 payload type.
    /// </summary>
    public int PayloadType { get; }

    /// <summary>
    /// How far, in RTP timestamp units, this block's timestamp is behind the packet's
    /// timestamp. Always 0 for the primary block.
    /// </summary>
    public int TimestampOffset { get; }

    /// <summary>
    /// The block data. For T.140 this is UTF-8 encoded text and may be empty.
    /// </summary>
    public byte[] Data { get; }

    public T140RedBlock(int payloadType, int timestampOffset, byte[] data)
    {
        PayloadType = payloadType;
        TimestampOffset = timestampOffset;
        Data = data ?? Array.Empty<byte>();
    }
}

/// <summary>
/// RFC 2198 RED payload encoding and decoding as profiled for T.140 by RFC 4103.
/// </summary>
/// <remarks>
/// Each redundant block has a 4 byte header:
///  0                   1                   2                   3
///  0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
/// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
/// |1|   T140 PT   |  timestamp offset of "R"  | "R" block length  |
/// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
/// followed by a 1 byte header for the primary block, then the block data with
/// the oldest redundant generation first and the primary block last.
/// </remarks>
public static class T140RedPayload
{
    public const int MAX_TIMESTAMP_OFFSET = 0x3FFF;   // 14 bits.
    public const int MAX_BLOCK_LENGTH = 0x3FF;        // 10 bits.

    private const int REDUNDANT_HEADER_LENGTH = 4;
    private const int PRIMARY_HEADER_LENGTH = 1;
    private const byte FOLLOW_FLAG = 0x80;
    private const byte PAYLOAD_TYPE_MASK = 0x7F;

    /// <summary>
    /// Builds a RED payload.
    /// </summary>
    /// <param name="redundantBlocks">The redundant generations, oldest first.</param>
    /// <param name="primary">The primary (newest) block.</param>
    public static byte[] Encode(IReadOnlyList<T140RedBlock> redundantBlocks, T140RedBlock primary)
    {
        int length = PRIMARY_HEADER_LENGTH + primary.Data.Length;

        foreach (var block in redundantBlocks)
        {
            if (block.Data.Length > MAX_BLOCK_LENGTH)
            {
                throw new ArgumentException($"A RED block cannot exceed {MAX_BLOCK_LENGTH} bytes.", nameof(redundantBlocks));
            }

            if (block.TimestampOffset < 0 || block.TimestampOffset > MAX_TIMESTAMP_OFFSET)
            {
                throw new ArgumentException($"A RED timestamp offset must be between 0 and {MAX_TIMESTAMP_OFFSET}.", nameof(redundantBlocks));
            }

            length += REDUNDANT_HEADER_LENGTH + block.Data.Length;
        }

        var buffer = new byte[length];
        int posn = 0;

        foreach (var block in redundantBlocks)
        {
            buffer[posn++] = (byte)(FOLLOW_FLAG | (block.PayloadType & PAYLOAD_TYPE_MASK));
            buffer[posn++] = (byte)(block.TimestampOffset >> 6);
            buffer[posn++] = (byte)(((block.TimestampOffset & 0x3F) << 2) | (block.Data.Length >> 8));
            buffer[posn++] = (byte)(block.Data.Length & 0xFF);
        }

        buffer[posn++] = (byte)(primary.PayloadType & PAYLOAD_TYPE_MASK);

        foreach (var block in redundantBlocks)
        {
            Buffer.BlockCopy(block.Data, 0, buffer, posn, block.Data.Length);
            posn += block.Data.Length;
        }

        Buffer.BlockCopy(primary.Data, 0, buffer, posn, primary.Data.Length);

        return buffer;
    }

    /// <summary>
    /// Parses a RED payload.
    /// </summary>
    /// <param name="payload">The RTP payload, without the RTP header.</param>
    /// <param name="redundantBlocks">The redundant generations, oldest first.</param>
    /// <param name="primary">The primary (newest) block.</param>
    /// <returns>True if the payload was well formed.</returns>
    public static bool TryDecode(byte[] payload, out List<T140RedBlock> redundantBlocks, out T140RedBlock primary)
    {
        redundantBlocks = new List<T140RedBlock>();
        primary = default;

        if (payload == null || payload.Length == 0)
        {
            return false;
        }

        var headers = new List<(int pt, int offset, int length)>();
        int posn = 0;

        while (posn < payload.Length && (payload[posn] & FOLLOW_FLAG) != 0)
        {
            if (posn + REDUNDANT_HEADER_LENGTH > payload.Length)
            {
                return false;
            }

            int pt = payload[posn] & PAYLOAD_TYPE_MASK;
            int offset = (payload[posn + 1] << 6) | (payload[posn + 2] >> 2);
            int length = ((payload[posn + 2] & 0x03) << 8) | payload[posn + 3];
            headers.Add((pt, offset, length));
            posn += REDUNDANT_HEADER_LENGTH;
        }

        if (posn >= payload.Length)
        {
            return false;
        }

        int primaryPayloadType = payload[posn++] & PAYLOAD_TYPE_MASK;

        foreach (var (pt, offset, length) in headers)
        {
            if (posn + length > payload.Length)
            {
                return false;
            }

            var data = new byte[length];
            Buffer.BlockCopy(payload, posn, data, 0, length);
            redundantBlocks.Add(new T140RedBlock(pt, offset, data));
            posn += length;
        }

        var primaryData = new byte[payload.Length - posn];
        Buffer.BlockCopy(payload, posn, primaryData, 0, primaryData.Length);
        primary = new T140RedBlock(primaryPayloadType, 0, primaryData);

        return true;
    }

    /// <summary>
    /// Parses the RED format parameters from an SDP fmtp attribute, e.g. "98/98/98" means
    /// the primary and two redundant generations all use payload type 98.
    /// </summary>
    /// <param name="fmtp">The fmtp value without the "a=fmtp:100 " prefix.</param>
    /// <param name="t140PayloadType">The payload type the RED blocks carry.</param>
    /// <param name="redundancy">The number of redundant generations, one less than the
    /// number of payload types listed.</param>
    public static bool TryParseFmtp(string fmtp, out int t140PayloadType, out int redundancy)
    {
        t140PayloadType = -1;
        redundancy = 0;

        if (string.IsNullOrWhiteSpace(fmtp))
        {
            return false;
        }

        var fields = fmtp.Trim().Split('/');

        foreach (var field in fields)
        {
            if (!int.TryParse(field.Trim(), out int pt) || pt < 0 || pt > PAYLOAD_TYPE_MASK)
            {
                return false;
            }

            if (t140PayloadType == -1)
            {
                t140PayloadType = pt;
            }
            else if (pt != t140PayloadType)
            {
                // RFC 4103 only uses RED to carry t140 so all the listed payload types should be the same.
                return false;
            }
        }

        redundancy = fields.Length - 1;
        return true;
    }

    /// <summary>
    /// Builds the RED fmtp value for a t140 payload type and redundancy level, e.g. "98/98/98".
    /// </summary>
    public static string BuildFmtp(int t140PayloadType, int redundancy)
    {
        var fields = new string[redundancy + 1];

        for (int i = 0; i < fields.Length; i++)
        {
            fields[i] = t140PayloadType.ToString();
        }

        return string.Join("/", fields);
    }
}
