//-----------------------------------------------------------------------------
// Filename: RtpRedPayload.cs
//
// Description: Encodes and decodes RTP redundancy (RED) payloads as defined
// in RFC 2198. Supports both multi-block redundancy and single-block encapsulation.
//
// RFC 2198 "RTP Payload for Redundant Audio Data":
// https://www.rfc-editor.org/rfc/rfc2198
//
// Author(s):
// SIPSorcery contributors
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace SIPSorcery.Net
{
    /// <summary>
    /// A single block within an RFC 2198 RED payload.
    /// </summary>
    public readonly struct RtpRedBlock
    {
        /// <summary>
        /// The payload type of the block's data.
        /// </summary>
        public int PayloadType { get; }

        /// <summary>
        /// Timestamp offset in RTP timestamp units relative to the packet's timestamp.
        /// Always 0 for the primary block.
        /// </summary>
        public int TimestampOffset { get; }

        /// <summary>
        /// The payload bytes for this block.
        /// </summary>
        public byte[] Data { get; }

        public RtpRedBlock(int payloadType, int timestampOffset, byte[] data)
        {
            PayloadType = payloadType;
            TimestampOffset = timestampOffset;
            Data = data ?? Array.Empty<byte>();
        }

        public RtpRedBlock(int payloadType, byte[] data)
            : this(payloadType, 0, data)
        {
        }
    }

    /// <summary>
    /// RFC 2198 RED payload encoder and decoder.
    /// </summary>
    /// <remarks>
    /// Each redundant block has a 4-byte header:
    ///  0                   1                   2                   3
    ///  0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
    /// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    /// |1|   block PT  |  timestamp offset of "R"  | "R" block length  |
    /// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    /// followed by a 1-byte header for the primary block:
    ///  0 1 2 3 4 5 6 7
    /// +-+-+-+-+-+-+-+-+
    /// |0|  primary PT |
    /// +-+-+-+-+-+-+-+-+
    /// followed by the data of each redundant block in order, and finally the primary block data.
    /// For single-block RED (no redundant blocks), only the 1-byte primary header is present,
    /// followed immediately by the primary data.
    /// </remarks>
    public static class RtpRedPayload
    {
        public const int MAX_TIMESTAMP_OFFSET = 0x3FFF; // 14 bits
        public const int MAX_BLOCK_LENGTH = 0x3FF;      // 10 bits

        private const int REDUNDANT_HEADER_LENGTH = 4;
        private const int PRIMARY_HEADER_LENGTH = 1;
        private const byte FOLLOW_FLAG = 0x80;
        private const byte PAYLOAD_TYPE_MASK = 0x7F;

        /// <summary>
        /// Encodes a multi-block or single-block RED payload.
        /// </summary>
        /// <param name="redundantBlocks">Redundant blocks, oldest first. Can be null or empty for single-block.</param>
        /// <param name="primary">The primary (current) block.</param>
        /// <returns>Serialized RFC 2198 RED payload.</returns>
        public static byte[] Encode(IReadOnlyList<RtpRedBlock> redundantBlocks, RtpRedBlock primary)
        {
            int redundantCount = redundantBlocks?.Count ?? 0;
            if (redundantCount == 0)
            {
                return EncodeSingleBlock(primary.Data, (byte)primary.PayloadType);
            }

            int length = PRIMARY_HEADER_LENGTH + (primary.Data?.Length ?? 0);

            for (int i = 0; i < redundantCount; i++)
            {
                var block = redundantBlocks[i];
                int blockLen = block.Data?.Length ?? 0;
                if (blockLen > MAX_BLOCK_LENGTH)
                {
                    throw new ArgumentException($"A RED block cannot exceed {MAX_BLOCK_LENGTH} bytes.", nameof(redundantBlocks));
                }

                if (block.TimestampOffset < 0 || block.TimestampOffset > MAX_TIMESTAMP_OFFSET)
                {
                    throw new ArgumentException($"A RED timestamp offset must be between 0 and {MAX_TIMESTAMP_OFFSET}.", nameof(redundantBlocks));
                }

                length += REDUNDANT_HEADER_LENGTH + blockLen;
            }

            var buffer = new byte[length];
            int posn = 0;

            for (int i = 0; i < redundantCount; i++)
            {
                var block = redundantBlocks[i];
                int blockLen = block.Data?.Length ?? 0;
                buffer[posn++] = (byte)(FOLLOW_FLAG | (block.PayloadType & PAYLOAD_TYPE_MASK));
                buffer[posn++] = (byte)(block.TimestampOffset >> 6);
                buffer[posn++] = (byte)(((block.TimestampOffset & 0x3F) << 2) | (blockLen >> 8));
                buffer[posn++] = (byte)(blockLen & 0xFF);
            }

            buffer[posn++] = (byte)(primary.PayloadType & PAYLOAD_TYPE_MASK);

            for (int i = 0; i < redundantCount; i++)
            {
                var block = redundantBlocks[i];
                int blockLen = block.Data?.Length ?? 0;
                if (blockLen > 0)
                {
                    Buffer.BlockCopy(block.Data, 0, buffer, posn, blockLen);
                    posn += blockLen;
                }
            }

            if (primary.Data?.Length > 0)
            {
                Buffer.BlockCopy(primary.Data, 0, buffer, posn, primary.Data.Length);
            }

            return buffer;
        }

        /// <summary>
        /// Fast-path encoding for single-block RED (common in WebRTC video where RED encapsulates
        /// a single primary media payload or a single ULPFEC repair packet).
        /// </summary>
        public static byte[] EncodeSingleBlock(byte[] primaryData, byte primaryPayloadType)
        {
            if (primaryData == null || primaryData.Length == 0)
            {
                return new byte[] { (byte)(primaryPayloadType & PAYLOAD_TYPE_MASK) };
            }

            byte[] buffer = new byte[PRIMARY_HEADER_LENGTH + primaryData.Length];
            buffer[0] = (byte)(primaryPayloadType & PAYLOAD_TYPE_MASK);
            Buffer.BlockCopy(primaryData, 0, buffer, PRIMARY_HEADER_LENGTH, primaryData.Length);
            return buffer;
        }

        /// <summary>
        /// Parses an RFC 2198 RED payload into its component redundant blocks and primary block.
        /// </summary>
        /// <param name="payload">The RTP payload bytes (excluding RTP header).</param>
        /// <param name="redundantBlocks">List of parsed redundant blocks.</param>
        /// <param name="primary">The parsed primary block.</param>
        /// <returns>True if decoding succeeded; false if malformed.</returns>
        public static bool TryDecode(byte[] payload, out List<RtpRedBlock> redundantBlocks, out RtpRedBlock primary)
        {
            redundantBlocks = new List<RtpRedBlock>();
            primary = default;

            if (payload == null || payload.Length < PRIMARY_HEADER_LENGTH)
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
                if (length > 0)
                {
                    Buffer.BlockCopy(payload, posn, data, 0, length);
                }
                redundantBlocks.Add(new RtpRedBlock(pt, offset, data));
                posn += length;
            }

            int primaryLength = payload.Length - posn;
            if (primaryLength < 0)
            {
                return false;
            }

            var primaryData = new byte[primaryLength];
            if (primaryLength > 0)
            {
                Buffer.BlockCopy(payload, posn, primaryData, 0, primaryLength);
            }

            primary = new RtpRedBlock(primaryPayloadType, 0, primaryData);
            return true;
        }

        /// <summary>
        /// Unwraps the primary block from an RFC 2198 RED packet.
        /// </summary>
        public static bool TryUnwrapPrimary(byte[] payload, out byte primaryPayloadType, out byte[] primaryData)
        {
            if (TryDecode(payload, out _, out var primary))
            {
                primaryPayloadType = (byte)primary.PayloadType;
                primaryData = primary.Data;
                return true;
            }

            primaryPayloadType = 0;
            primaryData = Array.Empty<byte>();
            return false;
        }
    }
}
