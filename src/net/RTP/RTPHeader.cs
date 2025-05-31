//-----------------------------------------------------------------------------
// Filename: RTPHeader.cs
//
// Description: RTP Header as defined in RFC3550.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 22 May 2005	Aaron Clauson	Created, Dublin, Ireland.
// 11 Aug 2019  Aaron Clauson   Added full license header.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using SIPSorcery.Sys;

namespace SIPSorcery.Net;

public class RTPHeader
{
    public const int MIN_HEADER_LEN = 12;

    public const int RTP_VERSION = 2;
    public const int ONE_BYTE_EXTENSION_PROFILE = 0xBEDE;
    public const int TWO_BYTE_EXTENSION_PROFILE = 0x1000;

    public int Version = RTP_VERSION;                       // 2 bits.
    public int PaddingFlag = 0;                             // 1 bit.
    public int HeaderExtensionFlag = 0;                     // 1 bit.
    public int CSRCCount = 0;                               // 4 bits
    public int MarkerBit = 0;                               // 1 bit.
    public int PayloadType = 0;                             // 7 bits.
    public UInt16 SequenceNumber;                           // 16 bits.
    public uint Timestamp;                                  // 32 bits.
    public uint SyncSource;                                 // 32 bits.
    public int[] CSRCList;                                  // 32 bits.
    public UInt16 ExtensionProfile;                         // 16 bits.
    public UInt16 ExtensionLength;                          // 16 bits, length of the header extensions in 32 bit words.
    public byte[] ExtensionPayload;

    public int PayloadSize;
    public byte PaddingCount;
    public DateTime ReceivedTime;
    public int Length
    {
        get { return MIN_HEADER_LEN + (CSRCCount * 4) + ((HeaderExtensionFlag == 0) ? 0 : 4 + (ExtensionLength * 4)); }
    }

    public RTPHeader()
    {
        SequenceNumber = Crypto.GetRandomUInt16();
        SyncSource = Crypto.GetRandomUInt();
        Timestamp = Crypto.GetRandomUInt();
    }

    /// <summary>
    /// Extract and load the RTP header from an RTP packet.
    /// </summary>
    /// <param name="packet"></param>
    public RTPHeader(byte[] packet)
    {
        if (packet.Length < MIN_HEADER_LEN)
        {
            throw new ApplicationException("The packet did not contain the minimum number of bytes for an RTP header packet.");
        }

        UInt16 firstWord = BitConverter.ToUInt16(packet, 0);

        if (BitConverter.IsLittleEndian)
        {
            firstWord = NetConvert.DoReverseEndian(firstWord);
            SequenceNumber = NetConvert.DoReverseEndian(BitConverter.ToUInt16(packet, 2));
            Timestamp = NetConvert.DoReverseEndian(BitConverter.ToUInt32(packet, 4));
            SyncSource = NetConvert.DoReverseEndian(BitConverter.ToUInt32(packet, 8));
        }
        else
        {
            SequenceNumber = BitConverter.ToUInt16(packet, 2);
            Timestamp = BitConverter.ToUInt32(packet, 4);
            SyncSource = BitConverter.ToUInt32(packet, 8);
        }


        Version = firstWord >> 14;
        PaddingFlag = (firstWord >> 13) & 0x1;
        HeaderExtensionFlag = (firstWord >> 12) & 0x1;
        CSRCCount = (firstWord >> 8) & 0xf;

        MarkerBit = (firstWord >> 7) & 0x1;
        PayloadType = firstWord & 0x7f;

        int headerExtensionLength = 0;
        int headerAndCSRCLength = 12 + 4 * CSRCCount;

        if (HeaderExtensionFlag == 1 && (packet.Length >= (headerAndCSRCLength + 4)))
        {
            if (BitConverter.IsLittleEndian)
            {
                ExtensionProfile = NetConvert.DoReverseEndian(BitConverter.ToUInt16(packet, 12 + 4 * CSRCCount));
                headerExtensionLength += 2;
                ExtensionLength = NetConvert.DoReverseEndian(BitConverter.ToUInt16(packet, 14 + 4 * CSRCCount));
                headerExtensionLength += 2 + ExtensionLength * 4;
            }
            else
            {
                ExtensionProfile = BitConverter.ToUInt16(packet, 12 + 4 * CSRCCount);
                headerExtensionLength += 2;
                ExtensionLength = BitConverter.ToUInt16(packet, 14 + 4 * CSRCCount);
                headerExtensionLength += 2 + ExtensionLength * 4;
            }

            if (ExtensionLength > 0 && packet.Length >= (headerAndCSRCLength + 4 + ExtensionLength * 4))
            {
                ExtensionPayload = new byte[ExtensionLength * 4];
                Buffer.BlockCopy(packet, headerAndCSRCLength + 4, ExtensionPayload, 0, ExtensionLength * 4);
            }
        }

        PayloadSize = packet.Length - (headerAndCSRCLength + headerExtensionLength);
        if (PaddingFlag == 1)
        {
            PaddingCount = packet[packet.Length - 1];
            if (PaddingCount < PayloadSize)//Prevent some protocol attacks 
            {
                PayloadSize -= PaddingCount;
            }
        }
    }

