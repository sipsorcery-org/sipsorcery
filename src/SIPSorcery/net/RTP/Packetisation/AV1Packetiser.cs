//-----------------------------------------------------------------------------
// Filename: AV1Packetiser.cs
//
// Description: Provides AV1 RTP packetisation helpers.
//
// Based on the Alliance for Open Media RTP Payload Format for AV1:
// https://aomediacodec.github.io/av1-rtp-spec/
//
// Author(s):
// OpenAI
//
// History:
// 28 Mar 2026  OpenAI         Created, Vancouver.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace SIPSorcery.Net;

/// <summary>
/// Provides AV1 RTP packetisation helpers based on the Alliance for Open Media
/// RTP Payload Format for AV1.
/// </summary>
/// <example>
/// <code>
/// var packets = AV1Packetiser.Packetize(temporalUnit, 1200);
/// foreach (var pkt in packets)
/// {
///     // send pkt.Payload via RTP, set marker bit when pkt.IsLast is true
/// }
/// </code>
/// </example>
public class AV1Packetiser
{
    public const int AV1_AGGREGATION_HEADER_LENGTH = 1;

    private const byte Z_MASK = 0x80;
    private const byte Y_MASK = 0x40;
    private const byte N_MASK = 0x08;
    private const byte OBU_TYPE_MASK = 0x78;
    private const int OBU_TYPE_SHIFT = 3;
    private const byte OBU_EXTENSION_FLAG_MASK = 0x04;
    private const byte OBU_HAS_SIZE_FIELD_MASK = 0x02;

    public enum AV1ObuType : byte
    {
        Reserved = 0,
        SequenceHeader = 1,
        TemporalDelimiter = 2,
        FrameHeader = 3,
        TileGroup = 4,
        Metadata = 5,
        Frame = 6,
        RedundantFrameHeader = 7,
        TileList = 8,
        Padding = 15
    }

    public readonly struct AV1RtpPacket
    {
        public ReadOnlyMemory<byte> Payload { get; }
        public bool IsLast { get; }

        public AV1RtpPacket(ReadOnlyMemory<byte> payload, bool isLast)
        {
            Payload = payload;
            IsLast = isLast;
        }
    }

    /// <summary>
    /// Packetises an AV1 temporal unit into one or more RTP packets.
    /// </summary>
    /// <param name="temporalUnit">The AV1 temporal unit (sequence of OBUs) to packetise.</param>
    /// <param name="maxPayloadSize">The maximum RTP payload size in bytes.</param>
    /// <returns>A list of RTP packets ready for transmission.</returns>
    public static List<AV1RtpPacket> Packetize(ReadOnlySpan<byte> temporalUnit, int maxPayloadSize)
    {
        if (temporalUnit.IsEmpty)
        {
            return [];
        }

        if (maxPayloadSize <= AV1_AGGREGATION_HEADER_LENGTH + 2)
        {
            throw new ArgumentException("The maximum RTP payload size is too small for AV1 packetisation.", nameof(maxPayloadSize));
        }

        var packets = new List<AV1RtpPacket>();
        var obus = ParseObus(temporalUnit);
        obus.RemoveAll(static obu => ShouldSkipObu(GetObuType(obu)));

        if (obus.Count == 0)
        {
            return packets;
        }

        var startsCodedVideoSequence = GetObuType(obus[0]) == AV1ObuType.SequenceHeader;
        var packetStartIdx = 0;
        var currentPacketSize = AV1_AGGREGATION_HEADER_LENGTH;

        for (var i = 0; i < obus.Count; i++)
        {
            var obu = obus[i];
            var obuCost = GetLeb128Length(obu.Length) + obu.Length;

            if (obuCost + AV1_AGGREGATION_HEADER_LENGTH > maxPayloadSize)
            {
                if (i > packetStartIdx)
                {
                    packets.Add(CreatePacket(obus, packetStartIdx, i - packetStartIdx, false, false, startsCodedVideoSequence && packets.Count == 0, false));
                }

                FragmentObu(obu, maxPayloadSize, startsCodedVideoSequence && packets.Count == 0, packets);

                packetStartIdx = i + 1;
                currentPacketSize = AV1_AGGREGATION_HEADER_LENGTH;
                continue;
            }

            if (currentPacketSize + obuCost > maxPayloadSize)
            {
                packets.Add(CreatePacket(obus, packetStartIdx, i - packetStartIdx, false, false, startsCodedVideoSequence && packets.Count == 0, false));
                packetStartIdx = i;
                currentPacketSize = AV1_AGGREGATION_HEADER_LENGTH;
            }

            currentPacketSize += obuCost;
        }

        if (obus.Count > packetStartIdx)
        {
            packets.Add(CreatePacket(obus, packetStartIdx, obus.Count - packetStartIdx, false, false, startsCodedVideoSequence && packets.Count == 0, false));
        }

        if (packets.Count > 0)
        {
            packets[packets.Count - 1] = new AV1RtpPacket(packets[packets.Count - 1].Payload, true);
        }

        return packets;
    }

