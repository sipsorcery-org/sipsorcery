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
using System.Buffers;
using System.Collections.Generic;
using CommunityToolkit.HighPerformance.Buffers;

namespace SIPSorcery.Net;

[Flags]
internal enum AV1ObuHeaderFlags : byte
{
    None = 0,
    HasSizeField = 0x02,
    Extension = 0x04
}

[Flags]
internal enum AV1AggregationHeaderFlags : byte
{
    None = 0,
    NewCodedVideoSequence = 0x08, // N bit.
    LastObuContinues = 0x40, // Y bit.
    FirstObuIsContinuation = 0x80 // Z bit.
}

public class AV1Packetiser
{
    public const int AV1_AGGREGATION_HEADER_LENGTH = 1;

    internal const int OBU_HEADER_LENGTH = 1;
    internal const int OBU_EXTENSION_HEADER_LENGTH = 1;
    private const byte OBU_TYPE_MASK = 0x78;
    private const int OBU_TYPE_SHIFT = 3;
    private const int MIN_OBU_FRAGMENT_LENGTH = OBU_HEADER_LENGTH;
    private const int MIN_LEB128_LENGTH = 1;
    private const int MIN_OBU_ELEMENT_LENGTH = MIN_OBU_FRAGMENT_LENGTH + MIN_LEB128_LENGTH;
    private const int MIN_RTP_PAYLOAD_LENGTH = AV1_AGGREGATION_HEADER_LENGTH + MIN_OBU_ELEMENT_LENGTH + OBU_EXTENSION_HEADER_LENGTH;
    private const int LEB128_BITS_PER_BYTE = 7;
    private const byte LEB128_VALUE_MASK = 0x7f;
    private const byte LEB128_CONTINUATION_MASK = 0x80;
    private const int MAX_LEB128_LENGTH = 8; // AV1 limits leb128() to eight bytes.

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

    public struct AV1RtpPacket
    {
        public byte[] Payload { get; }
        public bool IsLast { get; }

        public AV1RtpPacket(byte[] payload, bool isLast)
        {
            Payload = payload;
            IsLast = isLast;
        }
    }

    private readonly struct Obu
    {
        // Reference the source by offsets so packetisation also supports stack-backed spans.
        public int Start { get; }
        public int SourceLength { get; }
        public int HeaderLength { get; }
        private int PayloadOffset { get; }
        public int Length => HeaderLength + SourceLength - PayloadOffset;
        public AV1ObuType Type { get; }

        public Obu(int start, int sourceLength, int headerLength, int payloadOffset, AV1ObuType type)
        {
            Start = start;
            SourceLength = sourceLength;
            HeaderLength = headerLength;
            PayloadOffset = payloadOffset;
            Type = type;
        }

        public void CopyFragmentTo(ReadOnlySpan<byte> temporalUnit, int offset, Span<byte> destination)
        {
            var source = temporalUnit.Slice(Start, SourceLength);
            int written = 0;
            if (offset < HeaderLength)
            {
                written = Math.Min(HeaderLength - offset, destination.Length);
                source.Slice(offset, written).CopyTo(destination);
                if (offset == 0)
                {
                    destination[0] = (byte)((AV1ObuHeaderFlags)destination[0] & ~AV1ObuHeaderFlags.HasSizeField);
                }
                offset += written;
            }

            if (written < destination.Length)
            {
                source.Slice(PayloadOffset + offset - HeaderLength, destination.Length - written)
                    .CopyTo(destination.Slice(written));
            }
        }
    }

    [Obsolete("Use Packetize(ReadOnlySpan<byte>, int) instead. Pass array.AsSpan() for byte arrays.")]
    public static List<AV1RtpPacket> Packetize(byte[] temporalUnit, int maxPayloadSize)
        => Packetize(temporalUnit.AsSpan(), maxPayloadSize);

    /// <summary>
    /// Packetises an AV1 temporal unit into RTP payloads, each in an array of its own.
    /// </summary>
    /// <param name="temporalUnit">The temporal unit, a sequence of OBUs.</param>
    /// <param name="maxPayloadSize">The maximum RTP payload size.</param>
    /// <returns>The payloads the <see cref="IBufferWriter{T}"/> overload writes, the last one marked.</returns>
    public static List<AV1RtpPacket> Packetize(ReadOnlySpan<byte> temporalUnit, int maxPayloadSize)
    {
        var packets = new List<AV1RtpPacket>();
        if (temporalUnit.IsEmpty)
        {
            return packets;
        }

        var payloadLengths = new List<int>();
        using (var payloads = new ArrayPoolBufferWriter<byte>(temporalUnit.Length))
        {
            Packetize(temporalUnit, maxPayloadSize, payloads, payloadLengths);

            ReadOnlySpan<byte> remaining = payloads.WrittenSpan;
            for (int i = 0; i < payloadLengths.Count; i++)
            {
                packets.Add(new AV1RtpPacket(remaining.Slice(0, payloadLengths[i]).ToArray(), i == payloadLengths.Count - 1));
                remaining = remaining.Slice(payloadLengths[i]);
            }
        }

        return packets;
    }