    public byte[] GetHeader(UInt16 sequenceNumber, uint timestamp, uint syncSource)
    {
        SequenceNumber = sequenceNumber;
        Timestamp = timestamp;
        SyncSource = syncSource;

        return GetBytes();
    }

    public byte[] GetBytes()
    {
        byte[] header = new byte[Length];

        UInt16 firstWord = Convert.ToUInt16(Version * 16384 + PaddingFlag * 8192 + HeaderExtensionFlag * 4096 + CSRCCount * 256 + MarkerBit * 128 + PayloadType);

        if (BitConverter.IsLittleEndian)
        {
            Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(firstWord)), 0, header, 0, 2);
            Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(SequenceNumber)), 0, header, 2, 2);
            Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(Timestamp)), 0, header, 4, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(SyncSource)), 0, header, 8, 4);

            if (HeaderExtensionFlag == 1)
            {
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(ExtensionProfile)), 0, header, 12 + 4 * CSRCCount, 2);
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(ExtensionLength)), 0, header, 14 + 4 * CSRCCount, 2);
            }
        }
        else
        {
            Buffer.BlockCopy(BitConverter.GetBytes(firstWord), 0, header, 0, 2);
            Buffer.BlockCopy(BitConverter.GetBytes(SequenceNumber), 0, header, 2, 2);
            Buffer.BlockCopy(BitConverter.GetBytes(Timestamp), 0, header, 4, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(SyncSource), 0, header, 8, 4);

            if (HeaderExtensionFlag == 1)
            {
                Buffer.BlockCopy(BitConverter.GetBytes(ExtensionProfile), 0, header, 12 + 4 * CSRCCount, 2);
                Buffer.BlockCopy(BitConverter.GetBytes(ExtensionLength), 0, header, 14 + 4 * CSRCCount, 2);
            }
        }

        if (ExtensionLength > 0 && ExtensionPayload != null)
        {
            Buffer.BlockCopy(ExtensionPayload, 0, header, 16 + 4 * CSRCCount, ExtensionLength * 4);
        }

        return header;
    }

    private RTPHeaderExtensionData GetExtensionAtPosition(ref int position, int id, int len, RTPHeaderExtensionType type, out bool invalid)
    {
        RTPHeaderExtensionData ext = null;
        if (ExtensionPayload != null)
        {
            if (id != 0)
            {
                if (position + len > ExtensionPayload.Length)
                {
                    // invalid extension
                    invalid = true;
                    return null;
                }
                ext = new RTPHeaderExtensionData(id, ExtensionPayload.Skip(position).Take(len).ToArray(), type);
                position += len;
            }
            else
            {
                position++;
            }
            while ((position < ExtensionPayload.Length) && (ExtensionPayload[position] == 0))
            {
                position++;
            }
        }
        invalid = false;
        return ext;
    }

    public List<RTPHeaderExtensionData> GetHeaderExtensions()
    {

        // See https://github.com/pion/rtp/blob/master/packet.go#L88
        /*
         *  0                   1                   2                   3
         *  0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
         * +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
         * |V=2|P|X|  CC   |M|     PT      |       sequence number         |
         * +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
         * |                           timestamp                           |
         * +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
         * |           synchronization source (SSRC) identifier            |
         * +=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+
         * |            contributing source (CSRC) identifiers             |
         * |                             ....                              |
         * +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
         */

        var extensions = new List<RTPHeaderExtensionData>();
        RTPHeaderExtensionData extension = null;
        var i = 0;
        bool invalid = false;
        if (ExtensionPayload != null)
        {
            while (i + 1 < ExtensionPayload.Length)
            {
                if (ExtensionProfile == ONE_BYTE_EXTENSION_PROFILE)
                {
                    var id = (ExtensionPayload[i] & 0xF0) >> 4;
                    var len = (ExtensionPayload[i] & 0x0F) + 1;
                    i++;
                    extension = GetExtensionAtPosition(ref i, id, len, RTPHeaderExtensionType.OneByte, out invalid);

                }
                else if (ExtensionProfile == TWO_BYTE_EXTENSION_PROFILE)
                {
                    var id = ExtensionPayload[i++];
                    var len = ExtensionPayload[i++] + 1;
                    extension = GetExtensionAtPosition(ref i, id, len, RTPHeaderExtensionType.TwoByte, out invalid);
                }
                else
                {
                    //We don't recognize this extension, ignore it
                    break;
                }

                if (!invalid && extension != null)
                {
                    extensions.Add(extension);
                }
            }
        }

        return extensions;
    }

    public static bool TryParse(
        ReadOnlySpan<byte> buffer,
        out RTPHeader header,
        out int consumed)
    {
        header = new RTPHeader();
        consumed = 0;
        int offset = 0;
        if (buffer.Length < MIN_HEADER_LEN)
        {
            return false;
        }

        var firstWord = BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(offset));
        offset += 2;

        header.SequenceNumber =BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(offset));
        offset += 2;
        header.Timestamp = BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(offset));
        offset += 4;
        header.SyncSource = BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(offset));
        offset += 4;

        header.Version = firstWord >> 14;
        header.PaddingFlag = (firstWord >> 13) & 0x1;
        header.HeaderExtensionFlag = (firstWord >> 12) & 0x1;
        header.CSRCCount = (firstWord >> 8) & 0xf;

        header.MarkerBit = (firstWord >> 7) & 0x1;
        header.PayloadType = firstWord & 0x7f;

        int headerAndCSRCLength = offset + 4 * header.CSRCCount;

        if (header.HeaderExtensionFlag == 1 && (buffer.Length >= (headerAndCSRCLength + 4)))
        {
            header.ExtensionProfile =BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(offset));
            offset += 2;
            header.ExtensionLength = BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(offset));
            offset += 2 + header.ExtensionLength * 4;

            var extensionPayloadLength = header.ExtensionLength * 4;
            if (header.ExtensionLength > 0 && buffer.Length >= extensionPayloadLength)
            {
                header.ExtensionPayload = new byte[extensionPayloadLength];
                Buffer.BlockCopy(buffer.ToArray(), headerAndCSRCLength + 4, header.ExtensionPayload, 0, extensionPayloadLength);
            }
        }

        header.PayloadSize = buffer.Length - offset;
        if (header.PaddingFlag == 1)
        {
            // ReSharper disable once UseIndexFromEndExpression
            header.PaddingCount = buffer[buffer.Length - 1];
            if (header.PaddingCount < header.PayloadSize)//Prevent some protocol attacks 
            {
                header.PayloadSize -= header.PaddingCount;
            }
        }

        consumed = offset;
        return header.PayloadSize>=0;
    }

    /// <summary>
    /// Given a previously seen RTP timestamp (previousTs), returns
    /// the difference in RTP‐timestamp units between this header’s
    /// Timestamp and previousTs, correctly handling 32‐bit wraparound.
    /// If previousTs is zero, returns 0.
    /// </summary>
    public uint GetTimestampDelta(uint previousTs)
    {
        if (previousTs == 0)
        {
            return 0;
        }

        uint currentTs = this.Timestamp;
        if (currentTs >= previousTs)
        {
            // No wraparound
            return currentTs - previousTs;
        }
        else
        {
            // Wrapped around 2^32
            const ulong FullRange = (ulong)uint.MaxValue + 1UL;
            ulong diff = (ulong)currentTs + FullRange - previousTs;
            return (uint)(diff & 0xFFFFFFFF);
        }
    }
}