    /// <summary>
    /// Parses the OBUs from an AV1 temporal unit byte stream.
    /// </summary>
    public static List<byte[]> ParseObus(ReadOnlySpan<byte> temporalUnit)
    {
        var obus = new List<byte[]>();

        if (temporalUnit.IsEmpty)
        {
            return obus;
        }

        var offset = 0;
        while (offset < temporalUnit.Length)
        {
            var obuStart = offset;
            var obuHeader = temporalUnit[offset++];
            var hasExtension = (obuHeader & OBU_EXTENSION_FLAG_MASK) != 0;
            var hasSizeField = (obuHeader & OBU_HAS_SIZE_FIELD_MASK) != 0;

            if (hasExtension)
            {
                if (offset >= temporalUnit.Length)
                {
                    throw new SipSorceryException("The AV1 OBU extension header was truncated.");
                }

                offset++;
            }

            if (!hasSizeField)
            {
                obus.Add(temporalUnit.Slice(obuStart).ToArray());
                return obus;
            }

            if (!TryReadLeb128(temporalUnit, ref offset, out var obuPayloadSize, out _))
            {
                throw new SipSorceryException("The AV1 OBU size field could not be parsed.");
            }

            if (offset + obuPayloadSize > temporalUnit.Length)
            {
                throw new SipSorceryException("The AV1 OBU payload exceeded the source buffer.");
            }

            var totalObuLength = (offset - obuStart) + obuPayloadSize;
            obus.Add(temporalUnit.Slice(obuStart, totalObuLength).ToArray());

            offset += obuPayloadSize;
        }

        return obus;
    }

    /// <summary>
    /// Returns the OBU type from the first byte of an OBU.
    /// </summary>
    public static AV1ObuType GetObuType(ReadOnlySpan<byte> obu)
    {
        if (obu.IsEmpty)
        {
            return AV1ObuType.Reserved;
        }

        return (AV1ObuType)((obu[0] & OBU_TYPE_MASK) >> OBU_TYPE_SHIFT);
    }

    /// <summary>
    /// Tries to read a LEB128-encoded unsigned integer from the buffer.
    /// </summary>
    /// <remarks>
    /// REVIEW: The loop limit of 8 bytes can shift beyond 32 bits (shift reaches 35 on the
    /// 6th byte), overflowing the <see cref="int"/> accumulator. Cap at 5 iterations for
    /// 32-bit safety, or use <see langword="long"/> if larger values are needed.
    /// </remarks>
    public static bool TryReadLeb128(ReadOnlySpan<byte> buffer, ref int offset, out int value, out int leb128Length)
    {
        value = 0;
        leb128Length = 0;
        var shift = 0;

        while (offset < buffer.Length && leb128Length < 8)
        {
            var current = buffer[offset++];
            leb128Length++;
            value |= (current & 0x7f) << shift;

            if ((current & 0x80) == 0)
            {
                return true;
            }

            shift += 7;
        }

        value = 0;
        return false;
    }