    /// <summary>
    /// Packetises an AV1 temporal unit, writing the RTP payloads one after another into a buffer writer. Each payload
    /// is an aggregation header followed by OBU elements, every one preceded by its length: whole OBUs, or fragments
    /// of an OBU too large for the rest of the payload, with their size fields left out. Temporal delimiter, tile
    /// list and padding OBUs are not sent.
    /// </summary>
    /// <param name="temporalUnit">The temporal unit, a sequence of OBUs. Only read during the call.</param>
    /// <param name="maxPayloadSize">The maximum RTP payload size.</param>
    /// <param name="output">The payloads are written to it one after another, each with one
    /// <see cref="IBufferWriter{T}.GetSpan(int)"/> of <paramref name="maxPayloadSize"/> bytes and one
    /// <see cref="IBufferWriter{T}.Advance(int)"/>.</param>
    /// <param name="payloadLengths">The length of each payload written is added to it, in order. The last one
    /// ends the temporal unit, so its packet takes the RTP marker bit.</param>
    /// <exception cref="ArgumentNullException"><paramref name="output"/> or <paramref name="payloadLengths"/> is
    /// null.</exception>
    /// <exception cref="ArgumentException"><paramref name="maxPayloadSize"/> is too small for AV1 packetisation. An
    /// empty temporal unit writes nothing and is not refused.</exception>
    /// <exception cref="FormatException">An OBU in the temporal unit is malformed. The payloads before the one that
    /// would have held it are already written.</exception>
    public static void Packetize(ReadOnlySpan<byte> temporalUnit, int maxPayloadSize, IBufferWriter<byte> output, ICollection<int> payloadLengths)
    {
        if (output == null)
        {
            throw new ArgumentNullException(nameof(output));
        }

        if (payloadLengths == null)
        {
            throw new ArgumentNullException(nameof(payloadLengths));
        }

        if (temporalUnit.IsEmpty)
        {
            return;
        }

        if (maxPayloadSize < MIN_RTP_PAYLOAD_LENGTH)
        {
            throw new ArgumentException("The maximum RTP payload size is too small for AV1 packetisation.", nameof(maxPayloadSize));
        }

        // Each OBU is parsed when the payloads reach it, rather than all of them first.
        int parseOffset = 0;
        if (!TryParseNextTransmittedObu(temporalUnit, ref parseOffset, out var obu))
        {
            return;
        }

        bool startsCodedVideoSequence = obu.Type == AV1ObuType.SequenceHeader;
        bool hasObu = true;
        int obuOffset = 0;
        bool isFirstPayload = true;

        while (hasObu)
        {
            Span<byte> payload = output.GetSpan(maxPayloadSize);
            int length = AV1_AGGREGATION_HEADER_LENGTH;
            int capacity = maxPayloadSize - AV1_AGGREGATION_HEADER_LENGTH;
            bool firstObuIsContinuation = obuOffset > 0;
            bool lastObuContinues = false;

            while (hasObu && capacity >= MIN_OBU_ELEMENT_LENGTH)
            {
                int fragmentLength = GetMaxFragmentLength(obu.Length - obuOffset, capacity);
                if (obuOffset == 0 && fragmentLength < obu.HeaderLength)
                {
                    // Keep the complete OBU header in its first fragment.
                    break;
                }

                length += WriteLeb128(fragmentLength, payload.Slice(length));
                obu.CopyFragmentTo(temporalUnit, obuOffset, payload.Slice(length, fragmentLength));
                length += fragmentLength;
                capacity -= GetLeb128Size(fragmentLength) + fragmentLength;
                obuOffset += fragmentLength;
                lastObuContinues = obuOffset < obu.Length;
                if (lastObuContinues)
                {
                    break;
                }

                // Fill the remaining space with the next OBU's first fragment, rather
                // than sending a sequence header separately from a large frame OBU.
                hasObu = TryParseNextTransmittedObu(temporalUnit, ref parseOffset, out obu);
                obuOffset = 0;
            }

            var header = AV1AggregationHeaderFlags.None;
            if (firstObuIsContinuation)
            {
                header |= AV1AggregationHeaderFlags.FirstObuIsContinuation;
            }
            if (lastObuContinues)
            {
                header |= AV1AggregationHeaderFlags.LastObuContinues;
            }
            if (startsCodedVideoSequence && isFirstPayload)
            {
                header |= AV1AggregationHeaderFlags.NewCodedVideoSequence;
            }
            payload[0] = (byte)header;

            output.Advance(length);
            payloadLengths.Add(length);
            isFirstPayload = false;
        }
    }

    // Parses OBUs from offset until one that is sent, skipping those that are not.
    private static bool TryParseNextTransmittedObu(ReadOnlySpan<byte> temporalUnit, ref int offset, out Obu obu)
    {
        while (TryParseObu(temporalUnit, ref offset, out obu))
        {
            if (!ShouldSkipObu(obu.Type))
            {
                return true;
            }
        }

        return false;
    }

