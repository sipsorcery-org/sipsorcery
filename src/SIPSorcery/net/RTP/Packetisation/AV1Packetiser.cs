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

        public static List<AV1RtpPacket> Packetize(byte[] temporalUnit, int maxPayloadSize)
        {
            if (temporalUnit == null || temporalUnit.Length == 0)
            {
                return new List<AV1RtpPacket>();
            }

            if (maxPayloadSize <= AV1_AGGREGATION_HEADER_LENGTH + 2)
            {
                throw new ArgumentException("The maximum RTP payload size is too small for AV1 packetisation.", nameof(maxPayloadSize));
            }

            var packets = new List<AV1RtpPacket>();
            var transmittedObus = new List<byte[]>();

            foreach (var obu in ParseObus(temporalUnit))
            {
                if (!ShouldSkipObu(GetObuType(obu)))
                {
                    transmittedObus.Add(obu);
                }
            }

            if (transmittedObus.Count == 0)
            {
                return packets;
            }

            bool startsCodedVideoSequence = GetObuType(transmittedObus[0]) == AV1ObuType.SequenceHeader;
            var currentPacketObus = new List<byte[]>();
            int currentPacketSize = AV1_AGGREGATION_HEADER_LENGTH;

            for (int i = 0; i < transmittedObus.Count; i++)
            {
                var obu = transmittedObus[i];
                int obuCost = GetLeb128Size(obu.Length) + obu.Length;

                if (obuCost + AV1_AGGREGATION_HEADER_LENGTH > maxPayloadSize)
                {
                    if (currentPacketObus.Count > 0)
                    {
                        packets.Add(CreatePacket(currentPacketObus, false, false, startsCodedVideoSequence && packets.Count == 0, false));
                        currentPacketObus.Clear();
                        currentPacketSize = AV1_AGGREGATION_HEADER_LENGTH;
                    }

                    foreach (var fragment in FragmentObu(obu, maxPayloadSize, startsCodedVideoSequence && packets.Count == 0))
                    {
                        packets.Add(fragment);
                    }

                    continue;
                }

                if (currentPacketSize + obuCost > maxPayloadSize)
                {
                    packets.Add(CreatePacket(currentPacketObus, false, false, startsCodedVideoSequence && packets.Count == 0, false));
                    currentPacketObus = new List<byte[]>();
                    currentPacketSize = AV1_AGGREGATION_HEADER_LENGTH;
                }

                currentPacketObus.Add(obu);
                currentPacketSize += obuCost;
            }

            if (currentPacketObus.Count > 0)
            {
                packets.Add(CreatePacket(currentPacketObus, false, false, startsCodedVideoSequence && packets.Count == 0, false));
            }

            if (packets.Count > 0)
            {
                packets[packets.Count - 1] = new AV1RtpPacket(packets[packets.Count - 1].Payload, true);
            }

            return packets;
        }

        public static IEnumerable<byte[]> ParseObus(byte[] temporalUnit)
        {
            if (temporalUnit == null)
            {
                yield break;
            }

            int offset = 0;
            while (offset < temporalUnit.Length)
            {
                int obuStart = offset;
                byte obuHeader = temporalUnit[offset++];
                bool hasExtension = (obuHeader & OBU_EXTENSION_FLAG_MASK) != 0;
                bool hasSizeField = (obuHeader & OBU_HAS_SIZE_FIELD_MASK) != 0;

                if (hasExtension)
                {
                    if (offset >= temporalUnit.Length)
                    {
                        throw new ApplicationException("The AV1 OBU extension header was truncated.");
                    }

                    offset++;
                }

                if (!hasSizeField)
                {
                    var finalObu = new byte[temporalUnit.Length - obuStart];
                    Buffer.BlockCopy(temporalUnit, obuStart, finalObu, 0, finalObu.Length);
                    yield return finalObu;
                    yield break;
                }

                if (!TryReadLeb128(temporalUnit, ref offset, out int obuPayloadSize, out _))
                {
                    throw new ApplicationException("The AV1 OBU size field could not be parsed.");
                }

                if (offset + obuPayloadSize > temporalUnit.Length)
                {
                    throw new ApplicationException("The AV1 OBU payload exceeded the source buffer.");
                }

                int totalObuLength = (offset - obuStart) + obuPayloadSize;
                var obu = new byte[totalObuLength];
                Buffer.BlockCopy(temporalUnit, obuStart, obu, 0, totalObuLength);

                offset += obuPayloadSize;
                yield return obu;
            }
        }

        public static AV1ObuType GetObuType(byte[] obu)
        {
            if (obu == null || obu.Length == 0)
            {
                return AV1ObuType.Reserved;
            }

            return (AV1ObuType)((obu[0] & OBU_TYPE_MASK) >> OBU_TYPE_SHIFT);
        }

        public static bool TryReadLeb128(byte[] buffer, ref int offset, out int value, out int leb128Length)
        {
            value = 0;
            leb128Length = 0;
            int shift = 0;

            while (offset < buffer.Length && leb128Length < 8)
            {
                byte current = buffer[offset++];
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

        public static byte[] WriteLeb128(int value)
        {
            if (value < 0)
            {
                throw new ArgumentException("AV1 leb128 values must be non-negative.", nameof(value));
            }

            var bytes = new List<byte>();
            int remainder = value;

            do
            {
                byte current = (byte)(remainder & 0x7f);
                remainder >>= 7;
                if (remainder > 0)
                {
                    current |= 0x80;
                }

                bytes.Add(current);
            } while (remainder > 0);

            return bytes.ToArray();
        }

        private static IEnumerable<AV1RtpPacket> FragmentObu(byte[] obu, int maxPayloadSize, bool startsCodedVideoSequence)
        {
            int payloadCapacity = maxPayloadSize - AV1_AGGREGATION_HEADER_LENGTH;
            int offset = 0;
            bool isFirstFragment = true;

            while (offset < obu.Length)
            {
                int fragmentLength = GetMaxFragmentLength(obu.Length - offset, payloadCapacity);
                var fragment = new byte[fragmentLength];
                Buffer.BlockCopy(obu, offset, fragment, 0, fragmentLength);

                bool z = !isFirstFragment;
                bool y = offset + fragmentLength < obu.Length;
                bool n = startsCodedVideoSequence && isFirstFragment;

                yield return CreatePacket(new List<byte[]> { fragment }, z, y, n, false);

                offset += fragmentLength;
                isFirstFragment = false;
            }
        }

        private static AV1RtpPacket CreatePacket(List<byte[]> obuElements, bool z, bool y, bool n, bool isLast)
        {
            int payloadLength = AV1_AGGREGATION_HEADER_LENGTH;
            for (int i = 0; i < obuElements.Count; i++)
            {
                payloadLength += GetLeb128Size(obuElements[i].Length) + obuElements[i].Length;
            }

            var payload = new byte[payloadLength];
            payload[0] = (byte)((z ? Z_MASK : 0) | (y ? Y_MASK : 0) | (n ? N_MASK : 0));

            int dstOffset = 1;
            for (int i = 0; i < obuElements.Count; i++)
            {
                var leb128 = WriteLeb128(obuElements[i].Length);
                Buffer.BlockCopy(leb128, 0, payload, dstOffset, leb128.Length);
                dstOffset += leb128.Length;

                Buffer.BlockCopy(obuElements[i], 0, payload, dstOffset, obuElements[i].Length);
                dstOffset += obuElements[i].Length;
            }

            return new AV1RtpPacket(payload, isLast);
        }

        private static bool ShouldSkipObu(AV1ObuType obuType) =>
            obuType == AV1ObuType.TemporalDelimiter || obuType == AV1ObuType.TileList;

        private static int GetLeb128Size(int value)
        {
            int size = 1;
            int remainder = value >> 7;
            while (remainder > 0)
            {
                size++;
                remainder >>= 7;
            }

            return size;
        }

        private static int GetMaxFragmentLength(int remainingBytes, int payloadCapacity)
        {
            int fragmentLength = Math.Min(remainingBytes, payloadCapacity - 1);
            while (fragmentLength > 0 && fragmentLength + GetLeb128Size(fragmentLength) > payloadCapacity)
            {
                fragmentLength--;
            }

            if (fragmentLength <= 0)
            {
                throw new ApplicationException("Unable to fit an AV1 OBU fragment into the RTP payload.");
            }

            return fragmentLength;
        }
}