    /// <summary>
    /// Returns the number of bytes needed to encode a value as LEB128.
    /// </summary>
    public static int GetLeb128Length(int value)
    {
        var size = 1;
        var remainder = value >> 7;
        while (remainder > 0)
        {
            size++;
            remainder >>= 7;
        }

        return size;
    }

    /// <summary>
    /// Writes a non-negative integer as a LEB128 byte sequence into the destination span.
    /// </summary>
    /// <param name="destination">The span to write the encoded bytes into.</param>
    /// <param name="value">The non-negative value to encode.</param>
    /// <returns>The number of bytes written.</returns>
    public static int WriteLeb128(Span<byte> destination, int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);

        var remainder = value;
        var written = 0;

        do
        {
            var current = (byte)(remainder & 0x7f);
            remainder >>= 7;
            if (remainder > 0)
            {
                current |= 0x80;
            }

            destination[written++] = current;
        } while (remainder > 0);

        return written;
    }

    private static void FragmentObu(ReadOnlySpan<byte> obu, int maxPayloadSize, bool startsCodedVideoSequence, List<AV1RtpPacket> packets)
    {
        var payloadCapacity = maxPayloadSize - AV1_AGGREGATION_HEADER_LENGTH;
        var offset = 0;
        var isFirstFragment = true;

        while (offset < obu.Length)
        {
            var fragmentLength = GetMaxFragmentLength(obu.Length - offset, payloadCapacity);

            var z = !isFirstFragment;
            var y = offset + fragmentLength < obu.Length;
            var n = startsCodedVideoSequence && isFirstFragment;

            var leb128Len = GetLeb128Length(fragmentLength);
            var payload = new byte[AV1_AGGREGATION_HEADER_LENGTH + leb128Len + fragmentLength];
            payload[0] = (byte)((z ? Z_MASK : 0) | (y ? Y_MASK : 0) | (n ? N_MASK : 0));
            WriteLeb128(payload.AsSpan(1), fragmentLength);
            obu.Slice(offset, fragmentLength).CopyTo(payload.AsSpan(1 + leb128Len));

            packets.Add(new AV1RtpPacket(payload, false));

            offset += fragmentLength;
            isFirstFragment = false;
        }
    }

    private static AV1RtpPacket CreatePacket(List<byte[]> obus, int start, int count, bool z, bool y, bool n, bool isLast)
    {
        var payloadLength = AV1_AGGREGATION_HEADER_LENGTH;
        var end = start + count;
        for (var i = start; i < end; i++)
        {
            payloadLength += GetLeb128Length(obus[i].Length) + obus[i].Length;
        }

        var payload = new byte[payloadLength];
        payload[0] = (byte)((z ? Z_MASK : 0) | (y ? Y_MASK : 0) | (n ? N_MASK : 0));

        var dstOffset = 1;
        for (var i = start; i < end; i++)
        {
            var leb128Written = WriteLeb128(payload.AsSpan(dstOffset), obus[i].Length);
            dstOffset += leb128Written;

            obus[i].AsSpan().CopyTo(payload.AsSpan(dstOffset));
            dstOffset += obus[i].Length;
        }

        return new AV1RtpPacket(payload, isLast);
    }

    private static bool ShouldSkipObu(AV1ObuType obuType) =>
        obuType is AV1ObuType.TemporalDelimiter or AV1ObuType.TileList;

    private static int GetMaxFragmentLength(int remainingBytes, int payloadCapacity)
    {
        var fragmentLength = Math.Min(remainingBytes, payloadCapacity - 1);
        while (fragmentLength > 0 && fragmentLength + GetLeb128Length(fragmentLength) > payloadCapacity)
        {
            fragmentLength--;
        }

        if (fragmentLength <= 0)
        {
            throw new SipSorceryException("Unable to fit an AV1 OBU fragment into the RTP payload.");
        }

        return fragmentLength;
    }
}