    public static IEnumerable<byte[]> ParseObus(byte[] temporalUnit)
    {
        int offset = 0;
        while (TryParseObu(temporalUnit, ref offset, out var obu))
        {
            yield return temporalUnit.AsMemory(obu.Start, obu.SourceLength).ToArray();
        }
    }

    private static bool TryParseObu(ReadOnlySpan<byte> temporalUnit, ref int offset, out Obu obu)
    {
        obu = default;
        if (offset >= temporalUnit.Length)
        {
            return false;
        }

        int obuStart = offset;
        var obuHeader = (AV1ObuHeaderFlags)temporalUnit[offset++];
        var obuType = (AV1ObuType)(((byte)obuHeader & OBU_TYPE_MASK) >> OBU_TYPE_SHIFT);

        if (obuHeader.HasFlag(AV1ObuHeaderFlags.Extension))
        {
            if (offset >= temporalUnit.Length)
            {
                throw new FormatException("The AV1 OBU extension header was truncated.");
            }

            offset += OBU_EXTENSION_HEADER_LENGTH;
        }

        if (!obuHeader.HasFlag(AV1ObuHeaderFlags.HasSizeField))
        {
            int headerLength = offset - obuStart;
            obu = new Obu(obuStart, temporalUnit.Length - obuStart, headerLength, headerLength, obuType);
            offset = temporalUnit.Length;
            return true;
        }

        int payloadHeaderLength = offset - obuStart;
        if (!TryReadLeb128(temporalUnit, ref offset, out int obuPayloadSize, out _))
        {
            throw new FormatException("The AV1 OBU size field could not be parsed.");
        }

        if (obuPayloadSize > temporalUnit.Length - offset)
        {
            throw new FormatException("The AV1 OBU payload exceeded the source buffer.");
        }

        int totalObuLength = (offset - obuStart) + obuPayloadSize;
        obu = new Obu(obuStart, totalObuLength, payloadHeaderLength, offset - obuStart, obuType);
        offset += obuPayloadSize;
        return true;
    }

    public static AV1ObuType GetObuType(byte[] obu)
        => GetObuType(obu.AsSpan());

    public static AV1ObuType GetObuType(ReadOnlySpan<byte> obu)
    {
        if (obu.IsEmpty)
        {
            return AV1ObuType.Reserved;
        }

        return (AV1ObuType)((obu[0] & OBU_TYPE_MASK) >> OBU_TYPE_SHIFT);
    }

    public static bool TryReadLeb128(byte[] buffer, ref int offset, out int value, out int leb128Length)
        => TryReadLeb128(buffer.AsSpan(), ref offset, out value, out leb128Length);

    public static bool TryReadLeb128(ReadOnlySpan<byte> buffer, ref int offset, out int value, out int leb128Length)
    {
        value = 0;
        leb128Length = 0;
        if (offset < 0)
        {
            return false;
        }

        ulong result = 0;
        while (offset < buffer.Length && leb128Length < MAX_LEB128_LENGTH)
        {
            byte current = buffer[offset++];
            result |= (ulong)(current & LEB128_VALUE_MASK) << (LEB128_BITS_PER_BYTE * leb128Length);
            leb128Length++;

            if ((current & LEB128_CONTINUATION_MASK) == 0)
            {
                if (result > int.MaxValue)
                {
                    return false;
                }

                value = (int)result;
                return true;
            }
        }

        value = 0;
        return false;
    }

    public static byte[] WriteLeb128(int value)
    {
        if (value < 0)
        {
            throw new ArgumentException("AV1 leb128 values must be non-negative.", nameof(value));
        }

        var bytes = new byte[GetLeb128Size(value)];
        WriteLeb128(value, bytes.AsSpan());
        return bytes;
    }

    internal static int WriteLeb128(int value, Span<byte> destination)
    {
        int written = 0;
        int remainder = value;

        do
        {
            byte current = (byte)(remainder & LEB128_VALUE_MASK);
            remainder >>= LEB128_BITS_PER_BYTE;
            if (remainder > 0)
            {
                current |= LEB128_CONTINUATION_MASK;
            }

            destination[written++] = current;
        } while (remainder > 0);

        return written;
    }

    private static bool ShouldSkipObu(AV1ObuType obuType) =>
        obuType is AV1ObuType.TemporalDelimiter or AV1ObuType.TileList or AV1ObuType.Padding;

    internal static int GetLeb128Size(int value)
    {
        int size = MIN_LEB128_LENGTH;
        int remainder = value >> LEB128_BITS_PER_BYTE;
        while (remainder > 0)
        {
            size++;
            remainder >>= LEB128_BITS_PER_BYTE;
        }

        return size;
    }

    private static int GetMaxFragmentLength(int remainingBytes, int payloadCapacity)
    {
        int fragmentLength = Math.Min(remainingBytes, payloadCapacity - MIN_LEB128_LENGTH);
        while (fragmentLength > 0 && fragmentLength + GetLeb128Size(fragmentLength) > payloadCapacity)
        {
            fragmentLength--;
        }

        if (fragmentLength <= 0)
        {
            throw new InvalidOperationException("Unable to fit an AV1 OBU fragment into the RTP payload.");
        }

        return fragmentLength;
    }
}
